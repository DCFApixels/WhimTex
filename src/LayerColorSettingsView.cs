using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class LayerColorSettingsView
    {
        internal static DropdownField GroupBlend(Layer group, Action<BlendMode, bool> change,
            WhimTexUI.ValueBindings bindings)
        {
            var choices = new List<string> { "Pass Through" };
            foreach (BlendMode mode in Enum.GetValues(typeof(BlendMode))) choices.Add(ObjectNames.NicifyVariableName(mode.ToString()));
            var field = new DropdownField(choices, 0);
            bindings.Track(field, () => group.compositing == GroupCompositing.PassThrough
                ? choices[0] : choices[(int)group.blendMode + 1]);
            field.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index >= 0) change(index == 0 ? group.blendMode : (BlendMode)(index - 1), index == 0);
            });
            return field;
        }

        internal static void Build(VisualElement root, Layer layer, Action<string, Action> apply,
            WhimTexUI.ValueBindings bindings, bool expanded, Action<bool> expansionChanged, TextureCompositor owner = null)
        {
            var card = WhimTexUI.CreateInspectorSection("Color & Blending", "colorSection",
                LayerActionIcon.Kind.Alpha, expanded, expansionChanged);
            card.AddToClassList("whimtex-color-card");
            var preset = new DropdownField(new List<string> { "Standard", "HDR" }, 0);
            preset.AddToClassList("whimtex-color-preset");
            preset.tooltip = "Set both Color Range and Blend Range. An empty value means the ranges differ.";
            bindings.Track(preset, () => layer.colorRange == LayerColorRange.Standard && layer.blendRange == LayerBlendRange.Standard
                ? "Standard" : layer.colorRange == LayerColorRange.HDR && layer.blendRange == LayerBlendRange.HDR ? "HDR" : string.Empty);
            preset.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != "Standard" && evt.newValue != "HDR") return;
                apply("Change Layer Color and Blend Ranges", () =>
                {
                    bool hdr = evt.newValue == "HDR";
                    SetColorRange(layer, hdr ? LayerColorRange.HDR : LayerColorRange.Standard);
                    layer.blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard;
                });
            });
            card.hierarchy.Add(preset);
            root.Add(card);
            var opacity = WhimTexUI.ConfigureField(new Slider("Opacity", 0f, 1f)
            {
                name = "layerOpacity", showInputField = true,
                tooltip = "Layer opacity from 0 to 1. This is the same value as in the Layers list."
            });
            bindings.Track(opacity, () => layer.opacity);
            opacity.RegisterValueChangedCallback(evt =>
            {
                float value = float.IsNaN(evt.newValue) ? 0f : Mathf.Clamp01(evt.newValue);
                opacity.SetValueWithoutNotify(value);
                apply("Change Layer Opacity", () => layer.opacity = value);
            });
            card.Add(opacity);
            if (layer.AsGroup() is Layer blendGroup)
            {
                var mode = GroupBlend(blendGroup, (value, passThrough) => apply("Change Group Blend Mode", () =>
                {
                    blendGroup.compositing = passThrough ? GroupCompositing.PassThrough : GroupCompositing.Isolated;
                    if (!passThrough) blendGroup.blendMode = value;
                }), bindings);
                mode.label = "Blend Mode";
                mode.name = "layerBlendMode";
                card.Add(WhimTexUI.ConfigureField(mode));
            }
            else
            {
                var mode = WhimTexUI.ConfigureField(new EnumField("Blend Mode", layer.blendMode) { name = "layerBlendMode" });
                bindings.Track(mode, () => (Enum)layer.blendMode);
                mode.RegisterValueChangedCallback(evt => apply("Change Layer Blend Mode", () => layer.blendMode = (BlendMode)evt.newValue));
                card.Add(mode);
            }
            var color = WhimTexUI.ConfigureField(new EnumField("Color Range", layer.colorRange));
            color.tooltip = "Standard clamps this layer after its FX and Swizzle. HDR keeps signed linear values beyond 0–1.";
            bindings.Track(color, () => (Enum)layer.colorRange);
            color.RegisterValueChangedCallback(evt => apply("Change Layer Color Range",
                () => SetColorRange(layer, (LayerColorRange)evt.newValue)));
            card.Add(color);
            var blend = WhimTexUI.ConfigureField(new EnumField("Blend Range", layer.blendRange));
            blend.tooltip = "Standard uses bounded blend functions in the legacy color space. HDR evaluates extended functions in linear light. Neither clamps the entire backdrop.";
            bindings.Track(blend, () => (Enum)layer.blendRange);
            blend.RegisterValueChangedCallback(evt => apply("Change Layer Blend Range", () => layer.blendRange = (LayerBlendRange)evt.newValue));
            card.Add(blend);
            card.Add(BuildSwizzle(layer, apply, bindings, owner));
            bindings.Add(() =>
            {
                bool active = !(layer?.AsGroup() is Layer group) || !group.IsPassThrough ||
                    (owner != null && owner.IsGroupIsolatedByClipping(group));
                color.SetEnabled(active); blend.SetEnabled(active); preset.SetEnabled(active);
            });
            if (layer?.Behaviour is DrawingLayerBehaviour pixels)
            {
                var storage = new Label();
                storage.AddToClassList("whimtex-storage-description");
                bindings.Add(() => storage.text = HdrUtility.IsHdr(pixels.StoredTexture)
                    ? "Drawing storage: 16-bit float / channel" : "Drawing storage: 8-bit / channel");
                card.Add(storage);
                var compact = new Button(() =>
                {
                    if (EditorUtility.DisplayDialog("Convert Drawing to 8-bit?",
                        "Clamp source pixels to 0–1 and reduce their precision. Color Range becomes Standard. Undo restores the pixels and storage format.", "Convert", "Cancel"))
                        apply("Convert Drawing to 8-bit", pixels.ConvertTo8Bit);
                }) { text = "Convert to 8-bit" };
                compact.AddToClassList("whimtex-compact-storage");
                bindings.Add(() => compact.SetEnabled(HdrUtility.IsHdr(pixels.StoredTexture)));
                card.Add(compact);
            }
        }

        private static VisualElement BuildSwizzle(Layer layer, Action<string, Action> apply, WhimTexUI.ValueBindings bindings, TextureCompositor owner)
        {
            var container = new VisualElement();
            container.AddToClassList("whimtex-swizzle");
            var row = new VisualElement();
            row.AddToClassList("whimtex-swizzle-row");
            var label = new Label("Swizzle");
            label.AddToClassList("unity-base-field__label");
            row.Add(label);
            WhimTexUI.ConfigureField(row);
            var channels = new VisualElement();
            channels.AddToClassList("whimtex-swizzle-channels");
            row.Add(channels);
            for (int channel = 0; channel < 4; channel++)
            {
                int output = channel;
                var choices = new List<string>(LayerSwizzle.Labels);
                var field = new DropdownField(choices, (int)layer.swizzle[output]);
                field.AddToClassList("whimtex-swizzle-channel");
                field.tooltip = "Output " + LayerSwizzle.Labels[output] + ": select a source channel, its inverse, or a constant. Applied after FX in linear space, before Color Range and blending.";
                bindings.Track(field, () => LayerSwizzle.Labels[(int)layer.swizzle[output]]);
                field.RegisterValueChangedCallback(evt =>
                {
                    int source = choices.IndexOf(evt.newValue);
                    if (source >= 0) apply("Change Layer Swizzle", () => layer.swizzle[output] = (SwizzleChannel)source);
                });
                channels.Add(field);
            }
            container.Add(row);
            if (layer?.AsGroup() is Layer group)
            {
                var hint = new HelpBox("", HelpBoxMessageType.Info);
                bindings.Add(() =>
                {
                    bool clipping = owner != null && owner.IsGroupIsolatedByClipping(group);
                    hint.text = clipping
                        ? "Clipping isolates this group using Normal blending. Pass Through resumes when clipping is removed and Swizzle is R G B A."
                        : "Swizzle isolates this group using Normal blending. Restore R G B A to resume Pass Through.";
                    hint.EnableInClassList("whimtex-swizzle-hint--hidden",
                        group.compositing != GroupCompositing.PassThrough || (group.swizzle.IsIdentity && !clipping));
                });
                container.Add(hint);
            }
            return container;
        }

        private static void SetColorRange(Layer layer, LayerColorRange range)
        {
            if (layer?.Behaviour is DrawingLayerBehaviour drawing) drawing.SetColorRange(range);
            else layer.colorRange = range;
        }
    }
}
