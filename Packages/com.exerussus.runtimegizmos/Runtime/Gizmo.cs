using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using RuntimeGizmos.Internal;
using Cond = System.Diagnostics.ConditionalAttribute;

namespace RuntimeGizmos
{
    /// <summary>Выравнивание текстовой метки относительно якоря.</summary>
    public enum GizmoTextAlign { Left, Center, Right }

    /// <summary>Угол экрана для DrawScreenText.</summary>
    public enum GizmoCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// Полная замена UnityEngine.Gizmos, работающая и в билде, и в редакторе,
    /// и видимая через игровую камеру, а не только в Scene View.
    ///
    /// Чтобы перевести существующий код без правок, добавьте в начало файла:
    ///     using Gizmos = RuntimeGizmos.Gizmo;
    ///
    /// Все методы Draw* помечены [Conditional] и вырезаются компилятором из релизных
    /// билдов вместе с вычислением аргументов. Чтобы оставить их в релизе — определите
    /// символ RUNTIME_GIZMOS_ALWAYS.
    ///
    /// Вызовы допускаются только из главного потока.
    /// </summary>
    public static unsafe partial class Gizmo
    {
        internal const string EDITOR = "UNITY_EDITOR";
        internal const string DEV = "DEVELOPMENT_BUILD";
        internal const string ALWAYS = "RUNTIME_GIZMOS_ALWAYS";

        static Color s_Color = Color.white;
        static Matrix4x4 s_Matrix = Matrix4x4.identity;
        static bool s_Identity = true;

        // ================================================================= состояние

        /// <summary>Глобальный выключатель. Ничего не пишется в буферы, пока false.</summary>
        public static bool enabled
        {
            get => GizmoRenderer.Enabled;
            set => GizmoRenderer.Enabled = value;
        }

        public static Color color
        {
            get => s_Color;
            set { s_Color = value; GizmoRenderer.Color = value; }
        }

        public static Matrix4x4 matrix
        {
            get => s_Matrix;
            set
            {
                s_Matrix = value;
                s_Identity = value.m00 == 1f && value.m11 == 1f && value.m22 == 1f && value.m33 == 1f &&
                             value.m01 == 0f && value.m02 == 0f && value.m03 == 0f &&
                             value.m10 == 0f && value.m12 == 0f && value.m13 == 0f &&
                             value.m20 == 0f && value.m21 == 0f && value.m23 == 0f &&
                             value.m30 == 0f && value.m31 == 0f && value.m32 == 0f;
            }
        }

        /// <summary>
        /// Толщина линий в пикселях. Значение &lt;= 1 включает быстрый путь (MeshTopology.Lines).
        /// Стартовое значение берётся из GizmoSettings.DefaultLineWidth и зависит от платформы.
        /// </summary>
        public static float lineWidth
        {
            get => GizmoRenderer.Width;
            set => GizmoRenderer.Width = value;
        }

        /// <summary>false — рисовать поверх всей геометрии (ZTest Always).</summary>
        public static bool depthTest
        {
            get => GizmoRenderer.Z == 0;
            set => GizmoRenderer.Z = value ? 0 : 1;
        }

        /// <summary>Время жизни в секундах. 0 — один кадр. Работает и при вызове из FixedUpdate/колбэков.</summary>
        public static float duration
        {
            get => GizmoRenderer.Duration;
            set => GizmoRenderer.Duration = value;
        }

        /// <summary>
        /// Период пунктира в мировых единицах: штрих и такой же пропуск. 0 — сплошная.
        /// Фаза накапливается вдоль ломаной, поэтому у путей штрихи не рвутся на изломах.
        /// </summary>
        public static float dash
        {
            get => GizmoRenderer.Dash;
            set { GizmoRenderer.Dash = value; GizmoRenderer.DashRun = 0f; }
        }

        public static void Reset() => ResetState();

        /// <summary>
        /// Нарисуется ли символ. Непокрытый даёт пустой квадрат — заметно, но узнать об этом
        /// хочется до того, как подпись уедет к пользователю.
        /// </summary>
        public static bool IsRenderable(char c) => GizmoFont.Supported(c);

        /// <summary>
        /// Первый символ строки, для которого нет глифа, либо '\0'. Удобно поставить
        /// в Assert рядом с локализацией: список покрытия описан в README.
        /// </summary>
        public static char FirstUnrenderable(string text)
        {
            if (string.IsNullOrEmpty(text)) return '\0';

            for (int i = 0; i < text.Length; i++)
                if (!GizmoFont.Supported(text[i]))
                    return text[i];

            return '\0';
        }

        // То же самое без [Conditional]: нужно слою GizmoLazy, который обязан
        // компилироваться и в релизе, даже если фактически там не исполняется.
        internal static void ResetState()
        {
            color = Color.white;
            matrix = Matrix4x4.identity;
            lineWidth = GizmoSettings.DefaultLineWidth;
            depthTest = true;
            duration = 0f;
            GizmoRenderer.Dash = 0f;
            GizmoRenderer.DashRun = 0f;
        }

        // ================================================================= scope

        /// <summary>Структурный scope без аллокаций: using (Gizmo.Scope(Color.red)) { ... }</summary>
        public static GizmoScope Scope() => new GizmoScope(true);
        public static GizmoScope Scope(Color c) { var s = new GizmoScope(true); color = c; return s; }
        public static GizmoScope Scope(Matrix4x4 m) { var s = new GizmoScope(true); matrix = m; return s; }
        public static GizmoScope Scope(Color c, Matrix4x4 m) { var s = new GizmoScope(true); color = c; matrix = m; return s; }

        public readonly struct GizmoScope : IDisposable
        {
            readonly Color _c;
            readonly Matrix4x4 _m;
            readonly float _w;
            readonly int _z;
            readonly float _d;

            internal GizmoScope(bool dummy)
            {
                this._c = s_Color;
                this._m = s_Matrix;
                this._w = GizmoRenderer.Width;
                this._z = GizmoRenderer.Z;
                this._d = GizmoRenderer.Duration;
            }

            public void Dispose()
            {
                color = _c;
                matrix = _m;
                GizmoRenderer.Width = _w;
                GizmoRenderer.Z = _z;
                GizmoRenderer.Duration = _d;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3 Xf(Vector3 p) => s_Identity ? p : s_Matrix.MultiplyPoint3x4(p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Matrix4x4 Local(Vector3 pos, Quaternion rot, Vector3 scale) =>
            s_Identity ? Matrix4x4.TRS(pos, rot, scale) : s_Matrix * Matrix4x4.TRS(pos, rot, scale);

        // ================================================================= линии

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLine(Vector3 from, Vector3 to) => GizmoRenderer.Line(Xf(from), Xf(to));

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLine(Vector3 from, Vector3 to, Color c)
        {
            var prev = s_Color;
            color = c;
            GizmoRenderer.Line(Xf(from), Xf(to));
            color = prev;
        }

        // ================================================================= текст

        /// <summary>
        /// Текстовая метка, привязанная к мировой точке. Размер задаётся в пикселях, поэтому
        /// метка одинаково читается на любом расстоянии от камеры и на любом разрешении.
        /// Толщина штриха берётся из Gizmo.lineWidth, цвет и duration — как у всего остального.
        /// Шрифт штриховой и встроенный: ни шрифтового ассета, ни атласа, ни зависимостей.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawText(string text, Vector3 position, float sizePixels = 14f)
            => GizmoRenderer.Text(text, Xf(position), sizePixels, Vector2.zero, 0.5f);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawText(string text, Vector3 position, Color c, float sizePixels = 14f)
        {
            var prev = s_Color;
            color = c;
            GizmoRenderer.Text(text, Xf(position), sizePixels, Vector2.zero, 0.5f);
            color = prev;
        }

        /// <summary>
        /// Метка со смещением в пикселях от якоря и выравниванием — удобно подписывать точку,
        /// не перекрывая её саму: DrawText("hp 80", p, 14f, new Vector2(8f, 0f), GizmoTextAlign.Left).
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawText(string text, Vector3 position, float sizePixels,
                                    Vector2 pixelOffset, GizmoTextAlign align = GizmoTextAlign.Center)
            => GizmoRenderer.Text(text, Xf(position), sizePixels, pixelOffset, AlignFactor(align));

        /// <summary>
        /// Метка размером в МИРОВЫХ единицах: в отличие от DrawText, уменьшается с
        /// расстоянием, как обычная геометрия. То, что нужно для ников над игроками и
        /// подписей над предметами. Всегда развёрнута к камере.
        ///
        /// worldHeight — высота прописной буквы в юнитах. Толщина штриха берётся из
        /// Gizmo.lineWidth как доля высоты: 1 — обычная, 2 — вдвое жирнее.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTextWorld(string text, Vector3 position, float worldHeight = 0.25f)
            => GizmoRenderer.Text(text, Xf(position), worldHeight, Vector2.zero, 0.5f, 1);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTextWorld(string text, Vector3 position, Color c, float worldHeight = 0.25f)
        {
            var prev = s_Color;
            color = c;
            GizmoRenderer.Text(text, Xf(position), worldHeight, Vector2.zero, 0.5f, 1);
            color = prev;
        }

        /// <summary>Смещение здесь тоже в мировых единицах, по осям экрана камеры.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTextWorld(string text, Vector3 position, float worldHeight,
                                         Vector2 worldOffset, GizmoTextAlign align = GizmoTextAlign.Center)
            => GizmoRenderer.Text(text, Xf(position), worldHeight, worldOffset, AlignFactor(align), 1);

        /// <summary>
        /// Текст в пикселях экрана, начало координат в левом верхнем углу. Для HUD:
        /// счётчиков, дампов состояния. Мировая матрица и глубина не применяются.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawScreenText(string text, Vector2 screenPos, float sizePixels = 14f,
                                          GizmoTextAlign align = GizmoTextAlign.Left)
            => GizmoRenderer.Text(text, screenPos, sizePixels, Vector2.zero, AlignFactor(align), 2);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawScreenText(string text, Vector2 screenPos, Color c,
                                          float sizePixels = 14f, GizmoTextAlign align = GizmoTextAlign.Left)
        {
            var prev = s_Color;
            color = c;
            GizmoRenderer.Text(text, screenPos, sizePixels, Vector2.zero, AlignFactor(align), 2);
            color = prev;
        }

        /// <summary>
        /// Текст, прижатый к углу экрана. Строки в одном углу за кадр укладываются
        /// стопкой автоматически — счётчик сбрасывается на границе кадра.
        /// </summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawScreenText(string text, GizmoCorner corner, float sizePixels = 14f)
            => GizmoRenderer.CornerText(text, corner, sizePixels);

        static float AlignFactor(GizmoTextAlign a) =>
            a == GizmoTextAlign.Left ? 0f : a == GizmoTextAlign.Right ? 1f : 0.5f;

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRay(Vector3 from, Vector3 direction) => GizmoRenderer.Line(Xf(from), Xf(from + direction));

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRay(Ray r) => GizmoRenderer.Line(Xf(r.origin), Xf(r.origin + r.direction));

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawRay(Ray r, float distance) => GizmoRenderer.Line(Xf(r.origin), Xf(r.origin + r.direction * distance));

        /// <summary>Пары точек: (0,1), (2,3), (4,5)...</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLineList(ReadOnlySpan<Vector3> points)
        {
            int n = points.Length & ~1;
            for (int i = 0; i < n; i += 2) GizmoRenderer.Line(Xf(points[i]), Xf(points[i + 1]));
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawLineStrip(ReadOnlySpan<Vector3> points, bool looped = false)
        {
            int n = points.Length;
            if (n < 2) return;
            var prev = Xf(points[0]);
            var first = prev;
            for (int i = 1; i < n; i++)
            {
                var cur = Xf(points[i]);
                GizmoRenderer.Line(prev, cur);
                prev = cur;
            }
            if (looped) GizmoRenderer.Line(prev, first);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawPolyLine(IReadOnlyList<Vector3> points, bool looped = false)
        {
            int n = points.Count;
            if (n < 2) return;
            var prev = Xf(points[0]);
            var first = prev;
            for (int i = 1; i < n; i++)
            {
                var cur = Xf(points[i]);
                GizmoRenderer.Line(prev, cur);
                prev = cur;
            }
            if (looped) GizmoRenderer.Line(prev, first);
        }

        // ================================================================= куб

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireCube(Vector3 center, Vector3 size)
        {
            GizmoPrimitives.Ensure();
            GizmoRenderer.LineArray((Vector3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(GizmoPrimitives.WireCube),
                GizmoPrimitives.WireCube.Length, Local(center, Quaternion.identity, size));
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawCube(Vector3 center, Vector3 size)
        {
            GizmoPrimitives.Ensure();
            GizmoRenderer.TriangleArray((Vector3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(GizmoPrimitives.SolidCube),
                GizmoPrimitives.SolidCube.Length, Local(center, Quaternion.identity, size));
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireCube(Vector3 center, Quaternion rotation, Vector3 size)
        {
            GizmoPrimitives.Ensure();
            GizmoRenderer.LineArray((Vector3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(GizmoPrimitives.WireCube),
                GizmoPrimitives.WireCube.Length, Local(center, rotation, size));
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawBounds(Bounds b) => DrawWireCube(b.center, b.size);

        // ================================================================= сфера

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireSphere(Vector3 center, float radius)
        {
            GizmoPrimitives.Ensure();
            GizmoRenderer.LineArray((Vector3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(GizmoPrimitives.WireSphere),
                GizmoPrimitives.WireSphere.Length,
                Local(center, Quaternion.identity, new Vector3(radius, radius, radius)));
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawSphere(Vector3 center, float radius)
        {
            GizmoPrimitives.Ensure();
            GizmoRenderer.TriangleArray((Vector3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(GizmoPrimitives.SolidSphere),
                GizmoPrimitives.SolidSphere.Length,
                Local(center, Quaternion.identity, new Vector3(radius, radius, radius)));
        }

        // ================================================================= меши

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMesh(Mesh mesh) => DrawMesh(mesh, -1, Vector3.zero, Quaternion.identity, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMesh(Mesh mesh, Vector3 position) => DrawMesh(mesh, -1, position, Quaternion.identity, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation) =>
            DrawMesh(mesh, -1, position, rotation, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) =>
            DrawMesh(mesh, -1, position, rotation, scale);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawMesh(Mesh mesh, int submeshIndex, Vector3 position = default,
            Quaternion rotation = default, Vector3 scale = default)
        {
            if (mesh == null) return;
            if (rotation.x == 0 && rotation.y == 0 && rotation.z == 0 && rotation.w == 0) rotation = Quaternion.identity;
            if (scale == Vector3.zero) scale = Vector3.one;

            var m = Local(position, rotation, scale);
            if (submeshIndex < 0)
            {
                for (int i = 0; i < mesh.subMeshCount; i++) GizmoRenderer.MeshCmd(mesh, i, m, s_Color);
            }
            else GizmoRenderer.MeshCmd(mesh, submeshIndex, m, s_Color);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireMesh(Mesh mesh) => DrawWireMesh(mesh, -1, Vector3.zero, Quaternion.identity, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireMesh(Mesh mesh, Vector3 position) =>
            DrawWireMesh(mesh, -1, position, Quaternion.identity, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation) =>
            DrawWireMesh(mesh, -1, position, rotation, Vector3.one);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) =>
            DrawWireMesh(mesh, -1, position, rotation, scale);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireMesh(Mesh mesh, int submeshIndex, Vector3 position = default,
            Quaternion rotation = default, Vector3 scale = default)
        {
            if (mesh == null) return;
            if (rotation.x == 0 && rotation.y == 0 && rotation.z == 0 && rotation.w == 0) rotation = Quaternion.identity;
            if (scale == Vector3.zero) scale = Vector3.one;

            var m = Local(position, rotation, scale);
            int from = submeshIndex < 0 ? 0 : submeshIndex;
            int to = submeshIndex < 0 ? mesh.subMeshCount : submeshIndex + 1;

            for (int i = from; i < to; i++)
            {
                var wire = GizmoWireMeshCache.Get(mesh, i);
                if (wire != null) GizmoRenderer.MeshCmd(wire, 0, m, s_Color);
                else GizmoRenderer.MeshCmd(mesh, i, m, s_Color);
            }
        }

        // ================================================================= фрустум

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawFrustum(Vector3 center, float fov, float maxRange, float minRange, float aspect)
        {
            float tan = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float nh = tan * minRange, nw = nh * aspect;
            float fh = tan * maxRange, fw = fh * aspect;

            Vector3 n0 = center + new Vector3(-nw, -nh, minRange);
            Vector3 n1 = center + new Vector3(nw, -nh, minRange);
            Vector3 n2 = center + new Vector3(nw, nh, minRange);
            Vector3 n3 = center + new Vector3(-nw, nh, minRange);
            Vector3 f0 = center + new Vector3(-fw, -fh, maxRange);
            Vector3 f1 = center + new Vector3(fw, -fh, maxRange);
            Vector3 f2 = center + new Vector3(fw, fh, maxRange);
            Vector3 f3 = center + new Vector3(-fw, fh, maxRange);

            DrawLine(n0, n1); DrawLine(n1, n2); DrawLine(n2, n3); DrawLine(n3, n0);
            DrawLine(f0, f1); DrawLine(f1, f2); DrawLine(f2, f3); DrawLine(f3, f0);
            DrawLine(n0, f0); DrawLine(n1, f1); DrawLine(n2, f2); DrawLine(n3, f3);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawFrustum(Camera cam)
        {
            if (cam == null) return;
            var prev = s_Matrix;
            matrix = prev * Matrix4x4.TRS(cam.transform.position, cam.transform.rotation, Vector3.one);
            DrawFrustum(Vector3.zero, cam.fieldOfView, cam.farClipPlane, cam.nearClipPlane, cam.aspect);
            matrix = prev;
        }

        // ================================================================= иконки и экранные текстуры

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawIcon(Vector3 center, string name, bool allowScaling = true) =>
            DrawIcon(center, name, allowScaling, s_Color);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawIcon(Vector3 center, string name, bool allowScaling, Color tint)
        {
            var tex = GizmoIcons.Resolve(name);
            if (tex != null) DrawIcon(center, tex, allowScaling, tint);
        }

        /// <param name="allowScaling">true — постоянный размер в пикселях; false — размер в мировых единицах.</param>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawIcon(Vector3 center, Texture texture, bool allowScaling = true, Color tint = default, float size = 32f)
        {
            if (texture == null) return;
            if (tint == default(Color)) tint = s_Color;
            GizmoRenderer.Quad(texture, Xf(center), new Vector2(size, size), !allowScaling, tint);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawGUITexture(Rect screenRect, Texture texture) =>
            GizmoRenderer.ScreenQuad(texture, screenRect, s_Color);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawGUITexture(Rect screenRect, Texture texture, Color tint) =>
            GizmoRenderer.ScreenQuad(texture, screenRect, tint);

        // Бордеры оригинального API (9-slice) не поддерживаются — параметры игнорируются.
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawGUITexture(Rect screenRect, Texture texture,
            int leftBorder, int rightBorder, int topBorder, int bottomBorder) =>
            GizmoRenderer.ScreenQuad(texture, screenRect, s_Color);

        // ================================================================= расширения

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius) =>
            DrawWireArc(center, normal, Perp(normal), 360f, radius);

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireArc(Vector3 center, Vector3 normal, Vector3 from, float angleDeg, float radius)
        {
            GizmoPrimitives.Ensure();
            normal = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            from = from - Vector3.Dot(from, normal) * normal;
            if (from.sqrMagnitude < 1e-8f) from = Perp(normal);
            from.Normalize();
            Vector3 side = Vector3.Cross(normal, from);

            int seg = Mathf.Max(2, Mathf.CeilToInt(GizmoPrimitives.CircleSegments * Mathf.Abs(angleDeg) / 360f));
            float step = angleDeg * Mathf.Deg2Rad / seg;

            Vector3 prev = Xf(center + from * radius);
            for (int i = 1; i <= seg; i++)
            {
                float a = step * i;
                Vector3 p = Xf(center + (from * Mathf.Cos(a) + side * Mathf.Sin(a)) * radius);
                GizmoRenderer.Line(prev, p);
                prev = p;
            }
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireCapsule(Vector3 start, Vector3 end, float radius)
        {
            Vector3 axis = end - start;
            float len = axis.magnitude;
            Vector3 up = len < 1e-6f ? Vector3.up : axis / len;
            Vector3 fwd = Perp(up);
            Vector3 right = Vector3.Cross(up, fwd);

            DrawWireDisc(start, up, radius);
            DrawWireDisc(end, up, radius);
            // Знаки подобраны так, чтобы полусферы смотрели наружу от оси капсулы.
            DrawWireArc(start, right, fwd, 180f, radius);
            DrawWireArc(start, fwd, right, -180f, radius);
            DrawWireArc(end, right, fwd, -180f, radius);
            DrawWireArc(end, fwd, right, 180f, radius);

            DrawLine(start + fwd * radius, end + fwd * radius);
            DrawLine(start - fwd * radius, end - fwd * radius);
            DrawLine(start + right * radius, end + right * radius);
            DrawLine(start - right * radius, end - right * radius);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireCapsule(Vector3 center, Quaternion rotation, float height, float radius)
        {
            float half = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 up = rotation * Vector3.up;
            DrawWireCapsule(center - up * half, center + up * half, radius);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireCone(Vector3 apex, Vector3 direction, float angleDeg, float length)
        {
            Vector3 dir = direction.sqrMagnitude < 1e-8f ? Vector3.forward : direction.normalized;
            Vector3 baseC = apex + dir * length;
            float r = Mathf.Tan(angleDeg * Mathf.Deg2Rad) * length;
            Vector3 a = Perp(dir), b = Vector3.Cross(dir, a);

            DrawWireDisc(baseC, dir, r);
            DrawLine(apex, baseC + a * r);
            DrawLine(apex, baseC - a * r);
            DrawLine(apex, baseC + b * r);
            DrawLine(apex, baseC - b * r);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawArrow(Vector3 from, Vector3 to, float headSize = 0.25f, float headAngle = 22f)
        {
            DrawLine(from, to);
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-6f) return;
            dir /= len;

            float hs = Mathf.Min(headSize, len * 0.5f);
            Vector3 a = Perp(dir), b = Vector3.Cross(dir, a);
            float t = Mathf.Tan(headAngle * Mathf.Deg2Rad) * hs;
            Vector3 basePos = to - dir * hs;

            DrawLine(to, basePos + a * t);
            DrawLine(to, basePos - a * t);
            DrawLine(to, basePos + b * t);
            DrawLine(to, basePos - b * t);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawAxes(Vector3 position, Quaternion rotation, float size = 1f)
        {
            var prev = s_Color;
            color = Color.red; DrawLine(position, position + rotation * Vector3.right * size);
            color = Color.green; DrawLine(position, position + rotation * Vector3.up * size);
            color = Color.blue; DrawLine(position, position + rotation * Vector3.forward * size);
            color = prev;
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawPoint(Vector3 position, float size = 0.1f)
        {
            float h = size * 0.5f;
            DrawLine(position - Vector3.right * h, position + Vector3.right * h);
            DrawLine(position - Vector3.up * h, position + Vector3.up * h);
            DrawLine(position - Vector3.forward * h, position + Vector3.forward * h);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawTriangle(Vector3 a, Vector3 b, Vector3 c) =>
            GizmoRenderer.Triangle(Xf(a), Xf(b), Xf(c));

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 wa = Xf(a), wb = Xf(b), wc = Xf(c), wd = Xf(d);
            GizmoRenderer.Triangle(wa, wb, wc);
            GizmoRenderer.Triangle(wa, wc, wd);
        }

        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void DrawWireQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            DrawLine(a, b); DrawLine(b, c); DrawLine(c, d); DrawLine(d, a);
        }

        /// <summary>Сбросить всю накопленную геометрию, включая заданную через duration.</summary>
        [Cond(EDITOR), Cond(DEV), Cond(ALWAYS)]
        public static void Clear() => GizmoRenderer.ClearAll();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3 Perp(Vector3 n)
        {
            Vector3 a = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 r = Vector3.Cross(n, a);
            float m = r.magnitude;
            return m < 1e-6f ? Vector3.right : r / m;
        }
    }

    internal static class GizmoIcons
    {
        static readonly Dictionary<string, Texture> _cache = new Dictionary<string, Texture>();
        static readonly string[] IconExtensions = { ".png", ".psd", ".tga", ".jpg", ".asset" };

        public static Texture Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var t)) return t;

            t = Resources.Load<Texture>(name);

            // Resources.Load хочет имя БЕЗ расширения, а Gizmos.DrawIcon исторически
            // принимает "icon.png" — пробуем оба варианта.
            if (t == null)
            {
                string noExt = System.IO.Path.GetFileNameWithoutExtension(name);
                if (noExt != name) t = Resources.Load<Texture>(noExt);
            }

#if UNITY_EDITOR
            // Как у настоящего Gizmos.DrawIcon: сначала Assets/Gizmos/.
            if (t == null) t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>("Assets/Gizmos/" + name);
            if (t == null && !name.Contains("."))
                foreach (var ext in IconExtensions)
                {
                    t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>("Assets/Gizmos/" + name + ext);
                    if (t != null) break;
                }

            // Последняя попытка — встроенная иконка редактора. Имя передаём ЦЕЛИКОМ:
            // у встроенных иконок точка это часть имени ("console.infoicon"), а не
            // расширение, и обрезка по ней ломала весь этот путь.
            if (t == null)
            {
                var content = UnityEditor.EditorGUIUtility.IconContent(name);
                t = content?.image;

                if (t == null)
                    Debug.LogWarning($"[RuntimeGizmos] Иконка '{name}' не найдена: ни в Resources, " +
                                     "ни в Assets/Gizmos/, ни среди встроенных иконок редактора. " +
                                     "Сообщение выше — от самого редактора. Повторяться не будет: " +
                                     "промах закэширован.");
            }
#endif
            _cache[name] = t;
            return t;
        }
    }
}
