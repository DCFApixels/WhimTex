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
            private static List<Vector2> meshUv;
            private static List<Vector3> meshClipMin, meshClipMax, meshClips, meshColors;
            private static List<Vector4> meshTiles;
            private static List<Vector4> meshStamps;
            private static List<Vector4> meshContexts;
            private static bool meshSequence, meshMotion, meshHadSequence;
            private static List<int> meshIndices;
            private static Vector3 meshColor;
            private static Vector4 meshStamp;
            private static readonly PaintTileSet meshDrawTiles = new PaintTileSet();
            private static MaterialPropertyBlock tileProperties;
            private static RenderTexture tileBackdrop;

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
                    meshClipMin = new List<Vector3>(1024);
                    meshClipMax = new List<Vector3>(1024);
                    meshClips = new List<Vector3>(1024);
                    meshTiles = new List<Vector4>(1024);
                    meshColors = new List<Vector3>(1024);
                    meshStamps = new List<Vector4>(1024);
                    meshContexts = new List<Vector4>(1024);
                    meshIndices = new List<int>(1024);
                }
                meshPositions.Clear(); meshUv.Clear(); meshClipMin.Clear(); meshClipMax.Clear();
                meshClips.Clear(); meshTiles.Clear(); meshColors.Clear(); meshStamps.Clear(); meshIndices.Clear();
                meshContexts.Clear();
                meshDrawTiles.Reset(DrawTiles.Width, DrawTiles.Height);
            }

            private static void AddBrushMeshVertex(float x, float y, float u, float v, PaintStamp stamp, int tileMode)
            {
                meshIndices.Add(meshPositions.Count);
                meshPositions.Add(new Vector3(x, y, 0f));
                meshUv.Add(new Vector2(u, v));
                // Reuse spare clip components; dynamic stamps need no extra UV channel/pass.
                meshClipMin.Add(new Vector3(stamp.clipRect.x, stamp.clipRect.y, meshMotion ? stamp.dynamicContext.deltaPixels.x : 0f));
                meshClipMax.Add(new Vector3(stamp.clipRect.z, stamp.clipRect.w, meshMotion ? stamp.dynamicContext.deltaPixels.y : 0f));
                meshClips.Add(new Vector3(stamp.clipMode, stamp.clipAngleCenter, stamp.clipAngleHalfWidth));
                meshTiles.Add(new Vector4(stamp.center.x, stamp.center.y, tileMode, stamp.opacityPressure));
                meshColors.Add(meshColor);
                meshStamps.Add(meshStamp);
                if (meshSequence) meshContexts.Add(stamp.dynamicContext.StampVertex);
            }

            private static void EndBrushMesh(RenderTexture target, Material material, PaintStrokeBuffer tiledStroke = null, bool sequential = false)
            {
                if (meshPositions.Count == 0) return;
                using (WhimTexProfiler.Measure("brush.mesh_upload"))
                {
                    brushMesh.Clear(meshSequence == meshHadSequence);
                    meshHadSequence = meshSequence;
                    brushMesh.SetVertices(meshPositions);
                    brushMesh.SetUVs(0, meshUv);
                    brushMesh.SetUVs(1, meshClipMin);
                    brushMesh.SetUVs(2, meshClipMax);
                    brushMesh.SetUVs(3, meshClips);
                    brushMesh.SetUVs(4, meshTiles);
                    brushMesh.SetUVs(5, meshColors);
                    brushMesh.SetUVs(6, meshStamps);
                    if (meshSequence) brushMesh.SetUVs(7, meshContexts);
                    brushMesh.SetIndices(meshIndices, MeshTopology.Quads, 0, false);
                    brushMesh.bounds = new Bounds(new Vector3(.5f, .5f, 0f), new Vector3(2f, 2f, 2f));
                }
                using var submit = WhimTexProfiler.Measure("brush.mesh_submit");
                using var gpu = submit.Gpu();
                brushCommands.Clear();
                if (tiledStroke == null)
                {
                    brushCommands.SetRenderTarget(target);
                    brushCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
                    brushCommands.DrawMesh(brushMesh, Matrix4x4.identity, material, 0, 0);
                }
                else
                {
                    tileProperties ??= new MaterialPropertyBlock();
                    if (sequential && tileBackdrop == null)
                    {
                        tileBackdrop = new RenderTexture(PaintStrokeBuffer.TileSize, PaintStrokeBuffer.TileSize, 0,
                            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { name = "WhimTex stroke tile backdrop", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                        tileBackdrop.Create();
                    }
                    for (int i = 0; i < meshDrawTiles.Count; i++)
                    {
                        var tile = meshDrawTiles[i];
                        var block = tiledStroke.Ensure(tile);
                        tileProperties.Clear();
                        if (sequential)
                        {
                            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                            {
                                var slot = tiledStroke.Slot(tile);
                                brushCommands.CopyTexture(block, 0, 0, slot.x, slot.y, PaintStrokeBuffer.TileSize,
                                    PaintStrokeBuffer.TileSize, tileBackdrop, 0, 0, 0, 0);
                            }
                            else
                            {
                                var slot = tiledStroke.Slot(tile);
                                brushCommands.Blit(block, tileBackdrop, Vector2.one * (PaintStrokeBuffer.TileSize / (float)block.width),
                                    new Vector2(slot.x, slot.y) / block.width);
                            }
                            tileProperties.SetTexture("_Backdrop", tileBackdrop);
                            tileProperties.SetVector("_BackdropTransform", new Vector4(target.width / (float)PaintStrokeBuffer.TileSize,
                                target.height / (float)PaintStrokeBuffer.TileSize, -tile.x / (float)PaintStrokeBuffer.TileSize, -tile.y / (float)PaintStrokeBuffer.TileSize));
                        }
                        brushCommands.SetRenderTarget(block);
                        var viewport = tiledStroke.Slot(tile);
                        brushCommands.SetViewport(new Rect(viewport.x, viewport.y, viewport.width, viewport.height));
                        float x = tile.x / (float)target.width, y = tile.y / (float)target.height;
                        brushCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(x,
                            x + PaintStrokeBuffer.TileSize / (float)target.width, y,
                            y + PaintStrokeBuffer.TileSize / (float)target.height, -1f, 1f));
                        brushCommands.DrawMesh(brushMesh, Matrix4x4.identity, material, 0, 0, tileProperties);
                    }
                }
                Graphics.ExecuteCommandBuffer(brushCommands);
                brushCommands.Clear();
            }

            private static void CopyStampBackdrop(RenderTexture target, Material material, ref RenderTexture backdrop)
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
                int width = right - x, height = top - y;
                if (backdrop == null || backdrop.width < width || backdrop.height < height)
                {
                    // Small capacity buckets avoid a new pool entry for every pressure/jitter value.
                    int capacityX = Mathf.Min(target.width, ((width + 127) / 128) * 128);
                    int capacityY = Mathf.Min(target.height, ((height + 127) / 128) * 128);
                    if (backdrop != null)
                    {
                        capacityX = Mathf.Max(capacityX, backdrop.width); capacityY = Mathf.Max(capacityY, backdrop.height);
                        ProfiledRenderTexture.ReleaseTemporary(backdrop); backdrop = null;
                    }
                    backdrop = HdrUtility.Temporary(capacityX, capacityY);
                    backdrop.filterMode = FilterMode.Point;
                }
                RenderTexture.active = null;
                if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                    Graphics.CopyTexture(target, 0, 0, x, y, width, height, backdrop, 0, 0, 0, 0);
                else
                {
                    var copy = WhimTexMaterials.Blend;
                    try
                    {
                        DrawBrushRegion(target, backdrop, copy, 3,
                            new Rect(0, 0, width / (float)backdrop.width, height / (float)backdrop.height),
                            new Rect(x / (float)target.width, y / (float)target.height, width / (float)target.width, height / (float)target.height));
                    }
                    finally { copy.SetTexture("_MainTex", null); }
                }
                material.SetTexture("_Backdrop", backdrop);
                material.SetVector("_BackdropTransform", new Vector4(target.width / (float)backdrop.width,
                    target.height / (float)backdrop.height, -x / (float)backdrop.width, -y / (float)backdrop.height));
                RenderTexture.active = target;
            }

            private static void ReleaseBrushMesh()
            {
                ReleaseBrushSdfGradient();
                if (brushMesh != null) Object.DestroyImmediate(brushMesh);
                brushMesh = null;
                meshHadSequence = false;
                brushCommands?.Release();
                brushCommands = null;
                if (tileBackdrop != null) Object.DestroyImmediate(tileBackdrop);
                tileBackdrop = null;
                tileProperties = null;
                meshPositions = meshClipMin = meshClipMax = meshClips = meshColors = null;
                meshTiles = null;
                meshStamps = null;
                meshContexts = null;
                meshUv = null;
                meshIndices = null;
            }
        }
    }
}
