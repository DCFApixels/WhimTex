using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private class PreviewPane : VisualElement
        {
            protected readonly MorphGroupEditorWindow editor;
            protected readonly VisualElement frame;
            protected readonly Image image;
            internal readonly VisualElement Surface;
            protected readonly Label caption;
            private readonly Label error;
            internal RenderTexture Texture { get; private set; }
            internal ulong Revision { get; private set; }
            protected bool failed;

            internal PreviewPane(MorphGroupEditorWindow editor, string name, string title)
            {
                this.editor = editor;
                AddToClassList("whimtex-morph-pane");
                caption = new Label(title);
                caption.AddToClassList("whimtex-morph-caption");
                Add(caption);
                Surface = new VisualElement { name = name };
                Surface.AddToClassList("whimtex-morph-surface");
                Add(Surface);
                frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.AddToClassList("whimtex-morph-frame");
                frame.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
                Surface.Add(frame);
                image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                image.StretchToParentSize();
                frame.Add(image);
                error = new Label { pickingMode = PickingMode.Ignore };
                error.AddToClassList("whimtex-morph-error");
                Surface.Add(error);
                SetVisible(error, false);
                Surface.RegisterCallback<GeometryChangedEvent>(_ => FitFrame());
                RegisterCallback<DetachFromPanelEvent>(_ => Release());
            }

            protected void FitFrame()
            {
                var doc = editor.document;
                Rect area = Surface.contentRect;
                if (doc == null || !(area.width > 0 && area.height > 0)) return;
                // Align all previews with the 10-point marker radius at the UV boundaries.
                float scale = Mathf.Min(Mathf.Max(1, area.width - 20) / doc.width, Mathf.Max(1, area.height - 20) / doc.height);
                var size = new Vector2(doc.width, doc.height) * scale;
                Rect desired = new Rect(area.center - size * .5f, size);
                frame.style.left = desired.x;
                frame.style.top = desired.y;
                frame.style.width = size.x;
                frame.style.height = size.y;
            }

            internal void ShowTexture(RenderTexture next)
            {
                if (next == null) throw new InvalidOperationException("No preview image was returned.");
                image.image = next;
                if (Texture != null) ProfiledRenderTexture.ReleaseTemporary(Texture);
                Texture = next;
                Revision++;
                failed = false;
                SetVisible(frame, true);
                SetVisible(error, false);
            }

            internal void ShowFailure(Exception ex)
            {
                Release();
                failed = true;
                error.text = "Preview unavailable. " + ex.Message;
                SetVisible(error, true);
            }

            internal virtual void Release()
            {
                image.image = null;
                if (Texture != null) ProfiledRenderTexture.ReleaseTemporary(Texture);
                Texture = null;
                failed = false;
                SetVisible(frame, false);
                SetVisible(error, false);
            }
        }

        private sealed class EndpointPane : PreviewPane
        {
            private readonly bool isFrom;
            private readonly List<Label> markers = new();
            private Rect ImageRect => Surface.WorldToLocal(image.worldBound);
            private bool undoRecorded;
            private int pointer = -1, dragged = -1;
            private Vector2 dragOffset, dragUv, originalUv, pointerStart;
            private double nextDragUpdate;

            internal EndpointPane(MorphGroupEditorWindow editor, bool isFrom)
                : base(editor, isFrom ? "morphPreviewA" : "morphPreviewB", isFrom ? "A" : "B")
            {
                this.isFrom = isFrom;
                Surface.focusable = true;
                Surface.RegisterCallback<PointerDownEvent>(Down);
                Surface.RegisterCallback<PointerMoveEvent>(Move);
                Surface.RegisterCallback<PointerUpEvent>(Up);
                Surface.RegisterCallback<PointerCaptureOutEvent>(evt =>
                {
                    if (pointer != evt.pointerId) return;
                    PublishDrag(true);
                    dragged = pointer = -1;
                    RefreshMarkers();
                });
                Surface.RegisterCallback<PointerCancelEvent>(evt =>
                {
                    if (pointer == evt.pointerId) { RollbackDrag(); CancelDrag(); RefreshMarkers(); }
                });
            }

            internal void Bind(Layer layer)
            {
                caption.text = layer == null ? (isFrom ? "A" : "B") : $"{(isFrom ? "A" : "B")} · {layer.layerName}";
                FitFrame();
            }

            internal void Render(WhimTexDocument doc, Layer group, Layer child, bool invalidated)
            {
                if (child == null) { Release(); return; }
                if (failed && !invalidated) return;
                // Fitting the retained image on resize does not re-render either endpoint.
                float scale = Mathf.Min(1f, 512f / Mathf.Max(doc.width, doc.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(doc.width * scale));
                int h = Mathf.Max(1, Mathf.RoundToInt(doc.height * scale));
                try
                {
                    ShowTexture(doc.RenderMorphEndpoint(group, child, w, h));
                }
                catch (Exception ex) { ShowFailure(ex); }
            }

            internal override void Release()
            {
                CancelDrag();
                base.Release();
            }

            private Vector2 UvAt(Vector2 local)
            {
                Rect rect = ImageRect;
                return new Vector2((local.x - rect.xMin) / rect.width, (rect.yMax - local.y) / rect.height);
            }

            private Vector2 AnchorUv(int index)
            {
                var point = editor.transition.anchors[index];
                return isFrom ? point.from : point.to;
            }

            internal void RefreshMarkers()
            {
                int count = editor.transition?.anchors.Count ?? 0;
                int visible = count + (editor.pendingPane == this ? 1 : 0);
                while (markers.Count < visible)
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("whimtex-morph-marker");
                    frame.Add(label);
                    markers.Add(label);
                }
                for (int i = 0; i < markers.Count; i++)
                {
                    var marker = markers[i];
                    SetVisible(marker, i < visible);
                    if (i >= visible) continue;
                    bool pending = i == count;
                    Vector2 uv = pending ? editor.pendingUv : i == dragged ? dragUv : AnchorUv(i);
                    marker.text = pending ? "+" : (i + 1).ToString();
                    marker.EnableInClassList("whimtex-morph-marker--selected", !pending && i == editor.selected);
                    marker.EnableInClassList("whimtex-morph-marker--pending", pending);
                    PositionMarker(marker, uv);
                }
            }

            private static void PositionMarker(VisualElement marker, Vector2 uv)
            {
                marker.style.left = Length.Percent(100f * uv.x);
                marker.style.top = Length.Percent(100f * (1f - uv.y));
            }

            private int Hit(Vector2 local)
            {
                Rect imageRect = ImageRect;
                int best = -1;
                float distance = 10f * 10f;
                for (int i = 0; i < (editor.transition?.anchors.Count ?? 0); i++)
                {
                    Vector2 uv = AnchorUv(i);
                    var center = new Vector2(imageRect.xMin + uv.x * imageRect.width, imageRect.yMax - uv.y * imageRect.height);
                    float squared = (local - center).sqrMagnitude;
                    if (squared <= distance) { best = i; distance = squared; }
                }
                return best;
            }

            private void Down(PointerDownEvent evt)
            {
                Rect imageRect = ImageRect;
                if (pointer >= 0 || !editor.Writable || Texture == null || evt.button != 0 && evt.button != 1 ||
                    imageRect.width <= 0 || imageRect.height <= 0) return;
                Vector2 local = evt.localPosition;
                int hit = Hit(local);
                if (hit < 0 && (evt.button != 0 || !imageRect.Contains(local))) return;
                Surface.Focus();
                if (hit < 0 || editor.pendingPane != null && editor.pendingPane != this && evt.button == 0)
                {
                    // A second endpoint may coincide with one of the existing controls.
                    editor.Place(this, HitUv(local));
                }
                else
                {
                    editor.document.SetPlayback(false);
                    editor.CancelGesture();
                    editor.selected = hit;
                    if (evt.button == 1) editor.RemoveSelected();
                    else
                    {
                        dragged = hit;
                        originalUv = dragUv = AnchorUv(hit);
                        pointerStart = local;
                        undoRecorded = false;
                        nextDragUpdate = 0;
                        dragOffset = dragUv - UvAt(local);
                        pointer = evt.pointerId;
                        Surface.CapturePointer(pointer);
                    }
                    editor.RefreshControls();
                }
                evt.StopPropagation();
            }

            private Vector2 HitUv(Vector2 local)
            {
                Vector2 uv = UvAt(local);
                return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            }

            private Vector2 DragPosition(Vector2 local)
            {
                if (local.Equals(pointerStart)) return originalUv;
                Vector2 uv = UvAt(local) + dragOffset;
                return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            }

            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer || dragged < 0) return;
                dragUv = DragPosition(evt.localPosition);
                PositionMarker(markers[dragged], dragUv);
                PublishDrag();
                evt.StopPropagation();
            }

            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0 || dragged < 0) return;
                dragUv = DragPosition(evt.localPosition);
                PublishDrag(true);
                CancelDrag();
                editor.RefreshControls();
                evt.StopPropagation();
            }

            internal void PublishDrag(bool immediate = false)
            {
                if (dragged < 0 || !editor.Writable || AnchorUv(dragged).Equals(dragUv)) return;
                double now = EditorApplication.timeSinceStartup;
                if (!immediate && now < nextDragUpdate) return;
                nextDragUpdate = now + 1d / 20d;
                if (!undoRecorded)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(editor.document, "Move Morph Anchor");
                    undoRecorded = true;
                }
                // The canvas receives ordinary document changes. Only this pane keeps its capture.
                editor.publishingDrag = true;
                try
                {
                    var point = editor.transition.anchors[dragged];
                    if (isFrom) point.from = dragUv;
                    else point.to = dragUv;
                    editor.document.MarkChanged();
                }
                finally { editor.publishingDrag = false; }
            }

            internal void RollbackDrag()
            {
                if (dragged < 0 || !undoRecorded) return;
                dragUv = originalUv;
                PublishDrag(true);
            }

            internal void CancelDrag()
            {
                int id = pointer;
                dragged = pointer = -1;
                if (id >= 0 && Surface.HasPointerCapture(id)) Surface.ReleasePointer(id);
            }
        }
    }
}
