// Unity Pipeline eval_file. Transient layers/textures only; no asset, scene or Undo changes.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var previous = UnityEngine.RenderTexture.active;
bool srgb = UnityEngine.GL.sRGBWrite;
var sentinel = UnityEngine.RenderTexture.GetTemporary(8, 8);
var noise = new DCFApixels.WhimTex.NoiseLayerBehaviour();
var shape = new DCFApixels.WhimTex.ShapeLayerBehaviour();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Release(DCFApixels.WhimTex.LayerBehaviour layer) => layer.GetType().GetMethod("ReleaseTransientResources", flags).Invoke(layer, null);
UnityEngine.Texture2D Preview(DCFApixels.WhimTex.LayerBehaviour layer, int size = 18)
{
    UnityEngine.RenderTexture.active = sentinel;
    UnityEngine.GL.sRGBWrite = true;
    var result = layer.GetPreviewTexture(size);
    Check(result != null && result.width == size && result.height == size, "Thumbnail has requested dimensions");
    Check(UnityEngine.RenderTexture.active == sentinel && UnityEngine.GL.sRGBWrite, "Rendering preserves caller graphics state");
    Check(result.hideFlags == UnityEngine.HideFlags.HideAndDontSave && result.mipmapCount == 1, "Thumbnail is transient without mipmaps");
    Check(UnityEngine.Object.ReferenceEquals(result, layer.GetPreviewTexture(size)), "Unchanged thumbnail reuses cached texture");
    return result;
}
try
{
    foreach (DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType kind in System.Enum.GetValues(typeof(DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType)))
    {
        noise.noiseType = kind;
        var texture = Preview(noise);
        float min = 1f, max = 0f;
        foreach (var pixel in texture.GetPixels()) { min = UnityEngine.Mathf.Min(min, pixel.r); max = UnityEngine.Mathf.Max(max, pixel.r); }
        Check(max - min > .01f, "Noise thumbnail contains varying pixels: " + kind);
    }
    var oldNoise = Preview(noise);
    noise.seed++;
    var changedNoise = Preview(noise);
    Check(oldNoise == null && changedNoise != null, "Noise settings replace and release cached pixels");
    noise.enabled = false; noise.opacity = 0;
    noise.transform.position = new UnityEngine.Vector2(9000, 9000);
    Check(UnityEngine.Object.ReferenceEquals(changedNoise, Preview(noise)), "Hidden/off-canvas layers keep their source thumbnail");
    foreach (DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind kind in System.Enum.GetValues(typeof(DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind)))
    {
        shape.kind = kind;
        shape.fillColor = UnityEngine.Color.red;
        shape.strokeColor = UnityEngine.Color.red;
        var texture = Preview(shape);
        float maxAlpha = 0f;
        foreach (var pixel in texture.GetPixels()) maxAlpha = UnityEngine.Mathf.Max(maxAlpha, pixel.a);
        Check(maxAlpha > .01f, "Shape thumbnail contains visible pixels: " + kind);
    }
    shape.kind = DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind.Ellipse;
    var ellipse = Preview(shape);
    Check(ellipse.GetPixel(0,0).a < .01f && ellipse.GetPixel(9,9).r > .9f, "Ellipse has clear corners and colored center");
    shape.fillColor = UnityEngine.Color.blue;
    var blue = Preview(shape);
    Check(ellipse == null && blue.GetPixel(9,9).b > .9f && blue.GetPixel(9,9).r < .01f, "Color edits refresh pixels");
    var resized = Preview(shape, 32);
    Check(blue == null, "Resizing releases old thumbnail");
    var nativeBefore = UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositor>().Length;
    for (int i = 0; i < 12; i++) { noise.seed++; Preview(noise); }
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositor>().Length == nativeBefore, "No transient document leaks");
    Release(shape); Check(resized == null, "Resource release destroys thumbnail");
    Preview(shape);
    var replacement = Preview(shape);
    shape.Owner.SetBehaviour(new DCFApixels.WhimTex.NoiseLayerBehaviour());
    Check(replacement == null, "Behaviour replacement releases thumbnail");
    return "Procedural thumbnail checks passed: " + checks;
}
finally
{
    Release(noise); Release(shape);
    UnityEngine.RenderTexture.active = previous;
    UnityEngine.GL.sRGBWrite = srgb;
    UnityEngine.RenderTexture.ReleaseTemporary(sentinel);
}
