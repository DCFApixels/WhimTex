// Opt-in after manual compilation. Temporary CPU textures/documents only; no windows, assets, Undo or reload triggered.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var drawingType = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour);
var documentType = typeof(DCFApixels.WhimTex.TextureCompositor);
var pixelsField = drawingType.GetField("pixels", Hidden);
var disable = documentType.GetMethod("OnDisable", Hidden);
var enable = documentType.GetMethod("OnEnable", Hidden);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new System.Exception(message);
    checks++;
}
foreach (var format in new[] { UnityEngine.TextureFormat.RGBA32, UnityEngine.TextureFormat.RGBAHalf })
{
    var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    var textures = new System.Collections.Generic.List<UnityEngine.Texture2D>();
    var layers = new System.Collections.Generic.List<DCFApixels.WhimTex.DrawingLayerBehaviour>();
    try
    {
        for (int i = 0; i < 2; i++)
        {
            var texture = new UnityEngine.Texture2D(2, 2, format, false, format == UnityEngine.TextureFormat.RGBAHalf)
                { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
            float red = format == UnityEngine.TextureFormat.RGBAHalf ? 2.5f : .75f;
            var color = new UnityEngine.Color(red, .25f, .5f, 1f);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply(false, false);
            textures.Add(texture);
            var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
            pixelsField.SetValue(layer, texture);
            layers.Add(layer);
        }
        document.layers.Add(layers[0]);
        document.layers.Add(new DCFApixels.WhimTex.GroupLayerBehaviour
        {
            layers = new System.Collections.Generic.List<DCFApixels.WhimTex.Layer> {
                new DCFApixels.WhimTex.GroupLayerBehaviour {
                    layers = new System.Collections.Generic.List<DCFApixels.WhimTex.Layer> { layers[1] }
                }
            }
        });
        for (int cycle = 0; cycle < 3; cycle++)
        {
            disable.Invoke(document, null);
            enable.Invoke(document, null);
            for (int i = 0; i < layers.Count; i++)
            {
                var texture = (UnityEngine.Texture2D)pixelsField.GetValue(layers[i]);
                Check(texture != null && texture == textures[i], "Disable/Enable preserves root and nested texture identity");
                Check(texture.format == format, "Drawing storage format stays unchanged");
                float expected = format == UnityEngine.TextureFormat.RGBAHalf ? 2.5f : .75f;
                Check(UnityEngine.Mathf.Abs(texture.GetPixel(0, 0).r - expected) < .01f, "Drawing pixels survive repeated disable/enable");
            }
        }
        UnityEngine.Object.DestroyImmediate(document);
        foreach (var texture in textures) Check(texture == null, "Final document destruction releases owned Drawing pixels");
    }
    finally
    {
        if (document != null) UnityEngine.Object.DestroyImmediate(document);
        foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
    }
}
return "Drawing disable/enable lifecycle checks passed: " + checks + "; actual domain reload and pending GPU strokes require manual verification.";
