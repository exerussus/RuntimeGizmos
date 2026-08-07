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
        int _cursor;

        bool _prepared;
        Mesh _readyMesh;
        Bounds _readyBounds;
        float _lastDataTime;

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
            return _retained;
        }

        public void BeginFrame(bool strict, float now, float staleTimeout)
        {
            CompactRetained(now);

            if (_back.Count > 0)
            {
                var tmp = _front;
                _front = _back;
                _back = tmp;
                _back.Clear();
                _lastDataTime = now;
                _dirty = true;
            }
            else if (strict || now - _lastDataTime > staleTimeout)
            {
                // strict (play mode): нет новых команд — значит в этом кадре ничего не рисуем.
                // edit mode: держим последний снимок, пока он не протух, иначе будет мерцание
                // между тиками EditorApplication.update и перерисовками вьюпорта.
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

            if (w != n) _dirty = true;      // что-то выбыло — меш пора перезалить
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
                if (total == 0)
                {
                    _readyMesh = null;
                }
                else if (_dirty || _readyMesh == null)
                {
                    Upload(total);
                    _dirty = false;
                }
                // иначе в меше с прошлого кадра лежат ровно эти данные: и вершины,
                // и bounds, и SubMeshDescriptor. Перезаливать нечего, курсор кольца
                // мешей не двигаем — переиспользуем тот же меш, как обычную статику.
            }

            mesh = _readyMesh;
            bounds = _readyBounds;
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

            // Ёмкость округляем вверх до степени двойки — реаллокация GPU-буферов происходит
            // только при реальном росте, а не каждый кадр.
            int cap = Mathf.Max(_primVerts, Mathf.NextPowerOfTwo(total));
            if (_meshVertCap[_cursor] != cap)
            {
                m.SetVertexBufferParams(cap, _layout);
                _meshVertCap[_cursor] = cap;
                _meshIdxCap[_cursor] = 0; // принудительно перезалить индексы
            }

            if (_meshIdxCap[_cursor] < cap)
            {
                GizmoIndexPool.Ensure(cap);
                m.SetIndexBufferParams(cap, IndexFormat.UInt32);
                m.SetIndexBufferData(GizmoIndexPool.Indices, 0, 0, cap, GizmoMeshFlags.Silent);
                _meshIdxCap[_cursor] = cap;
            }

            if (_retained.Count > 0)
                m.SetVertexBufferData(_retained.Array, 0, 0, _retained.Count, 0, GizmoMeshFlags.Silent);
            if (_front.Count > 0)
                m.SetVertexBufferData(_front.Array, 0, _retained.Count, _front.Count, 0, GizmoMeshFlags.Silent);

            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            if (_retained.HasBounds) { mn = Vector3.Min(mn, _retained.Min); mx = Vector3.Max(mx, _retained.Max); }
            if (_front.HasBounds) { mn = Vector3.Min(mn, _front.Min); mx = Vector3.Max(mx, _front.Max); }
            if (mn.x > mx.x) { mn = Vector3.zero; mx = Vector3.zero; }

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

        public void Clear()
        {
            _front.Clear();
            _back.Clear();
            _retained.Clear();
            _retainedExpiry.Clear();
            _prepared = false;
            _readyMesh = null;
            _dirty = true;
            _retainedTouched = false;
            _minExpiry = float.MaxValue;
        }

        public void Dispose()
        {
            _front.Dispose();
            _back.Dispose();
            _retained.Dispose();
            _retainedExpiry.Dispose();
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_meshes[i]);
                else UnityEngine.Object.DestroyImmediate(_meshes[i]);
                _meshes[i] = null;
            }
        }
    }
}
