using System;
using System.Collections.Generic;
using UnityEngine;
using RuntimeGizmos.Internal;
using RuntimeGizmos.Extensions;
using Object = UnityEngine.Object;

namespace RuntimeGizmos
{
    /// <summary>Живые регистрации GizmoLazy. Структуры в списке — регистрация не мусорит.</summary>
    internal static partial class Registry
    {
        internal struct Entry
        {
            public Object Target;
            public GizmoObjectId TargetId;
            public Transform Xf;

            public string Key;      // явный ключ либо путь к файлу вызова
            public int Line;
            public GizmoLazyKind Kind;

            public float Expiry;
            public int Category;
            public Color Color;
            public float A, B;      // числовые параметры команды
            public string Text;
            public Object Ref;      // вторая цель, для Link
            public Action Custom;
        }

        static readonly List<Entry> _entries = new List<Entry>(32);
        static bool _overflowWarned;

        public static int Count => _entries.Count;

        public static void Add(in GizmoLazyTarget src, GizmoLazyKind kind, string file, int line,
                               Color color, float a, float b, string text, Object rf, Action custom)
        {
            // Выключенный слой не должен копить регистрации. Раньше проверка стояла только
            // в Tick: рисование прекращалось, а список продолжал расти — вплоть до MaxTracked
            // и предупреждения о переполнении у слоя, который никто не рисует.
            // Записи, сделанные до выключения, остаются: их разберёт Tick после включения.
            if (!GizmoLazy.Enabled) return;

            if (src.Target == null) return;

            string key = src.ExplicitKey ?? file;
            var id = GizmoObjectId.Of(src.Target);

            // Вид команды входит в ключ: разные команды с одной строки не затирают друг друга.
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.TargetId != id || e.Kind != kind || e.Line != line || e.Key != key) continue;
                Fill(ref e, src, kind, key, line, color, a, b, text, rf, custom);
                _entries[i] = e;
                return;
            }

            if (_entries.Count >= GizmoLazy.MaxTracked)
            {
                if (!_overflowWarned)
                {
                    _overflowWarned = true;
                    Debug.LogWarning($"[RuntimeGizmos] Живых регистраций GizmoLazy больше {GizmoLazy.MaxTracked}, " +
                                     "новые игнорируются. Чаще всего это регистрация в цикле по разным целям " +
                                     "с одной строки — добавьте .Key(своё_имя) или поднимите GizmoLazy.MaxTracked.\n" +
                                     Offenders());
                }
                return;
            }

            var n = new Entry();
            Fill(ref n, src, kind, key, line, color, a, b, text, rf, custom);
            _entries.Add(n);
        }

        static void Fill(ref Entry e, in GizmoLazyTarget src, GizmoLazyKind kind, string key, int line,
                         Color color, float a, float b, string text, Object rf, Action custom)
        {
            e.Target = src.Target;
            e.TargetId = GizmoObjectId.Of(src.Target);
            e.Xf = src.Xf;
            e.Key = key;
            e.Line = line;
            e.Kind = kind;
            e.Expiry = src.Expiry;

            // Категория снимается в момент регистрации: `using (Gizmo.Category(x))` вокруг
            // Track(...) — и запись будет рисоваться в этой категории, а не в той, что
            // случайно окажется текущей на границе кадра.
            e.Category = Internal.GizmoRenderer.Category;
            e.Color = color;
            e.A = a; e.B = b;
            e.Text = text;
            e.Ref = rf;
            e.Custom = custom;
        }

        // Кто именно занял места. Без этого предупреждение сообщает о проблеме,
        // но не о том, где её искать.
        static string Offenders()
        {
            var byKey = new Dictionary<string, int>();
            foreach (var e in _entries)
                byKey[e.Key] = byKey.TryGetValue(e.Key, out int n) ? n + 1 : 1;

            var sb = new System.Text.StringBuilder("Занято мест по местам вызова:");
            int shown = 0;
            foreach (var kv in byKey)
            {
                if (shown++ == 5) { sb.Append("\n  …и ещё ").Append(byKey.Count - 5); break; }
                sb.Append("\n  ").Append(kv.Value).Append(" × ").Append(kv.Key);
            }
            return sb.ToString();
        }

        // Идёт обход в Tick. Пользовательский колбэк (GizmoLazy…Draw(() => …)) вправе позвать
        // Untrack или Clear прямо изнутри — например, «дорисовал и снялся». Список при этом
        // сжимать нельзя: обход идёт по индексам, и следующая же итерация (или RemoveAt в catch)
        // вылетала за границу. Исключение уходило из узла PlayerLoop, и граница кадра в этом
        // кадре не проводилась вовсе. Поэтому во время обхода записи только помечаются
        // (Expiry = -∞ — «уже истекла»), а физически убираются одним проходом после него.
        static bool _ticking;

        public static void Remove(Object target, string key)
        {
            if (target == null) return;
            var id = GizmoObjectId.Of(target);

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].TargetId != id) continue;
                if (key != null && _entries[i].Key != key) continue;

                if (_ticking) Kill(i);
                else _entries.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            _overflowWarned = false;

            if (_ticking)
            {
                for (int i = 0; i < _entries.Count; i++) Kill(i);
                return;
            }

            _entries.Clear();
        }

        // Есть что убирать. Без флага Sweep каждый кадр перепроверял бы Unity-null у каждой
        // записи второй раз подряд — обход только что сделал ровно то же самое.
        static bool _needSweep;

        static void Kill(int i)
        {
            _needSweep = true;
            var e = _entries[i];
            e.Expiry = float.NegativeInfinity;
            e.Custom = null;                    // не держим замыкание до конца обхода
            _entries[i] = e;
        }

        static bool Dead(in Entry e, float now) =>
            e.Target == null || now >= e.Expiry ||
            // Связь с уничтоженной второй целью ничего не рисует и сама не снимется никогда.
            (e.Kind == GizmoLazyKind.Link && e.Ref == null);

        /// <summary>На границе кадра, до обмена буферов — иначе опоздает на кадр.</summary>
        public static void Tick()
        {
            if (_entries.Count == 0) return;

            float now = Time.realtimeSinceStartup;

            // Выключенный слой не рисует, но чистить обязан: иначе истёкшие записи и записи
            // уничтоженных объектов держат свои замыкания и занимают места до MaxTracked
            // всё время, пока слой выключен.
            if (!GizmoLazy.Enabled || !Internal.GizmoRenderer.Enabled)
            {
                Sweep(now);
                return;
            }

            // Состояние рисования глобальное, а записи рисуются с чистого листа (ResetState
            // ниже). Без снимка пользовательские Gizmo.lineWidth / depthTest / color, выставленные
            // один раз в Start, молча сбрасывались в дефолт на первой же границе кадра, как
            // только в реестре появлялась хоть одна запись.
            var saved = Gizmo.Scope();
            _ticking = true;

            try
            {
                // Записи, добавленные из колбэка, встают в хвост и в этот обход не попадают.
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];
                    if (Dead(in e, now)) { _needSweep = true; continue; }

                    // Категорию проверяем ЗДЕСЬ, а не полагаемся на Begin() внутри примитивов:
                    // тело записи — это Volume() с GetComponentsInChildren, Label с форматированием
                    // и пользовательская лямбда. Проверка на уровне вершины выключила бы только
                    // заливку буфера, оставив всю дорогую часть.
                    //
                    // Запись при этом остаётся в реестре: категорию включат — она снова нарисуется.
                    if ((e.Category & GizmoSettings.EnabledCategories) == 0) continue;

                    // Чистое состояние на запись: иначе порядок регистраций стал бы значимым.
                    Gizmo.ResetState();
                    Internal.GizmoRenderer.Category = e.Category;

                    try
                    {
                        Draw(in e);
                    }
                    catch (Exception ex)
                    {
                        if (i < _entries.Count) Kill(i);
                        Debug.LogError("[RuntimeGizmos] Исключение внутри GizmoLazy, регистрация снята.\n" + ex);
                    }
                }
            }
            finally
            {
                _ticking = false;
                saved.Dispose();
            }

            if (_needSweep) Sweep(now);
        }

        static void Sweep(float now)
        {
            _needSweep = false;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (Dead(_entries[i], now)) _entries.RemoveAt(i);
        }

        static void Draw(in Entry e)
        {
            var xf = e.Xf;

            switch (e.Kind)
            {
                case GizmoLazyKind.Custom:
                    e.Custom?.Invoke();
                    return;

                case GizmoLazyKind.Volume:
                    xf.DrawVolume(e.Color); return;

                case GizmoLazyKind.Bounds:
                    xf.DrawBounds(e.Color); return;

                case GizmoLazyKind.Label:
                    Gizmo.color = e.Color;
                    xf.DrawLabel(e.Text, e.A);
                    return;

                case GizmoLazyKind.Link:
                    xf.DrawLinkTo(e.Ref as Transform, e.Color); return;

                case GizmoLazyKind.Axes:
                    xf.DrawAxes(e.A); return;

                case GizmoLazyKind.Forward:
                    Gizmo.color = e.Color;
                    xf.DrawForward(e.A);
                    return;

                case GizmoLazyKind.Range:
                    Gizmo.color = e.Color;
                    Gizmo.DrawRange(xf.position, e.A, e.B);
                    return;

                case GizmoLazyKind.Fov:
                    Gizmo.color = e.Color;
                    Gizmo.DrawFieldOfView(xf.position, xf.forward, e.A, e.B);
                    return;

                case GizmoLazyKind.Hierarchy:
                    xf.DrawHierarchy(e.Color, (int)e.A); return;

                default:
                    DrawPhysics(in e);
                    return;
            }
        }

        // Реализация в GizmoLazy.Physics.cs. Файла нет — вызов выбрасывается компилятором.
        static partial void DrawPhysics(in Entry e);
    }
}
