// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXExternalCodeTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type bridge = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXExternalCode", true);
    static readonly Type sessionType = bridge.GetNestedType("Session", BindingFlags.NonPublic);
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXExternalCodeTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD())
        {
            ExecuteMain();
            CheckShaderLayer();
        }
    });

    private static Action CreatePoll(UnityEngine.Object owner, string layerId, string codePath, string baselinePath, out object session)
    {
        var ownerId = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.UnityObjectID", true)
            .GetMethod("FromObject", Flags).Invoke(null, new object[] { owner });
        var instance = Activator.CreateInstance(sessionType, Flags, null, new[] { ownerId, layerId, codePath, baselinePath }, null);
        session = instance;
        var resolve = sessionType.GetMethod("TryGetTarget", Flags);
        var poll = bridge.GetMethod("PollSession", Flags);
        return () =>
        {
            var args = new object[1];
            if (!(bool)resolve.Invoke(instance, args)) throw new InvalidOperationException("The test session did not resolve its owner.");
            poll.Invoke(null, new[] { instance, args[0] });
        };
    }

    private static void ExecuteMain()
    {
        MethodInfo setDraft = typeof(ShaderFX).GetMethod("SetDraftCode", Flags);
        var utf8 = new UTF8Encoding(false);
        string folder = fixture.TempFolder();
        string codePath = Path.Combine(folder, "test.hlsl");
        string baselinePath = Path.Combine(folder, "test.baseline");
        string requestPath = codePath + ".apply";
        const string original = "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }";
        const string edited = "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * 0.75; }";
        const string invalid = "// @param unsupported _Broken\n" + original;
        ShaderFX effect = null;
        void Check(bool value, string message) { context.True(value, message); }
        object Get(string name) => typeof(ShaderFX).GetProperty(name, Flags).GetValue(effect);
        string AppliedCode() => ((ShaderProgram)typeof(ShaderFX).GetProperty("Program", Flags).GetValue(effect)).workingCode;

        try
        {
            Directory.CreateDirectory(folder);
            effect = ScriptableObject.CreateInstance<ShaderFX>();
            effect.hideFlags = HideFlags.HideAndDontSave;
            setDraft.Invoke(effect, new object[] { original });
            File.WriteAllText(codePath, original, utf8);
            File.WriteAllText(baselinePath, original, utf8);
            var Poll = CreatePoll(effect, null, codePath, baselinePath, out object session);
            void Save(string code)
            {
                File.WriteAllText(codePath, code, utf8);
                File.WriteAllText(requestPath, "apply", utf8);
                Poll();
            }

            Poll();
            Check(!(bool)Get("HasWorkingShader"), "Idle synchronization must not compile a draft.");

            setDraft.Invoke(effect, new object[] { edited });
            Poll();
            Check(File.ReadAllText(codePath) == edited && File.ReadAllText(baselinePath) == edited,
                "Document edits must synchronize to the code file and baseline.");

            File.WriteAllText(codePath, original, utf8);
            sessionType.GetField("PendingWriteTicks", Flags).SetValue(session, 0L);
            Poll();
            Poll();
            Check((string)Get("Code") == edited, "Unsignalled external edits must wait for stable writes.");
            Poll();
            Check((string)Get("Code") == original && File.ReadAllText(baselinePath) == original &&
                !(bool)Get("HasWorkingShader"), "Stable external edits must update only the draft.");

            Save(edited);
            Check((string)Get("Code") == edited && AppliedCode() == edited &&
                !(bool)Get("LastCompileFailed") && !File.Exists(requestPath),
                "Save must import and apply immediately, consuming the request.");
            Save(edited);
            Check(AppliedCode() == edited && !File.Exists(requestPath),
                "Saving unchanged code must still handle an Apply request.");

            Save(invalid);
            Check((string)Get("Code") == invalid && (bool)Get("LastCompileFailed") &&
                (bool)Get("HasWorkingShader") && AppliedCode() == edited && !File.Exists(requestPath),
                "Failed Apply must preserve the working shader and consume the request.");
            Save(original);
            Check(!(bool)Get("LastCompileFailed") && AppliedCode() == original,
                "A corrected save must recover from the failed Apply.");

            setDraft.Invoke(effect, new object[] { edited });
            File.WriteAllText(codePath, invalid, utf8);
            File.WriteAllText(requestPath, "apply", utf8);
            sessionType.GetField("LastConflictTicks", Flags).SetValue(session, File.GetLastWriteTimeUtc(codePath).Ticks);
            Poll();
            Check((string)Get("Code") == edited && File.ReadAllText(codePath) == invalid && File.Exists(requestPath),
                "A deferred conflict must preserve both versions and the pending request.");

            return;
        }
        finally
        {
            if (effect != null)
            {
                Undo.ClearUndo(effect);
                UnityEngine.Object.DestroyImmediate(effect);
            }
            foreach (string path in new[] { requestPath, codePath, baselinePath })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    private static void CheckShaderLayer()
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        string folder = fixture.TempFolder();
        string codePath = Path.Combine(folder, "layer.hlsl");
        string baselinePath = Path.Combine(folder, "layer.baseline");
        string requestPath = codePath + ".apply";
        const string original = "float4 ApplyFX(float4 color) { return color; }";
        const string edited = "float4 ApplyFX(float4 color) { return color * 0.75; }";
        try
        {
            var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Code, draftCode = original, workingCode = original };
            document.layers.Add(shader);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(document, null);
            Directory.CreateDirectory(folder);
            File.WriteAllText(codePath, original);
            File.WriteAllText(baselinePath, original);
            var poll = CreatePoll(document, shader.Id, codePath, baselinePath, out object session);

            File.WriteAllText(codePath, edited);
            sessionType.GetField("PendingWriteTicks", Flags).SetValue(session, 0L);
            poll();
            poll();
            poll();
            context.Equal(edited, shader.draftCode, "Saving in the script editor updates the Shader layer draft");
            context.Equal(original, shader.workingCode, "An ordinary save leaves the working program intact");

            shader.mode = ShaderProgramMode.Graph;
            File.WriteAllText(requestPath, "apply");
            poll();
            context.True(File.Exists(requestPath), "Graph mode pauses external Code sessions");
            shader.mode = ShaderProgramMode.Code;
            poll();
            context.Equal(edited, shader.workingCode, "A VS Code save request compiles the Shader layer");
            context.True(!File.Exists(requestPath), "The Shader layer consumes the compilation request");
        }
        finally
        {
            Undo.ClearUndo(document);
            UnityEngine.Object.DestroyImmediate(document);
            foreach (string path in new[] { requestPath, codePath, baselinePath })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
