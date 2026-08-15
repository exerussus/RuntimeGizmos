using NUnit.Framework;
using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Internal;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Обещание пакета: на горячем пути managed-аллокаций нет вообще.
    /// Офлайн-бенчмарк считает их суммарно, здесь проверяется покадрово и адресно.
    ///
    /// Прогрев обязателен: первый вызов дотягивает нативные буферы до рабочего
    /// размера и строит шрифт, и это законная разовая аллокация.
    /// </summary>
    public class AllocationTests
    {
        [SetUp]
        public void SetUp()
        {
            GizmoTestHarness.Boot();
            Gizmo.lineWidth = 1f;
            Warmup();
        }

        [TearDown] public void TearDown() => GizmoTestHarness.Shutdown();

        static void Warmup()
        {
            for (int i = 0; i < 3; i++)
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawWireSphere(Vector3.zero, 1f);
                Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmo.DrawSphere(Vector3.zero, 1f);
                Gizmo.DrawText("Wq0.", Vector3.zero, 12f);

                Gizmo.lineWidth = 4f;
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.lineWidth = 1f;

                // Scope тоже нужно прогреть: он не встречается ни в одной строке выше,
                // и первое же его выполнение внутри Assert засчитывалось как аллокация JIT.
                using (Gizmo.Scope(Color.white, Matrix4x4.identity))
                    Gizmo.DrawLine(Vector3.zero, Vector3.one);

                GizmoTestHarness.At(i);
                GizmoTestHarness.Frame();
            }
        }

        [Test]
        public void Тонкие_линии_не_аллоцируют()
        {
            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Толстые_линии_не_аллоцируют()
        {
            Gizmo.lineWidth = 4f;
            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Кэшированные_примитивы_не_аллоцируют()
        {
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Gizmo.DrawWireSphere(Vector3.zero, 1f);
                    Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
                    Gizmo.DrawSphere(Vector3.zero, 1f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Отложенная_геометрия_не_аллоцирует()
        {
            Assert.That(() =>
            {
                Gizmo.duration = 100f;
                for (int i = 0; i < 200; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.duration = 0f;
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Граница_кадра_не_аллоцирует()
        {
            Gizmo.duration = 1000f;
            for (int i = 0; i < 200; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 0f;
            GizmoTestHarness.At(10f);
            GizmoTestHarness.Frame();

            Assert.That(() =>
            {
                for (int f = 0; f < 20; f++)
                {
                    GizmoTestHarness.At(11f + f * 0.016f);
                    GizmoTestHarness.Frame();
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Scope_не_аллоцирует()
        {
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                    using (Gizmo.Scope(Color.red, Matrix4x4.identity))
                        Gizmo.DrawLine(Vector3.zero, Vector3.one);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Регистрация_GizmoLazy_не_аллоцирует()
        {
            var go = new GameObject("~alloc") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // Прогрев обязан идти по ТОЙ ЖЕ строке, что и измерение: ключ регистрации
                // собирается из [CallerLineNumber], поэтому вызов с соседней строки создавал
                // отдельную запись и прогревал только ветку добавления. Ветка обновления
                // существующей записи оставалась непрогретой, и её JIT попадал в замер.
                TrackFiftyTimes(go);

                Assert.That(() => TrackFiftyTimes(go), Is.Not.AllocatingGCMemory());
            }
            finally
            {
                GizmoLazy.Clear();
                GizmoTestHarness.Destroy(go);
            }
        }

        // Вынесено в метод, чтобы прогрев и замер шли через одно и то же место вызова.
        static void TrackFiftyTimes(GameObject go)
        {
            for (int i = 0; i < 50; i++) GizmoLazy.Track(go).Volume(Color.red);
        }

        /// <summary>
        /// Ради этого экранная раскладка и держит текст срезами общей арены, а числа
        /// форматирует через TryFormat: ToString() на каждое значение каждый кадр —
        /// самый заметный источник мусора в отладочном выводе, и в профайлере он
        /// маскирует ровно то, ради чего HUD включали.
        ///
        /// Замер покрывает и раскладку: Flush меряет текст, считает колонки и выпускает
        /// вершины — если там заведётся строка или замыкание, тест это увидит.
        /// </summary>
        [Test]
        public void Экранная_таблица_не_аллоцирует()
        {
            FillTable();   // прогрев: первое обращение выделяет арену и буферы раскладки

            Assert.That(FillTable, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Якорные_надписи_не_аллоцируют()
        {
            FillCorners();

            Assert.That(FillCorners, Is.Not.AllocatingGCMemory());
        }

        static void FillTable()
        {
            var t = Gizmo.Table(GizmoAnchor.TopRight);
            t.Columns(GizmoTextAlign.Left, GizmoTextAlign.Right);
            t.Title("player");

            for (int i = 0; i < 50; i++)
            {
                t.Row("hp", i * 1.5f, "F1");
                t.Row("ammo", i);
                t.Row("grounded", i % 2 == 0);
            }

            t.Separator();
            t.Row("velocity", Vector3.one, "F2");

            GizmoHud.Flush();
        }

        static void FillCorners()
        {
            for (int i = 0; i < 50; i++)
            {
                Gizmo.DrawScreenText("состояние", GizmoCorner.TopLeft);
                Gizmo.DrawScreenText("по центру", GizmoAnchor.Center);
            }

            GizmoHud.Flush();
        }
    }
}
