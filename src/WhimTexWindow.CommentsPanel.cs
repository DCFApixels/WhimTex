using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [SerializeField] private bool commentsExpanded = true;
        private VisualElement commentDrawer;
        private Button commentTab;
        private Label commentListTitle, commentListEmpty;
        private ListView commentList;

        private void BuildCommentTab(VisualElement tabs)
        {
            commentTab = new Button(ToggleCommentDrawer)
            { name = "commentsTab", tooltip = "Show or hide the list of all comments." };
            commentTab.AddToClassList("whimtex-post-fx-tab");
            tabs.Add(commentTab);
        }

        private void BuildCommentDrawer(VisualElement panel)
        {
            commentDrawer = new VisualElement { name = "commentsDrawer" };
            commentDrawer.AddToClassList("whimtex-post-fx-drawer");
            commentDrawer.AddToClassList("whimtex-comments-drawer");
            commentListTitle = CreatePaneHeader("Comments", "commentsTitle");
            commentDrawer.Add(commentListTitle);
            commentListEmpty = new Label("Click the canvas to add a comment.");
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
                    var item = activeDocument.comments[index];
                    ((CommentListRow)element).Bind(item, index + 1, commentId == item.id);
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
        }

        private void RefreshCommentList()
        {
            if (commentList == null) return;
            var items = activeDocument?.comments;
            int count = items?.Count ?? 0;
            if (commentListTitle != null) commentListTitle.text = "Comments (" + count + ")";
            commentListEmpty.EnableInClassList("whimtex-hidden", count != 0);
            commentList.EnableInClassList("whimtex-hidden", count == 0);
            commentList.style.height = count * commentList.fixedItemHeight;
            if (!ReferenceEquals(commentList.itemsSource, items)) commentList.itemsSource = items;
            else commentList.RefreshItems();
        }

        private void OpenCommentFromList(string id)
        {
            var item = activeDocument?.FindComment(id);
            if (item == null) return;
            OpenCanvasComment(id, item.anchor);
            if (commentId != id) return;
            if (item.resolved) commentShowResolved = true;
            RefreshCanvasComments();
            toolkitHeaderBindings?.Refresh();
        }

        private sealed class CommentListRow : Button
        {
            private readonly Label heading, preview;
            private string id;

            internal CommentListRow(WhimTexWindow owner)
            {
                AddToClassList("whimtex-comment-list-row");
                heading = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
                heading.AddToClassList("whimtex-comment-list-heading");
                preview = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
                preview.AddToClassList("whimtex-comment-list-preview");
                Add(heading); Add(preview);
                clicked += () => owner.OpenCommentFromList(id);
            }

            internal void Bind(WhimTexComment item, int number, bool selected)
            {
                id = item.id; userData = id;
                style.borderLeftColor = item.color;
                int replies = item.messages.Count - 1;
                heading.text = "#" + number + (item.resolved ? " · Resolved" : "") +
                    (replies == 0 ? "" : " · " + replies + (replies == 1 ? " reply" : " replies"));
                preview.text = item.messages[0].text.Replace('\r', ' ').Replace('\n', ' ');
                tooltip = item.messages[0].text;
                EnableInClassList("whimtex-comment-list-row--selected", selected);
            }
        }
    }
}
