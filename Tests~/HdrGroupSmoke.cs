// Opt-in after manual compilation, using a live Editor C# evaluation bridge.
// No project build, source import, scene edit or persistent asset is performed.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
Texture2D Stored() => (Texture2D)drawing.GetType().GetProperty("StoredTexture", flags).GetValue(drawing);
Color Pixel()
{
    var texture = document.Compose();
    try { Check(texture.format == TextureFormat.RGBAHalf, "Composition stores linear half-float"); return texture.GetPixel(2, 2); }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
bool Near(float a, float b) => Mathf.Abs(a - b) < 0.008f;
try
{
    document.width = document.height = 8;
    var bright = new DCFApixels.WhimTex.ColorFillLayerBehaviour
    { color = new Color(2, -.5f, .25f, 1), colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
    document.layers.Add(bright);
    Color hdr = Pixel();
    Check(hdr.r > 4 && hdr.g < 0, "HDR preserves bright and negative RGB");
    bright.colorRange = DCFApixels.WhimTex.LayerColorRange.Standard;
    Color limited = Pixel();
    Check(Near(limited.r, 1) && Near(limited.g, 0), "Standard clamps own output");
    bright.colorRange = DCFApixels.WhimTex.LayerColorRange.HDR;
    var transparent = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.clear };
    document.layers.Insert(0, transparent);
    Check(Near(Pixel().r, hdr.r), "Transparent Standard layer preserves underlying HDR");
    transparent.color = Color.white; transparent.opacity = 0;
    Check(Near(Pixel().r, hdr.r), "Zero opacity preserves underlying HDR");

    var child = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new Color(.8f, 0, 0, 1),
        blendMode = DCFApixels.WhimTex.BlendMode.Multiply, blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR };
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour();
    group.layers.Add(child);
    bright.color = new Color(.5f, .5f, .5f, 1);
    document.layers.Clear(); document.layers.Add(group); document.layers.Add(bright);
    Color pass = Pixel();
    group.compositing = DCFApixels.WhimTex.GroupCompositing.Isolated;
    Color isolated = Pixel();
    Check(isolated.r > pass.r * 3, "Isolation prevents child blend from sampling external backdrop");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.PassThrough;
    group.opacity = .5f;
    Color faded = Pixel();
    Check(Near(faded.r, (pass.r + .214041f) * .5f), "Pass-through opacity interpolates before/after in linear light");

    document.layers.Remove(bright);
    child.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
    child.color = Color.red;
    group.layers.Add(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.red });
    Check(Near(Pixel().a, .5f), "Group opacity is not multiplied into each child");
    var outer = new DCFApixels.WhimTex.GroupLayerBehaviour { opacity = .5f };
    outer.layers.Add(group); document.layers[0] = outer;
    Check(Near(Pixel().a, .25f), "Nested group opacity");
    document.layers.Add(bright);
    var coverage = (RenderTexture)Call(document, "RenderGroupAlpha", outer.Owner, 8, 8, 1f,
        new System.Collections.Generic.HashSet<DCFApixels.WhimTex.Layer>());
    var readback = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
    var previous = RenderTexture.active;
    try
    {
        RenderTexture.active = coverage;
        readback.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        Check(Near(readback.GetPixel(2, 2).a, .25f), "Effect group target excludes external backdrop");
    }
    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(coverage); UnityEngine.Object.DestroyImmediate(readback); }

    document.layers.Clear(); document.layers.Add(drawing);
    Call(drawing, "InitializeCanvas", 8, 8);
    Check(Stored().format == TextureFormat.RGBA32, "Drawing begins compact");
    Call(drawing, "SetColorRange", DCFApixels.WhimTex.LayerColorRange.HDR);
    Check(Stored().format == TextureFormat.RGBAHalf, "Selecting HDR promotes storage");
    Color[] source = new Color[64];
    for (int i = 0; i < source.Length; i++) source[i] = new Color(4, 0, 0, 1);
    Stored().SetPixels(source); Stored().Apply(false, false); Call(drawing, "InvalidatePaintSurface");
    Call(drawing, "SetColorRange", DCFApixels.WhimTex.LayerColorRange.Standard);
    Check(Stored().format == TextureFormat.RGBAHalf && Near(Stored().GetPixel(0, 0).r, 4), "Standard retains hidden HDR");
    drawing.brushColor = new Color(.5f, .5f, .5f, .5f); drawing.brushSize = 3; drawing.brushHardness = 1;
    Call(drawing, "PrepareStroke", 8, 8, "HDR smoke stroke");
    Call(drawing, "BeginStroke", new Vector2(.5625f, .5625f));
    Call(drawing, "PaintPoint", new Vector2(.5625f, .5625f), 8, 8, Call(drawing, "GetStrokeParameters", false));
    Call(drawing, "EndStroke"); Call(drawing, "SyncSurfaceToTexture");
    Check(Near(Stored().GetPixel(4, 4).r, .6070205f), "Standard brush reads clamped HDR under its footprint");
    Check(Near(Stored().GetPixel(0, 0).r, 4), "Brush leaves hidden HDR outside footprint unchanged");
    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(document, "HDR smoke compact");
    Call(drawing, "ConvertTo8Bit"); Call(document, "MarkChanged"); Undo.FlushUndoRecordObjects();
    Check(Stored().format == TextureFormat.RGBA32, "Explicit compact changes storage");
    Undo.PerformUndo(); drawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)document.layers[0];
    Check(Stored().format == TextureFormat.RGBAHalf && Near(Stored().GetPixel(0, 0).r, 4), "Undo restores storage format and HDR pixels");
    Undo.PerformRedo(); drawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)document.layers[0];
    Check(Stored().format == TextureFormat.RGBA32, "Redo reapplies compact");

    document.layers.Clear(); document.layers.Add(bright);
    bright.color = new Color(float.NaN, 1000, .5f, 1);
    Color safe = Pixel();
    Check(Near(safe.r, 0) && Near(safe.g, 65504) && safe.b > 0, "Nonfinite components become zero; finite half-overflow clamps");
    Debug.Log($"HDR/group smoke: {checks} checks passed. Persistent save/reopen still needs its separate opt-in asset test.");
}
finally
{
    if (Stored() != null) Undo.ClearUndo(Stored());
    Undo.ClearUndo(document);
    Call(drawing, "ReleaseTransientResources");
    UnityEngine.Object.DestroyImmediate(document);
}
