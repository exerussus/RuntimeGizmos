// ВНИМАНИЕ: бенчмарк. Компилируется ТОЛЬКО вне Unity — через run.sh, отдельным
// исполняемым файлом (в tests.cs свой Main, два в одной сборке не уживутся).
//
// Что меряем: во что обходится СТАТИЧЕСКАЯ геометрия, то есть та, которая от кадра
// к кадру не меняется. Три статьи расхода:
//   1. Draw*        — трансформация точек и запись вершин в нативный буфер;
//   2. BeginFrame   — компактация retained-буфера (обход всех вершин + пересчёт bounds);
//   3. Prepare      — SetVertexBufferData, то есть перезаливка тех же байт в GPU.
//
// Собирается с RUNTIME_GIZMOS_ALWAYS и БЕЗ UNITY_EDITOR/DEVELOPMENT_BUILD: так Draw*
// не вырезаются, но и отладочная проверка потока в Begin() не участвует в замере —
// меряем ровно тот путь, который работает в билде.
#if !UNITY_2020_3_OR_NEWER

using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

public static class Bench
{
    // Профиль по умолчанию: 2000 «объектов» — каркасная сфера, каркасный куб,
    // толстая линия и подпись на каждый. Это примерно то, что даёт отладочная
    // отрисовка среднего уровня, и заведомо больше, чем десяток гизмо в кадре.
    const int Objects = 2000;
    const int Frames = 600;

    static string[] _labels;
    static Camera _cam;

    // ------------------------------------------------------------------ оснастка

    static void Boot()
    {
        GizmoRenderer.Dispose();
        GizmoSettings.ResetSession();
        Shader.Available = true;
        Shader.Supported = true;
        GizmoRenderer.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        GizmoRenderer.Enabled = true;
        Gizmo.ResetState();
        GizmoRenderer.Ensure();

        Graphics.Record = false;      // иначе список сабмитов вырастет до сотен тысяч
        Graphics.Last.Clear();
        Graphics.Calls = 0;
        Mesh.ResetCounters();

        _cam = new Camera { cameraType = CameraType.Game };
        _cam.transform = new Transform();
    }

    /// <summary>Рисует сцену. duration = 0 — кадровая геометрия, &gt; 0 — retained.</summary>
    static void Paint(int n, float duration)
    {
        Gizmo.duration = duration;
        for (int i = 0; i < n; i++)
        {
            float x = i * 0.7f;
            Gizmo.color = (i & 1) == 0 ? Color.cyan : Color.yellow;

            Gizmo.lineWidth = 1f;
            Gizmo.DrawWireSphere(new Vector3(x, 0f, 0f), 0.5f);
            Gizmo.DrawWireCube(new Vector3(x, 1f, 0f), Vector3.one);

            Gizmo.lineWidth = 3f;
            Gizmo.DrawLine(new Vector3(x, 0f, 0f), new Vector3(x, 2f, 0f));

            Gizmo.DrawText(_labels[i], new Vector3(x, 2f, 0f), 12f);
        }
        Gizmo.duration = 0f;
        Gizmo.lineWidth = 1f;
    }

    /// <summary>Сколько вершин сейчас лежит во всех каналах (front + retained).</summary>
    static int ChannelVerts()
    {
        int total = 0;
        foreach (var name in new[] { "_thin", "_wide", "_tri", "_text" })
        {
            var arr = (Array)typeof(GizmoRenderer)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (arr == null) continue;
            foreach (var ch in arr)
            {
                var t = ch.GetType();
                foreach (var f in new[] { "_front", "_back", "_retained" })
                {
                    var buf = t.GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ch);
                    total += (int)buf.GetType().GetProperty("Count").GetValue(buf);
                }
            }
        }
        return total;
    }

    struct Result
    {
        public string Mode;
        public double DrawMs, FrameMs, SubmitMs;   // среднее на кадр
        public long UpBytes;                        // залито в вершинные буферы, всего
        public int UpCalls, Submits, Verts;
        public long Alloc;                          // managed-аллокации за прогон
        public double TotalMs => DrawMs + FrameMs + SubmitMs;
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // ------------------------------------------------------------------ режимы

    /// <summary>Сцена перерисовывается каждый кадр — то, как статику рисуют обычно.</summary>
    static Result ModeFrame(int n, int frames)
    {
        Boot();
        float t = 1000f;

        // Прогрев: буферы дорастают до рабочего размера, примитивы и шрифт строятся.
        for (int f = 0; f < 3; f++)
        {
            Paint(n, 0f);
            t += 0.016f; Time.realtimeSinceStartup = t; GizmoRenderer.BeginFrame(true);
            GizmoRenderer.Submit(_cam);
        }

        int verts = ChannelVerts();
        Mesh.ResetCounters();
        Graphics.Calls = 0;
        long alloc0 = GC.GetTotalAllocatedBytes(true);
        long draw = 0, frame = 0, submit = 0;

        for (int f = 0; f < frames; f++)
        {
            long a = Stopwatch.GetTimestamp();
            Paint(n, 0f);
            long b = Stopwatch.GetTimestamp();

            t += 0.016f; Time.realtimeSinceStartup = t;
            GizmoRenderer.BeginFrame(true);
            long c = Stopwatch.GetTimestamp();

            GizmoRenderer.Submit(_cam);
            long d = Stopwatch.GetTimestamp();

            draw += b - a; frame += c - b; submit += d - c;
        }

        return new Result
        {
            Mode = "каждый кадр",
            DrawMs = Ms(draw) / frames,
            FrameMs = Ms(frame) / frames,
            SubmitMs = Ms(submit) / frames,
            UpBytes = Mesh.UpBytes,
            UpCalls = Mesh.UpCalls,
            Submits = Graphics.Calls,
            Verts = verts,
            Alloc = GC.GetTotalAllocatedBytes(true) - alloc0,
        };
    }

    /// <summary>Сцена рисуется один раз с большим duration и дальше только живёт.</summary>
    static Result ModeRetained(int n, int frames)
    {
        Boot();
        float t = 2000f;
        Time.realtimeSinceStartup = t;
        GizmoRenderer.BeginFrame(true);          // синхронизируем штамп времени

        Paint(n, 1e6f);                          // один раз, живёт «вечно»

        for (int f = 0; f < 3; f++)              // прогрев
        {
            t += 0.016f; Time.realtimeSinceStartup = t; GizmoRenderer.BeginFrame(true);
            GizmoRenderer.Submit(_cam);
        }

        int verts = ChannelVerts();
        Mesh.ResetCounters();
        Graphics.Calls = 0;
        long alloc0 = GC.GetTotalAllocatedBytes(true);
        long frame = 0, submit = 0;

        for (int f = 0; f < frames; f++)
        {
            t += 0.016f; Time.realtimeSinceStartup = t;

            long b = Stopwatch.GetTimestamp();
            GizmoRenderer.BeginFrame(true);
            long c = Stopwatch.GetTimestamp();

            GizmoRenderer.Submit(_cam);
            long d = Stopwatch.GetTimestamp();

            frame += c - b; submit += d - c;
        }

        return new Result
        {
            Mode = "retained",
            DrawMs = 0.0,
            FrameMs = Ms(frame) / frames,
            SubmitMs = Ms(submit) / frames,
            UpBytes = Mesh.UpBytes,
            UpCalls = Mesh.UpCalls,
            Submits = Graphics.Calls,
            Verts = verts,
            Alloc = GC.GetTotalAllocatedBytes(true) - alloc0,
        };
    }

    /// <summary>Пустая сцена: пол, ниже которого кадр не станет. К нему должен
    /// сойтись retained-режим после этапа 1.</summary>
    static Result ModeIdle(int frames)
    {
        Boot();
        float t = 3000f;
        for (int f = 0; f < 3; f++)
        {
            t += 0.016f; Time.realtimeSinceStartup = t; GizmoRenderer.BeginFrame(true);
            GizmoRenderer.Submit(_cam);
        }

        Mesh.ResetCounters();
        Graphics.Calls = 0;
        long alloc0 = GC.GetTotalAllocatedBytes(true);
        long frame = 0, submit = 0;

        for (int f = 0; f < frames; f++)
        {
            t += 0.016f; Time.realtimeSinceStartup = t;
            long b = Stopwatch.GetTimestamp();
            GizmoRenderer.BeginFrame(true);
            long c = Stopwatch.GetTimestamp();
            GizmoRenderer.Submit(_cam);
            long d = Stopwatch.GetTimestamp();
            frame += c - b; submit += d - c;
        }

        return new Result
        {
            Mode = "пусто (пол)",
            DrawMs = 0.0,
            FrameMs = Ms(frame) / frames,
            SubmitMs = Ms(submit) / frames,
            UpBytes = Mesh.UpBytes,
            UpCalls = Mesh.UpCalls,
            Submits = Graphics.Calls,
            Verts = 0,
            Alloc = GC.GetTotalAllocatedBytes(true) - alloc0,
        };
    }

    // ------------------------------------------------------------------ вывод

    static string Bytes(double b)
    {
        if (b >= 1024 * 1024) return (b / (1024 * 1024)).ToString("0.00") + " МБ";
        if (b >= 1024) return (b / 1024).ToString("0.0") + " КБ";
        return b.ToString("0") + " Б";
    }

    static void Row(Result r, int frames)
    {
        Console.WriteLine(
            "  {0,-14}{1,9:0.000}{2,11:0.000}{3,9:0.000}{4,11:0.000}{5,14}{6,10:0.0}{7,12}",
            r.Mode, r.DrawMs, r.FrameMs, r.SubmitMs, r.TotalMs,
            Bytes((double)r.UpBytes / frames), (double)r.UpCalls / frames, Bytes(r.Alloc));
    }

    public static int Main(string[] argv)
    {
        // TryParse кладёт в out ноль, если разбор не удался, поэтому присваиваем
        // только после успеха: run.sh передаёт пустые строки, когда профиль не задан.
        int n = Objects, frames = Frames;
        if (argv.Length > 0 && int.TryParse(argv[0], out int argN) && argN > 0) n = argN;
        if (argv.Length > 1 && int.TryParse(argv[1], out int argF) && argF > 0) frames = argF;

        _labels = new string[n];
        for (int i = 0; i < n; i++) _labels[i] = "n" + i;

        Console.WriteLine("== Стоимость статической геометрии ==");
        Console.WriteLine($"   объектов: {n}   кадров: {frames}   (сфера + куб + толстая линия + подпись на объект)");
        Console.WriteLine();
        Console.WriteLine("  {0,-14}{1,9}{2,11}{3,9}{4,11}{5,14}{6,10}{7,12}",
                          "режим", "Draw*", "BeginFrame", "Submit", "итого/кадр", "заливка/кадр", "заливок", "GC за прогон");
        Console.WriteLine("  " + new string('-', 90));

        var a = ModeFrame(n, frames);
        Row(a, frames);
        var b = ModeRetained(n, frames);
        Row(b, frames);
        var z = ModeIdle(frames);
        Row(z, frames);

        Console.WriteLine();
        Console.WriteLine($"   вершин в каналах: {a.Verts} (кадровый режим), {b.Verts} (retained)");
        Console.WriteLine($"   сабмитов на кадр: {(double)a.Submits / frames:0.0} (кадровый) / {(double)b.Submits / frames:0.0} (retained)");
        Console.WriteLine();
        Console.WriteLine("   ВАЖНО: SetVertexBufferData в заглушке только считает байты и ничего");
        Console.WriteLine("   не копирует, поэтому колонка Submit НЕ включает реальную загрузку в GPU.");
        Console.WriteLine("   Колонка «заливка/кадр» — это трафик, который в Unity был бы настоящим.");
        Console.WriteLine();

        // Что именно снимает каждый этап: этап 1 (dirty-флаг) убирает из retained-режима
        // компактацию и перезаливку, этап 3 (бейк) убирает Draw* из кадрового режима,
        // но добавляет сабмиты.
        Console.WriteLine("   потолок выигрыша:");
        Console.WriteLine($"     этап 1 на retained-сцене: {b.TotalMs:0.000} → {z.TotalMs:0.000} мс/кадр (пол)");
        Console.WriteLine($"     этап 3 на кадровой сцене: {a.TotalMs:0.000} → около {z.TotalMs:0.000} мс/кадр");
        Console.WriteLine($"     трафик в GPU, который снимается: {Bytes((double)b.UpBytes / frames)}/кадр");

        if (a.Alloc > 0 || b.Alloc > 0 || z.Alloc > 0)
            Console.WriteLine($"\n   ВНИМАНИЕ: на горячем пути есть managed-аллокации: {a.Alloc} / {b.Alloc} / {z.Alloc} байт");

        return 0;
    }
}

#endif
