using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [SerializeField] private string commentAuthor = "", commentPeriod = "Any time", commentTimeBasis = "Last activity";
        [SerializeField] private string commentSort = "Number", commentFrom = "", commentTo = "";
        [SerializeField] private bool commentSortDescending, commentQueryExpanded;
        private DropdownField commentAuthorField, commentPeriodField, commentTimeBasisField, commentSortField;
        private TextField commentFromField, commentToField;
        private Toggle commentDescendingField;
        private VisualElement commentDateRange;
        private Label commentDateError;
        private const string AllCommentAuthors = "All authors";

        private WhimTexCommentQuery CurrentCommentQuery()
        {
            var query = new WhimTexCommentQuery { author = commentAuthor, search = commentSearch, status = commentFilter.ToString(),
                activityTime = commentTimeBasis == "Last activity", sort = commentSort == "Last activity" ? "Activity" : commentSort,
                descending = commentSortDescending };
            DateTime today = DateTime.Today;
            if (commentPeriod == "Today") query.fromUtcTicks = today.ToUniversalTime().Ticks;
            else if (commentPeriod == "Last 7 days") query.fromUtcTicks = today.AddDays(-6).ToUniversalTime().Ticks;
            else if (commentPeriod == "Last 30 days") query.fromUtcTicks = today.AddDays(-29).ToUniversalTime().Ticks;
            else if (commentPeriod == "Custom dates") query.valid = TryCommentDates(out query.fromUtcTicks, out query.toUtcTicks);
            if (query.NeedsLinkContext) query.linkPrefix = CreateCommentLinkPrefix(activeDocument, out _);
            return query;
        }

        private bool TryCommentDates(out long from, out long to)
        {
            from = to = 0;
            bool Parse(string text, out DateTime date) => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out date);
            if (!string.IsNullOrWhiteSpace(commentFrom))
            { if (!Parse(commentFrom, out var date)) return false; from = date.ToUniversalTime().Ticks; }
            if (!string.IsNullOrWhiteSpace(commentTo))
            { if (!Parse(commentTo, out var date) || date.Date == DateTime.MaxValue.Date) return false; to = date.AddDays(1).ToUniversalTime().Ticks; }
            return to == 0 || from == 0 || to > from;
        }

        private void BuildCommentQueries(VisualElement parent)
        {
            var foldout = new Foldout { text = "Filters & Sort", name = "commentQueryOptions", value = commentQueryExpanded };
            foldout.AddToClassList("whimtex-comment-query-options"); parent.Add(foldout);
            foldout.RegisterValueChangedCallback(e => { if (e.target == foldout) commentQueryExpanded = e.newValue; });
            DropdownField Choice(string label, string name, List<string> choices, string value, Action<string> apply)
            {
                var field = WhimTexUI.ConfigureField(new DropdownField(label, choices, Math.Max(0, choices.IndexOf(value))) { name = name }, 70);
                field.RegisterValueChangedCallback(e => { apply(e.newValue); ApplyCommentFilter(); });
                foldout.Add(field); return field;
            }
            commentAuthorField = Choice("Author", "commentAuthor", new List<string> { AllCommentAuthors }, AllCommentAuthors,
                value => commentAuthor = value == AllCommentAuthors ? "" : value.Substring(3));
            commentAuthorField.tooltip = "Discussions containing a message by this author.";
            commentTimeBasisField = Choice("Time", "commentTimeBasis", new List<string> { "Created", "Last activity" }, commentTimeBasis, value => commentTimeBasis = value);
            commentTimeBasisField.tooltip = "Last activity is the latest reply or message edit.";
            commentPeriodField = Choice("Period", "commentPeriod", new List<string> { "Any time", "Today", "Last 7 days", "Last 30 days", "Custom dates" },
                commentPeriod, value => commentPeriod = value);
            commentDateRange = new VisualElement(); foldout.Add(commentDateRange);
            TextField DateField(string label, string name, string value, Action<string> set)
            {
                var field = WhimTexUI.ConfigureField(new TextField(label) { name = name, isDelayed = true, value = value,
                    tooltip = "Local calendar date: yyyy-MM-dd. Leave empty for no bound. Both dates are inclusive." }, 70);
                field.RegisterValueChangedCallback(e => { set(e.newValue.Trim()); ApplyCommentFilter(); }); commentDateRange.Add(field);
                return field;
            }
            commentFromField = DateField("From", "commentFrom", commentFrom, value => commentFrom = value);
            commentToField = DateField("Through", "commentTo", commentTo, value => commentTo = value);
            commentDateError = new Label("Use yyyy-MM-dd; the end date must not precede the start.");
            commentDateError.AddToClassList("whimtex-comment-query-error"); commentDateRange.Add(commentDateError);
            commentSortField = Choice("Sort", "commentSort", new List<string> { "Number", "Created", "Last activity", "Author" }, commentSort, value => commentSort = value);
            commentSortField.tooltip = "Author sorts by the discussion's original author.";
            var descending = commentDescendingField = WhimTexUI.ConfigureField(new Toggle("Descending") { name = "commentDescending", value = commentSortDescending }, 90);
            descending.RegisterValueChangedCallback(e => { commentSortDescending = e.newValue; ApplyCommentFilter(); }); foldout.Add(descending);
            var reset = new Button(() => { ResetCommentQuery(); ApplyCommentFilter(); }) { text = "Reset Filters", name = "resetCommentFilters" };
            foldout.Add(reset);
        }

        private void RefreshCommentQueryControls()
        {
            if (commentAuthorField == null) return;
            var authors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeDocument?.comments != null) foreach (var item in activeDocument.comments) foreach (var message in item.messages) authors.Add(message.author);
            if (!string.IsNullOrEmpty(commentAuthor)) authors.Add(commentAuthor);
            var choices = new List<string> { AllCommentAuthors }; foreach (var author in authors) choices.Add("By " + author);
            bool changed = choices.Count != commentAuthorField.choices.Count;
            for (int i = 0; !changed && i < choices.Count; i++) changed = choices[i] != commentAuthorField.choices[i];
            if (changed) commentAuthorField.choices = choices;
            commentAuthorField.SetValueWithoutNotify(string.IsNullOrEmpty(commentAuthor) ? AllCommentAuthors : "By " + commentAuthor);
            commentPeriodField.SetValueWithoutNotify(commentPeriod);
            commentTimeBasisField.SetValueWithoutNotify(commentTimeBasis);
            commentSortField.SetValueWithoutNotify(commentSort);
            commentDescendingField.SetValueWithoutNotify(commentSortDescending);
            commentFromField.SetValueWithoutNotify(commentFrom);
            commentToField.SetValueWithoutNotify(commentTo);
            commentDateRange.EnableInClassList("whimtex-hidden", commentPeriod != "Custom dates");
            commentDateError.EnableInClassList("whimtex-hidden", TryCommentDates(out _, out _));
        }

        private void ResetCommentQuery()
        {
            commentFilter = CommentFilter.All; commentSearch = commentAuthor = commentFrom = commentTo = "";
            commentPeriod = "Any time";
            commentSearchField?.SetValueWithoutNotify("");
        }
    }
}
