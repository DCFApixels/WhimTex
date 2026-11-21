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
        private static MorphGroupEditorWindow instance;
        private WhimTexSplitView columns;
        private ResultPane result;
        private readonly MorphRasterRenderer.FlowCache resultFlow = new();
        private Slider timeline;
        private Label clock, hint;
        private HelpBox fallback;
        private Button remove, midpoint, finishGuide;
        private EndpointPane a, b, pendingPane;
        private bool IsRaster => morph?.mode == MorphMode.Raster;
        private enum GuideSelection { Point, Segment, Path }
        private GuideSelection selection;
        private bool EdgeSelected => IsRaster && selection == GuideSelection.Segment;
        private bool PathSelected => IsRaster && selection == GuideSelection.Path;
        private Vector2 CanvasSize => new Vector2(document.width, document.height);
        private MorphGuidePath SelectedPath => PathSelected && selected >= 0 && selected < (transition?.guides.Count ?? 0)
            ? transition.guides[selected] : null;
        private List<Vector2> SelectedMarks => SelectedPath?.marks ?? SelectedEdge?.marks;
        private bool EditingMark => selectedMark >= 0 || pendingEdge >= 0;
        private MorphAnchor SelectedPoint => IsRaster && selection == GuideSelection.Point && selected >= 0 &&
            selected < (transition?.anchors.Count ?? 0) ? transition.anchors[selected] : null;
        private MorphSegment SelectedEdge => EdgeSelected && selected >= 0 &&
            selected < (transition?.segments.Count ?? 0) ? transition.segments[selected] : null;
        private int selectedMark = -1, pendingEdge = -1;
        private float pendingParameter;
        private Toggle bezier, smoothJoint;
        private VisualElement guideOptions, influenceOptions;
        private FloatField radius;
        private Toggle boundary;
        private bool lastEnd;
        private readonly List<GuideKnot> firstKnots = new(), matchedKnots = new();
        private bool closedDraft, matchingKnots, continuingSegment;
        private int extendingPath = -1;
        private bool extendAtStart;
        private int selected = -1;
        private bool modelDirty, previewsDirty, animatedPreviewPending, publishingDrag, timelineVisible;
        private ulong timeRevision;
        private double nextAnimatedPreview;
        private bool GroupWritable => !modelDirty && morph != null && document != null &&
            !WhimTexApi.IsLayerContentLocked(document, group);
        private bool Writable => GroupWritable && to != null;
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
            if (instance != null)
            {
                instance.ShowGroup(document, group);
                instance.Focus();
                return;
            }
            var window = CreateInstance<MorphGroupEditorWindow>();
            window.document = document;
            window.groupId = group.Id;
            window.minSize = new Vector2(320, 340);
            window.titleContent = WhimTexBranding.WindowTitle("Edit Morph");
            window.ShowUtility();
        }

        internal static void FollowSelection(WhimTexDocument owner, string layerId)
        {
            if (instance == null || owner == null || layerId == null ||
                instance.document == owner && instance.groupId == layerId) return;
            var layer = owner.FindLayer(layerId);
            if (layer?.Behaviour is MorphGroupLayerBehaviour) instance.ShowGroup(owner, layer);
        }

        private void ShowGroup(WhimTexDocument owner, Layer next)
        {
            if (document == owner && groupId == next.Id) return;
            InvalidateModel();
            a?.Release();
            b?.Release();
            result?.Release();
            resultFlow.Clear();
            from = to = null;
            fromId = toId = null;
            selected = -1;
            marcoMessage = null;
            document = owner;
            groupId = next.Id;
            a?.ResetView();
            b?.ResetView();
            result?.ResetView();
            if (pairs != null) RefreshDocument();
        }

        private void OnEnable()
        {
            if (instance != null && instance != this)
            {
                EditorApplication.delayCall += CloseDuplicate;
                return;
            }
            instance = this;
            WhimTexDocument.Changed += DocumentChanged;
            WhimTexDocument.RenderResourcesChanged += ResourcesChanged;
            WhimTexApi.LiveEditLocksChanged += InvalidateModel;
            WhimTexUserSettings.Changed += InvalidateModel;
        }

        private void CloseDuplicate()
        {
            if (this != null && instance != this) Close();
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= CloseDuplicate;
            if (instance == this) instance = null;
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

        private void OnLostFocus()
        {
            a?.EndPan();
            b?.EndPan();
            result?.EndPan();
            result?.CancelDrag();
        }

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
            var header = new VisualElement { name = "morphHeader" };
            header.AddToClassList("whimtex-morph-header");
            header.Add(settings);
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
            AddGroupSettings(settings);
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
                var path = SelectedPath;
                float previous = point?.radius ?? path?.radius ?? edge?.radius ?? 0;
                if (!Writable || EditingMark || (point == null && edge == null && path == null) || !MorphTransition.ValidRadius(evt.newValue))
                    radius.SetValueWithoutNotify(previous);
                else if (evt.newValue != previous)
                    EditSelectedGuide("Change Morph Guide Radius", () =>
                    {
                        if (point != null) point.radius = evt.newValue;
                        else if (path != null) path.radius = evt.newValue;
                        else edge.radius = evt.newValue;
                    });
            });
            influenceOptions.Add(radius);
            boundary = new Toggle { name = "morphGuideBoundary", text = "Boundary" };
            boundary.RegisterValueChangedCallback(evt =>
            {
                var edge = SelectedEdge;
                var path = SelectedPath;
                bool current = path?.boundary ?? edge?.boundary ?? false;
                if (!Writable || EditingMark || path == null && edge == null) boundary.SetValueWithoutNotify(current);
                else if (evt.newValue != current)
                    EditSelectedGuide("Change Morph Guide Boundary", () =>
                    {
                        if (path != null) path.boundary = evt.newValue;
                        else edge.boundary = evt.newValue;
                    });
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
                if (!Writable || !EdgeSelected || selected < 0) return;
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
            fallback = WhimTexUI.AddHelpBox(settings,
                "This pair uses Raster interpolation. Vector requires compatible paths, appearance and ordered anchors.",
                HelpBoxMessageType.Info);
            fallback.name = "morphRasterFallback";
            a = new EndpointPane(this, true);
            b = new EndpointPane(this, false);
            result = new ResultPane(this);
            var workArea = new VisualElement();
            columns = CreateColumns();
            workArea.Add(columns);
            var split = new WhimTexSplitView(header, workArea, 0, 154, false) { name = "morphHeaderSplit" };
            split.style.flexGrow = 1;
            workArea.style.minHeight = 110;
            root.Add(split);
            hint = new Label();
            hint.AddToClassList("whimtex-morph-hint");
            header.Add(hint);
            remove = new Button(RemoveSelected) { text = "Remove Pair", name = "removeMorphPair" };
            var actions = WhimTexUI.CreateRow();
            midpoint = new Button(() => SeekMidpoint(pairs.index)) { text = "Preview Midpoint", name = "morphMidpoint" };
            finishGuide = new Button(FinishGuide) { name = "finishMorphGuide", text = "Finish Guide" };
            actions.Add(midpoint);
            actions.Add(finishGuide);
            actions.Add(remove);
            header.Add(actions);
            timeline = WhimTexUI.ConfigureField(new Slider("Timeline", 0, 1) { name = "morphScrub" });
            timeline.RegisterValueChangedCallback(evt => Seek(EndTime * evt.newValue));
            workArea.Add(timeline);
            clock = new Label();
            workArea.Add(clock);
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
            RefreshGroupSettings();
            columns.SetFixedPaneVisible(raster && to != null);
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
            var path = SelectedPath;
            bool showInfluence = !EditingMark && (point != null || edge != null || path != null);
            SetVisible(influenceOptions, showInfluence);
            influenceOptions.SetEnabled(Writable);
            radius.SetValueWithoutNotify(point?.radius ?? path?.radius ?? edge?.radius ?? 0);
            SetVisible(boundary, (edge != null || path != null) && !EditingMark);
            boundary.SetValueWithoutNotify(path?.boundary ?? edge?.boundary ?? false);
            SetVisible(guideOptions, edge != null && !EditingMark);
            guideOptions.SetEnabled(Writable);
            bezier.SetValueWithoutNotify(edge?.bezier ?? false);
            bool joint = edge != null && transition.OtherCubic(edge, lastEnd, out _) != null;
            SetVisible(smoothJoint, joint);
            smoothJoint.SetValueWithoutNotify(joint && transition.nodes[(lastEnd ? edge.endNode : edge.startNode) - 1].smooth);
            SetVisible(finishGuide, extendingPath >= 0 || matchingKnots && !continuingSegment);
            finishGuide.SetEnabled(Writable && CanFinishGuide);
            remove.text = selectedMark >= 0 ? "Remove Mark" : PathSelected ? "Remove Guide" : EdgeSelected ? "Remove Segment" : "Remove Pair";
            SetVisible(remove, selected >= 0 && pendingPane == null);
            remove.SetEnabled(Writable);
            hint.text = morph == null ? "This Morph Group is no longer in the document." :
                to == null ? "Add or enable two child layers to edit a transition." :
                !Writable ? "This Morph Group is locked for editing." :
                pendingEdge >= 0 ? "Click the matching detail anywhere on the highlighted guide in the other image. Esc cancels." :
                firstKnots.Count > 0 ? DraftHint() :
                HasCorrectionGuide && SelectedCorrection.mode != MorphCorrectionMode.None ?
                    (timelineVisible ? "Choose Preview Midpoint. " : "") + "On Result, drag the circle for width or the diamond for feather." :
                IsRaster ? "Click matching points in A and B, or keep adding vertices on one image. Drag to set tangents." :
                    "Click a point in each preview to add a pair. Drag a numbered point to move it.";
        }

        private readonly struct GuideKnot
        {
            internal readonly Vector2 uv, tangent;
            internal GuideKnot(Vector2 uv, Vector2 tangent) { this.uv = uv; this.tangent = tangent; }
        }

        private bool CanFinishGuide => extendingPath >= 0 ? firstKnots.Count >= 2 :
            matchingKnots && !continuingSegment && matchedKnots.Count >= (closedDraft ? 3 : 2);

        private string DraftHint()
        {
            string other = pendingPane == a ? "B" : "A";
            if (extendingPath >= 0) return "Add vertices on this image. Enter finishes the extension. Esc cancels.";
            if (continuingSegment) return $"Draw the corresponding segment vertices on {other}. Esc cancels.";
            if (matchingKnots) return closedDraft ? "Draw the other contour. Enter closes it. Esc cancels." :
                "Draw this side with as many vertices as it needs. Enter finishes the guide. Esc cancels.";
            if (closedDraft) return $"Draw the corresponding contour on {other}.";
            if (!IsRaster) return $"Click the matching point on {other}. Esc cancels.";
            return $"Add vertices here or start the corresponding shape on {other}. Esc cancels.";
        }

        private void FinishGuide()
        {
            if (!Writable || !CanFinishGuide) return;
            if (extendingPath >= 0) FinishExtension();
            else CommitPath();
            RefreshControls();
        }

        private void BeginExtension(EndpointPane pane, int index, bool atStart)
        {
            var path = transition.guides[index];
            if (!Writable || path.closed) return;
            CancelGesture();
            document.SetPlayback(false);
            extendingPath = index;
            extendAtStart = atStart;
            pendingPane = pane;
            var side = path.Side(pane == a);
            var end = atStart ? side[0] : side[^1];
            firstKnots.Add(new GuideKnot(end.position, atStart ? end.inTangent : end.outTangent));
            selected = -1;
            RefreshControls();
        }

        private static MorphPathKnot CreateKnot(GuideKnot knot) => new MorphPathKnot
        {
            position = knot.uv, inTangent = -knot.tangent, outTangent = knot.tangent, smooth = knot.tangent != Vector2.zero
        };

        private void FinishExtension()
        {
            int index = extendingPath;
            var path = transition.guides[index];
            bool source = pendingPane == a;
            var knots = path.Side(source);
            int oldCount = knots.Count;
            Edit("Extend Morph Guide", () =>
            {
                var extension = new List<MorphPathKnot>(firstKnots.Count - 1);
                for (int i = 1; i < firstKnots.Count; i++)
                {
                    var knot = CreateKnot(firstKnots[i]);
                    if (extendAtStart) (knot.inTangent, knot.outTangent) = (knot.outTangent, knot.inTangent);
                    extension.Add(knot);
                }
                if (extendAtStart) { extension.Reverse(); knots.InsertRange(0, extension); }
                else knots.AddRange(extension);
                float start = extendAtStart ? path.LengthAt(source, extension.Count, 0, CanvasSize) : 0;
                float end = extendAtStart ? 1 : path.LengthAt(source, oldCount - 2, 1, CanvasSize);
                RemapSideMarks(path.marks, source, start, end);
            });
            selection = GuideSelection.Path;
            selected = index;
        }

        // Fractions are stored as floats. Reserve distinct representable positions at both ends
        // when reversing a mark very near the seam or extending a very short marked path.
        private static void RemapSideMarks(List<Vector2> marks, bool source, double start, double end)
        {
            if (marks.Count == 0) return;
            bool reversed = end < start;
            var original = reversed ? marks.ToArray() : null;
            int previous = 0;
            int one = BitConverter.SingleToInt32Bits(1f);
            for (int i = 0; i < marks.Count; i++)
            {
                var input = reversed ? original[marks.Count - 1 - i] : marks[i];
                float mapped = Mathf.Clamp01((float)(start + (end - start) * (source ? input.x : input.y)));
                int bits = Mathf.Clamp(BitConverter.SingleToInt32Bits(mapped), previous + 1, one - (marks.Count - i));
                previous = bits;
                var mark = marks[i];
                if (source) mark.x = BitConverter.Int32BitsToSingle(bits);
                else mark.y = BitConverter.Int32BitsToSingle(bits);
                marks[i] = mark;
            }
        }

        private void CommitPath()
        {
            var path = new MorphGuidePath { closed = closedDraft };
            foreach (var knot in firstKnots) path.Side(pendingPane == a).Add(CreateKnot(knot));
            foreach (var knot in matchedKnots) path.Side(pendingPane != a).Add(CreateKnot(knot));
            int index = transition?.guides.Count ?? 0;
            Edit("Add Morph Guide", () => morph.GetOrCreate(fromId, toId).guides.Add(path));
            selection = GuideSelection.Path;
            selected = index;
        }

        private void ContinueGuide(EndpointPane pane, int index, bool end)
        {
            if (!Writable || !transition.CanContinueGuide(index, end)) return;
            CancelGesture();
            continuingSegment = true;
            document.SetPlayback(false);
            pendingPane = pane;
            selected = -1;
            Vector4 pair = transition.Position(transition.segments[index], end);
            firstKnots.Add(new GuideKnot(MorphSegment.Side(pair, pane == a), default));
            matchedKnots.Add(new GuideKnot(MorphSegment.Side(pair, pane != a), default));
            RefreshControls();
        }

        private void PlaceKnot(EndpointPane pane, Vector2 uv, Vector2 tangent, bool close)
        {
            if (!Writable || pendingEdge >= 0) return;
            pendingPane ??= pane;
            if (pane == pendingPane)
            {
                if (matchingKnots || closedDraft) return;
                if (close && IsRaster && firstKnots.Count >= 3)
                    closedDraft = true;
                else
                {
                    if (!IsRaster) firstKnots.Clear();
                    if (firstKnots.Count > 0 && firstKnots[^1].uv == uv) return;
                    firstKnots.Add(new GuideKnot(uv, tangent));
                }
                selected = -1;
                selectedMark = -1;
            }
            else
            {
                if (extendingPath >= 0 || continuingSegment && matchedKnots.Count >= firstKnots.Count ||
                    matchedKnots.Count > 0 && matchedKnots[^1].uv == uv) return;
                if (closedDraft && matchedKnots.Count > 0 && (close || matchedKnots[0].uv == uv))
                {
                    FinishGuide();
                    return;
                }
                matchingKnots = true;
                matchedKnots.Add(new GuideKnot(uv, tangent));
                if ((continuingSegment || firstKnots.Count == 1) && matchedKnots.Count == firstKnots.Count && !CommitSegmentOrPoint())
                {
                    matchedKnots.RemoveAt(matchedKnots.Count - 1);
                    RefreshControls();
                    hint.text = "This guide exceeds the document limit or has overlapping endpoints. Esc cancels.";
                    return;
                }
            }
            RefreshControls();
        }

        private bool CommitSegmentOrPoint()
        {
            var source = pendingPane == a ? firstKnots : matchedKnots;
            var target = pendingPane == b ? firstKnots : matchedKnots;
            if (source.Count == 1)
            {
                int index = transition?.anchors.Count ?? 0;
                var point = new MorphAnchor { from = source[0].uv, to = target[0].uv };
                Edit("Add Morph Anchor Pair", () => morph.GetOrCreate(fromId, toId).anchors.Add(point));
                selection = GuideSelection.Point;
                selected = index;
                return true;
            }

            int count = source.Count - (closedDraft ? 0 : 1);
            var path = new List<MorphSegment>(count);
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % source.Count;
                var edge = new MorphSegment
                {
                    fromStart = source[i].uv, fromEnd = source[j].uv, toStart = target[i].uv, toEnd = target[j].uv,
                    fromOut = source[i].tangent, fromIn = -source[j].tangent,
                    toOut = target[i].tangent, toIn = -target[j].tangent
                };
                edge.bezier = edge.fromOut != Vector2.zero || edge.fromIn != Vector2.zero ||
                    edge.toOut != Vector2.zero || edge.toIn != Vector2.zero;
                path.Add(edge);
            }
            if (!(transition ?? new MorphTransition()).CanAppendGuidePath(path)) return false;
            Edit("Add Morph Guide", () =>
            {
                var pair = morph.GetOrCreate(fromId, toId);
                pair.segments.AddRange(path);
                pair.BindNodes();
                for (int j = closedDraft ? 0 : 1; j < count; j++)
                {
                    if (source[j].tangent == Vector2.zero && target[j].tangent == Vector2.zero) continue;
                    var edge = path[j];
                    if (pair.OtherCubic(edge, false, out _) != null) pair.nodes[edge.startNode - 1].smooth = true;
                }
            });
            selection = GuideSelection.Segment;
            lastEnd = true;
            selected = transition.segments.Count - 1;
            return true;
        }

        private void PlaceMark(EndpointPane pane, int index, float t, bool path = false)
        {
            if (!Writable || !IsRaster || t <= 0 || t >= 1) return;
            document.SetPlayback(false);
            if (pendingPane == null || pendingPane == pane)
            {
                pendingPane = pane;
                selection = path ? GuideSelection.Path : GuideSelection.Segment;
                pendingEdge = selected = index;
                selectedMark = -1;
                pendingParameter = t;
            }
            else if (pendingEdge == index && path == PathSelected)
            {
                var mark = pane == a ? new Vector2(t, pendingParameter) : new Vector2(pendingParameter, t);
                var marks = SelectedMarks ?? new List<Vector2>();
                int at = 0;
                while (at < marks.Count && marks[at].x < mark.x) at++;
                Vector2 lo = at > 0 ? marks[at - 1] : Vector2.zero;
                Vector2 hi = at < marks.Count ? marks[at] : Vector2.one;
                if (mark.x <= lo.x || mark.y <= lo.y || mark.x >= hi.x || mark.y >= hi.y)
                {
                    hint.text = "Marks must follow the same order on both sides. Choose another position or press Esc.";
                    return;
                }
                Edit("Add Morph Correspondence Mark", () =>
                {
                    if (!path) SelectedEdge.marks = marks;
                    marks.Insert(at, mark);
                });
                selected = index;
                selectedMark = at;
            }
            RefreshControls();
        }

        private void RemoveSelected()
        {
            if (!Writable || selected < 0 || pendingPane != null) return;
            int index = selected;
            if (EditingMark)
            {
                int mark = selectedMark;
                var marks = SelectedMarks;
                if (mark >= 0 && mark < (marks?.Count ?? 0))
                    Edit("Remove Morph Correspondence Mark", () => marks.RemoveAt(mark));
                return;
            }
            if (SelectedPath != null)
            {
                Edit("Remove Morph Guide", () => transition.guides.RemoveAt(index));
                return;
            }
            if (EdgeSelected)
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
            if (to != null) EditGroup(undoName, change);
        }

        private void EditGroup(string undoName, Action change)
        {
            if (!GroupWritable) return;
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
            firstKnots.Clear();
            matchedKnots.Clear();
            matchingKnots = closedDraft = continuingSegment = false;
            extendingPath = -1;
            pendingEdge = selectedMark = -1;
            a?.CancelDrag();
            b?.CancelDrag();
            result?.CancelDrag();
        }

        private void OnKey(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) &&
                CanFinishGuide && (evt.target == a.Surface || evt.target == b.Surface))
                FinishGuide();
            else if (evt.keyCode == KeyCode.Escape)
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
