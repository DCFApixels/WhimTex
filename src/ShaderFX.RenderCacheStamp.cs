using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [NonSerialized] private RenderFingerprint.ObjectStamp shaderFingerprint, ownerFingerprint;
        [NonSerialized] private SourceFingerprint draftFingerprint, appliedFingerprint, expandedFingerprint;
        [NonSerialized] private SourceFingerprint diagnosticFingerprint, includeFingerprint, keyFingerprint, guidFingerprint, pathFingerprint, dependencyFingerprint;

        // Immutable HLSL is hashed once per string replacement. Mutable parameter
        // objects still participate every frame, including direct edits and Undo.
        internal ulong RenderCacheStamp()
        {
            ulong hash = Mix(14695981039346656037UL, active ? 1UL : 0UL);
            hash = Mix(hash, shaderFingerprint.Get(compiledShader));
            hash = Mix(hash, diagnosticFingerprint.Get(diagnostics));
            hash = Mix(hash, lastApplyFailed ? 1UL : 0UL);
            hash = Mix(hash, ownerFingerprint.Get(embeddedOwner));
            hash = Mix(hash, includeFingerprint.Get(documentIncludeBasePath));
            hash = Mix(hash, keyFingerprint.Get(shaderKey));
            hash = Mix(hash, shaderCreationRecorded ? 1UL : 0UL);
            hash = Mix(hash, guidFingerprint.Get(catalogGuid));
            hash = Mix(hash, pathFingerprint.Get(catalogSourcePath));
            hash = Mix(hash, dependencyFingerprint.Get(catalogDependencyHash));
            hash = HashParameters(hash, parameters);
            hash = HashParameters(hash, appliedParameters);
            hash = Mix(hash, draftFingerprint.Get(code));
            hash = Mix(hash, appliedFingerprint.Get(appliedCode));
            return Mix(hash, expandedFingerprint.Get(appliedSource));
        }

        // Only values consumed by GetMaterial affect pixels, not labels/help or
        // the unused color/vector/transform defaults of a scalar declaration.
        private ulong HashParameters(ulong hash, List<ShaderFXParameter> source)
        {
            hash = Mix(hash, source == null ? ulong.MaxValue : (ulong)source.Count);
            if (source == null) return hash;
            foreach (var value in source)
            {
                hash = Mix(hash, value == null ? 0UL : 1UL);
                if (value == null) continue;
                hash = Mix(hash, value.nameFingerprint.Get(value.name)); hash = Mix(hash, value.idFingerprint.Get(value.id));
                hash = Mix(hash, (ulong)value.type);
                hash = Mix(hash, value.pixelUnits ? 1UL : 0UL);
                switch (value.type)
                {
                    case ShaderFXParameterType.Float:
                    case ShaderFXParameterType.Bool:
                    case ShaderFXParameterType.Enum:
                        hash = MixFloat(hash, value.floatValue); break;
                    case ShaderFXParameterType.Color:
                        hash = MixFloat(MixFloat(MixFloat(MixFloat(hash, value.colorValue.r), value.colorValue.g), value.colorValue.b), value.colorValue.a); break;
                    case ShaderFXParameterType.Vector:
                    case ShaderFXParameterType.Vector2:
                    case ShaderFXParameterType.Vector3:
                    case ShaderFXParameterType.Normal:
                    case ShaderFXParameterType.Point:
                        hash = MixFloat(MixFloat(MixFloat(MixFloat(hash, value.vectorValue.x), value.vectorValue.y), value.vectorValue.z), value.vectorValue.w); break;
                    case ShaderFXParameterType.Texture2D:
                        hash = Mix(hash, value.textureFingerprint.Get(value.textureValue));
                        hash = Mix(hash, (ulong)value.textureSource); hash = Mix(hash, value.textureLayerFingerprint.Get(value.textureLayerId)); break;
                    case ShaderFXParameterType.Transform2D:
                        var transform = value.transformValue;
                        hash = RenderFingerprint.Double(hash, transform.position.x); hash = RenderFingerprint.Double(hash, transform.position.y);
                        hash = RenderFingerprint.Double(hash, transform.size.x); hash = RenderFingerprint.Double(hash, transform.size.y);
                        hash = RenderFingerprint.Double(hash, transform.rotation); hash = Mix(hash, (ulong)transform.storage);
                        var m = transform.matrix;
                        hash = RenderFingerprint.Double(hash, m.m00); hash = RenderFingerprint.Double(hash, m.m01); hash = RenderFingerprint.Double(hash, m.m02);
                        hash = RenderFingerprint.Double(hash, m.m10); hash = RenderFingerprint.Double(hash, m.m11); hash = RenderFingerprint.Double(hash, m.m12);
                        hash = RenderFingerprint.Double(hash, m.m20); hash = RenderFingerprint.Double(hash, m.m21); hash = RenderFingerprint.Double(hash, m.m22);
                        break;
                    case ShaderFXParameterType.Gradient:
                        hash = Mix(hash, value.gradientValue == null ? 0 : value.gradientValue.ContentFingerprint()); break;
                    case ShaderFXParameterType.Curve:
                        var curve = value.curveValue;
                        hash = Mix(hash, curve == null ? ulong.MaxValue : (ulong)curve.length);
                        if (curve == null) break;
                        hash = Mix(hash, (ulong)curve.preWrapMode); hash = Mix(hash, (ulong)curve.postWrapMode);
                        // The indexer reads keys without allocating AnimationCurve.keys arrays.
                        for (int i = 0; i < curve.length; i++)
                        {
                            var key = curve[i];
                            hash = MixFloat(hash, key.time); hash = MixFloat(hash, key.value);
                            hash = MixFloat(hash, key.inTangent); hash = MixFloat(hash, key.outTangent);
                            hash = MixFloat(hash, key.inWeight); hash = MixFloat(hash, key.outWeight);
                            hash = Mix(hash, (ulong)key.weightedMode);
                        }
                        break;
                    default: hash = Mix(hash, HashText(JsonUtility.ToJson(value))); break;
                }
            }
            return hash;
        }

        private static ulong MixFloat(ulong hash, float value) => Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

        private struct SourceFingerprint
        {
            private string source;
            private ulong hash;
            internal ulong Get(string value)
            {
                if (!ReferenceEquals(source, value))
                {
                    source = value;
                    hash = HashText(value);
                }
                return hash;
            }
        }

        private static ulong HashText(string value)
        {
            if (value == null) return 0;
            ulong hash = 14695981039346656037UL;
            foreach (char c in value) hash = Mix(hash, c);
            return Mix(hash, (ulong)value.Length);
        }
        private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
    }

    // Content, not revisions: direct mutable-data edits and Undo must invalidate too.
    internal static class RenderFingerprint
    {
        internal static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
        internal static ulong Float(ulong hash, float value) => Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        internal static ulong Double(ulong hash, double value) => Mix(hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        internal struct TextStamp
        {
            string text;
            ulong hash;
            internal ulong Get(string value)
            {
                if (!ReferenceEquals(text, value))
                {
                    text = value; hash = 0;
                    if (value != null)
                    {
                        hash = 14695981039346656037UL;
                        foreach (char c in value) hash = Mix(hash, c);
                        hash = Mix(hash, (ulong)value.Length);
                    }
                }
                return hash;
            }
        }
        internal struct ObjectStamp
        {
            UnityObjectID id;
            ulong hash;
            bool initialized;
            internal ulong Get(UnityEngine.Object value)
            {
                var next = UnityObjectID.FromObject(value);
                if (!initialized || !id.Equals(next))
                {
                    initialized = true; id = next; hash = 14695981039346656037UL;
                    foreach (char c in id.ToString()) hash = Mix(hash, c);
                }
                return hash;
            }
        }
    }
}
