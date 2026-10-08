using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class ShaderFXDiagnosticsTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static string Run() => TestContext.Run("FX share diagnostic severity, text and Console suppression across UI and API", test =>
    {
        Assembly assembly = typeof(ShaderFX).Assembly;
        Type shared = assembly.GetType("DCFApixels.WhimTex.ShaderFXDiagnostics", true);
        var reported = (HashSet<Hash128>)shared.GetField("reportedMessages", All).GetValue(null);
        var previousReported = new HashSet<Hash128>(reported);
        var effects = new List<ShaderFX>();
        WhimTexDocument document = null;
        var logs = new List<(string text, LogType type)>();
        string token = Guid.NewGuid().ToString("N");
        string path = "Assets/WhimTexTestMigration/Diagnostics_" + token + ".hlsl";
        string clean = "float4 ApplyFX(float2 uv, float4 color) { return color; }";
        string control = "// @control(_Missing_" + token + ")\n" + clean;
        string mixed = control.Replace("return color;", "return color + _Time.y * 0.01;");
        string broken = "float4 ApplyFX(float2 uv, float4 color) { return color + _Time.y + _Missing_" + token + "; }";
        string malformed = "// @param nonsense _Bad_" + token + "\n" + clean;
        Application.LogCallback capture = (text, stack, type) =>
        {
            if (text.StartsWith("WhimTex: ", StringComparison.Ordinal)) logs.Add((text, type));
        };
        Application.logMessageReceived += capture;
        try
        {
            ShaderFX Create(string source, string sourcePath)
            {
                var effect = ScriptableObject.CreateInstance<ShaderFX>();
                effects.Add(effect);
                effect.hideFlags = HideFlags.HideAndDontSave;
                effect.name = "Diagnostics " + token;
                typeof(ShaderFX).GetField("documentIncludeBasePath", All).SetValue(effect, sourcePath);
                SetCode(effect, source);
                return effect;
            }
            int Logged(string sourcePath) => logs.FindAll(item => item.text.StartsWith("WhimTex: " + sourcePath + "\n", StringComparison.Ordinal)).Count;
            var effect = Create(control, path);
            test.True(Apply(effect), "Non-time metadata warnings permit normal Apply");
            test.Equal("Warning", Read(effect, "DiagnosticSeverity").ToString(), "Metadata warning has shared severity");
            test.True(!(bool)Read(effect, "IsUnavailable"), "Warnings do not skip the FX");
            test.Equal(1, Logged(path), "Metadata warning uses the common Console reporter");
            test.Equal(LogType.Warning, logs.Find(item => item.text.Contains("_Missing_" + token)).type, "Metadata Console severity agrees");
            test.True(Apply(effect), "Repeated metadata warning still compiles");
            test.Equal(1, Logged(path), "Non-time warnings are deduplicated too");

            SetCode(effect, mixed);
            test.True(Apply(effect), "Time and metadata warnings coexist");
            test.Equal(2, Messages(effect).Count, "Both producers enter the same list");
            test.True(((IList)Read(effect, "DiagnosticMessages")).IsReadOnly, "Consumers cannot modify the shared diagnostic snapshot");
            string mixedText = (string)Read(effect, "Diagnostics");
            test.Equal(mixedText, Read(effect, "DiagnosticNotice"), "UI notice reuses the common diagnostic text");
            CheckUi(test, assembly, effect, HelpBoxMessageType.Warning);
            Layer layer = new ColorFillLayerBehaviour();
            layer.fx.Add(effect);
            test.True(ReferenceEquals(effect, typeof(Layer).GetProperty("DiagnosticEffect", All).GetValue(layer)), "Collapsed layer and section can discover a warning-only FX");

            var transient = Create(mixed, path);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", All).Invoke(transient, null);
            test.Equal(mixedText, Read(transient, "Diagnostics"), "Normal and transient Apply produce identical messages");
            test.Equal(2, Logged(path), "Transient compiles share suppression for every warning");
            object compiled = ParseJson(WhimTexApi.CompileFXSource(mixed));
            CheckApi(test, compiled, true, 2);
            CheckParity(test, Messages(effect), At(compiled, "diagnostics"));
            int beforeRepeat = logs.Count;
            CheckApi(test, ParseJson(WhimTexApi.CompileFXSource(mixed)), true, 2);
            test.Equal(beforeRepeat, logs.Count, "Agent Console suppression does not erase API diagnostics");
            object snapshot = typeof(WhimTexApi).GetMethod("LiveFxSnapshot", All).Invoke(null, new object[] { layer, null });
            object first = First(snapshot);
            test.Equal(mixedText, At(first, "diagnostics").ToString(), "Live inspection reuses common text");
            CheckParity(test, Messages(effect), At(first, "diagnosticMessages"));
            test.Equal(2, Count(At(first, "warnings")), "Live inspection exposes all warning records");
            test.Equal(0, Count(At(first, "errors")), "Live inspection does not turn warnings into errors");

            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 8;
            document.layers.Add(layer);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", All).Invoke(document, null);
            using (var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json))
            {
                test.Equal(1, loaded.Warnings.Count, "Successful JSON preparation returns the shared warning notice");
                test.Equal(mixedText, loaded.Warnings[0], "JSON load retains the same diagnostic text");
                var loadedFx = (ShaderFX)loaded.Document.layers[0].fx[0];
                test.Equal(2, Messages(loadedFx).Count, "JSON load preserves both warning producers");
                test.True(!(bool)Read(loadedFx, "IsUnavailable"), "JSON warning-only FX remains usable");
            }
            using (var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false))
            {
                Type clipboardType = typeof(WhimTexApi).GetNestedType("ProceduralClipboard", All);
                using var clipboard = (IDisposable)Activator.CreateInstance(clipboardType, true);
                var clipboardEffects = (List<ShaderFX>)clipboardType.GetField("Effects", All).GetValue(clipboard);
                clipboardEffects.Add((ShaderFX)loaded.Document.layers[0].fx[0]);
                clipboardType.GetField("Document", All).SetValue(clipboard, loaded.TakeDocument());
                clipboardType.GetMethod("Compile", All).Invoke(clipboard, null);
                var clipboardWarnings = (List<string>)clipboardType.GetField("Warnings", All).GetValue(clipboard);
                test.Equal(1, clipboardWarnings.Count, "Clipboard preparation exposes warning-only FX too");
                test.Equal(mixedText, clipboardWarnings[0], "Clipboard uses the same messages as Apply and JSON");
            }

            SetCode(effect, malformed);
            test.True(!Apply(effect), "Invalid parameter declarations fail Apply");
            test.Equal("Error", Read(effect, "DiagnosticSeverity").ToString(), "Declaration failure shares Error severity");
            test.True((bool)Read(effect, "IsUnavailable"), "Declaration failure skips any previous shader");
            layer.fx.Insert(0, transient);
            test.True(ReferenceEquals(effect, typeof(Layer).GetProperty("DiagnosticEffect", All).GetValue(layer)), "Layer indicator prioritizes errors over earlier warnings");
            layer.fx.RemoveAt(0);
            CheckUi(test, assembly, effect, HelpBoxMessageType.Error);
            int failureLogs = Logged(path);
            test.True(!Apply(effect), "Repeated declaration failure stays failed");
            test.Equal(failureLogs, Logged(path), "Errors use the same suppression as warnings");
            object invalid = ParseJson(WhimTexApi.CompileFXSource(malformed));
            CheckApi(test, invalid, false, 0);
            CheckParity(test, Messages(effect), At(invalid, "diagnostics"));
            test.True(HasSeverityLog(logs, At(First(At(invalid, "errors")), "message").ToString(), LogType.Error),
                "Preflight declaration errors use Console Error too");

            SetCode(effect, broken);
            test.True(!Apply(effect), "An actual HLSL error fails Apply");
            test.Equal("Error", Read(effect, "DiagnosticSeverity").ToString(), "Compiler errors determine overall severity");
            test.True(HasMessage(Messages(effect), "Warning", "time-dependent Unity inputs"), "Compiler failure does not erase time warnings");
            test.True(HasMessage(Messages(effect), "Error", "_Missing_" + token), "Compiler error keeps its own message");
            CheckUi(test, assembly, effect, HelpBoxMessageType.Error);
            object badShader = ParseJson(WhimTexApi.CompileFXSource(broken));
            test.True(!bool.Parse(At(badShader, "compiled").ToString()), "Agent exposes actual shader failure");
            test.True(Count(At(badShader, "warnings")) >= 1 && Count(At(badShader, "errors")) >= 1, "Agent retains warnings and errors simultaneously");

            SetCode(effect, clean);
            test.True(Apply(effect), "A clean Apply clears the failure");
            test.Equal(0, Messages(effect).Count, "Clean Apply clears all stale diagnostics");
            test.Equal<object>(null, Read(effect, "DiagnosticNotice"), "Clean FX clears UI warning indicators");
            test.Equal<object>(null, typeof(Layer).GetProperty("DiagnosticEffect", All).GetValue(layer), "Clean layer has no warning indicator");
            Shader shader = (Shader)typeof(ShaderFX).GetField("compiledShader", All).GetValue(effect);
            object unusable = shared.GetMethod("Collect", All).Invoke(null, new object[] { shader, mixed, mixed, Read(effect, "Parameters"), path, false });
            test.True(HasMessage(Items(unusable), "Error", "not supported"), "Unsupported shader adds an Error even when warnings already exist");
            test.True(HasMessage(Items(unusable), "Warning", "time-dependent"), "Unsupported shader does not erase collected warnings");

            var missingPreset = Create(clean, path + ".catalog");
            typeof(ShaderFX).GetField("catalogGuid", All).SetValue(missingPreset, Guid.NewGuid().ToString("N"));
            typeof(ShaderFX).GetMethod("ReloadCatalogSource", All).Invoke(missingPreset, new object[] { true });
            test.Equal("Error", Read(missingPreset, "DiagnosticSeverity").ToString(), "Catalog reload failure enters the shared mechanism");
            test.True(HasSeverityLog(logs, "effect source is missing", LogType.Error), "Catalog failure uses common Console severity");
        }
        finally
        {
            Application.logMessageReceived -= capture;
            if (document != null) Object.DestroyImmediate(document);
            foreach (var effect in effects) if (effect != null) Object.DestroyImmediate(effect);
            reported.RemoveWhere(identity => !previousReported.Contains(identity));
        }
    });

    private static object Read(ShaderFX effect, string property) => typeof(ShaderFX).GetProperty(property, All).GetValue(effect);
    private static void SetCode(ShaderFX effect, string source) => typeof(ShaderFX).GetMethod("SetDraftCode", All).Invoke(effect, new object[] { source });
    private static bool Apply(ShaderFX effect) => (bool)typeof(ShaderFX).GetMethod("ApplyCore", All).Invoke(effect, new object[] { false });
    private static List<object> Items(object sequence) { var result = new List<object>(); foreach (object item in (IEnumerable)sequence) result.Add(item); return result; }
    private static List<object> Messages(ShaderFX effect) => Items(Read(effect, "DiagnosticMessages"));
    private static object Field(object item, string name) => item.GetType().GetField(name, All).GetValue(item);
    private static bool HasMessage(List<object> messages, string severity, string part)
        => messages.Exists(item => Field(item, "Severity").ToString() == severity && Field(item, "Message").ToString().Contains(part));
    private static bool HasSeverityLog(List<(string text, LogType type)> logs, string part, LogType type)
        => logs.Exists(item => item.type == type && item.text.Contains(part));

    private static void CheckUi(TestContext test, Assembly assembly, ShaderFX effect, HelpBoxMessageType severity)
    {
        Type ui = assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true);
        var warning = (VisualElement)ui.GetMethod("CreateFxWarning", All).Invoke(null, new object[] { false });
        ui.GetMethod("RefreshFxWarning", All).Invoke(null, new object[] { warning, effect });
        test.True(!warning.ClassListContains("whimtex-hidden"), "FX header reveals its diagnostic indicator");
        test.Equal((string)Read(effect, "DiagnosticNotice"), warning.tooltip, "Tooltip uses the same messages");
        using var serialized = new SerializedObject(effect);
        var view = (VisualElement)assembly.GetType("DCFApixels.WhimTex.ShaderFXEditor", true)
            .GetMethod("BuildView", All).Invoke(null, new object[] { effect, serialized });
        test.Equal(severity, view.Q<HelpBox>().messageType, "Editor status uses the shared severity");
        test.Equal((string)Read(effect, "Diagnostics"), view.Q<TextField>("shaderFXDiagnostics").value, "Editor Diagnostics uses the same text");
        view.Unbind();
    }

    private static object ParseJson(string json)
    {
        var reference = Array.Find(typeof(WhimTexApi).Assembly.GetReferencedAssemblies(), item => item.Name == "Newtonsoft.Json");
        return Assembly.Load(reference).GetType("Newtonsoft.Json.Linq.JObject", true)
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
    }
    private static object At(object value, string key) => value.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(value, new object[] { key });
    private static int Count(object array) => (int)array.GetType().GetProperty("Count").GetValue(array);
    private static object First(object array) => Items(array)[0];
    private static void CheckApi(TestContext test, object result, bool compiled, int warnings)
    {
        test.True(bool.Parse(At(result, "success").ToString()), "Diagnostic request itself completes");
        test.Equal(compiled, bool.Parse(At(result, "compiled").ToString()), "Apply and API agree on compilation");
        test.Equal(warnings, Count(At(result, "warnings")), "API warning count agrees");
        test.Equal(!compiled, Count(At(result, "errors")) > 0, "API preserves Error severity");
    }
    private static void CheckParity(TestContext test, List<object> messages, object json)
    {
        var exported = Items(json);
        test.Equal(messages.Count, exported.Count, "API exports the same diagnostic list");
        for (int i = 0; i < messages.Count; i++)
        {
            test.Equal(Field(messages[i], "Severity").ToString(), At(exported[i], "severity").ToString(), "Same severity");
            test.Equal(Field(messages[i], "Message").ToString(), At(exported[i], "message").ToString(), "Same message");
            int line = (int)Field(messages[i], "Line");
            if (line > 0) test.Equal(line, int.Parse(At(exported[i], "line").ToString()), "Same source line");
        }
    }
}
