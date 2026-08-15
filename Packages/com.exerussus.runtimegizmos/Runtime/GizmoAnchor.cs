namespace RuntimeGizmos
{
    /// <summary>
    /// Точка привязки экранного блока.
    ///
    /// Числовое значение — не украшение, а формат обмена с шейдером: индекс 0..8 уезжает
    /// в z вершины, и GizmoText.shader разбирает его как ax = idx % 3, ay = idx / 3,
    /// начало блока = (ax * 0.5 * ширина таргета, ay * 0.5 * высота таргета).
    ///
    /// Смысл в том, что край экрана вычисляется НА GPU, из _ScreenParams текущей камеры.
    /// Раньше правый и нижний края отмерялись на CPU через Screen.width/height — и любой
    /// рендер в RenderTexture другого размера уводил надписи за кадр. Теперь один и тот же
    /// меш корректен сразу для всех камер кадра, а перевыпускать геометрию на камеру не нужно.
    ///
    /// ПОРЯДОК И ЗНАЧЕНИЯ МЕНЯТЬ НЕЛЬЗЯ — на них завязан шейдер. TopLeft обязан остаться
    /// нулём: у экранного текста без якоря z равен нулю, и он должен означать левый верхний угол.
    /// </summary>
    public enum GizmoAnchor
    {
        TopLeft = 0,
        Top = 1,
        TopRight = 2,

        Left = 3,
        Center = 4,
        Right = 5,

        BottomLeft = 6,
        Bottom = 7,
        BottomRight = 8,
    }

    /// <summary>Перевод между старым GizmoCorner и якорем.</summary>
    public static class GizmoAnchorUtil
    {
        /// <summary>Угол как частный случай якоря.</summary>
        public static GizmoAnchor FromCorner(GizmoCorner corner)
        {
            switch (corner)
            {
                case GizmoCorner.TopRight: return GizmoAnchor.TopRight;
                case GizmoCorner.BottomLeft: return GizmoAnchor.BottomLeft;
                case GizmoCorner.BottomRight: return GizmoAnchor.BottomRight;
                default: return GizmoAnchor.TopLeft;
            }
        }

        /// <summary>0 — левый край, 1 — центр, 2 — правый.</summary>
        public static int HorizontalOf(GizmoAnchor a) => (int)a % 3;

        /// <summary>0 — верх, 1 — середина, 2 — низ.</summary>
        public static int VerticalOf(GizmoAnchor a) => (int)a / 3;
    }
}
