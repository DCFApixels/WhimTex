using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class BrushPresetAssetSmoke
{
    public static string Main()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:BrushPresetAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<BrushPresetAsset>(path);
            if (!(AssetImporter.GetAtPath(path) is BrushPresetImporter)) throw new Exception("Wrong importer: " + path);
            var resolver = typeof(TextureCompositorWindow).GetMethod("GetDraggedBrushPresetPath",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var previousObjects = DragAndDrop.objectReferences;
            try
            {
                DragAndDrop.objectReferences = new UnityEngine.Object[] { asset };
                if ((string)resolver.Invoke(null, null) != path) throw new Exception("Brush drop not recognized");
                DragAndDrop.objectReferences = new UnityEngine.Object[] { asset, asset };
                if (resolver.Invoke(null, null) != null) throw new Exception("Multiple brush drop must be ignored");
                DragAndDrop.objectReferences = new UnityEngine.Object[] { Texture2D.whiteTexture };
                if (resolver.Invoke(null, null) != null) throw new Exception("Texture mistaken for brush");
            }
            finally { DragAndDrop.objectReferences = previousObjects; }
            var editor = Editor.CreateEditor(asset);
            Texture2D preview = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                preview = editor.RenderStaticPreview(path, null, 128, 64);
                if (preview == null || preview.width != 128 || preview.height != 128) throw new Exception("Thumbnail must be square: " + path);
                bool painted = false;
                Color background = preview.GetPixel(0, 0);
                if (Mathf.Abs(background.r - .18f) > .01f || background.a != 1f) throw new Exception("Invalid thumbnail background");
                foreach (Color pixel in preview.GetPixels())
                {
                    painted |= Mathf.Abs(pixel.r - background.r) > .02f || Mathf.Abs(pixel.g - background.g) > .02f;
                    if (pixel.a != 1f) throw new Exception("Thumbnail must be opaque");
                }
                if (!painted) throw new Exception("Missing brush stroke: " + path);
                if (RenderTexture.active != previous || GL.sRGBWrite != srgb) throw new Exception("Render state leaked");
                if (editor.CreateInspectorGUI().Q<Image>() == null) throw new Exception("Inspector preview missing");
                count++;
            }
            finally
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }
        if (count == 0) throw new Exception("Add a .sebrush preset to the test project first.");
        return "PASS: " + count + " imported brush presets, drag recognition, thumbnail pixels, Inspector preview and render-state restoration.";
    }
}
