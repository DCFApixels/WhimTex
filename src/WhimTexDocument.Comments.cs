using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexCommentStatus { InProgress, AwaitingReview, Resolved }

    [Serializable]
    public sealed class WhimTexCommentMessage
    {
        public string id;
        public string author = "User";
        public string authorId;
        public Color authorColor;
        public string text;
        public long createdUtcTicks;
        public long editedUtcTicks;
        public List<WhimTexCommentAttachment> attachments = new List<WhimTexCommentAttachment>();
        public string agentWorkId;
        public string agentContentHash;
    }

    [Serializable]
    public sealed class WhimTexComment
    {
        public static readonly Color DefaultColor = new Color32(48, 96, 133, 255);
        public string id;
        public int number;
        // Bottom-left canvas UV. Crop may place retained comments outside the canvas.
        public Vector2 anchor;
        public Color color = DefaultColor;
        public bool useAuthorColor;
        public string colorAuthorId;
        public bool resolved;
        public bool awaitingReview;
        public WhimTexCommentStatus Status => resolved ? WhimTexCommentStatus.Resolved :
            awaitingReview ? WhimTexCommentStatus.AwaitingReview : WhimTexCommentStatus.InProgress;
        public List<WhimTexCommentMessage> messages = new List<WhimTexCommentMessage>();

        public static Color ColorFromAuthor(string author)
        {
            uint hash = 2166136261;
            unchecked
            {
                foreach (char character in string.IsNullOrWhiteSpace(author) ? "User" : author)
                    hash = (hash ^ character) * 16777619;
                hash ^= hash >> 16; hash *= 0x7feb352d;
                hash ^= hash >> 15; hash *= 0x846ca68b;
                hash ^= hash >> 16;
            }
            Color.RGBToHSV(DefaultColor, out _, out float saturation, out float value);
            return Color.HSVToRGB((hash >> 8) / 16777216f, saturation, value);
        }
    }

    public sealed partial class WhimTexDocument
    {
        public const int MaximumCommentThreads = 4096;
        public const int MaximumCommentMessages = 128;
        public const int MaximumCommentTextLength = 16384;
        public const int MaximumCommentAuthorLength = 128;
        public const int MaximumCommentAttachments = 32;
        public const int MaximumCommentsBlockBytes = 16 * 1024 * 1024;

        // Unity serializes this list for Undo. TIFF stores it in a separate optional block.
        public List<WhimTexComment> comments = new List<WhimTexComment>();
        // A high-water mark survives deletion of the last/highest numbered discussion.
        public int commentNumberSequence;
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
            EnsureCommentNumbers();
            comments ??= new List<WhimTexComment>();
            agentReceipts ??= new List<WhimTexAgentReceipt>();
        }

        public void ValidateComments()
        {
            ValidateCommentList(comments); ValidateAgentReceipts();
            if (commentNumberSequence < 0) throw CommentError("The comment number sequence cannot be negative.");
        }

        internal void EnsureCommentNumbers()
        {
            ValidateComments();
            int last = commentNumberSequence, missing = 0;
            if (comments == null) return;
            foreach (var item in comments)
            {
                last = Math.Max(last, item.number);
                if (item.number == 0) missing++;
            }
            if ((long)last + missing > int.MaxValue) throw CommentError("The comment number sequence is exhausted.");
            foreach (var item in comments) if (item.number == 0) item.number = ++last;
            commentNumberSequence = last;
        }

        /// <summary>Adds a committed thread. The caller owns Undo and change notification.</summary>
        public WhimTexComment AddComment(Vector2 anchor, string text, string author = "User", Color? color = null,
            List<WhimTexCommentAttachment> attachments = null)
        {
            EnsureCommentNumbers();
            if (commentNumberSequence == int.MaxValue) throw CommentError("The comment number sequence is exhausted.");
            var comment = new WhimTexComment
            {
                id = Guid.NewGuid().ToString("N"), number = commentNumberSequence + 1, anchor = anchor,
                color = color ?? WhimTexComment.ColorFromAuthor(author), useAuthorColor = !color.HasValue,
                messages = new List<WhimTexCommentMessage> { NewCommentMessage(text, author, attachments) }
            };
            var candidate = comments == null ? new List<WhimTexComment>() : new List<WhimTexComment>(comments);
            candidate.Add(comment);
            ValidateCommentList(candidate);
            comments ??= new List<WhimTexComment>();
            comments.Add(comment);
            commentNumberSequence = comment.number;
            return comment;
        }

        /// <summary>Adds a committed reply. The caller owns Undo and change notification.</summary>
        public WhimTexCommentMessage AddCommentMessage(string commentId, string text, string author = "User",
            List<WhimTexCommentAttachment> attachments = null)
        {
            ValidateComments();
            WhimTexComment comment = FindComment(commentId);
            if (comment == null) throw new ArgumentException("The comment no longer belongs to this document.", nameof(commentId));
            WhimTexCommentMessage message = NewCommentMessage(text, author, attachments);
            var replacement = new WhimTexComment
            {
                id = comment.id, number = comment.number, anchor = comment.anchor, color = comment.color, resolved = comment.resolved,
                useAuthorColor = comment.useAuthorColor, colorAuthorId = comment.colorAuthorId, awaitingReview = comment.awaitingReview,
                messages = new List<WhimTexCommentMessage>(comment.messages) { message }
            };
            var candidate = new List<WhimTexComment>(comments);
            candidate[candidate.IndexOf(comment)] = replacement;
            ValidateCommentList(candidate);
            comment.messages.Add(message);
            return message;
        }

        internal void EditCommentMessage(string commentId, string messageId, string text, List<WhimTexCommentAttachment> attachments)
        {
            var message = FindComment(commentId)?.messages.Find(value => value.id == messageId);
            if (message == null) throw CommentError("The message no longer belongs to this document.");
            string previousText = message.text, previousHash = message.agentContentHash;
            var previousAttachments = message.attachments;
            long previousEditTime = message.editedUtcTicks;
            try
            {
                message.text = text; message.attachments = WhimTexCommentAttachment.Copy(attachments);
                message.agentContentHash = null;
                message.editedUtcTicks = Math.Max(message.createdUtcTicks, DateTime.UtcNow.Ticks);
                ValidateComments();
            }
            catch
            {
                message.text = previousText; message.attachments = previousAttachments;
                message.agentContentHash = previousHash; message.editedUtcTicks = previousEditTime;
                throw;
            }
        }

        public bool RemoveComment(string id)
        {
            EnsureCommentNumbers();
            WhimTexComment comment = FindComment(id);
            return comment != null && comments.Remove(comment);
        }

        /// <summary>Deletes a reply without removing its discussion or initial message.</summary>
        public bool RemoveCommentReply(string commentId, string messageId)
        {
            ValidateComments();
            var item = FindComment(commentId);
            int index = item?.messages.FindIndex(message => message.id == messageId) ?? -1;
            if (index <= 0) return false;
            item.messages.RemoveAt(index);
            return true;
        }

        public bool SetCommentResolved(string id, bool resolved)
            => SetCommentStatus(id, resolved ? WhimTexCommentStatus.Resolved : WhimTexCommentStatus.InProgress);

        public bool SetCommentStatus(string id, WhimTexCommentStatus status)
        {
            ValidateComments();
            if (status < WhimTexCommentStatus.InProgress || status > WhimTexCommentStatus.Resolved)
                throw CommentError("Unknown comment status.");
            WhimTexComment comment = FindComment(id);
            if (comment == null || comment.Status == status) return false;
            comment.resolved = status == WhimTexCommentStatus.Resolved;
            comment.awaitingReview = status == WhimTexCommentStatus.AwaitingReview;
            return true;
        }

        public bool SetCommentColor(string id, Color color)
        {
            ValidateComments(); ValidateCommentColor(color);
            WhimTexComment comment = FindComment(id);
            if (comment == null || !comment.useAuthorColor && comment.color.Equals(color)) return false;
            comment.color = color;
            comment.useAuthorColor = false;
            return true;
        }

        public bool SetCommentAuthorColor(string id, Color color, string authorId = null)
        {
            ValidateComments(); ValidateCommentColor(color); ValidateCommentColorAuthor(authorId);
            var comment = FindComment(id);
            if (comment == null || comment.useAuthorColor && comment.color.Equals(color) && comment.colorAuthorId == authorId) return false;
            comment.color = color; comment.colorAuthorId = authorId; comment.useAuthorColor = true;
            return true;
        }

        internal bool RefreshCommentAuthorColors(string authorId, Color color)
        {
            if (string.IsNullOrEmpty(authorId) || comments == null) return false;
            ValidateCommentColor(color);
            bool changed = false;
            foreach (var comment in comments)
            {
                if (comment == null) continue;
                if (comment.useAuthorColor && comment.colorAuthorId == authorId && !comment.color.Equals(color))
                { comment.color = color; changed = true; }
                if (comment.messages == null) continue;
                foreach (var message in comment.messages)
                    if (message != null && message.authorId == authorId && !message.authorColor.Equals(color))
                    { message.authorColor = color; changed = true; }
            }
            return changed;
        }

        private static void ValidateCommentColorAuthor(string authorId)
        {
            if (!string.IsNullOrEmpty(authorId) && !Guid.TryParseExact(authorId, "N", out _))
                throw CommentError("The color author ID must be a profile GUID.");
        }

        private static void ValidateCommentColor(Color color)
        {
            if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) ||
                color.r < 0 || color.r > 1 || color.g < 0 || color.g > 1 || color.b < 0 || color.b > 1 || color.a != 1)
                throw CommentError("Marker colors require RGB values in 0..1 and alpha 1.");
        }

        private static WhimTexCommentMessage NewCommentMessage(string text, string author, List<WhimTexCommentAttachment> attachments) => new WhimTexCommentMessage
        {
            id = Guid.NewGuid().ToString("N"), text = text,
            author = string.IsNullOrWhiteSpace(author) ? "User" : author,
            authorColor = WhimTexComment.ColorFromAuthor(author),
            createdUtcTicks = DateTime.UtcNow.Ticks, attachments = WhimTexCommentAttachment.Copy(attachments)
        };

        internal static void ValidateCommentList(List<WhimTexComment> list)
        {
            if (list == null) return;
            if (list.Count > MaximumCommentThreads) throw CommentError("There can be at most 4096 threads.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var numbers = new HashSet<int>();
            long textBytes = 0;
            long maskPixels = 0;
            var maskIds = new HashSet<string>(StringComparer.Ordinal);
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
                if (comment.resolved && comment.awaitingReview) throw CommentError("A resolved thread cannot await review.");
                Id(comment.id);
                if (comment.number < 0 || comment.number > 0 && !numbers.Add(comment.number))
                    throw CommentError("Assigned comment numbers must be positive and unique.");
                if (!float.IsFinite(comment.anchor.x) || !float.IsFinite(comment.anchor.y)) throw CommentError("Anchors must be finite canvas UV coordinates.");
                ValidateCommentColor(comment.color);
                ValidateCommentColorAuthor(comment.colorAuthorId);
                if (comment.messages == null || comment.messages.Count < 1 || comment.messages.Count > MaximumCommentMessages)
                    throw CommentError("A committed thread must contain 1..128 messages.");
                foreach (WhimTexCommentMessage message in comment.messages)
                {
                    if (message == null) throw CommentError("A message cannot be null.");
                    Id(message.id);
                    Text(message.author, MaximumCommentAuthorLength, "Comment author");
                    ValidateCommentColorAuthor(message.authorId);
                    if (!message.authorColor.Equals(Color.clear)) ValidateCommentColor(message.authorColor);
                    Text(message.text, MaximumCommentTextLength, "Comment text");
                    if (message.attachments?.Count > MaximumCommentAttachments) throw CommentError("A message supports at most 32 attachments.");
                    if (message.attachments != null) foreach (var attachment in message.attachments)
                    {
                        if (attachment == null) throw CommentError("An attachment cannot be null.");
                        if (attachment.mask != null)
                        {
                            var mask = attachment.mask;
                            maskPixels += (long)mask.width * mask.height;
                            if (maskPixels > 67108864) throw CommentError("Attached masks exceed 64 Mi pixels in total.");
                            Text(mask.coverageDeflate, MaximumCommentsBlockBytes, "Compressed mask");
                            if (!maskIds.Add(mask.id ?? "")) throw CommentError("Mask IDs must be unique in a document.");
                        }
                        attachment.Validate();
                        Text(attachment.name, 1024, "Attachment name");
                    }
                    if (!string.IsNullOrEmpty(message.agentWorkId)) Text(message.agentWorkId, 128, "Agent work ID");
                    if (!string.IsNullOrEmpty(message.agentContentHash)) Text(message.agentContentHash, 128, "Agent message hash");
                    if (message.createdUtcTicks < DateTime.MinValue.Ticks || message.createdUtcTicks > DateTime.MaxValue.Ticks)
                        throw CommentError("Message timestamps must contain valid UTC ticks.");
                    if (message.editedUtcTicks < 0 || message.editedUtcTicks > DateTime.MaxValue.Ticks ||
                        message.editedUtcTicks != 0 && message.editedUtcTicks < message.createdUtcTicks)
                        throw CommentError("Edited timestamps must be zero or valid UTC ticks at or after creation.");
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
