using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using RuntimeGizmos;
using RuntimeGizmos.Extensions;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Геометрия против настоящих объектов Unity: коллайдеры с их правилами
    /// масштабирования, каркас произвольного меша, вырожденный и «грязный» ввод.
    /// </summary>
    public class GeometryTests
    {
        readonly List<Object> _trash = new List<Object>();

        [SetUp] public void SetUp() => GizmoTestHarness.Boot();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _trash) GizmoTestHarness.Destroy(o);
            _trash.Clear();
            GizmoTestHarness.Shutdown();
        }

        GameObject Make(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(go);
            return go;
        }

        static int Thin => GizmoTestHarness.Thin[0].Back.Count;

        // ------------------------------------------------------------------ примитивы

        [Test]
        public void Каркасный_куб_это_двенадцать_рёбер()
        {
            Gizmo.lineWidth = 1f;
            Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
            Assert.AreEqual(24, Thin, "12 рёбер по 2 вершины");
        }

        [Test]
        public void Окружность_состоит_из_заявленного_числа_сегментов()
        {
            Gizmo.lineWidth = 1f;
            Gizmo.DrawWireDisc(Vector3.zero, Vector3.up, 1f);
            Assert.AreEqual(GizmoPrimitives.CircleSegments * 2, Thin,
                            "окружность обязана состоять ровно из CircleSegments отрезков");
        }

        [Test]
        public void Смена_детализации_пересобирает_примитивы()
        {
            int before = GizmoPrimitives.CircleSegments;

            // Примитивы считаются один раз, поэтому детализацию надо менять между
            // Dispose и Ensure — ровно так, как об этом сказано в README.
            GizmoRenderer.Dispose();
            GizmoSettings.CircleSegments = 17;
            GizmoRenderer.Ensure();

            Assert.AreEqual(17, GizmoPrimitives.CircleSegments);
            Assert.AreNotEqual(before, GizmoPrimitives.CircleSegments);

            GizmoSettings.ResetSession();
        }

        [Test]
        public void Вырожденная_геометрия_не_роняет()
        {
            Gizmo.lineWidth = 1f;
            Assert.DoesNotThrow(() =>
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.zero);
                Gizmo.DrawWireSphere(Vector3.zero, 0f);
                Gizmo.DrawWireSphere(Vector3.zero, -5f);
                Gizmo.DrawWireCube(Vector3.zero, Vector3.zero);
                Gizmo.DrawWireDisc(Vector3.zero, Vector3.zero, 1f);
                Gizmo.DrawWireCapsule(Vector3.zero, Vector3.zero, 0f);
                Gizmo.DrawArrow(Vector3.zero, Vector3.zero);
            });
        }

        [Test]
        public void NaN_и_бесконечность_не_роняют()
        {
            Gizmo.lineWidth = 1f;
            var nan = new Vector3(float.NaN, float.NaN, float.NaN);
            var inf = new Vector3(float.PositiveInfinity, 0f, 0f);

            Assert.DoesNotThrow(() =>
            {
                Gizmo.DrawLine(nan, Vector3.one);
                Gizmo.DrawLine(inf, Vector3.one);
                Gizmo.DrawWireSphere(nan, float.NaN);
                Gizmo.DrawText("x", nan, 12f);
                GizmoTestHarness.At(10f);
                GizmoTestHarness.Frame();
                GizmoTestHarness.Thin[0].Prepare(out _, out _);
            });
        }

        [Test]
        public void Переполнение_канала_отбрасывает_лишнее_но_не_падает()
        {
            GizmoSettings.MaxVerticesPerChannel = 512;
            Gizmo.lineWidth = 1f;

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 5000; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
                GizmoTestHarness.At(20f);
                GizmoTestHarness.Frame();
                GizmoTestHarness.Thin[0].Prepare(out _, out _);
            });

            Assert.LessOrEqual(GizmoTestHarness.Thin[0].Back.Count, 512, "потолок обязан держать");
            GizmoSettings.ResetSession();
        }

        // ------------------------------------------------------------------ каркас меша

        [Test]
        public void Каркас_меша_строится_и_кэшируется()
        {
            var src = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(src);
            src.SetVertices(new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up });
            src.SetTriangles(new List<int> { 0, 1, 2 }, 0);

            var wire = GizmoWireMeshCache.Get(src, 0);
            Assert.IsNotNull(wire, "каркас обязан построиться на читаемом меше");
            Assert.AreEqual(MeshTopology.Lines, wire.GetTopology(0));
            Assert.AreEqual(6, wire.GetIndexCount(0), "один треугольник — три ребра по два индекса");

            Assert.AreSame(wire, GizmoWireMeshCache.Get(src, 0), "второй запрос обязан прийти из кэша");
        }

        [Test]
        public void Каркас_меша_терпит_сабмеш_вне_диапазона()
        {
            var src = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(src);
            src.SetVertices(new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up });
            src.SetTriangles(new List<int> { 0, 1, 2 }, 0);

            Assert.DoesNotThrow(() => GizmoWireMeshCache.Get(src, 99));
            Assert.AreSame(GizmoWireMeshCache.Get(src, 5), GizmoWireMeshCache.Get(src, 99),
                           "индексы вне диапазона обязаны схлопнуться в один ключ");
        }

        [Test]
        public void Пустой_меш_не_роняет_каркас()
        {
            var empty = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(empty);
            Assert.DoesNotThrow(() => GizmoWireMeshCache.Get(empty, 0));
        }

        // ------------------------------------------------------------------ коллайдеры

        [Test]
        public void Сфера_берёт_наибольшую_ось_масштаба()
        {
            var go = Make("sphere");
            go.transform.localScale = new Vector3(1f, 3f, 2f);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 1f;

            Gizmo.lineWidth = 1f;
            col.DrawShape(Color.green);
            GizmoTestHarness.At(30f);
            GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out var b));

            // Движок масштабирует сферический коллайдер наибольшей осью — значит
            // габарит обязан дотянуться до радиуса 3, а не 1 и не 2.
            Assert.AreEqual(3f, b.extents.x, 0.3f, "радиус посчитан не по наибольшей оси: " + b);
            Assert.AreEqual(3f, b.extents.y, 0.3f, "радиус посчитан не по наибольшей оси: " + b);
        }

        [Test]
        public void Ящик_рисуется_настоящей_формой()
        {
            var go = Make("box");
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(2f, 4f, 6f);

            Gizmo.lineWidth = 1f;
            col.DrawShape(Color.green);
            GizmoTestHarness.At(40f);
            GizmoTestHarness.Frame();
            Assert.IsTrue(GizmoTestHarness.Thin[0].Prepare(out _, out var b));

            Assert.AreEqual(1f, b.extents.x, 0.01f);
            Assert.AreEqual(2f, b.extents.y, 0.01f);
            Assert.AreEqual(3f, b.extents.z, 0.01f);
        }

        [Test]
        public void Капсула_не_бывает_ниже_двух_радиусов()
        {
            var go = Make("capsule");
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 1f;
            col.height = 0.5f;                    // меньше 2*radius — движок поднимет до 2

            Gizmo.lineWidth = 1f;
            Assert.DoesNotThrow(() => col.DrawShape(Color.green));
            Assert.Greater(Thin, 0, "капсула обязана что-то нарисовать");
        }

        [Test]
        public void Неизвестный_коллайдер_рисуется_габаритом()
        {
            var go = Make("mesh-collider");
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(m);
            m.SetVertices(new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up });
            m.SetTriangles(new List<int> { 0, 1, 2 }, 0);

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = m;

            Gizmo.lineWidth = 1f;
            Assert.DoesNotThrow(() => col.DrawShape(Color.green));
        }

        [Test]
        public void Расширения_терпят_null()
        {
            Gizmo.lineWidth = 1f;
            Collider nullCollider = null;
            Transform nullTransform = null;
            Rigidbody nullBody = null;

            Assert.DoesNotThrow(() =>
            {
                nullCollider.DrawShape(Color.green);
                nullTransform.DrawAxes(1f);
                nullTransform.DrawVolume(Color.red);
                nullBody.DrawVelocity(Color.cyan);
            });
        }

        [Test]
        public void Габариты_иерархии_охватывают_детей()
        {
            var root = Make("root");
            var child = Make("child");
            child.transform.SetParent(root.transform, true);
            child.transform.position = new Vector3(10f, 0f, 0f);
            child.AddComponent<MeshRenderer>();
            child.AddComponent<MeshFilter>().sharedMesh = BuildQuad();

            var b = Gizmo.WorldBounds(root.transform);
            Assert.GreaterOrEqual(b.max.x, 9f, "габариты обязаны дотянуться до ребёнка: " + b);
        }

        Mesh BuildQuad()
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _trash.Add(m);
            m.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            m.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
