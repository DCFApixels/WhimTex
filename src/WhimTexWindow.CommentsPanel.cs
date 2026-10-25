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
        [SerializeField] private bool commentsExpanded;
        private VisualElement commentDrawer;
        private Button commentTab, closeAllCommentsButton;
        private Label commentListTitle, commentListEmpty;
        private ListView commentList;
        private enum CommentFilter { Open, Resolved, All, InProgress, AwaitingReview }
        private static readonly List<CommentFilter> CommentFilterValues = new() { CommentFilter.Open, CommentFilter.InProgress, CommentFilter.AwaitingReview, CommentFilter.Resolved, CommentFilter.All };
        private static readonly List<string> CommentFilterLabels = new() { "Open", "In Progress", "Awaiting Review", "Resolved", "All" };
        private static readonly List<string> CommentStatusLabels = new() { "In Progress", "Awaiting Review", "Resolved" };
        [SerializeField] private CommentFilter commentFilter;
        [SerializeField] private string commentSearch = "";
        private readonly List<WhimTexComment> filteredComments = new();
        private readonly List<CommentChoiceField> commentFilters = new();
        private ToolbarSearchField commentSearchField;

        private int OpenCommentCount()
        {
            int count = 0;
            if (activeDocument?.comments != null) foreach (var item in activeDocument.comments) if (!item.resolved) count++;
            return count;
        }

        private bool MatchesCommentFilter(WhimTexComment item) => CurrentCommentQuery().Matches(item);

        private CommentChoiceField CreateCommentFilter()
        {
            commentFilters.RemoveAll(field => field.panel == null);
            var field = new CommentChoiceField(this, CommentFilterLabels, CommentFilterValues.IndexOf(commentFilter))
                { name = "commentFilter", tooltip = "Filter both the list and canvas markers" };
            field.AddToClassList("whimtex-comment-filter");
            commentFilters.Add(field);
            field.RegisterValueChangedCallback(_ =>
            {
                commentFilter = CommentFilterValues[field.index];
                SaveToolPreferences();
                ApplyCommentFilter();
            });
            return field;
        }

        private void ApplyCommentFilter()
        {
            foreach (var filter in commentFilters) filter.SetValueWithoutNotify(CommentFilterLabels[CommentFilterValues.IndexOf(commentFilter)]);
            RefreshCanvasComments(); toolkitHeaderBindings?.Refresh();
        }

        private void BuildCommentTab(VisualElement tabs)
        {
            commentTab = CreateCanvasDrawerTab("commentsTab", "Comments — show or hide the list of all comments.",
                new CanvasToolIcon(CanvasTool.Comment), ToggleCommentDrawer);
            tabs.Add(commentTab);
        }

        private void BuildCommentDrawer(VisualElement panel)
        {
            commentDrawer = new VisualElement { name = "commentsDrawer" };
            commentDrawer.AddToClassList("whimtex-post-fx-drawer");
            commentDrawer.AddToClassList("whimtex-comments-drawer");
            var header = new VisualElement { name = "commentsHeader" };
            header.AddToClassList("whimtex-pane-header");
            header.AddToClassList("whimtex-comments-header");
            header.EnableInClassList("whimtex-pane-header--light", !EditorGUIUtility.isProSkin);
            commentListTitle = new Label("Comments") { name = "commentsTitle", enableRichText = false };
            header.Add(commentListTitle);
            closeAllCommentsButton = WhimTexUI.CreateButton("Close All", DismissAllCanvasComments);
            closeAllCommentsButton.name = "closeAllComments";
            closeAllCommentsButton.tooltip = "Close all comment cards and keep drafts.";
            closeAllCommentsButton.SetEnabled(HasOpenCanvasComments());
            header.Add(closeAllCommentsButton);
            commentDrawer.Add(header);
            var filters = new VisualElement(); filters.AddToClassList("whimtex-comment-list-filters");
            filters.Add(CreateCommentFilter());
            commentSearchField = new ToolbarSearchField { name = "commentSearch", tooltip = "Search messages, authors or comment numbers, or paste an exact comment link" };
            commentSearchField.SetValueWithoutNotify(commentSearch ?? "");
            commentSearchField.RegisterValueChangedCallback(evt => { commentSearch = evt.newValue; ApplyCommentFilter(); });
            filters.Add(commentSearchField); commentDrawer.Add(filters);
            BuildCommentQueries(commentDrawer);
            commentListEmpty = new Label("Select the Comment tool and click the canvas to add a comment.");
            commentListEmpty.AddToClassList("whimtex-comments-empty");
            commentDrawer.Add(commentListEmpty);
            commentList = new ListView
            {
                name = "commentsList", fixedItemHeight = 76,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = () => new CommentListRow(this),
                bindItem = (element, index) =>
                {
                    var item = filteredComments[index];
                    ((CommentListRow)element).Bind(item, item.number, FindOpenCommentEditor(item.id) != null, HasCommentDraft(item.id));
                }
            };
            commentList.AddToClassList("whimtex-comments-list");
            commentList.Q<ScrollView>().verticalScrollerVisibility = ScrollerVisibility.Auto;
            commentList.Q<ScrollView>().horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            commentDrawer.Add(commentList);
            commentDrawer.RegisterCallback<GeometryChangedEvent>(_ => PositionCanvasComments());
            panel.Add(commentDrawer);
            RefreshCommentList();
        }

        private void ToggleCommentDrawer()
        {
            commentsExpanded = !commentsExpanded;
            if (commentsExpanded) brushesExpanded = postFxExpanded = uvExpanded = false;
            RefreshPostFxPanel();
            RefreshCommentList();
            PositionCanvasComments();
            UpdateToolkitCanvasPresentation();
        }

        private void RefreshCommentList() => RefreshCommentList(CurrentCommentQuery());

        private void RefreshCommentList(WhimTexCommentQuery query)
        {
            if (commentList == null) return;
            var items = activeDocument?.comments;
            filteredComments.Clear();
            if (items != null) foreach (var item in items) if (query.Matches(item)) filteredComments.Add(item);
            query.Sort(filteredComments);
            RefreshCommentQueryControls();
            int count = filteredComments.Count;
            if (commentListTitle != null) commentListTitle.text = "Comments · " + OpenCommentCount() + " open";
            commentListEmpty.text = items?.Count > 0 ? "No matching comments." : "Select the Comment tool and click the canvas to add a comment.";
            commentListEmpty.EnableInClassList("whimtex-hidden", count != 0);
            commentList.EnableInClassList("whimtex-hidden", count == 0);
            commentList.style.height = count * commentList.fixedItemHeight;
            if (!ReferenceEquals(commentList.itemsSource, filteredComments)) commentList.itemsSource = filteredComments;
            else commentList.RefreshItems();
        }

        private void OpenCommentFromList(string id)
        {
            var item = activeDocument?.FindComment(id);
            if (item == null) return;
            if (!MatchesCommentFilter(item))
            {
                ResetCommentQuery();
                foreach (var filter in commentFilters) filter.SetValueWithoutNotify("All");
            }
            OpenCanvasComment(id, item.anchor);
            toolkitHeaderBindings?.Refresh();
        }

        private void ShowCommentOnCanvas(string id)
        {
            var item = activeDocument?.FindComment(id);
            if (item == null || toolkitCanvas == null) return;
            OpenCommentFromList(id);
            if (FindOpenCommentEditor(id) == null) return;
            Rect view = toolkitCanvas.contentRect;
            if (commentsExpanded && commentDrawer?.resolvedStyle.display == DisplayStyle.Flex)
            {
                Rect drawer = toolkitCanvas.WorldToLocal(commentDrawer.worldBound);
                if (view.Overlaps(drawer) && drawer.xMin > view.xMin)
                    view.xMax = Mathf.Min(view.xMax, drawer.xMin);
            }
            if (view.width <= 0 || view.height <= 0) return;
            toolkitCanvas.Pan(view.center - CommentViewPoint(item.anchor));
            if (commentPins.TryGetValue(id, out var pin)) pin.Highlight();
        }

        private sealed class CommentListRow : Button
        {
            private readonly Label heading, preview;
            private readonly CommentStatusIcon statusIcon;
            private string id;

            internal CommentListRow(WhimTexWindow owner)
            {
                AddToClassList("whimtex-comment-list-row");
                heading = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
                heading.AddToClassList("whimtex-comment-list-heading");
                statusIcon = new CommentStatusIcon { name = "commentListStatusIcon" };
                statusIcon.AddToClassList("whimtex-comment-list-status-icon");
                heading.Add(statusIcon);
                preview = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
                preview.AddToClassList("whimtex-comment-list-preview");
                Add(heading); Add(preview);
                clicked += () => owner.OpenCommentFromList(id);
                this.AddManipulator(new ContextualMenuManipulator(e => e.menu.AppendAction("Copy Link", _ => owner.CopyCommentLink(id))));
                RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.button == 0 && evt.clickCount == 2) owner.ShowCommentOnCanvas(id);
                });
            }

            internal void Bind(WhimTexComment item, int number, bool selected, bool draft)
            {
                id = item.id; userData = id;
                style.borderLeftColor = WhimTexUserSettings.GetCommentColor(item);
                statusIcon.SetStatus(item.Status);
                int replies = item.messages.Count - 1;
                heading.text = "#" + number + (draft ? " · Draft" : "") +
                    (replies == 0 ? "" : " · " + replies + (replies == 1 ? " reply" : " replies"));
                preview.text = item.messages[0].text.Replace('\r', ' ').Replace('\n', ' ');
                tooltip = item.messages[0].text;
                EnableInClassList("whimtex-comment-list-row--selected", selected);
            }
        }
    }
}
