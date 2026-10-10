// Run via Unity Pipeline run_script, entry NoiseRandomizeTests.Run. No assets saved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class NoiseRandomizeTests
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string ExecuteRun()
    {
        int checks = 0;
        void Check(bool condition, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(condition, message); checks++; }
        bool Grain(NoiseLayerBehaviour.NoiseType type) => type == NoiseLayerBehaviour.NoiseType.WhiteNoise || type == NoiseLayerBehaviour.NoiseType.BlueNoise;
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.width = doc.height = 32;
        var noise = new NoiseLayerBehaviour { encoding = NoiseLayerBehaviour.OutputEncoding.Gradient,
            offset = new Vector3(12.5f, -37.25f, 4.75f), direction = 37.5f, periodic1D = true };
        var preservedOffset = noise.offset;
        doc.layers.Add(noise);
        typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(doc, null);
        var layer = doc.layers[0];
        layer.layerName = "Preserve";
        layer.opacity = .7f;
        layer.transform.rotation = 17f;
        string transform = JsonUtility.ToJson(layer.transform);
        var randomize = typeof(NoiseLayerEditorWindow).GetMethod("RandomizeParameters", Any);
        var fields = typeof(NoiseLayerBehaviour).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var seen = fields.ToDictionary(f => f.Name, f => new HashSet<object>());
        var bounds = new Dictionary<string, (float lo, float hi)>
        {
            ["grainSize"]=(1,1024), ["direction"]=(-180,180), ["scale"]=(.01f,1000),
            ["octaves"]=(1,8), ["lacunarity"]=(1,4), ["gain"]=(0,1), ["weightedStrength"]=(0,1),
            ["pingPongStrength"]=(.01f,8), ["cellularJitter"]=(0,1), ["warpStrength"]=(0,100), ["warpScale"]=(.25f,4), ["warpScaleY"]=(.25f,4)
        };
        NoiseLayerEditorWindow window = null;
        try
        {
            var unityRandom = UnityEngine.Random.state;
            for (int i=0; i<256; i++)
            {
                if (i % 32 == 0) noise.noiseType = (NoiseLayerBehaviour.NoiseType)(i / 32);
                bool grain = Grain(noise.noiseType);
                int seed = noise.seed;
                var dimensions = i % 2 == 0 ? NoiseLayerBehaviour.NoiseDimensions.TwoD : NoiseLayerBehaviour.NoiseDimensions.OneD;
                noise.dimensions = dimensions;
                randomize.Invoke(null, new object[] {noise});
                Check(Grain(noise.noiseType) == grain, "Random All stays in the selected noise group across repeated calls");
                Check(noise.dimensions == dimensions, "Random All preserves chosen 1D/2D mode");
                Check(noise.seed != seed, "Each press gets a fresh seed");
                foreach (var field in fields)
                {
                    var value = field.GetValue(noise);
                    seen[field.Name].Add(value);
                    if (field.FieldType.IsEnum) Check(Enum.IsDefined(field.FieldType, value), "Valid enum " + field.Name);
                    if (bounds.TryGetValue(field.Name, out var range))
                    {
                        float number = Convert.ToSingle(value);
                        Check(!float.IsNaN(number) && number >= range.lo && number <= range.hi, "Valid range " + field.Name);
                    }
                }
                Check(noise.offset.Equals(preservedOffset), "Random All preserves Offset XYZ exactly");
                Check(noise.direction == 37.5f && noise.periodic1D, "Random All preserves Direction and 1D Seamless");
                if (i % 4 == 0)
                {
                    var image = doc.ComposeCanvas();
                    try { Check(image.GetPixels().All(c => c.r >= 0 && c.r <= 1 && c.g >= 0 && c.g <= 1 && c.b >= 0 && c.b <= 1 && c.a >= 0 && c.a <= 1), "Finite bounded render"); }
                    finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image); }
                }
            }
            foreach (var field in fields)
            {
                if (field.Name == "scaleZ" || field.Name == "dimensions" || field.Name == "direction" || field.Name == "encoding" || field.Name == "gradient" || field.Name == "periodic" || field.Name == "periodic1D" || field.Name == "linkScale" || field.Name == "linkWarpScale" || field.Name == "offset")
                {
                    if (field.Name != "dimensions") Check(seen[field.Name].Count == 1, "Preserves " + field.Name);
                    continue;
                }
                Check(seen[field.Name].Count > 1, "Randomizes " + field.Name);
                if (field.FieldType.IsEnum) Check(seen[field.Name].Count == Enum.GetValues(field.FieldType).Length, "All choices reachable " + field.Name);
            }
            Check(UnityEngine.Random.state.Equals(unityRandom), "Does not modify Unity random state");
            var sample3D=typeof(NoiseLayerEditorWindow).GetMethod("RandomizeScale3D",Any);
            noise.noiseType=NoiseLayerBehaviour.NoiseType.Perlin;noise.dimensions=NoiseLayerBehaviour.NoiseDimensions.ThreeD;
            noise.Scale3D=new Vector3(4,8,2);var random3D=new System.Random(1337);
            foreach(bool linked in new[]{true,false})
            {
                noise.linkScale=linked;
                for(int i=0;i<128;i++)
                {
                    var sample=(Vector3)sample3D.Invoke(null,new object[]{noise,random3D});
                    Check(sample.x>=.01f&&sample.x<=1000&&sample.y>=.01f&&sample.y<=1000&&sample.z>=.01f&&sample.z<=1000,"3D random scale is bounded");
                    if(linked)Check(Mathf.Abs(sample.y/sample.x-2)<.00001f&&Mathf.Abs(sample.z/sample.x-.5f)<.00001f,"Random All preserves XYZ ratios");
                }
            }
            noise.linkScale=true;noise.dimensions=NoiseLayerBehaviour.NoiseDimensions.TwoD;
            foreach (NoiseLayerBehaviour.OutputEncoding output in Enum.GetValues(typeof(NoiseLayerBehaviour.OutputEncoding)))
            {
                noise.encoding = output;
                var palette = noise.gradient.Clone();
                var effectiveOutput = typeof(NoiseLayerBehaviour).GetProperty("EffectiveOutput", Any).GetValue(noise);
                bool sawTrue = false, sawFalse = false;
                var outputs = new HashSet<NoiseLayerBehaviour.OutputEncoding>();
                for (int i = 0; i < 64; i++)
                {
                    randomize.Invoke(null, new object[] { noise });
                    Check(noise.gradient.Equals(palette), "Random All preserves palette");
                    if (output == NoiseLayerBehaviour.OutputEncoding.Gradient)
                    {
                        Check(noise.encoding == output, "Gradient output is retained");
                        Check(Equals(effectiveOutput, typeof(NoiseLayerBehaviour).GetProperty("EffectiveOutput", Any).GetValue(noise)), "Gradient effective output is retained");
                    }
                    else Check(noise.encoding == NoiseLayerBehaviour.OutputEncoding.ColorValues || noise.encoding == NoiseLayerBehaviour.OutputEncoding.LinearData,
                        "Raw outputs never switch to Gradient");
                    outputs.Add(noise.encoding);
                    sawTrue |= noise.inverted; sawFalse |= !noise.inverted;
                }
                Check(sawTrue && sawFalse, "Random All still varies Inverted: " + output);
                Check(outputs.Count == (output == NoiseLayerBehaviour.OutputEncoding.Gradient ? 1 : 2), "Expected output choices reached: " + output);
            }
            Check(layer.layerName == "Preserve" && layer.opacity == .7f && JsonUtility.ToJson(layer.transform) == transform && layer.fx.Count == 0, "Layer settings stay intact");

            window = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<NoiseLayerEditorWindow>());
            typeof(LayerEditorWindowBase).GetMethod("Initialize", Any).Invoke(window, new object[] {layer,doc});
            window.ShowUtility();
            void Refresh() => typeof(LayerEditorWindowBase).GetMethod("RefreshInterface", Any).Invoke(window, new object[] {true});
            Refresh();
            var warpFields = window.rootVisualElement.Query<FloatField>().ToList();
            var warpScaleField = window.rootVisualElement.Query<Vector2Field>().ToList().Single(f => f.label == "Warp Scale");
            var warpStrengthField = warpFields.Single(f => f.label == "Warp Strength");
            Check(warpScaleField.parent == warpStrengthField.parent, "Warp fields share a section");
            Check(warpScaleField.parent.IndexOf(warpScaleField) == warpStrengthField.parent.IndexOf(warpStrengthField) + 1, "Warp Scale follows Warp Strength");
            float previousScale = noise.scale;
            warpScaleField.value = new Vector2(3,3);
            Check(Mathf.Approximately(noise.warpScale, 3) && noise.scale == previousScale,
                "Warp Scale UI edits independently: warp=" + noise.warpScale + " scale=" + noise.scale + " previous=" + previousScale);
            var warpLink = window.rootVisualElement.Q<Button>("linkWarpScale");
            Check(warpLink != null && noise.linkWarpScale, "Warp chain defaults linked");
            void ToggleWarpLink()
            {
                using var evt = NavigationSubmitEvent.GetPooled(); evt.target = warpLink; warpLink.SendEvent(evt);
            }
            ToggleWarpLink();
            warpScaleField.value = new Vector2(2,5);
            Check(noise.WarpScale == new Vector2(2,5) && !noise.linkWarpScale, "Independent warp axes");
            ToggleWarpLink();
            Check(noise.WarpScale == new Vector2(2,5), "Link retains proportions");
            warpScaleField.value = new Vector2(4,5);
            Check(noise.WarpScale == new Vector2(4,10), "Linked warp edit preserves ratio");
            for(int i=0;i<32;i++)
            {
                randomize.Invoke(null,new object[]{noise});
                Check(Mathf.Abs(noise.WarpScale.y/noise.WarpScale.x-2.5f)<.00001f,"Random All preserves linked warp ratio");
            }
            Refresh();
            void Click()
            {
                var button = window.rootVisualElement.Q<Button>("whimtex-noise-random-all");
                Check(button != null && button.text == "Random All", "Random All in actual properties UI");
                var dimensions = ((NoiseLayerBehaviour)doc.layers[0].Behaviour).dimensions;
                bool grain = Grain(((NoiseLayerBehaviour)doc.layers[0].Behaviour).noiseType);
                using var evt = NavigationSubmitEvent.GetPooled();
                evt.target = button;
                button.SendEvent(evt);
                Check(Grain(((NoiseLayerBehaviour)doc.layers[0].Behaviour).noiseType) == grain, "UI activation preserves noise group");
                Check(((NoiseLayerBehaviour)doc.layers[0].Behaviour).dimensions == dimensions, "UI activation preserves Dimensions");
                Check(((NoiseLayerBehaviour)doc.layers[0].Behaviour).offset.Equals(preservedOffset), "UI Random All preserves Offset XYZ");
            }
            string before = JsonUtility.ToJson(doc.layers[0].Behaviour);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Click();
            string after = JsonUtility.ToJson(doc.layers[0].Behaviour);
            Check(before != after, "UI activation changes settings");
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.PerformUndo();
            Refresh();
            Check(JsonUtility.ToJson(doc.layers[0].Behaviour) == before, "One Undo restores every noise setting");
            Undo.PerformRedo();
            Refresh();
            Check(JsonUtility.ToJson(doc.layers[0].Behaviour) == after, "Redo restores exact randomized settings");
            int lastSeed = ((NoiseLayerBehaviour)doc.layers[0].Behaviour).seed;
            Click();
            Check(((NoiseLayerBehaviour)doc.layers[0].Behaviour).seed != lastSeed, "Repeated UI activation changes seed");
            Check(window.rootVisualElement.Query<IntegerField>().ToList().Any(f => f.label == "Seed" && f.value == ((NoiseLayerBehaviour)doc.layers[0].Behaviour).seed), "Seed field refreshes");
            Check(window.rootVisualElement.Query<Vector2Field>().ToList().Any(f => f.label == "Warp Scale" && f.value == ((NoiseLayerBehaviour)doc.layers[0].Behaviour).WarpScale), "Warp Scale refreshes after Random All and Undo/Redo");
            var currentNoise = (NoiseLayerBehaviour)doc.layers[0].Behaviour;
            var unchanged = fields.Where(f => f.Name != "seed").ToDictionary(f => f, f => f.GetValue(currentNoise));
            var seedButton = window.rootVisualElement.Query<Button>().ToList().Single(b => b.text == "Random");
            for (int i=0; i<8; i++)
            {
                lastSeed = currentNoise.seed;
                using var evt = NavigationSubmitEvent.GetPooled();
                evt.target = seedButton;
                seedButton.SendEvent(evt);
                Check(currentNoise.seed != lastSeed, "Seed-only button changes on every press");
                Check(unchanged.All(pair => Equals(pair.Value, pair.Key.GetValue(currentNoise))), "Seed-only button preserves other settings");
            }
            return "PASS NoiseRandomizeTests: " + checks + " checks; 256 combinations from all eight starting types, 64 GPU renders, preserved Direction/Offset/Seamless/gradient, noise groups and 1D/2D, UI activation, Undo/Redo and bindings.";
        }
        finally
        {
            if (window != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(window);
            Undo.ClearUndo(doc);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
        }
    }

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseRandomizeTests.Run", () => { ExecuteRun(); });
}
