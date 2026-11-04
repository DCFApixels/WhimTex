using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderTextureSource { Texture = 0, Layer = 1, None = 2, Self = 3 }

    public enum ShaderParameterType { Float, Color, Vector, Texture2D, Transform2D, Toggle, Enum, Gradient, Vector2, Vector3, Normal, Curve, Point, Seed }

    public enum ShaderContainerKind { Group, Foldout, Tabs, Tab }

    [Serializable]
    public struct ShaderLayoutContainer : IEquatable<ShaderLayoutContainer>
    {
        public ShaderContainerKind kind;
        public int id;
        public string title;
        public string headerParameter;

        public bool Equals(ShaderLayoutContainer other) => kind == other.kind && id == other.id &&
            string.Equals(title ?? string.Empty, other.title ?? string.Empty, StringComparison.Ordinal) &&
            string.Equals(headerParameter ?? string.Empty, other.headerParameter ?? string.Empty, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ShaderLayoutContainer other && Equals(other);
        public override int GetHashCode() => id;
    }

    [Serializable]
    public struct ShaderEnableCondition : IEquatable<ShaderEnableCondition>
    {
        public string parameter;
        public bool notEqual;
        public double value;
        public bool Equals(ShaderEnableCondition other) => parameter == other.parameter && notEqual == other.notEqual && value.Equals(other.value);
        public override bool Equals(object obj) => obj is ShaderEnableCondition other && Equals(other);
        public override int GetHashCode() => parameter?.GetHashCode() ?? 0;
    }

    [Serializable]
    public sealed class ShaderParameterControl
    {
        public string tooltip;
        public string label;
        public string previewFunction;
        public string[] previewParameters = Array.Empty<string>();
        public string[] headers = Array.Empty<string>();
        public string[] helpBoxes = Array.Empty<string>();
        public bool hidden;
        public bool seed;
        public bool inGroup;
        public int groupId;
        public string groupTitle;
        public string groupHeaderParameter;
        public ShaderLayoutContainer[] containers = Array.Empty<ShaderLayoutContainer>();
        public ShaderParameterType type;
        public int order;
        public bool hasMinimum, hasMaximum;
        public bool softMinimum, softMaximum;
        public bool cyclicRange;
        public float minimum, maximum;
        public string[] optionNames = Array.Empty<string>();
        public double[] optionValues = Array.Empty<double>();
        public string valueType;
        public bool rgba;
        public double valueMinimum, valueMaximum;
        // Editor-only visibility condition. Values remain serialized and applied to the shader.
        public string visibleIfParameter;
        public bool visibleIfNotEqual;
        public double visibleIfValue;
        public ShaderEnableCondition[] enableIf = Array.Empty<ShaderEnableCondition>();
    }

    [Serializable]
    public sealed partial class ShaderParameter
    {
        public string name = "_Amount";
        public ShaderParameterType type;
        public float floatValue = 1f;
        [ColorUsage(true, true)] public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public Texture2D textureValue;
        public ShaderTextureSource textureSource;
        public string textureLayerId;
        public WhimTexGradient gradientValue;
        public AnimationCurve curveValue;
        public ShaderTransform transformValue = ShaderTransform.Default;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        [HideInInspector] public bool hasMinimum, hasMaximum;
        [HideInInspector] public bool softMinimum, softMaximum;
        [HideInInspector] public bool cyclicRange;
        [HideInInspector] public bool pixelUnits;
        internal bool HasSoftRange => softMinimum || softMaximum;
        internal bool IsInterval => type == ShaderParameterType.Vector2 && hasMinimum && hasMaximum;
        [HideInInspector] public float minimum, maximum;
        [HideInInspector] public List<ShaderParameterControl> controls = new List<ShaderParameterControl>();
        [NonSerialized] private string cachedName, cachedId;
        [NonSerialized] private string cachedPrefix;
        [NonSerialized] private int propertyId, gradientPropertyId, gradientWrapPropertyId, curvePropertyId;
        [NonSerialized] private int textureMapId, textureSizeId;
        [NonSerialized] private int[] transformPropertyIds;
        [NonSerialized] internal RenderFingerprint.ObjectStamp textureFingerprint;
        [NonSerialized] internal RenderFingerprint.TextStamp nameFingerprint, idFingerprint, textureLayerFingerprint;

        private void PreparePropertyIds()
        {
            if (cachedPrefix != null && cachedName == name && cachedId == id) return;
            cachedName = name; cachedId = id;
            cachedPrefix = "_WhimTex_" + name.TrimStart('_') + "_" + id + "_";
            propertyId = Shader.PropertyToID(name);
            textureMapId = Shader.PropertyToID(ShaderTextureViews.Prefix(name) + "_UvMap");
            textureSizeId = Shader.PropertyToID(ShaderTextureViews.Prefix(name) + "_Size");
            gradientPropertyId = Shader.PropertyToID(cachedPrefix + "Gradient");
            gradientWrapPropertyId = Shader.PropertyToID(cachedPrefix + "GradientWrap");
            curvePropertyId = Shader.PropertyToID(cachedPrefix + "Curve");
            transformPropertyIds = null;
        }
        internal string InternalPrefix { get { PreparePropertyIds(); return cachedPrefix; } }
        internal int PropertyId { get { PreparePropertyIds(); return propertyId; } }
        internal int TextureMapId { get { PreparePropertyIds(); return textureMapId; } }
        internal int TextureSizeId { get { PreparePropertyIds(); return textureSizeId; } }
        internal int GradientPropertyId { get { PreparePropertyIds(); return gradientPropertyId; } }
        internal int GradientWrapPropertyId { get { PreparePropertyIds(); return gradientWrapPropertyId; } }
        internal int CurvePropertyId { get { PreparePropertyIds(); return curvePropertyId; } }
        internal int[] TransformPropertyIds
        {
            get
            {
                PreparePropertyIds();
                if (transformPropertyIds == null)
                {
                    string prefix = cachedPrefix;
                    transformPropertyIds = new[] { Shader.PropertyToID(prefix + "ToLocalRow0"), Shader.PropertyToID(prefix + "ToLocalRow1"), Shader.PropertyToID(prefix + "ToLocalRow2"),
                        Shader.PropertyToID(prefix + "ToInputRow0"), Shader.PropertyToID(prefix + "ToInputRow1"), Shader.PropertyToID(prefix + "ToInputRow2") };
                }
                return transformPropertyIds;
            }
        }
        internal float Clamp(float value) => cyclicRange ? RepeatRange(value, minimum, maximum) :
            hasMinimum && !softMinimum && value < minimum ? minimum : hasMaximum && !softMaximum && value > maximum ? maximum : value;
        internal static float RepeatRange(float value, float minimum, float maximum)
        {
            if (!ProjectiveMatrix.Finite(value)) return minimum;
            if (value >= minimum && value <= maximum) return value;
            double period = (double)maximum - minimum;
            double offset = ((double)value - minimum) % period;
            return (float)(minimum + (offset < 0 ? offset + period : offset));
        }
        internal Vector4 ClampInterval(Vector4 value)
        {
            if (!IsInterval) return value;
            if (cyclicRange && IsFullCycle(value, minimum, maximum)) return new Vector4(value.x, value.y, 0, 0);
            float x = ProjectiveMatrix.Finite(value.x) ? Mathf.Clamp(value.x, minimum, maximum) : minimum;
            float y = ProjectiveMatrix.Finite(value.y) ? Mathf.Clamp(value.y, minimum, maximum) : maximum;
            return cyclicRange ? new Vector4(x, y, 0, 0) : new Vector4(Mathf.Min(x, y), Mathf.Max(x, y), 0, 0);
        }
        internal static bool IsFullCycle(Vector2 value, float minimum, float maximum)
        {
            float period = maximum - minimum;
            double rounding = Math.Max(Math.Abs(value.x), Math.Max(Math.Abs(value.y), Math.Abs(period))) * (1.0 / 16777216.0);
            return ProjectiveMatrix.Finite(value.x) && ProjectiveMatrix.Finite(value.y) && value.y > value.x &&
                (value.x >= minimum && value.x <= maximum || value.y >= minimum && value.y <= maximum) &&
                Math.Abs((double)value.y - value.x - period) <= rounding;
        }
        internal static bool IsValidInterval(Vector2 value, float minimum, float maximum, bool cyclic)
        {
            return cyclic && IsFullCycle(value, minimum, maximum) ||
                ProjectiveMatrix.Finite(value.x) && ProjectiveMatrix.Finite(value.y) &&
                value.x >= minimum && value.x <= maximum && value.y >= minimum && value.y <= maximum &&
                (cyclic || value.x <= value.y);
        }
        internal static Vector3 NormalizeNormal(Vector4 value)
        {
            var v = new Vector3(value.x, value.y, value.z);
            return v.sqrMagnitude > 1e-12f && !float.IsInfinity(v.sqrMagnitude) ? v.normalized : Vector3.forward;
        }
        internal bool BoolValue => GetNumber() >= 0.5;

        internal ShaderParameter Copy()
        {
            var copy = (ShaderParameter)MemberwiseClone();
            WhimTexDocumentJson.PreserveShaderParameterAssets(this, copy);
            copy.integerValues = integerValues == null ? new long[4] : (long[])integerValues.Clone();
            copy.typedPropertyIds = null;
            copy.gradientValue = gradientValue?.Clone();
            copy.curveValue = curveValue == null ? null : WhimTexCurveTexture.Copy(curveValue);
            copy.controls = new List<ShaderParameterControl>(controls.Count);
            foreach (var c in controls)
                copy.controls.Add(new ShaderParameterControl { type = c.type, valueType = c.valueType, rgba = c.rgba, valueMinimum = c.valueMinimum, valueMaximum = c.valueMaximum, order = c.order, tooltip = c.tooltip, label = c.label, previewFunction = c.previewFunction,
                    previewParameters = c.previewParameters == null ? Array.Empty<string>() : (string[])c.previewParameters.Clone(),
                    headers = c.headers == null ? Array.Empty<string>() : (string[])c.headers.Clone(),
                    helpBoxes = c.helpBoxes == null ? Array.Empty<string>() : (string[])c.helpBoxes.Clone(),
                    hidden = c.hidden, seed = c.seed, inGroup = c.inGroup, groupId = c.groupId,
                    groupTitle = c.groupTitle, groupHeaderParameter = c.groupHeaderParameter,
                    containers = c.containers == null ? Array.Empty<ShaderLayoutContainer>() : (ShaderLayoutContainer[])c.containers.Clone(),
                    hasMinimum = c.hasMinimum, hasMaximum = c.hasMaximum, softMinimum = c.softMinimum, softMaximum = c.softMaximum, cyclicRange = c.cyclicRange, minimum = c.minimum, maximum = c.maximum,
                    optionNames = (string[])c.optionNames.Clone(), optionValues = (double[])c.optionValues.Clone(),
                    visibleIfParameter = c.visibleIfParameter, visibleIfNotEqual = c.visibleIfNotEqual, visibleIfValue = c.visibleIfValue,
                    enableIf = c.enableIf == null ? Array.Empty<ShaderEnableCondition>() : (ShaderEnableCondition[])c.enableIf.Clone() });
            return copy;
        }

        internal void SetValue(Material material, ShaderParameter declaration, Vector2 dimensions, float pixelScale = 1f)
        {
            if (ShaderNumericValue.NeedsTypedBinding(declaration)) { SetTypedValue(material, declaration); return; }
            int property = declaration.PropertyId;
            float units = declaration.pixelUnits ? pixelScale : 1f;
            switch (type)
            {
                case ShaderParameterType.Enum:
                case ShaderParameterType.Float: material.SetFloat(property, floatValue * units); break;
                case ShaderParameterType.Toggle: material.SetFloat(property, BoolValue ? 1f : 0f); break;
                case ShaderParameterType.Color: HdrUtility.SetShaderColor(material, declaration.name, colorValue); break;
                case ShaderParameterType.Vector2:
                case ShaderParameterType.Point:
                case ShaderParameterType.Vector3:
                case ShaderParameterType.Vector: material.SetVector(property, vectorValue * units); break;
                case ShaderParameterType.Normal: material.SetVector(property, NormalizeNormal(vectorValue)); break;
                case ShaderParameterType.Texture2D:
                    material.SetTexture(property, textureValue != null ? textureValue : Texture2D.whiteTexture);
                    break;
                case ShaderParameterType.Transform2D:
                    transformValue.GetRows(dimensions, out var l0, out var l1, out var l2, out var i0, out var i1, out var i2);
                    int[] ids = declaration.TransformPropertyIds;
                    material.SetVector(ids[0], l0);
                    material.SetVector(ids[1], l1);
                    material.SetVector(ids[2], l2);
                    material.SetVector(ids[3], i0);
                    material.SetVector(ids[4], i1);
                    material.SetVector(ids[5], i2);
                    break;
            }
        }
    }
}
