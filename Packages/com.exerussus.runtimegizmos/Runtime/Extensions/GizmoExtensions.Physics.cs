using UnityEngine;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos.Extensions
{
    /// <summary>
    /// Расширения для UnityEngine.PhysicsModule.
    ///
    /// Файл отдельный намеренно: если в проекте модуль физики выключен, достаточно
    /// удалить именно его, не трогая остальное.
    /// </summary>
    public static class GizmoPhysicsExtensions
    {
        const string EDITOR = "UNITY_EDITOR";
        const string DEV = "DEVELOPMENT_BUILD";
        const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        // ============================================================ коллайдеры

        /// <summary>
        /// Настоящая форма коллайдера, а не габаритный ящик: Box, Sphere, Capsule,
        /// CharacterController и MeshCollider рисуются каждый по-своему, с учётом
        /// center/offset, поворота и масштаба трансформа.
        ///
        /// Правила масштабирования взяты те же, что у самого движка: сфера берёт
        /// наибольшую ось, капсула — наибольшую из двух осей, перпендикулярных её
        /// направлению, а высота никогда не бывает меньше двух радиусов.
        /// Именно на этих правилах чаще всего и путаются, когда рисуют вручную.
        ///
        /// Неизвестные типы (террейн, составные) рисуются габаритным ящиком.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawShape(this Collider col, Color c)
        {
            if (col == null) return;
            var t = col.transform;

            using (Gizmo.Scope(c))
                switch (col)
                {
                    case BoxCollider box:
                        Gizmo.DrawWireCube(t.TransformPoint(box.center), t.rotation,
                                           Scale(box.size, t.lossyScale));
                        break;

                    case SphereCollider sph:
                        Gizmo.DrawWireSphere(t.TransformPoint(sph.center),
                                             sph.radius * MaxAxis(t.lossyScale));
                        break;

                    case CapsuleCollider cap:
                        DrawCapsule(t, cap.center, cap.radius, cap.height, cap.direction);
                        break;

                    case CharacterController cc:
                        // Контроллер всегда вертикальный, ось Y.
                        DrawCapsule(t, cc.center, cc.radius, cc.height, 1);
                        break;

                    case MeshCollider mc when mc.sharedMesh != null:
                        // Каркас кэшируется, но мешу нужна галка Read/Write Enabled.
                        Gizmo.DrawWireMesh(mc.sharedMesh, t.position, t.rotation, t.lossyScale);
                        break;

                    default:
                        Gizmo.DrawBounds(col.bounds);
                        break;
                }
        }

        /// <summary>
        /// Мировой AABB коллайдера — тот ящик, по которому его отбирает broadphase.
        /// Не путать с формой: у повёрнутой капсулы ящик заметно больше самой капсулы.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(this Collider col, Color c)
        {
            if (col == null) return;
            using (Gizmo.Scope(c)) Gizmo.DrawBounds(col.bounds);
        }

        static void DrawCapsule(Transform t, Vector3 center, float radius, float height, int direction)
        {
            var s = t.lossyScale;

            // Ось капсулы: 0 = X, 1 = Y, 2 = Z.
            Vector3 axis = direction == 0 ? Vector3.right : direction == 2 ? Vector3.forward : Vector3.up;

            // Радиус масштабируется наибольшей из ДВУХ поперечных осей, высота — своей.
            float radScale = direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                           : direction == 2 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y))
                           :                  Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float hScale = direction == 0 ? Mathf.Abs(s.x)
                         : direction == 2 ? Mathf.Abs(s.z)
                         :                  Mathf.Abs(s.y);

            float r = radius * radScale;
            float h = Mathf.Max(height * hScale, r * 2f);   // ниже двух радиусов не бывает

            var mid = t.TransformPoint(center);
            var dir = t.rotation * axis;
            float half = h * 0.5f - r;

            Gizmo.DrawWireCapsule(mid + dir * half, mid - dir * half, r);
        }

        static Vector3 Scale(Vector3 a, Vector3 b) =>
            new Vector3(a.x * Mathf.Abs(b.x), a.y * Mathf.Abs(b.y), a.z * Mathf.Abs(b.z));

        static float MaxAxis(Vector3 v) =>
            Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

        // ============================================================ Rigidbody

        /// <summary>
        /// Вектор линейной скорости из центра масс, подписанный величиной в м/с.
        /// </summary>
        /// <param name="scale">Сколько юнитов длины на 1 м/с.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawVelocity(this Rigidbody rb, Color c, float scale = 0.2f)
        {
            if (rb == null) return;
            using (Gizmo.Scope(c))
                Gizmo.DrawVector(rb.worldCenterOfMass, rb.linearVelocity, scale, "");
        }

        /// <summary>
        /// Ось вращения и его величина: стрелка вдоль оси плюс диск, показывающий
        /// плоскость вращения. Подписано в рад/с.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAngularVelocity(this Rigidbody rb, Color c, float scale = 0.2f)
        {
            if (rb == null) return;

            var w = rb.angularVelocity;
            float mag = w.magnitude;
            if (mag < 1e-4f) return;

            var axis = w / mag;
            var p = rb.worldCenterOfMass;

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawArrow(p, p + axis * (mag * scale));
                Gizmo.DrawWireDisc(p, axis, mag * scale * 0.5f);
                Gizmo.DrawText(mag.ToString("0.##") + " rad/s", p + axis * (mag * scale), 12f,
                               new Vector2(0f, 14f));
            }
        }

        /// <summary>
        /// Центр масс. Расхождение с визуальным центром объекта — причина номер один,
        /// почему транспорт и ragdoll ведут себя не так, как ожидается.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawCenterOfMass(this Rigidbody rb, Color c, float size = 0.12f)
        {
            if (rb == null) return;

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawPoint(rb.worldCenterOfMass, size);
                Gizmo.DrawWireSphere(rb.worldCenterOfMass, size * 0.6f);
            }
        }

        // ============================================================ рейкасты

        /// <summary>Луч заданной длины со стрелкой на конце.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void Draw(this Ray ray, Color c, float distance = 100f)
        {
            using (Gizmo.Scope(c))
                Gizmo.DrawArrow(ray.origin, ray.origin + ray.direction * distance);
        }

        /// <summary>
        /// Точка попадания: крестик в плоскости поверхности, диск и стрелка нормали.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void Draw(this RaycastHit hit, Color c, float size = 0.15f)
        {
            using (Gizmo.Scope(c)) Gizmo.DrawHit(hit.point, hit.normal, size);
        }

        /// <summary>
        /// Самый частый паттерн отладки рейкаста в одном вызове: участок до попадания
        /// одним цветом, непройденный остаток — приглушённым, плюс маркер в точке
        /// попадания и подпись дистанцией.
        /// </summary>
        /// <param name="maxDistance">Длина, с которой запускался рейкаст.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTo(this Ray ray, RaycastHit hit, float maxDistance,
                                  Color hitColor, float restAlpha = 0.25f)
        {
            var end = ray.origin + ray.direction * maxDistance;
            var rest = new Color(hitColor.r, hitColor.g, hitColor.b,
                                 hitColor.a * Mathf.Clamp01(restAlpha));

            using (Gizmo.Scope(hitColor))
            {
                Gizmo.DrawLine(ray.origin, hit.point);
                Gizmo.DrawHit(hit.point, hit.normal, 0.12f);
                Gizmo.DrawText(hit.distance.ToString("0.##"), hit.point, 12f, new Vector2(0f, -16f));

                Gizmo.color = rest;
                Gizmo.DrawLine(hit.point, end);
            }
        }

        // ============================================================ джойнты

        /// <summary>
        /// Точки крепления джойнта: свой anchor, чужой connectedAnchor, линия между ними
        /// и ось. Если тела разъехались, эта линия сразу это показывает.
        ///
        /// anchor задан в локальных координатах своего тела, connectedAnchor — в
        /// локальных координатах присоединённого, а без него в мировых. Именно эта
        /// асимметрия обычно и путает.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAnchors(this Joint joint, Color c, float size = 0.08f)
        {
            if (joint == null) return;
            var t = joint.transform;

            var own = t.TransformPoint(joint.anchor);
            var other = joint.connectedBody != null
                ? joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)
                : joint.connectedAnchor;

            using (Gizmo.Scope(c))
            {
                Gizmo.DrawPoint(own, size);
                Gizmo.DrawWireSphere(other, size);
                Gizmo.DrawLine(own, other);
                Gizmo.DrawArrow(own, own + t.TransformDirection(joint.axis).normalized * size * 6f);
            }
        }
    }
}
