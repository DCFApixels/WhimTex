using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private Button canvasCommentButton;
        private CanvasToolIcon commentToolIcon;
        private CommentManipulator commentManipulator;
        private VisualElement commentOverlay, commentCard;
        private ScrollView commentMessages;
        private TextField commentInput;
        private Label commentTitle;
        private WhimTexColorField commentColor;
        private CommentPin commentDraftPin;
        private Button commentPost, commentResolve, commentDelete;
        private WhimTexDocument commentDocument;
        private string commentId, commentMessageId;
        private Vector2 commentDraftAnchor;
        private Color commentDraftColor = WhimTexComment.DefaultColor;
        private bool commentShowResolved;
        private readonly Dictionary<string, CommentPin> commentPins = new Dictionary<string, CommentPin>();
        private bool HasNavigableCanvas => activeDocument != null && (HasCanvasLayers || canvasTool == CanvasTool.Comment);

        private void BuildCanvasCommentTool()
        {
            commentPins.Clear();
            commentOverlay = new VisualElement { name = "commentPins", pickingMode = PickingMode.Ignore };
            commentOverlay.AddToClassList("whimtex-comment-overlay");
            commentDraftPin = new CommentPin { text = "+" };
            commentDraftPin.AddToClassList("whimtex-comment-pin");
            commentDraftPin.AddToClassList("whimtex-comment-pin--selected");
            commentDraftPin.AddToClassList("whimtex-hidden");
            commentOverlay.Add(commentDraftPin);
            toolkitCanvas.Add(commentOverlay);
            commentManipulator = new CommentManipulator(this);
            toolkitCanvas.AddManipulator(commentManipulator);
            toolkitCanvas.ViewChanged += PositionCanvasComments;
            toolkitCanvas.RegisterCallback<DetachFromPanelEvent>(_ => CloseCanvasCommentEditor());
        }

        private void AddCanvasCommentSettings()
        {
            var row = CreateCanvasSettingsRow();
            BindCanvasSettingsRow(row, CanvasTool.Comment);
            var list = WhimTexUI.CreateButton("Comments", ToggleCommentDrawer);
            list.name = "commentsListButton";
            row.Add(list);
            var resolved = new Toggle("Show Resolved") { value = commentShowResolved, name = "showResolvedComments" };
            resolved.RegisterValueChangedCallback(evt => { commentShowResolved = evt.newValue; RefreshCanvasComments(); });
            row.Add(resolved);
            toolkitHeaderBindings.Add(() =>
            {
                resolved.SetValueWithoutNotify(commentShowResolved);
                list.text = "Comments (" + (activeDocument?.comments?.Count ?? 0) + ")";
            });
            toolkitCanvasViewHeader.Add(row);
        }

        private Vector2 CommentViewPoint(Vector2 anchor)
        {
            Rect image = toolkitCanvas.ImageRect;
            return toolkitCanvas.ToView(new Vector2(image.x + anchor.x * image.width, image.yMax - anchor.y * image.height));
        }

        private Vector2 CommentAnchor(Vector2 point)
        {
            Rect image = toolkitCanvas.ImageRect;
            Vector2 canvas = toolkitCanvas.ToCanvas(point);
            return new Vector2((canvas.x - image.x) / image.width, (image.yMax - canvas.y) / image.height);
        }

        private void RefreshCanvasComments()
        {
            commentToolIcon?.SetHasComments((activeDocument?.comments?.Count ?? 0) > 0);
            RefreshCommentList();
            if (commentOverlay == null) return;
            bool visible = canvasTool == CanvasTool.Comment && activeDocument != null;
            commentOverlay.EnableInClassList("whimtex-hidden", !visible);
            if (commentDocument != null && (commentDocument != activeDocument || !visible ||
                commentId != null && activeDocument.FindComment(commentId) == null)) CloseCanvasCommentEditor();
            if (!visible) { commentManipulator?.Cancel(); return; }
            var remaining = new HashSet<string>(commentPins.Keys);
            if (activeDocument.comments != null)
                for (int i = 0; i < activeDocument.comments.Count; i++)
                {
                    var item = activeDocument.comments[i];
                    if (item.resolved && !commentShowResolved) continue;
                    remaining.Remove(item.id);
                    if (!commentPins.TryGetValue(item.id, out var pin))
                    {
                        pin = new CommentPin();
                        pin.AddToClassList("whimtex-comment-pin");
                        commentPins.Add(item.id, pin); commentOverlay.Add(pin);
                    }
                    pin.text = (i + 1).ToString();
                    pin.Color = item.color;
                    pin.EnableInClassList("whimtex-comment-pin--resolved", item.resolved);
                    pin.EnableInClassList("whimtex-comment-pin--selected", commentId == item.id);
                    pin.MarkDirtyRepaint();
                }
            foreach (string id in remaining) { commentPins[id].RemoveFromHierarchy(); commentPins.Remove(id); }
            if (commentDocument != null) RefreshCommentMessages();
            PositionCanvasComments();
        }

        private void PositionCanvasComments()
        {
            if (toolkitCanvas == null || activeDocument == null) return;
            bool pending = commentManipulator?.IsDragging == true && commentManipulator.MovingId == null;
            bool draft = commentCard != null && commentDocument == activeDocument && commentId == null;
            if (commentDraftPin != null)
            {
                commentDraftPin.Color = commentDraftColor;
                commentDraftPin.EnableInClassList("whimtex-hidden", !pending && !draft);
                Vector2 point = CommentViewPoint(pending ? commentManipulator.PendingAnchor : commentDraftAnchor);
                commentDraftPin.style.translate = new Translate(point.x - CommentPin.Anchor.x, point.y - CommentPin.Anchor.y);
            }
            foreach (var entry in commentPins)
            {
                var item = activeDocument.FindComment(entry.Key);
                if (item == null) continue;
                Vector2 anchor = commentManipulator?.MovingId == item.id ? commentManipulator.PendingAnchor : item.anchor;
                Vector2 point = CommentViewPoint(anchor);
                entry.Value.style.translate = new Translate(point.x - CommentPin.Anchor.x, point.y - CommentPin.Anchor.y);
            }
            if (commentCard == null || commentDocument != activeDocument) return;
            var comment = commentId == null ? null : activeDocument.FindComment(commentId);
            Vector2 local = CommentViewPoint(comment?.anchor ?? commentDraftAnchor);
            Vector2 position = rootVisualElement.WorldToLocal(toolkitCanvas.LocalToWorld(local));
            Rect bounds = rootVisualElement.contentRect;
            float width = Mathf.Min(296f, Mathf.Max(120f, bounds.width - 16f));
            commentCard.style.width = width;
            commentCard.style.maxHeight = Mathf.Max(80f, bounds.height - 16f);
            float height = float.IsNaN(commentCard.layout.height) ? 300f : commentCard.layout.height;
            float x = position.x + 18f;
            if (x + width > bounds.xMax - 8f) x = position.x - width - 18f;
            x = Mathf.Clamp(x, bounds.xMin + 8f, Mathf.Max(bounds.xMin + 8f, bounds.xMax - width - 8f));
            float y = Mathf.Clamp(position.y - 12f, bounds.yMin + 8f, Mathf.Max(bounds.yMin + 8f, bounds.yMax - height - 8f));
            if (commentsExpanded && commentDrawer?.resolvedStyle.display == DisplayStyle.Flex)
            {
                Rect drawer = rootVisualElement.WorldToLocal(commentDrawer.worldBound);
                if (drawer.Overlaps(new Rect(x, y, width, height))) x = Mathf.Max(bounds.xMin + 8f, drawer.xMin - width - 8f);
            }
            commentCard.style.left = x;
            commentCard.style.top = y;
        }

        private void OpenCanvasComment(string id, Vector2 anchor)
        {
            if (activeDocument == null) return;
            if (commentCard != null && !string.IsNullOrWhiteSpace(commentInput.value) && (id == null || commentId != id))
            { ShowNotification(new GUIContent("Post or cancel the current comment first.")); return; }
            if (commentCard != null && commentId == id && id != null) { commentInput.Focus(); return; }
            CloseCanvasCommentEditor();
            commentDocument = activeDocument; commentId = id; commentDraftAnchor = anchor; commentDraftColor = WhimTexComment.DefaultColor;
            commentCard = new VisualElement { name = "commentEditor" };
            commentCard.AddToClassList("whimtex-comment-card");
            commentCard.EnableInClassList("whimtex-comment-card--light", !EditorGUIUtility.isProSkin);
            var header = new VisualElement(); header.AddToClassList("whimtex-comment-heading");
            commentTitle = new Label(); header.Add(commentTitle);
            commentColor = WhimTexUI.ConfigureField(new WhimTexColorField
            {
                name = "commentColor", tooltip = "Comment color", hdr = false, showAlpha = false,
                showEyeDropper = false, Range = WhimTexColorRange.StandardOnly,
                value = activeDocument.FindComment(id)?.color ?? commentDraftColor
            });
            commentColor.AddToClassList("whimtex-comment-color");
            commentColor.RegisterValueChangedCallback(evt =>
            {
                if (commentDocument != activeDocument) return;
                Color color = evt.newValue; color.a = 1;
                if (commentId == null) { commentDraftColor = color; PositionCanvasComments(); }
                else ExecuteContextChange("Change Comment Color", () => activeDocument.SetCommentColor(commentId, color));
            });
            header.Add(commentColor);
            header.Add(WhimTexUI.CreateButton("×", CloseCanvasCommentEditor));
            commentCard.Add(header);
            commentMessages = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            commentMessages.AddToClassList("whimtex-comment-messages"); commentCard.Add(commentMessages);
            commentInput = new TextField { name = "commentText", multiline = true, maxLength = WhimTexDocument.MaximumCommentTextLength };
            commentInput.AddToClassList("whimtex-comment-input");
            commentInput.RegisterValueChangedCallback(_ => commentPost.SetEnabled(!string.IsNullOrWhiteSpace(commentInput.value)));
            commentInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { CloseCanvasCommentEditor(); WhimTexUI.ConsumeEvent(evt); }
                else if ((evt.ctrlKey || evt.commandKey) && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                { PostCanvasComment(); WhimTexUI.ConsumeEvent(evt); }
            }, TrickleDown.TrickleDown);
            commentCard.Add(commentInput);
            var actions = new VisualElement(); actions.AddToClassList("whimtex-comment-actions");
            commentPost = WhimTexUI.CreateButton("Post", PostCanvasComment); commentPost.SetEnabled(false); actions.Add(commentPost);
            commentResolve = WhimTexUI.CreateButton("Resolve", () =>
            {
                var item = commentDocument?.FindComment(commentId); if (item == null) return;
                ExecuteContextChange("Resolve Comment", () => activeDocument.SetCommentResolved(item.id, !item.resolved));
                CloseCanvasCommentEditor(); RefreshCanvasComments();
            });
            actions.Add(commentResolve);
            commentDelete = WhimTexUI.CreateButton("Delete", () =>
            {
                string remove = commentId;
                if (commentDocument != activeDocument || remove == null) return;
                CloseCanvasCommentEditor();
                ExecuteContextChange("Delete Comment", () => activeDocument.RemoveComment(remove));
            });
            actions.Add(commentDelete); commentCard.Add(actions);
            rootVisualElement.Add(commentCard);
            commentCard.RegisterCallback<GeometryChangedEvent>(_ => PositionCanvasComments());
            RefreshCanvasComments();
            var input = commentInput;
            input.schedule.Execute(() => { if (commentInput == input && input.panel != null) input.Focus(); });
        }

        private void RefreshCommentMessages()
        {
            if (commentMessages == null) return;
            var item = commentId == null ? null : commentDocument.FindComment(commentId);
            commentColor.SetValueWithoutNotify(item?.color ?? commentDraftColor);
            commentTitle.text = item == null ? "New Comment" : item.resolved ? "Resolved Comment" : "Comment";
            commentResolve.style.display = commentDelete.style.display = item == null ? DisplayStyle.None : DisplayStyle.Flex;
            commentResolve.text = item?.resolved == true ? "Reopen" : "Resolve";
            commentMessages.Clear();
            if (item == null) return;
            foreach (var message in item.messages)
            {
                var row = new VisualElement(); row.AddToClassList("whimtex-comment-message");
                var heading = new VisualElement(); heading.AddToClassList("whimtex-comment-heading");
                var byline = new Label(message.author + " · " + new DateTime(message.createdUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g"));
                byline.AddToClassList("whimtex-comment-byline"); heading.Add(byline);
                string messageId = message.id;
                heading.Add(WhimTexUI.CreateButton("Edit", () =>
                {
                    if (!string.IsNullOrWhiteSpace(commentInput.value) && commentMessageId != messageId)
                    { ShowNotification(new GUIContent("Post or cancel the current comment first.")); return; }
                    commentMessageId = messageId; commentInput.value = message.text; commentPost.text = "Save"; commentInput.Focus();
                }));
                row.Add(heading);
                var text = new Label(message.text) { enableRichText = false }; text.AddToClassList("whimtex-comment-body"); row.Add(text);
                commentMessages.Add(row);
            }
        }

        private void PostCanvasComment()
        {
            if (commentDocument == null || commentDocument != activeDocument || string.IsNullOrWhiteSpace(commentInput?.value)) return;
            string text = commentInput.value.Trim(), id = commentId, edit = commentMessageId;
            if (edit != null && activeDocument.FindComment(id)?.messages.Find(message => message.id == edit) == null)
            { ShowNotification(new GUIContent("This message no longer exists.")); return; }
            string postedId = id;
            bool posted = ExecuteContextChange(id == null ? "Add Comment" : edit == null ? "Reply to Comment" : "Edit Comment", () =>
            {
                if (id == null) postedId = activeDocument.AddComment(commentDraftAnchor, text, color: commentDraftColor).id;
                else if (edit == null) activeDocument.AddCommentMessage(id, text);
                else activeDocument.FindComment(id).messages.Find(message => message.id == edit).text = text;
            });
            if (!posted || commentInput == null) return;
            commentId = postedId;
            commentInput.value = string.Empty; commentMessageId = null; commentPost.text = "Post";
            RefreshCanvasComments(); commentInput.Focus();
        }

        private void CloseCanvasCommentEditor()
        {
            commentCard?.RemoveFromHierarchy(); commentCard = null; commentMessages = null; commentInput = null;
            commentDocument = null; commentId = null; commentMessageId = null; commentColor = null;
            foreach (var pin in commentPins.Values) { pin.RemoveFromClassList("whimtex-comment-pin--selected"); pin.MarkDirtyRepaint(); }
            RefreshCommentList();
            PositionCanvasComments();
        }

        private bool HandleCanvasCommentKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || (commentCard == null && commentManipulator?.IsDragging != true)) return false;
            commentManipulator?.Cancel(); CloseCanvasCommentEditor(); toolkitCanvas?.Focus();
            WhimTexUI.ConsumeEvent(evt); return true;
        }

        private sealed class CommentPin : VisualElement
        {
            internal static readonly Vector2 Anchor = new Vector2(6, 25);
            private readonly Label number;
            private Color color = WhimTexComment.DefaultColor;
            internal Color Color
            {
                set
                {
                    if (color.Equals(value)) return;
                    color = value;
                    number.style.color = value.grayscale > .55f ? UnityEngine.Color.black : UnityEngine.Color.white;
                    MarkDirtyRepaint();
                }
            }
            internal string text { get => number.text; set { number.text = value; number.style.fontSize = value.Length > 3 ? 8 : 10; } }
            internal CommentPin()
            {
                pickingMode = PickingMode.Ignore;
                number = new Label { pickingMode = PickingMode.Ignore };
                number.AddToClassList("whimtex-comment-pin-number"); Add(number);
                generateVisualContent += Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                bool selected = ClassListContains("whimtex-comment-pin--selected");
                bool resolved = ClassListContains("whimtex-comment-pin--resolved");
                var p = context.painter2D;
                Color fill = resolved ? UnityEngine.Color.Lerp(color, new Color(.32f, .32f, .32f), .45f) : color;
                p.fillColor = fill;
                p.strokeColor = selected ? UnityEngine.Color.white : UnityEngine.Color.Lerp(fill, UnityEngine.Color.white, .65f);
                p.lineWidth = selected ? 2 : 1;
                p.BeginPath(); p.MoveTo(new Vector2(4.5f, .5f)); p.LineTo(new Vector2(21.5f, .5f));
                p.BezierCurveTo(new Vector2(24, .5f), new Vector2(25.5f, 2), new Vector2(25.5f, 4.5f));
                p.LineTo(new Vector2(25.5f, 15.5f));
                p.BezierCurveTo(new Vector2(25.5f, 18), new Vector2(24, 19.5f), new Vector2(21.5f, 19.5f));
                p.LineTo(new Vector2(12.5f, 19.5f)); p.LineTo(Anchor); p.LineTo(new Vector2(6, 19.5f));
                p.LineTo(new Vector2(4.5f, 19.5f));
                p.BezierCurveTo(new Vector2(2, 19.5f), new Vector2(.5f, 18), new Vector2(.5f, 15.5f));
                p.LineTo(new Vector2(.5f, 4.5f));
                p.BezierCurveTo(new Vector2(.5f, 2), new Vector2(2, .5f), new Vector2(4.5f, .5f));
                p.ClosePath(); p.Fill(); p.Stroke();
            }
        }

        private sealed class CommentManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private WhimTexDocument document;
            private int pointer = -1;
            private Vector2 down, original, downAnchor;
            private bool moved;
            internal string MovingId { get; private set; }
            internal Vector2 PendingAnchor { get; private set; }
            internal bool IsDragging => pointer >= 0;
            internal CommentManipulator(WhimTexWindow owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted); target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<GeometryChangedEvent>(Geometry);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel(); target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted); target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<GeometryChangedEvent>(Geometry);
                owner.toolkitCanvas.ViewChanged -= owner.PositionCanvasComments;
            }
            private void Down(PointerDownEvent evt)
            {
                if (owner.canvasTool != CanvasTool.Comment || owner.activeDocument == null || evt.button != 0 || evt.altKey ||
                    IsDragging || owner.toolkitCanvas.ImageRect.width <= 0f) return;
                Vector2 point = target.WorldToLocal(evt.position);
                if (!target.contentRect.Contains(point)) return;
                string hit = null;
                var comments = owner.activeDocument.comments;
                for (int i = comments.Count - 1; i >= 0; i--)
                    if ((!comments[i].resolved || owner.commentShowResolved) &&
                        new Rect(-2, -2, 30, 30).Contains(point - owner.CommentViewPoint(comments[i].anchor) + CommentPin.Anchor))
                    { hit = comments[i].id; break; }
                Vector2 anchor = owner.CommentAnchor(point);
                if (hit == null && (anchor.x < 0f || anchor.x > 1f || anchor.y < 0f || anchor.y > 1f)) return;
                document = owner.activeDocument; pointer = evt.pointerId; MovingId = hit; down = point; downAnchor = anchor; moved = false;
                PendingAnchor = original = hit == null ? anchor : document.FindComment(hit).anchor;
                owner.PositionCanvasComments();
                target.CapturePointer(pointer); target.Focus(); WhimTexUI.ConsumeEvent(evt);
            }
            private bool Valid => pointer >= 0 && target.HasPointerCapture(pointer) && document != null && document == owner.activeDocument && owner.canvasTool == CanvasTool.Comment &&
                (MovingId == null || document.FindComment(MovingId) != null);
            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer) return;
                if (!Valid) { Cancel(); return; }
                Vector2 point = target.WorldToLocal(evt.position);
                moved |= (point - down).sqrMagnitude >= 16f;
                if (moved) PendingAnchor = original + owner.CommentAnchor(point) - downAnchor;
                owner.PositionCanvasComments(); WhimTexUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                string id = MovingId; Vector2 anchor = PendingAnchor; bool commit = moved; bool valid = Valid;
                Cancel();
                if (valid)
                {
                    if (id != null && commit) owner.ExecuteContextChange("Move Comment", () => owner.activeDocument.FindComment(id).anchor = anchor);
                    owner.OpenCanvasComment(id, anchor);
                }
                WhimTexUI.ConsumeEvent(evt);
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1; MovingId = null; document = null;
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                owner.PositionCanvasComments();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
        }
    }
}
