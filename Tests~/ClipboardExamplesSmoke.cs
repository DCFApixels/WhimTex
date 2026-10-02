using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class ClipboardExamplesSmoke
{
    const string Root = "Packages/com.dcfapixels.whimtex/";
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static object Read(string json) => typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 512, 512 });
    static TextureCompositor Document(object data) => (TextureCompositor)data.GetType().GetField("Document", F).GetValue(data);
    static void Compile(object data) => data.GetType().GetMethod("Compile", F).Invoke(data, null);
    public static string Run() => CheckExamples(false);
    public static string Generate() => CheckExamples(true);
    static string CheckExamples(bool generate)
    {
        int count = 0;
        float maximum = 0;
        foreach (string file in Directory.GetFiles(Root + "Tests~/Fixtures/LegacyClipboard", "*.json"))
        {
            if (Path.GetFileName(file) == "stone-wall-retro.json") continue;
            using var legacy = (IDisposable)Read(File.ReadAllText(file));
            Compile(legacy);
            var source = Document(legacy);
            var filter = legacy.GetType().GetField("CanvasFilter", F).GetValue(legacy);
            if (filter is FilterMode mode) source.outputFilter = mode;
            string destination = Root + "Documentation~/Examples/Clipboard/" + Path.GetFileName(file);
            string json = generate ? WhimTexDocumentJson.Write(source).Json : File.ReadAllText(destination);
            if (!json.Contains("\"whimtex.document\"")) throw new Exception(destination + ": not unified JSON");
            using var current = (IDisposable)Read(json);
            Compile(current);
            var restored = Document(current);
            if (restored.width != source.width || restored.height != source.height || restored.layers.Count != source.layers.Count)
                throw new Exception(destination + ": document context changed");
            var before = Pixels(source);
            var after = Pixels(restored);
            for (int i = 0; i < before.Length; i++)
                for (int channel = 0; channel < 4; channel++)
                {
                    float delta = Mathf.Abs(before[i][channel] - after[i][channel]);
                    if (float.IsNaN(delta) || delta > 0.002f) throw new Exception(destination + ": render mismatch " + delta);
                    maximum = Mathf.Max(maximum, delta);
                }
            if (generate) File.WriteAllText(destination, json + "\n");
            count++;
        }
        return "PASS: " + count + " unified clipboard recipes; legacy/current rendering max delta " + maximum;
    }
    static Color[] Pixels(TextureCompositor document)
    {
        var previous = RenderTexture.active;
        RenderTexture rendered = null;
        Texture2D pixels = null;
        try
        {
            rendered = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview", F).Invoke(document, new object[] { 64 });
            if (rendered == null) throw new Exception("Missing preview");
            pixels = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = rendered;
            pixels.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
        }
    }
}
