using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed class CommentEditorView
        {
            internal readonly CommentEditorState state;
            internal VisualElement card, connector, contact, messageEditor, attachments, inlineMessage;
            internal ScrollView messages;
            internal TextField input;
            internal Label title;
            internal WhimTexColorField color;
            internal Button post, delete, reply, showOnCanvas, copyLink;
            internal readonly Dictionary<string, CommentMessageView> rows = new();
            internal readonly List<string> staleRows = new();
            internal CommentMessageView draftRow;
            internal VisualElement missingMessage;
            internal CommentChoiceField status;
            internal CommentCardManipulator gesture;
            internal Vector2 lineStart, lineEnd;
            internal CommentArrowGeometry contactArrow;
            internal Color lineColor;
            internal Rect contactBounds;
            internal float contactRadius, contactOffset;
            internal bool revealMessageEditorPending;
            internal CommentEditorView(CommentEditorState state) { this.state = state; }
        }

        private readonly List<CommentEditorView> commentEditors = new();
        private CommentEditorView activeCommentEditor;

        private static bool CommentMessageEditing(CommentEditorView editor) =>
            editor.state.IsEditing;

        private void RefreshCommentMessageEditor(CommentEditorView editor)
        {
            bool editing = CommentMessageEditing(editor);
            if (!editing && editor.messageEditor.parent != editor.card) editor.card.Add(editor.messageEditor);
            editor.messageEditor.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            editor.reply.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            editor.reply.BringToFront();
        }

        private void AddCanvasCommentReply(CommentEditorView editor)
        {
            if (editor.state.document != activeDocument) return;
            if (editor.state.messageId != null && !TryFinishUnchangedCommentEdit(editor)) return;
            editor.state.BeginReply();
            FocusCommentMessageEditor(editor);
        }

        private void FocusCommentMessageEditor(CommentEditorView editor)
        {
            if (editor.state.document != activeDocument) return;
            editor.state.composing = true;
            RefreshCommentMessages(editor);
            ActivateCommentEditor(editor);
            editor.input.schedule.Execute(() =>
            {
                if (activeCommentEditor != editor || editor.input.panel == null || !CommentMessageEditing(editor)) return;
                editor.input.Focus();
                RevealCommentMessageEditor(editor);
            });
        }

        private static void RevealCommentMessageEditor(CommentEditorView editor)
        {
            if (editor.revealMessageEditorPending || !CommentMessageEditing(editor)) return;
            editor.revealMessageEditorPending = true;
            editor.messages.schedule.Execute(() =>
            {
                editor.revealMessageEditorPending = false;
                if (editor.messageEditor.panel == null || !CommentMessageEditing(editor)) return;
                var viewport = editor.messages.contentViewport.worldBound;
                var bounds = editor.messageEditor.worldBound;
                if (!(viewport.height > 0) || !float.IsFinite(bounds.height)) return;
                float delta = bounds.height > viewport.height || bounds.yMax > viewport.yMax ? bounds.yMax - viewport.yMax :
                    bounds.yMin < viewport.yMin ? bounds.yMin - viewport.yMin : 0;
                if (Mathf.Abs(delta) < .5f) return;
                var offset = editor.messages.scrollOffset;
                offset.y = Mathf.Clamp(offset.y + delta, editor.messages.verticalScroller.lowValue, editor.messages.verticalScroller.highValue);
                editor.messages.scrollOffset = offset;
            });
        }

        private CommentEditorView FindOpenCommentEditor(string id) =>
            commentEditors.Find(editor => editor.state.document == activeDocument && (editor.state.id ?? "") == (id ?? ""));

        private CommentEditorView FindCommentEditor(object target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element.userData is CommentEditorView editor) return editor;
            return null;
        }

        private void ActivateCommentEditor(CommentEditorView editor)
        {
            if (editor == null || !commentEditors.Contains(editor)) return;
            foreach (var state in commentEditorStates)
                if (state.document == editor.state.document) state.focused = ReferenceEquals(state, editor.state);
            editor.card.BringToFront();
            editor.contact?.BringToFront();
            activeCommentChoice?.BringToFront();
            if (activeCommentEditor == editor) return;
            activeCommentEditor = editor;
            foreach (var other in commentEditors)
            {
                other.state.focused = other == editor;
                other.card.EnableInClassList("whimtex-comment-card--focused", other == editor);
            }
        }

        private void ActivateTopCommentEditor()
        {
            CommentEditorView top = null;
            foreach (var editor in commentEditors)
                if (top == null || rootVisualElement.IndexOf(editor.card) > rootVisualElement.IndexOf(top.card)) top = editor;
            ActivateCommentEditor(top);
        }

        private void OnCommentOutsidePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            var clickedEditor = FindCommentEditor(evt.target);
            foreach (var editor in commentEditors)
            {
                if (editor == clickedEditor || editor.state.id == null || !CommentMessageEditing(editor)) continue;
                CaptureCommentDraft(editor);
                if (CommentDraftHasChanges(editor.state)) continue;
                ClearCommentDraft(editor);
                RefreshCommentAttachmentEditor(editor);
                RefreshCommentMessages(editor);
            }
            var draft = FindOpenCommentEditor(null);
            if (draft == null || clickedEditor == draft ||
                !string.IsNullOrWhiteSpace(draft.input.value) || draft.state.attachments.Count > 0 ||
                !draft.state.automaticColor || draft.state.positioned || draft.state.sized) return;
            // No document entry exists before Post. Discard only this untouched local draft.
            RemoveCommentEditor(draft);
            commentEditorStates.Remove(draft.state);
            if (activeCommentEditor == null) ActivateTopCommentEditor();
            // Preserve the clicked control and its event path in the other cards.
            PositionCanvasComments(); toolkitHeaderBindings?.Refresh();
        }

        private void OpenCanvasComment(string id, Vector2 anchor)
        {
            if (activeDocument == null) return;
            var editor = FindOpenCommentEditor(id);
            if (editor == null) editor = BuildCommentEditor(RestoreCommentDraft(id, anchor));
            editor.state.open = true;
            ActivateCommentEditor(editor);
            RefreshCanvasComments();
            editor.input.schedule.Execute(() =>
            {
                if (activeCommentEditor != editor || editor.card.panel == null) return;
                if (CommentMessageEditing(editor)) { editor.input.Focus(); RevealCommentMessageEditor(editor); }
                else editor.card.Focus();
            });
        }

        private void SyncCommentEditors(bool visible, WhimTexCommentQuery query)
        {
            for (int i = commentEditors.Count - 1; i >= 0; i--)
            {
                var editor = commentEditors[i];
                bool deleted = editor.state.document == null || editor.state.id != null && editor.state.document.FindComment(editor.state.id) == null;
                if (deleted) editor.state.open = editor.state.focused = false;
                if (deleted || !visible || editor.state.document != activeDocument ||
                    editor.state.id != null && !query.Matches(activeDocument.FindComment(editor.state.id)))
                    RemoveCommentEditor(editor);
            }
            if (!visible) return;
            CommentEditorView focused = activeCommentEditor;
            foreach (var state in commentEditorStates)
            {
                if (!state.open || state.document != activeDocument || FindOpenCommentEditor(state.id) != null) continue;
                if (!string.IsNullOrEmpty(state.id) && !query.Matches(activeDocument.FindComment(state.id))) continue;
                NormalizeCommentDraft(state);
                var editor = BuildCommentEditor(state);
                if (state.focused || focused == null) focused = editor;
            }
            if (focused != null) ActivateCommentEditor(focused);
            else ActivateTopCommentEditor();
        }

        private void RemoveCommentEditor(CommentEditorView editor)
        {
            CaptureCommentDraft(editor);
            if (!CommentDraftHasChanges(editor.state)) ClearCommentDraft(editor);
            editor.gesture?.Cancel();
            commentEditors.Remove(editor);
            if (activeCommentEditor == editor) activeCommentEditor = null;
            ClearCommentMaskPreview(editor);
            if (commentAttachmentDropHighlight != null && editor.card.Contains(commentAttachmentDropHighlight))
                ClearCommentAttachmentDropHighlight();
            editor.connector?.RemoveFromHierarchy();
            editor.contact?.RemoveFromHierarchy();
            editor.card.RemoveFromHierarchy();
        }

        // Suspend all views for tool/document switches and reload; preserve the open set and drafts.
        private void CloseCanvasCommentEditor()
        {
            for (int i = commentEditors.Count - 1; i >= 0; i--) RemoveCommentEditor(commentEditors[i]);
            RefreshCommentList(); PositionCanvasComments();
        }

        private bool HasOpenCanvasComments()
        {
            if (activeDocument == null) return false;
            foreach (var state in commentEditorStates)
                if (state.document == activeDocument && state.open) return true;
            return false;
        }

        private void DismissAllCanvasComments()
        {
            commentManipulator?.Cancel();
            for (int i = commentEditors.Count - 1; i >= 0; i--)
                if (commentEditors[i].state.document == activeDocument) RemoveCommentEditor(commentEditors[i]);
            foreach (var state in commentEditorStates)
                if (state.document == activeDocument)
                {
                    state.open = state.focused = state.positioned = false;
                    state.rect.position = Vector2.zero;
                }
            RefreshCanvasComments(); toolkitHeaderBindings?.Refresh();
        }

        private void DismissCanvasComment(CommentEditorView editor)
        {
            if (editor == null) return;
            bool active = activeCommentEditor == editor;
            if (commentEditors.Contains(editor)) RemoveCommentEditor(editor);
            editor.state.open = editor.state.focused = editor.state.positioned = false;
            editor.state.rect.position = Vector2.zero;
            if (active && commentEditors.Count > 0)
                ActivateTopCommentEditor();
            RefreshCanvasComments(); toolkitHeaderBindings?.Refresh();
        }
    }
}
