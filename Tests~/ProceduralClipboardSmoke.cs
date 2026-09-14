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
        Reject(head + "[]}");
        Reject(head + "[{\"type\":\"drawing\"}]}");
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
            using var data = (IDisposable)Build("{\"format\":\"whimtex.layers\",\"version\":1,\"canvas\":{\"width\":64,\"height\":96},\"layers\":[{\"type\":\"color\"}]}");
            var method = typeof(TextureCompositorWindow).GetMethod("PasteCopiedLayers", Hidden);
            method.Invoke(window, new object[] { Document(data), true });
            Check(document.width == 64 && document.height == 96 && document.layers.Count == 1, "Window resize paste failed.");
            Undo.PerformUndo();
            Check(document.width == oldWidth && document.height == oldHeight && document.layers.Count == 0, "Window paste Undo failed.");
            Undo.PerformRedo();
            Check(document.width == 64 && document.layers.Count == 1, "Window paste Redo failed.");
            Document(data).width = 32;
            method.Invoke(window, new object[] { Document(data), false });
            Check(document.width == 64 && document.layers.Count == 2, "Keep-size paste failed.");
            Undo.ClearUndo(document);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
        return $"Procedural clipboard: {checks} checks passed; examples, HLSL, validation, IDs, targets, Undo/Redo and canvas resize.";
    }
}
