using UnityEngine;

namespace RuntimeGizmos
{
    /// <summary>Класс платформы: различие не между Windows и Linux, а между типами экрана.</summary>
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
            // Первым: Quest — это Android, но профиль ему нужен свой.
#if ENABLE_VR
            if (UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive)
                return GizmoPlatform.XR;
#endif

#if UNITY_EDITOR
            // По активному build target, а не по тому, что редактор на десктопе:
            // Scene View сразу показывает настройки целевой платформы.
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
                    // Консольных BuildTarget здесь нет: живут в отдельных модулях и
                    // периодически переименовываются. В рантайме определятся, в редакторе —
                    // через GizmoSettings.PlatformOverride.
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
