using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed class CommentCluster
        {
            internal readonly List<WhimTexComment> items = new();
            internal Vector2 point;
            internal CommentPin pin;
        }
        private readonly List<CommentCluster> commentClusters = new();
        private readonly HashSet<string> clusteredCommentIds = new();
        private readonly Stack<CommentCluster> spareCommentClusters = new();
        private readonly Dictionary<Vector2Int, List<CommentCluster>> commentClusterCells = new();
        private readonly Stack<List<CommentCluster>> spareCommentClusterCells = new();
        private readonly List<WhimTexComment> commentClusterCandidates = new();
        private readonly HashSet<string> openCommentIds = new();

        private void PositionCommentClusters()
        {
            foreach (var cluster in commentClusters)
            { cluster.items.Clear(); spareCommentClusters.Push(cluster); }
            commentClusters.Clear(); clusteredCommentIds.Clear();
            foreach (var bucket in commentClusterCells.Values) { bucket.Clear(); spareCommentClusterCells.Push(bucket); }
            commentClusterCells.Clear(); commentClusterCandidates.Clear(); openCommentIds.Clear();
            foreach (var entry in commentPins) entry.Value.RemoveFromClassList("whimtex-hidden");
            if (!CanvasCommentsVisible || commentOverlay == null)
            {
                foreach (var cluster in spareCommentClusters) cluster.pin?.RemoveFromHierarchy();
                return;
            }
            // Representatives remain at actual anchors; a spatial grid bounds work in dense stacks.
            const float spacing = 32f;
            var cells = commentClusterCells;
            var candidates = commentClusterCandidates;
            foreach (var editor in commentEditors)
                if (editor.state.document == activeDocument && editor.state.id != null) openCommentIds.Add(editor.state.id);
            foreach (var item in activeDocument.comments)
                if (commentPins.ContainsKey(item.id) && !openCommentIds.Contains(item.id) && commentManipulator?.MovingId != item.id)
                    candidates.Add(item);
            candidates.Sort((a, b) => a.number.CompareTo(b.number));
            foreach (var item in candidates)
            {
                Vector2 point = CommentViewPoint(item.anchor);
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) continue;
                var cell = new Vector2Int(Mathf.FloorToInt(point.x / spacing), Mathf.FloorToInt(point.y / spacing));
                CommentCluster nearest = null; float distance = float.MaxValue;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                    if (cells.TryGetValue(cell + new Vector2Int(x, y), out var neighbors)) foreach (var other in neighbors)
                    {
                        Vector2 delta = point - other.point;
                        if (Mathf.Abs(delta.x) < spacing && Mathf.Abs(delta.y) < spacing && delta.sqrMagnitude < distance)
                        { nearest = other; distance = delta.sqrMagnitude; }
                    }
                if (nearest == null)
                {
                    nearest = spareCommentClusters.Count > 0 ? spareCommentClusters.Pop() : new CommentCluster();
                    nearest.point = point; commentClusters.Add(nearest);
                    if (!cells.TryGetValue(cell, out var bucket))
                        cells.Add(cell, bucket = spareCommentClusterCells.Count > 0 ? spareCommentClusterCells.Pop() : new List<CommentCluster>());
                    bucket.Add(nearest);
                }
                nearest.items.Add(item);
            }
            for (int i = commentClusters.Count - 1; i >= 0; i--)
                if (commentClusters[i].items.Count < 2)
                {
                    var cluster = commentClusters[i]; cluster.items.Clear();
                    spareCommentClusters.Push(cluster); commentClusters.RemoveAt(i);
                }
            foreach (var cluster in spareCommentClusters) cluster.pin?.RemoveFromHierarchy();
            foreach (var cluster in commentClusters)
            {
                bool review = false, resolved = true;
                foreach (var item in cluster.items)
                {
                    clusteredCommentIds.Add(item.id); commentPins[item.id].AddToClassList("whimtex-hidden");
                    review |= item.awaitingReview; resolved &= item.resolved;
                }
                if (cluster.pin == null)
                {
                    cluster.pin = new CommentPin { Color = WhimTexComment.DefaultColor, name = "commentCluster" };
                    cluster.pin.AddToClassList("whimtex-comment-pin"); cluster.pin.AddToClassList("whimtex-comment-pin--cluster");
                }
                var pin = cluster.pin;
                pin.text = "×" + cluster.items.Count; pin.tooltip = cluster.items.Count + " comments — click to choose";
                pin.EnableInClassList("whimtex-comment-pin--review", review);
                pin.EnableInClassList("whimtex-comment-pin--resolved", resolved);
                pin.style.translate = new Translate(cluster.point.x - CommentPin.Anchor.x, cluster.point.y - CommentPin.Anchor.y);
                if (pin.parent != commentOverlay) commentOverlay.Add(pin);
                pin.BringToFront();
            }
        }

        private bool TryOpenCommentCluster(Vector2 point)
        {
            foreach (var cluster in commentClusters)
                if (new Rect(-2, -5, 34, 33).Contains(point - cluster.point + CommentPin.Anchor))
                {
                    var bounds = new Rect(toolkitCanvas.LocalToWorld(cluster.point - CommentPin.Anchor), new Vector2(30, 28));
                    UnityEditor.PopupWindow.Show(bounds, new CommentClusterPopup(this, cluster.items));
                    return true;
                }
            return false;
        }

        private sealed class CommentClusterPopup : PopupWindowContent
        {
            private readonly WhimTexWindow owner;
            private readonly List<WhimTexComment> items;
            internal CommentClusterPopup(WhimTexWindow owner, List<WhimTexComment> items)
            { this.owner = owner; this.items = new List<WhimTexComment>(items); }
            public override Vector2 GetWindowSize() => new Vector2(290, Mathf.Min(320, 8 + items.Count * 48));
            public override void OnGUI(Rect rect) { }
            public override void OnOpen()
            {
                var root = editorWindow.rootVisualElement; root.userData = owner;
                WhimTexUI.ApplyWindowStyles(root);
                var scroll = new ScrollView(ScrollViewMode.Vertical); root.Add(scroll);
                foreach (var item in items)
                {
                    string id = item.id;
                    var button = new Button(() => { editorWindow.Close(); if (owner != null) owner.ShowCommentOnCanvas(id); })
                        { name = "commentClusterChoice", userData = id, tooltip = item.messages[0].text };
                    button.AddToClassList("whimtex-comment-cluster-choice");
                    button.style.borderLeftColor = WhimTexUserSettings.GetCommentColor(item);
                    button.Add(new Label("#" + item.number + " · " + item.messages[0].author) { enableRichText = false });
                    var preview = new Label(item.messages[0].text.Replace('\n', ' ').Replace('\r', ' ')) { enableRichText = false };
                    preview.AddToClassList("whimtex-comment-cluster-preview"); button.Add(preview);
                    scroll.Add(button);
                }
                root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) editorWindow.Close(); });
                root.Q<Button>()?.Focus();
            }
        }
    }
}
