using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        // Window-local selection; opening a card and selecting a pin are independent.
        private readonly HashSet<string> selectedCommentIds = new();
        private WhimTexDocument commentSelectionDocument;

        private void NormalizeCommentSelection(WhimTexCommentQuery query)
        {
            if (commentSelectionDocument != activeDocument || canvasTool != CanvasTool.Comment)
                selectedCommentIds.Clear();
            commentSelectionDocument = activeDocument;
            selectedCommentIds.RemoveWhere(id => !query.Matches(activeDocument?.FindComment(id)));
        }

        private void SetCanvasCommentSelection(IEnumerable<string> ids)
        {
            selectedCommentIds.Clear();
            if (ids != null) selectedCommentIds.UnionWith(ids);
            commentSelectionDocument = activeDocument;
            RefreshCommentSelection();
        }

        private void RefreshCommentSelection()
        {
            foreach (var entry in commentPins)
            {
                entry.Value.EnableInClassList("whimtex-comment-pin--marked", selectedCommentIds.Contains(entry.Key));
                entry.Value.MarkDirtyRepaint();
            }
            foreach (var cluster in commentClusters)
            {
                cluster.pin?.EnableInClassList("whimtex-comment-pin--marked", cluster.items.Exists(item => selectedCommentIds.Contains(item.id)));
                cluster.pin?.MarkDirtyRepaint();
            }
        }

        private static Rect CommentPinBounds(Vector2 point) => new Rect(point - CommentPin.Anchor - Vector2.one * 2, new Vector2(30, 30));

        private void MoveCanvasComments(WhimTexDocument document, Dictionary<string, Vector2> positions, Vector2 delta)
        {
            if (document == null || document != activeDocument || positions.Count == 0 || delta == Vector2.zero) return;
            foreach (string id in positions.Keys)
                if (document.FindComment(id) == null) return;
            ExecuteContextChange(positions.Count == 1 ? "Move Comment" : "Move Comments", () =>
            {
                foreach (var entry in positions) document.FindComment(entry.Key).anchor = entry.Value + delta;
            });
        }

        private void SelectCanvasComments(Rect rectangle, CanvasCoordinates coordinates, IEnumerable<string> keep)
        {
            selectedCommentIds.Clear();
            if (keep != null) selectedCommentIds.UnionWith(keep);
            commentSelectionDocument = activeDocument;
            var query = CurrentCommentQuery();
            foreach (var item in activeDocument.comments)
                if (!clusteredCommentIds.Contains(item.id) && query.Matches(item) &&
                    rectangle.Overlaps(CommentPinBounds(coordinates.UvToView(item.anchor)))) selectedCommentIds.Add(item.id);
            // A visible stack represents all of its filtered discussions.
            foreach (var cluster in commentClusters)
                if (rectangle.Overlaps(CommentPinBounds(cluster.point)))
                    foreach (var item in cluster.items) if (query.Matches(item)) selectedCommentIds.Add(item.id);
            RefreshCommentSelection();
        }

        private bool HandleCommentSelectionKey(KeyDownEvent evt)
        {
            if (canvasTool != CanvasTool.Comment || IsTextInputTarget(evt.target as VisualElement)) return false;
            NormalizeCommentSelection(CurrentCommentQuery());
            if (evt.keyCode == KeyCode.Escape && (commentManipulator?.IsDragging == true || selectedCommentIds.Count > 0))
            {
                if (commentManipulator?.IsDragging == true) commentManipulator.Cancel();
                else SetCanvasCommentSelection(null);
                WhimTexUI.ConsumeEvent(evt); return true;
            }
            if (evt.keyCode != KeyCode.Delete || evt.ctrlKey || evt.commandKey || evt.altKey ||
                toolkitCanvas == null || evt.target is not VisualElement target ||
                (target != toolkitCanvas && !toolkitCanvas.Contains(target))) return false;
            // Do not let Delete act on another kind of selection during a canvas gesture.
            WhimTexUI.ConsumeEvent(evt);
            if (commentManipulator?.IsDragging != true && selectedCommentIds.Count > 0)
            {
                VisualElement anchor = toolkitCanvas;
                foreach (string id in selectedCommentIds)
                    if (commentPins.TryGetValue(id, out var pin) && !clusteredCommentIds.Contains(id)) { anchor = pin; break; }
                if (anchor == toolkitCanvas)
                    foreach (var cluster in commentClusters)
                        if (cluster.items.Exists(item => selectedCommentIds.Contains(item.id))) { anchor = cluster.pin; break; }
                RequestDeleteCanvasComments(activeDocument, selectedCommentIds, anchor);
            }
            return true;
        }
    }
}
