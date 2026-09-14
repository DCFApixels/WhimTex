// Opt-in live-Editor eval AFTER manual compilation and shader import.
// Only transient objects and in-memory image/JSON round-trips; no saved assets or document Undo.
var ns = "DCFApixels.WhimTex.";
var assembly = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly;
var instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var utility = assembly.GetType(ns + "HdrUtility");
if (typeof(DCFApixels.WhimTex.ColorFillLayerBehaviour).GetProperty("color") == null)
    throw new Exception("Manually compile the color compatibility fix before running this test.");
var brushShader = Shader.Find("Hidden/TextureCompositor/PaintBrush");
if (brushShader.GetPropertyType(brushShader.FindPropertyIndex("_Color")) != UnityEngine.Rendering.ShaderPropertyType.Vector)
    throw new Exception("Manually import the fixed brush shader before running this test.");
var doc = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.hideFlags = HideFlags.HideAndDontSave;
doc.width = doc.height = 8;
var layers = new List<DCFApixels.WhimTex.Layer>();
var objects = new List<UnityEngine.Object>();
var failures = new List<string>();
int checks = 0;
var previous = RenderTexture.active;
bool previousSrgbWrite = GL.sRGBWrite;
object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, instance).Invoke(target, args);
object Static(string name, params object[] args) => utility.GetMethod(name, statics).Invoke(null, args);
Color Decode(Color color) => (Color)Static("Decode", color);
Color Encode(Color color) => (Color)Static("Encode", color);
void Near(Color actual, Color expected, string label, float tolerance = .007f)
{
    checks++;
    if (float.IsNaN(actual.r + actual.g + actual.b + actual.a) ||
        Mathf.Abs(actual.r - expected.r) > tolerance || Mathf.Abs(actual.g - expected.g) > tolerance ||
        Mathf.Abs(actual.b - expected.b) > tolerance || Mathf.Abs(actual.a - expected.a) > tolerance)
        failures.Add(label + ": expected " + expected + ", got " + actual);
}
void Use(params DCFApixels.WhimTex.Layer[] values)
{
    doc.layers.Clear();
    foreach (var value in values) { doc.layers.Add(value); if (!layers.Contains(value)) layers.Add(value); }
}
Color Pixel()
{
    var rendered = doc.Compose();
    try { return rendered.GetPixel(4, 4); }
    finally { UnityEngine.Object.DestroyImmediate(rendered); }
}
Texture2D Texture(Color value, bool linear)
{
    var texture = new Texture2D(8, 8, linear ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, linear)
    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
    objects.Add(texture);
    var pixels = new Color[64]; for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
    texture.SetPixels(pixels); texture.Apply(false, false);
    return texture;
}
Gradient Constant(Color value)
{
    var gradient = new Gradient();
    gradient.SetKeys(new[] { new GradientColorKey(value, 0), new GradientColorKey(value, 1) },
        new[] { new GradientAlphaKey(value.a, 0), new GradientAlphaKey(value.a, 1) });
    return gradient;
}
try
{
    foreach (var color in new[] { new Color(.463f, 0, 1, 1), new Color(1, .2f, .5f, 1), new Color(.37f, .61f, .23f, .6f) })
    {
        Color linear = Decode(color);
        var fill = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = color };
        Use(fill); Near(Pixel(), linear, "Color Fill");
        var copy = JsonUtility.FromJson<DCFApixels.WhimTex.ColorFillLayerBehaviour>(JsonUtility.ToJson(fill));
        Use(copy); Near(Pixel(), linear, "New fill JSON round-trip");
        Use(new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = Texture(color, false) });
        Near(Pixel(), linear, "sRGB File");
        Use(new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = Texture(linear, true) });
        Near(Pixel(), linear, "Linear File");
        Use(new DCFApixels.WhimTex.GradientLayerBehaviour { gradient = Constant(color) });
        Near(Pixel(), linear, "Gradient");
        foreach (var range in new[] { DCFApixels.WhimTex.LayerColorRange.Standard, DCFApixels.WhimTex.LayerColorRange.HDR })
        {
            var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour
            { brushColor = color, brushSize = 4, brushHardness = 1, colorRange = range };
            Use(drawing);
            Call(drawing, "PaintPoint", new Vector2(.5625f, .5625f), 8, 8, Call(drawing, "GetStrokeParameters", false));
            Near(Pixel(), linear, "Brush " + range);
            Call(drawing, "SyncSurfaceToTexture");
            Call(drawing, "InvalidatePaintSurface");
            Near(Pixel(), linear, "Drawing storage round-trip " + range);
        }
        Use(fill);
        var composed = doc.Compose(); objects.Add(composed);
        var ldr = (Texture2D)Static("ToLdr", composed, false); objects.Add(ldr);
        Near(ldr.GetPixel(4, 4), color, "LDR export encoding");
        var png = new Texture2D(2, 2); objects.Add(png);
        png.LoadImage(ldr.EncodeToPNG()); Near(png.GetPixel(4, 4), color, "PNG memory round-trip");
        var previewMaterial = new Material(Shader.Find("Hidden/TextureCompositor/PreviewChannels")); objects.Add(previewMaterial);
        previewMaterial.SetVector("_Channels", Vector4.one); previewMaterial.SetFloat("_Exposure", 1);
        var preview = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previewRead = new Texture2D(8, 8, TextureFormat.RGBA32, false); objects.Add(previewRead);
        try
        {
            Graphics.Blit(composed, preview, previewMaterial);
            RenderTexture.active = preview;
            previewRead.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
            Near(previewRead.GetPixel(4, 4), color, "Preview display encoding");
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(preview); }

        var shapeTexture = Texture(Color.white, false);
        for (int edge = 0; edge < 8; edge++)
        {
            shapeTexture.SetPixel(edge, 0, Color.clear); shapeTexture.SetPixel(edge, 7, Color.clear);
            shapeTexture.SetPixel(0, edge, Color.clear); shapeTexture.SetPixel(7, edge, Color.clear);
        }
        shapeTexture.Apply(false, false);
        var shape = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = shapeTexture };
        var sdf = new DCFApixels.WhimTex.SDFLayerBehaviour { gradient = Constant(color) };
        Use(sdf, shape);
        Color opaque = color; opaque.a = 1;
        sdf.gradient = Constant(opaque);
        Near(Pixel(), Decode(opaque), "SDF gradient");
        var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour
        { outlineColor = opaque, outlineWidth = 100, outlineSoftness = 0,
            outlinePosition = DCFApixels.WhimTex.OutlineLayerBehaviour.OutlinePosition.Inside };
        Use(outline, shape); Near(Pixel(), Decode(opaque), "Outline color");
    }

    var hdrColor = new Color(2, -.25f, .4f, 1);
    var hdrDrawing = new DCFApixels.WhimTex.DrawingLayerBehaviour
    { colorRange = DCFApixels.WhimTex.LayerColorRange.HDR, brushColor = hdrColor, brushSize = 4, brushHardness = 1 };
    Use(hdrDrawing);
    Call(hdrDrawing, "PaintPoint", new Vector2(.5625f, .5625f), 8, 8, Call(hdrDrawing, "GetStrokeParameters", false));
    Near(Pixel(), Decode(hdrColor), "Signed HDR brush", .015f);

    // Huge picker intensities must stay colored without changing Drawing/Compose storage formats.
    foreach (Color input in new[] { new Color(128, 64, -32, 1), new Color(float.MaxValue, float.MaxValue / 2, 0, 1) })
    {
        Color bounded = (Color)Static("DecodePaintColor", input);
        double Linear(float v) => Math.Pow((Math.Abs((double)v) + .055) / 1.055, 2.4) * Math.Sign(v);
        double peak = Linear(input.r);
        var expected = new Color(65504, (float)(65504 * Linear(input.g) / peak),
            input.b == 0 ? 0 : (float)(65504 * Linear(input.b) / peak), 1);
        Near(bounded, expected, "Paint intensity preserves RGB ratios", .02f);
        var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour
        { colorRange = DCFApixels.WhimTex.LayerColorRange.HDR, brushColor = input, brushSize = 4, brushHardness = 1 };
        Use(drawing);
        Call(drawing, "PaintPoint", new Vector2(.5625f, .5625f), 8, 8, Call(drawing, "GetStrokeParameters", false));
        Near(Pixel(), bounded, "Bright HDR brush is not black", 64f);
        Call(drawing, "SyncSurfaceToTexture"); Call(drawing, "InvalidatePaintSurface");
        Near(Pixel(), bounded, "Bright brush half storage round-trip", 64f);
        var composed = doc.Compose(); objects.Add(composed);
        checks++;
        if (composed.format != TextureFormat.RGBAHalf) failures.Add("Compose must retain RGBAHalf output");
    }
    Near((Color)Static("DecodePaintColor", hdrColor), Decode(hdrColor), "In-range paint remains unchanged");

    var source = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new Color(.3f,.6f,.2f,1) };
    var top = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new Color(.8f,.2f,.4f,1), opacity = .5f };
    Use(top, source); Near(Pixel(), Decode(Color.Lerp(source.color, top.color, .5f)), "Standard opacity blends in sRGB");

    // Test both uniform layouts without compiling a Shader FX: cached FX used Color, new FX use Vector.
    foreach (string shaderName in new[] { "Unlit/Color", "Hidden/TextureCompositor/PaintBrush" })
    {
        var shader = Shader.Find(shaderName);
        if (shader == null || !shader.isSupported) throw new Exception("Missing test shader: " + shaderName);
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; objects.Add(material);
        var parameter = new DCFApixels.WhimTex.ShaderFXParameter
        { name = "_Color", type = DCFApixels.WhimTex.ShaderFXParameterType.Color, colorValue = new Color(.463f, .23f, .71f, 1) };
        Call(parameter, "SetValue", material, parameter, new Vector2(8, 8));
        if (shader == brushShader)
        { material.SetFloat("_Hardness", 1); material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 0); }
        var target = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true); objects.Add(read);
        try
        {
            RenderTexture.active = target; GL.Clear(false, true, Color.clear); GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                if (!material.SetPass(0)) throw new Exception("Cannot draw " + shaderName);
                GL.Begin(GL.QUADS);
                try
                {
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                }
                finally { GL.End(); }
            }
            finally { GL.PopMatrix(); }
            read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
            Near(read.GetPixel(4, 4), Decode(parameter.colorValue), "Shader FX color uniform " + shaderName);
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }

    // CPU writes must respect the destination's encoding, independently of the project color space.
    using (var values = new Unity.Collections.NativeArray<Color>(1, Unity.Collections.Allocator.Temp))
    {
        var writable = values; writable[0] = new Color(.2f, .4f, .6f, 1);
        foreach (bool linear in new[] { true, false })
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear); objects.Add(texture);
            Static("WritePixels", texture, values);
            Near(texture.GetPixel(0, 0), linear ? values[0] : Encode(values[0]), "CPU write encoding " + linear);
        }
    }
    using (var fillSource = new Unity.Collections.NativeArray<Color>(4, Unity.Collections.Allocator.TempJob))
    using (var fillReference = new Unity.Collections.NativeArray<Color>(4, Unity.Collections.Allocator.TempJob))
    using (var fillOutput = new Unity.Collections.NativeArray<Color>(4, Unity.Collections.Allocator.TempJob))
    using (var valid = new Unity.Collections.NativeArray<byte>(4, Unity.Collections.Allocator.TempJob))
    {
        var writable = valid; for (int i = 0; i < 4; i++) writable[i] = 1;
        Color value = Decode(new Color(.37f, .61f, .23f, .6f));
        assembly.GetType(ns + "HdrFloodFillUtility").GetMethod("Fill", statics).Invoke(null,
            new object[] { fillSource, fillReference, valid, fillOutput, 2, 2, 0, value, 0, 0, false, true, true });
        Near(fillOutput[0], value, "Flood fill linear color and alpha");
        value = (Color)Static("DecodePaintColor", new Color(128, 64, 32, 1));
        assembly.GetType(ns + "HdrFloodFillUtility").GetMethod("Fill", statics).Invoke(null,
            new object[] { fillSource, fillReference, valid, fillOutput, 2, 2, 0, value, 0, 0, false, true, false });
        Near(fillOutput[0], value, "Bright fill retains bounded HDR color", .02f);
    }
    if (failures.Count != 0) throw new Exception(string.Join("\n", failures));
    return $"Color pipeline: {checks} checks passed ({QualitySettings.activeColorSpace}).";
}
finally
{
    RenderTexture.active = previous; GL.sRGBWrite = previousSrgbWrite;
    doc.layers.Clear();
    foreach (var layer in layers) Call(layer, "ReleaseTransientResources");
    UnityEngine.Object.DestroyImmediate(doc);
    foreach (var value in objects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
}
