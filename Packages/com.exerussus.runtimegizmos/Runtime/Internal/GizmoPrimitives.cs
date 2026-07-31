using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Единожды посчитанные примитивы в локальном пространстве, лежащие в нативной памяти.
    /// Рисование сводится к «трансформировать N точек и скопировать» — без аллокаций.
    /// </summary>
    internal static unsafe class GizmoPrimitives
    {
        public static NativeArray<Vector3> WireCube;    // 24 верш., куб -0.5..0.5
        public static NativeArray<Vector3> SolidCube;   // 36 верш.
        public static NativeArray<Vector3> WireSphere;  // 3 окружности радиуса 1
        public static NativeArray<Vector3> SolidSphere; // UV-сфера радиуса 1, суп треугольников

        public static Vector2[] Circle; // (cos, sin), длина CircleSegments + 1
        public static int CircleSegments;

        static bool _built;

        public static void Ensure()
        {
            if (_built) return;
            _built = true;

            CircleSegments = Mathf.Clamp(GizmoSettings.CircleSegments, 6, 256);
            Circle = new Vector2[CircleSegments + 1];
            for (int i = 0; i <= CircleSegments; i++)
            {
                float a = i * (Mathf.PI * 2f / CircleSegments);
                Circle[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            BuildWireCube();
            BuildSolidCube();
            BuildWireSphere();
            BuildSolidSphere();
        }

        static NativeArray<Vector3> Alloc(int n) =>
            new NativeArray<Vector3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        static void BuildWireCube()
        {
            WireCube = Alloc(24);
            var p = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(WireCube);
            const float h = 0.5f;
            Vector3 c000 = new Vector3(-h, -h, -h), c100 = new Vector3(h, -h, -h);
            Vector3 c110 = new Vector3(h, h, -h), c010 = new Vector3(-h, h, -h);
            Vector3 c001 = new Vector3(-h, -h, h), c101 = new Vector3(h, -h, h);
            Vector3 c111 = new Vector3(h, h, h), c011 = new Vector3(-h, h, h);

            p[0] = c000; p[1] = c100; p[2] = c100; p[3] = c110;
            p[4] = c110; p[5] = c010; p[6] = c010; p[7] = c000;
            p[8] = c001; p[9] = c101; p[10] = c101; p[11] = c111;
            p[12] = c111; p[13] = c011; p[14] = c011; p[15] = c001;
            p[16] = c000; p[17] = c001; p[18] = c100; p[19] = c101;
            p[20] = c110; p[21] = c111; p[22] = c010; p[23] = c011;
        }

        static void BuildSolidCube()
        {
            SolidCube = Alloc(36);
            var p = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(SolidCube);
            const float h = 0.5f;
            Vector3[] v =
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(-h, -h,  h), new Vector3(h, -h,  h), new Vector3(h, h,  h), new Vector3(-h, h,  h)
            };
            int[] idx =
            {
                0,2,1, 0,3,2, // -Z
                5,6,4, 4,6,7, // +Z
                4,7,0, 0,7,3, // -X
                1,2,5, 5,2,6, // +X
                3,7,2, 2,7,6, // +Y
                4,0,5, 5,0,1  // -Y
            };
            for (int i = 0; i < 36; i++) p[i] = v[idx[i]];
        }

        static void BuildWireSphere()
        {
            int seg = CircleSegments;
            WireSphere = Alloc(seg * 2 * 3);
            var p = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(WireSphere);
            int i = 0;
            for (int s = 0; s < seg; s++)
            {
                Vector2 a = Circle[s], b = Circle[s + 1];
                p[i++] = new Vector3(a.x, a.y, 0); p[i++] = new Vector3(b.x, b.y, 0);
                p[i++] = new Vector3(a.x, 0, a.y); p[i++] = new Vector3(b.x, 0, b.y);
                p[i++] = new Vector3(0, a.x, a.y); p[i++] = new Vector3(0, b.x, b.y);
            }
        }

        static void BuildSolidSphere()
        {
            int rings = Mathf.Clamp(GizmoSettings.SphereRings, 3, 64);
            int segs = Mathf.Clamp(GizmoSettings.SphereSegments, 4, 128);
            SolidSphere = Alloc(rings * segs * 6);
            var p = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(SolidSphere);
            int i = 0;

            for (int r = 0; r < rings; r++)
            {
                float t0 = (float)r / rings * Mathf.PI;
                float t1 = (float)(r + 1) / rings * Mathf.PI;
                float y0 = Mathf.Cos(t0), r0 = Mathf.Sin(t0);
                float y1 = Mathf.Cos(t1), r1 = Mathf.Sin(t1);

                for (int s = 0; s < segs; s++)
                {
                    float f0 = (float)s / segs * Mathf.PI * 2f;
                    float f1 = (float)(s + 1) / segs * Mathf.PI * 2f;
                    float c0 = Mathf.Cos(f0), s0 = Mathf.Sin(f0);
                    float c1 = Mathf.Cos(f1), s1 = Mathf.Sin(f1);

                    Vector3 a = new Vector3(r0 * c0, y0, r0 * s0);
                    Vector3 b = new Vector3(r0 * c1, y0, r0 * s1);
                    Vector3 c = new Vector3(r1 * c1, y1, r1 * s1);
                    Vector3 d = new Vector3(r1 * c0, y1, r1 * s0);

                    p[i++] = a; p[i++] = b; p[i++] = c;
                    p[i++] = a; p[i++] = c; p[i++] = d;
                }
            }
        }

        public static void Dispose()
        {
            if (WireCube.IsCreated) WireCube.Dispose();
            if (SolidCube.IsCreated) SolidCube.Dispose();
            if (WireSphere.IsCreated) WireSphere.Dispose();
            if (SolidSphere.IsCreated) SolidSphere.Dispose();
            Circle = null;
            _built = false;
        }
    }
}
