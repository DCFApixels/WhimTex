using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JObject = WhimTex.Tests.ApiJson;
using JArray = WhimTex.Tests.ApiJson;
using JToken = WhimTex.Tests.ApiJson;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class CommentWorkflowTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags)?.GetValue(value) ?? value.GetType().GetProperty(name, Flags).GetValue(value);
    static object Invoke(string name, params object[] args) => typeof(WhimTexApi).GetMethod(name, Flags).Invoke(null, args);
    static JObject Request(string op, params object[] fields)
    { var r = new JObject { ["apiVersion"] = 1, ["op"] = op }; for (int i = 0; i < fields.Length; i += 2) r[(string)fields[i]] = JToken.FromObject(fields[i + 1]); return r; }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Execute(TestContext t, CancellationToken token)
    {
        WhimTexWindow window = null; WhimTexDocument document = null; Texture2D texture = null;
        var jobs = (System.Collections.IDictionary)typeof(WhimTexApi).GetField("liveJobs", Flags).GetValue(null);
        t.True(jobs.Values.Cast<object>().All(j => (string)Read(j, "state") != "pending"), "Recovery tests require no borrowed pending operations");
        var recovery = (System.Collections.IDictionary)typeof(WhimTexApi).GetField("recoverableLiveJobs", Flags).GetValue(null);
        t.Equal(0, recovery.Count, "Recovery tests require no borrowed interrupted operations");
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Read(window, "AgentDocument"); document.width = 96; document.height = 64; document.layers.Clear();
            texture = new Texture2D(96, 64, TextureFormat.RGBA32, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(.3f, .1f, .5f, 1), 96 * 64).ToArray()); texture.Apply();
            Color[] originalPixels = texture.GetPixels();
            var drawing = new DrawingLayerBehaviour(); var layer = new Layer(drawing);
            typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", Flags).Invoke(drawing, new object[] { texture });
            typeof(Layer).GetMethod("AssignNewId", Flags).Invoke(layer, null); document.layers.Add(layer);
            var coverage = new byte[96 * 64];
            for (int y = 16; y < 40; y++) for (int x = 20; x < 60; x++) if (!(x >= 32 && x < 48 && y >= 24 && y < 34)) coverage[y * 96 + x] = 255;
            var mask = WhimTexCommentMask.Capture(coverage, 96, 64);
            string thread = document.AddComment(new Vector2(.5f, .5f), "Repair this region", attachments:
                new System.Collections.Generic.List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(mask) }).id;
            mask = document.FindComment(thread).messages[0].attachments[0].mask;
            window.ShowUtility(); window.CreateGUI(); await AsyncD.Tick(window.rootVisualElement, token);
            string session = (string)Read(window, "AgentSessionId");
            JObject Check(string json, string error = null)
            { var r = JObject.Parse(json); t.True(error == null ? (bool)r["success"] : !(bool)r["success"] && (string)r["errorCode"] == error, json); return r; }
            JObject Live(JObject request, string error = null) => Check(WhimTexApi.LiveJson(request.ToString()), error);
            JObject Comments(JObject request) => Check(WhimTexApi.CommentsJson(request.ToString()));
            string Revision() => (string)Live(Request("inspect", "sessionId", session))["document"]["revision"];
            string initial = Revision();
            var bundle = Comments(Request("context", "assistantSessionId", session, "commentIds", new[] { thread }, "maxSize", 96));
            t.Equal(initial, Revision(), "Context bundle does not change the document");
            t.Equal(1, ((JArray)bundle["masks"]).Count, "Bundle includes mask exports and overlay");
            t.Equal(1, ((JArray)bundle["layers"]).Count, "Bundle includes compact layer context");
            t.True(bundle["layers"][0]["settings"] == null && (string)bundle["layers"][0]["type"] == "drawing", "Context omits unrelated brush settings");
            foreach (string key in new[] { "imagePath", "annotatedPath", "checkerboardPath" }) t.True(File.Exists((string)bundle[key]), key);
            t.True(File.Exists((string)bundle["masks"][0]["overlayPath"]), "Mask overlay exists");
            var maskRef = (JObject)bundle["masks"][0]["maskRef"];
            JObject Begin(bool replacement = true, JObject application = null) => Request("begin", "sessionId", session,
                "requestId", Guid.NewGuid().ToString("N"), "source", "layer", "sourceLayerId", layer.Id,
                "area", "commentMask", "maskRef", maskRef, "selectionMode", "guide", "padding", 0,
                "applicationMask", application ?? new JObject { ["fromGuide"] = true, ["expandPixels"] = 2, ["featherPixels"] = 3 },
                "destination", replacement ? "replacePixels" : "newLayer");
            var begin = Begin(); begin["targetLayerId"] = layer.Id;
            var invalid = Begin(); invalid["targetLayerId"] = layer.Id; invalid["selectionMode"] = "strict";
            Live(invalid, "invalid_request");
            var job = Live(begin); string jobId = (string)job["jobId"];
            var activeJob = jobs[jobId];
            byte[] appliedCoverage = (byte[])Read(activeJob, "applicationMask");
            Texture2D Load(string path) { var image = new Texture2D(2, 2); ImageConversion.LoadImage(image, File.ReadAllBytes(path)); return image; }
            var applicationImage = Load((string)job["capture"]["applicationMaskPath"]);
            try
            {
                var region = (JArray)job["capture"]["region"];
                byte At(int x, int y) => applicationImage.GetPixels32()[(y - (int)region[1]) * applicationImage.width + x - (int)region[0]].r;
                t.Equal((byte)255, At(18, 18), "Two-pixel expansion is solid");
                t.True(At(17, 18) > 0 && At(17, 18) < 255, "Outer feather has partial coverage");
                t.Equal((byte)0, At(40, 29), "Distant interior hole stays empty");
            }
            finally { Object.DestroyImmediate(applicationImage); }
            var green = new Texture2D(96, 64, TextureFormat.RGBA32, false, false);
            string path = Path.GetFullPath("Temp/WhimTex/workflow-green-" + Guid.NewGuid().ToString("N") + ".png");
            try { green.SetPixels(Enumerable.Repeat(Color.green, 96 * 64).ToArray()); green.Apply(); File.WriteAllBytes(path, green.EncodeToPNG()); }
            finally { Object.DestroyImmediate(green); }
            string beforePreview = Revision();
            var preview = Live(Request("preview", "jobId", jobId, "imagePath", path, "review", true, "maxSize", 96));
            t.Equal(beforePreview, Revision(), "Review remains detached");
            t.Equal(0, (int)preview["review"]["outsideApplicationPixels"], "No composite change outside allowed coverage");
            t.True((int)preview["review"]["outsideGuidePixels"] > 0, "Expanded application permits changes beyond guide");
            t.True(!(bool)preview["review"]["sampled"], "Native-size review is identified");
            t.True(preview["review"]["attachment"]["maskImagePath"] != null, "Actual changes can be attached directly");
            var comparison = Load((string)preview["review"]["comparisonPath"]);
            try { t.Equal(192, comparison.width, "Before/after comparison has two panels"); t.Equal(64, comparison.height, "Comparison keeps canvas aspect"); }
            finally { Object.DestroyImmediate(comparison); }
            // Exercise the serialized recovery protocol without changing any borrowed document/job.
            void Interrupt(string id)
            {
                Invoke("SaveLiveRecovery");
                typeof(WhimTexApi).GetField("liveReloading", Flags).SetValue(null, false);
                jobs.Remove(id);
            }
            Interrupt(jobId);
            t.Equal("interrupted", (string)Live(Request("status", "jobId", jobId))["state"], "Interrupted operation remains discoverable");
            layer.opacity = .9f;
            Live(Request("resume", "jobId", jobId, "sessionId", session), "revision_conflict"); layer.opacity = 1;
            var changedPlacement = mask.localToCanvas; mask.localToCanvas.m02 += .1;
            Live(Request("resume", "jobId", jobId, "sessionId", session), "mask_revision_conflict"); mask.localToCanvas = changedPlacement;
            var saved = JObject.Parse(recovery[jobId].ToString());
            string frozenPath = (string)saved["applicationMask"]["path"];
            byte[] frozenBytes = File.ReadAllBytes(frozenPath);
            try
            {
                var corrupt = (byte[])frozenBytes.Clone(); corrupt[0] ^= 255; File.WriteAllBytes(frozenPath, corrupt);
                Live(Request("resume", "jobId", jobId, "sessionId", session), "recovery_unavailable");
            }
            finally { File.WriteAllBytes(frozenPath, frozenBytes); }
            t.True((bool)Live(Request("resume", "jobId", jobId, "sessionId", session))["resumed"], "Unchanged operation resumes");
            t.Equal(jobId, (string)Live(Request("resume", "jobId", jobId, "sessionId", session))["jobId"], "Resume retry is idempotent");
            Live(Request("complete", "jobId", jobId, "imagePath", path));
            var resultTexture = (Texture2D)Read(drawing, "StoredTexture");
            t.True(resultTexture.GetPixel(3, 3).g < .2f, "Outside source pixels remain original after completion");
            t.True(resultTexture.GetPixel(24, 18).g > .9f, "Inside source pixels receive the candidate");
            Color[] resultPixels = resultTexture.GetPixels();
            t.True(Enumerable.Range(0, appliedCoverage.Length).Where(i => appliedCoverage[i] == 0)
                .All(i => resultPixels[i].Equals(originalPixels[i])), "Every source pixel outside application coverage is exactly preserved");
            // Reproduce the full mask-driven edit -> review attachment -> reply sequence.
            string Feedback() => (string)Comments(Request("list", "assistantSessionId", session))["comments"].First(c => (string)c["id"] == thread)["feedbackRevision"];
            string feedbackBeforeReply = Feedback();
            var replyRequest = new JObject { ["apiVersion"] = 1, ["sessionId"] = session, ["expectedRevision"] = Revision(),
                ["workId"] = Guid.NewGuid().ToString("N"), ["requestId"] = "masked-result",
                ["operations"] = JToken.FromObject(new[] { new { op = "comment", commentId = thread, expectedFeedbackRevision = feedbackBeforeReply,
                    reply = "Repaired the masked region", attachments = new object[] {
                        new { layer = layer.Id }, new { assetPath = "Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs" },
                        new { maskImagePath = (string)preview["review"]["attachment"]["maskImagePath"], channel = "red", bounds = new[] { 0, 0, 96, 64 } },
                        new { maskRef = new { maskId = (string)maskRef["maskId"], expectedRevision = (string)maskRef["expectedRevision"] } }
                    } } }) };
            Check(WhimTexApi.AssistantExecuteJson(replyRequest.ToString()));
            t.Equal(feedbackBeforeReply, Feedback(), "A result reply with layer, asset, review-mask and copied-mask attachments preserves feedbackRevision");
            await AsyncD.Tick(window.rootVisualElement, token);
            t.Equal(feedbackBeforeReply, Feedback(), "UI refresh after the attached reply preserves feedbackRevision");
            Undo.PerformUndo(); t.Equal(feedbackBeforeReply, Feedback(), "Undo of the attached agent reply preserves feedbackRevision");
            Undo.PerformRedo(); t.Equal(feedbackBeforeReply, Feedback(), "Redo of the attached agent reply preserves feedbackRevision");
            Check(WhimTexApi.AssistantExecuteJson(replyRequest.ToString()));
            t.Equal(2, document.FindComment(thread).messages.Count, "Retry of the attached reply does not duplicate the message");
            using (var roundtrip = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { AllowDrawingOmission = true }).Json, false))
                t.Equal(feedbackBeforeReply, (string)Invoke("FeedbackRevision", roundtrip.Document.FindComment(thread)), "JSON roundtrip retains attached reply provenance");
            var replyMask = document.FindComment(thread).messages.Last().attachments[2].mask;
            var replyPlacement = replyMask.localToCanvas; replyMask.localToCanvas.m02 += .01;
            t.True(feedbackBeforeReply != Feedback(), "User changes to an agent reply's mask become new feedback"); replyMask.localToCanvas = replyPlacement;
            document.SetCommentStatus(thread, WhimTexCommentStatus.AwaitingReview);
            t.True(feedbackBeforeReply != Feedback(), "Explicit review status deliberately changes feedbackRevision, even with the same reply");
            document.SetCommentStatus(thread, WhimTexCommentStatus.InProgress);
            // A separate application mask is guarded independently of the guide, and survives forks.
            var second = WhimTexCommentMask.Capture(coverage, 96, 64);
            second = document.AddComment(Vector2.one * .5f, "Application", attachments: new System.Collections.Generic.List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(second) }).messages[0].attachments[0].mask;
            var listed = Comments(Request("list", "assistantSessionId", session));
            var info = listed["comments"].Last()["messages"][0]["attachments"][0];
            var separate = new JObject { ["maskRef"] = new JObject { ["maskId"] = info["maskId"], ["expectedRevision"] = info["revision"] } };
            job = Live(Begin(false, separate)); jobId = (string)job["jobId"];
            var fork = Live(Request("fork", "jobId", jobId, "requestId", Guid.NewGuid().ToString("N")));
            second.localToCanvas.m12 += .1;
            Live(Request("preview", "jobId", (string)fork["jobId"], "imagePath", path), "mask_revision_conflict"); second.localToCanvas.m12 -= .1;
            Live(Request("cancel", "sessionId", session, "layerId", (string)fork["layerId"]));
            Interrupt(jobId);
            Live(Request("cancel", "sessionId", session, "layerId", (string)job["layerId"]));
            Live(Request("resume", "sessionId", session, "jobId", jobId), "recovery_unavailable");
            t.Equal(0, ((System.Collections.IDictionary)typeof(WhimTexApi).GetField("recoverableLiveJobs", Flags).GetValue(null)).Count, "Cancelled records are not revived");
            await AsyncD.Tick(window.rootVisualElement, token);
        }
        finally
        {
            typeof(WhimTexApi).GetField("liveReloading", Flags).SetValue(null, false);
            AsyncD.CleanupOwned(
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null) { window.DiscardChanges(); window.Close(); Object.DestroyImmediate(window); } },
                () => { if (document != null) Object.DestroyImmediate(document); },
                () => { if (texture != null) Object.DestroyImmediate(texture); });
        }
    }
}
