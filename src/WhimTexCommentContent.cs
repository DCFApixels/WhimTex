using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexCommentContent
    {
        internal static bool Equals(string text, List<WhimTexCommentAttachment> attachments,
            WhimTexCommentMessage message, bool trimText = false)
        {
            if (message == null) return false;
            string left = text ?? "", right = message.text ?? "";
            return (trimText ? left.Trim() == right.Trim() : left == right) && AttachmentsEqual(attachments, message.attachments);
        }

        internal static bool AttachmentsEqual(List<WhimTexCommentAttachment> left, List<WhimTexCommentAttachment> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if ((left?.Count ?? 0) != (right?.Count ?? 0)) return false;
            if (left != null) for (int i = 0; i < left.Count; i++)
            {
                var a = left[i]; var b = right[i];
                if (ReferenceEquals(a, b)) continue;
                if (a == null || b == null || !SameString(a.layerId, b.layerId) || !SameString(a.assetGuid, b.assetGuid) ||
                    a.localId != b.localId || !SameString(a.name, b.name) || !MasksEqual(a.mask, b.mask)) return false;
            }
            return true;
        }

        private static bool SameString(string a, string b) => (a ?? "") == (b ?? "");

        private static bool MasksEqual(WhimTexCommentMask a, WhimTexCommentMask b) => ReferenceEquals(a, b) ||
            a != null && b != null && SameString(a.id, b.id) && a.version == b.version && a.width == b.width && a.height == b.height &&
            SameString(a.sha256, b.sha256) && SameString(a.coverageDeflate, b.coverageDeflate) && a.localToCanvas.Equals(b.localToCanvas);
    }
}
