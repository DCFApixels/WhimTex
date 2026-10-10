using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class NoiseLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(NoiseLayerBehaviour);
        protected override bool ImmediateLayerPreviewUpdates => true;
        public static void Open(NoiseLayerBehaviour layer, WhimTexDocument activeDocument) =>
            OpenPropertiesWindow<NoiseLayerEditorWindow>(layer, activeDocument);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (NoiseLayerBehaviour)source, Document, ApplyLayerChange, SettingsBindings);

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

        internal static Vector3 RandomizeScale3D(NoiseLayerBehaviour layer, System.Random random)
        {
            Vector3 axes = layer.Scale3D;
            if (layer.EffectiveDimensions != NoiseLayerBehaviour.NoiseDimensions.ThreeD)
            {
                Vector2 xy = RandomizeScale(layer, random);
                return new Vector3(xy.x, xy.y, axes.z);
            }
            double mean = ((double)axes.x + axes.y + axes.z) / 3;
            double unitX = axes.x / mean, unitY = axes.y / mean, unitZ = axes.z / mean;
            double feasibleMin = .01 / Math.Min(unitX, Math.Min(unitY, unitZ));
            double feasibleMax = 1000 / Math.Max(unitX, Math.Max(unitY, unitZ));
            double meanMin = Math.Max(1, feasibleMin), meanMax = Math.Min(64, feasibleMax);
            if (meanMin > meanMax) { meanMin = feasibleMin; meanMax = feasibleMax; }
            float Sample() => (float)Math.Exp(random.NextDouble() * Math.Log(64));
            while (true)
            {
                Vector3 candidate;
                if (layer.linkScale)
                {
                    double sampledMean = Math.Exp(Math.Log(meanMin) + random.NextDouble() * Math.Log(meanMax / meanMin));
                    candidate = new Vector3((float)(unitX * sampledMean), (float)(unitY * sampledMean), (float)(unitZ * sampledMean));
                }
                else candidate = new Vector3(Sample(), Sample(), Sample());
                double distance = Math.Log((candidate.x + (double)candidate.y + candidate.z) / 24, 2);
                if (random.NextDouble() * 2 < 1 + Math.Exp(-.5 * distance * distance)) return candidate;
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
            bool grain = layer.IsGrain;
            var noiseTypes = (NoiseLayerBehaviour.NoiseType[])Enum.GetValues(typeof(NoiseLayerBehaviour.NoiseType));
            int count = 0;
            foreach (var type in noiseTypes)
                if (IsGrainNoise(type) == grain && NoiseLayerBehaviour.SupportsNoiseType(layer.field, type)) noiseTypes[count++] = type;
            layer.noiseType = noiseTypes[random.Next(count)];
            if (!grain || layer.encoding != NoiseLayerBehaviour.OutputEncoding.Gradient)
                layer.grainColor = Pick<NoiseLayerBehaviour.GrainColor>();
            layer.grainSize = LogRange(1f, 32f);
            layer.Scale3D = RandomizeScale3D(layer, random);
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
            layer.warpSeed = NewSeed(layer.warpSeed);
            layer.warpStrength = LogRange(.05f, 8f);
            float randomWarpScale = LogRange(.25f, 4f);
            if (layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD)
                layer.WarpScale3D = layer.linkWarpScale
                    ? layer.AdjustWarpScale3D(new Vector3(randomWarpScale, layer.WarpScale.y, layer.WarpScale3D.z))
                    : new Vector3(randomWarpScale, LogRange(.25f, 4f), LogRange(.25f, 4f));
            else
                layer.WarpScale = layer.linkWarpScale ? layer.AdjustWarpScale(new Vector2(randomWarpScale, layer.WarpScale.y))
                    : new Vector2(randomWarpScale, LogRange(.25f, 4f));
            if (layer.encoding != NoiseLayerBehaviour.OutputEncoding.Gradient)
                layer.encoding = random.Next(2) == 0
                    ? NoiseLayerBehaviour.OutputEncoding.ColorValues : NoiseLayerBehaviour.OutputEncoding.LinearData;
            layer.inverted = random.Next(2) != 0;
        }

        internal static void BuildFields(VisualElement root, NoiseLayerBehaviour layer, WhimTexDocument activeDocument,
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

            Choice(root, "Field", () => layer.field, value => layer.field = value);
            var allNoise = new System.Collections.Generic.List<NoiseLayerBehaviour.NoiseType>();
            var curlNoise = new System.Collections.Generic.List<NoiseLayerBehaviour.NoiseType>();
            var gradientNoise = new System.Collections.Generic.List<NoiseLayerBehaviour.NoiseType>();
            var cellNoise = new System.Collections.Generic.List<NoiseLayerBehaviour.NoiseType> { NoiseLayerBehaviour.NoiseType.Cellular };
            foreach (NoiseLayerBehaviour.NoiseType type in Enum.GetValues(typeof(NoiseLayerBehaviour.NoiseType)))
            {
                allNoise.Add(type);
                if (NoiseLayerBehaviour.SupportsNoiseType(NoiseLayerBehaviour.Field.Curl, type)) curlNoise.Add(type);
                if (NoiseLayerBehaviour.SupportsNoiseType(NoiseLayerBehaviour.Field.GradientVector, type)) gradientNoise.Add(type);
            }
            System.Collections.Generic.List<NoiseLayerBehaviour.NoiseType> NoiseChoices() => layer.field switch
            {
                NoiseLayerBehaviour.Field.Curl => curlNoise,
                NoiseLayerBehaviour.Field.GradientVector => gradientNoise,
                NoiseLayerBehaviour.Field.CellDirection => cellNoise,
                _ => allNoise
            };
            string NoiseLabel(NoiseLayerBehaviour.NoiseType value) => UnityEditor.ObjectNames.NicifyVariableName(value.ToString());
            var noiseType = WhimTexUI.ConfigureField(new PopupField<NoiseLayerBehaviour.NoiseType>("Noise Type",
                NoiseChoices(), layer.EffectiveNoiseType, NoiseLabel, NoiseLabel));
            bindings.Track(noiseType, () => layer.EffectiveNoiseType);
            noiseType.RegisterValueChangedCallback(evt => applyChange("Change Noise Type", () => layer.noiseType = evt.newValue));
            root.Add(noiseType);
            var grain = new VisualElement();
            Choice(grain, "Color", () => layer.grainColor, value => layer.grainColor = value);
            Number(grain, "Grain Size (px)", () => layer.grainSize, value => layer.grainSize = value, 1f, 1024f);
            root.Add(grain);
            var allDimensions = new System.Collections.Generic.List<string> { "1D", "2D", "3D" };
            var grainDimensions = new System.Collections.Generic.List<string> { "1D", "2D" };
            var vectorDimensions = new System.Collections.Generic.List<string> { "2D", "3D" };
            string DimensionLabel() => layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.OneD ? "1D"
                : layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD ? "3D" : "2D";
            var dimensions = WhimTexUI.ConfigureField(new PopupField<string>("Dimensions",
                layer.IsVectorField ? vectorDimensions : layer.IsGrain ? grainDimensions : allDimensions, DimensionLabel()));
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
            void Seed(VisualElement parent, string label, Func<int> get, Action<int> set)
            {
                var seed = WhimTexUI.ConfigureField(new IntegerField(label));
                bindings.Track(seed, get);
                seed.RegisterValueChangedCallback(evt => applyChange("Change Noise " + label, () => set(evt.newValue)));
                var seedRow = WhimTexUI.CreateRow();
                seed.style.flexGrow = 1f;
                seed.style.flexShrink = 1f;
                seedRow.Add(seed);
                seedRow.Add(WhimTexUI.CreateToolbarButton("Random", () =>
                    applyChange("Randomize Noise " + label, () => set(NewSeed(get()))), 64f));
                parent.Add(seedRow);
            }
            Seed(root, "Seed", () => layer.seed, value => layer.seed = value);
            var scale = new VisualElement();
            var scaleXY = LinkedScale(scale, "Scale", "linkScale", () => layer.Scale, value => layer.Scale = value,
                layer.AdjustScale, () => layer.linkScale, () => layer.linkScale = !layer.linkScale);
            var scale3D = WhimTexUI.ConfigureField(new Vector3Field("Scale") { name = "noiseScale3D" });
            scale3D.AddToClassList("whimtex-linked-vector");
            scale3D.tooltip = "Scale each noise axis. Z scales the distance between 3D slices; Offset Z selects the slice.";
            bindings.Track(scale3D, () => layer.Scale3D);
            scale3D.RegisterValueChangedCallback(evt =>
            {
                var next = layer.AdjustScale3D(evt.newValue);
                applyChange("Change Noise Scale", () => layer.Scale3D = next);
                scale3D.SetValueWithoutNotify(layer.Scale3D);
            });
            ScaleLink(scale3D, "Scale", "linkScale3D", () => layer.linkScale,
                () => layer.linkScale = !layer.linkScale, "X, Y and Z");
            scale.Add(scale3D);
            root.Add(scale);
            Vector2Field LinkedScale(VisualElement parent, string label, string linkName, Func<Vector2> get,
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
                ScaleLink(scaleField, label, linkName, linked, toggle, "X and Y");
                parent.Add(scaleField);
                return scaleField;
            }
            void ScaleLink(VisualElement field, string label, string linkName, Func<bool> linked, Action toggle, string axes)
            {
                var icon = new WhimTexLinkIcon();
                var link = new Button(() => applyChange("Link Noise " + label, toggle)) { name = linkName };
                link.AddToClassList("whimtex-vector-link"); link.Add(icon);
                field.Q(className: "unity-base-field__input").Insert(0, link);
                bindings.Add(() =>
                {
                    icon.SetLinked(linked());
                    link.tooltip = linked() ? "Linked: change " + axes + " proportionally. Click to unlink."
                        : "Unlinked: edit " + axes + " independently. Click to link without changing the values.";
                });
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
            var cellularReturn = Choice(cellular, "Return", () => layer.cellularReturn, value => layer.cellularReturn = value);
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
            Seed(warp, "Warp Seed", () => layer.warpSeed, value => layer.warpSeed = value);
            Number(warp, "Warp Strength", () => layer.warpStrength, value => layer.warpStrength = value, 0f, 100f);
            var warpScaleXY = LinkedScale(warp, "Warp Scale", "linkWarpScale", () => layer.WarpScale, value => layer.WarpScale = value,
                layer.AdjustWarpScale, () => layer.linkWarpScale, () => layer.linkWarpScale = !layer.linkWarpScale);
            var warpScale3D = WhimTexUI.ConfigureField(new Vector3Field("Warp Scale") { name = "noiseWarpScale3D" });
            warpScale3D.AddToClassList("whimtex-linked-vector");
            bindings.Track(warpScale3D, () => layer.WarpScale3D);
            warpScale3D.RegisterValueChangedCallback(evt =>
            {
                var next = layer.AdjustWarpScale3D(evt.newValue);
                applyChange("Change Noise Warp Scale", () => layer.WarpScale3D = next);
                warpScale3D.SetValueWithoutNotify(layer.WarpScale3D);
            });
            ScaleLink(warpScale3D, "Warp Scale", "linkWarpScale3D", () => layer.linkWarpScale,
                () => layer.linkWarpScale = !layer.linkWarpScale, "X, Y and Z");
            warp.Add(warpScale3D);
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
            var vectorSettings = new VisualElement();
            Choice(vectorSettings, "Output", () => layer.vectorOutput, value => layer.vectorOutput = value);
            var normalize = WhimTexUI.ConfigureField(new Toggle("Normalize"));
            normalize.tooltip = "Keep direction but set nonzero vectors to unit length, before Strength. Zero remains zero.";
            bindings.Track(normalize, () => layer.normalize);
            normalize.RegisterValueChangedCallback(evt => applyChange("Normalize Noise Vectors", () => layer.normalize = evt.newValue));
            vectorSettings.Add(normalize);
            Number(vectorSettings, "Strength", () => layer.strength, value => layer.strength = value, 0f, 100f);
            var vectorHint = new HelpBox("Signed Vector and values outside 0–1 require Color Range = HDR. Use Blend Range = HDR when combining data layers; an isolated group also needs HDR.", HelpBoxMessageType.Info);
            vectorSettings.Add(vectorHint);
            root.Add(vectorSettings);
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
                bool showGradient = !layer.IsVectorField && layer.EffectiveOutput == NoiseLayerBehaviour.OutputEncoding.Gradient;
                gradient.EnableInClassList("whimtex-hidden", !showGradient);
            }
            bindings.Add(RefreshGradient);
            RefreshGradient();

            bindings.Add(() =>
            {
                bool isGrain = layer.IsGrain;
                bool isCellDirection = layer.field == NoiseLayerBehaviour.Field.CellDirection;
                var noiseChoices = NoiseChoices();
                if (!ReferenceEquals(noiseType.choices, noiseChoices)) noiseType.choices = noiseChoices;
                noiseType.SetValueWithoutNotify(layer.EffectiveNoiseType);
                noiseType.SetEnabled(!isCellDirection);
                encoding.EnableInClassList("whimtex-hidden", layer.IsVectorField);
                vectorSettings.EnableInClassList("whimtex-hidden", !layer.IsVectorField);
                inverted.tooltip = layer.IsVectorField ? "Reverse the vector direction." : "Invert the value before color or gradient output.";
                grain.EnableInClassList("whimtex-hidden", !isGrain);
                var dimensionChoices = layer.IsVectorField ? vectorDimensions : isGrain ? grainDimensions : allDimensions;
                if (!ReferenceEquals(dimensions.choices, dimensionChoices)) dimensions.choices = dimensionChoices;
                dimensions.SetValueWithoutNotify(DimensionLabel());
                bool three = layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                scaleXY.EnableInClassList("whimtex-hidden", three);
                scale3D.EnableInClassList("whimtex-hidden", !three);
                warpScaleXY.EnableInClassList("whimtex-hidden", three);
                warpScale3D.EnableInClassList("whimtex-hidden", !three);
                offset.EnableInClassList("whimtex-hidden", three);
                offset3D.EnableInClassList("whimtex-hidden", !three);
                periodic.EnableInClassList("whimtex-hidden", isGrain || layer.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.OneD);
                periodic1D.EnableInClassList("whimtex-hidden", isGrain || layer.EffectiveDimensions != NoiseLayerBehaviour.NoiseDimensions.OneD);
                scale.EnableInClassList("whimtex-hidden", isGrain);
                fractalChoice.EnableInClassList("whimtex-hidden", isGrain || isCellDirection);
                warpChoice.EnableInClassList("whimtex-hidden", isGrain);
                offset.tooltip = isGrain ? "Move the grain in canvas pixels."
                    : "Move the noise in noise-space units.";
                axis.EnableInClassList("whimtex-hidden", layer.EffectiveDimensions != NoiseLayerBehaviour.NoiseDimensions.OneD);
                cellular.EnableInClassList("whimtex-hidden", layer.EffectiveNoiseType != NoiseLayerBehaviour.NoiseType.Cellular);
                cellularReturn.EnableInClassList("whimtex-hidden", isCellDirection);
                fractal.EnableInClassList("whimtex-hidden", isGrain || layer.EffectiveFractal == NoiseLayerBehaviour.FractalType.None);
                pingPong.EnableInClassList("whimtex-hidden", layer.fractal != NoiseLayerBehaviour.FractalType.PingPong);
                warp.EnableInClassList("whimtex-hidden", isGrain || layer.warp == NoiseLayerBehaviour.WarpType.None);
            });
        }
    }
}
