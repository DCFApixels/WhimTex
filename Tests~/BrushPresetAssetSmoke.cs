// run_script entry BrushPresetAssetSmoke.Main. Creates its own temporary project preset and removes it.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class BrushPresetAssetSmoke
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);

    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);

    private static void CheckImportedAsset(string path, BrushPresetAsset asset)
    {
        Check(asset != null, "temporary brush preset imported");
        Check(AssetImporter.GetAtPath(path) is BrushPresetImporter, "correct scripted importer");
        var resolver = typeof(TextureCompositorWindow).GetMethod("GetDraggedBrushPresetPath", Hidden);
        var previousObjects = DragAndDrop.objectReferences;
        try
        {
            DragAndDrop.objectReferences = new UnityEngine.Object[] { asset };
            Check((string)resolver.Invoke(null, null) == path, "brush drop is recognized");
            DragAndDrop.objectReferences = new UnityEngine.Object[] { asset, asset };
            Check(resolver.Invoke(null, null) == null, "multiple brush drops are ignored");
            DragAndDrop.objectReferences = new UnityEngine.Object[] { Texture2D.whiteTexture };
            Check(resolver.Invoke(null, null) == null, "texture is not mistaken for a brush");
        }
        finally { DragAndDrop.objectReferences = previousObjects; }

        var editor = Editor.CreateEditor(asset);
        Texture2D preview = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            preview = editor.RenderStaticPreview(path, null, 128, 64);
            Check(preview != null && preview.width == 128 && preview.height == 128, "thumbnail is square");
            Color background = preview.GetPixel(0, 0);
            Check(Mathf.Abs(background.r - .18f) <= .01f && background.a == 1f, "thumbnail background is opaque dark gray");
            bool painted = false;
            foreach (Color pixel in preview.GetPixels())
            {
                painted |= Mathf.Abs(pixel.r - background.r) > .02f || Mathf.Abs(pixel.g - background.g) > .02f;
                Check(pixel.a == 1f, "thumbnail is opaque");
            }
            Check(painted, "thumbnail contains the brush stroke");
            Check(RenderTexture.active == previous && GL.sRGBWrite == srgb, "render state is restored");
            Check(editor.CreateInspectorGUI().Q<Image>() != null, "inspector exposes a preview");
        }
        finally
        {
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            UnityEngine.Object.DestroyImmediate(editor);
        }
    }

    public static string Main()
    {
        string folder = "Assets/WhimTexBrushPresetSmoke_" + Guid.NewGuid().ToString("N");
        string path = folder + "/Fixture.sebrush";
        Texture2D tip = null;
        try
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var assembly = typeof(TextureCompositorWindow).Assembly;
            Type settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
            object settings = Activator.CreateInstance(settingsType);
            Set(settings, "brushSize", 57f);
            object dynamics = Get(settings, "dynamics");
            tip = new Texture2D(8, 8, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(1f, i % 8 < 4 ? 0.15f : 0.9f, 0.05f, 1f);
            tip.SetPixels(pixels);
            tip.Apply(false, false);
            Set(dynamics, "tip", tip);

            Type library = assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary", true);
            MethodInfo save = library.GetMethod("Save", Hidden);
            save.Invoke(null, new object[] { Path.GetFullPath(path), settings, false });
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            CheckImportedAsset(path, AssetDatabase.LoadAssetAtPath<BrushPresetAsset>(path));
            return "PASS: generated project brush asset, drag recognition, square thumbnail and inspector preview.";
        }
        finally
        {
            if (tip != null) UnityEngine.Object.DestroyImmediate(tip);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }
    }
}
