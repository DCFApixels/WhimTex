using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class FreeformGradientEditor
    {
        internal static void Build(VisualElement root, GradientLayerBehaviour layer, WhimTexDocument document,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings) =>
            Build(root, () => layer.freeform, value => layer.freeform = value,
                () => layer.selectedFreeformPoint, value => layer.selectedFreeformPoint = value,
                () => layer.gradientType == GradientLayerBehaviour.GradientType.Freeform, document, apply, bindings);

        internal static void Build(VisualElement root, Func<FreeformGradient> read, Action<FreeformGradient> write,
            Func<int> selection, Action<int> select, Func<bool> active, WhimTexDocument document,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            int Index() => Mathf.Clamp(selection(), 0, (read()?.points.Count ?? 1) - 1);
            FreeformGradientPoint Read() => read() != null && read().points.Count > 0
                ? read().points[Index()] : default;
            void Write(string label, Func<FreeformGradientPoint, FreeformGradientPoint> edit)
            {
                if (read() == null || read().points.Count == 0) return;
                int index = Index();
                var next = read().Clone();
                next.points[index] = edit(Read()); next.Validate();
                apply(label, () => write(next));
            }

            var model = WhimTexUI.ConfigureField(new PopupField<string>("Color Model", new List<string> { "Linear", "Gamma", "Perceptual", "Hue" }, 2)
                { name = "freeformColorModel" });
            bindings.Track(model, () => (read()?.colorMode ?? WhimTexGradientColorMode.Perceptual).ToString());
            model.RegisterValueChangedCallback(e =>
            {
                if (read() == null) return;
                var next = read().Clone();
                next.colorMode = Enum.Parse<WhimTexGradientColorMode>(e.newValue); next.Validate();
                apply("Change Freeform Color Model", () => write(next));
            });
            root.Add(model);
            var point = WhimTexUI.ConfigureField(new PopupField<int>("Point", new List<int> { 0 }, 0,
                value => (value + 1).ToString(), value => (value + 1).ToString()) { name = "freeformPoint" });
            point.RegisterValueChangedCallback(e => select(e.newValue));
            root.Add(point);
            var position = WhimTexUI.ConfigureField(new Vector2Field("Position") { name = "freeformPosition" });
            bindings.Track(position, () => Read().position);
            position.RegisterValueChangedCallback(e =>
            {
                if (!float.IsFinite(e.newValue.x) || !float.IsFinite(e.newValue.y)) return;
                Write("Move Freeform Point", p => { p.position = new Vector2(Mathf.Clamp(e.newValue.x, -10000, 10000), Mathf.Clamp(e.newValue.y, -10000, 10000)); return p; });
            });
            root.Add(position);
            var color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(
                new WhimTexColorField("Color") { name = "freeformColor", showAlpha = true, Document = () => document }, bindings, () => Read().color));
            color.RegisterValueChangedCallback(e => Write("Change Freeform Color", p => { p.color = e.newValue; return p; }));
            color.OpenPickerOverride = () =>
            {
                int index = Index(); var expected = read();
                if (expected == null || index < 0 || index >= expected.points.Count) return;
                bool Valid() => color.panel != null && color.enabledInHierarchy &&
                    active() && read() == expected && Index() == index;
                var initial = expected.points[index].color;
                var picker = WhimTexColorPicker.Open(initial, color.hdr || initial.maxColorComponent > 1 ||
                    initial.r < 0 || initial.g < 0 || initial.b < 0, true, WhimTexColorRange.Switchable, document, value =>
                {
                    if (!Valid() || expected.points[index].color.Equals(value)) return;
                    var next = expected.Clone(); var p = next.points[index]; p.color = value; next.points[index] = p;
                    apply("Change Freeform Color", () => write(next)); expected = next;
                }, value => WhimTexColorInputs.Hdr = value, Valid);
                picker.SetChannelSource(color.ResolveChannelSource());
            };
            root.Add(color);
            var opacity = WhimTexUI.ConfigureField(new Slider("Opacity", 0, 100) { name = "freeformOpacity", showInputField = true });
            bindings.Track(opacity, () => Read().color.a * 100);
            opacity.RegisterValueChangedCallback(e => Write("Change Freeform Opacity", p => { p.color.a = Mathf.Clamp01(e.newValue / 100); return p; }));
            root.Add(opacity);
            var influence = WhimTexUI.ConfigureField(new FloatField("Influence (%)") { name = "freeformInfluence" });
            bindings.Track(influence, () => Read().influence * 100);
            influence.RegisterValueChangedCallback(e =>
            {
                if (float.IsFinite(e.newValue)) Write("Change Freeform Influence", p =>
                { p.influence = Mathf.Clamp(e.newValue / 100, FreeformGradient.MinInfluence, FreeformGradient.MaxInfluence); return p; });
            });
            root.Add(influence);
            var remove = new Button(() =>
            {
                if (read().points.Count <= 1) return;
                int index = Index();
                apply("Delete Freeform Point", () =>
                { var next = read().Clone(); next.points.RemoveAt(index); write(next); select(Mathf.Min(index, next.points.Count - 1)); });
            }) { text = "Delete Point", name = "freeformDelete" };
            root.Add(remove);
            bindings.Add(() =>
            {
                bool visible = active();
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible || read() == null) return;
                int count = read().points.Count;
                if (point.choices.Count != count)
                {
                    var choices = new List<int>(count);
                    for (int i = 0; i < count; i++) choices.Add(i);
                    point.choices = choices;
                }
                point.SetValueWithoutNotify(Index());
                remove.SetEnabled(count > 1);
            });
        }
    }
}
