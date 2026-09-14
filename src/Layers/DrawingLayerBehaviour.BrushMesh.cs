using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        private static partial class PaintBrushRenderer
        {
            private static Mesh brushMesh;
            private static CommandBuffer brushCommands;
            private static List<Vector3> meshPositions;
            private static List<Vector2> meshUv, meshClipMin, meshClipMax;
            private static List<Vector3> meshClips, meshTiles, meshColors;
            private static List<Vector4> meshStamps;
            private static List<int> meshIndices;
            private static Vector3 meshColor;
            private static Vector4 meshStamp;
            private static bool writingBrushMesh;

            static PaintBrushRenderer()
            {
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseBrushMesh;
                EditorApplication.quitting += ReleaseBrushMesh;
            }

            private static void BeginBrushMesh()
            {
                if (brushMesh == null)
                {
                    brushMesh = new Mesh { name = "Brush Stamp Batch", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    brushMesh.MarkDynamic();
                    brushCommands = new CommandBuffer { name = "Brush Stamp Batch" };
                    meshPositions = new List<Vector3>(1024);
                    meshUv = new List<Vector2>(1024);
                    meshClipMin = new List<Vector2>(1024);
                    meshClipMax = new List<Vector2>(1024);
                    meshClips = new List<Vector3>(1024);
                    meshTiles = new List<Vector3>(1024);
                    meshColors = new List<Vector3>(1024);
                    meshStamps = new List<Vector4>(1024);
                    meshIndices = new List<int>(1024);
                }
                meshPositions.Clear(); meshUv.Clear(); meshClipMin.Clear(); meshClipMax.Clear();
                meshClips.Clear(); meshTiles.Clear(); meshColors.Clear(); meshStamps.Clear(); meshIndices.Clear();
                writingBrushMesh = true;
            }

            private static void AddBrushMeshVertex(float x, float y, float u, float v, PaintStamp stamp, int tileMode)
            {
                meshIndices.Add(meshPositions.Count);
                meshPositions.Add(new Vector3(x, y, 0f));
                meshUv.Add(new Vector2(u, v));
                meshClipMin.Add(new Vector2(stamp.clipRect.x, stamp.clipRect.y));
                meshClipMax.Add(new Vector2(stamp.clipRect.z, stamp.clipRect.w));
                meshClips.Add(new Vector3(stamp.clipMode, stamp.clipAngleCenter, stamp.clipAngleHalfWidth));
                meshTiles.Add(new Vector3(stamp.center.x, stamp.center.y, tileMode));
                meshColors.Add(meshColor);
                meshStamps.Add(meshStamp);
            }

            private static void EndBrushMesh(RenderTexture target, Material material, RenderTexture backdrop = null)
            {
                writingBrushMesh = false;
                if (meshPositions.Count == 0) return;
                if (backdrop != null) CopyStampBackdrop(target, backdrop);
                brushMesh.Clear();
                brushMesh.SetVertices(meshPositions);
                brushMesh.SetUVs(0, meshUv);
                brushMesh.SetUVs(1, meshClipMin);
                brushMesh.SetUVs(2, meshClipMax);
                brushMesh.SetUVs(3, meshClips);
                brushMesh.SetUVs(4, meshTiles);
                brushMesh.SetUVs(5, meshColors);
                brushMesh.SetUVs(6, meshStamps);
                brushMesh.SetIndices(meshIndices, MeshTopology.Quads, 0, false);
                brushMesh.bounds = new Bounds(new Vector3(.5f, .5f, 0f), new Vector3(2f, 2f, 2f));
                brushCommands.Clear();
                brushCommands.SetRenderTarget(target);
                brushCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
                brushCommands.DrawMesh(brushMesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(brushCommands);
                brushCommands.Clear();
            }

            private static void CopyStampBackdrop(RenderTexture target, RenderTexture backdrop)
            {
                float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
                foreach (Vector3 p in meshPositions)
                {
                    minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                    maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
                }
                int x = Mathf.Clamp(Mathf.FloorToInt(minX * target.width) - 1, 0, target.width);
                int y = Mathf.Clamp(Mathf.FloorToInt(minY * target.height) - 1, 0, target.height);
                int right = Mathf.Clamp(Mathf.CeilToInt(maxX * target.width) + 1, 0, target.width);
                int top = Mathf.Clamp(Mathf.CeilToInt(maxY * target.height) + 1, 0, target.height);
                if (right <= x || top <= y) return;
                RenderTexture.active = null;
                if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                    Graphics.CopyTexture(target, 0, 0, x, y, right - x, top - y, backdrop, 0, 0, x, y);
                else Graphics.Blit(target, backdrop);
                RenderTexture.active = target;
            }

            private static void ReleaseBrushMesh()
            {
                ReleaseBrushSdfGradient();
                writingBrushMesh = false;
                if (brushMesh != null) Object.DestroyImmediate(brushMesh);
                brushMesh = null;
                brushCommands?.Release();
                brushCommands = null;
                meshPositions = meshClips = meshTiles = meshColors = null;
                meshStamps = null;
                meshUv = meshClipMin = meshClipMax = null;
                meshIndices = null;
            }
        }
    }
}
