using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static string CommentMaskRevision(WhimTexDocument document, WhimTexCommentMask mask) => HashAgentToken(new JObject
        {
            ["id"] = mask.id, ["sha256"] = mask.sha256, ["width"] = mask.width, ["height"] = mask.height,
            ["localToCanvas"] = JObject.Parse(JsonUtility.ToJson(mask.localToCanvas)),
            ["canvas"] = new JArray(document.width, document.height)
        });

        private static JObject CommentMaskSnapshot(WhimTexDocument document, WhimTexCommentMask mask) => new JObject
        {
            ["kind"] = "mask", ["name"] = "Selection", ["maskId"] = mask.id,
            ["revision"] = CommentMaskRevision(document, mask), ["width"] = mask.width, ["height"] = mask.height,
            ["canvasBounds"] = LiveRect(mask.CanvasBounds(document.width, document.height)),
            ["localToCanvas"] = JObject.Parse(JsonUtility.ToJson(mask.localToCanvas)),
            ["coordinates"] = "Mask local UV and canvas UV: bottom-left. canvasBounds: clipped bottom-left canvas pixels.",
            ["coverage"] = "8-bit linear coverage: 0 outside, 255 inside; partial values preserve antialiasing. Nearest pixel-centre sampling."
        };

        private static WhimTexCommentMask CheckCommentMask(WhimTexDocument document, JToken token)
        {
            var reference = Obj(token, "maskRef"); Keys(reference, "maskId", "expectedRevision");
            string id = Text(reference, "maskId"), expected = Text(reference, "expectedRevision");
            Require(!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(expected), "maskRef requires maskId and expectedRevision.", "revision_required");
            document.ValidateComments();
            if (document.comments != null) foreach (var comment in document.comments) foreach (var message in comment.messages)
                if (message.attachments != null) foreach (var attachment in message.attachments)
                    if (attachment.mask?.id == id)
                    {
                        Require(CommentMaskRevision(document, attachment.mask) == expected,
                            "The attached mask or canvas placement changed; reread it.", "mask_revision_conflict");
                        return attachment.mask;
                    }
            throw new WhimTexApiException("mask_not_found", "The attached selection mask no longer exists.");
        }

        private static WhimTexCommentMask ParseCommentMask(WhimTexDocument document, JObject item, CanvasSelection selection)
        {
            if (item["selection"] != null)
            {
                Keys(item, "selection"); Require(Bool(item, "selection"), "selection must be true.");
                Require(selection != null && selection.Active && selection.Bounds.width > 0,
                    "selection:true requires a nonempty selection in an assistant session.", "selection_required");
                Require(selection.Width == document.width && selection.Height == document.height,
                    "Attach the selection before resizing the canvas in this batch.");
                return WhimTexCommentMask.Capture(selection.Coverage, selection.Width, selection.Height);
            }
            if (item["maskRef"] != null)
            {
                Keys(item, "maskRef"); return CheckCommentMask(document, item["maskRef"]).Copy(true);
            }
            Keys(item, "maskImagePath", "channel", "bounds");
            string channel = Text(item, "channel");
            Require(channel == "red" || channel == "alpha", "Mask PNG channel must explicitly be red or alpha.");
            Require(item["bounds"] is JArray bounds && bounds.Count == 4, "bounds must be [x,y,width,height] in bottom-left canvas pixels.");
            var b = (JArray)item["bounds"];
            var placement = new double[4];
            for (int i = 0; i < 4; i++)
            {
                Require(b[i].Type == JTokenType.Integer || b[i].Type == JTokenType.Float, "Mask bounds must be numeric.");
                placement[i] = (double)b[i]; Require(ProjectiveMatrix.Finite(placement[i]), "Mask bounds must be finite.");
            }
            Require(placement[2] > 0 && placement[3] > 0, "Mask bounds must have positive dimensions.");
            Texture2D image = ReadLiveImage(Text(item, "maskImagePath"));
            try
            {
                var pixels = image.GetPixels32(); var coverage = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) coverage[i] = channel == "red" ? pixels[i].r : pixels[i].a;
                var mask = WhimTexCommentMask.Capture(coverage, image.width, image.height);
                mask.localToCanvas = ProjectiveMatrix.Translate(placement[0] / document.width, placement[1] / document.height) *
                    ProjectiveMatrix.Scale(placement[2] / document.width, placement[3] / document.height) * mask.localToCanvas;
                mask.Validate(); return mask;
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static void ExportCommentMask(WhimTexDocument document, JObject request, JObject result)
        {
            RequireGraphics();
            var mask = CheckCommentMask(document, request["maskRef"]);
            string stem = "Temp/WhimTex/Agent/comment-mask-" + Guid.NewGuid().ToString("N");
            result["mask"] = CommentMaskSnapshot(document, mask);
            result["maskPath"] = WriteCommentMaskPng(mask.Rasterize(document.width, document.height), document.width, document.height, stem + ".png");
            result["sourceMaskPath"] = WriteCommentMaskPng(mask.Decode(), mask.width, mask.height, stem + "-source.png");
            result["maskMeaning"] = "Exact linear 8-bit coverage in RGB; alpha is 255. maskPath is the current full canvas; sourceMaskPath is the retained cropped source. PNG files have normal top-left image orientation.";
            result["workingSelectionChanged"] = false;
        }

        private static string WriteCommentMaskPng(byte[] coverage, int width, int height, string path)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var pixels = texture.GetRawTextureData<Color32>();
                for (int i = 0; i < pixels.Length; i++) { byte c = coverage[i]; pixels[i] = new Color32(c, c, c, 255); }
                texture.Apply(false, false); return WriteLiveBytes(texture.EncodeToPNG(), path);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
