using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Narrow file reader for retired fields and data normalization from 0.12.5.
    // Match the owner as well as the name: an extension's similarly named field is not retired.
    internal static class WhimTexFileCompatibility0125
    {
        // Read-only provenance: a removed @param must not become a manual declaration.
        private static readonly ConditionalWeakTable<ShaderFXParameter, object> SourceParameters = new();
        private static readonly object SourceParameter = new();
        internal static void ReadDeclarationFlag(ShaderFXParameter parameter, bool declared)
        {
            if (declared) SourceParameters.GetValue(parameter, _ => SourceParameter);
        }
        internal static bool IsRetiredField(Type owner, string name)
        {
            if (owner == typeof(TextureCompositor))
                return name == "outputSettings" || name == "savedOutputSettings" || name == "spriteSlices";
            if (owner == typeof(DrawingLayerBehaviour))
                return name == "originalImageUrl" || name == "originalImageRevision" || name == "pixelsRevision";
            if (owner == typeof(ShaderFXParameter)) return name == "declaredInCode";
            return false;
        }
        internal static bool IsReadOnlyField(Type owner, string name) => owner == typeof(ShapeLayerBehaviour) && name == "roundness";

        internal static void Normalize(object value, float? uniformRoundness = null)
        {
            if (value is NoiseLayerBehaviour noise)
            {
                if (noise.scaleY == 0f) noise.scaleY = noise.scale;
                if (noise.warpScaleY == 0f) noise.warpScaleY = noise.warpScale;
            }
            else if (value is FillPatternSettings pattern && pattern.sizeY == 0f)
                pattern.sizeY = pattern.size;
            else if (value is ShapeLayerBehaviour shape)
            {
                var corners = shape.cornerRoundness;
                for (int i = 0; i < 4; i++)
                    if (corners[i] < 0f) corners[i] = uniformRoundness ?? 0f;
                shape.cornerRoundness = corners;
            }
            else if (value is ShaderFX effect) effect.NormalizeFileParameters();
        }

        // File input only. Values remain stored, including arbitrary gradients/curves and references.
        internal static string DeclareSavedParameters(string source, IReadOnlyList<ShaderFXParameter> saved)
        {
            List<ShaderFXParameter> declarations;
            try { declarations = ShaderFXMetadata.Parse(source, false, out _); }
            catch (FormatException) { return source; } // Preserve broken drafts for Apply diagnostics.
            var extra = new StringBuilder();
            foreach (var parameter in saved)
            {
                if (parameter == null || parameter.controls.Count > 0 || SourceParameters.TryGetValue(parameter, out _) || declarations.Exists(p =>
                    ShaderFXMetadata.MatchesNameOrFormerName(p, parameter.name, parameter.type))) continue;
                string type = parameter.type switch
                {
                    ShaderFXParameterType.Float or ShaderFXParameterType.Enum => "float",
                    ShaderFXParameterType.Color => "color",
                    ShaderFXParameterType.Vector => "float4",
                    ShaderFXParameterType.Texture2D => "texture2D",
                    ShaderFXParameterType.Transform2D => "transform2D",
                    ShaderFXParameterType.Bool => "bool",
                    ShaderFXParameterType.Gradient => "gradient",
                    ShaderFXParameterType.Vector2 => "float2",
                    ShaderFXParameterType.Vector3 => "float3",
                    ShaderFXParameterType.Normal => "normal",
                    ShaderFXParameterType.Curve => "curve",
                    ShaderFXParameterType.Point => "point",
                    _ => throw new FormatException("Unsupported saved parameter type: " + parameter.type)
                };
                if (parameter.controls.Count == 0 && ShaderFXMetadata.IsScalar(parameter.type))
                    parameter.floatValue = parameter.Clamp(parameter.floatValue);
                string declaration = "// @param " + type + " " + parameter.name;
                if (type == "float") declaration = ShaderFXPresetWriter.Declaration(parameter);
                extra.AppendLine(declaration);
            }
            return extra.Length == 0 ? source : (source ?? string.Empty) + "\n" + extra;
        }
    }
}
