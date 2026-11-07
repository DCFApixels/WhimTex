using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        Material causticsMaterial;
        static readonly int RayMapId = Shader.PropertyToID("_RayMap");
        static readonly int RaySizeId = Shader.PropertyToID("_RaySize");
        static readonly int ReceiverSizeId = Shader.PropertyToID("_ReceiverSize");
        static readonly int PeriodicId = Shader.PropertyToID("_Periodic");

        void RenderCaustics(Texture input, RenderTexture destination, ShaderGraphWrap wrap, Material producer)
        {
            if (causticsMaterial == null)
            {
                var shader = Shader.Find("Hidden/WhimTex/Caustics");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Caustics requires a supported Shader Model 4.5 renderer.");
                causticsMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            // Receiver resolution and ray density are independent. Bound the geometry
            // workload even for 8K canvases; no CPU mesh or per-ray managed allocations.
            int width = Mathf.Clamp(destination.width, 16, 512), height = Mathf.Clamp(destination.height, 16, 512);
            var rays = ProfiledRenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                bool periodic = wrap == ShaderGraphWrap.Repeat;
                rays.filterMode = FilterMode.Point;
                rays.wrapMode = periodic ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                producer.SetVector(BakeSizeId, new Vector4(width, height, 1f / width, 1f / height));
                Graphics.Blit(input, rays, producer, 0);
                causticsMaterial.SetTexture(RayMapId, rays);
                causticsMaterial.SetVector(RaySizeId, new Vector4(width, height, 1f / width, 1f / height));
                causticsMaterial.SetVector(ReceiverSizeId, new Vector4(destination.width, destination.height, 1f / destination.width, 1f / destination.height));
                causticsMaterial.SetInt(PeriodicId, periodic ? 1 : 0);
                Graphics.SetRenderTarget(destination);
                GL.Clear(false, true, Color.clear);
                if (!causticsMaterial.SetPass(0)) throw new InvalidOperationException("Cannot render the caustics projection pass.");
                Graphics.DrawProceduralNow(MeshTopology.Triangles, checked(width * height * 6), periodic ? 9 : 1);
            }
            finally
            {
                causticsMaterial.SetTexture(RayMapId, null);
                RenderTexture.active = previous;
                ProfiledRenderTexture.ReleaseTemporary(rays);
            }
        }
    }
}
