// Run with Unity Pipeline eval_file. Uses transient objects only; no scene/asset writes.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var originalTarget = UnityEngine.RenderTexture.active;
bool originalSrgb = UnityEngine.GL.sRGBWrite;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 16;
var source = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RGBAHalf, false, true);
source.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var colors = new UnityEngine.Color[256];
for (int i = 0; i < colors.Length; i++) colors[i] = new UnityEngine.Color(.2f, .4f, .6f, 1f);
source.SetPixels(colors);
source.Apply(false, false);
var file = new DCFApixels.SpriteEditor.FileLayerBehaviour { sourceTexture = source };
var leaf = new DCFApixels.SpriteEditor.Layer(file);
var groupBehaviour = new DCFApixels.SpriteEditor.GroupLayerBehaviour();
var group = new DCFApixels.SpriteEditor.Layer(groupBehaviour);
groupBehaviour.layers.Add(leaf);
document.layers.Add(group);
var sentinel = new UnityEngine.RenderTexture(8, 8, 0) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
var sample = new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBAFloat, false, true);
sentinel.Create();
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new System.Exception("Render state regression: " + label);
    checks++;
}
void Probe(string name, object[] args, UnityEngine.RenderTexture expected, bool srgb, bool expectNull = false, bool expectThrow = false)
{
    UnityEngine.Object result = null;
    UnityEngine.RenderTexture.active = expected;
    UnityEngine.GL.sRGBWrite = srgb;
    try
    {
        bool threw = false;
        try { result = (UnityEngine.Object)type.GetMethod(name, flags).Invoke(document, args); }
        catch (System.Reflection.TargetInvocationException e) when (expectThrow && e.InnerException is System.InvalidOperationException) { threw = true; }
        Check(threw == expectThrow, name + " exception");
        Check(UnityEngine.RenderTexture.active == expected, name + " target restoration");
        Check(UnityEngine.GL.sRGBWrite == srgb, name + " sRGB restoration");
        Check(expectThrow || (result == null) == expectNull, name + " result");
        if (result is UnityEngine.RenderTexture rt)
        {
            Check(rt != expected && rt.IsCreated(), name + " output ownership");
            UnityEngine.RenderTexture.active = rt;
            sample.ReadPixels(new UnityEngine.Rect(8, 8, 1, 1), 0, 0, false);
            Check(sample.GetPixel(0, 0).a > .99f, name + " output pixels");
        }
        else if (result is UnityEngine.Texture2D texture)
            Check(texture.isReadable && texture.GetPixel(8, 8).a > .99f, name + " output pixels");
    }
    finally
    {
        // The probe itself must restore state even when running against broken code.
        UnityEngine.RenderTexture.active = expected;
        if (result is UnityEngine.RenderTexture rt) UnityEngine.RenderTexture.ReleaseTemporary(rt);
        else if (result != null) UnityEngine.Object.DestroyImmediate(result);
    }
}
try
{
    foreach (bool useSentinel in new[] { false, true })
    foreach (bool srgb in new[] { false, true })
    {
        var expected = useSentinel ? sentinel : null;
        Probe("RenderPreview", new object[] { 16 }, expected, srgb);
        foreach (var layer in new[] { leaf, group })
        {
            Probe("RenderLayerPreview", new object[] { layer, 16 }, expected, srgb);
            Probe("RenderAgentLayerPreview", new object[] { layer, 16 }, expected, srgb);
            Probe("RasterizeLayer", new object[] { layer, false }, expected, srgb);
            Probe("RasterizeLayer", new object[] { layer, true }, expected, srgb);
            Probe("RenderAreaSelectionSource", new object[] { layer }, expected, srgb);
            Probe("RenderAreaSelectionAlphaSource", new object[] { layer }, expected, srgb);
            Probe("RenderPsdPixels", new object[] { layer, false }, expected, srgb);
        }
        Probe("RenderPsdGroupContent", new object[] { group }, expected, srgb);
        leaf.enabled = false;
        Probe("RenderLayerPreview", new object[] { leaf, 16 }, expected, srgb, expectNull: true);
        Probe("RenderAgentLayerPreview", new object[] { leaf, 16 }, expected, srgb);
        leaf.enabled = true;
        Probe("RenderLayerPreview", new object[] { null, 16 }, expected, srgb, expectNull: true);
        Probe("RasterizeLayer", new object[] { null, false }, expected, srgb, expectThrow: true);
        Check(file.sourceTexture == source && source.GetPixel(8, 8).a == 1f, "borrowed source preserved");
    }
    return "Render state smoke passed: " + checks + " checks; null/non-null target, sRGB on/off, leaf/group, hidden/null/error paths.";
}
finally
{
    UnityEngine.RenderTexture.active = originalTarget;
    UnityEngine.GL.sRGBWrite = originalSrgb;
    UnityEngine.Object.DestroyImmediate(document);
    UnityEngine.Object.DestroyImmediate(source);
    UnityEngine.Object.DestroyImmediate(sentinel);
    UnityEngine.Object.DestroyImmediate(sample);
}
