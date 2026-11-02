// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// run_script ShaderFX0125PresetTests.Run. Current metadata, canonical values, render parity,
// TIFF save/reopen, linked catalog refresh and owned native serialization.
// The historical class/scenario name is retained for the runner, not as a 0.12.5 compatibility claim.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static class ShaderFX0125PresetTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFX0125PresetTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Metadata = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
    static int checks;
    static void Check(bool ok, string message) { context.True(ok, message); }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)Metadata.GetMethod("Parse", F)
        .Invoke(null, new object[] { source, false, null });
    static List<ShaderFXParameter> Parameters(ShaderFX fx) =>
        (List<ShaderFXParameter>)typeof(ShaderFX).GetProperty("parameters", F).GetValue(fx);
    static void VerifyValues(ShaderFX fx, string id)
    {
        var parameters = Parameters(fx);
        var opacity = parameters.Find(p => p.name == "_Opacity");
        Check(opacity != null && Mathf.Abs(opacity.floatValue - .37f) < 1e-6f && opacity.id == id,
            "Current preset opacity and identity retained");
        foreach (var p in parameters)
            Check(p.name != "_Amount" && p.name != "_Density", "No obsolete built-in parameter");
        var mask = parameters.Find(p => p.name == "_MaskChannel");
        if (mask != null)
            Check(mask.controls[0].groupHeaderParameter == "_MaskChannel", "Current preset group header retained");
    }
    static void Compare(Color[] expected, WhimTexDocument doc, string context)
    {
        var render = doc.ComposeCanvas();
        try
        {
            var actual = render.GetPixels();
            Check(actual.Length == expected.Length, context + ": render size");
            float worst = 0;
            for (int i = 0; i < actual.Length; i++)
                for (int channel = 0; channel < 4; channel++)
                {
                    Check(!float.IsNaN(actual[i][channel]) && !float.IsInfinity(actual[i][channel]), context + ": finite channel");
                    worst = Mathf.Max(worst, Mathf.Abs(expected[i][channel] - actual[i][channel]));
                }
            Check(worst <= .002f, context + ": rendered image changed, delta=" + worst);
        }
        finally { Object.DestroyImmediate(render); }
    }
    private static void ExecuteRun()
    {
        checks = 0;
        string folder = fixture.AssetFolder();
        // GUID asset folder already created by fixture.
        try
        {
            foreach (string name in new[] { "ColorFilter", "Negative", "Mask", "GradientMap" })
            {
                WhimTexDocument doc = null, loaded = null;
                ShaderFX fx = null;
                try
                {
                    string current = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + name + ".hlsl");
                    // Exercise current-format linking/persistence, not historical parser migration.
                    // The comment distinguishes the embedded source from the catalog without changing pixels.
                    string embedded = current + "\n// Independent document copy before catalog linking.\n";
                    var saved = Parse(embedded);
                    var opacity = saved.Find(p => p.name == "_Opacity");
                    Check(opacity != null, "Current preset declares _Opacity: " + name);
                    opacity.floatValue = .37f;
                    string id = opacity.id;
                    doc = ScriptableObject.CreateInstance<WhimTexDocument>();
                    doc.hideFlags = HideFlags.HideAndDontSave;
                    doc.width = 16; doc.height = 16;
                    var layer = new Layer(new ColorFillLayerBehaviour { color = new Color(.24f, .57f, .83f, .68f) });
                    doc.layers.Add(layer);
                    fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                        new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                        .Invoke(null, new object[] { doc, embedded, saved });
                    layer.fx.Add(fx);
                    Call(fx, "ApplyAgentDraft");
                    VerifyValues(fx, id);
                    var reference = doc.ComposeCanvas();
                    Color[] expected;
                    try { expected = reference.GetPixels(); }
                    finally { Object.DestroyImmediate(reference); }
                    // Linking replaces the independent source, retaining current-schema values and IDs.
                    var identityType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetIdentity", true);
                    object identity = identityType.GetMethod("Read", F).Invoke(null, new object[] { current });
                    string presetId = (string)identityType.GetField("id", F).GetValue(identity);
                    int presetVersion = (int)identityType.GetField("version", F).GetValue(identity);
                    Check(!string.IsNullOrEmpty(presetId) && presetVersion > 0, "Built-in preset ID/version: " + name);
                    typeof(ShaderFX).GetProperty("presetId", F).SetValue(fx, presetId);
                    typeof(ShaderFX).GetProperty("presetVersion", F).SetValue(fx, presetVersion);
                    Call(fx, "ReloadCatalogSource", true);
                    Check((bool)typeof(ShaderFX).GetProperty("HasAppliedShader", F).GetValue(fx) &&
                        !(bool)typeof(ShaderFX).GetProperty("UsingPresetFallback", F).GetValue(fx), "Catalog refresh compiled without fallback: " + name);
                    VerifyValues(fx, id);
                    Compare(expected, doc, name + ": canonical preset");
                    string path = WhimTexDocumentFile.Save(doc, folder + "/" + name + ".tiff");
                    loaded = WhimTexDocumentFile.Load(path);
                    Check(string.IsNullOrEmpty((string)typeof(WhimTexDocument).GetField("documentLoadWarning", F).GetValue(loaded)),
                        "Complete TIFF read: " + name);
                    VerifyValues((ShaderFX)loaded.layers[0].fx[0], id);
                    Compare(expected, loaded, name + ": TIFF reopen");
                    // Native object serialization only; this does not make .asset files catalog presets.
                    var native = ScriptableObject.CreateInstance<ShaderFX>();
                    string assetPath = folder + "/" + name + ".asset";
                    try
                    {
                        typeof(ShaderFX).GetProperty("code", F).SetValue(native, current);
                        typeof(ShaderFX).GetProperty("parameters", F).SetValue(native, Parameters(fx).ConvertAll(p =>
                            (ShaderFXParameter)p.GetType().GetMethod("Copy", F).Invoke(p, null)));
                        AssetDatabase.CreateAsset(native, assetPath);
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                        var preset = AssetDatabase.LoadAssetAtPath<ShaderFX>(assetPath);
                        VerifyValues(preset, id);
                    }
                    finally { if (!EditorUtility.IsPersistent(native)) Object.DestroyImmediate(native); }
                }
                catch (TargetInvocationException e) { throw e.InnerException ?? e; }
                finally
                {
                    if (loaded != null) Object.DestroyImmediate(loaded);
                    if (fx != null) { Undo.ClearUndo(fx); Object.DestroyImmediate(fx); }
                    if (doc != null) Object.DestroyImmediate(doc);
                }
            }
            return;
        }
        finally { MigrationD.DeleteAsset(folder); }
    }
}

