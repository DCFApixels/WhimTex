using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class ShaderGraphReloadTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Prefix = "WhimTex Shader Graph Reload ";
    static WhimTex.Tests.TestContext context;
    static object Get(object owner, string name) => owner.GetType().GetField(name, F).GetValue(owner);
    static object Property(object owner, string name) => owner.GetType().GetProperty(name, F).GetValue(owner);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, F).Invoke(owner, args);
    static void Check(bool success, string message) => context.True(success, message);
    static string Key(string id) => Prefix + id;
    static ShaderGraphWindow[] Windows(string id) => Resources.FindObjectsOfTypeAll<ShaderGraphWindow>()
        .Where(w => w.name.StartsWith(Key(id) + "/")).OrderBy(w => w.name).ToArray();
    static ShaderGraph SavedGraph(ShaderGraphWindow window) => (ShaderGraph)Property(Property(window, "Target"), "Graph");
    static string Snapshot(ShaderGraph graph) => JsonUtility.ToJson(graph);

    public static async Task<string> Begin(string id)
    {
        context = new WhimTex.Tests.TestContext();
        try
        {
            UnityBReload.Begin("ShaderGraph", id, context);
            await Prepare(id);
            UnityBReload.Arm("ShaderGraph", id);
            return WhimTex.Tests.TestContext.Result("passed", context.Checks, "Shader Graph reload prepared.").ToJson();
        }
        catch (Exception error) { return WhimTex.Tests.TestContext.Result("failed", context.Checks, "Shader Graph reload preparation", error.ToString()).ToJson(); }
    }
    public static string Trigger(string id) => UnityBReload.Trigger("ShaderGraph", id);
    public static string ReloadPoll(string id) => UnityBReload.ReloadPoll("ShaderGraph", id);

    static async Task Prepare(string id)
    {
        Check(Guid.TryParseExact(id, "N", out _), "A per-run GUID is required.");
        Check(Windows(id).Length == 0, "This run already has windows.");
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.name = Key(id);
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 32;
        for (int i = 0; i < 3; i++)
        {
            var shader = new ShaderLayerBehaviour();
            shader.layerName = "Reload " + i;
            shader.draftGraph.parameters[0].colorValue = new Color(.2f, .5f, .8f, 1);
            var graph = shader.draftGraph;
            var add = new ShaderGraphNode { kind = ShaderGraphNodeKind.Add, position = new Vector2(250, 70) };
            graph.nodes[1].position = new Vector2(510, 70);
            graph.edges[0].inputNode = add.id;
            graph.edges[0].inputPort = "A";
            graph.edges.Add(new ShaderGraphEdge { outputNode = add.id, inputNode = graph.nodes[1].id, inputPort = "RGBA" });
            graph.nodes.Add(add);
            document.layers.Add(shader);
        }
        Call(document, "NormalizeModel");
        var fx = (ShaderFX)Call(document, "AddEmbeddedShaderFX", document.layers[2]);
        var fxProgram = (ShaderProgram)Property(fx, "Program");
        fxProgram.mode = ShaderProgramMode.Graph;
        // A resource value and disconnected node also have to survive a native reload.
        var literal = new ShaderGraphNode { kind = ShaderGraphNodeKind.Value, valueType = ShaderGraphValueType.Float,
            literal = new ShaderParameter { type = ShaderParameterType.Float, floatValue = .375f },
            position = new Vector2(40, 370), previewExpanded = false };
        fxProgram.draftGraph.nodes.Add(literal);
        for (int i = 0; i < 3; i++)
        {
            if (i == 2) ShaderGraphWindow.Open(fx, document.layers[i], document);
            else ShaderGraphWindow.Open((ShaderLayerBehaviour)document.layers[i].Behaviour, document);
            var window = Resources.FindObjectsOfTypeAll<ShaderGraphWindow>().Single(w =>
                (WhimTexDocument)Get(w, "document") == document && (string)Get(w, "layerId") == document.layers[i].Id);
            window.name = Key(id) + "/" + i;
            window.ShowUtility();
            window.position = new Rect(50 + i * 40, 50 + i * 40, 850, 650);
            window.GetType().GetField("svmPreviews", F).SetValue(window, i == 1);
            window.CreateGUI();
        }
        foreach (var window in Windows(id))
        {
            window.Focus();
            Call(Get(window, "canvas"), "FrameAll");
            await WaitForPreviews(window);
            SessionState.SetString(window.name + "/graph", Snapshot(SavedGraph(window)));
            SessionState.SetString(window.name + "/pixels", Pixels((RenderTexture)Get(Get(window, "preview"), "source")));
        }
    }

    static async Task WaitForPreviews(ShaderGraphWindow window)
    {
        var deadline = EditorApplication.timeSinceStartup + 15;
        while (EditorApplication.timeSinceStartup < deadline)
        {
            var preview = Get(window, "preview");
            var pixels = preview == null ? null : Get(preview, "source") as RenderTexture;
            var canvas = Get(window, "canvas");
            var sources = canvas == null ? null : (IDictionary)Get(canvas, "previews");
            if (pixels != null && sources != null && sources.Count > 0 &&
                sources.Values.Cast<object>().Any(v => (bool)Property(Get(v, "source"), "HasImage")))
                return;
            await Task.Delay(50);
        }
        throw new Exception("Graph/node previews did not recover for " + window.name);
    }

    static string Pixels(RenderTexture source, Color? expectedColor = null)
    {
        Check(source != null, "Rendered output must exist.");
        var previous = RenderTexture.active;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = source;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            var color = copy.GetPixel(source.width / 2, source.height / 2);
            var expected = (expectedColor ?? new Color(.2f, .5f, .8f, 1)).linear;
            Check(Mathf.Abs(color.r - expected.r) < .002f && Mathf.Abs(color.g - expected.g) < .002f &&
                Mathf.Abs(color.b - expected.b) < .002f && color.a > .99f, "Output pixel color changed: " + color);
            return Convert.ToBase64String(copy.GetRawTextureData());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }

    public static async Task<string> Verify(string id)
    {
        context = new WhimTex.Tests.TestContext();
        try
        {
            UnityBReload.Verify("ShaderGraph", id, context);
            await VerifyWindows(id);
            return WhimTex.Tests.TestContext.Result("passed", context.Checks,
                "Shader Graph restores document bindings, HLSL/SVM/FX pixels and editing after native reload.").ToJson();
        }
        catch (Exception error) { return WhimTex.Tests.TestContext.Result("failed", context.Checks, "Shader Graph reload", error.ToString()).ToJson(); }
    }

    static async Task VerifyWindows(string id)
    {
        var windows = Windows(id);
        Check(windows.Length == 3, "All three graph windows must survive.");
        foreach (var window in windows)
        {
            var saved = SavedGraph(window);
            Check(Snapshot(saved) == SessionState.GetString(window.name + "/graph", ""), "Editable graph content changed.");
            var root = (ShaderGraph)Property(window, "RootGraph");
            var debug = window.GetType().GetField("debugGraphLayout", F)?.GetValue(window);
            Check(ReferenceEquals(root, saved) && debug == null,
                $"Window uses a detached graph after reload: saved={saved?.nodes.Count}, displayed={root?.nodes.Count}, debug={debug != null}.");
            window.Focus();
            await WaitForPreviews(window);
            Check(Pixels((RenderTexture)Get(Get(window, "preview"), "source")) == SessionState.GetString(window.name + "/pixels", ""),
                "Rendered pixels changed across reload.");
            var canvas = Get(window, "canvas");
            Check(((IDictionary)Get(canvas, "nodeViews")).Count == saved.nodes.Count, "Every stored node must be displayed.");
            if ((bool)Get(window, "svmPreviews"))
                Check(Get(Get(window, "preview"), "renderedVM") != null, "SVM preview must resume after reload.");
            var parameter = saved.parameters.FirstOrDefault();
            if (parameter != null)
            {
                Action<ShaderGraph> edit = graph => graph.parameters[0].colorValue = new Color(.6f, .3f, .1f, 1);
                Call(window, "Edit", "Reload regression edit", edit, false, false, false);
                await Task.Delay(300);
                Check(ReferenceEquals((ShaderGraph)Property(window, "RootGraph"), saved) &&
                    parameter.colorValue.r == .6f, "Editing must reach the document graph.");
                Pixels((RenderTexture)Get(Get(window, "preview"), "source"), parameter.colorValue);
            }
        }
    }

    public static string Cleanup(string id) => UnityBReload.Cleanup("ShaderGraph", id, () => CleanOwned(id));
    static void CleanOwned(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("A per-run GUID is required.");
        foreach (var window in Windows(id))
        {
            SessionState.EraseString(window.name + "/graph");
            SessionState.EraseString(window.name + "/pixels");
            window.Close();
        }
        foreach (var document in Resources.FindObjectsOfTypeAll<WhimTexDocument>().Where(d => d.name == Key(id)))
        {
            UnityBReload.DestroyOwned(document);
        }
    }
}
