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
        private CommentManipulator commentManipulator;
        private VisualElement commentOverlay;
        private CommentPin commentDraftPin;
        private readonly Dictionary<string, CommentPin> commentPins = new Dictionary<string, CommentPin>();
        private bool CanvasCommentsVisible => activeDocument != null &&
            (canvasTool == CanvasTool.Comment || commentsExpanded || HasOpenCanvasComments());
        private bool HasNavigableCanvas => activeDocument != null && (HasCanvasLayers || CanvasCommentsVisible);

        private void BuildCanvasCommentTool()
        {
            commentPins.Clear();
            commentClusters.Clear(); clusteredCommentIds.Clear();
            spareCommentClusters.Clear(); commentClusterCells.Clear(); spareCommentClusterCells.Clear();
            commentClusterCandidates.Clear(); openCommentIds.Clear();
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
            row.Add(CreateCommentFilter());
            var draft = WhimTexUI.CreateButton("Resume Draft", () => OpenCanvasComment(null, Vector2.one * .5f));
            draft.name = "resumeCommentDraft"; row.Add(draft);
            toolkitHeaderBindings.Add(() =>
            {
                list.text = "Comments (" + OpenCommentCount() + " open)";
                closeAllCommentsButton?.SetEnabled(HasOpenCanvasComments());
                draft.style.display = HasCommentDraft(null) ? DisplayStyle.Flex : DisplayStyle.None;
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

        private void SyncCommentAuthorColors()
        {
            if (activeDocument != null && activeDocument.RefreshCommentAuthorColors(
                WhimTexUserSettings.CommentAuthorId, WhimTexUserSettings.CommentAuthorColor))
            {
                temporaryDocumentDirty = true;
                activeDocument.MarkChanged();
            }
        }

        private void RefreshCanvasComments()
        {
            commentPanelIcon?.SetCommentsState((activeDocument?.comments?.Count ?? 0) > 0,
                activeDocument?.comments?.Exists(item => item.Status == WhimTexCommentStatus.AwaitingReview) == true);
            if (commentOverlay == null) return;
            bool visible = CanvasCommentsVisible;
            var query = CurrentCommentQuery();
            commentOverlay.EnableInClassList("whimtex-hidden", !visible);
            SyncCommentEditors(activeDocument != null, query);
            closeAllCommentsButton?.SetEnabled(HasOpenCanvasComments());
            RefreshCommentList(query);
            if (!visible) { commentManipulator?.Cancel(); return; }
            var remaining = new HashSet<string>(commentPins.Keys);
            if (activeDocument.comments != null)
                for (int i = 0; i < activeDocument.comments.Count; i++)
                {
                    var item = activeDocument.comments[i];
                    if (!query.Matches(item)) continue;
                    remaining.Remove(item.id);
                    if (!commentPins.TryGetValue(item.id, out var pin))
                    {
                        pin = new CommentPin();
                        pin.AddToClassList("whimtex-comment-pin");
                        commentPins.Add(item.id, pin); commentOverlay.Add(pin);
                    }
                    pin.text = item.number.ToString();
                    pin.Color = WhimTexUserSettings.GetCommentColor(item);
                    pin.EnableInClassList("whimtex-comment-pin--resolved", item.resolved);
                    pin.EnableInClassList("whimtex-comment-pin--review", item.Status == WhimTexCommentStatus.AwaitingReview);
                    pin.tooltip = "#" + item.number + " · " + CommentStatusLabels[(int)item.Status];
                    pin.EnableInClassList("whimtex-comment-pin--selected", FindOpenCommentEditor(item.id) != null);
                    pin.MarkDirtyRepaint();
                }
            foreach (string id in remaining) { commentPins[id].RemoveFromHierarchy(); commentPins.Remove(id); }
            foreach (var editor in commentEditors)
            {
                if (editor.state.automaticColor) editor.state.color = WhimTexUserSettings.CommentAuthorColor;
                RefreshCommentMessages(editor);
            }
            RefreshCommentMaskPreview();
            PositionCanvasComments();
        }

        private void PositionCanvasComments()
        {
            if (toolkitCanvas == null || activeDocument == null) return;
            bool pending = commentManipulator?.IsDragging == true && commentManipulator.MovingId == null;
            var draft = FindOpenCommentEditor(null);
            if (commentDraftPin != null)
            {
                commentDraftPin.Color = draft?.state.color ?? WhimTexUserSettings.CommentAuthorColor;
                commentDraftPin.EnableInClassList("whimtex-hidden", !pending && draft == null);
                Vector2 point = CommentViewPoint(pending ? commentManipulator.PendingAnchor : draft?.state.anchor ?? Vector2.zero);
                commentDraftPin.style.translate = new Translate(point.x - CommentPin.Anchor.x, point.y - CommentPin.Anchor.y);
            }
            foreach (var item in activeDocument.comments)
            {
                if (!commentPins.TryGetValue(item.id, out var pin)) continue;
                Vector2 anchor = commentManipulator?.MovingId == item.id ? commentManipulator.PendingAnchor : item.anchor;
                Vector2 point = CommentViewPoint(anchor);
                pin.style.translate = new Translate(point.x - CommentPin.Anchor.x, point.y - CommentPin.Anchor.y);
            }
            PositionCommentClusters();
            foreach (var editor in commentEditors) PositionCommentEditor(editor);
        }

        private void PositionCommentEditor(CommentEditorView editor)
        {
            var comment = editor.state.id == null ? null : activeDocument.FindComment(editor.state.id);
            Vector2 cardAnchor = comment != null && commentManipulator?.MovingId == comment.id
                ? commentManipulator.PendingAnchor : comment?.anchor ?? editor.state.anchor;
            Vector2 local = CommentViewPoint(cardAnchor);
            Vector2 position = rootVisualElement.WorldToLocal(toolkitCanvas.LocalToWorld(local));
            if (PositionFloatingComment(editor, position)) return;
            Rect bounds = rootVisualElement.contentRect;
            Rect size = ClampCommentCard(editor, editor.state.rect);
            float width = editor.state.sized ? size.width : Mathf.Min(296f, Mathf.Max(120f, bounds.width - 16f));
            editor.card.style.translate = new Translate(0, 0);
            editor.card.style.width = width;
            editor.card.style.height = editor.state.sized ? new StyleLength(size.height) : StyleKeyword.Auto;
            editor.card.EnableInClassList("whimtex-comment-card--sized", editor.state.sized);
            editor.card.style.maxHeight = Mathf.Max(80f, bounds.height - 16f);
            float height = editor.state.sized ? size.height : float.IsNaN(editor.card.layout.height) ? 300f : editor.card.layout.height;
            float x = position.x + 18f;
            if (x + width > bounds.xMax - 8f) x = position.x - width - 18f;
            x = Mathf.Clamp(x, bounds.xMin + 8f, Mathf.Max(bounds.xMin + 8f, bounds.xMax - width - 8f));
            float y = Mathf.Clamp(position.y - 12f, bounds.yMin + 8f, Mathf.Max(bounds.yMin + 8f, bounds.yMax - height - 8f));
            if (commentsExpanded && commentDrawer?.resolvedStyle.display == DisplayStyle.Flex)
            {
                Rect drawer = rootVisualElement.WorldToLocal(commentDrawer.worldBound);
                if (drawer.Overlaps(new Rect(x, y, width, height))) x = Mathf.Max(bounds.xMin + 8f, drawer.xMin - width - 8f);
            }
            var pinBounds = new Rect(position - CommentPin.Anchor - Vector2.one * 3f, Vector2.one * 32f);
            if (pinBounds.Overlaps(new Rect(x, y, width, height)))
            {
                float below = bounds.yMax - 8f - (pinBounds.yMax + 8f);
                float above = pinBounds.yMin - 8f - (bounds.yMin + 8f);
                bool useBelow = height <= below || height > above && below >= above;
                float available = Mathf.Max(80f, useBelow ? below : above);
                editor.card.style.maxHeight = available;
                y = useBelow ? pinBounds.yMax + 8f : pinBounds.yMin - 8f - Mathf.Min(height, available);
            }
            editor.card.style.left = x;
            editor.card.style.top = y;
            RefreshCommentConnector(editor, position, new Rect(x, y, width, height));
        }

        private CommentEditorView BuildCommentEditor(CommentEditorState state)
        {
            var editor = new CommentEditorView(state);
            commentEditors.Add(editor);
            string id = state.id;
            editor.card = new VisualElement { name = "commentEditor", userData = editor, focusable = true };
            editor.card.AddToClassList("whimtex-comment-card");
            editor.card.EnableInClassList("whimtex-comment-card--light", !EditorGUIUtility.isProSkin);
            var header = new VisualElement { name = "commentCardHeader" }; header.AddToClassList("whimtex-comment-heading");
            header.AddToClassList("whimtex-comment-card-header");
            editor.title = new Label(); header.Add(editor.title);
            var show = editor.showOnCanvas = WhimTexUI.CreateButton("", () => ShowCommentOnCanvas(editor.state.id));
            show.Add(new CommentHeaderIcon(CommentHeaderAction.Locate));
            show.name = "showCommentOnCanvas";
            show.tooltip = "Show on Canvas";
            show.style.display = id == null ? DisplayStyle.None : DisplayStyle.Flex;
            header.Add(show);
            var copyLink = editor.copyLink = WhimTexUI.CreateButton("", () => CopyCommentLink(editor.state.id));
            copyLink.Add(new CommentHeaderIcon(CommentHeaderAction.Link));
            copyLink.name = "copyCommentLink"; copyLink.tooltip = "Copy Link"; header.Add(copyLink);
            editor.color = new WhimTexColorField
            {
                name = "commentColor", tooltip = "Comment color", hdr = false, showAlpha = false,
                showEyeDropper = false, Range = WhimTexColorRange.StandardOnly,
                value = activeDocument.FindComment(id) is WhimTexComment existing ? WhimTexUserSettings.GetCommentColor(existing) : editor.state.color
            };
            editor.color.AddToClassList("whimtex-comment-color");
            editor.color.RegisterValueChangedCallback(evt =>
            {
                if (editor.state.document != activeDocument) return;
                Color color = evt.newValue; color.a = 1;
                if (editor.state.id == null)
                { editor.state.color = color; editor.state.automaticColor = false; PositionCanvasComments(); }
                else ExecuteContextChange("Change Comment Color", () => activeDocument.SetCommentColor(editor.state.id, color));
            });
            editor.color.AddManipulator(new ContextualMenuManipulator(evt => BuildCommentColorMenu(editor, evt.menu)));
            header.Add(editor.color);
            var close = WhimTexUI.CreateButton("", () => DismissCanvasComment(editor));
            close.Add(new CommentHeaderIcon(CommentHeaderAction.Close));
            close.name = "closeComment"; close.tooltip = "Close (keep draft)"; header.Add(close);
            editor.card.Add(header);
            editor.messages = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            editor.messages.contentContainer.AddToClassList("whimtex-comment-messages-content");
            editor.messages.AddToClassList("whimtex-comment-messages"); editor.card.Add(editor.messages);
            var messages = editor.messages;
            bool scrollToEnd = true;
            void ScrollAfterLayout(GeometryChangedEvent evt)
            {
                if (!scrollToEnd) return;
                messages.schedule.Execute(() =>
                {
                    if (!scrollToEnd || messages.panel == null || messages.childCount == 0) return;
                    if (editor.inlineMessage != null) RevealCommentMessageEditor(editor);
                    else messages.scrollOffset = new Vector2(0, Mathf.Max(0, messages.verticalScroller.highValue));
                    scrollToEnd = false;
                });
            }
            messages.contentContainer.RegisterCallback<GeometryChangedEvent>(ScrollAfterLayout);
            messages.contentViewport.RegisterCallback<GeometryChangedEvent>(ScrollAfterLayout);
            messages.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RevealCommentMessageEditor(editor));
            editor.reply = new Button(() => AddCanvasCommentReply(editor)) { text = "+" };
            editor.reply.name = "replyToComment";
            editor.reply.tooltip = "Add message";
            editor.reply.AddToClassList("whimtex-comment-reply");
            messages.contentViewport.Add(editor.reply);
            editor.messageEditor = new VisualElement { name = "commentMessageEditor" };
            editor.messageEditor.AddToClassList("whimtex-comment-message-editor");
            editor.messageEditor.RegisterCallback<GeometryChangedEvent>(_ => RevealCommentMessageEditor(editor));
            editor.card.Add(editor.messageEditor);
            editor.input = new TextField { name = "commentText", multiline = true, maxLength = WhimTexDocument.MaximumCommentTextLength };
            editor.input.AddToClassList("whimtex-comment-input");
            editor.input.SetValueWithoutNotify(editor.state.text ?? "");
            editor.input.RegisterValueChangedCallback(_ =>
            {
                editor.post.SetEnabled(!string.IsNullOrWhiteSpace(editor.input.value));
                toolkitHeaderBindings?.Refresh();
            });
            editor.input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { DismissCanvasComment(editor); WhimTexUI.ConsumeEvent(evt); }
                else if ((evt.ctrlKey || evt.commandKey) && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                { PostCanvasComment(editor); WhimTexUI.ConsumeEvent(evt); }
            }, TrickleDown.TrickleDown);
            editor.messageEditor.Add(editor.input);
            editor.attachments = new ScrollView(ScrollViewMode.Vertical) { name = "commentAttachmentEditor", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            editor.attachments.AddToClassList("whimtex-comment-attachment-editor");
            editor.messageEditor.Add(editor.attachments); RefreshCommentAttachmentEditor(editor);
            var actions = new VisualElement(); actions.AddToClassList("whimtex-comment-actions");
            var cancel = WhimTexUI.CreateButton("Cancel", () => CancelCanvasCommentDraft(editor));
            cancel.name = "cancelCommentDraft"; cancel.tooltip = "Discard the current draft or message edit";
            actions.Add(cancel);
            editor.post = WhimTexUI.CreateButton(editor.state.messageId == null ? "Post" : "Save", () => PostCanvasComment(editor));
            editor.post.name = "postComment";
            editor.post.SetEnabled(!string.IsNullOrWhiteSpace(editor.input.value)); actions.Add(editor.post);
            editor.status = new CommentChoiceField(this, CommentStatusLabels, 0) { name = "commentStatus" };
            editor.status.AddToClassList("whimtex-comment-status");
            editor.status.RegisterValueChangedCallback(_ =>
            {
                var item = editor.state.document?.FindComment(editor.state.id); if (item == null) return;
                if (editor.state.document != activeDocument) return;
                var status = (WhimTexCommentStatus)editor.status.index;
                ExecuteContextChange("Change Comment Status", () => activeDocument.SetCommentStatus(item.id, status));
                RefreshCanvasComments();
            });
            header.Insert(0, editor.status);
            editor.delete = WhimTexUI.CreateButton("", () =>
            {
                if (editor.state.document != activeDocument) return;
                var item = activeDocument?.FindComment(editor.state.id);
                if (item == null) return;
                int count = item.messages.Count;
                string message = "Delete this discussion and " + (count == 1 ? "its message" : "all " + count + " messages") +
                    ", including attachments?";
                if (!string.IsNullOrEmpty(editor.input.value) || editor.state.attachments.Count > 0)
                    message += "\nThe current draft will also be discarded.";
                message += "\n\nYou can restore the discussion with Undo.";
                if (CommentDeleteConfirmation.Confirm(this, editor.delete, "Delete discussion #" + item.number + "?", message))
                    DeleteCanvasComment(editor);
            });
            editor.delete.name = "deleteComment";
            editor.delete.AddToClassList("whimtex-comment-delete");
            editor.delete.Add(new CommentHeaderIcon(CommentHeaderAction.Delete));
            header.Insert(header.IndexOf(close), editor.delete);
            editor.messageEditor.Add(actions);
            RefreshCommentMessageEditor(editor);
            rootVisualElement.Add(editor.card);
            BuildFloatingComment(editor, header);
            editor.card.RegisterCallback<GeometryChangedEvent>(_ => PositionCanvasComments());
            editor.card.RegisterCallback<PointerDownEvent>(_ => ActivateCommentEditor(editor), TrickleDown.TrickleDown);
            editor.card.RegisterCallback<FocusInEvent>(_ => ActivateCommentEditor(editor));
            return editor;
        }

        private void RefreshCommentMessages(CommentEditorView editor)
        {
            if (editor.messages == null) return;
            RefreshCommentMessageEditor(editor);
            var item = editor.state.id == null ? null : editor.state.document.FindComment(editor.state.id);
            bool newMessage = CommentMessageEditing(editor) && editor.state.messageId == null;
            editor.card.EnableInClassList("whimtex-comment-card--single-message", (item?.messages.Count ?? 0) + (newMessage ? 1 : 0) == 1);
            editor.color.SetValueWithoutNotify(item == null ? editor.state.color : WhimTexUserSettings.GetCommentColor(item));
            editor.title.text = item == null ? "New Comment" : "#" + item.number;
            editor.status.style.display = editor.delete.style.display = item == null ? DisplayStyle.None : DisplayStyle.Flex;
            editor.showOnCanvas.style.display = editor.copyLink.style.display = item == null ? DisplayStyle.None : DisplayStyle.Flex;
            editor.status.SetValueWithoutNotify(CommentStatusLabels[(int)(item?.Status ?? WhimTexCommentStatus.InProgress)]);
            editor.inlineMessage = null;
            foreach (var view in editor.rows.Values) view.used = false;
            VisualElement previousRow = null;
            DateTime previousDate = default;
            int rowIndex = 0;
            void Place(WhimTexCommentMessage message, CommentMessageView view)
            {
                var created = new DateTime(message.createdUtcTicks, DateTimeKind.Utc).ToLocalTime();
                UpdateCommentMessageRow(editor, item, message, created, view);
                view.row.RemoveFromClassList("whimtex-comment-message--day-end");
                if (previousRow != null && created.Date != previousDate)
                {
                    previousRow.AddToClassList("whimtex-comment-message--day-end");
                    if (view.separator == null || view.date != created.Date)
                    {
                        view.separator?.RemoveFromHierarchy();
                        view.separator = CreateCommentDaySeparator(created);
                    }
                    PlaceCommentRow(editor.messages, rowIndex++, view.separator);
                }
                else view.separator?.RemoveFromHierarchy();
                view.date = created.Date;
                PlaceCommentRow(editor.messages, rowIndex++, view.row);
                if (view.editing) editor.inlineMessage = view.row;
                previousRow = view.row; previousDate = created.Date;
            }
            if (item != null) foreach (var message in item.messages)
            {
                if (!editor.rows.TryGetValue(message.id, out var view))
                    editor.rows.Add(message.id, view = new CommentMessageView());
                view.used = true;
                Place(message, view);
            }
            editor.staleRows.Clear();
            foreach (var entry in editor.rows)
                if (!entry.Value.used)
                {
                    entry.Value.row.RemoveFromHierarchy(); entry.Value.separator?.RemoveFromHierarchy();
                    editor.staleRows.Add(entry.Key);
                }
            foreach (string id in editor.staleRows) editor.rows.Remove(id);
            if (newMessage)
            {
                if (editor.state.draftCreatedUtcTicks == 0) editor.state.draftCreatedUtcTicks = DateTime.UtcNow.Ticks;
                editor.draftRow ??= new CommentMessageView();
                Place(new WhimTexCommentMessage { author = WhimTexUserSettings.CommentAuthorName,
                    createdUtcTicks = editor.state.draftCreatedUtcTicks }, editor.draftRow);
            }
            else if (editor.draftRow != null)
            {
                editor.draftRow.row.RemoveFromHierarchy(); editor.draftRow.separator?.RemoveFromHierarchy();
                editor.draftRow = null;
            }
            if (editor.state.messageId != null && editor.inlineMessage == null)
            {
                if (editor.missingMessage == null)
                {
                    editor.missingMessage = new VisualElement(); editor.missingMessage.AddToClassList("whimtex-comment-message");
                    var warning = new Label("This message no longer exists. Copy your draft or cancel.");
                    warning.AddToClassList("whimtex-comment-body"); editor.missingMessage.Add(warning);
                }
                if (editor.messageEditor.parent != editor.missingMessage) editor.missingMessage.Add(editor.messageEditor);
                PlaceCommentRow(editor.messages, rowIndex, editor.missingMessage);
            }
            else editor.missingMessage?.RemoveFromHierarchy();
        }

        private static void PlaceCommentRow(ScrollView messages, int index, VisualElement row)
        {
            if (index < messages.childCount && messages.ElementAt(index) == row) return;
            messages.Insert(index, row);
        }

        private void UpdateCommentMessageRow(CommentEditorView editor, WhimTexComment item, WhimTexCommentMessage message,
            DateTime created, CommentMessageView view)
        {
            bool draft = message.id == null, editing = draft || message.id == editor.state.messageId;
            if (view.row == null)
            {
                view.row = new VisualElement { userData = message.id }; view.row.AddToClassList("whimtex-comment-message");
                if (draft) view.row.name = "commentDraftMessage";
                else RegisterCommentMessageEditing(editor, view.row, message.id);
                var heading = new VisualElement(); heading.AddToClassList("whimtex-comment-heading");
                view.author = new VisualElement { name = "commentMessageAuthor", userData = message.id };
                view.author.AddToClassList("whimtex-comment-author");
                view.authorName = new Label { enableRichText = false }; view.author.Add(view.authorName); heading.Add(view.author);
                if (!draft) AddCommentMessageMenu(editor, heading, item, message);
                view.row.Add(heading);
                view.time = new Label(); view.time.AddToClassList("whimtex-comment-time"); view.row.Add(view.time);
            }
            view.authorName.text = message.author;
            view.author.style.borderLeftColor = draft ? WhimTexUserSettings.CommentAuthorColor : WhimTexUserSettings.GetCommentMessageAuthorColor(item, message);
            view.time.text = created.ToString("g") + (draft ? " · Draft" : message.editedUtcTicks == 0 ? "" : " · edited");
            view.time.tooltip = message.editedUtcTicks == 0 ? "" : "Edited " + new DateTime(message.editedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("f");
            bool changed = !view.initialized || view.editing != editing || !editing &&
                (view.text != message.text || !WhimTexCommentContent.AttachmentsEqual(view.attachments, message.attachments));
            if (changed)
            {
                for (int i = view.row.childCount - 1; i >= 2; i--)
                    if (view.row.ElementAt(i) != editor.messageEditor) view.row.RemoveAt(i);
                view.refreshAttachments.Clear();
                if (!editing)
                {
                    var text = new CommentBodyLabel(message.text); text.AddToClassList("whimtex-comment-body"); view.row.Add(text);
                    AddCommentTextLinks(view.row, message.text);
                    view.attachments = WhimTexCommentAttachment.Copy(message.attachments);
                    if (view.attachments.Count > 0) AddCommentAttachments(editor, view.row, view.attachments, false, view.refreshAttachments);
                    view.text = message.text;
                }
                view.initialized = true;
            }
            view.editing = editing;
            if (editing && editor.messageEditor.parent != view.row) view.row.Add(editor.messageEditor);
            foreach (var refresh in view.refreshAttachments) refresh();
        }

        private static VisualElement CreateCommentDaySeparator(DateTime date)
        {
            var separator = new VisualElement { pickingMode = PickingMode.Ignore };
            separator.AddToClassList("whimtex-comment-day");
            var before = new VisualElement { pickingMode = PickingMode.Ignore };
            var after = new VisualElement { pickingMode = PickingMode.Ignore };
            before.AddToClassList("whimtex-comment-day-line");
            after.AddToClassList("whimtex-comment-day-line");
            separator.Add(before);
            separator.Add(new Label(date.ToString("d MMM yyyy")) { pickingMode = PickingMode.Ignore });
            separator.Add(after);
            return separator;
        }

        private void PostCanvasComment(CommentEditorView editor)
        {
            if (editor.state.document == null || editor.state.document != activeDocument || editor.input == null) return;
            CaptureCommentDraft(editor);
            if (!CommentDraftHasChanges(editor.state)) { CancelCanvasCommentDraft(editor); return; }
            if (string.IsNullOrWhiteSpace(editor.input.value)) return;
            string text = editor.input.value.Trim(), id = editor.state.id, edit = editor.state.messageId;
            if (edit != null && activeDocument.FindComment(id)?.messages.Find(message => message.id == edit) == null)
            { ShowNotification(new GUIContent("This message no longer exists.")); return; }
            var currentMessage = edit == null ? null : activeDocument.FindComment(id).messages.Find(message => message.id == edit);
            if (edit != null && !WhimTexCommentContent.Equals(currentMessage.text, currentMessage.attachments, editor.state.OriginalMessage))
            { ShowNotification(new GUIContent("This message changed. Reopen it before editing.")); return; }
            string postedId = id;
            if (edit == null) WhimTexUserSettings.EnsureCommentAuthorName();
            bool posted = ExecuteContextChange(id == null ? "Add Comment" : edit == null ? "Reply to Comment" : "Edit Comment", () =>
            {
                if (id == null)
                {
                    var thread = activeDocument.AddComment(editor.state.anchor, text, WhimTexUserSettings.CommentAuthorName,
                        color: editor.state.automaticColor ? (Color?)null : editor.state.color, attachments: editor.state.attachments);
                    thread.colorAuthorId = WhimTexUserSettings.EnsureCommentAuthorId();
                    WhimTexUserSettings.AssignCommentMessageAuthor(thread.messages[0]);
                    if (editor.state.automaticColor)
                        activeDocument.SetCommentAuthorColor(thread.id, WhimTexUserSettings.CommentAuthorColor, thread.colorAuthorId);
                    postedId = thread.id;
                }
                else if (edit == null)
                    WhimTexUserSettings.AssignCommentMessageAuthor(activeDocument.AddCommentMessage(id, text,
                        WhimTexUserSettings.CommentAuthorName, attachments: editor.state.attachments));
                else activeDocument.EditCommentMessage(id, edit, text, editor.state.attachments);
                activeDocument.ValidateComments();
            });
            if (!posted || editor.input == null) return;
            editor.state.id = postedId;
            // A freshly posted discussion must remain discoverable in both surfaces.
            if (id == null && !MatchesCommentFilter(activeDocument.FindComment(postedId)))
            {
                ResetCommentQuery(); commentFilter = CommentFilter.Open;
                foreach (var filter in commentFilters) filter.SetValueWithoutNotify("Open");
            }
            ClearCommentDraft(editor);
            RefreshCommentAttachmentEditor(editor);
            RefreshCanvasComments(); editor.reply.Focus();
        }

        private void BuildCommentColorMenu(CommentEditorView editor, DropdownMenu menu)
        {
            var item = editor.state.document?.FindComment(editor.state.id);
            bool canReset = editor.state.document == activeDocument &&
                !(item?.useAuthorColor ?? editor.state.automaticColor);
            menu.AppendAction("Reset to Author Color", _ => ResetCanvasCommentColor(editor),
                canReset ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private void ResetCanvasCommentColor(CommentEditorView editor)
        {
            if (editor.state.document != activeDocument) return;
            if (editor.state.id == null)
            {
                editor.state.automaticColor = true; RefreshCanvasComments(); return;
            }
            var item = activeDocument.FindComment(editor.state.id);
            if (item == null) return;
            string authorId = item.colorAuthorId;
            if (string.IsNullOrEmpty(authorId) && item.messages[0].author == WhimTexUserSettings.CommentAuthorName)
                authorId = WhimTexUserSettings.EnsureCommentAuthorId();
            Color color = !string.IsNullOrEmpty(authorId) && authorId == WhimTexUserSettings.CommentAuthorId
                ? WhimTexUserSettings.CommentAuthorColor : WhimTexComment.ColorFromAuthor(item.messages[0].author);
            ExecuteContextChange("Use Comment Author Color", () => activeDocument.SetCommentAuthorColor(item.id, color, authorId));
        }

        private bool HandleCanvasCommentKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || (activeCommentEditor == null && commentManipulator?.IsDragging != true)) return false;
            var editor = FindCommentEditor(evt.target) ?? activeCommentEditor;
            if (editor?.gesture?.IsDragging == true)
            { editor.gesture.Cancel(); WhimTexUI.ConsumeEvent(evt); return true; }
            commentManipulator?.Cancel(); DismissCanvasComment(editor); toolkitCanvas?.Focus();
            WhimTexUI.ConsumeEvent(evt); return true;
        }

        private static void DrawCommentReviewBadge(Painter2D painter, Vector2 center, float radius, bool outlined = true)
        {
            painter.fillColor = CommentReviewColor;
            painter.strokeColor = new Color(.12f, .12f, .12f);
            painter.lineWidth = 1;
            painter.BeginPath(); painter.Arc(center, radius, 0, 360);
            painter.ClosePath(); painter.Fill();
            if (outlined) painter.Stroke();
        }

        private sealed class CommentPin : VisualElement
        {
            internal static readonly Vector2 Anchor = new Vector2(6, 25);
            private readonly Label number;
            private Color color = WhimTexComment.DefaultColor;
            private float highlight;
            private IVisualElementScheduledItem highlightSchedule;
            internal void Highlight()
            {
                highlightSchedule?.Pause();
                highlight = 1;
                double start = EditorApplication.timeSinceStartup;
                MarkDirtyRepaint();
                highlightSchedule = schedule.Execute(() =>
                {
                    highlight = 1 - Mathf.Clamp01(((float)(EditorApplication.timeSinceStartup - start) - .3f) / .6f);
                    MarkDirtyRepaint();
                    if (highlight == 0) highlightSchedule.Pause();
                }).Every(16);
            }
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
                RegisterCallback<DetachFromPanelEvent>(_ => { highlightSchedule?.Pause(); highlight = 0; });
            }
            private void Draw(MeshGenerationContext context)
            {
                bool selected = ClassListContains("whimtex-comment-pin--selected");
                bool resolved = ClassListContains("whimtex-comment-pin--resolved");
                var p = context.painter2D;
                Color fill = resolved ? UnityEngine.Color.Lerp(color, new Color(.32f, .32f, .32f), .45f) : color;
                p.fillColor = UnityEngine.Color.Lerp(fill, UnityEngine.Color.white, .35f * highlight);
                p.strokeColor = selected ? UnityEngine.Color.white : UnityEngine.Color.Lerp(fill, UnityEngine.Color.white, .65f);
                p.lineWidth = (selected ? 2 : 1) + 2 * highlight;
                if (ClassListContains("whimtex-comment-pin--cluster"))
                {
                    p.BeginPath(); p.MoveTo(new Vector2(6, -3)); p.LineTo(new Vector2(26, -3));
                    p.LineTo(new Vector2(29, 0)); p.LineTo(new Vector2(29, 16)); p.Stroke();
                }
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
                if (ClassListContains("whimtex-comment-pin--review"))
                    DrawCommentReviewBadge(p, new Vector2(23, 3), 3.5f);
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
                if (owner.TryOpenCommentCluster(point)) { WhimTexUI.ConsumeEvent(evt); return; }
                string hit = null;
                var comments = owner.activeDocument.comments;
                var query = owner.CurrentCommentQuery();
                for (int i = comments.Count - 1; i >= 0; i--)
                    if (!owner.clusteredCommentIds.Contains(comments[i].id) && query.Matches(comments[i]) &&
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
                    else owner.OpenCanvasComment(id, anchor);
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
