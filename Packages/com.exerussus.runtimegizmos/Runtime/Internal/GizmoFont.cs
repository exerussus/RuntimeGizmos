using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Штриховой шрифт: глиф — набор отрезков на сетке x 0..4, y 0..9. Базовая линия y=2,
    /// высота строчной y=6, прописной y=8, выносной элемент до y=0, диакритика до y=9.
    ///
    /// Покрытие: ASCII 32..126, кириллица А..я, Ё и ё. Непокрытый символ рисуется пустым
    /// прямоугольником: молча пропадать он не должен, иначе подпись врёт о своём содержимом.
    ///
    /// Свой, а не системный, потому что OS-шрифты недоступны на WebGL и консолях, а
    /// динамический атлас требует обработки Font.textureRebuilt.
    /// </summary>
    internal static class GizmoFont
    {
        const int AsciiFirst = 32;
        const int AsciiLast = 126;
        const int AsciiSlots = AsciiLast - AsciiFirst + 1;   // 95

        const int CyrFirst = 0x410;                           // А
        const int CyrLast = 0x44F;                            // я
        const int CyrBase = AsciiSlots;                       // 95
        const int CyrSlots = CyrLast - CyrFirst + 1;          // 64

        const int SlotYo = CyrBase + CyrSlots;                // Ё
        const int SlotYoLower = SlotYo + 1;                   // ё
        const int SlotTofu = SlotYoLower + 1;                 // заглушка
        const int SlotCount = SlotTofu + 1;

        /// <summary>
        /// Ширина ячейки в единицах сетки. Глиф занимает 4 единицы, остальное — просвет.
        /// Шрифт моноширинный намеренно: отладочный вывод почти всегда идёт колонками
        /// («hp 100» под «mp  50»), и пропорциональные метрики их разъезжают.
        /// </summary>
        public const float Advance = 5.5f;

        /// <summary>Высота прописной буквы в единицах сетки. Ею масштабируется размер в пикселях.</summary>
        public const float CapHeight = 6f;

        /// <summary>Базовая линия в единицах сетки от низа.</summary>
        public const float Baseline = 2f;

        /// <summary>Шаг между строками в единицах сетки.</summary>
        public const float LineStep = 10f;

        /// <summary>Отрезки всех глифов подряд: (x0, y0, x1, y1) в единицах сетки.</summary>
        public static NativeArray<Vector4> Segments;

        static readonly int[] _start = new int[SlotCount + 1];
        static bool _built;

        static readonly string[] _glyphs =
        {
            "",                                            // space
            "2823|2222",                                   // !
            "1816|3836",                                   // quote
            "1812|3832|0646|0444",                         // #
            "4717061535443303|2822",                       // $
            "0807|4342|0248|0718|1817|1707|3343|4342|4232|3233", // %
            "4215172837360403123243",                      // &
            "2826",                                        // '
            "38161432",                                    // (
            "18363412",                                    // )
            "2723|0644|4604",                              // *
            "2723|0545",                                   // +
            "232211",                                      // ,
            "0545",                                        // -
            "2222",                                        // .
            "0248",                                        // /
            "123243473818070312|1337",                     // 0
            "162822|1232",                                 // 1
            "07183847460242",                              // 2
            "0848254443321203",                            // 3
            "32380444",                                    // 4
            "480806364543321203",                          // 5
            "473818070312324344351504",                    // 6
            "084812",                                      // 7
            "15060718384746351504031232434435",            // 8
            "031232434738180706153546",                    // 9
            "2626|2323",                                   // :
            "2626|232211",                                 // ;
            "470543",                                      // <
            "0646|0444",                                   // =
            "074503",                                      // >
            "07183847462423|2222",                         // ?
            "331315353212030718384744",                    // @
            "0206284642|0444",                             // A
            "02083847463505|3544433202",                   // B
            "4738180703123243",                            // C
            "02083847433202",                              // D
            "48080242|0535",                               // E
            "480802|0535",                                 // F
            "47381807031232434525",                        // G
            "0208|4248|0545",                              // H
            "1232|2228|1838",                              // I
            "3833221203",                                  // J
            "0208|480542",                                 // K
            "080242",                                      // L
            "0208254842",                                  // M
            "02084248",                                    // N
            "123243473818070312",                          // O
            "02083847463505",                              // P
            "123243473818070312|2442",                     // Q
            "02083847463505|2542",                         // R
            "473818070615354443321203",                    // S
            "0848|2822",                                   // T
            "080312324348",                                // U
            "082248",                                      // V
            "0812253248",                                  // W
            "0248|0842",                                   // X
            "082548|2522",                                 // Y
            "08480242",                                    // Z
            "38181232",                                    // [
            "0842",                                        // backslash
            "18383212",                                    // ]
            "062846",                                      // ^
            "0141",                                        // _
            "1836",                                        // `
            "051636454212031444",                          // a
            "0802|0312324345361605",                       // b
            "4536160503123243",                            // c
            "4842|4332120305163645",                       // d
            "04444536160503123243",                        // e
            "12172838|0636",                               // f
            "4641301001|4536160503123243",                 // g
            "0802|0516364542",                             // h
            "2622|2828",                                   // i
            "3631201001|3838",                             // j
            "0802|460432",                                 // k
            "18132232",                                    // l
            "0206|05162522|25364542",                      // m
            "0206|0516364542",                             // n
            "123243453616050312",                          // o
            "0006|0516364543321203",                       // p
            "4046|4536160503123243",                       // q
            "0206|05163645",                               // r
            "4616051434433202",                            // s
            "18132232|0636",                               // t
            "0603123243|4642",                             // u
            "062246",                                      // v
            "0612243246",                                  // w
            "0246|0642",                                   // x
            "0603123243|4641301001",                       // y
            "06460242",                                    // z
            "38272615242332",                              // {
            "2821",                                        // |
            "18272635242312",                              // }
            "05163445",                                    // ~
            "0206284642|0444",                             // А
            "020848|053544433202",                         // Б
            "02083847463505|3544433202",                   // В
            "020848",                                      // Г
            "1838420218|0201|4241",                        // Д
            "48080242|0535",                               // Е
            "2228|0225|0825|4225|4825",                    // Ж
            "0718384746254443321203",                      // З
            "0208|4248|0248",                              // И
            "0208|4248|0248|1939",                         // Й
            "0208|480542",                                 // К
            "02184842",                                    // Л
            "0208254842",                                  // М
            "0208|4248|0545",                              // Н
            "123243473818070312",                          // О
            "02084842",                                    // П
            "02083847463505",                              // Р
            "4738180703123243",                            // С
            "0848|2822",                                   // Т
            "0824|481101",                                 // У
            "2228|170604133344463717",                     // Ф
            "0248|0842",                                   // Х
            "08024248|4240",                               // Ц
            "080545|4842",                                 // Ч
            "08024248|2228",                               // Ш
            "08024248|2228|4240",                          // Щ
            "0818|18123243443515",                         // Ъ
            "08022233342505|4842",                         // Ы
            "08022233342505",                              // Ь
            "0718384743321203|2545",                       // Э
            "0208|0515|223243473828171322",                // Ю
            "42481807061545|2502",                         // Я
            "051636454212031444",                          // а
            "48180703123243443505",                        // б
            "020636453404|34433202",                       // в
            "020646",                                      // г
            "1636420216|0200|4240",                        // д
            "04444536160503123243",                        // е
            "2226|0224|0624|4224|4624",                    // ж
            "051636452443321203",                          // з
            "0206|4246|0246",                              // и
            "0206|4246|0246|1737",                         // й
            "0206|460442",                                 // к
            "02164642",                                    // л
            "0206234642",                                  // м
            "0206|4246|0444",                              // н
            "123243453616050312",                          // о
            "02064642",                                    // п
            "0006|0516364543321203",                       // р
            "4536160503123243",                            // с
            "0646|2622",                                   // т
            "0623|4610",                                   // у
            "2027|160503123243453616",                     // ф
            "0246|0642",                                   // х
            "06024246|4240",                               // ц
            "060444|4642",                                 // ч
            "06024246|2226",                               // ш
            "06024246|2226|4240",                          // щ
            "0616|161232433414",                           // ъ
            "060222332404|4642",                           // ы
            "060222332404",                                // ь
            "0516364543321203|2444",                       // э
            "0206|0414|223243453626151322",                // ю
            "424616051444|2402",                           // я
            "48080242|0535|1919|3939",                     // Ё
            "04444536160503123243|1717|3737",              // ё
            "0242470702",                                  // заглушка
        };

        public static void Ensure()
        {
            if (_built) return;
            _built = true;

            int total = 0;
            for (int i = 0; i < _glyphs.Length; i++) total += CountSegments(_glyphs[i]);

            Segments = new NativeArray<Vector4>(Mathf.Max(1, total), Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            int w = 0;
            for (int i = 0; i < _glyphs.Length; i++)
            {
                _start[i] = w;
                w = Emit(_glyphs[i], w);
            }
            _start[_glyphs.Length] = w;
        }

        static int CountSegments(string s)
        {
            int n = 0, run = 0;
            for (int i = 0; i < s.Length; i += 2)
            {
                if (s[i] == '|') { if (run == 1) n++; run = 0; i -= 1; continue; }
                run++;
                if (run > 1) n++;
            }
            if (run == 1) n++;   // одиночная точка тоже даёт отрезок
            return n;
        }

        static int Emit(string s, int w)
        {
            float px = 0f, py = 0f;
            int run = 0;

            for (int i = 0; i < s.Length; i += 2)
            {
                if (s[i] == '|')
                {
                    if (run == 1) Segments[w++] = Dot(px, py);
                    run = 0;
                    i -= 1;
                    continue;
                }

                float x = s[i] - '0';
                float y = s[i + 1] - '0';

                // Точки закодированы отрезком нулевой длины: квад из него был бы пустым.
                if (run > 0)
                    Segments[w++] = (px == x && py == y)
                        ? Dot(x, y)
                        : new Vector4(px, py, x, y);

                px = x; py = y;
                run++;
            }

            if (run == 1) Segments[w++] = Dot(px, py);
            return w;
        }

        static Vector4 Dot(float x, float y) => new Vector4(x, y, x + 0.34f, y);

        /// <summary>Неизвестный видимый символ — в заглушку, управляющий — в пустоту.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Slot(char c)
        {
            if (c < AsciiFirst) return -1;
            if (c <= AsciiLast) return c - AsciiFirst;
            if (c >= CyrFirst && c <= CyrLast) return CyrBase + (c - CyrFirst);
            if (c == 'Ё') return SlotYo;
            if (c == 'ё') return SlotYoLower;
            return SlotTofu;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Glyph(char c, out int start, out int count)
        {
            int i = Slot(c);
            if (i < 0) { start = 0; count = 0; return false; }
            start = _start[i];
            count = _start[i + 1] - start;
            return count > 0;
        }

        /// <summary>Ширина строки из count символов, в единицах сетки.</summary>
        public static float LineWidth(int count) => count <= 0 ? 0f : count * Advance - 1.5f;

        /// <summary>Ширина текста в единицах сетки. Многострочный — по самой длинной строке.</summary>
        public static float Width(string text)
        {
            Measure(text, out _, out float w);
            return w;
        }

        /// <summary>Число строк и ширина самой длинной, в единицах сетки.</summary>
        public static void Measure(string text, out int lines, out float maxWidth)
        {
            lines = 0; maxWidth = 0f;
            if (string.IsNullOrEmpty(text)) return;

            int start = 0;
            while (true)
            {
                int end = text.IndexOf('\n', start);
                int stop = end < 0 ? text.Length : end;

                int len = stop - start;
                if (len > 0 && text[stop - 1] == '\r') len--;   // CRLF

                lines++;
                float w = LineWidth(len);
                if (w > maxWidth) maxWidth = w;

                if (end < 0) return;
                start = end + 1;
            }
        }

        public static void Dispose()
        {
            if (Segments.IsCreated) Segments.Dispose();
            _built = false;
        }
    }
}
