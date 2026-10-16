using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static WhimTexCommentQuery ParseCommentQuery(JObject request, bool explicitIds)
        {
            var query = new WhimTexCommentQuery { status = explicitIds || Bool(request, "includeResolved") ? "All" : "Open",
                sort = Text(request, "sort", "Number"), descending = Bool(request, "descending") };
            Require(query.sort == "Number" || query.sort == "Created" || query.sort == "Activity" || query.sort == "Author", "sort must be Number, Created, Activity or Author.");
            if (request["filter"] == null) return query;
            var filter = Obj(request["filter"], "filter");
            Keys(filter, "status", "author", "search", "time", "fromUtc", "toUtc");
            query.status = Text(filter, "status", query.status);
            Require(query.status == "Open" || query.status == "All" || query.status == "InProgress" || query.status == "AwaitingReview" || query.status == "Resolved", "Unknown comment status filter.");
            query.author = Text(filter, "author"); query.search = Text(filter, "search");
            Require((query.author?.Length ?? 0) <= WhimTexDocument.MaximumCommentAuthorLength && (query.search?.Length ?? 0) <= WhimTexDocument.MaximumCommentTextLength, "Comment filter text is too long.");
            string time = Text(filter, "time", "Activity");
            Require(time == "Created" || time == "Activity", "filter.time must be Created or Activity.");
            query.activityTime = time == "Activity";
            long Bound(string key)
            {
                if (filter[key] == null) return 0;
                string value = Text(filter, key);
                Require(value != null && Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$") &&
                    DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _), key + " requires an ISO 8601 timestamp with a timezone.");
                return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcTicks;
            }
            query.fromUtcTicks = Bound("fromUtc"); query.toUtcTicks = Bound("toUtc");
            Require(query.fromUtcTicks == 0 || query.toUtcTicks == 0 || query.fromUtcTicks < query.toUtcTicks, "fromUtc must precede toUtc (exclusive).");
            return query;
        }
    }
}
