using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeGizmos.Internal
{
    internal struct GizmoMeshCmd
    {
        public Mesh Mesh;
        public int Submesh;
        public Matrix4x4 Matrix;
        public Color Color;
        public int Z;
        public float Expiry;
    }

    internal sealed class GizmoMeshCmdList
    {
        public GizmoMeshCmd[] Items = new GizmoMeshCmd[16];
        public int Count;

        public ref GizmoMeshCmd Add()
        {
            if (Count == Items.Length) Array.Resize(ref Items, Count << 1);
            return ref Items[Count++];
        }

        public void Clear() => Count = 0;
    }

    internal sealed class GizmoTexturedBatch : IDisposable
    {
        public Texture Texture;
        public readonly GizmoChannel<GizmoQuadVertex> Channel;
        public readonly MaterialPropertyBlock Props = new MaterialPropertyBlock();

        public GizmoTexturedBatch(Texture tex, string name)
        {
            Texture = tex;
            Channel = new GizmoChannel<GizmoQuadVertex>(name, GizmoVertexLayouts.Quad,
                MeshTopology.Triangles, 6, 64);
            Props.SetTexture(GizmoRenderer.MainTexId, tex);
        }

        public void Dispose() => Channel.Dispose();
    }

    /// <summary>
    /// Ядро. Хранит состояние отрисовки, каналы батчинга и материалы,
    /// а также рассылает готовые меши по камерам через Graphics.RenderMesh.
    /// </summary>
    internal static unsafe class GizmoRenderer
    {
        // ------------------------------------------------------------------ состояние (горячий путь)
        internal static bool Enabled = true;
        internal static Color32 Color = new Color32(255, 255, 255, 255);
        internal static float Width;      // <=1 → тонкая линия (MeshTopology.Lines)

        // Толщину никто не выставлял — в Width лежит дефолт платформы. Нужен Ensure: тот обязан
        // подтянуть свежий дефолт (платформа могла уточниться, см. RedetectPlatform), но не
        // имеет права затирать значение, которое пользователь выставил до первого Draw*.
        internal static bool WidthIsDefault = true;
        internal static int Z;            // 0 = с тестом глубины, 1 = поверх всего, 2 = призрак

        /// <summary>
        /// Маска категорий, в которые пишут последующие Draw*. Проверяется в Begin() против
        /// GizmoSettings.EnabledCategories: вызов в выключенной категории не доходит до буферов
        /// и не считается данными кадра.
        /// </summary>
        internal static int Category = Gizmo.DefaultCategory;
        internal static float Duration;   // 0 = один кадр
        internal static float Dash;       // период пунктира в юнитах, 0 = сплошная

        // Накопитель длины вдоль ломаной: без него у каждого сегмента фаза начиналась
        // бы с нуля и штрихи ломались на каждом изломе пути.
        internal static float DashRun;

        // Фаза для отрезка [a,b]. Возвращает -1 у сплошных линий.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DashPhase(in Vector3 a, in Vector3 b, out float pa, out float pb)
        {
            if (Dash <= 0f) { pa = -1f; pb = -1f; return; }
            float inv = 1f / Dash;
            pa = DashRun * inv;
            DashRun += (b - a).magnitude;
            pb = DashRun * inv;
        }
        internal static float Now;

        internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWriteMode");
        static readonly int BiasId = Shader.PropertyToID("_DepthBias");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        // ------------------------------------------------------------------ каналы
        static GizmoChannel<GizmoVertex>[] _thin;
        static GizmoChannel<GizmoWideVertex>[] _wide;
        static GizmoChannel<GizmoVertex>[] _tri;

        static Material[] _thinMat, _wideMat, _triMat, _meshMat, _iconMat, _screenMat;

        /// <summary>
        /// Материалы «перекрытой» половины призрачного слоя: ZTest Greater и своя альфа.
        /// Слой Z = 2 отправляется дважды одним и тем же мешем — обычным материалом и этим.
        /// Двойного блендинга не возникает: LEqual и Greater делят пиксели без пересечения.
        /// </summary>
        static Material _thinMatOcc, _wideMatOcc, _triMatOcc, _meshMatOcc, _textMatOcc;

        /// <summary>Слои глубины: 0 — с тестом, 1 — поверх всего, 2 — призрак.</summary>
        internal const int ZCount = 3;

        /// <summary>Индекс призрачного слоя.</summary>
        internal const int ZGhost = 2;

        static readonly Dictionary<GizmoObjectId, GizmoTexturedBatch> _icons = new Dictionary<GizmoObjectId, GizmoTexturedBatch>();

        // Иконки «поверх всего» (Gizmo.depthTest = false). Раньше батч ключевался одной
        // текстурой и всегда рисовался материалом с тестом глубины: второй материал
        // создавался, но не использовался, и depthTest на иконки не действовал.
        static readonly Dictionary<GizmoObjectId, GizmoTexturedBatch> _iconsTop = new Dictionary<GizmoObjectId, GizmoTexturedBatch>();
        static readonly Dictionary<GizmoObjectId, GizmoTexturedBatch> _screen = new Dictionary<GizmoObjectId, GizmoTexturedBatch>();

        static GizmoMeshCmdList _meshFront = new GizmoMeshCmdList();
        static GizmoMeshCmdList _meshBack = new GizmoMeshCmdList();
        static readonly GizmoMeshCmdList _meshRetained = new GizmoMeshCmdList();

        // Пул property-блоков: по одному на команду отрисовки меша. Так мы не зависим
        // от того, копирует ли RenderParams.matProps данные в момент сабмита.
        static readonly List<MaterialPropertyBlock> _mpbPool = new List<MaterialPropertyBlock>();
        static int _mpbCursor;
        static bool _ready;
        static float _meshLastData;
        static bool _meshShown;
        static bool _linearColor;

        // Последняя альфа, уже разложенная по материалам. GlobalAlpha меняется редко,
        // а SetFloat дёргался на каждый канал, на каждую камеру, каждый кадр.
        // NaN на старте: любое сравнение с ним ложно, поэтому первый Submit применит
        // значение в любом случае, даже если оно совпало с тем, что выставил MakeMat.
        static float _appliedAlpha = float.NaN;
        static float _appliedGhost = float.NaN;

        // ==================================================================== init / teardown

        static bool _failed;

        static GizmoChannel<GizmoTextVertex>[] _text;
        static Material[] _textMat;

        /// <summary>Поток, на котором система была установлена. Всё остальное — ошибка.</summary>
        internal static int MainThreadId;

        /// <summary>Рендерер поднят: материалы, шрифт и буферы на месте.</summary>
        internal static bool Ready => _ready;

        internal static void Ensure()
        {
            if (_ready || _failed) return;

            // Платформа кэшируется при первом обращении к настройкам, а оно случается на
            // SubsystemRegistration — раньше, чем XR Management поднимает загрузчик. К первому
            // Draw* устройство уже активно, поэтому перепроверяем здесь: иначе профиль XR
            // автоматически не выбирался никогда, и шлем получал профиль Mobile.
            GizmoSettings.RedetectPlatform();

            _linearColor = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Now = Time.realtimeSinceStartup;
            GizmoPrimitives.Ensure();

            var unlit = LoadShader("GizmoUnlit");
            var wide = LoadShader("GizmoWideLine");
            var icon = LoadShader("GizmoBillboard");
            var screen = LoadShader("GizmoScreen");
            var text = LoadShader("GizmoText");

            if (unlit == null || wide == null || icon == null || screen == null || text == null)
            {
                _failed = true;
                _disabledByFailure = Enabled;
                Enabled = false;
                return;
            }

            _ready = true;
            _appliedAlpha = float.NaN;      // материалы пересозданы — кэш недействителен
            _appliedGhost = float.NaN;

            // Раньше здесь стояло безусловное Width = DefaultLineWidth, и Gizmo.lineWidth = 3,
            // выставленный в Awake до первого Draw*, молча сбрасывался первым же вызовом.
            if (WidthIsDefault) Width = GizmoSettings.DefaultLineWidth;

            _thinMat = new Material[ZCount];
            _wideMat = new Material[ZCount];
            _triMat = new Material[ZCount];
            _meshMat = new Material[ZCount];
            _iconMat = new Material[2];
            _textMat = new Material[ZCount];
            _screenMat = new Material[1];

            for (int z = 0; z < ZCount; z++)
            {
                // Призрачный слой уважает глубину так же, как обычный: отличается он только
                // тем, что перекрытую половину рисует ещё раз приглушёнными материалами ниже.
                bool depth = z != 1;
                bool ghost = z == ZGhost;
                int zTest = depth ? (int)CompareFunction.LessEqual : (int)CompareFunction.Always;

                // У призрака своя очередь: с обычным слоем они рисуют одно и то же место
                // экрана, и порядок между ними не должен решаться сортировщиком прозрачных.
                int queue = (int)RenderQueue.Transparent + (ghost ? 200 : depth ? 100 : 400);

                float bias = depth ? GizmoSettings.DepthBias : 0f;

                _thinMat[z] = MakeMat(unlit, "GizmoThin", zTest, 0, queue, bias);
                _wideMat[z] = MakeMat(wide, "GizmoWide", zTest, 0, queue, bias);
                // Сплошные фигуры с тестом глубины пишут в depth — так они корректно
                // перекрывают друг друга. Режим "поверх всего" depth не пишет.
                //
                // Призрак не пишет тоже, и это принципиально: обе его половины обязаны
                // тестироваться против ОДНОГО И ТОГО ЖЕ буфера — того, что оставила сцена.
                // Стоило видимой половине записать туда себя, и перекрытая начинала ловить
                // собственные дальние грани: LEqual и Greater переставали делить пиксели,
                // часть фигуры блендилась дважды, а какая именно — решал порядок сортировки.
                // Цена — призрачные сплошные фигуры не перекрывают друг друга; для режима,
                // который весь про «видно сквозь», это ожидаемо.
                bool zwrite = depth && !ghost;
                _triMat[z] = MakeMat(unlit, "GizmoSolid", zTest, zwrite ? 1 : 0, queue - 10, 0f);
                _meshMat[z] = MakeMat(unlit, "GizmoMesh", zTest, zwrite ? 1 : 0, queue - 10, 0f);
                if (z < _iconMat.Length) _iconMat[z] = MakeMat(icon, "GizmoIcon", zTest, 0, queue + 10, bias);
                _textMat[z] = MakeMat(text, "GizmoText", zTest, 0, queue + 20, bias);
            }

            // Перекрытая половина призрака. ZWrite всегда выключен: прятать за собой то,
            // что на самом деле ближе к камере, нельзя даже сплошным фигурам.
            {
                int greater = (int)CompareFunction.Greater;
                int queue = (int)RenderQueue.Transparent + 200;   // та же очередь, что у видимой половины
                float bias = GizmoSettings.DepthBias;

                _thinMatOcc = MakeMat(unlit, "GizmoThinOccluded", greater, 0, queue, bias);
                _wideMatOcc = MakeMat(wide, "GizmoWideOccluded", greater, 0, queue, bias);
                _triMatOcc = MakeMat(unlit, "GizmoSolidOccluded", greater, 0, queue - 10, 0f);
                _meshMatOcc = MakeMat(unlit, "GizmoMeshOccluded", greater, 0, queue - 10, 0f);
                _textMatOcc = MakeMat(text, "GizmoTextOccluded", greater, 0, queue + 20, bias);
            }

            _screenMat[0] = MakeMat(screen, "GizmoScreen", (int)CompareFunction.Always, 0,
                (int)RenderQueue.Overlay, 0f);

            _thin = new[]
            {
                new GizmoChannel<GizmoVertex>("thin0", GizmoVertexLayouts.Thin, MeshTopology.Lines, 2, 2048),
                new GizmoChannel<GizmoVertex>("thin1", GizmoVertexLayouts.Thin, MeshTopology.Lines, 2, 512),
                new GizmoChannel<GizmoVertex>("thin2", GizmoVertexLayouts.Thin, MeshTopology.Lines, 2, 512),
            };
            _wide = new[]
            {
                new GizmoChannel<GizmoWideVertex>("wide0", GizmoVertexLayouts.Wide, MeshTopology.Triangles, 6, 512, 0.25f),
                new GizmoChannel<GizmoWideVertex>("wide1", GizmoVertexLayouts.Wide, MeshTopology.Triangles, 6, 256, 0.25f),
                new GizmoChannel<GizmoWideVertex>("wide2", GizmoVertexLayouts.Wide, MeshTopology.Triangles, 6, 256, 0.25f),
            };
            _tri = new[]
            {
                new GizmoChannel<GizmoVertex>("tri0", GizmoVertexLayouts.Thin, MeshTopology.Triangles, 3, 1024),
                new GizmoChannel<GizmoVertex>("tri1", GizmoVertexLayouts.Thin, MeshTopology.Triangles, 3, 256),
                new GizmoChannel<GizmoVertex>("tri2", GizmoVertexLayouts.Thin, MeshTopology.Triangles, 3, 256),
            };
            _text = new[]
            {
                new GizmoChannel<GizmoTextVertex>("text0", GizmoVertexLayouts.Text, MeshTopology.Triangles, 6, 512, 0.25f),
                new GizmoChannel<GizmoTextVertex>("text1", GizmoVertexLayouts.Text, MeshTopology.Triangles, 6, 256, 0.25f),
                new GizmoChannel<GizmoTextVertex>("text2", GizmoVertexLayouts.Text, MeshTopology.Triangles, 6, 256, 0.25f),
            };
        }

        static Shader LoadShader(string name)
        {
            var s = Resources.Load<Shader>("RuntimeGizmos/" + name);
            if (s == null) s = Shader.Find("Hidden/RuntimeGizmos/" + name.Replace("Gizmo", ""));
            if (s == null)
                Debug.LogError($"[RuntimeGizmos] Не найден шейдер RuntimeGizmos/{name}. " +
                               "Папка Resources/RuntimeGizmos должна лежать в проекте.");
            else if (!s.isSupported)
            {
                Debug.LogError($"[RuntimeGizmos] Шейдер {s.name} не скомпилировался на этой платформе. " +
                               "Шейдеры пакета рассчитаны на URP; убедитесь, что пакет " +
                               "com.unity.render-pipelines.universal установлен.");
                return null;
            }
            return s;
        }

        static Material MakeMat(Shader sh, string name, int zTest, int zWrite, int queue, float depthBias)
        {
            var m = new Material(sh) { name = name, hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat(ZTestId, zTest);
            m.SetFloat(ZWriteId, zWrite);
            m.SetFloat(BiasId, depthBias);
            m.SetFloat(AlphaId, 1f);
            m.renderQueue = queue;
            return m;
        }

        // Систему выключил неудачный Ensure, а не пользователь. Dispose снимает _failed, чтобы
        // следующая сессия попробовала подняться заново, — но Enabled оставался false, и после
        // починки шейдеров (или назначения URP-ассета) гизмо молчали до перезагрузки домена.
        static bool _disabledByFailure;

        // Явное слово пользователя сильнее: после него Dispose ничего сам не включает.
        internal static void SetEnabledByUser(bool value)
        {
            Enabled = value;
            _disabledByFailure = false;
        }

        // Меши, про которые уже сказано «нет Read/Write». Живёт здесь, а не в расширениях,
        // чтобы чиститься на границе сессии вместе со всем остальным.
        internal static readonly HashSet<GizmoObjectId> WarnedUnreadable = new HashSet<GizmoObjectId>();

        internal static void Dispose()
        {
            _failed = false;
            if (_disabledByFailure)
            {
                _disabledByFailure = false;
                Enabled = true;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _threadReported = false;
#endif
            if (!_ready) return;
            _ready = false;

            DisposeChannels(_thin); DisposeChannels(_wide); DisposeChannels(_tri);
            DisposeChannels(_text);
            foreach (var b in _icons.Values) b.Dispose();
            foreach (var b in _iconsTop.Values) b.Dispose();
            foreach (var b in _screen.Values) b.Dispose();
            _icons.Clear();
            _iconsTop.Clear();
            _screen.Clear();

            DestroyMats(_thinMat); DestroyMats(_wideMat); DestroyMats(_triMat);
            DestroyMats(_meshMat); DestroyMats(_iconMat); DestroyMats(_screenMat);
            DestroyMats(_textMat);
            DestroyMat(ref _thinMatOcc); DestroyMat(ref _wideMatOcc); DestroyMat(ref _triMatOcc);
            DestroyMat(ref _meshMatOcc); DestroyMat(ref _textMatOcc);

            WarnedUnreadable.Clear();

            GizmoIndexPool.Dispose();
            GizmoPrimitives.Dispose();
            GizmoFont.Dispose();
            GizmoHud.Dispose();
            GizmoWireMeshCache.Dispose();
        }

        static void DisposeChannels<T>(GizmoChannel<T>[] arr) where T : unmanaged
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) arr[i]?.Dispose();
        }

        static void DestroyMat(ref Material m)
        {
            if (m == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(m);
            else UnityEngine.Object.DestroyImmediate(m);
            m = null;
        }

        static void DestroyMats(Material[] arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(arr[i]);
                else UnityEngine.Object.DestroyImmediate(arr[i]);
                arr[i] = null;
            }
        }

        internal static void ClearAll()
        {
            if (!_ready) return;
            for (int i = 0; i < ZCount; i++) { _thin[i].Clear(); _wide[i].Clear(); _tri[i].Clear(); _text[i].Clear(); }
            foreach (var b in _icons.Values) b.Channel.Clear();
            foreach (var b in _iconsTop.Values) b.Channel.Clear();
            foreach (var b in _screen.Values) b.Channel.Clear();
            _meshFront.Clear(); _meshBack.Clear(); _meshRetained.Clear();
        }

        // ==================================================================== часы эдит-мода

        /// <summary>
        /// Часы, по которым в эдит-моде считается протухание последнего снимка.
        ///
        /// Стенные часы для этого не годятся. Продюсер геометрии в эдит-моде — это
        /// Update компонента с [ExecuteAlways] или вызов из кода редактора, и он
        /// молчит вместе с редактором: переключение окна, потеря фокуса, импорт
        /// ассета, компиляция. Стенное время при этом идёт, и первый же тик после
        /// паузы видел разрыв больше таймаута и сбрасывал снимок — а вьюпорт успевал
        /// перерисоваться пустым. Это и есть мигание при переключении окон.
        ///
        /// Поэтому шаг часов зажат: сколь угодно долгая пауза считается за один
        /// обычный кадр. Снимок переживает переключение окон, компиляцию и импорт,
        /// а по-настоящему замолчавший продюсер по-прежнему гаснет за
        /// EditorStaleTimeout секунд живого редактора.
        /// </summary>
        internal static float EditorClock;

        /// <summary>
        /// Эдит-мод: часы стоят.
        ///
        /// Ставится, когда редактор не активен в системе. Продюсер геометрии в это время
        /// всё равно не работает, а гасить снимок за то, что человек ушёл в другое
        /// приложение, нельзя: он наведёт курсор на неактивное окно Unity — а Game View
        /// перерисуется и покажет пустоту.
        /// </summary>
        internal static bool EditorClockPaused;

        /// <summary>Максимальный шаг часов эдит-мода за одну границу кадра.</summary>
        const float EditorClockMaxStep = 0.05f;

        static float _editorClockReal;
        static float _editorLastData = float.NegativeInfinity;

        /// <summary>
        /// Сколько секунд часов эдит-мода прошло с последних команд Draw*.
        /// Отрицательное значение — команд в этой сессии не было вовсе.
        /// </summary>
        internal static float EditorSinceData
            => float.IsNegativeInfinity(_editorLastData) ? -1f : EditorClock - _editorLastData;

        static void AdvanceEditorClock()
        {
            float real = Time.realtimeSinceStartup;
            float dt = real - _editorClockReal;
            _editorClockReal = real;   // базу двигаем всегда: иначе пауза «догонит» после возврата

            if (EditorClockPaused) return;

            // dt <= 0 — часы перезапустились вместе с сессией (или это первый вызов).
            if (dt <= 0f) return;

            EditorClock += dt < EditorClockMaxStep ? dt : EditorClockMaxStep;
        }

        // ==================================================================== кадр

        internal static bool HasProducedData;

        // ------------------------------------------------------------------ счётчики кадра
        //
        // Снимаются на границе кадра, уже после обмена буферов, — то есть описывают ровно ту
        // геометрию, которую увидят камеры. Публичный доступ — через Gizmo.Stats.

        internal static int StatVertices, StatRetained, StatPeakChannel, StatMeshes, StatDrawCalls;
        internal static bool StatOverflow;

        static int _drawCalls;

        static void CollectStats()
        {
            int verts = 0, retained = 0, peak = 0;
            bool over = false;

            for (int i = 0; i < ZCount; i++)
            {
                Account(_thin[i].VertexCount, _thin[i].RetainedCount, _thin[i].ConsumeOverflow());
                Account(_wide[i].VertexCount, _wide[i].RetainedCount, _wide[i].ConsumeOverflow());
                Account(_tri[i].VertexCount, _tri[i].RetainedCount, _tri[i].ConsumeOverflow());
                Account(_text[i].VertexCount, _text[i].RetainedCount, _text[i].ConsumeOverflow());
            }

            foreach (var b in _icons.Values) Account(b.Channel.VertexCount, b.Channel.RetainedCount, b.Channel.ConsumeOverflow());
            foreach (var b in _iconsTop.Values) Account(b.Channel.VertexCount, b.Channel.RetainedCount, b.Channel.ConsumeOverflow());
            foreach (var b in _screen.Values) Account(b.Channel.VertexCount, b.Channel.RetainedCount, b.Channel.ConsumeOverflow());

            StatVertices = verts;
            StatRetained = retained;
            StatPeakChannel = peak;
            StatMeshes = _meshFront.Count + _meshRetained.Count;
            StatOverflow = over;

            void Account(int count, int ret, bool overflowed)
            {
                verts += count;
                retained += ret;
                if (count > peak) peak = count;
                over |= overflowed;
            }
        }

        internal static void BeginFrame(bool strict)
        {
            if (!_ready)
            {
                // Рендерера ещё нет, но Draw* уже могли накопить экранных блоков:
                // без сброса они дожили бы до следующего кадра и удвоились.
                GizmoHud.Reset();
                return;
            }

            // Экранная раскладка выпускается ДО обмена буферов: иначе всё, что она нарисует,
            // уедет во Front только на следующей границе кадра и опоздает ровно на кадр.
            GizmoHud.Flush();

            // Истечение считаем по ТОМУ ЖЕ времени, которым штамповались команды этого кадра.
            //
            // Draw* идёт из Update/LateUpdate, то есть до BeginFrame, и берёт Now, выставленный
            // на прошлой границе кадра. Если бы мы сначала обновили Now, а потом проверяли
            // Expiry > Now, то любая длительность короче кадра истекала бы мгновенно: duration = 0
            // жил бы кадр, а duration = 0.001 — ноль кадров. Теперь геометрия с duration > 0
            // гарантированно переживает кадр, в котором была нарисована.
            float t = Now;
            float stale = GizmoSettings.EditorStaleTimeout;

            // Протухание снимка в эдит-моде считается не стенными часами, а своими —
            // с зажатым шагом. Подробности у EditorClock.
            if (!strict) AdvanceEditorClock();
            float clock = EditorClock;

            for (int i = 0; i < ZCount; i++)
            {
                _thin[i].BeginFrame(strict, t, clock, stale);
                _wide[i].BeginFrame(strict, t, clock, stale);
                _tri[i].BeginFrame(strict, t, clock, stale);
                _text[i].BeginFrame(strict, t, clock, stale);
            }

            foreach (var b in _icons.Values) b.Channel.BeginFrame(strict, t, clock, stale);
            foreach (var b in _iconsTop.Values) b.Channel.BeginFrame(strict, t, clock, stale);
            foreach (var b in _screen.Values) b.Channel.BeginFrame(strict, t, clock, stale);

            // Текстуру могли уничтожить — батч с ней уже ничего не нарисует, а нативные
            // буферы и меши держит. Словари крошечные, обход раз в кадр бесплатный.
            PruneDeadBatches(_icons);
            PruneDeadBatches(_iconsTop);
            PruneDeadBatches(_screen);

            // отложенные меши
            int w = 0;
            for (int i = 0; i < _meshRetained.Count; i++)
                if (_meshRetained.Items[i].Expiry > t)
                    _meshRetained.Items[w++] = _meshRetained.Items[i];
            _meshRetained.Count = w;

            if (_meshBack.Count > 0)
            {
                var tmp = _meshFront; _meshFront = _meshBack; _meshBack = tmp;
                _meshBack.Clear();
                _meshLastData = clock;
                _meshShown = false;
            }
            else if (strict || (_meshShown && clock - _meshLastData > stale))
            {
                _meshFront.Clear();
            }

            DashRun = 0f;

            CollectStats();

            // Диагностика страницы настроек: когда продюсер последний раз что-то нарисовал.
            if (HasProducedData) _editorLastData = clock;
            HasProducedData = false;

            // Штамп для команд следующего кадра.
            Now = Time.realtimeSinceStartup;
        }

        static readonly List<GizmoObjectId> _deadBatches = new List<GizmoObjectId>();

        static void PruneDeadBatches(Dictionary<GizmoObjectId, GizmoTexturedBatch> batches)
        {
            if (batches.Count == 0) return;

            _deadBatches.Clear();
            foreach (var kv in batches)
                if (kv.Value.Texture == null) _deadBatches.Add(kv.Key);

            for (int i = 0; i < _deadBatches.Count; i++)
            {
                batches[_deadBatches[i]].Dispose();
                batches.Remove(_deadBatches[i]);
            }
        }

        // ==================================================================== отправка на камеру

        internal static void Submit(Camera cam)
        {
            if (!_ready || cam == null) return;

            switch (cam.cameraType)
            {
                case CameraType.Game: if (!GizmoSettings.DrawInGameView) return; break;
                case CameraType.SceneView: if (!GizmoSettings.DrawInSceneView) return; break;
                case CameraType.Preview:
                case CameraType.Reflection:
                case CameraType.VR:
                default: if (!GizmoSettings.DrawInOtherCameras) return; break;
            }

            var rp = new RenderParams
            {
                layer = GizmoSettings.Layer,
                renderingLayerMask = GizmoSettings.RenderingLayerMask,
                camera = cam,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                rendererPriority = 0,
                matProps = null,
            };

            float alpha = GizmoSettings.GlobalAlpha;
            ApplyAlpha(alpha, GizmoSettings.GhostAlpha);

            // Счётчик обнуляется на камеру: у всех камер кадра он одинаковый, и в статистику
            // попадает последняя — «сколько сабмитов стоит один показ».
            _drawCalls = 0;

            for (int z = 0; z < ZCount; z++)
            {
                bool ghost = z == ZGhost;
                if (_tri[z].Prepare(out var m, out var b))
                {
                    Emit(ref rp, _triMat[z], m, b);
                    if (ghost) Emit(ref rp, _triMatOcc, m, b);
                }
                if (_thin[z].Prepare(out m, out b))
                {
                    Emit(ref rp, _thinMat[z], m, b);
                    if (ghost) Emit(ref rp, _thinMatOcc, m, b);
                }
                if (_wide[z].Prepare(out m, out b))
                {
                    Emit(ref rp, _wideMat[z], m, b);
                    if (ghost) Emit(ref rp, _wideMatOcc, m, b);
                }
            }

            var textBounds = new Bounds(cam.transform.position, new Vector3(1e5f, 1e5f, 1e5f));
            for (int z = 0; z < ZCount; z++)
                if (_text[z].Prepare(out var tm, out _))
                {
                    Emit(ref rp, _textMat[z], tm, textBounds);
                    if (z == ZGhost) Emit(ref rp, _textMatOcc, tm, textBounds);
                }

            // Иконки, как и текст, отсекаются боксом вокруг камеры, а не своими габаритами.
            // Канал знает только центры квадов: размер у них в пикселях (или в мире, но
            // разворачивается шейдером), и бокс из центров отсекал иконку целиком, стоило
            // её центру выйти за край кадра, — хотя половина ещё была видна.
            var camBounds = new Bounds(cam.transform.position, new Vector3(1e4f, 1e4f, 1e4f));
            SubmitIcons(ref rp, _icons, _iconMat[0], camBounds);
            SubmitIcons(ref rp, _iconsTop, _iconMat[1], camBounds);

            if (_screen.Count > 0)
            {
                // Экранная геометрия задаётся прямо в клип-пространстве, поэтому
                // ограничивающий бокс привязываем к камере, иначе её отсечёт куллинг.
                foreach (var kv in _screen)
                    if (kv.Value.Channel.Prepare(out var m, out _))
                    {
                        if (kv.Value.Texture == null) continue;
                        rp.material = _screenMat[0];
                        rp.matProps = kv.Value.Props;
                        rp.worldBounds = camBounds;
                        Graphics.RenderMesh(rp, m, 0, Matrix4x4.identity);
                        rp.matProps = null;
                        _drawCalls++;
                    }
            }

            _mpbCursor = 0;
            SubmitMeshList(ref rp, _meshRetained);
            SubmitMeshList(ref rp, _meshFront);
            if (_meshFront.Count > 0) _meshShown = true;

            StatDrawCalls = _drawCalls;
        }

        /// <summary>
        /// Раскладывает глобальную альфу по материалам — но только когда она
        /// действительно поменялась. Набор материалов здесь ровно тот, что получал
        /// альфу раньше: иконки и экранные квады её не берут ни до, ни после.
        /// Перекрытая половина призрака получает произведение: глушится она поверх
        /// глобальной альфы, а не вместо неё.
        /// </summary>
        static void ApplyAlpha(float alpha, float ghost)
        {
            if (alpha == _appliedAlpha && ghost == _appliedGhost) return;
            _appliedAlpha = alpha;
            _appliedGhost = ghost;

            for (int z = 0; z < ZCount; z++)
            {
                _triMat[z].SetFloat(AlphaId, alpha);
                _thinMat[z].SetFloat(AlphaId, alpha);
                _wideMat[z].SetFloat(AlphaId, alpha);
                _textMat[z].SetFloat(AlphaId, alpha);
                _meshMat[z].SetFloat(AlphaId, alpha);
            }

            float occ = alpha * ghost;
            _triMatOcc.SetFloat(AlphaId, occ);
            _thinMatOcc.SetFloat(AlphaId, occ);
            _wideMatOcc.SetFloat(AlphaId, occ);
            _textMatOcc.SetFloat(AlphaId, occ);
            _meshMatOcc.SetFloat(AlphaId, occ);
        }

        static void SubmitIcons(ref RenderParams rp, Dictionary<GizmoObjectId, GizmoTexturedBatch> batches,
                                Material mat, Bounds camBounds)
        {
            foreach (var kv in batches)
                if (kv.Value.Channel.Prepare(out var m, out _))
                {
                    if (kv.Value.Texture == null) continue;
                    rp.material = mat;
                    rp.matProps = kv.Value.Props;
                    rp.worldBounds = camBounds;
                    Graphics.RenderMesh(rp, m, 0, Matrix4x4.identity);
                    rp.matProps = null;
                    _drawCalls++;
                }
        }

        static void Emit(ref RenderParams rp, Material mat, Mesh mesh, Bounds b)
        {
            rp.material = mat;
            rp.worldBounds = b;
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
            _drawCalls++;
        }

        static void SubmitMeshList(ref RenderParams rp, GizmoMeshCmdList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ref var c = ref list.Items[i];
                if (c.Mesh == null) continue;

                var mat = _meshMat[c.Z];

                while (_mpbPool.Count <= _mpbCursor) _mpbPool.Add(new MaterialPropertyBlock());
                var mpb = _mpbPool[_mpbCursor++];

                var col = c.Color;
                mpb.SetColor(ColorId, _linearColor ? col.linear : col);
                rp.material = mat;
                rp.matProps = mpb;

                var b = c.Mesh.bounds;
                var center = c.Matrix.MultiplyPoint3x4(b.center);
                var ext = b.extents;
                var m = c.Matrix;
                var wext = new Vector3(
                    Mathf.Abs(m.m00) * ext.x + Mathf.Abs(m.m01) * ext.y + Mathf.Abs(m.m02) * ext.z,
                    Mathf.Abs(m.m10) * ext.x + Mathf.Abs(m.m11) * ext.y + Mathf.Abs(m.m12) * ext.z,
                    Mathf.Abs(m.m20) * ext.x + Mathf.Abs(m.m21) * ext.y + Mathf.Abs(m.m22) * ext.z);
                rp.worldBounds = new Bounds(center, wext * 2f);

                Graphics.RenderMesh(rp, c.Mesh, c.Submesh, c.Matrix);
                _drawCalls++;

                if (c.Z == ZGhost)
                {
                    rp.material = _meshMatOcc;
                    Graphics.RenderMesh(rp, c.Mesh, c.Submesh, c.Matrix);
                    _drawCalls++;
                }

                rp.matProps = null;
            }
        }

        // ==================================================================== запись примитивов

        /// <summary>
        /// Дойдёт ли до буферов вызов с текущим состоянием: пакет включён и текущая категория
        /// не выключена. Те же две проверки, что открывают Begin(), но без подъёма рендерера
        /// и без отметки «в кадре были данные» — чтобы составные вызовы могли выйти ДО своей
        /// работы. Иначе выключенная категория отсекала только запись в буфер: капсула
        /// продолжала считать 264 синуса и косинуса, DrawVolume(Transform) — обходить иерархию,
        /// DrawNormals — читать меш целиком.
        /// </summary>
        internal static bool Active
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Enabled && (Category & GizmoSettings.EnabledCategories) != 0;
        }

        // internal, а не private: экранная раскладка (GizmoHud) — такой же вход в систему,
        // как Draw*, и обязана проходить ту же проверку. Пока она шла мимо, проект, в котором
        // рисуется только HUD, не поднимал рендерер вовсе и оставался с пустым экраном.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool Begin()
        {
            if (!Enabled) return false;

            // Выключенная категория — это «не рисовать», а не «нарисовать и спрятать»:
            // ни буферов, ни отметки о данных кадра, ни перерисовки в эдит-моде.
            if ((Category & GizmoSettings.EnabledCategories) == 0) return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Запись идёт сырыми указателями в нативные буферы без всякой синхронизации,
            // поэтому вызов из джоба или потока — это тихая порча кучи, которая всплывёт
            // через несколько кадров в совершенно другом месте. Ловим сразу.
            // MainThreadId == 0 означает «Install ещё не отработал», а не «чужой поток».
            // Без этой оговорки проверка срабатывала на ГЛАВНОМ потоке: любой Draw*, случившийся
            // до GizmoLoop.Install (порядок InitializeOnLoadMethod между сборками не определён,
            // а после Dispose флаг отчёта сбрасывается), сравнивался с нулём и всегда его не
            // проходил. В итоге рисование молча выключалось и печаталась ошибка про поток,
            // которого не было.
            if (MainThreadId != 0
                && System.Threading.Thread.CurrentThread.ManagedThreadId != MainThreadId
                && !ReportThread())
                return false;
#endif

            // Ensure может не подняться (нет шейдеров, не тот конвейер). Раньше здесь
            // возвращался true, и вызывающий шёл в ещё не созданные каналы — NRE на
            // первом же Draw*. Теперь такой кадр просто молча пропускается.
            if (!_ready)
            {
                Ensure();
                if (!_ready) return false;
            }

            HasProducedData = true;
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static bool _threadReported;

        static bool ReportThread()
        {
            if (_threadReported) return false;
            _threadReported = true;

            // Номер потока в сообщении нужен для диагностики: без него непонятно,
            // кто именно вызвал, и остаётся только гадать. Стек-трейс Unity допишет сам.
            var current = System.Threading.Thread.CurrentThread;

            Debug.LogError($"[RuntimeGizmos] Draw* вызван не из главного потока " +
                           $"(поток {current.ManagedThreadId} '{current.Name ?? "без имени"}', " +
                           $"главный — {MainThreadId}). " +
                           "Буферы не потокобезопасны, вызовы из джобов и потоков игнорируются. " +
                           "Соберите данные в джобе и рисуйте после Complete().");
            return false;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Line(Vector3 a, Vector3 b)
        {
            if (!Begin()) return;
            if (Width > 1f) WideLine(a, b);
            else ThinLine(a, b);
        }

        /// <summary>
        /// Отрезок без входной проверки. Для составных фигур, которые один раз прошли Begin()
        /// и дальше пишут сегмент за сегментом: проверка потока, категории и подъёма рендерера
        /// на каждом из 32 сегментов окружности — та же самая работа 32 раза.
        /// Звать только после Begin() == true в том же вызове.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LineAfterBegin(in Vector3 a, in Vector3 b)
        {
            if (Width > 1f) WideLine(a, b);
            else ThinLine(a, b);
        }

        /// <param name="mode">0 — пиксели от мирового якоря, 1 — мировые единицы, 2 — пиксели экрана.</param>
        internal static unsafe void Text(ReadOnlySpan<char> text, Vector3 anchor, float size, Vector2 offset,
                                         float align, int mode = 0)
        {
            if (text.IsEmpty || size <= 0f) return;
            if (!Begin()) return;

            GizmoFont.Ensure();

            var ch = _text[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var col = Color;

            float scale = size / GizmoFont.CapHeight;

            // В мировом режиме толщина привязана к высоте буквы, иначе на дистанции
            // текст схлопнулся бы в кляксу: lineWidth = 1 даёт штрих в 1/12 высоты.
            float stroke = mode == 1
                ? size * Mathf.Max(0.25f, Width) / 12f
                : Mathf.Max(1f, Width);

            GizmoFont.Measure(text, out int lines, out _);

            float lineStep = GizmoFont.LineStep * scale;
            float top = offset.y + (lines - 1) * lineStep * 0.5f;   // блок центрируется по вертикали

            var segs = GizmoFont.Segments;
            float expiry = Now + Duration;
            int start = 0;

            for (int li = 0; li < lines; li++)
            {
                int nl = text.Slice(start).IndexOf('\n');
                int stop = nl < 0 ? text.Length : start + nl;
                int len = stop - start;
                if (len > 0 && text[stop - 1] == '\r') len--;

                float penX = offset.x - GizmoFont.LineWidth(len) * scale * align;
                float baseY = top - li * lineStep - GizmoFont.Baseline * scale;

                for (int i = 0; i < len; i++)
                {
                    if (GizmoFont.Glyph(text[start + i], out int s0, out int n))
                    {
                        for (int k = 0; k < n; k++)
                        {
                            Vector4 g = segs[s0 + k];
                            var p0 = new Vector2(penX + g.x * scale, baseY + g.y * scale);
                            var p1 = new Vector2(penX + g.z * scale, baseY + g.w * scale);

                            var v = buf.Reserve(6);

                            // Концы не переставляем: обе вершины квада должны считать одну
                            // локальную систему. Признак конца — в модуле поля стороны.
                            SetText(v + 0, anchor, col, p0, p1, -1f, stroke, mode);
                            SetText(v + 1, anchor, col, p0, p1, +1f, stroke, mode);
                            SetText(v + 2, anchor, col, p0, p1, +2f, stroke, mode);
                            SetText(v + 3, anchor, col, p0, p1, -1f, stroke, mode);
                            SetText(v + 4, anchor, col, p0, p1, +2f, stroke, mode);
                            SetText(v + 5, anchor, col, p0, p1, -2f, stroke, mode);

                            if (ret)
                            {
                                var e = ch.RetainedExpiry.Reserve(6);
                                for (int q = 0; q < 6; q++) e[q] = expiry;
                            }
                        }
                    }

                    penX += GizmoFont.Advance * scale;
                }

                if (nl < 0) break;
                start = stop + 1;
            }

            if (mode != 2) buf.Encapsulate(anchor);
        }

        /// <summary>Полоса: фон во всю ширину и заливка поверх. Рисуется каналом текста,
        /// поэтому разворачивается к камере тем же способом.</summary>
        internal static unsafe void Bar(Vector3 anchor, float t, float width, float height,
                                        in Color fill, in Color back, Vector2 offset, int mode)
        {
            if (!Begin() || width <= 0f || height <= 0f) return;

            var ch = _text[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            float expiry = Now + Duration;

            float half = width * 0.5f;
            float y = offset.y;
            t = Mathf.Clamp01(t);

            Seg(offset.x - half, offset.x + half, back);
            if (t > 0f) Seg(offset.x - half, offset.x - half + width * t, fill);

            if (mode != 2) buf.Encapsulate(anchor);

            void Seg(float x0, float x1, in Color c)
            {
                // Торцы у капсулы круглые, поэтому укорачиваем на полтолщины —
                // иначе полоса окажется шире заказанной.
                float r = height * 0.5f;
                var p0 = new Vector2(x0 + r, y);
                var p1 = new Vector2(Mathf.Max(x1 - r, x0 + r), y);
                Color32 c32 = c;

                var v = buf.Reserve(6);
                SetText(v + 0, anchor, c32, p0, p1, -1f, height, mode);
                SetText(v + 1, anchor, c32, p0, p1, +1f, height, mode);
                SetText(v + 2, anchor, c32, p0, p1, +2f, height, mode);
                SetText(v + 3, anchor, c32, p0, p1, -1f, height, mode);
                SetText(v + 4, anchor, c32, p0, p1, +2f, height, mode);
                SetText(v + 5, anchor, c32, p0, p1, -2f, height, mode);

                if (!ret) return;
                var e = ch.RetainedExpiry.Reserve(6);
                for (int q = 0; q < 6; q++) e[q] = expiry;
            }
        }

        /// <summary>
        /// Угловая надпись. Всё, что раньше считалось здесь, переехало в GizmoHud:
        /// угол стал частным случаем якоря, а сама надпись — неявной таблицей в одну колонку.
        ///
        /// Двух независимых механизмов раскладки экранного текста в пакете быть не должно —
        /// иначе таблица и угловая надпись на одном углу ничего не знают друг о друге
        /// и наезжают. Заодно ушла привязка к Screen.width/height: край экрана теперь
        /// вычисляет шейдер по размеру таргета текущей камеры.
        /// </summary>
        internal static void CornerText(string text, GizmoCorner corner, float size) =>
            GizmoHud.AnchoredLine(text, GizmoAnchorUtil.FromCorner(corner), size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void SetText(GizmoTextVertex* v, in Vector3 anchor, in Color32 c,
                                   in Vector2 offset, in Vector2 other, float side, float width, int mode)
        {
            v->Position = anchor;
            v->Color = c;
            v->Offset = offset;
            v->Other = other;
            v->Params = new Vector3(side, width, mode);
        }

        static void ThinLine(in Vector3 a, in Vector3 b)
        {
            var ch = _thin[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(2);
            var c = Color;
            DashPhase(a, b, out float da, out float db);
            p[0].Position = a; p[0].Color = c; p[0].Dash = da;
            p[1].Position = b; p[1].Color = c; p[1].Dash = db;
            buf.Encapsulate(a); buf.Encapsulate(b);
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(2);
                float t = Now + Duration;
                e[0] = t; e[1] = t;
            }
        }

        static void WideLine(in Vector3 a, in Vector3 b)
        {
            var ch = _wide[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(6);
            var c = Color;
            float w = Width;

            // Два треугольника. Сторона у конца B инвертируется, потому что направление
            // отрезка в шейдере считается как normalize(Other - Position).
            DashPhase(a, b, out float da, out float db);
            p[0].Position = a; p[0].Other = b; p[0].Color = c; p[0].Params = new Vector3(-1f, w, da);
            p[1].Position = a; p[1].Other = b; p[1].Color = c; p[1].Params = new Vector3(+1f, w, da);
            p[2].Position = b; p[2].Other = a; p[2].Color = c; p[2].Params = new Vector3(+1f, w, db);
            p[3].Position = b; p[3].Other = a; p[3].Color = c; p[3].Params = new Vector3(+1f, w, db);
            p[4].Position = a; p[4].Other = b; p[4].Color = c; p[4].Params = new Vector3(+1f, w, da);
            p[5].Position = b; p[5].Other = a; p[5].Color = c; p[5].Params = new Vector3(-1f, w, db);

            buf.Encapsulate(a); buf.Encapsulate(b);
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(6);
                float t = Now + Duration;
                for (int i = 0; i < 6; i++) e[i] = t;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if (!Begin()) return;
            var ch = _tri[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(3);
            var col = Color;
            p[0].Position = a; p[0].Color = col; p[0].Dash = -1f;
            p[1].Position = b; p[1].Color = col; p[1].Dash = -1f;
            p[2].Position = c; p[2].Color = col; p[2].Dash = -1f;
            buf.Encapsulate(a); buf.Encapsulate(b); buf.Encapsulate(c);
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(3);
                float t = Now + Duration;
                e[0] = t; e[1] = t; e[2] = t;
            }
        }

        /// <summary>Пакетная заливка списка отрезков (пары точек) из кэшированного примитива.</summary>
        internal static void LineArray(Vector3* src, int n, Matrix4x4 m)
        {
            if (!Begin() || n <= 0) return;

            if (Width > 1f)
            {
                for (int i = 0; i < n; i += 2)
                    WideLine(m.MultiplyPoint3x4(src[i]), m.MultiplyPoint3x4(src[i + 1]));
                return;
            }

            var ch = _thin[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(n);
            var c = Color;
            float inv = Dash > 0f ? 1f / Dash : 0f;
            var prev = Vector3.zero;

            for (int i = 0; i < n; i++)
            {
                var w = m.MultiplyPoint3x4(src[i]);
                p[i].Position = w;
                p[i].Color = c;

                if (inv == 0f) p[i].Dash = -1f;
                else
                {
                    // Точки идут парами: у начала каждой пары фаза продолжается,
                    // у конца — прирастает длиной отрезка.
                    if ((i & 1) == 1) DashRun += (w - prev).magnitude;
                    p[i].Dash = DashRun * inv;
                }

                prev = w;
                buf.Encapsulate(w);
            }
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(n);
                float t = Now + Duration;
                for (int i = 0; i < n; i++) e[i] = t;
            }
        }

        /// <summary>Пакетная заливка супа треугольников из кэшированного примитива.</summary>
        internal static void TriangleArray(Vector3* src, int n, Matrix4x4 m)
        {
            if (!Begin() || n <= 0) return;

            var ch = _tri[Z];
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(n);
            var c = Color;
            for (int i = 0; i < n; i++)
            {
                var w = m.MultiplyPoint3x4(src[i]);
                p[i].Position = w;
                p[i].Color = c;
                p[i].Dash = -1f;      // буфер выделен неинициализированным
                buf.Encapsulate(w);
            }
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(n);
                float t = Now + Duration;
                for (int i = 0; i < n; i++) e[i] = t;
            }
        }

        internal static void MeshCmd(Mesh mesh, int submesh, Matrix4x4 m, Color color)
        {
            if (!Begin() || mesh == null) return;

            // Graphics.RenderMesh бросает на индексе вне диапазона, а пустой меш
            // (subMeshCount == 0) не нарисовать вообще ничем.
            int count = mesh.subMeshCount;
            if (count <= 0) return;
            if ((uint)submesh >= (uint)count) submesh = Mathf.Clamp(submesh, 0, count - 1);

            bool ret = Duration > 0f;
            var list = ret ? _meshRetained : _meshBack;
            ref var c = ref list.Add();
            c.Mesh = mesh;
            c.Submesh = submesh;
            c.Matrix = m;
            c.Color = color;
            c.Z = Z;
            c.Expiry = ret ? Now + Duration : 0f;
        }

        // ==================================================================== текстурные батчи

        internal static void Quad(Texture tex, Vector3 center, Vector2 size, bool worldSize, Color color)
        {
            if (!Begin() || tex == null) return;

            var id = GizmoObjectId.Of(tex);
            // Призрачного набора у иконок нет: текстурный билборд, нарисованный дважды,
            // читается хуже, чем один. Z = 2 для них равен обычному тесту глубины.
            var icons = Z == 1 ? _iconsTop : _icons;
            if (!icons.TryGetValue(id, out var batch))
            {
                batch = new GizmoTexturedBatch(tex, (Z == 1 ? "iconTop" : "icon") + id);
                icons[id] = batch;
            }
            batch.Texture = tex;

            var sz = worldSize ? new Vector2(-Mathf.Abs(size.x), -Mathf.Abs(size.y)) : size;
            WriteQuad(batch, center, sz, color);
        }

        internal static void ScreenQuad(Texture tex, Rect rect, Color color)
        {
            if (!Begin() || tex == null) return;

            var id = GizmoObjectId.Of(tex);
            if (!_screen.TryGetValue(id, out var batch))
            {
                batch = new GizmoTexturedBatch(tex, "screen" + id);
                _screen[id] = batch;
            }
            batch.Texture = tex;

            var ch = batch.Channel;
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(6);
            Color32 c = color;

            // позиции сразу в пикселях экрана, шейдер переведёт их в клип-пространство
            Vector3 p00 = new Vector3(rect.xMin, rect.yMin, 0);
            Vector3 p10 = new Vector3(rect.xMax, rect.yMin, 0);
            Vector3 p11 = new Vector3(rect.xMax, rect.yMax, 0);
            Vector3 p01 = new Vector3(rect.xMin, rect.yMax, 0);

            SetScreenVert(p + 0, p00, c, 0, 1);
            SetScreenVert(p + 1, p10, c, 1, 1);
            SetScreenVert(p + 2, p11, c, 1, 0);
            SetScreenVert(p + 3, p00, c, 0, 1);
            SetScreenVert(p + 4, p11, c, 1, 0);
            SetScreenVert(p + 5, p01, c, 0, 0);

            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(6);
                float t = Now + Duration;
                for (int i = 0; i < 6; i++) e[i] = t;
            }
        }

        static void SetScreenVert(GizmoQuadVertex* v, Vector3 pos, Color32 c, float u, float vv)
        {
            v->Position = pos;
            v->Color = c;
            v->Corner = new Vector4(0, 0, u, vv);
            v->Size = Vector2.zero;
        }

        static void WriteQuad(GizmoTexturedBatch batch, Vector3 center, Vector2 size, Color color)
        {
            var ch = batch.Channel;
            bool ret = Duration > 0f;
            var buf = ch.Target(ret);
            var p = buf.Reserve(6);
            Color32 c = color;

            WriteCorner(p + 0, center, size, c, -1, -1, 0, 0);
            WriteCorner(p + 1, center, size, c, +1, -1, 1, 0);
            WriteCorner(p + 2, center, size, c, +1, +1, 1, 1);
            WriteCorner(p + 3, center, size, c, -1, -1, 0, 0);
            WriteCorner(p + 4, center, size, c, +1, +1, 1, 1);
            WriteCorner(p + 5, center, size, c, -1, +1, 0, 1);

            buf.Encapsulate(center);
            if (ret)
            {
                var e = ch.RetainedExpiry.Reserve(6);
                float t = Now + Duration;
                for (int i = 0; i < 6; i++) e[i] = t;
            }
        }

        static void WriteCorner(GizmoQuadVertex* v, Vector3 center, Vector2 size, Color32 c,
            float cx, float cy, float u, float vv)
        {
            v->Position = center;
            v->Color = c;
            v->Corner = new Vector4(cx, cy, u, vv);
            v->Size = size;
        }
    }

    /// <summary>Кэш каркасных (line-topology) версий произвольных мешей для DrawWireMesh.</summary>
    internal static class GizmoWireMeshCache
    {
        struct Entry { public Mesh Source; public Mesh Wire; }

        static readonly Dictionary<GizmoMeshKey, Entry> _cache = new Dictionary<GizmoMeshKey, Entry>();
        static readonly List<Vector3> _verts = new List<Vector3>();
        static readonly List<int> _tris = new List<int>();
        static readonly List<int> _lines = new List<int>();

        public static Mesh Get(Mesh src, int submesh)
        {
            if (src == null) return null;

            // На пустом меше Mathf.Clamp(submesh, 0, -1) вернул бы -1, и следующий же
            // GetTopology(-1) бросил бы исключение.
            int subCount = src.subMeshCount;
            if (subCount <= 0) return null;

            // Кламп ДО вычисления ключа: иначе submesh 5 и 99 на односабмешевом меше
            // дали бы два разных ключа и две одинаковые копии каркаса.
            submesh = Mathf.Clamp(submesh, 0, subCount - 1);

            var key = new GizmoMeshKey(src, submesh);
            if (_cache.TryGetValue(key, out var e))
            {
                // Instance ID может быть переиспользован после выгрузки ассета — сверяем источник,
                // иначе однажды нарисовали бы каркас совсем другого меша.
                if (e.Source == src) return e.Wire;
                Destroy(e.Wire);
                _cache.Remove(key);
            }

            if (!src.isReadable)
            {
                Debug.LogWarning($"[RuntimeGizmos] Меш '{src.name}' не Read/Write Enabled — " +
                                 "каркас построить нельзя, рисую заливкой.");
                _cache[key] = new Entry { Source = src, Wire = null };
                return null;
            }

            src.GetVertices(_verts);
            if (src.GetTopology(submesh) != MeshTopology.Triangles)
            {
                _cache[key] = new Entry { Source = src, Wire = null };
                return null;
            }

            src.GetTriangles(_tris, submesh);
            // Общее ребро двух треугольников кладём один раз. Раньше оно попадало в каркас
            // дважды: индексов вдвое больше нужного, а при альфе меньше единицы внутренние
            // рёбра накладывались сами на себя и выходили ярче граничных.
            _lines.Clear();
            _edges.Clear();
            for (int i = 0; i + 2 < _tris.Count; i += 3)
            {
                int a = _tris[i], b = _tris[i + 1], c = _tris[i + 2];
                AddEdge(a, b);
                AddEdge(b, c);
                AddEdge(c, a);
            }

            // Clear ёмкость не отдаёт: после каркаса меша на миллион треугольников набор
            // держал бы десятки мегабайт до конца сессии.
            bool huge = _edges.Count > 1 << 16;
            _edges.Clear();
            if (huge) _edges.TrimExcess();

            var m = new Mesh
            {
                name = "~GizmoWire_" + src.name,
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = _verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            m.SetVertices(_verts);
            m.SetIndices(_lines, MeshTopology.Lines, 0, true);
            _cache[key] = new Entry { Source = src, Wire = m };
            if (_cache.Count > PurgeAt) Purge();
            return m;
        }

        static readonly HashSet<ulong> _edges = new HashSet<ulong>();

        static void AddEdge(int a, int b)
        {
            if (a == b) return;                                   // вырожденное ребро
            uint lo = (uint)(a < b ? a : b), hi = (uint)(a < b ? b : a);
            if (!_edges.Add(((ulong)hi << 32) | lo)) return;
            _lines.Add(a); _lines.Add(b);
        }

        // Источник могли выгрузить — тогда каркас держать незачем. Чистим редко и только
        // по достижении порога, чтобы не платить за обход каждый кадр.
        const int PurgeAt = 64;
        static readonly List<GizmoMeshKey> _dead = new List<GizmoMeshKey>();

        static void Purge()
        {
            _dead.Clear();
            foreach (var kv in _cache)
                if (kv.Value.Source == null) _dead.Add(kv.Key);

            for (int i = 0; i < _dead.Count; i++)
            {
                Destroy(_cache[_dead[i]].Wire);
                _cache.Remove(_dead[i]);
            }
        }

        static void Destroy(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(m);
            else UnityEngine.Object.DestroyImmediate(m);
        }

        public static void Dispose()
        {
            foreach (var e in _cache.Values) Destroy(e.Wire);
            _cache.Clear();
            _dead.Clear();
        }
    }
}
