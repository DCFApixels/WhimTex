using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class ShaderFXTimeWarningTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string TimeSource = "float4 ApplyFX(float2 uv, float4 color) { return color + _Time.y * 0.01; }";
    private const string SinSource = "float4 ApplyFX(float2 uv, float4 color) { return color + _SinTime.x * 0.01; }";
    private const string CleanSource = "// _Time is only a comment\nfloat4 ApplyFX(float2 uv, float4 color) { return color; }";

    public static string Run() => TestContext.Run("Unity time inputs compile with visible, deduplicated warnings", test =>
    {
        var effects = new List<ShaderFX>();
        var warnings = new List<string>();
        var severities = new List<LogType>();
        string path = "Assets/WhimTexTestMigration/TimeWarning_" + Guid.NewGuid().ToString("N") + ".hlsl";
        string agentPath = "Assets/WhimTexAgentInput.hlsl";
        Type builder = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder", true);
        Type cache = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache", true);
        string Warning(string source) => (string)builder.GetMethod("GetDeterminismWarning", Static).Invoke(null, new object[] { source });
        int Count(string sourcePath) => warnings.FindAll(message => message.StartsWith("WhimTex: " + sourcePath + "\n", StringComparison.Ordinal)).Count;
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (message.StartsWith("WhimTex:", StringComparison.Ordinal) && message.Contains("time-dependent Unity inputs"))
            {
                severities.Add(type);
                warnings.Add(message);
            }
        };
        Application.logMessageReceived += capture;
        try
        {
            test.Equal<string>(null, Warning(CleanSource + "\n/* _SinTime\nunity_DeltaTime */"), "Comments do not produce warnings");
            test.Equal<string>(null, Warning("float _TimeScale, prefix_Time, _CosTimeExtra;"), "Identifier substrings do not produce warnings");
            string all = Warning("_Time _SinTime _CosTime _TimeParameters unity_DeltaTime unity_Time unity_SinTime unity_CosTime _Time");
            foreach (string name in new[] { "_Time", "_SinTime", "_CosTime", "_TimeParameters", "unity_DeltaTime", "unity_Time", "unity_SinTime", "unity_CosTime" })
                test.True(all.Contains(name), "Warning identifies " + name);
            test.Equal(Warning("_Time _SinTime"), Warning("_SinTime _Time _Time"), "Warning identity ignores ordering and repeated uses");

            ShaderFX effect = Create(path, TimeSource, effects);
            test.True(Apply(effect), "Normal Apply compilation succeeds with a Unity time uniform");
            CheckApplied(test, effect, true);
            test.Equal(1, Count(path), "First successful Apply writes one Console warning");
            test.True(Apply(effect), "Repeated Apply still succeeds");
            SetCode(effect, TimeSource.Replace("0.01", "0.02"));
            test.True(Apply(effect), "An ordinary source edit still compiles");
            for (int i = 0; i < 20; i++)
                test.True(!(bool)cache.GetMethod("CanCacheFx", Static).Invoke(null, new object[] { effect }), "Time-dependent FX bypass result caching");
            test.Equal(1, Count(path), "Repeated Apply, code edits and cache queries do not spam Console");

            ShaderFX transient = Create(path, TimeSource, effects);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", Instance).Invoke(transient, null);
            CheckApplied(test, transient, true);
            test.Equal(1, Count(path), "Transient compilation shares suppression for the same source and uniform set");

            SetCode(effect, SinSource);
            test.True(Apply(effect), "Changing the time-uniform set still compiles");
            test.Equal(2, Count(path), "A different uniform set produces a new warning");
            ShaderFX other = Create(path + ".other.hlsl", TimeSource, effects);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", Instance).Invoke(other, null);
            test.Equal(1, Count(path + ".other.hlsl"), "A different source has its own Console warning");

            SetCode(effect, CleanSource);
            test.True(Apply(effect), "Removing time dependence still compiles");
            CheckApplied(test, effect, false);
            test.True((bool)cache.GetMethod("CanCacheFx", Static).Invoke(null, new object[] { effect }), "Deterministic FX may be cached again");
            test.Equal(2, Count(path), "Removing time dependence emits no time warning");

            object first = ParseJson(WhimTexApi.CompileFXSource(TimeSource));
            CheckAgent(test, first, true);
            int firstAgentLogs = Count(agentPath);
            test.True(firstAgentLogs <= 1, "Agent compilation emits at most one Console warning");
            object repeated = ParseJson(WhimTexApi.CompileFXSource(TimeSource));
            CheckAgent(test, repeated, true);
            test.Equal(firstAgentLogs, Count(agentPath), "Repeated agent preflight does not spam Console");
            CheckAgent(test, ParseJson(WhimTexApi.CompileFXSource(CleanSource)), false);
            test.Equal(firstAgentLogs, Count(agentPath), "Clean agent source produces no time warning");
            foreach (LogType severity in severities)
                test.Equal(LogType.Warning, severity, "Console severity is Warning, not Error");
        }
        finally
        {
            Application.logMessageReceived -= capture;
            foreach (ShaderFX effect in effects) if (effect != null) Object.DestroyImmediate(effect);
            // Remove only suppression entries created by this run, not pre-existing user warnings.
            var reported = (HashSet<Hash128>)typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXDiagnostics", true)
                .GetField("reportedMessages", Static).GetValue(null);
            foreach (string message in warnings)
            {
                string identity = message.Substring("WhimTex: ".Length);
                reported.Remove(Hash128.Compute(identity));
            }
        }
    });

    private static ShaderFX Create(string path, string source, List<ShaderFX> owned)
    {
        var effect = ScriptableObject.CreateInstance<ShaderFX>();
        owned.Add(effect);
        effect.name = "Time warning test";
        effect.hideFlags = HideFlags.HideAndDontSave;
        typeof(ShaderFX).GetField("documentIncludeBasePath", Instance).SetValue(effect, path);
        SetCode(effect, source);
        return effect;
    }

    private static void SetCode(ShaderFX effect, string source) => typeof(ShaderFX).GetMethod("SetDraftCode", Instance).Invoke(effect, new object[] { source });

    // Use the real Apply compilation path without touching the user's Undo history or assets.
    private static bool Apply(ShaderFX effect) => (bool)typeof(ShaderFX).GetMethod("ApplyCore", Instance).Invoke(effect, new object[] { false });

    private static void CheckApplied(TestContext test, ShaderFX effect, bool hasTime)
    {
        test.True((bool)typeof(ShaderFX).GetProperty("HasAppliedShader", Instance).GetValue(effect), "A usable shader is retained");
        test.True(!(bool)typeof(ShaderFX).GetProperty("LastApplyFailed", Instance).GetValue(effect), "Time warning does not mark Apply as failed");
        string diagnostics = (string)typeof(ShaderFX).GetProperty("Diagnostics", Instance).GetValue(effect);
        test.Equal(hasTime, diagnostics.Contains("Warning: time-dependent Unity inputs"), "Diagnostics stay visible independently of Console suppression");
        test.Equal(hasTime, (bool)typeof(ShaderFX).GetProperty("UsesUnityTimeInputs", Instance).GetValue(effect), "Cache policy follows the applied source");
    }

    // Select the package's public JSON API, not a conflicting Editor integration's copy.
    private static object ParseJson(string json)
    {
        AssemblyName reference = Array.Find(typeof(WhimTexApi).Assembly.GetReferencedAssemblies(), value => value.Name == "Newtonsoft.Json");
        return Assembly.Load(reference).GetType("Newtonsoft.Json.Linq.JObject", true)
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
    }

    private static object At(object value, string key) => value.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(value, new object[] { key });

    private static bool HasTimeWarning(object array)
    {
        foreach (object item in (System.Collections.IEnumerable)array)
            if (At(item, "severity").ToString() == "Warning" && At(item, "message").ToString().Contains("time-dependent Unity inputs")) return true;
        return false;
    }

    private static void CheckAgent(TestContext test, object result, bool hasTime)
    {
        test.True(bool.Parse(At(result, "success").ToString()) && bool.Parse(At(result, "compiled").ToString()), "Agent reports successful compilation");
        object errors = At(result, "errors");
        test.Equal(0, (int)errors.GetType().GetProperty("Count").GetValue(errors), "Time usage is not an agent error");
        test.Equal(hasTime, HasTimeWarning(At(result, "warnings")), "Agent warning list remains populated even when Console is suppressed");
        test.Equal(hasTime, HasTimeWarning(At(result, "diagnostics")), "Agent diagnostics preserve warning severity");
    }
}
