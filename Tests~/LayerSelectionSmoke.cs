// Opt-in after manual compilation. Tests only temporary in-memory layer trees.
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
var operations = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType(
    "DCFApixels.WhimTex.LayerSelectionOperations", true);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
int checks = 0;
void Check(bool value, string name) { if (!value) throw new System.Exception(name); checks++; }
object Call(string method, params object[] args) => operations.GetMethod(method, flags).Invoke(null, args);
System.Collections.Generic.List<DCFApixels.WhimTex.Layer> Layers(params DCFApixels.WhimTex.Layer[] values)
    => new System.Collections.Generic.List<DCFApixels.WhimTex.Layer>(values);
bool Same(System.Collections.Generic.List<DCFApixels.WhimTex.Layer> actual,
    params DCFApixels.WhimTex.Layer[] expected)
{
    if (actual.Count != expected.Length) return false;
    for (int i = 0; i < expected.Length; i++) if (!object.ReferenceEquals(actual[i], expected[i])) return false;
    return true;
}
try
{
    var a = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "A" };
    var b = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "B" };
    var c = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "C" };
    var d = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "D" };
    var e = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "E" };
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    var nested = new DCFApixels.WhimTex.GroupLayerBehaviour();

    // Every flat selection, including empty/all, at both boundaries and with gaps.
    var original = Layers(a, b, c, d, e);
    for (int mask = 0; mask < 32; mask++)
    foreach (int direction in new[] { -1, 1 })
    {
        document.layers = new System.Collections.Generic.List<DCFApixels.WhimTex.Layer>(original);
        var selected = Layers();
        foreach (var item in original)
            if ((mask & (1 << original.IndexOf(item))) != 0) selected.Add(item);
        bool available = (bool)Call("Move", document, selected, direction, false);
        Check(Same(document.layers, a, b, c, d, e), "Availability must not mutate the tree");
        bool moved = (bool)Call("Move", document, selected, direction, true);
        Check(moved == available, "Availability matches execution");
        var selectedAfter = Layers();
        var otherAfter = Layers();
        var otherBefore = Layers();
        foreach (var item in original) if (!selected.Contains(item)) otherBefore.Add(item);
        foreach (var item in document.layers)
            (selected.Contains(item) ? selectedAfter : otherAfter).Add(item);
        Check(Same(selectedAfter, selected.ToArray()), "Selected relative order is stable");
        Check(Same(otherAfter, otherBefore.ToArray()), "Unselected relative order is stable");
        bool blocked = false;
        for (int step = 0; step < original.Count; step++)
        {
            int i = direction < 0 ? step : original.Count - 1 - step;
            var item = original[i];
            if (!selected.Contains(item)) { blocked = false; continue; }
            if (i == (direction < 0 ? 0 : original.Count - 1)) blocked = true;
            int expectedIndex = blocked ? i : i + direction;
            Check(document.layers.IndexOf(item) == expectedIndex, "Move by one slot or stay at boundary");
        }
    }

    nested.layers = Layers(b, c);
    group.layers = Layers(a, nested);
    document.layers = Layers(group, d);
    var chosen = new System.Collections.Generic.HashSet<DCFApixels.WhimTex.Layer>(Layers(group, nested, b, d));
    var roots = (System.Collections.Generic.List<DCFApixels.WhimTex.Layer>)Call("Collect", document.layers, chosen, true);
    var all = (System.Collections.Generic.List<DCFApixels.WhimTex.Layer>)Call("Collect", document.layers, chosen, false);
    Check(Same(roots, group, d), "Selected descendants must not be processed twice");
    Check(Same(all, group, nested, b, d), "Per-layer actions keep explicit nested selection in tree order");
    var expanded = (System.Collections.Generic.List<DCFApixels.WhimTex.Layer>)Call("Ungroup", document, all);
    Check(Same(document.layers, a, b, c, d), "Nested ungroup processes deepest groups first");
    Check(expanded.Count == 4 && !expanded.Contains(group) && !expanded.Contains(nested), "Ungroup selects resulting layers once");

    group.layers = Layers(a);
    document.layers = Layers(group, b, c, d);
    var plan = Call("PlanGroupMoves", document, Layers(b, c), true);
    Check(Same(document.layers, group, b, c, d) && Same(group.layers, a), "Group move planning is read-only");
    Call("ApplyGroupMoves", plan, true, document);
    Check(Same(document.layers, group, d) && Same(group.layers, a, b, c), "Move selected run into preceding group");
    plan = Call("PlanGroupMoves", document, Layers(b, c), false);
    Call("ApplyGroupMoves", plan, false, document);
    Check(Same(document.layers, group, b, c, d) && Same(group.layers, a), "Move out preserves sibling order");

    group.layers = Layers(a, b);
    nested.layers = Layers(c, d);
    document.layers = Layers(group, nested, e);
    plan = Call("PlanGroupMoves", document, Layers(a, b, c, d), false);
    Call("ApplyGroupMoves", plan, false, document);
    Check(Same(document.layers, group, a, b, nested, c, d, e), "Move out of multiple groups preserves anchors");
    Check(group.layers.Count == 0 && nested.layers.Count == 0, "All eligible children moved");
    for (int count = 0; count <= 5; count++)
    {
        Check((bool)Call("CanApplyChannelPreset", count) == (count >= 1 && count <= 4), "Automatic channel preset selection limit");
    }
    document.layers = Layers(a,b,c,d,e);
    foreach (var item in document.layers)
    {
        item.swizzle = default;
        item.blendMode = DCFApixels.WhimTex.BlendMode.Multiply;
        item.opacity = .42f;
    }
    Call("ApplyChannelPreset", document, Layers(e,c,a));
    var rgb = Layers(a,c,e);
    for (int index = 0; index < 3; index++)
    {
        for (int channel = 0; channel < 4; channel++)
            Check((int)rgb[index].swizzle[channel] == (channel == 3 ? 9 : channel == index ? 10 : 8), "RGB routes each source R times A in tree order");
        Check(rgb[index].blendMode == (index < 2 ? DCFApixels.WhimTex.BlendMode.Add : DCFApixels.WhimTex.BlendMode.Multiply), "Only upper selected layers change blending");
        Check(rgb[index].opacity == .42f, "Preset preserves opacity");
    }
    Check(b.swizzle.IsIdentity && d.swizzle.IsIdentity, "Unselected layers remain unchanged");
    string beforeInvalid = UnityEngine.JsonUtility.ToJson(document);
    Call("ApplyChannelPreset", document, Layers(a,b,c,d,e));
    Check(UnityEngine.JsonUtility.ToJson(document) == beforeInvalid, "Oversized selections cannot partially apply presets");
    Call("ApplyChannelPreset", document, Layers(d,b,c,a));
    var rgba = Layers(a,b,c,d);
    for (int index = 0; index < 4; index++)
        for (int channel = 0; channel < 4; channel++)
            Check((int)rgba[index].swizzle[channel] == (channel == index ? 10 : 8), "RGBA routes source R times A to every assigned channel, including alpha");
    Check(a.blendMode == DCFApixels.WhimTex.BlendMode.Add && b.blendMode == DCFApixels.WhimTex.BlendMode.Multiply,
        "RGBA does not rewrite blending");
    group.layers = Layers(a);
    group.compositing = DCFApixels.WhimTex.GroupCompositing.PassThrough;
    document.layers = Layers(group,b);
    Call("ApplyChannelPreset", document, Layers(b,group));
    Check(group.compositing == DCFApixels.WhimTex.GroupCompositing.Isolated && group.blendMode == DCFApixels.WhimTex.BlendMode.Add,
        "RGB makes group Add effective");
    Check(group.swizzle[0] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA && b.swizzle[1] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA,
        "A selected group counts once and unselected descendants do not consume channels");
    Call("ApplyChannelPreset", document, Layers(b,a,group));
    Check(group.swizzle[0] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA && a.swizzle[1] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA &&
        b.swizzle[2] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA, "Explicitly selected descendants count separately in tree order");
    Check(group.swizzle[3] == DCFApixels.WhimTex.SwizzleChannel.One, "Three nested selections automatically use RGB");
    Call("ApplyChannelPreset", document, Layers(b));
    Check(b.swizzle[0] == DCFApixels.WhimTex.SwizzleChannel.RMultiplyA && b.swizzle[3] == DCFApixels.WhimTex.SwizzleChannel.One,
        "Single selection automatically uses the first RGB channel");
    return new { success = true, checks };
}
finally { UnityEngine.Object.DestroyImmediate(document); }
