// Файл компилируется, только если в проекте включён модуль com.unity.modules.audio: символ RG_AUDIO
// выставляет versionDefines в RuntimeGizmos.asmdef. Раньше предлагалось «модуль выключен —
// удалите файл», но у пакета, поставленного по git URL, удалять нечего: PackageCache
// неизменяем, и выключенный модуль ронял компиляцию всей сборки RuntimeGizmos.
#if RG_AUDIO
using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos.Extensions
{
    /// <summary>Расширения для UnityEngine.AudioModule.</summary>
    public static class GizmoAudioExtensions
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        /// <summary>minDistance и maxDistance двумя сферами.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawDistances(this AudioSource src, Color c)
        {
            if (src == null) return;
            var p = src.transform.position;

            var inner = c;
            var outer = new Color(c.r, c.g, c.b, c.a * 0.35f);

            using (Gizmo.Scope(inner))
            {
                Gizmo.DrawWireSphere(p, src.minDistance);
                Gizmo.DrawNumber("min ", src.minDistance, "0.##", default,
                                 p + Vector3.up * src.minDistance, 12f, Vector2.zero);

                Gizmo.color = outer;
                Gizmo.DrawWireSphere(p, src.maxDistance);
                Gizmo.DrawNumber("max ", src.maxDistance, "0.##", default,
                                 p + Vector3.up * src.maxDistance, 12f, Vector2.zero);
            }
        }
    }
}

#endif // RG_AUDIO
