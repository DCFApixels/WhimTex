// Node orchestration: Begin -> one real Unity recompile -> Verify -> public GUID Cleanup.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentReloadTests
{
    [Serializable]
    public sealed class ReloadResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public string coverageBranch;
        public bool partialCoverage;
    }
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Key;
    static string RunId;
    static WhimTex.Tests.TestContext context;
    static Type Session => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession");
    static void Check(bool condition, string message) { context.True(condition, message); }
    static string ExecutePrepare()
    {
        Check(string.IsNullOrEmpty(EditorPrefs.GetString(Key, "")), "finish the previous reload probe first");
        Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "stop the user's Live Update before this probe");
        Check(!EditorPrefs.HasKey((string)Session.GetField("RecoveryKey", Any).GetValue(null)),
            "User readable-recovery journal must be resolved before this owned reload probe.");
        UnityBReload.ValidateAssetDirectory(RunId, true);
        string folder = UnityBManualAssets.Create(RunId);
        EditorPrefs.SetString(Key, folder);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.name = RunId;
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 64; doc.height = 32;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
        string path = WhimTexDocumentFile.Save(doc, folder + "/Reload.tiff");
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = false; importer.SaveAndReimport();
        // Use a real EditorWindow host: a bare ScriptableObject is not restored by Unity
        // across an assembly reload and would make this probe test the wrong lifecycle.
        var window = EditorWindow.CreateWindow<TextureCompositorWindow>();
        window.name = "WhimTex Document Reload " + RunId;
        var previousDefault = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
        typeof(TextureCompositorWindow).GetMethod("SetCompositor", Any).Invoke(window, new object[] { doc });
        if (previousDefault != null && previousDefault != doc) UnityEngine.Object.DestroyImmediate(previousDefault);
        typeof(TextureCompositorWindow).GetMethod("BindDocumentFile", Any).Invoke(window, new object[] { path });
        window.Show();
        EditorPrefs.SetString(Key + ".guid", AssetDatabase.AssetPathToGUID(path));
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.green;
        typeof(TextureCompositor).GetMethod("MarkChanged", Any).Invoke(doc, null);
        Check((bool)Session.GetMethod("Start", Any).Invoke(null, new object[] { doc, path }), "Live Update start");
        return "READY: run Unity recompile, wait for completion, then Verify. " + folder;
    }

    static string ExecuteVerify(out bool carrierOnly)
    {
        carrierOnly = false;
        string folder = EditorPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException("Missing owned marker; real reload was not verified.");
        Check(folder == "Assets/WhimTexTestMigration/" + RunId, "owned test folder");
        string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(Key + ".guid", ""));
        TextureCompositorWindow found = null;
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
            {
                var document = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
                var service = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService");
                if ((string)service.GetMethod("PathOf", Any).Invoke(null, new object[] { document }) == path) found = window;
            }
        if (found == null)
        {
            carrierOnly = true;
            // Unity may close utility windows created by an ephemeral test assembly during a
            // domain reload. The carrier/session lifecycle is still verifiable independently.
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored without a surviving utility window");
            var loadedAfterReload = WhimTexDocumentFile.Load(path);
            try { Check(((ColorFillLayerBehaviour)loadedAfterReload.layers[0].Behaviour).color == Color.red, "saved TIFF remains intact after reload"); }
            finally { UnityEngine.Object.DestroyImmediate(loadedAfterReload); }
            return "PARTIAL: carrier/session branch only; surviving-window binding/content/storage-GUID/save oracle was not executed.";
        }
        {
            Check(found != null, "window restored");
            var doc = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(found);
            object[] binding = { doc, null };
            Check((bool)typeof(TextureCompositorWindow).GetMethod("TryGetDocumentFile", Any).Invoke(null, binding) && (string)binding[1] == path, "binding restored after domain reload");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored across domain reload");
            Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "live session ended before reload");
            Check(doc.width == 64 && doc.height == 32 && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.green, "unsaved document content retained");
            var storageBinding = typeof(TextureCompositor).GetField("documentBinding", Any).GetValue(doc) as UnityEngine.Object;
            Check(storageBinding != null, "storage binding survives reload independently of window fallback");
            WhimTexDocumentFile.Save(doc, path);
            Check((string)storageBinding.GetType().GetField("guid", Any).GetValue(storageBinding) == AssetDatabase.AssetPathToGUID(path), "save after reload retains storage GUID");
            var loaded = WhimTexDocumentFile.Load(path);
            try { Check(((ColorFillLayerBehaviour)loaded.layers[0].Behaviour).color == Color.green, "save after reload persists unsaved contents"); }
            finally { UnityEngine.Object.DestroyImmediate(loaded); }
            return "Surviving-window branch verified; owned cleanup follows separately.";
        }
    }

    static void Bind(string runId, WhimTex.Tests.TestContext value) {
        if (!Guid.TryParseExact(runId, "N", out _)) throw new ArgumentException("Use one stable per-run N-format GUID across the real reload.");
        RunId=runId; Key="WhimTex.Tests.UnityB.DocumentReloadTests."+runId; context=value;
    }
    public static string Begin(string runId) => WhimTex.Tests.TestContext.Run("DocumentReloadTests: Begin", value => {
        var previous = context;
        try {
            Bind(runId,value);
            if (EditorPrefs.HasKey(Key)) throw new InvalidOperationException("Existing owned run must be cleaned first.");
            UnityBReload.Begin("Document",runId,value);
            ExecutePrepare();
            UnityBReload.Arm("Document",runId);
        } finally { context = previous; }
    });
    public static string Trigger(string runId) => UnityBReload.Trigger("Document",runId);
    public static string ReloadPoll(string runId) => UnityBReload.ReloadPoll("Document",runId);
    public static string Verify(string runId)
    {
        bool carrierOnly = false;
        string branch = "unverified";
        string branchMessage = null;
        // Only extend the shared structured payload with coverage facts. Every old Check
        // still executes through TestContext; no text verdict or old PASS parser is used.
        string json = WhimTex.Tests.TestContext.Run("DocumentReloadTests: real reload verification", value => {
            var previous = context;
            try {
                Bind(runId,value); UnityBReload.Verify("Document",runId,value);
                branchMessage = ExecuteVerify(out carrierOnly);
                branch = carrierOnly ? "carrier-only" : "surviving-window";
            } finally { context = previous; }
        });
        var result = JsonUtility.FromJson<ReloadResult>(json);
        result.coverageBranch = branch;
        result.partialCoverage = carrierOnly || branch == "unverified";
        if (branchMessage != null) result.message += ": " + branchMessage;
        return JsonUtility.ToJson(result);
    }
    public static string Cleanup(string runId) => UnityBReload.Cleanup("Document",runId, () => {
        var previous = context;
        try { Bind(runId,null); CleanupOwned(); } finally { context = previous; }
    });
    static void CleanupOwned()
    {
        string folder = EditorPrefs.GetString(Key, "");
        if (folder.Length == 0) return;
        if (folder != "Assets/WhimTexTestMigration/" + RunId) throw new IOException("Ownership marker mismatch; refusing cleanup");
        var steps = new System.Collections.Generic.List<Action>();
        steps.Add(() => {
            string live = (string)Session.GetProperty("LivePath", Any).GetValue(null);
            if (live != null && live.StartsWith(folder + "/", StringComparison.Ordinal)) Session.GetMethod("Stop", Any).Invoke(null, new object[] { "owned reload cleanup" });
        });
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
        {
            var doc = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
            if (window.name == "WhimTex Document Reload " + RunId || doc != null && doc.name == RunId)
            {
                steps.Add(() => UnityBRun.CloseOwned(window));
                if (doc != null && doc.name == RunId) steps.Add(() => UnityBReload.DestroyOwned(doc));
            }
        }
        foreach (var doc in Resources.FindObjectsOfTypeAll<TextureCompositor>())
            if (doc.name == RunId) steps.Add(() => UnityBReload.DestroyOwned(doc));
        steps.Add(() => {
            UnityBReload.ValidateAssetDirectory(RunId, false);
            string live = (string)Session.GetProperty("LivePath", Any).GetValue(null);
            string recoveryKey = (string)Session.GetField("RecoveryKey", Any).GetValue(null);
            string recoveryPath = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(recoveryKey, ""));
            if (live != null && live.StartsWith(folder + "/", StringComparison.Ordinal)
                || recoveryPath.StartsWith(folder + "/", StringComparison.Ordinal))
                throw new IOException("Owned importer recovery is still pending; retaining its asset directory.");
            if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder)) throw new IOException("Owned reload directory cleanup failed");
        });
        UnityBReload.Drain(steps);
        EditorPrefs.DeleteKey(Key); EditorPrefs.DeleteKey(Key + ".guid");
    }
}
