using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal const string CommentsBlockName = "comments";

        internal byte[] EncodeCommentsBlock(WhimTexDocumentContainer container)
        {
            ValidateComments();
            if (comments == null || comments.Count == 0) return null;
            if (container == null) throw CommentError("A document container is required to preserve comments.");
            byte[] bytes = WhimTexDocumentSerializer.Serialize(comments, container);
            if (bytes.Length > MaximumCommentsBlockBytes) throw CommentError("The comments block exceeds 16 MiB.");
            return bytes;
        }

        internal void WriteCommentsBlock(WhimTexDocumentContainer container, byte[] bytes)
        {
            if (bytes != null) container.Set(CommentsBlockName, bytes);
            else if (container != null && container.Contains(CommentsBlockName)) container.Remove(CommentsBlockName);
        }

        internal void ReadCommentsBlock(WhimTexDocumentContainer container)
        {
            if (container == null || !container.Contains(CommentsBlockName))
            {
                comments = new List<WhimTexComment>();
                return;
            }
            if (container.LengthOf(CommentsBlockName) > MaximumCommentsBlockBytes) throw CommentError("The comments block exceeds 16 MiB.");
            byte[] bytes = container.Get(CommentsBlockName);
            if (bytes.Length > MaximumCommentsBlockBytes) throw CommentError("The comments block exceeds 16 MiB.");
            if (bytes.Length < sizeof(int)) throw CommentError("The comments block is incomplete.");
            WhimTexDocumentSerializer.ModelReadResult read;
            try { read = WhimTexDocumentSerializer.Deserialize(bytes, container, typeof(List<WhimTexComment>)); }
            catch (System.Exception error) when (!(error is System.OperationCanceledException))
            { throw new WhimTexDocumentException("Cannot read the comments block: " + error.Message, error); }
            if (!(read.Model is List<WhimTexComment> loaded) || read.SkippedFields.Count != 0 || read.MissingTypes.Count != 0 || read.UnresolvedReferences.Count != 0)
                throw CommentError("The comments block contains unsupported or incomplete data.");
            ValidateCommentList(loaded);
            comments = loaded;
        }
    }
}
