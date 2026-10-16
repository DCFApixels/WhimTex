using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void RequireLiveMaskBudget(LiveJob candidate)
        {
            long Bytes(LiveJob job) => (job.mask?.LongLength ?? 0) +
                (ReferenceEquals(job.mask, job.applicationMask) ? 0 : job.applicationMask?.LongLength ?? 0);
            Require(liveJobs.Values.Where(j => j.state == "pending").Sum(Bytes) + Bytes(candidate) <= 67108864,
                "Pending guide/application masks exceed 64 MiB. Finish or cancel jobs first.", "resource_limit");
        }

        private static void CaptureApplicationMask(LiveJob job, JObject request)
        {
            if (request["applicationMask"] == null)
            { job.applicationMask = job.selectionMode == "strict" ? job.mask : null; return; }
            var spec = Obj(request["applicationMask"], "applicationMask");
            Require(job.mask == null || job.selectionMode == "guide", "An independent applicationMask requires selectionMode:guide; strict already enforces its selection.");
            Keys(spec, "fromGuide", "maskRef", "expandPixels", "featherPixels");
            Require((spec["fromGuide"] != null) != (spec["maskRef"] != null),
                "applicationMask requires exactly one of fromGuide:true or maskRef.");
            byte[] coverage;
            if (spec["fromGuide"] != null)
            {
                Require(Bool(spec, "fromGuide") && job.mask != null, "fromGuide:true requires a nonempty guide/selection.");
                coverage = job.mask;
            }
            else
            {
                coverage = CheckCommentMask(job.document, spec["maskRef"]).Rasterize(job.width, job.height);
                job.applicationMaskRef = (JObject)spec["maskRef"].DeepClone();
            }
            int expand = Int(spec, "expandPixels", 0, 0, 512), feather = Int(spec, "featherPixels", 0, 0, 512);
            var selection = new CanvasSelection(job.width, job.height);
            selection.Set(coverage, SelectionCombine.Replace);
            Require(selection.Active && selection.Bounds.width > 0, "Application mask has no coverage on the canvas.", "selection_required");
            job.applicationMask = GrowApplicationMask(coverage, job.width, job.height, expand, feather);
            selection.Set(job.applicationMask, SelectionCombine.Replace);
            var b = selection.Bounds;
            int left = Math.Min(job.region.xMin, b.xMin), bottom = Math.Min(job.region.yMin, b.yMin);
            job.region = new RectInt(left, bottom, Math.Max(job.region.xMax, b.xMax) - left, Math.Max(job.region.yMax, b.yMax) - bottom);
        }

        // Exact squared Euclidean distance transform; bounded O(width * height), independent of radius.
        private static byte[] GrowApplicationMask(byte[] input, int width, int height, int expand, int feather)
        {
            if (expand == 0 && feather == 0) return (byte[])input.Clone();
            const float far = 1e12f;
            var distances = new float[input.Length];
            int size = Math.Max(width, height);
            var f = new float[size]; var d = new float[size]; var sites = new int[size]; var cuts = new float[size + 1];
            void Transform(int count)
            {
                int k = 0; sites[0] = 0; cuts[0] = float.NegativeInfinity; cuts[1] = float.PositiveInfinity;
                for (int q = 1; q < count; q++)
                {
                    float s;
                    do
                    {
                        int v = sites[k]; s = (float)(((f[q] + (double)q * q) - (f[v] + (double)v * v)) / (2 * (q - v)));
                        if (s > cuts[k]) break;
                        k--;
                    } while (k >= 0);
                    k++; sites[k] = q; cuts[k] = s; cuts[k + 1] = float.PositiveInfinity;
                }
                k = 0;
                for (int q = 0; q < count; q++)
                { while (cuts[k + 1] < q) k++; float delta = q - sites[k]; d[q] = delta * delta + f[sites[k]]; }
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) f[x] = input[y * width + x] > 0 ? 0 : far;
                Transform(width); Array.Copy(d, 0, distances, y * width, width);
            }
            var output = new byte[input.Length];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++) f[y] = distances[y * width + x];
                Transform(height);
                for (int y = 0; y < height; y++)
                {
                    int i = y * width + x;
                    if (input[i] > 0) { output[i] = expand > 0 ? (byte)255 : input[i]; continue; }
                    float distance = Mathf.Sqrt(d[y]);
                    float a = distance <= expand ? 1 : feather > 0 ? Mathf.Clamp01(1 - (distance - expand) / feather) : 0;
                    output[i] = (byte)Mathf.RoundToInt(255 * a);
                }
            }
            return output;
        }

        private static string WriteCoverageRegion(byte[] coverage, int canvasWidth, RectInt region, string path)
        {
            var texture = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = texture.GetRawTextureData<Color32>();
                for (int y = 0, i = 0; y < region.height; y++) for (int x = 0; x < region.width; x++, i++)
                { byte v = coverage[(y + region.y) * canvasWidth + x + region.x]; pixels[i] = new Color32(v, v, v, 255); }
                texture.Apply(false, false); return WriteLiveBytes(texture.EncodeToPNG(), path);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
