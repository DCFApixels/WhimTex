// run_script entry DeferredDrawingTests.Run. Creates and removes only its own temporary TIFF asset.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DeferredDrawingTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static void Check(bool condition, string message)
    { T.True(condition, message); }

    private static string BodyRun()
    {
        string folder = Scope.Assets;
        // Scope.Assets already created its owned GUID folder.
        string path = folder + "/Deferred.tiff";
        TextureCompositor source = Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        source.hideFlags = HideFlags.HideAndDontSave;
        Texture2D sourceTexture = null;
        TextureCompositor loaded = null;
        try
        {
            source.width = 64;
            source.height = 32;
            sourceTexture = new Texture2D(64, 32, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            var pixels = sourceTexture.GetRawTextureData<Color32>();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(i & 255), 67, 201, 255);
            sourceTexture.Apply(false, false);
            var drawing = new DrawingLayerBehaviour();
            typeof(DrawingLayerBehaviour).GetField("pixels", Any).SetValue(drawing, sourceTexture);
            source.layers.Add(new Layer(drawing));
            WhimTexDocumentFile.Save(source, path);
            UnityEngine.Object.DestroyImmediate(source);
            source = null;
            UnityEngine.Object.DestroyImmediate(sourceTexture);
            sourceTexture = null;

            loaded = WhimTexDocumentFile.Load(path);
            var loadedDrawing = (DrawingLayerBehaviour)loaded.layers[0].Behaviour;
            PropertyInfo deferred = typeof(DrawingLayerBehaviour).GetProperty("HasDeferredTexture", Any);
            PropertyInfo stored = typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any);
            Check((bool)deferred.GetValue(loadedDrawing), "TIFF load keeps Drawing texture deferred");
            Check(typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(loadedDrawing) == null,
                "deferred Drawing has no Texture2D before first access");
            var materialized = (Texture2D)stored.GetValue(loadedDrawing);
            Check(materialized != null && !(bool)deferred.GetValue(loadedDrawing),
                "first StoredTexture access materializes Drawing");
            Check(materialized.width == 64 && materialized.height == 32, "materialized dimensions survive");
            Check(materialized.GetPixel(0, 0).b > .7f, "materialized pixels survive");
            WhimTexDocumentFile.Save(loaded, path);
            return null;
        }
        finally
        {
            if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (sourceTexture != null) UnityEngine.Object.DestroyImmediate(sourceTexture);
            { /* Asset deletion is owned by UnityAScope. */ }
        }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

