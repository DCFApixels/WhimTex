using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void CaptureLiveInput(LiveJob job, TextureCompositorWindow window, JObject request)
        {
            string area = Text(request, "area", "canvas");
            Require(area == "canvas" || area == "selection", "area must be canvas or selection.");
            job.selectionMode = Text(request, "selectionMode", "strict");
            Require(job.selectionMode == "strict" || job.selectionMode == "guide", "selectionMode must be strict or guide.");
            Require(area == "selection" || request["selectionMode"] == null, "selectionMode is only used with area=selection.");
            CanvasSelection selection = window.AgentSelection;
            job.region = new RectInt(0, 0, job.width, job.height);
            if (area == "selection")
            {
                Require(selection.Active && selection.Bounds.width > 0 && selection.Bounds.height > 0,
                    "Select a nonempty area first.", "selection_required");
                job.mask = (byte[])selection.Coverage.Clone();
                int padding = Int(request, "padding", job.selectionMode == "guide" ? 128 : 32, 0, 4096);
                var b = selection.Bounds;
                int x = Math.Max(0, b.xMin - padding), y = Math.Max(0, b.yMin - padding);
                job.region = new RectInt(x, y, Math.Min(job.width, b.xMax + padding) - x,
                    Math.Min(job.height, b.yMax + padding) - y);
            }
            else Require(request["padding"] == null, "padding is only used with area=selection.");
            string source = Text(request, "source", "none");
            Require(source == "none" || source == "merged" || source == "layer", "source must be none, merged or layer.");
            Require(source == "layer" || request["sourceLayerId"] == null, "sourceLayerId is only used with source=layer.");
            Layer layer = source == "layer" ? job.document.FindLayer(Text(request, "sourceLayerId")) : null;
            Require(source != "layer" || layer != null && !(layer?.Behaviour is PendingLayerBehaviour), "Specify a valid sourceLayerId.");
            job.capture = new JObject { ["source"] = source, ["sourceLayerId"] = layer?.Id,
                ["region"] = LiveRect(job.region), ["canvas"] = new JArray(job.width, job.height),
                ["selection"] = job.mask != null, ["selectionMode"] = job.mask != null ? job.selectionMode : null,
                ["maskEnforced"] = job.mask != null && job.selectionMode == "strict",
                ["colorSpace"] = "sRGB, 8-bit PNG; HDR is clamped for image generation",
                ["coordinates"] = "region: bottom-left canvas pixels; PNG: normal top-left image orientation" };
            if (source == "none" && job.mask == null) return;
            RequireGraphics();
            string folder = "Temp/WhimTex/Agent/" + job.id + "/";
            Texture2D rendered = null, crop = null;
            try
            {
                if (source != "none")
                {
                    rendered = layer == null ? job.document.Compose() : job.document.RenderAreaSelectionAlphaSource(layer);
                    Require(rendered != null, "Could not render the source.", "render_failed");
                    using var pixels = HdrUtility.ReadPixels(rendered, Allocator.Temp);
                    crop = new Texture2D(job.region.width, job.region.height, TextureFormat.RGBAHalf, false, true);
                    using var values = new NativeArray<Color>(job.region.width * job.region.height, Allocator.Temp);
                    var output = values;
                    for (int y = 0, i = 0; y < job.region.height; y++)
                    for (int x = 0; x < job.region.width; x++, i++)
                        output[i] = pixels[(job.region.y + y) * job.width + job.region.x + x];
                    HdrUtility.WritePixels(crop, values);
                    job.capture["imagePath"] = WriteLivePng(crop, folder + "source.png");
                }
                if (job.mask != null)
                {
                    var mask = new Texture2D(job.region.width, job.region.height, TextureFormat.RGBA32, false, true);
                    try
                    {
                        var pixels = mask.GetRawTextureData<Color32>();
                        for (int y = 0, i = 0; y < job.region.height; y++)
                        for (int x = 0; x < job.region.width; x++, i++)
                        {
                            byte c = job.mask[(job.region.y + y) * job.width + job.region.x + x];
                            pixels[i] = new Color32(c, c, c, 255);
                        }
                        mask.Apply(false, false);
                        job.capture["maskPath"] = WriteLiveBytes(mask.EncodeToPNG(), folder + "mask.png");
                        job.capture["maskMeaning"] = job.selectionMode == "strict"
                            ? "White = editable; black = preserve. Mask is also enforced locally on completion."
                            : "White = suggested subject area; black = surrounding context, not protected pixels. Return the whole crop; output may extend anywhere within it. Mask is not enforced.";
                    }
                    finally { Object.DestroyImmediate(mask); }
                }
            }
            finally
            {
                if (rendered != null) Object.DestroyImmediate(rendered);
                if (crop != null) Object.DestroyImmediate(crop);
            }
        }

        private static string WriteLiveBytes(byte[] bytes, string path)
        {
            Require(path != null && path.StartsWith("Temp/WhimTex/", StringComparison.Ordinal) &&
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Preview path must be Temp/WhimTex/*.png.");
            ValidateSegments(path);
            string full = FullPath(path); RejectLinks(full);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes, 0, bytes.Length);
            return full;
        }

        private static string WriteLivePng(Texture2D image, string path)
        {
            var encoded = HdrUtility.ToLdr(image);
            try { return WriteLiveBytes(encoded.EncodeToPNG(), path); }
            finally { Object.DestroyImmediate(encoded); }
        }

        private static JObject RenderLiveSession(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "sourceLayerId", "maxSize", "outputPath");
            var document = LiveWindow(Text(request, "sessionId")).AgentDocument;
            LiveReady(document); RequireGraphics();
            string layerId = Text(request, "sourceLayerId");
            var layer = layerId == null ? null : document.FindLayer(layerId);
            Require(layerId == null || layer != null && !(layer?.Behaviour is PendingLayerBehaviour), "Source layer not found.");
            int size = Int(request, "maxSize", 1024, 1, 4096);
            Texture2D texture = null;
            RenderTexture rt = null;
            try
            {
                if (layer == null) texture = document.ComposePreview(size);
                else
                {
                    rt = document.RenderAgentLayerPreview(layer, size);
                    Require(rt != null, "No layer preview was generated.", "render_failed");
                    texture = HdrUtility.ReadLinear(rt);
                }
                var result = Success();
                result["outputPath"] = WriteLivePng(texture, Text(request, "outputPath",
                    "Temp/WhimTex/Agent/preview-" + Guid.NewGuid().ToString("N") + ".png"));
                result["width"] = texture.width; result["height"] = texture.height;
                return result;
            }
            finally
            {
                if (texture != null) Object.DestroyImmediate(texture);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // PNG dimensions are checked before decoding to avoid allocating an unbounded image.
        private static Texture2D ReadLiveImage(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
                Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase), "imagePath must be an absolute local PNG path.");
            var file = new FileInfo(path);
            Require(file.Exists && file.Length >= 33 && file.Length <= 64 * 1024 * 1024, "PNG must exist and be at most 64 MiB.");
            byte[] bytes = File.ReadAllBytes(path);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            for (int i = 0; i < signature.Length; i++) Require(bytes[i] == signature[i], "Invalid PNG header.");
            uint width = BigEndian(16), height = BigEndian(20);
            Require(width > 0 && height > 0 && width <= 16384 && height <= 16384 &&
                (long)width * height <= MaxCanvasPixels, "PNG dimensions exceed the supported limit.", "resource_limit");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            try
            {
                Require(ImageConversion.LoadImage(texture, bytes, false), "Could not decode PNG.");
                Require(texture.width == width && texture.height == height, "Decoded PNG dimensions do not match its header.");
                return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
            uint BigEndian(int p) => (uint)bytes[p] << 24 | (uint)bytes[p + 1] << 16 | (uint)bytes[p + 2] << 8 | bytes[p + 3];
        }

        private static string LiveLayerRevision(Layer layer)
        {
            // Name and visibility do not affect ownership of pixels. Other layer settings do.
            var model = JObject.Parse(JsonUtility.ToJson(layer));
            model.Remove("layerName"); model.Remove("enabled");
            model.Remove("children");
            using var hash = SHA256.Create();
            var text = new StringBuilder(model.ToString(Newtonsoft.Json.Formatting.None));
            if (layer.modifiers != null)
                foreach (var modifier in layer.modifiers)
                    if (modifier != null) text.Append(EditorJsonUtility.ToJson(modifier));
            if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null)
                text.Append(Convert.ToBase64String(hash.ComputeHash(drawing.StoredTexture.GetRawTextureData())));
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static Rect LiveImagePlacement(RectInt region, int width, int height, string fit)
        {
            Require(fit == "stretch" || fit == "contain", "fit must be stretch or contain.");
            float drawWidth = region.width, drawHeight = region.height;
            if (fit == "contain")
            {
                float scale = Math.Min(drawWidth / width, drawHeight / height);
                drawWidth = width * scale; drawHeight = height * scale;
            }
            return new Rect(region.x + (region.width - drawWidth) * .5f,
                region.y + (region.height - drawHeight) * .5f, drawWidth, drawHeight);
        }

        private static Texture2D CreateLiveDrawingImage(LiveJob job, Texture2D image, string fit,
            out TextureTransform transform)
        {
            Rect placement = LiveImagePlacement(job.region, image.width, image.height, fit);
            transform = TextureTransform.Default;
            transform.scale = new Vector2(placement.width / job.width, placement.height / job.height);
            transform.position = placement.center - new Vector2(job.width * .5f, job.height * .5f);
            var texture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            try
            {
                var colors = image.GetPixels32();
                if (job.mask != null && job.selectionMode == "strict")
                {
                    for (int y = 0, i = 0; y < image.height; y++)
                    for (int x = 0; x < image.width; x++, i++)
                    {
                        int px = Mathf.FloorToInt(placement.x + (x + .5f) / image.width * placement.width);
                        int py = Mathf.FloorToInt(placement.y + (y + .5f) / image.height * placement.height);
                        int coverage = LiveSelectionCoverage(job, px, py);
                        colors[i].a = (byte)((colors[i].a * coverage + 127) / 255);
                    }
                }
                texture.SetPixels32(colors);
                texture.Apply(false, false);
                return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
        }

        private static NativeArray<Color> LiveImagePixels(LiveJob job, Texture2D image, string fit, DrawingLayerBehaviour target)
        {
            Require(fit == "stretch" || fit == "contain", "fit must be stretch or contain.");
            int width = target?.StoredTexture != null ? target.StoredTexture.width : job.width;
            int height = target?.StoredTexture != null ? target.StoredTexture.height : job.height;
            var output = target?.StoredTexture != null ? HdrUtility.ReadPixels(target.StoredTexture, Allocator.TempJob)
                : new NativeArray<Color>(width * height, Allocator.TempJob);
            try
            {
                using var source = HdrUtility.ReadPixels(image, Allocator.Temp);
                Rect placement = LiveImagePlacement(job.region, image.width, image.height, fit);
                var canvasTransform = target != null ? job.document.GetPaintTransform(target) : TextureTransform.Default;
                for (int y = 0, i = 0; y < height; y++)
                for (int x = 0; x < width; x++, i++)
                {
                    Vector2 uv = new Vector2((x + .5f) / width, (y + .5f) / height);
                    if (target != null) uv = TiledCanvasUtility.ToDocument(uv, canvasTransform, job.width, job.height);
                    float px = uv.x * job.width, py = uv.y * job.height;
                    if (px < job.region.xMin || py < job.region.yMin || px >= job.region.xMax || py >= job.region.yMax) continue;
                    float coverage = LiveSelectionCoverage(job, (int)px, (int)py) / 255f;
                    if (coverage <= 0) continue;
                    float u = (px - placement.x) / placement.width, v = (py - placement.y) / placement.height;
                    Color color = u < 0 || v < 0 || u >= 1 || v >= 1 ? UnityEngine.Color.clear :
                        SampleLiveImage(source, image.width, image.height, u, v);
                    output[i] = BlendLiveCoverage(output[i], color, coverage);
                }
                return output;
            }
            catch { output.Dispose(); throw; }
        }

        private static int LiveSelectionCoverage(LiveJob job, int x, int y)
        {
            if (x < 0 || y < 0 || x >= job.width || y >= job.height) return 0;
            return job.mask == null || job.selectionMode == "guide" ? 255 : job.mask[y * job.width + x];
        }

        private static Color SampleLiveImage(NativeArray<Color> pixels, int width, int height, float u, float v)
        {
            float x = Mathf.Clamp(u * width - .5f, 0, width - 1), y = Mathf.Clamp(v * height - .5f, 0, height - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            return BlendLiveCoverage(BlendLiveCoverage(pixels[y0 * width + x0], pixels[y0 * width + x1], x - x0),
                BlendLiveCoverage(pixels[y1 * width + x0], pixels[y1 * width + x1], x - x0), y - y0);
        }

        internal static Color BlendLiveCoverage(Color before, Color after, float coverage)
        {
            if (coverage <= 0) return before;
            if (coverage >= 1) return after;
            float alpha = Mathf.Lerp(before.a, after.a, coverage);
            Color premultiplied = UnityEngine.Color.Lerp(before * before.a, after * after.a, coverage);
            return alpha > 0 ? new Color(premultiplied.r / alpha, premultiplied.g / alpha, premultiplied.b / alpha, alpha) : UnityEngine.Color.clear;
        }
    }
}
