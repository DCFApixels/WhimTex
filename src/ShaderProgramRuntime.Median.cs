using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        static ComputeShader medianColumns;

        void RenderMedian(Texture input, BufferImage image, in LayerRenderContext context)
        {
            float requested = Material.GetVector(image.pass.imageRadius).x;
            int radius = float.IsFinite(requested) ? (int)Math.Min(Math.Max(Math.Floor(requested), 0), int.MaxValue / 4) : 0;
            int components = ShaderGraphNodes.Components(image.pass.type);
            int tile = 128, width = image.texture.width, height = image.texture.height;
            if ((long)radius * 2 + tile > SystemInfo.maxTextureSize)
                throw new ShaderGraphException(image.pass.nodeId, "Median sampling halo exceeds this device's maximum texture size.");
            bool compute = SystemInfo.supportsComputeShaders &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore);
            if (compute && medianColumns == null)
                medianColumns = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/MedianColumns.compute");
            compute &= medianColumns != null;
            int columns = -1, select = -1, resolve = -1;
            if (compute)
            {
                columns = medianColumns.FindKernel("Columns"); select = medianColumns.FindKernel("Select"); resolve = medianColumns.FindKernel("Resolve");
                compute = medianColumns.IsSupported(columns) && medianColumns.IsSupported(select) && medianColumns.IsSupported(resolve);
            }
            // Bound scratch storage independently of canvas size. The fallback remains exact.
            long Bytes(int side) => (long)(side + 2 * radius) * side * (2 * radius + 1) * components * 4;
            while (tile > 1 && Bytes(tile) > 32 * 1024 * 1024) tile /= 2;
            compute &= Bytes(tile) <= 32 * 1024 * 1024;
            var producer = image.imageSource.material; var filter = image.material;
            producer.CopyPropertiesFromMaterial(Material);
            if (image.imageSource.pass.program.usesLighting) context.activeDocument.BindLighting(producer);
            filter.SetVector(ViewportId, new Vector4(0, 0, 1, 1));
            ComputeBuffer sorted = null, selected = null;
            try
            {
                if (compute) { sorted = new ComputeBuffer((int)(Bytes(tile) / 4), 4); selected = new ComputeBuffer(tile * tile * 4, 4); }
                for (int y = 0; y < height; y += tile)
                for (int x = 0; x < width; x += tile)
                {
                    int w = Math.Min(tile, width - x), h = Math.Min(tile, height - y), sw = w + 2 * radius, sh = h + 2 * radius;
                    RenderTexture source = null, output = null;
                    try
                    {
                        source = ImageFilterTemporary(sw, sh);
                        producer.SetVector(ViewportId, new Vector4((float)(x - radius) / width, (float)(y - radius) / height, (float)sw / width, (float)sh / height));
                        producer.SetVector(BakeSizeId, new Vector4(width, height, 1f / width, 1f / height));
                        Graphics.Blit(input, source, producer, 0);
                        output = ImageFilterTemporary(w, h, compute);
                        if (compute)
                        {
                            medianColumns.SetInts("_Size", w, h); medianColumns.SetInt("_Radius", radius); medianColumns.SetInt("_Components", components);
                            medianColumns.SetTexture(columns, "_Source", source); medianColumns.SetBuffer(columns, "_Columns", sorted);
                            medianColumns.Dispatch(columns, (sw + 63) / 64, (h + 7) / 8, components);
                            medianColumns.SetBuffer(select, "_Columns", sorted); medianColumns.SetBuffer(select, "_Selected", selected);
                            medianColumns.Dispatch(select, (w + 31) / 32, (h + 1) / 2, components);
                            medianColumns.SetBuffer(resolve, "_Selected", selected); medianColumns.SetTexture(resolve, "_Result", output);
                            medianColumns.Dispatch(resolve, (w + 7) / 8, (h + 7) / 8, 1);
                        }
                        else
                        {
                            filter.SetTexture("_MedianSource", source); filter.SetVector("_MedianSize", new Vector4(w, h, radius, 0));
                            Graphics.Blit(input, output, filter, 0);
                        }
                        Graphics.CopyTexture(output, 0, 0, 0, 0, w, h, image.texture, 0, 0, x, y);
                        BufferDrawCount++;
                    }
                    finally
                    {
                        filter.SetTexture("_MedianSource", null);
                        if (source != null) ProfiledRenderTexture.ReleaseTemporary(source);
                        if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
                    }
                }
            }
            finally { sorted?.Dispose(); selected?.Dispose(); }
        }
    }
}
