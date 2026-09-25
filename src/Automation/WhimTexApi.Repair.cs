using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void PaintRepair(TextureCompositor document, Layer target, JObject operation, bool execute)
        {
            bool healing = Text(operation, "op") == "healStroke";
            if (healing) Keys(operation, "op", "layer", "points", "size", "hardness", "source", "tiled", "search", "quality", "seed", "transparentOnly", "maskPath");
            else Keys(operation, "op", "layer", "points", "size", "hardness", "source", "tiled", "strength", "flow");
            Require(target.Behaviour is DrawingLayerBehaviour, "Repair requires Drawing. Convert explicitly first.");
            var drawing = (DrawingLayerBehaviour)target.Behaviour;
            int width = document.width, height = document.height;
            float size = Number(operation, "size", 32, 1, 512), hardness = Number(operation, "hardness", .8f, 0, 1);
            bool tiled = Bool(operation, "tiled");
            string source = Text(operation, "source", "CurrentLayer");
            Require(source == "CurrentLayer" || source == "CurrentAndBelow" || !healing && source == "AllLayers", "Invalid repair source.");
            int search = healing ? Int(operation, "search", 64, 8, 512) : 0;
            var quality = healing ? Enum(operation, "quality", HealingQuality.Balanced) : HealingQuality.Fast;
            int seed = healing ? Int(operation, "seed", 1, int.MinValue, int.MaxValue) : 0;
            bool transparentOnly = healing && Bool(operation, "transparentOnly");
            float strength = healing ? 1 : Number(operation, "strength", 1, 0, 1) * Number(operation, "flow", 1, 0, 1);
            var transform = document.GetPaintTransform(drawing);
            Require(transform.ToMatrix(width, height).TryInverse(out var inverse), "Repair needs an invertible transform.");
            Require(transform.tiling == TransformTilingMode.Clip || transform.tiling == TransformTilingMode.Unbounded,
                "Repair edits the source frame; use tiled:true for canvas-edge wrapping, not a repeating layer transform.");
            Texture2D suppliedMask = null;
            Vector2[] points = null;
            if (healing && operation["maskPath"] != null)
            {
                Require(operation["points"] == null, "Supply points or maskPath, not both.");
                string path = Text(operation, "maskPath");
                Require(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal), "maskPath must be a project texture path.");
                ValidateSegments(path);
                suppliedMask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(suppliedMask != null, "Mask texture not found.");
            }
            else
            {
                Require(operation["points"] is JArray array && array.Count > 0 && array.Count <= 4096, "points must contain 1..4096 canvas pixel pairs (top-left origin).");
                var pointArray = (JArray)operation["points"];
                points = new Vector2[pointArray.Count];
                double distance = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p = Vector(pointArray[i], "point");
                    Require(p.x >= -width && p.x <= 2 * width && p.y >= -height && p.y <= 2 * height, "Repair points exceed the canvas neighborhood.", "resource_limit");
                    points[i] = new Vector2(p.x, height - p.y);
                    if (i > 0) distance += Vector2.Distance(points[i], points[i - 1]);
                }
                Require(distance / Math.Max(.5f, size * .05f) + points.Length <= 32768, "Repair stroke exceeds the stamp limit.", "resource_limit");
            }
            Require(!healing || (long)width * height <= HealingBrushUtility.MaximumWorkingPixels, "Healing API currently supports canvases up to 1,048,576 pixels.", "resource_limit");
            Require(healing || (long)width * height * points.Length <= 67108864, "Blur stroke exceeds the pixel-pass budget; simplify the path.", "resource_limit");
            if (!execute) return;
            RenderTexture sample = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                drawing.PrepareStroke(width, height, UndoName);
                sample = source == "AllLayers" ? document.RenderAllLayers(width, height) :
                    source == "CurrentAndBelow" ? document.RenderLayerAndBelow(target, width, height) :
                    healing ? target.Render(new LayerRenderContext(document, null, width, height, 1, applyModifiers: false)) : drawing.CaptureBlurSource(width, height);
                Require(sample != null, "No repair source available.");
                if (healing) HealPixels(document, drawing, sample, points, suppliedMask, size, hardness, search, tiled, quality, seed, transparentOnly, inverse);
                else
                {
                    Vector2 dimensions = new Vector2(width, height);
                    Vector2 last = transform.Unmap(points[0] / dimensions, dimensions);
                    drawing.BlurSegment(last, last, width, height, size, hardness, strength, sample, tiled);
                    for (int i = 1; i < points.Length; i++)
                    {
                        Vector2 next = transform.Unmap(points[i] / dimensions, dimensions);
                        drawing.BlurSegment(last, next, width, height, size, hardness, strength, sample, tiled);
                        last = next;
                    }
                }
                drawing.SyncSurfaceToTexture();
            }
            finally
            {
                RenderTexture.active = previous;
                if (sample != null) RenderTexture.ReleaseTemporary(sample);
            }
        }

        private static void HealPixels(TextureCompositor document, DrawingLayerBehaviour drawing, RenderTexture sample,
            Vector2[] points, Texture2D suppliedMask, float size, float hardness, int search, bool tiled,
            HealingQuality quality, int seed, bool transparentOnly, ProjectiveMatrix inverse)
        {
            int width = document.width, height = document.height;
            using var stroke = new DrawingLayerBehaviour.HealingStrokeBuffer(width, height, size, hardness, tiled, inverse, null);
            if (points != null) foreach (var point in points) stroke.Add(point);
            RectInt bounds = points != null ? stroke.Bounds(search) : new RectInt(0, 0, width, height);
            Require(bounds.width > 0 && bounds.height > 0, "Stroke does not touch the canvas.");
            Texture2D patch = null, mask = null;
            try
            {
                Color[] raster = ReadRepairRegion(suppliedMask != null ? suppliedMask : stroke.Texture, bounds, width, height, tiled);
                var coverage = new byte[raster.Length];
                int covered = 0;
                for (int i = 0; i < coverage.Length; i++)
                {
                    // External masks use linear red, stroke buffers use alpha. White means repair.
                    coverage[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(suppliedMask != null ? raster[i].r : raster[i].a) * 255);
                    if (coverage[i] != 0) covered++;
                }
                Require(covered > 0, "Repair mask is empty.");
                if (tiled) bounds = HealingBrushUtility.RecenterTiledRegion(bounds, ref coverage, width, height);
                var pixels = ReadRepairRegion(sample, bounds, width, height, tiled);
                // A bounded synchronous operation never leaves a detached worker that could commit later.
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = HealingBrushUtility.Heal(pixels, coverage, bounds.width, bounds.height, transparentOnly, (int)quality, seed, cancellation.Token, null);
                patch = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                patch.SetPixels(result.pixels); patch.Apply(false, false);
                mask = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                var bytes = new Color32[result.target.Length];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = new Color32(result.target[i], 0, 0, 255);
                mask.SetPixels32(bytes); mask.Apply(false, false);
                drawing.ApplyHealingPatch(patch, mask, bounds, width, height, document.GetPaintTransform(drawing).ToMatrix(width, height), tiled);
            }
            finally
            {
                if (patch != null) Object.DestroyImmediate(patch);
                if (mask != null) Object.DestroyImmediate(mask);
            }
        }

        private static Color[] ReadRepairRegion(Texture source, RectInt bounds, int width, int height, bool tiled)
        {
            var previous = RenderTexture.active;
            var wrapU = source.wrapModeU; var wrapV = source.wrapModeV; var wrapW = source.wrapModeW;
            var filter = source.filterMode;
            var cropped = RenderTexture.GetTemporary(bounds.width, bounds.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Texture2D readback = null;
            try
            {
                source.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                source.filterMode = FilterMode.Point;
                Graphics.Blit(source, cropped, new Vector2(bounds.width / (float)width, bounds.height / (float)height), new Vector2(bounds.x / (float)width, bounds.y / (float)height));
                readback = HdrUtility.ReadLinear(cropped);
                return readback.GetPixels();
            }
            finally
            {
                source.wrapModeU = wrapU; source.wrapModeV = wrapV; source.wrapModeW = wrapW; source.filterMode = filter;
                RenderTexture.active = previous;
                if (readback != null) Object.DestroyImmediate(readback);
                RenderTexture.ReleaseTemporary(cropped);
            }
        }
    }
}
