using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    // Shared read-only query semantics for the list, canvas markers and agent API.
    internal sealed class WhimTexCommentQuery
    {
        internal string author, search, status;
        internal long fromUtcTicks, toUtcTicks;
        internal bool activityTime = true, descending;
        internal string sort = "Number";
        internal string linkPrefix;
        internal bool valid = true;
        internal bool NeedsLinkContext => search?.TrimStart().StartsWith("whimtex://", StringComparison.Ordinal) == true;

        internal static long Created(WhimTexComment item) => item.messages[0].createdUtcTicks;
        internal static long Activity(WhimTexComment item)
        {
            long time = 0;
            foreach (var message in item.messages) time = Math.Max(time, Math.Max(message.createdUtcTicks, message.editedUtcTicks));
            return time;
        }

        internal bool Matches(WhimTexComment item)
        {
            if (!valid || item == null) return false;
            if (status == "Open" && item.resolved || status != null && status != "All" && status != "Open" && item.Status.ToString() != status) return false;
            bool authorMatch = string.IsNullOrEmpty(author), textMatch = string.IsNullOrWhiteSpace(search);
            string term = search?.Trim();
            if (!textMatch) textMatch = ("#" + item.number).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var message in item.messages)
            {
                authorMatch |= string.Equals(message.author, author, StringComparison.OrdinalIgnoreCase);
                if (!textMatch) textMatch = message.text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.author.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (!textMatch && linkPrefix != null) textMatch = string.Equals(term, linkPrefix + Uri.EscapeDataString(item.id), StringComparison.Ordinal);
            long time = activityTime ? Activity(item) : Created(item);
            return authorMatch && textMatch && (fromUtcTicks == 0 || time >= fromUtcTicks) && (toUtcTicks == 0 || time < toUtcTicks);
        }

        internal void Sort(List<WhimTexComment> items) => items.Sort(Compare);
        private int Compare(WhimTexComment a, WhimTexComment b)
        {
            int result = sort == "Created" ? Created(a).CompareTo(Created(b)) :
                sort == "Activity" ? Activity(a).CompareTo(Activity(b)) :
                sort == "Author" ? StringComparer.OrdinalIgnoreCase.Compare(a.messages[0].author, b.messages[0].author) : a.number.CompareTo(b.number);
            if (result != 0) return descending ? -result : result;
            result = a.number.CompareTo(b.number);
            return result != 0 ? result : StringComparer.Ordinal.Compare(a.id, b.id);
        }
    }
}
