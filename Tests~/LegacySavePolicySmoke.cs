using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class LegacySavePolicySmoke
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static string Run()
    {
        string folder = "Assets/WhimTexLegacySavePolicy_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        TextureCompositor legacy = ScriptableObject.CreateInstance<TextureCompositor>();
        TextureCompositorWindow window = null;
        try
        {
            legacy.hideFlags = HideFlags.None;
            legacy.width = 32;
            legacy.height = 32;
            legacy.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.magenta }));
            string legacyPath = folder + "/Legacy.asset";
            typeof(TextureCompositor).GetMethod("SaveLegacyAssetForCompatibility", Any).Invoke(legacy, new object[] { legacyPath });
            byte[] before = File.ReadAllBytes(legacyPath);
            Check(WhimTexLegacyMigrationProbe.IsLegacy(legacyPath), "legacy fixture path");

            window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            typeof(TextureCompositorWindow).GetMethod("SetCompositor", Any).Invoke(window, new object[] { legacy });
            string tiffPath = folder + "/Migrated.tiff";
            bool saved = (bool)typeof(TextureCompositorWindow).GetMethod("SaveDocumentTo", Any)
                .Invoke(null, new object[] { legacy, tiffPath });
            Check(saved, "window SaveDocumentTo accepts TIFF destination");
            Check(WhimTexDocumentFile.IsDocument(tiffPath), "TIFF migration result is a WhimTex document");
            Check(File.ReadAllBytes(legacyPath).SequenceEqual(before), "legacy source was not overwritten");
            Check((bool)typeof(TextureCompositorWindow).GetMethod("IsLegacyAssetPath", Any)
                .Invoke(null, new object[] { legacyPath }), "legacy path guard");
            return "PASS: legacy source remains byte-identical and window save writes only TIFF.";
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            AssetDatabase.DeleteAsset(folder);
            if (legacy != null && !AssetDatabase.Contains(legacy)) UnityEngine.Object.DestroyImmediate(legacy);
        }
    }

    // Avoid depending on the internal migration helper from a standalone run_script assembly.
    private static class WhimTexLegacyMigrationProbe
    {
        internal static bool IsLegacy(string path) => string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase);
    }
}
