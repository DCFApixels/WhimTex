// Run with Unity Pipeline run_script, entry ProceduralClipboardTests.Main.
// Only temporary documents are used. No scenes or project assets are changed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ProceduralClipboardTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Read = typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", BindingFlags.NonPublic | BindingFlags.Static);
    static int checks;
    static void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static object Build(string text) => Read.Invoke(null, new object[] { text, 128, 128 });
    static TextureCompositor Document(object value) => (TextureCompositor)value.GetType().GetField("Document", Hidden).GetValue(value);
    static void Compile(object value) => value.GetType().GetMethod("Compile", Hidden).Invoke(value, null);
    static void Render(TextureCompositor document)
    {
        RenderTexture previous = RenderTexture.active;
        var rendered = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderCanvas", Hidden).Invoke(document, new object[] { 64 });
        try { Check(rendered != null && rendered.width > 0, "Preview render failed."); Check(RenderTexture.active == previous, "Render target leaked."); }
        finally { if (rendered != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rendered); }
    }
    static void Reject(string text)
    {
        object result = null;
        try { result = Build(text); }
        catch (TargetInvocationException) { WhimTex.Tests.UnityC.FixtureContext.Context.True(true, "Expected rejection was observed"); checks++; return; }
        finally { (result as IDisposable)?.Dispose(); }
        WhimTex.Tests.UnityC.FixtureContext.Context.True(false, "Accepted invalid JSON: " + text);
    }
    static string ExecuteMain()
    {
        string folder = "Packages/com.dcfapixels.whimtex/Documentation~/Examples/Clipboard";
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            using var data = (IDisposable)Build(File.ReadAllText(file));
            Compile(data);
            Check(Document(data).layers.Count > 0, file + " did not create layers.");
            Render(Document(data));
        }
        const string plain = "{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"color\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}]}";
        Check((bool)typeof(WhimTexApi).GetMethod("IsProceduralClipboard", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "{'format':'whimtex.layers','version':1,'layers':[{'type':'color'}]}" }),
            "Retired JSON must route to rejection, not fall back to a stale native layer copy.");
        Reject("{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[{\"type\":\"color\"}]}");
        Reject(plain.Replace("whimtex.document", "unknown.document"));
        Reject(plain.Replace("\"version\":1", "\"version\":2"));
        Reject(plain.Replace("\"ColorFillLayerBehaviour\"", "\"UnknownLayerBehaviour\""));
        Reject(plain.Replace("\"id\":\"color\"", "\"id\":\"\""));
        Reject(plain.Replace("\"id\":\"color\"", "\"id\":\"color\",\"unexpected\":1"));
        Reject(plain.Replace("\"version\":1", "\"version\":1,\"version\":1"));
        Reject("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"blur\",\"behaviour\":{\"$type\":\"BlurLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"missing\"}}]}");
        Reject("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"blur\",\"behaviour\":{\"$type\":\"BlurLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"blur\"}}]}");
        Reject("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"a\",\"behaviour\":{\"$type\":\"BlurLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"b\"}},{\"id\":\"b\",\"behaviour\":{\"$type\":\"BlurLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"a\"}}]}");
        Reject("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"color\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}},{\"id\":\"color\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\"}}]}");
        Reject("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"drawing\",\"behaviour\":{\"$type\":\"DrawingLayerBehaviour\"},\"url\":\"https://example.com/a.png\"}]}");
        using (var fenced = (IDisposable)Build("```json\n" + plain + "\n```"))
            Check(Document(fenced).layers.Count == 1, "Fenced JSON failed.");
        using (var bom = (IDisposable)Build("\uFEFF" + plain))
            Check(Document(bom).layers.Count == 1, "BOM JSON failed.");
        using (var inherited = (IDisposable)Build(plain))
        {
            Check(Document(inherited).width == 128 && Document(inherited).height == 128, "Omitted dimensions inherit destination.");
            Check(!(bool)inherited.GetType().GetField("HasCanvas", Hidden).GetValue(inherited), "Omitted size must not prompt.");
        }

        var destination = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        destination.hideFlags = HideFlags.HideAndDontSave;
        destination.width = 128; destination.height = 128;
        var paste = typeof(TextureCompositor).GetMethod("PasteLayers", Hidden);
        int undo = -1;
        try
        {
            using var data = (IDisposable)Build(File.ReadAllText(Path.Combine(folder, "mystic-fog.json")));
            TextureCompositor source = Document(data);
            paste.Invoke(destination, new object[] { source });
            Layer group = destination.layers[0];
            Check(group.Id != source.layers[0].Id, "Pasted ID was reused.");
            var target = (TargetedLayerBehaviour)group.children[1].Behaviour;
            Check(target.TargetLayerId == group.children[2].Id, "Forward target was not remapped.");
            Check(!group.children[2].enabled, "Hidden source was enabled.");
            string firstId = group.Id;
            paste.Invoke(destination, new object[] { source });
            Check(destination.layers[0].Id != firstId, "Repeated paste duplicated an ID.");
            Undo.PerformUndo();
            Check(destination.layers.Count == 1 && destination.layers[0].Id == firstId, "Undo did not remove one paste.");
            Undo.PerformRedo();
            Check(destination.layers.Count == 2, "Redo failed.");

            // Resize and nested paste must undo as one operation, as in the window handler.
            Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(destination, "Test JSON canvas");
            destination.width = 256; destination.height = 64;
            paste.Invoke(destination, new object[] { source });
            Undo.CollapseUndoOperations(undo);
            Undo.PerformUndo();
            Check(destination.width == 128 && destination.height == 128 && destination.layers.Count == 2, "Resize+paste Undo was not atomic.");
            Undo.PerformRedo();
            Check(destination.width == 256 && destination.height == 64 && destination.layers.Count == 3, "Resize+paste Redo failed.");
        }
        finally { Undo.ClearUndo(destination); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(destination); }

        var shaderDestination = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        shaderDestination.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            using (var data = (IDisposable)Build(File.ReadAllText(Path.Combine(folder, "local-distortion.json"))))
            {
                Compile(data);
                paste.Invoke(shaderDestination, new object[] { Document(data) });
                Check(shaderDestination.layers[0].fx[0] != Document(data).layers[0].fx[0], "Shader instance was shared with the temporary source.");
            }
            Check(shaderDestination.layers[0].fx[0] != null, "Shader was destroyed with source.");
            Render(shaderDestination);
            Undo.PerformUndo();
            Check(shaderDestination.layers.Count == 0, "Shader paste Undo failed.");
            Undo.PerformRedo();
            Check(shaderDestination.layers.Count == 1 && shaderDestination.layers[0].fx[0] != null, "Shader paste Redo failed.");
            Render(shaderDestination);
        }
        finally { Undo.ClearUndo(shaderDestination); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(shaderDestination); }

        // Exercise the actual window paste helper, including its resize transaction.
        var window = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        try
        {
            var document = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Hidden).GetValue(window);
            int oldWidth = document.width, oldHeight = document.height;
            FilterMode oldFilter = document.outputFilter;
            using var data = (IDisposable)Build("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"color\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}],\"document\":{\"width\":64,\"height\":96,\"outputFilter\":\"Point\"}}");
            var method = typeof(TextureCompositorWindow).GetMethod("PasteCopiedLayers", Hidden);
            method.Invoke(window, new object[] { Document(data), true });
            Check(document.outputFilter == oldFilter, "JSON paste must preserve destination output filter.");
            Check(document.width == 64 && document.height == 96 && document.layers.Count == 1, "Window resize paste failed.");
            Undo.PerformUndo();
            Check(document.outputFilter == oldFilter, "Canvas filter Undo failed.");
            Check(document.width == oldWidth && document.height == oldHeight && document.layers.Count == 0, "Window paste Undo failed.");
            Undo.PerformRedo();
            Check(document.outputFilter == oldFilter, "Redo changed destination filter.");
            Check(document.width == 64 && document.layers.Count == 1, "Window paste Redo failed.");
            Document(data).width = 32;
            method.Invoke(window, new object[] { Document(data), false });
            Check(document.outputFilter == oldFilter, "Keep-size paste changed filter.");
            method.Invoke(window, new object[] { Document(data), false });
            Check(document.width == 64 && document.outputFilter == oldFilter, "Keep-size paste changed document context.");
            Undo.PerformUndo();
            Check(document.width == 64 && document.layers.Count == 2, "Keep-size paste failed.");
            Undo.ClearUndo(document);
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(window); }
        // A linked Drawing layer keeps the downloaded resolution: the paste must not resample it to the canvas.
        var adopt = typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", Hidden);
        var stored = typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Hidden);
        var fit = typeof(Layer).GetMethod("TryGetOriginalAspectTransform", Hidden);
        var linkedDestination = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        linkedDestination.hideFlags = HideFlags.HideAndDontSave;
        linkedDestination.width = 64; linkedDestination.height = 64;
        try
        {
            using (var data = (IDisposable)Build("{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"drawing\",\"behaviour\":{\"$type\":\"DrawingLayerBehaviour\"}}]}"))
            {
                Layer linked = Document(data).layers[0];
                var image = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(96, 48, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave });
                adopt.Invoke(linked.Behaviour, new object[] { image });
                object[] fitted = { Document(data), null, false };
                Check((bool)fit.Invoke(linked, fitted), "A linked layer did not fit its transform.");
                linked.transform = (TextureTransform)fitted[1];
                Check(Math.Abs(linked.transform.scale.x - 1d) < .0001f && Math.Abs(linked.transform.scale.y - .5d) < .0001f,
                    "A 96x48 image was not fitted to a 64x64 canvas.");
                paste.Invoke(linkedDestination, new object[] { Document(data) });
                var pasted = (DrawingLayerBehaviour)linkedDestination.layers[0].Behaviour;
                var pastedTexture = (Texture2D)stored.GetValue(pasted);
                Check(pastedTexture != null && pastedTexture.width == 96 && pastedTexture.height == 48,
                    "The pasted image lost its source resolution.");
                Check(pastedTexture != image, "The pasted layer shares the downloaded texture.");
                Render(linkedDestination);
            }
            Undo.PerformUndo();
            Check(linkedDestination.layers.Count == 0, "Linked paste Undo failed.");
        }
        finally
        {
            Undo.ClearUndo(linkedDestination);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(linkedDestination);
        }
        return $"Procedural clipboard: {checks} checks passed; examples, HLSL, validation, IDs, targets, Undo/Redo, canvas resize and linked image resolution.";
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("ProceduralClipboardTests.Main", () => { ExecuteMain(); });
}
