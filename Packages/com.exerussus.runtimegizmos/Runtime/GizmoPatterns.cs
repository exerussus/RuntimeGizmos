using System.Collections.Generic;
using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    /// <summary>
    /// Готовые паттерны поверх базовых примитивов — то, что в проектах приходится
    /// собирать заново в каждом дебажном скрипте.
    ///
    /// Все они вырезаются из релиза так же, как обычные Draw*, и уважают текущие
    /// color / matrix / depthTest / duration.
    /// </summary>
    public static partial class Gizmo
    {
        // ================================================================= объём

        /// <summary>
        /// Габариты объекта, читаемые с одного взгляда и не забивающие кадр.
        ///
        /// Углы рисуются в полную силу — именно они читают размер, — а полный контур
        /// и три сечения через центр приглушаются. Получается ощущение объёма без
        /// каши из двенадцати рёбер, которую даёт обычный wire-куб.
        /// </summary>
        /// <param name="cornerFraction">Какую долю ребра занимает уголок, 0.02..0.5.</param>
        /// <param name="faint">Множитель альфы для контура и сечений.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(Vector3 center, Quaternion rotation, Vector3 size,
                                      float cornerFraction = 0.22f, float faint = 0.28f)
        {
            var e = size * 0.5f;
            if (e.x <= 0f && e.y <= 0f && e.z <= 0f) return;

            var full = color;
            var soft = new Color(full.r, full.g, full.b, full.a * Mathf.Clamp01(faint));

            color = soft;
            DrawWireCube(center, rotation, size);
            PlaneRect(center, rotation, new Vector3(e.x, e.y, 0f));
            PlaneRect(center, rotation, new Vector3(e.x, 0f, e.z));
            PlaneRect(center, rotation, new Vector3(0f, e.y, e.z));

            color = full;
            float f = Mathf.Clamp(cornerFraction, 0.02f, 0.5f);
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;

                var c = center + rotation * new Vector3(e.x * sx, e.y * sy, e.z * sz);
                DrawLine(c, c - rotation * new Vector3(e.x * sx * 2f * f, 0f, 0f));
                DrawLine(c, c - rotation * new Vector3(0f, e.y * sy * 2f * f, 0f));
                DrawLine(c, c - rotation * new Vector3(0f, 0f, e.z * sz * 2f * f));
            }
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(Bounds worldBounds, float cornerFraction = 0.22f, float faint = 0.28f)
            => DrawVolume(worldBounds.center, Quaternion.identity, worldBounds.size, cornerFraction, faint);

        /// <summary>Габариты объекта вместе со всеми его рендерерами.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(Transform t, float cornerFraction = 0.22f, float faint = 0.28f)
        {
            if (t == null) return;
            DrawVolume(WorldBounds(t), cornerFraction, faint);
        }

        // Прямоугольник в плоскости, где одна компонента полуразмера равна нулю.
        static void PlaneRect(Vector3 c, Quaternion r, Vector3 e)
        {
            Vector3 a, b;
            if (e.z == 0f) { a = new Vector3(e.x, 0f, 0f); b = new Vector3(0f, e.y, 0f); }
            else if (e.y == 0f) { a = new Vector3(e.x, 0f, 0f); b = new Vector3(0f, 0f, e.z); }
            else { a = new Vector3(0f, e.y, 0f); b = new Vector3(0f, 0f, e.z); }

            var p0 = c + r * (-a - b);
            var p1 = c + r * (a - b);
            var p2 = c + r * (a + b);
            var p3 = c + r * (-a + b);
            DrawLine(p0, p1); DrawLine(p1, p2); DrawLine(p2, p3); DrawLine(p3, p0);
        }

        /// <summary>
        /// Габариты объекта со всеми рендерерами в иерархии. Если рендереров нет —
        /// маленький куб в точке трансформа, чтобы объект всё равно было видно.
        /// </summary>
        static readonly List<Renderer> _rendererBuffer = new List<Renderer>(32);

        public static Bounds WorldBounds(Transform t)
        {
            if (t == null) return new Bounds(Vector3.zero, Vector3.zero);

            // Перегрузка со списком, а не GetComponentsInChildren<Renderer>() — та отдаёт
            // НОВЫЙ массив на каждый вызов, то есть мусор каждый кадр. Список переиспользуется.
            _rendererBuffer.Clear();
            t.GetComponentsInChildren(_rendererBuffer);

            bool any = false;
            var b = new Bounds(t.position, Vector3.zero);

            for (int i = 0; i < _rendererBuffer.Count; i++)
            {
                var r = _rendererBuffer[i];
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }

            _rendererBuffer.Clear();

            if (!any) b = new Bounds(t.position, Vector3.one * 0.25f);
            return b;
        }

        // ================================================================= связь

        /// <summary>
        /// Связь между двумя объектами: у каждого показывается объём, между ними идёт
        /// линия, обрезанная по границам габаритов, а на середине стоит шеврон,
        /// показывающий направление from → to.
        ///
        /// Основной паттерн для отладки ссылок: цель у ИИ, владелец у предмета,
        /// родитель в графе, кто на кого агрится.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLink(Transform from, Transform to, Color linkColor,
                                    float width = 2f, string label = null)
        {
            if (from == null || to == null) return;
            DrawLink(WorldBounds(from), WorldBounds(to), linkColor, width, label);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLink(Bounds from, Bounds to, Color linkColor,
                                    float width = 2f, string label = null)
        {
            var prevColor = color;
            float prevWidth = lineWidth;

            color = linkColor;
            lineWidth = width;

            DrawVolume(from);
            DrawVolume(to);

            var d = to.center - from.center;
            float dist = d.magnitude;
            if (dist > 1e-4f)
            {
                d /= dist;

                // Обрезаем линию по габаритам, чтобы она не ныряла внутрь объектов.
                float start = SupportAlong(from.extents, d);
                float end = dist - SupportAlong(to.extents, d);

                if (end > start + 1e-3f)
                {
                    var p0 = from.center + d * start;
                    var p1 = from.center + d * end;
                    DrawLine(p0, p1);

                    var mid = (p0 + p1) * 0.5f;
                    Chevron(mid, d, Mathf.Min(0.3f, (end - start) * 0.3f));

                    if (!string.IsNullOrEmpty(label))
                        DrawText(label, mid, 13f, new Vector2(0f, 16f));
                }
            }

            color = prevColor;
            lineWidth = prevWidth;
        }

        // Насколько далеко габаритный ящик простирается вдоль направления.
        static float SupportAlong(Vector3 extents, Vector3 dir) =>
            Mathf.Abs(dir.x) * extents.x + Mathf.Abs(dir.y) * extents.y + Mathf.Abs(dir.z) * extents.z;

        static void Chevron(Vector3 p, Vector3 dir, float size)
        {
            var up = Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right;
            var side = Vector3.Cross(dir, up).normalized * (size * 0.5f);
            var back = -dir * size;
            DrawLine(p, p + back + side);
            DrawLine(p, p + back - side);
        }

        // ================================================================= подпись

        /// <summary>
        /// Подпись над объектом, поднятая над его габаритами. worldHeight = 0 —
        /// размер в пикселях (одинаков на любой дистанции), больше нуля — в юнитах
        /// (уменьшается с расстоянием, как ник над игроком).
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLabel(Transform t, string text, float worldHeight = 0f)
        {
            if (t == null || string.IsNullOrEmpty(text)) return;

            var b = WorldBounds(t);
            var top = new Vector3(b.center.x, b.max.y, b.center.z);

            if (worldHeight > 0f) DrawTextWorld(text, top + Vector3.up * worldHeight * 0.8f, worldHeight);
            else DrawText(text, top, 14f, new Vector2(0f, 16f));
        }

        // ================================================================= путь

        /// <summary>
        /// Маршрут по точкам: ломаная, узлы и шевроны направления. Для патрульных
        /// путей, найденной навигации, записанной траектории.
        /// </summary>
        /// <param name="nodeSize">Размер маркера узла. 0 — не рисовать.</param>
        /// <param name="arrowEvery">Ставить шеврон каждые N сегментов. 0 — не ставить.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawPath(IReadOnlyList<Vector3> points, float nodeSize = 0.08f,
                                    int arrowEvery = 1, bool looped = false)
        {
            if (points == null || points.Count < 2) return;

            int last = looped ? points.Count : points.Count - 1;
            for (int i = 0; i < last; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                DrawLine(a, b);

                if (arrowEvery > 0 && i % arrowEvery == 0)
                {
                    var d = b - a;
                    float len = d.magnitude;
                    if (len > 1e-4f) Chevron((a + b) * 0.5f, d / len, Mathf.Min(0.25f, len * 0.3f));
                }
            }

            if (nodeSize > 0f)
                for (int i = 0; i < points.Count; i++) DrawPoint(points[i], nodeSize);
        }

        // ================================================================= вектор

        /// <summary>
        /// Вектор из точки со стрелкой и, если нужно, подписью. Скорость, сила,
        /// нормаль, направление взгляда.
        /// </summary>
        /// <param name="label">null — без подписи, "" — подписать длиной вектора.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVector(Vector3 origin, Vector3 vector, float scale = 1f, string label = null)
        {
            float len = vector.magnitude;
            if (len < 1e-5f)
            {
                DrawPoint(origin, 0.05f);
                return;
            }

            var tip = origin + vector * scale;
            DrawArrow(origin, tip);

            if (label == null) return;
            DrawText(label.Length == 0 ? len.ToString("0.##") : label, tip, 12f, new Vector2(0f, 14f));
        }

        // ================================================================= радиус

        /// <summary>
        /// Игровой радиус на земле: окружность в горизонтальной плоскости плюс,
        /// если задана высота, вторая окружность и вертикальные стойки. Для радиуса
        /// атаки, агро, подбора — читается лучше, чем сфера.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRange(Vector3 center, float radius, float height = 0f)
        {
            if (radius <= 0f) return;

            DrawWireDisc(center, Vector3.up, radius);
            if (height <= 0f) return;

            var top = center + Vector3.up * height;
            DrawWireDisc(top, Vector3.up, radius);

            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                DrawLine(center + o, top + o);
            }
        }

        // ================================================================= обзор

        /// <summary>
        /// Сектор обзора в горизонтальной плоскости: две кромки и дуга между ними.
        /// Для зоны видимости ИИ, конуса атаки, области срабатывания.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawFieldOfView(Vector3 origin, Vector3 forward, float angleDeg, float distance)
        {
            if (distance <= 0f) return;

            var flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-8f) flat = Vector3.forward;
            flat.Normalize();

            angleDeg = Mathf.Clamp(angleDeg, 0f, 360f);
            var left = Quaternion.AngleAxis(-angleDeg * 0.5f, Vector3.up) * flat;
            var right = Quaternion.AngleAxis(angleDeg * 0.5f, Vector3.up) * flat;

            DrawLine(origin, origin + left * distance);
            DrawLine(origin, origin + right * distance);
            DrawWireArc(origin, Vector3.up, left, angleDeg, distance);
        }

        // ================================================================= размеры

        /// <summary>
        /// Двухсторонняя стрелка от точки к точке — простейший замер.
        /// </summary>
        /// <param name="label">
        /// Пустая строка — подставить расстояние, null — без подписи, иначе свой текст.
        /// </param>
        /// <param name="arrowSize">Длина стрелки в юнитах. 0 — подобрать от длины замера.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMeasure(Vector3 a, Vector3 b, string label = "", float arrowSize = 0f)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) { DrawPoint(a, 0.05f); return; }
            d /= len;

            // Плоскость чертежа не задана — берём любую перпендикулярную ось.
            var up = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            var side = Vector3.Cross(d, up).normalized;

            MeasureLine(a, b, d, side, len, label, arrowSize, Vector3.zero);
        }

        /// <summary>
        /// Вынос размера, как на чертеже: от двух измеряемых точек уходят выносные линии
        /// в заданном направлении, между их концами идёт размерная линия со стрелками
        /// на обоих концах и подписью.
        /// </summary>
        /// <param name="a">Первая измеряемая точка.</param>
        /// <param name="b">Вторая измеряемая точка.</param>
        /// <param name="extensionDir">Куда отводить размер — направление выносных линий.</param>
        /// <param name="extensionLength">Расстояние от измеряемых точек до размерной линии.</param>
        /// <param name="label">Пустая строка — подставить расстояние, null — без подписи.</param>
        /// <param name="gap">Отступ выносной линии от самой измеряемой точки.</param>
        /// <param name="overshoot">
        /// Насколько выносная линия выходит за размерную. Отрицательное — взять 12%
        /// от длины выноса. На этот же отступ отодвигается подпись.
        /// </param>
        /// <param name="arrowSize">Длина стрелки в юнитах. 0 — подобрать от ширины замера.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawDimension(Vector3 a, Vector3 b, Vector3 extensionDir, float extensionLength,
                                         string label = "", float gap = 0f, float overshoot = -1f,
                                         float arrowSize = 0f)
        {
            var e = extensionDir;
            if (e.sqrMagnitude < 1e-8f) return;
            e = e.normalized;

            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            d /= len;

            if (overshoot < 0f) overshoot = Mathf.Abs(extensionLength) * 0.12f;

            // Выносные линии: от точки (с отступом) до чуть дальше размерной линии.
            DrawLine(a + e * gap, a + e * (extensionLength + overshoot));
            DrawLine(b + e * gap, b + e * (extensionLength + overshoot));

            // Размерная линия. Стрелки лежат в плоскости чертежа — их «щёки»
            // разводятся вдоль выносного направления, а не по случайной оси.
            var da = a + e * extensionLength;
            var db = b + e * extensionLength;
            MeasureLine(da, db, d, e, len, label, arrowSize, -e * Mathf.Max(overshoot, len * 0.04f));
        }

        /// <summary>
        /// То же самое, но замер задан точкой, осью и шириной — когда ширина известна
        /// заранее, а вторую точку считать не хочется.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawDimension(Vector3 from, Vector3 axis, float width,
                                         Vector3 extensionDir, float extensionLength,
                                         string label = "", float gap = 0f, float overshoot = -1f)
        {
            if (axis.sqrMagnitude < 1e-8f) return;
            DrawDimension(from, from + axis.normalized * width, extensionDir, extensionLength,
                          label, gap, overshoot);
        }

        // Размерная линия со стрелками наружу на обоих концах и подписью посередине.
        static void MeasureLine(Vector3 a, Vector3 b, Vector3 dir, Vector3 side, float len,
                                string label, float arrowSize, Vector3 labelOffset)
        {
            DrawLine(a, b);

            float s = arrowSize > 0f ? arrowSize : Mathf.Min(len * 0.15f, 0.3f);
            Barbs(a, -dir, side, s);
            Barbs(b, dir, side, s);

            if (label == null) return;
            DrawText(label.Length == 0 ? len.ToString("0.##") : label,
                     (a + b) * 0.5f + labelOffset, 13f);
        }

        // Стрелка: остриё в tip, щёки уходят назад против outward и разводятся вдоль side.
        static void Barbs(Vector3 tip, Vector3 outward, Vector3 side, float size)
        {
            var back = tip - outward * size;
            var half = side * (size * 0.35f);
            DrawLine(tip, back + half);
            DrawLine(tip, back - half);
        }

        // ================================================================= попадание

        /// <summary>
        /// Точка попадания: крестик в точке и нормаль от неё. Для отладки рейкастов,
        /// оверлапов, точек контакта.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawHit(Vector3 point, Vector3 normal, float size = 0.15f)
        {
            var n = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;

            var up = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            var a = Vector3.Cross(n, up).normalized * size;
            var b = Vector3.Cross(n, a).normalized * size;

            DrawLine(point - a, point + a);
            DrawLine(point - b, point + b);
            DrawWireDisc(point, n, size * 0.7f);
            DrawArrow(point, point + n * size * 3f);
        }
    }
}
