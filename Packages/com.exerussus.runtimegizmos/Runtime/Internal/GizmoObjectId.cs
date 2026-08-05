using System;
using System.Runtime.CompilerServices;
using Object = UnityEngine.Object;

namespace RuntimeGizmos.Internal
{
    /// <summary>
    /// Идентичность объекта Unity, одинаково работающая до и после 6.5.
    ///
    /// В 6.5 <c>GetInstanceID</c> стал ошибкой компиляции, а <c>EntityId</c> — 64-битный
    /// и в int не влезает. Поэтому здесь именно обёртка над полным значением, а не хеш:
    /// хеш дал бы коллизии, а по этим ключам ищутся регистрации и кэш каркасов.
    /// </summary>
    internal readonly struct GizmoObjectId : IEquatable<GizmoObjectId>
    {
#if UNITY_6000_5_OR_NEWER
        readonly UnityEngine.EntityId _value;
#else
        readonly int _value;
#endif

        GizmoObjectId(
#if UNITY_6000_5_OR_NEWER
            UnityEngine.EntityId
#else
            int
#endif
            value) => _value = value;

        /// <summary>Идентичность объекта. Для null — значение по умолчанию.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GizmoObjectId Of(Object obj)
        {
            if (obj == null) return default;
#if UNITY_6000_5_OR_NEWER
            return new GizmoObjectId(obj.GetEntityId());
#else
            return new GizmoObjectId(obj.GetInstanceID());
#endif
        }

        public bool Equals(GizmoObjectId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is GizmoObjectId o && Equals(o);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();

        public static bool operator ==(GizmoObjectId a, GizmoObjectId b) => a.Equals(b);
        public static bool operator !=(GizmoObjectId a, GizmoObjectId b) => !a.Equals(b);
    }

    /// <summary>Ключ кэша каркасных мешей: исходный меш плюс индекс сабмеша.</summary>
    internal readonly struct GizmoMeshKey : IEquatable<GizmoMeshKey>
    {
        readonly GizmoObjectId _mesh;
        readonly int _submesh;

        public GizmoMeshKey(Object mesh, int submesh)
        {
            _mesh = GizmoObjectId.Of(mesh);
            _submesh = submesh;
        }

        public bool Equals(GizmoMeshKey other) => _submesh == other._submesh && _mesh == other._mesh;
        public override bool Equals(object obj) => obj is GizmoMeshKey k && Equals(k);
        public override int GetHashCode() => _mesh.GetHashCode() * 397 ^ _submesh;
    }
}
