using UnityEngine;

namespace RuntimeGizmos
{
    /// <summary>Снимок разрешённой конфигурации. Пересобирается только при изменениях.</summary>
    public struct GizmoConfig
    {
        /// <summary>Рисовать в игровых камерах, в том числе в билде.</summary>
        public bool DrawInGameView;

        /// <summary>Рисовать в Scene View.</summary>
        public bool DrawInSceneView;

        /// <summary>Превью-камеры инспектора, отражения и прочее.</summary>
        public bool DrawInOtherCameras;

        /// <summary>Слой геометрии. Учитывается culling mask камеры.</summary>
        public int Layer;

        /// <summary>Rendering layer mask, с которым уходит геометрия.</summary>
        public uint RenderingLayerMask;

        /// <summary>Множитель альфы для всей отрисовки.</summary>
        public float GlobalAlpha;

        /// <summary>Толщина линии в пикселях, с которой стартует Gizmo.lineWidth и к которой возвращает Gizmo.Reset().</summary>
        public float DefaultLineWidth;

        /// <summary>Сдвиг глубины к камере (в единицах NDC) для линий с тестом глубины.</summary>
        public float DepthBias;

        /// <summary>Потолок роста вершинного буфера на канал. 0 — без ограничения.</summary>
        public int MaxVerticesPerChannel;

        /// <summary>Сегментов в окружности.</summary>
        public int CircleSegments;

        /// <summary>Колец в сплошной сфере.</summary>
        public int SphereRings;

        /// <summary>Сегментов в кольце сплошной сферы.</summary>
        public int SphereSegments;

        /// <summary>Edit Mode: сколько секунд держать последний снимок геометрии без новых команд.</summary>
        public float EditorStaleTimeout;

        /// <summary>Edit Mode: запрашивать перерисовку Scene View при появлении новой геометрии.</summary>
        public bool EditorAutoRepaint;

        /// <summary>
        /// Отступ угловых надписей DrawScreenText от краёв экрана, в пикселях.
        ///
        /// Считается до КРАЯ ЧЕРНИЛ, а не до якоря строки: выносные элементы и верхушки
        /// заглавных внутрь зоны не залезают. На телевизорах и телефонах с вырезом
        /// поднимите значение — там края экрана физически не видны.
        /// </summary>
        public float ScreenSafeArea;

        /// <summary>Зажать в допустимые диапазоны, чтобы кривой ассет не ронял отрисовку.</summary>
        public void Sanitize()
        {
            Layer                 = Mathf.Clamp(Layer, 0, 31);
            GlobalAlpha           = Mathf.Clamp01(GlobalAlpha);
            DefaultLineWidth      = Mathf.Max(0f, DefaultLineWidth);
            DepthBias             = Mathf.Max(0f, DepthBias);
            MaxVerticesPerChannel = Mathf.Max(0, MaxVerticesPerChannel);
            CircleSegments        = Mathf.Clamp(CircleSegments, 6, 256);
            SphereRings           = Mathf.Clamp(SphereRings, 3, 64);
            SphereSegments        = Mathf.Clamp(SphereSegments, 4, 128);
            EditorStaleTimeout    = Mathf.Max(0f, EditorStaleTimeout);
            ScreenSafeArea        = Mathf.Max(0f, ScreenSafeArea);
        }

        /// <summary>Дефолты под платформу.</summary>
        public static GizmoConfig DefaultsFor(GizmoPlatform platform)
        {
            var c = new GizmoConfig
            {
                DrawInGameView     = true,
                DrawInSceneView    = true,
                DrawInOtherCameras = false,
                Layer              = 0,
                RenderingLayerMask = uint.MaxValue,
                GlobalAlpha        = 1f,
                EditorStaleTimeout = 0.35f,
                EditorAutoRepaint  = true,
                ScreenSafeArea     = 12f,
            };

            switch (platform)
            {
                // Высокий DPI: линия в один физический пиксель почти не видна.
                case GizmoPlatform.Mobile:
                    c.DefaultLineWidth      = 2f;
                    c.CircleSegments        = 20;
                    c.SphereRings           = 6;
                    c.SphereSegments        = 12;
                    c.MaxVerticesPerChannel = 1 << 18;   // ~4 МБ тонкого канала
                    break;

                // Куча WebGL фиксируется на старте — потолок самый жёсткий.
                case GizmoPlatform.Web:
                    c.DefaultLineWidth      = 2f;
                    c.CircleSegments        = 20;
                    c.SphereRings           = 6;
                    c.SphereSegments        = 12;
                    c.MaxVerticesPerChannel = 1 << 17;   // ~2 МБ тонкого канала
                    break;

                // Рисуется дважды, по разу на глаз; фокус в паре метров.
                case GizmoPlatform.XR:
                    c.DefaultLineWidth      = 3f;
                    c.CircleSegments        = 24;
                    c.SphereRings           = 6;
                    c.SphereSegments        = 12;
                    c.MaxVerticesPerChannel = 1 << 17;
                    break;

                // Телевизор через комнату.
                case GizmoPlatform.Console:
                    c.DefaultLineWidth      = 2f;
                    c.CircleSegments        = 32;
                    c.SphereRings           = 8;
                    c.SphereSegments        = 16;
                    c.MaxVerticesPerChannel = 1 << 20;
                    break;

                default: // Desktop
                    c.DefaultLineWidth      = 1f;
                    c.CircleSegments        = 32;
                    c.SphereRings           = 8;
                    c.SphereSegments        = 16;
                    c.MaxVerticesPerChannel = 1 << 20;   // ~16 МБ тонкого канала
                    break;
            }

            // Зависит не от платформы, а от буфера глубины: у прямого [0,1] в OpenGL,
            // GLES и WebGL точности меньше, чем у reversed-Z с float.
            c.DepthBias = SystemInfo.usesReversedZBuffer ? 1e-4f : 3e-4f;

            return c;
        }
    }
}
