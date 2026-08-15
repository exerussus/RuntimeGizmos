using System;
using System.Globalization;
using UnityEngine;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Экранная раскладка: таблицы и якорные надписи.
    ///
    /// Хендл-фасад. Наружу торчит только GizmoTable — непрозрачная структура из индекса
    /// и штампа кадра. Буферы сюда не отдаются: снаружи их некому чинить, а внутри они
    /// лежат одним куском и переиспользуются кадр за кадром.
    ///
    /// ПОЧЕМУ РАСКЛАДКА ОТЛОЖЕНА ДО ГРАНИЦЫ КАДРА. Ширину колонки нельзя знать, пока не
    /// поданы все строки, — а автоширина и есть весь смысл таблицы. Поэтому Row() только
    /// складывает ячейки в буфер, а измерение и выпуск вершин идут в Flush(), который
    /// вызывается первым делом в GizmoRenderer.BeginFrame — то есть ПОСЛЕ пользовательского
    /// кода и ДО обмена буферов канала. Опоздания на кадр нет.
    ///
    /// Побочно это дало то, чего немедленный режим не умел вовсе: средние якоря
    /// (Left/Center/Right) центрируют стопку блоков по вертикали, потому что к моменту
    /// раскладки её полная высота уже известна.
    ///
    /// ТЕКСТ НЕ ХРАНИТСЯ СТРОКАМИ. Ячейка — это (смещение, длина) в общей арене char[],
    /// которая живёт кадр и переиспользуется. Так и string, и результат TryFormat ложатся
    /// в одно место, в SoA-буферах нет ни одной управляемой ссылки, а перегрузки вида
    /// Row("hp", value) не создают мусора вообще.
    ///
    /// Аллокации — только в Ensure(), по капасити из настроек. Переполнение роняет лишнее
    /// и один раз пишет в консоль, как это делает GizmoLazy при переполнении MaxTracked.
    /// </summary>
    internal static class GizmoHud
    {
        /// <summary>Потолок колонок. Выравнивание пакуется по 2 бита на колонку в один int.</summary>
        internal const int MaxColumns = 8;

        /// <summary>Блоков на кадр. Константа, а не настройка: столько HUD-блоков никто не читает.</summary>
        const int MaxTables = 64;

        const byte KindCells = 0;
        const byte KindSeparator = 1;
        const byte KindTitle = 2;

        const int Anchors = 9;

        // ------------------------------------------------------------------ арена символов

        static char[] _chars;
        static int _charsUsed;

        // ------------------------------------------------------------------ ячейки (SoA)

        static int[] _cellOffset;
        static int[] _cellLength;
        static Color32[] _cellColor;
        static int _cellCount;

        // ------------------------------------------------------------------ строки (SoA)

        static int[] _rowFirstCell;
        static int[] _rowCells;
        static byte[] _rowKind;
        static Color32[] _rowColor;      // используется только разделителем
        static int[] _rowNext;           // односвязный список строк таблицы, -1 — конец
        static int _rowCount;

        // ------------------------------------------------------------------ таблицы (SoA)

        static readonly byte[] _tblAnchor = new byte[MaxTables];
        static readonly float[] _tblSize = new float[MaxTables];
        static readonly int[] _tblAlign = new int[MaxTables];      // 2 бита на колонку
        static readonly byte[] _tblCols = new byte[MaxTables];
        static readonly int[] _tblFirstRow = new int[MaxTables];
        static readonly int[] _tblLastRow = new int[MaxTables];
        static readonly float[] _tblStroke = new float[MaxTables];
        static readonly byte[] _tblZ = new byte[MaxTables];
        static readonly bool[] _tblFixedCols = new bool[MaxTables];
        static int _tblCount;

        /// <summary>
        /// Открытая неявная таблица на якорь — та, в которую дописывает DrawScreenText(corner).
        /// Обнуляется, как только на этом якоре появляется явная таблица: иначе следующая
        /// угловая надпись дописалась бы в блок, стоящий ВЫШЕ явной таблицы, и порядок вызовов
        /// перестал бы совпадать с порядком на экране.
        /// </summary>
        static readonly int[] _implicitAt = new int[Anchors];

        static readonly float[] _colWidth = new float[MaxColumns];

        static int _stamp = 1;
        static bool _warned;
        static bool _ready;

        /// <summary>Штамп текущего кадра. Ноль зарезервирован под невалидный хендл.</summary>
        internal static int Stamp => _stamp;

        // ================================================================== жизненный цикл

        static void Ensure()
        {
            if (_ready) return;
            _ready = true;

            int cells = Mathf.Max(16, GizmoSettings.HudMaxCells);
            int chars = Mathf.Max(64, GizmoSettings.HudMaxChars);

            _chars = new char[chars];

            _cellOffset = new int[cells];
            _cellLength = new int[cells];
            _cellColor = new Color32[cells];

            // Строк не больше, чем ячеек: у строки с ячейками их минимум одна. Разделители
            // и заголовки ячеек не занимают, поэтому теоретически строк может быть больше —
            // на этот случай есть проверка на переполнение, а не молчаливая порча памяти.
            _rowFirstCell = new int[cells];
            _rowCells = new int[cells];
            _rowKind = new byte[cells];
            _rowColor = new Color32[cells];
            _rowNext = new int[cells];

            for (int i = 0; i < Anchors; i++) _implicitAt[i] = -1;
        }

        internal static void Dispose()
        {
            _ready = false;
            _warned = false;
            _chars = null;
            _cellOffset = null; _cellLength = null; _cellColor = null;
            _rowFirstCell = null; _rowCells = null; _rowKind = null; _rowColor = null; _rowNext = null;
            Reset();
        }

        /// <summary>Сброс накопленного за кадр. Штамп сдвигается — старые хендлы протухают.</summary>
        internal static void Reset()
        {
            _charsUsed = 0;
            _cellCount = 0;
            _rowCount = 0;
            _tblCount = 0;

            for (int i = 0; i < Anchors; i++) _implicitAt[i] = -1;

            unchecked { _stamp++; }
            if (_stamp == 0) _stamp = 1;
        }

        static void Overflow()
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[RuntimeGizmos] Экранная раскладка переполнена, лишнее не нарисовано. " +
                             "Поднимите GizmoSettings.HudMaxCells и GizmoSettings.HudMaxChars " +
                             "или выводите меньше строк за кадр.");
        }

        // ================================================================== набор данных

        internal static GizmoTable NewTable(GizmoAnchor anchor, float sizePixels, bool implicitBlock)
        {
            Ensure();

            if (_tblCount == MaxTables) { Overflow(); return default; }

            int t = _tblCount++;
            _tblAnchor[t] = (byte)anchor;
            _tblSize[t] = sizePixels;
            _tblAlign[t] = 0;                 // все колонки влево
            _tblCols[t] = 1;
            _tblFixedCols[t] = false;
            _tblFirstRow[t] = -1;
            _tblLastRow[t] = -1;

            // Толщина штриха и слой глубины снимаются здесь, а не в Flush: к границе кадра
            // Gizmo.lineWidth уже чужой, и таблица нарисовалась бы тем, что осталось от
            // последнего вызова в кадре.
            _tblStroke[t] = GizmoRenderer.Width;
            _tblZ[t] = (byte)GizmoRenderer.Z;

            if (!implicitBlock) _implicitAt[(int)anchor] = -1;

            return new GizmoTable(t, _stamp);
        }

        /// <summary>Одна якорная надпись: дописывается в открытый неявный блок или открывает новый.</summary>
        internal static void AnchoredLine(ReadOnlySpan<char> text, GizmoAnchor anchor, float sizePixels)
        {
            if (text.IsEmpty || sizePixels <= 0f) return;

            Ensure();

            int a = (int)anchor;
            int t = _implicitAt[a];

            // Размер входит в условие: надписи разного кегля укладывать общим шагом нечем.
            if (t < 0 || _tblSize[t] != sizePixels)
            {
                var handle = NewTable(anchor, sizePixels, true);
                if (!handle.IsValid) return;
                t = handle.Index;
                _implicitAt[a] = t;
            }

            int r = OpenRow(t, _stamp, KindCells);
            PutText(r, text);
        }

        internal static bool Valid(int table, int stamp) =>
            stamp == _stamp && stamp != 0 && table >= 0 && table < _tblCount;

        internal static void SetColumns(int table, int stamp, ReadOnlySpan<GizmoTextAlign> aligns)
        {
            if (!Valid(table, stamp) || aligns.IsEmpty) return;

            int n = Mathf.Min(aligns.Length, MaxColumns);
            int bits = 0;
            for (int i = 0; i < n; i++) bits |= ((int)aligns[i] & 3) << (i * 2);

            _tblCols[table] = (byte)n;
            _tblAlign[table] = bits;
            _tblFixedCols[table] = true;
        }

        internal static void Separator(int table, int stamp, Color32 color)
        {
            int r = OpenRow(table, stamp, KindSeparator);
            if (r >= 0) _rowColor[r] = color;
        }

        /// <summary>Начать строку. Ячейки обязаны дописываться сразу: они лежат подряд.</summary>
        internal static int OpenRow(int table, int stamp, byte kind)
        {
            if (!Valid(table, stamp)) return -1;
            if (_rowCount == _rowFirstCell.Length) { Overflow(); return -1; }

            int r = _rowCount++;
            _rowFirstCell[r] = _cellCount;
            _rowCells[r] = 0;
            _rowKind[r] = kind;
            _rowNext[r] = -1;

            if (_tblFirstRow[table] < 0) _tblFirstRow[table] = r;
            else _rowNext[_tblLastRow[table]] = r;
            _tblLastRow[table] = r;

            return r;
        }

        internal static int OpenTitleRow(int table, int stamp) => OpenRow(table, stamp, KindTitle);

        internal static void PutText(int row, ReadOnlySpan<char> s)
        {
            if (row < 0) return;
            if (!Claim(s.Length, out int offset)) return;

            s.CopyTo(_chars.AsSpan(offset));
            Commit(row, offset, s.Length);
        }

        internal static void PutFloat(int row, float value, ReadOnlySpan<char> format)
        {
            if (row < 0) return;

            // Пишем прямо в арену: промежуточной строки, а значит и мусора, не возникает.
            var free = FreeSpan();
            if (!value.TryFormat(free, out int written, format, CultureInfo.InvariantCulture))
            {
                Overflow();
                return;
            }

            if (!Claim(written, out int offset)) return;
            Commit(row, offset, written);
        }

        internal static void PutInt(int row, int value)
        {
            if (row < 0) return;

            var free = FreeSpan();
            if (!value.TryFormat(free, out int written, default, CultureInfo.InvariantCulture))
            {
                Overflow();
                return;
            }

            if (!Claim(written, out int offset)) return;
            Commit(row, offset, written);
        }

        static Span<char> FreeSpan() =>
            _chars == null ? Span<char>.Empty : _chars.AsSpan(_charsUsed);

        /// <summary>Занять место в арене и слот ячейки. Смещение — то же, куда пишет FreeSpan.</summary>
        static bool Claim(int length, out int offset)
        {
            offset = _charsUsed;

            if (_cellCount == _cellOffset.Length) { Overflow(); return false; }
            if (_charsUsed + length > _chars.Length) { Overflow(); return false; }
            return true;
        }

        static void Commit(int row, int offset, int length)
        {
            _cellOffset[_cellCount] = offset;
            _cellLength[_cellCount] = length;
            _cellColor[_cellCount] = GizmoRenderer.Color;

            _charsUsed = offset + length;
            _cellCount++;
            _rowCells[row]++;
        }

        static ReadOnlySpan<char> CellText(int cell) =>
            new ReadOnlySpan<char>(_chars, _cellOffset[cell], _cellLength[cell]);

        // ================================================================== раскладка и выпуск

        /// <summary>
        /// Вызывается первым делом в BeginFrame — до обмена буферов канала, иначе всё
        /// нарисованное здесь опоздало бы ровно на кадр.
        /// </summary>
        internal static void Flush()
        {
            if (_tblCount == 0) { Reset(); return; }

            // На границе кадра состояние рисования пользователю уже не принадлежит,
            // но следующий кадр он вправе начать с того же, чем закончил этот.
            var savedColor = GizmoRenderer.Color;
            float savedWidth = GizmoRenderer.Width;
            int savedZ = GizmoRenderer.Z;
            float savedDuration = GizmoRenderer.Duration;

            GizmoRenderer.Duration = 0f;   // HUD живёт ровно кадр, retained ему не нужен

            for (int a = 0; a < Anchors; a++) FlushAnchor(a);

            GizmoRenderer.Color = savedColor;
            GizmoRenderer.Width = savedWidth;
            GizmoRenderer.Z = savedZ;
            GizmoRenderer.Duration = savedDuration;

            Reset();
        }

        static void FlushAnchor(int anchor)
        {
            // Первый проход считает полную высоту стопки. Без неё средние якоря не смогли бы
            // центрироваться, а нижние — отмерить безопасную зону от нижнего края чернил.
            float total = 0f, tail = 0f;
            bool any = false;

            for (int t = 0; t < _tblCount; t++)
            {
                if (_tblAnchor[t] != anchor) continue;
                Measure(t, out float h, out float slack, out _);
                if (h <= 0f) continue;
                total += h;
                tail = slack;
                any = true;
            }

            if (!any) return;

            float pad = Mathf.Max(0f, GizmoSettings.ScreenSafeArea);

            // Высота ЧЕРНИЛ, а не сумма шагов строк: последняя строка не дотягивает до
            // своего шага на выносной элемент, и без поправки нижний блок висел бы выше
            // заказанного отступа.
            float ink = total - tail;

            int ay = anchor / 3;
            float y = ay == 0 ? pad
                    : ay == 1 ? -ink * 0.5f
                    : -(pad + ink);

            for (int t = 0; t < _tblCount; t++)
            {
                if (_tblAnchor[t] != anchor) continue;
                Measure(t, out float h, out _, out float w);
                if (h <= 0f) continue;
                Emit(t, anchor, y, w);
                y += h;
            }
        }

        /// <summary>
        /// Высота блока, недобор последней строки до полного шага и ширина.
        /// Побочно заполняет _colWidth — Emit идёт сразу следом и пользуется им.
        /// </summary>
        static void Measure(int table, out float height, out float tailSlack, out float width)
        {
            float scale = _tblSize[table] / GizmoFont.CapHeight;
            float lineStep = GizmoFont.LineStep * scale;

            // Columns() не звали — берём число колонок по самой длинной строке. Иначе
            // Row("hp", "100") без объявления колонок молча терял бы второе значение,
            // а это ровно тот вызов, который пишут первым.
            if (!_tblFixedCols[table])
            {
                int widest = 1;
                for (int r = _tblFirstRow[table]; r >= 0; r = _rowNext[r])
                    if (_rowKind[r] == KindCells && _rowCells[r] > widest) widest = _rowCells[r];

                _tblCols[table] = (byte)Mathf.Min(widest, MaxColumns);
                _tblFixedCols[table] = true;
            }

            int cols = _tblCols[table];

            for (int c = 0; c < MaxColumns; c++) _colWidth[c] = 0f;

            height = 0f;
            tailSlack = 0f;
            float titleWidth = 0f;

            for (int r = _tblFirstRow[table]; r >= 0; r = _rowNext[r])
            {
                if (_rowKind[r] == KindSeparator)
                {
                    height += lineStep * 0.5f;
                    tailSlack = 0f;
                    continue;
                }

                int first = _rowFirstCell[r];
                int n = _rowCells[r];
                int maxLines = 1;

                for (int i = 0; i < n; i++)
                {
                    GizmoFont.Measure(CellText(first + i), out int lines, out float w);
                    if (lines > maxLines) maxLines = lines;

                    if (_rowKind[r] == KindTitle)
                    {
                        if (w > titleWidth) titleWidth = w;
                    }
                    else
                    {
                        int c = i < cols ? i : cols - 1;
                        if (w > _colWidth[c]) _colWidth[c] = w;
                    }
                }

                height += maxLines * lineStep;

                // Шаг строки — 10 единиц сетки, чернила занимают заглавную (6) плюс
                // выносной элемент (2). Остаток — межстрочный воздух, он в высоту блока
                // по нижнему краю не входит.
                tailSlack = lineStep - (GizmoFont.CapHeight + GizmoFont.Baseline) * scale;
            }

            float gap = Mathf.Max(0f, GizmoSettings.HudColumnGap) * GizmoFont.Advance * scale;

            width = 0f;
            for (int c = 0; c < cols; c++) width += _colWidth[c] * scale;
            if (cols > 1) width += gap * (cols - 1);

            float title = titleWidth * scale;
            if (title > width) width = title;
        }

        static void Emit(int table, int anchor, float top, float width)
        {
            float size = _tblSize[table];
            float scale = size / GizmoFont.CapHeight;
            float lineStep = GizmoFont.LineStep * scale;
            float capTop = GizmoFont.CapHeight * scale;
            float gap = Mathf.Max(0f, GizmoSettings.HudColumnGap) * GizmoFont.Advance * scale;
            float pad = Mathf.Max(0f, GizmoSettings.ScreenSafeArea);

            int cols = _tblCols[table];
            int ax = anchor % 3;

            float stroke = Mathf.Max(1f, _tblStroke[table]);

            // Штрих рисуется капсулой и выходит за отрезок на полтолщины в каждую сторону.
            // Безопасная зона отмеряется до края ЧЕРНИЛ, поэтому поправка обязана быть —
            // иначе жирный шрифт вылезал бы за отступ ровно на эту половину.
            float halfStroke = stroke * 0.5f;

            float x0 = ax == 0 ? pad + halfStroke
                     : ax == 1 ? -width * 0.5f
                     : -(pad + halfStroke + width);

            GizmoRenderer.Width = _tblStroke[table];
            GizmoRenderer.Z = _tblZ[table];
            float y = top;

            for (int r = _tblFirstRow[table]; r >= 0; r = _rowNext[r])
            {
                if (_rowKind[r] == KindSeparator)
                {
                    // Полоса с t = 0 рисует только фон — ровно один сегмент, без наложения.
                    GizmoRenderer.Bar(new Vector3(0f, y + lineStep * 0.25f, anchor),
                                      0f, width, stroke,
                                      default, _rowColor[r],
                                      new Vector2(x0 + width * 0.5f, 0f), 2);
                    y += lineStep * 0.5f;
                    continue;
                }

                int first = _rowFirstCell[r];
                int n = _rowCells[r];

                int maxLines = 1;
                for (int i = 0; i < n; i++)
                {
                    GizmoFont.Measure(CellText(first + i), out int lines, out _);
                    if (lines > maxLines) maxLines = lines;
                }

                // Text центрирует блок по вертикали вокруг якоря, поэтому якорь строки —
                // не её верх, а середина: верх чернил + половина высоты блока.
                float anchorY = y + capTop + (maxLines - 1) * lineStep * 0.5f;

                if (_rowKind[r] == KindTitle)
                {
                    if (n > 0)
                    {
                        GizmoRenderer.Color = _cellColor[first];
                        GizmoRenderer.Text(CellText(first), new Vector3(x0, anchorY, anchor),
                                           size, Vector2.zero, 0f, 2);
                    }
                }
                else
                {
                    float cx = x0;
                    for (int i = 0; i < n && i < cols; i++)
                    {
                        float af = AlignFactor((GizmoTextAlign)((_tblAlign[table] >> (i * 2)) & 3));
                        float cw = _colWidth[i] * scale;

                        // Text отмеряет penX = x - ширинаТекста * align, поэтому в x идёт
                        // точка выравнивания колонки, а не её левый край.
                        GizmoRenderer.Color = _cellColor[first + i];
                        GizmoRenderer.Text(CellText(first + i),
                                           new Vector3(cx + cw * af, anchorY, anchor),
                                           size, Vector2.zero, af, 2);

                        cx += cw + gap;
                    }
                }

                y += maxLines * lineStep;
            }
        }

        static float AlignFactor(GizmoTextAlign a) =>
            a == GizmoTextAlign.Left ? 0f : a == GizmoTextAlign.Right ? 1f : 0.5f;
    }
}
