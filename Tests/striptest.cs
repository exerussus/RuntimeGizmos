// Проба на вырезаемость: собирается БЕЗ символов отладки и проверяется, что от
// вызовов не осталось следа в метаданных сборки. Компилируется отдельно от тестов.
#if !UNITY_2020_3_OR_NEWER

using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Extensions;

public static class StripProbe
{
    public static string Expensive(Transform t) => "префикс " + t.name + t.position;

    public static void Use(Transform t, Transform other, Collider col, Rigidbody rb)
    {
        // цепочка с дорогим аргументом — из релиза обязана исчезнуть целиком
        GizmoLazy.Track(t).For(5f).Key("k").Label(Expensive(t), Color.red);
        GizmoLazy.Track(t).Volume(Color.green);
        GizmoLazy.Track(t).LinkTo(other);
        GizmoLazy.Track(col).Shape();
        GizmoLazy.Track(rb).Velocity();
        GizmoLazy.Track(t).Draw(() => t.DrawVolume(Color.red));
        GizmoLazy.Untrack(t);
        GizmoLazy.Clear();

        // базовый слой для сравнения
        Gizmo.DrawLine(t.position, other.position);
        t.DrawVolume(Color.red);
    }
}

#endif
