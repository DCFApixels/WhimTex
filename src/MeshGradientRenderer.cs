using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class MeshGradientRenderer : IDisposable
    {
        private sealed class Patch
        {
            internal Mesh mesh;
            internal readonly Vector4[] positions = new Vector4[16], colors = new Vector4[16];
            internal int nx, ny;
        }
        private Patch[] patches;
        private int columns, rows;
        private readonly Vector4[] positionBuffer = new Vector4[16];
        private readonly Vector4[] colorBuffer = new Vector4[16];

        internal void Render(MeshGradient data, RenderTexture target)
        {
            data.Validate();
            Prepare(data, target.width, target.height);
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture surface = null;
            try
            {
                var descriptor = target.descriptor; descriptor.msaaSamples = 4; descriptor.bindMS = false;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.depthBufferBits = 32;
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                surface = ProfiledRenderTexture.GetTemporary(descriptor);
                RenderTexture.active = surface;
                GL.sRGBWrite = false; GL.Clear(true, true, Color.clear);
                var material = WhimTexMaterials.MeshGradient;
                if (material == null || !material.shader.isSupported) throw new InvalidOperationException("Mesh gradient shader is unavailable.");
                material.SetInt("_MeshColorMode", (int)data.colorMode);
                material.SetVector("_MeshTargetSize", new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));
                for (int index = 0; index < patches.Length; index++)
                {
                    var patch = patches[index];
                    material.SetVector("_MeshPriority", data.PatchPriority(index % columns, index / columns));
                    material.SetVector("_MeshUvStep", new Vector4(1f / patch.nx, 1f / patch.ny, 0, 0));
                    material.SetVectorArray("_MeshPositions", patch.positions);
                    material.SetVectorArray("_MeshColors", patch.colors);
                    if (!material.SetPass(0)) throw new InvalidOperationException("Mesh gradient shader pass is unavailable.");
                    Graphics.DrawMeshNow(patch.mesh, Matrix4x4.identity);
                }
                Graphics.Blit(surface, target);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (surface != null) ProfiledRenderTexture.ReleaseTemporary(surface);
            }
        }

        private void Prepare(MeshGradient data, int width, int height)
        {
            if (patches == null || columns != data.columns || rows != data.rows)
            {
                Dispose(); columns = data.columns; rows = data.rows; patches = new Patch[columns * rows];
                for (int i = 0; i < patches.Length; i++) patches[i] = new Patch();
            }
            var horizontal = new int[columns]; var vertical = new int[rows];
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
                    changed |= patch.positions[j] != positionBuffer[j];
                }
                Array.Copy(positionBuffer, patch.positions, 16); Array.Copy(colorBuffer, patch.colors, 16);
                int nx = horizontal[c], ny = vertical[r];
                if (!changed && patch.nx == nx && patch.ny == ny) continue;
                patch.nx = nx; patch.ny = ny;
                var vertices = new Vector3[(nx + 1) * (ny + 1)]; var uv = new Vector2[vertices.Length];
                var triangles = new int[nx * ny * 6];
                for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
                {
                    int i = y * (nx + 1) + x; uv[i] = new Vector2(x / (float)nx, y / (float)ny);
                    vertices[i] = data.EvaluatePosition(c, r, uv[i].x, uv[i].y);
                }
                int at = 0;
                for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                {
                    int i = y * (nx + 1) + x;
                    triangles[at++] = i; triangles[at++] = i + 1; triangles[at++] = i + nx + 1;
                    triangles[at++] = i + 1; triangles[at++] = i + nx + 2; triangles[at++] = i + nx + 1;
                }
                patch.mesh ??= new Mesh { name = "Gradient Mesh Patch", hideFlags = HideFlags.HideAndDontSave };
                patch.mesh.Clear(); patch.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                patch.mesh.vertices = vertices; patch.mesh.uv = uv; patch.mesh.triangles = triangles;
            }
        }
        public void Dispose()
        {
            if (patches != null) foreach (var patch in patches) if (patch.mesh != null) UnityEngine.Object.DestroyImmediate(patch.mesh);
            patches = null;
        }
    }
}
