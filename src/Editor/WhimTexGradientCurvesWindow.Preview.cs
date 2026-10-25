using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientCurvesWindow
    {
        private VisualElement previewStrip, gestureTarget;
        private IVisualElementScheduledItem previewUpdate;
        private bool previewQueued, draggingStrip, stripSelection, stripCanRemove, stripRemovalPending;
        private double previewStart, previewSpan;
        private WhimTexGradientWrapMode previewWrap;
        private WhimTexGradient stripSource;
        private uint stripRevision;
        private readonly List<StripMarker> stripMarkers = new List<StripMarker>();

        private struct StripMarker
        {
            internal WhimTexGradientCurveKey key;
            internal int[] ids;
            internal int channelMask;
            internal Color color;
        }

        private void BuildPreviewStrip(VisualElement root)
        {
            previewUpdate?.Pause(); previewQueued = false;
            previewStrip = new VisualElement { name = "gradientCurvesStrip", focusable = true };
            previewStrip.AddToClassList("whimtex-gradient-curves-strip");
            preview = new VisualElement { name = "gradientCurvesPreview", pickingMode = PickingMode.Ignore };
            preview.AddToClassList("whimtex-gradient-curves-preview");
            preview.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
            previewImage = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            previewImage.AddToClassList("whimtex-gradient-curves-preview-image");
            preview.Add(previewImage); previewStrip.Add(preview);
            var markers = new VisualElement { pickingMode = PickingMode.Ignore };
            markers.AddToClassList("whimtex-gradient-curves-strip-markers");
            markers.generateVisualContent += DrawStripMarkers;
            previewStrip.Add(markers); root.Add(previewStrip);
            previewStrip.RegisterCallback<GeometryChangedEvent>(_ => { RequestPreview(); markers.MarkDirtyRepaint(); });
            previewStrip.RegisterCallback<PointerDownEvent>(StripDown);
            previewStrip.RegisterCallback<PointerMoveEvent>(Move);
            previewStrip.RegisterCallback<PointerUpEvent>(Up);
            previewStrip.RegisterCallback<PointerCaptureOutEvent>(_ => { if (draggingStrip) EndGesture(false); });
            previewStrip.RegisterCallback<PointerCancelEvent>(_ => { if (draggingStrip) EndGesture(true); });
            previewStrip.RegisterCallback<KeyDownEvent>(KeyDown);
            previewStrip.AddManipulator(new ContextualMenuManipulator(StripMenu));
            previewStrip.RegisterCallback<DetachFromPanelEvent>(_ => { previewUpdate?.Pause(); previewQueued = false; });
        }

        private void RequestPreview()
        {
            if (previewStrip == null) return;
            foreach (var child in previewStrip.Children()) child.MarkDirtyRepaint();
            if (previewQueued) return;
            previewQueued = true;
            previewUpdate = previewStrip.schedule.Execute(() => { previewQueued = false; UpdatePreview(); });
        }

        private void UpdatePreview()
        {
            if (gradient == null || previewImage == null || !(view.width > 0)) return;
            bool changed = previewSnapshot == null || !previewSnapshot.EqualsRamp(gradient);
            if (previewTexture == null || changed || previewStart != view.x || previewSpan != view.width || previewWrap != gradient.WrapMode)
            {
                previewTexture = WhimTexGradientWindow.BakePreviewTexture(gradient, previewTexture, previewRamp, previewPixels,
                    timeStart: view.x, timeEnd: view.x + view.width);
                previewTexture.filterMode = FilterMode.Bilinear;
                if (changed) previewSnapshot = gradient.Clone();
                previewStart = view.x; previewSpan = view.width; previewWrap = gradient.WrapMode;
            }
            previewImage.image = previewTexture;
        }

        private void RefreshStripMarkers()
        {
            if (gradient == null || ReferenceEquals(stripSource, gradient) && stripRevision == gradient.Revision) return;
            stripSource = gradient; stripRevision = gradient.Revision; stripMarkers.Clear();
            foreach (var key in gradient.GetCustomMarkers(null))
            {
                int[] ids = gradient.GetCustomMarkerGroup(key.id);
                int mask = 0;
                foreach (int id in ids) if (gradient.TryGetCustomKey(id, out var item)) mask |= 1 << item.channel;
                stripMarkers.Add(new StripMarker { key = key, ids = ids, channelMask = mask,
                    color = mask == 8 ? new Color(1, 1, 1, Mathf.Clamp01(key.value)) :
                        GradientCustomMarkers.Swatch(gradient, key, false) });
            }
        }

        private float StripX(float time) => (float)((time - view.x) / view.width * previewStrip.contentRect.width);
        private float StripTime(float x) => (float)(view.x + x / Mathf.Max(1, previewStrip.contentRect.width) * view.width);
        private bool MarkerSelected(StripMarker marker)
        {
            foreach (int id in marker.ids) if (selected.Contains(id)) return true;
            return false;
        }
        private int HitStrip(Vector2 point)
        {
            if (point.y < 22 || point.y > 44) return -1;
            int found = -1; float nearest = 9;
            for (int i = 0; i < stripMarkers.Count; i++)
            {
                float x = StripX(stripMarkers[i].key.time);
                if (x < 0 || x > previewStrip.contentRect.width) continue;
                float distance = Mathf.Abs(x - point.x);
                if (distance > nearest) continue;
                if (distance == nearest && found >= 0 && !MarkerSelected(stripMarkers[i])) continue;
                nearest = distance; found = i;
            }
            return found;
        }
        private void DrawStripMarkers(MeshGenerationContext context)
        {
            if (gradient == null || !(view.width > 0)) return;
            var p = context.painter2D;
            for (int pass = 0; pass < 2; pass++)
            foreach (var marker in stripMarkers)
            {
                bool active = MarkerSelected(marker);
                if (active != (pass == 1)) continue;
                float x = StripX(marker.key.time);
                if (x < 0 || x > previewStrip.contentRect.width) continue;
                bool removing = stripRemovalPending && active;
                Color color = marker.color;
                if (marker.channelMask == 8) DrawAlphaStripMarker(p, x, color.a, removing ? .25f : 1);
                else
                {
                    if (removing) color.a *= .25f;
                    p.fillColor = color;
                    StripMarkerPath(p, x); p.Fill();
                }
                p.strokeColor = removing ? new Color(1, .3f, .25f) : active ? new Color(.3f, .7f, 1) : Color.black;
                p.lineWidth = 2;
                StripMarkerPath(p, x); p.Stroke();
            }
        }
        private static void StripMarkerPath(Painter2D p, float x)
        {
            p.BeginPath(); p.MoveTo(new Vector2(x, 24)); p.LineTo(new Vector2(x - 6, 31));
            p.LineTo(new Vector2(x - 6, 41)); p.LineTo(new Vector2(x + 6, 41));
            p.LineTo(new Vector2(x + 6, 31)); p.ClosePath();
        }
        private static void DrawAlphaStripMarker(Painter2D p, float x, float alpha, float opacity)
        {
            float dark = Mathf.Lerp(.20f, 1, alpha), light = Mathf.Lerp(.42f, 1, alpha);
            p.fillColor = new Color(dark, dark, dark, opacity);
            StripMarkerPath(p, x); p.Fill();
            if (alpha >= 1) return;
            p.fillColor = new Color(light, light, light, opacity);
            p.BeginPath();
            const float size = 6;
            for (int row = -1; row < 2; row++)
            for (int column = 0; column < 2; column++)
            {
                if (((row + column) & 1) == 0) continue;
                float top = 30 + row * size, bottom = Mathf.Min(top + size, 41);
                float topHalf = Mathf.Min(6, (top - 24) * 6 / 7), bottomHalf = Mathf.Min(6, (bottom - 24) * 6 / 7);
                float left = Mathf.Max(-6 + column * size, -bottomHalf);
                float right = Mathf.Min(-6 + (column + 1) * size, bottomHalf);
                if (right <= left) continue;
                p.MoveTo(new Vector2(x + left, bottom));
                p.LineTo(new Vector2(x + left, Mathf.Max(top, 24 + Mathf.Abs(left) * 7 / 6)));
                if (left < -topHalf && right > -topHalf) p.LineTo(new Vector2(x - topHalf, top));
                if (left < topHalf && right > topHalf) p.LineTo(new Vector2(x + topHalf, top));
                p.LineTo(new Vector2(x + right, Mathf.Max(top, 24 + Mathf.Abs(right) * 7 / 6)));
                p.LineTo(new Vector2(x + right, bottom)); p.ClosePath();
            }
            p.Fill();
        }
        private void SelectStripMarker(StripMarker marker, bool additive)
        {
            if (!additive) selected.Clear();
            foreach (int id in marker.ids) selected.Add(id);
            activeId = marker.key.id; channel = marker.key.channel;
            visibleChannels |= marker.channelMask; stripSelection = true; Refresh();
        }
        private void StripDown(PointerDownEvent e)
        {
            if (e.button != 0 || pointerDown || gradient == null || read == null) return;
            previewStrip.Focus();
            int hit = HitStrip(e.localPosition);
            if (hit < 0)
            {
                float time = StripTime(e.localPosition.x);
                if (e.clickCount == 2 && time >= 0 && time <= 1 && AddStripKey(time))
                    BeginKeyDrag(e, previewStrip, true, mutationUndoGroup);
                e.StopPropagation(); return;
            }
            SelectStripMarker(stripMarkers[hit], e.ctrlKey || e.commandKey);
            BeginKeyDrag(e, previewStrip, true);
        }
        private void MoveStrip(Vector2 point)
        {
            SetStripRemovalPending(stripCanRemove && IsStripRemovalPosition(point));
            if (stripRemovalPending) return;
            if (!dragStart.TryGetCustomKey(activeId, out var anchor)) return;
            float delta = (float)((point.x - downPoint.x) / Mathf.Max(1, previewStrip.contentRect.width) * downView.width);
            float time = SnapTime(anchor.time + delta, dragSelection);
            var next = dragStart.Clone();
            try { next.MoveCustomKeys(dragSelection, time - anchor.time); Apply(next); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException) { status.text = error.Message; }
        }
        private bool IsStripRemovalPosition(Vector2 point)
        {
            const float margin = 16;
            return point.y < -margin || point.y > previewStrip.contentRect.height + margin;
        }
        private void SetStripRemovalPending(bool value)
        {
            if (stripRemovalPending == value) return;
            stripRemovalPending = value;
            foreach (var child in previewStrip.Children()) child.MarkDirtyRepaint();
        }
        private void FinishStripDrag(Vector2 point)
        {
            string errorMessage = null;
            try
            {
                if (stripCanRemove && IsStripRemovalPosition(point))
                {
                    var next = gradient.Clone();
                    next.RemoveCustomKeys(dragSelection);
                    Apply(next);
                }
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            { errorMessage = error.Message; }
            finally { EndGesture(false); }
            if (errorMessage != null) status.text = errorMessage;
        }
        private bool AddStripKey(float time)
        {
            int id = 0;
            if (!Mutate("Add Gradient Keys", g => { id = g.AddCustomColorKeys(time); g.AddCustomKey(3, time); })) return false;
            foreach (var marker in stripMarkers)
                if (Array.IndexOf(marker.ids, id) >= 0) { SelectStripMarker(marker, false); return true; }
            return false;
        }
        private void StripMenu(ContextualMenuPopulateEvent e)
        {
            int hit = HitStrip(e.localMousePosition);
            if (hit >= 0) SelectStripMarker(stripMarkers[hit], false);
            float time = StripTime(e.localMousePosition.x);
            e.menu.AppendAction("Add Keys", _ => AddStripKey(time), time >= 0 && time <= 1 ?
                DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            e.menu.AppendAction("Delete Selected Keys", _ => Delete(), selected.Count > 0 ?
                DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            if (hit < 0) return;
            foreach (int id in stripMarkers[hit].ids)
                if (gradient.TryGetCustomKey(id, out var key))
                    e.menu.AppendAction("Select/" + gradient.CustomChannelName(key.channel), _ => { stripSelection = false; Select(id, false); });
        }

        private float SnapTime(float time, int[] moving)
        {
            float result = time; double nearest = 5 * view.width / Math.Max(1, plot.contentRect.width);
            Try(0); Try(1);
            foreach (var key in (dragStart ?? gradient).CustomKeys)
                if (Array.IndexOf(moving, key.id) < 0) Try(key.time);
            return result;
            void Try(float value)
            {
                double distance = Math.Abs((double)value - time);
                if (distance > nearest) return;
                nearest = distance; result = value;
            }
        }
        private float SnapValue(WhimTexGradientCurveKey anchor, float movedTime, float value)
        {
            float result = value; double nearest = 5 * view.height / Math.Max(1, plot.contentRect.height);
            Try(0); Try(1);
            if (dragStart.WrapMode == WhimTexGradientWrapMode.Repeat && (movedTime == 0 || movedTime == 1))
            {
                WhimTexGradientCurveKey opposite = default;
                foreach (var key in dragStart.CustomKeys)
                    if (key.channel == anchor.channel && (opposite.id == 0 ||
                        (movedTime == 0 ? key.time > opposite.time : key.time < opposite.time))) opposite = key;
                if (opposite.id != 0 && Array.IndexOf(dragSelection, opposite.id) < 0) Try(opposite.value);
            }
            return result;
            void Try(float candidate)
            {
                double distance = Math.Abs((double)candidate - value);
                if (distance > nearest) return;
                nearest = distance; result = candidate;
            }
        }
    }
}
