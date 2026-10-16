using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static Color32[] CheckerboardPixels(Color32[] input, int width, int height)
        {
            var result = new Color32[input.Length];
            for (int y = 0, i = 0; y < height; y++) for (int x = 0; x < width; x++, i++)
            {
                float gray = ((x / 8 + y / 8) & 1) == 0 ? .22f : .32f;
                Color value = Color.Lerp(new Color(gray, gray, gray, 1), input[i], input[i].a / 255f); value.a = 1;
                result[i] = value;
            }
            return result;
        }

        private static string WriteDiagnosticPixels(Color32[] pixels, int width, int height, string path)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try { image.SetPixels32(pixels); image.Apply(false, false); return WriteLiveBytes(image.EncodeToPNG(), path); }
            finally { Object.DestroyImmediate(image); }
        }

        private static JObject ReviewLiveCandidate(LiveJob job, WhimTexDocument candidate, int maxSize)
        {
            Texture2D before = null, after = null, beforeLdr = null, afterLdr = null;
            var active = RenderTexture.active;
            try
            {
                before = job.document.ComposeCanvas(maxSize); after = candidate.ComposeCanvas(maxSize);
                beforeLdr = HdrUtility.ToLdr(before); afterLdr = HdrUtility.ToLdr(after);
                int width = after.width, height = after.height;
                var a = beforeLdr.GetPixels32(); var b = afterLdr.GetPixels32();
                var difference = new Color32[a.Length]; var changedMask = new Color32[a.Length];
                var left = CheckerboardPixels(a, width, height); var right = CheckerboardPixels(b, width, height);
                var pair = new Color32[a.Length * 2]; var overlay = (Color32[])right.Clone();
                int changed = 0, outsideGuide = 0, outsideApplication = 0;
                const float threshold = 1f / 255f;
                for (int y = 0, i = 0; y < height; y++) for (int x = 0; x < width; x++, i++)
                {
                    Color ac = a[i], bc = b[i];
                    float delta = Mathf.Max(Mathf.Abs(ac.a - bc.a), Mathf.Max(Mathf.Abs(ac.r * ac.a - bc.r * bc.a),
                        Mathf.Max(Mathf.Abs(ac.g * ac.a - bc.g * bc.a), Mathf.Abs(ac.b * ac.a - bc.b * bc.a))));
                    bool different = delta > threshold;
                    int cx = Mathf.Min(job.width - 1, (int)((x + .5f) * job.width / width));
                    int cy = Mathf.Min(job.height - 1, (int)((y + .5f) * job.height / height));
                    int canvasIndex = cy * job.width + cx;
                    if (different)
                    {
                        changed++;
                        if (job.mask != null && job.mask[canvasIndex] == 0) outsideGuide++;
                        if (job.applicationMask != null && job.applicationMask[canvasIndex] == 0) outsideApplication++;
                        overlay[i] = Color.Lerp(right[i], new Color(1, .45f, .05f, 1), .65f);
                    }
                    byte v = different ? (byte)255 : (byte)0;
                    changedMask[i] = new Color32(v, v, v, 255);
                    difference[i] = new Color(Mathf.Clamp01(delta * 8), Mathf.Clamp01(delta * 2), 0, 1);
                    pair[y * width * 2 + x] = left[i]; pair[y * width * 2 + width + x] = right[i];
                }
                string stem = "Temp/WhimTex/Agent/" + job.id + "/review-" + Guid.NewGuid().ToString("N");
                string maskPath = WriteDiagnosticPixels(changedMask, width, height, stem + "-mask.png");
                return new JObject
                {
                    ["beforePath"] = WriteLiveBytes(beforeLdr.EncodeToPNG(), stem + "-before.png"),
                    ["afterPath"] = WriteLiveBytes(afterLdr.EncodeToPNG(), stem + "-after.png"),
                    ["comparisonPath"] = WriteDiagnosticPixels(pair, width * 2, height, stem + "-comparison.png"),
                    ["differencePath"] = WriteDiagnosticPixels(difference, width, height, stem + "-difference.png"),
                    ["changedOverlayPath"] = WriteDiagnosticPixels(overlay, width, height, stem + "-overlay.png"),
                    ["changedMaskPath"] = maskPath, ["width"] = width, ["height"] = height,
                    ["canvasBounds"] = new JArray(0, 0, job.width, job.height), ["changedPixels"] = changed,
                    ["outsideGuidePixels"] = job.mask == null ? (JToken)JValue.CreateNull() : new JValue(outsideGuide),
                    ["outsideApplicationPixels"] = job.applicationMask == null ? (JToken)JValue.CreateNull() : new JValue(outsideApplication),
                    ["sampled"] = width != job.width || height != job.height,
                    ["meaning"] = "Current composite before/after at the reported resolution; premultiplied sRGB/alpha difference > 1/255. Orange shows changed samples. FX may spread beyond application coverage. Comparison: before left, after right, on checkerboard.",
                    ["attachment"] = changed == 0 ? null : new JObject { ["maskImagePath"] = maskPath, ["channel"] = "red", ["bounds"] = new JArray(0, 0, job.width, job.height) }
                };
            }
            finally
            {
                RenderTexture.active = active;
                if (before != null) Object.DestroyImmediate(before); if (after != null) Object.DestroyImmediate(after);
                if (beforeLdr != null) Object.DestroyImmediate(beforeLdr); if (afterLdr != null) Object.DestroyImmediate(afterLdr);
            }
        }
    }
}
