using System.Collections.Generic;
using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos.Extensions
{
    /// <summary>
    /// Расширения для UnityEngine.Physics2DModule. Отдельный файл: модуль выключен —
    /// удаляется он один.
    ///
    /// Формы строятся точками и рисуются ломаной, а не дугами: так контур гарантированно
    /// замкнут и не зависит от направления отсчёта угла в <see cref="Gizmo.DrawWireArc"/>.
    /// </summary>
    public static class GizmoPhysics2DExtensions
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        const int ArcSegments = 12;

        static readonly List<Vector3> _outline = new List<Vector3>(128);
        static readonly List<Vector2> _path = new List<Vector2>(128);

        // ============================================================ коллайдеры 2D

        /// <summary>
        /// Настоящая форма: Box, Circle, Capsule, Polygon со всеми путями, Edge.
        /// Точки читаются неаллоцирующими GetPath и GetPoints в общие списки.
        /// Неизвестные типы рисуются габаритным ящиком.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawShape(this Collider2D col, Color c)
        {
            if (col == null) return;
            var t = col.transform;

            using (Gizmo.Scope(c))
                switch (col)
                {
                    case BoxCollider2D box:
                        Rect2D(t, box.offset, box.size);
                        break;

                    case CircleCollider2D cir:
                        Gizmo.DrawWireDisc(t.TransformPoint(cir.offset), t.forward,
                                           cir.radius * MaxAxis2(t.lossyScale));
                        break;

                    case CapsuleCollider2D cap:
                        Capsule2D(t, cap);
                        break;

                    case PolygonCollider2D poly:
                        for (int p = 0; p < poly.pathCount; p++)
                        {
                            _path.Clear();
                            poly.GetPath(p, _path);
                            Outline(t, poly.offset, looped: true);
                        }
                        break;

                    case EdgeCollider2D edge:
                        _path.Clear();
                        edge.GetPoints(_path);
                        Outline(t, edge.offset, looped: false);
                        break;

                    default:
                        Gizmo.DrawBounds(col.bounds);
                        break;
                }
        }

        /// <summary>Мировой AABB — им работает broadphase.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Collider2D col, Color c)
        {
            if (col == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(col.bounds);
        }

        static void Rect2D(Transform t, Vector2 offset, Vector2 size)
        {
            Vector2 h = size * 0.5f;
            Gizmo.DrawWireQuad(
                t.TransformPoint(new Vector3(offset.x - h.x, offset.y - h.y, 0f)),
                t.TransformPoint(new Vector3(offset.x + h.x, offset.y - h.y, 0f)),
                t.TransformPoint(new Vector3(offset.x + h.x, offset.y + h.y, 0f)),
                t.TransformPoint(new Vector3(offset.x - h.x, offset.y + h.y, 0f)));
        }

        static void Capsule2D(Transform t, CapsuleCollider2D cap)
        {
            var s = t.lossyScale;
            bool vertical = cap.direction == CapsuleDirection2D.Vertical;

            // Вертикальная: ширина — диаметр, высота — длина. Горизонтальная — наоборот.
            float r = (vertical ? cap.size.x : cap.size.y) * 0.5f
                      * Mathf.Abs(vertical ? s.x : s.y);
            float len = (vertical ? cap.size.y : cap.size.x) * Mathf.Abs(vertical ? s.y : s.x);

            float half = Mathf.Max(len * 0.5f - r, 0f);
            var axis = vertical ? t.up : t.right;
            var side = vertical ? t.right : t.up;

            var mid = t.TransformPoint(cap.offset);
            var a = mid + axis * half;
            var b = mid - axis * half;

            _outline.Clear();

            // Верх: от +side через +axis к -side.
            for (int i = 0; i <= ArcSegments; i++)
            {
                float ang = Mathf.PI * i / ArcSegments;
                _outline.Add(a + side * (Mathf.Cos(ang) * r) + axis * (Mathf.Sin(ang) * r));
            }

            // Низ: от -side через -axis к +side.
            for (int i = 0; i <= ArcSegments; i++)
            {
                float ang = Mathf.PI * i / ArcSegments;
                _outline.Add(b - side * (Mathf.Cos(ang) * r) - axis * (Mathf.Sin(ang) * r));
            }

            Gizmo.DrawPolyLine(_outline, looped: true);
            _outline.Clear();
        }

        static void Outline(Transform t, Vector2 offset, bool looped)
        {
            if (_path.Count < 2) return;

            _outline.Clear();
            for (int i = 0; i < _path.Count; i++)
                _outline.Add(t.TransformPoint(new Vector3(_path[i].x + offset.x,
                                                          _path[i].y + offset.y, 0f)));

            Gizmo.DrawPolyLine(_outline, looped);
            _outline.Clear();
        }

        static float MaxAxis2(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y));

        // ============================================================ Rigidbody2D

        /// <summary>Скорость из центра масс, подписана.</summary>
        /// <param name="scale">Юнитов длины на 1 м/с.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVelocity(this Rigidbody2D rb, Color c, float scale = 0.2f)
        {
            if (rb == null) return;
            using (Gizmo.Scope(c))
                Gizmo.DrawVector(rb.worldCenterOfMass, rb.linearVelocity, scale, "");
        }

        /// <summary>Диск в плоскости сцены и подпись в градусах в секунду.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAngularVelocity(this Rigidbody2D rb, Color c, float scale = 0.005f)
        {
            if (rb == null) return;

            float w = rb.angularVelocity;
            if (Mathf.Abs(w) < 1e-3f) return;

            var p = (Vector3)rb.worldCenterOfMass;
            float radius = Mathf.Abs(w) * scale;

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawWireDisc(p, Vector3.forward, radius);
                // Штрих показывает знак вращения.
                Gizmo.DrawArrow(p, p + Vector3.right * (radius * Mathf.Sign(w)));
                Gizmo.DrawText(w.ToString("0.#") + " deg/s", p, 12f, new Vector2(0f, 14f));
            }
        }

        /// <summary>Центр масс.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawCenterOfMass(this Rigidbody2D rb, Color c, float size = 0.12f)
        {
            if (rb == null) return;
            var p = (Vector3)rb.worldCenterOfMass;

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawPoint(p, size);
                Gizmo.DrawWireDisc(p, Vector3.forward, size * 0.6f);
            }
        }

        // ============================================================ рейкасты 2D

        /// <summary>Точка попадания. Пустой результат пропускается молча.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void Draw(this RaycastHit2D hit, Color c, float size = 0.15f)
        {
            if (hit.collider == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawHit(hit.point, hit.normal, size);
        }
    }

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
                Gizmo.DrawText("min " + src.minDistance.ToString("0.##"),
                               p + Vector3.up * src.minDistance, 12f);

                Gizmo.color = outer;
                Gizmo.DrawWireSphere(p, src.maxDistance);
                Gizmo.DrawText("max " + src.maxDistance.ToString("0.##"),
                               p + Vector3.up * src.maxDistance, 12f);
            }
        }
    }
}
