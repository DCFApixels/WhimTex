using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow : EditorWindow
    {
        [SerializeField] private WhimTexGradient gradient = new WhimTexGradient();
        [SerializeField] private UnityEngine.Object owner;
        [SerializeField] private string ownerPath;
        [SerializeField] private bool alphaTrack;
        [SerializeField] private int selected;
        [SerializeField] private bool midpointSelected;
        [SerializeField] private float exposure;
        private VisualElement strip;
        private Image image;
        private ColorField color;
        private ColorField allColors;
        private VisualElement allColorsMixedSwatch;
        private Func<int> channelSource;
        private VisualElement colorControls;
        private Button hdrToggle;
        private FloatField intensity;
        private readonly Dictionary<float, bool> hdrPreferences = new Dictionary<float, bool>();
        private readonly Dictionary<float, float> intensityPreferences = new Dictionary<float, float>();
        private Slider alpha, smoothness, chromaCorrection;
        private FloatField location;
        private EnumField mode, colorMode, huePath, wrapMode;
        private bool pendingRemoval;
        private bool writingOwner;
        private bool checkFocus;
        private WhimTexColorPicker keyColorPicker;
        private bool waitingForColorPicker;
        private double focusCheckAfter;
        private Texture2D preview;
        private WhimTexGradient previewSource;
        private uint previewRevision;
        private float previewExposure;
        private GradientColorKey[] colors;
        private GradientAlphaKey[] alphas;
        private readonly Color[] ramp = new Color[512];
        private readonly Color32[] pixels = new Color32[512 * 2];

        public static WhimTexGradientWindow Open(UnityEngine.Object owner, string propertyPath)
        {
            var value = WhimTexGradientBinding.Read(owner, propertyPath);
            var window = GetWindow<WhimTexGradientWindow>(true, "Gradient");
            if (window.owner != owner || window.ownerPath != propertyPath)
            {
                if (window.owner is WhimTexGradientSession previousSession)
                {
                    Undo.ClearUndo(previousSession);
                    DestroyImmediate(previousSession);
                }
                window.owner = owner;
                window.ownerPath = propertyPath;
                window.gradient = JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(value));
                window.selected = 0; window.selectedCustomKey = 0; window.midpointSelected = false;
                window.hdrPreferences.Clear(); window.intensityPreferences.Clear();
                Undo.ClearUndo(window);
            }
            window.titleContent = new GUIContent("Gradient");
            window.gradient = value;
            window.pendingColorMode = value.ColorMode;
            window.channelSource = owner is WhimTexGradientSession session ? session.channelSource :
                WhimTexColorChannels.FindSource(null, WhimTexColorPicker.DocumentFor(owner));
            window.minSize = new Vector2(380, 460);
#if WHIMTEX_DEEP_DEBUG
            window.minSize += new Vector2(0, 110);
#endif
            window.Refresh();
            window.ShowUtility();
            return window;
        }
        private void OnEnable()
        {
            Undo.undoRedoPerformed += ReloadOwner;
            WhimTexGradientBinding.Changed += OnOwnerChanged;
            EditorApplication.update += CheckFocus;
            AssemblyReloadEvents.beforeAssemblyReload += CloseSession;
        }
        private void CloseSession()
        {
            if (owner is WhimTexGradientSession) Close();
        }
        internal static void CloseSession(WhimTexGradientSession session)
        {
            if (session == null) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>())
                if (window.owner == session) window.Close();
        }
        private void OnFocus() => checkFocus = false;
        private void OnLostFocus()
        {
            checkFocus = true;
            focusCheckAfter = EditorApplication.timeSinceStartup + .15;
        }
        private void CheckFocus()
        {
            if (curvesWindow != null) { checkFocus = false; return; }
            if (waitingForColorPicker)
            {
                if (keyColorPicker != null) return;
                waitingForColorPicker = false;
                checkFocus = false;
                if (!EditorApplication.isCompiling) Focus();
                return;
            }
            if (!checkFocus || EditorApplication.timeSinceStartup < focusCheckAfter || EditorApplication.isCompiling) return;
            var focused = focusedWindow;
            if (focused == this) { checkFocus = false; return; }
            Close();
        }
        private void OnDisable()
        {
            curvesWindow = null;
            if (historyCursor != null) DestroyImmediate(historyCursor);
            historyCursor = null;
            ReleasePresetPreviews();
            EditorApplication.update -= CheckFocus;
            AssemblyReloadEvents.beforeAssemblyReload -= CloseSession;
            Undo.undoRedoPerformed -= ReloadOwner;
            WhimTexGradientBinding.Changed -= OnOwnerChanged;
            if (preview != null) DestroyImmediate(preview);
            preview = null;
            if (owner is WhimTexGradientSession session)
            {
                owner = null;
                Undo.ClearUndo(session);
                DestroyImmediate(session);
            }
        }
        public void CreateGUI()
        {
            if (owner != null) gradient = WhimTexGradientBinding.Read(owner, ownerPath);
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/Editor/WhimTexGradientWindow.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("whimtex-gradient-editor");
            colorMode = new EnumField("Color Mode", gradient.ColorMode) { name = "gradientColorMode" };
            TwoChoiceDropdown.Attach(colorMode);
            colorMode.RegisterValueChangedCallback(e =>
            {
                pendingColorMode = (WhimTexGradientColorMode)e.newValue;
                if (IsCustom) convertColorMode.SetEnabled(pendingColorMode != gradient.ColorMode);
                else Edit(() => gradient.ColorMode = pendingColorMode);
            });
            rootVisualElement.Add(colorMode);
            BuildCustomControls();
            mode = new EnumField("Interpolation", gradient.Interpolation) { name = "gradientInterpolation" };
            TwoChoiceDropdown.Attach(mode);
            mode.RegisterValueChangedCallback(e =>
            {
                var next = (WhimTexGradientInterpolation)e.newValue;
                Edit(() => { gradient.Interpolation = next; pendingColorMode = gradient.ColorMode; });
            });
            rootVisualElement.Add(mode);
            wrapMode = new EnumField("Wrap", gradient.WrapMode) { tooltip = "How values outside 0..1 are sampled: clamp, repeat, or mirror." };
            TwoChoiceDropdown.Attach(wrapMode);
            wrapMode.RegisterValueChangedCallback(e => Edit(() => gradient.WrapMode = (WhimTexGradientWrapMode)e.newValue));
            rootVisualElement.Add(wrapMode);
            smoothness = new Slider("Smoothness", 0, 100) { showInputField = true };
            smoothness.RegisterValueChangedCallback(e => Edit(() => gradient.Smoothness = Mathf.Clamp01(e.newValue / 100)));
            rootVisualElement.Add(smoothness);
            chromaCorrection = new Slider("Chroma Correction", 0, 100) { name = "gradientChromaCorrection", showInputField = true };
            chromaCorrection.RegisterValueChangedCallback(e => Edit(() => gradient.ChromaCorrection = Mathf.Clamp01(e.newValue / 100)));
            rootVisualElement.Add(chromaCorrection);
            huePath = new EnumField("Hue Path", gradient.HuePath) { name = "gradientHuePath" };
            TwoChoiceDropdown.Attach(huePath);
            huePath.RegisterValueChangedCallback(e => Edit(() => gradient.HuePath = (WhimTexGradientHuePath)e.newValue));
            rootVisualElement.Add(huePath);
            strip = new VisualElement { focusable = true, name = "gradientStrip" };
            strip.AddToClassList("whimtex-gradient-strip");
            var background = new VisualElement { pickingMode = PickingMode.Ignore };
            background.AddToClassList("whimtex-gradient-image");
            background.generateVisualContent += DrawCheckerboard;
            strip.Add(background);
            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("whimtex-gradient-image");
            strip.Add(image);
            strip.generateVisualContent += DrawKeys;
            strip.AddManipulator(new KeyDrag(this));
            strip.AddManipulator(new ContextualMenuManipulator(e =>
            {
                CustomStripMenu(e);
                e.menu.AppendAction("Copy", _ => EditorGUIUtility.systemCopyBuffer = WhimTexGradientClipboard.Write(gradient));
                e.menu.AppendAction("Paste", _ =>
                {
                    if (WhimTexGradientField.TryReadClipboard(out var copy)) UsePreset(copy);
                }, WhimTexGradientField.TryReadClipboard(out _) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
            strip.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                { if (!midpointSelected) DeleteKey(); e.StopPropagation(); }
                if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow)
                {
                    float delta = (e.keyCode == KeyCode.LeftArrow ? -1 : 1) * (e.shiftKey ? .01f : .001f);
                    if (midpointSelected) MoveMidpoint(gradient.GetMidpoint(alphaTrack, selected) + delta, true);
                    else MoveKey(Time + delta, true);
                    e.StopPropagation();
                }
            });
            var stripRow = new VisualElement();
            stripRow.AddToClassList("whimtex-gradient-strip-row");
            allColors = new ColorField { name = "gradientAllColors", showAlpha = false, showEyeDropper = false,
                UseCanvasChannels = true, ReadCanvasChannels = () => channelSource?.Invoke() ?? -1,
                tooltip = "Set all color keys. Gray with a white diagonal indicates mixed colors." };
            allColors.AddToClassList("whimtex-gradient-strip-action");
            allColors.AddToClassList("whimtex-gradient-all-colors");
            allColors.OpenPickerOverride = OpenAllColors;
            allColors.RegisterValueChangedCallback(e => Edit(() => StoreAllColors(e.newValue)));
            allColorsMixedSwatch = new VisualElement { pickingMode = PickingMode.Ignore };
            allColorsMixedSwatch.AddToClassList("whimtex-gradient-mixed-color");
            allColorsMixedSwatch.generateVisualContent += DrawMixedColor;
            allColors.Add(allColorsMixedSwatch);
            var reverse = new Button(ReverseGradient) { name = "gradientReverse", tooltip = "Reverse gradient" };
            reverse.AddToClassList("whimtex-gradient-strip-action");
            reverse.AddToClassList("whimtex-gradient-reverse");
            var reverseIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            reverseIcon.AddToClassList("whimtex-gradient-reverse-icon");
            reverseIcon.generateVisualContent += DrawReverseIcon;
            reverse.Add(reverseIcon);
            stripRow.Add(strip); stripRow.Add(allColors); stripRow.Add(reverse);
            rootVisualElement.Add(stripRow);
#if WHIMTEX_DEEP_DEBUG
            BuildDebugCurves();
#endif
            color = new ColorField("Color") { hdr = false, showAlpha = false, UseCanvasChannels = true, ReadCanvasChannels = () => channelSource?.Invoke() ?? -1 };
            color.OpenPickerOverride = OpenKeyColor;
            color.RegisterValueChangedCallback(e => Edit(() =>
            {
                Color value = e.newValue;
                if (!color.hdr) value *= CurrentIntensity();
                else intensityPreferences.Remove(colors[selected].time);
                StoreDisplayColor(value);
            }));
            hdrToggle = new Button(ToggleHdr) { text = "HDR" };
            hdrToggle.AddToClassList("whimtex-gradient-hdr-button");
            intensity = new FloatField("×") { tooltip = "Color intensity. Set to 1 to return to standard brightness." };
            intensity.RegisterValueChangedCallback(e =>
            {
                if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) { Refresh(); return; }
                Edit(() =>
                {
                    float next = Mathf.Clamp(e.newValue, 1, 65504);
                    Color value = DisplayColor() / CurrentIntensity() * next;
                    intensityPreferences[colors[selected].time] = next;
                    StoreDisplayColor(value);
                });
            });
            alpha = new Slider("Opacity", 0, 100) { showInputField = true };
            alpha.RegisterValueChangedCallback(e => Edit(() =>
            {
                if (IsCustom) return;
                alphas[selected].alpha = Mathf.Clamp01(e.newValue / 100); gradient.SetKeys(colors, alphas);
            }));
            location = new FloatField("Location");
            var percent = new Label("%") { pickingMode = PickingMode.Ignore };
            percent.AddToClassList("whimtex-gradient-unit");
            location.Add(percent);
            location.RegisterValueChangedCallback(e =>
            {
                if (midpointSelected) MoveMidpoint(e.newValue / 100, true);
                else MoveKey(e.newValue / 100, true);
            });
            var keyRow = new VisualElement();
            keyRow.AddToClassList("whimtex-gradient-key-row");
            colorControls = new VisualElement();
            colorControls.AddToClassList("whimtex-gradient-key-value");
            colorControls.AddToClassList("whimtex-gradient-color-controls");
            color.AddToClassList("whimtex-gradient-color-field");
            intensity.AddToClassList("whimtex-gradient-intensity");
            colorControls.Add(color); colorControls.Add(hdrToggle); colorControls.Add(intensity);
            alpha.AddToClassList("whimtex-gradient-key-value");
            location.AddToClassList("whimtex-gradient-location");
            var separator = new VisualElement { pickingMode = PickingMode.Ignore };
            separator.AddToClassList("whimtex-gradient-key-separator");
            keyRow.Add(colorControls); keyRow.Add(alpha); BuildCustomValue(keyRow); keyRow.Add(separator); keyRow.Add(location);
            rootVisualElement.Add(keyRow);
            BuildColorHistory();
            BuildPresets();
            var spacer = new VisualElement();
            spacer.AddToClassList("whimtex-gradient-debug-spacer");
            rootVisualElement.Add(spacer);
            var debug = new VisualElement();
            debug.AddToClassList("whimtex-gradient-debug");
            var ev = new Slider("Preview EV", -10, 10) { value = exposure, showInputField = true,
                tooltip = "Debug preview exposure only. Stored colors and the gradient field are unchanged." };
            ev.RegisterValueChangedCallback(e => { exposure = e.newValue; Refresh(); });
            debug.Add(ev);
            rootVisualElement.Add(debug);
            Refresh();
        }
        private int Count => IsCustom ? CustomTrack.Length : alphaTrack ? alphas.Length : colors.Length;
        private void OpenAllColors()
        {
            var expected = gradient;
            var original = gradient.Clone();
            var originalIntensities = new Dictionary<float, float>(intensityPreferences);
            var picker = WhimTexColorPicker.Open(allColors.value, allColors.hdr, false, WhimTexColorRange.Switchable,
                WhimTexColorPicker.DocumentFor(owner), value =>
                {
                    if (this == null || gradient != expected) return;
                    Edit(() => StoreAllColors(value));
                    expected = gradient;
                }, valid: () => this != null && gradient == expected);
            picker.SetMultiValueSource(allColors.showMixedValue, () =>
                {
                    Edit(() =>
                    {
                        gradient = original.Clone();
                        intensityPreferences.Clear();
                        foreach (var entry in originalIntensities) intensityPreferences.Add(entry.Key, entry.Value);
                    });
                    expected = gradient;
                });
            picker.SetChannelSource(channelSource);
            keyColorPicker = picker;
            waitingForColorPicker = true;
        }

        private void StoreAllColors(Color value)
        {
            if (gradient.ColorSpace == ColorSpace.Linear) value = IsCustom ? HdrUtility.Decode(value) : value.linear;
            value.r = Mathf.Clamp(value.r, -65504, 65504);
            value.g = Mathf.Clamp(value.g, -65504, 65504);
            value.b = Mathf.Clamp(value.b, -65504, 65504);
            if (IsCustom) { GradientCustomMarkers.SetColor(gradient, value); intensityPreferences.Clear(); return; }
            for (int i = 0; i < colors.Length; i++)
            {
                value.a = colors[i].color.a;
                colors[i].color = value;
            }
            gradient.SetKeys(colors, alphas);
            intensityPreferences.Clear();
        }

        private void ReverseGradient() => Edit(() =>
        {
            gradient.Reverse();
            if (!IsCustom) selected = Count - (midpointSelected ? 2 : 1) - selected;
            ReversePreferences(hdrPreferences);
            ReversePreferences(intensityPreferences);
        });

        private static void ReversePreferences<T>(Dictionary<float, T> preferences)
        {
            if (preferences.Count == 0) return;
            var previous = new Dictionary<float, T>(preferences);
            preferences.Clear();
            foreach (var entry in previous) preferences[1 - entry.Key] = entry.Value;
        }

        private static void DrawMixedColor(MeshGenerationContext context)
        {
            var rect = context.visualElement.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;
            const float thickness = 1.8f;
            const float shadowThickness = 2.4f;
            var start = new Vector2(rect.xMin, rect.yMax);
            var end = new Vector2(rect.xMax, rect.yMin);
            var shadowOffset = new Vector2(rect.height, rect.width).normalized * (thickness * .7f);
            var p = context.painter2D;
            p.strokeColor = new Color(.18f, .18f, .18f, .75f);
            p.lineWidth = shadowThickness;
            p.lineCap = LineCap.Butt;
            p.BeginPath();
            p.MoveTo(start + shadowOffset);
            p.LineTo(end + shadowOffset);
            p.Stroke();
            p.strokeColor = new Color(1, 1, 1, .55f);
            p.lineWidth = thickness;
            p.BeginPath();
            p.MoveTo(start);
            p.LineTo(end);
            p.Stroke();
        }

        private static void DrawReverseIcon(MeshGenerationContext context)
        {
            var p = context.painter2D;
            p.strokeColor = context.visualElement.resolvedStyle.color;
            p.lineWidth = 1.5f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(11.5f, 6));
            p.BezierCurveTo(new Vector2(10.5f, 1), new Vector2(5, 1), new Vector2(2.5f, 4.5f));
            p.MoveTo(new Vector2(2.5f, 1.5f)); p.LineTo(new Vector2(2.5f, 4.5f)); p.LineTo(new Vector2(5.5f, 4.5f));
            p.MoveTo(new Vector2(2.5f, 8));
            p.BezierCurveTo(new Vector2(3.5f, 13), new Vector2(9, 13), new Vector2(11.5f, 9.5f));
            p.MoveTo(new Vector2(11.5f, 12.5f)); p.LineTo(new Vector2(11.5f, 9.5f)); p.LineTo(new Vector2(8.5f, 9.5f));
            p.Stroke();
        }

        private void OpenKeyColor()
        {
            if (IsCustom) { OpenCustomCurves(selectedCustomKey); return; }
            var expected = gradient;
            int index = selected;
            float time = colors[index].time;
            var picker = WhimTexColorPicker.Open(DisplayColor(), color.hdr, false, WhimTexColorRange.Switchable,
                WhimTexColorPicker.DocumentFor(owner), value =>
            {
                if (this == null || gradient != expected || alphaTrack || midpointSelected ||
                    selected != index || index >= colors.Length || colors[index].time != time) return;
                Edit(() => { intensityPreferences.Remove(time); StoreDisplayColor(value); });
                expected = gradient;
            }, value => { hdrPreferences[time] = value; Refresh(); },
                () => this != null && gradient == expected && !alphaTrack && !midpointSelected && selected == index && index < colors.Length && colors[index].time == time);
            picker.SetChannelSource(channelSource);
            keyColorPicker = picker;
            waitingForColorPicker = true;
        }
        private void ToggleHdr()
        {
            hdrPreferences[colors[selected].time] = !color.hdr;
            Refresh();
        }
        private Color DisplayColor() => gradient.ColorSpace == ColorSpace.Linear ? colors[selected].color.gamma : colors[selected].color;
        private static float ColorIntensity(Color value) => Mathf.Max(1, Mathf.Max(value.r, Mathf.Max(value.g, value.b)));
        private float CurrentIntensity()
        {
            float key = colors[selected].time;
            float minimum = ColorIntensity(DisplayColor());
            if (!intensityPreferences.TryGetValue(key, out float value) || value < minimum) value = minimum;
            intensityPreferences[key] = value;
            return value;
        }
        private void StoreDisplayColor(Color value)
        {
            if (gradient.ColorSpace == ColorSpace.Linear) value = IsCustom ? HdrUtility.Decode(value) : value.linear;
            value.r = Mathf.Clamp(value.r, -65504, 65504);
            value.g = Mathf.Clamp(value.g, -65504, 65504);
            value.b = Mathf.Clamp(value.b, -65504, 65504);
            if (IsCustom) { GradientCustomMarkers.SetColor(gradient, value, selectedCustomKey); return; }
            value.a = colors[selected].color.a;
            colors[selected].color = value; gradient.SetKeys(colors, alphas);
        }
        private float KeyTime(int i) => IsCustom ? CustomTrack[i].time : alphaTrack ? alphas[i].time : colors[i].time;
        private float Time => KeyTime(selected);
        private void Edit(Action action)
        {
            if (IsCustom)
            {
                var previous = gradient;
                gradient = previous.Clone();
                try { action(); }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                {
                    gradient = previous; ShowNotification(new GUIContent(e.Message)); Refresh(); return;
                }
                var next = gradient;
                gradient = previous;
                if (!next.Equals(previous))
                {
                    RecordUndo("Edit Gradient"); gradient = next;
                    try { Commit(); }
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                    { gradient = previous; ShowNotification(new GUIContent(e.Message)); }
                }
                Refresh(); return;
            }
            RecordUndo("Edit Gradient");
            try { action(); Commit(); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            { ShowNotification(new GUIContent(e.Message)); }
            Refresh();
        }
        private void RecordUndo(string name) => Undo.RegisterCompleteObjectUndo(owner != null ? owner : this, name);
        private void Commit()
        {
            if (owner == null) return;
            writingOwner = true;
            try { WhimTexGradientBinding.Write(owner, ownerPath, gradient); }
            finally { writingOwner = false; }
        }
        private void OnOwnerChanged(UnityEngine.Object target, string path)
        {
            if (!writingOwner && target == owner && path == ownerPath) ReloadOwner();
        }
        private void ReloadOwner()
        {
            if (owner is WhimTexGradientSession && curvesWindow == null) { Close(); return; }
            if (owner != null)
            {
                try { gradient = WhimTexGradientBinding.Read(owner, ownerPath); }
                catch (ArgumentException) { Close(); return; }
            }
            hdrPreferences.Clear(); intensityPreferences.Clear();
            pendingColorMode = gradient.ColorMode;
            Refresh();
        }
        private void Refresh()
        {
            if (strip == null) return;
            gradient ??= new WhimTexGradient();
            colors = gradient.ColorKeys; alphas = gradient.AlphaKeys;
            RefreshCustomSelection();
            selected = Mathf.Clamp(selected, 0, Count - 1);
            if (IsCustom || gradient.Interpolation == WhimTexGradientInterpolation.Fixed || selected >= Count - 1) midpointSelected = false;
            Color common = colors[0].color;
            bool mixed = false, anyHdr = false;
            foreach (var key in colors)
            {
                mixed |= key.color.r != common.r || key.color.g != common.g || key.color.b != common.b;
                Color display = gradient.ColorSpace == ColorSpace.Linear ? (IsCustom ? HdrUtility.Encode(key.color) : key.color.gamma) : key.color;
                anyHdr |= ColorIntensity(display) > 1 || display.r < 0 || display.g < 0 || display.b < 0;
            }
            common = gradient.ColorSpace == ColorSpace.Linear ? (IsCustom ? HdrUtility.Encode(common) : common.gamma) : common;
            common.a = 1;
            allColors.hdr = anyHdr;
            allColors.SetValueWithoutNotify(common);
            allColors.showMixedValue = mixed;
            allColorsMixedSwatch.EnableInClassList("whimtex-gradient-hidden", !mixed);
            bool fixedInterpolation = gradient.Interpolation == WhimTexGradientInterpolation.Fixed;
            mode.SetValueWithoutNotify(gradient.Interpolation);
            if (!IsCustom) pendingColorMode = gradient.ColorMode;
            colorMode.SetValueWithoutNotify(IsCustom ? pendingColorMode : gradient.ColorMode);
            colorMode.SetEnabled(!fixedInterpolation);
            huePath.SetValueWithoutNotify(gradient.HuePath);
            huePath.EnableInClassList("whimtex-gradient-hidden", gradient.ColorMode != WhimTexGradientColorMode.Hue);
            huePath.SetEnabled(!fixedInterpolation && !IsCustom);
            chromaCorrection.SetValueWithoutNotify(gradient.ChromaCorrection * 100);
            chromaCorrection.SetEnabled(!fixedInterpolation && !IsCustom);
            wrapMode.SetValueWithoutNotify(gradient.WrapMode);
            smoothness.SetValueWithoutNotify(gradient.Smoothness * 100);
            smoothness.SetEnabled(gradient.Interpolation == WhimTexGradientInterpolation.Blend || gradient.Interpolation == WhimTexGradientInterpolation.Balanced);
            smoothness.EnableInClassList("whimtex-gradient-hidden", gradient.Interpolation != WhimTexGradientInterpolation.Blend && gradient.Interpolation != WhimTexGradientInterpolation.Balanced);
            colorControls.EnableInClassList("whimtex-gradient-hidden", alphaTrack || IsCustom);
            alpha.EnableInClassList("whimtex-gradient-hidden", !alphaTrack || IsCustom);
            RefreshCustomControls();
            colorControls.SetEnabled(!midpointSelected); alpha.SetEnabled(!midpointSelected);
            if (alphaTrack && !IsCustom) alpha.SetValueWithoutNotify(alphas[selected].alpha * 100);
            else if (!IsCustom)
            {
                Color value = DisplayColor();
                float intensityGain = CurrentIntensity();
                bool hdr = hdrPreferences.TryGetValue(colors[selected].time, out bool preference) ? preference : intensityGain > 1 || value.r < 0 || value.g < 0 || value.b < 0;
                color.hdr = hdr;
                hdrToggle.EnableInClassList("whimtex-gradient-hdr-button--inset", !hdr);
                hdrToggle.tooltip = hdr ? "HDR enabled. Click to use the standard picker." : "HDR disabled. Click to use the HDR picker.";
                color.SetValueWithoutNotify(hdr ? value : value / intensityGain);
                intensity.SetValueWithoutNotify(intensityGain);
            }
            location.label = midpointSelected ? "Midpoint" : "Location";
            location.SetValueWithoutNotify((midpointSelected ? gradient.GetMidpoint(alphaTrack, selected) : Time) * 100);
            RefreshColorHistory();
            strip.MarkDirtyRepaint();
#if WHIMTEX_DEEP_DEBUG
            RefreshDebugCurves();
#endif
            if (newPresetImage != null) newPresetImage.image = preview;
            if (preview != null && ReferenceEquals(previewSource, gradient) && previewRevision == gradient.Revision && previewExposure == exposure)
            { image.image = preview; return; }
            preview = BakePreviewTexture(gradient, preview, ramp, pixels, exposure);
            image.image = preview;
            if (newPresetImage != null) newPresetImage.image = preview;
            previewSource = gradient; previewRevision = gradient.Revision; previewExposure = exposure;
        }
        internal static Texture2D BakePreviewTexture(WhimTexGradient gradient, Texture2D texture,
            Color[] ramp, Color32[] pixels, float exposure = 0, double timeStart = 0, double timeEnd = 1)
        {
            if (timeStart == 0 && timeEnd == 1) gradient.Bake(ramp);
            else for (int i = 0; i < ramp.Length; i++)
            {
                double time = timeStart + (timeEnd - timeStart) * (i / (double)Math.Max(1, ramp.Length - 1));
                switch (gradient.WrapMode)
                {
                    case WhimTexGradientWrapMode.Repeat: time -= Math.Floor(time); break;
                    case WhimTexGradientWrapMode.Mirror:
                        time -= Math.Floor(time * .5) * 2;
                        if (time > 1) time = 2 - time;
                        break;
                    default: time = Math.Max(0, Math.Min(1, time)); break;
                }
                ramp[i] = gradient.Evaluate((float)time);
            }
            float gain = Mathf.Pow(2, exposure);
            for (int x = 0; x < ramp.Length; x++)
            {
                Color c = gradient.ColorSpace == ColorSpace.Linear ? ramp[x] : ramp[x].linear;
                c *= gain; c = c.gamma;
                c.a = ramp[x].a;
                pixels[x] = pixels[ramp.Length + x] = c;
            }
            if (texture == null) texture = new Texture2D(ramp.Length, 2, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, false);
            return texture;
        }
        internal static void DrawCheckerboard(MeshGenerationContext context)
        {
            Rect rect = context.visualElement.contentRect;
            var p = context.painter2D;
            const int size = 8;
            for (int y = 0; y < rect.height; y += size)
            for (int x = 0; x < rect.width; x += size)
            {
                float shade = ((x / size + y / size) & 1) == 0 ? .28f : .42f;
                p.fillColor = new Color(shade, shade, shade, 1);
                float right = Mathf.Min(x + size, rect.width), bottom = Mathf.Min(y + size, rect.height);
                p.BeginPath();
                p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(right, y));
                p.LineTo(new Vector2(right, bottom)); p.LineTo(new Vector2(x, bottom));
                p.ClosePath(); p.Fill();
            }
        }
        private void MoveKey(float time, bool record, int[] customGroup = null)
        {
            if (float.IsNaN(time) || float.IsInfinity(time)) { Refresh(); return; }
            if (IsCustom)
            {
                var previous = gradient;
                try
                {
                    var next = previous.Clone();
                    if (next.TryGetCustomKey(selectedCustomKey, out var anchor))
                        next.MoveCustomKeys(customGroup ?? CustomStripGroup(selectedCustomKey), time - anchor.time);
                    if (!next.Equals(previous))
                    {
                        if (record) RecordUndo("Move Gradient Key");
                        gradient = next; Commit();
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                { gradient = previous; ShowNotification(new GUIContent(e.Message)); }
                Refresh(); return;
            }
            time = Mathf.Clamp01(time);
            int newIndex = 0;
            for (int i = 0; i < Count; i++)
            {
                if (i == selected) continue;
                if (Mathf.Abs(KeyTime(i) - time) < .000001f) return;
                if (KeyTime(i) < time) newIndex++;
            }
            if (record) RecordUndo("Move Gradient Key");
            float midpoint = gradient.GetMidpoint(alphaTrack, selected);
            if (!alphaTrack && hdrPreferences.TryGetValue(Time, out bool preference))
            { hdrPreferences.Remove(Time); hdrPreferences[time] = preference; }
            if (!alphaTrack && intensityPreferences.TryGetValue(Time, out float intensityValue))
            { intensityPreferences.Remove(Time); intensityPreferences[time] = intensityValue; }
            if (alphaTrack) alphas[selected].time = time; else colors[selected].time = time;
            gradient.SetKeys(colors, alphas);
            selected = newIndex;
            gradient.SetMidpoint(alphaTrack, selected, midpoint);
            Commit();
            Refresh();
        }
        private void MoveMidpoint(float value, bool record)
        {
            if (IsCustom) return;
            if (float.IsNaN(value) || float.IsInfinity(value)) { Refresh(); return; }
            if (record) RecordUndo("Move Gradient Midpoint");
            gradient.SetMidpoint(alphaTrack, selected, Mathf.Clamp(value, .01f, .99f));
            Commit();
            Refresh();
        }
        private float MidpointX(bool alpha, int index)
        {
            float left = alpha ? alphas[index].time : colors[index].time;
            float right = alpha ? alphas[index+1].time : colors[index+1].time;
            return 10 + Mathf.Lerp(left, right, gradient.GetMidpoint(alpha, index)) * (strip.contentRect.width - 20);
        }
        private int HitMidpoint(float x)
        {
            if (IsCustom || gradient.Interpolation == WhimTexGradientInterpolation.Fixed) return -1;
            for (int i = 0; i < Count - 1; i++)
                if (Mathf.Abs(x - MidpointX(alphaTrack, i)) <= 6) return i;
            return -1;
        }
        private void DeleteKey()
        {
            RemoveKey(true);
        }
        private void RemoveKey(bool record, int[] customGroup = null)
        {
            if (IsCustom)
            {
                var ids = customGroup ?? CustomStripGroup(selectedCustomKey);
                if (!GradientCustomMarkers.CanRemove(gradient, ids)) return;
                if (record) Edit(() => gradient.RemoveCustomKeys(ids));
                else
                {
                    var previous = gradient;
                    try
                    {
                        var next = previous.Clone(); next.RemoveCustomKeys(ids);
                        RecordUndo("Delete Gradient Key"); gradient = next; Commit();
                    }
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                    { gradient = previous; ShowNotification(new GUIContent(e.Message)); }
                    Refresh();
                }
                return;
            }
            if (Count <= 1) return;
            void Remove()
            {
                if (!alphaTrack) { hdrPreferences.Remove(Time); intensityPreferences.Remove(Time); }
                if (alphaTrack) { var keys = new List<GradientAlphaKey>(alphas); keys.RemoveAt(selected); alphas = keys.ToArray(); }
                else { var keys = new List<GradientColorKey>(colors); keys.RemoveAt(selected); colors = keys.ToArray(); }
                gradient.SetKeys(colors, alphas);
            }
            if (record) Edit(Remove);
            else { Remove(); Commit(); Refresh(); }
        }
        private bool IsRemovalPosition(Vector2 position)
        {
            const float margin = 16;
            return (IsCustom ? GradientCustomMarkers.CanRemove(gradient, CustomStripGroup(selectedCustomKey)) : Count > 1) &&
                (position.y < -margin || position.y > strip.contentRect.height + margin);
        }
        private void SetPendingRemoval(bool value)
        {
            if (pendingRemoval == value) return;
            pendingRemoval = value;
            strip.MarkDirtyRepaint();
        }
        private float AtX(float x) => Mathf.Clamp01((x - 10) / Mathf.Max(1, strip.contentRect.width - 20));
        private int Hit(float x)
        {
            if (IsCustom) return HitCustomStrip(x, false);
            int hit = -1; float best = 9;
            for (int i = 0; i < Count; i++)
            {
                float distance = Mathf.Abs(x - (10 + KeyTime(i) * (strip.contentRect.width - 20)));
                if (distance <= best) { best = distance; hit = i; }
            }
            return hit;
        }
        private bool AddKey(float time)
        {
            if (IsCustom) return AddCustomStripKey(time);
            const int limit = 64;
            if (Count >= limit) { ShowNotification(new GUIContent($"Maximum {limit} keys per track.")); return false; }
            for (int i = 0; i < Count; i++) if (Mathf.Abs(KeyTime(i) - time) < .00001f) return false;
            Color sample = gradient.Evaluate(time);
            if (alphaTrack)
            { var list = new List<GradientAlphaKey>(alphas) { new GradientAlphaKey(sample.a, time) }; alphas = list.ToArray(); }
            else
            { var list = new List<GradientColorKey>(colors) { new GradientColorKey(sample, time) }; colors = list.ToArray(); }
            gradient.SetKeys(colors, alphas);
            colors = gradient.ColorKeys; alphas = gradient.AlphaKeys;
            for (int i = 0; i < Count; i++) if (KeyTime(i) == time) selected = i;
            Commit();
            return true;
        }
        private void DrawKeys(MeshGenerationContext context)
        {
            if (colors == null || strip.contentRect.width < 20) return;
            if (IsCustom) { DrawCustomStrip(context); return; }
            var p = context.painter2D;
            for (int track = 0; track < 2; track++)
            for (int i = 0; i < (track == 0 ? alphas.Length : colors.Length); i++)
            {
                float t = track == 0 ? alphas[i].time : colors[i].time;
                float x = 10 + t * (strip.contentRect.width - 20);
                float y = track == 0 ? 21 : strip.contentRect.height - 21;
                float d = track == 0 ? -1 : 1;
                Color c = track == 0 ? new Color(alphas[i].alpha, alphas[i].alpha, alphas[i].alpha) : colors[i].color;
                float peak = Mathf.Max(1, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                c /= peak; c.a = 1;
                bool active = !midpointSelected && alphaTrack == (track == 0) && selected == i;
                if (active && pendingRemoval) c.a = .25f;
                p.fillColor = c; p.strokeColor = active ? (pendingRemoval ? new Color(1,.3f,.25f) : new Color(.3f,.7f,1)) : Color.black;
                p.lineWidth = 2;
                p.BeginPath(); p.MoveTo(new Vector2(x,y)); p.LineTo(new Vector2(x-6,y+d*7));
                p.LineTo(new Vector2(x-6,y+d*17)); p.LineTo(new Vector2(x+6,y+d*17));
                p.LineTo(new Vector2(x+6,y+d*7)); p.ClosePath(); p.Fill(); p.Stroke();
            }
            if (gradient.Interpolation == WhimTexGradientInterpolation.Fixed) return;
            for (int track = 0; track < 2; track++)
            for (int i = 0; i < (track == 0 ? alphas.Length : colors.Length) - 1; i++)
            {
                float x = MidpointX(track == 0, i), y = track == 0 ? 10 : strip.contentRect.height - 10;
                p.fillColor = midpointSelected && alphaTrack == (track == 0) && selected == i ? new Color(.3f,.7f,1) : new Color(.7f,.7f,.7f);
                p.strokeColor = Color.black; p.lineWidth = 1;
                p.BeginPath(); p.MoveTo(new Vector2(x,y-4)); p.LineTo(new Vector2(x+4,y));
                p.LineTo(new Vector2(x,y+4)); p.LineTo(new Vector2(x-4,y)); p.ClosePath(); p.Fill(); p.Stroke();
            }
        }
        private sealed class KeyDrag : PointerManipulator
        {
            private readonly WhimTexGradientWindow owner;
            private int pointer = -1, group;
            private string before;
            private int[] customGroup;
            public KeyDrag(WhimTexGradientWindow owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<KeyDownEvent>(Key);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<KeyDownEvent>(Key);
            }
            private void Down(PointerDownEvent e)
            {
                if (e.button != 0 || pointer >= 0) return;
                Vector2 pos = e.localPosition;
                if (pos.y > 22 && pos.y < target.contentRect.height - 22) return;
                owner.alphaTrack = pos.y < 22;
                int hit = owner.IsCustom ? owner.HitCustomStrip(pos.x, e.clickCount == 1) : owner.Hit(pos.x);
                int midpoint = hit < 0 ? owner.HitMidpoint(pos.x) : -1;
                if (e.clickCount == 2 && hit >= 0 && (!owner.alphaTrack || owner.IsCustom))
                {
                    owner.SelectKeyIndex(hit); owner.midpointSelected = false;
                    owner.Refresh();
                    owner.OpenKeyColor();
                    e.StopPropagation(); return;
                }
                before = JsonUtility.ToJson(owner.gradient);
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                if (!owner.IsCustom) owner.RecordUndo("Edit Gradient Key");
                owner.midpointSelected = midpoint >= 0;
                if (midpoint >= 0) owner.selected = midpoint;
                else if (hit >= 0) owner.SelectKeyIndex(hit);
                else if (!owner.AddKey(owner.AtX(pos.x))) { owner.Refresh(); return; }
                customGroup = owner.IsCustom ? owner.CustomStripGroup(owner.selectedCustomKey) : null;
                pointer = e.pointerId; target.CapturePointer(pointer); target.Focus();
                owner.Refresh(); e.StopPropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer) return;
                if (owner.midpointSelected)
                {
                    float left = owner.KeyTime(owner.selected), right = owner.KeyTime(owner.selected + 1);
                    owner.MoveMidpoint((owner.AtX(e.localPosition.x) - left) / (right - left), false);
                    e.StopPropagation(); return;
                }
                owner.SetPendingRemoval(owner.IsRemovalPosition(e.localPosition));
                if (!owner.pendingRemoval) owner.MoveKey(owner.AtX(e.localPosition.x), owner.IsCustom, customGroup);
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || e.button != 0) return;
                try
                {
                    if (!owner.midpointSelected && owner.IsRemovalPosition(e.localPosition)) owner.RemoveKey(false, customGroup);
                }
                finally { Finish(); }
                e.StopPropagation();
            }
            private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) Finish(); }
            private void Key(KeyDownEvent e)
            {
                if (pointer < 0 || e.keyCode != KeyCode.Escape) return;
                owner.gradient = JsonUtility.FromJson<WhimTexGradient>(before);
                owner.Commit();
                Finish(); owner.Refresh(); e.StopPropagation();
            }
            private void Finish()
            {
                int id = pointer; pointer = -1; customGroup = null;
                owner.SetPendingRemoval(false);
                if (id >= 0 && target.HasPointerCapture(id)) target.ReleasePointer(id);
                Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            }
        }
    }
}
