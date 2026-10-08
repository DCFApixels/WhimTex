using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexColorPicker : EditorWindow
    {
        private const string ColorModePreference = "WhimTex.ColorPicker.ColorMode";
        private sealed class DocumentContext { internal Func<WhimTexDocument> read; }
        private static readonly ConditionalWeakTable<VisualElement, DocumentContext> Contexts = new ConditionalWeakTable<VisualElement, DocumentContext>();
        private static WhimTexColorPicker current;
        internal object SourceContext;
        private Color original, color;
        private bool hdr, initialHdr, alphaVisible, finished, edited, syncing;
        private WhimTexColorRange range;
        private WhimTexDocument document;
        private Action<Color> changed;
        private Action<bool> modeChanged;
        private Func<bool> valid;
        private float hue, saturation, brightness, exposure;
        [SerializeField] private float previewEV;
        private Toggle hdrControl;
        private PopupField<string> mode;
        private Slider[] channels;
        private Slider alpha, intensity;
        private TextField hex;
        private ColorSurface plane, before, after;
        private HueRing hueRing;
        private readonly List<VisualElement> ramps = new List<VisualElement>();
        private VisualElement history;
        private ScrollView historyScroll;
        private UnityEditor.UIElements.ColorField eyedropper;
        private Texture2D historyCursor;

        internal static void SetDocument(VisualElement element, Func<WhimTexDocument> read)
        { Contexts.Remove(element); Contexts.Add(element, new DocumentContext { read = read }); }
        internal static WhimTexDocument FindDocument(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (Contexts.TryGetValue(element, out var context)) return context.read();
            return null;
        }
        internal static WhimTexDocument DocumentFor(UnityEngine.Object owner) => owner is WhimTexDocument doc ? doc :
            owner is WhimTexGradientSession session ? session.document :
            owner is ShaderFX fx ? WhimTexWindow.FindFXTransformDocument(fx) : null;
        internal static bool IsOpen => current != null;
        internal static bool TryApplySample(object context, Color sample)
        {
            var picker = current;
            if (context == null || picker == null || !ReferenceEquals(picker.SourceContext, context) ||
                picker.finished || picker.valid != null && !picker.valid() || !IsFinite(sample)) return false;
            picker.SetColor(sample);
            return true;
        }
        internal static bool IsFinite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        internal static bool IsHistoryColorVisible(Color value, bool hdr) => hdr ||
            value.r >= 0 && value.r <= 1 && value.g >= 0 && value.g <= 1 && value.b >= 0 && value.b <= 1;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static Color Limit(Color c, bool hdr, bool alpha, float previousAlpha)
        {
            float max = hdr ? 65504 : 1;
            return new Color(Mathf.Clamp(c.r, 0, max), Mathf.Clamp(c.g, 0, max), Mathf.Clamp(c.b, 0, max), alpha ? Mathf.Clamp01(c.a) : previousAlpha);
        }

        internal static WhimTexColorPicker Open(Color value, bool hdr, bool alpha, WhimTexColorRange range,
            WhimTexDocument document, Action<Color> changed, Action<bool> modeChanged = null, Func<bool> valid = null)
        {
            if (current != null) current.Finish(true);
            var window = CreateInstance<WhimTexColorPicker>();
            window.original = window.color = value;
            window.initialHdr = window.hdr = range == WhimTexColorRange.HdrOnly || range == WhimTexColorRange.Switchable && hdr;
            window.alphaVisible = alpha; window.range = range; window.document = document;
            window.changed = changed; window.modeChanged = modeChanged; window.valid = valid;
            window.titleContent = new GUIContent("Color");
            window.minSize = new Vector2(240, 520); window.maxSize = new Vector2(420, 800);
            window.position = new Rect(200, 150, 260, 540);
            current = window; window.Decode(); window.ShowUtility();
            return window;
        }

        private void OnEnable() { Undo.undoRedoPerformed += RefreshHistory; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload; }
        private void BeforeReload() => Finish(true);
        private void OnDisable()
        {
            try { StopEyedropper(); }
            finally
            {
                hueRing?.ReleaseMaterial();
                if (historyCursor != null) DestroyImmediate(historyCursor);
                historyCursor = null;
                Undo.undoRedoPerformed -= RefreshHistory; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                if (!finished) Complete(true);
                if (current == this) current = null;
            }
        }
        private void Update()
        {
            if (!finished && valid != null && !valid()) { finished = true; Close(); }
            if (!finished) UpdateEyedropper();
            if (!finished) UpdateColorChannels();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear();
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexColorPicker.uss"));
            root.AddToClassList("whimtex-color-picker");
            ramps.Clear();
            var toolbar = Row(root); toolbar.AddToClassList("whimtex-picker-toolbar");
            eyedropper = new UnityEditor.UIElements.ColorField { showAlpha = alphaVisible, showEyeDropper = true, hdr = true };
            eyedropper.AddToClassList("whimtex-picker-eyedropper"); toolbar.Add(eyedropper);
            eyedropper.RegisterValueChangedCallback(e => { if (!syncing) SetColor(e.newValue); });
            hdrControl = new Toggle("HDR"); toolbar.Add(hdrControl);
            hdrControl.SetEnabled(range == WhimTexColorRange.Switchable);
            hdrControl.tooltip = range == WhimTexColorRange.Switchable ? "Switch color entry range. Switching alone preserves the stored color." : "This input requires a fixed color range.";
            hdrControl.RegisterValueChangedCallback(e => { if (syncing) return; hdr = e.newValue; modeChanged?.Invoke(hdr); Refresh(); RefreshHistory(); });
            BuildChannelControl(toolbar);
            before = Swatch(toolbar, () => original); before.tooltip = "Original color — click to restore";
            after = Swatch(toolbar, () => color); after.tooltip = "New color";
            before.AddManipulator(new Clickable(() => SetColor(original)));
            var wheel = new VisualElement(); wheel.AddToClassList("whimtex-picker-wheel"); root.Add(wheel);
            hueRing = new HueRing(() => hue, () => PickerChannelMask, () => color.a);
            hueRing.AddToClassList("whimtex-picker-ring"); wheel.Add(hueRing);
            hueRing.AddManipulator(new ColorDrag(p => { hue = Mathf.Repeat(Mathf.Atan2(.5f-p.y,p.x-.5f)/(2*Mathf.PI),1); FromHsv(); },
                p => { float radius = (p-new Vector2(.5f,.5f)).magnitude; return radius >= .39f && radius <= .5f; }));
            plane = new ColorSurface(ColorSurface.Kind.Plane, () => hue, () => new Vector2(saturation, brightness), () => PickerChannelMask, () => color.a,
                c => PreviewColor(WithColorExposure(c)));
            plane.AddToClassList("whimtex-picker-plane"); wheel.Add(plane);
            plane.AddManipulator(new ColorDrag(p => { saturation = p.x; brightness = 1 - p.y; FromHsv(); }));
            BuildEyedropperPreview(wheel);
            int savedMode = EditorPrefs.GetInt(ColorModePreference, 2);
            mode = new PopupField<string>(new List<string> { "RGB 0–255", "RGB 0–1", "HSV" }, savedMode >= 0 && savedMode <= 2 ? savedMode : 2);
            mode.AddToClassList("whimtex-picker-mode");
            root.Add(mode); mode.RegisterValueChangedCallback(_ =>
            {
                EditorPrefs.SetInt(ColorModePreference, mode.index);
                Refresh();
            });
            channels = new Slider[3];
            for (int i = 0; i < 3; i++)
            {
                int channel = i;
                channels[i] = new Slider("", 0, 1) { showInputField = true }; root.Add(channels[i]);
                AddRamp(channels[i], t => ChannelColor(channel,t), true);
                channels[i].RegisterValueChangedCallback(e => EditChannel(channel, e.newValue));
            }
            alpha = new Slider("A", 0, 1) { showInputField = true }; root.Add(alpha);
            AddRamp(alpha, t => { var c = color; c.a = t; return c; });
            alpha.RegisterValueChangedCallback(e => { if (syncing) return; var next = color; next.a = e.newValue; SetColor(next); });
            intensity = new Slider("Exposure", -10, 16) { showInputField = true, tooltip = "HDR intensity in stops. +1 doubles RGB intensity." }; root.Add(intensity);
            intensity.AddToClassList("whimtex-picker-exposure");
            AddRamp(intensity, t => { var c = Color.HSVToRGB(hue,saturation,brightness) * Mathf.Pow(2,Mathf.Lerp(-10,16,t)); c.a=1; return c; }, true);
            intensity.RegisterValueChangedCallback(e => { if (syncing) return; exposure = e.newValue; FromHsv(); });
            hex = new TextField("Hexadecimal") { isDelayed = true }; hex.AddToClassList("whimtex-picker-hex"); root.Add(hex);
            var hexPrefix = new Label("#") { pickingMode = PickingMode.Ignore };
            hexPrefix.AddToClassList("whimtex-picker-hex-prefix");
            hex.Insert(hex.IndexOf(hex.Q(className: TextField.inputUssClassName)), hexPrefix);
            hex.tooltip = "RGB or RGBA hexadecimal, with or without #. Alpha is applied when editable; only RGB is displayed.";
            hex.RegisterValueChangedCallback(e =>
            {
                if (syncing) return;
                string digits = (e.newValue ?? "").Trim().TrimStart('#');
                if (ColorUtility.TryParseHtmlString("#" + digits, out Color parsed))
                {
                    float parsedAlpha = digits.Length == 4 || digits.Length == 8 ? parsed.a : color.a;
                    parsed *= hdr ? Mathf.Pow(2, exposure) : 1;
                    parsed.a = parsedAlpha;
                    SetColor(parsed);
                }
                else Refresh();
            });
            const string historyExpandedKey = "DCFApixels.WhimTex.ColorPicker.HistoryExpanded";
            var heading = new Foldout { text = "History", value = EditorPrefs.GetBool(historyExpandedKey, true), tooltip = "Click to reuse. Drag to reorder; drag outside the history to remove on release (red outline). Escape cancels the drag." };
            heading.AddToClassList("whimtex-picker-heading");
            heading.AddToClassList("whimtex-color-library-section");
            heading.RegisterValueChangedCallback(e =>
            {
                if (e.target == heading) EditorPrefs.SetBool(historyExpandedKey, e.newValue);
            });
            root.Add(heading);
            var scroll = historyScroll = new ScrollView(); scroll.AddToClassList("whimtex-picker-history-scroll"); heading.Add(scroll);
            history = new VisualElement(); history.AddToClassList("whimtex-picker-history"); scroll.Add(history);
            var spacer = new VisualElement(); spacer.AddToClassList("whimtex-picker-debug-spacer"); root.Add(spacer);
            var debug = new VisualElement(); debug.AddToClassList("whimtex-picker-debug"); root.Add(debug);
            var ev = new Slider("Preview EV", -10, 10) { name = "previewEV", value = previewEV, showInputField = true,
                tooltip = "Preview exposure only. Stored colors, numeric fields and HDR Exposure are unchanged." };
            ev.RegisterValueChangedCallback(e =>
            {
                if (!Finite(e.newValue)) { ev.SetValueWithoutNotify(previewEV); return; }
                previewEV = Mathf.Clamp(e.newValue, -10, 10); ev.SetValueWithoutNotify(previewEV);
                plane.MarkDirtyRepaint(); before.MarkDirtyRepaint(); after.MarkDirtyRepaint();
                foreach (var ramp in ramps) ramp.MarkDirtyRepaint();
                foreach (var chip in history.Children()) chip.MarkDirtyRepaint();
            });
            debug.Add(ev);
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { e.StopPropagation(); if (!StopEyedropper()) Finish(false); } });
            Refresh(); RefreshHistory();
        }

        private static VisualElement Row(VisualElement parent)
        { var row = new VisualElement(); row.AddToClassList("whimtex-picker-row"); parent.Add(row); return row; }
        private ColorSurface Swatch(VisualElement parent, Func<Color> read)
        { var swatch = new ColorSurface(read, channels: () => PickerChannelMask, preview: PreviewColor); swatch.AddToClassList("whimtex-picker-swatch"); parent.Add(swatch); return swatch; }
        private Color PreviewColor(Color value)
        {
            Color display = value.linear * Mathf.Pow(2, previewEV);
            display = display.gamma;
            return new Color(Mathf.Clamp01(display.r), Mathf.Clamp01(display.g), Mathf.Clamp01(display.b), value.a);
        }
        private Color WithColorExposure(Color value)
        {
            float a = value.a;
            value *= hdr ? Mathf.Pow(2, exposure) : 1;
            value.a = a;
            return value;
        }
        private void AddRamp(Slider slider, Func<float,Color> sample, bool useColorAlpha = false)
        {
            slider.AddToClassList("whimtex-picker-channel");
            var ramp = new ChannelRamp(t =>
            {
                var c = sample(t);
                int mask = PickerChannelMask;
                if (useColorAlpha && mask >= 0 && mask != 15) c.a = color.a;
                return WhimTexColorChannels.Apply(PreviewColor(c), mask);
            }) { pickingMode = PickingMode.Ignore };
            ramp.AddToClassList("whimtex-picker-ramp");
            slider.Q(className: Slider.trackerUssClassName).Add(ramp); ramps.Add(ramp);
        }
        private Color ChannelColor(int channel, float t)
        {
            if (mode.index == 2) return WithColorExposure(Color.HSVToRGB(channel==0?t:hue,channel==0?1:channel==1?t:saturation,channel==0?1:channel==2?t:brightness));
            var c = hdr ? color : WhimTexColorInputs.StandardColor(color);
            c[channel] = t * channels[channel].highValue / (mode.index==0?255:1); c.a=1;
            return c;
        }
        private void Decode()
        {
            var baseColor = WhimTexColorInputs.StandardColor(color);
            Color.RGBToHSV(baseColor, out float h, out saturation, out brightness);
            if (saturation > 0.00001f) hue = h;
            exposure = Mathf.Log(Mathf.Max(1, color.maxColorComponent), 2);
        }
        private void FromHsv()
        {
            Color next = Color.HSVToRGB(hue, saturation, brightness);
            next *= hdr ? Mathf.Pow(2, exposure) : 1; next.a = color.a;
            SetColor(next, false);
        }
        private void EditChannel(int channel, float value)
        {
            if (syncing || !Finite(value)) return;
            if (mode.index == 2)
            {
                if (channel == 0) hue = value / 360; else if (channel == 1) saturation = value / 100; else brightness = value / 100;
                FromHsv(); return;
            }
            var next = hdr ? color : WhimTexColorInputs.StandardColor(color);
            next[channel] = mode.index == 0 ? value / 255 : value; SetColor(next);
        }
        internal void SetColor(Color next, bool decode = true)
        {
            if (!IsFinite(next) || finished || valid != null && !valid()) return;
            next = Limit(next, hdr, alphaVisible, original.a);
            if (!color.Equals(next)) { color = next; edited = true; changed?.Invoke(color); }
            if (decode) Decode(); Refresh();
        }
        private void Refresh()
        {
            if (channels == null) return;
            syncing = true;
            try
            {
                hdrControl.SetValueWithoutNotify(hdr);
                var display = hdr ? color : WhimTexColorInputs.StandardColor(color);
                for (int i = 0; i < 3; i++)
                {
                    channels[i].label = mode.index == 2 ? new[] { "H", "S", "V" }[i] : new[] { "R", "G", "B" }[i];
                    float v = mode.index == 2 ? (i == 0 ? hue * 360 : i == 1 ? saturation * 100 : brightness * 100) : display[i] * (mode.index == 0 ? 255 : 1);
                    channels[i].SetValueWithoutNotify(0);
                    channels[i].highValue = mode.index == 2 ? (i == 0 ? 360 : 100) : Mathf.Max(mode.index == 0 ? 255 : 1, v);
                    channels[i].SetValueWithoutNotify(v);
                }
                alpha.EnableInClassList("whimtex-picker-hidden", !alphaVisible); alpha.SetValueWithoutNotify(color.a);
                intensity.EnableInClassList("whimtex-picker-hidden", !hdr); intensity.SetValueWithoutNotify(exposure);
                hex.SetValueWithoutNotify(ColorUtility.ToHtmlStringRGB(WhimTexColorInputs.StandardColor(color)));
                eyedropper.SetValueWithoutNotify(color);
                plane.MarkDirtyRepaint(); hueRing.MarkDirtyRepaint(); before.MarkDirtyRepaint(); after.MarkDirtyRepaint();
                foreach (var ramp in ramps) ramp.MarkDirtyRepaint();
            }
            finally { syncing = false; }
        }
        private void RefreshHistory()
        {
            if (history == null) return;
            history.Clear();
            var add = new ColorSurface(() => new Color(.5f, .5f, .5f), true) { tooltip = "Add current color to History", focusable = true };
            add.AddToClassList("whimtex-picker-chip");
            add.AddToClassList("whimtex-picker-add-color");
            add.AddManipulator(new Clickable(AddCurrentHistoryColor));
            add.RegisterCallback<NavigationSubmitEvent>(e => { AddCurrentHistoryColor(); e.StopPropagation(); });
            add.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                AddCurrentHistoryColor(); e.StopPropagation();
            });
            add.SetEnabled(document != null);
            history.Add(add);
            if (document == null) { history.Add(new Label("No document history for this input.")); return; }
            var colors = document.ColorHistory;
            for (int i = 0; i < colors.Count; i++)
            {
                if (!IsHistoryColorVisible(colors[i], hdr)) continue;
                AddHistoryChip(document, i, colors[i], history, historyScroll, () => PickerChannelMask,
                    ref historyCursor, RefreshHistory, SelectHistoryColor, PreviewColor);
            }
        }
        internal static void AddHistoryChip(WhimTexDocument document, int index, Color value,
            VisualElement grid, ScrollView scroll, Func<int> mask, ref Texture2D cursor,
            Action refresh, Action<int, Color> select, Func<Color, Color> preview = null)
        {
            var chip = new ColorSurface(() => value, channels: mask, preview: preview) { tooltip = value.ToString("F3"), userData = index, focusable = true };
            chip.AddToClassList("whimtex-picker-chip"); grid.Add(chip);
            if (cursor == null) cursor = WhimTexWindow.CreateEyedropperCursor();
            if (cursor != null) chip.style.cursor = new UnityEngine.UIElements.Cursor { texture = cursor, hotspot = WhimTexWindow.EyedropperCursorHotspot(cursor) };
            chip.AddManipulator(new HistoryDrag(document, grid, scroll, refresh, select, index, value));
            chip.AddManipulator(new ContextualMenuManipulator(e => e.menu.AppendAction("Remove", _ => { document.RemoveHistoryColor(index); refresh(); })));
            chip.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { e.StopPropagation(); document.RemoveHistoryColor(index); refresh(); }
                else if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { e.StopPropagation(); select(index, value); }
            });
        }
        private void AddCurrentHistoryColor()
        {
            if (document == null || finished || valid != null && !valid()) return;
            document.RememberColor(color);
            RefreshHistory();
        }
        private void SelectHistoryColor(int index, Color value)
        {
            if (document == null || finished || valid != null && !valid() || !IsHistoryColorVisible(value, hdr) ||
                index < 0 || index >= document.ColorHistory.Count || !document.ColorHistory[index].Equals(value)) return;
            document.MoveHistoryColor(index, 0);
            RefreshHistory();
            SetColor(value);
        }
        internal void Finish(bool accept) { if (finished) return; Complete(accept); Close(); }
        private void Complete(bool accept)
        {
            finished = true;
            if (valid != null && !valid()) return;
            if (accept) { if (document != null) document.RememberColor(color); }
            else { modeChanged?.Invoke(initialHdr); if (edited) changed?.Invoke(original); }
        }

        private sealed class ColorDrag : PointerManipulator
        {
            private readonly Action<Vector2> move;
            private readonly Func<Vector2,bool> hit;
            private int pointer = -1;
            internal ColorDrag(Action<Vector2> move, Func<Vector2,bool> hit = null) { this.move = move; this.hit = hit; }
            protected override void RegisterCallbacksOnTarget()
            { target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost); }
            protected override void UnregisterCallbacksFromTarget()
            { target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost); }
            private void Sample(Vector2 p) => move(new Vector2(Mathf.Clamp01(p.x / Mathf.Max(1,target.contentRect.width)), Mathf.Clamp01(p.y / Mathf.Max(1,target.contentRect.height))));
            private void Down(PointerDownEvent e) { if (e.button != 0 || hit != null && !hit(new Vector2(e.localPosition.x/Mathf.Max(1,target.contentRect.width),e.localPosition.y/Mathf.Max(1,target.contentRect.height)))) return; pointer = e.pointerId; target.CapturePointer(pointer); e.StopPropagation(); Sample(e.localPosition); }
            private void Move(PointerMoveEvent e) { if (pointer != e.pointerId) return; e.StopPropagation(); Sample(e.localPosition); }
            private void Up(PointerUpEvent e) { if (pointer != e.pointerId) return; e.StopPropagation(); target.ReleasePointer(pointer); pointer = -1; }
            private void Lost(PointerCaptureOutEvent e) { pointer = -1; }
        }
        private sealed class HistoryDrag : PointerManipulator
        {
            private readonly WhimTexDocument document;
            private readonly VisualElement history;
            private readonly ScrollView historyScroll;
            private readonly Action refresh;
            private readonly Action<int, Color> select;
            private readonly int index; private readonly Color value;
            private int pointer = -1, destination = -1; private Vector2 start; private bool dragging, pendingRemoval;
            internal HistoryDrag(WhimTexDocument document, VisualElement history, ScrollView historyScroll,
                Action refresh, Action<int, Color> select, int index, Color value)
            { this.document = document; this.history = history; this.historyScroll = historyScroll; this.refresh = refresh; this.select = select; this.index = index; this.value = value; }
            protected override void RegisterCallbacksOnTarget()
            { target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost); target.RegisterCallback<PointerCancelEvent>(Cancel); target.RegisterCallback<KeyDownEvent>(Key); }
            protected override void UnregisterCallbacksFromTarget()
            { target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost); target.UnregisterCallback<PointerCancelEvent>(Cancel); target.UnregisterCallback<KeyDownEvent>(Key); }
            private void Down(PointerDownEvent e) { if (e.button != 0 || pointer >= 0) return; pointer = e.pointerId; start = e.position; dragging = pendingRemoval = false; destination = -1; target.CapturePointer(pointer); target.Focus(); e.StopPropagation(); }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer) return;
                UpdateDrop(e.position); e.StopPropagation();
            }
            private void UpdateDrop(Vector2 position)
            {
                if (Vector2.Distance(start, position) > 5) dragging = true;
                Rect area = historyScroll.worldBound;
                const float margin = 16;
                area.xMin -= margin; area.xMax += margin; area.yMin -= margin; area.yMax += margin;
                pendingRemoval = dragging && !area.Contains(position);
                destination = -1;
                foreach (var child in history.Children())
                {
                    if (!(child.userData is int childIndex)) continue;
                    bool hit = dragging && !pendingRemoval && child.worldBound.Contains(position);
                    child.EnableInClassList("whimtex-picker-chip--target", hit);
                    if (hit) destination = childIndex;
                }
                target.EnableInClassList("whimtex-picker-chip--dragging", dragging);
                target.EnableInClassList("whimtex-picker-chip--remove", pendingRemoval);
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || e.button != 0) return;
                UpdateDrop(e.position);
                bool reorder = dragging, remove = pendingRemoval; int to = destination;
                Stop(); e.StopPropagation();
                if (remove) { document.RemoveHistoryColor(index); refresh(); }
                else if (reorder) { if (to >= 0) document.MoveHistoryColor(index, to); refresh(); }
                else select(index, value);
            }
            private void Key(KeyDownEvent e)
            { if (pointer >= 0 && e.keyCode == KeyCode.Escape) { Stop(); e.StopImmediatePropagation(); } }
            private void Cancel(PointerCancelEvent e) { if (pointer == e.pointerId) Stop(); }
            private void Stop() { int captured = pointer; Clear(); if (captured >= 0) target.ReleasePointer(captured); }
            private void Lost(PointerCaptureOutEvent e) => Clear();
            private void Clear()
            {
                pointer = -1; dragging = pendingRemoval = false; destination = -1;
                target.RemoveFromClassList("whimtex-picker-chip--dragging"); target.RemoveFromClassList("whimtex-picker-chip--remove");
                foreach (var child in history.Children()) child.RemoveFromClassList("whimtex-picker-chip--target");
            }
        }
        private sealed class HueRing : ImmediateModeElement
        {
            private static readonly int SizeId = Shader.PropertyToID("_Size");
            private static readonly int HueId = Shader.PropertyToID("_Hue");
            private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
            private static readonly int ChannelsId = Shader.PropertyToID("_Channels");
            private static readonly int SourceAlphaId = Shader.PropertyToID("_SourceAlpha");
            private readonly Func<float> hue;
            private readonly Func<int> channels;
            private readonly Func<float> sourceAlpha;
            private Material material;

            internal HueRing(Func<float> hue, Func<int> channels, Func<float> sourceAlpha)
            {
                this.hue = hue;
                this.channels = channels; this.sourceAlpha = sourceAlpha;
                cullingEnabled = true;
                RegisterCallback<AttachToPanelEvent>(_ => CreateMaterial());
                RegisterCallback<DetachFromPanelEvent>(_ => ReleaseMaterial());
            }

            private void CreateMaterial()
            {
                if (material != null) return;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexHueRing.shader");
                if (shader != null && shader.isSupported)
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "WhimTex Hue Ring" };
            }

            internal void ReleaseMaterial()
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                material = null;
            }

            protected override void ImmediateRepaint()
            {
                Rect r = contentRect;
                if (material == null || r.width <= 0 || r.height <= 0) return;
                float opacity = 1;
                for (VisualElement element = this; element != null; element = element.parent)
                    opacity *= element.resolvedStyle.opacity;
                material.SetVector(SizeId, new Vector4(r.width, r.height, 0, 0));
                material.SetFloat(HueId, hue());
                material.SetFloat(OpacityId, opacity);
                int mask = channels(); if (mask < 0) mask = 15;
                material.SetVector(ChannelsId, new Vector4((mask & 1) != 0 ? 1 : 0, (mask & 2) != 0 ? 1 : 0, (mask & 4) != 0 ? 1 : 0, (mask & 8) != 0 ? 1 : 0));
                material.SetFloat(SourceAlphaId, sourceAlpha());
                bool srgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = false;
                    Graphics.DrawTexture(r, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, material, 0);
                }
                finally { GL.sRGBWrite = srgb; }
            }
        }

        private sealed class ColorSurface : VisualElement
        {
            internal enum Kind { Swatch, Plane }
            private readonly Kind kind; private readonly Func<float> hue; private readonly Func<Vector2> marker; private readonly Func<Color> color;
            private readonly bool showPlus;
            private readonly Func<int> channels;
            private readonly Func<float> sourceAlpha;
            private readonly Func<Color, Color> preview;
            internal ColorSurface(Func<Color> color, bool showPlus = false, Func<int> channels = null, Func<Color, Color> preview = null) { this.color = color; this.showPlus = showPlus; this.channels = channels; this.preview = preview; generateVisualContent += Draw; }
            internal ColorSurface(Kind kind, Func<float> hue, Func<Vector2> marker, Func<int> channels, Func<float> sourceAlpha, Func<Color, Color> preview = null) { this.kind = kind; this.hue = hue; this.marker = marker; this.channels = channels; this.sourceAlpha = sourceAlpha; this.preview = preview; generateVisualContent += Draw; }
            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect; if (r.width <= 0 || r.height <= 0) return;
                var p = context.painter2D;
                int mask = channels?.Invoke() ?? -1;
                if (kind == Kind.Swatch)
                {
                    var c = preview != null ? preview(color()) : WhimTexColorInputs.StandardColor(color());
                    if (!showPlus && mask >= 0 && mask != 15)
                    {
                        float height = Mathf.Max(0, r.height - 2);
                        WhimTexColorChannels.DrawSplit(p, new Rect(0, 0, r.width, height), c, mask);
                        RectFill(p, new Rect(0, height, r.width, 2), new Color(.15f, .15f, .15f));
                        RectFill(p, new Rect(0, height, r.width * c.a, 2), new Color(.75f, .75f, .75f));
                        return;
                    }
                    for (int y = 0; y < r.height; y += 6) for (int x = 0; x < r.width; x += 6)
                    { float v = ((x / 6 + y / 6) & 1) == 0 ? .35f : .6f; RectFill(p, new Rect(x, y, Mathf.Min(6,r.width-x), Mathf.Min(6,r.height-y)), Color.Lerp(new Color(v,v,v), new Color(c.r,c.g,c.b), c.a)); }
                    if (showPlus)
                    {
                        Vector2 plusCenter = new Vector2(r.width, r.height) * .5f;
                        RectFill(p, new Rect(plusCenter.x - 3, plusCenter.y - .5f, 6, 1), Color.white);
                        RectFill(p, new Rect(plusCenter.x - .5f, plusCenter.y - 3, 1, 6), Color.white);
                    }
                    return;
                }
                int nx = 32, ny = 36;
                var mesh = context.Allocate((nx + 1) * (ny + 1), nx * ny * 6);
                for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
                {
                    Color c = Color.HSVToRGB(hue(), x / (float)nx, 1-y/(float)ny);
                    if (preview != null) c = preview(c);
                    if (mask >= 0)
                    {
                        c.a = sourceAlpha(); c = WhimTexColorChannels.Apply(c, mask); c.a = 1;
                    }
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x*r.width/nx,y*r.height/ny,Vertex.nearZ), tint = c });
                }
                for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                { ushort a = (ushort)(y*(nx+1)+x), b=(ushort)(a+1), c=(ushort)(a+nx+1), d=(ushort)(c+1);
                    mesh.SetNextIndex(a); mesh.SetNextIndex(b); mesh.SetNextIndex(d); mesh.SetNextIndex(a); mesh.SetNextIndex(d); mesh.SetNextIndex(c); }
                var m = marker(); Vector2 center = new Vector2(m.x*r.width,(1-m.y)*r.height);
                Marker(p,center,5);
            }
            private static void Marker(Painter2D p, Vector2 center, float radius)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    p.lineWidth = pass == 0 ? .75f : 1.75f;
                    p.strokeColor = pass == 0 ? new Color(0, 0, 0, .2f) : Color.white;
                    p.BeginPath(); p.Arc(center, radius + (pass == 0 ? 1.25f : 0), 0, 360); p.Stroke();
                }
            }
            private static void RectFill(Painter2D p, Rect r, Color c)
            { p.fillColor=c; p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax,r.y)); p.LineTo(r.max); p.LineTo(new Vector2(r.x,r.yMax)); p.ClosePath(); p.Fill(); }
        }
        private sealed class ChannelRamp : VisualElement
        {
            private readonly Func<float,Color> sample;
            internal ChannelRamp(Func<float,Color> sample) { this.sample=sample; generateVisualContent+=Draw; }
            private void Draw(MeshGenerationContext context)
            {
                var r=contentRect; if(r.width<1||r.height<1)return;
                int nx=Mathf.CeilToInt(r.width/4),ny=Mathf.CeilToInt(r.height/4);
                var mesh=context.Allocate(nx*ny*4,nx*ny*6);
                for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
                {
                    float x0=x*r.width/nx,x1=(x+1)*r.width/nx,y0=y*r.height/ny,y1=(y+1)*r.height/ny;
                    float bg=((x+y)&1)==0?.45f:.75f;
                    Color Composite(float t) { var c=sample(t); return Color.Lerp(new Color(bg,bg,bg),new Color(Mathf.Clamp01(c.r),Mathf.Clamp01(c.g),Mathf.Clamp01(c.b)),Mathf.Clamp01(c.a)); }
                    Color left=Composite(x/(float)nx),right=Composite((x+1)/(float)nx);
                    mesh.SetNextVertex(new Vertex{position=new Vector3(x0,y0,Vertex.nearZ),tint=left});mesh.SetNextVertex(new Vertex{position=new Vector3(x1,y0,Vertex.nearZ),tint=right});
                    mesh.SetNextVertex(new Vertex{position=new Vector3(x1,y1,Vertex.nearZ),tint=right});mesh.SetNextVertex(new Vertex{position=new Vector3(x0,y1,Vertex.nearZ),tint=left});
                    ushort a=(ushort)((y*nx+x)*4);mesh.SetNextIndex(a);mesh.SetNextIndex((ushort)(a+1));mesh.SetNextIndex((ushort)(a+2));mesh.SetNextIndex(a);mesh.SetNextIndex((ushort)(a+2));mesh.SetNextIndex((ushort)(a+3));
                }
            }
        }
    }
}
