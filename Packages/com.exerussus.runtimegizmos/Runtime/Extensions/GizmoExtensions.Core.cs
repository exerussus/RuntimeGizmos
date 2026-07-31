using System.Collections.Generic;
using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos.Extensions
{
    /// <summary>
    /// Расширения для типов из UnityEngine.CoreModule.
    ///
    /// Живут в отдельном namespace специально: без <c>using RuntimeGizmos.Extensions;</c>
    /// они не всплывают в автодополнении у каждого Transform в проекте.
    ///
    /// Все методы возвращают void — только так <c>[Conditional]</c> вырезает вызов из
    /// релизного билда вместе с вычислением аргументов. Fluent-цепочка выглядела бы
    /// красивее, но вырезаться перестала бы.
    /// </summary>
    public static class GizmoTransformExtensions
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        // ============================================================ Transform

        /// <summary>
        /// Габариты объекта со всеми рендерерами в иерархии: углы в полную силу,
        /// контур и сечения приглушены. Основной способ показать «вот этот объект».
        /// </summary>
        /// <param name="t">Объект. null игнорируется.</param>
        /// <param name="c">Цвет. Восстанавливается после вызова.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Transform t, Color c)
        {
            if (t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawVolume(t);
        }

        /// <summary>Габариты текущим цветом <see cref="Gizmo.color"/>.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Transform t)
        {
            if (t != null) Gizmo.DrawVolume(t);
        }

        /// <summary>
        /// Подпись над объектом, поднятая над его габаритами.
        /// </summary>
        /// <param name="text">Текст. Пустой или null игнорируется.</param>
        /// <param name="worldHeight">
        /// 0 — размер в пикселях, метка одинакова на любой дистанции.
        /// Больше нуля — высота буквы в юнитах, метка уменьшается с расстоянием.
        /// </param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLabel(this Transform t, string text, float worldHeight = 0f)
        {
            if (t != null) Gizmo.DrawLabel(t, text, worldHeight);
        }

        /// <summary>
        /// Связь с другим объектом: у обоих показан объём, между ними линия, обрезанная
        /// по границам габаритов, на середине шеврон направления this → other.
        /// </summary>
        /// <param name="label">
        /// Подпись на середине связи. null — без подписи. Пустая строка тоже даёт
        /// пустую подпись: у связи нет числа, которое имело бы смысл подставить,
        /// в отличие от замеров.
        /// </param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLinkTo(this Transform t, Transform other, Color c,
                                      float width = 2f, string label = null)
        {
            if (t != null && other != null) Gizmo.DrawLink(t, other, c, width, label);
        }

        /// <summary>Локальные оси объекта: X красная, Y зелёная, Z синяя.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAxes(this Transform t, float size = 1f)
        {
            if (t != null) Gizmo.DrawAxes(t.position, t.rotation, size);
        }

        /// <summary>
        /// Только направление «вперёд», стрелкой. Когда все три оси — визуальный шум,
        /// а нужно понять, куда смотрит объект.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawForward(this Transform t, float length = 1f)
        {
            if (t != null) Gizmo.DrawArrow(t.position, t.position + t.forward * length);
        }

        /// <summary>
        /// Простой каркасный ящик по габаритам, без приглушённых сечений.
        /// Когда объект один и приглушать нечего.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Transform t, Color c)
        {
            if (t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(Gizmo.WorldBounds(t));
        }

        /// <summary>
        /// Дерево иерархии: линии от каждого узла к его детям плюс точка в каждом узле.
        /// Отладка ригов, процедурного спавна, сборки префабов в рантайме.
        /// </summary>
        /// <param name="maxDepth">Сколько уровней вглубь. 0 — только прямые дети.</param>
        /// <param name="nodeSize">Размер точки в узле. 0 — не рисовать точки.</param>
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

        /// <summary>Габариты в приглушённом стиле: яркие углы, приглушённый контур.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(this Bounds b, Color c)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawVolume(b);
        }

        // ============================================================ коллекции

        /// <summary>
        /// Маршрут по точкам: ломаная, маркеры узлов, шевроны направления.
        /// </summary>
        /// <param name="nodeSize">Размер маркера узла. 0 — не рисовать.</param>
        /// <param name="arrowEvery">Шеврон каждые N сегментов. 0 — не рисовать.</param>
        /// <param name="looped">Замкнуть последнюю точку с первой.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawPath(this IReadOnlyList<Vector3> points, Color c,
                                    float nodeSize = 0.08f, int arrowEvery = 1, bool looped = false)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawPath(points, nodeSize, arrowEvery, looped);
        }

        /// <summary>Габариты сразу у всей коллекции объектов.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolumes(this IReadOnlyList<Transform> items, Color c)
        {
            if (items == null) return;

            using (Gizmo.Scope(c))
                // Индексом, а не foreach: перебор через интерфейс боксирует перечислитель.
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null) Gizmo.DrawVolume(items[i]);
        }

        // ============================================================ меши и рендереры

        /// <summary>
        /// Нормали меша — отрезки из каждой вершины. Отладка импорта ассетов, шейдинга,
        /// вывернутых наизнанку полигонов.
        ///
        /// Мешу нужна галка Read/Write Enabled. Вершины и нормали читаются в
        /// переиспользуемые списки, поэтому мусора вызов не создаёт.
        /// </summary>
        /// <param name="t">Трансформ, в котором меш находится.</param>
        /// <param name="length">Длина отрезка нормали в юнитах.</param>
        /// <param name="step">Брать каждую N-ю вершину. Для тяжёлых мешей ставьте больше 1.</param>
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

        /// <summary>
        /// Каркас меша в трансформе объекта. Каркасная версия строится один раз и кэшируется,
        /// но мешу нужна галка Read/Write Enabled.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWire(this Mesh mesh, Transform t, Color c)
        {
            if (mesh == null || t == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawWireMesh(mesh, t.position, t.rotation, t.lossyScale);
        }

        /// <summary>
        /// Мировой AABB рендерера — ровно тот ящик, по которому его куллит камера.
        /// Полезно, когда объект пропадает с экрана раньше, чем ожидалось.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Renderer r, Color c)
        {
            if (r == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(r.bounds);
        }

        // ============================================================ камера, свет, UI

        /// <summary>Пирамида видимости камеры по её текущим настройкам.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawFrustum(this Camera cam, Color c)
        {
            if (cam == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawFrustum(cam);
        }

        /// <summary>
        /// Зона действия источника света: точечный — сфера радиуса range, прожектор —
        /// конус по spotAngle, направленный — стрелка вдоль forward. Остальные типы
        /// рисуются габаритной сферой.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRange(this Light light, Color c)
        {
            if (light == null) return;
            var t = light.transform;

            using (Gizmo.Scope(c))
                switch (light.type)
                {
                    case LightType.Spot:
                        // DrawWireCone принимает ПОЛОВИНУ угла раствора, а spotAngle — полный.
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

        /// <summary>
        /// Четыре мировых угла RectTransform. Отладка UI, который «не там, где нарисован»:
        /// вылез за Canvas, схлопнулся в ноль, уехал по anchors.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWorldCorners(this RectTransform rt, Color c)
        {
            if (rt == null) return;

            // Массив переиспользуется: GetWorldCorners пишет в переданный, а не создаёт свой.
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
