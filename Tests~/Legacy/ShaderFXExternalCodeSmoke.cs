using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXExternalCodeSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Type bridge = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXExternalCode", true);
        Type sessionType = bridge.GetNestedType("Session", BindingFlags.NonPublic);
        MethodInfo poll = bridge.GetMethod("PollSession", flags);
        MethodInfo setDraft = typeof(ShaderFX).GetMethod("SetDraftCode", flags);
        var utf8 = new UTF8Encoding(false);
        string folder = Path.GetFullPath(Path.Combine("Library", "WhimTex", "Tests", "ExternalCode-" + Guid.NewGuid().ToString("N")));
        string codePath = Path.Combine(folder, "test.hlsl");
        string baselinePath = Path.Combine(folder, "test.baseline");
        string requestPath = codePath + ".apply";
        const string original = "float4 ApplyFX(float2 uv, float4 color) { return color; }";
        const string edited = "float4 ApplyFX(float2 uv, float4 color) { return color * 0.75; }";
        const string invalid = "// @param unsupported _Broken\n" + original;
        ShaderFX effect = null;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        object Get(string name) => typeof(ShaderFX).GetProperty(name, flags).GetValue(effect);
        string AppliedCode() => (string)typeof(ShaderFX).GetField("appliedCode", flags).GetValue(effect);

        try
        {
            Directory.CreateDirectory(folder);
            effect = ScriptableObject.CreateInstance<ShaderFX>();
            effect.hideFlags = HideFlags.HideAndDontSave;
            setDraft.Invoke(effect, new object[] { original });
            File.WriteAllText(codePath, original, utf8);
            File.WriteAllText(baselinePath, original, utf8);
            object session = Activator.CreateInstance(sessionType, flags, null,
                new object[] { effect, codePath, baselinePath }, null);
            void Poll() => poll.Invoke(null, new object[] { session, effect });
            void Save(string code)
            {
                File.WriteAllText(codePath, code, utf8);
                File.WriteAllText(requestPath, "apply", utf8);
                Poll();
            }

            Poll();
            Check(!(bool)Get("HasAppliedShader"), "Idle synchronization must not compile a draft.");

            setDraft.Invoke(effect, new object[] { edited });
            Poll();
            Check(File.ReadAllText(codePath) == edited && File.ReadAllText(baselinePath) == edited,
                "Document edits must synchronize to the code file and baseline.");

            File.WriteAllText(codePath, original, utf8);
            sessionType.GetField("PendingWriteTicks", flags).SetValue(session, 0L);
            Poll();
            Poll();
            Check((string)Get("Code") == edited, "Unsignalled external edits must wait for stable writes.");
            Poll();
            Check((string)Get("Code") == original && File.ReadAllText(baselinePath) == original &&
                !(bool)Get("HasAppliedShader"), "Stable external edits must update only the draft.");

            Save(edited);
            Check((string)Get("Code") == edited && AppliedCode() == edited &&
                !(bool)Get("LastApplyFailed") && !File.Exists(requestPath),
                "Save must import and apply immediately, consuming the request.");
            Save(edited);
            Check(AppliedCode() == edited && !File.Exists(requestPath),
                "Saving unchanged code must still handle an Apply request.");

            Save(invalid);
            Check((string)Get("Code") == invalid && (bool)Get("LastApplyFailed") &&
                (bool)Get("HasAppliedShader") && AppliedCode() == edited && !File.Exists(requestPath),
                "Failed Apply must preserve the working shader and consume the request.");
            Save(original);
            Check(!(bool)Get("LastApplyFailed") && AppliedCode() == original,
                "A corrected save must recover from the failed Apply.");

            setDraft.Invoke(effect, new object[] { edited });
            File.WriteAllText(codePath, invalid, utf8);
            File.WriteAllText(requestPath, "apply", utf8);
            sessionType.GetField("LastConflictTicks", flags).SetValue(session, File.GetLastWriteTimeUtc(codePath).Ticks);
            Poll();
            Check((string)Get("Code") == edited && File.ReadAllText(codePath) == invalid && File.Exists(requestPath),
                "A deferred conflict must preserve both versions and the pending request.");

            return "PASS: document sync, stable draft import, save/apply, unchanged save, failed Apply preservation, recovery and deferred conflict.";
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
}
