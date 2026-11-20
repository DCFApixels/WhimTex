using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow : EditorWindow
    {
        [SerializeField] private WhimTexDocument document;
        [SerializeField] private string groupId;
        [SerializeField] private string fromId, toId;
        private readonly List<Layer> keys = new();
        private readonly List<double> starts = new();
        private Layer group, from, to;
        private MorphGroupLayerBehaviour morph;
        private MorphTransition transition;
        private DropdownField pairs;
        private FloatField duration;
        private Toggle classicLiao, visibilityMask;
        private Toggle preserveGeometry, preserveStationaryDetails;
        private EnumField interpolation, maskBlend;
        private ResultPane result;
        private readonly MorphRasterRenderer.FlowCache resultFlow = new();
        private Slider timeline;
        private Label clock, hint;
        private HelpBox fallback;
        private Button remove, midpoint;
        private EndpointPane a, b, pendingPane;
        private enum GuideTool { Points, Segments, Bezier, Marks }
        private EnumField guideTool;
        private bool IsRaster => morph?.mode == MorphMode.Raster;
        private bool SegmentTool => IsRaster &&
            guideTool?.value is GuideTool.Segments or GuideTool.Bezier or GuideTool.Marks;
        private bool MarkTool => SegmentTool && guideTool.value is GuideTool.Marks;
        private MorphAnchor SelectedPoint => IsRaster && !SegmentTool && selected >= 0 &&
            selected < (transition?.anchors.Count ?? 0) ? transition.anchors[selected] : null;
        private MorphSegment SelectedEdge => SegmentTool && selected >= 0 &&
            selected < (transition?.segments.Count ?? 0) ? transition.segments[selected] : null;
        private int selectedMark = -1, pendingEdge = -1;
        private float pendingParameter;
        private Toggle bezier, smoothJoint;
        private VisualElement guideOptions, influenceOptions;
        private FloatField radius;
        private Toggle boundary;
        private bool lastEnd;
        private Vector2 pendingUv, pendingEnd;
        private int selected = -1;
        private bool modelDirty, previewsDirty, animatedPreviewPending, publishingDrag, timelineVisible;
        private ulong timeRevision;
        private double nextAnimatedPreview;
        private bool Writable => !modelDirty && morph != null && to != null &&
            document != null && !WhimTexApi.IsLayerContentLocked(document, group);
        private double EndTime => starts.Count == 0 ? 0 : starts[starts.Count - 1];

        private sealed class MorphNumberField : FloatField
        {
            private readonly float dragMinimum, dragMaximum;

            internal MorphNumberField(string label, float dragMinimum = 0, float dragMaximum = 1) : base(label)
            {
                this.dragMinimum = dragMinimum;
                this.dragMaximum = dragMaximum;
                isDelayed = true;
            }

            public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, float startValue)
            {
                base.ApplyInputDeviceDelta(delta, speed, startValue);
                // A delayed FloatField accumulates label movement in its text until release.
                float draft = StringToValue(text);
                float bounded = Mathf.Clamp(draft, Mathf.Min(dragMinimum, startValue), dragMaximum);
                if (draft != bounded) text = ValueToString(bounded);
            }
        }

        internal static void Open(WhimTexDocument document, Layer group)
        {
            foreach (var open in Resources.FindObjectsOfTypeAll<MorphGroupEditorWindow>())
                if (open.document == document && open.groupId == group.Id) { open.Focus(); return; }
            var window = CreateInstance<MorphGroupEditorWindow>();
            window.document = document;
            window.groupId = group.Id;
            window.minSize = new Vector2(320, 340);
            window.titleContent = WhimTexBranding.WindowTitle("Edit Morph");
            window.ShowUtility();
        }

        private void OnEnable()
        {
            WhimTexDocument.Changed += DocumentChanged;
            WhimTexDocument.RenderResourcesChanged += ResourcesChanged;
            WhimTexApi.LiveEditLocksChanged += InvalidateModel;
            WhimTexUserSettings.Changed += InvalidateModel;
        }

        private void OnDisable()
        {
            StopMarcoJob();
            WhimTexDocument.Changed -= DocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= ResourcesChanged;
            WhimTexApi.LiveEditLocksChanged -= InvalidateModel;
            WhimTexUserSettings.Changed -= InvalidateModel;
            a?.Release();
            b?.Release();
            result?.Release();
            resultFlow.Clear();
        }

        private void OnLostFocus() => result?.CancelDrag();

        private void DocumentChanged(WhimTexDocument changed)
        {
            if (changed != document) return;
            if (publishingDrag)
            {
                animatedPreviewPending = true;
                return;
            }
            selected = -1;
            InvalidateModel();
        }

        private void ResourcesChanged(WhimTexDocument changed)
        {
            if (changed != document) return;
            previewsDirty = true;
            if (marcoJob != null) checkMarcoKeys = true;
        }

        private void InvalidateModel()
        {
            StopMarcoJob();
            modelDirty = true;
            CancelGesture();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            resultFlow.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/Editor/MorphGroupEditorWindow.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("whimtex-morph-editor");
            var settings = new ScrollView(ScrollViewMode.Vertical);
            settings.AddToClassList("whimtex-morph-settings");
            root.Add(settings);
            pairs = WhimTexUI.ConfigureField(new DropdownField("Transition") { name = "morphTransition" });
            pairs.RegisterValueChangedCallback(_ =>
            {
                int index = pairs.index;
                if (modelDirty || index < 0 || index + 1 >= keys.Count) return;
                SeekMidpoint(index);
            });
            settings.Add(pairs);
            duration = WhimTexUI.ConfigureField(new MorphNumberField("Duration (s)", .001f, float.MaxValue) { name = "morphDuration" });
            duration.RegisterValueChangedCallback(evt =>
            {
                float value = evt.newValue;
                float previous = transition?.duration ?? 1f;
                if (!Writable || !float.IsFinite(value) || value <= 0) duration.SetValueWithoutNotify(previous);
                else if (value != previous)
                    Edit("Change Morph Duration", () => morph.GetOrCreate(fromId, toId).duration = value);
            });
            settings.Add(duration);
            classicLiao = WhimTexUI.ConfigureField(new Toggle("Classic Liao") { name = "classicLiao" });
            classicLiao.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster)
                    classicLiao.SetValueWithoutNotify(morph?.classicLiao ?? false);
                else if (evt.newValue != morph.classicLiao)
                    Edit("Change Classic Liao", () => morph.classicLiao = evt.newValue);
            });
            settings.Add(classicLiao);
            AddMatchingControls(settings);
            visibilityMask = WhimTexUI.ConfigureField(new Toggle("Visibility Mask") { name = "visibilityMask" });
            visibilityMask.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster || morph.classicLiao)
                    visibilityMask.SetValueWithoutNotify(morph?.visibilityMask ?? true);
                else if (evt.newValue != morph.visibilityMask)
                    Edit("Change Visibility Mask", () => morph.visibilityMask = evt.newValue);
            });
            settings.Add(visibilityMask);
            maskBlend = WhimTexUI.ConfigureField(new EnumField("Mask Blend", MorphMaskBlend.Original) { name = "maskBlend" });
            maskBlend.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || !IsRaster || morph.classicLiao || !morph.visibilityMask)
                    maskBlend.SetValueWithoutNotify(morph?.maskBlend ?? MorphMaskBlend.Original);
                else if ((MorphMaskBlend)evt.newValue != morph.maskBlend)
                    Edit("Change Morph Mask Blend", () => morph.maskBlend = (MorphMaskBlend)evt.newValue);
            });
            settings.Add(maskBlend);
            preserveGeometry = WhimTexUI.ConfigureField(new Toggle("Preserve Geometry") { name = "preserveGeometry" });
            preserveGeometry.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster)
                    preserveGeometry.SetValueWithoutNotify(morph?.preserveGeometry ?? false);
                else if (evt.newValue != morph.preserveGeometry)
                    Edit("Change Preserve Geometry", () => morph.preserveGeometry = evt.newValue);
            });
            settings.Add(preserveGeometry);
            preserveStationaryDetails = WhimTexUI.ConfigureField(new Toggle("Preserve Stationary Details") { name = "preserveStationaryDetails" });
            preserveStationaryDetails.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster)
                    preserveStationaryDetails.SetValueWithoutNotify(morph?.preserveStationaryDetails ?? false);
                else if (evt.newValue != morph.preserveStationaryDetails)
                    Edit("Change Preserve Stationary Details", () => morph.preserveStationaryDetails = evt.newValue);
            });
            settings.Add(preserveStationaryDetails);
            interpolation = WhimTexUI.ConfigureField(new EnumField("Interpolation", MorphInterpolation.Linear) { name = "morphInterpolation" });
            interpolation.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster)
                    interpolation.SetValueWithoutNotify(morph?.interpolation ?? MorphInterpolation.Linear);
                else if ((MorphInterpolation)evt.newValue != morph.interpolation)
                    Edit("Change Morph Interpolation", () => morph.interpolation = (MorphInterpolation)evt.newValue);
            });
            settings.Add(interpolation);
            guideTool = WhimTexUI.ConfigureField(new EnumField("Guide Tool", GuideTool.Points) { name = "morphGuideTool" });
            guideTool.RegisterValueChangedCallback(_ =>
            {
                CancelGesture();
                selected = -1;
                RefreshControls();
            });
            settings.Add(guideTool);
            influenceOptions = WhimTexUI.CreateRow();
            radius = WhimTexUI.ConfigureField(new MorphNumberField("Radius (0 = Auto)")
            {
                name = "morphGuideRadius",
                tooltip = "Fraction of the shorter canvas dimension, from 0 to 1."
            });
            radius.style.flexGrow = 1;
            radius.style.flexShrink = 1;
            radius.style.minWidth = 0;
            radius.RegisterValueChangedCallback(evt =>
            {
                var point = SelectedPoint;
                var edge = SelectedEdge;
                float previous = point?.radius ?? edge?.radius ?? 0;
                if (!Writable || MarkTool || (point == null && edge == null) || !MorphTransition.ValidRadius(evt.newValue))
                    radius.SetValueWithoutNotify(previous);
                else if (evt.newValue != previous)
                    EditSelectedGuide("Change Morph Guide Radius", () =>
                    {
                        if (point != null) point.radius = evt.newValue;
                        else edge.radius = evt.newValue;
                    });
            });
            influenceOptions.Add(radius);
            boundary = new Toggle { name = "morphGuideBoundary", text = "Boundary" };
            boundary.RegisterValueChangedCallback(evt =>
            {
                var edge = SelectedEdge;
                if (!Writable || MarkTool || edge == null) boundary.SetValueWithoutNotify(edge?.boundary ?? false);
                else if (evt.newValue != edge.boundary)
                    EditSelectedGuide("Change Morph Guide Boundary", () => edge.boundary = evt.newValue);
            });
            influenceOptions.Add(boundary);
            settings.Add(influenceOptions);
            bezier = WhimTexUI.ConfigureField(new Toggle("Bézier") { name = "morphGuideBezier" });
            bezier.RegisterValueChangedCallback(evt =>
            {
                var edge = SelectedEdge;
                if (!Writable || edge == null) return;
                EditSelectedGuide("Change Morph Guide Shape", () =>
                {
                    if (evt.newValue) edge.EnableBezier(transition.Position(edge, false), transition.Position(edge, true));
                    else edge.bezier = false;
                    if (edge.startNode > 0) transition.nodes[edge.startNode - 1].smooth = false;
                    if (edge.endNode > 0) transition.nodes[edge.endNode - 1].smooth = false;
                });
            });
            guideOptions = WhimTexUI.CreateRow();
            bezier.style.flexGrow = 1;
            guideOptions.Add(bezier);
            smoothJoint = WhimTexUI.ConfigureField(new Toggle("Smooth Joint") { name = "morphSmoothJoint" });
            smoothJoint.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || !SegmentTool || selected < 0) return;
                var edge = transition.segments[selected];
                if (transition.OtherCubic(edge,lastEnd,out _) == null) return;
                var node = transition.nodes[(lastEnd ? edge.endNode : edge.startNode) - 1];
                EditSelectedGuide("Change Morph Joint Tangents", () =>
                {
                    node.smooth = evt.newValue;
                    if (!node.smooth) return;
                    transition.SetTangent(edge, lastEnd, true, edge.Offset(true, lastEnd));
                    transition.SetTangent(edge, lastEnd, false, edge.Offset(false, lastEnd));
                });
            });
            smoothJoint.style.flexGrow = 1;
            guideOptions.Add(smoothJoint);
            settings.Add(guideOptions);
            AddCorrectionControls(settings);
            fallback = WhimTexUI.AddHelpBox(root,
                "This pair uses Raster interpolation. Vector requires compatible paths, appearance and ordered anchors.",
                HelpBoxMessageType.Info);
            fallback.name = "morphRasterFallback";
            var views = new VisualElement();
            views.AddToClassList("whimtex-morph-views");
            a = new EndpointPane(this, true);
            b = new EndpointPane(this, false);
            views.Add(a);
            views.Add(b);
            result = new ResultPane(this);
            views.Add(result);
            root.Add(views);
            hint = new Label();
            hint.AddToClassList("whimtex-morph-hint");
            root.Add(hint);
            remove = new Button(RemoveSelected) { text = "Remove Pair", name = "removeMorphPair" };
            var actions = WhimTexUI.CreateRow();
            midpoint = new Button(() => SeekMidpoint(pairs.index)) { text = "Preview Midpoint", name = "morphMidpoint" };
            actions.Add(midpoint);
            actions.Add(remove);
            root.Add(actions);
            timeline = WhimTexUI.ConfigureField(new Slider("Timeline", 0, 1) { name = "morphScrub" });
            timeline.RegisterValueChangedCallback(evt => Seek(EndTime * evt.newValue));
            root.Add(timeline);
            clock = new Label();
            root.Add(clock);
            root.UnregisterCallback<KeyDownEvent>(OnKey);
            root.RegisterCallback<KeyDownEvent>(OnKey);
            RefreshDocument();
        }

        private void Update()
        {
            if (document == null) { Close(); return; }
            if (pairs == null) return;
            if (modelDirty) RefreshDocument();
            if (timeRevision != document.TimeRevision) RefreshTime();
            PollMarcoJob();
            a.PublishDrag();
            b.PublishDrag();
            if (previewsDirty || animatedPreviewPending && EditorApplication.timeSinceStartup >= nextAnimatedPreview)
            {
                bool rasterPair = morph?.mode == MorphMode.Raster && to != null;
                double time = document.PreviewTime;
                double duration = transition?.duration ?? 1f;
                if (rasterPair && !timelineVisible) time = starts[pairs.index] + .5 * duration;
                using var scope = document.EvaluateAtTime(time);
                if (previewsDirty || from != null && document.LayerPreviewUsesTime(from)) a.Render(document, group, from, previewsDirty);
                if (previewsDirty || to != null && document.LayerPreviewUsesTime(to)) b.Render(document, group, to, previewsDirty);
                if (rasterPair) RenderResult(Mathf.Clamp01((float)((time - starts[pairs.index]) / duration)));
                previewsDirty = animatedPreviewPending = false;
                RefreshMarcoAction();
                nextAnimatedPreview = EditorApplication.timeSinceStartup + .08;
            }
            RefreshFallback();
        }

        private void RenderResult(float time)
        {
            if (a.Texture == null || b.Texture == null)
            {
                result.Release();
                resultFlow.Clear();
                return;
            }
            try
            {
                var edges = transition?.RasterGuides(a.Texture.width, a.Texture.height);
                var matching = morph.classicLiao ? MorphMatching.Liao : morph.matching;
                var points = transition?.RasterAnchors(matching == MorphMatching.MARCO, edges, a.Texture.width, a.Texture.height);
                result.ShowTexture(MorphRasterRenderer.RenderGuided(IsRaster, a.Texture, b.Texture, points, time, resultFlow,
                    (from, to, a.Revision, b.Revision, a.Texture.width, a.Texture.height, 1f),
                    morph.interpolation, morph.classicLiao, morph.preserveGeometry, edges, morph.preserveStationaryDetails, morph.visibilityMask,
                    matching == MorphMatching.ThreeHypotheses, morph.maskBlend));
                result.SetFrameTime(time);
            }
            catch (Exception ex) { result.ShowFailure(ex); }
        }

        private void RefreshDocument()
        {
            modelDirty = false;
            group = document != null ? document.FindLayer(groupId) : null;
            morph = group?.Behaviour as MorphGroupLayerBehaviour;
            keys.Clear();
            starts.Clear();
            var choices = new List<string>();
            double start = 0;
            int previous = -1;
            if (morph != null)
            {
                foreach (var child in group.children)
                {
                    if (!MorphGroupLayerBehaviour.IsKey(child)) continue;
                    if (keys.Count > 0)
                    {
                        var last = keys[keys.Count - 1];
                        if (last.Id == fromId && child.Id == toId) previous = choices.Count;
                        choices.Add($"{keys.Count}. {last.layerName} → {child.layerName}");
                        start += morph.Find(last.Id, child.Id)?.duration ?? 1f;
                    }
                    keys.Add(child);
                    starts.Add(start);
                }
                titleContent = WhimTexBranding.WindowTitle("Edit Morph — " + group.layerName);
            }
            pairs.choices = choices;
            pairs.SetEnabled(choices.Count > 1);
            timelineVisible = WhimTexUserSettings.ShowTimeline;
            SetVisible(timeline, timelineVisible && choices.Count > 0);
            SetVisible(clock, timelineVisible && keys.Count > 0);
            int index = timelineVisible || previous < 0 ? PairAtTime(document != null ? document.PreviewTime : 0) : previous;
            BindPair(index);
            if (document != null) RefreshTime();
        }

        private int PairAtTime(double time)
        {
            if (keys.Count < 2) return -1;
            int index = starts.BinarySearch(time);
            if (index < 0) index = ~index - 1;
            return Mathf.Clamp(index, 0, keys.Count - 2);
        }

        private void BindPair(int index)
        {
            Layer nextFrom = index >= 0 ? keys[index] : keys.Count == 1 ? keys[0] : null;
            Layer nextTo = index >= 0 ? keys[index + 1] : null;
            if (from != nextFrom || to != nextTo)
            {
                StopMarcoJob();
                marcoMessage = null;
                CancelGesture();
                selected = -1;
                a.Release();
                b.Release();
                result.Release();
            }
            from = nextFrom;
            to = nextTo;
            fromId = from?.Id;
            toId = to?.Id;
            transition = to != null ? morph.Find(fromId, toId) : null;
            pairs.SetValueWithoutNotify(index < 0 ? string.Empty : pairs.choices[index]);
            duration.SetValueWithoutNotify(transition?.duration ?? 1f);
            duration.SetEnabled(Writable);
            bool raster = morph?.mode == MorphMode.Raster;
            SetVisible(classicLiao, raster);
            classicLiao.SetValueWithoutNotify(morph?.classicLiao ?? false);
            classicLiao.SetEnabled(Writable);
            SetVisible(matchingField, raster);
            matchingField.SetValueWithoutNotify(morph?.matching ?? MorphMatching.Liao);
            matchingField.SetEnabled(Writable && morph?.classicLiao == false);
            RefreshMarcoAction();
            SetVisible(visibilityMask, raster);
            SetVisible(maskBlend, raster);
            maskBlend.SetValueWithoutNotify(morph?.maskBlend ?? MorphMaskBlend.Original);
            maskBlend.SetEnabled(Writable && morph?.classicLiao == false && morph.visibilityMask);
            visibilityMask.SetValueWithoutNotify(morph?.visibilityMask ?? true);
            visibilityMask.SetEnabled(Writable && morph?.classicLiao == false);
            SetVisible(preserveGeometry, raster);
            preserveGeometry.SetValueWithoutNotify(morph?.preserveGeometry ?? false);
            preserveGeometry.SetEnabled(Writable);
            SetVisible(preserveStationaryDetails, raster);
            preserveStationaryDetails.SetValueWithoutNotify(morph?.preserveStationaryDetails ?? false);
            preserveStationaryDetails.SetEnabled(Writable);
            SetVisible(guideTool, raster);
            guideTool.SetEnabled(to != null);
            SetVisible(interpolation, raster);
            interpolation.SetValueWithoutNotify(morph?.interpolation ?? MorphInterpolation.Linear);
            interpolation.SetEnabled(Writable);
            SetVisible(result, raster && to != null);
            if (!raster || to == null)
            {
                result.Release();
                resultFlow.Clear();
            }
            SetVisible(midpoint, timelineVisible && to != null);
            RefreshFallback();
            a.Bind(from);
            b.Bind(to);
            RefreshControls();
            previewsDirty = true;
        }

        private void RefreshFallback()
        {
            // The canvas can finish matching a paused pair after the last time or document change.
            bool visible = to != null && morph.mode == MorphMode.Vector && morph.NeedsRasterFor(from, to);
            if (visible != (fallback.style.display.value == DisplayStyle.Flex))
                SetVisible(fallback, visible);
        }

        private void SeekMidpoint(int index)
        {
            if (modelDirty || index < 0 || index + 1 >= starts.Count) return;
            BindPair(index);
            if (timelineVisible) Seek((starts[index] + starts[index + 1]) * .5);
        }

        private void Seek(double time)
        {
            if (modelDirty || document == null) return;
            document.SetPlayback(false);
            document.SetPreviewTime(Math.Min(time, float.MaxValue));
            RefreshTime();
        }

        private void RefreshTime()
        {
            timeRevision = document.TimeRevision;
            animatedPreviewPending = true;
            if (!timelineVisible || keys.Count == 0) return;
            double time = document.PreviewTime;
            int index = PairAtTime(time);
            if (index >= 0 && from != keys[index]) BindPair(index);
            result.RefreshOverlay();
            timeline.SetValueWithoutNotify(EndTime > 0 ? Mathf.Clamp01((float)(time / EndTime)) : 0);
            clock.text = time >= EndTime ? $"{time:0.###} s · Holding {keys[keys.Count - 1].layerName}" :
                $"{time:0.###} / {EndTime:0.###} s";
        }

        private void RefreshControls()
        {
            a.RefreshMarkers();
            b.RefreshMarkers();
            var edge = SelectedEdge;
            var point = SelectedPoint;
            RefreshCorrectionControls();
            result.RefreshOverlay();
            SetVisible(influenceOptions, IsRaster && !MarkTool);
            influenceOptions.SetEnabled(Writable && (point != null || edge != null));
            radius.SetValueWithoutNotify(point?.radius ?? edge?.radius ?? 0);
            SetVisible(boundary, SegmentTool && !MarkTool);
            boundary.SetValueWithoutNotify(edge?.boundary ?? false);
            SetVisible(guideOptions, SegmentTool && !MarkTool);
            bezier.SetEnabled(Writable && edge != null);
            bezier.SetValueWithoutNotify(edge?.bezier ?? false);
            bool joint = edge != null && !MarkTool && transition.OtherCubic(edge, lastEnd, out _) != null;
            smoothJoint.SetEnabled(Writable && joint);
            smoothJoint.SetValueWithoutNotify(joint && transition.nodes[(lastEnd ? edge.endNode : edge.startNode) - 1].smooth);
            remove.text = MarkTool ? "Remove Mark" : SegmentTool ? "Remove Segment" : "Remove Pair";
            remove.SetEnabled(Writable && selected >= 0 && (!MarkTool || selectedMark >= 0));
            hint.text = morph == null ? "This Morph Group is no longer in the document." :
                to == null ? "Add or enable two child layers to edit a transition." :
                !Writable ? "This Morph Group is locked for editing." :
                HasCorrectionGuide && SelectedCorrection.mode != MorphCorrectionMode.None ?
                    (timelineVisible ? "Choose Preview Midpoint. " : "") +
                    "On Result, drag the circle for width or the diamond for feather. Esc cancels." :
                MarkTool ? pendingPane != null ? "Click the matching feature on the same guide in the other image. Keep marks in order." :
                    "Click the same feature on each guide to pair it. Drag an existing mark along its edge." :
                SegmentTool ? pendingPane != null ? $"Draw the matching segment in {(pendingPane == a ? "B" : "A")} in the same direction. Esc cancels." :
                    "Drag along the edge in each preview. Shift-drag starts a new segment on an existing handle." :
                pendingPane != null ? $"Click the matching point in {(pendingPane == a ? "B" : "A")}. Esc cancels." :
                "Click a point in each preview to add a pair. Drag a numbered point to move it.";
        }

        private void Place(EndpointPane pane, Vector2 uv)
        {
            if (!Writable) return;
            document.SetPlayback(false);
            if (pendingPane == null || pendingPane == pane)
            {
                pendingPane = pane;
                pendingUv = uv;
                selected = -1;
            }
            else
            {
                var point = new MorphAnchor { from = pane == a ? uv : pendingUv, to = pane == b ? uv : pendingUv };
                int index = transition?.anchors.Count ?? 0;
                Edit("Add Morph Anchor Pair", () => morph.GetOrCreate(fromId, toId).anchors.Add(point));
                selected = index;
            }
            RefreshControls();
        }

        private void PlaceSegment(EndpointPane pane, Vector2 start, Vector2 end)
        {
            if (!Writable || !SegmentTool || start == end) return;
            if (pendingPane == pane || pendingPane == null)
            {
                pendingPane = pane;
                pendingUv = start;
                pendingEnd = end;
                selected = -1;
            }
            else
            {
                var line = new MorphSegment
                {
                    fromStart = pane == a ? start : pendingUv, fromEnd = pane == a ? end : pendingEnd,
                    toStart = pane == b ? start : pendingUv, toEnd = pane == b ? end : pendingEnd
                };
                int index = transition?.segments.Count ?? 0;
                Edit("Add Morph Guide Segment", () =>
                {
                    var pair = morph.GetOrCreate(fromId, toId);
                    if (guideTool.value is GuideTool.Bezier) line.EnableBezier(line.Pair(false), line.Pair(true));
                    pair.segments.Add(line);
                    pair.BindNodes();
                });
                selected = index;
            }
            RefreshControls();
        }

        private void PlaceMark(EndpointPane pane, int index, float t)
        {
            if (!Writable || !MarkTool || t <= 0 || t >= 1) return;
            document.SetPlayback(false);
            if (pendingPane == null || pendingPane == pane)
            {
                pendingPane = pane;
                pendingEdge = selected = index;
                selectedMark = -1;
                pendingParameter = t;
            }
            else if (pendingEdge == index)
            {
                var mark = pane == a ? new Vector2(t, pendingParameter) : new Vector2(pendingParameter, t);
                var edge = transition.segments[index];
                int at = 0;
                while (at < (edge.marks?.Count ?? 0) && edge.marks[at].x < mark.x) at++;
                Vector2 lo = at > 0 ? edge.marks[at - 1] : Vector2.zero;
                Vector2 hi = at < (edge.marks?.Count ?? 0) ? edge.marks[at] : Vector2.one;
                if (mark.x <= lo.x || mark.y <= lo.y || mark.x >= hi.x || mark.y >= hi.y)
                {
                    hint.text = "Marks must follow the same order on both sides. Choose another position or press Esc.";
                    return;
                }
                Edit("Add Morph Correspondence Mark", () => (edge.marks ??= new()).Insert(at, mark));
                selected = index;
                selectedMark = at;
            }
            RefreshControls();
        }

        private void RemoveSelected()
        {
            if (!Writable || selected < 0) return;
            int index = selected;
            if (MarkTool)
            {
                int mark = selectedMark;
                if (mark >= 0 && mark < (transition?.segments[index].marks?.Count ?? 0))
                    Edit("Remove Morph Correspondence Mark", () => transition.segments[index].marks.RemoveAt(mark));
                return;
            }
            if (SegmentTool)
            {
                if (index < (transition?.segments.Count ?? 0))
                    Edit("Remove Morph Guide Segment", () => transition.RemoveEdge(index));
                return;
            }
            if (index >= (transition?.anchors.Count ?? 0)) return;
            Edit("Remove Morph Anchor Pair", () => transition.anchors.RemoveAt(index));
        }

        private void Edit(string undoName, Action change)
        {
            if (!Writable) return;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(document, undoName);
            change();
            document.MarkChanged();
            RefreshDocument();
        }

        private void EditSelectedGuide(string undoName, Action change)
        {
            int index = selected;
            Edit(undoName, change);
            selected = index;
            RefreshControls();
        }

        private void CancelGesture()
        {
            pendingPane = null;
            pendingEdge = selectedMark = -1;
            a?.CancelDrag();
            b?.CancelDrag();
            result?.CancelDrag();
        }

        private void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                a.RollbackDrag();
                b.RollbackDrag();
                CancelGesture();
                RefreshControls();
            }
            else if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) &&
                (evt.target == a.Surface || evt.target == b.Surface)) RemoveSelected();
            else return;
            evt.StopPropagation();
        }

        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
