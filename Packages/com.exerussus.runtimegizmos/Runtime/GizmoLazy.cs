using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using RuntimeGizmos.Extensions;
using Cond = System.Diagnostics.ConditionalAttribute;
using Object = UnityEngine.Object;

namespace RuntimeGizmos
{
    /// <summary>
    /// Регистрация рисуется каждый кадр, пока жива цель.
    /// <code>GizmoLazy.Track(enemy).For(5f).Label("агрится");</code>
    /// Команды читают цель заново, поэтому рисунок следует за объектом.
    ///
    /// Аллоцирует только <see cref="GizmoLazyTarget.Draw"/> — лямбда есть замыкание,
    /// её место в Start, не в Update. Остальные команды хранятся структурами.
    ///
    /// Цепочка заканчивается одной командой: терминал возвращает void, иначе к нему
    /// не применить [Conditional], а с ним из релиза исчезает всё выражение целиком.
    /// </summary>
    public static class GizmoLazy
    {
        /// <summary>Общий выключатель слоя. Базового слоя не касается.</summary>
        public static bool Enabled = true;

        /// <summary>Потолок живых регистраций.</summary>
        public static int MaxTracked = 256;

        /// <summary>Сколько регистраций живо сейчас.</summary>
        public static int Count => Registry.Count;

        /// <summary>Рисовать, пока жива цель.</summary>
        public static GizmoLazyTarget Track(Transform target) => new GizmoLazyTarget(target, target);


        public static GizmoLazyTarget Track(GameObject target)
            => new GizmoLazyTarget(target, target != null ? target.transform : null);

        /// <summary>Цель-компонент. Нужна для <c>Shape</c> и <c>Velocity</c>.</summary>
        public static GizmoLazyTarget Track(Component target)
            => new GizmoLazyTarget(target, target != null ? target.transform : null);

        /// <summary>Снять все регистрации по цели.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public static void Untrack(Object target) => Registry.Remove(target, null);

        /// <summary>Снять регистрацию по цели и явному ключу.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public static void Untrack(Object target, string key) => Registry.Remove(target, key);

        /// <summary>Снять всё.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public static void Clear() => Registry.Clear();
    }

    /// <summary>Звено цепочки. Структура на стеке.</summary>
    public readonly partial struct GizmoLazyTarget
    {
        internal readonly Object Target;
        internal readonly Transform Xf;
        internal readonly float Expiry;
        internal readonly string ExplicitKey;

        internal GizmoLazyTarget(Object target, Transform xf)
        {
            Target = target; Xf = xf;
            Expiry = float.PositiveInfinity; ExplicitKey = null;
        }

        GizmoLazyTarget(Object target, Transform xf, float expiry, string key)
        {
            Target = target; Xf = xf; Expiry = expiry; ExplicitKey = key;
        }

        /// <summary>Ограничить время жизни.</summary>
        public GizmoLazyTarget For(float seconds) =>
            new GizmoLazyTarget(Target, Xf, Time.realtimeSinceStartup + seconds, ExplicitKey);

        /// <summary>
        /// Ключ вручную. По умолчанию регистрация опознаётся по цели, месту вызова и виду
        /// команды — но цикл по целям идёт с одной строки, и там записи схлопнутся в одну.
        /// </summary>
        public GizmoLazyTarget Key(string key) =>
            new GizmoLazyTarget(Target, Xf, Expiry, key);

        // ================================================================ команды

        /// <summary>Габариты: яркие углы, приглушённый контур.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Volume(Color color = default,
                           [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Volume, f, l, Fix(color), 0f, 0f, null, null, null);

        /// <summary>Каркасный ящик по габаритам.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Bounds(Color color = default,
                           [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Bounds, f, l, Fix(color), 0f, 0f, null, null, null);

        /// <summary>
        /// Подпись. Строка сохраняется при регистрации, поэтому меняющееся значение —
        /// через <see cref="Draw"/>.
        /// </summary>
        /// <param name="worldHeight">0 — пиксели, иначе высота буквы в юнитах.</param>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Label(string text, Color color = default, float worldHeight = 0f,
                          [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Label, f, l, Fix(color), worldHeight, 0f, text, null, null);

        /// <summary>Связь: объёмы обоих, линия по границам, шеврон направления.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void LinkTo(Transform other, Color color = default,
                           [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Link, f, l, Fix(color), 0f, 0f, null, other, null);

        /// <summary>Локальные оси.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Axes(float size = 1f,
                         [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Axes, f, l, Color.white, size, 0f, null, null, null);

        /// <summary>Направление вперёд, стрелкой.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Forward(float length = 1f, Color color = default,
                            [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Forward, f, l, Fix(color), length, 0f, null, null, null);

        /// <summary>Радиус на земле.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Range(float radius, Color color = default, float height = 0f,
                          [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Range, f, l, Fix(color), radius, height, null, null, null);

        /// <summary>Сектор обзора.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void FieldOfView(float angleDeg, float distance, Color color = default,
                                [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Fov, f, l, Fix(color), angleDeg, distance, null, null, null);

        /// <summary>Дерево иерархии вниз.</summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Hierarchy(Color color = default, int maxDepth = 8,
                              [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Hierarchy, f, l, Fix(color), maxDepth, 0f, null, null, null);

        /// <summary>
        /// Произвольная отрисовка. Единственная аллоцирующая команда: лямбда с захватом —
        /// это замыкание плюс делегат. Из Update будет мусорить каждый кадр.
        /// </summary>
        [Cond(Gizmo.EDITOR), Cond(Gizmo.DEV), Cond(Gizmo.ALWAYS)]
        public void Draw(Action draw,
                         [CallerFilePath] string f = "", [CallerLineNumber] int l = 0)
            => Registry.Add(this, GizmoLazyKind.Custom, f, l, Color.white, 0f, 0f, null, null, draw);

        // default(Color) — прозрачный чёрный.
        internal static Color Fix(Color c) => c == default ? Color.white : c;
    }

    internal enum GizmoLazyKind : byte
    {
        Volume, Bounds, Label, Link, Axes, Forward, Range, Fov, Hierarchy, Custom,
        Shape, Velocity,
    }
}
