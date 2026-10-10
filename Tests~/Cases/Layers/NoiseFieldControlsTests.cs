using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using WhimTex.Tests;

public static class NoiseFieldControlsTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    public static string Run(string suite = "api") => TestContext.Run("Noise field " + suite, t =>
    {
        var n = new NoiseLayerBehaviour();
        if (suite == "api")
        {
            var setter = typeof(WhimTexApi).GetMethod("SetNoise", Static);
            var snapshot = typeof(WhimTexApi).GetMethod("NoiseSnapshot", Static);
            var jsonType = setter.GetParameters()[1].ParameterType;
            object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
            void Set(string json) => setter.Invoke(null, new object[] { n, Json(json) });
            Set("{\"field\":\"Curl\",\"noiseType\":\"WhiteNoise\",\"dimensions\":\"OneD\",\"vectorOutput\":\"SignedVector\",\"normalize\":true,\"strength\":2,\"fractal\":\"PingPong\",\"weightedStrength\":0.7}");
            t.Equal(NoiseLayerBehaviour.NoiseType.WhiteNoise, n.noiseType, "Inactive type retained");
            t.Equal(NoiseLayerBehaviour.NoiseType.OpenSimplex2, n.EffectiveNoiseType, "Curl fallback basis");
            t.Equal(NoiseLayerBehaviour.NoiseDimensions.TwoD, n.EffectiveDimensions, "Vector 1D fallback");
            Set("{\"field\":\"Value\"}");
            t.Equal(NoiseLayerBehaviour.NoiseType.WhiteNoise, n.EffectiveNoiseType, "Value restores grain");
            t.Equal(NoiseLayerBehaviour.NoiseDimensions.OneD, n.EffectiveDimensions, "Value restores 1D");
            Set("{\"field\":\"CellDirection\",\"dimensions\":\"ThreeD\",\"warpSeed\":-2147483648,\"warpScale\":[2,3,4],\"linkWarpScale\":true}");
            t.Equal(NoiseLayerBehaviour.NoiseType.Cellular, n.EffectiveNoiseType, "Cell Direction fixed basis");
            t.Equal(new Vector3(2, 3, 4), n.WarpScale3D, "Literal XYZ despite link");
            t.Equal(int.MinValue, n.warpSeed, "Full signed Warp Seed");
            Set("{\"warpScale\":[5,6]}");
            t.Equal(new Vector3(5, 6, 4), n.WarpScale3D, "Pair retains Warp Z");
            Set("{\"warpScale\":7}");
            t.Equal(new Vector3(7, 7, 7), n.WarpScale3D, "3D scalar sets XYZ");
            Set("{\"dimensions\":\"TwoD\",\"warpScale\":2}");
            t.Equal(new Vector3(2, 2, 7), n.WarpScale3D, "2D scalar keeps Z");
            var copy = new NoiseLayerBehaviour();
            setter.Invoke(null, new[] { (object)copy, snapshot.Invoke(null, new object[] { n }) });
            t.Equal(snapshot.Invoke(null, new object[] { n }).ToString(), snapshot.Invoke(null, new object[] { copy }).ToString(), "API complete roundtrip");
            foreach (string json in new[] { "{\"field\":\"Unknown\"}", "{\"vectorOutput\":\"Unknown\"}", "{\"normalize\":1}",
                "{\"strength\":-1}", "{\"strength\":101}", "{\"warpSeed\":2147483648}", "{\"warpScale\":[1,2,0]}",
                "{\"warpScale\":[1,2,1001]}", "{\"warpScale\":[1,2,3,4]}", "{\"whiteNoiseColor\":\"Color\"}", "{\"whiteNoiseSize\":4}" })
            {
                bool rejected = false;
                try { Set(json); }
                catch (TargetInvocationException error) { rejected = error.InnerException?.GetType().Name == "WhimTexApiException"; }
                t.True(rejected, "Reject invalid/retired key " + json);
            }
            string discovery = WhimTexApi.Describe();
            foreach (string key in new[] { "noiseFields", "noiseFieldTypes", "noiseVectorOutputs", "noiseGrainColors" })
                t.True(discovery.Contains(key), "Discovery " + key);
        }
        else if (suite == "ui")
        {
            var root = new VisualElement();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.titleContent = new GUIContent("Noise fields test");
            window.rootVisualElement.Add(root);
            window.ShowUtility();
            var bindingsType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI")
                .GetNestedType("ValueBindings", BindingFlags.NonPublic);
            var bindings = Activator.CreateInstance(bindingsType, true);
            void Refresh() => bindingsType.GetMethod("Refresh").Invoke(bindings, new object[] { true });
            bool Visible(VisualElement element)
            {
                for (var parent = element; parent != null; parent = parent.parent)
                    if (parent.ClassListContains("whimtex-hidden")) return false;
                return element != null;
            }
            T Find<T, TValue>(string label) where T : BaseField<TValue> => root.Query<T>().Where(v => v.label == label).First();
            try
            {
                typeof(NoiseLayerEditorWindow).GetMethod("BuildFields", Static).Invoke(null,
                    new object[] { root, n, null, (Action<string, Action>)((_, action) => action()), bindings });
                Refresh();
                var field = Find<EnumField, Enum>("Field");
                var type = Find<PopupField<NoiseLayerBehaviour.NoiseType>, NoiseLayerBehaviour.NoiseType>("Noise Type");
                var dimensions = Find<PopupField<string>, string>("Dimensions");
                n.noiseType = NoiseLayerBehaviour.NoiseType.BlueNoise; n.dimensions = NoiseLayerBehaviour.NoiseDimensions.OneD;
                field.value = NoiseLayerBehaviour.Field.Curl; Refresh();
                t.Equal(3, type.choices.Count, "Curl supports three bases");
                t.True(!dimensions.choices.Contains("1D"), "No vector 1D choice");
                t.Equal(NoiseLayerBehaviour.NoiseType.BlueNoise, n.noiseType, "UI retains hidden type");
                t.True(Visible(Find<Slider, float>("Weighted Strength")), "Shared vector Weighted Strength");
                t.True(!Visible(Find<PopupField<NoiseLayerBehaviour.OutputEncoding>, NoiseLayerBehaviour.OutputEncoding>("Output")), "Scalar output hidden");
                t.True(Visible(Find<Toggle, bool>("Normalize")), "Vector controls visible");
                field.value = NoiseLayerBehaviour.Field.GradientVector; Refresh();
                t.Equal(5, type.choices.Count, "Gradient supports five bases");
                field.value = NoiseLayerBehaviour.Field.CellDirection; Refresh();
                t.True(!type.enabledSelf && type.value == NoiseLayerBehaviour.NoiseType.Cellular, "Cell basis fixed");
                t.True(!Visible(Find<EnumField, Enum>("Fractal")), "Cell Fractal hidden");
                t.True(!Visible(Find<EnumField, Enum>("Return")), "Cell Return hidden");
                t.True(Visible(Find<EnumField, Enum>("Distance")), "Cell Distance visible");
                n.warp = NoiseLayerBehaviour.WarpType.BasicGrid; n.dimensions = NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                n.WarpScale3D = new Vector3(2, 3, 4); Refresh();
                var warpScale = root.Q<Vector3Field>("noiseWarpScale3D");
                t.True(Visible(warpScale), "3D Warp XYZ visible");
                warpScale.value = new Vector3(2, 3, 8);
                t.Equal(new Vector3(4, 6, 8), n.WarpScale3D, "Warp Z linked proportionally");
                field.value = NoiseLayerBehaviour.Field.Value; Refresh();
                t.Equal(NoiseLayerBehaviour.NoiseType.BlueNoise, type.value, "Returning restores grain type");
                t.True(!Visible(warpScale), "Grain hides Domain Warp");
                t.True(Visible(Find<FloatField, float>("Grain Size (px)")), "Grain control visible");
                n.field = NoiseLayerBehaviour.Field.Curl; n.noiseType = NoiseLayerBehaviour.NoiseType.Perlin;
                n.normalize = true; n.strength = 2; n.vectorOutput = NoiseLayerBehaviour.VectorOutput.SignedVector;
                n.dimensions = NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                for (int i = 0; i < 12; i++)
                {
                    int seed = n.seed, warpSeed = n.warpSeed;
                    var oldScale = n.WarpScale3D;
                    typeof(NoiseLayerEditorWindow).GetMethod("RandomizeParameters", Static).Invoke(null, new object[] { n });
                    t.True(n.seed != seed && n.warpSeed != warpSeed, "Both seeds randomize independently");
                    t.True(NoiseLayerBehaviour.SupportsNoiseType(n.field, n.noiseType), "Random All uses supported bases");
                    t.True(n.normalize && n.strength == 2 && n.vectorOutput == NoiseLayerBehaviour.VectorOutput.SignedVector, "Vector output preserved");
                    t.Near(oldScale.y / oldScale.x, n.WarpScale3D.y / n.WarpScale3D.x, .0001, "Random Warp linked Y");
                    t.Near(oldScale.z / oldScale.x, n.WarpScale3D.z / n.WarpScale3D.x, .0001, "Random Warp linked Z");
                }
            }
            finally
            {
                bindingsType.GetMethod("Clear").Invoke(bindings, null);
                if (window != null) window.Close();
            }
        }
        else throw new ArgumentException("Unknown Noise field suite: " + suite);
    });
}
