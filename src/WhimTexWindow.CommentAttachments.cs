using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private VisualElement layerPing, layerPingRow;
        private VisualElement commentAttachmentDropHighlight;
        private IVisualElementScheduledItem layerPingAnimation;
        private StyleScale layerPingPreviousScale;
#if UNITY_6000_7_OR_NEWER
        private StyleInt layerPingPreviousZIndex;
#endif

        private sealed class CommentAttachmentDropTarget
        {
            internal CommentEditorView editor;
            internal bool editable;
            internal int index;
            internal VisualElement highlightTarget;
        }

        private void RefreshCommentAttachmentEditor(CommentEditorView editor)
        {
            if (editor.attachments == null) return;
            ClearCommentMaskPreview(editor);
            ClearCommentAttachmentDropHighlight();
            editor.attachments.Clear();
            AddCommentAttachments(editor, editor.attachments, editor.state.attachments, true);
        }

        private void AddCommentAttachments(CommentEditorView editor, VisualElement parent, List<WhimTexCommentAttachment> values, bool editable,
            List<Action> refreshFields = null)
        {
            var block = new VisualElement { name = editable ? "commentAttachmentFields" : "commentAttachments" };
            block.AddToClassList("whimtex-comment-attachments");
            block.userData = new CommentAttachmentDropTarget { editor = editor, editable = editable, index = -1, highlightTarget = block };
            if (values != null) for (int i = 0; i < values.Count; i++)
            {
                int index = i;
                var attachment = values[i];
                if (attachment?.mask != null) { AddCommentMaskRow(editor, block, attachment, index, editable); continue; }
                var row = new VisualElement(); row.AddToClassList("whimtex-comment-attachment-row");
                var field = new Button(() => PingCommentAttachment(attachment)) { name = "commentAttachmentField" };
                row.userData = new CommentAttachmentDropTarget { editor = editor, editable = editable, index = index, highlightTarget = field };
                field.AddToClassList("whimtex-comment-attachment-field");
                var icon = new Image { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("whimtex-comment-attachment-icon");
                var text = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
                text.AddToClassList("whimtex-comment-attachment-name");
                field.Add(icon); field.Add(text);
                void RefreshField()
                {
                    var document = editor.state.document;
                    Layer layer = attachment?.IsLayer == true ? document?.FindLayer(attachment.layerId) : null;
                    var asset = attachment?.assetGuid == null ? null : attachment.ResolveAsset();
                    string label = attachment == null ? "None (Layer or Asset)" : layer != null ? layer.layerName : asset != null ? asset.name : "Missing: " + attachment.name;
                    text.text = label;
                    icon.image = asset != null ? AssetPreview.GetMiniThumbnail(asset) : layer != null ? document.GetLayerThumbnail(layer, 18, EffectsAreInteractive) : null;
                    field.tooltip = attachment == null ? "Drop a layer or Project asset here." :
                        attachment.IsLayer ? "Ping layer: " + label : AssetDatabase.GUIDToAssetPath(attachment.assetGuid) + "\nClick to ping the asset.";
                    field.EnableInClassList("whimtex-comment-attachment--missing", attachment != null && layer == null && asset == null);
                }
                RefreshField(); refreshFields?.Add(RefreshField);
                row.Add(field);
                if (editable)
                {
                    var remove = WhimTexUI.CreateButton("×", () =>
                    { editor.state.attachments.RemoveAt(index); RefreshCommentAttachmentEditor(editor); });
                    remove.name = "removeCommentAttachment"; remove.tooltip = "Remove attachment"; row.Add(remove);
                }
                block.Add(row);
            }
            if (editable)
            {
                var add = WhimTexUI.CreateButton("+", () =>
                {
                    if (editor.state.attachments.Count >= WhimTexDocument.MaximumCommentAttachments) return;
                    editor.state.attachments.Add(null); RefreshCommentAttachmentEditor(editor);
                });
                add.name = "addCommentAttachment"; add.tooltip = "Add an attachment field, or drop layers and Project assets here.";
                add.userData = new CommentAttachmentDropTarget { editor = editor, editable = true, index = -1, highlightTarget = add };
                add.SetEnabled(editor.state.attachments.Count < WhimTexDocument.MaximumCommentAttachments);
                var actions = new VisualElement(); actions.AddToClassList("whimtex-comment-attachment-actions");
                add.AddToClassList("whimtex-comment-attachment-add"); actions.Add(add); block.Add(actions);
                var selection = WhimTexUI.CreateButton("Attach Selection", () => AttachCommentSelection(editor));
                selection.name = "attachCommentSelection"; selection.tooltip = "Attach a snapshot of the current selection.";
                void RefreshEnabled() => selection.SetEnabled(CanAttachCommentSelection(editor) && editor.state.attachments.Count < WhimTexDocument.MaximumCommentAttachments);
                RefreshEnabled(); selection.schedule.Execute(RefreshEnabled).Every(200); actions.Add(selection);
            }
            parent.Add(block);
        }

        private CommentAttachmentDropTarget FindCommentAttachmentTarget(object target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element.userData is CommentAttachmentDropTarget drop) return drop;
            return null;
        }

        private List<WhimTexCommentAttachment> ReadCommentAttachmentDrag()
        {
            var result = new List<WhimTexCommentAttachment>();
            var source = DragAndDrop.GetGenericData(DraggedDocumentIdKey) as WhimTexDocument;
            if (source != null)
            {
                if (source != activeDocument) return result;
                if (DragAndDrop.GetGenericData(DraggedLayersKey) is List<Layer> layers)
                    foreach (var layer in layers)
                        if (layer != null && source.FindLayer(layer.Id) == layer && !WhimTexApi.ContainsReservation(layer))
                            result.Add(WhimTexCommentAttachment.ForLayer(layer));
                return result;
            }
            foreach (var asset in DragAndDrop.objectReferences)
            {
                var attachment = WhimTexCommentAttachment.ForAsset(asset);
                if (attachment != null) result.Add(attachment);
            }
            return result;
        }

        private bool CanDropCommentAttachments(CommentAttachmentDropTarget target, List<WhimTexCommentAttachment> incoming) =>
            target.editable && target.editor.state.document == activeDocument && commentEditors.Contains(target.editor) && incoming.Count > 0 &&
            target.editor.state.attachments.Count + incoming.Count - (target.index >= 0 ? 1 : 0) <= WhimTexDocument.MaximumCommentAttachments;

        private void OnCommentAttachmentDragUpdated(DragUpdatedEvent evt)
        {
            var target = FindCommentAttachmentTarget(evt.target);
            bool accepted = target != null && CanDropCommentAttachments(target, ReadCommentAttachmentDrag());
            var highlighted = accepted ? target.highlightTarget : null;
            if (commentAttachmentDropHighlight?.parent != highlighted)
            {
                ClearCommentAttachmentDropHighlight();
                if (highlighted != null)
                {
                    commentAttachmentDropHighlight = new VisualElement { name = "commentAttachmentDropHighlight", pickingMode = PickingMode.Ignore };
                    commentAttachmentDropHighlight.AddToClassList("whimtex-comment-attachment-drop-highlight");
                    highlighted.Add(commentAttachmentDropHighlight);
                }
            }
            if (target == null) return;
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            ClearToolkitDropIndicator(); layerDragAutoScroll?.Stop();
            evt.StopImmediatePropagation();
        }

        private void OnCommentAttachmentDragPerform(DragPerformEvent evt)
        {
            ClearCommentAttachmentDropHighlight();
            var target = FindCommentAttachmentTarget(evt.target); if (target == null) return;
            evt.StopImmediatePropagation();
            var editor = target.editor;
            var incoming = ReadCommentAttachmentDrag();
            if (!CanDropCommentAttachments(target, incoming)) return;
            DragAndDrop.AcceptDrag();
            if (target.index < 0) editor.state.attachments.AddRange(incoming);
            else
            {
                editor.state.attachments.RemoveAt(target.index);
                editor.state.attachments.InsertRange(target.index, incoming);
            }
            var sourceWindow = DragAndDrop.GetGenericData(DraggedWindowKey) as WhimTexWindow;
            sourceWindow?.ClearLayerDragGhost(); sourceWindow?.activeLayerDrag?.Cancel();
            ClearLayerDragData(); ClearToolkitDropIndicator(); layerDragAutoScroll?.Stop();
            RefreshCommentAttachmentEditor(editor);
        }

        private void ClearCommentAttachmentDropHighlight()
        { commentAttachmentDropHighlight?.RemoveFromHierarchy(); commentAttachmentDropHighlight = null; }

        private void OnCommentAttachmentDragLeave(DragLeaveEvent evt)
        {
            var target = commentAttachmentDropHighlight?.parent;
            if (target != null && evt.target is VisualElement element && (element == target || element.Contains(target)))
                ClearCommentAttachmentDropHighlight();
        }

        private void OnCommentAttachmentDragExited(DragExitedEvent evt) => ClearCommentAttachmentDropHighlight();

        private void PingCommentAttachment(WhimTexCommentAttachment attachment)
        {
            if (attachment == null) return;
            if (attachment.IsLayer) PingLayer(attachment.layerId);
            else
            {
                var asset = attachment.ResolveAsset();
                if (asset != null) EditorGUIUtility.PingObject(asset);
                else ShowNotification(new GUIContent("The attached asset is missing."));
            }
        }

        private void PingLayer(string id)
        {
            var layer = activeDocument?.FindLayer(id);
            if (layer == null) { ShowNotification(new GUIContent("The attached layer is missing.")); return; }
            if (activeDocument.TryFindLayer(layer, out var container, out _))
                while (activeDocument.TryFindParentGroup(container, out var parent, out var ancestors, out _))
                { groupExpansion[parent.Id] = true; container = ancestors; }
            StopLayerPing();
            RefreshToolkitInterface();
            foreach (var row in toolkitLayerHierarchyRoot.Children())
                if (row.userData as string == id)
                {
                    toolkitSettingsScroll.ScrollTo(row);
                    var overlay = new VisualElement { name = "layerPing", pickingMode = PickingMode.Ignore };
                    overlay.AddToClassList("whimtex-layer-ping"); rootVisualElement.Add(overlay); layerPing = overlay;
                    layerPingRow = row; layerPingPreviousScale = row.style.scale;
#if UNITY_6000_7_OR_NEWER
                    layerPingPreviousZIndex = row.style.zIndex;
                    row.style.zIndex = 1000; overlay.style.zIndex = 1001;
#endif
                    row.RegisterCallback<DetachFromPanelEvent>(OnPingRowDetached);
                    overlay.style.visibility = Visibility.Hidden;
                    overlay.RegisterCallback<DetachFromPanelEvent>(_ => { if (layerPing == overlay) StopLayerPing(); });
                    double start = double.NaN;
                    layerPingAnimation = overlay.schedule.Execute(() =>
                    {
                        if (double.IsNaN(start))
                        {
                            if (!(row.layout.width > 0f) || !(row.layout.height > 0f)) return;
                            toolkitSettingsScroll.ScrollTo(row);
                            start = EditorApplication.timeSinceStartup;
                        }
                        UpdateLayerPing((float)(EditorApplication.timeSinceStartup - start));
                    }).Every(16);
                    break;
                }
        }

        private void UpdateLayerPing(float elapsed)
        {
            if (layerPing == null || layerPingRow == null) return;
            const float popDuration = .18f, holdDuration = .5f, fadeDuration = .4f;
            layerPing.style.visibility = Visibility.Visible;
            float pop = 1f - Mathf.Clamp01(elapsed / popDuration); pop *= pop;
            layerPingRow.style.scale = elapsed < popDuration
                ? new StyleScale(new Scale(new Vector3(1f + .4f * pop, 1f + .4f * pop, 1f))) : layerPingPreviousScale;
#if UNITY_6000_7_OR_NEWER
            if (elapsed >= popDuration) layerPingRow.style.zIndex = layerPingPreviousZIndex;
#endif
            PositionLayerPingOutline(pop);
            layerPing.style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - popDuration - holdDuration) / fadeDuration));
            if (elapsed >= popDuration + holdDuration + fadeDuration) StopLayerPing();
        }

        private void PositionLayerPingOutline(float pop)
        {
            // Draw above adjacent rows and outside their bounds, without adding a
            // layout child to the row or changing its place in the hierarchy.
            var bounds = rootVisualElement.WorldToLocal(layerPingRow.parent.LocalToWorld(layerPingRow.layout));
            var scale = new Vector3(1f + .4f * pop, 1f + .4f * pop, 1f);
            bounds = new Rect(bounds.center - Vector2.Scale(bounds.size, scale) * .5f, Vector2.Scale(bounds.size, scale));
            layerPing.style.left = bounds.xMin - 3f; layerPing.style.top = bounds.yMin - 3f;
            layerPing.style.width = bounds.width + 6f; layerPing.style.height = bounds.height + 6f;
        }

        private void OnPingRowDetached(DetachFromPanelEvent evt)
        { if (evt.target == layerPingRow) StopLayerPing(); }

        private void StopLayerPing()
        {
            layerPingAnimation?.Pause(); layerPingAnimation = null;
            if (layerPingRow != null)
            {
                layerPingRow.UnregisterCallback<DetachFromPanelEvent>(OnPingRowDetached);
                layerPingRow.style.scale = layerPingPreviousScale;
#if UNITY_6000_7_OR_NEWER
                layerPingRow.style.zIndex = layerPingPreviousZIndex;
#endif
            }
            layerPingRow = null;
            var overlay = layerPing; layerPing = null;
            overlay?.RemoveFromHierarchy();
        }
    }
}
