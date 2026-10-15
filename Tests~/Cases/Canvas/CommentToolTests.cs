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

public static class CommentToolTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Read(object value, string field) => value.GetType().GetField(field, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(Flags)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point) };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Key(VisualElement target, KeyCode key, EventModifiers modifiers = EventModifiers.None)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = modifiers });
        e.target = target; target.SendEvent(e);
    }
    static void Click(Button button)
    {
        using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e);
    }
    sealed class CaptureProbe : ImmediateModeElement
    {
        internal WhimTexWindow owner;
        internal string filename = "comment-tool-preview.png";
        internal readonly TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D texture = null;
            try
            {
                var target = RenderTexture.active;
                int w = target != null ? target.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int h = target != null ? target.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); texture.Apply(false);
                Directory.CreateDirectory("Temp/WhimTex");
                File.WriteAllBytes("Temp/WhimTex/" + filename, texture.EncodeToPNG()); done.TrySetResult(true);
            }
            catch (Exception error) { done.TrySetException(error); }
            finally { if (texture != null) Object.DestroyImmediate(texture); }
        }
    }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    static async Task Execute(TestContext t, CancellationToken token)
    {
        string[] keys = { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings" };
        var preferences = keys.Select(key => (key, exists: EditorPrefs.HasKey(key), value: EditorPrefs.GetString(key))).ToArray();
        WhimTexWindow window = null; WhimTexDocument document = null;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Read(window, "activeDocument");
            document.width = 320; document.height = 240; document.layers.Clear();
            document.AddComment(new Vector2(.2f, .8f), "Check the edge here.");
            window.ShowUtility(); window.position = new Rect(80, 80, 1280, 800); window.CreateGUI();
            var root = window.rootVisualElement;
            for (int i = 0; i < 4; i++) await AsyncD.Tick(root, token);
            var canvas = (VisualElement)Read(window, "toolkitCanvas");
            Key(canvas, KeyCode.N); await AsyncD.Tick(root, token);
            t.Equal("Comment", Read(window, "canvasTool").ToString(), "N selects Comment");
            t.True(root.Q<Button>("commentTool").ClassListContains("whimtex-tool-button--selected"), "Toolbar selects Comment");
            var crop = root.Q<Button>("cropTool"); var tool = root.Q<Button>("commentTool");
            t.Equal(crop.parent.IndexOf(crop) + 1, tool.parent.IndexOf(tool), "Comment is directly below Crop");
            t.True(root.Q("commentPins") != null, "Canvas owns its comment overlay");
            bool FilledIcon() => (bool)Read(Read(window, "commentToolIcon"), "hasComments");
            t.True(FilledIcon(), "Opening a document with comments fills the tool icon");
            var drawer = root.Q("commentsDrawer");
            var list = root.Q<ListView>("commentsList");
            Button Row(string itemId) => list.Query<Button>(className: "whimtex-comment-list-row").ToList().First(b => (string)b.userData == itemId);
            t.Equal(DisplayStyle.Flex, drawer.resolvedStyle.display, "Comment opens its side panel by default");
            t.True(drawer.ClassListContains("whimtex-post-fx-drawer"), "Comments reuse the shared drawer layout");
            t.Equal(1, list.itemsSource.Count, "The list shows the existing comment");
            var scroll = list.Q<ScrollView>();
            t.Equal(DisplayStyle.None, scroll.verticalScroller.resolvedStyle.display, "A fitting list has no scrollbar");
            t.Near(list.fixedItemHeight, scroll.contentViewport.resolvedStyle.height, 1, "A short list fits its content height");
            Click(root.Q<Button>("commentsTab")); await AsyncD.Tick(root, token);
            t.Equal(DisplayStyle.None, drawer.resolvedStyle.display, "The edge tab collapses the panel");
            Click(root.Q<Button>("commentsListButton")); await AsyncD.Tick(root, token);
            t.Equal(DisplayStyle.Flex, drawer.resolvedStyle.display, "The header button opens the panel");
            Vector2 View(Vector2 uv) => (Vector2)Call(window, "CommentViewPoint", uv);
            void Tap(Vector2 uv) { var p = View(uv); Pointer(canvas, EventType.MouseDown, p); Pointer(canvas, EventType.MouseUp, p); }
            TextField Input() => root.Q<TextField>("commentText");
            var initial = new Vector2(.4f, .55f);
            Tap(initial); await AsyncD.Tick(root, token);
            t.True(Input() != null, "A click opens the editor on an empty canvas");
            t.Equal(1, document.comments.Count, "An unposted draft does not create stored data");
            var chosen = new Color(.85f, .2f, .12f);
            root.Q<WhimTexColorField>("commentColor").value = chosen;
            t.Equal(1, document.comments.Count, "Draft color does not create stored data");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Click releases capture before keyboard editing");
            Input().value = "Review this area\nSecond line";
            Key(Input(), KeyCode.B); t.Equal("Comment", Read(window, "canvasTool").ToString(), "Typing does not activate another tool");
            Tap(new Vector2(.6f, .7f));
            t.Equal("Review this area\nSecond line", Input().value, "Clicking elsewhere preserves a nonempty draft");
            Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal(2, document.comments.Count, "Ctrl+Enter posts one thread");
            string id = document.comments[1].id;
            t.Equal(chosen, document.FindComment(id).color, "Post saves the draft marker color");
            t.Near(initial.x, document.FindComment(id).anchor.x, 1e-5, "Draft retains its initial X");
            t.Near(initial.y, document.FindComment(id).anchor.y, 1e-5, "Draft retains its initial Y");
            Undo.PerformUndo(); t.Equal(1, document.comments.Count, "Owned post Undo removes the whole thread");
            Undo.PerformRedo(); t.Equal(2, document.comments.Count, "Owned post Redo restores the whole thread");
            Tap(initial); await AsyncD.Tick(root, token);
            var changedColor = new Color(.95f, .75f, .2f);
            root.Q<WhimTexColorField>("commentColor").value = changedColor;
            t.Equal(changedColor, document.FindComment(id).color, "Header changes an existing marker color");
            Undo.PerformUndo(); t.Equal(chosen, document.FindComment(id).color, "Color change has owned Undo");
            Undo.PerformRedo(); t.Equal(changedColor, document.FindComment(id).color, "Color change has owned Redo");
            await AsyncD.Tick(root, token);
            t.Equal(changedColor, Row(id).resolvedStyle.borderLeftColor, "List stripe follows color changes and Redo");
            t.Near(5, Row(id).resolvedStyle.borderLeftWidth, .01, "List stripe is five pixels wide");
            Input().value = "A reply"; Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal(2, document.FindComment(id).messages.Count, "Posting in a thread adds a reply");
            Click(root.Q("commentEditor").Query<Button>().ToList().First(b => b.text == "Edit"));
            Input().value = "Updated first message"; Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal("Updated first message", document.FindComment(id).messages[0].text, "Edit updates the chosen message");
            t.Equal(2, document.FindComment(id).messages.Count, "Edit does not add a reply");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            var card = root.Q("commentEditor");
            t.True(card.worldBound.xMin >= root.worldBound.xMin && card.worldBound.xMax <= root.worldBound.xMax + 1, "Card fits the window horizontally");
            t.True(card.worldBound.yMin >= root.worldBound.yMin && card.worldBound.yMax <= root.worldBound.yMax + 1, "Card fits the window vertically");
            t.True(!card.worldBound.Overlaps(drawer.worldBound), "The card does not cover its list at normal window size");
            t.Equal("Comments (2)", root.Q<Label>("commentsTitle").text, "Panel title counts every thread");
            t.Equal("Updated first message", Row(id).Q<Label>(className: "whimtex-comment-list-preview").text, "List preview follows message edits");
            var probe = new CaptureProbe { owner = window, pickingMode = PickingMode.Ignore };
            probe.style.position = Position.Absolute; probe.style.width = probe.style.height = 1;
            root.Add(probe);
            try { using (token.Register(() => probe.done.TrySetCanceled())) { window.Repaint(); await probe.done.Task; } }
            finally { probe.RemoveFromHierarchy(); }
            Click((Button)Read(window, "commentResolve")); t.True(document.FindComment(id).resolved, "Resolve retains the thread");
            t.True(!((System.Collections.IDictionary)Read(window, "commentPins")).Contains(id), "Resolved pin is hidden by default");
            await AsyncD.Tick(root, token);
            t.Equal(2, list.itemsSource.Count, "Resolved threads remain in the list with hidden pins");
            t.True(Row(id).Q<Label>(className: "whimtex-comment-list-heading").text.Contains("Resolved"), "List marks resolved threads");
            Click(Row(id)); await AsyncD.Tick(root, token);
            t.True(root.Q<Toggle>("showResolvedComments").value, "Opening a resolved row reveals its pin");
            t.Equal(id, (string)Read(window, "commentId"), "Clicking a row opens the corresponding thread");
            t.True(Row(id).ClassListContains("whimtex-comment-list-row--selected"), "The opened row is highlighted");
            t.Equal("Reopen", ((Button)Read(window, "commentResolve")).text, "Resolved thread offers Reopen");
            Click((Button)Read(window, "commentResolve")); t.True(!document.FindComment(id).resolved, "Reopen makes the thread active");
            Call(window, "SetCanvasRotation", 31f); Call(window, "SetCanvasZoomPercent", 100f);
            await AsyncD.Tick(root, token);
            var next = new Vector2(.55f, .4f);
            var down = View(initial); var up = View(next);
            Pointer(canvas, EventType.MouseDown, down); Pointer(canvas, EventType.MouseDrag, up);
            t.Near(initial.x, document.FindComment(id).anchor.x, 1e-5, "Dragging is only a preview until release");
            Pointer(canvas, EventType.MouseUp, up);
            t.Near(next.x, document.FindComment(id).anchor.x, 1e-5, "Rotated-view drag moves canvas X");
            t.Near(next.y, document.FindComment(id).anchor.y, 1e-5, "Rotated-view drag moves canvas Y");
            Undo.PerformUndo(); t.Near(initial.x, document.FindComment(id).anchor.x, 1e-5, "Move has one owned Undo");
            Undo.PerformRedo(); t.Near(next.x, document.FindComment(id).anchor.x, 1e-5, "Move Redo restores position");
            Key(canvas, KeyCode.Escape);
            down = View(next); up = View(new Vector2(.7f, .3f));
            Pointer(canvas, EventType.MouseDown, down); Pointer(canvas, EventType.MouseDrag, up); Key(canvas, KeyCode.Escape); Pointer(canvas, EventType.MouseUp, up);
            t.Near(next.x, document.FindComment(id).anchor.x, 1e-5, "Escape cancels a move");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Escape releases pointer capture");
            Pointer(canvas, EventType.MouseDown, down); Pointer(canvas, EventType.MouseDrag, up); canvas.ReleasePointer(PointerId.mousePointerId);
            await AsyncD.Tick(root, token); Pointer(canvas, EventType.MouseUp, up);
            t.Near(next.x, document.FindComment(id).anchor.x, 1e-5, "Capture loss cancels a move");
            Tap(next); Click((Button)Read(window, "commentDelete"));
            t.True(document.FindComment(id) == null, "Delete removes the thread and replies");
            Undo.PerformUndo(); t.Equal(2, document.FindComment(id).messages.Count, "Delete Undo restores all replies");
            Key(canvas, KeyCode.B); t.True(root.Q("commentEditor") == null, "Changing tools closes the editor");
            t.True(root.Q("commentPins").ClassListContains("whimtex-hidden"), "Pins hide outside Comment tool");
            t.True(FilledIcon(), "The filled icon remains visible outside the Comment tool");
            await AsyncD.Tick(root, token);
            t.Equal(DisplayStyle.None, drawer.resolvedStyle.display, "Comment panel hides with other tools");
            Key(canvas, KeyCode.N);
            for (int i = 0; i < 38; i++)
                document.AddComment(new Vector2(.1f, .1f), "Review note " + (i + 3) + ": a longer comment preview wraps within the list row.", color: i % 2 == 0 ? chosen : changedColor);
            Call(window, "RefreshCanvasComments");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.Equal(40, list.itemsSource.Count, "The list includes all threads in a long document");
            t.True(scroll.verticalScroller.highValue > 0, "Long lists have a working vertical scrollbar");
            t.Equal(DisplayStyle.Flex, scroll.verticalScroller.resolvedStyle.display, "Overflow reveals the scrollbar");
            t.Near(root.Q("postFxOverlay").worldBound.height, drawer.worldBound.height, 1, "A long list uses the full available height");
            t.True(list.Query<Button>(className: "whimtex-comment-list-row").ToList().Count < 20, "Long lists virtualize their rows");
            list.ScrollToItem(39);
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            string lastId = document.comments[39].id;
            Click(Row(lastId)); await AsyncD.Tick(root, token);
            t.Equal(lastId, (string)Read(window, "commentId"), "A recycled row opens the correct thread after scrolling");
            float offset = scroll.scrollOffset.y;
            root.Q<WhimTexColorField>("commentColor").value = Color.cyan;
            await AsyncD.Tick(root, token);
            t.Near(offset, scroll.scrollOffset.y, 1, "Refreshing a color preserves the scroll position");
            t.Equal(Color.cyan, Row(lastId).resolvedStyle.borderLeftColor, "Recycled rows receive the current color");
            var listProbe = new CaptureProbe { owner = window, filename = "comment-list-preview.png", pickingMode = PickingMode.Ignore };
            listProbe.style.position = Position.Absolute; listProbe.style.width = listProbe.style.height = 1;
            root.Add(listProbe);
            try { using (token.Register(() => listProbe.done.TrySetCanceled())) { window.Repaint(); await listProbe.done.Task; } }
            finally { listProbe.RemoveFromHierarchy(); }
            Key(canvas, KeyCode.Escape);
            window.position = new Rect(80, 80, 760, 420);
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            var narrowProbe = new CaptureProbe { owner = window, filename = "comment-list-narrow-preview.png", pickingMode = PickingMode.Ignore };
            narrowProbe.style.position = Position.Absolute; narrowProbe.style.width = narrowProbe.style.height = 1;
            root.Add(narrowProbe);
            try { using (token.Register(() => narrowProbe.done.TrySetCanceled())) { window.Repaint(); await narrowProbe.done.Task; } }
            finally { narrowProbe.RemoveFromHierarchy(); }
            t.True(drawer.worldBound.yMax <= canvas.worldBound.yMax + 1, "The panel fits a short window: drawer=" + drawer.worldBound + ", canvas=" + canvas.worldBound);
            t.True(drawer.worldBound.xMax <= canvas.worldBound.xMax + 1, "The panel fits a narrow canvas pane");
            t.True(scroll.contentViewport.resolvedStyle.height > 20, "The short panel retains a usable scrolling viewport");
            t.Near(root.Q("postFxOverlay").worldBound.height, drawer.worldBound.height, 1, "An overflowing list fills a short window too");
            document.comments.RemoveRange(1, document.comments.Count - 1); Call(window, "RefreshCanvasComments");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.Equal(DisplayStyle.None, scroll.verticalScroller.resolvedStyle.display, "Removing overflow hides the scrollbar again");
            t.Near(list.fixedItemHeight, scroll.contentViewport.resolvedStyle.height, 1, "The panel shrinks after comments are removed");
            document.comments.Clear(); Call(window, "RefreshCanvasComments");
            await AsyncD.Tick(root, token);
            t.Equal(DisplayStyle.None, list.resolvedStyle.display, "Empty documents hide list rows");
            t.Equal(DisplayStyle.Flex, ((Label)Read(window, "commentListEmpty")).resolvedStyle.display, "Empty documents show the creation hint");
            t.True(!FilledIcon(), "Removing the last thread restores the outline icon");
            Tap(new Vector2(.5f, .5f)); Input().value = "First comment";
            t.True(!FilledIcon(), "An unposted draft keeps the outline icon");
            Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.True(FilledIcon(), "Posting the first thread fills the icon");
            string onlyId = document.comments[0].id;
            Click((Button)Read(window, "commentResolve"));
            t.True(FilledIcon(), "Resolved comments still fill the tool icon");
            await AsyncD.Tick(root, token); Click(Row(onlyId));
            Click((Button)Read(window, "commentDelete"));
            t.True(!FilledIcon(), "Deleting the last resolved thread restores the outline");
            Undo.PerformUndo(); t.True(FilledIcon(), "Undo restores the filled icon with the thread");
            Undo.PerformRedo(); t.True(!FilledIcon(), "Redo restores the outline icon after deletion");
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null) { window.DiscardChanges(); if (window.rootVisualElement.panel != null) window.Close(); } },
                () => { if (window != null) Object.DestroyImmediate(window); },
                () => { if (document != null) Object.DestroyImmediate(document); },
                () => { foreach (var p in preferences) { if (p.exists) EditorPrefs.SetString(p.key, p.value); else EditorPrefs.DeleteKey(p.key); } });
        }
    }
}
