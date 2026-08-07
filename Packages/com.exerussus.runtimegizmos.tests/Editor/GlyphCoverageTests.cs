using System.Text;
using NUnit.Framework;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Сторож покрытия шрифта.
    ///
    /// Непокрытый символ рисуется пустым квадратом — молча, без ошибки и без лога.
    /// Поймать это можно только глазами и только на том экране, где такой текст встретился,
    /// поэтому список поддержанного обязан проверяться тестом, а не доверием к README.
    ///
    /// Строка ниже — то же самое, что раздел «Поддерживаемые символы» в README.
    /// Расходиться они не имеют права: тест упадёт.
    /// </summary>
    public class GlyphCoverageTests
    {
        const string Ascii =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~";

        const string Cyrillic =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
            "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";

        const string Typography = "«»—–…№×°•→←↔±";

        [SetUp] public void SetUp() => GizmoFont.Ensure();

        [Test]
        public void Вся_ASCII_покрыта()
        {
            AssertCovered(Ascii);
        }

        [Test]
        public void Вся_кириллица_покрыта()
        {
            AssertCovered(Cyrillic);
        }

        [Test]
        public void Типографика_покрыта()
        {
            // Ровно те символы, из-за которых в подписях появлялись квадраты:
            // длинное тире в «Tab — сменить роль» и кавычки-ёлочки.
            AssertCovered(Typography);
        }

        [Test]
        public void Непокрытый_символ_честно_сообщает_о_себе()
        {
            Assert.IsFalse(Gizmo.IsRenderable('漢'), "иероглиф глифа не имеет");
            Assert.AreEqual('漢', Gizmo.FirstUnrenderable("текст 漢 текст"));
            Assert.AreEqual('\0', Gizmo.FirstUnrenderable("обычный текст — с тире"));
        }

        [Test]
        public void Переносы_строк_не_считаются_непокрытыми()
        {
            // Иначе любой многострочный текст выглядел бы как ошибка покрытия.
            Assert.AreEqual('\0', Gizmo.FirstUnrenderable("первая\nвторая\r\nтретья"));
        }

        [Test]
        public void Длинное_тире_шире_дефиса()
        {
            // Если глифы совпадут, различить их в тексте будет нельзя, и смысл
            // отдельного символа пропадёт.
            Assert.AreNotEqual(Segments('-'), Segments('—'));
        }

        static void AssertCovered(string set)
        {
            var missing = new StringBuilder();

            foreach (var c in set)
                if (!Gizmo.IsRenderable(c))
                    missing.Append(c).Append(' ');

            Assert.AreEqual(0, missing.Length,
                $"нет глифов для: {missing}. Либо добавьте их в GizmoFont, " +
                "либо уберите из README и из этого теста — расхождение недопустимо.");
        }

        static string Segments(char c)
        {
            Assert.IsTrue(GizmoFont.Glyph(c, out int start, out int count), $"нет глифа для '{c}'");

            var sb = new StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(GizmoFont.Segments[start + i]).Append(';');
            return sb.ToString();
        }
    }
}
