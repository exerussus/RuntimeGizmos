// Команды GizmoLazy, зависящие от модулей физики. Файл отдельный по той же причине,
// что и GizmoExtensions.Physics.cs: если модуль в проекте выключен, удаляется он один.
// Вместе с ним исчезают и сами команды, и их ветка в диспетчере — частичный метод
// без реализации компилятор выбрасывает вместе с вызовом.
using System.Runtime.CompilerServices;
using UnityEngine;
using RuntimeGizmos.Extensions;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    public readonly partial struct GizmoLazyTarget
    {
        /// <summary>
        /// Настоящая форма коллайдера, а не габариты: Box, Sphere, Capsule,
        /// CharacterController, Mesh — и то же для 2D. Целью должен быть сам коллайдер:
        /// <c>GizmoLazy.Track(myCollider).Shape()</c>.
        /// </summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Shape(Color color = default,
                          [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Shape, f, l, Fix(color), 0f, 0f, null, null, null);

        /// <summary>
        /// Вектор скорости из центра масс, подписанный величиной. Целью должно быть
        /// физическое тело: <c>GizmoLazy.Track(myRigidbody).Velocity()</c>.
        /// </summary>
        /// <param name="scale">Сколько юнитов длины на 1 м/с.</param>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Velocity(Color color = default, float scale = 0.2f,
                             [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Velocity, f, l, Fix(color), scale, 0f, null, null, null);
    }

    internal static partial class Registry
    {
        static partial void DrawPhysics(in Entry e)
        {
            switch (e.Kind)
            {
                case GizmoLazyKind.Shape:
                    if (e.Target is Collider c3) c3.DrawShape(e.Color);
                    else if (e.Target is Collider2D c2) c2.DrawShape(e.Color);
                    return;

                case GizmoLazyKind.Velocity:
                    if (e.Target is Rigidbody rb) rb.DrawVelocity(e.Color, e.A);
                    else if (e.Target is Rigidbody2D rb2) rb2.DrawVelocity(e.Color, e.A);
                    return;
            }
        }
    }
}
