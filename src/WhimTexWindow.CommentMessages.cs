using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed class CommentMessageView
        {
            internal VisualElement row, author, separator;
            internal Label authorName, time;
            internal DateTime date;
            internal string text;
            internal List<WhimTexCommentAttachment> attachments;
            internal bool editing, initialized, used;
            internal readonly List<Action> refreshAttachments = new();
        }

        private sealed class CommentBodyLabel : Label
        {
            private readonly string layoutText;

            internal CommentBodyLabel(string value)
            {
                layoutText = (value ?? "").Replace("<", "<noparse><</noparse>");
                text = "<align=justified>" + layoutText + "</align>";
                enableRichText = true;
                selection.isSelectable = true;
            }

            protected override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode) =>
                MeasureTextSize(layoutText, desiredWidth, widthMode, desiredHeight, heightMode);
        }

        private sealed class CommentDeleteConfirmation : EditorWindow
        {
            internal static bool Confirm(WhimTexWindow owner, VisualElement anchor, string title, string message)
            {
                Rect button = anchor.worldBound;
                button.position = GUIUtility.GUIToScreenPoint(button.position);
                Rect bounds = owner.rootVisualElement.worldBound;
                bounds.position = GUIUtility.GUIToScreenPoint(bounds.position);
                var size = new Vector2(340, 172);
                float x = Mathf.Clamp(button.xMax - size.x, bounds.xMin + 4, Mathf.Max(bounds.xMin + 4, bounds.xMax - size.x - 4));
                float y = button.yMax + 30;
                if (y + size.y > bounds.yMax - 4) y = button.yMin - size.y - 8;
                y = Mathf.Clamp(y, bounds.yMin + 26, Mathf.Max(bounds.yMin + 26, bounds.yMax - size.y - 4));
                var rect = new Rect(new Vector2(x, y), size);
                bool confirmed = false;
                var window = CreateInstance<CommentDeleteConfirmation>();
                try
                {
                    window.hideFlags = HideFlags.HideAndDontSave;
                    window.titleContent = new GUIContent(title);
                    window.minSize = window.maxSize = size;
                    window.position = rect;
                    var root = window.rootVisualElement;
                    WhimTexUI.ApplyWindowStyles(root);
                    root.AddToClassList("whimtex-comment-confirmation");
                    var text = new Label(message);
                    text.AddToClassList("whimtex-comment-confirmation-message");
                    root.Add(text);
                    var actions = new VisualElement();
                    actions.AddToClassList("whimtex-comment-confirmation-actions");
                    var cancel = new Button(window.Close) { text = "Cancel", name = "cancelCommentDeletion" };
                    var delete = new Button(() => { confirmed = true; window.Close(); })
                        { text = "Delete", name = "confirmCommentDeletion" };
                    actions.Add(cancel); actions.Add(delete); root.Add(actions);
                    root.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode != KeyCode.Escape) return;
                        WhimTexUI.ConsumeEvent(evt); window.Close();
                    }, TrickleDown.TrickleDown);
                    window.ShowUtility();
                    window.position = rect;
                    cancel.Focus();
                    window.ShowModalUtility();
                    return confirmed;
                }
                finally
                {
                    if (window != null) window.Close();
                }
            }
        }

        private void AddCommentMessageMenu(CommentEditorView editor, VisualElement heading, WhimTexComment item, WhimTexCommentMessage message)
        {
            var menu = new ToolbarMenu { name = "commentMessageMenu", tooltip = "Message actions", userData = message.id };
            menu.AddToClassList("whimtex-comment-message-menu");
            for (int i = 0; i < 3; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("whimtex-layer-menu-dot");
                menu.Add(dot);
            }
            BuildCommentMessageMenu(menu.menu, editor, item, message);
            heading.Add(menu);
            heading.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                var currentItem = editor.state.document?.FindComment(editor.state.id);
                var current = currentItem?.messages.Find(value => value.id == message.id);
                if (current != null) BuildCommentMessageMenu(evt.menu, editor, currentItem, current);
                evt.StopPropagation();
            }));
        }

        private void BuildCommentMessageMenu(DropdownMenu menu, CommentEditorView editor, WhimTexComment item, WhimTexCommentMessage message)
        {
            menu.AppendAction("Copy Text", _ =>
            {
                var current = editor.state.document?.FindComment(editor.state.id)?.messages.Find(value => value.id == message.id);
                if (current != null) EditorGUIUtility.systemCopyBuffer = current.text;
            });
            menu.AppendAction("Edit", _ => EditCanvasCommentMessage(editor, message.id));
            if (item.messages[0] != message)
            {
                menu.AppendSeparator();
                menu.AppendAction("Delete Reply", _ => DeleteCanvasCommentReply(editor, message.id));
            }
        }

        private void EditCanvasCommentMessage(CommentEditorView editor, string messageId)
        {
            var message = editor.state.document?.FindComment(editor.state.id)?.messages.Find(value => value.id == messageId);
            if (message == null || editor.input == null) return;
            if (editor.state.messageId == messageId) { FocusCommentMessageEditor(editor); return; }
            if (!TryFinishUnchangedCommentEdit(editor)) return;
            editor.state.BeginEdit(message);
            RefreshCommentAttachmentEditor(editor);
            editor.input.value = message.text; editor.post.text = "Save"; FocusCommentMessageEditor(editor);
        }

        private void RegisterCommentMessageEditing(CommentEditorView editor, VisualElement row, string messageId)
        {
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2 || editor.state.messageId == messageId) return;
                for (var target = evt.target as VisualElement; target != null && target != row; target = target.parent)
                    if (target is Button || target is ToolbarMenu || target is TextField ||
                        target.ClassListContains("whimtex-comment-attachments")) return;
                EditCanvasCommentMessage(editor, messageId);
                WhimTexUI.ConsumeEvent(evt);
            }, TrickleDown.TrickleDown);
        }

        private void DeleteCanvasComment(CommentEditorView editor)
        {
            var document = editor.state.document;
            string id = editor.state.id;
            if (document == null || document != activeDocument || document.FindComment(id) == null) return;
            if (!ExecuteContextChange("Delete Comment", () => document.RemoveComment(id))) return;
            CancelCanvasCommentDraft(editor); DismissCanvasComment(editor);
            commentEditorStates.RemoveAll(state => state.document == document && state.id == id);
        }

        private void DeleteCanvasCommentReply(CommentEditorView editor, string messageId)
        {
            if (editor.state.document != activeDocument || editor.state.id == null) return;
            string id = editor.state.id;
            bool removed = ExecuteContextChange("Delete Comment Reply", () => activeDocument.RemoveCommentReply(id, messageId));
            if (removed && editor.state.messageId == messageId) CancelCanvasCommentDraft(editor);
            RefreshCanvasComments();
        }
    }
}
