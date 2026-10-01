// Run via Unity Pipeline run_script, entry NoiseRandomizeSmoke.Run. No assets saved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class NoiseRandomizeSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        bool Grain(NoiseLayerBehaviour.NoiseType type) => type == NoiseLayerBehaviour.NoiseType.WhiteNoise || type == NoiseLayerBehaviour.NoiseType.BlueNoise;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = doc.height = 32;
        var noise = new NoiseLayerBehaviour { encoding = NoiseLayerBehaviour.OutputEncoding.Gradient };
        doc.layers.Add(noise);
        typeof(TextureCompositor).GetMethod("NormalizeModel", Any).Invoke(doc, null);
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
            ["whiteNoiseSize"]=(1,1024), ["direction"]=(-180,180), ["scale"]=(.01f,1000),
            ["octaves"]=(1,8), ["lacunarity"]=(1,4), ["gain"]=(0,1), ["weightedStrength"]=(0,1),
            ["pingPongStrength"]=(.01f,8), ["cellularJitter"]=(0,1), ["warpStrength"]=(0,100)
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
                Check(Mathf.Abs(noise.offset.x) <= 10000 && Mathf.Abs(noise.offset.y) <= 10000, "Valid offset");
                if (i % 4 == 0)
                {
                    var image = doc.Compose();
                    try { Check(image.GetPixels().All(c => c.r >= 0 && c.r <= 1 && c.g >= 0 && c.g <= 1 && c.b >= 0 && c.b <= 1 && c.a >= 0 && c.a <= 1), "Finite bounded render"); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
            }
            foreach (var field in fields)
            {
                if (field.Name == "dimensions" || field.Name == "encoding" || field.Name == "gradient" || field.Name == "periodic" || field.Name == "linkScale")
                {
                    if (field.Name != "dimensions") Check(seen[field.Name].Count == 1, "Preserves " + field.Name);
                    continue;
                }
                Check(seen[field.Name].Count > 1, "Randomizes " + field.Name);
                if (field.FieldType.IsEnum) Check(seen[field.Name].Count == Enum.GetValues(field.FieldType).Length, "All choices reachable " + field.Name);
            }
            Check(UnityEngine.Random.state.Equals(unityRandom), "Does not modify Unity random state");
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
            Check(layer.layerName == "Preserve" && layer.opacity == .7f && JsonUtility.ToJson(layer.transform) == transform && layer.modifiers.Count == 0, "Layer settings stay intact");

            window = ScriptableObject.CreateInstance<NoiseLayerEditorWindow>();
            typeof(LayerEditorWindowBase).GetMethod("Initialize", Any).Invoke(window, new object[] {layer,doc});
            window.ShowUtility();
            void Refresh() => typeof(LayerEditorWindowBase).GetMethod("RefreshInterface", Any).Invoke(window, new object[] {true});
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
            return "PASS NoiseRandomizeSmoke: " + checks + " checks; 256 combinations from all eight starting types, 64 GPU renders, " + (fields.Length - 3) + " randomized fields, preserved gradient, noise groups and 1D/2D, UI activation, Undo/Redo and bindings.";
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            Undo.ClearUndo(doc);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
