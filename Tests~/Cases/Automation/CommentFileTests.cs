using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class CommentFileTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target) ?? target.GetType().GetProperty(name, Flags).GetValue(target);
    static ApiJson Request(string op, params object[] values)
    { var r = new ApiJson { ["apiVersion"] = 1, ["op"] = op }; for (int i = 0; i < values.Length; i += 2) r[(string)values[i]] = ApiJson.FromObject(values[i + 1]); return r; }
    public static string Run() => TestContext.Run("Comments: full file and Headless lifecycle", t =>
    {
        using var scope = new MigrationD();
        string folder = scope.AssetFolder(), temp = scope.TempFolder(), path = folder + "/Comments.tiff";
        string session = "comment-file-" + Guid.NewGuid().ToString("N"); bool active = false;
        WhimTexWindow opened = null; WhimTexDocument openedDocument = null;
        string[] keys = { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings" };
        var preferences = keys.Select(key => (key, exists: EditorPrefs.HasKey(key), value: EditorPrefs.GetString(key))).ToArray();
        ApiJson Check(string json, string error = null)
        { var r = ApiJson.Parse(json); t.True(error == null ? (bool)r["success"] : !(bool)r["success"] && (string)r["errorCode"] == error, json); return r; }
        ApiJson Head(string op, params object[] values)
        { var r = Request(op, values); r["sessionId"] = session; return Check(WhimTexApi.TiffLiveJson(r.ToString())); }
        ApiJson FileComments() => Check(WhimTexApi.CommentsJson(Request("list", "assetPath", path, "includeResolved", true).ToString()));
        try
        {
            var mask = new Texture2D(16, 12, TextureFormat.RGBA32, false, true);
            string maskPath = Path.Combine(temp, "mask.png");
            var coverage = new Color32[16 * 12];
            for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++)
            { byte v = (byte)(x >= 3 && x < 12 && y > 2 && y < 10 && !(x > 6 && x < 9 && y > 5 && y < 8) ? x == 3 ? 85 : 255 : 0); coverage[y * 16 + x] = new Color32(v, v, v, 255); }
            try { mask.SetPixels32(coverage); mask.Apply(); File.WriteAllBytes(maskPath, mask.EncodeToPNG()); }
            finally { Object.DestroyImmediate(mask); }
            Head("begin", "assetPath", path, "create", true, "width", 16, "height", 12); active = true;
            var created = Head("preview", "requestId", "create", "operations", new object[] {
                new { op = "add", type = "color", settings = new { name = "Context" }, @as = "context" },
                new { op = "commentCreate", anchorUv = new[] { .5, .5 }, text = "Repair this mask", author = "User", attachments = new object[] {
                    new { layer = "@context" }, new { assetPath = "Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs" },
                    new { maskImagePath = maskPath, channel = "red", bounds = new[] { 0, 0, 16, 12 } }
                } }
            });
            string id = (string)created["document"]["comments"][0]["id"], layerId = (string)created["document"]["layers"][0]["id"];
            var headless = Check(WhimTexApi.CommentsJson(Request("context", "headlessSessionId", session, "commentIds", new[] { id }).ToString()));
            t.Equal(1, headless["masks"].Count, "Headless context exports attached mask coverage");
            t.True(!File.Exists(path), "Preview and context do not save the TIFF");
            Head("complete", "requestId", "save"); active = false;
            var saved = FileComments(); var thread = saved["comments"][0];
            t.Equal(id, (string)thread["id"], "TIFF retains comment identity");
            t.Equal(3, thread["messages"][0]["attachments"].Count, "TIFF retains all attachment kinds");
            string feedback = (string)thread["feedbackRevision"], link = (string)thread["link"];
            t.True((bool)thread["persistentLink"] && link.StartsWith("whimtex://comment/asset/"), "Saved-file inspection exposes a persistent GUID link");
            string revision = (string)saved["revision"];
            var maskInfo = thread["messages"][0]["attachments"][2];
            var exported = Check(WhimTexApi.CommentsJson(Request("mask", "assetPath", path,
                "maskRef", new { maskId = (string)maskInfo["maskId"], expectedRevision = (string)maskInfo["revision"] }).ToString()));
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try { ImageConversion.LoadImage(decoded, File.ReadAllBytes((string)exported["maskPath"])); t.True(coverage.Select(p => p.r).SequenceEqual(decoded.GetPixels32().Select(p => p.r)), "Saved-file mask export preserves soft edges, holes and orientation"); }
            finally { Object.DestroyImmediate(decoded); }
            object[] operations = {
                new { op = "set", layer = layerId, settings = new { opacity = .5 } },
                new { op = "comment", commentId = id, expectedFeedbackRevision = feedback, reply = "Applied masked correction", attachments = new object[] {
                    new { layer = layerId }, new { assetPath = "Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs" },
                    new { maskRef = new { maskId = (string)maskInfo["maskId"], expectedRevision = (string)maskInfo["revision"] } }
                } }
            };
            var batch = new ApiJson { ["apiVersion"] = 1, ["assetPath"] = path, ["expectedRevision"] = revision,
                ["operations"] = ApiJson.FromObject(operations), ["dryRun"] = true };
            byte[] baseline = File.ReadAllBytes(path);
            Check(WhimTexApi.ExecuteJson(batch.ToString()));
            t.True(baseline.SequenceEqual(File.ReadAllBytes(path)), "File dry-run preserves the exact TIFF bytes");
            batch["dryRun"] = false; Check(WhimTexApi.ExecuteJson(batch.ToString()));
            saved = FileComments(); thread = saved["comments"][0];
            t.Equal(feedback, (string)thread["feedbackRevision"], "File save/reopen preserves feedbackRevision after an agent reply with mask and references");
            t.True((bool)thread["messages"][1]["agentMessage"], "TIFF preserves attached agent reply provenance");
            Check(WhimTexApi.ExecuteJson(batch.ToString()), "revision_conflict");
            Head("begin", "assetPath", path, "expectedRevision", (string)saved["revision"]); active = true;
            Head("preview", "operations", new[] { new { op = "comment", commentId = id, expectedFeedbackRevision = feedback, status = "AwaitingReview", reply = "Ready for review" } });
            t.Equal("InProgress", (string)FileComments()["comments"][0]["status"], "Headless preview leaves saved status unchanged");
            Head("cancel"); active = false;
            t.Equal(2, FileComments()["comments"][0]["messages"].Count, "Cancel discards the pending reply");
            session = "comment-file-" + Guid.NewGuid().ToString("N");
            Head("begin", "assetPath", path, "expectedRevision", (string)FileComments()["revision"]); active = true;
            Head("complete", "requestId", "final", "operations", new[] { new { op = "comment", commentId = id, expectedFeedbackRevision = feedback, status = "AwaitingReview", reply = "Ready for review" } }); active = false;
            t.Equal("AwaitingReview", (string)FileComments()["comments"][0]["status"], "Headless completion saves review status");
            t.True(feedback != (string)FileComments()["comments"][0]["feedbackRevision"], "Explicit status transition intentionally changes feedbackRevision");
            string renamed = folder + "/Renamed.tiff";
            t.Equal("", AssetDatabase.MoveAsset(path, renamed), "Owned document can be renamed"); path = renamed;
            var navigation = Check(WhimTexApi.CommentsJson(Request("open", "link", link).ToString()));
            opened = Resources.FindObjectsOfTypeAll<WhimTexWindow>().Single(w => (string)Read(w, "AgentSessionId") == (string)navigation["sessionId"]);
            openedDocument = (WhimTexDocument)Read(opened, "AgentDocument");
            t.Equal(id, (string)navigation["commentId"], "The same link opens the renamed file's exact discussion");
            t.Equal("Comment", Read(opened, "canvasTool").ToString(), "Link activates Comment tool");
            t.Equal(id, (string)Read(Read(Read(opened, "activeCommentEditor"), "state"), "id"), "Link actually opens the card");
        }
        finally
        {
            if (active) Check(WhimTexApi.TiffLiveJson(Request("cancel", "sessionId", session).ToString()));
            if (opened != null) { opened.DiscardChanges(); opened.Close(); Object.DestroyImmediate(opened); }
            if (openedDocument != null) Object.DestroyImmediate(openedDocument);
            foreach (var p in preferences) if (p.exists) EditorPrefs.SetString(p.key, p.value); else EditorPrefs.DeleteKey(p.key);
        }
    });
}
