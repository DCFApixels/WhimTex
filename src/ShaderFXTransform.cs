using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public struct ShaderFXTransform
    {
        public Vector2 position;
        public Vector2 size;
        public float rotation;
        public static ShaderFXTransform Default => new ShaderFXTransform { position = Vector2.one * 0.5f, size = Vector2.one };

        internal TextureTransform ToLayerTransform(Vector2 dimensions)
        {
            var value = TextureTransform.Default;
            value.position = Vector2.Scale(position - Vector2.one * 0.5f, dimensions);
            value.scale = size;
            value.rotation = rotation;
            value.pivot = Vector2.one * 0.5f;
            return value;
        }

        internal static ShaderFXTransform FromLayerTransform(TextureTransform value, Vector2 dimensions) =>
            new ShaderFXTransform { position = new Vector2((float)(value.position.x / dimensions.x), (float)(value.position.y / dimensions.y)) + Vector2.one * 0.5f,
                size = value.scale, rotation = (float)value.rotation };

        internal static float SafeSize(float value) => value < 0 ? Mathf.Min(value, -0.00001f) : Mathf.Max(value, 0.00001f);

        internal void GetRows(Vector2 dimensions, out Vector4 local0, out Vector4 local1, out Vector4 input0, out Vector4 input1)
        {
            float c = Mathf.Cos(rotation * Mathf.Deg2Rad), s = Mathf.Sin(rotation * Mathf.Deg2Rad);
            float aspect = dimensions.x / dimensions.y;
            float sx = SafeSize(size.x), sy = SafeSize(size.y);
            float a = c * sx, b = -s * sy / aspect, d = s * sx * aspect, e = c * sy;
            input0 = new Vector4(a, b, position.x - 0.5f * (a + b), 0);
            input1 = new Vector4(d, e, position.y - 0.5f * (d + e), 0);
            a = c / sx; b = s / (sx * aspect); d = -s * aspect / sy; e = c / sy;
            local0 = new Vector4(a, b, 0.5f - a * position.x - b * position.y, 0);
            local1 = new Vector4(d, e, 0.5f - d * position.x - e * position.y, 0);
        }
    }
}
