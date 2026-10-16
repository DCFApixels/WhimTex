using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class StrokeDashEditor : VisualElement
    {
        internal const string ExpandedKey = "DCFApixels.WhimTex.Vector.DashSegmentsExpanded";
        private const int MaxValues = 256;
        private const float MaxLength = 8192;
        private readonly Func<StrokeStyle> read;
        private readonly Action<List<float>, VectorStrokeCap?> write;
        private StrokeStyle shape => read();
        private readonly Action<string, Action> change;
        private readonly TextField pattern;
        private readonly Button presetButton, addPair;
        private readonly VisualElement gallery;
        private readonly ScrollView pairs;
        private readonly Label error, oddHint;
        private readonly StrokeDashPreview preview;
        private readonly List<FloatField> values = new List<FloatField>();
        private readonly List<Button> removes = new List<Button>();
        private readonly List<StrokeDashPreview> thumbnails = new List<StrokeDashPreview>();
        private List<float> displayed = new List<float>(), observed;
        private bool textPending, observedRelative;
        private float observedWidth = -1, observedOffset;
        private VectorStrokeCap observedCap;

        private sealed class Preset
        {
            internal readonly string name;
            internal readonly float[] pattern;
            internal readonly VectorStrokeCap? cap;
            internal Preset(string name, float[] pattern, VectorStrokeCap? cap = null)
            { this.name = name; this.pattern = pattern; this.cap = cap; }
        }

        private static readonly Preset[] Presets =
        {
            new Preset("Solid", Array.Empty<float>()),
            new Preset("Short Dash", new[] { 2f, 2f }),
            new Preset("Long Dash", new[] { 6f, 3f }),
            new Preset("Dots · Round", new[] { .01f, 2f }, VectorStrokeCap.Round),
            new Preset("Dash-Dot · Round", new[] { 6f, 3f, .01f, 3f }, VectorStrokeCap.Round),
            new Preset("Dash-Dot-Dot · Round", new[] { 6f, 3f, .01f, 3f, .01f, 3f }, VectorStrokeCap.Round)
        };

        internal StrokeDashEditor(VectorShape shape, Action<string, Action> change)
            : this(() => StrokeStyle.From(shape), (pattern, cap) =>
            { shape.dashPattern = pattern; shape.dashPatternRelative = true; if (cap.HasValue) shape.cap = cap.Value; }, change) { }

        internal StrokeDashEditor(ShapeLayerBehaviour shape, Action<string, Action> change)
            : this(() => StrokeStyle.From(shape), (pattern, cap) =>
            { shape.dashPattern = pattern; if (cap.HasValue) shape.strokeCap = cap.Value; }, change) { }

        private StrokeDashEditor(Func<StrokeStyle> read, Action<List<float>, VectorStrokeCap?> write, Action<string, Action> change)
        {
            this.read = read; this.write = write; this.change = change;
            name = "strokeDashes"; AddToClassList("whimtex-dashes");
            var header = new VisualElement(); header.AddToClassList("whimtex-dashes-header"); Add(header);
            var label = new Label("Dashes"); label.AddToClassList("whimtex-dashes-label"); header.Add(label);
            presetButton = new Button(() => gallery.ToggleInClassList("whimtex-hidden"))
                { name = "dashPresets", tooltip = "Choose a repeating pattern. Presets keep stroke width, color, position, joins and offset." };
            header.Add(presetButton);
            gallery = new VisualElement { name = "dashPresetGallery" };
            gallery.AddToClassList("whimtex-dashes-gallery"); gallery.AddToClassList("whimtex-hidden"); Add(gallery);
            for (int i = 0; i < Presets.Length; i++)
            {
                var preset = Presets[i];
                var button = new Button(() => ApplyPreset(preset)) { name = "dashPreset" + i,
                    tooltip = preset.cap.HasValue ? "Uses 0.01 × Width short dashes and selects Round caps to form dots. Other stroke settings are preserved." : "Change only the dash pattern, in multiples of stroke width." };
                button.AddToClassList("whimtex-dashes-preset");
                button.Add(new Label(preset.name) { pickingMode = PickingMode.Ignore });
                var thumbnail = new StrokeDashPreview(); thumbnail.AddToClassList("whimtex-dashes-thumbnail");
                thumbnails.Add(thumbnail); button.Add(thumbnail); gallery.Add(button);
            }
            preview = new StrokeDashPreview { name = "dashPreview",
                tooltip = "Live pattern preview, including width, caps and offset. Dash and gap lengths scale with stroke width; the preview is reduced proportionally when needed to fit." };
            Add(preview);
            pattern = WhimTexUI.ConfigureField(new TextField("Pattern (× Width)") { name = "dashPattern", maxLength = 16384,
                tooltip = "Alternating dash and gap lengths in multiples of stroke width: 2 1 = dash 2 × Width, gap 1 × Width. Separate with spaces, commas or semicolons. Empty = solid. Odd lists repeat twice. Enter or leave to apply; Escape to cancel." });
            Add(pattern);
            error = new Label { name = "dashError" }; error.AddToClassList("whimtex-dashes-error"); error.AddToClassList("whimtex-hidden"); Add(error);
            pattern.RegisterValueChangedCallback(e =>
            {
                textPending = true;
                if (TryParse(e.newValue, out var next)) { SetError(false); Show(next); }
                else SetError(true);
            });
            pattern.RegisterCallback<FocusOutEvent>(_ => CommitText());
            pattern.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Refresh(true); WhimTexUI.ConsumeEvent(e); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { CommitText(); WhimTexUI.ConsumeEvent(e); }
            }, TrickleDown.TrickleDown);
            var segments = new Foldout { text = "Segments", name = "dashSegments", value = EditorPrefs.GetBool(ExpandedKey, false) };
            segments.RegisterValueChangedCallback(e => { if (e.target == segments) EditorPrefs.SetBool(ExpandedKey, e.newValue); });
            Add(segments);
            oddHint = new Label("Odd pattern: the second repeat swaps Dash and Gap. Repeated values are linked.");
            oddHint.AddToClassList("whimtex-dashes-hint"); segments.Add(oddHint);
            pairs = new ScrollView(ScrollViewMode.Vertical) { name = "dashPairs" }; pairs.AddToClassList("whimtex-dashes-pairs"); segments.Add(pairs);
            addPair = new Button(() => EditPairs(-1)) { text = "+ Add Pair", name = "dashAddPair" }; segments.Add(addPair);
            Refresh(true);
        }

        internal void Refresh() => Refresh(false);

        private void Refresh(bool force)
        {
            bool changed = !Equal(observed, shape.dashPattern) || observedRelative != shape.dashPatternRelative ||
                (!shape.dashPatternRelative && observedWidth != shape.strokeWidth);
            if (force || changed)
            {
                observed = Copy(shape.dashPattern); textPending = false;
                observedRelative = shape.dashPatternRelative;
                var relative = shape.RelativeDashPattern();
                pattern.SetValueWithoutNotify(Format(relative)); SetError(false); Show(relative);
            }
            if (force || changed || observedWidth != shape.strokeWidth || observedCap != shape.cap || observedOffset != shape.dashOffset)
            {
                observedWidth = shape.strokeWidth; observedCap = shape.cap; observedOffset = shape.dashOffset;
                UpdatePreviews();
                for (int i = 0; i < Presets.Length; i++)
                    thumbnails[i].Set(Presets[i].pattern, shape.strokeWidth, Presets[i].cap ?? shape.cap, 0);
            }
        }

        private void Show(List<float> next)
        {
            displayed = next; UpdatePreviews();
            bool odd = (next.Count & 1) != 0;
            oddHint.EnableInClassList("whimtex-hidden", !odd);
            int count = next.Count * (odd ? 2 : 1);
            if (values.Count != count)
            {
                pairs.Clear(); values.Clear(); removes.Clear();
                for (int i = 0; i < count; i += 2)
                {
                    int pair = i / 2;
                    var row = new VisualElement(); row.AddToClassList("whimtex-dashes-pair"); pairs.Add(row);
                    for (int j = 0; j < 2; j++)
                    {
                        int index = i + j;
                        var field = WhimTexUI.ConfigureField(new FloatField(j == 0 ? "Dash" : "Gap") { isDelayed = true,
                            name = (j == 0 ? "dashLength" : "dashGap") + pair,
                            tooltip = "Length as a multiple of stroke width. 1 = one stroke width. Positive values up to 8192." }, 32);
                        row.Add(field); values.Add(field);
                        field.RegisterValueChangedCallback(e =>
                        {
                            if (index >= values.Count || displayed.Count == 0) return;
                            if (!float.IsFinite(e.newValue) || e.newValue <= 0 || e.newValue > MaxLength)
                            { field.SetValueWithoutNotify(displayed[index % displayed.Count]); return; }
                            var edited = Copy(displayed); edited[index % edited.Count] = e.newValue;
                            Apply(edited);
                        });
                    }
                    var remove = new Button(() => EditPairs(pair)) { text = "−", name = "dashRemovePair" + pair, tooltip = "Remove this dash/gap pair." };
                    removes.Add(remove); row.Add(remove);
                }
            }
            for (int i = 0; i < count; i++) values[i].SetValueWithoutNotify(next[i % next.Count]);
            addPair.SetEnabled(count + 2 <= MaxValues);
            addPair.tooltip = count + 2 <= MaxValues ? "Copy the last Dash/Gap pair (2/1 × Width for an empty pattern). Odd lists are expanded to their equivalent full cycle." : "Maximum 256 values. Edit the Pattern text to shorten this list.";
            foreach (var remove in removes) remove.SetEnabled(count - 2 <= MaxValues);
        }

        private void EditPairs(int remove)
        {
            if (!enabledInHierarchy || panel == null) return;
            int count = displayed.Count * ((displayed.Count & 1) == 0 ? 1 : 2);
            var next = new List<float>(count + 2);
            for (int i = 0; i < count; i++) if (i / 2 != remove) next.Add(displayed[i % displayed.Count]);
            if (remove < 0)
            {
                float dash = count >= 2 ? next[count - 2] : 2;
                float gap = count >= 2 ? next[count - 1] : 1;
                next.Add(dash); next.Add(gap);
            }
            if (next.Count <= MaxValues) Apply(next);
        }

        private void CommitText()
        {
            if (!textPending || !enabledInHierarchy || panel == null) return;
            if (TryParse(pattern.value, out var next)) Apply(next);
            else SetError(true);
        }

        private void ApplyPreset(Preset preset)
        {
            Apply(new List<float>(preset.pattern), preset.cap);
            gallery.AddToClassList("whimtex-hidden");
        }

        private void Apply(List<float> next, VectorStrokeCap? cap = null)
        {
            if (!enabledInHierarchy || panel == null) return;
            if (!shape.dashPatternRelative || !Equal(shape.dashPattern, next) || (cap.HasValue && shape.cap != cap.Value))
                change("Change Stroke Dashes", () => write(next, cap));
            Refresh(true);
        }

        private void UpdatePreviews()
        {
            preview.Set(displayed, shape.strokeWidth, shape.cap, shape.dashOffset);
            string title = "Custom";
            foreach (var preset in Presets)
                if (Equal(displayed, preset.pattern) && (!preset.cap.HasValue || preset.cap == shape.cap)) { title = preset.name; break; }
            presetButton.text = title + " ▾";
            for (int i = 0; i < Presets.Length; i++)
                gallery[i].EnableInClassList("whimtex-dashes-preset--selected", title == Presets[i].name);
        }

        private void SetError(bool invalid)
        {
            pattern.EnableInClassList("whimtex-dashes-invalid", invalid);
            error.text = "Use positive multiples of Width up to 8192; maximum 256 values. The last valid pattern is kept.";
            error.EnableInClassList("whimtex-hidden", !invalid);
        }

        internal static bool TryParse(string text, out List<float> result)
        {
            result = new List<float>();
            if (text == null) return true;
            if (text.Length > 16384) return false;
            foreach (string token in text.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (result.Count >= MaxValues || !float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    !float.IsFinite(value) || value <= 0 || value > MaxLength) return false;
                result.Add(value);
            }
            return true;
        }

        private static List<float> Copy(IReadOnlyList<float> source)
        {
            var copy = new List<float>(source?.Count ?? 0);
            if (source != null) for (int i = 0; i < source.Count; i++) copy.Add(source[i]);
            return copy;
        }
        private static string Format(List<float> source) => string.Join(" ", source.ConvertAll(v => v.ToString("R", CultureInfo.InvariantCulture)));
        private static bool Equal(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            for (int i = 0; i < (a?.Count ?? 0); i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
