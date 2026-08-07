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
        internal static int Z;            // 0 = с тестом глубины, 1 = поверх всего
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

        static readonly Dictionary<GizmoObjectId, GizmoTexturedBatch> _icons = new Dictionary<GizmoObjectId, GizmoTexturedBatch>();
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
        static bool _linearColor;

        // Последняя альфа, уже разложенная по материалам. GlobalAlpha меняется редко,
        // а SetFloat дёргался на каждый канал, на каждую камеру, каждый кадр.
        // NaN на старте: любое сравнение с ним ложно, поэтому первый Submit применит
        // значение в любом случае, даже если оно совпало с тем, что выставил MakeMat.
        static float _appliedAlpha = float.NaN;

        // ==================================================================== init / teardown

        static bool _failed;

        static GizmoChannel<GizmoTextVertex>[] _text;
        static Material[] _textMat;

        /// <summary>Поток, на котором система была установлена. Всё остальное — ошибка.</summary>
        internal static int MainThreadId;

        internal static void Ensure()
        {
            if (_ready || _failed) return;

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
                Enabled = false;
                return;
            }

            _ready = true;
            _appliedAlpha = float.NaN;      // материалы пересозданы — кэш недействителен
            Width = GizmoSettings.DefaultLineWidth;

            _thinMat = new Material[2];
            _wideMat = new Material[2];
            _triMat = new Material[2];
            _meshMat = new Material[2];
            _iconMat = new Material[2];
            _textMat = new Material[2];
            _screenMat = new Material[1];

            for (int z = 0; z < 2; z++)
            {
                bool depth = z == 0;
                int zTest = depth ? (int)CompareFunction.LessEqual : (int)CompareFunction.Always;
                int queue = (int)RenderQueue.Transparent + (depth ? 100 : 400);

                float bias = depth ? GizmoSettings.DepthBias : 0f;

                _thinMat[z] = MakeMat(unlit, "GizmoThin", zTest, 0, queue, bias);
                _wideMat[z] = MakeMat(wide, "GizmoWide", zTest, 0, queue, bias);
                // Сплошные фигуры с тестом глубины пишут в depth — так они корректно
                // перекрывают друг друга. Режим "поверх всего" depth не пишет.
                _triMat[z] = MakeMat(unlit, "GizmoSolid", zTest, depth ? 1 : 0, queue - 10, 0f);
                _meshMat[z] = MakeMat(unlit, "GizmoMesh", zTest, depth ? 1 : 0, queue - 10, 0f);
                _iconMat[z] = MakeMat(icon, "GizmoIcon", zTest, 0, queue + 10, bias);
                _textMat[z] = MakeMat(text, "GizmoText", zTest, 0, queue + 20, bias);
            }

            _screenMat[0] = MakeMat(screen, "GizmoScreen", (int)CompareFunction.Always, 0,
                (int)RenderQueue.Overlay, 0f);

            _thin = new[]
            {
                new GizmoChannel<GizmoVertex>("thin0", GizmoVertexLayouts.Thin, MeshTopology.Lines, 2, 2048),
                new GizmoChannel<GizmoVertex>("thin1", GizmoVertexLayouts.Thin, MeshTopology.Lines, 2, 512),
            };
            _wide = new[]
            {
                new GizmoChannel<GizmoWideVertex>("wide0", GizmoVertexLayouts.Wide, MeshTopology.Triangles, 6, 512, 0.25f),
                new GizmoChannel<GizmoWideVertex>("wide1", GizmoVertexLayouts.Wide, MeshTopology.Triangles, 6, 256, 0.25f),
            };
            _tri = new[]
            {
                new GizmoChannel<GizmoVertex>("tri0", GizmoVertexLayouts.Thin, MeshTopology.Triangles, 3, 1024),
                new GizmoChannel<GizmoVertex>("tri1", GizmoVertexLayouts.Thin, MeshTopology.Triangles, 3, 256),
            };
            _text = new[]
            {
                new GizmoChannel<GizmoTextVertex>("text0", GizmoVertexLayouts.Text, MeshTopology.Triangles, 6, 512, 0.25f),
                new GizmoChannel<GizmoTextVertex>("text1", GizmoVertexLayouts.Text, MeshTopology.Triangles, 6, 256, 0.25f),
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

        internal static void Dispose()
        {
            _failed = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _threadReported = false;
#endif
            if (!_ready) return;
            _ready = false;

            DisposeChannels(_thin); DisposeChannels(_wide); DisposeChannels(_tri);
            DisposeChannels(_text);
            foreach (var b in _icons.Values) b.Dispose();
            foreach (var b in _screen.Values) b.Dispose();
            _icons.Clear();
            _screen.Clear();

            DestroyMats(_thinMat); DestroyMats(_wideMat); DestroyMats(_triMat);
            DestroyMats(_meshMat); DestroyMats(_iconMat); DestroyMats(_screenMat);
            DestroyMats(_textMat);

            GizmoIndexPool.Dispose();
            GizmoPrimitives.Dispose();
            GizmoFont.Dispose();
            GizmoWireMeshCache.Dispose();
        }

        static void DisposeChannels<T>(GizmoChannel<T>[] arr) where T : unmanaged
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) arr[i]?.Dispose();
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
            for (int i = 0; i < 2; i++) { _thin[i].Clear(); _wide[i].Clear(); _tri[i].Clear(); _text[i].Clear(); }
            foreach (var b in _icons.Values) b.Channel.Clear();
            foreach (var b in _screen.Values) b.Channel.Clear();
            _meshFront.Clear(); _meshBack.Clear(); _meshRetained.Clear();
        }

        // ==================================================================== кадр

        internal static bool HasProducedData;

        internal static void BeginFrame(bool strict)
        {
            if (!_ready) return;

            // Истечение считаем по ТОМУ ЖЕ времени, которым штамповались команды этого кадра.
            //
            // Draw* идёт из Update/LateUpdate, то есть до BeginFrame, и берёт Now, выставленный
            // на прошлой границе кадра. Если бы мы сначала обновили Now, а потом проверяли
            // Expiry > Now, то любая длительность короче кадра истекала бы мгновенно: duration = 0
            // жил бы кадр, а duration = 0.001 — ноль кадров. Теперь геометрия с duration > 0
            // гарантированно переживает кадр, в котором была нарисована.
            float t = Now;
            float stale = GizmoSettings.EditorStaleTimeout;

            for (int i = 0; i < 2; i++)
            {
                _thin[i].BeginFrame(strict, t, stale);
                _wide[i].BeginFrame(strict, t, stale);
                _tri[i].BeginFrame(strict, t, stale);
                _text[i].BeginFrame(strict, t, stale);
            }

            foreach (var b in _icons.Values) b.Channel.BeginFrame(strict, t, stale);
            foreach (var b in _screen.Values) b.Channel.BeginFrame(strict, t, stale);

            // Текстуру могли уничтожить — батч с ней уже ничего не нарисует, а нативные
            // буферы и меши держит. Словари крошечные, обход раз в кадр бесплатный.
            PruneDeadBatches(_icons);
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
                _meshLastData = t;
            }
            else if (strict || t - _meshLastData > stale)
            {
                _meshFront.Clear();
            }

            ResetCorners();
            DashRun = 0f;
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
            ApplyAlpha(alpha);

            for (int z = 0; z < 2; z++)
            {
                if (_tri[z].Prepare(out var m, out var b)) Emit(ref rp, _triMat[z], m, b);
                if (_thin[z].Prepare(out m, out b)) Emit(ref rp, _thinMat[z], m, b);
                if (_wide[z].Prepare(out m, out b)) Emit(ref rp, _wideMat[z], m, b);
            }

            var textBounds = new Bounds(cam.transform.position, new Vector3(1e5f, 1e5f, 1e5f));
            for (int z = 0; z < 2; z++)
                if (_text[z].Prepare(out var tm, out _))
                    Emit(ref rp, _textMat[z], tm, textBounds);

            foreach (var kv in _icons)
                if (kv.Value.Channel.Prepare(out var m, out var b))
                {
                    if (kv.Value.Texture == null) continue;
                    rp.material = _iconMat[0];
                    rp.matProps = kv.Value.Props;
                    rp.worldBounds = b;
                    Graphics.RenderMesh(rp, m, 0, Matrix4x4.identity);
                    rp.matProps = null;
                }

            if (_screen.Count > 0)
            {
                // Экранная геометрия задаётся прямо в клип-пространстве, поэтому
                // ограничивающий бокс привязываем к камере, иначе её отсечёт куллинг.
                var camBounds = new Bounds(cam.transform.position, new Vector3(1e4f, 1e4f, 1e4f));
                foreach (var kv in _screen)
                    if (kv.Value.Channel.Prepare(out var m, out _))
                    {
                        if (kv.Value.Texture == null) continue;
                        rp.material = _screenMat[0];
                        rp.matProps = kv.Value.Props;
                        rp.worldBounds = camBounds;
                        Graphics.RenderMesh(rp, m, 0, Matrix4x4.identity);
                        rp.matProps = null;
                    }
            }

            _mpbCursor = 0;
            SubmitMeshList(ref rp, _meshRetained);
            SubmitMeshList(ref rp, _meshFront);
        }

        /// <summary>
        /// Раскладывает глобальную альфу по материалам — но только когда она
        /// действительно поменялась. Набор материалов здесь ровно тот, что получал
        /// альфу раньше: иконки и экранные квады её не берут ни до, ни после.
        /// </summary>
        static void ApplyAlpha(float alpha)
        {
            if (alpha == _appliedAlpha) return;
            _appliedAlpha = alpha;

            for (int z = 0; z < 2; z++)
            {
                _triMat[z].SetFloat(AlphaId, alpha);
                _thinMat[z].SetFloat(AlphaId, alpha);
                _wideMat[z].SetFloat(AlphaId, alpha);
                _textMat[z].SetFloat(AlphaId, alpha);
                _meshMat[z].SetFloat(AlphaId, alpha);
            }
        }

        static void Emit(ref RenderParams rp, Material mat, Mesh mesh, Bounds b)
        {
            rp.material = mat;
            rp.worldBounds = b;
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
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
                rp.matProps = null;
            }
        }

        // ==================================================================== запись примитивов

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Begin()
        {
            if (!Enabled) return false;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <param name="worldSpace">
        /// false — размер в пикселях, метка одинакова на любом расстоянии.
        /// true — размер в мировых единицах, метка уменьшается с расстоянием.
        /// </param>
        /// <param name="mode">0 — пиксели от мирового якоря, 1 — мировые единицы, 2 — пиксели экрана.</param>
        internal static unsafe void Text(string text, Vector3 anchor, float size, Vector2 offset,
                                         float align, int mode = 0)
        {
            if (string.IsNullOrEmpty(text) || size <= 0f) return;
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
                int nl = text.IndexOf('\n', start);
                int stop = nl < 0 ? text.Length : nl;
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
                start = nl + 1;
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

        static readonly int[] _cornerLines = new int[4];

        internal static void ResetCorners() { for (int i = 0; i < 4; i++) _cornerLines[i] = 0; }

        internal static void CornerText(string text, GizmoCorner corner, float size)
        {
            if (string.IsNullOrEmpty(text)) return;

            int idx = (int)corner;
            GizmoFont.Measure(text, out int lines, out _);

            float scale = size / GizmoFont.CapHeight;
            float step = GizmoFont.LineStep * scale;
            float used = _cornerLines[idx] * step;
            _cornerLines[idx] += lines;

            bool right = corner == GizmoCorner.TopRight || corner == GizmoCorner.BottomRight;
            bool bottom = corner == GizmoCorner.BottomLeft || corner == GizmoCorner.BottomRight;

            // Безопасная зона считается до КРАЯ ЧЕРНИЛ, а не до якоря строки.
            //
            // Якорь — вертикальный центр блока, и раньше отступ отмерялся от него: у верхних
            // надписей за экран уезжала вся высота заглавной (это ровно size пикселей),
            // у нижних — выносные элементы (Baseline * scale). Визуально текст лип к краю
            // и обрезался, хотя формально «отступ» был.
            float pad = Mathf.Max(0f, GizmoSettings.ScreenSafeArea);
            float halfBlock = (lines - 1) * step * 0.5f;
            float capTop = GizmoFont.CapHeight * scale;      // от якоря вверх до верха заглавной
            float descender = GizmoFont.Baseline * scale;    // от якоря вниз до низа выносного
            float halfStroke = Mathf.Max(1f, Width) * 0.5f;  // штрих рисуется капсулой, шире отрезка

            float x = right ? Screen.width - pad - halfStroke : pad + halfStroke;
            float y = bottom
                ? Screen.height - pad - used - halfBlock - descender
                : pad + used + halfBlock + capTop;

            Text(text, new Vector3(x, y, 0f), size, Vector2.zero,
                 right ? 1f : 0f, 2);
        }

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
            if (!_icons.TryGetValue(id, out var batch))
            {
                batch = new GizmoTexturedBatch(tex, "icon" + id);
                _icons[id] = batch;
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
            _lines.Clear();
            for (int i = 0; i < _tris.Count; i += 3)
            {
                int a = _tris[i], b = _tris[i + 1], c = _tris[i + 2];
                _lines.Add(a); _lines.Add(b);
                _lines.Add(b); _lines.Add(c);
                _lines.Add(c); _lines.Add(a);
            }

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
