using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        Material causticsMaterial;
        Vector4[] causticsWeights;
        // CIE 1931 / D65, in projection order from 664.46 to 434.72 nm.
        // vis.gl XYZ bands transformed to linear sRGB and normalized to sum to white.
        // Keep signed weights until after all bands overlap. See ThirdPartyNotices.md.
        static readonly Vector4[] CieD65Weights =
        {
            new Vector4( .098109451f, -.010122706f, -.000726397f, 0),
            new Vector4( .761519073f, -.027248051f, -.011985062f, 0),
            new Vector4( .549441211f,  .405399950f, -.060711233f, 0),
            new Vector4(-.357448968f,  .622482230f, -.023446752f, 0),
            new Vector4(-.113213731f,  .069418560f,  .609301223f, 0),
            new Vector4( .061592964f, -.059929983f,  .487568222f, 0),
        };
        static readonly int CausticsResponseId = Shader.PropertyToID("_CausticsResponse");
        static readonly int RayMapId = Shader.PropertyToID("_RayMap");
        static readonly int RaySizeId = Shader.PropertyToID("_RaySize");
        static readonly int ReceiverSizeId = Shader.PropertyToID("_ReceiverSize");
        static readonly int PeriodicId = Shader.PropertyToID("_Periodic");
        static readonly int CausticsBandId = Shader.PropertyToID("_GraphCausticsBand");
        Vector4[] PrepareCausticsWeights(Material producer, ShaderGraphBufferPass pass)
        {
            if (pass.causticsSpectrum == null) return CieD65Weights;
            causticsWeights ??= new Vector4[6];
            // Gradient LUTs retain their CPU copy. Read six bands without GPU readback.
            var texture = producer.GetTexture(pass.causticsSpectrum.GradientPropertyId) as Texture2D;
            if (texture == null || !texture.isReadable) throw new InvalidOperationException("Caustics Spectrum requires a readable gradient LUT.");
            int wrap = Mathf.RoundToInt(producer.GetFloat(pass.causticsSpectrum.GradientWrapPropertyId));
            Vector4 total = Vector4.zero;
            for (int i=0;i<6;i++)
            {
                float t = wrap == 1 && i == 5 ? 0 : i/5f;
                float x = t*(texture.width-1); int left = Mathf.FloorToInt(x);
                Color c = texture.filterMode == FilterMode.Point ? texture.GetPixel(Mathf.FloorToInt(x+.5f),0) :
                    Color.LerpUnclamped(texture.GetPixel(left,0),texture.GetPixel(Mathf.Min(left+1,texture.width-1),0),x-left);
                causticsWeights[i]=new Vector4(Mathf.Max(c.r,0),Mathf.Max(c.g,0),Mathf.Max(c.b,0),0);
                total+=causticsWeights[i];
            }
            for (int i=0;i<6;i++) for (int channel=0;channel<3;channel++)
                causticsWeights[i][channel]/=pass.causticsWhiteBalance?Mathf.Max(total[channel],1e-6f):6;
            return causticsWeights;
        }

        void RenderCaustics(Texture input, RenderTexture destination, ShaderGraphBufferPass pass, Material producer)
        {
            bool color = pass.causticsColor;
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
                bool periodic = pass.wrap == ShaderGraphWrap.Repeat;
                var weights = color ? PrepareCausticsWeights(producer, pass) : null;
                rays.filterMode = FilterMode.Point;
                rays.wrapMode = periodic ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                producer.SetVector(BakeSizeId, new Vector4(width, height, 1f / width, 1f / height));
                causticsMaterial.SetTexture(RayMapId, rays);
                causticsMaterial.SetVector(RaySizeId, new Vector4(width, height, 1f / width, 1f / height));
                causticsMaterial.SetVector(ReceiverSizeId, new Vector4(destination.width, destination.height, 1f / destination.width, 1f / destination.height));
                causticsMaterial.SetInt(PeriodicId, periodic ? 1 : 0);
                Graphics.SetRenderTarget(destination);
                GL.Clear(false, true, color ? new Color(0,0,0,1) : Color.clear);
                for (int band = 0; band < (color ? 6 : 1); band++)
                {
                    producer.SetInt(CausticsBandId, color ? band : 0);
                    Graphics.Blit(input, rays, producer, 0);
                    causticsMaterial.SetVector(CausticsResponseId, color ? weights[band] : new Vector4(1,0,0,0));
                    Graphics.SetRenderTarget(destination);
                    if (!causticsMaterial.SetPass(0)) throw new InvalidOperationException("Cannot render the caustics projection pass.");
                    Graphics.DrawProceduralNow(MeshTopology.Triangles, checked(width * height * 6), periodic ? 9 : 1);
                }
            }
            finally
            {
                causticsMaterial.SetTexture(RayMapId, null);
                producer.SetInt(CausticsBandId, 0);
                RenderTexture.active = previous;
                ProfiledRenderTexture.ReleaseTemporary(rays);
            }
        }
    }
}
