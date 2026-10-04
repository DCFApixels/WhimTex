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
    [Serializable] public sealed class Entry { public string title, recipe, preview; public int layers; public Canvas canvas; public bool outputSrgb; }
    [Serializable] public sealed class Canvas { public int width, height; public string filter; }
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
    public static string Run(int start = 0, int count = 38)
    {
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var manifest = (Manifest)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { File.ReadAllText(Folder + "manifest.json"), typeof(Manifest) });
        Check(manifest.samples.Length == 38, "Expected 38 samples.");
        Check(start >= 0 && start < manifest.samples.Length && count > 0, "Invalid sample range.");
        var ldr = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.HdrUtility")
            .GetMethod("ToLdr", BindingFlags.Static | BindingFlags.NonPublic);
        int checkedCount = 0;
        for (int sampleIndex = start; sampleIndex < Math.Min(start + count, manifest.samples.Length); sampleIndex++)
        {
            var entry = manifest.samples[sampleIndex];
            Texture2D preview = null, rendered = null, encoded = null;
            try
            {
                int width = entry.canvas.width, height = entry.canvas.height;
                Check(Math.Max(width, height) == 256, entry.title + " canvas dimensions.");
                using var recipe = WhimTexDocumentJson.Read(File.ReadAllText(Folder + entry.recipe));
                var generated = recipe.Document;
                Check(generated.outputFilter.ToString() == entry.canvas.filter && generated.outputSrgb == entry.outputSrgb, "Stored output settings differ.");
                rendered = generated.ComposeCanvas();
                Check(CheckLayers(generated.layers) == entry.layers, entry.title + " layer count.");
                Check(rendered.width == width && rendered.height == height, entry.title + " render dimensions.");
                double alpha = 0;
                foreach (var pixel in rendered.GetPixels())
                {
                    Check(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b) && float.IsFinite(pixel.a), entry.title + " non-finite output.");
                    alpha += pixel.a;
                }
                Check(alpha > 1, entry.title + " is empty.");
                if (entry.outputSrgb) encoded = (Texture2D)ldr.Invoke(null, new object[] { rendered, false });
                else
                {
                    encoded = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                    encoded.SetPixels(rendered.GetPixels()); encoded.Apply();
                    if (entry.recipe == "Sphere_Distortion.whimtex.json")
                    {
                        var neutral = rendered.GetPixel(0, 0);
                        Check(Math.Abs(neutral.r - .5f) < .001f && Math.Abs(neutral.g - .5f) < .001f && neutral.b == 0,
                            "Sphere vector field must retain neutral RG=0.5, B=0.");
                    }
                }
                preview = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Check(preview.LoadImage(File.ReadAllBytes(Folder + entry.preview)), entry.title + " invalid PNG.");
                Check(preview.width == width && preview.height == height, entry.title + " PNG dimensions.");
                var actual = encoded.GetPixels32(); var expected = preview.GetPixels32();
                double difference = 0; int maximum = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    var a = actual[i]; var b = expected[i];
                    int r=Math.Abs(a.r-b.r), g=Math.Abs(a.g-b.g), blue=Math.Abs(a.b-b.b), opacity=Math.Abs(a.a-b.a);
                    difference += r+g+blue+opacity;
                    maximum = Math.Max(maximum, Math.Max(Math.Max(r,g),Math.Max(blue,opacity)));
                }
                Check(maximum <= 2 && difference / (actual.Length * 4) <= .05,
                    entry.title + " preview differs: byte MAE=" + difference / (actual.Length * 4) + ", max=" + maximum);
                checkedCount++;
            }
            finally
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                if (rendered != null) UnityEngine.Object.DestroyImmediate(rendered);
                if (encoded != null) UnityEngine.Object.DestroyImmediate(encoded);
            }
        }
        return "PASS: " + checkedCount + " recipes from index " + start + " compile without warnings, preserve dimensions/layer counts and match their individual PNG previews.";
    }
}
