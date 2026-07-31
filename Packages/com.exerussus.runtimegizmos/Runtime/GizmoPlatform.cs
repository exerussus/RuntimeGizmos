using UnityEngine;

namespace RuntimeGizmos
{
    /// <summary>
    /// Класс платформы, а не конкретная платформа: настройки отрисовки различаются не между
    /// Windows и Linux, а между «экран в метре от лица» и «экран в очках».
    /// </summary>
    public enum GizmoPlatform
    {
        /// <summary>Windows, macOS, Linux, редактор.</summary>
        Desktop = 0,

        /// <summary>Android, iOS.</summary>
        Mobile = 1,

        /// <summary>WebGL.</summary>
        Web = 2,

        /// <summary>Консоли и tvOS — экран далеко.</summary>
        Console = 3,

        /// <summary>Активна XR-сессия. Перекрывает Mobile и Desktop.</summary>
        XR = 4,
    }

    internal static class GizmoPlatformUtil
    {
        internal static GizmoPlatform Detect()
        {
            // XR определяется первым: Quest — это Android, но настройки ему нужны свои.
#if ENABLE_VR
            if (UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive)
                return GizmoPlatform.XR;
#endif

#if UNITY_EDITOR
            // В редакторе ориентируемся на активный build target, а не на то, что редактор
            // крутится на десктопе: тогда Scene View показывает ровно те настройки, которые
            // увидит игрок на целевой платформе, ещё до сборки.
            switch (UnityEditor.EditorUserBuildSettings.activeBuildTarget)
            {
                case UnityEditor.BuildTarget.Android:
                case UnityEditor.BuildTarget.iOS:
                    return GizmoPlatform.Mobile;

                case UnityEditor.BuildTarget.WebGL:
                    return GizmoPlatform.Web;

                case UnityEditor.BuildTarget.tvOS:
                    return GizmoPlatform.Console;

                default:
                    // Консольные значения BuildTarget живут в отдельных модулях и время от
                    // времени переименовываются, поэтому здесь их нет. На самой консоли
                    // платформа определится в рантайме; в редакторе её можно выставить
                    // руками через GizmoSettings.PlatformOverride.
                    return GizmoPlatform.Desktop;
            }
#else
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                case RuntimePlatform.IPhonePlayer:
                    return GizmoPlatform.Mobile;

                case RuntimePlatform.WebGLPlayer:
                    return GizmoPlatform.Web;

                case RuntimePlatform.PS4:
                case RuntimePlatform.PS5:
                case RuntimePlatform.XboxOne:
                case RuntimePlatform.GameCoreXboxOne:
                case RuntimePlatform.GameCoreXboxSeries:
                case RuntimePlatform.Switch:
                case RuntimePlatform.tvOS:
                    return GizmoPlatform.Console;

                default:
                    return GizmoPlatform.Desktop;
            }
#endif
        }
    }
}
