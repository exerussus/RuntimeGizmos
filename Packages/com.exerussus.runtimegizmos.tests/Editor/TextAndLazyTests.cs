using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>Текстовые метки: покрытие шрифта, многострочность, экранный текст.</summary>
    public class TextTests
    {
        [SetUp] public void SetUp() => GizmoTestHarness.Boot();
        [TearDown] public void TearDown() => GizmoTestHarness.Shutdown();

        // Проверять «есть ли глиф» через возврат Glyph бесполезно: непокрытый символ
        // подставляет пустой квадрат и тоже возвращает true. Поэтому сравниваем
        // отрезки символа с отрезками самого квадрата — совпали, значит промах.
        [Test]
        public void Шрифт_покрывает_латиницу_и_кириллицу()
        {
            GizmoFont.Ensure();

            Assert.IsTrue(GizmoFont.Glyph('一', out int boxStart, out int boxCount),
                          "непокрытый символ обязан подставлять видимую заглушку");
            Assert.Greater(boxCount, 0);

            string missed = "";
            foreach (char c in Covered())
            {
                if (c == '□') continue;                     // сам квадрат — законно
                if (!GizmoFont.Glyph(c, out int s, out int n) || (s == boxStart && n == boxCount))
                    missed += c;
            }

            Assert.IsEmpty(missed, "символы без собственного глифа: " + missed);
        }

        static System.Collections.Generic.IEnumerable<char> Covered()
        {
            for (int c = 33; c <= 126; c++) yield return (char)c;      // пробел без отрезков — норма
            for (int c = 0x410; c <= 0x44F; c++) yield return (char)c;
            yield return 'Ё';
            yield return 'ё';
        }

        [Test]
        public void Пробел_не_рисуется_но_двигает_перо()
        {
            GizmoFont.Ensure();
            Assert.IsFalse(GizmoFont.Glyph(' ', out _, out int n), "у пробела нет отрезков");
            Assert.AreEqual(0, n);
            Assert.Greater(GizmoFont.LineWidth(3), GizmoFont.LineWidth(1), "перо обязано двигаться");
        }

        [Test]
        public void Перенос_строки_действительно_переносит()
        {
            GizmoFont.Measure("одна", out int one, out _);
            GizmoFont.Measure("одна\nдве", out int two, out _);
            GizmoFont.Measure("одна\r\nдве\r\nтри", out int three, out _);

            Assert.AreEqual(1, one);
            Assert.AreEqual(2, two, "\\n обязан переносить");
            Assert.AreEqual(3, three, "\\r\\n тоже");
        }

        [Test]
        public void Текст_пишет_вершины()
        {
            int before = GizmoTestHarness.TotalVertices();
            Gizmo.DrawText("привет", Vector3.zero, 14f);
            Assert.AreNotEqual(before, TextVerts(), "текст не попал в свой канал");
        }

        [Test]
        public void Пустой_текст_ничего_не_пишет()
        {
            int before = TextVerts();
            Gizmo.DrawText("", Vector3.zero, 14f);
            Gizmo.DrawText(null, Vector3.zero, 14f);
            Gizmo.DrawText("что-то", Vector3.zero, 0f);       // нулевой размер
            Gizmo.DrawText("что-то", Vector3.zero, -5f);      // отрицательный
            Assert.AreEqual(before, TextVerts());
        }

        [Test]
        public void Экранный_текст_укладывается_стопкой_в_углу()
        {
            Assert.DoesNotThrow(() =>
            {
                Gizmo.DrawScreenText("первая", GizmoCorner.TopLeft);
                Gizmo.DrawScreenText("вторая", GizmoCorner.TopLeft);
                Gizmo.DrawScreenText("справа", GizmoCorner.BottomRight);
                GizmoTestHarness.At(1f);
                GizmoTestHarness.Frame();
            });
            Assert.Greater(TextVerts(), 0);
        }

        static int TextVerts()
        {
            var arr = typeof(GizmoRenderer)
                .GetField("_text", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null) as System.Array;
            if (arr == null) return 0;

            int n = 0;
            foreach (var ch in arr)
            {
                var t = ch.GetType();
                foreach (var f in new[] { "_front", "_back", "_retained" })
                {
                    var buf = t.GetField(f, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ch);
                    n += (int)buf.GetType().GetProperty("Count").GetValue(buf);
                }
            }
            return n;
        }
    }

    /// <summary>Ленивая отладка: регистрации, ключи, снятие, потолок.</summary>
    public class LazyTests
    {
        GameObject _target;

        [SetUp]
        public void SetUp()
        {
            GizmoTestHarness.Boot();
            GizmoLazy.Clear();
            GizmoLazy.Enabled = true;
            _target = new GameObject("~lazy") { hideFlags = HideFlags.HideAndDontSave };
        }

        [TearDown]
        public void TearDown()
        {
            GizmoLazy.Clear();
            GizmoTestHarness.Destroy(_target);
            GizmoTestHarness.Shutdown();
        }

        [Test]
        public void Регистрация_учитывается()
        {
            Assert.AreEqual(0, GizmoLazy.Count);
            GizmoLazy.Track(_target).Volume(Color.red);
            Assert.AreEqual(1, GizmoLazy.Count);
        }

        [Test]
        public void Повтор_с_того_же_места_заменяет_а_не_копит()
        {
            for (int i = 0; i < 10; i++) GizmoLazy.Track(_target).Volume(Color.red);
            Assert.AreEqual(1, GizmoLazy.Count, "ключ собирается из цели, файла и строки");
        }

        [Test]
        public void Разные_команды_с_одной_строки_сосуществуют()
        {
            GizmoLazy.Track(_target).Volume(Color.red);
            GizmoLazy.Track(_target).Axes(1f);
            Assert.AreEqual(2, GizmoLazy.Count);
        }

        [Test]
        public void Ключ_различает_цели_в_цикле()
        {
            var extra = new GameObject("~lazy2") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var go in new[] { _target, extra })
                    GizmoLazy.Track(go).Key("агро").Volume(Color.red);

                Assert.AreEqual(2, GizmoLazy.Count, "ключ один, но цели разные");
            }
            finally { GizmoTestHarness.Destroy(extra); }
        }

        [Test]
        public void Untrack_снимает_по_цели()
        {
            GizmoLazy.Track(_target).Volume(Color.red);
            GizmoLazy.Track(_target).Axes(1f);
            GizmoLazy.Untrack(_target);
            Assert.AreEqual(0, GizmoLazy.Count);
        }

        [Test]
        public void Смерть_цели_снимает_регистрацию()
        {
            var doomed = new GameObject("~doomed") { hideFlags = HideFlags.HideAndDontSave };
            GizmoLazy.Track(doomed).Volume(Color.red);
            Assert.AreEqual(1, GizmoLazy.Count);

            GizmoTestHarness.Destroy(doomed);

            // Регистрации разбирает Registry.Tick, а не граница кадра: в бою его
            // зовёт GizmoLoop прямо перед BeginFrame.
            GizmoTestHarness.At(1f);
            Registry.Tick();

            Assert.AreEqual(0, GizmoLazy.Count, "мёртвая цель обязана вычищаться сама");
        }

        [Test]
        public void Выключенный_слой_не_регистрирует()
        {
            GizmoLazy.Enabled = false;
            try
            {
                GizmoLazy.Track(_target).Volume(Color.red);
                Assert.AreEqual(0, GizmoLazy.Count);
            }
            finally { GizmoLazy.Enabled = true; }
        }

        [Test]
        public void Исключение_в_Draw_снимает_только_виновника()
        {
            GizmoLazy.Track(_target).Volume(Color.red);
            GizmoLazy.Track(_target).Key("бомба").Draw(() => throw new System.InvalidOperationException("тест"));
            Assert.AreEqual(2, GizmoLazy.Count);

            LogAssert.ignoreFailingMessages = true;
            try
            {
                GizmoTestHarness.At(2f);
                Registry.Tick();
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            Assert.AreEqual(1, GizmoLazy.Count, "выбывает только упавшая запись");
        }
    }
}
