using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public struct ShaderFXTransform
    {
        public Double2 position;
        public Double2 size;
        public double rotation;
        public TransformStorage storage;
        public ProjectiveMatrix matrix;
        public static ShaderFXTransform Default => new ShaderFXTransform { position = new Double2(.5, .5), size = new Double2(1, 1) };

        internal TextureTransform ToLayerTransform(Vector2 dimensions)
        {
            var value = TextureTransform.Default;
            value.position = new Double2((position.x - .5) * dimensions.x, (position.y - .5) * dimensions.y);
            value.scale = size;
            value.rotation = rotation;
            value.pivot = new Double2(.5, .5);
            value.storage = storage;
            value.matrix = matrix;
            return value;
        }

        internal static ShaderFXTransform FromLayerTransform(TextureTransform value, Vector2 dimensions) =>
            new ShaderFXTransform { position = new Double2(value.position.x / dimensions.x + .5, value.position.y / dimensions.y + .5),
                size = value.scale, rotation = value.rotation, storage = value.storage, matrix = value.matrix };

        internal void GetDisplay(Vector2 dimensions, out Double2 location, out Double2 scale, out double angle)
        {
            ToLayerTransform(dimensions).GetDisplay(dimensions, out var offset, out scale, out angle);
            location = new Double2(offset.x / dimensions.x + .5, offset.y / dimensions.y + .5);
        }

        internal void EditPosition(Double2 location, Vector2 dimensions)
        {
            var value = ToLayerTransform(dimensions);
            value.EditPosition(new Double2((location.x - .5) * dimensions.x, (location.y - .5) * dimensions.y), dimensions);
            this = FromLayerTransform(value, dimensions);
        }

        internal void EditSize(Double2 scale, Vector2 dimensions)
        {
            var value = ToLayerTransform(dimensions);
            value.EditScale(scale, dimensions);
            this = FromLayerTransform(value, dimensions);
        }

        internal void EditRotation(double angle, Vector2 dimensions)
        {
            var value = ToLayerTransform(dimensions);
            value.EditRotation(angle, dimensions);
            this = FromLayerTransform(value, dimensions);
        }

        internal bool TrySetMatrix(ProjectiveMatrix matrix)
        {
            var value = TextureTransform.Default;
            if (!value.TrySetMatrix(matrix)) return false;
            this.matrix = value.matrix;
            storage = TransformStorage.Projective;
            return true;
        }

        internal static float SafeSize(float value) => (float)SafeScale(value);
        private static double SafeScale(double value) => value < 0 ? Math.Min(value, -0.00001) : Math.Max(value, 0.00001);

        internal void GetRows(Vector2 dimensions, out Vector4 local0, out Vector4 local1, out Vector4 local2,
            out Vector4 input0, out Vector4 input1, out Vector4 input2)
        {
            var value = ToLayerTransform(dimensions);
            if (storage == TransformStorage.TRS)
                value.scale = new Double2(SafeScale(size.x), SafeScale(size.y));
            var input = value.ToMatrix(dimensions.x, dimensions.y);
            if (!input.TryInverse(out var local)) input = local = ProjectiveMatrix.Identity;
            Rows(local, out local0, out local1, out local2);
            Rows(input, out input0, out input1, out input2);
        }

        private static void Rows(ProjectiveMatrix m, out Vector4 a, out Vector4 b, out Vector4 c)
        {
            a = new Vector4((float)m.m00, (float)m.m01, (float)m.m02, 0);
            b = new Vector4((float)m.m10, (float)m.m11, (float)m.m12, 0);
            c = new Vector4((float)m.m20, (float)m.m21, (float)m.m22, 0);
        }
    }
}
