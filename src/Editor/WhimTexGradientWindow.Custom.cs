using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        [SerializeField] private int selectedCustomKey;
        private WhimTexGradientCurveKey[] customColors = Array.Empty<WhimTexGradientCurveKey>();
        private WhimTexGradientCurveKey[] customAlphas = Array.Empty<WhimTexGradientCurveKey>();
        private WhimTexGradientCurveKey[] customKeys = Array.Empty<WhimTexGradientCurveKey>();
        private FloatField customValue;
        private Label customSelection;
        private Button convertColorMode, editCurves;
        private WhimTexGradientColorMode pendingColorMode;
        private WhimTexGradientCurvesWindow curvesWindow;
        private bool IsCustom => GradientCustomMarkers.IsCustom(gradient);
        private WhimTexGradientCurveKey[] CustomTrack => alphaTrack ? customAlphas : customColors;

        private void BuildCustomControls()
        {
            pendingColorMode = gradient.ColorMode;
            convertColorMode = new Button(() => Edit(() =>
            {
                gradient.ConvertCustomColorMode(pendingColorMode);
                selectedCustomKey = 0;
            })) { text = "Convert Color Mode", name = "gradientConvertColorMode" };
            rootVisualElement.Add(convertColorMode);
            editCurves = new Button(() => OpenCustomCurves(selectedCustomKey))
                { text = "Edit Curves", name = "gradientEditCurves" };
            rootVisualElement.Add(editCurves);
        }

        private void BuildCustomValue(VisualElement row)
        {
            customValue = new FloatField("Value") { name = "gradientCustomValue" };
            customValue.AddToClassList("whimtex-gradient-key-value");
            customValue.RegisterValueChangedCallback(e =>
            {
                if (!IsCustom || !gradient.TryGetCustomKey(selectedCustomKey, out var key)) return;
                if (!float.IsFinite(e.newValue))
                { ShowNotification(new GUIContent("Key values must be finite.")); Refresh(); return; }
                key.value = key.channel == 3 ? Mathf.Clamp01(e.newValue) : e.newValue;
                Edit(() => gradient.UpdateCustomKey(key));
            });
            row.Add(customValue);
            customSelection = new Label { name = "gradientCustomSelection" };
            rootVisualElement.Add(customSelection);
        }

        private void RefreshCustomSelection()
        {
            if (!IsCustom) return;
            customKeys = gradient.CustomKeys;
            customColors = gradient.GetCustomMarkers(); customAlphas = gradient.GetCustomMarkers(true);
            var track = CustomTrack;
            int index = Array.FindIndex(track, k => k.id == selectedCustomKey);
            if (index < 0)
            {
                var group = GradientCustomMarkers.Group(gradient, selectedCustomKey);
                index = Array.FindIndex(track, k => Array.IndexOf(group, k.id) >= 0);
            }
            selected = index >= 0 ? index : 0;
            if (index < 0) selectedCustomKey = track.Length > 0 ? track[selected].id : 0;
            midpointSelected = false;
        }

        private void SelectKeyIndex(int index)
        {
            selected = index;
            if (IsCustom && index >= 0 && index < CustomTrack.Length) selectedCustomKey = CustomTrack[index].id;
        }

        private void RefreshCustomControls()
        {
            convertColorMode.EnableInClassList("whimtex-gradient-hidden", !IsCustom);
            convertColorMode.SetEnabled(IsCustom && pendingColorMode != gradient.ColorMode);
            editCurves.EnableInClassList("whimtex-gradient-hidden", !IsCustom);
            customValue.EnableInClassList("whimtex-gradient-hidden", !IsCustom);
            customSelection.EnableInClassList("whimtex-gradient-hidden", !IsCustom);
            if (IsCustom && gradient.TryGetCustomKey(selectedCustomKey, out var key))
            {
                customValue.label = gradient.CustomChannelName(key.channel);
                customValue.SetValueWithoutNotify(key.value);
                customSelection.text = GradientCustomMarkers.Label(gradient, key) + " · Double-click to edit curves";
            }
        }

        private void OpenCustomCurves(int keyId)
        {
            if (!IsCustom) return;
            var expectedOwner = owner; var expectedPath = ownerPath;
            bool hadOwner = expectedOwner != null;
            bool Valid() => this != null && (!hadOwner || expectedOwner != null) && owner == expectedOwner && ownerPath == expectedPath && IsCustom;
            curvesWindow = WhimTexGradientCurvesWindow.ShowFor(
                () => Valid() ? gradient : null,
                value =>
                {
                    if (!Valid()) return;
                    Edit(() =>
                    {
                        if (value.ColorMode != gradient.ColorMode) pendingColorMode = value.ColorMode;
                        gradient = value.Clone();
                    });
                }, owner != null ? owner : this, keyId);
            checkFocus = false;
        }

        private bool AddCustomStripKey(float time)
        {
            try
            {
                var next = gradient.Clone();
                int id = alphaTrack ? next.AddCustomKey(3, time) : next.AddCustomColorKeys(time);
                if (!next.Equals(gradient))
                { RecordUndo("Add Gradient Key"); gradient = next; Commit(); }
                selectedCustomKey = id;
                RefreshCustomSelection(); return true;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            { ShowNotification(new GUIContent(e.Message)); return false; }
        }

        private void CustomStripMenu(ContextualMenuPopulateEvent e)
        {
            if (!IsCustom) return;
            var point = e.localMousePosition;
            bool previousTrack = alphaTrack;
            alphaTrack = point.y < strip.contentRect.height * .5f;
            int hit = Hit(point.x);
            if (hit >= 0) SelectKeyIndex(hit); else alphaTrack = previousTrack;
            Refresh();
            int id = selectedCustomKey;
            if (!gradient.TryGetCustomKey(id, out var anchor)) return;
            var expected = gradient;
            bool Valid() => this != null && IsCustom && ReferenceEquals(expected, gradient) && gradient.TryGetCustomKey(id, out _);
            e.menu.AppendAction("Edit Curves", _ => { if (Valid()) OpenCustomCurves(id); });
            var group = GradientCustomMarkers.Group(gradient, id);
            foreach (var key in customKeys)
            {
                if (Array.IndexOf(group, key.id) < 0 || key.id == id) continue;
                int otherId = key.id;
                string name = gradient.CustomChannelName(key.channel) + " #" + key.id;
                e.menu.AppendAction("Select/" + name, _ =>
                {
                    if (!Valid()) return;
                    alphaTrack = key.channel == 3; selectedCustomKey = otherId; Refresh(); OpenCustomCurves(otherId);
                });
            }
            e.menu.AppendAction("Delete Keys", _ => { if (Valid()) { selectedCustomKey = id; DeleteKey(); } }, GradientCustomMarkers.CanRemove(gradient, id)
                ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            e.menu.AppendSeparator();
        }

        private void DrawCustomStrip(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var selectedGroup = GradientCustomMarkers.Group(gradient, selectedCustomKey);
            for (int track = 0; track < 2; track++)
            {
                var markers = track == 0 ? customAlphas : customColors;
                for (int pass = 0; pass < 2; pass++)
                foreach (var key in markers)
                {
                    bool active = alphaTrack == (track == 0) && Array.IndexOf(selectedGroup, key.id) >= 0;
                    if (active != (pass == 1)) continue;
                    float x = 10 + key.time * (strip.contentRect.width - 20);
                    float y = track == 0 ? 21 : strip.contentRect.height - 21, d = track == 0 ? -1 : 1;
                    Color c = GradientCustomMarkers.Swatch(gradient, key);
                    if (active && pendingRemoval) c.a = .25f;
                    p.fillColor = c;
                    p.strokeColor = active ? (pendingRemoval ? new Color(1, .3f, .25f) : new Color(.3f, .7f, 1)) : Color.black;
                    p.lineWidth = 2;
                    p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x - 6, y + d * 7));
                    p.LineTo(new Vector2(x - 6, y + d * 17)); p.LineTo(new Vector2(x + 6, y + d * 17));
                    p.LineTo(new Vector2(x + 6, y + d * 7)); p.ClosePath(); p.Fill(); p.Stroke();
                }
            }
        }

        private int HitCustomStrip(float x, bool cycle)
        {
            var hits = new List<int>();
            float nearest = 9;
            for (int i = 0; i < CustomTrack.Length; i++)
            {
                float distance = Mathf.Abs(x - (10 + CustomTrack[i].time * (strip.contentRect.width - 20)));
                if (distance > nearest + .1f) continue;
                if (distance < nearest - .1f) { hits.Clear(); nearest = distance; }
                hits.Add(i);
            }
            if (hits.Count == 0) return -1;
            var selectedGroup = GradientCustomMarkers.Group(gradient, selectedCustomKey);
            int previous = hits.FindIndex(i => Array.IndexOf(selectedGroup, CustomTrack[i].id) >= 0);
            return previous < 0 ? hits[0] : hits[cycle ? (previous + 1) % hits.Count : previous];
        }
    }
}
