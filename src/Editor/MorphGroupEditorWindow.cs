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
        private EnumField rasterMethod;
        private PreviewPane result;
        private readonly MorphRasterRenderer.FlowCache resultFlow = new();
        private Slider timeline;
        private Label clock, hint;
        private HelpBox fallback;
        private Button remove, midpoint;
        private EndpointPane a, b, pendingPane;
        private Vector2 pendingUv;
        private int selected = -1;
        private bool modelDirty, previewsDirty, animatedPreviewPending, publishingDrag, timelineVisible;
        private ulong timeRevision;
        private double nextAnimatedPreview;
        private bool Writable => !modelDirty && morph != null && to != null &&
            document != null && !WhimTexApi.IsLayerContentLocked(document, group);
        private double EndTime => starts.Count == 0 ? 0 : starts[starts.Count - 1];

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
            WhimTexDocument.Changed -= DocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= ResourcesChanged;
            WhimTexApi.LiveEditLocksChanged -= InvalidateModel;
            WhimTexUserSettings.Changed -= InvalidateModel;
            a?.Release();
            b?.Release();
            result?.Release();
            resultFlow.Clear();
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
            if (changed == document) previewsDirty = true;
        }

        private void InvalidateModel()
        {
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
            pairs = WhimTexUI.ConfigureField(new DropdownField("Transition") { name = "morphTransition" });
            pairs.RegisterValueChangedCallback(_ =>
            {
                int index = pairs.index;
                if (modelDirty || index < 0 || index + 1 >= keys.Count) return;
                SeekMidpoint(index);
            });
            root.Add(pairs);
            duration = WhimTexUI.ConfigureField(new FloatField("Duration (s)") { isDelayed = true, name = "morphDuration" });
            duration.RegisterValueChangedCallback(evt =>
            {
                float value = evt.newValue;
                float previous = transition?.duration ?? 1f;
                if (!Writable || !float.IsFinite(value) || value <= 0) duration.SetValueWithoutNotify(previous);
                else if (value != previous)
                    Edit("Change Morph Duration", () => morph.GetOrCreate(fromId, toId).duration = value);
            });
            root.Add(duration);
            rasterMethod = WhimTexUI.ConfigureField(new EnumField("Raster Method", MorphRasterMethod.LocalContrast) { name = "rasterMethod" });
            rasterMethod.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || morph.mode != MorphMode.Raster)
                {
                    rasterMethod.SetValueWithoutNotify(morph?.rasterMethod ?? MorphRasterMethod.LocalContrast);
                    return;
                }
                var method = (MorphRasterMethod)evt.newValue;
                if (method != morph.rasterMethod)
                    Edit("Change Raster Method", () => morph.rasterMethod = method);
            });
            root.Add(rasterMethod);
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
            result = new PreviewPane(this, "morphPreviewResult", "Result");
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
                result.ShowTexture(MorphRasterRenderer.Render(a.Texture, b.Texture, transition?.anchors, time, resultFlow,
                    (from, to, a.Revision, b.Revision, a.Texture.width, a.Texture.height, 1f), morph.rasterMethod));
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
                CancelGesture();
                selected = -1;
                a.Release();
                b.Release();
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
            SetVisible(rasterMethod, raster);
            rasterMethod.SetValueWithoutNotify(morph?.rasterMethod ?? MorphRasterMethod.LocalContrast);
            rasterMethod.SetEnabled(Writable);
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
            timeline.SetValueWithoutNotify(EndTime > 0 ? Mathf.Clamp01((float)(time / EndTime)) : 0);
            clock.text = time >= EndTime ? $"{time:0.###} s · Holding {keys[keys.Count - 1].layerName}" :
                $"{time:0.###} / {EndTime:0.###} s";
        }

        private void RefreshControls()
        {
            a.RefreshMarkers();
            b.RefreshMarkers();
            remove.SetEnabled(Writable && selected >= 0);
            hint.text = morph == null ? "This Morph Group is no longer in the document." :
                to == null ? "Add or enable two child layers to edit a transition." :
                !Writable ? "This Morph Group is locked for editing." :
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

        private void RemoveSelected()
        {
            if (!Writable || selected < 0 || selected >= (transition?.anchors.Count ?? 0)) return;
            int index = selected;
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

        private void CancelGesture()
        {
            pendingPane = null;
            a?.CancelDrag();
            b?.CancelDrag();
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
