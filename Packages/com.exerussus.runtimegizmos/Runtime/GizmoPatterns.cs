using System.Collections.Generic;
using UnityEngine;
using RuntimeGizmos.Internal;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    /// <summary>Готовые сборки поверх примитивов.</summary>
    public static partial class Gizmo
    {
        // ================================================================= объём

        /// <summary>
        /// Углы в полную силу, контур и три сечения приглушены. Так десяток объёмов
        /// в кадре не превращается в кашу из рёбер.
        /// </summary>
        /// <param name="cornerFraction">Доля ребра под уголок, 0.02..0.5.</param>
        /// <param name="faint">Множитель альфы контура и сечений.</param>
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

        /// <summary>Габариты со всеми рендерерами в иерархии.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVolume(Transform t, float cornerFraction = 0.22f, float faint = 0.28f)
        {
            if (t == null) return;
            DrawVolume(WorldBounds(t), cornerFraction, faint);
        }

        // Плоскость задаётся нулевой компонентой полуразмера.
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

        /// <summary>Габариты со всеми рендерерами. Без них — куб 0.25 в точке трансформа.</summary>
        static readonly List<Renderer> _rendererBuffer = new List<Renderer>(32);

        public static Bounds WorldBounds(Transform t)
        {
            if (t == null) return new Bounds(Vector3.zero, Vector3.zero);

            // Перегрузка со списком: GetComponentsInChildren<Renderer>() отдаёт новый массив.
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
        /// Объёмы обоих, линия между ними по границам габаритов, шеврон направления.
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

                // Обрезка по габаритам, чтобы линия не ныряла внутрь.
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

        // Опорная функция ящика вдоль направления.
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
        /// Подпись над габаритами. worldHeight = 0 — пиксели, больше — юниты.
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

        /// <summary>Ломаная с узлами и шевронами направления.</summary>
        /// <param name="nodeSize">Маркер узла. 0 — не рисовать.</param>
        /// <param name="arrowEvery">Шеврон каждые N сегментов. 0 — не ставить.</param>
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

        /// <summary>Вектор стрелкой.</summary>
        /// <param name="label">null — без подписи, "" — подписать длиной.</param>
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
        /// Окружность на земле, при height &gt; 0 — вторая сверху и четыре стойки.
        /// Читается лучше сферы.
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

        /// <summary>Две кромки и дуга между ними, в горизонтальной плоскости.</summary>
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

        // ================================================================= полоса

        /// <summary>
        /// Полоса заполнения, развёрнутая к камере: здоровье, откат, прогресс.
        /// Размеры в пикселях, поэтому читается одинаково на любой дистанции.
        /// </summary>
        /// <param name="t">Заполнение 0..1.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBar(Vector3 position, float t, float widthPixels = 48f,
                                   float heightPixels = 6f, float pixelOffsetY = 0f)
        {
            var c = color;
            var back = new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, c.a * 0.8f);
            GizmoRenderer.Bar(Xf(position), t, widthPixels, heightPixels, c, back,
                              new Vector2(0f, pixelOffsetY), 0);
        }

        /// <summary>Полоса с явными цветами заливки и фона.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBar(Vector3 position, float t, Color fill, Color back,
                                   float widthPixels = 48f, float heightPixels = 6f, float pixelOffsetY = 0f)
            => GizmoRenderer.Bar(Xf(position), t, widthPixels, heightPixels, fill, back,
                                 new Vector2(0f, pixelOffsetY), 0);

        /// <summary>Полоса размером в мировых единицах — уменьшается с расстоянием.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBarWorld(Vector3 position, float t, float width = 0.6f,
                                        float height = 0.08f, float offsetY = 0f)
        {
            var c = color;
            var back = new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, c.a * 0.8f);
            GizmoRenderer.Bar(Xf(position), t, width, height, c, back, new Vector2(0f, offsetY), 1);
        }

        // ================================================================= кривые и сетки

        /// <summary>
        /// Баллистическая траектория. Гравитация по умолчанию земная, чтобы паттерн
        /// не тянул модуль физики.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTrajectory(Vector3 origin, Vector3 velocity, float time = 3f,
                                          int steps = 24, Vector3 gravity = default)
        {
            if (time <= 0f) return;
            if (steps < 2) steps = 2;
            if (gravity == default) gravity = new Vector3(0f, -9.81f, 0f);

            var prev = origin;
            for (int i = 1; i <= steps; i++)
            {
                float t = time * i / steps;
                var p = origin + velocity * t + gravity * (0.5f * t * t);
                DrawLine(prev, p);
                prev = p;
            }
        }

        /// <summary>Кубическая кривая Безье по четырём точкам.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int segments = 24)
        {
            if (segments < 1) segments = 1;

            var prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments, u = 1f - t;
                var p = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                DrawLine(prev, p);
                prev = p;
            }
        }

        /// <summary>Плоская сетка в плоскости XZ повёрнутой системы.</summary>
        /// <param name="cell">Размер ячейки.</param>
        /// <param name="count">Число ячеек по X и по Z.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawGrid(Vector3 center, Quaternion rotation, Vector2 cell, Vector2Int count)
        {
            if (cell.x <= 0f || cell.y <= 0f || count.x < 1 || count.y < 1) return;

            float hx = cell.x * count.x * 0.5f;
            float hz = cell.y * count.y * 0.5f;

            for (int i = 0; i <= count.x; i++)
            {
                float x = -hx + i * cell.x;
                DrawLine(center + rotation * new Vector3(x, 0f, -hz),
                         center + rotation * new Vector3(x, 0f, hz));
            }
            for (int j = 0; j <= count.y; j++)
            {
                float z = -hz + j * cell.y;
                DrawLine(center + rotation * new Vector3(-hx, 0f, z),
                         center + rotation * new Vector3(hx, 0f, z));
            }
        }

        // ================================================================= размеры

        /// <summary>Двухсторонняя стрелка от точки к точке.</summary>
        /// <param name="label">"" — подставить расстояние, null — без подписи.</param>
        /// <param name="arrowSize">0 — подобрать от длины замера.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMeasure(Vector3 a, Vector3 b, string label = "", float arrowSize = 0f)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) { DrawPoint(a, 0.05f); return; }
            d /= len;

            // Плоскость не задана — любая перпендикулярная ось.
            var up = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            var side = Vector3.Cross(d, up).normalized;

            MeasureLine(a, b, d, side, len, label, arrowSize, Vector3.zero);
        }

        /// <summary>Чертёжный вынос: выносные линии, размерная со стрелками, подпись.</summary>
        /// <param name="extensionDir">Направление выносных линий.</param>
        /// <param name="extensionLength">От измеряемых точек до размерной линии.</param>
        /// <param name="label">"" — подставить расстояние, null — без подписи.</param>
        /// <param name="gap">Отступ выносной линии от точки.</param>
        /// <param name="overshoot">Вылет за размерную линию. Меньше 0 — 12% от выноса.
        /// На него же отодвигается подпись.</param>
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

            DrawLine(a + e * gap, a + e * (extensionLength + overshoot));
            DrawLine(b + e * gap, b + e * (extensionLength + overshoot));

            // Щёки стрелок разводятся вдоль выноса — чтобы лежали в плоскости чертежа.
            var da = a + e * extensionLength;
            var db = b + e * extensionLength;
            MeasureLine(da, db, d, e, len, label, arrowSize, -e * Mathf.Max(overshoot, len * 0.04f));
        }

        /// <summary>Замер точкой, осью и шириной.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawDimension(Vector3 from, Vector3 axis, float width,
                                         Vector3 extensionDir, float extensionLength,
                                         string label = "", float gap = 0f, float overshoot = -1f)
        {
            if (axis.sqrMagnitude < 1e-8f) return;
            DrawDimension(from, from + axis.normalized * width, extensionDir, extensionLength,
                          label, gap, overshoot);
        }

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

        // Остриё в tip, щёки назад против outward, разведены вдоль side.
        static void Barbs(Vector3 tip, Vector3 outward, Vector3 side, float size)
        {
            var back = tip - outward * size;
            var half = side * (size * 0.35f);
            DrawLine(tip, back + half);
            DrawLine(tip, back - half);
        }

        // ================================================================= попадание

        /// <summary>Крестик в плоскости поверхности, диск и стрелка нормали.</summary>
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
