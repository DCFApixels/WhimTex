// Independent migrated assertions; compiled and executed only by the parent runner.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
public static class ChannelMappingTests
{
public static string Run() => TestContext.Run("ChannelMappingTests", context => { using (var fixture = new MigrationD()) Execute(context, fixture); });
private static void Execute(TestContext context, MigrationD fixture)
{
// Opt-in after manual compilation. Uses temporary documents/textures, no persistent assets or preferences.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>();
int checks = 0;
void Check(bool condition, string message) { context.True(condition, message); }
bool Near(float a, float b) => UnityEngine.Mathf.Abs(a - b) < .012f;
UnityEngine.Color Pixel()
{
    var texture = document.ComposeCanvas();
    try { return texture.GetPixel(2, 2); }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
DCFApixels.WhimTex.LayerChannelMapping Route(int output, int source)
{
    var result = new DCFApixels.WhimTex.LayerChannelMapping();
    result[output] = (DCFApixels.WhimTex.ChannelMappingSource)source;
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
    Check(layer.channelMapping.IsIdentity, "New layers default to identity");
    Check(UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.ColorFillLayerBehaviour>("{}").channelMapping.IsIdentity,
        "Missing serialized channelMapping defaults to identity");
    UnityEngine.Color before = Pixel();
    for (int output = 0; output < 4; output++)
    for (int source = 0; source < System.Enum.GetValues(typeof(DCFApixels.WhimTex.ChannelMappingSource)).Length; source++)
    {
        layer.channelMapping = Route(output, source);
        var clone = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.Layer>(UnityEngine.JsonUtility.ToJson(layer.Owner));
        Check(clone.channelMapping[output] == (DCFApixels.WhimTex.ChannelMappingSource)source, "Channel serialization round-trip");
        var expected = before;
        float luminance = .2126f * before.r + .7152f * before.g + .0722f * before.b;
        expected[output] = source == 13 ? luminance : source == 14 ? luminance * before.a : source >= 10 ? before[source - 10] * before.a : source == 8 ? 0f : source == 9 ? 1f : source >= 4 ? 1f - before[source - 4] : before[source];
        var actual = Pixel();
        if (expected.a == 0f) Check(Near(actual.a, 0f), "Zero alpha is transparent");
        else for (int c = 0; c < 4; c++) Check(Near(actual[c], expected[c]), "ChannelMapping output channel " + output + " source " + source);
    }
    foreach (float alpha in new[] { 0f, .25f, 1f })
    {
        layer.channelMapping = default;
        layer.color = new UnityEngine.Color(2f, .5f, .25f, alpha);
        var original = Pixel();
        var product = Route(0, 10);
        product[1] = DCFApixels.WhimTex.ChannelMappingSource.GMultiplyA;
        product[2] = DCFApixels.WhimTex.ChannelMappingSource.BMultiplyA;
        product[3] = DCFApixels.WhimTex.ChannelMappingSource.One;
        layer.channelMapping = product;
        var actual = Pixel();
        Check(Near(actual.a, 1f), "Product mapping can set output alpha independently");
        for (int channel = 0; channel < 3; channel++)
            Check(Near(actual[channel], original[channel] * alpha), "Products use original alpha, not remapped A");
    }
    layer.color = new UnityEngine.Color(2f, .5f, .25f, 1f);
    layer.channelMapping = Route(0, 4);
    Check(Pixel().r < -3f, "HDR inverse preserves signed extended RGB");
    layer.colorRange = DCFApixels.WhimTex.LayerColorRange.Standard;
    Check(Near(Pixel().r, 0f), "Standard clamps after channelMapping");
    layer.channelMapping = default;
    layer.color = new UnityEngine.Color(.8f, .4f, .2f, 1f);
    layer.blendMode = DCFApixels.WhimTex.BlendMode.Multiply;
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    group.layers.Add(layer);
    var backdrop = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new UnityEngine.Color(.5f, .5f, .5f, 1f) };
    document.layers.Clear(); document.layers.Add(group); document.layers.Add(backdrop);
    var pass = Pixel();
    group.channelMapping = Route(0, 2);
    var automatic = Pixel();
    group.compositing = DCFApixels.WhimTex.GroupCompositing.Isolated;
    var isolated = Pixel();
    for (int c = 0; c < 4; c++) Check(Near(automatic[c], isolated[c]), "ChannelMapping forces equivalent isolation");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.PassThrough;
    group.channelMapping = default;
    var restored = Pixel();
    for (int c = 0; c < 4; c++) Check(Near(pass[c], restored[c]), "Identity restores Pass Through");
    document.layers.Remove(backdrop);
    layer.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
    layer.color = new UnityEngine.Color(.8f, .4f, .2f, .5f);
    group.channelMapping = default;
    var groupBefore = Pixel();
    group.channelMapping = Route(0, 10);
    var groupProduct = Pixel();
    Check(Near(groupProduct.r, groupBefore.r * groupBefore.a), "Group RGB multiplies isolated source alpha");
    Check(Near(groupProduct.a, groupBefore.a), "RGB product leaves group alpha unchanged");
    layer.color = UnityEngine.Color.red;
    group.channelMapping = Route(3, 1);
    Check(Near(Pixel().a, 0f), "Group alpha is remapped");
    var coverage = (UnityEngine.RenderTexture)typeof(DCFApixels.WhimTex.WhimTexDocument)
        .GetMethod("RenderGroupAlpha", flags).Invoke(document,
            new object[] { group.Owner, 8, 8, 1f, new System.Collections.Generic.HashSet<DCFApixels.WhimTex.Layer>() });
    var readback = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.RGBAFloat, false, true);
    var previous = UnityEngine.RenderTexture.active;
    try
    {
        UnityEngine.RenderTexture.active = coverage;
        readback.ReadPixels(new UnityEngine.Rect(0, 0, 8, 8), 0, 0, false);
        Check(Near(readback.GetPixel(2, 2).a, 0f), "SDF/Outline group target uses channelMapped alpha");
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.RenderTexture.ReleaseTemporary(coverage);
        UnityEngine.Object.DestroyImmediate(readback);
    }
    var channelMapped = Route(0, 2); channelMapped[2] = DCFApixels.WhimTex.ChannelMappingSource.R;
    layer.channelMapping = channelMapped;
    var rasterize = typeof(DCFApixels.WhimTex.WhimTexDocument).GetMethod("RasterizeLayer", flags);
    var raw = (UnityEngine.Texture2D)rasterize.Invoke(document, new object[] { layer.Owner, true });
    try { Check(raw.GetPixel(2, 2).r > .98f && raw.GetPixel(2, 2).b < .01f, "Conversion keeps channelMapping unbaked for non-group layers"); }
    finally { UnityEngine.Object.DestroyImmediate(raw); }
    var description = DCFApixels.WhimTex.WhimTexApi.Describe();
    Check(description.Contains("channelMappingSources") && description.Contains("1-A") &&
        description.Contains("R * A") && description.Contains("G * A") && description.Contains("B * A") &&
        description.Contains("Luminance") && description.Contains("Luminance * A"), "Agent discovery includes channelMapping choices");
    CheckLuminance(context, document);
    CheckContract(context, document);
    return;
}
finally { UnityEngine.Object.DestroyImmediate(document); }

}

private static void CheckContract(TestContext context, DCFApixels.WhimTex.WhimTexDocument document)
{
    context.Equal(2, DCFApixels.WhimTex.WhimTexDocumentJson.Version, "Current JSON format version");
    context.Equal(2, DCFApixels.WhimTex.WhimTexDocumentContainer.CurrentVersion, "Current TIFF container version");
    var layer = document.layers[0];
    var mapping = new DCFApixels.WhimTex.LayerChannelMapping
    {
        [0] = DCFApixels.WhimTex.ChannelMappingSource.B,
        [1] = DCFApixels.WhimTex.ChannelMappingSource.Luminance,
        [2] = DCFApixels.WhimTex.ChannelMappingSource.Zero,
        [3] = DCFApixels.WhimTex.ChannelMappingSource.One
    };
    layer.channelMapping = mapping;
    string json = DCFApixels.WhimTex.WhimTexDocumentJson.Write(document,
        new DCFApixels.WhimTex.WhimTexJsonWriteOptions { Mode = DCFApixels.WhimTex.WhimTexJsonWriteMode.Full }).Json;
    context.True(json.Contains("\"channelMapping\"") && !json.Contains("\"swizzle\""), "Only the current storage key is written");
    void RejectJson(string input, string message)
    {
        bool rejected = false;
        try { using var read = DCFApixels.WhimTex.WhimTexDocumentJson.Read(input, false); }
        catch (DCFApixels.WhimTex.WhimTexDocumentException) { rejected = true; }
        context.True(rejected, message);
    }
    RejectJson(json.Replace("\"version\": 2", "\"version\": 1"), "Old JSON versions are rejected");
    RejectJson(json.Replace("\"channelMapping\"", "\"swizzle\""), "Old storage keys are not aliases");
    var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    var serializer = document.GetType().Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
    using var container = new DCFApixels.WhimTex.WhimTexDocumentContainer();
    var bytes = (byte[])serializer.GetMethod("Serialize", flags).Invoke(null, new object[] { document, container });
    context.Equal(2, BitConverter.ToInt32(bytes, 0), "Current binary model version");
    context.True(System.Text.Encoding.UTF8.GetString(bytes).Contains("LayerChannelMapping"), "Binary data records the current type name");
    container.Set(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock, bytes);
    byte[] payload = container.Serialize();
    context.Equal(2, BitConverter.ToInt32(payload, 8), "Serialized container version");
    using (var parsed = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(payload))
        context.Equal(2, BitConverter.ToInt32(parsed.Get(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock), 0), "Current container keeps current model");
    Array.Copy(BitConverter.GetBytes(1), 0, payload, 8, 4);
    bool rejectedContainer = false;
    try { using var parsed = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(payload); }
    catch (DCFApixels.WhimTex.WhimTexDocumentException) { rejectedContainer = true; }
    context.True(rejectedContainer, "Old container versions are rejected");
    Array.Copy(BitConverter.GetBytes(1), bytes, 4);
    bool rejectedModel = false;
    try { serializer.GetMethod("Deserialize", flags).Invoke(null, new object[] { bytes, container, document.GetType(), null, false }); }
    catch (System.Reflection.TargetInvocationException error) { rejectedModel = error.InnerException is DCFApixels.WhimTex.WhimTexDocumentException; }
    context.True(rejectedModel, "Old binary model versions are rejected");
    var setLayer = typeof(DCFApixels.WhimTex.WhimTexApi).GetMethod("SetLayer", flags);
    var settingsType = setLayer.GetParameters()[2].ParameterType;
    object Settings(string value) => settingsType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
    setLayer.Invoke(null, new[] { (object)document, layer, Settings("{\"channelMapping\":[\"G\",\"R\",\"B\",\"1\"]}") });
    context.Equal(DCFApixels.WhimTex.ChannelMappingSource.G, layer.channelMapping[0], "API applies current mapping key");
    context.Equal(DCFApixels.WhimTex.ChannelMappingSource.One, layer.channelMapping[3], "API keeps source labels independent of output channels");
    bool rejectedApi = false;
    try { setLayer.Invoke(null, new[] { (object)document, layer, Settings("{\"swizzle\":[\"R\",\"G\",\"B\",\"A\"]}") }); }
    catch (System.Reflection.TargetInvocationException error) { rejectedApi = error.InnerException.Message.Contains("swizzle"); }
    context.True(rejectedApi, "Old API key is rejected explicitly");
    context.Equal(DCFApixels.WhimTex.ChannelMappingSource.G, layer.channelMapping[0], "Rejected patch does not replace the mapping");
}

private static void CheckLuminance(TestContext context, DCFApixels.WhimTex.WhimTexDocument document)
{
    var type = document.GetType();
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    float Luminance(Color color) => .2126f * color.r + .7152f * color.g + .0722f * color.b;
    void Same(Color expected, Color actual, string message)
    {
        for (int c = 0; c < 4; c++) context.Near(expected[c], actual[c], .003, message + " channel " + c);
    }
    Color Read(RenderTexture target)
    {
        context.True(target != null, "Rendered target exists");
        var previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            return pixels.GetPixel(2, 2);
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
    }
    DCFApixels.WhimTex.LayerChannelMapping Grayscale(int source)
    {
        var result = new DCFApixels.WhimTex.LayerChannelMapping();
        for (int output = 0; output < 3; output++) result[output] = (DCFApixels.WhimTex.ChannelMappingSource)source;
        result[3] = DCFApixels.WhimTex.ChannelMappingSource.One;
        return result;
    }
    foreach (var input in new[] { new Color(.3f, .6f, .8f, 0), new Color(.3f, .6f, .8f, .25f),
        new Color(.3f, .6f, .8f, 1), new Color(2, -3, 6, .5f) })
    foreach (int source in new[] { 13, 14 })
    foreach (bool clamp in new[] { false, true })
    {
        var raw = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        try { RenderTexture.active = raw; GL.Clear(false, true, input); }
        finally { RenderTexture.active = previous; }
        RenderTexture finished;
        try { finished = (RenderTexture)type.GetMethod("FinishStage", flags).Invoke(document, new object[] { raw, clamp, Grayscale(source) }); }
        catch { RenderTexture.ReleaseTemporary(raw); throw; }
        float value = Luminance(input) * (source == 14 ? input.a : 1);
        if (clamp) value = Mathf.Clamp01(value);
        Same(new Color(value, value, value, 1), Read(finished), "Linear luminance / original alpha / Color Range");
    }
    var fill = new DCFApixels.WhimTex.ColorFillLayerBehaviour
    {
        color = new Color(.6f, .8f, .3f, 1),
        colorRange = DCFApixels.WhimTex.LayerColorRange.HDR,
        blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR
    };
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    group.layers.Add(fill);
    document.layers.Clear(); document.layers.Add(group);
    float brightness;
    var original = document.ComposeCanvas();
    try { brightness = Luminance(original.GetPixel(2, 2)); }
    finally { UnityEngine.Object.DestroyImmediate(original); }
    group.channelMapping = new DCFApixels.WhimTex.LayerChannelMapping
    {
        [0] = DCFApixels.WhimTex.ChannelMappingSource.One, [1] = DCFApixels.WhimTex.ChannelMappingSource.One,
        [2] = DCFApixels.WhimTex.ChannelMappingSource.One, [3] = DCFApixels.WhimTex.ChannelMappingSource.Luminance
    };
    var expectedGroup = new Color(1, 1, 1, brightness);
    var composite = document.ComposeCanvas();
    try { Same(expectedGroup, composite.GetPixel(2, 2), "Group luminance to alpha composite"); }
    finally { UnityEngine.Object.DestroyImmediate(composite); }
    var expectedCoverage = new Color(brightness, brightness, brightness, brightness);
    Same(expectedCoverage, Read((RenderTexture)type.GetMethod("RenderLayerPreview", flags).Invoke(document,
        new object[] { group.Owner, 8 })), "Group coverage preview");
    Same(expectedGroup, Read((RenderTexture)type.GetMethod("RenderAgentLayerPreview", flags).Invoke(document,
        new object[] { group.Owner, 8 })), "Target color input");
    var alpha = Read((RenderTexture)type.GetMethod("RenderGroupAlpha", flags).Invoke(document,
        new object[] { group.Owner, 8, 8, 1f, new HashSet<DCFApixels.WhimTex.Layer>() }));
    context.Near(brightness, alpha.a, .003, "Group target alpha");
    var exported = (Texture2D)type.GetMethod("RenderPsdGroupContent", flags).Invoke(document, new object[] { group.Owner });
    try { Same(expectedGroup, exported.GetPixel(2, 2), "PSD group pixels"); }
    finally { UnityEngine.Object.DestroyImmediate(exported); }
    var cacheType = type.Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache");
    using (var cache = (IDisposable)Activator.CreateInstance(cacheType, true))
    {
        for (int pass = 0; pass < 2; pass++)
            Same(expectedGroup, Read((RenderTexture)type.GetMethod("RenderCanvasWithCache", flags).Invoke(document,
                new object[] { 8, cache, false, null })), "Cached canvas");
        Same(expectedCoverage, Read((RenderTexture)type.GetMethod("RenderLayerThumbnail", flags).Invoke(document,
            new object[] { group.Owner, 8, cache })), "Group coverage thumbnail");
        group.channelMapping = default;
        var restored = Read((RenderTexture)type.GetMethod("RenderCanvasWithCache", flags).Invoke(document,
            new object[] { 8, cache, false, null }));
        context.Near(1, restored.a, .003, "Cache invalidates changed ChannelMapping");
    }
    document.layers.Clear(); document.layers.Add(fill);
    fill.channelMapping = Grayscale(14);
    fill.channelMapping[3] = DCFApixels.WhimTex.ChannelMappingSource.Luminance;
    foreach (DCFApixels.WhimTex.WhimTexJsonWriteMode mode in Enum.GetValues(typeof(DCFApixels.WhimTex.WhimTexJsonWriteMode)))
    {
        var json = DCFApixels.WhimTex.WhimTexDocumentJson.Write(document,
            new DCFApixels.WhimTex.WhimTexJsonWriteOptions { Mode = mode });
        using var read = DCFApixels.WhimTex.WhimTexDocumentJson.Read(json.Json, false);
        context.Equal(0, read.Warnings.Count, "JSON has no unknown field warnings");
        context.Equal(fill.channelMapping, read.Document.layers[0].channelMapping, "JSON keeps both new packed channel codes");
        var pixels = read.Document.ComposeCanvas();
        var baseline = document.ComposeCanvas();
        try { Same(baseline.GetPixel(2, 2), pixels.GetPixel(2, 2), "JSON render roundtrip " + mode); }
        finally { UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(baseline); }
    }
    var serializer = type.Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
    using (var container = new DCFApixels.WhimTex.WhimTexDocumentContainer())
    {
        var bytes = serializer.GetMethod("Serialize").Invoke(null, new object[] { document, container });
        var read = serializer.GetMethod("Deserialize").Invoke(null, new object[] { bytes, container, type, null, false });
        var clone = (DCFApixels.WhimTex.WhimTexDocument)read.GetType().GetProperty("Model", flags).GetValue(read);
        try { context.Equal(fill.channelMapping, clone.layers[0].channelMapping, "TIFF model serialization keeps new channels"); }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
    var clipped = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.red, clippingMask = true };
    fill.channelMapping = new DCFApixels.WhimTex.LayerChannelMapping { [3] = DCFApixels.WhimTex.ChannelMappingSource.Luminance };
    document.layers.Insert(0, clipped);
    var clippedPixels = document.ComposeCanvas();
    try { Same(new Color(1, 0, 0, brightness), clippedPixels.GetPixel(2, 2), "Clipping uses luminance alpha"); }
    finally { UnityEngine.Object.DestroyImmediate(clippedPixels); }
}
}
