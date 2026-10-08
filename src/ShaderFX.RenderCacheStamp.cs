using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [NonSerialized] private RenderCacheValues renderCacheValues;
        [NonSerialized] private RenderCacheCurve renderCacheCurve;
        [NonSerialized] private SourceFingerprint draftFingerprint, appliedFingerprint, expandedFingerprint;

        // Immutable HLSL is hashed once per string replacement. Mutable parameter
        // objects still participate every frame, including direct edits and Undo.
        internal ulong RenderCacheStamp()
        {
            renderCacheValues ??= new RenderCacheValues();
            var values = renderCacheValues;
            values.active = active;
            values.compiledShader = compiledShader;
            values.diagnostics = diagnostics;
            values.lastApplyFailed = lastApplyFailed;
            values.embeddedOwner = embeddedOwner;
            values.documentIncludeBasePath = documentIncludeBasePath;
            values.shaderKey = shaderKey;
            values.shaderCreationRecorded = shaderCreationRecorded;
            values.catalogGuid = catalogGuid;
            values.catalogSourcePath = catalogSourcePath;
            values.catalogDependencyHash = catalogDependencyHash;
            ulong hash = HashText(JsonUtility.ToJson(values));
            hash = HashParameters(hash, parameters);
            hash = HashParameters(hash, appliedParameters);
            hash = Mix(hash, draftFingerprint.Get(code));
            hash = Mix(hash, appliedFingerprint.Get(appliedCode));
            return Mix(hash, expandedFingerprint.Get(appliedSource));
        }

        [Serializable] private sealed class RenderCacheValues
        {
            public bool active, lastApplyFailed, shaderCreationRecorded;
            public Shader compiledShader;
            public TextureCompositor embeddedOwner;
            public string diagnostics, documentIncludeBasePath, shaderKey;
            public string catalogGuid, catalogSourcePath, catalogDependencyHash;
        }

        [Serializable] private sealed class RenderCacheCurve
        {
            public AnimationCurve value;
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
                hash = Mix(hash, HashText(value.name)); hash = Mix(hash, HashText(value.id));
                hash = Mix(hash, (ulong)value.type);
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
                        hash = Mix(hash, unchecked((ulong)(value.textureValue != null ? value.textureValue.GetHashCode() : 0)));
                        hash = Mix(hash, (ulong)value.textureSource); hash = Mix(hash, HashText(value.textureLayerId)); break;
                    case ShaderFXParameterType.Transform2D:
                        hash = Mix(hash, HashText(JsonUtility.ToJson(value.transformValue))); break;
                    case ShaderFXParameterType.Gradient:
                        hash = Mix(hash, value.gradientValue == null ? 0 : HashText(JsonUtility.ToJson(value.gradientValue))); break;
                    case ShaderFXParameterType.Curve:
                        renderCacheCurve ??= new RenderCacheCurve(); renderCacheCurve.value = value.curveValue;
                        hash = Mix(hash, HashText(JsonUtility.ToJson(renderCacheCurve))); break;
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
}
