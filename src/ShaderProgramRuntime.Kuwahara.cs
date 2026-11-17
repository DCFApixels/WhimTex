using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        static ComputeShader kuwaharaTables, kuwaharaAnisotropic;
        static readonly string[] KuwaharaKernels = { "Rows", "RowCarry", "Columns", "ColumnCarry" };

        void RenderKuwahara(Texture input, BufferImage image, in LayerRenderContext context)
        {
            const int tileSize = 256;
            float requested = Material.GetVector(image.pass.imageRadius).x;
            int radius = float.IsFinite(requested) ? (int)Math.Min(Math.Max(Math.Floor(requested), 0), int.MaxValue / 4) : 0;
            bool anisotropic = image.pass.kuwaharaAnisotropic;
            float Finite(string name, float fallback)
            { float value = Material.GetVector(name).x; return float.IsFinite(value) ? value : fallback; }
            var settings = Vector4.zero;
            if (anisotropic)
            {
                settings = new Vector4(float.IsFinite(requested) ? Math.Max(0, requested) : 0,
                    Mathf.Clamp01(Finite(image.pass.kuwaharaSharpness, 1)),
                    Math.Max(0, Mathf.Floor(Finite(image.pass.kuwaharaUniformity, 4))),
                    Mathf.Clamp(Finite(image.pass.kuwaharaEccentricity, 1), 0, 2));
                radius = (int)Math.Min(int.MaxValue / 4, Math.Max(Math.Ceiling(settings.x * (1 + Math.Max(.01, settings.w))), settings.z + 1));
            }
            // A halo contains real evaluations outside the canvas, not repeated edge pixels.
            if ((long)radius * 2 + tileSize > SystemInfo.maxTextureSize)
                throw new ShaderGraphException(image.pass.nodeId, "Kuwahara sampling halo exceeds this device's maximum texture size. Reduce Radius or connect Radius to use direct branch evaluation.");
            bool tables = !anisotropic && !image.pass.kuwaharaHighPrecision && SystemInfo.supportsComputeShaders &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore);
            if (tables && kuwaharaTables == null)
                kuwaharaTables = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/KuwaharaTables.compute");
            tables &= kuwaharaTables != null;
            int[] kernels = null;
            if (tables)
            {
                kernels = new int[4];
                for (int i = 0; i < 4; i++) { kernels[i] = kuwaharaTables.FindKernel(KuwaharaKernels[i]); tables &= kuwaharaTables.IsSupported(kernels[i]); }
            }
            int[] tensorKernels = null;
            if (anisotropic && settings.x > 0 && SystemInfo.supportsComputeShaders &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore))
            {
                if (kuwaharaAnisotropic == null)
                    kuwaharaAnisotropic = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/KuwaharaAnisotropic.compute");
                if (kuwaharaAnisotropic != null)
                {
                    tensorKernels = new[] { kuwaharaAnisotropic.FindKernel("Tensor"), kuwaharaAnisotropic.FindKernel("BlurX"), kuwaharaAnisotropic.FindKernel("BlurY") };
                    foreach (int kernel in tensorKernels) if (!kuwaharaAnisotropic.IsSupported(kernel)) { tensorKernels = null; break; }
                }
            }
            var producer = image.imageSource.material;
            var filter = image.material;
            producer.CopyPropertiesFromMaterial(Material);
            if (image.imageSource.pass.program.usesLighting) context.activeDocument.BindLighting(producer);
            filter.SetVector(ViewportId, new Vector4(0, 0, 1, 1));
            int width = image.texture.width, height = image.texture.height;
            for (int y = 0; y < height; y += tileSize)
            for (int x = 0; x < width; x += tileSize)
            {
                int w = Math.Min(tileSize, width - x), h = Math.Min(tileSize, height - y);
                int sw = w + 2 * radius, sh = h + 2 * radius;
                RenderTexture source = null, sumA = null, squareA = null, sumB = null, squareB = null, output = null, tensorA = null, tensorB = null;
                try
                {
                    source = ImageFilterTemporary(sw, sh);
                    producer.SetVector(ViewportId, new Vector4((float)(x - radius) / width, (float)(y - radius) / height, (float)sw / width, (float)sh / height));
                    producer.SetVector(BakeSizeId, new Vector4(width, height, 1f / width, 1f / height));
                    Graphics.Blit(input, source, producer, 0);
                    if (tables)
                    {
                        sumA = ImageFilterTemporary(sw, sh, true); squareA = ImageFilterTemporary(sw, sh, true);
                        sumB = ImageFilterTemporary(sw, sh, true); squareB = ImageFilterTemporary(sw, sh, true);
                        kuwaharaTables.SetInts("_Size", sw, sh);
                        for (int stage = 0; stage < 4; stage++)
                        {
                            int kernel = kernels[stage]; bool even = (stage & 1) == 0;
                            if (stage == 0) kuwaharaTables.SetTexture(kernel, "_Source", source);
                            if (stage != 0)
                            {
                                kuwaharaTables.SetTexture(kernel, "_SumIn", even ? sumB : sumA);
                                kuwaharaTables.SetTexture(kernel, "_SquareIn", even ? squareB : squareA);
                            }
                            kuwaharaTables.SetTexture(kernel, "_SumOut", even ? sumA : sumB);
                            kuwaharaTables.SetTexture(kernel, "_SquareOut", even ? squareA : squareB);
                            kuwaharaTables.Dispatch(kernel, stage == 0 ? (sw + 127) / 128 : stage == 2 ? sw : (sw + 7) / 8,
                                stage == 0 ? sh : stage == 2 ? (sh + 127) / 128 : (sh + 7) / 8, 1);
                        }
                    }
                    if (tensorKernels != null)
                    {
                        tensorA = ImageFilterTemporary(sw, sh, true);
                        kuwaharaAnisotropic.SetInts("_Size", sw, sh);
                        kuwaharaAnisotropic.SetInt("_Components", ShaderGraphNodes.Components(image.pass.type));
                        kuwaharaAnisotropic.SetInt("_Uniformity", (int)settings.z);
                        if (settings.z > 0) tensorB = ImageFilterTemporary(sw, sh, true);
                        for (int stage = 0; stage < (settings.z > 0 ? 3 : 1); stage++)
                        {
                            int kernel = tensorKernels[stage];
                            kuwaharaAnisotropic.SetTexture(kernel, "_Source", stage == 0 ? source : stage == 1 ? tensorA : tensorB);
                            kuwaharaAnisotropic.SetTexture(kernel, "_Result", stage == 1 ? tensorB : tensorA);
                            kuwaharaAnisotropic.Dispatch(kernel, (sw + 7) / 8, (sh + 7) / 8, 1);
                        }
                    }
                    filter.SetTexture("_KuwSource", source);
                    // Bind valid resources even when the direct branch does not read the tables.
                    if (anisotropic)
                    {
                        filter.SetTexture("_KuwTensorImage", tensorA != null ? tensorA : source);
                        filter.SetInt("_KuwHasTensor", tensorA != null ? 1 : 0);
                        filter.SetVector("_KuwAnisotropic", settings);
                    }
                    else
                    {
                        filter.SetTexture("_KuwSum", tables ? sumB : source); filter.SetTexture("_KuwSquare", tables ? squareB : source);
                        filter.SetInt("_KuwTables", tables ? 1 : 0);
                    }
                    filter.SetVector("_KuwSize", new Vector4(w, h, radius, ShaderGraphNodes.Components(image.pass.type)));
                    if (w == width && h == height) Graphics.Blit(input, image.texture, filter, 0);
                    else
                    {
                        output = ImageFilterTemporary(w, h);
                        Graphics.Blit(input, output, filter, 0);
                        Graphics.CopyTexture(output, 0, 0, 0, 0, w, h, image.texture, 0, 0, x, y);
                    }
                    BufferDrawCount++;
                }
                finally
                {
                    filter.SetTexture("_KuwSource", null);
                    if (anisotropic) filter.SetTexture("_KuwTensorImage", null);
                    else { filter.SetTexture("_KuwSum", null); filter.SetTexture("_KuwSquare", null); }
                    if (tensorA != null) ProfiledRenderTexture.ReleaseTemporary(tensorA);
                    if (tensorB != null) ProfiledRenderTexture.ReleaseTemporary(tensorB);
                    if (source != null) ProfiledRenderTexture.ReleaseTemporary(source);
                    if (sumA != null) ProfiledRenderTexture.ReleaseTemporary(sumA);
                    if (squareA != null) ProfiledRenderTexture.ReleaseTemporary(squareA);
                    if (sumB != null) ProfiledRenderTexture.ReleaseTemporary(sumB);
                    if (squareB != null) ProfiledRenderTexture.ReleaseTemporary(squareB);
                    if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
                }
            }
        }
    }
}
