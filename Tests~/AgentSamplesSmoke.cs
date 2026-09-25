// Unity Pipeline run_script entry: AgentSamplesSmoke.Run.
// Reads package samples and renders detached documents; never writes project Assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class AgentSamplesSmoke
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Folder = "Packages/com.dcfapixels.whimtex/Samples~/AgentTextures/";
    [Serializable] public sealed class Manifest { public Entry[] samples; }
    [Serializable] public sealed class Entry { public string title, document, recipe; public int layers; }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int CheckLayers(List<Layer> layers)
    {
        int count = 0;
        foreach (var layer in layers)
        {
            count++;
            Check(!string.IsNullOrWhiteSpace(layer.layerName), "Missing layer name.");
            Check(layer.Behaviour != null, "Missing layer behaviour.");
            Check(!(layer.Behaviour is DrawingLayerBehaviour) && !(layer.Behaviour is FileLayerBehaviour), "Unexpected raster dependency.");
            foreach (var modifier in layer.modifiers)
            {
                Check(modifier is ShaderFX, "Missing or non-HLSL modifier.");
                var fx = (ShaderFX)modifier;
                Check(!(bool)typeof(ShaderFX).GetProperty("LastApplyFailed", Hidden).GetValue(fx), fx.name + " failed compilation.");
                Check(!(bool)typeof(ShaderFX).GetProperty("HasPendingChanges", Hidden).GetValue(fx), fx.name + " has pending changes.");
                string diagnostics = (string)typeof(ShaderFX).GetProperty("Diagnostics", Hidden).GetValue(fx);
                Check(diagnostics.IndexOf("warning", StringComparison.OrdinalIgnoreCase) < 0, diagnostics);
            }
            if (layer.children != null) count += CheckLayers(layer.children);
        }
        return count;
    }
    public static string Run()
    {
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var manifest = (Manifest)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { File.ReadAllText(Folder + "manifest.json"), typeof(Manifest) });
        var read = typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", BindingFlags.Static | BindingFlags.NonPublic);
        Check(manifest.samples.Length == 12, "Expected twelve samples.");
        foreach (var entry in manifest.samples)
        {
            TextureCompositor saved = null;
            Texture2D a = null, b = null;
            try
            {
                string path = Path.GetFullPath(Folder + entry.document);
                Check(WhimTexDocumentFile.IsDocument(path), entry.title + " is not an editable TIFF.");
                Check(WhimTexDocumentFile.TryLoad(path, out saved, out string error), entry.title + ": " + error);
                Check(saved.width == 256 && saved.height == 256, entry.title + " canvas dimensions.");
                using var recipe = (IDisposable)read.Invoke(null, new object[] { File.ReadAllText(Folder + entry.recipe), 256, 256 });
                recipe.GetType().GetMethod("Compile", Hidden).Invoke(recipe, null);
                var generated = (TextureCompositor)recipe.GetType().GetField("Document", Hidden).GetValue(recipe);
                generated.outputFilter = FilterMode.Bilinear;
                Check(CheckLayers(saved.layers) == entry.layers, entry.title + " TIFF layer count.");
                Check(CheckLayers(generated.layers) == entry.layers, entry.title + " recipe layer count.");
                a = saved.Compose(); b = generated.Compose();
                Check(a.width == 256 && a.height == 256 && b.width == 256 && b.height == 256, entry.title + " render dimensions.");
                var pa = a.GetPixels(); var pb = b.GetPixels();
                double difference = 0, alpha = 0;
                for (int i = 0; i < pa.Length; i++)
                {
                    var x = pa[i]; var y = pb[i];
                    Check(float.IsFinite(x.r) && float.IsFinite(x.g) && float.IsFinite(x.b) && float.IsFinite(x.a), entry.title + " non-finite output.");
                    difference += Math.Abs(x.r-y.r) + Math.Abs(x.g-y.g) + Math.Abs(x.b-y.b) + Math.Abs(x.a-y.a);
                    alpha += x.a;
                }
                Check(alpha > 1, entry.title + " is empty.");
                Check(difference / (pa.Length * 4) < .001, entry.title + " TIFF differs from the recipe: " + difference / (pa.Length * 4));
            }
            finally
            {
                if (a != null) UnityEngine.Object.DestroyImmediate(a);
                if (b != null) UnityEngine.Object.DestroyImmediate(b);
                if (saved != null) UnityEngine.Object.DestroyImmediate(saved);
            }
        }
        return "PASS: 12 package TIFFs reload at 256x256; all recipes compile without warnings, preserve layer counts and reproduce their TIFF renders.";
    }
}
