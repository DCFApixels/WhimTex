// Run with Unity Pipeline run_script, entry ProceduralClipboardSmoke.Main.
// Only temporary documents are used. No scenes or project assets are changed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ProceduralClipboardSmoke
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Read = typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", BindingFlags.NonPublic | BindingFlags.Static);
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Build(string text) => Read.Invoke(null, new object[] { text, 128, 128 });
    static TextureCompositor Document(object value) => (TextureCompositor)value.GetType().GetField("Document", Hidden).GetValue(value);
    static void Compile(object value) => value.GetType().GetMethod("Compile", Hidden).Invoke(value, null);
    static void Render(TextureCompositor document)
    {
        RenderTexture previous = RenderTexture.active;
        var rendered = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview", Hidden).Invoke(document, new object[] { 64 });
        try { Check(rendered != null && rendered.width > 0, "Preview render failed."); Check(RenderTexture.active == previous, "Render target leaked."); }
        finally { if (rendered != null) RenderTexture.ReleaseTemporary(rendered); }
    }
    static void Reject(string text)
    {
        object result = null;
        try { result = Build(text); }
        catch (TargetInvocationException) { checks++; return; }
        finally { (result as IDisposable)?.Dispose(); }
        throw new Exception("Accepted invalid JSON: " + text);
    }
    public static string Main()
    {
        string folder = "Packages/com.dcfapixels.whimtex/Documentation~/Examples/Clipboard";
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            using var data = (IDisposable)Build(File.ReadAllText(file));
            Compile(data);
            Check(Document(data).layers.Count > 0, file + " did not create layers.");
            Render(Document(data));
        }
        const string head = "{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":";
        foreach (string mode in new[] { "Point", "Bilinear", "Trilinear" })
        {
            using var filtered = (IDisposable)Build(head + "[{\"type\":\"color\"}],\"canvas\":{\"width\":64,\"height\":96,\"filter\":\"" + mode + "\"}}");
            Check(filtered.GetType().GetField("CanvasFilter", Hidden).GetValue(filtered).ToString() == mode, "Canvas filter parsing failed.");
        }
        Reject(head + "[{\"type\":\"color\"}],\"canvas\":{\"width\":64,\"height\":96,\"filter\":\"Source\"}}");
        Reject(head + "[{\"type\":\"color\"}],\"canvas\":{\"width\":64,\"height\":96,\"filter\":null}}");
        Reject(head + "[]}");
        // A Drawing layer is either empty or points at a link that is fetched before the paste.
        Reject(head + "[{\"type\":\"drawing\",\"url\":\"ftp://example.com/a.png\"}]}");
        Reject(head + "[{\"type\":\"drawing\",\"url\":\"/local/a.png\"}]}");
        Reject(head + "[{\"type\":\"drawing\",\"url\":\"not a link\"}]}");
        Reject(head + "[{\"type\":\"color\",\"url\":\"https://example.com/a.png\"}]}");
        Reject(head + "[{\"type\":\"drawing\",\"url\":\"https://example.com/a.png\",\"transform\":{\"scale\":[2,2]}}]}");
        using (var empty = (IDisposable)Build(head + "[{\"type\":\"drawing\"}]}"))
            Check(Document(empty).layers.Count == 1, "An empty Drawing layer is allowed.");
        using (var linked = (IDisposable)Build(head + "[{\"type\":\"drawing\",\"url\":\"https://example.com/a.png\"},{\"type\":\"color\"}]}"))
        {
            var images = (IList)linked.GetType().GetField("Images", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(linked);
            Check(images != null && images.Count == 1, "A Drawing layer link is queued for download.");
            Check(Document(linked).layers.Count == 2, "The link keeps its place in the tree.");
        }
        string LinkLayers(int count)
        {
            var text = new System.Text.StringBuilder(head + "[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append("{\"type\":\"drawing\",\"url\":\"https://example.com/a.png\"}");
            }
            return text.Append("]}").ToString();
        }
        Reject(LinkLayers(17));
        using (var sixteen = (IDisposable)Build(LinkLayers(16)))
            Check(((IList)sixteen.GetType().GetField("Images", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sixteen)).Count == 16,
                "Sixteen linked images are accepted.");
        Reject(head + "[{\"type\":\"file\",\"properties\":{\"source\":\"Assets/x.png\"}}]}");
        Reject(head + "[{\"type\":\"color\",\"properties\":{\"opacity\":2}}]}");
        Reject(head + "[{\"type\":\"color\",\"properties\":{\"unknown\":1}}]}");
        Reject(head + "[{\"type\":\"noise\",\"properties\":{\"noise\":{\"noiseType\":\"Fake\"}}}]}");
        Reject(head + "[{\"type\":\"blur\",\"target\":\"missing\"}]}");
        Reject(head + "[{\"type\":\"blur\",\"id\":\"self\",\"target\":\"self\"}]}");
        Reject(head + "[{\"type\":\"blur\",\"id\":\"a\",\"target\":\"b\"},{\"type\":\"blur\",\"id\":\"b\",\"target\":\"a\"}]}");
        Reject(head + "[{\"type\":\"group\",\"id\":\"g\",\"children\":[{\"type\":\"blur\",\"target\":\"g\"}]}]}");
        Reject(head + "[{\"type\":\"color\",\"id\":\"a\"},{\"type\":\"color\",\"id\":\"a\"}]}");
        Reject(head + "[{\"type\":\"color\",\"children\":[]}]}");
        Reject(head + "[{\"type\":\"color\",\"transform\":{\"scale\":[0,1]}}]}");
        Reject(head + "[{\"type\":\"color\",\"type\":\"noise\"}]}");
        Reject(head + "[{\"type\":\"color\"}],\"canvas\":{\"width\":16384,\"height\":16384}}");
        Reject(head + "[{\"type\":\"color\"}],\"canvas\":{\"width\":512}}");
        Reject(head + "[{\"type\":\"shaderProcessor\",\"fx\":[{\"code\":\"#include something\"}]}]}");
        Reject(head + "[{\"type\":\"shaderProcessor\",\"fx\":[{\"code\":\"// @param texture2D _Tex = guid:abc\"}]}]}");
        Reject(head + "[{\"type\":\"color\"}],\"version\":2}");
        Reject(head + "[" + string.Join(",", new string[129]).Replace(",", "{\"type\":\"color\"},") + "{\"type\":\"color\"}]}");
        Reject(new string(' ', 1024 * 1024 + 1));
        string nested = "{\"type\":\"color\"}";
        for (int i = 0; i < 9; i++) nested = "{\"type\":\"group\",\"children\":[" + nested + "]}";
        Reject(head + "[" + nested + "]}");
        using (var fenced = (IDisposable)Build("```json\n" + head + "[{\"type\":\"color\"}]}\n```"))
            Check(Document(fenced).layers.Count == 1, "Fenced JSON failed.");
        foreach (string type in new[] { "outline", "sdf", "normalMap", "blur", "makeSeamless" })
        {
            using var effect = (IDisposable)Build(head + "[{\"type\":\"" + type + "\",\"target\":\"base\"},{\"type\":\"shape\",\"id\":\"base\"}]}");
            Render(Document(effect));
        }
        using (var badShader = (IDisposable)Build(head + "[{\"type\":\"shaderProcessor\",\"fx\":[{\"code\":\"float4 ApplyFX(float2 uv, float4 color) { return missing_symbol; }\"}]}]}"))
        {
            bool rejected = false;
            try { Compile(badShader); } catch (TargetInvocationException) { rejected = true; }
            Check(rejected, "Invalid shader compiled.");
        }

        var destination = ScriptableObject.CreateInstance<TextureCompositor>();
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
        finally { Undo.ClearUndo(destination); UnityEngine.Object.DestroyImmediate(destination); }

        var shaderDestination = ScriptableObject.CreateInstance<TextureCompositor>();
        shaderDestination.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            using (var data = (IDisposable)Build(File.ReadAllText(Path.Combine(folder, "local-distortion.json"))))
            {
                Compile(data);
                paste.Invoke(shaderDestination, new object[] { Document(data) });
                Check(shaderDestination.layers[0].modifiers[0] != Document(data).layers[0].modifiers[0], "Shader instance was shared with the temporary source.");
            }
            Check(shaderDestination.layers[0].modifiers[0] != null, "Shader was destroyed with source.");
            Render(shaderDestination);
            Undo.PerformUndo();
            Check(shaderDestination.layers.Count == 0, "Shader paste Undo failed.");
            Undo.PerformRedo();
            Check(shaderDestination.layers.Count == 1 && shaderDestination.layers[0].modifiers[0] != null, "Shader paste Redo failed.");
            Render(shaderDestination);
        }
        finally { Undo.ClearUndo(shaderDestination); UnityEngine.Object.DestroyImmediate(shaderDestination); }

        // Exercise the actual window paste helper, including its resize transaction.
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        try
        {
            var document = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Hidden).GetValue(window);
            int oldWidth = document.width, oldHeight = document.height;
            FilterMode oldFilter = document.outputFilter;
            using var data = (IDisposable)Build("{\"format\":\"whimtex.layers\",\"version\":1,\"canvas\":{\"width\":64,\"height\":96},\"layers\":[{\"type\":\"color\"}]}");
            var method = typeof(TextureCompositorWindow).GetMethod("PasteCopiedLayers", Hidden);
            method.Invoke(window, new object[] { Document(data), true, FilterMode.Point });
            Check(document.outputFilter == FilterMode.Point, "Canvas filter paste failed.");
            Check(document.width == 64 && document.height == 96 && document.layers.Count == 1, "Window resize paste failed.");
            Undo.PerformUndo();
            Check(document.outputFilter == oldFilter, "Canvas filter Undo failed.");
            Check(document.width == oldWidth && document.height == oldHeight && document.layers.Count == 0, "Window paste Undo failed.");
            Undo.PerformRedo();
            Check(document.outputFilter == FilterMode.Point, "Canvas filter Redo failed.");
            Check(document.width == 64 && document.layers.Count == 1, "Window paste Redo failed.");
            Document(data).width = 32;
            method.Invoke(window, new object[] { Document(data), false, null });
            Check(document.outputFilter == FilterMode.Point, "Omitted filter changed canvas.");
            method.Invoke(window, new object[] { Document(data), false, FilterMode.Trilinear });
            Check(document.width == 64 && document.outputFilter == FilterMode.Trilinear, "Filter without resizing failed.");
            Undo.PerformUndo();
            Check(document.width == 64 && document.layers.Count == 2, "Keep-size paste failed.");
            Undo.ClearUndo(document);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
        // A linked Drawing layer keeps the downloaded resolution: the paste must not resample it to the canvas.
        var adopt = typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", Hidden);
        var stored = typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Hidden);
        var fit = typeof(Layer).GetMethod("TryGetOriginalAspectTransform", Hidden);
        var linkedDestination = ScriptableObject.CreateInstance<TextureCompositor>();
        linkedDestination.hideFlags = HideFlags.HideAndDontSave;
        linkedDestination.width = 64; linkedDestination.height = 64;
        try
        {
            using (var data = (IDisposable)Build(head + "[{\"type\":\"drawing\",\"url\":\"https://example.com/a.png\"}]}"))
            {
                Layer linked = Document(data).layers[0];
                var image = new Texture2D(96, 48, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
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
            UnityEngine.Object.DestroyImmediate(linkedDestination);
        }
        return $"Procedural clipboard: {checks} checks passed; examples, HLSL, validation, IDs, targets, Undo/Redo, canvas resize and linked image resolution.";
    }
}
