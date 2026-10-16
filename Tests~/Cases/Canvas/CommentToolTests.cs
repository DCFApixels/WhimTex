using System;
using System.Collections.Generic;
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
    static void DoubleClick(Button button)
    {
        using var e = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = 2, mousePosition = button.worldBound.center });
        e.target = button; button.SendEvent(e);
    }
    sealed class CaptureProbe : ImmediateModeElement
    {
        internal EditorWindow owner;
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
        string[] keys = { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings", "DCFApixels.WhimTex.CommentAuthor", "DCFApixels.WhimTex.CommentAuthorColor", "DCFApixels.WhimTex.CommentAuthorId" };
        var preferences = keys.Select(key => (key, exists: EditorPrefs.HasKey(key), value: EditorPrefs.GetString(key))).ToArray();
        WhimTexWindow window = null; WhimTexDocument document = null;
        try
        {
            EditorPrefs.SetString("DCFApixels.WhimTex.CommentAuthorId", Guid.NewGuid().ToString("N"));
            EditorPrefs.DeleteKey("DCFApixels.WhimTex.CommentAuthorColor");
            EditorPrefs.SetString("DCFApixels.WhimTex.CommentAuthor", "{\"name\":\"First Artist\",\"source\":3}");
            var settingsType = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings");
            const BindingFlags settingsFlags = BindingFlags.NonPublic | BindingFlags.Static;
            void SetUserColor(Color value) => settingsType.GetProperty("CommentAuthorColor", settingsFlags).SetValue(null, value);
            void ResetUserColor() => settingsType.GetMethod("ResetCommentAuthorColor", settingsFlags).Invoke(null, null);
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Read(window, "activeDocument");
            document.width = 320; document.height = 240; document.layers.Clear();
            document.AddComment(new Vector2(.2f, .8f), "Check the edge here.", color: WhimTexComment.DefaultColor);
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
            var listHeader = drawer.Q("commentsHeader");
            var headerCloseAll = listHeader.Q<Button>("closeAllComments");
            t.True(headerCloseAll != null && root.Query<Button>("closeAllComments").ToList().Count == 1,
                "Close All appears only in the comments list header");
            t.True(headerCloseAll.worldBound.xMin >= listHeader.Q<Label>("commentsTitle").worldBound.xMax,
                "Close All is to the right of the Comments title");
            t.Near(listHeader.worldBound.center.y, headerCloseAll.worldBound.center.y, 1,
                "Close All is vertically centered in the list header");
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
            object Editor() => Read(window, "activeCommentEditor");
            object Draft() => Read(Editor(), "state");
            VisualElement Card() => Editor() == null ? null : (VisualElement)Read(Editor(), "card");
            void DismissAll()
            {
                foreach (var editor in ((System.Collections.IEnumerable)Read(window, "commentEditors")).Cast<object>().ToArray())
                    Call(window, "DismissCanvasComment", editor);
            }
            TextField Input() => Card()?.Q<TextField>("commentText");
            void EditFirst() => ((DropdownMenuAction)Card().Q<ToolbarMenu>("commentMessageMenu").menu.MenuItems()[1]).Execute();
            void ResetColor(VisualElement card)
            {
                var menu = new DropdownMenu();
                Call(window, "BuildCommentColorMenu", card.userData, menu);
                ((DropdownMenuAction)menu.MenuItems().Single(item => item is DropdownMenuAction action && action.name == "Reset to Author Color")).Execute();
            }
            VisualElement Author(string messageId) => root.Query<VisualElement>("commentMessageAuthor").ToList().Single(e => (string)e.userData == messageId);
            var initial = new Vector2(.4f, .55f);
            Tap(initial); await AsyncD.Tick(root, token);
            t.True(Input() != null, "A click opens the editor on an empty canvas");
            t.Equal(1, document.comments.Count, "An unposted draft does not create stored data");
            t.Equal(WhimTexComment.ColorFromAuthor("First Artist"), Card().Q<WhimTexColorField>("commentColor").value, "A new draft uses the user's hashed color");
            SetUserColor(Color.green);
            t.Equal(Color.green, Card().Q<WhimTexColorField>("commentColor").value, "An automatic draft follows user color changes");
            var chosen = new Color(.85f, .2f, .12f);
            Card().Q<WhimTexColorField>("commentColor").value = chosen;
            SetUserColor(Color.red);
            t.Equal(chosen, Card().Q<WhimTexColorField>("commentColor").value, "A manually colored draft ignores user color changes");
            ResetUserColor();
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
            t.True(!document.FindComment(id).useAuthorColor, "Post retains an explicit manual color");
            t.Equal("First Artist", document.FindComment(id).messages[0].author, "UI post snapshots the saved author");
            t.Near(initial.x, document.FindComment(id).anchor.x, 1e-5, "Draft retains its initial X");
            t.Near(initial.y, document.FindComment(id).anchor.y, 1e-5, "Draft retains its initial Y");
            Undo.PerformUndo(); t.Equal(1, document.comments.Count, "Owned post Undo removes the whole thread");
            Undo.PerformRedo(); t.Equal(2, document.comments.Count, "Owned post Redo restores the whole thread");
            Tap(initial); await AsyncD.Tick(root, token);
            var changedColor = new Color(.95f, .75f, .2f);
            Card().Q<WhimTexColorField>("commentColor").value = changedColor;
            t.Equal(changedColor, document.FindComment(id).color, "Header changes an existing marker color");
            Undo.PerformUndo(); t.Equal(chosen, document.FindComment(id).color, "Color change has owned Undo");
            Undo.PerformRedo(); t.Equal(changedColor, document.FindComment(id).color, "Color change has owned Redo");
            await AsyncD.Tick(root, token);
            t.Equal(changedColor, Row(id).resolvedStyle.borderLeftColor, "List stripe follows color changes and Redo");
            t.Near(5, Row(id).resolvedStyle.borderLeftWidth, .01, "List stripe is five pixels wide");
            ResetColor(Card());
            t.True(document.FindComment(id).useAuthorColor, "Header reset switches a posted comment to author color");
            SetUserColor(Color.green); await AsyncD.Tick(root, token);
            t.Equal(Color.green, document.FindComment(id).color, "Existing automatic comments follow the user color");
            t.Equal(Color.green, Row(id).resolvedStyle.borderLeftColor, "List stripe immediately follows automatic user color");
            t.Equal(Color.green, Card().Q<WhimTexColorField>("commentColor").value, "Open comment field follows automatic user color");
            t.Equal(WhimTexComment.DefaultColor, document.comments[0].color, "Old manual-blue comments keep their color");
            Card().Q<WhimTexColorField>("commentColor").value = changedColor;
            SetUserColor(Color.red);
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.Equal(changedColor, document.FindComment(id).color, "A manual override stops automatic updates");
            var firstMessage = document.FindComment(id).messages[0];
            double stripeDeadline = EditorApplication.timeSinceStartup + 3;
            while (Author(firstMessage.id).resolvedStyle.borderLeftWidth == 0 && EditorApplication.timeSinceStartup < stripeDeadline)
                await AsyncD.Tick(root, token);
            t.Equal(Color.red, Author(firstMessage.id).resolvedStyle.borderLeftColor, "Author stripe follows the user color independently of manual discussion color");
            t.Near(5, Author(firstMessage.id).resolvedStyle.borderLeftWidth, .01, "Author stripe matches the list's five-pixel width");
            t.Equal(Color.red, firstMessage.authorColor, "Author color snapshot updates even in a manually colored discussion");
            ResetUserColor();
            EditorPrefs.SetString("DCFApixels.WhimTex.CommentAuthor", "{\"name\":\"Second Artist\",\"source\":3}");
            t.Equal(DisplayStyle.None, Card().Q("commentMessageEditor").style.display.value, "Posted notes hide the message editor");
            Click(Card().Q<Button>("replyToComment"));
            t.Equal(DisplayStyle.Flex, Card().Q("commentMessageEditor").style.display.value, "Plus opens the message editor without hiding messages");
            t.Equal(DisplayStyle.None, Card().Q("replyToComment").style.display.value, "A new draft hides the add-message button");
            t.Equal("commentDraftMessage", Input().parent.parent.name, "Plus appends an editable draft row to the message list");
            t.Equal(1, document.FindComment(id).messages.Count, "An empty new row is not yet posted to the document");
            const string replyText = "A reply with enough words to wrap across several lines at different card widths. " +
                "The message describes the changes, their location, and what remains to be reviewed. " +
                "Проверка длинного сообщения: слова должны переноситься целиком, без растягивания каждой буквы на отдельную строку. " +
                "Resizing the discussion must retain readable paragraphs and ordinary spacing within each word.";
            Input().value = replyText; Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal(DisplayStyle.None, Card().Q("commentMessageEditor").style.display.value, "Post closes the cleared message editor");
            t.Equal(DisplayStyle.Flex, Card().Q("replyToComment").style.display.value, "Post restores the add-message button");
            t.Equal(2, document.FindComment(id).messages.Count, "Posting in a thread adds a reply");
            t.Equal("Second Artist", document.FindComment(id).messages[1].author, "Reply uses the current saved author");
            t.Equal(document.FindComment(id).messages[0].authorId, document.FindComment(id).messages[1].authorId, "Replies retain author identity after a nickname change");
            var replyBody = Card().Query<Label>(className: "whimtex-comment-body").ToList()
                .Single(label => (string)label.parent.userData == document.FindComment(id).messages[1].id);
            var originalBodyWidth = replyBody.style.width;
            try
            {
                foreach (float width in new[] {180f, 270f, 680f, 270f})
                {
                    replyBody.style.width = width;
                    for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                    var expected = replyBody.MeasureTextSize(replyText, replyBody.contentRect.width,
                        VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    t.Near(expected.y, replyBody.contentRect.height, 1,
                        "Justified text retains ordinary wrapping height at width " + width);
                }
            }
            finally { replyBody.style.width = originalBodyWidth; }
            string unchangedDiscussion = JsonUtility.ToJson(document.FindComment(id));
            Click(Card().Q<Button>("replyToComment"));
            t.Equal("commentDraftMessage", Input().parent.parent.name, "Plus starts a new row after posting");
            EditFirst();
            t.Equal(DisplayStyle.None, Card().Q("replyToComment").style.display.value, "Editing an existing message hides the add-message button");
            t.True(Card().Q("commentDraftMessage") == null, "Switching from an empty new message removes its draft row");
            Call(window, "EditCanvasCommentMessage", Editor(), document.FindComment(id).messages[1].id);
            t.Equal(document.FindComment(id).messages[1].id, (string)Read(Draft(), "messageId"), "An unchanged edit does not block editing another message");
            Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal(unchangedDiscussion, JsonUtility.ToJson(document.FindComment(id)), "Saving an unchanged edit preserves message times and document contents");
            EditFirst();
            t.Equal(DisplayStyle.Flex, Card().Q("commentMessageEditor").style.display.value, "Edit opens the message editor");
            var inlineInput = Input();
            var inlineRow = inlineInput.parent.parent;
            t.True(inlineRow.ClassListContains("whimtex-comment-message"), "Edit replaces the original message body in place");
            t.Equal(firstMessage.id, (string)inlineRow.userData, "The inline editor belongs to the chosen message");
            t.True(Card().Q("commentComposer") == null, "There is no separate reply input block");
            inlineInput.value = "Updated first message";
            inlineInput.Focus(); await AsyncD.Tick(root, token);
            var editFocus = root.panel.focusController.focusedElement;
            Call(window, "RefreshCommentMessages", Editor());
            t.True(ReferenceEquals(inlineInput, Input()) && ReferenceEquals(inlineRow, Input().parent.parent),
                "Refreshing history retains the active editor and its message row");
            t.True(ReferenceEquals(editFocus, root.panel.focusController.focusedElement), "Refreshing history preserves editing focus");
            EditFirst();
            t.Equal("Updated first message", Input().value, "Repeated Edit does not overwrite an unfinished edit");
            Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.Equal("Updated first message", document.FindComment(id).messages[0].text, "Edit updates the chosen message");
            t.Equal("First Artist", document.FindComment(id).messages[0].author, "Editing preserves original authorship");
            t.Equal(2, document.FindComment(id).messages.Count, "Edit does not add a reply");
            t.True(document.FindComment(id).messages[0].editedUtcTicks >= document.FindComment(id).messages[0].createdUtcTicks,
                "An edit records its time without changing authorship");
            var messageBody = Card().Q<Label>(className: "whimtex-comment-body");
            using (var doubleClick = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0,
                clickCount = 2, mousePosition = messageBody.worldBound.center }))
            { doubleClick.target = messageBody; messageBody.SendEvent(doubleClick); }
            t.Equal(firstMessage.id, (string)Read(Draft(), "messageId"), "Double-click edits the clicked message");
            Input().value = "Cancelled inline change";
            Click(Card().Q<Button>("cancelCommentDraft"));
            t.Equal("<align=justified>Updated first message</align>", Card().Q<Label>(className: "whimtex-comment-body").text,
                "Cancel restores the posted message with justified alignment");
            t.Equal(DisplayStyle.Flex, Card().Q("replyToComment").style.display.value, "Cancel leaves the add-message button available");
            t.Equal(DisplayStyle.None, Card().Q("commentMessageEditor").style.display.value, "Cancel closes the message editor");
            string replyId = document.FindComment(id).messages[1].id;
            Call(window, "DeleteCanvasCommentReply", Editor(), replyId);
            t.Equal(1, document.FindComment(id).messages.Count, "Individual reply deletion preserves the initial message");
            Undo.PerformUndo();
            t.Equal(replyId, document.FindComment(id).messages[1].id, "Reply deletion has owned Undo");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.Equal(WhimTexComment.ColorFromAuthor("Second Artist"), Author(document.FindComment(id).messages[0].id).resolvedStyle.borderLeftColor,
                "An older message keeps following its author after rename");
            t.Equal(WhimTexComment.ColorFromAuthor("Second Artist"), Author(document.FindComment(id).messages[1].id).resolvedStyle.borderLeftColor,
                "Reply stripe uses its author's color, not the discussion override");
            var datedMessages = document.FindComment(id).messages;
            var savedTimes = datedMessages.Select(message => (message.createdUtcTicks, message.editedUtcTicks)).ToArray();
            try
            {
                var lateEvening = new DateTime(2025, 1, 2, 23, 55, 0, DateTimeKind.Local);
                datedMessages[0].createdUtcTicks = lateEvening.ToUniversalTime().Ticks;
                datedMessages[1].createdUtcTicks = lateEvening.AddMinutes(1).ToUniversalTime().Ticks;
                Call(window, "RefreshCommentMessages", Editor());
                var history = (ScrollView)Read(Editor(), "messages");
                t.Equal(0, history.Query(className: "whimtex-comment-day").ToList().Count, "Messages on one local day need no day separator");
                var nextMorning = lateEvening.AddMinutes(10);
                datedMessages[1].createdUtcTicks = nextMorning.ToUniversalTime().Ticks;
                datedMessages[0].editedUtcTicks = nextMorning.AddDays(2).ToUniversalTime().Ticks;
                string datedSnapshot = JsonUtility.ToJson(document);
                Call(window, "RefreshCommentMessages", Editor());
                Call(window, "RefreshCommentMessages", Editor());
                t.Equal(datedSnapshot, JsonUtility.ToJson(document), "Day grouping changes no document data");
                var separators = history.Query(className: "whimtex-comment-day").ToList();
                t.Equal(1, separators.Count, "Crossing local midnight creates exactly one separator, independent of edit dates");
                t.Equal(nextMorning.ToString("d MMM yyyy"), separators[0].Q<Label>().text, "Separator labels the following message's local date");
                t.Equal(1, history.IndexOf(separators[0]), "The separator stays between the original messages");
                for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                t.Near(0, history.ElementAt(0).resolvedStyle.borderBottomWidth, .01, "Day boundary replaces the ordinary message divider");
                t.True(separators[0].worldBound.height > 0 && separators[0].worldBound.width > 0, "Day separator has visible layout");
                foreach (var line in separators[0].Query(className: "whimtex-comment-day-line").ToList())
                {
                    t.True(line.resolvedStyle.width > 0, "Day separator lines fill the space beside the date");
                    t.Near(1, line.resolvedStyle.height, .01, "Day separator lines have a one-pixel surface");
                }
                var dateProbe = new CaptureProbe { owner = window, filename = "comment-days-preview.png", pickingMode = PickingMode.Ignore };
                dateProbe.style.position = Position.Absolute; dateProbe.style.width = dateProbe.style.height = 1;
                root.Add(dateProbe);
                try { using (token.Register(() => dateProbe.done.TrySetCanceled())) { window.Repaint(); await dateProbe.done.Task; } }
                finally { dateProbe.RemoveFromHierarchy(); }
            }
            finally
            {
                for (int i = 0; i < datedMessages.Count; i++)
                {
                    datedMessages[i].createdUtcTicks = savedTimes[i].createdUtcTicks;
                    datedMessages[i].editedUtcTicks = savedTimes[i].editedUtcTicks;
                }
                Call(window, "RefreshCommentMessages", Editor());
            }
            var card = Card();
            t.True(card.worldBound.xMin >= root.worldBound.xMin && card.worldBound.xMax <= root.worldBound.xMax + 1, "Card fits the window horizontally");
            t.True(card.worldBound.yMin >= root.worldBound.yMin && card.worldBound.yMax <= root.worldBound.yMax + 1, "Card fits the window vertically");
            t.True(!card.worldBound.Overlaps(drawer.worldBound), "The card does not cover its list at normal window size");
            t.Equal("Comments · 2 open", root.Q<Label>("commentsTitle").text, "Panel title counts open threads");
            t.Equal("Updated first message", Row(id).Q<Label>(className: "whimtex-comment-list-preview").text, "List preview follows message edits");
            var search = root.Q<ToolbarSearchField>("commentSearch");
            search.value = "A reply";
            t.Equal(1, list.itemsSource.Count, "Search includes replies");
            t.Equal(1, ((System.Collections.IDictionary)Read(window, "commentPins")).Count, "Search filters canvas markers too");
            search.value = "";
            Click(Card().Q<Button>("replyToComment"));
            Input().value = "Retained draft";
            Click(Card().Q<Button>("addCommentAttachment"));
            Key(canvas, KeyCode.B); Key(canvas, KeyCode.N);
            t.Equal("Retained draft", Input().value, "Tool switches restore the unposted draft");
            t.Equal(1, ((List<WhimTexCommentAttachment>)Read(Draft(), "attachments")).Count, "Tool switches retain empty attachment fields");
            Click(Card().Q<Button>("cancelCommentDraft"));
            t.Equal("", Input().value, "Cancel explicitly discards a reply draft");
            t.Equal(DisplayStyle.None, Card().Q("commentMessageEditor").style.display.value, "Cancel returns to the compact plus button");
            card = Card();
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            Rect beforeDrag = card.worldBound;
            var headingDrag = Card().Q("commentCardHeader");
            Vector2 dragWorld = headingDrag.worldBound.center;
            string beforeCardGesture = JsonUtility.ToJson(document);
            Pointer(headingDrag, EventType.MouseDown, headingDrag.WorldToLocal(dragWorld));
            Pointer(card, EventType.MouseDrag, card.WorldToLocal(dragWorld + new Vector2(-40, 25)));
            Pointer(card, EventType.MouseUp, card.WorldToLocal(dragWorld + new Vector2(-40, 25)));
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            t.Near(beforeDrag.x - 40, card.worldBound.x, 1, "Dragging the title moves the card");
            t.Near(beforeDrag.y + 25, card.worldBound.y, 1, "Dragging preserves the pointer offset");
            Rect floating = card.worldBound;
            Call(canvas, "Pan", new Vector2(20, 15)); await AsyncD.Tick(root, token);
            t.Equal(floating, card.worldBound, "A moved card stays fixed while panning the canvas");
            t.True(root.Q("commentConnector") != null, "A floating card retains a connector");
            var expectedLine = root.WorldToLocal(canvas.LocalToWorld(View(document.FindComment(id).anchor)));
            t.True(((Vector2)Read(Editor(), "lineStart") - expectedLine).sqrMagnitude < .1f, "Connector follows the anchor after pan");
            var corner = Card().Q("commentResize1_1"); Vector2 resizeWorld = corner.worldBound.center;
            Pointer(corner, EventType.MouseDown, corner.WorldToLocal(resizeWorld));
            Pointer(card, EventType.MouseDrag, card.WorldToLocal(resizeWorld + new Vector2(60, 45)));
            Pointer(card, EventType.MouseUp, card.WorldToLocal(resizeWorld + new Vector2(60, 45)));
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.Near(floating.width + 60, card.worldBound.width, 1, "The corner resizes the card horizontally");
            t.Near(Mathf.Min(floating.height + 45, root.worldBound.yMax - 8 - floating.y), card.worldBound.height, 1,
                "The corner resizes the card vertically within the window bounds");
            t.Equal(beforeCardGesture, JsonUtility.ToJson(document), "Card gestures do not change document content or anchors");
            var probe = new CaptureProbe { owner = window, pickingMode = PickingMode.Ignore };
            probe.style.position = Position.Absolute; probe.style.width = probe.style.height = 1;
            root.Add(probe);
            try { using (token.Register(() => probe.done.TrySetCanceled())) { window.Repaint(); await probe.done.Task; } }
            finally { probe.RemoveFromHierarchy(); }
            var statusField = (BaseField<string>)Read(Editor(), "status");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = statusField; statusField.SendEvent(submit); }
            {
                var choicesRoot = root.Q("commentChoices");
                t.True(choicesRoot != null && choicesRoot.panel == statusField.panel, "The status icon opens its choices in the same panel");
                t.Equal(3, choicesRoot.Query<Button>().ToList().Count, "The menu offers all three statuses");
                t.True(choicesRoot.Query<Button>().ToList().All(button => string.IsNullOrEmpty(button.text)), "Status choices use icons only");
                t.Equal("Awaiting Review", choicesRoot.Q<Button>("commentChoice1").tooltip, "Review choice keeps its full hover label");
                Click(choicesRoot.Q<Button>("commentChoice1"));
            }
            t.Equal(WhimTexCommentStatus.AwaitingReview, document.FindComment(id).Status, "The card sends the discussion to review");
            bool ReviewIcon() => (bool)Read(Read(window, "commentToolIcon"), "hasReviewComments");
            t.True(ReviewIcon() && FilledIcon(), "A pending review adds a badge to the filled tool icon");
            var pins = (System.Collections.IDictionary)Read(window, "commentPins");
            t.True(((VisualElement)pins[id]).ClassListContains("whimtex-comment-pin--review"), "The pending pin has its review badge");
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            var reviewProbe = new CaptureProbe { owner = window, filename = "comment-review-preview.png", pickingMode = PickingMode.Ignore };
            reviewProbe.style.position = Position.Absolute; reviewProbe.style.width = reviewProbe.style.height = 1;
            root.Add(reviewProbe);
            try { using (token.Register(() => reviewProbe.done.TrySetCanceled())) { window.Repaint(); await reviewProbe.done.Task; } }
            finally { reviewProbe.RemoveFromHierarchy(); }
            string beforeReading = JsonUtility.ToJson(document);
            Click(Row(id)); await AsyncD.Tick(root, token);
            t.Equal(beforeReading, JsonUtility.ToJson(document), "Opening an awaiting-review discussion does not mutate the document");
            root.Q<BaseField<string>>("commentFilter").value = "In Progress";
            t.True(!pins.Contains(id) && ReviewIcon(), "Filtering out pending pins retains the global review badge");
            root.Q<BaseField<string>>("commentFilter").value = "Awaiting Review";
            await AsyncD.Tick(root, token);
            t.Equal(1, list.itemsSource.Count, "Awaiting Review lists exactly the pending discussion");
            root.Q<BaseField<string>>("commentFilter").value = "Open";
            Click(Row(id)); await AsyncD.Tick(root, token);
            ((BaseField<string>)Read(Editor(), "status")).value = "In Progress";
            t.True(!ReviewIcon(), "Returning to work clears the last review badge");
            Undo.PerformUndo();
            t.True(ReviewIcon(), "Undo restores the review status and badge");
            Undo.PerformRedo();
            t.True(!ReviewIcon(), "Redo clears the review badge");
            Click(Row(id)); await AsyncD.Tick(root, token);
            ((BaseField<string>)Read(Editor(), "status")).value = "Resolved"; t.True(document.FindComment(id).resolved, "Resolve retains the thread");
            t.True(!((System.Collections.IDictionary)Read(window, "commentPins")).Contains(id), "Resolved pin is hidden by default");
            await AsyncD.Tick(root, token);
            t.Equal(1, list.itemsSource.Count, "Open filter hides resolved rows and pins together");
            root.Q<BaseField<string>>("commentFilter").value = "All"; await AsyncD.Tick(root, token);
            t.Equal(2, list.itemsSource.Count, "All restores resolved rows");
            t.Equal("Resolved", Row(id).Q("commentListStatusIcon").tooltip, "Resolved list icon retains its hover label");
            t.True(!Row(id).Q<Label>(className: "whimtex-comment-list-heading").text.Contains("Resolved"), "List status text is replaced by its icon");
            Click(Row(id)); await AsyncD.Tick(root, token);
            t.True(root.Query<BaseField<string>>("commentFilter").ToList().All(field => field.value == "All"), "Header and drawer filters remain synchronized");
            t.Equal(id, (string)Read(Draft(), "id"), "Clicking a row opens the corresponding thread");
            t.True(Row(id).ClassListContains("whimtex-comment-list-row--selected"), "The opened row is highlighted");
            t.Equal("Resolved", ((BaseField<string>)Read(Editor(), "status")).value, "Resolved status is shown in the card");
            t.Equal("Resolved", ((BaseField<string>)Read(Editor(), "status")).tooltip, "Card status keeps its hover label");
            ((BaseField<string>)Read(Editor(), "status")).value = "Awaiting Review";
            t.True(ReviewIcon(), "A resolved discussion can go directly back to review");
            ((BaseField<string>)Read(Editor(), "status")).value = "In Progress"; t.True(!document.FindComment(id).resolved, "Reopen makes the thread active");
            Call(window, "SetCanvasRotation", 31f); Call(window, "SetCanvasZoomPercent", 100f);
            await AsyncD.Tick(root, token);
            Call(canvas, "Pan", new Vector2(1500, -1200));
            var beforeOpen = View(initial);
            Click(Row(id)); await AsyncD.Tick(root, token);
            t.Equal(beforeOpen, View(initial), "Opening a discussion does not unexpectedly move the canvas");
            Input().value = "Unposted navigation draft";
            Vector2 viewAxes = View(Vector2.one) - View(Vector2.zero);
            string beforeNavigation = JsonUtility.ToJson(document);
            Click(Card().Q<Button>("showCommentOnCanvas"));
            Vector2 center = View(initial);
            float visibleRight = Mathf.Min(canvas.worldBound.xMax, drawer.worldBound.xMin);
            t.Near((canvas.worldBound.xMin + visibleRight) * .5f, canvas.LocalToWorld(center).x, 1, "Show on Canvas centers the marker outside the side panel");
            t.Near(canvas.worldBound.center.y, canvas.LocalToWorld(center).y, 1, "Show on Canvas centers the marker vertically");
            t.True((viewAxes - (View(Vector2.one) - View(Vector2.zero))).sqrMagnitude < .001f, "Navigation preserves scale and rotation");
            t.Equal(beforeNavigation, JsonUtility.ToJson(document), "Navigation changes no document data");
            t.Equal("Unposted navigation draft", Input().value, "Navigation preserves the current draft");
            var pin = ((System.Collections.IDictionary)Read(window, "commentPins"))[id];
            t.Equal(1f, (float)Read(pin, "highlight"), "Navigation highlights the marker immediately");
            DoubleClick(Row(document.comments[0].id));
            t.Equal(document.comments[0].id, (string)Read(Draft(), "id"), "A draft does not block opening another discussion");
            Click(Row(id)); Click(Card().Q<Button>("showCommentOnCanvas"));
            t.Equal("Unposted navigation draft", Input().value, "Returning to a discussion restores its own draft");
            Input().value = "";
            double highlightStart = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - highlightStart < 1.05) await AsyncD.Tick(root, token);
            t.Equal(0f, (float)Read(pin, "highlight"), "Marker highlight fades out without pulsing");
            document.FindComment(id).anchor = new Vector2(-.4f, 1.2f);
            Click(Card().Q<Button>("showCommentOnCanvas"));
            t.True((View(document.FindComment(id).anchor) - center).sqrMagnitude < .001f, "Navigation also reaches retained off-canvas comments");
            document.FindComment(id).anchor = initial;
            Click(Card().Q<Button>("showCommentOnCanvas"));
            var next = new Vector2(.55f, .4f);
            var down = View(initial); var up = View(next);
            Pointer(canvas, EventType.MouseDown, down); Pointer(canvas, EventType.MouseDrag, up);
            t.Near(initial.x, document.FindComment(id).anchor.x, 1e-5, "Dragging is only a preview until release");
            t.True(((Vector2)Read(Editor(), "lineStart") - root.WorldToLocal(canvas.LocalToWorld(up))).sqrMagnitude < .1f,
                "The card connector follows a moving marker before release");
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
            Tap(next); Call(window, "DeleteCanvasComment", Editor());
            t.True(document.FindComment(id) == null, "Confirmed deletion removes the thread and replies; the modal confirmation is checked manually");
            Undo.PerformUndo(); t.Equal(2, document.FindComment(id).messages.Count, "Delete Undo restores all replies");
            Key(canvas, KeyCode.B); t.True(Card() == null, "Changing tools closes the editor");
            t.True(root.Q("commentPins").ClassListContains("whimtex-hidden"), "Pins hide outside Comment tool");
            t.True(FilledIcon(), "The filled icon remains visible outside the Comment tool");
            double hideDeadline = EditorApplication.timeSinceStartup + 2;
            while (drawer.resolvedStyle.display != DisplayStyle.None && EditorApplication.timeSinceStartup < hideDeadline)
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
            t.Equal(lastId, (string)Read(Draft(), "id"), "A recycled row opens the correct thread after scrolling");
            DoubleClick(Row(lastId));
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            var shownPin = (VisualElement)((System.Collections.IDictionary)Read(window, "commentPins"))[lastId];
            t.True(canvas.worldBound.Contains(canvas.LocalToWorld(View(document.FindComment(lastId).anchor))), "Double-clicking a recycled row navigates to its marker");
            t.True(!Card().worldBound.Overlaps(shownPin.worldBound), "The discussion card does not cover the navigated marker beside a full-height list");
            float offset = scroll.scrollOffset.y;
            Card().Q<WhimTexColorField>("commentColor").value = Color.cyan;
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
            Call(window, "ChangeCanvasZoom", true);
            Tap(new Vector2(.5f, .5f)); Input().value = "First comment";
            t.True(!FilledIcon(), "An unposted draft keeps the outline icon");
            Key(Input(), KeyCode.Return, EventModifiers.Control);
            t.True(FilledIcon(), "Posting the first thread fills the icon");
            string onlyId = document.comments[0].id;
            ((BaseField<string>)Read(Editor(), "status")).value = "Resolved";
            t.True(FilledIcon(), "Resolved comments still fill the tool icon");
            await AsyncD.Tick(root, token); Click(Row(onlyId));
            Call(window, "DeleteCanvasComment", Editor());
            t.True(!FilledIcon(), "Deleting the last resolved thread restores the outline");
            Undo.PerformUndo(); t.True(FilledIcon(), "Undo restores the filled icon with the thread");
            Undo.PerformRedo(); t.True(!FilledIcon(), "Redo restores the outline icon after deletion");
            window.position = new Rect(80, 80, 1280, 800);
            var group = new Layer(new GroupLayerBehaviour()); var child = new Layer(new ColorFillLayerBehaviour());
            var other = new Layer(new ColorFillLayerBehaviour());
            foreach (var value in new[] { group, child, other }) Call(value, "AssignNewId");
            group.layerName = "Attachment group"; child.layerName = "Referenced layer"; other.layerName = "Keep selected";
            ((GroupLayerBehaviour)group.Behaviour).layers.Add(child); document.layers.Add(group); document.layers.Add(other);
            Call(window, "SelectOnlyLayer", other.Id); Call(window, "RefreshToolkitInterface", true);
            var expansion = (Dictionary<string, bool>)Read(window, "groupExpansion"); expansion[group.Id] = false;
            Call(window, "RefreshToolkitInterface", true);
            Tap(new Vector2(.35f, .65f)); await AsyncD.Tick(root, token);
            Click(Card().Q<Button>("addCommentAttachment"));
            t.Equal(1, Card().Q("commentAttachmentFields").Query<Button>("commentAttachmentField").ToList().Count, "Plus creates an empty editable field");
            string DragKey(string name) => (string)typeof(WhimTexWindow).GetField(name, Flags | BindingFlags.Static).GetValue(null);
            var dragKeys = new[] { "DraggedDocumentIdKey", "DraggedLayersKey", "DraggedWindowKey", "DraggedLayerIdKey" }.Select(DragKey).ToArray();
            var dragState = dragKeys.Select(DragAndDrop.GetGenericData).ToArray(); var objectState = DragAndDrop.objectReferences;
            void Drop(VisualElement target, bool accepted = true)
            {
                using var update = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated }); update.target = target; target.SendEvent(update);
                t.Equal(accepted, root.Q("commentAttachmentDropHighlight") != null, "Only valid attachment drops highlight their target");
                using var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform }); perform.target = target; target.SendEvent(perform);
                t.True(root.Q("commentAttachmentDropHighlight") == null, "Drop clears the attachment highlight");
            }
            try
            {
                DragAndDrop.SetGenericData(dragKeys[0], document);
                DragAndDrop.SetGenericData(dragKeys[1], new List<Layer> { child });
                DragAndDrop.SetGenericData(dragKeys[2], window);
                Drop(Card().Q("commentAttachmentFields").Q<Button>("commentAttachmentField"));
                t.Equal(1, ((List<WhimTexCommentAttachment>)Read(Draft(), "attachments")).Count, "Dropping on a field replaces its value");
                t.Equal(0, document.comments.Count, "Attachment edits remain a draft until Post");
                var script = AssetDatabase.LoadMainAssetAtPath("Packages/com.dcfapixels.whimtex/src/WhimTexWindow.cs");
                t.True(script != null, "Read-only package asset is available for attachment testing");
                DragAndDrop.objectReferences = new[] { script };
                Drop(Card().Q<Button>("addCommentAttachment"));
                t.Equal(2, ((List<WhimTexCommentAttachment>)Read(Draft(), "attachments")).Count, "Project asset can be dropped directly on plus");
                Drop(Card().Q("commentAttachmentFields"));
                t.Equal(3, ((List<WhimTexCommentAttachment>)Read(Draft(), "attachments")).Count, "Dropping on the field block appends another attachment");
                Click(Card().Q("commentAttachmentFields").Query<Button>("removeCommentAttachment").ToList().Last());
                Input().value = "Review these references"; Key(Input(), KeyCode.Return, EventModifiers.Control);
                string attachedId = document.comments[0].id;
                t.Equal(2, document.comments[0].messages[0].attachments.Count, "Post retains both reference kinds");
                var published = Card().Q("commentAttachments");
                t.True(published.Q("removeCommentAttachment") == null, "Published attachments have no editing controls");
                Drop(published, false);
                t.Equal(2, document.comments[0].messages[0].attachments.Count, "Published fields reject drag replacement");
                string selected = (string)Read(window, "selectedLayerId");
                Click(published.Query<Button>("commentAttachmentField").ToList()[0]);
                t.Equal(selected, (string)Read(window, "selectedLayerId"), "Layer ping does not change selection");
                t.True(expansion[group.Id], "Layer ping reveals collapsed ancestors");
                t.True(root.Q("layerPing") != null, "Layer ping has a visible animation overlay");
                var ping = root.Q("layerPing");
                var pingRow = (VisualElement)Read(window, "layerPingRow");
#if UNITY_6000_7_OR_NEWER
                t.Equal(1000, pingRow.style.zIndex.value, "The original row uses z-index during its pop");
#endif
                ((IVisualElementScheduledItem)Read(window, "layerPingAnimation")).Pause();
                await AsyncD.Tick(root, token);
                double layoutDeadline = EditorApplication.timeSinceStartup + 3;
                while (!float.IsFinite(pingRow.layout.width) && EditorApplication.timeSinceStartup < layoutDeadline)
                    await AsyncD.Tick(root, token);
                t.True(float.IsFinite(pingRow.layout.width), "The revealed layer row completes layout before comparing its geometry");
                Rect pingLayout = pingRow.layout;
                t.True(ping.parent == root, "Ping outline draws outside the row above its neighbors");
                async Task AdvancePing(float elapsed)
                {
                    Call(window, "UpdateLayerPing", elapsed);
                    for (int frame = 0; frame < 2; frame++) await AsyncD.Tick(root, token);
                }
                await AdvancePing(0);
                t.Equal(Visibility.Visible, ping.resolvedStyle.visibility, "The first animation frame reveals the outline");
                t.Near(1.4, pingRow.resolvedStyle.scale.value.y, .001, "The row starts with a strong visual pop");
                await AdvancePing(.09f);
                t.True(pingRow.resolvedStyle.scale.value.y > 1 && pingRow.resolvedStyle.scale.value.y < 1.4,
                    "The pop contracts toward the original scale");
                foreach (float elapsed in new[] { .18f, .67f })
                {
                    await AdvancePing(elapsed);
                    t.Near(1, pingRow.resolvedStyle.scale.value.y, .001, "The hold uses the ordinary row scale");
                    t.Equal(StyleKeyword.Null, pingRow.style.scale.keyword, "The hold restores the original scale style");
                    t.Near(1, ping.resolvedStyle.opacity, .001, "The outline stays fully visible throughout the hold");
                    t.Near(2, ping.resolvedStyle.borderTopWidth, .01, "The held outline uses a two-pixel border");
                    t.True(ping.worldBound.width > pingRow.worldBound.width, "The held outline surrounds the row externally");
#if UNITY_6000_7_OR_NEWER
                    t.Equal(StyleKeyword.Null, pingRow.style.zIndex.keyword, "The pop restores the original stacking style before fading");
#endif
                }
                await AdvancePing(.88f);
                t.True(ping.resolvedStyle.opacity > 0 && ping.resolvedStyle.opacity < 1, "The outline fades after holding");
                await AdvancePing(1.1f);
                t.True(root.Q("layerPing") == null, "Ping outline disappears after its short fade");
                t.Equal(pingLayout, pingRow.layout, "Visual scaling does not change row layout");
                t.Near(1, pingRow.resolvedStyle.scale.value.y, .001f, "Ping returns to ordinary scale");
                Call(window, "PingLayer", child.Id);
                ((IVisualElementScheduledItem)Read(window, "layerPingAnimation")).Pause();
                var delayedRow = (VisualElement)Read(window, "layerPingRow");
                for (int frame = 0; frame < 2; frame++) await AsyncD.Tick(root, token);
                await AdvancePing(0);
                await AdvancePing(5);
                t.True(root.Q("layerPing") == null, "A delayed frame beyond the animation lifetime still removes the outline");
                t.Equal(StyleKeyword.Null, delayedRow.style.scale.keyword, "A delayed frame restores the row's scale style");
                var selection = Selection.objects;
                Click(Card().Q("commentAttachments").Query<Button>("commentAttachmentField").ToList()[1]);
                t.True(selection.SequenceEqual(Selection.objects), "Asset ping does not change Project selection");
                EditFirst();
                Click(Card().Q("commentAttachmentFields").Query<Button>("removeCommentAttachment").ToList().Last());
                t.Equal(2, document.FindComment(attachedId).messages[0].attachments.Count, "Editing fields does not mutate the posted message before Save");
                Key(Input(), KeyCode.Return, EventModifiers.Control);
                t.Equal(1, document.FindComment(attachedId).messages[0].attachments.Count, "Save applies attachment edits");
                Undo.PerformUndo(); t.Equal(2, document.FindComment(attachedId).messages[0].attachments.Count, "Attachment changes support Undo");
                for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                Call(window, "PingLayer", child.Id);
                t.True(root.Q("layerPing") != null, "The live scheduler starts a separate ping");
                for (int i = 0; i < 5; i++) await AsyncD.Tick(root, token);
                var attachmentProbe = new CaptureProbe { owner = window, filename = "comment-attachments-preview.png", pickingMode = PickingMode.Ignore };
#if UNITY_6000_7_OR_NEWER
                attachmentProbe.style.zIndex = 2000;
#endif
                attachmentProbe.style.position = Position.Absolute; attachmentProbe.style.width = attachmentProbe.style.height = 1; root.Add(attachmentProbe);
                try { using (token.Register(() => attachmentProbe.done.TrySetCanceled())) { window.Repaint(); await attachmentProbe.done.Task; } }
                finally { attachmentProbe.RemoveFromHierarchy(); }
                double pingDeadline = EditorApplication.timeSinceStartup + 3;
                while (root.Q("layerPing") != null && EditorApplication.timeSinceStartup < pingDeadline)
                    await AsyncD.Tick(root, token);
                t.True(root.Q("layerPing") == null, "The live scheduler completes the animation without manual advancement");
                DismissAll();
                for (int i = 0; i < 14; i++) document.AddCommentMessage(attachedId, "Reply " + i + "\nAdditional context to fill the discussion.");
                document.AddCommentMessage(attachedId, string.Join("\n", Enumerable.Repeat("A long final reply.", 30)));
                Call(window, "OpenCanvasComment", attachedId, Vector2.zero);
                for (int i = 0; i < 4; i++) await AsyncD.Tick(root, token);
                var history = (ScrollView)Read(Editor(), "messages");
                t.Equal(WhimTexComment.ColorFromAuthor("User"), Author(document.FindComment(attachedId).messages.Last().id).resolvedStyle.borderLeftColor,
                    "Another author's reply uses its own hue");
                t.True(history.verticalScroller.highValue > 0, "The long discussion overflows its message viewport");
                t.Near(history.verticalScroller.highValue, history.scrollOffset.y, 1, "Opening a discussion scrolls to its last message");
                history.scrollOffset = Vector2.zero;
                for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
                t.Near(0, history.scrollOffset.y, .1f, "Automatic opening scroll does not fight subsequent manual scrolling");
                DismissAll();
                Call(window, "OpenCanvasComment", null, new Vector2(.2f, .5f));
                var areaMaskSelection = Call(window, "GetAreaSelection");
                var replaceMode = Enum.Parse(typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.SelectionCombine"), "Replace");
                Call(areaMaskSelection, "Clear"); Call(window, "RefreshCommentAttachmentEditor", Editor());
                t.True(!Card().Q<Button>("attachCommentSelection").enabledSelf, "Attach Selection is disabled without an active nonempty selection");
                var coverage = new byte[document.width * document.height];
                for (int y = document.height / 4; y < document.height * 3 / 4; y++)
                    for (int x = document.width / 4; x < document.width * 3 / 4; x++)
                        if (!(x > document.width * .4 && x < document.width * .6 && y > document.height * .4 && y < document.height * .6) &&
                            !(x > document.width * .6 && y > document.height * .6))
                            coverage[y * document.width + x] = (byte)(x == document.width / 4 ? 85 : 255);
                Call(areaMaskSelection, "Set", coverage, replaceMode); Call(window, "RefreshCommentAttachmentEditor", Editor());
                Click(Card().Q<Button>("attachCommentSelection")); await AsyncD.Tick(root, token);
                var maskDraft = (List<WhimTexCommentAttachment>)Read(Draft(), "attachments");
                t.Equal(1, maskDraft.Count, "Attach Selection adds one mask row");
                t.True(coverage.SequenceEqual(maskDraft[0].mask.Rasterize(document.width, document.height)), "Draft keeps exact selection coverage");
                Input().value = "Review this exact region"; Key(Input(), KeyCode.Return, EventModifiers.Control);
                string maskThreadId = (string)Read(Draft(), "id");
                var postedMask = document.FindComment(maskThreadId).messages[0].attachments[0].mask;
                Call(areaMaskSelection, "All");
                var fullSelection = (byte[])areaMaskSelection.GetType().GetProperty("Coverage", Flags).GetValue(areaMaskSelection);
                fullSelection = (byte[])fullSelection.Clone();
                var maskRow = Card().Q("commentAttachments").Q("commentMaskAttachment");
                t.True(maskRow.Q("replaceCommentMask") == null && maskRow.Q("removeCommentAttachment") == null, "Posted mask rows cannot replace or remove data");
                Click(maskRow.Q<Button>("previewCommentMask")); await AsyncD.Tick(root, token);
                t.True(root.Q("commentMaskPreview") != null, "Mask thumbnail toggles a canvas overlay");
                t.True(fullSelection.SequenceEqual((byte[])areaMaskSelection.GetType().GetProperty("Coverage", Flags).GetValue(areaMaskSelection)), "Preview leaves the working selection unchanged");
                maskRow = Card().Q("commentAttachments").Q("commentMaskAttachment");
                var thumbnail = maskRow.Q<Image>().image;
                t.True(thumbnail != null, "Mask has a visible thumbnail");
                var maskProbe = new CaptureProbe { owner = window, filename = "comment-mask-preview.png", pickingMode = PickingMode.Ignore };
                maskProbe.style.position = Position.Absolute; maskProbe.style.width = maskProbe.style.height = 1; root.Add(maskProbe);
                try { using (token.Register(() => maskProbe.done.TrySetCanceled())) { window.Repaint(); await maskProbe.done.Task; } }
                finally { maskProbe.RemoveFromHierarchy(); }
                var previewImage = new Texture2D(2, 2);
                try
                {
                    ImageConversion.LoadImage(previewImage, File.ReadAllBytes("Temp/WhimTex/comment-mask-preview.png"));
                    Color At(Vector2 uv)
                    {
                        var p = root.WorldToLocal(canvas.LocalToWorld(View(uv))) * EditorGUIUtility.pixelsPerPoint;
                        return previewImage.GetPixel(Mathf.RoundToInt(p.x), previewImage.height - 1 - Mathf.RoundToInt(p.y));
                    }
                    Color upper = At(new Vector2(.7f, .65f)), lower = At(new Vector2(.7f, .35f));
                    t.True(upper.r > .95f && lower.b - lower.r > .08f, "Rendered mask preserves the asymmetric bottom-left coverage orientation");
                }
                finally { Object.DestroyImmediate(previewImage); }
                Click(Card().Q("commentAttachments").Q<Button>("useCommentMask"));
                t.True(coverage.SequenceEqual((byte[])areaMaskSelection.GetType().GetProperty("Coverage", Flags).GetValue(areaMaskSelection)), "Use restores the exact mask as the working selection");
                t.True(root.Q("commentMaskPreview") == null, "Use removes the temporary preview");
                EditFirst();
                Call(areaMaskSelection, "All"); Call(window, "RefreshCommentAttachmentEditor", Editor());
                Click(Card().Q<Button>("replaceCommentMask"));
                t.True(coverage.SequenceEqual(postedMask.Rasterize(document.width, document.height)), "Replace edits the draft without changing the posted mask");
                DismissAll();
                t.True(thumbnail == null, "Closing the card releases its thumbnail texture");
                t.True(coverage.SequenceEqual(document.FindComment(maskThreadId).messages[0].attachments[0].mask.Rasterize(document.width, document.height)), "Cancelling replacement preserves the published snapshot");
                Call(window, "OpenCanvasComment", maskThreadId, Vector2.zero);
                t.Equal(document.FindComment(maskThreadId).messages[0].id, (string)Read(Draft(), "messageId"), "Closing and reopening preserves the message being edited");
                Click(Card().Q<Button>("cancelCommentDraft"));
                t.True(Read(Draft(), "messageId") == null, "Explicit Cancel exits editing");
                Call(window, "OpenCanvasComment", null, new Vector2(.2f, .2f));
                Input().value = "Discard this draft"; Click(Card().Q<Button>("cancelCommentDraft"));
                Call(window, "OpenCanvasComment", null, new Vector2(.7f, .8f));
                t.Equal(new Vector2(.7f, .8f), (Vector2)Read(Draft(), "anchor"), "A new comment after Cancel uses the new click position");
                for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                var emptyCard = Card(); Rect beforeCancelDrag = emptyCard.worldBound;
                var emptyHeader = Card().Q("commentCardHeader"); Vector2 emptyDown = emptyHeader.worldBound.center;
                Pointer(emptyHeader, EventType.MouseDown, emptyHeader.WorldToLocal(emptyDown));
                Pointer(emptyCard, EventType.MouseDrag, emptyCard.WorldToLocal(emptyDown + new Vector2(-50, 20)));
                Key(emptyCard, KeyCode.Escape);
                for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
                t.True(Card() != null, "Escape during a card gesture cancels the gesture without closing the card");
                t.Equal(beforeCancelDrag, emptyCard.worldBound, "Escape restores the original card geometry");
                t.True(!emptyCard.HasPointerCapture(PointerId.mousePointerId), "Cancelling a card gesture releases capture");
                Click(Card().Q<Button>("cancelCommentDraft"));

                DismissAll();
                var first = document.AddComment(new Vector2(.25f, .65f), "First independent discussion", color: Color.cyan);
                var second = document.AddComment(new Vector2(.75f, .35f), "Second independent discussion", color: Color.yellow);
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                var editorA = Editor(); var cardA = Card();
                Input().value = "Reply for the first discussion";
                Click(cardA.Q<Button>("attachCommentSelection"));
                Call(window, "OpenCanvasComment", second.id, second.anchor);
                var editorB = Editor(); var cardB = Card();
                Input().value = "Draft for the second discussion";
                Click(cardB.Q<Button>("addCommentAttachment"));
                t.Equal(2, root.Query<VisualElement>("commentEditor").ToList().Count, "Opening another discussion retains both independent cards");
                t.Equal(2, root.Query<VisualElement>("commentConnector").ToList().Count, "Each open card owns one connector");
                t.Equal("Reply for the first discussion", cardA.Q<TextField>("commentText").value, "The first draft stays in its original input");
                t.True(((List<WhimTexCommentAttachment>)Read(Read(editorA, "state"), "attachments"))[0].mask != null,
                    "The first card retains its selection attachment");
                t.True(((List<WhimTexCommentAttachment>)Read(Read(editorB, "state"), "attachments"))[0] == null,
                    "The second card has its own independent empty attachment slot");
                // Exercise a menu captured from the first card while the other card is active.
                Click(cardA.Q<Button>("postComment"));
                t.Equal("Reply for the first discussion", first.messages.Last().text, "A background card posts to its own thread");
                t.Equal(1, second.messages.Count, "Posting to the first card never replies to the second");
                t.Equal("Draft for the second discussion", cardB.Q<TextField>("commentText").value, "Posting elsewhere preserves the second draft");
                DragAndDrop.SetGenericData(dragKeys[0], document);
                DragAndDrop.SetGenericData(dragKeys[1], new List<Layer> { child });
                DragAndDrop.SetGenericData(dragKeys[2], window);
                Drop(cardB.Q("commentAttachmentFields").Q<Button>("commentAttachmentField"));
                t.Equal(child.Id, ((List<WhimTexCommentAttachment>)Read(Read(editorB, "state"), "attachments"))[0].layerId,
                    "Dropping onto an inactive card changes that card's attachment");
                t.Equal(0, ((List<WhimTexCommentAttachment>)Read(Read(editorA, "state"), "attachments")).Count,
                    "A background drop never changes the active card's draft attachments");
                Click(cardA.Q("commentAttachments").Q<Button>("previewCommentMask"));
                Click(cardB.Q<Button>("addCommentAttachment"));
                t.True(root.Q("commentMaskPreview") != null, "Editing another card's attachments preserves the first card's mask preview");
                Click(cardA.Q("commentAttachments").Q<Button>("previewCommentMask"));
                cardA.Q<WhimTexColorField>("commentColor").value = Color.magenta;
                t.Equal(Color.magenta, first.color, "Color callbacks retain their owning discussion");
                t.Equal(Color.yellow, second.color, "Another card's color remains unchanged");
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                t.Equal(2, root.Query<VisualElement>("commentEditor").ToList().Count, "Reopening an existing card never duplicates it");
                t.True(root.IndexOf(cardA) > root.IndexOf(cardB), "The requested card moves above the other card");
                foreach (var line in root.Query<VisualElement>("commentConnector").ToList())
                    t.True(root.IndexOf(line) < Math.Min(root.IndexOf(cardA), root.IndexOf(cardB)), "All connector strokes remain behind every card");
                void Place(object editor, Rect rect)
                {
                    var editorState = Read(editor, "state");
                    editorState.GetType().GetField("positioned").SetValue(editorState, true);
                    editorState.GetType().GetField("sized").SetValue(editorState, true);
                    editorState.GetType().GetField("rect").SetValue(editorState, rect);
                }
                Place(editorA, new Rect(32, 40, 296, 220)); Place(editorB, new Rect(920, 185, 310, 410));
                Call(window, "SetCanvasRotation", 0f); Call(window, "ChangeCanvasZoom", true);
                Call(window, "PositionCanvasComments");
                for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                t.Near(0, ((ScrollView)Read(editorB, "messages")).scrollOffset.y, .1f,
                    "A short discussion has no negative scroll offset or empty gap above its first message");
                Rect stationaryB = cardB.worldBound;
                var headerA = cardA.Q("commentCardHeader"); Vector2 startA = headerA.worldBound.center;
                Pointer(headerA, EventType.MouseDown, headerA.WorldToLocal(startA));
                Pointer(cardA, EventType.MouseDrag, cardA.WorldToLocal(startA + new Vector2(35, 30)));
                Pointer(cardA, EventType.MouseUp, cardA.WorldToLocal(startA + new Vector2(35, 30)));
                for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
                t.Equal(stationaryB, cardB.worldBound, "Dragging the first card does not move the second");
                t.Near(root.worldBound.x + 67, cardA.worldBound.x, 1, "The first card moves independently");
                var cornerA = cardA.Q("commentResize1_1"); Vector2 cornerStart = cornerA.worldBound.center;
                Pointer(cornerA, EventType.MouseDown, cornerA.WorldToLocal(cornerStart));
                Pointer(cardA, EventType.MouseDrag, cardA.WorldToLocal(cornerStart + new Vector2(40, 20)));
                Pointer(cardA, EventType.MouseUp, cardA.WorldToLocal(cornerStart + new Vector2(40, 20)));
                for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
                t.Near(336, cardA.worldBound.width, 1, "The first card resizes independently");
                t.Equal(stationaryB, cardB.worldBound, "Resizing the first card does not resize the second");
                Call(canvas, "Pan", new Vector2(12, -9)); await AsyncD.Tick(root, token);
                foreach (var pair in new[] { (editorA, first), (editorB, second) })
                {
                    Vector2 anchor = root.WorldToLocal(canvas.LocalToWorld(View(pair.Item2.anchor)));
                    Rect clip = root.WorldToLocal(canvas.worldBound);
                    anchor = new Vector2(Mathf.Clamp(anchor.x, clip.xMin, clip.xMax), Mathf.Clamp(anchor.y, clip.yMin, clip.yMax));
                    t.True(((Vector2)Read(pair.Item1, "lineStart") - anchor).sqrMagnitude < .1f,
                        "Each connector follows its own marker when the canvas pans");
                    var ownCard = (VisualElement)Read(pair.Item1, "card");
                    Vector2 end = (Vector2)Read(pair.Item1, "lineEnd");
                    var ownRect = root.WorldToLocal(ownCard.worldBound);
                    t.True(Mathf.Min(Mathf.Abs(end.x - ownRect.xMin), Mathf.Abs(end.x - ownRect.xMax),
                        Mathf.Abs(end.y - ownRect.yMin), Mathf.Abs(end.y - ownRect.yMax)) < 1,
                        "Each connector terminates on its own card edge: anchor=" + anchor + ", end=" + end + ", card=" + ownRect);
                }
                var multiProbe = new CaptureProbe { owner = window, filename = "comment-multiple-preview.png", pickingMode = PickingMode.Ignore };
                multiProbe.style.position = Position.Absolute; multiProbe.style.width = multiProbe.style.height = 1; root.Add(multiProbe);
                try { using (token.Register(() => multiProbe.done.TrySetCanceled())) { window.Repaint(); await multiProbe.done.Task; } }
                finally { multiProbe.RemoveFromHierarchy(); }
                Click(cardA.Q<Button>("closeComment"));
                t.True(cardA.panel == null && cardB.panel != null, "Closing one card keeps the other on screen");
                t.Equal(1, root.Query<VisualElement>("commentConnector").ToList().Count, "Closing one card removes only its connector");
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                cardA = Card(); editorA = Editor();
                for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                t.True(!(bool)Read(Draft(), "positioned"), "Reopening a closed card uses automatic placement beside its marker");
                t.Equal(Vector2.zero, ((Rect)Read(Draft(), "rect")).position, "Closing clears the previous card position");
                t.Near(336, cardA.worldBound.width, 1, "Reopening retains the resized width independently of position");
                var firstInput = cardA.Q<TextField>("commentText"); firstInput.value = "Separate keyboard draft";
                Call(window, "OpenCanvasComment", second.id, second.anchor);
                Key(firstInput, KeyCode.Escape);
                t.True(cardA.panel == null && cardB.panel != null, "Escape closes the event's own card even if another card was activated");
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                t.Equal("Separate keyboard draft", Input().value, "Closing one card keeps its draft for reopening");
                t.True(!(bool)Read(Draft(), "positioned"), "Escape also restores automatic placement on reopening");
                search.value = "First independent";
                t.Equal(1, root.Query<VisualElement>("commentEditor").ToList().Count, "Filtering suspends only nonmatching cards");
                search.value = "";
                t.Equal(2, root.Query<VisualElement>("commentEditor").ToList().Count, "Clearing the filter restores the previously open cards");
                Key(canvas, KeyCode.B); Key(canvas, KeyCode.N);
                t.Equal(2, root.Query<VisualElement>("commentEditor").ToList().Count, "Tool switching restores the entire open set");
                var restoredB = Call(window, "FindOpenCommentEditor", second.id);
                t.Equal("Draft for the second discussion", ((TextField)Read(restoredB, "input")).value, "Tool switching retains each independent draft");

                int storedComments = document.comments.Count;
                Call(window, "OpenCanvasComment", null, new Vector2(.15f, .25f));
                var untouchedCard = Card();
                Pointer(untouchedCard.Q<TextField>("commentText"), EventType.MouseDown, new Vector2(8, 8));
                Pointer(untouchedCard.Q<TextField>("commentText"), EventType.MouseUp, new Vector2(8, 8));
                t.True(untouchedCard.panel != null, "Clicking inside an empty draft keeps it open for typing");
                Tap(new Vector2(.65f, .85f));
                t.True(untouchedCard.panel == null, "Clicking elsewhere removes an untouched new card");
                t.True(((Vector2)Read(Draft(), "anchor") - new Vector2(.65f, .85f)).sqrMagnitude < .000001f,
                    "The same click starts a fresh draft at the new canvas point");
                t.Equal(storedComments, document.comments.Count, "Discarding an empty draft never creates or deletes published comments");
                var replacementCard = Card();
                Pointer(cardB = (VisualElement)Read(restoredB, "card"), EventType.MouseDown, new Vector2(12, 12));
                Pointer(cardB, EventType.MouseUp, new Vector2(12, 12));
                t.True(replacementCard.panel == null, "Clicking a published card removes only the untouched draft");
                t.Equal(2, root.Query<VisualElement>("commentEditor").ToList().Count, "Published discussions remain open after outside clicks");
                Call(window, "OpenCanvasComment", null, Vector2.one * .4f);
                var configuredCard = Card(); configuredCard.Q<WhimTexColorField>("commentColor").value = Color.red;
                Pointer(cardB, EventType.MouseDown, new Vector2(12, 12)); Pointer(cardB, EventType.MouseUp, new Vector2(12, 12));
                t.True(configuredCard.panel != null, "An empty draft with a manually chosen color is retained");
                ResetColor(configuredCard); Click(configuredCard.Q<Button>("addCommentAttachment"));
                Pointer(cardB, EventType.MouseDown, new Vector2(12, 12)); Pointer(cardB, EventType.MouseUp, new Vector2(12, 12));
                t.True(configuredCard.panel != null, "An empty draft with an attachment field is retained");

                configuredCard.Q<TextField>("commentText").value = "Unposted note with a mask";
                configuredCard.Q<WhimTexColorField>("commentColor").value = Color.red;
                Click(configuredCard.Q<Button>("attachCommentSelection"));
                var newDraft = Read(Call(window, "FindOpenCommentEditor", (object)null), "state");
                var newAttachments = (List<WhimTexCommentAttachment>)Read(newDraft, "attachments");
                string draftAttachments = string.Join("|", newAttachments.Select(JsonUtility.ToJson));
                var firstEditor = Call(window, "FindOpenCommentEditor", first.id);
                Place(firstEditor, new Rect(50, 70, 336, 240));
                Call(window, "PositionCanvasComments");
                string beforeCloseAll = JsonUtility.ToJson(document);
                var closeAll = root.Q<Button>("closeAllComments");
                t.True(closeAll != null && closeAll.enabledSelf, "Close All is available with open cards");
                search.value = "First independent";
                t.True(Call(window, "FindOpenCommentEditor", second.id) == null, "The second draft is suspended by the filter before closing all");
                Click(closeAll);
                t.Equal(0, root.Query<VisualElement>("commentEditor").ToList().Count, "Close All removes every visible card");
                t.Equal(0, root.Query<VisualElement>("commentConnector").ToList().Count, "Close All removes every connector");
                t.True(Editor() == null && !closeAll.enabledSelf, "No active card remains and Close All becomes disabled");
                t.Equal(beforeCloseAll, JsonUtility.ToJson(document), "Close All neither posts drafts nor changes stored discussions");
                search.value = "";
                Key(canvas, KeyCode.B); Key(canvas, KeyCode.N);
                t.Equal(0, root.Query<VisualElement>("commentEditor").ToList().Count, "Clearing filters and switching tools do not reopen explicitly closed cards");
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                t.Equal("Separate keyboard draft", Input().value, "Close All preserves the first reply draft");
                t.True(!(bool)Read(Draft(), "positioned"), "Close All restores automatic placement on reopening");
                t.Equal(Vector2.zero, ((Rect)Read(Draft(), "rect")).position, "Close All clears the old card position");
                t.Equal(new Vector2(336, 240), ((Rect)Read(Draft(), "rect")).size, "Close All keeps the chosen size");
                Call(window, "OpenCanvasComment", second.id, second.anchor);
                t.Equal("Draft for the second discussion", Input().value, "Close All preserves a filter-hidden reply draft");
                t.True(!(bool)Read(Draft(), "positioned"), "Close All also clears placement for a filter-hidden card");
                Click(root.Q<Button>("resumeCommentDraft"));
                t.Equal("Unposted note with a mask", Input().value, "Resume Draft restores the unposted new comment after Close All");
                t.Equal(Color.red, Card().Q<WhimTexColorField>("commentColor").value, "Close All preserves the draft's manual color");
                t.Equal(draftAttachments, string.Join("|", ((List<WhimTexCommentAttachment>)Read(Draft(), "attachments")).Select(JsonUtility.ToJson)),
                    "Close All preserves the exact mask and empty attachment slot");
                Click(Card().Q<Button>("cancelCommentDraft"));
                Call(window, "OpenCanvasComment", null, Vector2.one * .3f);
                Card().Q<WhimTexColorField>("commentColor").value = Color.red;
                Click(closeAll); Click(root.Q<Button>("resumeCommentDraft"));
                t.Equal(Color.red, Card().Q<WhimTexColorField>("commentColor").value, "A color-only new draft survives Close All and Resume Draft");
                Click(Card().Q<Button>("cancelCommentDraft"));
                t.True(!(bool)Call(window, "HasCommentDraft", (object)null), "Cancel explicitly discards a configured new draft");
                Call(window, "OpenCanvasComment", first.id, first.anchor);
                Click(Card().Q<Button>("cancelCommentDraft")); EditFirst();
                string editedMessageId = (string)Read(Draft(), "messageId");
                string editSnapshot = (string)Read(Draft(), "editingSnapshot");
                Input().value = "Unposted message edit";
                Click(closeAll); Call(window, "OpenCanvasComment", first.id, first.anchor);
                t.Equal("Unposted message edit", Input().value, "Close All preserves an unposted message edit");
                t.Equal(editedMessageId, (string)Read(Draft(), "messageId"), "Reopening retains the message being edited");
                t.Equal(editSnapshot, (string)Read(Draft(), "editingSnapshot"), "Reopening retains the edit conflict guard");
                t.Equal("Save", Card().Q<Button>("postComment").text, "Reopening an edit still offers Save");
                t.Equal(editedMessageId, (string)Input().parent.parent.userData, "Reopening restores the edit inside its original message");
                t.True(Card().Q("commentComposer") == null, "A restored edit has no separate input block");
            }
            finally
            {
                for (int i = 0; i < dragKeys.Length; i++) DragAndDrop.SetGenericData(dragKeys[i], dragState[i]);
                DragAndDrop.objectReferences = objectState;
            }
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null) { window.DiscardChanges(); if (window.rootVisualElement.panel != null) window.Close(); } },
                () => { if (window != null) Object.DestroyImmediate(window); },
                () => { if (document != null) Object.DestroyImmediate(document); },
                () =>
                {
                    foreach (var p in preferences) { if (p.exists) EditorPrefs.SetString(p.key, p.value); else EditorPrefs.DeleteKey(p.key); }
                    var settings = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings");
                    ((Action)settings.GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null))?.Invoke();
                });
        }
    }
}
