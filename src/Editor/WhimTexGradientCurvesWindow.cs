using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientCurvesWindow : EditorWindow
    {
        [SerializeField] private WhimTexGradient gradient;
        [SerializeField] private Object undoOwner;
        [SerializeField] private int activeId, channel, visibleChannels = 15;
        [SerializeField] private CurveView view = new CurveView(0, 0, 1, 1);
        private Func<WhimTexGradient> read;
        private Action<WhimTexGradient> commit;
        private WhimTexGradient observed;
        private uint observedRevision;
        private WhimTexGradientCurveKey[] keys = Array.Empty<WhimTexGradientCurveKey>();
        private readonly HashSet<int> selected = new HashSet<int>();
        private VisualElement plot, preview;
        private Image previewImage;
        private Texture2D previewTexture;
        private WhimTexGradient previewSnapshot;
        private readonly Color[] previewRamp = new Color[512];
        private readonly Color32[] previewPixels = new Color32[512 * 2];
        private FloatField timeField, valueField, incomingField, outgoingField;
        private EnumField tangentField, convertMode;
        private Label status, keyLabel;
        private readonly Button[] channels = new Button[4];
        private readonly List<Label> xLabels = new List<Label>(), yLabels = new List<Label>();
        private VisualElement axes;
        private const int MaxAxisTicks = 32;
        private const double MaximumGridStep = .5;
        private const int GridFadeDepth = 4;
        private double gridReferenceStep = MaximumGridStep;
        private readonly AxisTick[] xTicks = new AxisTick[MaxAxisTicks], yTicks = new AxisTick[MaxAxisTicks];
        private int xTickCount, yTickCount;
        private struct AxisTick { internal double value; internal float position; }
        private bool updating, showOutput = true, pointerDown, panning, boxSelecting;
        private int pointerId, tangentSide, undoGroup, mutationUndoGroup;
        private Vector2 downPoint, boxPoint;
        private CurveView downView;
        private WhimTexGradient dragStart;
        private int[] dragSelection;
        private static readonly Color[] ChannelColors = { new Color(1,.4f,.38f), new Color(.4f,.85f,.48f), new Color(.4f,.65f,1), new Color(.9f,.9f,.9f) };

        public static WhimTexGradientCurvesWindow ShowFor(Func<WhimTexGradient> read,
            Action<WhimTexGradient> commit, Object undoOwner, int selectedKeyId = 0)
        {
            var window = GetWindow<WhimTexGradientCurvesWindow>(false, "Gradient Curves", true);
            window.read = read ?? throw new ArgumentNullException(nameof(read));
            window.commit = commit ?? throw new ArgumentNullException(nameof(commit));
            window.undoOwner = undoOwner;
            window.observed = null; window.Pull();
            window.selected.Clear();
            if (selectedKeyId != 0) window.Select(selectedKeyId, false);
            else if (window.keys.Length > 0) window.Select(window.keys[0].id, false);
            window.Fit(); window.Refresh(); window.Show(); window.Focus();
            return window;
        }
        private void OnEnable()
        {
            if (!(view.width > 0) || !(view.height > 0)) view = new CurveView(0, 0, 1, 1);
            minSize = new Vector2(650, 430);
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += Tick;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.update -= Tick;
            if (pointerDown) EndGesture(false);
            horizontalScroll?.FinishDrag(); verticalScroll?.FinishDrag();
            previewUpdate?.Pause(); previewQueued = false;
            if (previewTexture != null) DestroyImmediate(previewTexture);
            previewTexture = null; previewSnapshot = null;
            if (previewImage != null) previewImage.image = null;
        }
        private void OnUndo() { observed = null; Pull(); Refresh(); }
        private void Tick()
        {
            if (pointerDown) return;
            Pull();
        }
        private void Pull()
        {
            if (read == null) { if (status != null) status.text = "Reopen Edit Curves to reconnect after script reload."; return; }
            WhimTexGradient value;
            try { value = read(); } catch (MissingReferenceException) { value = null; }
            if (value == null || value.Interpolation != WhimTexGradientInterpolation.Custom)
            {
                rootVisualElement.SetEnabled(false);
                if (status != null) status.text = "Select Custom interpolation in the gradient editor.";
                return;
            }
            rootVisualElement.SetEnabled(true);
            if (ReferenceEquals(observed, value) && observedRevision == value.Revision) return;
            observed = value; observedRevision = value.Revision; gradient = value.Clone();
            keys = gradient.CustomKeys; selected.RemoveWhere(id => !gradient.TryGetCustomKey(id, out _));
            Refresh();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.RemoveFromClassList("whimtex-gradient-curves-panning");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/Editor/WhimTexGradientWindow.uss");
            if (sheet != null && !rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            var root = rootVisualElement; root.style.paddingLeft = root.style.paddingRight = 6;
            root.style.paddingTop = root.style.paddingBottom = 6;
            var graph = new VisualElement { name = "gradientCurvesGraph" };
            graph.AddToClassList("whimtex-gradient-curves-graph");
            axes = graph; xLabels.Clear(); yLabels.Clear(); xTickCount = yTickCount = 0;
            plot = new VisualElement { name = "gradientCurvesPlot", focusable = true };
            plot.AddToClassList("whimtex-gradient-curves-editor-plot");
            plot.style.overflow = Overflow.Hidden; plot.style.backgroundColor = new Color(.12f,.12f,.12f);
            graph.Add(plot);
            plot.generateVisualContent += DrawGraph;
            plot.RegisterCallback<GeometryChangedEvent>(_ => { UpdateAxes(); plot.MarkDirtyRepaint(); });
            plot.RegisterCallback<PointerDownEvent>(Down);
            plot.RegisterCallback<PointerMoveEvent>(Move);
            plot.RegisterCallback<PointerUpEvent>(Up);
            plot.RegisterCallback<PointerCaptureOutEvent>(_ => { if (pointerDown) EndGesture(false); });
            plot.RegisterCallback<PointerCancelEvent>(_ => { if (pointerDown) EndGesture(true); });
            plot.RegisterCallback<WheelEvent>(Wheel);
            plot.RegisterCallback<KeyDownEvent>(KeyDown);
            plot.AddManipulator(new ContextualMenuManipulator(Menu));
            BuildNavigation(graph);
            root.Add(graph);
            BuildPreviewStrip(root);

            var footer = new VisualElement { name = "gradientCurvesFooter" };
            footer.AddToClassList("whimtex-gradient-curves-footer");
            var output = new Toggle("Output") { name = "gradientCurvesOutput", value = showOutput };
            output.RegisterValueChangedCallback(e => { showOutput = e.newValue; plot.MarkDirtyRepaint(); });
            footer.Add(output);
            var footerSpacer = new VisualElement(); footerSpacer.style.flexGrow = 1; footer.Add(footerSpacer);
            var channelGroup = new VisualElement { name = "gradientCurvesChannels" };
            channelGroup.AddToClassList("whimtex-gradient-curves-channels");
            var channelSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/WhimTexSplitView.uss");
            if (channelSheet != null) channelGroup.styleSheets.Add(channelSheet);
            for (int i = 0; i < channels.Length; i++)
            {
                int bit = 1 << i;
                var button = new Button(() => ToggleCurveChannel(bit))
                    { name = "gradientCurveChannel" + i, text = "RGBA"[i].ToString() };
                button.AddToClassList("whimtex-channel-button");
                if (i < 3) button.AddToClassList("whimtex-channel-button--" + "rgb"[i]);
                button.EnableInClassList("whimtex-channel-button--enabled", (visibleChannels & bit) != 0);
                channels[i] = button; channelGroup.Add(button);
            }
            channelGroup.AddManipulator(new ChannelDragManipulator(channels, () => visibleChannels, ToggleCurveChannel));
            footer.Add(channelGroup); root.Add(footer);

            var fields = new VisualElement(); fields.style.flexDirection = FlexDirection.Row; fields.style.flexWrap = Wrap.Wrap;
            keyLabel = new Label(); keyLabel.style.minWidth = 90; keyLabel.style.unityTextAlign = TextAnchor.MiddleLeft; fields.Add(keyLabel);
            timeField = Field("t", fields, 115); valueField = Field("Value", fields, 150);
            tangentField = new EnumField("Tangents", WhimTexGradientTangentMode.Auto); tangentField.style.width = 220;
            tangentField.labelElement.style.minWidth = 65; tangentField.labelElement.style.width = 65; fields.Add(tangentField);
            incomingField = Field("In", fields, 135); outgoingField = Field("Out", fields, 135);
            timeField.RegisterValueChangedCallback(e =>
            {
                if (stripSelection && !updating && gradient != null && gradient.TryGetCustomKey(activeId, out var key))
                    Mutate("Move Gradient Keys", g => g.MoveCustomKeys(new List<int>(selected).ToArray(), e.newValue - key.time));
                else EditKey(k => { k.time = e.newValue; return k; });
            });
            valueField.RegisterValueChangedCallback(e => EditKey(k => { k.value = k.channel == 3 ? Mathf.Clamp01(e.newValue) : e.newValue; return k; }));
            tangentField.RegisterValueChangedCallback(e => EditKey(k =>
            {
                k.inTangent = gradient.CustomKeyTangent(k.id, true); k.outTangent = gradient.CustomKeyTangent(k.id, false);
                k.tangentMode = (WhimTexGradientTangentMode)e.newValue;
                if (k.tangentMode == WhimTexGradientTangentMode.Smooth) k.inTangent = k.outTangent;
                return k;
            }));
            incomingField.RegisterValueChangedCallback(e => EditKey(k => { k.inTangent = e.newValue; if (k.tangentMode == WhimTexGradientTangentMode.Smooth) k.outTangent = e.newValue; return k; }));
            outgoingField.RegisterValueChangedCallback(e => EditKey(k => { k.outTangent = e.newValue; if (k.tangentMode == WhimTexGradientTangentMode.Smooth) k.inTangent = e.newValue; return k; }));
            root.Add(fields);
            var conversion = new VisualElement(); conversion.style.flexDirection = FlexDirection.Row; conversion.style.marginTop = 4;
            convertMode = new EnumField("Color Mode", WhimTexGradientColorMode.Perceptual); convertMode.style.width = 250;
            conversion.Add(convertMode);
            conversion.Add(new Button(() => Mutate("Convert Gradient Curves", g => g.ConvertCustomColorMode((WhimTexGradientColorMode)convertMode.value))) { text = "Convert" });
            var help = new Label("Wheel: zoom · Ctrl+wheel: vertical zoom · Middle drag: pan · Ctrl: multi-select · Double-click a curve: add key");
            help.style.flexGrow = 1; help.style.fontSize = 10; help.style.unityTextAlign = TextAnchor.MiddleRight; conversion.Add(help); root.Add(conversion);
            status = new Label(); status.style.fontSize = 10; status.style.whiteSpace = WhiteSpace.Normal; root.Add(status);
            Pull(); Refresh();
        }
        private static FloatField Field(string label, VisualElement parent, float width)
        {
            var field = new FloatField(label) { isDelayed = true }; field.style.width = width;
            field.labelElement.style.minWidth = 38; field.labelElement.style.width = 38; parent.Add(field); return field;
        }
        private void ToggleCurveChannel(int bit)
        {
            visibleChannels = (visibleChannels ^ bit) & 15;
            if ((visibleChannels & bit) != 0)
                for (int i = 0; i < channels.Length; i++) if (bit == (1 << i)) { channel = i; break; }
            Refresh();
        }
        private void Refresh()
        {
            if (plot == null || gradient == null) return;
            updating = true;
            keys = gradient.CustomKeys;
            selected.RemoveWhere(id => !gradient.TryGetCustomKey(id, out _));
            for (int i = 0; i < 4; i++)
            {
                channels[i].text = gradient.CustomChannelName(i);
                channels[i].EnableInClassList("whimtex-channel-button--enabled", (visibleChannels & (1 << i)) != 0);
            }
            bool valid = gradient.TryGetCustomKey(activeId, out var key);
            timeField.SetEnabled(valid); valueField.SetEnabled(valid); tangentField.SetEnabled(valid);
            incomingField.SetEnabled(valid && (key.tangentMode == WhimTexGradientTangentMode.Smooth || key.tangentMode == WhimTexGradientTangentMode.Broken));
            outgoingField.SetEnabled(incomingField.enabledSelf);
            if (valid)
            {
                keyLabel.text = gradient.CustomChannelName(key.channel);
                timeField.SetValueWithoutNotify(key.time); valueField.SetValueWithoutNotify(key.value);
                tangentField.SetValueWithoutNotify(key.tangentMode);
                incomingField.SetValueWithoutNotify(gradient.CustomKeyTangent(key.id, true)); outgoingField.SetValueWithoutNotify(gradient.CustomKeyTangent(key.id, false));
            }
            else keyLabel.text = "No key";
            convertMode.SetValueWithoutNotify(gradient.CustomColorMode);
            status.text = gradient.CustomApproximationError > 0 ?
                "Conversion: observed error " + gradient.CustomApproximationError.ToString("G3", CultureInfo.InvariantCulture) +
                ". Additional knots may be used; original automatic settings are retained." :
                "Strip markers move coincident keys together. Output shows the bounded result; dim curves show the authored values.";
            updating = false; RefreshStripMarkers(); UpdateAxes(); plot.MarkDirtyRepaint();
        }
        private void Select(int id, bool additive)
        {
            if (!gradient.TryGetCustomKey(id, out var key)) return;
            if (!additive) selected.Clear();
            if (additive && selected.Contains(id)) selected.Remove(id); else selected.Add(id);
            activeId = id; channel = key.channel; visibleChannels |= 1 << channel; Refresh();
        }
        private void EditKey(Func<WhimTexGradientCurveKey, WhimTexGradientCurveKey> update)
        {
            if (updating || gradient == null || !gradient.TryGetCustomKey(activeId, out var key)) return;
            Mutate("Edit Gradient Curve Key", g => g.UpdateCustomKey(update(key)));
        }
        private bool Mutate(string name, Action<WhimTexGradient> change)
        {
            if (updating || gradient == null || read == null) return false;
            var current = read();
            if (current == null || current.Interpolation != WhimTexGradientInterpolation.Custom) { Pull(); return false; }
            var next = gradient.Clone();
            try { change(next); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is OverflowException)
            { status.text = error.Message; RefreshFieldsAfterError(); return false; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(undoOwner != null ? undoOwner : this, name);
            Apply(next); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); mutationUndoGroup = group; return true;
        }
        private void RefreshFieldsAfterError()
        {
            string message = status.text; Refresh(); status.text = message;
        }
        private void Apply(WhimTexGradient next)
        {
            gradient = next;
            commit?.Invoke(next.Clone());
            observed = null; keys = gradient.CustomKeys; Refresh();
        }
        private void Delete() => Mutate("Delete Gradient Curve Keys", g => g.RemoveCustomKeys(new List<int>(selected).ToArray()));
        private bool Add(Vector2 point)
        {
            int curveChannel = HitCurve(point, out float time);
            if (curveChannel < 0) return false;
            int added = 0;
            if (!Mutate("Add Gradient Curve Key", g => added = g.AddCustomKey(curveChannel, time))) return false;
            Select(added, false); return true;
        }
        private int HitCurve(Vector2 point, out float time)
        {
            time = ToValue(point).x;
            if (!plot.contentRect.Contains(point) || time < 0 || time > 1) return -1;
            int found = -1;
            float nearest = 10;
            for (int c = 0; c < channels.Length; c++)
            {
                if ((visibleChannels & (1 << c)) == 0) continue;
                float value = gradient.EvaluateCustomChannel(c, time);
                if (!float.IsFinite(value)) continue;
                float distance = Mathf.Abs(ToPoint(time, value).y - point.y);
                if (distance > nearest || (distance == nearest && found >= 0 && c != channel)) continue;
                nearest = distance; found = c;
            }
            return found;
        }
        private Vector2 ToPoint(double t, double value) => new Vector2((float)((t - view.xMin) / view.width * plot.contentRect.width),
            (float)((view.yMax - value) / view.height * plot.contentRect.height));
        private Vector2 ToValue(Vector2 point) => new Vector2((float)(view.xMin + point.x / Mathf.Max(1, plot.contentRect.width) * view.width),
            (float)(view.yMax - point.y / Mathf.Max(1, plot.contentRect.height) * view.height));
        private void Fit()
        {
            if (gradient == null) return;
            horizontalScroll?.FinishDrag(); verticalScroll?.FinishDrag();
            UpdateCurveRange();
            view = new CurveView(0, curveMinimum, 1, curveMaximum - curveMinimum);
            UpdateAxes(); plot?.MarkDirtyRepaint();
        }
        private void UpdateAxes()
        {
            if (plot == null || axes == null) return;
            double width = plot.contentRect.width, height = plot.contentRect.height;
            if (!(width > 0) || !(height > 0) || !(view.width > 0) || !(view.height > 0)) return;
            gridReferenceStep = MaximumGridStep * Math.Min(1, Math.Max(view.width, view.height));
            xTickCount = BuildAxisTicks(xTicks, view.xMin, view.width, width, false, out double xLabelStep);
            yTickCount = BuildAxisTicks(yTicks, view.yMin, view.height, height, true, out double yLabelStep);
            float yLabelWidth = UpdateAxisLabels(yLabels, yTicks, yTickCount, false, yLabelStep);
            UpdateAxisLabels(xLabels, xTicks, xTickCount, true, xLabelStep, yLabelWidth + 8);
            UpdateNavigation();
            RequestPreview(); previewStrip?.MarkDirtyRepaint();
        }
        private static double GridFade(double value, double minimum, double maximum)
        {
            double t = Math.Max(0, Math.Min(1, (value - minimum) / (maximum - minimum)));
            return t * t * (3 - 2 * t);
        }
        private static double DecimalStep(int index)
        {
            int exponent = (int)Math.Floor(index / 3d);
            int multiple = index - exponent * 3;
            return Math.Pow(10, exponent) * (multiple == 0 ? 1 : multiple == 1 ? 2 : 5);
        }
        private static int CeilingDecimalStepIndex(double requested)
        {
            int exponent = (int)Math.Floor(Math.Log10(requested));
            double multiple = requested / Math.Pow(10, exponent);
            return exponent * 3 + (multiple <= 1 + 1e-12 ? 0 : multiple <= 2 + 1e-12 ? 1 : multiple <= 5 + 1e-12 ? 2 : 3);
        }
        private double GridStepOpacity(double step)
        {
            double depth = Math.Max(0, Math.Log(gridReferenceStep / step, 2));
            return Math.Pow(.5, depth) * (1 - GridFade(depth, GridFadeDepth - 1, GridFadeDepth));
        }
        private int BuildAxisTicks(AxisTick[] ticks, double start, double span, double pixels, bool reverse, out double labelStep)
        {
            double spacing = Math.Max(reverse ? 72 : 160, pixels / (MaxAxisTicks - 2));
            double requested = span / pixels * spacing;
            labelStep = DecimalStep(CeilingDecimalStepIndex(requested));
            if (!double.IsFinite(labelStep) || !(labelStep > 0)) return 0;
            double end = start + span;
            double first = Math.Ceiling(start / labelStep);
            int count = 0;
            for (int i = 0; i < ticks.Length; i++)
            {
                double value = (first + i) * labelStep;
                if (value > end) break;
                double fraction = (value - start) / span;
                ticks[count++] = new AxisTick { value = value == 0 ? 0 : value,
                    position = (float)((reverse ? 1 - fraction : fraction) * pixels) };
            }
            return count;
        }
        private float UpdateAxisLabels(List<Label> labels, AxisTick[] ticks, int count, bool horizontal, double labelStep, float leftInset = 4)
        {
            while (labels.Count < count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("whimtex-gradient-axis-label");
                label.AddToClassList(horizontal ? "whimtex-gradient-axis-label-x" : "whimtex-gradient-axis-label-y");
                axes.Insert(1, label); labels.Add(label);
            }
            int decimals = (int)Math.Max(0, Math.Min(15, Math.Ceiling(-Math.Log10(labelStep) - 1e-12)));
            string format = decimals == 0 ? "0" : "0." + new string('#', decimals);
            float widest = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                label.EnableInClassList("whimtex-gradient-hidden", i >= count);
                if (i >= count) continue;
                double value = ticks[i].value;
                double magnitude = Math.Abs(value);
                bool scientific = magnitude >= 1000000 || magnitude > 0 && magnitude < .000001;
                int significant = scientific ? (int)Math.Max(1, Math.Min(17,
                    Math.Floor(Math.Log10(magnitude)) - Math.Floor(Math.Log10(labelStep)) + 1)) : 0;
                label.text = value.ToString(scientific ? "G" + significant : format, CultureInfo.InvariantCulture);
                float measured = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined).x;
                float labelWidth = Mathf.Ceil(float.IsFinite(measured) ? measured : 0) + 2;
                widest = Mathf.Max(widest, labelWidth);
                label.style.width = labelWidth;
                label.EnableInClassList("whimtex-gradient-axis-label-zero", value == 0);
                if (horizontal)
                    label.style.left = Mathf.Clamp(plot.layout.x + ticks[i].position - labelWidth * .5f,
                        leftInset, Mathf.Max(leftInset, axes.contentRect.width - labelWidth - 18));
                else
                {
                    label.style.left = 4;
                    label.style.top = Mathf.Clamp(plot.layout.y + ticks[i].position - 8, 2, Mathf.Max(2, axes.contentRect.height - 32));
                }
            }
            return widest;
        }
        private int Hit(Vector2 point)
        {
            int found = 0; float best = 9 * 9;
            foreach (var key in keys)
            {
                if ((visibleChannels & (1 << key.channel)) == 0) continue;
                float distance = (ToPoint(key.time, key.value) - point).sqrMagnitude;
                if (distance < best || (distance == best && key.channel == channel)) { best = distance; found = key.id; }
            }
            return found;
        }
        private Vector2 TangentPoint(WhimTexGradientCurveKey key, bool incoming)
        {
            float span = (float)(.15 * view.width);
            foreach (var other in keys) if (other.channel == key.channel &&
                (incoming ? other.time < key.time : other.time > key.time)) span = Mathf.Min(span, Mathf.Abs(other.time - key.time) / 3);
            span = Mathf.Max(.0000001f, span) * (incoming ? -1 : 1);
            return ToPoint(key.time + span, key.value + span * gradient.CustomKeyTangent(key.id, incoming));
        }
        private void Down(PointerDownEvent e)
        {
            if (pointerDown || gradient == null || read == null || (e.button != 0 && e.button != 2)) return;
            plot.Focus(); var point = e.localPosition;
            if (e.button == 0 && e.clickCount == 2 && Hit(point) == 0)
            {
                if (Add(point)) BeginKeyDrag(e, plot, false, mutationUndoGroup);
                e.StopPropagation(); return;
            }
            tangentSide = 0;
            if (e.button == 0 && gradient.TryGetCustomKey(activeId, out var active) && active.tangentMode != WhimTexGradientTangentMode.Constant)
            {
                if ((TangentPoint(active, true) - (Vector2)point).sqrMagnitude < 49) tangentSide = -1;
                else if ((TangentPoint(active, false) - (Vector2)point).sqrMagnitude < 49) tangentSide = 1;
            }
            int hit = tangentSide == 0 ? Hit(point) : activeId;
            if (e.button == 0 && hit == 0)
            {
                int curveChannel = HitCurve(point, out _);
                if (curveChannel >= 0)
                {
                    channel = curveChannel;
                    if (!e.ctrlKey && !e.commandKey) { selected.Clear(); activeId = 0; }
                    Refresh(); e.StopPropagation(); return;
                }
            }
            if (hit != 0 && tangentSide == 0)
            {
                if (stripSelection || e.ctrlKey || e.commandKey || !selected.Contains(hit)) Select(hit, e.ctrlKey || e.commandKey);
                else { activeId = hit; if (gradient.TryGetCustomKey(hit, out var key)) channel = key.channel; }
            }
            stripSelection = false;
            panning = e.button == 2; boxSelecting = !panning && hit == 0;
            rootVisualElement.EnableInClassList("whimtex-gradient-curves-panning", panning);
            if (boxSelecting && !e.ctrlKey && !e.commandKey) selected.Clear();
            pointerDown = true; pointerId = e.pointerId; downPoint = boxPoint = point; downView = view;
            dragStart = gradient.Clone(); dragSelection = new List<int>(selected).ToArray();
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            if (!panning && !boxSelecting) Undo.RegisterCompleteObjectUndo(undoOwner != null ? undoOwner : this, "Move Gradient Curve Key");
            gestureTarget = plot; plot.CapturePointer(pointerId); e.StopPropagation(); Refresh();
        }
        private void BeginKeyDrag(PointerDownEvent e, VisualElement target, bool strip, int creationGroup = -1)
        {
            pointerDown = true; draggingStrip = strip; stripSelection = strip;
            panning = boxSelecting = false; tangentSide = 0;
            pointerId = e.pointerId; downPoint = boxPoint = e.localPosition; downView = view;
            dragStart = gradient.Clone(); dragSelection = new List<int>(selected).ToArray();
            Undo.IncrementCurrentGroup();
            stripRemovalPending = false;
            stripCanRemove = strip && GradientCustomMarkers.CanRemove(dragStart, dragSelection);
            undoGroup = creationGroup >= 0 ? creationGroup : Undo.GetCurrentGroup();
            if (creationGroup < 0)
                Undo.RegisterCompleteObjectUndo(undoOwner != null ? undoOwner : this, "Move Gradient Keys");
            gestureTarget = target; target.CapturePointer(pointerId); e.StopPropagation();
        }
        private void Move(PointerMoveEvent e)
        {
            if (!pointerDown || e.pointerId != pointerId) return;
            var point = (Vector2)e.localPosition;
            if (draggingStrip) { MoveStrip(point); e.StopPropagation(); return; }
            if (panning)
            {
                Vector2 delta = point - downPoint;
                double x = downView.x - delta.x / Mathf.Max(1, plot.contentRect.width) * downView.width;
                double y = downView.y + delta.y / Mathf.Max(1, plot.contentRect.height) * downView.height;
                if (IsValidViewAxis(x, view.width, plot.contentRect.width)) view.x = x;
                if (IsValidViewAxis(y, view.height, plot.contentRect.height)) view.y = y;
                UpdateAxes(); plot.MarkDirtyRepaint();
            }
            else if (boxSelecting) { boxPoint = point; plot.MarkDirtyRepaint(); }
            else
            {
                Vector2 target = ToValue(point), origin = ToValue(downPoint); var next = dragStart.Clone();
                try
                {
                    if (tangentSide != 0 && next.TryGetCustomKey(activeId, out var key))
                    {
                        float dt = target.x - key.time;
                        dt = tangentSide < 0 ? Mathf.Min(-.000001f, dt) : Mathf.Max(.000001f, dt);
                        float slope = (target.y - key.value) / dt;
                        key.tangentMode = e.altKey || key.tangentMode == WhimTexGradientTangentMode.Broken ?
                            WhimTexGradientTangentMode.Broken : WhimTexGradientTangentMode.Smooth;
                        if (tangentSide < 0) key.inTangent = slope; else key.outTangent = slope;
                        if (key.tangentMode == WhimTexGradientTangentMode.Smooth) key.inTangent = key.outTangent = slope;
                        next.UpdateCustomKey(key);
                    }
                    else
                    {
                        Vector2 delta = target - origin;
                        if (dragStart.TryGetCustomKey(activeId, out var anchor))
                            delta.x = SnapTime(anchor.time + delta.x, dragSelection) - anchor.time;
                        next.MoveCustomKeys(dragSelection, delta.x);
                        if (next.TryGetCustomKey(activeId, out var moved))
                            delta.y = SnapValue(anchor, moved.time, anchor.value + delta.y) - anchor.value;
                        var movedKeys = next.CustomKeys;
                        for (int i = 0; i < movedKeys.Length; i++)
                        {
                            if (Array.IndexOf(dragSelection, movedKeys[i].id) < 0 ||
                                !dragStart.TryGetCustomKey(movedKeys[i].id, out var original)) continue;
                            movedKeys[i].value = original.value + delta.y;
                            if (movedKeys[i].channel == 3) movedKeys[i].value = Mathf.Clamp01(movedKeys[i].value);
                        }
                        next.SetCustomData(movedKeys, next.CustomColorMode);
                    }
                    Apply(next);
                }
                catch (Exception error) when (error is ArgumentException || error is InvalidOperationException) { status.text = error.Message; }
            }
            e.StopPropagation();
        }
        private void Up(PointerUpEvent e)
        {
            if (!pointerDown || e.pointerId != pointerId || e.button != (panning ? 2 : 0)) return;
            if (draggingStrip) { FinishStripDrag(e.localPosition); e.StopPropagation(); return; }
            if (boxSelecting)
            {
                Rect box = Rect.MinMaxRect(Mathf.Min(downPoint.x, boxPoint.x), Mathf.Min(downPoint.y, boxPoint.y),
                    Mathf.Max(downPoint.x, boxPoint.x), Mathf.Max(downPoint.y, boxPoint.y));
                foreach (var key in keys) if ((visibleChannels & (1 << key.channel)) != 0 && box.Contains(ToPoint(key.time, key.value)))
                { selected.Add(key.id); activeId = key.id; channel = key.channel; }
            }
            EndGesture(false); e.StopPropagation();
        }
        private void EndGesture(bool cancel)
        {
            if (!pointerDown) return;
            pointerDown = false;
            rootVisualElement.RemoveFromClassList("whimtex-gradient-curves-panning");
            if (cancel && !panning && !boxSelecting && dragStart != null) Apply(dragStart);
            if (gestureTarget != null && gestureTarget.HasPointerCapture(pointerId)) gestureTarget.ReleasePointer(pointerId);
            Undo.CollapseUndoOperations(undoGroup); Undo.IncrementCurrentGroup();
            dragStart = null; gestureTarget = null;
            panning = boxSelecting = draggingStrip = stripRemovalPending = stripCanRemove = false; Refresh();
        }
        private void Wheel(WheelEvent e)
        {
            if (pointerDown) return;
            UpdateCurveRange();
            double x = e.localMousePosition.x / Mathf.Max(1, plot.contentRect.width);
            double y = 1 - e.localMousePosition.y / Mathf.Max(1, plot.contentRect.height);
            double scale = Math.Pow(1.1, Mathf.Clamp(e.delta.y, -10, 10));
            double width = view.width * scale, height = view.height * scale;
            double startX = view.x + x * (view.width - width), startY = view.y + y * (view.height - height);
            if (!e.ctrlKey && IsValidViewAxis(startX, width, plot.contentRect.width)) { view.x = startX; view.width = width; }
            if (IsValidViewAxis(startY, height, plot.contentRect.height)) { view.y = startY; view.height = height; }
            UpdateAxes(); plot.MarkDirtyRepaint(); e.StopPropagation();
        }
        private void KeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape && pointerDown) EndGesture(true);
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) Delete();
            else if (e.keyCode == KeyCode.F) Fit();
            else return;
            e.StopPropagation();
        }
        private void Menu(ContextualMenuPopulateEvent e)
        {
            int hit = Hit(e.localMousePosition);
            if (hit != 0 && !selected.Contains(hit)) Select(hit, false);
            else if (hit != 0 && gradient.TryGetCustomKey(hit, out var clicked)) { activeId = hit; channel = clicked.channel; Refresh(); }
            e.menu.AppendAction("Delete Selected Keys", _ => Delete(), selected.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            foreach (WhimTexGradientTangentMode mode in Enum.GetValues(typeof(WhimTexGradientTangentMode)))
            {
                var choice = mode;
                e.menu.AppendAction("Tangents/" + mode, _ => Mutate("Set Curve Tangents", g =>
                {
                    foreach (int id in selected) if (g.TryGetCustomKey(id, out var item))
                    {
                        item.inTangent = g.CustomKeyTangent(id, true); item.outTangent = g.CustomKeyTangent(id, false);
                        item.tangentMode = choice; g.UpdateCustomKey(item);
                    }
                }), selected.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
        private static void Line(Painter2D painter, Vector2 a, Vector2 b)
        { painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke(); }
        private static void Disc(Painter2D painter, Vector2 center, float radius)
        { painter.BeginPath(); painter.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360)); painter.ClosePath(); painter.Fill(); painter.Stroke(); }
        private void DrawGraph(MeshGenerationContext context)
        {
            if (gradient == null || !gradient.HasCustomCurves) return;
            var p = context.painter2D; var rect = plot.contentRect;
            DrawUnitRangeBackground(p, rect);
            p.lineWidth = 1;
            DrawGridAxis(p, rect, view.x, view.width, false);
            DrawGridAxis(p, rect, view.y, view.height, true);
            int samples = Mathf.Clamp(Mathf.CeilToInt(rect.width / 2), 64, 1024);
            for (int c = 0; c < 4; c++) if ((visibleChannels & (1 << c)) != 0)
            {
                for (int pass = 0; pass < (showOutput ? 2 : 1); pass++)
                {
                    Color color = ChannelColors[c]; if (showOutput && pass == 0) color.a = .3f;
                    p.lineWidth = c == channel ? 1.8f : 1.2f;
                    DrawWrappedCurve(p, c, showOutput && pass == 1, color, samples);
                }
            }
            if (gradient.TryGetCustomKey(activeId, out var active) && (visibleChannels & (1 << active.channel)) != 0 &&
                active.tangentMode != WhimTexGradientTangentMode.Constant)
            {
                p.strokeColor = new Color(.8f,.8f,.8f,.8f); p.fillColor = new Color(.2f,.2f,.2f); p.lineWidth = 1;
                Vector2 point = ToPoint(active.time,active.value);
                foreach (bool incoming in new[] { true, false })
                {
                    Vector2 handle = TangentPoint(active,incoming);
                    if (float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(handle.x) && float.IsFinite(handle.y))
                        Line(p, new Vector2(Mathf.Clamp(point.x, -100000, 100000), Mathf.Clamp(point.y, -100000, 100000)),
                            new Vector2(Mathf.Clamp(handle.x, -100000, 100000), Mathf.Clamp(handle.y, -100000, 100000)));
                    if (rect.Contains(handle)) Disc(p,handle,3);
                }
            }
            foreach (var key in keys) if ((visibleChannels & (1 << key.channel)) != 0)
            {
                Vector2 point = ToPoint(key.time,key.value);
                if (point.x < -6 || point.x > rect.width + 6 || point.y < -6 || point.y > rect.height + 6 ||
                    !float.IsFinite(point.x) || !float.IsFinite(point.y)) continue;
                p.fillColor = ChannelColors[key.channel]; p.strokeColor = selected.Contains(key.id) ? Color.white : Color.black;
                p.lineWidth = selected.Contains(key.id) ? 2 : 1; Disc(p,point,activeId == key.id ? 4.5f : 3.5f);
            }
            if (boxSelecting)
            {
                p.strokeColor = new Color(.4f,.7f,1); p.lineWidth = 1;
                p.BeginPath(); p.MoveTo(downPoint); p.LineTo(new Vector2(boxPoint.x,downPoint.y)); p.LineTo(boxPoint); p.LineTo(new Vector2(downPoint.x,boxPoint.y)); p.ClosePath(); p.Stroke();
            }
        }

        private void DrawWrappedCurve(Painter2D painter, int curveChannel, bool final, Color color, int samples)
        {
            if (showOutput && !final)
            {
                DrawCurveSegment(painter, curveChannel, false, color, 0, 1, false, samples);
                return;
            }
            double minimum = view.x, maximum = view.x + view.width;
            Color continuation = color; continuation.a *= .4f;
            if (gradient.WrapMode == WhimTexGradientWrapMode.Clamp)
            {
                DrawCurveSegment(painter, curveChannel, final, color, 0, 1, false, samples);
                if (minimum < 0) DrawConstantCurve(painter, curveChannel, final, continuation, minimum, Math.Min(0, maximum), 0);
                if (maximum > 1) DrawConstantCurve(painter, curveChannel, final, continuation, Math.Max(1, minimum), maximum, 1);
                return;
            }

            double first = Math.Floor(minimum);
            if (view.width > Math.Max(1, plot.contentRect.width))
            {
                DrawDenseWrap(painter, curveChannel, final, continuation, samples);
                DrawCurveSegment(painter, curveChannel, final, color, 0, 1, false, samples);
                return;
            }
            int count = (int)Math.Ceiling(view.width) + 1;
            for (int i = 0; i < count; i++)
            {
                double start = first + i;
                bool reverse = gradient.WrapMode == WhimTexGradientWrapMode.Mirror && start - Math.Floor(start * .5) * 2 != 0;
                DrawCurveSegment(painter, curveChannel, final, start == 0 ? color : continuation, start, start + 1, reverse, samples);
            }
        }

        private void DrawDenseWrap(Painter2D painter, int curveChannel, bool final, Color color, int samples)
        {
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            for (int i = 0; i <= samples; i++) Include(i / (float)samples);
            foreach (var key in keys) if (key.channel == curveChannel) Include(key.time);
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum)) return;
            float top = ToPoint(view.x, maximum).y, bottom = ToPoint(view.x, minimum).y;
            Rect rect = plot.contentRect;
            if (top > rect.height || bottom < 0) return;
            top = Mathf.Clamp(top, 0, rect.height); bottom = Mathf.Clamp(bottom, 0, rect.height);
            if (bottom - top <= painter.lineWidth)
            {
                painter.strokeColor = color;
                Line(painter, new Vector2(0, (top + bottom) * .5f), new Vector2(rect.width, (top + bottom) * .5f));
                return;
            }
            color.a *= .35f; painter.fillColor = color;
            painter.BeginPath(); painter.MoveTo(new Vector2(0, top)); painter.LineTo(new Vector2(rect.width, top));
            painter.LineTo(new Vector2(rect.width, bottom)); painter.LineTo(new Vector2(0, bottom)); painter.ClosePath(); painter.Fill();

            void Include(float time)
            {
                float value = gradient.EvaluateCustomChannel(curveChannel, time, final);
                if (!float.IsFinite(value)) return;
                minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value);
            }
        }

        private void DrawConstantCurve(Painter2D painter, int curveChannel, bool final, Color color,
            double start, double end, float time)
        {
            if (!(end > start)) return;
            float value = gradient.EvaluateCustomChannel(curveChannel, time, final);
            double padding = (painter.lineWidth * .5 + 1) * view.height / Math.Max(1, plot.contentRect.height);
            if (!float.IsFinite(value) || value < view.y - padding || value > view.yMax + padding) return;
            painter.strokeColor = color;
            Line(painter, ToPoint(start, value), ToPoint(end, value));
        }

        private void DrawCurveSegment(Painter2D painter, int curveChannel, bool final, Color color,
            double segmentStart, double segmentEnd, bool reverse, int samples)
        {
            double start = Math.Max(view.x, segmentStart), end = Math.Min(view.x + view.width, segmentEnd);
            if (!(end > start)) return;
            int count = Math.Max(1, (int)Math.Ceiling((end - start) / view.width * samples));
            painter.strokeColor = color; painter.BeginPath(); bool started = false;
            for (int i = 0; i <= count; i++)
            {
                double t = start + (end - start) * i / count;
                float localTime = (float)(t - segmentStart);
                if (reverse) localTime = 1 - localTime;
                float value = gradient.EvaluateCustomChannel(curveChannel, localTime, final);
                if (!float.IsFinite(value)) continue;
                Vector2 point = ToPoint(t, value); point.y = Mathf.Clamp(point.y, -100000, 100000);
                if (!started) { painter.MoveTo(point); started = true; } else painter.LineTo(point);
            }
            if (started) painter.Stroke();
        }

        private void DrawGridAxis(Painter2D painter, Rect rect, double start, double span, bool vertical)
        {
            double pixels = vertical ? rect.height : rect.width;
            if (!(pixels > 0) || !(span > 0)) return;
            double end = start + span;
            int coarsestStep = CeilingDecimalStepIndex(MaximumGridStep);
            int finestStep = CeilingDecimalStepIndex(gridReferenceStep / Math.Pow(2, GridFadeDepth));
            for (int stepIndex = coarsestStep; stepIndex >= finestStep; stepIndex--)
            {
                double step = DecimalStep(stepIndex);
                double opacity = .09 * GridStepOpacity(step) * GridFade(step / span * pixels, 4, 12);
                if (!(opacity > 0)) continue;
                painter.strokeColor = new Color(1, 1, 1, (float)opacity);
                double first = Math.Ceiling(start / step);
                int count = (int)Math.Min(Math.Ceiling(pixels / 4) + 1, Math.Floor(end / step) - first + 1);
                int multiple = (stepIndex % 3 + 3) % 3;
                for (int i = 0; i < count; i++)
                {
                    double index = first + i;
                    if (index == 0) continue;
                    if (stepIndex != coarsestStep)
                    {
                        bool onCoarserGrid = multiple == 2 ? index % 2 == 0 : multiple == 1 ? index % 5 == 0 :
                            index % 2 == 0 || index % 5 == 0;
                        if (onCoarserGrid) continue;
                    }
                    DrawDivision(index * step);
                }
            }
            if (start <= 0 && end >= 0)
            {
                painter.strokeColor = new Color(1, 1, 1, .24f);
                DrawDivision(0);
            }

            void DrawDivision(double value)
            {
                float position = (float)((value - start) / span * pixels);
                if (vertical)
                    Line(painter, new Vector2(0, rect.height - position), new Vector2(rect.width, rect.height - position));
                else
                    Line(painter, new Vector2(position, 0), new Vector2(position, rect.height));
            }
        }

        private void DrawUnitRangeBackground(Painter2D painter, Rect rect)
        {
            float zero = (float)Math.Max(0, Math.Min(rect.width, -view.x / view.width * rect.width));
            float one = (float)Math.Max(0, Math.Min(rect.width, (1 - view.x) / view.width * rect.width));
            float top = (float)Math.Max(0, Math.Min(rect.height, (view.yMax - 1) / view.height * rect.height));
            float bottom = (float)Math.Max(0, Math.Min(rect.height, view.yMax / view.height * rect.height));
            painter.fillColor = new Color(0, 0, 0, .16f);
            FillRange(0, 0, zero, rect.height); FillRange(one, 0, rect.width, rect.height);
            FillRange(zero, 0, one, top); FillRange(zero, bottom, one, rect.height);

            void FillRange(float left, float upper, float right, float lower)
            {
                if (!(right > left) || !(lower > upper)) return;
                painter.BeginPath(); painter.MoveTo(new Vector2(left, upper)); painter.LineTo(new Vector2(right, upper));
                painter.LineTo(new Vector2(right, lower)); painter.LineTo(new Vector2(left, lower));
                painter.ClosePath(); painter.Fill();
            }
        }
    }
}
