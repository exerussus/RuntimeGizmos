using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Точки подключения к жизненному циклу.
    ///
    /// Граница кадра (BeginFrame — обмен буферов) ставится ПОСЛЕ пользовательского кода
    /// и ДО рендера: PostLateUpdate в плеймоде, EditorApplication.update в эдит-моде.
    /// Благодаря этому вызовы из Update/LateUpdate попадают в тот же кадр без задержки.
    ///
    /// Отправка на отрисовку — на каждую камеру перед куллингом, через
    /// RenderPipelineManager.beginCameraRendering. URP вызывает его до context.Cull(),
    /// поэтому сабмиты Graphics.RenderMesh успевают попасть в этот кадр.
    /// Пакет рассчитан только на SRP (URP); Built-in не поддерживается.
    /// </summary>
    internal static class GizmoLoop
    {
        struct GizmoBeginFrame { }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void EditorInit()
        {
            Install();
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
            AssemblyReloadEvents.beforeAssemblyReload -= Teardown;
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange s)
        {
            // С выключенным Domain Reload статика переживает вход и выход из Play Mode.
            // Поэтому на обеих границах сносим всё нажитое: и накопленную геометрию,
            // и материалы с нативными буферами, и рантайм-оверрайды настроек.
            if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.ExitingPlayMode)
            {
                GizmoRenderer.Dispose();
                GizmoSettings.ResetSession();
                Registry.Clear();
            }
        }

        static void EditorTick()
        {
            if (Application.isPlaying) return; // в плеймоде границу кадра держит PlayerLoop

            Registry.Tick();
            bool hadData = GizmoRenderer.HasProducedData;
            GizmoRenderer.BeginFrame(strict: false);

            if (hadData && GizmoSettings.EditorAutoRepaint)
                SceneView.RepaintAll();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuntimeInit()
        {
            // SubsystemRegistration отрабатывает на каждом входе в Play Mode, в том числе
            // когда Domain Reload выключен — это единственная точка, где можно гарантированно
            // начать сессию с чистого листа. Отрабатывает до Awake, так что оверрайды,
            // выставленные пользовательским кодом, уже не затрутся.
            GizmoRenderer.Dispose();
            GizmoSettings.ResetSession();
            Registry.Clear();

            Install();
            Application.quitting -= Teardown;
            Application.quitting += Teardown;
        }

        static void Install()
        {
            // Всё внутри идемпотентно: при входе в Play Mode Unity сбрасывает PlayerLoop,
            // а с выключенным Domain Reload статики переживают переход — поэтому раннего
            // выхода по флагу здесь быть не должно.
            // Install всегда идёт с главного потока — отсюда и берём эталон для проверки.
            GizmoRenderer.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

            InsertPlayerLoop();
            WarnIfNoScriptableRenderPipeline();
        }

        static void Teardown()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RemovePlayerLoop();
            GizmoRenderer.Dispose();
        }

        static bool _warned;

        static void WarnIfNoScriptableRenderPipeline()
        {
            if (_warned) return;
            if (GraphicsSettings.currentRenderPipeline != null) return;

            _warned = true;
            Debug.LogWarning("[RuntimeGizmos] Активен Built-in Render Pipeline — рисовать будет нечем. " +
                             "Пакету нужен URP: назначьте Universal Render Pipeline Asset " +
                             "в Project Settings → Graphics.");
        }

        static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam) => GizmoRenderer.Submit(cam);

        static void PlayerLoopBeginFrame()
        {
            // GizmoLazy рисует ДО обмена буферов, иначе опоздал бы на кадр.
            Registry.Tick();
            GizmoRenderer.BeginFrame(strict: true);
        }

        static void InsertPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertInto(ref loop, typeof(PostLateUpdate))) return;
            PlayerLoop.SetPlayerLoop(loop);
        }

        static bool InsertInto(ref PlayerLoopSystem root, Type parent)
        {
            var subs = root.subSystemList;
            if (subs == null) return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == parent)
                {
                    var children = subs[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                    for (int k = 0; k < children.Length; k++)
                        if (children[k].type == typeof(GizmoBeginFrame)) return true; // уже стоит

                    int at = FindInsertIndex(children);

                    var next = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, next, 0, at);
                    next[at] = new PlayerLoopSystem
                    {
                        type = typeof(GizmoBeginFrame),
                        updateDelegate = PlayerLoopBeginFrame
                    };
                    Array.Copy(children, at, next, at + 1, children.Length - at);

                    subs[i].subSystemList = next;
                    root.subSystemList = subs;
                    return true;
                }

                if (InsertInto(ref subs[i], parent))
                {
                    root.subSystemList = subs;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Куда встать внутри PostLateUpdate: прямо перед первым узлом рендера.
        ///
        /// Раньше вставка шла жёстко в позицию 0, и это было хрупко вдвойне. За нулевой индекс
        /// дерутся все, кто трогает PlayerLoop — например UniTask ставит туда свои раннеры, —
        /// и победитель зависит от порядка инициализации, то есть от везения. Плюс из нулевой
        /// позиции мы теряли бы вызовы, сделанные из PostLateUpdate-продолжений чужих
        /// планировщиков: они бы опоздали на кадр.
        ///
        /// Настоящее требование к границе кадра — не «быть первой», а «быть после
        /// пользовательского кода и до рендера». Именно оно здесь и обеспечивается.
        /// Если узел рендера не найден (сменилась структура PlayerLoop) — встаём в начало,
        /// как раньше: это заведомо до рендера.
        /// </summary>
        static int FindInsertIndex(PlayerLoopSystem[] children)
        {
            for (int i = 0; i < children.Length; i++)
            {
                var t = children[i].type;
                if (t != null && t.Name.Contains("FinishFrameRendering")) return i;
            }

            return 0;
        }

        static void RemovePlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (RemoveFrom(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        static bool RemoveFrom(ref PlayerLoopSystem root)
        {
            var subs = root.subSystemList;
            if (subs == null) return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == typeof(GizmoBeginFrame))
                {
                    var next = new PlayerLoopSystem[subs.Length - 1];
                    Array.Copy(subs, 0, next, 0, i);
                    Array.Copy(subs, i + 1, next, i, subs.Length - i - 1);
                    root.subSystemList = next;
                    return true;
                }

                if (RemoveFrom(ref subs[i]))
                {
                    root.subSystemList = subs;
                    return true;
                }
            }

            return false;
        }
    }
}
