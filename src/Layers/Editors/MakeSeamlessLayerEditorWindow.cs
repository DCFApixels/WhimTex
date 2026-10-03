using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class MakeSeamlessLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(MakeSeamlessLayerBehaviour);
        protected override string LayerPreviewTitle => "Layer Preview (Make Seamless)";
        public static void Open(MakeSeamlessLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<MakeSeamlessLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (MakeSeamlessLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);

        internal static void BuildFields(VisualElement root, MakeSeamlessLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);
            var mode = WhimTexUI.ConfigureField(new PopupField<MakeSeamlessLayerBehaviour.SeamlessMode>("Method",
                new List<MakeSeamlessLayerBehaviour.SeamlessMode>
                {
                    MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend,
                    MakeSeamlessLayerBehaviour.SeamlessMode.Mirror,
                    MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson,
                    MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting
                }, layer.EffectiveMode, MethodLabel, MethodLabel) { name = "seamlessMethod" });
            bindings.Track(mode, () => layer.EffectiveMode);
            mode.RegisterValueChangedCallback(evt => applyChange("Change Seamless Method",
                () => layer.mode = evt.newValue));
            root.Add(mode);
            string MethodLabel(MakeSeamlessLayerBehaviour.SeamlessMode value) => value switch
            {
                MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend => "Offset Blend",
                MakeSeamlessLayerBehaviour.SeamlessMode.Mirror => "Mirror",
                MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson => "Screened Poisson",
                MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting => "Patch Quilting",
                _ => value.ToString()
            };
            var channels = new VisualElement { name = "seamlessChannels",
                tooltip = "Process selected RGBA channels only. Unchecked channels keep source values; all off bypasses seam processing. Applied before layer FX and swizzle." };
            channels.AddToClassList("unity-base-field");
            channels.AddToClassList("whimtex-seamless-channels");
            var channelLabel = new Label("Channels");
            channelLabel.AddToClassList("unity-base-field__label");
            channels.Add(channelLabel);
            WhimTexUI.ConfigureField(channels);
            AddChannel("R", () => layer.processRed, value => layer.processRed = value);
            AddChannel("G", () => layer.processGreen, value => layer.processGreen = value);
            AddChannel("B", () => layer.processBlue, value => layer.processBlue = value);
            AddChannel("A", () => layer.processAlpha, value => layer.processAlpha = value);
            root.Add(channels);
            void AddChannel(string name, Func<bool> get, Action<bool> set)
            {
                var toggle = new Toggle { text = name, name = "seamlessChannel" + name };
                toggle.SetValueWithoutNotify(get());
                bindings.Track(toggle, get);
                toggle.RegisterValueChangedCallback(evt => applyChange("Change Seamless Channels", () => set(evt.newValue)));
                channels.Add(toggle);
            }
            var mirror = new VisualElement();
            root.Add(mirror);
            var processing = new VisualElement();
            root.Add(processing);
            var blendEdges = LabeledEdges("Copy Edges", BuildProcessingEdges(layer, applyChange, bindings));
            processing.Add(blendEdges);
            var poissonEdges = Direction("poissonEdges", () => layer.poissonEdges, value => layer.poissonEdges = value);
            processing.Add(poissonEdges);
            var radius = Percentage("Radius (%)", .5f, 25f, () => layer.screeningRadius,
                value => layer.screeningRadius = value, "How far correction spreads, relative to the smaller canvas dimension.");
            radius.name = "screenedSolveRadius";
            var band = Percentage("Blend Width (%)", 2f, 50f, () => layer.edgeWidth,
                value => layer.edgeWidth = value, "Width of the shifted-copy bands (minimum two pixels). Does not limit Poisson Correction.");
            band.name = "seamlessEdgeWidth";
            processing.Add(radius); processing.Add(band);
            var offsetOptions = new VisualElement { name = "offsetOptions" };
            processing.Add(offsetOptions);
            var transitionStart = Percentage("Transition Start (%)", -100f, 95f, () => layer.offsetTransitionStart,
                value => layer.offsetTransitionStart = value,
                "Position within Blend Width where the copied strip starts fading into the source. 0: fade from the edge. Negative values start outside the canvas and may expose a seam; use Poisson Correction if needed. Higher values narrow the fade.");
            transitionStart.name = "offsetTransitionStart";
            offsetOptions.Add(transitionStart);
            var offsetCompensate = ToggleField("Contrast Compensation", "offsetContrastCompensation",
                () => layer.offsetContrastCompensation, value => layer.offsetContrastCompensation = value,
                "Reduce contrast loss where shifted and original pixels mix. Colors can change.");
            offsetOptions.Add(offsetCompensate);
            var contrast = Percentage("Strength (%)", 0f, 100f, () => layer.histogramContrast,
                value => layer.histogramContrast = value, "0: ordinary blending. 100: full histogram contrast compensation.");
            contrast.name = "offsetCompensation";
            offsetOptions.Add(contrast);
            var offsetCorrect = ToggleField("Poisson Correction", "offsetSeamCorrection",
                () => layer.offsetSeamCorrection, value => layer.offsetSeamCorrection = value,
                "Global Screened Poisson pass, independent of the copy edges. Can change the interior and other edges; may exceed the color range.");
            offsetOptions.Add(offsetCorrect);
            var offsetEdges = Direction("offsetPoissonEdges", () => layer.offsetPoissonEdges, value => layer.offsetPoissonEdges = value);
            offsetOptions.Add(offsetEdges);
            var offsetAuto = ToggleField("Automatic Radius", "offsetAutoRadius",
                () => layer.offsetAutoRadius, value => layer.offsetAutoRadius = value,
                "Use one quarter of Blend Width as the radius (minimum 0.5% of the smaller dimension).");
            offsetOptions.Add(offsetAuto);
            var offsetRadius = Percentage("Radius (%)", .5f, 25f,
                () => layer.offsetAutoRadius ? Mathf.Max(.005f,layer.edgeWidth*.25f) : layer.offsetCorrectionRadius,
                value => layer.offsetCorrectionRadius = value,
                "Correction distance relative to the smaller dimension; not confined to Blend Width.");
            offsetRadius.name = "offsetCorrectionRadius";
            offsetOptions.Add(offsetRadius);
            var quilting = new VisualElement { name = "quiltingOptions" };
            root.Add(quilting);
            quilting.Add(Direction("quiltingEdges", () => layer.quiltingEdges, value => layer.quiltingEdges = value, "Patch Edges"));
            quilting.Add(QuiltingSlider("Patch Width (%)", "quiltingWidth", 2, 45,
                () => layer.quiltingWidth*100, value => layer.quiltingWidth=value/100,
                "Width of each repaired edge band. The center outside the bands is preserved unless Poisson Correction is enabled. Updates when dragging ends."));
            quilting.Add(QuiltingSlider("Feather (%)", "quiltingFeather", 0, 100,
                () => layer.quiltingFeather, value => layer.quiltingFeather=value,
                "Share of the available transition width, separately for each cut. 0: hard cut. 100: widest safe transition within the band. Blends patches; does not blur texture details. Pixel-sized bands may have no room for smoothing. Updates when dragging ends."));
            quilting.Add(ToggleField("Contrast Compensation", "quiltingContrastCompensation",
                () => layer.quiltingContrastCompensation, value => layer.quiltingContrastCompensation=value,
                "Reduce contrast loss in Feather transitions using histogram matching and the selected donor's correlation. Colors can change. Adds work only when enabled with nonzero Feather and compensation."));
            var quiltContrast=QuiltingSlider("Strength (%)", "quiltingContrast", 0, 100,
                () => layer.quiltingContrast*100, value => layer.quiltingContrast=value/100,
                "0: ordinary Feather blending. 100: full contrast compensation in mixed pixels. Pure copied/source pixels stay unchanged in each pass.");
            quilting.Add(quiltContrast);
            var quality = WhimTexUI.ConfigureField(new EnumField("Search Quality", layer.quiltingQuality)
                { name="quiltingQuality", tooltip="Higher quality tests more candidates at a finer resolution. Costs more time; does not guarantee a better match." });
            bindings.Track(quality, () => (Enum)layer.quiltingQuality);
            quality.RegisterValueChangedCallback(evt => applyChange("Change Quilting Quality", () => layer.quiltingQuality=(MakeSeamlessLayerBehaviour.QuiltingQuality)evt.newValue));
            quilting.Add(quality);
            quilting.Add(QuiltingSlider("Along-Seam Search (%)", "quiltingAlongSearch", 0, 25,
                () => layer.quiltingAlongSearch*100, value => layer.quiltingAlongSearch=value/100,
                "Search farther along the seam, relative to usable strip length. 0: no along-seam shift. Higher values add variety but may stretch details. Shifts fade out at strip ends."));
            var matching = WhimTexUI.ConfigureField(new EnumField("Channel Matching", layer.quiltingChannels)
                { name="quiltingChannels", tooltip="Linked: one donor and cut for selected channels, premultiplied color. Independent: separate straight-value cuts for packed data; not suitable for preserving color relationships." });
            bindings.Track(matching, () => (Enum)layer.quiltingChannels);
            matching.RegisterValueChangedCallback(evt => applyChange("Change Quilting Channel Matching", () => layer.quiltingChannels=(MakeSeamlessLayerBehaviour.QuiltingChannels)evt.newValue));
            quilting.Add(matching);
            var seed = WhimTexUI.ConfigureField(new IntegerField("Seed") { name="quiltingSeed", isDelayed=true });
            seed.SetValueWithoutNotify(layer.quiltingSeed);
            bindings.Track(seed, () => layer.quiltingSeed);
            seed.RegisterValueChangedCallback(evt => applyChange("Change Quilting Seed", () => layer.quiltingSeed=evt.newValue));
            var seedRow=WhimTexUI.CreateRow(); seedRow.Add(seed);
            seedRow.AddToClassList("whimtex-quilting-seed-row");
            var randomSeed = WhimTexUI.CreateToolbarButton("Random", () => applyChange("Randomize Quilting Seed",
                () => layer.quiltingSeed=unchecked(layer.quiltingSeed+(Guid.NewGuid().GetHashCode() | 1))),64f);
            randomSeed.name="quiltingRandomSeed";
            seedRow.Add(randomSeed);
            quilting.Add(seedRow);
            quilting.Add(ToggleField("Poisson Correction", "quiltingSeamCorrection", () => layer.quiltingSeamCorrection,
                value => layer.quiltingSeamCorrection=value, "Optional global correction; may change the center and exceed the color range."));
            var quiltEdges=Direction("quiltingPoissonEdges", () => layer.quiltingPoissonEdges, value => layer.quiltingPoissonEdges=value);
            quilting.Add(quiltEdges);
            var quiltRadius=QuiltingSlider("Radius (%)", "quiltingCorrectionRadius", .5f,25,
                () => layer.quiltingCorrectionRadius*100,value => layer.quiltingCorrectionRadius=value/100,
                "Global correction radius relative to the smaller canvas dimension.");
            quilting.Add(quiltRadius);
            var quiltHelp = new HelpBox("Check Tiled with Pencil selected before export: reduced previews can choose different patches.", HelpBoxMessageType.None);
            quilting.Add(quiltHelp);
            Slider QuiltingSlider(string label,string name,float min,float max,Func<float> get,Action<float> set,string tooltip)
            {
                var slider=WhimTexUI.ConfigureField(new Slider(label,min,max) {name=name,showInputField=true,tooltip=tooltip});
                bool dragging=false;float pending=get();
                slider.SetValueWithoutNotify(pending);
                bindings.Track(slider, () => dragging ? pending : get());
                void Commit()
                {
                    float value=Safe(pending,min,max,get());
                    if(value!=get()) applyChange("Change Quilting "+label, () => set(value));
                }
                slider.RegisterCallback<PointerCaptureEvent>(_ => { pending=slider.value; dragging=true; });
                slider.RegisterCallback<PointerCaptureOutEvent>(_ => { if(!dragging)return;dragging=false;Commit(); });
                slider.RegisterCallback<DetachFromPanelEvent>(_ => dragging=false);
                slider.RegisterValueChangedCallback(evt => { pending=evt.newValue;if(!dragging)Commit(); });
                return slider;
            }
            VisualElement Direction(string name, Func<MakeSeamlessLayerBehaviour.PoissonEdges> get, Action<MakeSeamlessLayerBehaviour.PoissonEdges> set, string label="Poisson Edges")
            {
                var container = new VisualElement { name = name };
                container.Add(EdgeHeading(label));
                var selector = new WhimTexEdgeSelector(name + "Selector", () => applyChange("Invert " + label, () =>
                {
                    var current = get();
                    set(current == MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges ? MakeSeamlessLayerBehaviour.PoissonEdges.None
                        : current == MakeSeamlessLayerBehaviour.PoissonEdges.None ? MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges
                        : current == MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom ? MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight
                        : MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom);
                })) { tooltip = "Opposite edges toggle together. Clear both pairs to skip this pass. Unselected pairs are not joined; their pixels may still change while another pair is selected." };
                container.Add(selector);
                Add("top", MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom);
                Add("bottom", MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom);
                Add("left", MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight);
                Add("right", MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight);
                return container;

                void Add(string edge, MakeSeamlessLayerBehaviour.PoissonEdges pair)
                {
                    var opposite = pair == MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom
                        ? MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight : MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom;
                    var button = new Button(() =>
                    {
                        var current = get();
                        applyChange("Change " + label, () => set(current == MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges
                            ? opposite : current == pair ? MakeSeamlessLayerBehaviour.PoissonEdges.None
                            : current == MakeSeamlessLayerBehaviour.PoissonEdges.None ? pair : MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges));
                    }) { name = name + "-" + edge };
                    button.AddToClassList("whimtex-seamless-edge");
                    button.AddToClassList("whimtex-seamless-edge--" + edge);
                    void Refresh()
                    {
                        var current = get();
                        button.EnableInClassList("whimtex-seamless-edge--selected", current == MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges || current == pair);
                        button.tooltip = pair == MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom ? "Toggle Top & Bottom together (vertical tiling)."
                            : "Toggle Left & Right together (horizontal tiling).";
                    }
                    Refresh(); bindings.Add(Refresh); selector.AddEdge(button, pair.ToString());
                }
            }
            Toggle ToggleField(string label, string name, Func<bool> get, Action<bool> set, string tooltip)
            {
                var toggle = WhimTexUI.ConfigureField(new Toggle(label) { name = name, tooltip = tooltip });
                toggle.SetValueWithoutNotify(get());
                bindings.Track(toggle, get);
                toggle.RegisterValueChangedCallback(evt => applyChange("Change Seamless " + label, () => set(evt.newValue)));
                return toggle;
            }
            Slider Percentage(string label, float min, float max, Func<float> get, Action<float> set, string tooltip)
            {
                var slider = WhimTexUI.ConfigureField(new Slider(label, min, max) { showInputField = true, tooltip = tooltip });
                slider.SetValueWithoutNotify(get()*100);
                bindings.Track(slider, () => get()*100);
                slider.RegisterValueChangedCallback(evt => applyChange("Change Seamless " + label,
                    () => set(Safe(evt.newValue/100, min/100, max/100, get()))));
                return slider;
            }
            var screened = new HelpBox("Poisson can change the interior and exceed the color range. Check Tiled preview.", HelpBoxMessageType.None);
            root.Add(screened);
            var correctionHelp = new HelpBox("Poisson Correction uses its own edge selection and can change the interior or exceed the color range. Check Tiled preview.", HelpBoxMessageType.None)
                { name = "seamlessCorrectionHelp" };
            root.Add(correctionHelp);
            void RefreshMode()
            {
                quiltContrast.EnableInClassList("whimtex-hidden", !layer.quiltingContrastCompensation);
                bool isMirror = layer.mode == MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
                mirror.EnableInClassList("whimtex-hidden", !isMirror);
                bool isQuilting=layer.EffectiveMode==MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting;
                processing.EnableInClassList("whimtex-hidden", isMirror || isQuilting);
                quilting.EnableInClassList("whimtex-hidden", !isQuilting);
                quiltEdges.EnableInClassList("whimtex-hidden", !layer.quiltingSeamCorrection);
                quiltRadius.EnableInClassList("whimtex-hidden", !layer.quiltingSeamCorrection);
                bool isScreened = layer.EffectiveMode == MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson;
                blendEdges.EnableInClassList("whimtex-hidden", isScreened);
                poissonEdges.EnableInClassList("whimtex-hidden", !isScreened);
                radius.EnableInClassList("whimtex-hidden", !isScreened);
                band.EnableInClassList("whimtex-hidden", isScreened);
                offsetOptions.EnableInClassList("whimtex-hidden", isScreened);
                contrast.EnableInClassList("whimtex-hidden", !layer.offsetContrastCompensation);
                offsetEdges.EnableInClassList("whimtex-hidden", !layer.offsetSeamCorrection);
                offsetAuto.EnableInClassList("whimtex-hidden", !layer.offsetSeamCorrection);
                offsetRadius.EnableInClassList("whimtex-hidden", !layer.offsetSeamCorrection);
                offsetRadius.SetEnabled(!layer.offsetAutoRadius);
                screened.EnableInClassList("whimtex-hidden", layer.EffectiveMode != MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson);
                bool showCorrectionHelp = isMirror && layer.mirrorSeamCorrection ||
                    layer.EffectiveMode == MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend && layer.offsetSeamCorrection;
                correctionHelp.EnableInClassList("whimtex-hidden", !showCorrectionHelp);
            }
            bindings.Add(RefreshMode);
            RefreshMode();
            root = mirror;
            root.Add(LabeledEdges("Mirror Direction", BuildEdgeSelector(layer, applyChange, bindings)));
            var width = WhimTexUI.ConfigureField(new Slider("Blend Width (%)", .1f, 50f)
                { name = "mirrorBlendWidth", showInputField = true, tooltip = "Transition width as a percentage of each canvas dimension. Wider transitions hide the join more gradually." });
            width.SetValueWithoutNotify(layer.blendWidth * 100f);
            bindings.Track(width, () => layer.blendWidth * 100f);
            width.RegisterValueChangedCallback(evt => applyChange("Change Seamless Blend Width", () =>
                layer.blendWidth = Safe(evt.newValue / 100f, .001f, .5f, layer.blendWidth)));
            root.Add(width);
            var mirrorTransition = Percentage("Transition Start (%)", -100f, 95f, () => layer.mirrorTransitionStart,
                value => layer.mirrorTransitionStart = value,
                "Position within Blend Width where reflection starts fading into the source. 0: fade from the edge. Negative values widen the transition beyond the canvas and may expose a seam; use Poisson Correction if needed. Positive values narrow the fade.");
            mirrorTransition.name = "mirrorTransitionStart";
            root.Add(mirrorTransition);
            var falloff = WhimTexUI.ConfigureField(new Slider("Falloff", .25f, 4f)
                { name = "mirrorFalloff", showInputField = true, tooltip = "Higher values concentrate the reflected image closer to the destination edge." });
            falloff.SetValueWithoutNotify(layer.falloff);
            bindings.Track(falloff, () => layer.falloff);
            falloff.RegisterValueChangedCallback(evt => applyChange("Change Seamless Falloff", () =>
                layer.falloff = Safe(evt.newValue, .25f, 4f, layer.falloff)));
            root.Add(falloff);
            var compensate = WhimTexUI.ConfigureField(new Toggle("Contrast Compensation")
                { name = "mirrorContrastCompensation", tooltip = "Reduce contrast loss where reflected and original pixels mix. Adds histogram analysis; colors can change." });
            compensate.SetValueWithoutNotify(layer.mirrorContrastCompensation);
            bindings.Track(compensate, () => layer.mirrorContrastCompensation);
            compensate.RegisterValueChangedCallback(evt => applyChange("Change Mirror Contrast Compensation",
                () => layer.mirrorContrastCompensation = evt.newValue));
            root.Add(compensate);
            var mirrorContrast = Percentage("Strength (%)", 0, 100, () => layer.mirrorContrast,
                value => layer.mirrorContrast = value, "0: ordinary Mirror. 100: full histogram compensation for reflected pairs.");
            root.Add(mirrorContrast);
            mirrorContrast.name = "mirrorCompensation";
            var correct = WhimTexUI.ConfigureField(new Toggle("Poisson Correction")
                { name = "mirrorSeamCorrection", tooltip = "Global Screened Poisson pass, independent of the reflection directions. Can change the interior and other edges; may exceed the color range. Reflection remains." });
            correct.SetValueWithoutNotify(layer.mirrorSeamCorrection);
            bindings.Track(correct, () => layer.mirrorSeamCorrection);
            correct.RegisterValueChangedCallback(evt => applyChange("Change Mirror Poisson Correction",
                () => layer.mirrorSeamCorrection = evt.newValue));
            root.Add(correct);
            var mirrorEdges = Direction("mirrorPoissonEdges", () => layer.mirrorPoissonEdges, value => layer.mirrorPoissonEdges = value);
            root.Add(mirrorEdges);
            var mirrorAuto = ToggleField("Automatic Radius", "mirrorAutoRadius",
                () => layer.mirrorAutoRadius, value => layer.mirrorAutoRadius = value,
                "Use one quarter of Blend Width as the radius (minimum 0.5% of the smaller dimension).");
            root.Add(mirrorAuto);
            var mirrorRadius = Percentage("Radius (%)", .5f, 25, () => layer.EffectiveMirrorRadius,
                value => layer.mirrorCorrectionRadius = value, "Correction distance relative to the smaller dimension; not confined to Blend Width.");
            mirrorRadius.name = "mirrorCorrectionRadius";
            root.Add(mirrorRadius);
            void RefreshMirrorOptions()
            {
                mirrorContrast.EnableInClassList("whimtex-hidden", !layer.mirrorContrastCompensation);
                mirrorRadius.EnableInClassList("whimtex-hidden", !layer.mirrorSeamCorrection);
                mirrorAuto.EnableInClassList("whimtex-hidden", !layer.mirrorSeamCorrection);
                mirrorEdges.EnableInClassList("whimtex-hidden", !layer.mirrorSeamCorrection);
            }
            bindings.Add(RefreshMirrorOptions);
            RefreshMirrorOptions();

            var offsetBlend = Group("offsetBlendSettings", band, transitionStart);
            var offsetContrast = Group("offsetContrastSettings", offsetCompensate, contrast);
            var offsetPoisson = Group("offsetPoissonSettings", offsetCorrect, offsetEdges, offsetAuto, offsetRadius);
            offsetOptions.Add(offsetBlend); offsetOptions.Add(offsetContrast); offsetOptions.Add(offsetPoisson);
            var mirrorBlend = Group("mirrorBlendSettings", width, mirrorTransition, falloff);
            var mirrorCompensation = Group("mirrorContrastSettings", compensate, mirrorContrast);
            var mirrorPoisson = Group("mirrorPoissonSettings", correct, mirrorEdges, mirrorAuto, mirrorRadius);
            mirror.Add(mirrorBlend); mirror.Add(mirrorCompensation); mirror.Add(mirrorPoisson);

            var quiltSearch = Group("quiltingSearchSettings", quilting.Q("quiltingWidth"),
                quilting.Q("quiltingAlongSearch"), quality, matching, seedRow);
            var feather = quilting.Q<Slider>("quiltingFeather");
            var quiltCompensate = quilting.Q<Toggle>("quiltingContrastCompensation");
            var quiltBlend = Group("quiltingBlendSettings", feather);
            var quiltCompensation = Group("quiltingContrastSettings", quiltCompensate, quiltContrast);
            var quiltPoisson = Group("quiltingPoissonSettings", quilting.Q("quiltingSeamCorrection"), quiltEdges, quiltRadius);
            quilting.Add(quiltSearch); quilting.Add(quiltBlend); quilting.Add(quiltCompensation); quilting.Add(quiltPoisson);
            quilting.Add(quiltHelp);
            foreach (var section in new[] { offsetContrast, offsetPoisson, mirrorCompensation, mirrorPoisson,
                         quiltBlend, quiltPoisson })
                section.AddToClassList("whimtex-seamless-section");

            void RefreshAvailability()
            {
                bool copy = layer.leftEdge || layer.rightEdge || layer.topEdge || layer.bottomEdge;
                bool reflect = layer.horizontal != MakeSeamlessLayerBehaviour.HorizontalDirection.Off ||
                    layer.vertical != MakeSeamlessLayerBehaviour.VerticalDirection.Off;
                bool patch = layer.quiltingEdges != MakeSeamlessLayerBehaviour.PoissonEdges.None;
                Available(offsetBlend, copy, "Select Copy Edges to enable blending.");
                Available(offsetContrast, copy, "Select Copy Edges to enable contrast compensation.");
                Available(mirrorBlend, reflect, "Select a Mirror Direction to enable blending.");
                Available(mirrorCompensation, reflect, "Select a Mirror Direction to enable contrast compensation.");
                Available(quiltSearch, patch, "Select Patch Edges to enable patch search.");
                Available(quiltBlend, patch, "Select Patch Edges to enable Feather.");
                Available(quiltCompensation, patch && layer.quiltingFeather > 0,
                    patch ? "Increase Feather above 0% to enable contrast compensation." : "Select Patch Edges to enable contrast compensation.");
                radius.SetEnabled(layer.poissonEdges != MakeSeamlessLayerBehaviour.PoissonEdges.None);
                bool offsetCorrection = layer.offsetPoissonEdges != MakeSeamlessLayerBehaviour.PoissonEdges.None;
                offsetAuto.SetEnabled(offsetCorrection);
                offsetRadius.SetEnabled(offsetCorrection && !layer.offsetAutoRadius);
                bool mirrorCorrection = layer.mirrorPoissonEdges != MakeSeamlessLayerBehaviour.PoissonEdges.None;
                mirrorAuto.SetEnabled(mirrorCorrection);
                mirrorRadius.SetEnabled(mirrorCorrection && !layer.mirrorAutoRadius);
                quiltRadius.SetEnabled(layer.quiltingPoissonEdges != MakeSeamlessLayerBehaviour.PoissonEdges.None);
            }
            bindings.Add(RefreshAvailability);
            RefreshAvailability();
        }

        private static VisualElement LabeledEdges(string label, VisualElement selector)
        {
            var container = new VisualElement();
            container.Add(EdgeHeading(label));
            container.Add(selector);
            return container;
        }

        private static Label EdgeHeading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("whimtex-seamless-heading");
            return label;
        }

        private static VisualElement Group(string name, params VisualElement[] children)
        {
            var group = new VisualElement { name = name };
            foreach (var child in children) group.Add(child);
            return group;
        }

        private static void Available(VisualElement group, bool enabled, string reason)
        {
            group.SetEnabled(enabled);
            group.tooltip = enabled ? string.Empty : reason;
        }

        private static VisualElement BuildEdgeSelector(MakeSeamlessLayerBehaviour layer,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {
            var selector = new WhimTexEdgeSelector("seamlessEdges", () => applyChange("Toggle Seamless Directions", () =>
            {
                bool active = layer.horizontal != MakeSeamlessLayerBehaviour.HorizontalDirection.Off
                    || layer.vertical != MakeSeamlessLayerBehaviour.VerticalDirection.Off;
                layer.horizontal = active ? MakeSeamlessLayerBehaviour.HorizontalDirection.Off : MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight;
                layer.vertical = active ? MakeSeamlessLayerBehaviour.VerticalDirection.Off : MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop;
            }), "Toggle both reflection axes.");

            AddEdge("left", "Copy the right edge onto the left. Click again to turn horizontal blending off.",
                () => layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft,
                () => layer.horizontal = layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft
                    ? MakeSeamlessLayerBehaviour.HorizontalDirection.Off : MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft);
            AddEdge("right", "Copy the left edge onto the right. Click again to turn horizontal blending off.",
                () => layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight,
                () => layer.horizontal = layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight
                    ? MakeSeamlessLayerBehaviour.HorizontalDirection.Off : MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight);
            AddEdge("top", "Copy the bottom edge onto the top. Click again to turn vertical blending off.",
                () => layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop,
                () => layer.vertical = layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop
                    ? MakeSeamlessLayerBehaviour.VerticalDirection.Off : MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop);
            AddEdge("bottom", "Copy the top edge onto the bottom. Click again to turn vertical blending off.",
                () => layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom,
                () => layer.vertical = layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom
                    ? MakeSeamlessLayerBehaviour.VerticalDirection.Off : MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom);
            return selector;

            void AddEdge(string edge, string tooltip, Func<bool> selected, Action toggle)
            {
                var button = new Button(() => applyChange("Change Seamless Direction", toggle))
                    { name = "seamlessEdge-" + edge, tooltip = tooltip };
                button.AddToClassList("whimtex-seamless-edge");
                button.AddToClassList("whimtex-seamless-edge--" + edge);
                void Refresh() => button.EnableInClassList("whimtex-seamless-edge--selected", selected());
                Refresh();
                bindings.Add(Refresh);
                selector.AddEdge(button, edge);
            }
        }

        private static VisualElement BuildProcessingEdges(MakeSeamlessLayerBehaviour layer,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {
            var selector = new WhimTexEdgeSelector("seamlessProcessingEdges", () => applyChange("Invert Seamless Edges", () =>
            {
                layer.leftEdge = !layer.leftEdge;
                layer.rightEdge = !layer.rightEdge;
                layer.topEdge = !layer.topEdge;
                layer.bottomEdge = !layer.bottomEdge;
            })) { tooltip = "Click edges to select copy bands. All off skips copying, not enabled Poisson Correction. Corner regions belong to either selected edge." };
            Add("left", () => layer.leftEdge, () => layer.leftEdge = !layer.leftEdge);
            Add("right", () => layer.rightEdge, () => layer.rightEdge = !layer.rightEdge);
            Add("top", () => layer.topEdge, () => layer.topEdge = !layer.topEdge);
            Add("bottom", () => layer.bottomEdge, () => layer.bottomEdge = !layer.bottomEdge);
            return selector;

            void Add(string edge, Func<bool> get, Action toggle)
            {
                var button = new Button(() => applyChange("Change Seamless Edges", toggle))
                    { name = "seamlessProcessing-" + edge, tooltip = "Toggle processing of the " + edge + " edge." };
                button.AddToClassList("whimtex-seamless-edge");
                button.AddToClassList("whimtex-seamless-edge--" + edge);
                void Refresh() => button.EnableInClassList("whimtex-seamless-edge--selected", get());
                Refresh(); bindings.Add(Refresh); selector.AddEdge(button, edge);
            }
        }


        private static float Safe(float value, float min, float max, float previous) =>
            float.IsNaN(value) || float.IsInfinity(value) ? previous : Mathf.Clamp(value, min, max);
    }
}
