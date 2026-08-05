using System.Collections.Generic;
using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos.Extensions
{
    /// <summary>
    /// Расширения для UnityEngine.CoreModule. Отдельный namespace, чтобы без
    /// <c>using RuntimeGizmos.Extensions;</c> не всплывать в автодополнении.
    /// </summary>
    public static class GizmoTransformExtensions
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        // ============================================================ Transform

        /// <summary>Габариты со всеми рендерерами: углы ярко, контур приглушён.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Transform t, Color c)
        {
            if (t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawVolume(t);
        }

        /// <summary>То же текущим цветом.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Transform t)
        {
            if (t != null) Gizmo.DrawVolume(t);
        }

        /// <summary>Подпись над габаритами.</summary>
        /// <param name="worldHeight">0 — пиксели, больше — высота буквы в юнитах.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLabel(this Transform t, string text, float worldHeight = 0f)
        {
            if (t != null) Gizmo.DrawLabel(t, text, worldHeight);
        }

        /// <summary>Объёмы обоих, линия по границам, шеврон направления this → other.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLinkTo(this Transform t, Transform other, Color c,
                                      float width = 2f, string label = null)
        {
            if (t != null && other != null) Gizmo.DrawLink(t, other, c, width, label);
        }

        /// <summary>Локальные оси: X красная, Y зелёная, Z синяя.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAxes(this Transform t, float size = 1f)
        {
            if (t != null) Gizmo.DrawAxes(t.position, t.rotation, size);
        }

        /// <summary>Только направление вперёд, стрелкой.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawForward(this Transform t, float length = 1f)
        {
            if (t != null) Gizmo.DrawArrow(t.position, t.position + t.forward * length);
        }

        /// <summary>Каркасный ящик по габаритам, без приглушённых сечений.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Transform t, Color c)
        {
            if (t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(Gizmo.WorldBounds(t));
        }

        /// <summary>Линии от узла к детям плюс точка в узле.</summary>
        /// <param name="maxDepth">Уровней вглубь. 0 — только прямые дети.</param>
        /// <param name="nodeSize">Точка в узле. 0 — не рисовать.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawHierarchy(this Transform t, Color c, int maxDepth = 8, float nodeSize = 0.03f)
        {
            if (t == null) return;
            using (Gizmo.Scope(c)) Hierarchy(t, maxDepth, nodeSize);
        }

        static void Hierarchy(Transform t, int depth, float nodeSize)
        {
            if (nodeSize > 0f) Gizmo.DrawPoint(t.position, nodeSize);
            if (depth < 0) return;

            int n = t.childCount;
            for (int i = 0; i < n; i++)
            {
                var ch = t.GetChild(i);
                if (ch == null) continue;
                Gizmo.DrawLine(t.position, ch.position);
                Hierarchy(ch, depth - 1, nodeSize);
            }
        }

        // ============================================================ Bounds

        /// <summary>Каркасный ящик по габаритам.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void Draw(this Bounds b, Color c)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(b);
        }

        /// <summary>Яркие углы, приглушённый контур.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Bounds b, Color c)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawVolume(b);
        }

        // ============================================================ коллекции

        /// <summary>Ломаная с узлами и шевронами.</summary>
        /// <param name="nodeSize">Маркер узла. 0 — не рисовать.</param>
        /// <param name="arrowEvery">Шеврон каждые N сегментов. 0 — не рисовать.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawPath(this IReadOnlyList<Vector3> points, Color c,
                                    float nodeSize = 0.08f, int arrowEvery = 1, bool looped = false)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawPath(points, nodeSize, arrowEvery, looped);
        }

        /// <summary>Габариты всей коллекции.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolumes(this IReadOnlyList<Transform> items, Color c)
        {
            if (items == null) return;

            using (Gizmo.Scope(c))
                // Индексом: foreach через интерфейс боксирует перечислитель.
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null) Gizmo.DrawVolume(items[i]);
        }

        // ============================================================ меши и рендереры

        /// <summary>Отрезки нормалей из вершин. Мешу нужна галка Read/Write Enabled.</summary>
        /// <param name="step">Брать каждую N-ю вершину.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawNormals(this Mesh mesh, Transform t, Color c,
                                       float length = 0.1f, int step = 1)
        {
            if (mesh == null || t == null) return;
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[RuntimeGizmos] У меша '{mesh.name}' нет Read/Write Enabled — " +
                                 "нормали прочитать нельзя.");
                return;
            }

            _verts.Clear();
            _norms.Clear();
            mesh.GetVertices(_verts);
            mesh.GetNormals(_norms);

            int n = Mathf.Min(_verts.Count, _norms.Count);
            if (step < 1) step = 1;

            using (Gizmo.Scope(c))
                for (int i = 0; i < n; i += step)
                {
                    var p = t.TransformPoint(_verts[i]);
                    Gizmo.DrawLine(p, p + t.TransformDirection(_norms[i]) * length);
                }

            _verts.Clear();
            _norms.Clear();
        }

        static readonly List<Vector3> _verts = new List<Vector3>(1024);
        static readonly List<Vector3> _norms = new List<Vector3>(1024);

        /// <summary>Каркас меша. Кэшируется, но мешу нужна галка Read/Write Enabled.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWire(this Mesh mesh, Transform t, Color c)
        {
            if (mesh == null || t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawWireMesh(mesh, t.position, t.rotation, t.lossyScale);
        }

        /// <summary>Мировой AABB — тот ящик, по которому объект куллит камера.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Renderer r, Color c)
        {
            if (r == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(r.bounds);
        }

        // ============================================================ камера, свет, UI

        /// <summary>Пирамида видимости.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawFrustum(this Camera cam, Color c)
        {
            if (cam == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawFrustum(cam);
        }

        /// <summary>Точечный — сфера, прожектор — конус, направленный — стрелка.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRange(this Light light, Color c)
        {
            if (light == null) return;
            var t = light.transform;

            using (Gizmo.Scope(c))
                switch (light.type)
                {
                    case LightType.Spot:
                        // DrawWireCone берёт половину угла, spotAngle — полный.
                        Gizmo.DrawWireCone(t.position, t.forward, light.spotAngle * 0.5f, light.range);
                        break;

                    case LightType.Directional:
                        Gizmo.DrawArrow(t.position, t.position + t.forward * 2f);
                        Gizmo.DrawWireDisc(t.position, t.forward, 0.4f);
                        break;

                    default:
                        Gizmo.DrawWireSphere(t.position, light.range);
                        break;
                }
        }

        /// <summary>Четыре мировых угла — где элемент на самом деле.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWorldCorners(this RectTransform rt, Color c)
        {
            if (rt == null) return;

            rt.GetWorldCorners(_corners);

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawLine(_corners[0], _corners[1]);
                Gizmo.DrawLine(_corners[1], _corners[2]);
                Gizmo.DrawLine(_corners[2], _corners[3]);
                Gizmo.DrawLine(_corners[3], _corners[0]);
                float dot = (_corners[2] - _corners[0]).magnitude * 0.02f;
                for (int i = 0; i < 4; i++) Gizmo.DrawPoint(_corners[i], dot);
            }
        }

        static readonly Vector3[] _corners = new Vector3[4];
    }
}
