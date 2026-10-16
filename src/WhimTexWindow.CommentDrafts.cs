using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        // Editor-only state: never stored in the document, exported, or shared with an agent.
        [Serializable]
        private sealed class CommentEditorState
        {
            public WhimTexDocument document;
            public string id, text, messageId, editingSnapshot;
            public long draftCreatedUtcTicks;
            public Vector2 anchor;
            public Color color;
            public bool automaticColor = true, positioned, sized, open, focused, composing;
            public Rect rect;
            [SerializeReference] public List<WhimTexCommentAttachment> attachments = new();
            [NonSerialized] private string parsedSnapshot;
            [NonSerialized] private WhimTexCommentMessage originalMessage;
            public bool IsEditing => id == null || composing || messageId != null;
            public bool HasDraft => !string.IsNullOrEmpty(text) || attachments?.Count > 0 || !string.IsNullOrEmpty(messageId) ||
                string.IsNullOrEmpty(id) && (!automaticColor || positioned || sized);

            internal WhimTexCommentMessage OriginalMessage
            {
                get
                {
                    if (parsedSnapshot != editingSnapshot)
                    {
                        originalMessage = string.IsNullOrEmpty(editingSnapshot) ? null : JsonUtility.FromJson<WhimTexCommentMessage>(editingSnapshot);
                        parsedSnapshot = editingSnapshot;
                    }
                    return originalMessage;
                }
            }

            internal void BeginReply()
            {
                if (!composing) draftCreatedUtcTicks = DateTime.UtcNow.Ticks;
                composing = true;
            }

            internal void BeginEdit(WhimTexCommentMessage message)
            {
                messageId = message.id; editingSnapshot = JsonUtility.ToJson(message);
                text = message.text; attachments = WhimTexCommentAttachment.Copy(message.attachments);
                composing = true;
            }

            internal void ClearDraft()
            {
                text = messageId = editingSnapshot = parsedSnapshot = null;
                originalMessage = null; attachments.Clear();
                composing = false; draftCreatedUtcTicks = 0;
            }
        }

        [SerializeField] private List<CommentEditorState> commentEditorStates = new();

        private CommentEditorState FindCommentEditorState(WhimTexDocument document, string id) =>
            commentEditorStates.Find(state => state.document == document && (state.id ?? "") == (id ?? ""));

        private void CaptureCommentDraft(CommentEditorView editor)
        {
            if (editor.input != null) editor.state.text = editor.input.value;
        }

        private static void NormalizeCommentDraft(CommentEditorState state)
        {
            if (string.IsNullOrEmpty(state.id)) state.id = null;
            if (string.IsNullOrEmpty(state.messageId)) state.messageId = null;
            state.attachments ??= new();
            if (state.messageId == null && (!string.IsNullOrWhiteSpace(state.text) || state.attachments.Count > 0)) state.composing = true;
            if (state.draftCreatedUtcTicks == 0 && (state.id == null || state.composing)) state.draftCreatedUtcTicks = DateTime.UtcNow.Ticks;
        }

        private CommentEditorState RestoreCommentDraft(string id, Vector2 anchor)
        {
            commentEditorStates.RemoveAll(state => state.document == null);
            var state = FindCommentEditorState(activeDocument, id);
            if (id == null && state != null && !state.HasDraft)
            {
                state.anchor = anchor;
                state.automaticColor = true;
                state.color = WhimTexUserSettings.CommentAuthorColor;
                state.positioned = state.sized = false;
            }
            if (state == null)
            {
                state = new CommentEditorState { document = activeDocument, id = id, anchor = anchor,
                    color = WhimTexUserSettings.CommentAuthorColor };
                commentEditorStates.Add(state);
            }
            NormalizeCommentDraft(state);
            return state;
        }

        private void CancelCanvasCommentDraft(CommentEditorView editor)
        {
            ClearCommentDraft(editor);
            if (editor.state.id == null)
            {
                editor.state.automaticColor = true;
                editor.state.positioned = editor.state.sized = false;
                DismissCanvasComment(editor); return;
            }
            RefreshCommentMessages(editor); editor.reply.Focus();
            RefreshCommentAttachmentEditor(editor); RefreshCommentList();
        }

        private static void ClearCommentDraft(CommentEditorView editor)
        {
            editor.input.SetValueWithoutNotify("");
            editor.state.ClearDraft();
            editor.post.text = "Post"; editor.post.SetEnabled(false);
        }

        private static bool CommentDraftHasChanges(CommentEditorState state)
        {
            if (state.messageId == null) return !string.IsNullOrWhiteSpace(state.text) || state.attachments.Count > 0;
            return !WhimTexCommentContent.Equals(state.text, state.attachments, state.OriginalMessage, trimText: true);
        }

        private bool TryFinishUnchangedCommentEdit(CommentEditorView editor)
        {
            CaptureCommentDraft(editor);
            if (CommentDraftHasChanges(editor.state))
            {
                FocusCommentMessageEditor(editor);
                ShowNotification(new GUIContent("Save or cancel the current draft before editing another message."));
                return false;
            }
            ClearCommentDraft(editor);
            RefreshCommentAttachmentEditor(editor);
            return true;
        }

        private bool HasCommentDraft(string id)
        {
            var editor = FindOpenCommentEditor(id);
            if (editor != null) CaptureCommentDraft(editor);
            return FindCommentEditorState(activeDocument, id)?.HasDraft == true;
        }
    }
}
