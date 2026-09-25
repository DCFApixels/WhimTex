using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class FillPatternFields
    {
        internal static void Build(VisualElement root, ColorFillLayerBehaviour layer,
            Action<string, Action> change, WhimTexUI.ValueBindings bindings)
        {
            FillPatternSettings P() => layer.pattern ??= new FillPatternSettings();
            EnumField Choice<T>(string label, Func<T> get, Action<T> set) where T : struct, Enum
            {
                var field = WhimTexUI.ConfigureField(new EnumField(label, (Enum)(object)get()));
                bindings.Track(field, () => (Enum)(object)get());
                field.RegisterValueChangedCallback(e => change("Change Pattern " + label, () => set((T)(object)e.newValue)));
                root.Add(field); return field;
            }
            void Number(string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = WhimTexUI.ConfigureField(new FloatField(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(e => change("Change Pattern " + label,
                    () => set(FillPatternSettings.Limit(e.newValue, min, max, get()))));
                root.Add(field);
            }
            Slider Slider(string label, Func<float> get, Action<float> set)
            {
                var field = WhimTexUI.ConfigureField(new Slider(label, 0, 1) { showInputField = true });
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(e => change("Change Pattern " + label,
                    () => set(FillPatternSettings.Limit(e.newValue, 0, 1, get()))));
                root.Add(field); return field;
            }
            void Toggle(string label, Func<bool> get, Action<bool> set)
            {
                var field = WhimTexUI.ConfigureField(new Toggle(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(e => change("Change Pattern " + label, () => set(e.newValue)));
                root.Add(field);
            }
            Choice("Shape", () => P().shape, v => P().shape = v);
            var layout = Choice("Layout", () => P().circleLayout, v => P().circleLayout = v);
            var size = WhimTexUI.ConfigureField(new Vector2Field("Size (px)"));
            size.AddToClassList("whimtex-linked-vector");
            bindings.Track(size, () => P().Size);
            size.RegisterValueChangedCallback(e =>
            {
                var next = P().AdjustSize(e.newValue);
                change("Change Pattern Size", () => P().Size = next);
                size.SetValueWithoutNotify(P().Size);
            });
            var icon = new WhimTexLinkIcon();
            var link = new Button(() => change("Link Pattern Size", () => P().linkSize = !P().linkSize)) { name = "linkSize" };
            link.AddToClassList("whimtex-vector-link");
            link.Add(icon);
            size.Q(className: "unity-base-field__input").Insert(0, link);
            bindings.Add(() =>
            {
                icon.SetLinked(P().linkSize);
                link.tooltip = P().linkSize ? "Linked: scale X and Y proportionally. Click to unlink."
                    : "Unlinked: edit X and Y independently. Click to link without changing the sizes.";
            });
            root.Add(size);
            Toggle("Seamless", () => P().seamless, v => P().seamless = v);
            Number("Rotation", () => P().rotation, v => P().rotation = P().seamless ? Mathf.Round(v / 90) * 90 : v, -360000, 360000);
            var offset = WhimTexUI.ConfigureField(new Vector2Field("Offset (px)"));
            bindings.Track(offset, () => P().offset);
            offset.RegisterValueChangedCallback(e => change("Change Pattern Offset", () => P().offset = new Vector2(
                FillPatternSettings.Limit(e.newValue.x, -1000000, 1000000, P().offset.x),
                FillPatternSettings.Limit(e.newValue.y, -1000000, 1000000, P().offset.y))));
            root.Add(offset);
            var fit = new HelpBox("Seamless fits the combined layer/group transform to the canvas. Rotation snaps to quarter turns; shear and perspective become an axis-aligned grid. FX can change the final seams.", HelpBoxMessageType.Info);
            root.Add(fit);
            var actual = new Label();
            root.Add(actual);
            Slider("Gap", () => P().gap, v => P().gap = Mathf.Min(v, .99f));
            var corners = new VisualElement();
            // Keep rounding in a dedicated container so circles do not show an unused control.
            var rounding = WhimTexUI.ConfigureField(new Slider("Roundness", 0, 1) { showInputField = true });
            bindings.Track(rounding, () => P().roundness);
            rounding.RegisterValueChangedCallback(e => change("Change Pattern Roundness", () => P().roundness = Mathf.Clamp01(e.newValue)));
            corners.Add(rounding); root.Add(corners);
            Slider("Bulge", () => P().bulge, v => P().bulge = v);
            Choice("Distance", () => P().position, v => P().position = v);
            Number("Distance Range", () => P().distanceRange, v => P().distanceRange = v, .001f, 16);
            Toggle("Inverted", () => P().inverted, v => P().inverted = v);
            var profile = WhimTexUI.ConfigureField(new CurveField("Profile"));
            bindings.Track(profile, () => P().profile ?? WhimTexCurveTexture.Default());
            profile.RegisterValueChangedCallback(e => change("Change Pattern Profile", () => P().profile = WhimTexCurveTexture.Copy(e.newValue)));
            root.Add(profile);
            var gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient"), bindings, () => P().gradient));
            gradient.RegisterValueChangedCallback(e => change("Change Pattern Gradient", () => P().gradient = GradientUtility.Create(e.newValue)));
            root.Add(gradient);
            Choice("Cell Color", () => P().cellColor, v => P().cellColor = v);
            var blend = Choice("Color Blend", () => P().colorBlend, v => P().colorBlend = v);
            var palette = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Palette"), bindings, () => P().palette));
            palette.tooltip = "Cell RGB colors. Use a Fixed gradient for a discrete palette. Alpha comes from the distance gradient.";
            palette.RegisterValueChangedCallback(e => change("Change Pattern Palette", () => P().palette = GradientUtility.Create(e.newValue)));
            root.Add(palette);
            var seed = WhimTexUI.ConfigureField(new IntegerField("Seed"));
            bindings.Track(seed, () => P().seed);
            seed.RegisterValueChangedCallback(e => change("Change Pattern Seed", () => P().seed = e.newValue));
            root.Add(seed);
            var variation = Slider("Variation", () => P().variation, v => P().variation = v);
            variation.tooltip = "Palette sampling range: zero uses its midpoint; one uses the full palette.";
            bindings.Add(() =>
            {
                bool uniform = P().cellColor == FillPatternSettings.CellColor.Uniform;
                bool random = P().cellColor == FillPatternSettings.CellColor.Random;
                blend.EnableInClassList("whimtex-hidden", uniform);
                palette.EnableInClassList("whimtex-hidden", uniform);
                seed.EnableInClassList("whimtex-hidden", !random);
                variation.EnableInClassList("whimtex-hidden", !random);
                bool circle = P().shape == FillPatternSettings.Shape.Circles;
                layout.EnableInClassList("whimtex-hidden", !circle);
                corners.EnableInClassList("whimtex-hidden", circle);
                fit.EnableInClassList("whimtex-hidden", !P().seamless);
                actual.EnableInClassList("whimtex-hidden", !P().seamless);
                var cell = P().EffectiveCell;
                string text = $"Fitted grid: {cell.x:0.##} × {cell.y:0.##} px · {P().EffectiveRotation:0}°";
                if (actual.text != text) actual.text = text;
            });
        }
    }
}
