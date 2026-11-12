using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Один канал батчинга = (формат вершины + топология + материал).
    ///
    /// Модель кадра:
    ///   - Draw* пишет в Back (кадровая геометрия) или в Retained (геометрия с duration).
    ///   - BeginFrame() чистит протухшие retained-примитивы и меняет местами Front/Back.
    ///   - Первая камера кадра дергает Prepare(): заливка Retained+Front в меш и отдача его на отрисовку.
    ///
    /// Меш берётся из кольца на 2 штуки, чтобы не переписывать буфер, который ещё может
    /// читать GPU/отложенный repaint редактора.
    /// </summary>
    internal sealed unsafe class GizmoChannel<T> : IDisposable where T : unmanaged
    {
        readonly VertexAttributeDescriptor[] _layout;
        readonly MeshTopology _topology;
        readonly int _primVerts; // вершин на примитив: 2 (Lines), 3 (Triangles), 6 (квад из двух треугольников)
        readonly string _name;
        readonly float _boundsPadding;

        GizmoNativeBuffer<T> _front;
        GizmoNativeBuffer<T> _back;
        readonly GizmoNativeBuffer<T> _retained;
        readonly GizmoNativeBuffer<float> _retainedExpiry;

        readonly Mesh[] _meshes = new Mesh[2];
        readonly int[] _meshVertCap = new int[2];
        readonly int[] _meshIdxCap = new int[2];
        readonly IndexFormat[] _meshIdxFormat = new IndexFormat[2];

        // Сжатие GPU-буферов после пика.
        //
        // Раньше голоса за сжатие копились только внутри Upload, а Upload не зовётся ни
        // у опустевшего канала, ни у статичного: после пика в миллион вершин такие каналы
        // держали оба меша кольца раздутыми до конца сессии. Теперь голосует Prepare,
        // раз в кадр: опустевший канал отдаёт меши целиком, а у живого меш, отставший
        // по размеру, пересоздаётся на ближайшей заливке (_reshape), — для статики эта
        // заливка назначается принудительно.
        int _shrinkVotes;
        readonly bool[] _reshape = new bool[2];
        const int ShrinkAfter = GizmoNativeBuffer<float>.ShrinkAfter;

        // Ниже этой ёмкости меш не сжимается и не отдаётся: пересоздавать мелочь дороже,
        // чем держать.
        const int KeepVerts = 4096;

        const float BoundsLimit = 1e9f;
        int _cursor;

        bool _prepared;
        Mesh _readyMesh;
        Bounds _readyBounds;

        // Часы эдит-мода на момент последних новых команд — по ним считается протухание.
        // Почему не стенные часы, написано у GizmoRenderer.EditorClock.
        float _lastDataClock;

        // Снимок хоть раз дошёл до камеры. Пока нет — гасить его нельзя: получится
        // геометрия, которая была нарисована, но не показана ни в одном кадре.
        // Ровно это и видно как мигание, когда редактор встаёт на паузу между
        // границей кадра и перерисовкой вьюпорта.
        bool _shown;

        // Пропуск лишней работы на неизменной геометрии.
        //
        // Статика, нарисованная с duration, стоила ровно столько же, сколько
        // динамика: компактор каждый кадр обходил весь retained-буфер с
        // Encapsulate на каждую вершину, а Prepare каждый кадр перезаливал в GPU
        // те же самые байты. Оба прохода нужны, только если содержимое изменилось.
        //
        // _dirty            — содержимое отличается от того, что уже лежит в меше.
        // _retainedTouched  — в retained писали, компактору нужен полный проход.
        // _minExpiry        — ближайшее истечение среди retained; пока оно впереди,
        //                     проходить буфер незачем: выбывать нечему.
        bool _dirty = true;
        bool _retainedTouched;
        float _minExpiry = float.MaxValue;

        // Версия содержимого retained и версия, лежащая в каждом меше кольца.
        //
        // Retained и кадровая геометрия живут в одном меше: retained префиксом, кадровая
        // за ним. Раньше любой обмен Front/Back взводил _dirty, и Upload перезаливал весь
        // префикс заново — то есть статика «ничего не стоила» только в канале, где больше
        // никто ничего не рисовал. Одна DrawLine в кадре рядом с каркасом уровня стоила
        // мегабайты трафика. Теперь префикс заливается, только если версия в меше отстала:
        // после правки retained — по одному разу в каждый меш кольца.
        //
        // Версия растёт везде, где меняются байты retained: запись (Target), выбывание
        // со сдвигом (CompactRetained) и Clear. 0 у меша — «содержимого нет»: новый меш
        // или SetVertexBufferParams, который буфер пересоздаёт.
        int _retainedVersion = 1;
        readonly int[] _meshRetainedVersion = new int[2];

        public GizmoChannel(string name, VertexAttributeDescriptor[] layout, MeshTopology topology,
            int primVerts, int initialCapacity, float boundsPadding = 0f)
        {
            _name = name;
            _layout = layout;
            _topology = topology;
            _primVerts = primVerts;
            _boundsPadding = boundsPadding;

            _front = new GizmoNativeBuffer<T>(initialCapacity);
            _back = new GizmoNativeBuffer<T>(initialCapacity);
            _retained = new GizmoNativeBuffer<T>(64);
            _retainedExpiry = new GizmoNativeBuffer<float>(64);
        }

        public GizmoNativeBuffer<T> Back => _back;

        /// <summary>Вершин в том, что уходит на отрисовку: снимок кадра плюс отложенное.</summary>
        public int VertexCount => _front.Count + _retained.Count;

        /// <summary>Из них отложенных (нарисованных с duration).</summary>
        public int RetainedCount => _retained.Count;

        /// <summary>Упирался ли канал в потолок MaxVerticesPerChannel с прошлого снятия.</summary>
        public bool ConsumeOverflow()
        {
            // Без | вместо || : нужны все три вызова, каждый со своим сбросом.
            bool a = _front.ConsumeOverflow();
            bool b = _back.ConsumeOverflow();
            bool c = _retained.ConsumeOverflow();
            return a || b || c;
        }
        public GizmoNativeBuffer<T> Retained => _retained;
        public GizmoNativeBuffer<float> RetainedExpiry => _retainedExpiry;

        /// <summary>
        /// Выбор целевого буфера: retained (с временем жизни) или кадровый.
        /// Вызов с retained = true считается заявкой на запись: писать в буфер,
        /// не спросив его здесь, неоткуда, поэтому это единственная точка, где
        /// нужно поднять флаги.
        /// </summary>
        public GizmoNativeBuffer<T> Target(bool retained)
        {
            if (!retained) return _back;
            _retainedTouched = true;
            _dirty = true;
            _retainedVersion++;
            return _retained;
        }

        public void BeginFrame(bool strict, float now, float clock, float staleTimeout)
        {
            CompactRetained(now);

            // До обмена: в Back лежит этот кадр, во Front — прошлый, в retained — живое.
            _front.TickShrink();
            _back.TickShrink();
            _retained.TickShrink();
            _retainedExpiry.TickShrink();

            if (_back.Count > 0)
            {
                var tmp = _front;
                _front = _back;
                _back = tmp;
                _back.Clear();
                _lastDataClock = clock;
                _shown = false;
                _dirty = true;
            }
            else if (strict || (_shown && clock - _lastDataClock > staleTimeout))
            {
                // strict (play mode): нет новых команд — значит в этом кадре ничего не рисуем.
                //
                // edit mode: держим последний снимок, пока он не протух, иначе будет мерцание
                // между тиками EditorApplication.update и перерисовками вьюпорта. Протухание
                // мерится часами эдит-мода (GizmoRenderer.EditorClock) и только после того,
                // как снимок хоть раз дошёл до камеры.
                //
                // Чистим только когда есть что чистить: иначе на статичной сцене
                // каждый кадр взводился бы _dirty и весь смысл флага пропал бы.
                if (_front.Count > 0)
                {
                    _front.Clear();
                    _dirty = true;
                }
            }

            _prepared = false;
        }

        void CompactRetained(float now)
        {
            int n = _retained.Count;
            if (n == 0)
            {
                _minExpiry = float.MaxValue;
                _retainedTouched = false;
                return;
            }

            // Ничего не дописывали, и ближайшее истечение ещё впереди — значит
            // выбывать нечему, содержимое буфера то же, что и в прошлом кадре,
            // а Min/Max посчитаны тогда же и остались верными.
            //
            // Условие точное, а не приблизительное: ниже примитив удаляется по
            // e[r] <= now, и ни один не удалится ровно тогда, когда min(e) > now.
            if (!_retainedTouched && _minExpiry > now) return;

            T* v = _retained.Ptr;
            float* e = _retainedExpiry.Ptr;
            int stride = _primVerts;
            int w = 0;
            float minExpiry = float.MaxValue;

            _retained.ResetBounds();

            for (int r = 0; r < n; r += stride)
            {
                if (e[r] <= now) continue;
                if (e[r] < minExpiry) minExpiry = e[r];

                if (w != r)
                {
                    UnsafeUtility.MemCpy(v + w, v + r, (long)stride * sizeof(T));
                    UnsafeUtility.MemCpy(e + w, e + r, (long)stride * sizeof(float));
                }

                for (int k = 0; k < stride; k++)
                    _retained.Encapsulate(*(Vector3*)(v + w + k)); // Position всегда по смещению 0

                w += stride;
            }

            _retained.SetCount(w);
            _retainedExpiry.SetCount(w);

            if (w != n)                     // что-то выбыло — меш пора перезалить
            {
                _dirty = true;
                _retainedVersion++;
            }
            _minExpiry = minExpiry;
            _retainedTouched = false;
        }

        /// <summary>Заливает данные в меш (один раз за кадр) и отдаёт его на отрисовку.</summary>
        public bool Prepare(out Mesh mesh, out Bounds bounds)
        {
            if (!_prepared)
            {
                _prepared = true;

                int total = _retained.Count + _front.Count;
                VoteShrink(total);

                if (total == 0)
                {
                    _readyMesh = null;
                }
                else if (_dirty || _readyMesh == null)
                {
                    Upload(total);

                    // Второй меш кольца тоже ждёт пересоздания — заливка нужна и на следующем кадре.
                    _dirty = _reshape[0] || _reshape[1];
                }
                // иначе в меше с прошлого кадра лежат ровно эти данные: и вершины,
                // и bounds, и SubMeshDescriptor. Перезаливать нечего, курсор кольца
                // мешей не двигаем — переиспользуем тот же меш, как обычную статику.
            }

            mesh = _readyMesh;
            bounds = _readyBounds;
            if (_readyMesh != null) _shown = true;
            return _readyMesh != null;
        }

        void Upload(int total)
        {
            _cursor ^= 1;
            var m = _meshes[_cursor];
            if (m == null)
            {
                m = new Mesh { name = "~Gizmo_" + _name + "_" + _cursor, hideFlags = HideFlags.HideAndDontSave };
                m.MarkDynamic();
                m.subMeshCount = 1;
                _meshes[_cursor] = m;
            }

            // Ёмкость округляем вверх до степени двойки и меняем ТОЛЬКО при реальном росте.
            //
            // Раньше здесь стояло сравнение на неравенство, и буфер не только рос, но и
            // сжимался: геометрия, число вершин которой гуляет вокруг степени двойки
            // (1020 ↔ 1030 — обычное дело для путей, контактов, лучей), пересоздавала
            // GPU-буфер и перезаливала индексы почти каждый кадр.
            //
            // Сжатие оставлено, но с гистерезисом: буфер должен оказаться вчетверо больше
            // нужного ShrinkAfter заливок подряд. Разовый всплеск память отдаст, а колебание
            // вокруг границы — нет.
            int want = Mathf.Max(_primVerts, Mathf.NextPowerOfTwo(total));
            int have = _meshVertCap[_cursor];
            int cap = have;

            if (want > have) cap = want;
            else if (_reshape[_cursor]) cap = Mathf.Min(have, want * 2);   // с запасом на рост
            _reshape[_cursor] = false;

            if (cap != have)
            {
                m.SetVertexBufferParams(cap, _layout);
                _meshVertCap[_cursor] = cap;
                _meshIdxCap[_cursor] = 0; // принудительно перезалить индексы
                _meshRetainedVersion[_cursor] = 0;  // буфер пересоздан — префикса в нём больше нет
            }

            var format = cap <= GizmoIndexPool.MaxIndex16 ? IndexFormat.UInt16 : IndexFormat.UInt32;
            if (_meshIdxCap[_cursor] < cap || _meshIdxFormat[_cursor] != format)
            {
                m.SetIndexBufferParams(cap, format);
                if (format == IndexFormat.UInt16)
                {
                    GizmoIndexPool.Ensure16(cap);
                    m.SetIndexBufferData(GizmoIndexPool.Indices16, 0, 0, cap, GizmoMeshFlags.Silent);
                }
                else
                {
                    GizmoIndexPool.Ensure(cap);
                    m.SetIndexBufferData(GizmoIndexPool.Indices, 0, 0, cap, GizmoMeshFlags.Silent);
                }
                _meshIdxCap[_cursor] = cap;
                _meshIdxFormat[_cursor] = format;
            }

            if (_retained.Count > 0 && _meshRetainedVersion[_cursor] != _retainedVersion)
                m.SetVertexBufferData(_retained.Array, 0, 0, _retained.Count, 0, GizmoMeshFlags.Silent);
            _meshRetainedVersion[_cursor] = _retainedVersion;

            if (_front.Count > 0)
                m.SetVertexBufferData(_front.Array, 0, _retained.Count, _front.Count, 0, GizmoMeshFlags.Silent);

            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            if (_retained.HasBounds) { mn = Vector3.Min(mn, _retained.Min); mx = Vector3.Max(mx, _retained.Max); }
            if (_front.HasBounds) { mn = Vector3.Min(mn, _front.Min); mx = Vector3.Max(mx, _front.Max); }
            if (mn.x > mx.x) { mn = Vector3.zero; mx = Vector3.zero; }

            // Бесконечность в координатах (деление на ноль в пользовательском коде) дала бы
            // «Invalid AABB» на каждом кадре. NaN сюда не доходит — его отсекают сравнения
            // в Encapsulate, — а бесконечность проходит. Зажимаем: примитив с такой вершиной
            // всё равно не нарисовать, зато остальной канал останется видимым.
            var lim = new Vector3(BoundsLimit, BoundsLimit, BoundsLimit);
            mn = Vector3.Min(Vector3.Max(mn, -lim), lim);
            mx = Vector3.Min(Vector3.Max(mx, -lim), lim);

            var pad = new Vector3(_boundsPadding, _boundsPadding, _boundsPadding);
            var b = new Bounds();
            b.SetMinMax(mn - pad, mx + pad);

            var desc = new SubMeshDescriptor(0, total, _topology)
            {
                firstVertex = 0,
                vertexCount = total,
                baseVertex = 0,
                bounds = b
            };
            m.SetSubMesh(0, desc, GizmoMeshFlags.Silent);
            m.bounds = b;

            _readyMesh = m;
            _readyBounds = b;
        }

        void VoteShrink(int total)
        {
            int want = total == 0 ? 0 : Mathf.Max(_primVerts, Mathf.NextPowerOfTwo(total));
            int have = Mathf.Max(_meshVertCap[0], _meshVertCap[1]);

            if (have <= KeepVerts || (long)want * 4 > have)
            {
                _shrinkVotes = 0;
                return;
            }

            if (++_shrinkVotes < ShrinkAfter) return;
            _shrinkVotes = 0;

            if (total == 0)
            {
                ReleaseMeshes();
                return;
            }

            for (int i = 0; i < 2; i++)
                if ((long)want * 4 <= _meshVertCap[i]) _reshape[i] = true;
            _dirty = true;
        }

        void ReleaseMeshes()
        {
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(_meshes[i]);
                    else UnityEngine.Object.DestroyImmediate(_meshes[i]);
                    _meshes[i] = null;
                }
                _meshVertCap[i] = 0;
                _meshIdxCap[i] = 0;
                _meshRetainedVersion[i] = 0;
                _reshape[i] = false;
            }
            _readyMesh = null;
        }

        public void Clear()
        {
            _front.Clear();
            _back.Clear();
            _retained.Clear();
            _retainedExpiry.Clear();
            _prepared = false;
            _readyMesh = null;
            _shown = false;
            _dirty = true;
            _retainedTouched = false;
            _minExpiry = float.MaxValue;
            _retainedVersion++;
        }

        public void Dispose()
        {
            _front.Dispose();
            _back.Dispose();
            _retained.Dispose();
            _retainedExpiry.Dispose();
            ReleaseMeshes();
        }
    }
}
