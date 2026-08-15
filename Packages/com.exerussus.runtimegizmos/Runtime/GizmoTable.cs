using System;
using UnityEngine;
using RuntimeGizmos.Internal;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    /// <summary>
    /// Экранная таблица: якорь, колонки с автошириной, строки.
    ///
    /// Получается из Gizmo.Table(...) и живёт РОВНО ОДИН КАДР. Внутри — индекс блока и
    /// штамп кадра; хендл, сохранённый в поле и использованный на следующем кадре, просто
    /// перестаёт что-либо делать, а не дописывает в чужую таблицу.
    ///
    /// ПОЧЕМУ ЭТО СТРУКТУРА С void-МЕТОДАМИ. Все методы помечены [Conditional] и вырезаются
    /// из релиза вместе с вычислением аргументов — то есть t.Row("hp", hp.ToString()) не
    /// вызовет ToString(). Ценой этого стало ограничение языка: условный метод обязан
    /// возвращать void (CS0578) и не может иметь out-параметров (CS0685). Поэтому таблица
    /// не «возвращает результат» и не отдаётся через out — она приходит от фабрики, тело
    /// которой в релизе схлопывается в return default.
    ///
    /// Раскладка считается не здесь, а на границе кадра: ширину колонки нельзя знать, пока
    /// не поданы все строки. Подробности — в GizmoHud.
    /// </summary>
    /// <example>
    /// <code>
    /// var t = Gizmo.Table(GizmoAnchor.TopRight);
    /// t.Columns(GizmoTextAlign.Left, GizmoTextAlign.Right);
    /// t.Title("player");
    /// t.Row("hp", 100f, "F0");
    /// t.Row("ammo", 24);
    /// t.Separator();
    /// t.Row("state", "idle");
    /// </code>
    /// </example>
    public readonly struct GizmoTable
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        internal readonly int Index;
        internal readonly int Stamp;

        internal GizmoTable(int index, int stamp)
        {
            Index = index;
            Stamp = stamp;
        }

        /// <summary>Хендл этого кадра и указывает на существующий блок.</summary>
        public bool IsValid => GizmoHud.Valid(Index, Stamp);

        // ================================================================= колонки

        /// <summary>
        /// Выравнивание по колонкам. Число колонок = число переданных значений, максимум 8.
        /// Без вызова колонка одна, выравнивание влево.
        ///
        /// Ширина колонки не задаётся: она берётся по самой широкой ячейке в ней.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Columns(ReadOnlySpan<GizmoTextAlign> aligns) =>
            GizmoHud.SetColumns(Index, Stamp, aligns);

        /// <summary>Две колонки — типовой случай: метка влево, значение вправо.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Columns(GizmoTextAlign first, GizmoTextAlign second)
        {
            Span<GizmoTextAlign> a = stackalloc GizmoTextAlign[2] { first, second };
            GizmoHud.SetColumns(Index, Stamp, a);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Columns(GizmoTextAlign first, GizmoTextAlign second, GizmoTextAlign third)
        {
            Span<GizmoTextAlign> a = stackalloc GizmoTextAlign[3] { first, second, third };
            GizmoHud.SetColumns(Index, Stamp, a);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Columns(GizmoTextAlign first, GizmoTextAlign second,
                            GizmoTextAlign third, GizmoTextAlign fourth)
        {
            Span<GizmoTextAlign> a = stackalloc GizmoTextAlign[4] { first, second, third, fourth };
            GizmoHud.SetColumns(Index, Stamp, a);
        }

        // ================================================================= строки

        /// <summary>Заголовок: одна ячейка во всю ширину таблицы, вне колоночной сетки.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Title(ReadOnlySpan<char> text) =>
            GizmoHud.PutText(GizmoHud.OpenTitleRow(Index, Stamp), text);

        /// <summary>Горизонтальная черта во всю ширину таблицы. Цвет — текущий Gizmo.color.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Separator() =>
            GizmoHud.Separator(Index, Stamp, GizmoRenderer.Color);

        /// <summary>Ячейки лишних колонок отбрасываются, недостающие остаются пустыми.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> a)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, a);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, a);
            GizmoHud.PutText(r, b);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, a);
            GizmoHud.PutText(r, b);
            GizmoHud.PutText(r, c);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> a, ReadOnlySpan<char> b,
                        ReadOnlySpan<char> c, ReadOnlySpan<char> d)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, a);
            GizmoHud.PutText(r, b);
            GizmoHud.PutText(r, c);
            GizmoHud.PutText(r, d);
        }

        // ----------------------------------------------------------------- значения без мусора
        //
        // Число форматируется прямо в арену символов через TryFormat. Это не украшение:
        // ToString() на каждое значение каждый кадр — самый заметный источник мусора
        // в отладочном выводе, и в профайлере он маскирует то, ради чего HUD и включали.

        /// <summary>Метка и число. format — как у float.ToString, например "F1".</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> label, float value, ReadOnlySpan<char> format = default)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, label);
            GizmoHud.PutFloat(r, value, format);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> label, int value)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, label);
            GizmoHud.PutInt(r, value);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> label, bool value)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, label);
            GizmoHud.PutText(r, value ? "да" : "нет");
        }

        /// <summary>Вектор тремя колонками: метка, x, y, z.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public void Row(ReadOnlySpan<char> label, Vector3 value, ReadOnlySpan<char> format = default)
        {
            int r = GizmoHud.OpenRow(Index, Stamp, 0);
            GizmoHud.PutText(r, label);
            GizmoHud.PutFloat(r, value.x, format);
            GizmoHud.PutFloat(r, value.y, format);
            GizmoHud.PutFloat(r, value.z, format);
        }
    }
}
