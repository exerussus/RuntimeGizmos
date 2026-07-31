using System;
using UnityEngine;

namespace RuntimeGizmos
{
    /// <summary>
    /// Настройки отрисовки. Значение каждого поля разрешается по слоям, сверху вниз:
    ///
    ///   1. рантайм-оверрайд из кода — GizmoSettings.X = v (снять: GizmoSettings.Overrides.X = null);
    ///   2. ассет GizmoSettingsAsset из любой папки Resources — сначала его платформенная секция,
    ///      затем общая;
    ///   3. дефолт под текущую платформу из GizmoConfig.DefaultsFor.
    ///
    /// Платформенное измерение есть только у данных (дефолты и ассет). Коду оно не нужно:
    /// код и так исполняется уже на целевой платформе, поэтому «оверрайд только для Android» —
    /// это обычный if по GizmoSettings.Platform.
    ///
    /// Оверрайды живут ровно одну сессию: при входе в Play Mode они сбрасываются, даже если
    /// Domain Reload выключен и статика физически пережила переход.
    /// </summary>
    public static class GizmoSettings
    {
        // ==================================================== разрешённая конфигурация

        static GizmoConfig _resolved;
        static bool _valid;

        /// <summary>Готовый снимок настроек со всеми применёнными слоями.</summary>
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

        /// <summary>
        /// Заставить систему считать себя другой платформой. Нужно ровно для одного:
        /// посмотреть в редакторе, как гизмо будут выглядеть в мобильном или веб-билде.
        /// null — определять автоматически.
        /// </summary>
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

        /// <summary>Имя ассета настроек. Ищется в любой папке Resources проекта.</summary>
        public const string AssetResourceName = "RuntimeGizmosSettings";

        static GizmoSettingsAsset _asset;
        static bool _assetLoaded;

        /// <summary>Ассет настроек, если он есть в проекте. Иначе null — тогда работают дефолты.</summary>
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

        /// <summary>
        /// Слой рантайм-оверрайдов. null в любом поле означает «не трогали» — тогда значение
        /// придёт из ассета или из платформенного дефолта.
        /// </summary>
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
            }
        }

        /// <summary>Снять все оверрайды и вернуться к ассету и дефолтам.</summary>
        public static void ResetOverrides() => Overrides.Clear();

        /// <summary>
        /// Полный сброс статики: оверрайды, кэш платформы, ссылка на ассет.
        /// Дёргается при входе в Play Mode и при выходе из него, чтобы состояние не протекало
        /// между сессиями при выключенном Domain Reload.
        /// </summary>
        internal static void ResetSession()
        {
            _platformOverride = null;
            _platformValid = false;
            _asset = null;
            _assetLoaded = false;
            Overrides.Clear();
        }

        // ==================================================== короткая запись

        // Читают разрешённое значение, пишут в слой оверрайдов.
        // GizmoSettings.X = v — то же самое, что GizmoSettings.Overrides.X = v.

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
