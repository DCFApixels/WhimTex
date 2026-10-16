using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void BuildCommentContextBundle(WhimTexDocument document, JArray comments, JObject result)
        {
            var masks = new JArray(); var layers = new JArray(); var checks = new JArray();
            var ids = comments.Select(c => (string)c["id"]).ToArray();
            var attached = ids.Select(document.FindComment).SelectMany(c => c.messages).SelectMany(m => m.attachments ?? new List<WhimTexCommentAttachment>())
                .Where(a => a.mask != null).Select(a => a.mask).ToArray();
            Require(attached.Length <= 32 && attached.Sum(m => (long)document.width * document.height) <= 67108864,
                "Context supports at most 32 masks / 64 million full-canvas mask pixels; select fewer commentIds.", "resource_limit");
            foreach (JObject comment in comments) checks.Add(new JObject { ["commentId"] = comment["id"], ["expectedFeedbackRevision"] = comment["feedbackRevision"] });
            void Walk(IEnumerable<Layer> list, string parent)
            {
                foreach (var layer in list)
                {
                    layers.Add(new JObject { ["id"] = layer.Id, ["name"] = layer.layerName, ["parent"] = parent,
                        ["type"] = TypeName(layer),
                        ["enabled"] = layer.enabled, ["contentRevision"] = LiveLayerRevision(layer) });
                    if (layer.IsGroup) Walk(layer.layers, layer.Id);
                }
            }
            Walk(document.layers, null);
            Texture2D clean = ReadLiveImage((string)result["imagePath"]);
            try
            {
                var bounds = (JArray)result["cropBounds"];
                var crop = new RectInt((int)bounds[0], (int)bounds[1], (int)bounds[2], (int)bounds[3]);
                string stem = "Temp/WhimTex/Agent/context-" + Guid.NewGuid().ToString("N");
                result["checkerboardPath"] = WriteDiagnosticPixels(CheckerboardPixels(clean.GetPixels32(), clean.width, clean.height), clean.width, clean.height, stem + "-checker.png");
                foreach (var mask in attached)
                {
                    var reference = new JObject { ["maskId"] = mask.id, ["expectedRevision"] = CommentMaskRevision(document, mask) };
                    var item = new JObject { ["maskRef"] = reference };
                    ExportCommentMask(document, new JObject { ["maskRef"] = reference.DeepClone() }, item);
                    byte[] coverage = mask.Rasterize(document.width, document.height);
                    var pixels = CheckerboardPixels(clean.GetPixels32(), clean.width, clean.height);
                    for (int y = 0, i = 0; y < clean.height; y++) for (int x = 0; x < clean.width; x++, i++)
                    {
                        int cx = crop.x + Mathf.Min(crop.width - 1, (int)((x + .5f) * crop.width / clean.width));
                        int cy = crop.y + Mathf.Min(crop.height - 1, (int)((y + .5f) * crop.height / clean.height));
                        float amount = coverage[cy * document.width + cx] / 255f * .55f;
                        pixels[i] = UnityEngine.Color.Lerp(pixels[i], new Color(0.1f, .65f, 1, 1), amount);
                    }
                    item["overlayPath"] = WriteDiagnosticPixels(pixels, clean.width, clean.height, stem + "-" + mask.id + "-overlay.png");
                    item["overlayBounds"] = bounds.DeepClone(); masks.Add(item);
                }
            }
            finally { Object.DestroyImmediate(clean); }
            result["masks"] = masks; result["layers"] = layers; result["commentChecks"] = checks;
            result["workingSelectionChanged"] = false;
        }
    }
}
