using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class ShaderPassDiagnosticsTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type TypeOf(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static object Static(string type, string name, params object[] args) => TypeOf(type).GetMethod(name, F).Invoke(null, args);

    public static string Run() => FixtureContext.Run("Shader pass diagnostics", () =>
    {
        var t = FixtureContext.Context;
        var graph = new ShaderGraph();
        ShaderGraphNode Node(ShaderGraphNodeKind kind)
        {
            var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
            graph.nodes.Add(n); return n;
        }
        void Wire(ShaderGraphNode from, string port, ShaderGraphNode to, string input) => graph.edges.Add(new ShaderGraphEdge
            { outputNode = from.id, outputPort = port, inputNode = to.id, inputPort = input });
        var function = new ShaderGraphFunction { body = "Result = ddx(In);", draft = "Result = ddx(In);",
            ports = new List<ShaderGraphFunctionPort> {
                new ShaderGraphFunctionPort { name = "In", type = ShaderGraphValueType.Color, value = Vector4.one },
                new ShaderGraphFunctionPort { name = "Result", type = ShaderGraphValueType.Color, direction = ShaderGraphFunctionDirection.Output }
            } };
        graph.functions.Add(function);
        ShaderGraphNode previous = null;
        for(int i = 0; i < 4; i++)
        {
            var custom = Node(ShaderGraphNodeKind.CustomFunction); custom.functionId = function.id;
            var branch = custom; string port = "Result";
            if(i < 3)
            {
                branch = Node(ShaderGraphNodeKind.Buffer);
                branch.bufferResolution = ShaderGraphBufferResolution.Custom; branch.bufferSize = new Vector2Int(8, 8);
                Wire(custom, port, branch, "In"); port = "Out";
            }
            if(previous == null) previous = branch;
            else
            {
                var add = Node(ShaderGraphNodeKind.Add); Wire(previous, "Out", add, "A"); Wire(branch, port, add, "B"); previous = add;
            }
        }
        var output = Node(ShaderGraphNodeKind.Output); Wire(previous, "Out", output, "RGBA"); graph.Validate();
        var program = new ShaderProgram { mode = ShaderProgramMode.Graph, draftGraph = graph };
        using var runtime = (IDisposable)Static("ShaderProgramRuntime", "CompileDraft", program, ShaderProgramMode.Graph,
            "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, null);
        var raw = Get(runtime, "DiagnosticMessages");
        var records = ((IEnumerable)raw).Cast<object>().ToList();
        t.Equal(3, ((Array)Get(runtime, "bufferExecutables")).Length, "Fixture has a main shader and three auxiliary passes");
        t.Equal(3, records.Count, "Three unverified buffers retain their locality diagnostics; spatial main needs no notice");
        t.Equal(3, records.Select(d => (string)Get(d, "Pass")).Distinct().Count(), "Each record retains its pass");
        t.True(records.All(d => ((string)Get(d, "Pass")).StartsWith("Buffer ")), "Auxiliary passes identified");
        t.Equal(1, ((IEnumerable)Get(runtime, "Messages")).Cast<object>().Count(), "Graph summary contains one message");
        string summary = (string)Static("ShaderDiagnostics", "Format", raw);
        t.Equal(1, summary.Split('\n').Length, "Plain text summary contains one message");

        var details = (Foldout)Activator.CreateInstance(TypeOf("ShaderDiagnosticPassDetails"), true);
        Call(details, "Refresh", raw);
        t.True(!details.value, "Details start collapsed");
        t.Equal(DisplayStyle.Flex, details.style.display.value, "Multi-pass details available");
        t.Equal(3, details.Query<HelpBox>().ToList().Count, "Details retain each pass result");
        details.value = true; Call(details, "Refresh", raw);
        t.True(details.value, "Refresh retains expanded state");
        var arrayType = TypeOf("WhimTexApi").GetMethod("AppendShaderDiagnostics", F).GetParameters()[1].ParameterType;
        var all = Activator.CreateInstance(arrayType); var warnings = Activator.CreateInstance(arrayType); var errors = Activator.CreateInstance(arrayType);
        Static("WhimTexApi", "AppendShaderDiagnostics", raw, all, warnings, errors);
        var api = ((IEnumerable)all).Cast<object>().ToList();
        t.Equal(3, api.Count, "Agent diagnostics preserve all producers");
        t.Equal(3, api.Select(d => d.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(d, new object[] { "pass" }).ToString()).Distinct().Count(), "Agent records identify each pass");
        t.Equal(0, ((IEnumerable)errors).Cast<object>().Count(), "Informational locality remains non-fatal");

        var state = Activator.CreateInstance(TypeOf("ShaderCompilationState"), true);
        Call(state, "SetDiagnostics", raw, false);
        string json = JsonUtility.ToJson(state);
        var restored = Activator.CreateInstance(TypeOf("ShaderCompilationState"), true); JsonUtility.FromJsonOverwrite(json, restored);
        var saved = ((IEnumerable)Get(restored, "DiagnosticMessages")).Cast<object>().ToList();
        t.Equal(3, saved.Select(d => (string)Get(d, "Pass")).Distinct().Count(), "Pass sources survive serialized compilation state");

        var diagnostic = TypeOf("ShaderDiagnostic"); var severity = TypeOf("ShaderDiagnosticSeverity");
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(diagnostic));
        object Item(string level, string file, int line, string pass)
        {
            var item = Activator.CreateInstance(diagnostic, F, null, new object[] { Enum.Parse(severity, level), "Same text", file, line }, null);
            diagnostic.GetField("Pass", F).SetValue(item, pass); return item;
        }
        list.Add(Item("Warning", "a.hlsl", 2, "Main")); list.Add(Item("Warning", "a.hlsl", 2, "Buffer 1"));
        list.Add(Item("Error", "a.hlsl", 2, "Buffer 2")); list.Add(Item("Warning", "b.hlsl", 2, "Buffer 2"));
        list.Add(Item("Warning", "a.hlsl", 3, "Buffer 2"));
        t.Equal(4, ((string)Static("ShaderDiagnostics", "Format", list)).Split('\n').Length, "Different severity and locations remain visible");
        Call(details, "Refresh", list);
        t.True(details.value, "New diagnostics retain disclosure state");
        t.True(details.Query<HelpBox>().ToList().Any(h => h.messageType == HelpBoxMessageType.Error), "Pass details retain error severity");
        Call(details, "Refresh", (object)null);
        t.Equal(DisplayStyle.None, details.style.display.value, "Cleared state hides stale pass details");
        t.Equal(0, details.Query<HelpBox>().ToList().Count, "Cleared state releases rows");
    });
}
