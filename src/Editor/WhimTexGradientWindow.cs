using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

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
        private VisualElement colorControls;
        private Button hdrToggle;
        private FloatField intensity;
        private readonly Dictionary<float, bool> hdrPreferences = new Dictionary<float, bool>();
        private readonly Dictionary<float, float> intensityPreferences = new Dictionary<float, float>();
        private Slider alpha, smoothness;
        private FloatField location;
        private EnumField mode, wrapMode;
        private bool pendingRemoval;
        private bool writingOwner;
        private bool checkFocus;
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
                window.selected = 0; window.midpointSelected = false;
                window.hdrPreferences.Clear(); window.intensityPreferences.Clear();
                Undo.ClearUndo(window);
            }
            window.titleContent = new GUIContent("Gradient");
            window.gradient = value;
            window.minSize = new Vector2(380, 350);
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
            if (!checkFocus || EditorApplication.timeSinceStartup < focusCheckAfter || EditorApplication.isCompiling) return;
            var focused = focusedWindow;
            if (focused == this) { checkFocus = false; return; }
            if (focused != null && focused.GetType().FullName == "UnityEditor.ColorPicker") return;
            Close();
        }
        private void OnDisable()
        {
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
            mode = new EnumField("Interpolation", gradient.Mode);
            TwoChoiceDropdown.Attach(mode);
            mode.RegisterValueChangedCallback(e =>
            {
                var next = (WhimTexGradientMode)e.newValue;
                Edit(() => gradient.Mode = next);
            });
            rootVisualElement.Add(mode);
            wrapMode = new EnumField("Wrap", gradient.WrapMode) { tooltip = "How values outside 0..1 are sampled: clamp, repeat, or mirror." };
            TwoChoiceDropdown.Attach(wrapMode);
            wrapMode.RegisterValueChangedCallback(e => Edit(() => gradient.WrapMode = (WhimTexGradientWrapMode)e.newValue));
            rootVisualElement.Add(wrapMode);
            smoothness = new Slider("Smoothness", 0, 100) { showInputField = true };
            smoothness.RegisterValueChangedCallback(e => Edit(() => gradient.Smoothness = Mathf.Clamp01(e.newValue / 100)));
            rootVisualElement.Add(smoothness);
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
            rootVisualElement.Add(strip);
            color = new ColorField("Color") { hdr = false, showAlpha = false };
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
            { alphas[selected].alpha = Mathf.Clamp01(e.newValue / 100); gradient.SetKeys(colors, alphas); }));
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
            keyRow.Add(colorControls); keyRow.Add(alpha); keyRow.Add(separator); keyRow.Add(location);
            rootVisualElement.Add(keyRow);
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
        private int Count => alphaTrack ? alphas.Length : colors.Length;
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
            if (gradient.ColorSpace == ColorSpace.Linear) value = value.linear;
            value.r = Mathf.Clamp(value.r, -65504, 65504);
            value.g = Mathf.Clamp(value.g, -65504, 65504);
            value.b = Mathf.Clamp(value.b, -65504, 65504);
            value.a = colors[selected].color.a;
            colors[selected].color = value; gradient.SetKeys(colors, alphas);
        }
        private float KeyTime(int i) => alphaTrack ? alphas[i].time : colors[i].time;
        private float Time => KeyTime(selected);
        private void Edit(Action action)
        {
            RecordUndo("Edit Gradient");
            try { action(); Commit(); }
            catch (ArgumentException e) { ShowNotification(new GUIContent(e.Message)); }
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
            if (owner is WhimTexGradientSession) { Close(); return; }
            if (owner != null)
            {
                try { gradient = WhimTexGradientBinding.Read(owner, ownerPath); }
                catch (ArgumentException) { Close(); return; }
            }
            hdrPreferences.Clear(); intensityPreferences.Clear();
            Refresh();
        }
        private void Refresh()
        {
            if (strip == null) return;
            gradient ??= new WhimTexGradient();
            colors = gradient.ColorKeys; alphas = gradient.AlphaKeys;
            selected = Mathf.Clamp(selected, 0, Count - 1);
            if (gradient.Mode == WhimTexGradientMode.Fixed || selected >= Count - 1) midpointSelected = false;
            mode.SetValueWithoutNotify(gradient.Mode);
            wrapMode.SetValueWithoutNotify(gradient.WrapMode);
            smoothness.SetValueWithoutNotify(gradient.Smoothness * 100);
            smoothness.SetEnabled(gradient.Mode != WhimTexGradientMode.Fixed);
            colorControls.EnableInClassList("whimtex-gradient-hidden", alphaTrack);
            alpha.EnableInClassList("whimtex-gradient-hidden", !alphaTrack);
            colorControls.SetEnabled(!midpointSelected); alpha.SetEnabled(!midpointSelected);
            if (alphaTrack) alpha.SetValueWithoutNotify(alphas[selected].alpha * 100);
            else
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
            strip.MarkDirtyRepaint();
            if (newPresetImage != null) newPresetImage.image = preview;
            if (preview != null && ReferenceEquals(previewSource, gradient) && previewRevision == gradient.Revision && previewExposure == exposure)
            { image.image = preview; return; }
            gradient.Bake(ramp);
            float gain = Mathf.Pow(2, exposure);
            for (int x = 0; x < 512; x++)
            {
                Color c = gradient.ColorSpace == ColorSpace.Linear ? ramp[x] : ramp[x].linear;
                c *= gain; c = c.gamma;
                c.a = ramp[x].a;
                pixels[x] = pixels[512 + x] = c;
            }
            if (preview == null) preview = new Texture2D(512, 2, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            preview.SetPixels32(pixels); preview.Apply(false, false); image.image = preview;
            if (newPresetImage != null) newPresetImage.image = preview;
            previewSource = gradient; previewRevision = gradient.Revision; previewExposure = exposure;
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
        private void MoveKey(float time, bool record)
        {
            if (float.IsNaN(time) || float.IsInfinity(time)) { Refresh(); return; }
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
            if (gradient.Mode == WhimTexGradientMode.Fixed) return -1;
            for (int i = 0; i < Count - 1; i++)
                if (Mathf.Abs(x - MidpointX(alphaTrack, i)) <= 6) return i;
            return -1;
        }
        private void DeleteKey()
        {
            RemoveKey(true);
        }
        private void RemoveKey(bool record)
        {
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
            return Count > 1 && (position.y < -margin || position.y > strip.contentRect.height + margin);
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
            if (gradient.Mode == WhimTexGradientMode.Fixed) return;
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
                int hit = owner.Hit(pos.x);
                int midpoint = hit < 0 ? owner.HitMidpoint(pos.x) : -1;
                before = JsonUtility.ToJson(owner.gradient);
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                owner.RecordUndo("Edit Gradient Key");
                owner.midpointSelected = midpoint >= 0;
                if (midpoint >= 0) owner.selected = midpoint;
                else if (hit >= 0) owner.selected = hit;
                else if (!owner.AddKey(owner.AtX(pos.x))) { owner.Refresh(); return; }
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
                if (!owner.pendingRemoval) owner.MoveKey(owner.AtX(e.localPosition.x), false);
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || e.button != 0) return;
                try
                {
                    if (!owner.midpointSelected && owner.IsRemovalPosition(e.localPosition)) owner.RemoveKey(false);
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
                int id = pointer; pointer = -1;
                owner.SetPendingRemoval(false);
                if (id >= 0 && target.HasPointerCapture(id)) target.ReleasePointer(id);
                Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            }
        }
    }
}
