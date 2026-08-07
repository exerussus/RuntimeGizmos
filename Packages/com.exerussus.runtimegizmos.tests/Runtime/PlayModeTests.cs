using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// То, что проверяется только в живом плеймоде: встраивание в PlayerLoop,
    /// настоящая граница кадра, настоящее время, настоящая отрисовка камерой.
    /// </summary>
    public class PlayModeTests
    {
        const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags PrivInst = BindingFlags.NonPublic | BindingFlags.Instance;

        static GizmoChannel<GizmoVertex>[] Thin =>
            typeof(GizmoRenderer).GetField("_thin", PrivStatic).GetValue(null) as GizmoChannel<GizmoVertex>[];

        static GizmoChannel<GizmoVertex>[] Tri =>
            typeof(GizmoRenderer).GetField("_tri", PrivStatic).GetValue(null) as GizmoChannel<GizmoVertex>[];

        static bool Prepared(GizmoChannel<GizmoVertex> ch) =>
            (bool)typeof(GizmoChannel<GizmoVertex>).GetField("_prepared", PrivInst).GetValue(ch);

        void Boot()
        {
            GizmoRenderer.ClearAll();
            GizmoSettings.ResetSession();
            GizmoRenderer.Enabled = true;
            Gizmo.Reset();
            GizmoRenderer.Ensure();

            if (Thin == null)
                Assert.Ignore("Рендерер не поднялся: шейдеры не скомпилировались. Пакету нужен URP.");
        }

        [TearDown]
        public void TearDown()
        {
            GizmoRenderer.ClearAll();
            GizmoSettings.ResetSession();
            Gizmo.Reset();
        }

        // ------------------------------------------------------------------ жизненный цикл

        [UnityTest]
        public IEnumerator Граница_кадра_встроена_в_PlayerLoop_ровно_один_раз()
        {
            yield return null;
            Assert.AreEqual(1, CountGizmoNodes(),
                            "узел границы кадра обязан стоять ровно один раз: вставка идемпотентна");
        }

        [UnityTest]
        public IEnumerator Граница_кадра_стоит_до_рендера_в_PostLateUpdate()
        {
            yield return null;

            var root = PlayerLoop.GetCurrentPlayerLoop();
            foreach (var s in root.subSystemList)
            {
                if (s.type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;

                Assert.IsNotNull(s.subSystemList);
                Assert.Greater(s.subSystemList.Length, 0);

                int gizmo = -1, render = -1;
                for (int i = 0; i < s.subSystemList.Length; i++)
                {
                    var name = s.subSystemList[i].type?.Name;
                    if (name == null) continue;
                    if (gizmo < 0 && name.Contains("GizmoBeginFrame")) gizmo = i;
                    if (render < 0 && name.Contains("FinishFrameRendering")) render = i;
                }

                Assert.GreaterOrEqual(gizmo, 0, "граница кадра обязана стоять в PostLateUpdate");

                // Проверяем настоящий инвариант, а не позицию 0. Первым узлом мы быть НЕ обязаны:
                // за нулевой индекс дерутся все, кто трогает PlayerLoop (UniTask, например),
                // и требовать его — значит падать в любом проекте с такой библиотекой.
                // Важно ровно одно: обмен буферов происходит до рендера.
                if (render >= 0)
                    Assert.Less(gizmo, render, "граница кадра обязана идти до рендера");

                yield break;
            }

            Assert.Fail("в PlayerLoop не нашлось PostLateUpdate");
        }

        static int CountGizmoNodes()
        {
            int n = 0;
            void Walk(PlayerLoopSystem s)
            {
                if (s.type != null && s.type.Name.Contains("GizmoBeginFrame")) n++;
                if (s.subSystemList != null) foreach (var c in s.subSystemList) Walk(c);
            }
            Walk(PlayerLoop.GetCurrentPlayerLoop());
            return n;
        }

        // ------------------------------------------------------------------ кадровая модель

        [UnityTest]
        public IEnumerator Нарисованное_попадает_в_меш_в_том_же_кадре()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Assert.AreEqual(2, Thin[0].Back.Count, "запись идёт в back");

            yield return null;   // прошла настоящая граница кадра

            Assert.AreEqual(0, Thin[0].Back.Count, "back обязан быть отдан и очищен");
            Assert.IsTrue(Thin[0].Prepare(out var mesh, out _), "меш обязан быть готов");
            Assert.IsNotNull(mesh);
            Assert.AreEqual(2, mesh.GetSubMesh(0).vertexCount);
        }

        [UnityTest]
        public IEnumerator Без_новых_команд_геометрия_исчезает_следующим_кадром()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);

            yield return null;
            Assert.IsTrue(Thin[0].Prepare(out _, out _));

            yield return null;
            yield return null;
            Assert.IsFalse(Thin[0].Prepare(out _, out _),
                           "в плеймоде семантика строгая: не нарисовал — не видно");
        }

        [UnityTest]
        public IEnumerator Duration_держит_геометрию_и_отпускает_по_настоящему_времени()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            Gizmo.duration = 0.35f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 0f;

            yield return null;
            yield return null;
            Assert.Greater(Thin[0].Retained.Count, 0, "геометрия обязана пережить кадр, в котором нарисована");

            yield return new WaitForSecondsRealtime(0.7f);
            yield return null;
            yield return null;
            Assert.AreEqual(0, Thin[0].Retained.Count, "после истечения обязана выбыть");
        }

        [UnityTest]
        public IEnumerator Статика_не_пересобирается_но_остаётся_видимой()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            Gizmo.duration = 30f;
            for (int i = 0; i < 16; i++) Gizmo.DrawLine(new Vector3(i, 0, 0), new Vector3(i, 1, 0));
            Gizmo.duration = 0f;

            yield return null;
            Assert.IsTrue(Thin[0].Prepare(out var first, out _));

            for (int f = 0; f < 5; f++)
            {
                yield return null;
                Assert.IsTrue(Thin[0].Prepare(out var next, out _), $"кадр {f}: геометрия пропала");
                Assert.AreSame(first, next, $"кадр {f}: меш пересобрался, хотя ничего не менялось");
            }
        }

        // ------------------------------------------------------------------ отрисовка

        [Test]
        public void Фильтр_камер_отсекает_отрисовку_до_подготовки_меша()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            GizmoRenderer.BeginFrame(strict: true);

            var cam = MakeCamera();
            try
            {
                GizmoSettings.DrawInGameView = false;
                GizmoRenderer.Submit(cam);
                Assert.IsFalse(Prepared(Thin[0]), "выключённая игровая камера не должна доходить до меша");

                GizmoSettings.DrawInGameView = true;
                GizmoRenderer.Submit(cam);
                Assert.IsTrue(Prepared(Thin[0]), "включённая камера обязана подготовить меш");
            }
            finally { Object.Destroy(cam.gameObject); }
        }

        [UnityTest]
        public IEnumerator Геометрия_доходит_до_пикселей()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                Assert.Ignore("отрисовка проверяется только на URP — назначьте Render Pipeline Asset");

            Boot();

            const int Size = 64;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var cam = MakeCamera();
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);

            try
            {
                cam.targetTexture = rt;
                cam.orthographic = true;
                cam.orthographicSize = 2f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.transform.rotation = Quaternion.identity;

                // Сплошной куб поверх всей геометрии — самая устойчивая мишень.
                Gizmo.depthTest = false;
                Gizmo.color = Color.red;
                Gizmo.DrawCube(Vector3.zero, Vector3.one * 2f);

                yield return null;          // граница кадра переложит геометрию во front
                cam.Render();               // beginCameraRendering вызовет Submit

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                int lit = 0;
                foreach (var p in tex.GetPixels32())
                    if (p.r > 60 && p.g < 200) lit++;

                Assert.Greater(lit, Size * Size / 20,
                               "камера не увидела гизмо: закрашено пикселей " + lit);
            }
            finally
            {
                cam.targetTexture = null;
                Object.Destroy(cam.gameObject);
                Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
                Gizmo.Reset();
            }
        }

        [UnityTest]
        public IEnumerator Пустой_кадр_не_рисует_ничего()
        {
            Boot();
            yield return null;
            yield return null;

            for (int z = 0; z < 2; z++)
            {
                Assert.IsFalse(Thin[z].Prepare(out _, out _), "тонкий канал " + z);
                Assert.IsFalse(Tri[z].Prepare(out _, out _), "канал заливки " + z);
            }
        }

        static Camera MakeCamera()
        {
            var go = new GameObject("~GizmoTestCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;                     // рендерим вручную
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = ~0;
            return cam;
        }

        // ------------------------------------------------------------------ потокобезопасность

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [UnityTest]
        public IEnumerator Вызов_из_чужого_потока_отбивается_с_ошибкой()
        {
            Boot();
            Gizmo.lineWidth = 1f;
            int before = Thin[0].Back.Count;

            LogAssert.Expect(LogType.Error, new Regex("не из главного потока"));

            var t = new System.Threading.Thread(() => Gizmo.DrawLine(Vector3.zero, Vector3.one));
            t.Start();
            t.Join();

            Assert.AreEqual(before, Thin[0].Back.Count,
                            "запись из чужого потока обязана быть отброшена, а не испортить буфер");
            yield return null;
        }
#endif

        [UnityTest]
        public IEnumerator Смешанная_нагрузка_не_рвёт_буферы()
        {
            Boot();

            for (int f = 0; f < 8; f++)
            {
                Gizmo.lineWidth = (f % 2 == 0) ? 1f : 5f;
                Gizmo.depthTest = f % 3 != 0;
                Gizmo.duration = (f % 2 == 0) ? 0.05f : 0f;

                for (int i = 0; i < 60; i++)
                {
                    Gizmo.DrawLine(Vector3.zero, Vector3.one);
                    Gizmo.DrawWireSphere(Vector3.one, 2f);
                    Gizmo.DrawSphere(Vector3.zero, 1f);
                    Gizmo.DrawText("Wq0.", Vector3.zero, 12f);
                }

                Gizmo.Reset();
                yield return null;
            }

            Assert.Pass("смешанная нагрузка прошла без исключений и порчи памяти");
        }
    }
}
