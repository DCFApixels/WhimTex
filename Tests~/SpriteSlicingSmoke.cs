using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class SpriteSlicingSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        string path = "Assets/WhimTexSliceTest_" + Guid.NewGuid().ToString("N") + ".asset";
        string copyPath = "Assets/WhimTexSliceCopyTest_" + Guid.NewGuid().ToString("N") + ".asset";
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        TextureCompositor copy = null;
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        object Call(TextureCompositor d, string method, params object[] args) => typeof(TextureCompositor).GetMethod(method, F).Invoke(d, args);
        void Save() => Call(document, "SaveLegacyAssetForCompatibility", new object[] { null });
        Sprite Find(string name) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single(x => x.name == name);
        long Id(Sprite sprite) { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id); return id; }
        try
        {
            document.width = 32; document.height = 16;
            document.layers.Add(new ColorFillLayerBehaviour());
            var settings = (TextureCompositor.OutputSettings)typeof(TextureCompositor).GetField("outputSettings", F).GetValue(document);
            settings.spriteMode = TextureCompositor.OutputSpriteMode.Multiple;
            var a = new TextureCompositor.SpriteSlice { name = "Left", rect = new Rect(0, 0, 16, 16), pivot = new Vector2(0, 1) };
            var b = new TextureCompositor.SpriteSlice { name = "Right", rect = new Rect(16, 0, 16, 16), border = Vector4.one };
            Call(document, "SetSpriteSlices", new object[] { new[] { a, b } });
            Call(document, "SaveLegacyAssetForCompatibility", path);
            long aId = Id(Find("Left")), bId = Id(Find("Right"));
            Check(Find("Left").pivot == new Vector2(0, 16) && Find("Right").border == Vector4.one, "Slice geometry");
            Check((document.OutputSprite.hideFlags & HideFlags.HideInHierarchy) != 0, "Full sprite should be hidden in Multiple");
            a.name = "Renamed";
            Call(document, "SetSpriteSlices", new object[] { new[] { b, a } }); Save();
            Check(Id(Find("Renamed")) == aId && Id(Find("Right")) == bId, "Reorder/rename broke references");
            settings.spriteMode = TextureCompositor.OutputSpriteMode.Single; Save();
            Check(Find("Renamed").hideFlags == HideFlags.HideInHierarchy && document.OutputSprite.hideFlags == HideFlags.None, "Single visibility");
            settings.spriteMode = TextureCompositor.OutputSpriteMode.Multiple; Save();
            Check(Id(Find("Renamed")) == aId && Find("Renamed").hideFlags == HideFlags.None, "Round trip lost references");
            copy = UnityEngine.Object.Instantiate(document);
            Call(copy, "SaveLegacyAssetForCompatibility", copyPath);
            Check(AssetDatabase.LoadAllAssetsAtPath(copyPath).OfType<Sprite>().Any(x => x.name == "Renamed" && x.texture == copy.OutputTexture), "Save As slices");
            Check(Id(Find("Renamed")) == aId, "Save As altered original");
            a.rect = new Rect(0, 0, 33, 16);
            bool rejected = false;
            try { Call(document, "SetSpriteSlices", new object[] { new[] { a } }); } catch (TargetInvocationException) { rejected = true; }
            Check(rejected && Id(Find("Renamed")) == aId, "Invalid bounds accepted");
            Call(document, "SetSpriteSlices", new object[] { new[] { b } }); Save();
            Check(!AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any(x => x.name == "Renamed") && Id(Find("Right")) == bId, "Deletion");
            Call(document, "SetSpriteSlices", new object[] { Array.Empty<TextureCompositor.SpriteSlice>() }); Save();
            Check(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() == 1, "Empty Multiple");
            return "PASS: " + checks + " package-independent slice checks.";
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            if (AssetDatabase.LoadMainAssetAtPath(copyPath) != null) AssetDatabase.DeleteAsset(copyPath);
            else if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
