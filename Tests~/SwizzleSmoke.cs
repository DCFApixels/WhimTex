// Opt-in after manual compilation. Uses temporary documents/textures, no persistent assets or preferences.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new System.Exception(message); checks++; }
bool Near(float a, float b) => UnityEngine.Mathf.Abs(a - b) < .012f;
UnityEngine.Color Pixel()
{
    var texture = document.Compose();
    try { return texture.GetPixel(2, 2); }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
DCFApixels.WhimTex.LayerSwizzle Route(int output, int source)
{
    var result = new DCFApixels.WhimTex.LayerSwizzle();
    result[output] = (DCFApixels.WhimTex.SwizzleChannel)source;
    return result;
}
try
{
    document.width = document.height = 8;
    var layer = new DCFApixels.WhimTex.ColorFillLayerBehaviour
    {
        color = new UnityEngine.Color(.3f, .6f, .8f, .7f),
        colorRange = DCFApixels.WhimTex.LayerColorRange.HDR,
        blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR
    };
    document.layers.Add(layer);
    Check(layer.swizzle.IsIdentity, "New layers default to identity");
    Check(UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.ColorFillLayerBehaviour>("{}").swizzle.IsIdentity,
        "Missing serialized swizzle defaults to identity");
    UnityEngine.Color before = Pixel();
    for (int output = 0; output < 4; output++)
    for (int source = 0; source < System.Enum.GetValues(typeof(DCFApixels.WhimTex.SwizzleChannel)).Length; source++)
    {
        layer.swizzle = Route(output, source);
        var clone = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.Layer>(UnityEngine.JsonUtility.ToJson(layer.Owner));
        Check(clone.swizzle[output] == (DCFApixels.WhimTex.SwizzleChannel)source, "Channel serialization round-trip");
        var expected = before;
        expected[output] = source >= 10 ? before[source - 10] * before.a : source == 8 ? 0f : source == 9 ? 1f : source >= 4 ? 1f - before[source - 4] : before[source];
        var actual = Pixel();
        if (expected.a == 0f) Check(Near(actual.a, 0f), "Zero alpha is transparent");
        else for (int c = 0; c < 4; c++) Check(Near(actual[c], expected[c]), "Swizzle output channel " + output + " source " + source);
    }
    foreach (float alpha in new[] { 0f, .25f, 1f })
    {
        layer.swizzle = default;
        layer.color = new UnityEngine.Color(2f, .5f, .25f, alpha);
        var original = Pixel();
        var product = Route(0, 10);
        product[1] = DCFApixels.WhimTex.SwizzleChannel.GMultiplyA;
        product[2] = DCFApixels.WhimTex.SwizzleChannel.BMultiplyA;
        product[3] = DCFApixels.WhimTex.SwizzleChannel.One;
        layer.swizzle = product;
        var actual = Pixel();
        Check(Near(actual.a, 1f), "Product mapping can set output alpha independently");
        for (int channel = 0; channel < 3; channel++)
            Check(Near(actual[channel], original[channel] * alpha), "Products use original alpha, not remapped A");
    }
    layer.color = new UnityEngine.Color(2f, .5f, .25f, 1f);
    layer.swizzle = Route(0, 4);
    Check(Pixel().r < -3f, "HDR inverse preserves signed extended RGB");
    layer.colorRange = DCFApixels.WhimTex.LayerColorRange.Standard;
    Check(Near(Pixel().r, 0f), "Standard clamps after swizzle");
    layer.swizzle = default;
    layer.color = new UnityEngine.Color(.8f, .4f, .2f, 1f);
    layer.blendMode = DCFApixels.WhimTex.BlendMode.Multiply;
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    group.layers.Add(layer);
    var backdrop = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new UnityEngine.Color(.5f, .5f, .5f, 1f) };
    document.layers.Clear(); document.layers.Add(group); document.layers.Add(backdrop);
    var pass = Pixel();
    group.swizzle = Route(0, 2);
    var automatic = Pixel();
    group.compositing = DCFApixels.WhimTex.GroupCompositing.Isolated;
    var isolated = Pixel();
    for (int c = 0; c < 4; c++) Check(Near(automatic[c], isolated[c]), "Swizzle forces equivalent isolation");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.PassThrough;
    group.swizzle = default;
    var restored = Pixel();
    for (int c = 0; c < 4; c++) Check(Near(pass[c], restored[c]), "Identity restores Pass Through");
    document.layers.Remove(backdrop);
    layer.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
    layer.color = new UnityEngine.Color(.8f, .4f, .2f, .5f);
    group.swizzle = default;
    var groupBefore = Pixel();
    group.swizzle = Route(0, 10);
    var groupProduct = Pixel();
    Check(Near(groupProduct.r, groupBefore.r * groupBefore.a), "Group RGB multiplies isolated source alpha");
    Check(Near(groupProduct.a, groupBefore.a), "RGB product leaves group alpha unchanged");
    layer.color = UnityEngine.Color.red;
    group.swizzle = Route(3, 1);
    Check(Near(Pixel().a, 0f), "Group alpha is remapped");
    var coverage = (UnityEngine.RenderTexture)typeof(DCFApixels.WhimTex.TextureCompositor)
        .GetMethod("RenderGroupAlpha", flags).Invoke(document,
            new object[] { group.Owner, 8, 8, 1f, new System.Collections.Generic.HashSet<DCFApixels.WhimTex.Layer>() });
    var readback = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.RGBAFloat, false, true);
    var previous = UnityEngine.RenderTexture.active;
    try
    {
        UnityEngine.RenderTexture.active = coverage;
        readback.ReadPixels(new UnityEngine.Rect(0, 0, 8, 8), 0, 0, false);
        Check(Near(readback.GetPixel(2, 2).a, 0f), "SDF/Outline group target uses swizzled alpha");
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.RenderTexture.ReleaseTemporary(coverage);
        UnityEngine.Object.DestroyImmediate(readback);
    }
    var swizzled = Route(0, 2); swizzled[2] = DCFApixels.WhimTex.SwizzleChannel.R;
    layer.swizzle = swizzled;
    var rasterize = typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("RasterizeLayer", flags);
    var raw = (UnityEngine.Texture2D)rasterize.Invoke(document, new object[] { layer.Owner, true });
    try { Check(raw.GetPixel(2, 2).r > .98f && raw.GetPixel(2, 2).b < .01f, "Conversion keeps swizzle unbaked for non-group layers"); }
    finally { UnityEngine.Object.DestroyImmediate(raw); }
    var description = DCFApixels.WhimTex.WhimTexApi.Describe();
    Check(description.Contains("swizzleChannels") && description.Contains("1-A") &&
        description.Contains("R * A") && description.Contains("G * A") && description.Contains("B * A"), "Agent discovery includes swizzle choices");
    return "Swizzle checks passed: " + checks;
}
finally { UnityEngine.Object.DestroyImmediate(document); }
