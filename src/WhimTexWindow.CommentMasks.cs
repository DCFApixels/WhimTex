using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private CommentMaskPreview commentMaskPreview;
        private CommentEditorView commentMaskPreviewEditor;

        private bool CanAttachCommentSelection(CommentEditorView editor) => editor.state.document == activeDocument &&
            GetAreaSelection() is CanvasSelection selection && selection.Active && selection.Bounds.width > 0;

        private void AttachCommentSelection(CommentEditorView editor, int replace = -1)
        {
            if (!CanAttachCommentSelection(editor) || replace < 0 && editor.state.attachments.Count >= WhimTexDocument.MaximumCommentAttachments) return;
            try
            {
                var selection = GetAreaSelection();
                var value = WhimTexCommentAttachment.ForMask(WhimTexCommentMask.Capture(selection.Coverage, selection.Width, selection.Height));
                if (replace >= 0) editor.state.attachments[replace] = value;
                else editor.state.attachments.Add(value);
                ClearCommentMaskPreview(editor); RefreshCommentAttachmentEditor(editor);
            }
            catch (Exception ex) { ShowNotification(new GUIContent(ex.Message)); }
        }

        private void AddCommentMaskRow(CommentEditorView editor, VisualElement block, WhimTexCommentAttachment attachment, int index, bool editable)
        {
            var mask = attachment.mask;
            var row = new VisualElement { name = "commentMaskAttachment", userData = mask.id };
            row.AddToClassList("whimtex-comment-attachment-row"); row.AddToClassList("whimtex-comment-mask-row");
            var field = new Button(() => ToggleCommentMaskPreview(editor, mask)) { name = "previewCommentMask",
                tooltip = "Preview the attached selection on the canvas. Click again to hide. Your working selection stays unchanged." };
            field.AddToClassList("whimtex-comment-attachment-field");
            var icon = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("whimtex-comment-mask-thumbnail");
            Texture2D thumbnail = null;
            icon.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (thumbnail == null) thumbnail = CreateCommentMaskTexture(mask, 32, false);
                icon.image = thumbnail;
            });
            icon.RegisterCallback<DetachFromPanelEvent>(_ =>
            { icon.image = null; if (thumbnail != null) DestroyImmediate(thumbnail); thumbnail = null; });
            field.Add(icon);
            var label = new Label("Selection · " + mask.width + " × " + mask.height) { enableRichText = false, pickingMode = PickingMode.Ignore };
            label.AddToClassList("whimtex-comment-attachment-name"); field.Add(label);
            row.Add(field);
            var use = WhimTexUI.CreateButton("Use", () => UseCommentMask(mask));
            use.name = "useCommentMask"; use.tooltip = "Use as Selection: replace the working selection with this mask."; row.Add(use);
            if (editable)
            {
                var replace = WhimTexUI.CreateButton("↻", () => AttachCommentSelection(editor, index));
                replace.name = "replaceCommentMask"; replace.tooltip = "Replace with Current Selection";
                replace.SetEnabled(CanAttachCommentSelection(editor));
                replace.schedule.Execute(() => replace.SetEnabled(CanAttachCommentSelection(editor))).Every(200); row.Add(replace);
                var remove = WhimTexUI.CreateButton("×", () =>
                { ClearCommentMaskPreview(editor); editor.state.attachments.RemoveAt(index); RefreshCommentAttachmentEditor(editor); });
                remove.name = "removeCommentAttachment"; remove.tooltip = "Remove attachment"; row.Add(remove);
            }
            block.Add(row);
        }

        private void UseCommentMask(WhimTexCommentMask mask)
        {
            if (activeDocument == null) return;
            var data = mask.Rasterize(activeDocument.width, activeDocument.height);
            if (WhimTexCommentMask.CoverageBounds(data, activeDocument.width, activeDocument.height).width == 0)
            { ShowNotification(new GUIContent("This mask has no selected pixels inside the current canvas.")); return; }
            ChangeAreaSelection(selection => selection.Set(data, SelectionCombine.Replace));
            ClearCommentMaskPreview();
        }

        private void ToggleCommentMaskPreview(CommentEditorView editor, WhimTexCommentMask mask)
        {
            bool hide = commentMaskPreview?.Mask.id == mask.id;
            ClearCommentMaskPreview();
            if (hide || activeDocument == null) return;
            commentMaskPreviewEditor = editor;
            commentMaskPreview = new CommentMaskPreview(this, mask);
            toolkitCanvas.Insert(toolkitCanvas.IndexOf(commentOverlay), commentMaskPreview);
        }

        private void ClearCommentMaskPreview(CommentEditorView editor = null)
        {
            if (editor != null && commentMaskPreviewEditor != editor) return;
            commentMaskPreview?.RemoveFromHierarchy(); commentMaskPreview = null; commentMaskPreviewEditor = null;
        }

        private void RefreshCommentMaskPreview()
        {
            if (commentMaskPreview == null) return;
            var editor = commentMaskPreviewEditor;
            if (!commentEditors.Contains(editor)) { ClearCommentMaskPreview(); return; }
            var shown = commentMaskPreview.Mask;
            bool Matches(WhimTexCommentAttachment attachment) => attachment.mask is WhimTexCommentMask mask &&
                mask.id == shown.id && mask.width == shown.width && mask.height == shown.height &&
                mask.coverageDeflate == shown.coverageDeflate && mask.sha256 == shown.sha256 && mask.localToCanvas.Equals(shown.localToCanvas);
            foreach (var attachment in editor.state.attachments) if (attachment != null && Matches(attachment)) return;
            var thread = activeDocument?.FindComment(editor.state.id);
            if (thread != null) foreach (var message in thread.messages)
                if (message.attachments != null) foreach (var attachment in message.attachments) if (Matches(attachment)) return;
            ClearCommentMaskPreview();
        }

        private static Texture2D CreateCommentMaskTexture(WhimTexCommentMask mask, int maxSize, bool overlay)
        {
            byte[] source = mask.Decode();
            float scale = Mathf.Min(1, (float)maxSize / Mathf.Max(mask.width, mask.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(mask.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(mask.height * scale));
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { name = "Comment selection preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = texture.GetRawTextureData<Color32>();
            for (int y = 0, i = 0; y < height; y++) for (int x = 0; x < width; x++, i++)
            {
                byte c = source[Math.Min(mask.height - 1, (int)((y + .5f) * mask.height / height)) * mask.width +
                    Math.Min(mask.width - 1, (int)((x + .5f) * mask.width / width))];
                pixels[i] = overlay ? new Color32(80, 175, 255, (byte)(c * .5f)) : new Color32(c, c, c, 255);
            }
            texture.Apply(false, false); return texture;
        }

        private sealed class CommentMaskPreview : VisualElement
        {
            private readonly WhimTexWindow owner;
            private Texture2D texture;
            internal readonly WhimTexCommentMask Mask;
            internal CommentMaskPreview(WhimTexWindow owner, WhimTexCommentMask mask)
            {
                this.owner = owner; Mask = mask.Copy();
                name = "commentMaskPreview"; pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
                texture = CreateCommentMaskTexture(mask, 2048, true);
                generateVisualContent += Draw;
                owner.toolkitCanvas.ViewChanged += MarkDirtyRepaint;
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    owner.toolkitCanvas.ViewChanged -= MarkDirtyRepaint;
                    if (texture != null) DestroyImmediate(texture); texture = null;
                });
            }
            private void Draw(MeshGenerationContext context)
            {
                if (texture == null) return;
                var corners = new[] { new Vector2(0, 1), Vector2.one, new Vector2(1, 0), Vector2.zero };
                var polygon = new List<(Vector2 point, Vector2 uv)>(4);
                for (int i = 0; i < 4; i++)
                {
                    polygon.Add((Mask.localToCanvas.Point(corners[i]), corners[i]));
                }
                // Clip only the preview to the current canvas; retain off-canvas pixels in the document.
                for (int edge = 0; edge < 4 && polygon.Count > 0; edge++)
                {
                    var clipped = new List<(Vector2 point, Vector2 uv)>();
                    float Distance(Vector2 p) => edge == 0 ? p.x : edge == 1 ? 1 - p.x : edge == 2 ? p.y : 1 - p.y;
                    var a = polygon[polygon.Count - 1]; float da = Distance(a.point);
                    foreach (var b in polygon)
                    {
                        float db = Distance(b.point);
                        if (da >= 0 != (db >= 0))
                        { float t = da / (da - db); clipped.Add((Vector2.Lerp(a.point, b.point, t), Vector2.Lerp(a.uv, b.uv, t))); }
                        if (db >= 0) clipped.Add(b);
                        a = b; da = db;
                    }
                    polygon = clipped;
                }
                if (polygon.Count < 3) return;
                var mesh = context.Allocate(polygon.Count, (polygon.Count - 2) * 3, texture);
                foreach (var vertex in polygon)
                {
                    Vector2 p = owner.CommentViewPoint(vertex.point);
                    mesh.SetNextVertex(new Vertex { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = Color.white, uv = vertex.uv });
                }
                for (ushort i = 1; i < polygon.Count - 1; i++)
                { mesh.SetNextIndex(0); mesh.SetNextIndex(i); mesh.SetNextIndex((ushort)(i + 1)); }
            }
        }
    }
}
