// Opt-in after manual compilation. Transient documents/textures only; no asset saves or imports.
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
doc.width = doc.height = 8;
int checks = 0;
var owned = new System.Collections.Generic.List<UnityEngine.Object>();
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Near(float value, float expected, string message) => Check(UnityEngine.Mathf.Abs(value - expected) < .013f,
    message + ": " + value + " != " + expected);
void Same(UnityEngine.Color actual, UnityEngine.Color expected, string message)
{
    for (int c = 0; c < 4; c++) Near(actual[c], expected[c], message + " channel " + c);
}
DCFApixels.WhimTex.ColorFillLayerBehaviour Fill(UnityEngine.Color color, bool clipped = false) =>
    new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = color, clippingMask = clipped };
void Alpha(DCFApixels.WhimTex.ColorFillLayerBehaviour layer, float alpha)
{
    var color = layer.color; color.a = alpha; layer.color = color;
}
void Stack(params DCFApixels.WhimTex.Layer[] layers)
{
    doc.layers.Clear(); doc.layers.AddRange(layers);
    type.GetMethod("NormalizeModel", flags).Invoke(doc, null);
}
UnityEngine.Color Pixel()
{
    var texture = doc.Compose();
    try { return texture.GetPixel(4, 4); }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
UnityEngine.Color Read(UnityEngine.RenderTexture rt)
{
    Check(rt != null, "Render target exists");
    var texture = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.RGBAFloat, false, true);
    var previous = UnityEngine.RenderTexture.active;
    try
    {
        UnityEngine.RenderTexture.active = rt;
        texture.ReadPixels(new UnityEngine.Rect(0, 0, 8, 8), 0, 0, false);
        return texture.GetPixel(4, 4);
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.RenderTexture.ReleaseTemporary(rt);
    }
}
UnityEngine.Color Coverage(DCFApixels.WhimTex.Layer layer)
{
    var method = type.GetMethod("RenderLayerPreview", flags);
    return Read((UnityEngine.RenderTexture)method.Invoke(doc, new object[] { layer, 8 }));
}
try
{
    var basis = Fill(new UnityEngine.Color(1, 0, 0, .25f));
    var lower = Fill(UnityEngine.Color.green, true);
    var upper = Fill(UnityEngine.Color.blue, true);
    basis.opacity = .4f;
    Stack(upper, lower, basis);
    Check(doc.GetClippingBase(upper) == basis && doc.GetClippingBase(lower) == basis, "A chain shares one base");
    Check(doc.GetClippingBase(basis) == null, "A base has no clipping dependency");
    Same(Pixel(), new UnityEngine.Color(0, 0, 1, .1f), "Two opaque clips retain soft base alpha and base opacity once");
    lower.enabled = false;
    Same(Pixel(), new UnityEngine.Color(0, 0, 1, .1f), "Hidden clipping sibling does not become a new base");
    upper.opacity = 0;
    Same(Pixel(), new UnityEngine.Color(1, 0, 0, .1f), "Zero-opacity clipping sources leave base unchanged");
    upper.opacity = 1;
    foreach (DCFApixels.WhimTex.BlendMode mode in System.Enum.GetValues(typeof(DCFApixels.WhimTex.BlendMode)))
    {
        upper.blendMode = mode;
        Near(Pixel().a, .1f, "Base alpha preserved with " + mode);
    }
    upper.blendMode = DCFApixels.WhimTex.BlendMode.Overwrite;
    upper.color = UnityEngine.Color.clear;
    Same(Pixel(), new UnityEngine.Color(1, 0, 0, .1f), "Transparent clipped Overwrite cannot erase the base");
    upper.color = UnityEngine.Color.blue;
    upper.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
    basis.enabled = false; Near(Pixel().a, 0, "Hidden base hides chain"); basis.enabled = true;
    basis.opacity = 0; Near(Pixel().a, 0, "Zero-opacity base hides chain"); basis.opacity = .4f;
    Alpha(basis, 0); Near(Pixel().a, 0, "Transparent base hides chain"); Alpha(basis, .25f);
    Stack(upper, lower); Near(Pixel().a, 0, "Orphan clipping layers are invisible");
    Stack(upper, lower, basis);
    Near(Coverage(upper).a, .1f, "Effect/preview coverage includes base alpha and opacity");
    var effect = new DCFApixels.WhimTex.OutlineLayerBehaviour();
    Stack(effect, upper, lower, basis);
    var input = (UnityEngine.RenderTexture)type.GetMethod("RenderPreviousInput", flags).Invoke(doc,
        new object[] { doc.layers, 0, 8, 8, 1f, new System.Collections.Generic.HashSet<DCFApixels.WhimTex.Layer>() });
    Near(Read(input).a, .1f, "Previous effect input uses clipped coverage");

    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    group.layers.Add(upper); group.layers.Add(lower);
    Stack(group, basis);
    Check(doc.GetClippingBase(upper) == null, "Clipping does not reach a root base from inside a group");
    Near(Pixel().a, .1f, "Orphan children contribute nothing outside their group");
    group.layers.Clear(); group.layers.Add(basis);
    Stack(upper, group);
    var passClipped = Pixel();
    Same(passClipped, new UnityEngine.Color(0, 0, 1, .1f), "Pass Through group can be a clipping base");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.Isolated;
    Same(Pixel(), passClipped, "Participating group is already isolated");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.PassThrough;
    group.layers.Clear(); group.layers.Add(Fill(UnityEngine.Color.blue));
    group.clippingMask = true; group.opacity = .5f;
    Stack(group, basis);
    Near(Pixel().a, .1f, "A clipped group does not change base coverage");
    Near(Coverage(group).a, .05f, "Clipped group input includes its opacity once");
    var groupBefore = Pixel();
    var groupPixels = (UnityEngine.Texture2D)type.GetMethod("RasterizeLayer", flags).Invoke(doc, new object[] { group.Owner, true });
    owned.Add(groupPixels);
    Near(groupPixels.GetPixel(4, 4).a, 1, "Group conversion leaves outer opacity and clipping unbaked");
    var groupDrawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour)
        .GetMethod("FromRasterizedLayer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        .Invoke(null, new object[] { group.Owner, groupPixels, true, true });
    Stack(groupDrawing, basis);
    Same(Pixel(), groupBefore, "Clipped group conversion preserves appearance");
    Stack(group, basis);

    // Alpha remapping and transforms happen before clipping; sources stay unmodified.
    basis.swizzle[3] = DCFApixels.WhimTex.SwizzleChannel.Zero;
    Near(Pixel().a, 0, "Base Swizzle determines clipping coverage");
    basis.swizzle = default;
    basis.transform.position = new UnityEngine.Vector2(100, 100);
    Near(Pixel().a, 0, "Base Transform determines clipping coverage");
    basis.transform = DCFApixels.WhimTex.TextureTransform.Default;
    Stack(upper, basis);
    var rasterized = (UnityEngine.Texture2D)type.GetMethod("RasterizeLayer", flags).Invoke(doc, new object[] { upper.Owner, true });
    owned.Add(rasterized);
    Near(rasterized.GetPixel(4, 4).a, 1, "Conversion leaves clipping unbaked");
    var converted = (DCFApixels.WhimTex.DrawingLayerBehaviour)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour)
        .GetMethod("FromRasterizedLayer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        .Invoke(null, new object[] { upper.Owner, rasterized, true, false });
    Check(converted.clippingMask, "Drawing conversion preserves clipping flag");
    Check(UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.Layer>(UnityEngine.JsonUtility.ToJson(upper.Owner)).clippingMask,
        "Copy/serialization preserves clipping");
    Check(!UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.ColorFillLayerBehaviour>("{}").clippingMask,
        "Legacy/new layers default to unclipped");

    // HDR uses the same alpha contract without clamping extended linear colors.
    upper.colorRange = DCFApixels.WhimTex.LayerColorRange.HDR;
    upper.blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR;
    basis.blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR;
    upper.color = new UnityEngine.Color(2, 0, 0, 1);
    Check(Coverage(upper).r > 1, "Clipped standalone source retains HDR");
    Near(Pixel().a, .1f, "HDR does not change clipping coverage");
    upper.colorRange = DCFApixels.WhimTex.LayerColorRange.Standard;
    upper.blendRange = basis.blendRange = DCFApixels.WhimTex.LayerBlendRange.Standard;
    upper.color = UnityEngine.Color.blue;

    // API validates and reports the setting for every layer type without asset I/O.
    var api = typeof(DCFApixels.WhimTex.WhimTexApi);
    var set = api.GetMethod("SetLayer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    foreach (var layer in new DCFApixels.WhimTex.Layer[] { upper, group, new DCFApixels.WhimTex.DrawingLayerBehaviour(),
        new DCFApixels.WhimTex.FileLayerBehaviour(), new DCFApixels.WhimTex.GradientLayerBehaviour(), effect, new DCFApixels.WhimTex.SDFLayerBehaviour() })
    {
        set.Invoke(null, new object[] { doc, layer, Newtonsoft.Json.Linq.JObject.Parse("{\"clippingMask\":true}") });
        Check(layer.clippingMask, "Agent setting accepted for " + layer.GetType().Name);
    }
    Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("clippingMask"), "Agent discovery documents clipping");

    // Merge rendering uses the same selection plan as the actual merge without persistent assets/Undo side effects.
    var createPlan = type.GetMethod("CreateMergePlan", flags);
    var rasterizeMerge = type.GetMethod("RasterizeMerge", flags);
    Stack(upper, basis);
    var all = new System.Collections.Generic.List<DCFApixels.WhimTex.Layer> { upper, basis };
    var plan = createPlan.Invoke(doc, new object[] { all });
    var merged = (UnityEngine.Texture2D)rasterizeMerge.Invoke(doc, new object[] { plan });
    owned.Add(merged);
    Near(merged.GetPixel(4, 4).a, .1f, "Full chain merge retains base alpha");
    plan = createPlan.Invoke(doc, new object[] { new System.Collections.Generic.List<DCFApixels.WhimTex.Layer> { upper } });
    merged = (UnityEngine.Texture2D)rasterizeMerge.Invoke(doc, new object[] { plan });
    owned.Add(merged);
    Same(merged.GetPixel(4, 4), new UnityEngine.Color(0, 0, 1, .1f), "Partial merge bakes mask without unselected base color");
    Alpha(upper, .5f);
    lower.enabled = true; Alpha(lower, .5f);
    Stack(upper, lower, basis);
    Near(Pixel().a, .1f, "Fractional clipping sources still preserve full base coverage");
    plan = createPlan.Invoke(doc, new object[] { new System.Collections.Generic.List<DCFApixels.WhimTex.Layer> { upper, lower } });
    merged = (UnityEngine.Texture2D)rasterizeMerge.Invoke(doc, new object[] { plan });
    owned.Add(merged);
    Near(merged.GetPixel(4, 4).a, .075f, "Partial chain applies base coverage once after source union");
    return "Clipping mask checks passed: " + checks;
}
finally
{
    foreach (var item in owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
    UnityEngine.Object.DestroyImmediate(doc);
}
