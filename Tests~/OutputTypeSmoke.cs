using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class OutputTypeSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var d = ScriptableObject.CreateInstance<TextureCompositor>();
        string path = "Assets/WhimTexOutputTypeTest_" + Guid.NewGuid().ToString("N") + ".asset";
        object Call(string name, params object[] args) => typeof(TextureCompositor).GetMethod(name, F).Invoke(d, args);
        void Save() => Call("SaveWithOutput", new object[] { null });
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        int Count() => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count();
        try
        {
            d.width = 16; d.height = 16; d.layers.Add(new ColorFillLayerBehaviour());
            var settings = (TextureCompositor.OutputSettings)typeof(TextureCompositor).GetField("outputSettings", F).GetValue(d);
            Check(settings.outputType == TextureCompositor.OutputType.Sprite, "Legacy default changed");
            settings.outputType = TextureCompositor.OutputType.Texture;
            settings.pixelsPerUnit = 0;
            Call("SaveWithOutput", path);
            var texture = d.OutputTexture;
            Check(Count() == 0 && d.OutputSprite == null, "Texture created a sprite");
            Check(!(bool)Call("HasUnsavedAssetChanges"), "Texture incorrectly marked unsaved");
            settings.outputType = TextureCompositor.OutputType.Sprite; settings.pixelsPerUnit = 100;
            Save(); Check(Count() == 1 && d.OutputSprite != null, "Single output missing");
            settings.spriteMode = TextureCompositor.OutputSpriteMode.Multiple;
            Call("SetSpriteSlices", new object[] { new[] { new TextureCompositor.SpriteSlice { name = "Slice", rect = new Rect(0, 0, 16, 16) } } });
            Save(); Check(Count() == 2, "Multiple output missing");
            settings.outputType = TextureCompositor.OutputType.Texture;
            d.width = 8; d.height = 8;
            Save(); Check(Count() == 0 && d.OutputTexture == texture, "Texture switch or identity failed");
            Check(!(bool)Call("HasUnsavedAssetChanges"), "Texture switch remains dirty");
            d.width = 16; d.height = 16; settings.outputType = TextureCompositor.OutputType.Sprite;
            Save(); Check(Count() == 2 && d.OutputTexture == texture, "Slice metadata was lost");
            return "PASS: Texture-only, Single/Multiple transitions, ignored sprite validation, retained slicing, stable texture and clean save state.";
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else UnityEngine.Object.DestroyImmediate(d);
        }
    }
}
