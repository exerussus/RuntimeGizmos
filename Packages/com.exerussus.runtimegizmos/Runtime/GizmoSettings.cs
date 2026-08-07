using System;
using UnityEngine;

namespace RuntimeGizmos
{
    /// <summary>
    /// Значение разрешается по слоям: оверрайд из кода → ассет GizmoSettingsAsset из любой
    /// папки Resources (платформенная секция, затем общая) → платформенный дефолт.
    ///
    /// Оверрайды живут одну сессию: сбрасываются на входе в Play Mode, даже если
    /// Domain Reload выключен и статика пережила переход.
    /// </summary>
    public static class GizmoSettings
    {
        // ==================================================== разрешённая конфигурация

        static GizmoConfig _resolved;
        static bool _valid;

        /// <summary>Снимок со всеми применёнными слоями.</summary>
        public static ref readonly GizmoConfig Current
        {
            get
            {
                if (!_valid) Resolve();
                return ref _resolved;
            }
        }

        /// <summary>Пересобрать конфигурацию при следующем обращении.</summary>
        public static void Invalidate() => _valid = false;

        static void Resolve()
        {
            var platform = Platform;

            _resolved = GizmoConfig.DefaultsFor(platform);

            var asset = Asset;
            if (asset != null) asset.ApplyTo(ref _resolved, platform);

            Overrides.ApplyTo(ref _resolved);

            _resolved.Sanitize();
            _valid = true;
        }

        // ==================================================== платформа

        static GizmoPlatform _platform;
        static bool _platformValid;
        static GizmoPlatform? _platformOverride;

        /// <summary>Класс платформы, под который берутся дефолты.</summary>
        public static GizmoPlatform Platform
        {
            get
            {
                if (_platformValid) return _platform;
                _platform = _platformOverride ?? GizmoPlatformUtil.Detect();
                _platformValid = true;
                return _platform;
            }
        }

        /// <summary>Посмотреть чужой профиль, не собирая билд. null — автоопределение.</summary>
        public static GizmoPlatform? PlatformOverride
        {
            get => _platformOverride;
            set
            {
                _platformOverride = value;
                _platformValid = false;
                Invalidate();
            }
        }

        // ==================================================== ассет

        /// <summary>Ищется в любой папке Resources.</summary>
        public const string AssetResourceName = "RuntimeGizmosSettings";

        static GizmoSettingsAsset _asset;
        static bool _assetLoaded;

        /// <summary>Ассет настроек или null.</summary>
        public static GizmoSettingsAsset Asset
        {
            get
            {
                if (_assetLoaded) return _asset;
                _asset = Resources.Load<GizmoSettingsAsset>(AssetResourceName);
                _assetLoaded = true;
                return _asset;
            }
        }

        /// <summary>Перечитать ассет с диска.</summary>
        public static void ReloadAsset()
        {
            _asset = null;
            _assetLoaded = false;
            Invalidate();
        }

        // ==================================================== оверрайды из кода

        /// <summary>Оверрайды из кода. null в поле — значение придёт из ассета или дефолта.</summary>
        public static class Overrides
        {
            /// <summary>Рисовать в игровых камерах, в том числе в билде.</summary>
            public static bool? DrawInGameView { get => _DrawInGameView; set { _DrawInGameView = value; Invalidate(); } }
            static bool? _DrawInGameView;

            /// <summary>Рисовать в Scene View.</summary>
            public static bool? DrawInSceneView { get => _DrawInSceneView; set { _DrawInSceneView = value; Invalidate(); } }
            static bool? _DrawInSceneView;

            /// <summary>Превью-камеры инспектора, отражения и прочее.</summary>
            public static bool? DrawInOtherCameras { get => _DrawInOtherCameras; set { _DrawInOtherCameras = value; Invalidate(); } }
            static bool? _DrawInOtherCameras;

            /// <summary>Слой геометрии. Учитывается culling mask камеры.</summary>
            public static int? Layer { get => _Layer; set { _Layer = value; Invalidate(); } }
            static int? _Layer;

            /// <summary>Rendering layer mask, с которым уходит геометрия.</summary>
            public static uint? RenderingLayerMask { get => _RenderingLayerMask; set { _RenderingLayerMask = value; Invalidate(); } }
            static uint? _RenderingLayerMask;

            /// <summary>Множитель альфы для всей отрисовки.</summary>
            public static float? GlobalAlpha { get => _GlobalAlpha; set { _GlobalAlpha = value; Invalidate(); } }
            static float? _GlobalAlpha;

            /// <summary>Толщина линии в пикселях, с которой стартует Gizmo.lineWidth и к которой возвращает Gizmo.Reset().</summary>
            public static float? DefaultLineWidth { get => _DefaultLineWidth; set { _DefaultLineWidth = value; Invalidate(); } }
            static float? _DefaultLineWidth;

            /// <summary>Сдвиг глубины к камере (в единицах NDC) для линий с тестом глубины.</summary>
            public static float? DepthBias { get => _DepthBias; set { _DepthBias = value; Invalidate(); } }
            static float? _DepthBias;

            /// <summary>Потолок роста вершинного буфера на канал. 0 — без ограничения.</summary>
            public static int? MaxVerticesPerChannel { get => _MaxVerticesPerChannel; set { _MaxVerticesPerChannel = value; Invalidate(); } }
            static int? _MaxVerticesPerChannel;

            /// <summary>Сегментов в окружности.</summary>
            public static int? CircleSegments { get => _CircleSegments; set { _CircleSegments = value; Invalidate(); } }
            static int? _CircleSegments;

            /// <summary>Колец в сплошной сфере.</summary>
            public static int? SphereRings { get => _SphereRings; set { _SphereRings = value; Invalidate(); } }
            static int? _SphereRings;

            /// <summary>Сегментов в кольце сплошной сферы.</summary>
            public static int? SphereSegments { get => _SphereSegments; set { _SphereSegments = value; Invalidate(); } }
            static int? _SphereSegments;

            /// <summary>Edit Mode: сколько секунд держать последний снимок геометрии без новых команд.</summary>
            public static float? EditorStaleTimeout { get => _EditorStaleTimeout; set { _EditorStaleTimeout = value; Invalidate(); } }
            static float? _EditorStaleTimeout;

            /// <summary>Edit Mode: запрашивать перерисовку Scene View при появлении новой геометрии.</summary>
            public static bool? EditorAutoRepaint { get => _EditorAutoRepaint; set { _EditorAutoRepaint = value; Invalidate(); } }
            static bool? _EditorAutoRepaint;

            /// <summary>Отступ угловых надписей DrawScreenText от краёв экрана, в пикселях.</summary>
            public static float? ScreenSafeArea { get => _ScreenSafeArea; set { _ScreenSafeArea = value; Invalidate(); } }
            static float? _ScreenSafeArea;

            /// <summary>Снять все оверрайды и вернуться к ассету и дефолтам.</summary>
            public static void Clear()
            {
                _DrawInGameView = null;
                _DrawInSceneView = null;
                _DrawInOtherCameras = null;
                _Layer = null;
                _RenderingLayerMask = null;
                _GlobalAlpha = null;
                _DefaultLineWidth = null;
                _DepthBias = null;
                _MaxVerticesPerChannel = null;
                _CircleSegments = null;
                _SphereRings = null;
                _SphereSegments = null;
                _EditorStaleTimeout = null;
                _EditorAutoRepaint = null;
                _ScreenSafeArea = null;
                Invalidate();
            }

            internal static void ApplyTo(ref GizmoConfig c)
            {
                if (_DrawInGameView.HasValue) c.DrawInGameView = _DrawInGameView.Value;
                if (_DrawInSceneView.HasValue) c.DrawInSceneView = _DrawInSceneView.Value;
                if (_DrawInOtherCameras.HasValue) c.DrawInOtherCameras = _DrawInOtherCameras.Value;
                if (_Layer.HasValue) c.Layer = _Layer.Value;
                if (_RenderingLayerMask.HasValue) c.RenderingLayerMask = _RenderingLayerMask.Value;
                if (_GlobalAlpha.HasValue) c.GlobalAlpha = _GlobalAlpha.Value;
                if (_DefaultLineWidth.HasValue) c.DefaultLineWidth = _DefaultLineWidth.Value;
                if (_DepthBias.HasValue) c.DepthBias = _DepthBias.Value;
                if (_MaxVerticesPerChannel.HasValue) c.MaxVerticesPerChannel = _MaxVerticesPerChannel.Value;
                if (_CircleSegments.HasValue) c.CircleSegments = _CircleSegments.Value;
                if (_SphereRings.HasValue) c.SphereRings = _SphereRings.Value;
                if (_SphereSegments.HasValue) c.SphereSegments = _SphereSegments.Value;
                if (_EditorStaleTimeout.HasValue) c.EditorStaleTimeout = _EditorStaleTimeout.Value;
                if (_EditorAutoRepaint.HasValue) c.EditorAutoRepaint = _EditorAutoRepaint.Value;
                if (_ScreenSafeArea.HasValue) c.ScreenSafeArea = _ScreenSafeArea.Value;
            }
        }

        /// <summary>Снять все оверрайды и вернуться к ассету и дефолтам.</summary>
        public static void ResetOverrides() => Overrides.Clear();

        /// <summary>Сброс на границах Play Mode: оверрайды, кэш платформы, ссылка на ассет.</summary>
        internal static void ResetSession()
        {
            _platformOverride = null;
            _platformValid = false;
            _asset = null;
            _assetLoaded = false;
            Overrides.Clear();
        }

        // ==================================================== короткая запись

        // Читают разрешённое, пишут в оверрайды.

        /// <summary>Рисовать в игровых камерах, в том числе в билде.</summary>
        public static bool DrawInGameView { get => Current.DrawInGameView; set => Overrides.DrawInGameView = value; }

        /// <summary>Рисовать в Scene View.</summary>
        public static bool DrawInSceneView { get => Current.DrawInSceneView; set => Overrides.DrawInSceneView = value; }

        /// <summary>Превью-камеры инспектора, отражения и прочее.</summary>
        public static bool DrawInOtherCameras { get => Current.DrawInOtherCameras; set => Overrides.DrawInOtherCameras = value; }

        /// <summary>Слой геометрии. Учитывается culling mask камеры.</summary>
        public static int Layer { get => Current.Layer; set => Overrides.Layer = value; }

        /// <summary>Rendering layer mask, с которым уходит геометрия.</summary>
        public static uint RenderingLayerMask { get => Current.RenderingLayerMask; set => Overrides.RenderingLayerMask = value; }

        /// <summary>Множитель альфы для всей отрисовки.</summary>
        public static float GlobalAlpha { get => Current.GlobalAlpha; set => Overrides.GlobalAlpha = value; }

        /// <summary>Толщина линии в пикселях, с которой стартует Gizmo.lineWidth и к которой возвращает Gizmo.Reset().</summary>
        public static float DefaultLineWidth { get => Current.DefaultLineWidth; set => Overrides.DefaultLineWidth = value; }

        /// <summary>Сдвиг глубины к камере (в единицах NDC) для линий с тестом глубины.</summary>
        public static float DepthBias { get => Current.DepthBias; set => Overrides.DepthBias = value; }

        /// <summary>
        /// Отступ угловых надписей DrawScreenText от краёв экрана, в пикселях.
        /// Считается до края чернил, а не до якоря строки.
        /// </summary>
        public static float ScreenSafeArea { get => Current.ScreenSafeArea; set => Overrides.ScreenSafeArea = value; }

        /// <summary>Потолок роста вершинного буфера на канал. 0 — без ограничения.</summary>
        public static int MaxVerticesPerChannel { get => Current.MaxVerticesPerChannel; set => Overrides.MaxVerticesPerChannel = value; }

        /// <summary>Сегментов в окружности.</summary>
        public static int CircleSegments { get => Current.CircleSegments; set => Overrides.CircleSegments = value; }

        /// <summary>Колец в сплошной сфере.</summary>
        public static int SphereRings { get => Current.SphereRings; set => Overrides.SphereRings = value; }

        /// <summary>Сегментов в кольце сплошной сферы.</summary>
        public static int SphereSegments { get => Current.SphereSegments; set => Overrides.SphereSegments = value; }

        /// <summary>Edit Mode: сколько секунд держать последний снимок геометрии без новых команд.</summary>
        public static float EditorStaleTimeout { get => Current.EditorStaleTimeout; set => Overrides.EditorStaleTimeout = value; }

        /// <summary>Edit Mode: запрашивать перерисовку Scene View при появлении новой геометрии.</summary>
        public static bool EditorAutoRepaint { get => Current.EditorAutoRepaint; set => Overrides.EditorAutoRepaint = value; }

    }
}
