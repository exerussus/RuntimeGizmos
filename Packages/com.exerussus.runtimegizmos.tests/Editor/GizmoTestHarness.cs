using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Общая оснастка тестов. Здесь же собраны все обходные пути, которых в
    /// офлайн-харнессе не было, потому что там Unity API — заглушки.
    /// </summary>
    internal static class GizmoTestHarness
    {
        /// <summary>Приводит систему в заведомо чистое состояние.</summary>
        public static void Boot()
        {
            GizmoRenderer.Dispose();
            GizmoSettings.ResetSession();
            GizmoRenderer.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            GizmoRenderer.Enabled = true;
            Gizmo.Reset();
            GizmoRenderer.Ensure();

            // Без URP шейдеры не компилируются, Ensure аккуратно выключает систему,
            // и каналов просто нет. Это не дефект пакета, а неподходящий проект —
            // поэтому кейс пропускается, а не падает.
            if (Thin == null)
                Assert.Ignore("Рендерер не поднялся: шейдеры не скомпилировались. " +
                              "Пакету нужен URP — назначьте Universal Render Pipeline Asset " +
                              "в Project Settings → Graphics.");
        }

        public static void Shutdown()
        {
            GizmoRenderer.ClearAll();
            GizmoRenderer.Dispose();
            GizmoSettings.ResetSession();
        }

        /// <summary>
        /// Шаг по времени.
        ///
        /// В Unity Time.realtimeSinceStartup только читается, поэтому «перемотать»
        /// часы, как в офлайн-харнессе, нельзя. Но истечение времени жизни считается
        /// по штампу GizmoRenderer.Now, который BeginFrame читает в начале, а в конце
        /// перезаписывает реальным временем. Значит достаточно выставлять штамп прямо
        /// перед каждым шагом — и перед рисованием, и перед границей кадра.
        ///
        ///     At(100f); Gizmo.duration = 5f; Gizmo.DrawLine(a, b);   // истекает в 105
        ///     At(100f); Frame();                                     // жива
        ///     At(110f); Frame();                                     // выбыла
        /// </summary>
        public static void At(float time) => GizmoRenderer.Now = time;

        public static void Frame(bool strict = true) => GizmoRenderer.BeginFrame(strict);

        /// <summary>Камера, которую не видно и которая ничего не рендерит сама.</summary>
        public static Camera MakeCamera(CameraType type = CameraType.Game)
        {
            var go = new GameObject("~GizmoTestCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;                 // рендерим вручную через cam.Render()
            cam.cameraType = type;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            return cam;
        }

        public static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        // ---------------------------------------------------------------- каналы
        //
        // Массивы каналов в рендерере приватные. Внутренние типы нам видны через
        // InternalsVisibleTo, а до приватных статических полей всё равно только
        // рефлексией — как и в офлайн-харнессе.

        static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;

        static T Field<T>(string name) where T : class
            => typeof(GizmoRenderer).GetField(name, Priv)?.GetValue(null) as T;

        public static GizmoChannel<GizmoVertex>[] Thin => Field<GizmoChannel<GizmoVertex>[]>("_thin");
        public static GizmoChannel<GizmoWideVertex>[] Wide => Field<GizmoChannel<GizmoWideVertex>[]>("_wide");
        public static GizmoChannel<GizmoVertex>[] Tri => Field<GizmoChannel<GizmoVertex>[]>("_tri");

        /// <summary>Кадровый (front) буфер канала — он приватный, добираемся рефлексией.</summary>
        public static int FrontCount<T>(GizmoChannel<T> ch) where T : unmanaged
        {
            var f = typeof(GizmoChannel<T>)
                .GetField("_front", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(ch) as GizmoNativeBuffer<T>;
            return f.Count;
        }

        /// <summary>Сумма вершин во всех линейных каналах — признак «что-то нарисовалось».</summary>
        public static int TotalVertices()
        {
            int n = 0;
            for (int z = 0; z < 2; z++)
            {
                n += Thin[z].Back.Count + Thin[z].Retained.Count + FrontCount(Thin[z]);
                n += Wide[z].Back.Count + Wide[z].Retained.Count + FrontCount(Wide[z]);
                n += Tri[z].Back.Count + Tri[z].Retained.Count + FrontCount(Tri[z]);
            }
            return n;
        }

        /// <summary>Сумма байт по разметке вершины — для сверки с sizeof структуры.</summary>
        public static int LayoutSize(UnityEngine.Rendering.VertexAttributeDescriptor[] layout)
        {
            int n = 0;
            foreach (var a in layout)
            {
                int el = a.format switch
                {
                    UnityEngine.Rendering.VertexAttributeFormat.Float32 => 4,
                    UnityEngine.Rendering.VertexAttributeFormat.Float16 => 2,
                    UnityEngine.Rendering.VertexAttributeFormat.UNorm8 => 1,
                    UnityEngine.Rendering.VertexAttributeFormat.SNorm8 => 1,
                    UnityEngine.Rendering.VertexAttributeFormat.UInt8 => 1,
                    UnityEngine.Rendering.VertexAttributeFormat.SInt8 => 1,
                    UnityEngine.Rendering.VertexAttributeFormat.UNorm16 => 2,
                    UnityEngine.Rendering.VertexAttributeFormat.SNorm16 => 2,
                    UnityEngine.Rendering.VertexAttributeFormat.UInt16 => 2,
                    UnityEngine.Rendering.VertexAttributeFormat.SInt16 => 2,
                    UnityEngine.Rendering.VertexAttributeFormat.UInt32 => 4,
                    UnityEngine.Rendering.VertexAttributeFormat.SInt32 => 4,
                    _ => throw new NotSupportedException(a.format.ToString()),
                };
                n += el * a.dimension;
            }
            return n;
        }
    }
}
