// ВНИМАНИЕ: тестовый харнесс. Компилируется ТОЛЬКО вне Unity — через run.sh.
//
// Директива ниже обязательна: файл лезет во внутренние типы RuntimeGizmos через
// рефлексию и опирается на заглушки Unity API. Внутри Unity он не собрался бы,
// а до ошибок компиляции дело доводить незачем — символ определён всегда, и файл
// схлопывается в пустой.
#if !UNITY_2020_3_OR_NEWER

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using RuntimeGizmos;
using RuntimeGizmos.Internal;
using RuntimeGizmos.Extensions;

public static class Tests
{
    static int _pass, _fail;
    static string _group = "";

    static void Group(string g) { _group = g; Console.WriteLine("\n── " + g); }

    static void Check(string name, bool ok, string extra = "")
    {
        if (ok) { _pass++; Console.WriteLine("  ok    " + name); }
        else { _fail++; Console.WriteLine("  ПАДЁТ " + name + (extra != "" ? "   [" + extra + "]" : "")); }
    }

    static void Throws(string name, Action a, bool expect)
    {
        bool threw = false; string msg = "";
        try { a(); } catch (Exception e) { threw = true; msg = e.GetType().Name + ": " + e.Message; }
        Check(name, threw == expect, threw ? msg : "не бросил");
    }

    static T Priv<T>(Type t, string field) => (T)t.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    static void Call(Type t, string m) => t.GetMethod(m, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

    // Сколько публичных расширений помечены [Conditional] — без этого в релизе
    // остался бы и Scope, и вычисление аргументов.
    static int ConditionalCount()
    {
        int n = 0;
        foreach (var t in new[] { typeof(GizmoTransformExtensions), typeof(GizmoPhysicsExtensions),
                                  typeof(GizmoPhysics2DExtensions), typeof(GizmoAudioExtensions) })
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.GetCustomAttributes(typeof(System.Diagnostics.ConditionalAttribute), false).Length > 0) n++;
        return n;
    }

    static string Indent(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in s.TrimEnd().Split('\n')) sb.Append("        ").Append(line).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    // Все символы, которые шрифт обязан покрывать.
    static System.Collections.Generic.IEnumerable<int> AllCovered()
    {
        for (int c = 32; c <= 126; c++) yield return c;
        for (int c = 0x410; c <= 0x44F; c++) yield return c;
        yield return 0x401; yield return 0x451; yield return 0x25A1;
    }

    // Один кадр: выставить время и пройти границу кадра.
    static void Tick(float time) { Time.realtimeSinceStartup = time; GizmoRenderer.BeginFrame(true); }

    static void Boot()
    {
        GizmoRenderer.Dispose();
        GizmoSettings.ResetSession();
        Shader.Available = true;
        Shader.Supported = true;
        GizmoRenderer.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        GizmoRenderer.Enabled = true;
        Gizmo.Reset();
    }

    public static int Main()
    {
        Boot();

        // ==================================================== A. нативный буфер
        Group("A. Нативный буфер");
        {
            var b = new GizmoNativeBuffer<int>(4);
            GizmoSettings.MaxVerticesPerChannel = 0;      // без потолка
            unsafe
            {
                var p = b.Reserve(4); for (int i = 0; i < 4; i++) p[i] = i + 1;
                var q = b.Reserve(100);                    // заставляем расти
                for (int i = 0; i < 100; i++) q[i] = 1000 + i;
                bool keep = true;
                for (int i = 0; i < 4; i++) keep &= b.Ptr[i] == i + 1;
                Check("A1 рост сохраняет уже записанное", keep);
                Check("A2 count после двух Reserve", b.Count == 104, "count=" + b.Count);
                bool tail = true;
                for (int i = 0; i < 100; i++) tail &= b.Ptr[4 + i] == 1000 + i;
                Check("A3 данные после роста целы", tail);
            }
            b.Clear();
            Check("A4 Clear обнуляет count", b.Count == 0);

            // потолок
            GizmoSettings.MaxVerticesPerChannel = 64;
            unsafe
            {
                var buf = new GizmoNativeBuffer<int>(4);
                buf.Reserve(60);
                int before = buf.Count;
                var sink = buf.Reserve(50);              // 110 > 64 → в сток
                sink[0] = 7; sink[49] = 9;               // запись в сток не должна падать
                Check("A5 потолок не даёт расти", buf.Count == before, "count=" + buf.Count);
                Check("A6 сток отдаёт валидный указатель", sink != null && sink[0] == 7 && sink[49] == 9);
                var small = buf.Reserve(4);              // 64 влезает ровно
                Check("A7 ровно по потолку проходит", buf.Count == 64, "count=" + buf.Count);
                buf.Dispose();
            }
            Throws("A8 двойной Dispose безопасен", () => { b.Dispose(); b.Dispose(); }, false);
            GizmoSettings.ResetOverrides();
        }

        // ==================================================== B. шрифт
        Group("B. Шрифт");
        {
            GizmoFont.Ensure();
            int total = 0, zero = 0, worst = 0;
            foreach (int c in AllCovered())
            {
                GizmoFont.Glyph((char)c, out int s0, out int n);
                total += n;
                worst = Math.Max(worst, s0 + n);
                for (int k = 0; k < n; k++)
                {
                    var g = GizmoFont.Segments[s0 + k];
                    if (g.x == g.z && g.y == g.w) zero++;
                }
            }
            Check("B1 сегментов разобрано", total == 824, "total=" + total);
            Check("B2 нет вырожденных отрезков", zero == 0, "zero=" + zero);
            Check("B3 индексы не вылезают за буфер", worst <= GizmoFont.Segments.Length,
                  "worst=" + worst + " len=" + GizmoFont.Segments.Length);
            Check("B4 пробел без сегментов", !GizmoFont.Glyph(' ', out _, out int ns) && ns == 0);
            Check("B5 'A' есть", GizmoFont.Glyph('A', out _, out int na) && na == 5, "n=" + na);

            bool safe = true;
            foreach (char c in new[] { '\n', '\t', '\0', 'ф', 'Ω', (char)200, (char)0xFFFF, (char)31, (char)127, (char)0x40F, (char)0x450 })
                try { GizmoFont.Glyph(c, out _, out _); } catch { safe = false; }
            Check("B6 неподдерживаемые символы не бросают", safe);
            bool cyr = true;
            for (int c = 0x410; c <= 0x44F; c++) cyr &= GizmoFont.Glyph((char)c, out _, out int n2) && n2 > 0;
            Check("B7 вся кириллица А..я на месте", cyr);
            Check("B7b Ё и ё на месте", GizmoFont.Glyph('Ё', out _, out _) && GizmoFont.Glyph('ё', out _, out _));

            // Неизвестный символ обязан дать заглушку, а не исчезнуть молча
            Check("B7c иероглиф даёт заглушку", GizmoFont.Glyph('\u6F22', out _, out int nt) && nt == 4, "n=" + nt);
            Check("B7d эмодзи-суррогат даёт заглушку", GizmoFont.Glyph('\uD83D', out _, out _));
            Check("B7e управляющие символы ничего не рисуют",
                  !GizmoFont.Glyph('\n', out _, out _) && !GizmoFont.Glyph('\t', out _, out _) && !GizmoFont.Glyph('\0', out _, out _));

            float w1 = GizmoFont.Width("A"), w3 = GizmoFont.Width("ABC");
            Check("B8 ширина растёт линейно", Math.Abs((w3 - w1) - 2 * GizmoFont.Advance) < 1e-4f,
                  "w1=" + w1 + " w3=" + w3);
        }

        // ==================================================== C. текст
        Group("C. Текстовые метки");
        {
            Boot();
            GizmoRenderer.Ensure();
            var ch = Priv<GizmoChannel<GizmoTextVertex>[]>(typeof(GizmoRenderer), "_text");
            Check("C0 текстовый канал создан", ch != null && ch.Length == 2);

            int Verts() => ch[0].Target(false).Count;
            int before = Verts();
            Gizmo.DrawText("A", Vector3.zero, 14f);
            GizmoFont.Glyph('A', out _, out int segA);
            Check("C1 6 вершин на отрезок глифа", Verts() - before == segA * 6,
                  "delta=" + (Verts() - before) + " ожидалось " + segA * 6);

            before = Verts();
            Gizmo.DrawText("", Vector3.zero, 14f);
            Gizmo.DrawText(null, Vector3.zero, 14f);
            Gizmo.DrawText("   ", Vector3.zero, 14f);
            Check("C2 пустая строка и пробелы ничего не пишут", Verts() == before);

            before = Verts();
            Gizmo.DrawText("A", Vector3.zero, 0f);
            Gizmo.DrawText("A", Vector3.zero, -5f);
            Check("C3 нулевой и отрицательный размер игнорируются", Verts() == before);

            before = Verts();
            Gizmo.DrawText("фыва", Vector3.zero, 14f);
            Check("C4 кириллица рисуется", Verts() > before);
            before = Verts();
            Gizmo.DrawText("\u6F22\u6F22", Vector3.zero, 14f);
            Check("C4b неизвестные символы дают заглушку, а не пустоту", Verts() - before == 2 * 4 * 6,
                  "delta=" + (Verts() - before));

            Throws("C5 длинная строка не падает",
                   () => Gizmo.DrawText(new string('W', 4000), Vector3.zero, 14f), false);

            // выравнивание сдвигает пиксельные смещения
            ch[0].Clear();
            Gizmo.DrawText("XX", Vector3.zero, 10f, Vector2.zero, GizmoTextAlign.Left);
            float leftMin = MinOffsetX(ch[0]);
            ch[0].Clear();
            Gizmo.DrawText("XX", Vector3.zero, 10f, Vector2.zero, GizmoTextAlign.Right);
            float rightMin = MinOffsetX(ch[0]);
            Check("C6 Right сдвигает левее Left", rightMin < leftMin, "L=" + leftMin + " R=" + rightMin);
            ch[0].Clear();
            Gizmo.DrawText("XX", Vector3.zero, 10f, Vector2.zero, GizmoTextAlign.Center);
            float centerMin = MinOffsetX(ch[0]);
            Check("C7 Center между ними", centerMin < leftMin && centerMin > rightMin,
                  "L=" + leftMin + " C=" + centerMin + " R=" + rightMin);

            // толщина штриха никогда не нулевая
            ch[0].Clear();
            Gizmo.lineWidth = 0f;
            Gizmo.DrawText("A", Vector3.zero, 14f);
            Check("C8 lineWidth=0 не даёт нулевой штрих", MinWidth(ch[0]) >= 1f, "w=" + MinWidth(ch[0]));
            Gizmo.Reset();
        }

        // ==================================================== D. кадровая модель
        Group("D. Кадровая модель");
        {
            Boot();
            GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Time.realtimeSinceStartup = 100f;

            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("D1 запись идёт в back", thin[0].Target(false).Count == 2);
            GizmoRenderer.BeginFrame(strict: true);
            Check("D2 после BeginFrame back пуст", thin[0].Target(false).Count == 0);
            Check("D3 меш готов к отрисовке", thin[0].Prepare(out _, out _));

            GizmoRenderer.BeginFrame(strict: true);
            Check("D4 strict: без новых команд геометрия исчезает", !thin[0].Prepare(out _, out _));

            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            GizmoRenderer.BeginFrame(strict: true);
            Time.realtimeSinceStartup = 100.1f;
            GizmoRenderer.BeginFrame(strict: false);
            Check("D5 edit mode: снимок держится внутри таймаута", thin[0].Prepare(out _, out _));
            Time.realtimeSinceStartup = 101f; GizmoRenderer.BeginFrame(strict: false);
            Time.realtimeSinceStartup = 102f; GizmoRenderer.BeginFrame(strict: false);
            Check("D6 edit mode: после таймаута исчезает", !thin[0].Prepare(out _, out _));

            // duration
            Boot();
            GizmoRenderer.Ensure();
            thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Time.realtimeSinceStartup = 200f;
            GizmoRenderer.BeginFrame(true);          // синхронизируем штамп времени
            Gizmo.duration = 2f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 0f;
            GizmoRenderer.BeginFrame(true);
            Check("D7 duration переживает кадр", thin[0].Prepare(out _, out _));
            Tick(201f);
            Check("D8 не истекла — ещё жива", thin[0].Prepare(out _, out _));
            Tick(203f); Tick(203.02f);   // истечение проверяется штампом прошлого кадра
            Check("D9 после истечения пропала", !thin[0].Prepare(out _, out _));

            // компактация только целыми примитивами
            Boot();
            GizmoRenderer.Ensure();
            thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Time.realtimeSinceStartup = 300f;
            GizmoRenderer.BeginFrame(true);
            Gizmo.duration = 1f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 5f; Gizmo.DrawLine(Vector3.one, Vector3.zero);
            Gizmo.duration = 0f;
            GizmoRenderer.BeginFrame(true);
            Tick(302f); Tick(302.02f);
            int left = thin[0].Target(true).Count;
            Check("D10 компактация оставляет целый примитив", left == 2, "осталось вершин=" + left);

            // duration короче кадра не должен вести себя хуже, чем duration = 0
            Boot(); GizmoRenderer.Ensure();
            thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Time.realtimeSinceStartup = 400f; GizmoRenderer.BeginFrame(true);
            Gizmo.duration = 0.001f;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 0f;
            Time.realtimeSinceStartup = 400.016f;    // прошёл кадр
            GizmoRenderer.BeginFrame(true);
            Check("D11 duration короче кадра всё равно виден кадр", thin[0].Prepare(out _, out _));

            Boot(); GizmoRenderer.Ensure();
            thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Time.realtimeSinceStartup = 500f; GizmoRenderer.BeginFrame(true);
            Gizmo.duration = 0.001f; Gizmo.DrawLine(Vector3.zero, Vector3.one); Gizmo.duration = 0f;
            Time.realtimeSinceStartup = 500.016f; GizmoRenderer.BeginFrame(true);
            Time.realtimeSinceStartup = 500.032f; GizmoRenderer.BeginFrame(true);
            Check("D12 и всё-таки исчезает следующим кадром", !thin[0].Prepare(out _, out _));
        }

        // ==================================================== E. настройки
        Group("E. Настройки");
        {
            GizmoSettings.ResetSession();
            GizmoSettings.PlatformOverride = GizmoPlatform.Desktop;
            Check("E1 дефолт десктопа", Math.Abs(GizmoSettings.DefaultLineWidth - 1f) < 1e-5f);
            GizmoSettings.PlatformOverride = GizmoPlatform.Mobile;
            Check("E2 дефолт мобилки", Math.Abs(GizmoSettings.DefaultLineWidth - 2f) < 1e-5f);
            GizmoSettings.PlatformOverride = GizmoPlatform.XR;
            Check("E3 дефолт XR", Math.Abs(GizmoSettings.DefaultLineWidth - 3f) < 1e-5f);
            GizmoSettings.PlatformOverride = GizmoPlatform.Web;
            Check("E4 потолок памяти в вебе жёстче", GizmoSettings.MaxVerticesPerChannel == 1 << 17,
                  "" + GizmoSettings.MaxVerticesPerChannel);

            GizmoSettings.DefaultLineWidth = 9f;
            Check("E5 оверрайд бьёт дефолт", Math.Abs(GizmoSettings.DefaultLineWidth - 9f) < 1e-5f);
            GizmoSettings.PlatformOverride = GizmoPlatform.Desktop;
            Check("E6 оверрайд не зависит от платформы", Math.Abs(GizmoSettings.DefaultLineWidth - 9f) < 1e-5f);
            GizmoSettings.Overrides.DefaultLineWidth = null;
            Check("E7 снятие оверрайда возвращает дефолт", Math.Abs(GizmoSettings.DefaultLineWidth - 1f) < 1e-5f);

            GizmoSettings.CircleSegments = 100000;
            Check("E8 Sanitize зажимает сегменты", GizmoSettings.CircleSegments == 256, "" + GizmoSettings.CircleSegments);
            GizmoSettings.CircleSegments = -5;
            Check("E9 Sanitize зажимает снизу", GizmoSettings.CircleSegments == 6, "" + GizmoSettings.CircleSegments);
            GizmoSettings.GlobalAlpha = 55f;
            Check("E10 альфа зажата в 0..1", Math.Abs(GizmoSettings.GlobalAlpha - 1f) < 1e-5f);
            GizmoSettings.Layer = 999;
            Check("E11 слой зажат в 0..31", GizmoSettings.Layer == 31, "" + GizmoSettings.Layer);
            GizmoSettings.MaxVerticesPerChannel = -7;
            Check("E12 потолок не бывает отрицательным", GizmoSettings.MaxVerticesPerChannel == 0);

            GizmoSettings.ResetSession();
            Check("E13 ResetSession снимает оверрайды", GizmoSettings.CircleSegments == 32, "" + GizmoSettings.CircleSegments);
            Check("E14 ResetSession снимает PlatformOverride", GizmoSettings.PlatformOverride == null);
        }

        // ==================================================== F. PlayerLoop
        Group("F. PlayerLoop");
        {
            var loop = typeof(GizmoLoop);
            UnityEngine.LowLevel.PlayerLoop.Fresh();
            Call(loop, "Install");
            Check("F1 вставился ровно один раз", CountGizmoNodes() == 1, "" + CountGizmoNodes());
            Call(loop, "Install");
            Call(loop, "Install");
            Check("F2 Install идемпотентен", CountGizmoNodes() == 1, "" + CountGizmoNodes());
            Call(loop, "RemovePlayerLoop");
            Check("F3 удаляется", CountGizmoNodes() == 0);
            UnityEngine.LowLevel.PlayerLoop.Fresh();     // Unity сбросила луп при входе в Play
            Call(loop, "Install");
            Check("F4 переустанавливается после сброса лупа", CountGizmoNodes() == 1);
            Check("F5 стоит первым в PostLateUpdate", FirstInPostLate());
        }

        // ==================================================== G. жизненный цикл
        Group("G. Жизненный цикл и отказы");
        {
            // шейдера нет → Draw* не должен падать (тот самый баг)
            GizmoRenderer.Dispose();
            GizmoSettings.ResetSession();
            Shader.Available = false;
            GizmoRenderer.Enabled = true;
            GizmoRenderer.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Throws("G1 нет шейдера → Draw* не бросает", () =>
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawWireSphere(Vector3.zero, 1f);
                Gizmo.DrawText("x", Vector3.zero, 12f);
                Gizmo.DrawCube(Vector3.zero, Vector3.one);
            }, false);
            Check("G2 система сама выключилась", !GizmoRenderer.Enabled);
            Throws("G3 Submit после отказа не бросает", () => GizmoRenderer.Submit(null), false);
            Throws("G4 BeginFrame после отказа не бросает", () => GizmoRenderer.BeginFrame(true), false);

            Shader.Available = true;
            Boot();
            Throws("G5 Dispose → Draw → Dispose", () =>
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                GizmoRenderer.Dispose();
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                GizmoRenderer.Dispose();
            }, false);

            Boot();
            Throws("G6 Draw из другого потока не портит буфер", () =>
            {
                var t = new System.Threading.Thread(() => Gizmo.DrawLine(Vector3.zero, Vector3.one));
                t.Start(); t.Join();
            }, false);
            GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Check("G7 чужой поток ничего не записал", thin[0].Target(false).Count == 0,
                  "" + thin[0].Target(false).Count);
        }

        // ==================================================== H. краевая геометрия
        Group("H. Краевые значения геометрии");
        {
            Boot();
            Throws("H1 вырожденные и абсурдные аргументы", () =>
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.zero);
                Gizmo.DrawWireSphere(Vector3.zero, 0f);
                Gizmo.DrawWireSphere(Vector3.zero, -3f);
                Gizmo.DrawCube(Vector3.zero, Vector3.zero);
                Gizmo.DrawWireCube(Vector3.zero, new Vector3(-1, -1, -1));
                Gizmo.duration = -5f; Gizmo.DrawLine(Vector3.zero, Vector3.one); Gizmo.duration = 0f;
                Gizmo.lineWidth = -10f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.lineWidth = 1e9f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.Reset();
                Gizmo.DrawRay(Vector3.zero, Vector3.zero);
                Gizmo.DrawWireDisc(Vector3.zero, Vector3.up, 0f);
                Gizmo.DrawArrow(Vector3.zero, Vector3.zero);
                Gizmo.DrawBounds(new Bounds(Vector3.zero, Vector3.zero));
                Gizmo.DrawWireCapsule(Vector3.zero, Vector3.zero, 0f);
                Gizmo.DrawFrustum(Vector3.zero, 0f, 0f, 0f, 0f);
                Gizmo.DrawMesh(null, Vector3.zero);
                Gizmo.DrawWireMesh(null, Vector3.zero);
                Gizmo.DrawIcon(Vector3.zero, (Texture)null);
                Gizmo.DrawIcon(Vector3.zero, (string)null);
                Gizmo.DrawIcon(Vector3.zero, "");
                Gizmo.DrawLineList(new Vector3[0]);
                Gizmo.DrawLineStrip(new Vector3[0], true);
                Gizmo.DrawLineStrip(new Vector3[] { Vector3.zero }, true);
            }, false);

            Boot();
            Throws("H2 NaN и бесконечности не роняют", () =>
            {
                var nan = new Vector3(float.NaN, float.NaN, float.NaN);
                var inf = new Vector3(float.PositiveInfinity, 0, 0);
                Gizmo.DrawLine(nan, inf);
                Gizmo.DrawWireSphere(nan, float.NaN);
                Gizmo.DrawText("nan", nan, float.NaN);
                GizmoRenderer.BeginFrame(true);
            }, false);

            Boot();
            GizmoSettings.MaxVerticesPerChannel = 4096;
            Throws("H3 переполнение канала не роняет", () =>
            {
                for (int i = 0; i < 20000; i++) Gizmo.DrawLine(Vector3.zero, Vector3.one);
                GizmoRenderer.BeginFrame(true);
                GizmoRenderer.Submit(null);
            }, false);
            GizmoSettings.ResetOverrides();

            Boot();
            Throws("H4 Clear в любой момент", () =>
            {
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.Clear();
                GizmoRenderer.BeginFrame(true);
                Gizmo.Clear();
            }, false);

            Boot();
            Throws("H5 вложенные Scope восстанавливают состояние", () =>
            {
                Gizmo.color = Color.red;
                using (Gizmo.Scope(Color.green))
                using (Gizmo.Scope(Color.blue, Matrix4x4.identity))
                    Gizmo.DrawLine(Vector3.zero, Vector3.one);
            }, false);
            Check("H6 цвет восстановлен после Scope", Gizmo.color == Color.red);
        }

        // ==================================================== I. память
        Group("I. Память");
        {
            int live0 = Unity.Collections.NativeGuard.Count;
            Boot();
            for (int i = 0; i < 50; i++) Gizmo.DrawWireSphere(Vector3.zero, 1f);
            Gizmo.DrawText("hello world", Vector3.zero, 14f);
            GizmoRenderer.BeginFrame(true);
            GizmoRenderer.Dispose();
            int live1 = Unity.Collections.NativeGuard.Count;
            Check("I1 Dispose освобождает всю нативку", live1 <= live0, "было " + live0 + " стало " + live1);

            Throws("I2 цикл Dispose/использование 20 раз", () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    Boot();
                    Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
                    Gizmo.DrawText("42", Vector3.zero, 12f);
                    GizmoRenderer.BeginFrame(true);
                    GizmoRenderer.Dispose();
                }
            }, false);
        }

        // ==================================================== J. разметка вершин
        Group("J. Разметка вершин против sizeof");
        {
            int Sum(VertexAttributeDescriptor[] d) { int n = 0; foreach (var a in d) n += a.ByteSize; return n; }
            int st(Type t) => (int)typeof(System.Runtime.CompilerServices.Unsafe)
                .GetMethod("SizeOf").MakeGenericMethod(t).Invoke(null, null);

            Check("J1 Thin: 20 байт", Sum(GizmoVertexLayouts.Thin) == st(typeof(GizmoVertex)) && Sum(GizmoVertexLayouts.Thin) == 20,
                  "layout=" + Sum(GizmoVertexLayouts.Thin) + " struct=" + st(typeof(GizmoVertex)));
            Check("J2 Wide: 40 байт", Sum(GizmoVertexLayouts.Wide) == st(typeof(GizmoWideVertex)) && Sum(GizmoVertexLayouts.Wide) == 40,
                  "layout=" + Sum(GizmoVertexLayouts.Wide) + " struct=" + st(typeof(GizmoWideVertex)));
            Check("J3 Quad: 40 байт", Sum(GizmoVertexLayouts.Quad) == st(typeof(GizmoQuadVertex)) && Sum(GizmoVertexLayouts.Quad) == 40,
                  "layout=" + Sum(GizmoVertexLayouts.Quad) + " struct=" + st(typeof(GizmoQuadVertex)));
            Check("J4 Text: 44 байта", Sum(GizmoVertexLayouts.Text) == st(typeof(GizmoTextVertex)) && Sum(GizmoVertexLayouts.Text) == 44,
                  "layout=" + Sum(GizmoVertexLayouts.Text) + " struct=" + st(typeof(GizmoTextVertex)));

            // Position обязан лежать по смещению 0 — компактор retained читает *(Vector3*)ptr
            bool zero = true;
            foreach (var t in new[] { typeof(GizmoVertex), typeof(GizmoWideVertex), typeof(GizmoQuadVertex), typeof(GizmoTextVertex) })
                zero &= System.Runtime.InteropServices.Marshal.OffsetOf(t, "Position").ToInt32() == 0;
            Check("J5 Position по смещению 0 во всех вершинах", zero);

            // порядок атрибутов должен быть каноническим, иначе Unity ругается
            bool ordered = true;
            foreach (var d in new[] { GizmoVertexLayouts.Thin, GizmoVertexLayouts.Wide, GizmoVertexLayouts.Quad, GizmoVertexLayouts.Text })
                for (int i = 1; i < d.Length; i++) ordered &= (int)d[i].attribute > (int)d[i - 1].attribute;
            Check("J6 атрибуты в каноническом порядке", ordered);
        }

        // ==================================================== K. остальные каналы
        Group("K. Иконки, экран, меши");
        {
            Boot(); GizmoRenderer.Ensure();
            var tex1 = new Texture2D { name = "a" };
            var tex2 = new Texture2D { name = "b" };
            var mesh = new Mesh { name = "m", subMeshCount = 1 };
            Throws("K1 иконки, GUI-текстуры и меши не роняют", () =>
            {
                for (int i = 0; i < 200; i++)
                {
                    Gizmo.DrawIcon(Vector3.zero, tex1);
                    Gizmo.DrawIcon(Vector3.one, tex2);
                    Gizmo.DrawGUITexture(new Rect(0, 0, 10, 10), tex1);
                    Gizmo.DrawMesh(mesh, Vector3.zero);
                }
                GizmoRenderer.BeginFrame(true);
            }, false);

            Throws("K2 Submit по камере не роняет", () =>
            {
                var cam = new Camera { cameraType = CameraType.Game };
                cam.transform = new Transform();
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawText("hud", Vector3.zero, 12f);
                Gizmo.DrawIcon(Vector3.zero, tex1);
                GizmoRenderer.BeginFrame(true);
                GizmoRenderer.Submit(cam);
                GizmoRenderer.Submit(cam);          // две камеры за кадр
                GizmoRenderer.Submit(cam);
            }, false);

            Throws("K3 retained вперемешку со всем", () =>
            {
                for (int f = 0; f < 30; f++)
                {
                    Gizmo.duration = 0.05f;
                    Gizmo.DrawLine(Vector3.zero, Vector3.one);
                    Gizmo.DrawText("t" + f, Vector3.zero, 10f);
                    Gizmo.DrawWireSphere(Vector3.zero, 1f);
                    Gizmo.duration = 0f;
                    Gizmo.DrawCube(Vector3.zero, Vector3.one);
                    Tick(1000f + f * 0.016f);
                }
                Gizmo.Reset();
            }, false);
        }

        // ==================================================== L. канарейки
        Group("L. Выход за границы буферов");
        {
            Boot(); GizmoRenderer.Ensure();
            for (int f = 0; f < 40; f++)
            {
                Gizmo.lineWidth = (f % 3 == 0) ? 1f : 4f;      // оба пути линий
                Gizmo.depthTest = (f % 2 == 0);
                Gizmo.duration = (f % 4 == 0) ? 0.05f : 0f;
                for (int i = 0; i < 30; i++)
                {
                    Gizmo.DrawLine(Vector3.zero, Vector3.one);
                    Gizmo.DrawWireSphere(Vector3.zero, 1f);
                    Gizmo.DrawSphere(Vector3.zero, 1f);
                    Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
                    Gizmo.DrawCube(Vector3.zero, Vector3.one);
                    Gizmo.DrawText("!@#$%^&*()_+ Wg 0123", Vector3.zero, 13f);
                    Gizmo.DrawWireCapsule(Vector3.zero, Vector3.up, 0.5f);
                }
                Tick(2000f + f * 0.016f);
            }
            Gizmo.Reset();
            var broken = Unity.Collections.NativeGuard.Broken();
            Check("L1 ни один буфер не переписан за границу", broken.Count == 0,
                  string.Join("; ", broken));
            Throws("L2 Dispose не находит порчи", () => GizmoRenderer.Dispose(), false);
        }

        // ==================================================== M. меши и кэш каркасов
        Group("M. Сабмеши, кэш каркасов, мёртвые текстуры");
        {
            Boot(); GizmoRenderer.Ensure();
            var empty = new Mesh { name = "empty", subMeshCount = 0 };
            var one = new Mesh { name = "one", subMeshCount = 1 };
            var three = new Mesh { name = "three", subMeshCount = 3 };

            Throws("M1 пустой меш (subMeshCount = 0) не роняет", () =>
            {
                Gizmo.DrawMesh(empty, Vector3.zero);
                Gizmo.DrawMesh(empty, 0, Vector3.zero);
                Gizmo.DrawWireMesh(empty, Vector3.zero);
                Gizmo.DrawWireMesh(empty, 0, Vector3.zero);
            }, false);

            var meshes = Priv<GizmoMeshCmdList>(typeof(GizmoRenderer), "_meshBack");
            int n0 = meshes.Count;
            Gizmo.DrawMesh(empty, 0, Vector3.zero);
            Check("M2 пустой меш не даёт команду отрисовки", meshes.Count == n0);

            Throws("M3 сабмеш вне диапазона не роняет", () =>
            {
                Gizmo.DrawMesh(one, 99, Vector3.zero);
                Gizmo.DrawMesh(one, -5, Vector3.zero);
                Gizmo.DrawMesh(three, 100, Vector3.zero);
                Gizmo.DrawWireMesh(one, 99, Vector3.zero);
            }, false);

            bool inRange = true;
            for (int i = 0; i < meshes.Count; i++)
            {
                ref var c = ref meshes.Items[i];
                if (c.Mesh != null && (c.Submesh < 0 || c.Submesh >= c.Mesh.subMeshCount)) inRange = false;
            }
            Check("M4 все сабмеши в команде валидны", inRange);

            // кэш: разные заведомо невалидные индексы на односабмешевом меше — одна запись
            var cache = typeof(GizmoWireMeshCache)
                .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int Size() => (int)cache.GetType().GetProperty("Count").GetValue(cache);
            var fresh = new Mesh { name = "fresh", subMeshCount = 1 };
            int c0 = Size();
            var w0 = GizmoWireMeshCache.Get(fresh, 0);
            var w1 = GizmoWireMeshCache.Get(fresh, 5);
            var w2 = GizmoWireMeshCache.Get(fresh, 99);
            Check("M5 кламп до ключа: одна запись в кэше", Size() - c0 == 1, "прибавилось " + (Size() - c0));
            Check("M6 все три запроса дали один каркас",
                  w0 != null && ReferenceEquals(w0, w1) && ReferenceEquals(w1, w2));

            var t3 = new Mesh { name = "t3", subMeshCount = 3 };
            c0 = Size();
            GizmoWireMeshCache.Get(t3, 0); GizmoWireMeshCache.Get(t3, 1); GizmoWireMeshCache.Get(t3, 2);
            Check("M6b разные сабмеши — разные записи", Size() - c0 == 3, "прибавилось " + (Size() - c0));

            // уничтоженный источник не должен отдавать чужой каркас
            UnityEngine.Object.DestroyImmediate(one);
            var other = new Mesh { name = "other", subMeshCount = 1 };
            Throws("M7 уничтоженный источник не роняет", () => GizmoWireMeshCache.Get(other, 0), false);

            // мёртвые текстуры вычищаются
            Boot(); GizmoRenderer.Ensure();
            var icons = Priv<Dictionary<GizmoObjectId, GizmoTexturedBatch>>(typeof(GizmoRenderer), "_icons");
            var t1 = new Texture2D { name = "t1" };
            var t2 = new Texture2D { name = "t2" };
            Gizmo.DrawIcon(Vector3.zero, t1);
            Gizmo.DrawIcon(Vector3.one, t2);
            GizmoRenderer.BeginFrame(true);
            int had = icons.Count;
            Check("M8 два батча на две текстуры", had == 2, "" + had);
            UnityEngine.Object.DestroyImmediate(t1);
            GizmoRenderer.BeginFrame(true);
            Check("M9 батч мёртвой текстуры вычищен", icons.Count == 1, "" + icons.Count);
            UnityEngine.Object.DestroyImmediate(t2);
            GizmoRenderer.BeginFrame(true);
            Check("M10 вычищены все", icons.Count == 0, "" + icons.Count);
            Throws("M11 Submit по пустым словарям", () => GizmoRenderer.Submit(null), false);
        }

        // ==================================================== N. инварианты заливки
        Group("N. Инварианты заливки в меш");
        {
            Boot(); GizmoRenderer.Ensure();
            Throws("N1 смешанная нагрузка заливается без нарушений", () =>
            {
                for (int f = 0; f < 12; f++)
                {
                    Gizmo.lineWidth = (f % 2 == 0) ? 1f : 5f;
                    Gizmo.depthTest = (f % 3 != 0);
                    Gizmo.duration = (f % 2 == 0) ? 0.1f : 0f;
                    for (int i = 0; i < 40 + f * 37; i++)
                    {
                        Gizmo.DrawLine(Vector3.zero, Vector3.one);
                        Gizmo.DrawWireSphere(Vector3.one, 2f);
                        Gizmo.DrawSphere(Vector3.zero, 1f);
                        Gizmo.DrawText("Wq0.", Vector3.zero, 12f);
                    }
                    Gizmo.Reset();
                    Tick(3000f + f * 0.016f);
                    var cam = new Camera { cameraType = CameraType.Game }; cam.transform = new Transform();
                    GizmoRenderer.Submit(cam);
                }
            }, false);

            // каждый залитый меш проверяем на согласованность
            bool ok = true; string why = "";
            foreach (var m in Graphics.Last)
            {
                if (!m.SubSet) { ok = false; why = "submesh не задан"; break; }
                if (m.Sub.indexCount != m.Sub.vertexCount) { ok = false; why = "indexCount != vertexCount"; break; }
                if (m.Sub.indexCount > m.IBFilled) { ok = false; why = "индексов залито меньше"; break; }
                if (m.Sub.vertexCount > m.VBCap) { ok = false; why = "вершин больше ёмкости"; break; }
                if (m.Covered != m.Sub.vertexCount) { ok = false; why = "залито " + m.Covered + ", в submesh " + m.Sub.vertexCount; break; }
            }
            Check("N2 index/vertex count и покрытие записей сходятся", ok, why);

            bool strideOk = true;
            foreach (var m in Graphics.Last)
            {
                int stride = m.Sub.topology == MeshTopology.Lines ? 2 : 3;
                if (m.Sub.indexCount % stride != 0) { strideOk = false; break; }
            }
            Check("N3 количество вершин кратно топологии", strideOk);
            Check("N4 отрисовка вообще дошла до Graphics", Graphics.Calls > 0, "" + Graphics.Calls);
        }

        // ==================================================== O. синхронность retained
        Group("O. Синхронность retained-буферов");
        {
            bool InSync(object ch)
            {
                var t = ch.GetType();
                object ret = t.GetField("_retained", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ch);
                object exp = t.GetField("_retainedExpiry", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ch);
                int stride = (int)t.GetField("_primVerts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ch);
                int a = (int)ret.GetType().GetProperty("Count").GetValue(ret);
                int b = (int)exp.GetType().GetProperty("Count").GetValue(exp);
                return a == b && a % stride == 0;
            }
            bool AllInSync()
            {
                foreach (var name in new[] { "_thin", "_wide", "_tri", "_text" })
                {
                    var arr = (Array)typeof(GizmoRenderer).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    foreach (var ch in arr) if (!InSync(ch)) return false;
                }
                return true;
            }

            Boot(); GizmoRenderer.Ensure();
            Gizmo.duration = 0.2f;
            for (int i = 0; i < 100; i++)
            {
                Gizmo.lineWidth = (i % 2 == 0) ? 1f : 4f;
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawSphere(Vector3.zero, 1f);
                Gizmo.DrawText("ab", Vector3.zero, 11f);
            }
            Gizmo.Reset();
            Check("O1 вершины и времена жизни идут в ногу", AllInSync());

            // то же под потолком буфера
            Boot(); GizmoRenderer.Ensure();
            GizmoSettings.MaxVerticesPerChannel = 512;
            Gizmo.duration = 0.2f;
            for (int i = 0; i < 3000; i++)
            {
                Gizmo.lineWidth = (i % 2 == 0) ? 1f : 4f;
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                Gizmo.DrawText("xy", Vector3.zero, 11f);
            }
            Gizmo.Reset();
            Check("O2 не разъезжаются при переполнении", AllInSync());
            Throws("O3 кадр после переполнения проходит", () => Tick(4000f), false);
            Check("O4 после компактации всё ещё в ногу", AllInSync());
            GizmoSettings.ResetOverrides();

            // компактация с шириной примитива 6 (толстые линии и текст)
            Boot(); GizmoRenderer.Ensure();
            var wide = Priv<GizmoChannel<GizmoWideVertex>[]>(typeof(GizmoRenderer), "_wide");
            Tick(5000f);
            Gizmo.lineWidth = 4f;
            Gizmo.duration = 1f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.duration = 9f; Gizmo.DrawLine(Vector3.one, Vector3.zero);
            Gizmo.Reset();
            Tick(5002f); Tick(5002.02f);
            int left = wide[0].Target(true).Count;
            Check("O5 компактация толстых линий целыми квадами", left == 6, "осталось " + left);

            // retained и обычная геометрия видны одновременно
            Boot(); GizmoRenderer.Ensure();
            var thin2 = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            Tick(6000f);
            Gizmo.duration = 5f; Gizmo.DrawLine(Vector3.zero, Vector3.one); Gizmo.duration = 0f;
            Tick(6000.016f);
            Gizmo.DrawLine(Vector3.one, Vector3.zero);
            Tick(6000.032f);
            Graphics.Last.Clear();
            var c2 = new Camera { cameraType = CameraType.Game }; c2.transform = new Transform();
            GizmoRenderer.Submit(c2);
            bool both = false;
            foreach (var m in Graphics.Last) if (m.Sub.vertexCount == 4) both = true;
            Check("O6 retained и обычная геометрия в одном меше", both);
        }

        // ==================================================== P. состояние Gizmo
        Group("P. Состояние Gizmo");
        {
            Boot(); GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            var wide = Priv<GizmoChannel<GizmoWideVertex>[]>(typeof(GizmoRenderer), "_wide");

            thin[0].Clear();
            Gizmo.matrix = Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.identity, Vector3.one);
            Gizmo.DrawLine(Vector3.zero, Vector3.zero);
            Vector3 got; unsafe { got = thin[0].Target(false).Ptr[0].Position; }
            Check("P1 matrix применяется к вершинам", got == new Vector3(10f, 20f, 30f), got.ToString());
            Gizmo.matrix = Matrix4x4.identity;

            thin[0].Clear();
            Gizmo.color = Color.red;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Color32 cc; unsafe { cc = thin[0].Target(false).Ptr[0].Color; }
            Check("P2 цвет доезжает до вершины", cc.r == 255 && cc.g == 0 && cc.b == 0 && cc.a == 255,
                  cc.r + "," + cc.g + "," + cc.b + "," + cc.a);
            Gizmo.color = Color.white;

            thin[0].Clear(); thin[1].Clear();
            Gizmo.depthTest = true; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("P3 depthTest=true идёт в канал 0", thin[0].Target(false).Count == 2 && thin[1].Target(false).Count == 0);
            thin[0].Clear(); thin[1].Clear();
            Gizmo.depthTest = false; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("P4 depthTest=false идёт в канал 1", thin[1].Target(false).Count == 2 && thin[0].Target(false).Count == 0);
            Gizmo.depthTest = true;

            thin[0].Clear(); wide[0].Clear();
            Gizmo.lineWidth = 1f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("P5 lineWidth<=1 — тонкий путь", thin[0].Target(false).Count == 2 && wide[0].Target(false).Count == 0);
            thin[0].Clear(); wide[0].Clear();
            Gizmo.lineWidth = 3f; Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("P6 lineWidth>1 — квады", wide[0].Target(false).Count == 6 && thin[0].Target(false).Count == 0,
                  "wide=" + wide[0].Target(false).Count);
            Gizmo.Reset();

            thin[0].Clear();
            Gizmo.enabled = false;
            Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Gizmo.DrawText("x", Vector3.zero, 12f);
            Gizmo.DrawWireSphere(Vector3.zero, 1f);
            Check("P7 enabled=false глушит всё", thin[0].Target(false).Count == 0);
            Gizmo.enabled = true;

            Gizmo.matrix = Matrix4x4.identity;
            var before = Gizmo.matrix;
            using (Gizmo.Scope(Color.green, Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one)))
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("P8 Scope восстанавливает matrix", Gizmo.matrix == before);

            thin[0].Clear();
            Gizmo.DrawLineStrip(new[] { Vector3.zero, Vector3.one, Vector3.up }, false);
            int open = thin[0].Target(false).Count;
            thin[0].Clear();
            Gizmo.DrawLineStrip(new[] { Vector3.zero, Vector3.one, Vector3.up }, true);
            int closed = thin[0].Target(false).Count;
            Check("P9 looped добавляет замыкающий отрезок", closed == open + 2, open + " → " + closed);
        }

        // ==================================================== Q. фильтрация камер
        Group("Q. Фильтрация камер");
        {
            Camera Cam(CameraType t) { var c = new Camera { cameraType = t }; c.transform = new Transform(); return c; }
            int Draws(Camera cam, Action setup = null)
            {
                Boot(); GizmoRenderer.Ensure();
                setup?.Invoke();                    // Boot сбрасывает настройки, ставим после него
                Gizmo.DrawLine(Vector3.zero, Vector3.one);
                GizmoRenderer.BeginFrame(true);
                Graphics.Calls = 0;
                GizmoRenderer.Submit(cam);
                return Graphics.Calls;
            }
            Check("Q1 игровая камера рисует", Draws(Cam(CameraType.Game)) > 0);
            Check("Q2 Scene View рисует", Draws(Cam(CameraType.SceneView)) > 0);
            Check("Q3 превью-камера по умолчанию не рисует", Draws(Cam(CameraType.Preview)) == 0);
            Check("Q4 отражения по умолчанию не рисуют", Draws(Cam(CameraType.Reflection)) == 0);

            Check("Q5 DrawInGameView=false выключает игровую",
                  Draws(Cam(CameraType.Game), () => GizmoSettings.DrawInGameView = false) == 0);
            Check("Q6 DrawInSceneView=false выключает Scene View",
                  Draws(Cam(CameraType.SceneView), () => GizmoSettings.DrawInSceneView = false) == 0);
            Check("Q7 DrawInOtherCameras=true включает превью",
                  Draws(Cam(CameraType.Preview), () => GizmoSettings.DrawInOtherCameras = true) > 0);
            Check("Q8 null-камера не роняет", Draws(null) == 0);
        }

        // ==================================================== R. мировой режим текста
        Group("R. Текст в мировых единицах");
        {
            Boot(); GizmoRenderer.Ensure();
            var ch = Priv<GizmoChannel<GizmoTextVertex>[]>(typeof(GizmoRenderer), "_text");

            float MinW(GizmoChannel<GizmoTextVertex> c)
            {
                var b = c.Target(false); float m = float.MaxValue;
                unsafe { for (int i = 0; i < b.Count; i++) m = Math.Min(m, b.Ptr[i].Params.y); }
                return m;
            }
            float MaxMode(GizmoChannel<GizmoTextVertex> c)
            {
                var b = c.Target(false); float m = 0f;
                unsafe { for (int i = 0; i < b.Count; i++) m = Math.Max(m, b.Ptr[i].Params.z); }
                return m;
            }
            float MaxW(GizmoChannel<GizmoTextVertex> c)
            {
                var b = c.Target(false); float m = float.MinValue;
                unsafe { for (int i = 0; i < b.Count; i++) m = Math.Max(m, b.Ptr[i].Params.y); }
                return m;
            }

            ch[0].Clear();
            Gizmo.DrawText("Ab", Vector3.zero, 14f);
            int pxVerts = ch[0].Target(false).Count;
            Check("R1 пиксельный режим", MaxMode(ch[0]) == 0f && MinW(ch[0]) > 0f, "режим " + MaxMode(ch[0]));

            ch[0].Clear();
            Gizmo.DrawTextWorld("Ab", Vector3.zero, 0.5f);
            Check("R2 мировой режим помечен полем", MaxMode(ch[0]) == 1f, "режим " + MaxMode(ch[0]));
            Check("R3 геометрия та же, режим меняет только знак", ch[0].Target(false).Count == pxVerts,
                  ch[0].Target(false).Count + " против " + pxVerts);

            ch[0].Clear(); Gizmo.DrawTextWorld("A", Vector3.zero, 0.5f);
            float w05 = Math.Abs(MinW(ch[0]));
            ch[0].Clear(); Gizmo.DrawTextWorld("A", Vector3.zero, 1.0f);
            float w10 = Math.Abs(MinW(ch[0]));
            Check("R4 толщина штриха пропорциональна высоте", Math.Abs(w10 - w05 * 2f) < 1e-5f,
                  w05 + " → " + w10);

            Gizmo.lineWidth = 2f;
            ch[0].Clear(); Gizmo.DrawTextWorld("A", Vector3.zero, 0.5f);
            float wBold = Math.Abs(MinW(ch[0]));
            Check("R5 lineWidth задаёт жирность и в мировом режиме", Math.Abs(wBold - w05 * 2f) < 1e-5f,
                  w05 + " → " + wBold);
            Gizmo.Reset();

            Throws("R6 краевые значения мирового текста", () =>
            {
                Gizmo.DrawTextWorld("x", Vector3.zero, 0f);
                Gizmo.DrawTextWorld("x", Vector3.zero, -1f);
                Gizmo.DrawTextWorld(null, Vector3.zero, 1f);
                Gizmo.DrawTextWorld("", Vector3.zero, 1f);
                Gizmo.DrawTextWorld("длинный ник игрока", Vector3.zero, Color.red, 0.3f);
                Gizmo.DrawTextWorld("смещение", Vector3.zero, 0.3f, new Vector2(0f, 0.5f), GizmoTextAlign.Left);
                Gizmo.lineWidth = 0f; Gizmo.DrawTextWorld("x", Vector3.zero, 0.3f);
                Gizmo.Reset();
            }, false);

            ch[0].Clear();
            Gizmo.lineWidth = 0f;
            Gizmo.DrawTextWorld("A", Vector3.zero, 0.5f);
            Check("R7 lineWidth=0 не даёт нулевой штрих в мировом режиме", Math.Abs(MinW(ch[0])) > 0f);
            Gizmo.Reset();

            // оба режима в одном кадре должны сосуществовать
            Boot(); GizmoRenderer.Ensure();
            Throws("R8 оба режима вперемешку в одном кадре", () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    Gizmo.DrawText("px " + i, Vector3.zero, 12f);
                    Gizmo.DrawTextWorld("world " + i, Vector3.one, 0.2f);
                }
                Tick(7000f);
                var cam = new Camera { cameraType = CameraType.Game }; cam.transform = new Transform();
                GizmoRenderer.Submit(cam);
            }, false);
        }

        // ==================================================== S. паттерны
        Group("S. Паттерны");
        {
            Boot(); GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");

            Throws("S1 вырожденные объёмы не роняют", () =>
            {
                Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, Vector3.zero);
                Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, new Vector3(-1f, -1f, -1f));
                Gizmo.DrawVolume(new Bounds(Vector3.zero, Vector3.zero));
                Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, Vector3.one, 0f, 0f);
                Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, Vector3.one, 99f, 99f);
                Gizmo.DrawVolume((Transform)null);
            }, false);

            Gizmo.color = Color.red;
            Gizmo.lineWidth = 3f;
            Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, Vector3.one);
            Check("S2 DrawVolume восстанавливает цвет", Gizmo.color == Color.red);

            Gizmo.DrawLink(new Bounds(Vector3.zero, Vector3.one),
                           new Bounds(new Vector3(10f, 0f, 0f), Vector3.one), Color.green, 7f, "связь");
            Check("S3 DrawLink восстанавливает цвет", Gizmo.color == Color.red);
            Check("S4 DrawLink восстанавливает толщину", Math.Abs(Gizmo.lineWidth - 3f) < 1e-5f,
                  "" + Gizmo.lineWidth);
            Gizmo.Reset();

            Throws("S5 краевые случаи связи", () =>
            {
                // совпадающие центры, пересекающиеся габариты, нули
                Gizmo.DrawLink(new Bounds(Vector3.zero, Vector3.one),
                               new Bounds(Vector3.zero, Vector3.one), Color.white);
                Gizmo.DrawLink(new Bounds(Vector3.zero, Vector3.one),
                               new Bounds(new Vector3(0.1f, 0f, 0f), Vector3.one), Color.white);
                Gizmo.DrawLink(new Bounds(Vector3.zero, Vector3.zero),
                               new Bounds(new Vector3(5f, 0f, 0f), Vector3.zero), Color.white);
                Gizmo.DrawLink((Transform)null, (Transform)null, Color.white);
                Gizmo.DrawLink(new Bounds(Vector3.zero, Vector3.one),
                               new Bounds(new Vector3(1e6f, 0f, 0f), Vector3.one), Color.white, 1f, "далеко");
            }, false);

            Throws("S6 краевые случаи пути", () =>
            {
                Gizmo.DrawPath(null);
                Gizmo.DrawPath(new Vector3[0]);
                Gizmo.DrawPath(new[] { Vector3.zero });
                Gizmo.DrawPath(new[] { Vector3.zero, Vector3.zero }, 0f, 0);
                Gizmo.DrawPath(new[] { Vector3.zero, Vector3.one, Vector3.up }, 0.1f, 1, true);
                Gizmo.DrawPath(new[] { Vector3.zero, Vector3.one }, -1f, -5);
            }, false);

            Throws("S7 краевые случаи вектора, радиуса, обзора и попадания", () =>
            {
                Gizmo.DrawVector(Vector3.zero, Vector3.zero);
                Gizmo.DrawVector(Vector3.zero, Vector3.up, 1f, "");
                Gizmo.DrawVector(Vector3.zero, Vector3.up, 0f, "подпись");

                Gizmo.DrawRange(Vector3.zero, 0f);
                Gizmo.DrawRange(Vector3.zero, -3f);
                Gizmo.DrawRange(Vector3.zero, 2f, 1.5f);

                Gizmo.DrawFieldOfView(Vector3.zero, Vector3.up, 90f, 5f);      // строго вертикальный взгляд
                Gizmo.DrawFieldOfView(Vector3.zero, Vector3.zero, 90f, 5f);    // нулевое направление
                Gizmo.DrawFieldOfView(Vector3.zero, Vector3.forward, 0f, 5f);
                Gizmo.DrawFieldOfView(Vector3.zero, Vector3.forward, 999f, 5f);
                Gizmo.DrawFieldOfView(Vector3.zero, Vector3.forward, 90f, 0f);

                Gizmo.DrawHit(Vector3.zero, Vector3.zero);
                Gizmo.DrawHit(Vector3.zero, Vector3.up, 0f);
            }, false);

            thin[0].Clear();
            Gizmo.DrawVolume(Vector3.zero, Quaternion.identity, Vector3.one);
            Check("S8 объём вообще что-то рисует", thin[0].Target(false).Count > 0);

            Check("S9 WorldBounds на null не падает",
                  Gizmo.WorldBounds(null).size == Vector3.zero);

            // --- размеры
            thin[0].Clear();
            Gizmo.DrawMeasure(Vector3.zero, new Vector3(5f, 0f, 0f), null);
            int measureVerts = thin[0].Target(false).Count;
            // линия + 2 стрелки по 2 отрезка = 5 отрезков = 10 вершин
            Check("S11 замер — линия и две стрелки", measureVerts == 10, "вершин=" + measureVerts);

            thin[0].Clear();
            Gizmo.DrawDimension(Vector3.zero, new Vector3(5f, 0f, 0f), Vector3.down, 2f, null);
            int dimVerts = thin[0].Target(false).Count;
            // + 2 выносные линии
            Check("S12 вынос добавляет две выносные линии", dimVerts == measureVerts + 4,
                  "вершин=" + dimVerts);

            thin[0].Clear();
            Gizmo.DrawDimension(Vector3.zero, new Vector3(5f, 0f, 0f), Vector3.down, 2f, "");
            Check("S13 пустая подпись добавляет текст", thin[0].Target(false).Count == dimVerts,
                  "тонких вершин=" + thin[0].Target(false).Count);
            var txt = Priv<GizmoChannel<GizmoTextVertex>[]>(typeof(GizmoRenderer), "_text");
            Check("S13b подпись ушла в текстовый канал", txt[0].Target(false).Count > 0);

            Throws("S14 краевые случаи размеров", () =>
            {
                Gizmo.DrawMeasure(Vector3.zero, Vector3.zero);
                Gizmo.DrawMeasure(Vector3.zero, Vector3.up, "своя подпись", 0.5f);
                Gizmo.DrawMeasure(Vector3.zero, Vector3.up, null, -5f);

                Gizmo.DrawDimension(Vector3.zero, Vector3.zero, Vector3.down, 2f);   // нулевая ширина
                Gizmo.DrawDimension(Vector3.zero, Vector3.one, Vector3.zero, 2f);     // нулевое направление
                Gizmo.DrawDimension(Vector3.zero, Vector3.one, Vector3.down, 0f);     // размерная прямо на точках
                Gizmo.DrawDimension(Vector3.zero, Vector3.one, Vector3.down, -3f);    // вынос в минус
                Gizmo.DrawDimension(Vector3.zero, Vector3.one, Vector3.one, 2f);      // вынос вдоль замера
                Gizmo.DrawDimension(Vector3.zero, Vector3.right, 5f, Vector3.down, 2f, "55");
                Gizmo.DrawDimension(Vector3.zero, Vector3.zero, 5f, Vector3.down, 2f);
                Gizmo.DrawDimension(Vector3.zero, Vector3.one, Vector3.down, 2f, "", 0.2f, 0.5f, 0.3f);
            }, false);

            Throws("S10 NaN в паттернах", () =>
            {
                var nan = new Vector3(float.NaN, float.NaN, float.NaN);
                Gizmo.DrawVolume(nan, Quaternion.identity, nan);
                Gizmo.DrawLink(new Bounds(nan, Vector3.one), new Bounds(Vector3.zero, Vector3.one), Color.white);
                Gizmo.DrawVector(nan, nan);
                Gizmo.DrawHit(nan, nan);
                Gizmo.DrawPath(new[] { nan, Vector3.zero });
                Gizmo.DrawMeasure(nan, Vector3.zero);
                Gizmo.DrawDimension(nan, Vector3.one, nan, float.NaN);
                GizmoRenderer.BeginFrame(true);
            }, false);
        }

        // ==================================================== T. расширения
        Group("T. Расширения");
        {
            Boot(); GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");

            Throws("T1 все расширения на null не роняют", () =>
            {
                ((Transform)null).DrawVolume(Color.red);
                ((Transform)null).DrawVolume();
                ((Transform)null).DrawLabel("x");
                ((Transform)null).DrawLinkTo(null, Color.red);
                ((Transform)null).DrawAxes();
                ((Transform)null).DrawForward();
                ((Transform)null).DrawBounds(Color.red);
                ((Transform)null).DrawHierarchy(Color.red);
                ((IReadOnlyList<Vector3>)null).DrawPath(Color.red);
                ((IReadOnlyList<Transform>)null).DrawVolumes(Color.red);
                ((Mesh)null).DrawNormals(null, Color.red);
                ((Mesh)null).DrawWire(null, Color.red);
                ((Renderer)null).DrawBounds(Color.red);
                ((Camera)null).DrawFrustum(Color.red);
                ((Light)null).DrawRange(Color.red);
                ((RectTransform)null).DrawWorldCorners(Color.red);
                ((Collider)null).DrawShape(Color.red);
                ((Collider)null).DrawBounds(Color.red);
                ((Rigidbody)null).DrawVelocity(Color.red);
                ((Rigidbody)null).DrawAngularVelocity(Color.red);
                ((Rigidbody)null).DrawCenterOfMass(Color.red);
                ((Joint)null).DrawAnchors(Color.red);
                ((Collider2D)null).DrawShape(Color.red);
                ((Collider2D)null).DrawBounds(Color.red);
                ((Rigidbody2D)null).DrawVelocity(Color.red);
                ((Rigidbody2D)null).DrawAngularVelocity(Color.red);
                ((Rigidbody2D)null).DrawCenterOfMass(Color.red);
                ((AudioSource)null).DrawDistances(Color.red);
                default(RaycastHit2D).Draw(Color.red);
            }, false);

            // формы коллайдеров: каждый тип должен уйти в свою ветку
            Transform T() { var t = new Transform(); t.lossyScale = Vector3.one; return t; }
            int Draws(Action act)
            {
                thin[0].Clear();
                act();
                return thin[0].Target(false).Count;
            }

            var box = new BoxCollider { size = Vector3.one }; box.transform = T();
            var sph = new SphereCollider { radius = 1f }; sph.transform = T();
            var cap = new CapsuleCollider { radius = 0.5f, height = 2f, direction = 1 }; cap.transform = T();
            var cc = new CharacterController { radius = 0.5f, height = 2f }; cc.transform = T();
            var terrain = new Collider { bounds = new Bounds(Vector3.zero, Vector3.one) }; terrain.transform = T();

            Check("T2 BoxCollider рисует куб", Draws(() => box.DrawShape(Color.red)) == 24,
                  "" + Draws(() => box.DrawShape(Color.red)));
            Check("T3 SphereCollider рисует сферу", Draws(() => sph.DrawShape(Color.red)) > 0);
            Check("T4 CapsuleCollider рисует капсулу", Draws(() => cap.DrawShape(Color.red)) > 0);
            Check("T5 CharacterController рисует капсулу", Draws(() => cc.DrawShape(Color.red)) > 0);
            Check("T6 неизвестный тип падает на габариты", Draws(() => terrain.DrawShape(Color.red)) == 24,
                  "" + Draws(() => terrain.DrawShape(Color.red)));

            Throws("T7 вырожденные коллайдеры", () =>
            {
                var z = new BoxCollider { size = Vector3.zero }; z.transform = T();
                z.DrawShape(Color.red);
                var zs = new SphereCollider { radius = 0f }; zs.transform = T();
                zs.DrawShape(Color.red);
                var zc = new CapsuleCollider { radius = 0f, height = 0f, direction = 0 }; zc.transform = T();
                zc.DrawShape(Color.red);
                zc.direction = 2; zc.DrawShape(Color.red);
                zc.direction = 99; zc.DrawShape(Color.red);          // мусорное направление
                var neg = new BoxCollider { size = new Vector3(-1f, -1f, -1f) };
                neg.transform = T(); neg.transform.lossyScale = new Vector3(-2f, 0f, 3f);
                neg.DrawShape(Color.red);                            // отрицательный и нулевой масштаб
            }, false);

            Throws("T8 2D-коллайдеры всех типов", () =>
            {
                var b2 = new BoxCollider2D { size = Vector2.zero }; b2.transform = T(); b2.DrawShape(Color.red);
                b2.size = new Vector2(2f, 1f); b2.DrawShape(Color.red);
                var c2 = new CircleCollider2D { radius = 0f }; c2.transform = T(); c2.DrawShape(Color.red);
                c2.radius = 1f; c2.DrawShape(Color.red);
                var cp = new CapsuleCollider2D { size = new Vector2(1f, 3f) }; cp.transform = T();
                cp.DrawShape(Color.red);
                cp.direction = CapsuleDirection2D.Horizontal; cp.DrawShape(Color.red);
                cp.size = Vector2.zero; cp.DrawShape(Color.red);      // капсула нулевого размера
                var pl = new PolygonCollider2D { pathCount = 0 }; pl.transform = T(); pl.DrawShape(Color.red);
                pl.pathCount = 3; pl.DrawShape(Color.red);            // пути пустые — контур не рисуется
                var ed = new EdgeCollider2D(); ed.transform = T(); ed.DrawShape(Color.red);
            }, false);

            Throws("T9 Rigidbody и джойнты", () =>
            {
                var rb = new Rigidbody(); rb.transform = T();
                rb.DrawVelocity(Color.red);                            // нулевая скорость
                rb.DrawAngularVelocity(Color.red);                     // нулевое вращение
                rb.DrawCenterOfMass(Color.red);
                rb.linearVelocity = Vector3.up * 5f; rb.angularVelocity = Vector3.up * 3f;
                rb.DrawVelocity(Color.red); rb.DrawAngularVelocity(Color.red);

                var rb2 = new Rigidbody2D(); rb2.transform = T();
                rb2.DrawVelocity(Color.red); rb2.DrawAngularVelocity(Color.red); rb2.DrawCenterOfMass(Color.red);
                rb2.angularVelocity = -180f; rb2.DrawAngularVelocity(Color.red);   // отрицательное вращение

                var j = new Joint { axis = Vector3.up }; j.transform = T();
                j.DrawAnchors(Color.red);                              // без connectedBody
                j.connectedBody = rb; j.DrawAnchors(Color.red);
                j.axis = Vector3.zero; j.DrawAnchors(Color.red);       // нулевая ось
            }, false);

            Throws("T10 рейкасты и свет", () =>
            {
                var ray = new Ray(Vector3.zero, Vector3.forward);
                ray.Draw(Color.red);
                ray.Draw(Color.red, 0f);
                var hit = new RaycastHit { point = Vector3.forward * 3f, normal = Vector3.up, distance = 3f };
                hit.Draw(Color.red);
                ray.DrawTo(hit, 10f, Color.green);
                ray.DrawTo(hit, 0f, Color.green);                       // попадание дальше maxDistance
                ray.DrawTo(default, 10f, Color.green);                  // пустое попадание

                foreach (var lt in new[] { LightType.Point, LightType.Spot, LightType.Directional,
                                           LightType.Rectangle, LightType.Disc })
                {
                    var li = new Light { type = lt, range = 5f, spotAngle = 60f }; li.transform = T();
                    li.DrawRange(Color.red);
                }
                var l0 = new Light { type = LightType.Spot, range = 0f, spotAngle = 0f }; l0.transform = T();
                l0.DrawRange(Color.red);
            }, false);

            // состояние обязано восстанавливаться: Scope внутри каждого расширения
            Gizmo.color = Color.magenta;
            Gizmo.lineWidth = 5f;
            box.DrawShape(Color.green);
            sph.DrawShape(Color.blue);
            new Ray(Vector3.zero, Vector3.up).Draw(Color.white);
            Check("T11 расширения не портят цвет", Gizmo.color == Color.magenta);
            Check("T12 расширения не портят толщину", Math.Abs(Gizmo.lineWidth - 5f) < 1e-5f);
            Gizmo.Reset();

            // иерархия и коллекции
            Throws("T13 иерархия и коллекции", () =>
            {
                var root = T();
                root.DrawHierarchy(Color.red, 0);
                root.DrawHierarchy(Color.red, -5);
                root.DrawHierarchy(Color.red, 100, 0f);
                new Vector3[] { Vector3.zero, Vector3.one }.DrawPath(Color.red);
                new Transform[] { null, T(), null }.DrawVolumes(Color.red);
                new Bounds(Vector3.zero, Vector3.one).Draw(Color.red);
                new Bounds(Vector3.zero, Vector3.one).DrawVolume(Color.red);
            }, false);

            // в релизе расширения должны исчезать вместе с аргументами
            Check("T14 расширения помечены Conditional", ConditionalCount() >= 28,
                  "помечено " + ConditionalCount());
        }

        // ==================================================== U. растеризация текста
        Group("U. Растеризация текста по данным для GPU");
        {
            Boot(); GizmoRenderer.Ensure();

            foreach (var probe in new[] { "Ж", "n", "R", "щ", "8" })
            {
                string img = Rast.Render(probe, 18f, 2f);
                bool bad = img.StartsWith("(");
                Check("U-квад '" + probe + "' собран корректно", !bad, bad ? img : "");
                if (!bad) { Console.WriteLine("        " + probe + ":"); Console.WriteLine(Indent(img)); }
            }

            string wimg = Rast.Render("Ab", 1f, 1f, world: true);
            Check("U-мировой режим тоже даёт корректные квады", !wimg.StartsWith("("), wimg);

            // толщина штриха обязана менять площадь заливки, а не только края
            int Ink(string s2) { int k = 0; foreach (var c2 in s2) if (c2 == '#') k++; return k; }
            int thin2 = Ink(Rast.Render("H", 24f, 1f));
            int thick = Ink(Rast.Render("H", 24f, 5f));
            Check("U-толщина влияет на заливку", thick > thin2 * 2, thin2 + " → " + thick);

            Gizmo.Reset();
        }

        // ==================================================== W. GizmoLazy
        Group("W. Ленивая отладка (GizmoLazy)");
        {
            Boot(); GizmoRenderer.Ensure();
            GizmoLazy.Clear();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            int Verts() => thin[0].Target(false).Count;

            Transform T(string n) { var t = new Transform { name = n }; t.lossyScale = Vector3.one; return t; }
            var enemy = T("enemy");

            GizmoLazy.Track(enemy).Volume(Color.red);
            Check("W1 регистрация добавилась", GizmoLazy.Count == 1, "" + GizmoLazy.Count);

            thin[0].Clear(); Registry.Tick();
            Check("W2 тик рисует", Verts() > 0, "" + Verts());
            thin[0].Clear(); Registry.Tick();
            Check("W3 и рисует снова следующим кадром", Verts() > 0);

            // повтор с той же строки заменяет
            int b4 = GizmoLazy.Count;
            for (int i = 0; i < 50; i++) GizmoLazy.Track(enemy).Volume(Color.red);
            Check("W4 пятьдесят вызовов с одной строки — одна запись",
                  GizmoLazy.Count - b4 == 1, "прибавилось " + (GizmoLazy.Count - b4));

            // разные команды с одной строки не затирают друг друга
            GizmoLazy.Clear();
            GizmoLazy.Track(enemy).Volume(); GizmoLazy.Track(enemy).Axes(); GizmoLazy.Track(enemy).Forward();
            Check("W5 разные команды сосуществуют", GizmoLazy.Count == 3, "" + GizmoLazy.Count);

            // смерть цели снимает
            GizmoLazy.Clear();
            var doomed = T("doomed");
            GizmoLazy.Track(doomed).Volume();
            UnityEngine.Object.DestroyImmediate(doomed);
            thin[0].Clear(); Registry.Tick();
            Check("W6 смерть цели снимает регистрацию", GizmoLazy.Count == 0 && Verts() == 0);

            // время
            GizmoLazy.Clear();
            Time.realtimeSinceStartup = 2000f;
            GizmoLazy.Track(enemy).For(0.5f).Volume();
            thin[0].Clear(); Registry.Tick();
            Check("W7 срочная регистрация рисует", Verts() > 0);
            Time.realtimeSinceStartup = 2000.6f;
            thin[0].Clear(); Registry.Tick();
            Check("W8 после срока снимается", GizmoLazy.Count == 0 && Verts() == 0);

            // ключ
            GizmoLazy.Clear();
            var a1 = T("a"); var a2 = T("b");
            foreach (var t in new[] { a1, a2 }) GizmoLazy.Track(t).Key(t.name).Volume();
            Check("W9 явный ключ разводит цели в цикле", GizmoLazy.Count == 2, "" + GizmoLazy.Count);
            GizmoLazy.Untrack(a1, "a");
            Check("W10 снятие по ключу", GizmoLazy.Count == 1);
            GizmoLazy.Untrack(a2);
            Check("W11 снятие по цели", GizmoLazy.Count == 0);

            // исключение изолируется
            GizmoLazy.Clear();
            GizmoLazy.Track(enemy).Key("bad").Draw(() => throw new InvalidOperationException("тест"));
            GizmoLazy.Track(enemy).Key("good").Draw(() => Gizmo.DrawLine(Vector3.zero, Vector3.one));
            thin[0].Clear();
            Throws("W12 исключение не роняет тик", () => Registry.Tick(), false);
            Check("W13 виновник снят, сосед жив", GizmoLazy.Count == 1, "" + GizmoLazy.Count);
            Check("W14 сосед всё ещё рисует", Verts() == 2, "" + Verts());

            // состояние не протекает
            GizmoLazy.Clear();
            GizmoLazy.Track(enemy).Key("s1").Draw(() => { Gizmo.color = Color.red; Gizmo.lineWidth = 9f; });
            GizmoLazy.Track(enemy).Key("s2").Draw(() => Gizmo.DrawLine(Vector3.zero, Vector3.one));
            thin[0].Clear(); Registry.Tick();
            Check("W15 состояние не протекает между записями", Verts() == 2,
                  "толщина 9 увела бы в канал квадов, вершин=" + Verts());

            // потолок
            GizmoLazy.Clear();
            GizmoLazy.MaxTracked = 8;
            for (int i = 0; i < 100; i++) GizmoLazy.Track(T("g" + i)).Key("k" + i).Volume();
            Check("W16 потолок соблюдается", GizmoLazy.Count == 8, "" + GizmoLazy.Count);
            GizmoLazy.MaxTracked = 256;

            // выключатели
            GizmoLazy.Clear();
            GizmoLazy.Track(enemy).Volume();
            GizmoLazy.Enabled = false;
            thin[0].Clear(); Registry.Tick();
            Check("W17 GizmoLazy.Enabled=false глушит слой", Verts() == 0);
            GizmoLazy.Enabled = true;
            GizmoRenderer.Enabled = false;
            thin[0].Clear(); Registry.Tick();
            Check("W18 Gizmo.enabled=false тоже глушит", Verts() == 0);
            GizmoRenderer.Enabled = true;

            // физика
            GizmoLazy.Clear();
            var box = new BoxCollider { size = Vector3.one }; box.transform = T("box");
            var rb = new Rigidbody { linearVelocity = Vector3.up * 3f }; rb.transform = T("rb");
            GizmoLazy.Track(box).Shape(Color.green);
            GizmoLazy.Track(rb).Velocity(Color.cyan);
            thin[0].Clear();
            Throws("W19 команды физики не роняют", () => Registry.Tick(), false);
            Check("W20 и что-то рисуют", Verts() > 0, "" + Verts());

            // все команды разом
            GizmoLazy.Clear();
            Throws("W21 все команды подряд", () =>
            {
                var t = T("all"); var o = T("other");
                GizmoLazy.Track(t).Volume();
                GizmoLazy.Track(t).Bounds();
                GizmoLazy.Track(t).Label("подпись");
                GizmoLazy.Track(t).Label("мировая", Color.red, 0.3f);
                GizmoLazy.Track(t).LinkTo(o);
                GizmoLazy.Track(t).Axes(2f);
                GizmoLazy.Track(t).Forward(3f);
                GizmoLazy.Track(t).Range(5f);
                GizmoLazy.Track(t).Range(5f, Color.red, 2f);
                GizmoLazy.Track(t).FieldOfView(70f, 8f);
                GizmoLazy.Track(t).Hierarchy();
                GizmoLazy.Track(t).For(1f).Key("k").Volume();
                Registry.Tick();
            }, false);
            Check("W22 все команды зарегистрировались", GizmoLazy.Count == 12, "" + GizmoLazy.Count);

            // краевые случаи
            GizmoLazy.Clear();
            Throws("W23 краевые случаи", () =>
            {
                GizmoLazy.Track((Transform)null).Volume();
                GizmoLazy.Track((GameObject)null).Volume();
                GizmoLazy.Track((Component)null).Shape();
                GizmoLazy.Track(enemy).Draw(null);
                GizmoLazy.Track(enemy).LinkTo(null);
                GizmoLazy.Track(enemy).For(-5f).Volume();
                GizmoLazy.Untrack(null);
                GizmoLazy.Untrack(null, "x");
                Registry.Tick();
            }, false);
            Check("W24 null-цели не регистрируются", GizmoLazy.Count <= 3, "" + GizmoLazy.Count);

            GizmoLazy.Clear();
            Check("W25 Clear снимает всё", GizmoLazy.Count == 0);
            Gizmo.Reset();
        }

        // ==================================================== X. новое в 1.2
        Group("X. Многострочность, пунктир, полоса, экран, кривые");
        {
            Boot(); GizmoRenderer.Ensure();
            var thin = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_thin");
            var wide = Priv<GizmoChannel<GizmoWideVertex>[]>(typeof(GizmoRenderer), "_wide");
            var txt = Priv<GizmoChannel<GizmoTextVertex>[]>(typeof(GizmoRenderer), "_text");
            int TV() => txt[0].Target(false).Count;

            // --- многострочность
            GizmoFont.Ensure();
            GizmoFont.Measure("ab", out int l1, out float w1);
            GizmoFont.Measure("ab\ncde", out int l2, out float w2);
            GizmoFont.Measure("ab\r\ncde", out int l3, out float w3);
            Check("X1 одна строка", l1 == 1);
            Check("X2 две строки", l2 == 2 && l3 == 2);
            Check("X3 ширина по самой длинной", w2 > w1 && Math.Abs(w2 - w3) < 1e-4f,
                  w1 + " / " + w2 + " / " + w3);

            txt[0].Clear(); Gizmo.DrawText("ab", Vector3.zero, 14f); int one = TV();
            txt[0].Clear(); Gizmo.DrawText("ab\nab", Vector3.zero, 14f); int two = TV();
            Check("X4 перенос строки удваивает геометрию", two == one * 2, one + " → " + two);

            txt[0].Clear(); Gizmo.DrawText("ab\r\nab", Vector3.zero, 14f);
            Check("X5 CRLF не рисует лишнего", TV() == two, TV() + " против " + two);

            float MinY(GizmoChannel<GizmoTextVertex> c)
            { var b=c.Target(false); float m=float.MaxValue; unsafe{for(int i=0;i<b.Count;i++) m=Math.Min(m,b.Ptr[i].Offset.y);} return m; }
            float MaxY(GizmoChannel<GizmoTextVertex> c)
            { var b=c.Target(false); float m=float.MinValue; unsafe{for(int i=0;i<b.Count;i++) m=Math.Max(m,b.Ptr[i].Offset.y);} return m; }
            txt[0].Clear(); Gizmo.DrawText("ab\nab", Vector3.zero, 14f);
            Check("X6 строки разнесены по вертикали", MaxY(txt[0]) - MinY(txt[0]) > 14f,
                  "разброс " + (MaxY(txt[0]) - MinY(txt[0])));

            // --- пунктир
            float MinDash(GizmoChannel<GizmoVertex> c)
            { var b=c.Target(false); float m=float.MaxValue; unsafe{for(int i=0;i<b.Count;i++) m=Math.Min(m,b.Ptr[i].Dash);} return m; }
            float MaxDash(GizmoChannel<GizmoVertex> c)
            { var b=c.Target(false); float m=float.MinValue; unsafe{for(int i=0;i<b.Count;i++) m=Math.Max(m,b.Ptr[i].Dash);} return m; }

            thin[0].Clear(); Gizmo.dash = 0f; Gizmo.DrawLine(Vector3.zero, Vector3.one * 10f);
            Check("X7 сплошная помечена -1", MaxDash(thin[0]) < 0f, "" + MaxDash(thin[0]));

            thin[0].Clear(); Gizmo.dash = 0.5f; Gizmo.DrawLine(Vector3.zero, new Vector3(4f, 0f, 0f));
            Check("X8 пунктир даёт растущую фазу", MinDash(thin[0]) >= 0f && MaxDash(thin[0]) > 7f,
                  MinDash(thin[0]) + ".." + MaxDash(thin[0]));

            thin[0].Clear(); Gizmo.DrawLine(Vector3.zero, new Vector3(4f, 0f, 0f));
            Check("X9 фаза продолжается между вызовами", MinDash(thin[0]) > 7f, "" + MinDash(thin[0]));

            var tri = Priv<GizmoChannel<GizmoVertex>[]>(typeof(GizmoRenderer), "_tri");
            tri[0].Clear(); Gizmo.DrawCube(Vector3.zero, Vector3.one);
            Check("X10 у заливки пунктира нет и мусора тоже", MaxDash(tri[0]) < 0f, "" + MaxDash(tri[0]));

            thin[0].Clear(); Gizmo.DrawWireSphere(Vector3.zero, 1f);
            Check("X11 пунктир доезжает до пакетных примитивов", MinDash(thin[0]) >= 0f);

            Gizmo.lineWidth = 4f; wide[0].Clear();
            Gizmo.DrawLine(Vector3.zero, new Vector3(4f, 0f, 0f));
            float wd; unsafe { wd = wide[0].Target(false).Ptr[0].Params.z; }
            Check("X12 толстые линии тоже пунктирные", wd >= 0f, "" + wd);
            Gizmo.Reset();

            thin[0].Clear(); Gizmo.DrawLine(Vector3.zero, Vector3.one);
            Check("X13 Reset возвращает сплошную", MaxDash(thin[0]) < 0f);

            // --- полоса
            txt[0].Clear(); Gizmo.DrawBar(Vector3.zero, 0.5f);
            Check("X14 полоса — фон плюс заливка", TV() == 12, "" + TV());
            txt[0].Clear(); Gizmo.DrawBar(Vector3.zero, 0f);
            Check("X15 пустая полоса — только фон", TV() == 6, "" + TV());
            txt[0].Clear(); Gizmo.DrawBar(Vector3.zero, 5f);
            Check("X16 заполнение зажимается", TV() == 12, "" + TV());
            Throws("X17 краевые случаи полосы", () =>
            {
                Gizmo.DrawBar(Vector3.zero, float.NaN);
                Gizmo.DrawBar(Vector3.zero, 0.5f, 0f, 0f);
                Gizmo.DrawBar(Vector3.zero, 0.5f, -10f, -1f);
                Gizmo.DrawBar(Vector3.zero, 0.5f, Color.red, Color.gray, 60f, 8f, 20f);
                Gizmo.DrawBarWorld(Vector3.zero, 0.7f);
                Gizmo.DrawBarWorld(Vector3.zero, 0.7f, 0f, 0f);
            }, false);

            // --- экранный текст
            txt[0].Clear(); Gizmo.DrawScreenText("hud", new Vector2(10f, 10f));
            float mode; unsafe { mode = txt[0].Target(false).Ptr[0].Params.z; }
            Check("X18 экранный текст помечен режимом 2", Math.Abs(mode - 2f) < 1e-5f, "" + mode);

            txt[0].Clear();
            GizmoRenderer.ResetCorners();
            Gizmo.DrawScreenText("одна", GizmoCorner.TopLeft);
            float y1; unsafe { y1 = txt[0].Target(false).Ptr[0].Position.y; }
            int after1 = TV();
            Gizmo.DrawScreenText("две", GizmoCorner.TopLeft);
            float y2; unsafe { y2 = txt[0].Target(false).Ptr[after1].Position.y; }
            Check("X19 угловой текст укладывается стопкой", y2 > y1, y1 + " → " + y2);

            GizmoRenderer.ResetCorners();
            txt[0].Clear(); Gizmo.DrawScreenText("снова", GizmoCorner.TopLeft);
            float y3; unsafe { y3 = txt[0].Target(false).Ptr[0].Position.y; }
            Check("X20 счётчик углов сбрасывается на кадре", Math.Abs(y3 - y1) < 1e-4f, y1 + " / " + y3);

            Throws("X21 все четыре угла", () =>
            {
                foreach (GizmoCorner c in System.Enum.GetValues(typeof(GizmoCorner)))
                    Gizmo.DrawScreenText("угол\nдве строки", c);
            }, false);

            // --- кривые и сетка
            thin[0].Clear(); Gizmo.DrawTrajectory(Vector3.zero, Vector3.up * 5f, 2f, 10);
            Check("X22 траектория даёт 10 сегментов", thin[0].Target(false).Count == 20,
                  "" + thin[0].Target(false).Count);
            thin[0].Clear(); Gizmo.DrawBezier(Vector3.zero, Vector3.up, Vector3.right, Vector3.one, 8);
            Check("X23 безье даёт 8 сегментов", thin[0].Target(false).Count == 16,
                  "" + thin[0].Target(false).Count);
            thin[0].Clear();
            Gizmo.DrawGrid(Vector3.zero, Quaternion.identity, new Vector2(1f, 1f), new Vector2Int(2, 3));
            Check("X24 сетка 2×3 даёт 3+4 линии", thin[0].Target(false).Count == (3 + 4) * 2,
                  "" + thin[0].Target(false).Count);

            Throws("X25 краевые случаи кривых и сетки", () =>
            {
                Gizmo.DrawTrajectory(Vector3.zero, Vector3.zero, 0f, 0);
                Gizmo.DrawTrajectory(Vector3.zero, Vector3.up, -1f, -5);
                Gizmo.DrawBezier(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, 0);
                Gizmo.DrawBezier(Vector3.zero, Vector3.one, Vector3.one, Vector3.zero, -3);
                Gizmo.DrawGrid(Vector3.zero, Quaternion.identity, Vector2.zero, new Vector2Int(2, 2));
                Gizmo.DrawGrid(Vector3.zero, Quaternion.identity, Vector2.one, new Vector2Int(0, 0));
                Gizmo.DrawGrid(Vector3.zero, Quaternion.identity, new Vector2(-1f, -1f), new Vector2Int(-2, -2));
                GizmoRenderer.BeginFrame(true);
            }, false);

            Gizmo.Reset();
        }

        // ==================================================== Y. идентичность объектов
        Group("Y. Идентичность объектов");
        {
            var a = new GameObject("a");
            var b = new GameObject("b");

            Check("Y1 разные объекты — разные идентичности",
                  GizmoObjectId.Of(a) != GizmoObjectId.Of(b));
            Check("Y2 один объект — одна идентичность",
                  GizmoObjectId.Of(a) == GizmoObjectId.Of(a));
            Check("Y3 null даёт значение по умолчанию",
                  GizmoObjectId.Of(null) == default(GizmoObjectId));
            Check("Y4 живой объект не равен null-идентичности",
                  GizmoObjectId.Of(a) != default(GizmoObjectId));
            Check("Y5 хеш стабилен",
                  GizmoObjectId.Of(a).GetHashCode() == GizmoObjectId.Of(a).GetHashCode());

            // как ключ словаря
            var map = new Dictionary<GizmoObjectId, string>();
            map[GizmoObjectId.Of(a)] = "a";
            map[GizmoObjectId.Of(b)] = "b";
            Check("Y6 работает ключом словаря",
                  map.Count == 2 && map[GizmoObjectId.Of(a)] == "a" && map[GizmoObjectId.Of(b)] == "b");

            var m1 = new Mesh { name = "m1", subMeshCount = 3 };
            var m2 = new Mesh { name = "m2", subMeshCount = 3 };
            Check("Y7 ключ меша различает сабмеши",
                  !new GizmoMeshKey(m1, 0).Equals(new GizmoMeshKey(m1, 1)));
            Check("Y8 ключ меша различает меши",
                  !new GizmoMeshKey(m1, 0).Equals(new GizmoMeshKey(m2, 0)));
            Check("Y9 одинаковые ключи равны",
                  new GizmoMeshKey(m1, 2).Equals(new GizmoMeshKey(m1, 2)));

            var mkeys = new Dictionary<GizmoMeshKey, int>();
            mkeys[new GizmoMeshKey(m1, 0)] = 1;
            mkeys[new GizmoMeshKey(m1, 1)] = 2;
            mkeys[new GizmoMeshKey(m2, 0)] = 3;
            Check("Y10 ключ меша работает в словаре", mkeys.Count == 3, "" + mkeys.Count);
        }

        Console.WriteLine("\n═══════════════════════════════════");
        Console.WriteLine($"  прошло {_pass}, упало {_fail}");
        Console.WriteLine("═══════════════════════════════════");
        return _fail == 0 ? 0 : 1;
    }

    // ---- вспомогательное
    static float MinOffsetX(GizmoChannel<GizmoTextVertex> ch)
    {
        var b = ch.Target(false);
        float m = float.MaxValue;
        unsafe { for (int i = 0; i < b.Count; i++) m = Math.Min(m, b.Ptr[i].Offset.x); }
        return m;
    }

    static float MinWidth(GizmoChannel<GizmoTextVertex> ch)
    {
        var b = ch.Target(false);
        float m = float.MaxValue;
        unsafe { for (int i = 0; i < b.Count; i++) m = Math.Min(m, b.Ptr[i].Params.y); }
        return m == float.MaxValue ? 0f : m;
    }

    static int CountGizmoNodes()
    {
        int n = 0;
        void Walk(UnityEngine.LowLevel.PlayerLoopSystem s)
        {
            if (s.type != null && s.type.Name.Contains("GizmoBeginFrame")) n++;
            if (s.subSystemList != null) foreach (var c in s.subSystemList) Walk(c);
        }
        Walk(UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop());
        return n;
    }

    static bool FirstInPostLate()
    {
        var root = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
        foreach (var s in root.subSystemList)
            if (s.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                return s.subSystemList != null && s.subSystemList.Length > 0
                       && s.subSystemList[0].type.Name.Contains("GizmoBeginFrame");
        return false;
    }
}

#endif
