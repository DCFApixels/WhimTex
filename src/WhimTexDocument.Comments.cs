using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class WhimTexCommentMessage
    {
        public string id;
        public string author = "Me";
        public string text;
        public long createdUtcTicks;
    }

    [Serializable]
    public sealed class WhimTexComment
    {
        public static readonly Color DefaultColor = new Color32(48, 96, 133, 255);
        public string id;
        // Bottom-left canvas UV. Crop may place retained comments outside the canvas.
        public Vector2 anchor;
        public Color color = DefaultColor;
        public bool resolved;
        public List<WhimTexCommentMessage> messages = new List<WhimTexCommentMessage>();
    }

    public sealed partial class WhimTexDocument
    {
        public const int MaximumCommentThreads = 4096;
        public const int MaximumCommentMessages = 128;
        public const int MaximumCommentTextLength = 16384;
        public const int MaximumCommentAuthorLength = 128;
        public const int MaximumCommentsBlockBytes = 16 * 1024 * 1024;

        // Unity serializes this list for Undo. TIFF stores it in a separate optional block.
        public List<WhimTexComment> comments = new List<WhimTexComment>();
        private static readonly UTF8Encoding CommentEncoding = new UTF8Encoding(false, true);

        public WhimTexComment FindComment(string id)
        {
            if (string.IsNullOrEmpty(id) || comments == null) return null;
            foreach (WhimTexComment comment in comments)
                if (comment != null && string.Equals(comment.id, id, StringComparison.Ordinal)) return comment;
            return null;
        }

        /// <summary>Initializes absent optional state; malformed committed data is never repaired.</summary>
        public void NormalizeComments()
        {
            ValidateComments();
            comments ??= new List<WhimTexComment>();
        }

        public void ValidateComments() => ValidateCommentList(comments);

        /// <summary>Adds a committed thread. The caller owns Undo and change notification.</summary>
        public WhimTexComment AddComment(Vector2 anchor, string text, string author = "Me", Color? color = null)
        {
            ValidateComments();
            var comment = new WhimTexComment
            {
                id = Guid.NewGuid().ToString("N"), anchor = anchor, color = color ?? WhimTexComment.DefaultColor,
                messages = new List<WhimTexCommentMessage> { NewCommentMessage(text, author) }
            };
            var candidate = comments == null ? new List<WhimTexComment>() : new List<WhimTexComment>(comments);
            candidate.Add(comment);
            ValidateCommentList(candidate);
            comments ??= new List<WhimTexComment>();
            comments.Add(comment);
            return comment;
        }

        /// <summary>Adds a committed reply. The caller owns Undo and change notification.</summary>
        public WhimTexCommentMessage AddCommentMessage(string commentId, string text, string author = "Me")
        {
            ValidateComments();
            WhimTexComment comment = FindComment(commentId);
            if (comment == null) throw new ArgumentException("The comment no longer belongs to this document.", nameof(commentId));
            WhimTexCommentMessage message = NewCommentMessage(text, author);
            var replacement = new WhimTexComment
            {
                id = comment.id, anchor = comment.anchor, color = comment.color, resolved = comment.resolved,
                messages = new List<WhimTexCommentMessage>(comment.messages) { message }
            };
            var candidate = new List<WhimTexComment>(comments);
            candidate[candidate.IndexOf(comment)] = replacement;
            ValidateCommentList(candidate);
            comment.messages.Add(message);
            return message;
        }

        public bool RemoveComment(string id)
        {
            ValidateComments();
            WhimTexComment comment = FindComment(id);
            return comment != null && comments.Remove(comment);
        }

        public bool SetCommentResolved(string id, bool resolved)
        {
            ValidateComments();
            WhimTexComment comment = FindComment(id);
            if (comment == null || comment.resolved == resolved) return false;
            comment.resolved = resolved;
            return true;
        }

        public bool SetCommentColor(string id, Color color)
        {
            ValidateComments(); ValidateCommentColor(color);
            WhimTexComment comment = FindComment(id);
            if (comment == null || comment.color.Equals(color)) return false;
            comment.color = color;
            return true;
        }

        private static void ValidateCommentColor(Color color)
        {
            if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) ||
                color.r < 0 || color.r > 1 || color.g < 0 || color.g > 1 || color.b < 0 || color.b > 1 || color.a != 1)
                throw CommentError("Marker colors require RGB values in 0..1 and alpha 1.");
        }

        private static WhimTexCommentMessage NewCommentMessage(string text, string author) => new WhimTexCommentMessage
        {
            id = Guid.NewGuid().ToString("N"), text = text,
            author = string.IsNullOrWhiteSpace(author) ? "Me" : author,
            createdUtcTicks = DateTime.UtcNow.Ticks
        };

        internal static void ValidateCommentList(List<WhimTexComment> list)
        {
            if (list == null) return;
            if (list.Count > MaximumCommentThreads) throw CommentError("There can be at most 4096 threads.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long textBytes = 0;
            void Text(string value, int maximum, string label)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
                    throw CommentError(label + " must be nonblank and at most " + maximum + " characters.");
                try { textBytes += CommentEncoding.GetByteCount(value); }
                catch (EncoderFallbackException) { throw CommentError(label + " contains invalid Unicode."); }
                if (textBytes > MaximumCommentsBlockBytes) throw CommentError("The comments block exceeds 16 MiB.");
            }
            void Id(string value)
            {
                Text(value, int.MaxValue, "Comment ID");
                if (!ids.Add(value)) throw CommentError("Thread and message IDs must be unique.");
            }
            foreach (WhimTexComment comment in list)
            {
                if (comment == null) throw CommentError("A thread cannot be null.");
                Id(comment.id);
                if (!float.IsFinite(comment.anchor.x) || !float.IsFinite(comment.anchor.y)) throw CommentError("Anchors must be finite canvas UV coordinates.");
                ValidateCommentColor(comment.color);
                if (comment.messages == null || comment.messages.Count < 1 || comment.messages.Count > MaximumCommentMessages)
                    throw CommentError("A committed thread must contain 1..128 messages.");
                foreach (WhimTexCommentMessage message in comment.messages)
                {
                    if (message == null) throw CommentError("A message cannot be null.");
                    Id(message.id);
                    Text(message.author, MaximumCommentAuthorLength, "Comment author");
                    Text(message.text, MaximumCommentTextLength, "Comment text");
                    if (message.createdUtcTicks < DateTime.MinValue.Ticks || message.createdUtcTicks > DateTime.MaxValue.Ticks)
                        throw CommentError("Message timestamps must contain valid UTC ticks.");
                }
            }
        }

        private static WhimTexDocumentException CommentError(string message) => new WhimTexDocumentException("Comments: " + message);

        private CanvasCommentAnchor[] PrepareCommentAnchors(ProjectiveMatrix canvasToNext)
        {
            ValidateComments();
            if (comments == null || comments.Count == 0 || canvasToNext.Equals(ProjectiveMatrix.Identity)) return Array.Empty<CanvasCommentAnchor>();
            var result = new CanvasCommentAnchor[comments.Count];
            for (int i = 0; i < comments.Count; i++)
            {
                WhimTexComment comment = comments[i];
                if (!canvasToNext.TryPoint(new Double2(comment.anchor.x, comment.anchor.y), out var point))
                    throw CommentError("Cannot map an anchor through the canvas change.");
                var anchor = new Vector2((float)point.x, (float)point.y);
                if (!float.IsFinite(anchor.x) || !float.IsFinite(anchor.y)) throw CommentError("The transformed anchor exceeds its supported range.");
                result[i] = new CanvasCommentAnchor(comment, anchor);
            }
            return result;
        }

        private readonly struct CanvasCommentAnchor
        {
            internal readonly WhimTexComment comment;
            internal readonly Vector2 anchor;
            internal CanvasCommentAnchor(WhimTexComment comment, Vector2 anchor) { this.comment = comment; this.anchor = anchor; }
        }
    }
}
