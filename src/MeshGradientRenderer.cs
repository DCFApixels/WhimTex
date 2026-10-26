using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed class MeshGradientRenderer : IDisposable
    {
        private sealed class Patch
        {
            internal Mesh mesh;
            internal readonly Vector4[] positions = new Vector4[16], colors = new Vector4[16];
            internal readonly float[] hueChroma = new float[16];
            internal int nx, ny;
        }
        private Patch[] patches;
        private int columns, rows;
        private readonly RenderBuffer[] colorBuffers = new RenderBuffer[2];
        private readonly Vector4[] positionBuffer = new Vector4[16];
        private readonly Vector4[] colorBuffer = new Vector4[16];
        private int[] horizontal, vertical;
        private NativeArray<Vector3> vertices;
        private NativeArray<Vector2> controls;
        private NativeArray<Vector4> horizontalBasis, verticalBasis;
        private int basisNx, basisNy;

        [BurstCompile(FloatMode = FloatMode.Strict)]
        private struct VerticesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector2> controls;
            [ReadOnly] public NativeArray<Vector4> horizontalBasis, verticalBasis;
            [WriteOnly] public NativeArray<Vector3> vertices;
            public int stride;

            public void Execute(int index)
            {
                Vector4 bu = horizontalBasis[index % stride], bv = verticalBasis[index / stride];
                Vector2 point = default;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                    point += controls[y * 4 + x] * (bu[x] * bv[y]);
                vertices[index] = new Vector3(point.x, point.y, 0);
            }
        }

        internal void Render(MeshGradient data, RenderTexture target)
        {
            data.Validate();
            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.Blend) ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.Blend))
                throw new InvalidOperationException("Mesh gradient rendering requires floating-point blending and two color targets.");
            Prepare(data, target.width, target.height);
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture surface = null, weights = null, resolved = null, region = null;
            Material material = null;
            try
            {
                var descriptor = target.descriptor; descriptor.msaaSamples = 4;
                descriptor.colorFormat = RenderTextureFormat.ARGBFloat;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                var weightDescriptor = descriptor; weightDescriptor.colorFormat = RenderTextureFormat.RGFloat;
                int samples = Mathf.Min(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor),
                    SystemInfo.GetRenderTextureSupportedMSAASampleCount(weightDescriptor));
                descriptor.msaaSamples = weightDescriptor.msaaSamples = samples;
                descriptor.bindMS = weightDescriptor.bindMS = samples > 1;
                surface = ProfiledRenderTexture.GetTemporary(descriptor);
                weights = ProfiledRenderTexture.GetTemporary(weightDescriptor);
                GL.sRGBWrite = false;
                RenderTexture.active = surface; GL.Clear(false, true, Color.clear);
                RenderTexture.active = weights; GL.Clear(false, true, Color.clear);
                colorBuffers[0] = surface.colorBuffer; colorBuffers[1] = weights.colorBuffer;
                Graphics.SetRenderTarget(colorBuffers, surface.depthBuffer);
                material = WhimTexMaterials.MeshGradient;
                if (material == null || !material.shader.isSupported) throw new InvalidOperationException("Mesh gradient shader is unavailable.");
                CompilePass(material, 0);
                material.SetInt("_MeshColorMode", (int)data.colorMode);
                material.SetVector("_MeshTargetSize", new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));
                for (int index = 0; index < patches.Length; index++)
                {
                    var patch = patches[index];
                    int column = index % columns, row = index / columns;
                    material.SetVector("_MeshOuterEdges", new Vector4(column == 0 ? 1 : 0, column == columns-1 ? 1 : 0,
                        row == 0 ? 1 : 0, row == rows-1 ? 1 : 0));
                    material.SetVector("_MeshPriority", data.PatchPriority(index % columns, index / columns));
                    material.SetVector("_MeshUvStep", new Vector4(1f / patch.nx, 1f / patch.ny, 0, 0));
                    material.SetVectorArray("_MeshPositions", patch.positions);
                    material.SetVectorArray("_MeshColors", patch.colors);
                    if (data.colorMode == WhimTexGradientColorMode.Hue) material.SetFloatArray("_MeshHueChroma", patch.hueChroma);
                    if (!material.SetPass(0)) throw new InvalidOperationException("Mesh gradient shader pass is unavailable.");
                    Graphics.DrawMeshNow(patch.mesh, Matrix4x4.identity);
                }
                material.SetTexture("_MeshAccumulatedColor", surface);
                material.SetTexture("_MeshAccumulatedWeight", weights);
                material.SetInt("_MeshSampleCount", samples);
                if (samples > 1) material.EnableKeyword("MESH_MULTISAMPLE");
                else material.DisableKeyword("MESH_MULTISAMPLE");
                CompilePass(material, 1);
                if (IsSingleSurface(data)) Graphics.Blit(null, target, material, 1);
                else
                {
                    for (int pass = 2; pass < 10; pass++) CompilePass(material, pass);
                    resolved = Temporary(target.width, target.height, RenderTextureFormat.ARGBFloat);
                    region = Temporary(target.width, target.height, RenderTextureFormat.RGFloat);
                    Graphics.Blit(null, resolved, material, 1);
                    Graphics.Blit(null, region, material, 2);
                    material.SetTexture("_MeshAccumulatedColor", null);
                    material.SetTexture("_MeshAccumulatedWeight", null);
                    ProfiledRenderTexture.ReleaseTemporary(surface); surface = null;
                    ProfiledRenderTexture.ReleaseTemporary(weights); weights = null;
                    Reconstruct(resolved, region, target, material);
                }
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (material != null)
                {
                    material.SetTexture("_MeshAccumulatedColor", null);
                    material.SetTexture("_MeshAccumulatedWeight", null);
                }
                if (surface != null) ProfiledRenderTexture.ReleaseTemporary(surface);
                if (weights != null) ProfiledRenderTexture.ReleaseTemporary(weights);
                if (resolved != null) ProfiledRenderTexture.ReleaseTemporary(resolved);
                if (region != null) ProfiledRenderTexture.ReleaseTemporary(region);
            }
        }

        private static void CompilePass(Material material, int pass)
        {
            // The first render may be cached: never cache an async-compilation placeholder.
            if (!ShaderUtil.IsPassCompiled(material, pass)) ShaderUtil.CompilePass(material, pass, true);
        }

        private static RenderTexture Temporary(int width, int height, RenderTextureFormat format)
        {
            var texture = ProfiledRenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // A positive-definite symmetric Jacobian makes the whole parameter rectangle
        // injective. Bernstein derivative control bounds certify this conservatively;
        // uncertain grids still use the exact raster coverage mask on the GPU.
        private static bool IsSingleSurface(MeshGradient data)
        {
            Vector2 origin = data.positions[0], u = data.positions[data.Width-1]-origin,
                v = data.positions[(data.Height-1)*data.Width]-origin;
            float determinant = u.x*v.y-u.y*v.x;
            if (!float.IsFinite(determinant) || Mathf.Abs(determinant) < 1e-12f) return false;
            Vector2 Frame(Vector2 d) => new Vector2(d.x*v.y-d.y*v.x, u.x*d.y-u.y*d.x)/determinant;
            for (int row = 0; row < data.rows; row++) for (int column = 0; column < data.columns; column++)
            {
                float minU = float.PositiveInfinity, minV = float.PositiveInfinity, crossU = 0, crossV = 0;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                {
                    int i = (row*3+y)*data.Width+column*3+x;
                    if (x < 3)
                    {
                        Vector2 d = Frame(3*data.columns*(data.positions[i+1]-data.positions[i]));
                        minU = Mathf.Min(minU, d.x); crossU = Mathf.Max(crossU, Mathf.Abs(d.y));
                    }
                    if (y < 3)
                    {
                        Vector2 d = Frame(3*data.rows*(data.positions[i+data.Width]-data.positions[i]));
                        minV = Mathf.Min(minV, d.y); crossV = Mathf.Max(crossV, Mathf.Abs(d.x));
                    }
                }
                float cross = crossU+crossV;
                if (!(minU > 0 && minV > 0 && 4*minU*minV > cross*cross+1e-6f)) return false;
            }
            return true;
        }

        private static void Reconstruct(RenderTexture source, RenderTexture mask, RenderTexture target, Material material)
        {
            using var scope = WhimTexProfiler.Measure("render.mesh.overlap_reconstruction");
            var colors = new List<RenderTexture> { source };
            var regions = new List<RenderTexture> { mask };
            RenderTexture coarse = null, work = null, next = null, history = null, distances = null, extended = null;
            try
            {
                // A compact transition band lets the solution match neighboring color slopes.
                material.SetVector("_MeshLevelSize", new Vector4(source.width, source.height, 0, 0));
                material.SetFloat("_MeshBandRadius", Mathf.Max(1, Mathf.Min(source.width, source.height)/96f));
                material.SetTexture("_MeshRegion", mask);
                distances = Temporary(source.width, source.height, RenderTextureFormat.RFloat);
                extended = Temporary(source.width, source.height, RenderTextureFormat.RGFloat);
                Graphics.Blit(null, distances, material, 8);
                Graphics.Blit(distances, extended, material, 9);
                ProfiledRenderTexture.ReleaseTemporary(distances); distances = null;
                mask = extended; regions[0] = extended;
                // Coarse-to-fine thin-plate relaxation carries boundary colors and slopes
                // through the whole overlap, instead of limiting the transition to a rim.
                while (colors[colors.Count-1].width > 8 || colors[colors.Count-1].height > 8)
                {
                    var previous = colors[colors.Count-1];
                    material.SetTexture("_MeshRegion", regions[regions.Count-1]);
                    material.SetVector("_MeshLevelSize", new Vector4(previous.width, previous.height, 0, 0));
                    material.SetInt("_MeshNormalizeCoverage", colors.Count == 1 ? 1 : 0);
                    int width = (previous.width+1)/2, height = (previous.height+1)/2;
                    var color = Temporary(width, height, RenderTextureFormat.ARGBFloat); colors.Add(color);
                    var region = Temporary(width, height, RenderTextureFormat.RGFloat); regions.Add(region);
                    Graphics.Blit(previous, color, material, 3);
                    Graphics.Blit(null, region, material, 4);
                }
                for (int level = colors.Count-1; level >= 0; level--)
                {
                    var fixedColor = colors[level];
                    material.SetTexture("_MeshFixedColor", fixedColor);
                    material.SetTexture("_MeshRegion", regions[level]);
                    material.SetVector("_MeshLevelSize", new Vector4(fixedColor.width, fixedColor.height, 0, 0));
                    material.SetInt("_MeshNormalizeCoverage", level == 0 ? 1 : 0);
                    material.SetTexture("_MeshCoarseColor", coarse);
                    material.SetInt("_MeshHasCoarse", coarse != null ? 1 : 0);
                    if (coarse != null) material.SetVector("_MeshCoarseSize", new Vector4(coarse.width, coarse.height, 0, 0));
                    work = Temporary(fixedColor.width, fixedColor.height, RenderTextureFormat.ARGBFloat);
                    next = Temporary(fixedColor.width, fixedColor.height, RenderTextureFormat.ARGBFloat);
                    Graphics.Blit(null, work, material, 5);
                    material.SetTexture("_MeshCoarseColor", null);
                    if (coarse != null) { ProfiledRenderTexture.ReleaseTemporary(coarse); coarse = null; }
                    history = Temporary(fixedColor.width, fixedColor.height, RenderTextureFormat.ARGBFloat);
                    for (int iteration = 0; iteration < 64; iteration++)
                    {
                        material.SetTexture("_MeshPreviousColor", iteration == 0 ? work : history);
                        material.SetFloat("_MeshMomentum", Mathf.Max(0, (iteration-1f)/(iteration+2f)));
                        Graphics.Blit(work, next, material, 6);
                        (history, work, next) = (work, next, history);
                    }
                    material.SetTexture("_MeshPreviousColor", null);
                    ProfiledRenderTexture.ReleaseTemporary(history); history = null;
                    ProfiledRenderTexture.ReleaseTemporary(next); next = null;
                    coarse = work; work = null;
                }
                material.SetTexture("_MeshFixedColor", source);
                material.SetTexture("_MeshRegion", mask);
                Graphics.Blit(coarse, target, material, 7);
            }
            finally
            {
                material.SetTexture("_MeshFixedColor", null);
                material.SetTexture("_MeshCoarseColor", null);
                material.SetTexture("_MeshRegion", null);
                material.SetTexture("_MainTex", null);
                material.SetTexture("_MeshPreviousColor", null);
                if (distances != null) ProfiledRenderTexture.ReleaseTemporary(distances);
                if (extended != null) ProfiledRenderTexture.ReleaseTemporary(extended);
                if (history != null) ProfiledRenderTexture.ReleaseTemporary(history);
                if (coarse != null) ProfiledRenderTexture.ReleaseTemporary(coarse);
                if (work != null) ProfiledRenderTexture.ReleaseTemporary(work);
                if (next != null) ProfiledRenderTexture.ReleaseTemporary(next);
                for (int i = 1; i < colors.Count; i++) ProfiledRenderTexture.ReleaseTemporary(colors[i]);
                for (int i = 1; i < regions.Count; i++) ProfiledRenderTexture.ReleaseTemporary(regions[i]);
            }
        }

        private void Prepare(MeshGradient data, int width, int height)
        {
            using var scope = WhimTexProfiler.Measure("render.mesh.prepare");
            if (patches == null || columns != data.columns || rows != data.rows)
            {
                Dispose(); columns = data.columns; rows = data.rows; patches = new Patch[columns * rows];
                for (int i = 0; i < patches.Length; i++) patches[i] = new Patch();
                horizontal = new int[columns]; vertical = new int[rows];
            }
            Array.Clear(horizontal, 0, horizontal.Length); Array.Clear(vertical, 0, vertical.Length);
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            {
                float bound = 0;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                {
                    int i = (r * 3 + y) * data.Width + c * 3 + x;
                    if (x < 2) bound = Mathf.Max(bound, Metric(data.positions[i] - 2 * data.positions[i + 1] + data.positions[i + 2]));
                    if (y < 2) bound = Mathf.Max(bound, Metric(data.positions[i] - 2 * data.positions[i + data.Width] + data.positions[i + 2 * data.Width]));
                    if (x < 3 && y < 3) bound = Mathf.Max(bound, Metric(data.positions[i] - data.positions[i + 1] - data.positions[i + data.Width] + data.positions[i + data.Width + 1]));
                }
                int steps = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(bound * 96))), 4, 256);
                horizontal[c] = Mathf.Max(horizontal[c], steps); vertical[r] = Mathf.Max(vertical[r], steps);
            }
            float Metric(Vector2 v) => Mathf.Max(Mathf.Abs(v.x) * width, Mathf.Abs(v.y) * height);
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            {
                var patch = patches[r * columns + c]; bool changed = patch.mesh == null;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                {
                    int i = (r * 3 + y) * data.Width + c * 3 + x, j = y * 4 + x;
                    positionBuffer[j] = data.positions[i]; colorBuffer[j] = data.colors[i];
                    patch.hueChroma[j] = data.ChromaCoefficient(i);
                    changed |= patch.positions[j] != positionBuffer[j];
                }
                Array.Copy(positionBuffer, patch.positions, 16); Array.Copy(colorBuffer, patch.colors, 16);
                int nx = horizontal[c], ny = vertical[r];
                if (!changed && patch.nx == nx && patch.ny == ny) continue;
                bool topologyChanged = patch.mesh == null || patch.nx != nx || patch.ny != ny;
                int vertexCount = (nx + 1) * (ny + 1);
                PrepareVertexBuffers(nx, ny, vertexCount);
                for (int i = 0; i < 16; i++) controls[i] = patch.positions[i];
                var job = new VerticesJob { controls = controls, horizontalBasis = horizontalBasis,
                    verticalBasis = verticalBasis, vertices = vertices, stride = nx + 1 };
                if (vertexCount < 1024) job.Run(vertexCount);
                else job.Schedule(vertexCount, 64).Complete();
                if (patch.mesh == null)
                {
                    patch.mesh = new Mesh { name = "Gradient Mesh Patch", hideFlags = HideFlags.HideAndDontSave };
                    patch.mesh.MarkDynamic();
                }
                if (topologyChanged)
                {
                    patch.mesh.Clear(); patch.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }
                patch.mesh.SetVertices(vertices, 0, vertexCount);
                if (topologyChanged)
                {
                    var uv = new Vector2[vertexCount];
                    var triangles = new int[nx * ny * 6];
                    for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
                        uv[y * (nx + 1) + x] = new Vector2(x / (float)nx, y / (float)ny);
                    int at = 0;
                    for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                    {
                        int i = y * (nx + 1) + x;
                        triangles[at++] = i; triangles[at++] = i + 1; triangles[at++] = i + nx + 1;
                        triangles[at++] = i + 1; triangles[at++] = i + nx + 2; triangles[at++] = i + nx + 1;
                    }
                    patch.mesh.uv = uv; patch.mesh.triangles = triangles;
                }
                patch.nx = nx; patch.ny = ny;
            }
            WhimTexProfiler.NativeCacheBytes(this, vertices.Length * 12L + controls.Length * 8L +
                (horizontalBasis.Length + verticalBasis.Length) * 16L);
        }

        private void PrepareVertexBuffers(int nx, int ny, int count)
        {
            if (!controls.IsCreated)
            {
                controls = new NativeArray<Vector2>(16, Allocator.Persistent);
                horizontalBasis = new NativeArray<Vector4>(257, Allocator.Persistent);
                verticalBasis = new NativeArray<Vector4>(257, Allocator.Persistent);
            }
            if (!vertices.IsCreated || vertices.Length < count)
            {
                if (vertices.IsCreated) vertices.Dispose();
                vertices = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            if (basisNx != nx)
            {
                for (int i = 0; i <= nx; i++) horizontalBasis[i] = MeshGradient.Basis(i / (float)nx);
                basisNx = nx;
            }
            if (basisNy != ny)
            {
                for (int i = 0; i <= ny; i++) verticalBasis[i] = MeshGradient.Basis(i / (float)ny);
                basisNy = ny;
            }
        }

        public void Dispose()
        {
            if (patches != null) foreach (var patch in patches) if (patch.mesh != null) UnityEngine.Object.DestroyImmediate(patch.mesh);
            patches = null;
            if (vertices.IsCreated) vertices.Dispose();
            if (controls.IsCreated) controls.Dispose();
            if (horizontalBasis.IsCreated) horizontalBasis.Dispose();
            if (verticalBasis.IsCreated) verticalBasis.Dispose();
            basisNx = basisNy = 0;
            WhimTexProfiler.NativeCacheBytes(this, 0);
        }
    }
}
