using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        Texture2D uniformReadback;
        internal int UniformReadbackCount { get; private set; }

        float ReadUniformScalar(RenderTexture source)
        {
            var previous = RenderTexture.active;
            try
            {
                uniformReadback ??= new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                RenderTexture.active = source;
                uniformReadback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                UniformReadbackCount++;
                return uniformReadback.GetPixel(0, 0).r;
            }
            finally { RenderTexture.active = previous; }
        }
    }
}
