// Тело фабрики вырезается тем же условием, каким [Conditional] вырезает Draw*.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RUNTIME_GIZMOS_ALWAYS
#define RUNTIME_GIZMOS_ON
#endif

using RuntimeGizmos.Internal;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    public static unsafe partial class Gizmo
    {
        /// <summary>
        /// Экранная таблица с автошириной колонок, привязанная к якорю.
        ///
        /// Единственный метод пакета, который НЕ помечен [Conditional], — и не может быть:
        /// условный метод обязан возвращать void (CS0578), а через out хендл не отдать
        /// (CS0685 запрещает out у условных методов). Поэтому в релизе вырезается не вызов,
        /// а тело: возвращается пустой хендл, все последующие t.Row(...) вырезаются как
        /// обычные Draw*, и таблица не набирает ни строки. Стоимость в релизе — один
        /// вызов, возвращающий две нулевые четвёрки байт.
        ///
        /// Блоки на одном якоре укладываются стопкой в порядке создания — вперемешку
        /// с угловыми надписями DrawScreenText(corner), курсор у них общий.
        /// </summary>
        /// <param name="anchor">Куда прижать блок. Средние якоря центрируют стопку.</param>
        /// <param name="sizePixels">Высота прописной буквы в пикселях.</param>
        public static GizmoTable Table(GizmoAnchor anchor, float sizePixels = 14f)
        {
#if RUNTIME_GIZMOS_ON
            return GizmoHud.NewTable(anchor, sizePixels, false);
#else
            return default;
#endif
        }

        /// <summary>Таблица у угла экрана. Короткая запись для привычного GizmoCorner.</summary>
        public static GizmoTable Table(GizmoCorner corner, float sizePixels = 14f)
        {
#if RUNTIME_GIZMOS_ON
            return GizmoHud.NewTable(GizmoAnchorUtil.FromCorner(corner), sizePixels, false);
#else
            return default;
#endif
        }

        /// <summary>
        /// Надпись у произвольного якоря, а не только у угла. Строки на одном якоре
        /// укладываются стопкой автоматически, счётчик сбрасывается на границе кадра.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawScreenText(string text, GizmoAnchor anchor, float sizePixels = 14f)
            => GizmoHud.AnchoredLine(text, anchor, sizePixels);
    }
}
