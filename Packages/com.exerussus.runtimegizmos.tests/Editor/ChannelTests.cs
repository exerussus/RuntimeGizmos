using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Кадровая модель и заливка в НАСТОЯЩИЙ Mesh. Офлайн-харнесс проверяет здесь
    /// арифметику против заглушки; тут проверяется, что Unity принимает результат.
    /// </summary>
    public class ChannelTests
    {
        [SetUp] public void SetUp() => GizmoTestHarness.Boot();
        [TearDown] public void TearDown() => GizmoTestHarness.Shutdown();

        [Test]
        public void Вызов_из_Update_попадает_в_тот_же_кадр()
        {
            GizmoTestHarness.At(100f);
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);

            Assert.AreEqual(2, GizmoTestHarness.Thin[0].Back.Count, "запись обязана идти в back");

            GizmoTestHarness.At(100f);
            GizmoTestHarness.Frame();
            Assert.AreEqual(0, GizmoTestHarness.Thin[0].Back.Count, "после границы кадра back пуст");
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var mesh, out _), "меш обязан быть готов");
            Assert.IsNotNull(mesh);
        }

        [Test]
        public void Меш_согласован_с_тем_что_в_нём_заявлено()
        {
            GizmoTestHarness.At(200f);
            Gizmo.lineWidth = 1f;
            for (int i = 0; i < 37; i++) Gizmo.DrawLine(new Vector3(i, 0, 0), new Vector3(i, 1, 0));

            GizmoTestHarness.At(200f);
            GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var mesh, out _));

            Assert.AreEqual(1, mesh.subMeshCount);
            var sub = mesh.GetSubMesh(0);
            Assert.AreEqual(MeshTopology.Lines, sub.topology, "тонкие линии обязаны идти топологией Lines");
            Assert.AreEqual(74, sub.indexCount, "37 отрезков — это 74 вершины");
            Assert.AreEqual(sub.indexCount, sub.vertexCount, "геометрия неиндексированная");
            Assert.GreaterOrEqual(mesh.vertexCount, sub.vertexCount, "ёмкость меньше заявленного диапазона");
            Assert.AreEqual(74, mesh.GetIndexCount(0));
        }

        [Test]
        public void Толстые_линии_идут_треугольниками()
        {
            GizmoTestHarness.At(300f);
            Gizmo.lineWidth = 4f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);

            GizmoTestHarness.At(300f);
            GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Wide[0].Prepare(out var mesh, out _));

            var sub = mesh.GetSubMesh(0);
            Assert.AreEqual(MeshTopology.Triangles, sub.topology);
            Assert.AreEqual(6, sub.indexCount, "один отрезок — это два треугольника");
        }

        [Test]
        public void Строгий_режим_убирает_геометрию_без_новых_команд()
        {
            GizmoTestHarness.At(400f);
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);

            GizmoTestHarness.At(400f); GizmoTestHarness.Frame(strict: true);
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out _));

            GizmoTestHarness.At(400.016f); GizmoTestHarness.Frame(strict: true);
            Assert.IsFalse(GizmoTestHarness.Thin[0].Prepare(out _, out _),
                           "в strict-режиме кадр без команд не рисует ничего");
        }

        [Test]
        public void Мягкий_режим_держит_последний_снимок_до_таймаута()
        {
            GizmoTestHarness.At(500f);
            Gizmo.lineWidth = 1f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            GizmoTestHarness.At(500f); GizmoTestHarness.Frame(strict: true);

            GizmoTestHarness.At(500.1f); GizmoTestHarness.Frame(strict: false);
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out _),
                          "внутри EditorStaleTimeout снимок держится");

            GizmoTestHarness.At(600f); GizmoTestHarness.Frame(strict: false);
            GizmoTestHarness.At(601f); GizmoTestHarness.Frame(strict: false);
            Assert.IsFalse(GizmoTestHarness.Thin[0].Prepare(out _, out _),
                           "после таймаута снимок обязан пропасть");
        }

        [Test]
        public void Duration_переживает_кадр_и_истекает()
        {
            Gizmo.lineWidth = 1f;

            GizmoTestHarness.At(700f);
            Gizmo.duration = 5f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 0f;

            GizmoTestHarness.At(700f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out _), "живёт в кадре, где нарисована");

            GizmoTestHarness.At(703f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out _), "срок ещё не вышел");

            GizmoTestHarness.At(710f); GizmoTestHarness.Frame();
            Assert.IsFalse(GizmoTestHarness.Thin[0].Prepare(out _, out _), "срок вышел — геометрии нет");
        }

        [Test]
        public void Компактация_выбрасывает_только_целые_примитивы()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(800f);
            Gizmo.duration = 1f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 50f; Gizmo.DrawLine(Vector3.one, Vector3.zero);
            Gizmo.duration = 0f;

            GizmoTestHarness.At(800f); GizmoTestHarness.Frame();
            GizmoTestHarness.At(805f); GizmoTestHarness.Frame();

            Assert.AreEqual(2, GizmoTestHarness.Thin[0].Retained.Count,
                            "от истёкшего примитива не должно остаться половины");
        }

        // ------------------------------------------------------------------ пропуск лишней работы

        [Test]
        public void Неизменная_статика_переиспользует_тот_же_меш()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(900f);
            Gizmo.duration = 10000f;
            for (int i = 0; i < 20; i++) Gizmo.DrawLine(new Vector3(i, 0, 0), new Vector3(i, 1, 0));
            Gizmo.duration = 0f;

            GizmoTestHarness.At(900f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var first, out _));

            for (int f = 1; f <= 10; f++)
            {
                GizmoTestHarness.At(900f + f * 0.016f);
                GizmoTestHarness.Frame();
                Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var next, out _));
                Assert.AreSame(first, next, $"кадр {f}: меш обязан быть тем же самым");
            }

            Assert.AreEqual(40, first.GetSubMesh(0).vertexCount, "данные в меше не должны были поменяться");
        }

        [Test]
        public void Изменение_геометрии_переключает_меш_кольца()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(1000f);
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            GizmoTestHarness.At(1000f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var a, out _));

            Gizmo.DrawLine(Vector3.one, Vector3.zero);
            GizmoTestHarness.At(1000.016f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var b, out _));

            Assert.AreNotSame(a, b, "после изменения содержимого меш берётся другой из кольца");
        }

        [Test]
        public void Кадровая_геометрия_поверх_статики_видна_целиком()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(1100f);
            Gizmo.duration = 10000f; Gizmo.DrawLine(Vector3.zero, Vector3.one); Gizmo.duration = 0f;
            GizmoTestHarness.At(1100f); GizmoTestHarness.Frame();
            GizmoTestHarness.Thin[0].Prepare(out _, out _);

            Gizmo.DrawLine(Vector3.one * 2f, Vector3.zero);
            GizmoTestHarness.At(1100.016f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var mesh, out _));

            Assert.AreEqual(4, mesh.GetSubMesh(0).vertexCount,
                            "в меше обязаны быть обе части: статика и кадровая");
        }

        [Test]
        public void Bounds_охватывают_нарисованное()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(1200f);
            Gizmo.DrawLine(new Vector3(-5f, -5f, -5f), new Vector3(5f, 5f, 5f));

            GizmoTestHarness.At(1200f); GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out var mesh, out var bounds));

            Assert.LessOrEqual(bounds.min.x, -5f + 1e-3f);
            Assert.GreaterOrEqual(bounds.max.x, 5f - 1e-3f);
            Assert.LessOrEqual(mesh.bounds.min.y, -5f + 1e-3f);
            Assert.GreaterOrEqual(mesh.bounds.max.y, 5f - 1e-3f);
        }

        [Test]
        public void Bounds_переживают_пропуск_компактора()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(1300f);
            Gizmo.duration = 10000f;
            Gizmo.DrawLine(new Vector3(-7f, 0f, 0f), new Vector3(7f, 0f, 0f));
            Gizmo.duration = 0f;

            GizmoTestHarness.At(1300f); GizmoTestHarness.Frame();
            GizmoTestHarness.Thin[0].Prepare(out _, out var b1);

            for (int f = 1; f <= 5; f++)
            {
                GizmoTestHarness.At(1300f + f * 0.016f);
                GizmoTestHarness.Frame();
            }
            GizmoTestHarness.Thin[0].Prepare(out _, out var b2);

            Assert.AreEqual(b1.min.x, b2.min.x, 1e-4f, "минимум bounds уехал при пропуске");
            Assert.AreEqual(b1.max.x, b2.max.x, 1e-4f, "максимум bounds уехал при пропуске");
        }

        [Test]
        public void Clear_стирает_всё_включая_отложенное()
        {
            Gizmo.lineWidth = 1f;
            GizmoTestHarness.At(1400f);
            Gizmo.duration = 10000f; Gizmo.DrawLine(Vector3.zero, Vector3.one); Gizmo.duration = 0f;
            GizmoTestHarness.At(1400f); GizmoTestHarness.Frame();

            Gizmo.Clear();
            GizmoTestHarness.At(1400.016f); GizmoTestHarness.Frame();

            Assert.AreEqual(0, GizmoTestHarness.Thin[0].Retained.Count);
            Assert.IsFalse(GizmoTestHarness.Thin[0].Prepare(out _, out _));
        }

        [Test]
        public void Выключенный_Gizmo_ничего_не_пишет()
        {
            Gizmo.enabled = false;
            try
            {
                GizmoTestHarness.At(1500f);
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawWireSphere(Vector3.zero, 1f);
                Gizmo.DrawText("нет", Vector3.zero, 12f);
                Assert.AreEqual(0, GizmoTestHarness.TotalVertices());
            }
            finally { Gizmo.enabled = true; }
        }
    }
}
