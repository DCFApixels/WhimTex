using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class ClipboardExamplesTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const string Root = "Packages/com.dcfapixels.whimtex/";
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static object Read(string json) => typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 512, 512 });
    static TextureCompositor Document(object data) => (TextureCompositor)data.GetType().GetField("Document", F).GetValue(data);
    static void Compile(object data) => data.GetType().GetMethod("Compile", F).Invoke(data, null);
    private static string BodyRun()
    {
        int count = 0;
        float maximum = 0;
        foreach (string file in Directory.GetFiles(Root + "Documentation~/Examples/Clipboard", "*.json"))
        {
            using var input = (IDisposable)Read(File.ReadAllText(file));
            Compile(input);
            var source = Document(input);
            string json = WhimTexDocumentJson.Write(source).Json;
            T.True(!(!json.Contains("\"whimtex.document\"")), file + ": not unified JSON");
            using var current = (IDisposable)Read(json);
            Compile(current);
            var restored = Document(current);
            T.True(!(restored.width != source.width || restored.height != source.height || restored.layers.Count != source.layers.Count), file + ": document context changed");
            var before = Pixels(source);
            var after = Pixels(restored);
            for (int i = 0; i < before.Length; i++)
                for (int channel = 0; channel < 4; channel++)
                {
                    float delta = Mathf.Abs(before[i][channel] - after[i][channel]);
                    T.True(!(float.IsNaN(delta) || delta > 0.002f), file + ": render mismatch " + delta);
                    maximum = Mathf.Max(maximum, delta);
                }
            count++;
        }
        return null;
    }
    static Color[] Pixels(TextureCompositor document)
    {
        var previous = RenderTexture.active;
        RenderTexture rendered = null;
        Texture2D pixels = null;
        try
        {
            rendered = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderCanvas", F).Invoke(document, new object[] { 64 });
            T.True(!(rendered == null), "Missing preview");
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
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

