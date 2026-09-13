using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed class NoiseLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(NoiseLayerBehaviour);
        protected override bool ImmediatePreviewUpdates => true;
        public static void Open(NoiseLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<NoiseLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (NoiseLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);

        internal static void BuildFields(VisualElement root, NoiseLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, SpriteEditorUI.ValueBindings bindings)
        {

            EnumField Choice<T>(VisualElement parent, string label, Func<T> get, Action<T> set) where T : struct, Enum
            {
                var field = SpriteEditorUI.ConfigureField(new EnumField(label, (Enum)(object)get()));
                bindings.Track(field, () => (Enum)(object)get());
                field.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label, () => set((T)(object)evt.newValue)));
                parent.Add(field);
                return field;
            }
            void Number(VisualElement parent, string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = SpriteEditorUI.ConfigureField(new FloatField(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label,
                    () => set(NoiseLayerBehaviour.Limit(evt.newValue, min, max, get()))));
                parent.Add(field);
            }
            void Slider(VisualElement parent, string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = SpriteEditorUI.ConfigureField(new Slider(label, min, max) { showInputField = true });
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label,
                    () => set(NoiseLayerBehaviour.Limit(evt.newValue, min, max, get()))));
                parent.Add(field);
            }

            Choice(root, "Noise Type", () => layer.noiseType, value => layer.noiseType = value);
            var white = new VisualElement();
            Choice(white, "Color", () => layer.whiteNoiseColor, value => layer.whiteNoiseColor = value);
            Number(white, "Grain Size (px)", () => layer.whiteNoiseSize, value => layer.whiteNoiseSize = value, 1f, 1024f);
            root.Add(white);
            var dimensions = SpriteEditorUI.ConfigureField(new PopupField<string>("Dimensions",
                new System.Collections.Generic.List<string> { "2D", "1D" }, layer.dimensions == NoiseLayerBehaviour.NoiseDimensions.OneD ? 1 : 0));
            bindings.Track(dimensions, () => layer.dimensions == NoiseLayerBehaviour.NoiseDimensions.OneD ? "1D" : "2D");
            dimensions.RegisterValueChangedCallback(evt => applyChange("Change Noise Dimensions",
                () => layer.dimensions = evt.newValue == "1D" ? NoiseLayerBehaviour.NoiseDimensions.OneD : NoiseLayerBehaviour.NoiseDimensions.TwoD));
            root.Add(dimensions);
            var axis = new VisualElement();
            axis.tooltip = "Direction of variation. 0: vertical stripes; 90: horizontal stripes. Positive angles turn counterclockwise.";
            Slider(axis, "Direction (deg)", () => layer.direction, value => layer.direction = value, -180f, 180f);
            root.Add(axis);
            var seed = SpriteEditorUI.ConfigureField(new IntegerField("Seed"));
            bindings.Track(seed, () => layer.seed);
            seed.RegisterValueChangedCallback(evt => applyChange("Change Noise Seed", () => layer.seed = evt.newValue));
            root.Add(seed);
            var scale = new VisualElement();
            Number(scale, "Scale", () => layer.scale, value => layer.scale = value, .01f, 1000f);
            root.Add(scale);
            var offset = SpriteEditorUI.ConfigureField(new Vector2Field("Offset"));
            bindings.Track(offset, () => layer.offset);
            offset.RegisterValueChangedCallback(evt => applyChange("Change Noise Offset", () => layer.offset = new Vector2(
                NoiseLayerBehaviour.Limit(evt.newValue.x, -10000f, 10000f, layer.offset.x),
                NoiseLayerBehaviour.Limit(evt.newValue.y, -10000f, 10000f, layer.offset.y))));
            root.Add(offset);

            var cellular = new VisualElement();
            Choice(cellular, "Distance", () => layer.cellularDistance, value => layer.cellularDistance = value);
            Choice(cellular, "Return", () => layer.cellularReturn, value => layer.cellularReturn = value);
            Slider(cellular, "Jitter", () => layer.cellularJitter, value => layer.cellularJitter = value, 0f, 1f);
            root.Add(cellular);

            var fractalChoice = Choice(root, "Fractal", () => layer.fractal, value => layer.fractal = value);
            var fractal = new VisualElement();
            var octaves = SpriteEditorUI.ConfigureField(new SliderInt("Octaves", 1, 8) { showInputField = true });
            bindings.Track(octaves, () => layer.octaves);
            octaves.RegisterValueChangedCallback(evt => applyChange("Change Noise Octaves", () => layer.octaves = Mathf.Clamp(evt.newValue, 1, 8)));
            fractal.Add(octaves);
            Number(fractal, "Lacunarity", () => layer.lacunarity, value => layer.lacunarity = value, 1f, 4f);
            Slider(fractal, "Gain", () => layer.gain, value => layer.gain = value, 0f, 1f);
            Slider(fractal, "Weighted Strength", () => layer.weightedStrength, value => layer.weightedStrength = value, 0f, 1f);
            var pingPong = new VisualElement();
            Number(pingPong, "Ping Pong Strength", () => layer.pingPongStrength, value => layer.pingPongStrength = value, .01f, 8f);
            fractal.Add(pingPong);
            root.Add(fractal);

            var warpChoice = Choice(root, "Domain Warp", () => layer.warp, value => layer.warp = value);
            var warp = new VisualElement();
            Number(warp, "Warp Strength", () => layer.warpStrength, value => layer.warpStrength = value, 0f, 100f);
            root.Add(warp);
            var encoding = Choice(root, "Output", () => layer.encoding, value => layer.encoding = value);
            encoding.tooltip = "Color Values: display colors. Linear Data: raw 0–1 values for masks, height maps and channel packing.";
            var inverted = SpriteEditorUI.ConfigureField(new Toggle("Inverted"));
            bindings.Track(inverted, () => layer.inverted);
            inverted.RegisterValueChangedCallback(evt => applyChange("Invert Noise", () => layer.inverted = evt.newValue));
            root.Add(inverted);

            bindings.Add(() =>
            {
                bool isWhite = layer.noiseType == NoiseLayerBehaviour.NoiseType.WhiteNoise
                    || layer.noiseType == NoiseLayerBehaviour.NoiseType.BlueNoise;
                white.EnableInClassList("sprite-editor-hidden", !isWhite);
                scale.EnableInClassList("sprite-editor-hidden", isWhite);
                fractalChoice.EnableInClassList("sprite-editor-hidden", isWhite);
                warpChoice.EnableInClassList("sprite-editor-hidden", isWhite);
                offset.tooltip = isWhite ? "Move the grain in canvas pixels."
                    : "Noise-space offset. Scale is measured across the shorter canvas side; preview resolution does not change the pattern.";
                axis.EnableInClassList("sprite-editor-hidden", layer.dimensions != NoiseLayerBehaviour.NoiseDimensions.OneD);
                cellular.EnableInClassList("sprite-editor-hidden", layer.noiseType != NoiseLayerBehaviour.NoiseType.Cellular);
                fractal.EnableInClassList("sprite-editor-hidden", isWhite || layer.fractal == NoiseLayerBehaviour.FractalType.None);
                pingPong.EnableInClassList("sprite-editor-hidden", layer.fractal != NoiseLayerBehaviour.FractalType.PingPong);
                warp.EnableInClassList("sprite-editor-hidden", isWhite || layer.warp == NoiseLayerBehaviour.WarpType.None);
            });
        }
    }
}
