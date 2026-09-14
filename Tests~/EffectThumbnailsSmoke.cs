// Unity Pipeline eval_file. Transient documents/textures only; no scene, asset or Undo edits.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 64;
var texture = new UnityEngine.Texture2D(64, 64, UnityEngine.TextureFormat.RGBA32, false);
texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var pixels = new UnityEngine.Color[4096];
for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
    pixels[y * 64 + x] = new UnityEngine.Color(x / 63f, .25f, .1f, x > 12 && x < 52 && y > 12 && y < 52 ? 1 : 0);
texture.SetPixels(pixels); texture.Apply(false, false);
var source = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = texture };
var sdf = new DCFApixels.WhimTex.SDFLayerBehaviour();
var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour { outlineWidth = 8 };
var normal = new DCFApixels.WhimTex.NormalMapLayerBehaviour();
var blur = new DCFApixels.WhimTex.BlurLayerBehaviour { radius = 8 };
var seamless = new DCFApixels.WhimTex.MakeSeamlessLayerBehaviour();
var processor = new DCFApixels.WhimTex.ShaderProcessorLayerBehaviour();
var effects = new DCFApixels.WhimTex.TargetedLayerBehaviour[] { sdf, outline, normal, blur, seamless };
document.layers.Add(processor);
foreach (var effect in effects) document.layers.Add(effect);
document.layers.Add(source);
void Normalize() => type.GetMethod("NormalizeModel", flags).Invoke(document, null);
Normalize();
foreach (var effect in effects) { effect.inputMode = DCFApixels.WhimTex.EffectInputMode.Specific; effect.TargetLayerId = source.Id; }
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
object Cache() => type.GetField("layerThumbnails", flags).GetValue(document);
void Invalidate() { var cache = Cache(); if (cache != null) cache.GetType().GetMethod("Invalidate", flags).Invoke(cache, null); }
int Renders() { var cache = Cache(); return cache == null ? 0 : (int)cache.GetType().GetProperty("RenderCount", flags).GetValue(cache); }
UnityEngine.Texture2D Get(DCFApixels.WhimTex.Layer layer, bool deferred = false, int size = 18) =>
    (UnityEngine.Texture2D)type.GetMethod("GetLayerThumbnail", flags).Invoke(document, new object[] { layer, size, deferred });
var previous = UnityEngine.RenderTexture.active;
bool srgb = UnityEngine.GL.sRGBWrite;
var sentinel = UnityEngine.RenderTexture.GetTemporary(8, 8);
var owned = new System.Collections.Generic.List<UnityEngine.Texture2D>();
try
{
    foreach (var effect in effects)
    {
        UnityEngine.RenderTexture.active = sentinel; UnityEngine.GL.sRGBWrite = true;
        var image = Get(effect); owned.Add(image);
        Check(image != null && image.width == 18 && image.height == 18, "Sized thumbnail: " + effect);
        Check(UnityEngine.RenderTexture.active == sentinel && UnityEngine.GL.sRGBWrite, "Graphics state preserved: " + effect);
        float alpha = 0; foreach (var pixel in image.GetPixels()) alpha = UnityEngine.Mathf.Max(alpha, pixel.a);
        Check(alpha > .01f, "Visible pixels: " + effect);
        Check(image.hideFlags == UnityEngine.HideFlags.HideAndDontSave && image.mipmapCount == 1, "Transient thumbnail");
        int count = Renders();
        for (int i = 0; i < 8; i++) { Invalidate(); Check(object.ReferenceEquals(image, Get(effect)), "Stable cache across polling cycles"); }
        Check(Renders() == count, "No redundant GPU renders: " + effect);
    }
    Check(Get(normal).GetPixel(9, 9).b > .8f, "Normal map blue channel");
    var oldOutline = Get(outline); outline.outlineWidth = 12; Invalidate();
    Check(Get(outline) != null && oldOutline == null, "Parameter edit replaces/releases thumbnail");
    int before = Renders(); source.layerName = "Changed source"; Invalidate(); Get(outline);
    Check(Renders() == before + 1, "Source revision invalidates dependent thumbnail");
    var untouched = Get(outline); seamless.falloff = 2; Invalidate();
    Check(object.ReferenceEquals(untouched, Get(outline)), "Unrelated effect does not invalidate specific target");
    var beforeTextureEdit = Get(blur);
    texture.SetPixel(32, 32, UnityEngine.Color.green); texture.Apply(false, false); Invalidate();
    Check(Get(blur) != null && beforeTextureEdit == null, "Texture content update invalidates dependencies");
    source.enabled = false; outline.enabled = false; Invalidate();
    Check(Get(outline) != null, "Hidden effect processes hidden source for its thumbnail");
    source.enabled = true; outline.enabled = true;
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour { enabled = false };
    document.layers.Remove(source); group.layers.Add(source); document.layers.Add(group); Normalize();
    foreach (var effect in effects) effect.TargetLayerId = group.Id;
    Invalidate();
    foreach (var effect in effects) Check(Get(effect) != null, "Isolated hidden group source: " + effect);
    var groupResult = Get(blur); source.opacity = .4f; Invalidate();
    Check(Get(blur) != null && groupResult == null, "Nested child change invalidates group consumer");
    var held = Get(blur); before = Renders(); blur.radius = 14; Invalidate();
    Check(object.ReferenceEquals(held, Get(blur, true)), "Interactive edit retains old thumbnail");
    Check(object.ReferenceEquals(held, Get(blur)) && Renders() == before, "Other consumers respect deferred refresh");
    Cache().GetType().GetField("deferUntil", flags).SetValue(Cache(), 0d); Invalidate();
    Check(Get(blur) != null && held == null && Renders() == before + 1, "Refresh once after interaction");
    var processorImage = Get(processor); Check(processorImage != null, "Shader Processor result thumbnail");
    before = Renders(); Invalidate(); Get(processor);
    Check(Renders() == before, "Shader Processor snapshot is not continuously rendered");
    sdf.inverted = !sdf.inverted; Invalidate();
    Check(Get(processor) != null && processorImage == null, "Shader Processor tracks layers below it");
    var normalCacheType = type.Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache");
    var normalCache = System.Activator.CreateInstance(normalCacheType, true);
    try
    {
        normalCacheType.GetMethod("BeginFrame", flags).Invoke(normalCache, new object[] { document, null });
        Check((ulong)normalCacheType.GetMethod("Stamp", flags).Invoke(normalCache, new object[] { processor.Owner }) == 0,
            "Main preview keeps dynamic Shader Processor uncached");
    }
    finally { ((System.IDisposable)normalCache).Dispose(); }
    var priorSize = Get(blur); document.width = 5120; document.height = 2560; Invalidate();
    var wide = Get(blur);
    Check(priorSize == null && wide.width == 18 && wide.height == 9, "Large canvas uses small aspect-correct thumbnail");
    var removed = Get(outline); document.layers.Remove(outline);
    type.GetMethod("RefreshThumbnailStructure", flags).Invoke(document, null);
    Check(removed == null, "Removing an effect releases its thumbnail even without further thumbnail requests");
    // Missing target and cyclic inputs remain bounded and recover when corrected.
    sdf.TargetLayerId = "missing"; Invalidate(); Get(sdf); before = Renders(); Invalidate(); Get(sdf);
    Check(Renders() == before, "Unavailable source does not trigger repeated rendering");
    sdf.TargetLayerId = blur.Id; blur.TargetLayerId = sdf.Id; Invalidate(); Get(sdf);
    before = Renders(); Invalidate(); Get(sdf); Check(Renders() == before, "Cyclic source does not trigger repeated rendering");
    sdf.TargetLayerId = blur.TargetLayerId = group.Id; Invalidate(); Check(Get(sdf) != null, "Thumbnail recovers after cycle is removed");
    var replaced = Get(blur); blur.Owner.SetBehaviour(new DCFApixels.WhimTex.ColorFillLayerBehaviour());
    type.GetMethod("RefreshThumbnailStructure", flags).Invoke(document, null);
    Check(replaced == null, "Changing to a non-effect behaviour releases cached effect thumbnail");
    var final = Get(normal);
    type.GetMethod("ReleaseLayerThumbnails", flags).Invoke(document, null);
    Check(final == null && Cache() == null, "Document lifecycle releases derived textures");
    foreach (var image in owned) Check(image == null, "No cached texture remains after release");
    return "Effect thumbnail checks passed: " + checks;
}
catch (System.Exception exception) { throw new System.Exception("After " + checks + " checks: " + exception.ToString()); }
finally
{
    UnityEngine.RenderTexture.active = previous; UnityEngine.GL.sRGBWrite = srgb;
    UnityEngine.RenderTexture.ReleaseTemporary(sentinel);
    UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(texture);
}
