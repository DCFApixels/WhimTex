using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXSeedParameterTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, string name, params object[] args) => type.GetMethod(name, F).Invoke(null, args);
    static List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)Call(Type("ShaderFXMetadata"), "Parse", code, false, null);

    public static string Run() => TestContext.Run("Seed metadata, UI, Undo, API and persistence", t =>
    {
        const string code = "// @whimtex-effect Test/Seed\n// @control(_Seed)\n" +
            "// @param label(Seed) seed float _Seed = 2.5 [2.1 .. 3.8]\n" +
            "// @param seed hidden float _Hidden = -1 [~ -2.2 .. ~ -0.1]\n" +
            "float4 ApplyFX(float2 uv, float4 color) { return float4(_Seed, _Hidden, color.b, color.a); }\n";
        var parameters = Parse(code);
        t.True(parameters[0].controls[0].seed && parameters[1].controls[0].seed, "Explicit modifier is stored on each control");
        t.True(parameters[1].controls[0].hidden && parameters[1].softMinimum && parameters[1].softMaximum, "Hidden/soft modifiers coexist");
        t.Equal("Seed", parameters[0].controls[0].label, "Modifier order preserves labels");
        t.True(!Parse("// @param float _Seed = 1 [0 .. 100]")[0].controls[0].seed, "No name-based seed inference");
        var next = Type("ShaderFXSeed").GetMethod("NextValue", F);
        var seen = new HashSet<float>();
        var range = Parse("// @param seed float _N = 0 [-2 .. 2]")[0].controls[0];
        for (int i = 0; i < 512; i++)
        {
            float value = (float)next.Invoke(null, new object[] { range }); seen.Add(value);
            t.True(value == Mathf.Floor(value) && value >= -2 && value <= 2, "Random is an integer inside inclusive bounds");
            t.Equal(3f, (float)next.Invoke(null, new object[] { parameters[0].controls[0] }), "Fractional bounds reduce to the integer interval");
            float soft = (float)next.Invoke(null, new object[] { parameters[1].controls[0] });
            t.True(soft == -2 || soft == -1, "Soft bounds constrain generation too");
        }
        t.True(seen.Contains(-2) && seen.Contains(2), "Both endpoints are reachable");
        foreach (float value in new[] { -16777216f, 0f, 16777216f })
        {
            var fixedRange = Parse("// @param seed float _N = " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " [" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " .. " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "]")[0];
            t.Equal(value, (float)next.Invoke(null, new object[] { fixedRange.controls[0] }), "Fixed ranges and exact float integer limits");
        }
        foreach (string declaration in new[] {
            "seed bool _N = false", "seed enum _N = A {A:0, B:1}", "seed float2 _N = (0,0)",
            "seed float _N", "seed float _N [0 ..]", "seed float _N [0.1 .. 0.9]",
            "seed float _N = 0 [-16777218 .. 0]", "seed float _N = 0 [0 .. 16777218]",
            "seed seed float _N [0 .. 10]", "seed float _N [NaN .. 10]" })
        {
            try { Parse("// @param " + declaration); t.True(false, "Malformed seed must fail: " + declaration); }
            catch (TargetInvocationException e) { t.True(e.InnerException is FormatException && e.InnerException.Message.Contains("Line 1"), "Seed authoring error has a source line"); }
        }
        var repeated = Parse("// @param seed float _N = 1 [0 .. 10]\n// @param label(Alternate) seed float _N [0 .. 20]");
        t.Equal(2, repeated[0].controls.Count, "Repeated declarations keep their controls");
        t.True(repeated[0].controls.All(c => c.seed), "Each repeated declaration keeps the seed modifier");
        var copy = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(repeated[0], null);
        t.True(copy.controls.All(c => c.seed) && !ReferenceEquals(copy.controls[0], repeated[0].controls[0]), "Copy preserves independent seed metadata");

        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        ShaderFX apiCopy = null;
        try
        {
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            fx = (ShaderFX)draft.Invoke(null, new object[] { document, code, parameters });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var viewType = Type("ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { fx }, null);
            t.Equal(1, view.Query<Button>("randomizeSeed").ToList().Count, "Visible seed has one Random button; hidden seed has no row");
            t.Equal(1, view.Query<FloatField>().ToList().Count, "Seed uses a draggable numeric field, not a slider");
            Undo.IncrementCurrentGroup();
            viewType.GetMethod("RandomizeSeed", F).Invoke(view, new object[] { values[0].id, values[0].controls[0] });
            Undo.FlushUndoRecordObjects();
            t.Equal(3f, values[0].floatValue, "UI Random changes the model once");
            t.Equal(3f, view.Q<FloatField>().value, "UI refresh follows the randomized value");
            t.True(!(bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Value edits do not require recompilation");
            Undo.PerformUndo();
            values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            t.Equal(2.5f, values[0].floatValue, "Undo restores manual fractional seed");
            Undo.PerformRedo();
            values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            t.Equal(3f, values[0].floatValue, "Redo restores the chosen value, not a new random choice");
            Undo.ClearUndo(fx);

            var header = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { fx, true, null, null, -1 }, null);
            t.True(header.Q<Button>("randomizeSeed") != null, "@control exposes Random in the header too");
            string exported = (string)Call(Type("ShaderFXPresetWriter"), "BuildParameterSource", code, values, "Test/Seed", "whimtex-effect", null);
            var exportedValues = Parse(exported);
            t.True(exportedValues[0].controls[0].seed && exportedValues[1].controls[0].seed, "Preset export retains seed modifiers");
            t.Equal(3f, exportedValues[0].floatValue, "Preset export retains the chosen value");

            var layer = new Layer(new ColorFillLayerBehaviour());
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
            document.layers.Add(layer); layer.fx.Add(fx);
            object snapshot = Call(typeof(WhimTexApi), "LiveFxSnapshot", layer, document);
            t.True(snapshot.ToString().Contains("\"seed\": true") && snapshot.ToString().Contains("\"seedMinimum\": 3"), "Agent sees seed metadata and integer bounds");
            var jArrayType = snapshot.GetType();
            object Names(string json) => jArrayType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
            values[0].floatValue = 2.5f;
            Call(typeof(WhimTexApi), "RandomizeFxSeeds", fx, Names("[\"_Seed\"]"), null, false);
            t.Equal(2.5f, values[0].floatValue, "API validation does not randomize");
            Call(typeof(WhimTexApi), "RandomizeFxSeeds", fx, Names("[\"_Seed\",\"_Hidden\"]"), null, true);
            t.Equal(3f, values[0].floatValue, "Agent randomizes using the same generator as UI");
            t.True(values[1].floatValue == -2 || values[1].floatValue == -1, "Agent can explicitly randomize hidden seed");
            foreach (string invalid in new[] { "[\"_Missing\"]", "[\"_Seed\",\"_Seed\"]", "[]", "[1]" })
            {
                try { Call(typeof(WhimTexApi), "RandomizeFxSeeds", fx, Names(invalid), null, false); t.True(false, "Invalid API randomization must fail"); }
                catch (TargetInvocationException) { t.True(true, "Invalid API randomization is rejected"); }
            }
            float savedSeed = values[1].floatValue;
            values[0].floatValue = 2.5f;
            Call(typeof(WhimTexApi), "ApplyFxOperations", document, layer,
                Names("[{\"op\":\"set\",\"index\":0,\"randomizeSeeds\":[\"_Seed\"]}]"), true, new Dictionary<string, Layer>());
            apiCopy = (ShaderFX)layer.fx[0];
            t.True(!ReferenceEquals(apiCopy, fx), "FX operation uses normal copy-on-write ownership");
            var apiValues = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(apiCopy);
            t.Equal(3f, apiValues[0].floatValue, "Complete agent FX set operation randomizes once");
            t.Equal(2.5f, values[0].floatValue, "Agent operation leaves the original shared effect unchanged");
            var saved = WhimTexDocumentJson.Write(document);
            t.Equal(savedSeed, values[1].floatValue, "Saving does not choose another seed");
            using var read = WhimTexDocumentJson.Read(saved.Json);
            var restored = (ShaderFX)read.Document.layers[0].fx[0];
            var restoredValues = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(restored);
            t.True(restoredValues[0].controls[0].seed, "JSON reconstructs seed metadata from code");
            t.Equal(savedSeed, restoredValues[1].floatValue, "JSON retains the chosen hidden value");
        }
        finally
        {
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            if (apiCopy != null) { Undo.ClearUndo(apiCopy); UnityEngine.Object.DestroyImmediate(apiCopy); }
            Undo.ClearUndo(document);
            UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
