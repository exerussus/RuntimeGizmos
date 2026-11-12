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
        bool _overflowedFrame;

        public Vector3 Min;
        public Vector3 Max;

        // Стартовая ёмкость — ниже неё буфер не сжимается.
        readonly int _minCapacity;

        // Сколько границ кадра подряд буфер был вчетверо больше нужного.
        int _shrinkVotes;

        /// <summary>Сколько границ кадра подряд буфер должен простоять полупустым, чтобы сжаться.</summary>
        internal const int ShrinkAfter = 240;

        public GizmoNativeBuffer(int capacity)
        {
            _minCapacity = Mathf.Max(64, Mathf.NextPowerOfTwo(capacity));
            Allocate(_minCapacity);
            ResetBounds();
        }

        /// <summary>Текущая ёмкость в элементах.</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Раз в кадр, на границе: отдать память после разового пика.
        ///
        /// Буфер только рос, и один кадр с миллионом вершин держал десятки мегабайт
        /// до конца сессии — а в эдит-моде сессия длится до перезагрузки домена. Сжатие
        /// с гистерезисом, как у GPU-буферов канала: вчетверо больше нужного ShrinkAfter
        /// границ подряд. Колебание вокруг степени двойки так ничего не пересоздаёт.
        /// </summary>
        public void TickShrink()
        {
            if (_capacity <= _minCapacity || (long)_count * 4 > _capacity)
            {
                _shrinkVotes = 0;
                return;
            }

            if (++_shrinkVotes < ShrinkAfter) return;
            _shrinkVotes = 0;

            int cap = Mathf.Max(_minCapacity, Mathf.NextPowerOfTwo(Mathf.Max(_count, 1)) * 2);
            if (cap < _capacity) Allocate(cap);
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
            _overflowedFrame = true;

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

        /// <summary>
        /// Упирались в потолок с прошлого снятия. Отдельно от _overflowed: тот — защёлка
        /// «предупредить в консоль один раз за сессию», а счётчикам нужен флаг за кадр,
        /// иначе строка «переполнение» не исчезнет уже никогда.
        /// </summary>
        public bool ConsumeOverflow()
        {
            bool v = _overflowedFrame;
            _overflowedFrame = false;
            return v;
        }

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

        static NativeArray<ushort> _indices16;

        /// <summary>
        /// Тот же пул в 16 битах — для мешей до 65536 вершин, то есть почти для всех.
        /// Индексы у каналов тождественные, и 32 бита на каждый тратили вдвое больше
        /// видеопамяти и выборки, чем нужно.
        /// </summary>
        public static NativeArray<ushort> Indices16 => _indices16;

        public const int MaxIndex16 = 65536;

        public static void Ensure16(int count)
        {
            if (_indices16.IsCreated && _indices16.Length >= count) return;

            int cap = Mathf.Min(MaxIndex16, Mathf.Max(1024, Mathf.NextPowerOfTwo(count)));
            var na = new NativeArray<ushort>(cap, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            ushort* p = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(na);
            for (int i = 0; i < cap; i++) p[i] = (ushort)i;
            if (_indices16.IsCreated) _indices16.Dispose();
            _indices16 = na;
        }

        public static void Dispose()
        {
            if (_indices.IsCreated) _indices.Dispose();
            if (_indices16.IsCreated) _indices16.Dispose();
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
