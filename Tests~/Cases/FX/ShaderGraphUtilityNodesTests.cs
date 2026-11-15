using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Same asynchronous native/SVM harness as ShaderGraphMathModesTests, with one small
// graph per case. Reflection below is restricted to WhimTex types, never Unity internals.
public static class ShaderGraphUtilityNodesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    const int Size = 32;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object target, string name) =>
        target.GetType().GetField(name, F | BindingFlags.Instance)?.GetValue(target) ??
        target.GetType().GetProperty(name, F | BindingFlags.Instance)?.GetValue(target);
    static object Static(string type, string name, params object[] args) => Call(Find(type), null, name, args);
    static Color Gray(float value) => new Color(value, value, value, 1);
    static Color XYZ(Vector3 value) => new Color(value.x, value.y, value.z, 1);
    static string[] Ports(ShaderGraphNode node) => ((IEnumerable)Static("ShaderGraphNodes", "Inputs", node))
        .Cast<object>().Select(p => (string)Get(p, "name")).ToArray();

    sealed class GpuCase
    {
        internal readonly ShaderGraph graph = new ShaderGraph();
        internal string label, port = "Out";
        internal ShaderGraphNode root;
        internal Color expected;
        internal bool fullTexture, hybrid;
        internal float tolerance = .0005f;
        internal ShaderGraphNode Node(ShaderGraphNodeKind kind)
        {
            var node = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
            graph.nodes.Add(node);
            return node;
        }
        internal void Connect(ShaderGraphNode source, ShaderGraphNode target, string input, string output = "Out") =>
            graph.edges.Add(new ShaderGraphEdge { outputNode = source.id, outputPort = output, inputNode = target.id, inputPort = input });
        internal ShaderGraphNode Parameter(float value, bool enumeration = false)
        {
            var p = new ShaderParameter { name = "Input" + graph.parameters.Count,
                type = enumeration ? ShaderParameterType.Enum : ShaderParameterType.Float, floatValue = value };
            if (enumeration) p.controls.Add(new ShaderParameterControl { type = ShaderParameterType.Enum,
                optionNames = new[] { "Low label", "Middle label", "High label" }, optionValues = new double[] { 0, 1, 2 } });
            graph.parameters.Add(p);
            var n = Node(ShaderGraphNodeKind.Parameter); n.parameterId = p.id;
            return n;
        }
        internal ShaderGraphNode Select(ShaderGraphNode condition)
        {
            var n = Node(ShaderGraphNodeKind.Select);
            Set(n, "True", Vector4.one); Set(n, "False", Vector4.zero);
            Connect(condition, n, "Condition"); return n;
        }
        internal void Finish(ShaderGraphNode node, Color value, string output = "Out")
        {
            root = node; expected = value; port = output;
            Connect(node, Node(ShaderGraphNodeKind.Output), "RGBA", output);
        }
    }

    static void Set(ShaderGraphNode node, string port, Vector4 value, ShaderGraphValueType? type = null)
    {
        if (!Ports(node).Contains(port)) throw new InvalidOperationException("Missing port " + node.kind + "." + port);
        var input = node.FindInput(port);
        if (input == null) { input = new ShaderGraphInput { port = port }; node.inputs.Add(input); }
        input.value = value;
        if (type.HasValue) input.type = type.Value;
    }
    static void Scalar(ShaderGraphNode node, string port, float value) => Set(node, port, Vector4.one * value);
    static ShaderGraphSwitchCase Choice(string id, ShaderGraphComparison comparison, float threshold) =>
        new ShaderGraphSwitchCase { port = id, comparison = comparison, threshold = threshold };
    static void SetCases(ShaderGraphNode node, params ShaderGraphSwitchCase[] choices)
    {
        node.switchCases = new List<ShaderGraphSwitchCase>(choices);
        // Factory A/B defaults are not valid stored inputs after replacing the layout.
        var visible = Ports(node);
        node.inputs.RemoveAll(input => !visible.Contains(input.port));
    }

    static List<GpuCase> Cases(TestContext t, NormalMapEncoding encoding)
    {
        var result = new List<GpuCase>();
        GpuCase Add(string label) { var c = new GpuCase { label = label }; result.Add(c); return c; }
        // Distinct discarded lanes catch accidental replication, interleaving and tail use.
        foreach (var widths in new[] { (1, 1), (1, 2), (1, 4), (2, 3), (4, 4) })
        {
            var c = Add("Append/" + widths); var n = c.Node(ShaderGraphNodeKind.Append);
            Set(n, "A", new Vector4(.1f, .2f, .3f, 9), (ShaderGraphValueType)(widths.Item1 - 1));
            Set(n, "B", new Vector4(.4f, .5f, .6f, 8), (ShaderGraphValueType)(widths.Item2 - 1));
            var expected = widths switch {
                (1, 1) => new Color(.1f, .4f, 0, 1), (1, 2) => new Color(.1f, .4f, .5f, 1),
                (1, 4) => new Color(.1f, .4f, .5f, .6f), (2, 3) => new Color(.1f, .2f, .4f, .5f),
                _ => new Color(.1f, .2f, .3f, .4f) };
            c.Finish(n, expected);
            var cache = Activator.CreateInstance(Find("ShaderGraphTypeCache"), true);
            Call(cache.GetType(), cache, "Prepare", c.graph);
            int width = Math.Min(3, widths.Item1) + Math.Min(4 - Math.Min(3, widths.Item1), widths.Item2);
            t.Equal((ShaderGraphValueType)(width - 1), (ShaderGraphValueType)Call(cache.GetType(), cache, "Output", n, "Out"), c.label + " inferred width");
        }

        // Every operator is exercised below, at and above its threshold, using bound
        // Enum numbers. Display labels deliberately differ from stable Switch port IDs.
        var comparisonExpected = new[] {
            new Color(1, 0, 0, 1), new Color(1, 1, 0, 1), new Color(0, 0, 1, 1),
            new Color(0, 1, 1, 1), new Color(0, 1, 0, 1), new Color(1, 0, 1, 1) };
        for (int op = 0; op < 6; op++)
        {
            var c = Add("Switch/Enum/" + (ShaderGraphComparison)op);
            var packed = c.Node(ShaderGraphNodeKind.Combine); packed.valueType = ShaderGraphValueType.Float3;
            for (int predicate = 0; predicate <= 2; predicate++)
            {
                var n = c.Node(ShaderGraphNodeKind.Switch); n.floorPredicate = false;
                SetCases(n, Choice("stable_choice", (ShaderGraphComparison)op, 1));
                Scalar(n, "stable_choice", 1); Scalar(n, "Fallback", 0);
                c.Connect(c.Parameter(predicate, true), n, "Predicate"); c.Connect(n, packed, "XYZ"[predicate].ToString());
                t.True(Ports(n).SequenceEqual(new[] { "Predicate", "Fallback", "stable_choice" }), c.label + " explicit ports");
            }
            c.Finish(packed, comparisonExpected[op]);
        }
        foreach (var data in new[] { (.75f, true, .2f), (1.75f, true, .6f), (-.25f, true, -.4f), (.75f, false, -.4f) })
        {
            var c = Add("Switch/default/Floor=" + data.Item2 + "/" + data.Item1);
            var n = c.Node(ShaderGraphNodeKind.Switch);
            t.True(n.floorPredicate, "Switch floors by default"); n.floorPredicate = data.Item2;
            Scalar(n, "Predicate", data.Item1); Scalar(n, "A", .2f); Scalar(n, "B", .6f); Scalar(n, "Fallback", -.4f);
            c.Finish(n, Gray(data.Item3));
        }
        {
            var c = Add("Switch/first matching case wins/dynamic widths"); var n = c.Node(ShaderGraphNodeKind.Switch);
            SetCases(n, Choice("first", ShaderGraphComparison.GreaterOrEqual, 0), Choice("second", ShaderGraphComparison.Equal, 1));
            c.Connect(c.Parameter(1, true), n, "Predicate");
            Set(n, "first", new Vector4(.2f, .3f, 0, 0), ShaderGraphValueType.Float2);
            Set(n, "second", new Vector4(8, 7, 6, 0), ShaderGraphValueType.Float3);
            Set(n, "Fallback", new Vector4(4, 3, 2, .7f), ShaderGraphValueType.Float4);
            c.Finish(n, new Color(.2f, .3f, 0, 0));
        }
        {
            var c = Add("Switch/explicit empty list returns fallback"); var n = c.Node(ShaderGraphNodeKind.Switch);
            SetCases(n); Scalar(n, "Predicate", 0);
            Set(n, "Fallback", new Vector4(.2f, -.3f, 1.2f, .4f), ShaderGraphValueType.Float4);
            c.Finish(n, new Color(.2f, -.3f, 1.2f, .4f));
        }

        foreach (var data in new[] { (.2f, 0f, 3f, 0f), (.2f, 1f, 3f, 1f), (.8f, .4f, 2f, .8f) })
        {
            var c = Add("FadeTransition/" + data); var n = c.Node(ShaderGraphNodeKind.FadeTransition);
            Scalar(n, "NoiseValue", data.Item1); Scalar(n, "FadeValue", data.Item2); Scalar(n, "FadeContrast", data.Item3);
            c.Finish(n, Gray(data.Item4));
        }
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var c = Add("ReplaceColor/" + new[] { "inside hard range", "fuzzy midpoint", "outside hard range" }[mode]);
            var n = c.Node(ShaderGraphNodeKind.ReplaceColor);
            var color = new Color(mode == 0 ? .2f : .5f, .3f, .4f, .37f);
            var to = new Color(1.4f, -.2f, .7f, .01f);
            Set(n, "Color", color); Set(n, "From", new Vector4(.2f, .3f, .4f, .9f)); Set(n, "To", to);
            Scalar(n, "Range", .1f); Scalar(n, "Fuzziness", mode == 1 ? .4f : 0);
            var expected = mode == 0 ? to : mode == 1 ? Color.LerpUnclamped(to, color, .5f) : color;
            expected.a = color.a; c.Finish(n, expected);
        }
        foreach (float amount in new[] { 0f, 2f })
        {
            var c = Add("Contrast/" + amount + (amount == 2 ? "/Custom Function shared artist helper" : ""));
            var n = c.Node(ShaderGraphNodeKind.Contrast); var input = new Color(-.2f, .5f, 1.6f, .31f);
            Set(n, "Color", input); Scalar(n, "Contrast", amount);
            if (amount == 2)
            {
                var fn = new ShaderGraphFunction { body = "Result = In;", draft = "Result = In;",
                    ports = new List<ShaderGraphFunctionPort> {
                        new ShaderGraphFunctionPort { name = "In", type = ShaderGraphValueType.Color },
                        new ShaderGraphFunctionPort { name = "Result", type = ShaderGraphValueType.Color, direction = ShaderGraphFunctionDirection.Output } } };
                c.graph.functions.Add(fn); var custom = c.Node(ShaderGraphNodeKind.CustomFunction);
                Static("ShaderGraphNodes", "SetFunction", custom, fn); Set(custom, "In", input);
                // Native Contrast requests its helper in the Custom Function producer.
                // The function body uses only its public ports, not internal Graph helpers.
                var neutralContrast = c.Node(ShaderGraphNodeKind.Contrast);
                Set(neutralContrast, "Color", input); Scalar(neutralContrast, "Contrast", 1);
                c.Connect(neutralContrast, custom, "In");
                // Arbitrary HLSL requires an explicit native image boundary for SVM.
                var buffer = c.Node(ShaderGraphNodeKind.Buffer);
                buffer.bufferResolution = ShaderGraphBufferResolution.Custom; buffer.bufferSize = new Vector2Int(Size, Size);
                c.Connect(custom, buffer, "In", "Result"); c.Connect(buffer, n, "Color"); c.hybrid = true;
            }
            float midpoint = (float)Math.Pow(.5, 2.2);
            c.Finish(n, new Color(midpoint + (input.r - midpoint) * amount, midpoint + (input.g - midpoint) * amount,
                midpoint + (input.b - midpoint) * amount, input.a));
        }
        foreach (bool outside in new[] { false, true })
        {
            var c = Add("NormalReconstructZ/" + (outside ? "outside unit disk" : "inside unit disk"));
            var n = c.Node(ShaderGraphNodeKind.NormalReconstructZ);
            Set(n, "In", outside ? new Vector4(3, 4, 0, 0) : new Vector4(.3f, .4f, 0, 0));
            c.Finish(n, outside ? new Color(.6f, .8f, 0, 1) : new Color(.3f, .4f, (float)Math.Sqrt(.75), 1));
        }
        foreach (int mode in new[] { 0, 1 })
        {
            var c = Add("NormalUnpack/" + (mode == 0 ? "Tangent/" + encoding : "Object/raw RGB"));
            var n = c.Node(ShaderGraphNodeKind.NormalUnpack); n.option = mode;
            // Nontrivial alpha and blue distinguish raw XYZ, RG and RG-or-AG packing.
            var packed = new Vector4(.5f, .75f, .125f, .75f); Set(n, "In", packed);
            Color expected;
            if (mode == 1 || encoding == NormalMapEncoding.XYZ) expected = new Color(0, .5f, -.75f, 1);
            else expected = new Color(-.25f, .5f, (float)Math.Sqrt(.6875), 1);
            c.tolerance = .002f; // UnityCG.UnpackNormal returns half precision on some backends.
            c.Finish(n, expected);
        }
        {
            var c = Add("RadialShear/asymmetric strength and offset"); var n = c.Node(ShaderGraphNodeKind.RadialShear);
            t.Equal(new Vector4(10, 10, 10, 10), n.FindInput("Strength").value, "RadialShear default strength");
            Set(n, "UV", new Vector4(.7f, .2f, 0, 0), ShaderGraphValueType.Float2);
            Set(n, "Center", new Vector4(.4f, .6f, 0, 0)); Set(n, "Strength", new Vector4(2, -3, 0, 0));
            Set(n, "Offset", new Vector4(.1f, -.2f, 0, 0)); c.Finish(n, new Color(.6f, .225f, 0, 1));
        }
        var logicInputs = new[] { new Vector4(-2, 0, 0, 0), new Vector4(0, -2, 7, 7),
            new Vector4(-1, -2, -3, 0), Vector4.zero };
        var logicExpected = new[] { new Color(1, 1, 0, 1), new Color(0, 1, 0, 1), new Color(1, 1, 0, 1), new Color(0, 0, 0, 1) };
        for (int width = 1; width <= 4; width++)
        {
            var c = Add("All+Any/width=" + width); var packed = c.Node(ShaderGraphNodeKind.Combine); packed.valueType = ShaderGraphValueType.Float2;
            for (int i = 0; i < 2; i++)
            {
                var n = c.Node(i == 0 ? ShaderGraphNodeKind.All : ShaderGraphNodeKind.Any);
                Set(n, "In", logicInputs[width - 1], (ShaderGraphValueType)(width - 1));
                c.Connect(c.Select(n), packed, i == 0 ? "X" : "Y");
            }
            c.Finish(packed, logicExpected[width - 1]);
        }
        foreach (var data in new[] { (0f, 0f, 1f, 0f), (1f, 0f, 0f, 1f), (-1f, 0f, 0f, 1f), (-2f, 4f, 0f, 0f) })
        {
            var c = Add("IsNaN+IsInfinite/bound Divide/" + data.Item1 + "/" + data.Item2);
            var divide = c.Node(ShaderGraphNodeKind.Divide);
            c.Connect(c.Parameter(data.Item1), divide, "A"); c.Connect(c.Parameter(data.Item2), divide, "B");
            var packed = c.Node(ShaderGraphNodeKind.Combine); packed.valueType = ShaderGraphValueType.Float2;
            for (int i = 0; i < 2; i++)
            {
                var n = c.Node(i == 0 ? ShaderGraphNodeKind.IsNaN : ShaderGraphNodeKind.IsInfinite);
                c.Connect(divide, n, "In"); c.Connect(c.Select(n), packed, i == 0 ? "X" : "Y");
            }
            c.Finish(packed, new Color(data.Item3, data.Item4, 0, 1));
        }
        foreach (float roundness in new[] { 0f, .3f, 1f })
        {
            var c = Add("RoundedPolygon/roundness=" + roundness); var n = c.Node(ShaderGraphNodeKind.Shape); n.option = 7;
            Scalar(n, "Sides", 5); Scalar(n, "Roundness", roundness); Scalar(n, "Rotation", 13);
            Set(n, "Size", new Vector4(.7f, .6f, 0, 0)); Scalar(n, "Feather", 0);
            c.fullTexture = true; c.Finish(n, default, "Out");
        }
        t.Equal(41, result.Count, "Bounded GPU case count");
        return result;
    }

    static void ModelChecks(TestContext t, List<GpuCase> cases)
    {
        var expectedKinds = new[] { ShaderGraphNodeKind.Append, ShaderGraphNodeKind.Switch, ShaderGraphNodeKind.FadeTransition,
            ShaderGraphNodeKind.ReplaceColor, ShaderGraphNodeKind.Contrast, ShaderGraphNodeKind.NormalReconstructZ,
            ShaderGraphNodeKind.NormalUnpack, ShaderGraphNodeKind.RadialShear, ShaderGraphNodeKind.All, ShaderGraphNodeKind.Any,
            ShaderGraphNodeKind.IsNaN, ShaderGraphNodeKind.IsInfinite };
        for (int i = 0; i < expectedKinds.Length; i++) t.Equal(196 + i, (int)expectedKinds[i], "Serialized kind " + expectedKinds[i]);
        var modes = (string[])Static("ShaderGraphNodes", "ModeNames", ShaderGraphNodeKind.Shape);
        t.Equal("Rounded Polygon", modes[7], "Rounded Polygon mode 7");
        t.True(((string[])Static("ShaderGraphNodes", "ModeNames", ShaderGraphNodeKind.NormalUnpack)).SequenceEqual(new[] { "Tangent", "Object" }), "Normal Unpack modes");
        foreach (var kind in new[] { ShaderGraphNodeKind.All, ShaderGraphNodeKind.Any, ShaderGraphNodeKind.IsNaN, ShaderGraphNodeKind.IsInfinite })
        {
            object[] args = { kind, ShaderGraphValueType.Float };
            t.True((bool)Static("ShaderGraphNodes", "TryUtilityOutputType", args), kind + " known output");
            t.Equal(ShaderGraphValueType.Boolean, (ShaderGraphValueType)args[1], kind + " Boolean output");
        }

        var fixture = new GpuCase(); var n = fixture.Node(ShaderGraphNodeKind.Switch);
        var nativeDefault = JsonUtility.FromJson<ShaderGraphNode>(JsonUtility.ToJson(n));
        t.True(nativeDefault.switchCases != null && nativeDefault.switchCases.Count == 2, "Native JSON materializes two default Switch cases");
        t.True(nativeDefault.switchCases.Select(p => p.port).SequenceEqual(new[] { "A", "B" }), "Native default IDs");
        t.True(nativeDefault.floorPredicate && nativeDefault.switchCases[0].threshold == 0 && nativeDefault.switchCases[1].threshold == 1 &&
            nativeDefault.switchCases.All(p => p.comparison == ShaderGraphComparison.Equal), "Native default conditions");
        SetCases(n);
        var nativeEmpty = JsonUtility.FromJson<ShaderGraphNode>(JsonUtility.ToJson(n));
        t.True(nativeEmpty.switchCases != null && nativeEmpty.switchCases.Count == 0, "Native JSON preserves explicitly empty cases");
        SetCases(n,
            Choice("case_stable_z", ShaderGraphComparison.GreaterOrEqual, -.25f),
            Choice("case_stable_a", ShaderGraphComparison.NotEqual, 1.75f));
        n.floorPredicate = false;
        Scalar(n, "case_stable_z", .2f); Scalar(n, "case_stable_a", .7f);
        fixture.Connect(fixture.Parameter(1, true), n, "Predicate");
        fixture.Connect(fixture.Parameter(.4f), n, "case_stable_a");
        fixture.Finish(n, default);
        var unrelated = fixture.Node(ShaderGraphNodeKind.Contrast);
        var snapshot = (ShaderGraph)Static("ShaderProgramStorage", "CopyWorkingGraph", fixture.graph);
        var copy = snapshot.FindNode(n.id);
        t.True(!ReferenceEquals(copy.switchCases, n.switchCases) && !ReferenceEquals(copy.switchCases[0], n.switchCases[0]), "Working snapshot deep-copies case objects and list");
        n.switchCases[0].threshold = 9; n.switchCases[0].comparison = ShaderGraphComparison.Less; n.switchCases.Reverse(); n.floorPredicate = true;
        t.True(copy.switchCases[0].port == "case_stable_z" && copy.switchCases[0].threshold == -.25f &&
            copy.switchCases[0].comparison == ShaderGraphComparison.GreaterOrEqual && !copy.floorPredicate, "Mutating live case data/order/Floor cannot change snapshot");
        n.switchCases.Reverse(); n.switchCases[0].threshold = -.25f; n.switchCases[0].comparison = ShaderGraphComparison.GreaterOrEqual; n.floorPredicate = false;

        var state = Activator.CreateInstance(Find("ShaderGraphPreviewState"), true);
        ulong Stamp(string id) => (ulong)Call(state.GetType(), state, "Fingerprint", id);
        void Invalidation(string label, Action change, Action restore)
        {
            Call(state.GetType(), state, "Prepare", fixture.graph);
            ulong before = Stamp(n.id), downstream = Stamp(fixture.graph.nodes.Single(p => p.kind == ShaderGraphNodeKind.Output).id), isolated = Stamp(unrelated.id);
            change(); Call(state.GetType(), state, "Prepare", fixture.graph);
            t.True(before != Stamp(n.id), label + " changes branch fingerprint");
            t.True(downstream != Stamp(fixture.graph.nodes.Single(p => p.kind == ShaderGraphNodeKind.Output).id), label + " invalidates downstream");
            t.Equal(isolated, Stamp(unrelated.id), label + " preserves unrelated branch");
            restore(); Call(state.GetType(), state, "Prepare", fixture.graph);
            t.Equal(before, Stamp(n.id), label + " restores branch fingerprint");
        }
        Invalidation("Threshold", () => n.switchCases[0].threshold = .5f, () => n.switchCases[0].threshold = -.25f);
        Invalidation("Comparison", () => n.switchCases[0].comparison = ShaderGraphComparison.Less, () => n.switchCases[0].comparison = ShaderGraphComparison.GreaterOrEqual);
        Invalidation("Floor", () => n.floorPredicate = true, () => n.floorPredicate = false);
        Invalidation("Case order", () => n.switchCases.Reverse(), () => n.switchCases.Reverse());
        var extra = Choice("extra_stable", ShaderGraphComparison.Equal, 3);
        Invalidation("Case count", () => n.switchCases.Add(extra), () => n.switchCases.Remove(extra));
        var renamed = n.FindInput("case_stable_z");
        Invalidation("Case ID", () => { n.switchCases[0].port = "renamed_stable"; renamed.port = "renamed_stable"; },
            () => { n.switchCases[0].port = "case_stable_z"; renamed.port = "case_stable_z"; });
        var defaults = fixture.Node(ShaderGraphNodeKind.Switch);
        var empty = fixture.Node(ShaderGraphNodeKind.Switch); SetCases(empty);
        // JSON validates omitted/null cases; explicit empty is intentionally different.
        var implicitDefaults = fixture.Node(ShaderGraphNodeKind.Switch); implicitDefaults.switchCases = null;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            AddLayer(document, fixture.graph);
            foreach (var c in cases) AddLayer(document, c.graph);
            using var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
            t.Equal(document.layers.Count, loaded.Document.layers.Count, "Current JSON layer count");
            var saved = ((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
            var savedNode = saved.FindNode(n.id);
            t.True(!savedNode.floorPredicate && savedNode.switchCases.Select(p => p.port).SequenceEqual(new[] { "case_stable_z", "case_stable_a" }), "JSON stable IDs and order");
            for (int i = 0; i < n.switchCases.Count; i++)
            {
                t.Equal(n.switchCases[i].comparison, savedNode.switchCases[i].comparison, "JSON comparison " + i);
                t.Equal(n.switchCases[i].threshold, savedNode.switchCases[i].threshold, "JSON threshold " + i);
            }
            t.Equal(2, saved.FindNode(defaults.id).switchCases.Count, "JSON default cases");
            t.Equal(2, saved.FindNode(implicitDefaults.id).switchCases.Count, "JSON null/default cases");
            t.Equal(0, saved.FindNode(empty.id).switchCases.Count, "JSON explicit empty cases");
            CheckEdges(t, fixture.graph, saved);
            for (int i = 0; i < cases.Count; i++)
            {
                var original = cases[i].graph;
                var roundtrip = ((ShaderLayerBehaviour)loaded.Document.layers[i + 1].Behaviour).draftGraph;
                foreach (var node in original.nodes)
                {
                    var restored = roundtrip.FindNode(node.id);
                    t.True(restored != null && restored.kind == node.kind && restored.option == node.option, "JSON node kind/mode: " + cases[i].label);
                }
                CheckEdges(t, original, roundtrip);
            }
            t.True(saved.parameters[0].controls[0].optionNames.SequenceEqual(new[] { "Low label", "Middle label", "High label" }), "JSON connected Enum labels");
            t.True(Ports(savedNode).SequenceEqual(new[] { "Predicate", "Fallback", "case_stable_z", "case_stable_a" }), "Enum labels do not generate Switch cases");
        }
        finally { Object.DestroyImmediate(document); }
    }

    static void CheckEdges(TestContext t, ShaderGraph original, ShaderGraph saved)
    {
        string Key(ShaderGraphEdge e) => e.outputNode + "/" + e.outputPort + ">" + e.inputNode + "/" + e.inputPort;
        t.True(original.edges.Select(Key).SequenceEqual(saved.edges.Select(Key)), "JSON preserves edge port identities and order");
    }
    static Layer AddLayer(WhimTexDocument document, ShaderGraph graph)
    {
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
        Call(typeof(Layer), layer, "AssignNewId"); document.layers.Add(layer); return layer;
    }
    static void CleanDiagnostics(TestContext t, IEnumerable diagnostics, string label)
    {
        t.True(diagnostics != null, label + " exposes compiler diagnostics");
        var messages = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            string severity = Get(diagnostic, "Severity").ToString();
            if (severity == "Warning" || severity == "Error") messages.Add(diagnostic.ToString());
        }
        t.True(messages.Count == 0, label + ": " + string.Join("\n", messages));
    }

    public static async Task<object> Run()
    {
        var t = new TestContext();
        // Public API only, read without changing build target or encoding settings:
        // https://docs.unity.cn/Documentation/ScriptReference/PlayerSettings.GetNormalMapEncoding.html
        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        var encoding = PlayerSettings.GetNormalMapEncoding(namedTarget);
        t.True(encoding == NormalMapEncoding.XYZ || encoding == NormalMapEncoding.DXT5nm, "Known normal-map encoding");
        var cases = Cases(t, encoding);
        ModelChecks(t, cases);
        var timer = Stopwatch.StartNew();
        var previousActive = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        WhimTexDocument document = null; RenderTexture target = null; Texture2D pixels = null;
        float maxError = 0, maxDifference = 0;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = Size;
            Layer layer = AddLayer(document, cases[0].graph);
            var owner = (ShaderLayerBehaviour)layer.Behaviour;
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
            pixels = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            t.True(target.Create(), "Float target allocation");
            var context = Activator.CreateInstance(Find("LayerRenderContext"), new object[] { document, null, Size, Size, 1f, false, false, false });
            var requestType = Find("ShaderProgramRuntime+CompilationRequest"); var vmType = Find("ShaderGraphPreviewVM");
            async Task Until(Func<bool> condition, string label)
            {
                while (!condition())
                {
                    if (timer.Elapsed.TotalSeconds > 50) throw new TimeoutException("Utility suite budget: " + label);
                    await Task.Delay(25);
                }
            }
            Color[] Read()
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); pixels.Apply(); return pixels.GetPixels(); }
                finally { RenderTexture.active = active; }
            }
            foreach (var c in cases)
            {
                if (timer.Elapsed.TotalSeconds > 50) throw new TimeoutException("Utility suite budget before " + c.label);
                owner.draftGraph = c.graph;
                using var request = (IDisposable)Activator.CreateInstance(requestType, BindingFlags.Instance | F, null,
                    new object[] { c.graph, c.root.id, c.port, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                object[] completed = { null };
                await Until(() => (bool)Call(requestType, request, "TryComplete", completed), c.label + " HLSL");
                using var runtime = (IDisposable)completed[0]; var rt = runtime.GetType();
                CleanDiagnostics(t, (IEnumerable)Get(runtime, "DiagnosticMessages"), c.label + " HLSL");
                Call(rt, runtime, "Bind", c.graph, context, null, true, false);
                Call(rt, runtime, "PrepareCoordinates", layer, context);
                GL.sRGBWrite = false; Call(rt, runtime, "Render", Texture2D.blackTexture, target, context);
                Color[] native = Read();
                IDisposable svm;
                if (c.hybrid)
                    svm = (IDisposable)Call(vmType, null, "BeginHybrid", c.graph, c.root.id, c.port, false, false, false);
                else
                {
                    object[] build = { c.graph, c.root.id, c.port, null };
                    t.True((bool)Call(vmType, null, "TryCreate", build), c.label + " pure SVM build"); svm = (IDisposable)build[3];
                }
                using (svm)
                {
                    t.True(svm != null, c.label + " preview VM");
                    if (c.hybrid) Call(vmType, svm, "ConfigureHybrid", document, layer, false, true);
                    await Until(() => {
                        bool ready = (bool)Call(vmType, svm, "UpdatePreparation");
                        if ((bool)Get(svm, "Failed"))
                        {
                            var reason = vmType.GetProperty("LastFallbackReason", F | BindingFlags.Static)?.GetValue(null);
                            throw new InvalidOperationException(c.label + " SVM failed: " + reason);
                        }
                        return ready;
                    }, c.label + " SVM preparation");
                    t.True(c.hybrid ? (bool)Call(vmType, svm, "RenderBoundaries", target, context) :
                        (bool)Call(vmType, svm, "Render", target, 0f), c.label + " SVM render");
                    var material = (Material)Get(svm, "material");
                    t.True(material != null, c.label + " SVM material");
                    CleanDiagnostics(t, (IEnumerable)Static("ShaderDiagnostics", "ReadCompiler", material.shader), c.label + " SVM");
                    if (c.hybrid) CleanDiagnostics(t, (IEnumerable)Get(Get(svm, "hybridRuntime"), "DiagnosticMessages"), c.label + " hybrid producer");
                    Color[] vm = Read();
                    if (c.fullTexture) CheckShape(t, c, native, vm, ref maxDifference);
                    else for (int channel = 0; channel < 4; channel++)
                    {
                        int sample = 3 * Size + 3;
                        float error = Mathf.Max(Mathf.Abs(native[sample][channel] - c.expected[channel]), Mathf.Abs(vm[sample][channel] - c.expected[channel]));
                        float difference = Mathf.Abs(native[sample][channel] - vm[sample][channel]);
                        t.True(float.IsFinite(error) && error <= c.tolerance,
                            $"{c.label}[{channel}]: expected {c.expected[channel]:R}, HLSL {native[sample][channel]:R}, SVM {vm[sample][channel]:R}");
                        t.True(float.IsFinite(difference) && difference <= c.tolerance, c.label + " HLSL/SVM difference " + difference);
                        maxError = Mathf.Max(maxError, error); maxDifference = Mathf.Max(maxDifference, difference);
                    }
                }
            }
        }
        finally
        {
            RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (document != null) Object.DestroyImmediate(document);
        }
        return new TestResult { status = "passed", checks = t.Checks, failures = Array.Empty<string>(),
            message = $"41 GPU cases; independent numeric expectations, pure SVM plus one Custom Function hybrid case; full-texture Rounded Polygon AA; clean compiler diagnostics; JSON/native roundtrips, deep snapshots and branch invalidation. Normal encoding {encoding}. Max error {maxError:R}, max HLSL/SVM difference {maxDifference:R}." };
    }

    static void CheckShape(TestContext t, GpuCase c, Color[] native, Color[] svm, ref float maxDifference)
    {
        int nativeAA = 0, svmAA = 0;
        for (int i = 0; i < native.Length; i++)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                float a = native[i][channel], b = svm[i][channel];
                t.True(float.IsFinite(a) && float.IsFinite(b) && a >= -.0001f && a <= 1.0001f && b >= -.0001f && b <= 1.0001f,
                    c.label + " finite bounded coverage at " + i + "/" + channel);
                float difference = Mathf.Abs(a - b); maxDifference = Mathf.Max(maxDifference, difference);
                t.True(difference <= .002f, c.label + " full-texture HLSL/SVM parity at " + i + "/" + channel + ": " + difference);
            }
            if (native[i].r > .001f && native[i].r < .999f) nativeAA++;
            if (svm[i].r > .001f && svm[i].r < .999f) svmAA++;
            t.Near(1, native[i].a, .0001, c.label + " HLSL alpha"); t.Near(1, svm[i].a, .0001, c.label + " SVM alpha");
        }
        foreach (var rendered in new[] { native, svm })
        {
            int center = (Size / 2) * Size + Size / 2;
            t.Near(1, rendered[center].r, .0001, c.label + " independent center-inside expectation");
            foreach (int corner in new[] { 0, Size - 1, Size * (Size - 1), Size * Size - 1 })
                t.Near(0, rendered[corner].r, .0001, c.label + " independent corner-outside expectation");
        }
        t.True(nativeAA > 0 && svmAA > 0, c.label + " nonempty antialiased boundary");
    }
}
