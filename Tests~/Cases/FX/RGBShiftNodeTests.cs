using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class RGBShiftNodeTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type TypeOf(string n) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + n);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static object Static(string type, string name, params object[] args) => TypeOf(type).GetMethod(name, F).Invoke(null, args);
    static ShaderGraphNode Node(ShaderGraphNodeKind kind) => (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
    static void Edge(ShaderGraph graph, ShaderGraphNode from, ShaderGraphNode to, string input) => graph.edges.Add(
        new ShaderGraphEdge { outputNode = from.id, outputPort = "Out", inputNode = to.id, inputPort = input });
    static void Set(ShaderGraphNode node, string port, float value) => node.FindInput(port).value = Vector4.one * value;
    static Color Field(Vector2 uv) => new Color(uv.x * 2 - .4f, uv.y * .7f, (uv.x + uv.y) * 1.5f, .2f + uv.x * .6f);
    static Color Expected(Vector2 uv, int w, int h, int mode, float amount, float blend, Vector2 center, float falloff, float angle)
    {
        var original = Field(uv);
        if (amount <= 0 || blend <= 0) return original;
        var delta = uv - center;
        float shift = amount;
        Vector2 direction;
        if (mode == 0)
        {
            direction = delta.magnitude > 1e-6f ? delta.normalized : Vector2.zero;
            shift *= Mathf.Pow(Mathf.Clamp01(delta.magnitude * Mathf.Sqrt(2)), Mathf.Max(falloff, .01f));
        }
        else direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        if (shift == 0) return original;
        Vector2 Clamp(Vector2 p) => new Vector2(Mathf.Clamp(p.x, .5f / w, 1 - .5f / w), Mathf.Clamp(p.y, .5f / h, 1 - .5f / h));
        var offset = new Vector2(direction.x * shift / w, direction.y * shift / h);
        Vector2 OffsetAt(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * amount / w, Mathf.Sin(radians) * amount / h);
        }
        float green = mode == 2 ? Field(Clamp(uv + OffsetAt(angle + 120))).g : original.g;
        var blue = mode == 2 ? uv + OffsetAt(angle + 240) : uv - offset;
        var shifted = new Color(Field(Clamp(uv + offset)).r, green, Field(Clamp(blue)).b, original.a);
        return Color.LerpUnclamped(original, shifted, Mathf.Clamp01(blend));
    }
    public static string Run() => FixtureContext.Run("RGB Shift GPU, HDR, sampling and JSON", () =>
    {
        var t = FixtureContext.Context; var scope = FixtureContext.Scope;
        var doc = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        const int width = 64, height = 32;
        var image = scope.Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); image.Create();
        var read = scope.Own(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true));
        var texture = scope.Own(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true));
        texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
        var data = new Color[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) data[y * width + x] = Field(new Vector2((x + .5f) / width, (y + .5f) / height));
        texture.SetPixels(data); texture.Apply();
        var graph = new ShaderGraph();
        var source = Node(ShaderGraphNodeKind.Expression); source.valueType = ShaderGraphValueType.Float4;
        source.expression = "float4(A.x*2-0.4,A.y*0.7,(A.x+A.y)*1.5,0.2+A.x*0.6)";
        source.inputs.Clear();
        var uvNode = Node(ShaderGraphNodeKind.UV);
        var node = Node(ShaderGraphNodeKind.RGBShift); var output = Node(ShaderGraphNodeKind.Output);
        graph.nodes.AddRange(new[] { uvNode, source, node, output }); Edge(graph, source, node, "In"); Edge(graph, node, output, "RGBA");
        Edge(graph, uvNode, source, "A");
        t.Equal("Filter", (string)Static("ShaderGraphNodes", "Category", node.kind), "Creation category");
        t.Equal("RGB Shift", (string)Static("ShaderGraphNodes", "Name", node.kind), "Node name");
        var modes = (string[])Static("ShaderGraphNodes", "ModeNames", node.kind);
        t.Equal("Radial,Directional,Triangle", string.Join(",", modes), "Mode labels and order");
        t.Equal(0, node.option, "Default mode is Radial"); t.Near(2, node.FindInput("Amount").value.x, 0, "Default amount");
        var cache = Activator.CreateInstance(TypeOf("ShaderGraphTypeCache"), true); Call(cache, "Prepare", graph);
        t.Equal(ShaderGraphValueType.Color, (ShaderGraphValueType)Call(cache, "Output", node, "Out"), "Color output after numeric conversion");
        foreach (bool sampledImage in new[] { false, true })
        {
            source.kind = sampledImage ? ShaderGraphNodeKind.SampleInput : ShaderGraphNodeKind.Expression;
            if (sampledImage) graph.edges.RemoveAll(e => e.inputNode == source.id);
            for (int mode = 0; mode < modes.Length; mode++)
            {
                node.option = mode; graph.Validate();
                using var native = (IDisposable)Static("ShaderProgramRuntime", "Create", graph, node.id, "Out", false);
                t.True(native != null, "Native preview compiles " + mode);
                t.Equal(mode != 0, (bool)Static("ShaderGraphNodes", "InputVisible", node, "Angle"), "Angle mode visibility");
                t.Equal(mode == 0, (bool)Static("ShaderGraphNodes", "InputVisible", node, "Center"), "Center mode visibility");
                t.Equal(mode == 0, (bool)Static("ShaderGraphNodes", "InputVisible", node, "Falloff"), "Falloff mode visibility");
                foreach (int size in sampledImage ? new[] { width } : new[] { width, 8192 })
                {
                    doc.width = size; doc.height = size == width ? height : 8192;
                    var context = Activator.CreateInstance(TypeOf("LayerRenderContext"), F, null,
                        new object[] { doc, null, width, height, 1f, false, false, false }, null);
                    foreach (var setting in new[] { new Vector4(2, 1, 1, 0), new Vector4(9, .35f, 2, 73),
                        new Vector4(0, 1, 1, 0), new Vector4(12, 0, 1, 0), new Vector4(-4, 1, 1, 0) })
                    {
                        Set(node, "Amount", setting.x); Set(node, "Blend", setting.y); Set(node, "Falloff", setting.z); Set(node, "Angle", setting.w);
                        var center = new Vector2(.5f, .5f); node.FindInput("Center").value = new Vector4(center.x, center.y, 0, 0);
                        GL.sRGBWrite = false; Call(native, "Bind", graph, context, null, false, false);
                        Call(native, "Render", texture, image, context);
                        RenderTexture.active = image; read.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                        var actual = read.GetPixels(); double maxError = 0;
                        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                        {
                            var expected = Expected(new Vector2((x + .5f) / width, (y + .5f) / height), doc.width, doc.height,
                                mode, setting.x, setting.y, center, setting.z, setting.w);
                            for (int c = 0; c < 4; c++) maxError = Math.Max(maxError, Math.Abs(expected[c] - actual[y * width + x][c]));
                        }
                        t.Near(0, maxError, .0003, "GPU vs independent field, image=" + sampledImage + " mode=" + mode + " canvas=" + size + " settings=" + setting);
                    }
                }
            }
        }
        source.kind = ShaderGraphNodeKind.Expression;
        Edge(graph, uvNode, source, "A");
        doc.layers.Add(new Layer(new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph }));
        Call(doc, "NormalizeModel");
        string json = WhimTexDocumentJson.Write(doc).Json;
        t.True(json.Contains("\"RGBShift\""), "Writes the new kind name");
        using (var parsed = WhimTexDocumentJson.Read(json, false))
        {
            var restored = ((ShaderLayerBehaviour)parsed.Document.layers[0].Behaviour).draftGraph.FindNode(node.id);
            t.Equal(node.kind, restored.kind, "Node kind roundtrip"); t.Equal(node.option, restored.option, "Mode roundtrip");
            t.Equal(node.FindInput("Amount").value, restored.FindInput("Amount").value, "Amount roundtrip");
        }
        foreach (int legacyMode in new[] { 0, 1 })
        {
            node.option = legacyMode;
            string oldJson = WhimTexDocumentJson.Write(doc).Json.Replace("\"RGBShift\"", "\"ChromaticAberration\"");
            using var parsed = WhimTexDocumentJson.Read(oldJson, false);
            var restored = ((ShaderLayerBehaviour)parsed.Document.layers[0].Behaviour).draftGraph.FindNode(node.id);
            t.Equal(ShaderGraphNodeKind.RGBShift, restored.kind, "Reads the old kind name");
            t.Equal(legacyMode, restored.option, "Old mode is preserved");
        }
        string catalog = Static("WhimTexApi", "ShaderNodeCatalog").ToString();
        t.True(catalog.Contains("\"RGBShift\"") && !catalog.Contains("\"ChromaticAberration\"") && catalog.Contains("amountUnits"), "Agent discovery includes the new kind and sampling contract");
    });
}
