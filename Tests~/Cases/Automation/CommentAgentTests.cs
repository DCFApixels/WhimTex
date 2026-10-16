using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class CommentAgentTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [Serializable] public class Attachment { public string kind, layerId, assetGuid, name, maskId, revision; public bool missing; }
    [Serializable] public class Message { public string id, author, text, createdUtc, editedUtc, workId; public bool agentMessage; public float[] authorColor; public Attachment[] attachments; }
    [Serializable] public class Comment { public string id, revision, feedbackRevision, status; public int number; public bool resolved, inView, useAuthorColor; public float[] anchorUv, anchorPixels, imagePoint, color; public Message[] messages; }
    [Serializable] public class Snapshot { public string revision; public Comment[] comments; }
    [Serializable] public class Operation { public string commentId, messageId, layerId; }
    [Serializable] public class Receipt { public Operation[] operations; }
    [Serializable] public class Response { public bool success, applied, replayed; public string errorCode, revision, imagePath, annotatedPath, maskPath, sourceMaskPath, jobId, layerId, state; public Comment[] comments; public Snapshot document; public int totalCount, imageWidth, imageHeight; public int[] cropBounds; public Operation[] operations; public Receipt receipt; }
    [Serializable] public class StringValue { public string value; }
    static string Q(string value) { string json = JsonUtility.ToJson(new StringValue { value = value }); return json.Substring(9, json.Length - 10); }
    static readonly MethodInfo Deserialize = typeof(WhimTexApi).GetMethod("Snapshot", Flags).ReturnType.Assembly
        .GetType("Newtonsoft.Json.JsonConvert").GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
    static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target) ?? target.GetType().GetProperty(name, Flags).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Execute(TestContext t, CancellationToken token)
    {
        WhimTexWindow window = null; WhimTexDocument document = null; Texture2D pixels = null;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Read(window, "activeDocument");
            document.width = 96; document.height = 64; document.layers.Clear();
            pixels = new Texture2D(96, 64, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 96; x++) pixels.SetPixel(x, y, y >= 32 ? Color.red : Color.blue);
            pixels.Apply(false);
            var drawing = new DrawingLayerBehaviour(); var layer = new Layer(drawing); document.layers.Add(layer);
            Call(layer, "AssignNewId");
            Call(drawing, "AdoptStoredTexture", pixels);
            string first = document.AddComment(new Vector2(.25f, .75f), "Поправь край / 边缘\nSecond line").id;
            string resolved = document.AddComment(new Vector2(.8f, .25f), "Finished", color: WhimTexComment.DefaultColor).id;
            document.SetCommentResolved(resolved, true);
            string outside = document.AddComment(new Vector2(-.2f, 1.2f), "Outside canvas").id;
            window.ShowUtility(); window.CreateGUI(); await AsyncD.Tick(window.rootVisualElement, token);
            string session = (string)Read(window, "AgentSessionId");
            Response Result(string json, string error = null)
            {
                var result = (Response)Deserialize.Invoke(null, new object[] { json, typeof(Response) });
                t.True(error == null ? result.success : !result.success && result.errorCode == error, json);
                if (error == null && json.Contains("\"document\":"))
                    t.True(result.document?.comments != null, "Document snapshot must include comments: " + json);
                return result;
            }
            Response Live(string fields, string error = null) => Result(WhimTexApi.LiveJson("{\"apiVersion\":1," + fields + "}"), error);
            Response Inspect() => Live("\"op\":\"inspect\",\"sessionId\":" + Q(session));
            Response Comments(string fields = "", string error = null) => Result(WhimTexApi.CommentsJson("{\"apiVersion\":1,\"assistantSessionId\":" + Q(session) + fields + "}"), error);
            Response Batch(string operations, bool dry = false, string error = null) => Result(WhimTexApi.AssistantExecuteJson("{\"apiVersion\":1,\"sessionId\":" + Q(session) +
                ",\"expectedRevision\":" + Q(Inspect().document.revision) + ",\"dryRun\":" + (dry ? "true" : "false") + ",\"operations\":[" + operations + "]}"), error);
            string Check(string id, string revision) => "\"commentId\":" + Q(id) + ",\"expectedRevision\":" + Q(revision);
            string CommentRevision(string id) => Inspect().document.comments.Single(c => c.id == id).revision;
            var snapshot = Inspect().document;
            t.Equal(3, snapshot.comments.Length, "Inspect includes unsaved and resolved threads");
            t.True(snapshot.comments[0].useAuthorColor, "Inspect exposes automatic comment color mode");
            t.True(!snapshot.comments[1].useAuthorColor, "Inspect preserves explicit manual color mode");
            Color expectedColor = WhimTexComment.ColorFromAuthor("User");
            t.True(snapshot.comments[0].color.SequenceEqual(new[] { expectedColor.r, expectedColor.g, expectedColor.b }), "Inspect reports the automatic marker color");
            t.True(snapshot.comments[1].messages[0].authorColor.SequenceEqual(new[] { expectedColor.r, expectedColor.g, expectedColor.b }),
                "Message author color is independent of the manual discussion color in agent snapshots");
            t.Equal(document.FindComment(first).messages[0].text, snapshot.comments[0].messages[0].text, "Inspect preserves Unicode and multiline text");
            t.Equal(24f, snapshot.comments[0].anchorPixels[0], "Anchor X is in canvas pixels");
            t.Equal(48f, snapshot.comments[0].anchorPixels[1], "Anchor Y has bottom-left origin");
            t.True(DateTime.TryParse(snapshot.comments[0].messages[0].createdUtc, out _), "Message time uses an interoperable timestamp");
            t.True(snapshot.comments[0].messages[0].editedUtc == null, "Unedited messages expose a null edit time");
            document.FindComment(first).messages[0].editedUtcTicks = DateTime.UtcNow.Ticks;
            t.True(DateTime.TryParse(Inspect().document.comments[0].messages[0].editedUtc, out _), "Edited messages expose an interoperable edit timestamp");
            t.Equal(2, Comments().comments.Length, "List defaults to unresolved comments");
            t.Equal(3, Comments(",\"includeResolved\":true").comments.Length, "List can include resolved comments");
            t.Equal(2, Comments(",\"commentIds\":[" + Q(resolved) + "]").comments[0].number, "Explicit IDs include resolved threads and preserve display numbers");
            Comments(",\"commentIds\":[\"missing\"]", "comment_not_found");
            Comments(",\"commentIds\":[" + Q(first) + "," + Q(first) + "]", "invalid_request");
            Comments(",\"unexpected\":true", "invalid_request");
            Comments(",\"assetPath\":\"Assets/unused.tiff\"", "invalid_request");
            string beforeRender = Inspect().document.revision;
            var previous = RenderTexture.active;
            var overview = Comments(",\"op\":\"render\",\"includeResolved\":true");
            t.True(RenderTexture.active == previous, "Comment rendering restores the caller render target");
            t.Equal(beforeRender, Inspect().document.revision, "Comment preview does not edit the document");
            t.Equal(96, overview.imageWidth, "Overview preserves native small-image width");
            t.True(!overview.comments.Single(c => c.id == outside).inView, "Off-canvas markers remain in metadata");
            t.Near(16, overview.comments[0].imagePoint[1], .001, "Marker Y converts to top-left image coordinates");
            Texture2D Load(string path) { var value = new Texture2D(2, 2); ImageConversion.LoadImage(value, File.ReadAllBytes(path)); return value; }
            var clean = Load(overview.imagePath); var annotated = Load(overview.annotatedPath);
            try
            {
                t.True(clean.GetPixel(4, 52).r > .99f && clean.GetPixel(4, 12).b > .99f, "Clean context preserves top and bottom content");
                t.True(!clean.GetPixels32().SequenceEqual(annotated.GetPixels32()), "Annotations exist only in the separate image");
            }
            finally { Object.DestroyImmediate(clean); Object.DestroyImmediate(annotated); }
            var crop = Comments(",\"op\":\"render\",\"commentIds\":[" + Q(first) + "],\"focusCommentId\":" + Q(first) + ",\"radius\":8");
            t.Equal(17, crop.imageWidth, "Focused context crops at native canvas resolution");
            clean = Load(crop.imagePath);
            try { t.True(clean.GetPixels().All(c => c.r > .99f && c.b < .01f), "Focused context uses the correct canvas region"); }
            finally { Object.DestroyImmediate(clean); }
            Comments(",\"op\":\"render\",\"focusCommentId\":" + Q(outside), "comment_outside_canvas");
            var current = CommentRevision(first);
            string reply = "{\"op\":\"comment\"," + Check(first, current) + ",\"reply\":\"Изменил край\"}";
            Batch(reply, true); t.Equal(1, document.FindComment(first).messages.Count, "Dry-run does not post a reply");
            Batch("{\"op\":\"set\",\"layer\":" + Q(layer.Id) + ",\"settings\":{\"opacity\":0.5}}," + reply);
            t.Equal(2, document.FindComment(first).messages.Count, "Batch appends one reply");
            t.Equal("Assistant", document.FindComment(first).messages[1].author, "Replies identify the assistant by default");
            t.True(!document.FindComment(first).resolved, "Reply does not silently resolve a discussion");
            Undo.PerformUndo(); t.Equal(1, document.FindComment(first).messages.Count, "One Undo removes reply with the image edit");
            t.Equal(1f, document.layers[0].opacity, "Undo restores the layer setting too");
            Undo.PerformRedo(); t.Equal(2, document.FindComment(first).messages.Count, "Redo restores the reply");
            document.FindComment(first).messages[1].authorColor = Color.yellow;
            t.True(Inspect().document.comments[0].messages[1].agentMessage, "Author color changes do not invalidate agent message provenance");
            Batch(reply, error: "comment_revision_conflict");
            current = CommentRevision(first);
            document.FindComment(first).messages[0].text = "Changed while the agent worked";
            Batch("{\"op\":\"comment\"," + Check(first, current) + "},{\"op\":\"set\",\"layer\":" + Q(layer.Id) + ",\"settings\":{\"opacity\":0.1}}", error: "comment_revision_conflict");
            t.Equal(.5f, document.layers[0].opacity, "A fresh document revision cannot bypass a stale discussion check");
            Batch("{\"op\":\"comment\"," + Check(first, CommentRevision(first)) + ",\"resolved\":true}");
            t.True(document.FindComment(first).resolved, "Resolve changes only the requested thread");
            Batch("{\"op\":\"comment\"," + Check(first, CommentRevision(first)) + ",\"resolved\":false}");
            t.True(!document.FindComment(first).resolved, "Reopen works through the same operation");
            foreach (string status in new[] { "AwaitingReview", "Resolved", "AwaitingReview", "InProgress" })
            {
                string operation = "{\"op\":\"comment\"," + Check(first, CommentRevision(first)) + ",\"status\":" + Q(status) + "}";
                string unchanged = JsonUtility.ToJson(document.FindComment(first));
                Batch(operation, true);
                t.Equal(unchanged, JsonUtility.ToJson(document.FindComment(first)), "Status dry-run does not change the live thread");
                Batch(operation);
                t.Equal(status, Inspect().document.comments.Single(c => c.id == first).status, "Agent can set and inspect every status");
            }
            string invalidPrefix = "{\"op\":\"comment\"," + Check(first, CommentRevision(first));
            foreach (string invalid in new[] { "\"unknown\"", "1", "null", "\"1\"" })
                Batch(invalidPrefix + ",\"status\":" + invalid + "}", error: "invalid_request");
            Batch(invalidPrefix + ",\"status\":\"AwaitingReview\",\"resolved\":false}", error: "invalid_request");
            string reviewFeedback = Inspect().document.comments.Single(c => c.id == first).feedbackRevision;
            Batch(invalidPrefix + ",\"status\":\"AwaitingReview\",\"reply\":\"Ready for review\"}");
            t.True(reviewFeedback != Inspect().document.comments.Single(c => c.id == first).feedbackRevision, "Status changes invalidate a stale feedback check");
            t.Equal("AwaitingReview", Comments().comments.Single(c => c.id == first).status, "Open comments include pending review");
            Undo.PerformUndo();
            t.Equal("InProgress", Inspect().document.comments.Single(c => c.id == first).status, "Undo restores the previous status with the reply");
            Batch("{\"op\":\"set\",\"layer\":" + Q(layer.Id) + ",\"settings\":{\"opacity\":0.2}},{\"op\":\"comment\"," + Check(first, CommentRevision(first)) + ",\"reply\":\" \"}", error: "invalid_request");
            t.Equal(.5f, document.layers[0].opacity, "An invalid reply rejects the whole batch before mutation");
            string checks = "\"commentChecks\":[{" + Check(first, CommentRevision(first)) + "}]";
            string job = Live("\"op\":\"begin\",\"sessionId\":" + Q(session) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) + "," + checks).jobId;
            string completion = "\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"layer\":{\"type\":\"color\"}";
            Live(completion);
            int completedCount = document.layers.Count;
            Batch("{\"op\":\"comment\"," + Check(first, CommentRevision(first)) + ",\"reply\":\"Added the requested layer\"}");
            Live(completion);
            t.Equal(completedCount, document.layers.Count, "A completed job retry remains idempotent after replying to its comment");
            checks = "\"commentChecks\":[{" + Check(first, CommentRevision(first)) + "}]";
            job = Live("\"op\":\"lock\",\"sessionId\":" + Q(session) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) + ",\"layerId\":" + Q(layer.Id) + "," + checks).jobId;
            Live("\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"changes\":{\"settings\":{\"opacity\":0.6}}");
            t.Equal(.6f, document.layers.Single(l => l.Id == layer.Id).opacity, "An unchanged comment permits a guarded existing-layer edit");
            Batch("{\"op\":\"set\",\"layer\":" + Q(layer.Id) + ",\"settings\":{\"opacity\":0.5}}");
            job = Live("\"op\":\"begin\",\"sessionId\":" + Q(session) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) + "," + checks).jobId;
            string fork = Live("\"op\":\"fork\",\"jobId\":" + Q(job) + ",\"requestId\":" + Q(Guid.NewGuid().ToString())).jobId;
            document.FindComment(first).messages[0].text += " again";
            Live("\"op\":\"preview\",\"jobId\":" + Q(job) + ",\"layer\":{\"type\":\"color\"}", "comment_revision_conflict");
            Live("\"op\":\"complete\",\"jobId\":" + Q(fork) + ",\"layer\":{\"type\":\"color\"}", "comment_revision_conflict");
            foreach (string id in new[] { job, fork })
            {
                var pending = Live("\"op\":\"status\",\"jobId\":" + Q(id));
                Live("\"op\":\"cancel\",\"sessionId\":" + Q(session) + ",\"layerId\":" + Q(pending.layerId));
            }
            checks = "\"commentChecks\":[{" + Check(first, CommentRevision(first)) + "}]";
            job = Live("\"op\":\"lock\",\"sessionId\":" + Q(session) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) + ",\"layerId\":" + Q(layer.Id) + "," + checks).jobId;
            document.RemoveComment(first);
            t.Equal(2, Inspect().document.comments.Single(c => c.id == resolved).number, "Deleting the first thread does not renumber agent snapshots");
            Live("\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"changes\":{\"settings\":{\"opacity\":0.2}}", "comment_not_found");
            Live("\"op\":\"unlock\",\"jobId\":" + Q(job));
            t.Equal(.5f, document.layers.Single(l => l.Id == layer.Id).opacity, "Deleting the source comment prevents completion of a locked edit");
            string work = Guid.NewGuid().ToString("N");
            string task = document.AddComment(Vector2.one * .5f, "Add a result").id;
            t.Equal(4, Inspect().document.comments.Single(c => c.id == task).number, "New threads continue the permanent sequence after deletion");
            string WorkRequest(string step, string operations) => "{\"apiVersion\":1,\"sessionId\":" + Q(session) + ",\"workId\":" + Q(work) +
                ",\"requestId\":" + Q(step) + ",\"expectedRevision\":" + Q(Inspect().document.revision) + ",\"operations\":[" + operations + "]}";
            string editRequest = WorkRequest("edit", "{\"op\":\"duplicate\",\"layer\":" + Q(layer.Id) + ",\"as\":\"copy\"},{\"op\":\"comment\"," +
                Check(task, CommentRevision(task)) + ",\"reply\":\"Result\",\"attachments\":[{\"layer\":\"@copy\"},{\"assetPath\":\"Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs\"}]}");
            Result(WhimTexApi.AssistantExecuteJson(editRequest));
            int layerCount = document.layers.Count;
            var message = document.FindComment(task).messages.Last();
            t.Equal(2, message.attachments.Count, "Agent can attach the newly created alias and a Project asset");
            t.True(message.attachments[1].ResolveAsset() != null, "Asset attachment resolves its GUID/local ID");
            t.Equal(work, message.agentWorkId, "Reply and edits share a work ID");
            t.True(Result(WhimTexApi.AssistantExecuteJson(editRequest)).replayed, "An identical request replays its receipt despite a stale document revision");
            t.Equal(layerCount, document.layers.Count, "Retry does not duplicate the layer");
            t.Equal(2, document.FindComment(task).messages.Count, "Retry does not duplicate the reply");
            Result(WhimTexApi.AssistantExecuteJson(editRequest.Replace("\"Result\"", "\"Different\"")), "request_conflict");
            Undo.PerformUndo();
            Result(WhimTexApi.AssistantExecuteJson(editRequest), "request_undone");
            Undo.PerformRedo();
            t.True(Result(WhimTexApi.AssistantExecuteJson(editRequest)).replayed, "Redo restores both edits and receipt");
            t.Equal(1, document.agentReceipts.Count, "Receipts participate in the same Undo group");
            Comments(",\"op\":\"work\",\"workId\":" + Q(work));
            string feedback = Inspect().document.comments.Single(c => c.id == task).feedbackRevision;
            string begin = "\"op\":\"begin\",\"sessionId\":" + Q(session) + ",\"workId\":" + Q(work) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) +
                ",\"commentChecks\":[{\"commentId\":" + Q(task) + ",\"expectedFeedbackRevision\":" + Q(feedback) + "}]";
            job = Live(begin).jobId;
            string fullRevision = CommentRevision(task);
            var colorThread = document.FindComment(task);
            colorThread.color = Color.magenta; colorThread.useAuthorColor = false;
            colorThread.colorAuthorId = Guid.NewGuid().ToString("N");
            foreach (var posted in colorThread.messages)
            { posted.authorColor = Color.green; posted.authorId = Guid.NewGuid().ToString("N"); }
            t.Equal(feedback, Inspect().document.comments.Single(c => c.id == task).feedbackRevision, "Thread and author colors do not change task feedback");
            t.True(fullRevision != CommentRevision(task), "Full comment revision remains strict about presentation metadata");
            Batch("{\"op\":\"comment\"," + Check(task, fullRevision) + "}", error: "comment_revision_conflict");
            Result(WhimTexApi.AssistantExecuteJson(WorkRequest("progress", "{\"op\":\"comment\"," + Check(task, CommentRevision(task)) + ",\"reply\":\"Working\"}")));
            t.Equal(feedback, Inspect().document.comments.Single(c => c.id == task).feedbackRevision, "Agent progress does not change task feedback");
            Live("\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"layer\":{\"type\":\"color\"}");
            t.Equal(3, document.agentReceipts.Count, "Live completion is retained alongside edit/progress receipts");
            ((System.Collections.IDictionary)typeof(WhimTexApi).GetField("liveJobs", Flags).GetValue(null)).Remove(job);
            int finalLayers = document.layers.Count;
            document.FindComment(task).messages[0].text += " Updated";
            t.True(Live(begin).replayed, "A completed reservation is recovered from the model without its transient job");
            t.Equal(finalLayers, document.layers.Count, "Recovering a completed reservation does not reserve a second layer");
            t.True(feedback != Inspect().document.comments.Single(c => c.id == task).feedbackRevision, "User corrections change task feedback");
            feedback = Inspect().document.comments.Single(c => c.id == task).feedbackRevision;
            document.FindComment(task).messages.Last().text += " edited by user";
            t.True(feedback != Inspect().document.comments.Single(c => c.id == task).feedbackRevision, "Editing an agent message turns it into new feedback");
            string Feedback() => Inspect().document.comments.Single(c => c.id == task).feedbackRevision;
            var guarded = document.FindComment(task);
            feedback = Feedback();
            guarded.anchor += Vector2.one * .1f;
            t.True(feedback != Feedback(), "Moving the marker changes task feedback");
            guarded.anchor -= Vector2.one * .1f; feedback = Feedback();
            guarded.resolved = true;
            t.True(feedback != Feedback(), "Resolving changes task feedback");
            guarded.resolved = false; feedback = Feedback();
            guarded.messages[0].attachments.Add(WhimTexCommentAttachment.ForLayer(document.layers[0]));
            t.True(feedback != Feedback(), "Adding context changes task feedback");

            int threadsBefore = document.comments.Count, layersBefore = document.layers.Count;
            string create = "{\"op\":\"commentCreate\",\"anchorUv\":[0.3,0.7],\"text\":\"Review the edge\",\"author\":\"Agent\",\"attachments\":[{\"layer\":\"@context\"},{\"assetPath\":\"Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs\"}]}";
            string createOps = "{\"op\":\"duplicate\",\"layer\":" + Q(layer.Id) + ",\"as\":\"context\"}," + create;
            Batch(createOps, true);
            t.Equal(threadsBefore, document.comments.Count, "Creating a discussion in dry-run leaves the original untouched");
            t.Equal(layersBefore, document.layers.Count, "Creation dry-run leaves no context layer");
            foreach (string invalid in new[] {
                "{\"op\":\"commentCreate\",\"text\":\"Missing anchor\"}",
                "{\"op\":\"commentCreate\",\"anchorUv\":[1.01,0],\"text\":\"Outside\"}",
                "{\"op\":\"commentCreate\",\"anchorUv\":[0,0],\"text\":\" \"}",
                "{\"op\":\"commentCreate\",\"anchorUv\":[0,0],\"text\":\"Context\",\"attachments\":[{\"layer\":\"missing\"}]}" })
            {
                var rejected = WhimTexApi.AssistantExecuteJson(WorkRequest("invalid-create", createOps + "," + invalid));
                t.True(!((Response)Deserialize.Invoke(null, new object[] { rejected, typeof(Response) })).success, "Invalid creation rejects the whole batch: " + rejected);
                t.Equal(threadsBefore, document.comments.Count, "Invalid creation leaves no partial discussion");
                t.Equal(layersBefore, document.layers.Count, "Invalid creation leaves no partial layer");
            }
            Batch("{\"op\":\"commentCreate\",\"anchorUv\":[0,1],\"text\":" + Q(new string('x', 16384)) + "}", true);
            Batch("{\"op\":\"commentCreate\",\"anchorUv\":[0,1],\"text\":" + Q(new string('x', 16385)) + "}", true, "invalid_request");
            string createRequest = WorkRequest("create-discussion", createOps);
            var creation = Result(WhimTexApi.AssistantExecuteJson(createRequest));
            string createdId = creation.operations[1].commentId;
            var created = document.FindComment(createdId);
            t.True(created != null, "Creation returns a stable comment ID");
            t.Equal(created.messages[0].id, creation.operations[1].messageId, "Creation returns the first message ID");
            t.Equal(new Vector2(.3f, .7f), created.anchor, "Creation uses bottom-left canvas UV");
            t.True(!created.resolved && created.useAuthorColor, "New discussions are open and use automatic author color");
            t.Equal(WhimTexComment.ColorFromAuthor("Agent"), created.color, "Agent discussion color comes from its author");
            t.Equal(work, created.messages[0].agentWorkId, "Created discussion is associated with its work");
            t.True(creation.document.comments.Last().messages[0].agentMessage, "Created message has agent provenance");
            t.Equal(creation.operations[0].layerId, created.messages[0].attachments[0].layerId, "Creation resolves an earlier layer alias");
            t.True(created.messages[0].attachments[1].ResolveAsset() != null, "Creation supports a Unity asset attachment");
            var replay = Result(WhimTexApi.AssistantExecuteJson(createRequest));
            t.True(replay.replayed, "Creation retry replays the receipt");
            t.Equal(createdId, replay.receipt.operations[1].commentId, "Replay retains the original discussion ID");
            t.Equal(threadsBefore + 1, document.comments.Count, "Retry creates no duplicate discussion");
            Undo.PerformUndo();
            t.True(document.FindComment(createdId) == null, "Undo removes the discussion");
            t.Equal(layersBefore, document.layers.Count, "Undo removes its context layer together");
            Result(WhimTexApi.AssistantExecuteJson(createRequest), "request_undone");
            Undo.PerformRedo();
            t.Equal(createdId, Result(WhimTexApi.AssistantExecuteJson(createRequest)).receipt.operations[1].commentId, "Redo restores IDs and retry receipt");
            Batch("{\"op\":\"comment\"," + Check(createdId, CommentRevision(createdId)) + ",\"reply\":\"Follow-up\"}");
            t.Equal(2, document.FindComment(createdId).messages.Count, "An agent-created discussion accepts ordinary replies");
            // Mask attachments use actual coverage, not selection bounds or mutable window state.
            var selection = Read(window, "AgentSelection");
            var combine = Enum.Parse(selection.GetType().Assembly.GetType("DCFApixels.WhimTex.SelectionCombine"), "Replace");
            var coverage = new byte[96 * 64];
            for (int y = 12; y < 40; y++) for (int x = 20; x < 60; x++)
                if (!(x > 28 && x < 44 && y > 20 && y < 30)) coverage[y * 96 + x] = (byte)(x == 20 ? 77 : 255);
            Call(selection, "Set", coverage, combine);
            string selectionCreate = "{\"op\":\"commentCreate\",\"anchorUv\":[0.25,0.5],\"text\":\"Mask context\",\"attachments\":[{\"selection\":true}]}";
            Batch(selectionCreate, true);
            int countBeforeMask = document.comments.Count;
            string maskThread = Batch(selectionCreate).operations[0].commentId;
            t.Equal(countBeforeMask + 1, document.comments.Count, "Agent attaches the current selection");
            var storedMask = document.FindComment(maskThread).messages[0].attachments[0].mask;
            string maskId = storedMask.id;
            t.True(coverage.SequenceEqual(storedMask.Rasterize(96, 64)), "Agent snapshots all coverage, including holes and AA");
            Undo.PerformUndo(); t.True(document.FindComment(maskThread) == null, "Mask posting participates in Undo");
            Undo.PerformRedo(); storedMask = document.FindComment(maskThread).messages[0].attachments[0].mask;
            t.Equal(maskId, storedMask.id, "Redo retains mask identity");
            t.True(coverage.SequenceEqual(storedMask.Rasterize(96, 64)), "Redo retains exact mask coverage");
            var maskInfo = Inspect().document.comments.Single(c => c.id == maskThread).messages[0].attachments[0];
            t.Equal("mask", maskInfo.kind, "Agent list distinguishes mask attachments");
            string MaskRef(Attachment a) => "{\"maskId\":" + Q(a.maskId) + ",\"expectedRevision\":" + Q(a.revision) + "}";
            string maskRef = MaskRef(maskInfo);
            var exported = Comments(",\"op\":\"mask\",\"maskRef\":" + maskRef);
            var decoded = Load(exported.maskPath);
            try { t.True(decoded.GetPixels32().Select(c => c.r).SequenceEqual(coverage), "Mask PNG has exact linear coverage and correct row orientation"); }
            finally { Object.DestroyImmediate(decoded); }
            Call(selection, "All"); var working = (byte[])((byte[])Read(selection, "Coverage")).Clone();
            t.True(coverage.SequenceEqual(storedMask.Rasterize(96, 64)), "Changing working selection does not change attachment");
            string copyMask = "{\"op\":\"commentCreate\",\"anchorUv\":[0.6,0.5],\"text\":\"Copied mask\",\"attachments\":[{\"maskRef\":" + maskRef + "}]}";
            string copyThread = Batch(copyMask).operations[0].commentId;
            var copiedMask = document.FindComment(copyThread).messages[0].attachments[0].mask;
            t.True(copiedMask.id != storedMask.id && coverage.SequenceEqual(copiedMask.Rasterize(96, 64)), "Referencing a mask attaches an independent snapshot with a new ID");
            string imageThread = Batch("{\"op\":\"commentCreate\",\"anchorUv\":[0.6,0.5],\"text\":\"PNG mask\",\"attachments\":[{\"maskImagePath\":" + Q(exported.maskPath) + ",\"channel\":\"red\",\"bounds\":[0,0,96,64]}]}").operations[0].commentId;
            t.True(coverage.SequenceEqual(document.FindComment(imageThread).messages[0].attachments[0].mask.Rasterize(96, 64)), "Agent can attach a supplied PNG using an explicit channel and bounds");
            string BeginMask(string mode) => Live("\"op\":\"begin\",\"sessionId\":" + Q(session) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()) +
                ",\"source\":\"merged\",\"area\":\"commentMask\",\"maskRef\":" + maskRef + ",\"selectionMode\":" + Q(mode) + ",\"padding\":0").jobId;
            job = BeginMask("strict");
            t.True(working.SequenceEqual((byte[])Read(selection, "Coverage")), "Direct mask use does not replace the user's working selection");
            var placement = storedMask.localToCanvas; storedMask.localToCanvas.m02 += .01;
            Live("\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"imagePath\":" + Q(exported.maskPath), "mask_revision_conflict");
            Live("\"op\":\"fork\",\"jobId\":" + Q(job) + ",\"requestId\":" + Q(Guid.NewGuid().ToString()), "mask_revision_conflict");
            storedMask.localToCanvas = placement;
            var completed = Live("\"op\":\"complete\",\"jobId\":" + Q(job) + ",\"imagePath\":" + Q(exported.maskPath));
            var renderedMask = (Texture2D)Call(document, "RenderAreaSelectionAlphaSource", document.layers.Single(l => l.Id == completed.layerId));
            try
            {
                var output = renderedMask.GetPixels();
                for (int i = 0; i < coverage.Length; i++) t.Near(coverage[i] / 255f, output[i].a, .002, "Strict completion enforces exact mask alpha");
            }
            finally { Object.DestroyImmediate(renderedMask); }
            job = BeginMask("guide");
            var pendingGuide = Live("\"op\":\"status\",\"jobId\":" + Q(job));
            Live("\"op\":\"cancel\",\"sessionId\":" + Q(session) + ",\"layerId\":" + Q(pendingGuide.layerId));
            int oldWidth = document.width; document.width++;
            Comments(",\"op\":\"mask\",\"maskRef\":" + maskRef, "mask_revision_conflict"); document.width = oldWidth;
            document.RemoveComment(maskThread);
            Comments(",\"op\":\"mask\",\"maskRef\":" + maskRef, "mask_not_found");
            await AsyncD.Tick(window.rootVisualElement, token);
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null) { window.DiscardChanges(); window.Close(); Object.DestroyImmediate(window); } },
                () => { if (document != null) Object.DestroyImmediate(document); },
                () => { if (pixels != null) Object.DestroyImmediate(pixels); });
        }
    }
}
