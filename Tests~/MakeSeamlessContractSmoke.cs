using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using DCFApixels.WhimTex;

public static class MakeSeamlessContractSmoke
{
    const string Root = "Packages/com.dcfapixels.whimtex/";
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly MethodInfo Set = typeof(WhimTexApi).GetMethod("SetMakeSeamless", Hidden);
    static readonly MethodInfo Snapshot = typeof(WhimTexApi).GetMethod("MakeSeamlessSnapshot", Hidden);
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static readonly Type JsonObject = Set.GetParameters()[1].ParameterType;
    static readonly MethodInfo ParseMethod = JsonObject.GetMethod("Parse", new[] { typeof(string) });
    static readonly MethodInfo Serialize = JsonObject.Assembly.GetType("Newtonsoft.Json.JsonConvert")
        .GetMethod("SerializeObject", new[] { typeof(object) });
    sealed class Node
    {
        public readonly object Token;
        public Node(object token) { Token = token; }
        public Node this[string key]
        {
            get
            {
                object token = JsonObject.GetProperty("Item", new[] { typeof(string) }).GetValue(Token, new object[] { key });
                return token == null ? null : new Node(token);
            }
        }
        public object Scalar => Token.GetType().GetProperty("Value").GetValue(Token);
        public string Name => (string)Token.GetType().GetProperty("Name").GetValue(Token);
        public Node Value => new Node(Token.GetType().GetProperty("Value").GetValue(Token));
        public int Count => (int)Token.GetType().GetProperty("Count").GetValue(Token);
        public double Number => Convert.ToDouble(Scalar, CultureInfo.InvariantCulture);
        public IEnumerable<Node> Children()
        {
            foreach (object child in (IEnumerable)Token) yield return new Node(child);
        }
        public override string ToString() => Token.ToString();
    }
    static Node Parse(string text) => new Node(ParseMethod.Invoke(null, new object[] { text }));
    static string Json(object value) => (string)Serialize.Invoke(null, new[] { value });
    static Node Inspect(MakeSeamlessLayerBehaviour layer) => new Node(Snapshot.Invoke(null, new object[] { layer }));
    static void Apply(MakeSeamlessLayerBehaviour layer, string key, object value) =>
        Set.Invoke(null, new object[] { layer, Parse(Json(new Dictionary<string, object> { [key] = value })).Token });
    static bool Equal(Node a, Node b) => a.Scalar is double || a.Scalar is float || b.Scalar is double || b.Scalar is float
        ? Math.Abs(a.Number - b.Number) < 1e-6 : a.ToString() == b.ToString();
    static void Reject(string field, object value)
    {
        try { Apply(MakeSeamlessLayerBehaviour.CreateDefault(), field, value); }
        catch (TargetInvocationException ex)
        {
            Check(ex.InnerException?.GetType().Name == "WhimTexApiException", "Expected validation error for " + field);
            return;
        }
        throw new Exception("Accepted invalid field/value: " + field + "=" + value);
    }

    public static string Main()
    {
        checks = 0;
        var description = Parse(WhimTexApi.Describe());
        Check((bool)description["success"].Scalar, "Describe failed");
        var defaults = description["makeSeamlessDefaults"];
        var schema = Parse(File.ReadAllText(Root + "Documentation~/AI/layers.schema.json"))["$defs"]["makeSeamless"]["properties"];
        Check(defaults.Count == schema.Count, "Default/schema field count");
        foreach (var field in schema.Children())
        {
            string key = field.Name;
            Check(Equal(defaults[key], field.Value["default"]), "Factory/schema default: " + key);
            var rule = field.Value;
            var values = new List<object>();
            if (rule["enum"] != null)
            {
                foreach (var value in rule["enum"].Children()) values.Add(value.Scalar);
                string discovery = key == "mode" ? "makeSeamlessModes" :
                    key == "horizontal" ? "makeSeamlessHorizontal" : key == "vertical" ? "makeSeamlessVertical" :
                    key == "quiltingQuality" ? "makeSeamlessQuiltingQuality" : key == "quiltingChannels" ? "makeSeamlessQuiltingChannels" : "makeSeamlessPoissonEdges";
                Check(description[discovery].ToString() == rule["enum"].ToString(), "Discovery enum " + key);
            }
            else if (rule["type"].ToString() == "boolean") { values.Add(false); values.Add(true); }
            else { values.Add(rule["minimum"].Scalar); values.Add(rule["maximum"].Scalar); }
            foreach (var value in values)
            {
                var layer = MakeSeamlessLayerBehaviour.CreateDefault();
                var before = Inspect(layer);
                try { Apply(layer, key, value); }
                catch (TargetInvocationException ex) { throw new Exception(key + "=" + Json(value) + ": " + ex.InnerException?.Message, ex.InnerException); }
                var after = Inspect(layer);
                Check(Equal(after[key], Parse("{\"value\":" + Json(value) + "}")["value"]), "Set/inspect roundtrip " + key);
                var restored = MakeSeamlessLayerBehaviour.CreateDefault();
                Set.Invoke(null, new object[] { restored, Parse(after.ToString()).Token });
                Check(Equal(Inspect(restored)[key], after[key]), "Serialized snapshot can be reapplied: " + key);
                foreach (var other in before.Children())
                    if (other.Name != key) Check(Equal(other.Value, after[other.Name]), "Partial update changed " + other.Name);
            }
            if (rule["enum"] != null) { Reject(key, "Unknown"); Reject(key, 0); }
            else if (rule["type"].ToString() == "boolean") Reject(key, "true");
            else { Reject(key, rule["minimum"].Number - 1); Reject(key, rule["maximum"].Number + 1); Reject(key, "0.2"); }
        }
        Reject("automaticRadius", true);
        Reject("mode", "HistogramBlend");
        Reject("quiltingSeed", 1.5);
        Reject("mirrorTransitionStart", .950001);
        Reject("offsetTransitionStart", .950001);

        var operations = typeof(WhimTexApi).GetMethod("ApplyOperation", Hidden);
        var temporary = ScriptableObject.CreateInstance<TextureCompositor>();
        try
        {
            temporary.layers.Clear();
            var aliases = new Dictionary<string, Layer>();
            string guide = File.ReadAllText(Root + "Documentation~/AgentAPI.md");
            guide = guide.Split(new[] { "### Make Seamless settings" }, StringSplitOptions.None)[1]
                .Split(new[] { "### Normal Map settings" }, StringSplitOptions.None)[0];
            var examples = Regex.Matches(guide, @"```json\s*\n([\s\S]*?)```");
            Check(examples.Count == 2, "Two operation examples");
            foreach (var operation in Parse("{\"operations\":" + examples[0].Groups[1].Value + "}")["operations"].Children())
                operations.Invoke(null, new object[] { temporary, operation.Token, aliases, false });
            var effect = (MakeSeamlessLayerBehaviour)aliases["tile"].Behaviour;
            Check(effect.TargetLayerId == aliases["source"].Id && !aliases["source"].enabled, "Add/target example resolves hidden source");
            operations.Invoke(null, new object[] { temporary,
                Parse(examples[1].Groups[1].Value.Replace("LAYER-ID", aliases["tile"].Id)).Token, aliases, false });
            Check(effect.mode == MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting && !effect.processAlpha &&
                effect.quiltingChannels == MakeSeamlessLayerBehaviour.QuiltingChannels.Independent, "Set example");
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }

        var parse = typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", Hidden);
        using (var data = (IDisposable)parse.Invoke(null, new object[] {
            File.ReadAllText(Root + "Documentation~/Examples/Clipboard/seamless-noise.json"), 64, 64 }))
        {
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var doc = (TextureCompositor)data.GetType().GetField("Document", instance).GetValue(data);
            var layer = (MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour;
            Check(layer.mode == MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend, "Recipe mode");
            Check(doc.layers.Count == 2 && !doc.layers[1].enabled, "Hidden recipe source");
            data.GetType().GetMethod("Compile", instance).Invoke(data, null);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture rendered = null;
            try
            {
                rendered = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview", instance).Invoke(doc, new object[] { 64 });
                Check(rendered != null && rendered.width == 64 && rendered.height == 64, "Recipe render");
                Check(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Render state restored");
            }
            finally { if (rendered != null) RenderTexture.ReleaseTemporary(rendered); }
        }
        return "Make Seamless contract: " + checks + " checks passed; defaults, enums, bounds, partial updates, rejection and transient clipboard recipe render. No saved assets or open documents changed.";
    }
}
