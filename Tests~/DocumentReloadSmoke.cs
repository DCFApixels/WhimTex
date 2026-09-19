// run_script entry DocumentReloadSmoke.Prepare -> Unity recompile -> DocumentReloadSmoke.Verify.
// Uses only its own window/assets. Verify always cleans them up.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentReloadSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Key = "WhimTex.ReloadSmoke";
    static Type Session => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession");
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
    public static string Prepare()
    {
        Check(string.IsNullOrEmpty(SessionState.GetString(Key, "")), "finish the previous reload probe first");
        Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "stop the user's Live Update before this probe");
        string folder = "Assets/WhimTexReload_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        SessionState.SetString(Key, folder);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 64; doc.height = 32;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
        string path = WhimTexDocumentFile.Save(doc, folder + "/Reload.whimtex.tiff");
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = false; importer.SaveAndReimport();
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        typeof(TextureCompositorWindow).GetMethod("SetCompositor", Any).Invoke(window, new object[] { doc });
        typeof(TextureCompositorWindow).GetMethod("BindDocumentFile", Any).Invoke(window, new object[] { path });
        window.Show();
        SessionState.SetString(Key + ".guid", AssetDatabase.AssetPathToGUID(path));
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.green;
        typeof(TextureCompositor).GetMethod("MarkChanged", Any).Invoke(doc, null);
        Check((bool)Session.GetMethod("Start", Any).Invoke(null, new object[] { doc, path }), "Live Update start");
        return "READY: run Unity recompile, wait for completion, then Verify. " + folder;
    }

    public static string Verify()
    {
        string folder = SessionState.GetString(Key, "");
        Check(folder.StartsWith("Assets/WhimTexReload_", StringComparison.Ordinal) && Path.GetFileName(folder).Length == "WhimTexReload_".Length + 32, "owned test folder");
        string path = AssetDatabase.GUIDToAssetPath(SessionState.GetString(Key + ".guid", ""));
        TextureCompositorWindow found = null;
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
            if ((string)typeof(TextureCompositorWindow).GetField("documentFileGuid", Any).GetValue(window) == SessionState.GetString(Key + ".guid", "")) found = window;
        try
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
            return "PASS: 8 domain reload checks.";
        }
        finally
        {
            if (found != null) { found.DiscardChanges(); UnityEngine.Object.DestroyImmediate(found); }
            AssetDatabase.DeleteAsset(folder);
            SessionState.EraseString(Key); SessionState.EraseString(Key + ".guid");
        }
    }
}
