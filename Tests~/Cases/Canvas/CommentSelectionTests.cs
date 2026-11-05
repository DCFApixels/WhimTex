using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class CommentSelectionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static object Get(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, F).SetValue(obj, value);
    static object Create(Type type, params object[] args) => Activator.CreateInstance(type, F, null, args, null);
    static void EnumField(object obj, string field, string value) => Set(obj, field, Enum.Parse(obj.GetType().GetField(field, F).FieldType, value));

    public static string Run() => TestContext.Run("Comment box selection, cancellation, scoped deletion and one-step Undo", t =>
    {
        // An unshown owned window only; no native input, modal dialogs or user documents.
        WhimTexWindow window = null;
        WhimTexDocument document = null, other = null;
        PointerManipulator manipulator = null;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Get(window, "activeDocument");
            document.width = document.height = 8192; document.layers.Clear();
            EnumField(window, "canvasTool", "Comment");
            EnumField(window, "commentFilter", "Open");
            var a = document.AddComment(new Vector2(.1f, .8f), "A", "Artist");
            var b = document.AddComment(new Vector2(.3f, .7f), "B", "Artist");
            var c = document.AddComment(new Vector2(.8f, .2f), "C", "Artist");
            var hidden = document.AddComment(a.anchor, "Hidden", "Artist");
            document.SetCommentResolved(hidden.id, true);
            var mask = WhimTexCommentMask.Capture(new byte[] { 0, 255, 13, 128 }, 2, 2);
            document.AddCommentMessage(a.id, "Reply with mask", "Artist", new List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(mask) });
            string initial = JsonUtility.ToJson(document);
            var selected = (HashSet<string>)Get(window, "selectedCommentIds");
            var viewport = Get(window, "canvasViewport");
            Type coordinateType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.CanvasCoordinates", true);
            var view = new Rect(0, 0, 1000, 1000);
            object coordinates = Create(coordinateType, viewport, view, view, new Vector2(8192, 8192));
            void Box(Rect rect, string[] keep = null) => Call(window, "SelectCanvasComments", rect, coordinates, keep);
            void Selection(params string[] expected) => t.True(selected.SetEquals(expected), "Selected IDs match visible candidates");
            Box(new Rect(70, 170, 270, 150)); Selection(a.id, b.id);
            Box(new Rect(70, 170, 270, 150), new[] { c.id }); Selection(a.id, b.id, c.id);
            Box(new Rect(450, 450, 20, 20)); Selection();
            t.Equal(initial, JsonUtility.ToJson(document), "Selection never changes document data");
            // The bottom-left UV convention survives rotated and panned/zoomed views.
            Call(viewport, "SetRotation", 37f, false);
            coordinates = Create(coordinateType, viewport, view, new Rect(-120, 80, 1400, 700), new Vector2(8192, 8192));
            Vector2 point = (Vector2)Call(coordinates, "UvToView", b.anchor);
            Box(new Rect(point - new Vector2(10, 30), new Vector2(40, 40))); Selection(b.id);
            Call(viewport, "SetRotation", 0f, false);
            coordinates = Create(coordinateType, viewport, view, view, new Vector2(8192, 8192));

            var clusters = (IList)Get(window, "commentClusters");
            var clustered = (HashSet<string>)Get(window, "clusteredCommentIds");
            object cluster = Create(typeof(WhimTexWindow).GetNestedType("CommentCluster", F));
            ((List<WhimTexComment>)Get(cluster, "items")).AddRange(new[] { a, c, hidden });
            Set(cluster, "point", new Vector2(200, 200)); clusters.Add(cluster);
            clustered.UnionWith(new[] { a.id, c.id, hidden.id });
            Box(new Rect(180, 170, 50, 50)); Selection(a.id, c.id);
            clusters.Clear(); clustered.Clear();
            Set(window, "commentSearch", "B");
            Call(window, "NormalizeCommentSelection", Call(window, "CurrentCommentQuery")); Selection();
            Box(view); Selection(b.id);
            Set(window, "commentSearch", "");

            var canvas = (VisualElement)Create(typeof(WhimTexWindow).GetNestedType("CanvasElement", F), viewport);
            Set(window, "toolkitCanvas", canvas);
            manipulator = (PointerManipulator)Create(typeof(WhimTexWindow).GetNestedType("CommentManipulator", F), window);
            canvas.AddManipulator(manipulator); Set(window, "commentManipulator", manipulator);
            ((HashSet<string>)Get(manipulator, "previousSelection")).Add(c.id);
            Set(manipulator, "pointer", 0); Set(manipulator, "document", document); Set(manipulator, "moved", true);
            Call(manipulator, "Cancel"); Selection(c.id);
            t.Equal(initial, JsonUtility.ToJson(document), "Cancel box does not create or move comments");

            bool Key(VisualElement target, KeyCode key)
            {
                using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
                evt.target = target;
                return (bool)Call(window, "HandleCommentSelectionKey", evt);
            }
            t.True(!Key(new TextField(), KeyCode.Delete), "Delete in text does not delete comments or open a dialog");
            t.True(!Key(new Button(), KeyCode.Delete), "Delete outside canvas is not intercepted");
            t.True(Key(canvas, KeyCode.Escape), "Escape clears selection"); Selection();
            t.True(Key(canvas, KeyCode.Delete), "Empty comment selection consumes canvas Delete without a dialog");
            Box(view); Selection(a.id, b.id, c.id);
            EnumField(window, "canvasTool", "Brush");
            Call(window, "NormalizeCommentSelection", Call(window, "CurrentCommentQuery")); Selection();
            EnumField(window, "canvasTool", "Comment");

            Call(window, "SetCanvasCommentSelection", (object)new[] { a.id, b.id });
            Set(manipulator, "document", document); Set(manipulator, "pointer", 0);
            manipulator.GetType().GetProperty("MovingId", F).SetValue(manipulator, a.id);
            Set(manipulator, "original", a.anchor); Set(manipulator, "moved", true);
            Call(manipulator, "BeginMove", a.id); Selection(a.id, b.id);
            var positions = new Dictionary<string, Vector2>((Dictionary<string, Vector2>)Get(manipulator, "movingAnchors"));
            t.Equal(2, positions.Count, "Grabbing a selected pin snapshots the whole selection");
            var delta = new Vector2(-.25f, .15f);
            manipulator.GetType().GetProperty("PendingAnchor", F).SetValue(manipulator, a.anchor + delta);
            Vector2 Preview(WhimTexComment item) => (Vector2)Call(manipulator, "PreviewAnchor", item);
            t.True(Vector2.Distance(a.anchor + delta, Preview(a)) < 1e-6f, "Grabbed pin preview follows pointer delta");
            t.True(Vector2.Distance(b.anchor + delta, Preview(b)) < 1e-6f, "Other selected pin preview follows identical delta");
            t.True(Vector2.Distance(b.anchor - a.anchor, Preview(b) - Preview(a)) < 1e-6f, "Group preview preserves spacing including outside canvas");
            t.Equal(c.anchor, Preview(c), "Unselected pin does not preview movement");
            t.Equal(hidden.anchor, Preview(hidden), "Filtered pin stays put");
            t.True((bool)Call(manipulator, "IsMoving", b.id), "All moving pins are excluded from clustering during drag");
            t.Equal(initial, JsonUtility.ToJson(document), "Group preview does not mutate the document");
            Call(manipulator, "Cancel"); Selection(a.id, b.id);
            t.Equal(b.anchor, Preview(b), "Cancel removes preview for the entire group");
            t.Equal(0, ((Dictionary<string, Vector2>)Get(manipulator, "movingAnchors")).Count, "Cancel releases the group snapshot");
            t.Equal(initial, JsonUtility.ToJson(document), "Cancel preserves every original anchor");
            Call(window, "MoveCanvasComments", document, positions, delta);
            foreach (var entry in positions)
                t.Equal(entry.Value + delta, document.FindComment(entry.Key).anchor, "Group move committed");
            t.Equal(c.anchor, document.FindComment(c.id).anchor, "Other pin unchanged by commit");
            Undo.PerformUndo();
            foreach (var entry in positions) t.Equal(entry.Value, document.FindComment(entry.Key).anchor, "One Undo restores the group");
            Undo.PerformRedo();
            foreach (var entry in positions) t.Equal(entry.Value + delta, document.FindComment(entry.Key).anchor, "One Redo moves the group");
            Undo.PerformUndo();
            string beforeMissing = JsonUtility.ToJson(document);
            var missing = new Dictionary<string, Vector2>(positions) { ["missing"] = Vector2.zero };
            Call(window, "MoveCanvasComments", document, missing, delta);
            t.Equal(beforeMissing, JsonUtility.ToJson(document), "Missing member cancels the whole move without partial changes");
            Set(manipulator, "document", document);
            Call(manipulator, "BeginMove", c.id); Selection(c.id);
            t.Equal(1, ((Dictionary<string, Vector2>)Get(manipulator, "movingAnchors")).Count, "Grabbing an unselected pin starts a single-pin move");
            Call(manipulator, "Cancel");
            Undo.ClearUndo(document);

            // Drafts of deleted discussions are discarded, unrelated drafts survive.
            object draftA = Call(window, "RestoreCommentDraft", a.id, a.anchor);
            object draftC = Call(window, "RestoreCommentDraft", c.id, c.anchor);
            Set(draftA, "text", "Draft A"); Set(draftC, "text", "Draft C");
            Set(draftA, "open", false); Set(draftC, "open", false);
            string aJson = JsonUtility.ToJson(a), bJson = JsonUtility.ToJson(b);
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(document, "Test parameter before deletion");
            document.width = 4096; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Call(window, "DeleteCanvasComments", document, new[] { a.id, b.id, a.id, "missing" });
            t.Equal(2, document.comments.Count, "Only two chosen discussions deleted, duplicate IDs ignored");
            t.True(document.FindComment(c.id) != null && document.FindComment(hidden.id) != null, "Other and filtered discussions retained");
            t.True(Call(window, "FindCommentEditorState", document, a.id) == null, "Deleted draft discarded");
            t.Equal("Draft C", (string)Get(Call(window, "FindCommentEditorState", document, c.id), "text"), "Other draft retained");
            Undo.PerformUndo();
            t.Equal(4, document.comments.Count, "One Undo restores both discussions");
            t.Equal(aJson, JsonUtility.ToJson(document.FindComment(a.id)), "Undo preserves replies, mask data, timestamps and IDs exactly");
            t.Equal(bJson, JsonUtility.ToJson(document.FindComment(b.id)), "Second discussion restored exactly");
            t.Equal(4096, document.width, "Adjacent parameter edit is not skipped");
            Undo.PerformRedo(); t.Equal(2, document.comments.Count, "One Redo deletes both discussions");
            Undo.PerformUndo(); Undo.PerformUndo(); t.Equal(8192, document.width, "Following Undo reaches the parameter edit");

            other = ScriptableObject.CreateInstance<WhimTexDocument>();
            var otherComment = other.AddComment(Vector2.one * .5f, "Other document", "Artist");
            Call(window, "DeleteCanvasComments", other, new[] { otherComment.id });
            t.Equal(1, other.comments.Count, "Stale document deletion is rejected");
            Call(window, "MoveCanvasComments", other, new Dictionary<string, Vector2> { [otherComment.id] = otherComment.anchor }, Vector2.one);
            t.Equal(Vector2.one * .5f, otherComment.anchor, "Stale document movement is rejected");
            Box(view); t.True(selected.Count > 0, "Selection set for document-switch check");
            Set(window, "activeDocument", other);
            Call(window, "NormalizeCommentSelection", Call(window, "CurrentCommentQuery")); Selection();
            Set(window, "activeDocument", document);
        }
        finally
        {
            if (manipulator != null) manipulator.target = null;
            if (window != null) { if (document != null) Set(window, "activeDocument", document); Object.DestroyImmediate(window); }
            if (document != null) { Undo.ClearUndo(document); Object.DestroyImmediate(document); }
            if (other != null) Object.DestroyImmediate(other);
        }
    });
}
