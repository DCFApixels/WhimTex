using System;
using UnityEngine;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Stable editor-object identity across Unity releases.
    /// Unity 6.4 introduced EntityId-based editor callbacks; older releases use
    /// the int instance ID API. Keep version-specific code in this type only.
    /// </summary>
    internal readonly struct UnityObjectID : IEquatable<UnityObjectID>
    {
#if UNITY_6000_4_OR_NEWER
        private readonly EntityId value;

        private UnityObjectID(EntityId value) => this.value = value;
        public static UnityObjectID FromEntityId(EntityId value) => new UnityObjectID(value);
#else
        private readonly int value;

        private UnityObjectID(int value) => this.value = value;
        public static UnityObjectID FromInstanceId(int value) => new UnityObjectID(value);
#endif

        public static UnityObjectID FromObject(UnityEngine.Object obj)
        {
            if (obj == null) return default(UnityObjectID);
#if UNITY_6000_4_OR_NEWER
            return FromEntityId(obj.GetEntityId());
#else
            return FromInstanceId(obj.GetInstanceID());
#endif
        }

        public bool IsValid => !Equals(default(UnityObjectID));

        public UnityEngine.Object Resolve()
        {
#if UNITY_6000_4_OR_NEWER
            return EditorUtility.EntityIdToObject(value);
#else
            return EditorUtility.InstanceIDToObject(value);
#endif
        }

        public bool Equals(UnityObjectID other) => value.Equals(other.value);
        public override bool Equals(object obj) => obj is UnityObjectID other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public static bool operator ==(UnityObjectID left, UnityObjectID right) => left.Equals(right);
        public static bool operator !=(UnityObjectID left, UnityObjectID right) => !left.Equals(right);
        public override string ToString() => value.ToString();
    }
}
