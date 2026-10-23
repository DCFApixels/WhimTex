using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class CommentNavigationTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target) ?? target.GetType().GetProperty(name, Flags).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static ApiJson Request(string op, params object[] values)
    { var request = new ApiJson { ["apiVersion"] = 1, ["op"] = op }; for (int i = 0; i < values.Length; i += 2) request[(string)values[i]] = ApiJson.FromObject(values[i + 1]); return request; }
    static void Click(Button button) { using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e); }
    static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point) };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    sealed class Capture : ImmediateModeElement
    {
        internal WhimTexWindow window;
        internal TaskCompletionSource<bool> done = new();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D image = null;
            try
            {
                var target = RenderTexture.active;
                image = new Texture2D(target != null ? target.width : (int)(window.position.width * EditorGUIUtility.pixelsPerPoint),
                    target != null ? target.height : (int)(window.position.height * EditorGUIUtility.pixelsPerPoint), TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/comment-navigation-preview-" + Guid.NewGuid().ToString("N") + ".png", image.EncodeToPNG()); done.SetResult(true);
            }
            catch (Exception error) { done.SetException(error); }
            finally { if (image != null) Object.DestroyImmediate(image); }
        }
    }
    static async Task Execute(TestContext t, CancellationToken token)
    {
        WhimTexWindow window = null; WhimTexDocument document = null;
        string[] keys = { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings" };
        var preferences = keys.Select(key => (key, exists: EditorPrefs.HasKey(key), value: EditorPrefs.GetString(key))).ToArray();
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); document = (WhimTexDocument)Read(window, "AgentDocument");
            document.width = 320; document.height = 240; document.layers.Clear();
            var first = document.AddComment(new Vector2(.5f, .5f), "Original edge", "Zoe");
            var second = document.AddComment(new Vector2(.52f, .5f), "Nearby detail", "Alice");
            var third = document.AddComment(new Vector2(.8f, .8f), "Separate detail", "Bob");
            var day = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Local);
            first.messages[0].createdUtcTicks = day.ToUniversalTime().Ticks;
            second.messages[0].createdUtcTicks = day.AddDays(1).ToUniversalTime().Ticks;
            third.messages[0].createdUtcTicks = day.AddDays(2).ToUniversalTime().Ticks;
            var reply = document.AddCommentMessage(first.id, "Latest reply", "Alice"); reply.createdUtcTicks = day.AddDays(3).ToUniversalTime().Ticks;
            window.ShowUtility(); window.position = new Rect(60, 60, 1100, 760); window.CreateGUI();
            var root = window.rootVisualElement;
            Call(window, "SetCanvasTool", Enum.Parse(Read(window, "canvasTool").GetType(), "Comment"));
            for (int i = 0; i < 4; i++) await AsyncD.Tick(root, token);
            var list = root.Q<ListView>("commentsList");
            string[] ListIds() => list.itemsSource.Cast<WhimTexComment>().Select(c => c.id).ToArray();
            string before = JsonUtility.ToJson(document);
            root.Q<Foldout>("commentQueryOptions").value = true;
            root.Q<DropdownField>("commentAuthor").value = "By Alice";
            t.True(ListIds().SequenceEqual(new[] { first.id, second.id }), "Author filter includes reply authors");
            t.Equal(2, ((IDictionary)Read(window, "commentPins")).Count, "Author filter applies to canvas pins too");
            root.Q<DropdownField>("commentAuthor").value = "All authors";
            root.Q<DropdownField>("commentSort").value = "Author";
            t.True(ListIds().SequenceEqual(new[] { second.id, third.id, first.id }), "Author sort uses each discussion's initial author");
            root.Q<DropdownField>("commentSort").value = "Last activity"; root.Q<Toggle>("commentDescending").value = true;
            t.True(ListIds().SequenceEqual(new[] { first.id, third.id, second.id }), "Replies affect last-activity sorting");
            root.Q<DropdownField>("commentTimeBasis").value = "Created";
            root.Q<DropdownField>("commentPeriod").value = "Custom dates";
            root.Q<TextField>("commentFrom").value = "2025-02-02"; root.Q<TextField>("commentTo").value = "2025-02-02";
            t.True(ListIds().SequenceEqual(new[] { second.id }), "An inclusive local-day filter selects the whole requested day");
            root.Q<TextField>("commentTo").value = "2025-01-01";
            t.Equal(0, list.itemsSource.Count, "An inverted range is rejected visibly rather than silently broadening the query");
            Click(root.Q<Button>("resetCommentFilters"));
            t.Equal(before, JsonUtility.ToJson(document), "Filtering and sorting do not mutate the document");
            Call(window, "SetCanvasZoomPercent", 100f); Call(window, "PositionCanvasComments");
            t.Equal(1, ((IList)Read(window, "commentClusters")).Count, "Overlapping screen-space pins form one cluster");
            Call(window, "SetCanvasZoomPercent", 800f); Call(window, "PositionCanvasComments");
            t.Equal(0, ((IList)Read(window, "commentClusters")).Count, "Zooming in separates nearby anchors");
            second.anchor = first.anchor; Call(window, "RefreshCanvasComments");
            t.Equal(1, ((IList)Read(window, "commentClusters")).Count, "Exactly overlapping anchors remain selectable at high zoom");
            var cluster = ((IList)Read(window, "commentClusters"))[0];
            EditorWindow popup = null;
            try
            {
                t.True((bool)Call(window, "TryOpenCommentCluster", Read(cluster, "point")), "Cluster opens its chooser");
                popup = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => ReferenceEquals(w.rootVisualElement.userData, window));
                t.True(popup != null, "Chooser is a native popup");
                var choices = popup.rootVisualElement.Query<Button>("commentClusterChoice").ToList();
                t.Equal(2, choices.Count, "Chooser lists every clustered discussion");
                Click(choices.Single(c => (string)c.userData == second.id));
            }
            finally
            {
                // Show can throw after creating its native object but before returning.
                if (popup == null) popup = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => ReferenceEquals(w.rootVisualElement.userData, window));
                if (popup != null)
                {
                    if (float.IsFinite(popup.position.y)) popup.Close();
                    else Object.DestroyImmediate(popup);
                }
            }
            t.Equal(second.id, (string)Read(Read(Read(window, "activeCommentEditor"), "state"), "id"), "Choosing a member opens that exact discussion");
            string session = (string)Read(window, "AgentSessionId");
            ApiJson Query(ApiJson request, string error = null)
            {
                var result = ApiJson.Parse(WhimTexApi.CommentsJson(request.ToString()));
                t.True(error == null ? (bool)result["success"] : !(bool)result["success"] && (string)result["errorCode"] == error, result.ToString()); return result;
            }
            var queried = Query(Request("list", "assistantSessionId", session, "filter", new { author = "Alice" }, "sort", "Activity", "descending", true));
            t.Equal(2, (int)queried["matchedCount"], "API applies the same participant filter");
            t.Equal(first.id, (string)queried["comments"][0]["id"], "API sorts by activity");
            var range = Query(Request("list", "assistantSessionId", session, "filter", new { time = "Created", fromUtc = day.AddDays(1).ToUniversalTime().ToString("O"), toUtc = day.AddDays(2).ToUniversalTime().ToString("O") }));
            t.Equal(second.id, (string)range["comments"][0]["id"], "API respects inclusive start and exclusive end");
            t.Equal(1, (int)range["matchedCount"], "Exclusive end excludes the next discussion");
            Query(Request("list", "assistantSessionId", session, "filter", new { fromUtc = "yesterday" }), "invalid_request");
            Query(Request("list", "assistantSessionId", session, "sort", "unknown"), "invalid_request");
            string link = (string)queried["comments"][0]["link"];
            t.True(link.StartsWith("whimtex://comment/session/") && !(bool)queried["comments"][0]["persistentLink"], "Unsaved discussions expose explicitly temporary links");
            var search = root.Q<ToolbarSearchField>("commentSearch");
            string beforeSearch = JsonUtility.ToJson(document);
            search.value = "ePl";
            t.True(ListIds().SequenceEqual(new[] { first.id }), "Text search finds a case-insensitive substring in replies");
            search.value = "LIC";
            t.True(ListIds().Length == 2 && ListIds().Contains(first.id) && ListIds().Contains(second.id), "Text search finds author substrings");
            foreach (string value in new[] { link, " \t\u00a0" + link + "\u00a0\t " })
            {
                search.value = value;
                t.True(ListIds().SequenceEqual(new[] { first.id }), "Search matches the full link, ignoring only surrounding whitespace");
                t.Equal(1, ((IDictionary)Read(window, "commentPins")).Count, "Exact link search filters canvas markers too");
                var found = Query(Request("list", "assistantSessionId", session, "filter", new { search = value }));
                t.Equal(1, (int)found["matchedCount"], "API uses the same exact link search");
                t.Equal(first.id, (string)found["comments"][0]["id"], "API link search returns the addressed discussion");
            }
            foreach (string value in new[] { link.Substring(0, link.Length - 1), link.Replace("whimtex", "WHIMTEX"),
                link.Replace("/comment/", "/ comment/"), link + "/", "prefix " + link,
                link.Replace(session, new string('0', 32)), link.Replace("/comment/", "/%63omment/") })
            {
                search.value = value;
                t.Equal(0, ListIds().Length, "Partial, altered, normalized or other-document links do not match: " + value);
                var found = Query(Request("list", "assistantSessionId", session, "filter", new { search = value }));
                t.Equal(0, (int)found["matchedCount"], "API does not normalize or partially match links");
            }
            search.value = link;
            root.Q<DropdownField>("commentAuthor").value = "By Bob";
            t.Equal(0, ListIds().Length, "Link search preserves other filters");
            root.Q<DropdownField>("commentAuthor").value = "All authors";
            search.value = " \t ";
            t.Equal(3, ListIds().Length, "Whitespace-only search restores all matching discussions");
            search.value = "";
            t.Equal(beforeSearch, JsonUtility.ToJson(document), "Searching by text or link does not change document data");
            root.Q<DropdownField>("commentAuthor").value = "By Bob";
            Query(Request("open", "link", link));
            t.Equal(first.id, (string)Read(Read(Read(window, "activeCommentEditor"), "state"), "id"), "Deep link selects the exact discussion despite filters");
            t.Equal("All authors", root.Q<DropdownField>("commentAuthor").value, "Link navigation clears excluding filters");
            Query(Request("open", "link", link + "?unexpected=1"), "comment_link_unavailable");
            document.RemoveComment(first.id); Query(Request("open", "link", link), "comment_link_unavailable");
            Call(window, "DismissAllCanvasComments");
            document.AddComment(second.anchor, "Another nearby review", "Reviewer"); Call(window, "RefreshCanvasComments");
            Call(window, "SetCanvasZoomPercent", 100f);
            for (int i = 0; i < 4; i++) await AsyncD.Tick(root, token);
            var canvas = (VisualElement)Read(window, "toolkitCanvas");
            Vector2 View(Vector2 anchor) => (Vector2)Call(window, "CommentViewPoint", anchor);
            void Drag(Vector2 from, Vector2 to)
            {
                Pointer(canvas, EventType.MouseDown, from); Pointer(canvas, EventType.MouseDrag, to);
                Pointer(canvas, EventType.MouseUp, to);
            }
            Vector2 initialAnchor = document.FindComment(third.id).anchor;
            Vector2 start = View(initialAnchor), end = start + new Vector2(28, 18);
            Vector2 nextAnchor = (Vector2)Call(window, "CommentAnchor", end);
            Drag(start, end);
            t.True((document.FindComment(third.id).anchor - nextAnchor).sqrMagnitude < 1e-8f, "Dragging a closed marker commits its position");
            t.Equal(0, root.Query("commentEditor").ToList().Count, "Dragging a closed marker does not open any card");
            Undo.PerformUndo();
            t.True((document.FindComment(third.id).anchor - initialAnchor).sqrMagnitude < 1e-8f, "Marker movement still supports Undo");
            Undo.PerformRedo();
            t.True((document.FindComment(third.id).anchor - nextAnchor).sqrMagnitude < 1e-8f, "Marker movement still supports Redo");
            t.Equal(0, root.Query("commentEditor").ToList().Count, "Undo and Redo do not open the moved discussion");
            start = View(nextAnchor);
            Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseDrag, start + new Vector2(20, 0));
            Pointer(canvas, EventType.MouseDrag, start); Pointer(canvas, EventType.MouseUp, start);
            t.Equal(0, root.Query("commentEditor").ToList().Count, "A drag returning to its start is not treated as a click");
            Drag(start, start + new Vector2(2, 1));
            var card = root.Q("commentEditor");
            t.True(card != null && root.Query("commentEditor").ToList().Count == 1, "A click with small pointer jitter opens the discussion");
            t.True((document.FindComment(third.id).anchor - nextAnchor).sqrMagnitude < 1e-8f, "Click jitter does not move the marker");
            card.Q<TextField>("commentText").value = "Retained reply draft";
            Drag(start, start + new Vector2(-15, 10));
            t.True(ReferenceEquals(card, root.Q("commentEditor")), "Dragging an open marker retains the existing card");
            t.Equal("Retained reply draft", card.Q<TextField>("commentText").value, "Dragging retains the card's reply draft");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            var status = card.Q<BaseField<string>>("commentStatus");
            int statusChanges = 0;
            status.RegisterValueChangedCallback(_ => statusChanges++);
            void Mouse(EventType type, Vector2 point) => window.SendEvent(new Event { type = type, button = 0, mousePosition = point });
            async Task ChoicePopup(bool keyboard, Action<EditorWindow, VisualElement> action)
            {
                try
                {
                    if (keyboard)
                    {
                        using var submit = NavigationSubmitEvent.GetPooled(); submit.target = status; status.SendEvent(submit);
                    }
                    else Mouse(EventType.MouseDown, status.worldBound.center);
                    var choices = root.Q("commentChoices");
                    t.True(choices != null && choices.panel == status.panel, "Status choices stay in the source window's panel");
                    t.Equal(!keyboard, status.HasPointerCapture(PointerId.mousePointerId), "Only a pointer opening captures the initiating field");
                    for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                    t.True(root.IndexOf(root.Q("commentChoiceOverlay")) > root.IndexOf(card), "Focus activation keeps the choices above the comment card");
                    action(window, choices);
                    await AsyncD.Tick(root, token);
                }
                finally
                {
                    if (root.Q("commentChoices") != null)
                    {
                        using var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                        escape.target = status; status.SendEvent(escape);
                    }
                }
            }
            await ChoicePopup(false, (popupWindow, popupRoot) =>
            {
                var option = popupRoot.Q<Button>("commentChoice1");
                t.True(popupRoot.panel != null && status.panel != null,
                    "Both the source field and its choice menu remain attached while selecting: popup=" + (popupRoot.panel != null) + ", field=" + (status.panel != null));
                Vector2 point = option.worldBound.center;
                popupWindow.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = point });
                t.True(option.ClassListContains("whimtex-comment-status-option--hover"), "Captured movement highlights the option while the source field holds the mouse");
                popupWindow.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
            });
            t.Equal(WhimTexCommentStatus.AwaitingReview, document.FindComment(third.id).Status, "The opening press selects on pointer release over an option, without a second press");
            t.Equal(1, statusChanges, "Hold-and-release selection changes status exactly once");
            t.True(!status.ClassListContains("whimtex-comment-choice--open"), "Selection closes the menu and clears its pressed appearance");
            t.True(!status.HasPointerCapture(PointerId.mousePointerId), "Selection releases the mouse capture");
            await ChoicePopup(false, (_, popupRoot) =>
            {
                Mouse(EventType.MouseUp, status.worldBound.center);
                t.Equal(WhimTexCommentStatus.AwaitingReview, document.FindComment(third.id).Status, "An ordinary opening click does not change the status");
                var option = popupRoot.Q<Button>("commentChoice0");
                Mouse(EventType.MouseDown, option.worldBound.center);
                Mouse(EventType.MouseUp, option.worldBound.center);
            });
            t.Equal(WhimTexCommentStatus.InProgress, document.FindComment(third.id).Status, "A separate ordinary option click still works");
            t.Equal(2, statusChanges, "Ordinary clicking also commits exactly once");
            await ChoicePopup(false, (_, popupRoot) =>
            {
                Mouse(EventType.MouseUp, new Vector2(-20, -20));
                t.Equal(2, statusChanges, "Release outside the options does not select a status");
                t.True(root.Q("commentChoices") == null, "Releasing the opening drag outside dismisses the choices");
            });
            await ChoicePopup(false, (_, popupRoot) =>
            {
                using var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                escape.target = popupRoot; popupRoot.SendEvent(escape);
                t.True(root.Q("commentChoices") == null && !status.HasPointerCapture(PointerId.mousePointerId), "Escape closes a held menu and releases capture");
                t.True(card.panel != null, "Menu Escape keeps the comment card open");
                Mouse(EventType.MouseUp, status.worldBound.center);
            });
            t.Equal(2, statusChanges, "Escape cancels without changing the discussion");
            await ChoicePopup(true, (_, popupRoot) => Click(popupRoot.Q<Button>("commentChoice1")));
            t.Equal(WhimTexCommentStatus.AwaitingReview, document.FindComment(third.id).Status, "Keyboard opening and submit remain supported");
            t.Equal(3, statusChanges, "Keyboard selection changes status once");
            t.Equal("Retained reply draft", card.Q<TextField>("commentText").value, "Status gestures preserve the current reply draft");
            var capture = new Capture { window = window, pickingMode = PickingMode.Ignore };
            capture.style.position = Position.Absolute; capture.style.width = capture.style.height = 1; root.Add(capture);
            try { using (token.Register(() => capture.done.TrySetCanceled())) { window.Repaint(); await capture.done.Task; } }
            finally { capture.RemoveFromHierarchy(); }
        }
        finally
        {
            AsyncD.CleanupOwned(() => { if (document != null) Undo.ClearUndo(document); if (window != null) { window.DiscardChanges(); window.Close(); Object.DestroyImmediate(window); } },
                () => { if (document != null) Object.DestroyImmediate(document); },
                () => { foreach (var p in preferences) if (p.exists) EditorPrefs.SetString(p.key, p.value); else EditorPrefs.DeleteKey(p.key); });
        }
    }
}
