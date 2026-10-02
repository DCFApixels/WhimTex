using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class NoiseLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(NoiseLayerBehaviour);
        protected override bool ImmediatePreviewUpdates => true;
        public static void Open(NoiseLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<NoiseLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (NoiseLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);

        private static int NewSeed(int previous)
        {
            int value;
            do { value = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0); }
            while (value == previous);
            return value;
        }

        private static bool IsGrainNoise(NoiseLayerBehaviour.NoiseType type) =>
            type == NoiseLayerBehaviour.NoiseType.WhiteNoise || type == NoiseLayerBehaviour.NoiseType.BlueNoise;

        internal static Vector2 RandomizeScale(NoiseLayerBehaviour layer, System.Random random)
        {
            float Sample() => (float)Math.Exp(random.NextDouble() * Math.Log(64));
            Vector2 axes = layer.Scale;
            double mean = ((double)axes.x + axes.y) * .5;
            double unitX = axes.x / mean, unitY = axes.y / mean;
            double feasibleMin = .01 / Math.Min(unitX, unitY);
            double feasibleMax = 1000 / Math.Max(unitX, unitY);
            double meanMin = Math.Max(1, feasibleMin), meanMax = Math.Min(64, feasibleMax);
            if (meanMin > meanMax) { meanMin = feasibleMin; meanMax = feasibleMax; }
            while (true)
            {
                Vector2 candidate;
                if (layer.linkScale)
                {
                    double sampledMean = Math.Exp(Math.Log(meanMin) + random.NextDouble() * Math.Log(meanMax / meanMin));
                    candidate = new Vector2((float)(unitX * sampledMean), (float)(unitY * sampledMean));
                }
                else candidate = new Vector2(Sample(), Sample());
                double distance = Math.Log((candidate.x + candidate.y) / 16.0, 2);
                double weight = 1 + Math.Exp(-.5 * distance * distance);
                if (random.NextDouble() * 2 < weight) return candidate;
            }
        }

        internal static void RandomizeParameters(NoiseLayerBehaviour layer)
        {
            int seed = NewSeed(layer.seed);
            var random = new System.Random(seed);
            float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            float LogRange(float min, float max) => Mathf.Exp(Range(Mathf.Log(min), Mathf.Log(max)));
            T Pick<T>() where T : struct, Enum
            {
                var values = Enum.GetValues(typeof(T));
                return (T)values.GetValue(random.Next(values.Length));
            }

            layer.seed = seed;
            bool grain = IsGrainNoise(layer.noiseType);
            var noiseTypes = (NoiseLayerBehaviour.NoiseType[])Enum.GetValues(typeof(NoiseLayerBehaviour.NoiseType));
            int count = 0;
            foreach (var type in noiseTypes)
                if (IsGrainNoise(type) == grain) noiseTypes[count++] = type;
            layer.noiseType = noiseTypes[random.Next(count)];
            if (!grain || layer.encoding != NoiseLayerBehaviour.OutputEncoding.Gradient)
                layer.whiteNoiseColor = Pick<NoiseLayerBehaviour.WhiteNoiseColor>();
            layer.whiteNoiseSize = LogRange(1f, 32f);
            layer.Scale = RandomizeScale(layer, random);
            layer.fractal = Pick<NoiseLayerBehaviour.FractalType>();
            layer.octaves = random.Next(1, 9);
            layer.lacunarity = Range(1f, 4f);
            layer.gain = Range(.15f, .85f);
            layer.weightedStrength = Range(0f, 1f);
            layer.pingPongStrength = Range(.5f, 4f);
            layer.cellularDistance = Pick<NoiseLayerBehaviour.CellularDistance>();
            layer.cellularReturn = Pick<NoiseLayerBehaviour.CellularReturn>();
            layer.cellularJitter = Range(0f, 1f);
            layer.warp = Pick<NoiseLayerBehaviour.WarpType>();
            layer.warpStrength = LogRange(.05f, 8f);
            float randomWarpScale = LogRange(.25f, 4f);
            layer.WarpScale = layer.linkWarpScale ? layer.AdjustWarpScale(new Vector2(randomWarpScale, layer.WarpScale.y))
                : new Vector2(randomWarpScale, LogRange(.25f, 4f));
            if (layer.encoding != NoiseLayerBehaviour.OutputEncoding.Gradient)
                layer.encoding = random.Next(2) == 0
                    ? NoiseLayerBehaviour.OutputEncoding.ColorValues : NoiseLayerBehaviour.OutputEncoding.LinearData;
            layer.inverted = random.Next(2) != 0;
        }

        internal static void BuildFields(VisualElement root, NoiseLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {
            var randomAll = WhimTexUI.CreateToolbarButton("Random All", () =>
                applyChange("Randomize Noise Parameters", () => RandomizeParameters(layer)));
            randomAll.name = "whimtex-noise-random-all";
            randomAll.tooltip = "Randomize noise settings within the current group. Keeps the gradient and tiling setup.";
            root.Add(randomAll);

            EnumField Choice<T>(VisualElement parent, string label, Func<T> get, Action<T> set) where T : struct, Enum
            {
                var field = WhimTexUI.ConfigureField(new EnumField(label, (Enum)(object)get()));
                bindings.Track(field, () => (Enum)(object)get());
                field.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label, () => set((T)(object)evt.newValue)));
                parent.Add(field);
                return field;
            }
            void Number(VisualElement parent, string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = WhimTexUI.ConfigureField(new FloatField(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label,
                    () => set(NoiseLayerBehaviour.Limit(evt.newValue, min, max, get()))));
                parent.Add(field);
            }
            void Slider(VisualElement parent, string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = WhimTexUI.ConfigureField(new Slider(label, min, max) { showInputField = true });
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
            var allDimensions = new System.Collections.Generic.List<string> { "1D", "2D", "3D" };
            var grainDimensions = new System.Collections.Generic.List<string> { "1D", "2D" };
            string DimensionLabel() => layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.OneD ? "1D"
                : layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD ? "3D" : "2D";
            var dimensions = WhimTexUI.ConfigureField(new PopupField<string>("Dimensions",
                layer.IsGrain ? grainDimensions : allDimensions, DimensionLabel()));
            bindings.Track(dimensions, DimensionLabel);
            dimensions.RegisterValueChangedCallback(evt => applyChange("Change Noise Dimensions",
                () => layer.dimensions = evt.newValue == "1D" ? NoiseLayerBehaviour.NoiseDimensions.OneD
                    : evt.newValue == "3D" ? NoiseLayerBehaviour.NoiseDimensions.ThreeD : NoiseLayerBehaviour.NoiseDimensions.TwoD));
            root.Add(dimensions);
            var periodic1D = WhimTexUI.ConfigureField(new Toggle("Seamless") { name = "periodic1D" });
            periodic1D.tooltip = "Repeat along the noise axis. Angled stripes do not necessarily tile at the canvas edges.";
            bindings.Track(periodic1D, () => layer.periodic1D);
            periodic1D.RegisterValueChangedCallback(evt => applyChange("Change 1D Noise Seamless", () => layer.periodic1D = evt.newValue));
            root.Add(periodic1D);
            var periodic = new VisualElement { name = "periodic" };
            periodic.tooltip = "Repeat the native noise lattice on X, Y or both axes. Applies to every fractal octave and Domain Warp. Z is never repeated. Periods fit the layer's source rectangle; transforms and FX can change canvas seams.";
            var periodicHeading = new Label("Seamless");
            periodicHeading.AddToClassList("whimtex-seamless-heading");
            periodic.Add(periodicHeading);
            var periodicEdges = new WhimTexEdgeSelector("periodicEdges", () =>
                applyChange("Invert Noise Seamless", () => layer.periodic ^= NoiseLayerBehaviour.PeriodicAxes.XY));
            periodic.Add(periodicEdges);
            root.Add(periodic);
            PeriodicEdge("top", NoiseLayerBehaviour.PeriodicAxes.Y);
            PeriodicEdge("bottom", NoiseLayerBehaviour.PeriodicAxes.Y);
            PeriodicEdge("left", NoiseLayerBehaviour.PeriodicAxes.X);
            PeriodicEdge("right", NoiseLayerBehaviour.PeriodicAxes.X);
            void PeriodicEdge(string edge, NoiseLayerBehaviour.PeriodicAxes pair)
            {
                var button = new Button(() => applyChange("Change Noise Seamless", () => layer.periodic ^= pair))
                {
                    name = "periodic-" + edge,
                    tooltip = pair == NoiseLayerBehaviour.PeriodicAxes.Y
                        ? "Toggle Top & Bottom together (Y). Clear both pairs to disable periodicity."
                        : "Toggle Left & Right together (X). Clear both pairs to disable periodicity."
                };
                button.AddToClassList("whimtex-seamless-edge");
                button.AddToClassList("whimtex-seamless-edge--" + edge);
                void Refresh() => button.EnableInClassList("whimtex-seamless-edge--selected", (layer.periodic & pair) != 0);
                Refresh();
                bindings.Add(Refresh);
                periodicEdges.AddEdge(button, pair.ToString());
            }
            var axis = new VisualElement();
            axis.tooltip = "Direction of variation. 0: vertical stripes; 90: horizontal stripes. Positive angles turn counterclockwise.";
            Slider(axis, "Direction (deg)", () => layer.direction, value => layer.direction = value, -180f, 180f);
            root.Add(axis);
            var seed = WhimTexUI.ConfigureField(new IntegerField("Seed"));
            bindings.Track(seed, () => layer.seed);
            seed.RegisterValueChangedCallback(evt => applyChange("Change Noise Seed", () => layer.seed = evt.newValue));
            var seedRow = WhimTexUI.CreateRow();
            seed.style.flexGrow = 1f;
            seed.style.flexShrink = 1f;
            seedRow.Add(seed);
            seedRow.Add(WhimTexUI.CreateToolbarButton("Random", () =>
            {
                int randomSeed = NewSeed(layer.seed);
                applyChange("Randomize Noise Seed", () => layer.seed = randomSeed);
            }, 64f));
            root.Add(seedRow);
            var scale = new VisualElement();
            LinkedScale(scale, "Scale", "linkScale", () => layer.Scale, value => layer.Scale = value,
                layer.AdjustScale, () => layer.linkScale, () => layer.linkScale = !layer.linkScale);
            root.Add(scale);
            void LinkedScale(VisualElement parent, string label, string linkName, Func<Vector2> get,
                Action<Vector2> set, Func<Vector2, Vector2> adjust, Func<bool> linked, Action toggle)
            {
                var scaleField = WhimTexUI.ConfigureField(new Vector2Field(label));
                scaleField.AddToClassList("whimtex-linked-vector");
                bindings.Track(scaleField, get);
                scaleField.RegisterValueChangedCallback(e =>
                {
                    var next = adjust(e.newValue);
                    applyChange("Change Noise " + label, () => set(next));
                    scaleField.SetValueWithoutNotify(get());
                });
                var icon = new WhimTexLinkIcon();
                var link = new Button(() => applyChange("Link Noise " + label, toggle)) { name = linkName };
                link.AddToClassList("whimtex-vector-link"); link.Add(icon);
                scaleField.Q(className: "unity-base-field__input").Insert(0, link);
                bindings.Add(() =>
                {
                    icon.SetLinked(linked());
                    link.tooltip = linked() ? "Linked: change X and Y proportionally. Click to unlink."
                        : "Unlinked: edit X and Y independently. Click to link without changing the values.";
                });
                parent.Add(scaleField);
            }
            var offset = WhimTexUI.ConfigureField(new Vector2Field("Offset"));
            bindings.Track(offset, () => (Vector2)layer.offset);
            offset.RegisterValueChangedCallback(evt => applyChange("Change Noise Offset", () => layer.offset = new Vector3(
                NoiseLayerBehaviour.Limit(evt.newValue.x, -10000f, 10000f, layer.offset.x),
                NoiseLayerBehaviour.Limit(evt.newValue.y, -10000f, 10000f, layer.offset.y), layer.offset.z)));
            root.Add(offset);
            var offset3D = WhimTexUI.ConfigureField(new Vector3Field("Offset"));
            bindings.Track(offset3D, () => layer.offset);
            offset3D.RegisterValueChangedCallback(evt => applyChange("Change Noise Offset", () => layer.offset = new Vector3(
                NoiseLayerBehaviour.Limit(evt.newValue.x, -10000f, 10000f, layer.offset.x),
                NoiseLayerBehaviour.Limit(evt.newValue.y, -10000f, 10000f, layer.offset.y),
                NoiseLayerBehaviour.Limit(evt.newValue.z, -10000f, 10000f, layer.offset.z))));
            offset3D.tooltip = "Move the noise. Z selects the 3D slice.";
            root.Add(offset3D);

            var cellular = new VisualElement();
            Choice(cellular, "Distance", () => layer.cellularDistance, value => layer.cellularDistance = value);
            Choice(cellular, "Return", () => layer.cellularReturn, value => layer.cellularReturn = value);
            Slider(cellular, "Jitter", () => layer.cellularJitter, value => layer.cellularJitter = value, 0f, 1f);
            root.Add(cellular);

            var fractalChoice = Choice(root, "Fractal", () => layer.fractal, value => layer.fractal = value);
            var fractal = new VisualElement();
            var octaves = WhimTexUI.ConfigureField(new SliderInt("Octaves", 1, 8) { showInputField = true });
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
            LinkedScale(warp, "Warp Scale", "linkWarpScale", () => layer.WarpScale, value => layer.WarpScale = value,
                layer.AdjustWarpScale, () => layer.linkWarpScale, () => layer.linkWarpScale = !layer.linkWarpScale);
            root.Add(warp);
            var scalarOutputs = new System.Collections.Generic.List<NoiseLayerBehaviour.OutputEncoding>
                { NoiseLayerBehaviour.OutputEncoding.ColorValues, NoiseLayerBehaviour.OutputEncoding.LinearData, NoiseLayerBehaviour.OutputEncoding.Gradient };
            var colorOutputs = new System.Collections.Generic.List<NoiseLayerBehaviour.OutputEncoding>
                { NoiseLayerBehaviour.OutputEncoding.ColorValues, NoiseLayerBehaviour.OutputEncoding.LinearData };
            string OutputLabel(NoiseLayerBehaviour.OutputEncoding value) => UnityEditor.ObjectNames.NicifyVariableName(value.ToString());
            var encoding = WhimTexUI.ConfigureField(new PopupField<NoiseLayerBehaviour.OutputEncoding>("Output",
                layer.SupportsGradient ? scalarOutputs : colorOutputs, layer.EffectiveOutput, OutputLabel, OutputLabel));
            bindings.Track(encoding, () => layer.EffectiveOutput);
            encoding.RegisterValueChangedCallback(evt => applyChange("Change Noise Output", () => layer.encoding = evt.newValue));
            encoding.tooltip = "Color Values displays encoded colors; Linear Data keeps raw 0–1 values; Gradient maps monochrome noise through a color palette. Color White/Blue Noise supports Color Values and Linear Data only.";
            root.Add(encoding);
            var inverted = WhimTexUI.ConfigureField(new Toggle("Inverted"));
            bindings.Track(inverted, () => layer.inverted);
            inverted.RegisterValueChangedCallback(evt => applyChange("Invert Noise", () => layer.inverted = evt.newValue));
            root.Add(inverted);

            var gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient"), bindings, () => layer.gradient));
            gradient.RegisterValueChangedCallback(evt =>
                applyChange("Change Noise Gradient", () => layer.gradient = GradientUtility.Create(evt.newValue)));
            root.Add(gradient);
            void RefreshGradient()
            {
                var choices = layer.SupportsGradient ? scalarOutputs : colorOutputs;
                if (!ReferenceEquals(encoding.choices, choices)) encoding.choices = choices;
                encoding.SetValueWithoutNotify(layer.EffectiveOutput);
                bool showGradient = layer.EffectiveOutput == NoiseLayerBehaviour.OutputEncoding.Gradient;
                gradient.EnableInClassList("whimtex-hidden", !showGradient);
            }
            bindings.Add(RefreshGradient);
            RefreshGradient();

            bindings.Add(() =>
            {
                bool isWhite = layer.noiseType == NoiseLayerBehaviour.NoiseType.WhiteNoise
                    || layer.noiseType == NoiseLayerBehaviour.NoiseType.BlueNoise;
                white.EnableInClassList("whimtex-hidden", !isWhite);
                var dimensionChoices = isWhite ? grainDimensions : allDimensions;
                if (!ReferenceEquals(dimensions.choices, dimensionChoices)) dimensions.choices = dimensionChoices;
                dimensions.SetValueWithoutNotify(DimensionLabel());
                bool three = layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                offset.EnableInClassList("whimtex-hidden", three);
                offset3D.EnableInClassList("whimtex-hidden", !three);
                periodic.EnableInClassList("whimtex-hidden", isWhite || layer.dimensions == NoiseLayerBehaviour.NoiseDimensions.OneD);
                periodic1D.EnableInClassList("whimtex-hidden", isWhite || layer.dimensions != NoiseLayerBehaviour.NoiseDimensions.OneD);
                scale.EnableInClassList("whimtex-hidden", isWhite);
                fractalChoice.EnableInClassList("whimtex-hidden", isWhite);
                warpChoice.EnableInClassList("whimtex-hidden", isWhite);
                offset.tooltip = isWhite ? "Move the grain in canvas pixels."
                    : "Move the noise in noise-space units.";
                axis.EnableInClassList("whimtex-hidden", layer.dimensions != NoiseLayerBehaviour.NoiseDimensions.OneD);
                cellular.EnableInClassList("whimtex-hidden", layer.noiseType != NoiseLayerBehaviour.NoiseType.Cellular);
                fractal.EnableInClassList("whimtex-hidden", isWhite || layer.fractal == NoiseLayerBehaviour.FractalType.None);
                pingPong.EnableInClassList("whimtex-hidden", layer.fractal != NoiseLayerBehaviour.FractalType.PingPong);
                warp.EnableInClassList("whimtex-hidden", isWhite || layer.warp == NoiseLayerBehaviour.WarpType.None);
            });
        }
    }
}
