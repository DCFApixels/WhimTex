using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private VisualElement correctionOptions;
        private EnumField correctionMode;
        private FloatField correctionWidth, correctionFeather, correctionStrength;
        private Toggle foregroundContour;
        private bool SupportsCorrections => IsRaster && !morph.classicLiao && !MarkTool;
        private bool HasCorrectionGuide => SupportsCorrections && (SelectedPoint != null || SelectedEdge != null);
        private MorphGuideCorrection SelectedCorrection => SelectedPoint?.correction ?? SelectedEdge?.correction ?? default;

        private void AddCorrectionControls(VisualElement parent)
        {
            correctionOptions = new VisualElement();
            parent.Add(correctionOptions);
            correctionMode = WhimTexUI.ConfigureField(new EnumField("Correction", MorphCorrectionMode.None)
                { name = "morphCorrectionMode" });
            correctionOptions.Add(correctionMode);
            correctionMode.RegisterValueChangedCallback(evt =>
            {
                var next = SelectedCorrection;
                var mode = (MorphCorrectionMode)evt.newValue;
                if (next.Equals(default(MorphGuideCorrection))) next = MorphGuideCorrection.Create(mode);
                next.mode = mode;
                CommitCorrection(next);
            });
            correctionWidth = AddAmount("Width", "morphCorrectionWidth");
            correctionFeather = AddAmount("Feather", "morphCorrectionFeather");
            correctionStrength = AddAmount("Strength", "morphCorrectionStrength");
            foregroundContour = WhimTexUI.ConfigureField(new Toggle("Foreground Contour") { name = "morphForegroundContour" });
            correctionOptions.Add(foregroundContour);
            foregroundContour.RegisterValueChangedCallback(evt =>
            {
                var edge = SelectedEdge;
                var loop = Writable && HasCorrectionGuide && edge != null ? transition.ClosedContour(edge) : null;
                if (loop == null || edge.foreground == evt.newValue)
                {
                    foregroundContour.SetValueWithoutNotify(edge?.foreground ?? false);
                    return;
                }
                EditSelectedGuide("Change Morph Foreground Contour", () =>
                {
                    foreach (var member in loop) member.foreground = evt.newValue;
                });
            });

            FloatField AddAmount(string label, string name)
            {
                var field = WhimTexUI.ConfigureField(new MorphNumberField(label) { name = name });
                correctionOptions.Add(field);
                field.RegisterValueChangedCallback(evt =>
                {
                    var next = SelectedCorrection;
                    if (next.mode == MorphCorrectionMode.None || !MorphTransition.ValidRadius(evt.newValue))
                    {
                        ShowAmounts(next);
                        return;
                    }
                    if (field == correctionWidth) next.width = evt.newValue;
                    else if (field == correctionFeather) next.feather = evt.newValue;
                    else next.strength = evt.newValue;
                    CommitCorrection(next);
                });
                return field;
            }
        }

        private void CommitCorrection(MorphGuideCorrection next)
        {
            if (!Writable || !HasCorrectionGuide || SelectedCorrection.Equals(next))
            {
                RefreshCorrectionControls();
                return;
            }
            var point = SelectedPoint;
            var edge = SelectedEdge;
            EditSelectedGuide("Change Morph Guide Correction", () =>
            {
                if (point != null) point.correction = next;
                else edge.correction = next;
            });
        }

        private void ShowAmounts(MorphGuideCorrection value)
        {
            correctionWidth.SetValueWithoutNotify(value.width);
            correctionFeather.SetValueWithoutNotify(value.feather);
            correctionStrength.SetValueWithoutNotify(value.strength);
        }

        private void RefreshCorrectionControls()
        {
            SetVisible(correctionOptions, SupportsCorrections);
            correctionOptions.SetEnabled(Writable && HasCorrectionGuide);
            var value = SelectedCorrection;
            correctionMode.SetValueWithoutNotify(value.mode);
            ShowAmounts(value);
            bool active = value.mode != MorphCorrectionMode.None;
            correctionWidth.SetEnabled(active);
            correctionFeather.SetEnabled(active);
            correctionStrength.SetEnabled(active);
            var edge = SelectedEdge;
            SetVisible(foregroundContour, SegmentTool);
            foregroundContour.SetValueWithoutNotify(edge?.foreground ?? false);
            foregroundContour.SetEnabled(HasCorrectionGuide && edge != null && transition.ClosedContour(edge) != null);
        }

        private bool AtCorrectionMidpoint => !timelineVisible || (document != null && !document.IsPlaying &&
            pairs.index >= 0 && pairs.index < starts.Count && Mathf.Approximately(.5f,
                (float)((document.PreviewTime - starts[pairs.index]) / (transition?.duration ?? 1f))));

        private sealed class ResultPane : PreviewPane
        {
            private readonly VisualElement overlay, widthHandle, featherHandle;
            private readonly List<MorphSegment> chords = new();
            private readonly List<Vector2> path = new();
            private Vector2 center, normal, dragStart;
            private MorphGuideCorrection initial, draft;
            private int pointer = -1;
            private bool halfwayFrame, editingWidth;
            private float ShortAxis => Mathf.Min(overlay.contentRect.width, overlay.contentRect.height);
            private MorphGuideCorrection Value => pointer >= 0 ? draft : editor.SelectedCorrection;
            private bool ShowOverlay => halfwayFrame && Texture != null && !editor.modelDirty && editor.HasCorrectionGuide &&
                editor.SelectedCorrection.mode != MorphCorrectionMode.None && editor.AtCorrectionMidpoint;

            internal ResultPane(MorphGroupEditorWindow editor) : base(editor, "morphPreviewResult", "Result")
            {
                overlay = new VisualElement { name = "morphCorrectionOverlay", pickingMode = PickingMode.Ignore };
                overlay.StretchToParentSize();
                overlay.AddToClassList("whimtex-morph-correction-overlay");
                frame.Add(overlay);
                overlay.generateVisualContent += Draw;
                overlay.RegisterCallback<GeometryChangedEvent>(_ => RefreshOverlay());
                widthHandle = AddHandle(true);
                featherHandle = AddHandle(false);
                Surface.focusable = true;
                Surface.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (pointer != evt.pointerId) return;
                    UpdateDraft(evt.position);
                    evt.StopPropagation();
                });
                Surface.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (pointer != evt.pointerId || evt.button != 0) return;
                    UpdateDraft(evt.position);
                    if (pointer >= 0)
                    {
                        var next = draft;
                        CancelDrag();
                        editor.CommitCorrection(next);
                    }
                    evt.StopPropagation();
                });
                Surface.RegisterCallback<PointerCaptureOutEvent>(evt => { if (pointer == evt.pointerId) CancelDrag(); });
                Surface.RegisterCallback<PointerCancelEvent>(evt => { if (pointer == evt.pointerId) CancelDrag(); });
                Surface.RegisterCallback<BlurEvent>(_ => CancelDrag());
            }

            private VisualElement AddHandle(bool width)
            {
                var handle = new VisualElement { name = width ? "morphCorrectionWidthHandle" : "morphCorrectionFeatherHandle" };
                handle.AddToClassList("whimtex-morph-correction-handle");
                handle.AddToClassList(width ? "whimtex-morph-width-handle" : "whimtex-morph-feather-handle");
                overlay.Add(handle);
                handle.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || pointer >= 0 || !editor.Writable || !ShowOverlay || !(ShortAxis > 0) ||
                        !width && editor.SelectedCorrection.width <= 0) return;
                    Surface.Focus();
                    initial = draft = editor.SelectedCorrection;
                    dragStart = overlay.WorldToLocal(evt.position);
                    editingWidth = width;
                    pointer = evt.pointerId;
                    Surface.CapturePointer(pointer);
                    evt.StopPropagation();
                });
                return handle;
            }

            private void UpdateDraft(Vector2 position)
            {
                if (!editor.Writable || !ShowOverlay || !(ShortAxis > 0)) { CancelDrag(); return; }
                float delta = Vector2.Dot(overlay.WorldToLocal(position) - dragStart, normal) / ShortAxis;
                if (editingWidth) draft.width = Mathf.Clamp01(initial.width + delta);
                else draft.feather = Mathf.Clamp01(initial.feather + delta / initial.width);
                editor.ShowAmounts(draft);
                PositionHandles();
            }

            internal void CancelDrag()
            {
                if (pointer < 0) return;
                int id = pointer;
                pointer = -1;
                if (Surface.HasPointerCapture(id)) Surface.ReleasePointer(id);
                editor.ShowAmounts(editor.SelectedCorrection);
                PositionHandles();
            }

            internal override void Release()
            {
                CancelDrag();
                halfwayFrame = false;
                base.Release();
            }

            internal void SetFrameTime(float time)
            {
                halfwayFrame = Mathf.Approximately(time, .5f);
                RefreshOverlay();
            }

            internal void RefreshOverlay()
            {
                bool visible = ShowOverlay;
                SetVisible(overlay, visible);
                if (!visible) { CancelDrag(); return; }
                overlay.SetEnabled(editor.Writable);
                if (!(ShortAxis > 0)) return;
                var rect = overlay.contentRect;
                Vector2 Pixel(Vector2 uv) => new Vector2(rect.xMin + uv.x * rect.width, rect.yMax - uv.y * rect.height);
                path.Clear();
                var point = editor.SelectedPoint;
                if (point != null) path.Add(Pixel((point.from + point.to) * .5f));
                else
                {
                    // Use the atlas correspondences, including paired marks and independent cubic arc lengths.
                    var edge = editor.SelectedEdge;
                    var pair = editor.transition;
                    chords.Clear();
                    edge.AppendLinear(pair.Position(edge, false), pair.Position(edge, true), new Vector2(Texture.width, Texture.height), chords);
                    if (chords.Count == 0) { SetVisible(overlay, false); CancelDrag(); return; }
                    path.Add(Pixel((chords[0].fromStart + chords[0].toStart) * .5f));
                    foreach (var chord in chords) path.Add(Pixel((chord.fromEnd + chord.toEnd) * .5f));
                }
                center = path[0];
                normal = Vector2.right;
                float length = 0;
                for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
                if (length == 0 && path.Count > 1) path.RemoveRange(1, path.Count - 1);
                float remaining = length * .5f;
                for (int i = 1; i < path.Count; i++)
                {
                    var axis = path[i] - path[i - 1];
                    float span = axis.magnitude;
                    if (span > 0 && remaining <= span)
                    {
                        center = Vector2.Lerp(path[i - 1], path[i], remaining / span);
                        normal = new Vector2(-axis.y, axis.x) / span;
                        break;
                    }
                    remaining -= span;
                }
                if (Vector2.Dot(normal, rect.center - center) < 0) normal = -normal;
                PositionHandles();
            }

            private void PositionHandles()
            {
                var value = Value;
                float radius = value.width * ShortAxis;
                float core = radius * (1 - value.feather);
                // Separate the grips tangentially for very narrow bands; dragging still measures the normal.
                Vector2 across = radius > 0 && radius + core < 12 ? new Vector2(-normal.y, normal.x) * 6 : Vector2.zero;
                Position(widthHandle, center + normal * radius + across);
                Position(featherHandle, center - normal * core - across);
                SetVisible(featherHandle, value.width > 0);
                overlay.MarkDirtyRepaint();

                void Position(VisualElement handle, Vector2 p)
                {
                    // Keep a grip reachable when the band's edge falls outside the image.
                    var rect = overlay.contentRect;
                    float inset = Mathf.Min(ShortAxis * .5f, 5f);
                    handle.style.left = Mathf.Clamp(p.x, rect.xMin + inset, rect.xMax - inset);
                    handle.style.top = Mathf.Clamp(p.y, rect.yMin + inset, rect.yMax - inset);
                }
            }

            private void Draw(MeshGenerationContext context)
            {
                if (path.Count == 0 || !ShowOverlay) return;
                var painter = context.painter2D;
                var value = Value;
                var tint = value.mode == MorphCorrectionMode.LocalChoice ? new Color(1, .72f, .16f) : new Color(.28f, .85f, 1);
                float radius = value.width * ShortAxis;
                Band(radius, .06f + .16f * value.strength);
                Band(radius * (1 - value.feather), .18f * value.strength);
                Band(.8f, .95f);

                void Band(float r, float alpha)
                {
                    if (!(r > 0 && alpha > 0)) return;
                    tint.a = alpha;
                    painter.BeginPath();
                    if (path.Count == 1)
                    {
                        painter.Arc(path[0], r, 0, 360);
                        painter.fillColor = tint;
                        painter.Fill();
                    }
                    else
                    {
                        painter.MoveTo(path[0]);
                        for (int i = 1; i < path.Count; i++) painter.LineTo(path[i]);
                        painter.lineCap = LineCap.Round;
                        painter.lineJoin = LineJoin.Round;
                        painter.lineWidth = 2 * r;
                        painter.strokeColor = tint;
                        painter.Stroke();
                    }
                }
            }
        }
    }
}
