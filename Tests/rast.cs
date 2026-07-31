// ВНИМАНИЕ: тестовый харнесс. Компилируется ТОЛЬКО вне Unity — через run.sh.
//
// Директива ниже обязательна: файл лезет во внутренние типы RuntimeGizmos через
// рефлексию и опирается на заглушки Unity API. Внутри Unity он не собрался бы,
// а до ошибок компиляции дело доводить незачем — символ определён всегда, и файл
// схлопывается в пустой.
#if !UNITY_2020_3_OR_NEWER

// Растеризация текста по ТЕМ ЖЕ данным, что уходят на GPU.
//
// Вершинный шейдер задаёт p = mid + dir*local.x + nrm*local.y, то есть отображение
// local -> p жёсткое (поворот со сдвигом). Значит обратное — local.x = dot(p-mid, dir),
// local.y = dot(p-mid, nrm) — точно совпадает с тем, что интерполятор выдаёт
// фрагментному шейдеру. Дальше считаем ровно тот же SDF капсулы.
using System;
using System.Collections.Generic;
using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

public static class Rast
{
    struct Seg { public Vector2 Mid, Dir, Nrm; public float HalfLen, HalfW; }

    public static unsafe string Render(string text, float sizePx, float strokeW, bool world = false)
    {
        var ch = (GizmoChannel<GizmoTextVertex>[])typeof(GizmoRenderer)
            .GetField("_text", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .GetValue(null);
        ch[0].Clear();

        Gizmo.lineWidth = strokeW;
        if (world) Gizmo.DrawTextWorld(text, Vector3.zero, sizePx);
        else Gizmo.DrawText(text, Vector3.zero, sizePx);

        var buf = ch[0].Target(false);
        int n = buf.Count;
        if (n == 0 || n % 6 != 0) return "(вершин " + n + ", не кратно 6)";

        var segs = new List<Seg>();
        for (int i = 0; i < n; i += 6)
        {
            var v = buf.Ptr[i];
            Vector2 o = v.Offset, ot = v.Other;
            Vector2 d = ot - o;
            float len = d.magnitude;
            Vector2 dir = len > 1e-5f ? d / len : new Vector2(1f, 0f);
            segs.Add(new Seg
            {
                Mid = (o + ot) * 0.5f,
                Dir = dir,
                Nrm = new Vector2(-dir.y, dir.x),
                HalfLen = len * 0.5f,
                HalfW = Mathf.Abs(v.SideWidth.y) * 0.5f,
            });

            // все шесть вершин квада обязаны нести одни и те же концы отрезка
            for (int k = 1; k < 6; k++)
            {
                var w = buf.Ptr[i + k];
                if (w.Offset.x != o.x || w.Offset.y != o.y || w.Other.x != ot.x || w.Other.y != ot.y)
                    return "(вершины квада #" + (i / 6) + " ссылаются на разные отрезки)";
            }
            // и ровно по разу каждую из четырёх комбинаций сторона×конец
            int m1 = 0, p1 = 0, m2 = 0, p2 = 0;
            for (int k = 0; k < 6; k++)
            {
                float sx = buf.Ptr[i + k].SideWidth.x;
                if (sx == -1f) m1++; else if (sx == 1f) p1++;
                else if (sx == -2f) m2++; else if (sx == 2f) p2++;
                else return "(неизвестный код стороны " + sx + ")";
            }
            if (m1 != 2 || p1 != 1 || m2 != 1 || p2 != 2)
                return "(разбиение квада на треугольники неверно: " + m1 + p1 + m2 + p2 + ")";
        }

        // границы картинки
        float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
        foreach (var s in segs)
        {
            float r = s.HalfLen + s.HalfW;
            x0 = Mathf.Min(x0, s.Mid.x - r); x1 = Mathf.Max(x1, s.Mid.x + r);
            y0 = Mathf.Min(y0, s.Mid.y - r); y1 = Mathf.Max(y1, s.Mid.y + r);
        }

        var sb = new System.Text.StringBuilder();
        for (float y = y1; y >= y0; y -= 1f)
        {
            for (float x = x0; x <= x1; x += 0.5f)
            {
                float best = 1e9f;
                var p = new Vector2(x, y);
                foreach (var s in segs)
                {
                    Vector2 rel = p - s.Mid;
                    float lx = rel.x * s.Dir.x + rel.y * s.Dir.y;      // dot(rel, dir)
                    float ly = rel.x * s.Nrm.x + rel.y * s.Nrm.y;      // dot(rel, nrm)
                    if (Mathf.Abs(lx) > s.HalfLen + s.HalfW || Mathf.Abs(ly) > s.HalfW) continue;
                    float qx = Mathf.Max(Mathf.Abs(lx) - s.HalfLen, 0f);
                    best = Mathf.Min(best, Mathf.Sqrt(qx * qx + ly * ly) - s.HalfW);
                }
                sb.Append(best < -0.5f ? '#' : best < 0.3f ? '+' : ' ');
            }
            sb.Append('\n');
        }
        Gizmo.Reset();
        return sb.ToString();
    }
}

#endif
