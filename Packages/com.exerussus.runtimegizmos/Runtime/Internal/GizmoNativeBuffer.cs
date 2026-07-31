using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Растущий персистентный нативный буфер. Никаких managed-аллокаций на горячем пути:
    /// Reserve() отдаёт сырой указатель, рост — амортизированное удвоение.
    /// </summary>
    internal sealed unsafe class GizmoNativeBuffer<T> : IDisposable where T : unmanaged
    {
        NativeArray<T> _array;
        T* _ptr;
        int _count;
        int _capacity;

        // Сток для геометрии, не влезшей в бюджет: writes уходят в никуда, вместо того
        // чтобы ронять процесс или бесконтрольно съедать нативную кучу.
        NativeArray<T> _sink;
        T* _sinkPtr;
        int _sinkCap;
        bool _overflowed;

        public Vector3 Min;
        public Vector3 Max;

        public GizmoNativeBuffer(int capacity)
        {
            Allocate(Mathf.Max(64, Mathf.NextPowerOfTwo(capacity)));
            ResetBounds();
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _count;
        }

        public NativeArray<T> Array => _array;

        public T* Ptr
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr;
        }

        void Allocate(int cap)
        {
            var na = new NativeArray<T>(cap, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (_ptr != null && _count > 0)
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafePtr(na), _ptr, (long)_count * sizeof(T));
            if (_array.IsCreated) _array.Dispose();
            _array = na;
            _ptr = (T*)NativeArrayUnsafeUtility.GetUnsafePtr(na);
            _capacity = cap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T* Reserve(int count)
        {
            int need = _count + count;
            if (need > _capacity)
            {
                int max = GizmoSettings.MaxVerticesPerChannel;
                if (max > 0 && need > max) return Overflow(count);
                Grow(need);
            }
            T* p = _ptr + _count;
            _count = need;
            return p;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        T* Overflow(int count)
        {
            if (!_overflowed)
            {
                _overflowed = true;
                Debug.LogWarning(
                    $"[RuntimeGizmos] Достигнут потолок GizmoSettings.MaxVerticesPerChannel " +
                    $"({GizmoSettings.MaxVerticesPerChannel} вершин). Лишняя геометрия кадра отбрасывается. " +
                    "Обычно это Draw* в цикле без ограничения — либо поднимите потолок, либо сократите вызовы.");
            }

            if (count > _sinkCap)
            {
                if (_sink.IsCreated) _sink.Dispose();
                _sink = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _sinkPtr = (T*)NativeArrayUnsafeUtility.GetUnsafePtr(_sink);
                _sinkCap = count;
            }

            return _sinkPtr;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void Grow(int need)
        {
            int cap = _capacity;
            while (cap < need) cap <<= 1;
            Allocate(cap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(in Vector3 p)
        {
            if (p.x < Min.x) Min.x = p.x;
            if (p.y < Min.y) Min.y = p.y;
            if (p.z < Min.z) Min.z = p.z;
            if (p.x > Max.x) Max.x = p.x;
            if (p.y > Max.y) Max.y = p.y;
            if (p.z > Max.z) Max.z = p.z;
        }

        public bool HasBounds => Min.x <= Max.x;

        public void SetCount(int c) => _count = c;

        public void Clear()
        {
            _count = 0;
            ResetBounds();
        }

        public void ResetBounds()
        {
            Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        public void Dispose()
        {
            if (_array.IsCreated) _array.Dispose();
            if (_sink.IsCreated) _sink.Dispose();
            _ptr = null;
            _sinkPtr = null;
            _count = 0;
            _capacity = 0;
            _sinkCap = 0;
        }
    }

    /// <summary>
    /// Общий на весь модуль identity-индексбуфер (0,1,2,...). Вся геометрия неиндексированная,
    /// поэтому индексы загружаются в меш только при росте ёмкости, а не каждый кадр.
    /// </summary>
    internal static unsafe class GizmoIndexPool
    {
        static NativeArray<int> _indices;

        public static NativeArray<int> Indices => _indices;

        public static void Ensure(int count)
        {
            if (_indices.IsCreated && _indices.Length >= count) return;

            int cap = Mathf.Max(1024, Mathf.NextPowerOfTwo(count));
            var na = new NativeArray<int>(cap, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            int* p = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(na);
            for (int i = 0; i < cap; i++) p[i] = i;
            if (_indices.IsCreated) _indices.Dispose();
            _indices = na;
        }

        public static void Dispose()
        {
            if (_indices.IsCreated) _indices.Dispose();
        }
    }

    internal static class GizmoMeshFlags
    {
        public const MeshUpdateFlags Silent =
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontResetBoneBounds |
            MeshUpdateFlags.DontNotifyMeshUsers |
            MeshUpdateFlags.DontRecalculateBounds;
    }
}
