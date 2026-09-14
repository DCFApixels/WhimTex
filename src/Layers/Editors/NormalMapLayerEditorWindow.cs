using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class NormalMapLayerEditorWindow : LayerEditorWindowBase
    {
        private enum SettingsView { Simple, Advanced }
        private const string AdvancedViewKey = "DCFApixels.WhimTex.NormalMap.AdvancedView";
        private static readonly NormalMapLayerBehaviour Defaults = new NormalMapLayerBehaviour();
        protected override Type EditedLayerType => typeof(NormalMapLayerBehaviour);
        protected override string PreviewTitle => "Preview (Normal Map)";
        public static void Open(NormalMapLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<NormalMapLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (NormalMapLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);

        internal static void BuildFields(VisualElement root, NormalMapLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            WhimTexUI.ApplyWindowStyles(root);
            addEffectTarget(root, layer);
            var settings = new VisualElement { name = "normalMapSettings" };
            settings.AddToClassList("whimtex-normal-map-settings");
            root.Add(settings);
            var view = WhimTexUI.ConfigureField(new EnumField("Settings",
                SessionState.GetBool(AdvancedViewKey, false) ? SettingsView.Advanced : SettingsView.Simple));
            view.name = "normalMapSettingsView";
            view.tooltip = "Changes only the visible controls. Hidden settings keep their values and still affect the result.";
            settings.Add(view);
            var advancedNotice = new Button(() => view.value = SettingsView.Advanced)
                { name = "normalMapAdvancedNotice", text = "Advanced settings modified — show" };
            advancedNotice.AddToClassList("whimtex-normal-map-notice");
            settings.Add(advancedNotice);
            VisualElement Section(string title, bool advanced = false)
            {
                var section = new VisualElement();
                section.AddToClassList("whimtex-normal-map-section");
                if (advanced) section.AddToClassList("whimtex-normal-map-advanced");
                var heading = new Label(title);
                heading.AddToClassList("whimtex-normal-map-heading");
                section.Add(heading);
                settings.Add(section);
                return section;
            }
            VisualElement current = Section("Source");
            void Choice<T>(string label, Func<T> get, Action<T> set, string tip = null, bool advanced = false) where T : Enum
            {
                var field = WhimTexUI.ConfigureField(new EnumField(label, get()));
                field.tooltip = tip;
                bindings.Track(field, () => (Enum)get());
                field.RegisterValueChangedCallback(evt => applyChange("Change Normal Map " + label, () => set((T)evt.newValue)));
                if (advanced) field.AddToClassList("whimtex-normal-map-advanced");
                current.Add(field);
            }
            void Number(string label, Func<float> get, Action<float> set, float min, float max, string tip = null,
                bool advanced = false)
            {
                var field = WhimTexUI.ConfigureField(new Slider(label, min, max) { showInputField = true });
                field.tooltip = tip;
                field.SetValueWithoutNotify(get());
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Normal Map " + label,
                    () => set(Mathf.Clamp(evt.newValue, min, max))));
                if (advanced) field.AddToClassList("whimtex-normal-map-advanced");
                current.Add(field);
            }
            void Flag(string label, Func<bool> get, Action<bool> set, string tip = null, bool advanced = false)
            {
                var field = WhimTexUI.ConfigureField(new Toggle(label));
                field.tooltip = tip;
                field.SetValueWithoutNotify(get());
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Normal Map " + label, () => set(evt.newValue)));
                if (advanced) field.AddToClassList("whimtex-normal-map-advanced");
                current.Add(field);
            }
            Choice("Generation", () => layer.mode, v => layer.mode = v,
                "Texture infers height from image contrast at multiple scales; it cannot recover true geometry or reliably separate lighting from surface color.");
            var explanation = new Label();
            explanation.AddToClassList("whimtex-normal-map-hint");
            current.Add(explanation);
            Choice("Source Channel", () => layer.sourceChannel, v => layer.sourceChannel = v);
            Choice("Input Space", () => layer.inputSpace, v => layer.inputSpace = v,
                "Color Values uses displayed RGB values. Linear uses working linear values, suitable for data textures imported without sRGB. Alpha is unchanged.", advanced: true);
            Flag("Ignore Transparent", () => layer.ignoreTransparent, v => layer.ignoreTransparent = v,
                "Normalize smoothing by source alpha to avoid dark fringes. Alpha height always includes transparency as height.", advanced: true);

            current = Section("Surface");
            Number("Strength", () => layer.strength, v => layer.strength = v, 0f, 128f,
                "Height-to-slope amplitude in full-resolution canvas pixels. Zero produces a flat normal.");
            Number("Smoothing (px)", () => layer.smoothing, v => layer.smoothing = v, 0f, 64f,
                "Suppress fine noise before calculating derivatives. Radius is in full-resolution canvas pixels.");
            Flag("Invert Height", () => layer.inverted, v => layer.inverted = v);
            Choice("Edges", () => layer.edges, v => layer.edges = v, "Sampling at canvas edges; independent of the layer's Transform tiling.");
            Choice("Derivative", () => layer.derivative, v => layer.derivative = v,
                "Method used to calculate surface slopes from neighbouring height samples.", advanced: true);

            current = Section("Height Levels", advanced: true);
            Number("Black Level", () => layer.blackLevel, v => layer.blackLevel = Mathf.Min(v, layer.whiteLevel - .0001f), 0f, 1f);
            Number("White Level", () => layer.whiteLevel, v => layer.whiteLevel = Mathf.Max(v, layer.blackLevel + .0001f), .0001f, 16f);
            Number("Gamma", () => layer.gamma, v => layer.gamma = v, .05f, 8f);

            var textureSection = Section("Texture Detail · Texture mode only", advanced: true);
            current = textureSection;
            Number("Medium Radius (px)", () => layer.mediumRadius, v => layer.mediumRadius = Mathf.Min(v, layer.largeRadius), .5f, 128f);
            Number("Large Radius (px)", () => layer.largeRadius, v => layer.largeRadius = Mathf.Max(v, layer.mediumRadius), .5f, 512f);
            Number("Fine Detail", () => layer.fineDetail, v => layer.fineDetail = v, 0f, 8f);
            Number("Medium Detail", () => layer.mediumDetail, v => layer.mediumDetail = v, 0f, 8f);
            Number("Large Detail", () => layer.largeDetail, v => layer.largeDetail = v, 0f, 8f);
            Number("Light Removal", () => layer.lightRemoval, v => layer.lightRemoval = v, 0f, 1f,
                "Attenuates the broadest brightness band. This may also remove real large-scale relief.");

            current = Section("Output");
            Flag("Flip X", () => layer.flipX, v => layer.flipX = v, advanced: true);
            Flag("Flip Y", () => layer.flipY, v => layer.flipY = v, "Reverse the green channel direction when required by the consuming shader.");
            Choice("Alpha", () => layer.alphaMode, v => layer.alphaMode = v);
            Choice("Output", () => layer.output, v => layer.output = v, "Height outputs the reconstructed height for tuning. Switch back to Normal for a normal map.", advanced: true);
            Choice("Encoding", () => layer.encoding, v => layer.encoding = v,
                "Packed Color keeps normal channel values correct in PNG/TGA/PSD and the color preview. Linear Data keeps raw 0–1 vector data in linear EXR/Texture assets; its color preview looks brighter.", advanced: true);

            void RefreshPresentation()
            {
                bool advanced = SessionState.GetBool(AdvancedViewKey, false);
                view.SetValueWithoutNotify(advanced ? SettingsView.Advanced : SettingsView.Simple);
                settings.EnableInClassList("whimtex-normal-map-simple", !advanced);
                advancedNotice.EnableInClassList("whimtex-normal-map-hidden", advanced || !HasAdvancedOverrides(layer));
                advancedNotice.text = layer.output == NormalMapLayerBehaviour.OutputMode.Height
                    ? "Advanced settings modified · Height output — show" : "Advanced settings modified — show";
                bool texture = layer.mode == NormalMapLayerBehaviour.GenerationMode.Texture;
                textureSection.SetEnabled(texture);
                explanation.text = texture
                    ? "Infer relief from image contrast. Detail controls are available in Advanced."
                    : "Use a height image: brighter values represent higher areas.";
            }
            view.RegisterValueChangedCallback(evt =>
            {
                SessionState.SetBool(AdvancedViewKey, (SettingsView)evt.newValue == SettingsView.Advanced);
                RefreshPresentation();
            });
            bindings.Add(RefreshPresentation);
            RefreshPresentation();
        }

        private static bool HasAdvancedOverrides(NormalMapLayerBehaviour layer) =>
            layer.inputSpace != Defaults.inputSpace || layer.ignoreTransparent != Defaults.ignoreTransparent ||
            layer.derivative != Defaults.derivative || layer.flipX != Defaults.flipX ||
            layer.output != Defaults.output || layer.encoding != Defaults.encoding ||
            !Mathf.Approximately(layer.blackLevel, Defaults.blackLevel) || !Mathf.Approximately(layer.whiteLevel, Defaults.whiteLevel) ||
            !Mathf.Approximately(layer.gamma, Defaults.gamma) || !Mathf.Approximately(layer.mediumRadius, Defaults.mediumRadius) ||
            !Mathf.Approximately(layer.largeRadius, Defaults.largeRadius) || !Mathf.Approximately(layer.fineDetail, Defaults.fineDetail) ||
            !Mathf.Approximately(layer.mediumDetail, Defaults.mediumDetail) || !Mathf.Approximately(layer.largeDetail, Defaults.largeDetail) ||
            !Mathf.Approximately(layer.lightRemoval, Defaults.lightRemoval);
    }
}
