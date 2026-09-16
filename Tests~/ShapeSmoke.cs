// Run with Unity Pipeline eval_file after compilation. Requires a graphics device.
// Only transient, unsaved objects; no existing document, scene, asset or Undo history is changed.
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = 128; document.height = 96;
var shape = new DCFApixels.WhimTex.ShapeLayerBehaviour();
var layer = new DCFApixels.WhimTex.Layer(shape);
document.layers.Add(layer);
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception(message); checks++; }
void Render(System.Action<UnityEngine.Texture2D> check)
{
    var pixels = document.Compose();
    try
    {
        foreach (var c in pixels.GetPixels())
            Check(!float.IsNaN(c.r + c.g + c.b + c.a) && !float.IsInfinity(c.r + c.g + c.b + c.a), "finite shape pixels");
        check(pixels);
    }
    finally { UnityEngine.Object.DestroyImmediate(pixels); }
}
try
{
    foreach (DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind kind in System.Enum.GetValues(typeof(DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind)))
    {
        shape.kind = kind;
        Render(p => {
            Check(p.GetPixel(64, 48).a > .99f, kind + " center filled");
            Check(p.GetPixel(0, 0).a == 0f, kind + " outside transparent");
        });
    }
    shape.kind = DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind.Rectangle;
    // Low-quality preview must keep the same document-space placement and stroke width.
    var instanceFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var preview = (UnityEngine.Texture2D)typeof(DCFApixels.WhimTex.TextureCompositor)
        .GetMethod("ComposePreview", instanceFlags).Invoke(document, new object[] { 64 });
    try
    {
        Check(preview.width == 64 && preview.height == 48, "preview dimensions");
        Check(preview.GetPixel(32, 24).a > .99f && preview.GetPixel(0, 0).a == 0f, "preview placement");
    }
    finally { UnityEngine.Object.DestroyImmediate(preview); }
    shape.fillColor = UnityEngine.Color.red;
    shape.stroke = true; shape.strokeColor = UnityEngine.Color.blue; shape.strokeWidth = 4f;
    Render(p => {
        Check(p.GetPixel(64, 48).r > .99f && p.GetPixel(64, 48).b < .01f, "red fill");
        Check(p.GetPixel(33, 48).b > .99f, "inside blue stroke");
    });
    shape.fill = false;
    Render(p => { Check(p.GetPixel(64, 48).a == 0f, "hollow center"); Check(p.GetPixel(33, 48).a > .99f, "hollow stroke"); });
    shape.fill = true; shape.stroke = false; shape.roundness = 1f;
    Render(p => Check(p.GetPixel(32, 24).a < .01f, "rounded rectangle corner"));
    shape.roundness = 0f;
    // Sample inside sharp corners, not the outer antialiased boundary pixels.
    var cornerPixels = new[] { new UnityEngine.Vector2Int(34, 69), new UnityEngine.Vector2Int(93, 69),
        new UnityEngine.Vector2Int(93, 26), new UnityEngine.Vector2Int(34, 26) };
    for (int corner = 0; corner < 4; corner++)
    {
        shape.cornerRoundness = UnityEngine.Vector4.zero;
        shape.cornerRoundness[corner] = 1f;
        Render(p => {
            for (int i = 0; i < 4; i++)
            {
                float a = p.GetPixel(cornerPixels[i].x, cornerPixels[i].y).a;
                Check(i == corner ? a < .01f : a > .99f, "independent corner " + corner + " / " + i);
            }
        });
    }
    shape.cornerRoundness = UnityEngine.Vector4.zero;
    layer.transform.position = new UnityEngine.Vector2(20, 10);
    layer.transform.scale = new UnityEngine.Vector2(.25f, .125f);
    layer.transform.rotation = 90f;
    Render(p => {
        Check(p.GetPixel(84, 58).r > .99f, "translated rotated center");
        Check(p.GetPixel(84, 70).a > .99f && p.GetPixel(96, 58).a == 0f, "rotation and non-square canvas");
        Check(p.GetPixel(64, 48).a == 0f, "no duplicate transform");
    });
    layer.enabled = false;
    Render(p => Check(p.GetPixel(84, 58).a == 0f, "visibility"));
    layer.enabled = true;
    layer.colorRange = DCFApixels.WhimTex.LayerColorRange.HDR;
    layer.blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR;
    shape.fillColor = new UnityEngine.Color(2, 0, 0, 1);
    Render(p => Check(p.GetPixel(84, 58).r > 1f, "HDR preserved"));
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    var adjust = typeof(DCFApixels.WhimTex.ShapeLayerBehaviour).GetMethod("AdjustCorner", flags);
    UnityEngine.Vector4 Adjust(UnityEngine.Vector4 v, int corner, float value, bool linked) =>
        (UnityEngine.Vector4)adjust.Invoke(null, new object[] { v, corner, value, linked });
    var ratios = new UnityEngine.Vector4(.1f, .2f, .3f, .4f);
    Check(UnityEngine.Vector4.Distance(Adjust(ratios, 0, .2f, true), ratios * 2f) < .0001f, "linked ratios");
    Check(UnityEngine.Vector4.Distance(Adjust(ratios, 0, 1f, true), ratios * 2.5f) < .0001f, "linked group limit");
    Check(UnityEngine.Vector4.Distance(Adjust(ratios, 0, .9f, false), new UnityEngine.Vector4(.9f,.2f,.3f,.4f)) < .0001f, "unlinked edit");
    Check(UnityEngine.Vector4.Distance(Adjust(UnityEngine.Vector4.zero, 0, .3f, true), UnityEngine.Vector4.one * .3f) < .0001f, "linked zero fallback");
    var drag = typeof(DCFApixels.WhimTex.TextureCompositorWindow).GetMethod("ShapeDragTransform", flags);
    var canvas = new UnityEngine.Vector2(512, 256);
    foreach (float dx in new[] { -70f, 0f, 70f }) foreach (float dy in new[] { -30f, 0f, 30f })
    {
        var start = new UnityEngine.Vector2(100, 120);
        var end = start + new UnityEngine.Vector2(dx, dy);
        var t = (DCFApixels.WhimTex.TextureTransform)drag.Invoke(null, new object[] { start, end, canvas, shape.kind, false, 8f });
        Check(UnityEngine.Vector2.Distance(t.position, (start + end - canvas) * .5f) < .0001f, "drag center");
        Check(System.Math.Abs(t.scale.x * canvas.x - UnityEngine.Mathf.Max(1f, UnityEngine.Mathf.Abs(dx))) < .0001f, "drag width");
        t = (DCFApixels.WhimTex.TextureTransform)drag.Invoke(null, new object[] { start, end, canvas, shape.kind, true, 8f });
        Check(System.Math.Abs(t.scale.x * canvas.x - t.scale.y * canvas.y) < .0001f, "Shift equal proportions");
        t = (DCFApixels.WhimTex.TextureTransform)drag.Invoke(null, new object[] { start, end, canvas, DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind.Line, true, 8f });
        Check(System.Math.Abs(t.rotation / 45d - System.Math.Round(t.rotation / 45d)) < .0001f, "Shift line angle");
    }
    var shader = UnityEngine.Shader.Find("Hidden/TextureCompositor/Shape");
    Check(shader != null && shader.isSupported, "shader supported");
    Check(!UnityEditor.ShaderUtil.ShaderHasError(shader), "shader compiled");
    Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("shapeDefaults"), "API describes shapes");
    // Exercise the actual partial-update setter without touching a saved asset or another window.
    var setter = typeof(DCFApixels.WhimTex.WhimTexApi).GetMethod("SetShape", flags);
    var jsonType = setter.GetParameters()[1].ParameterType;
    var json = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { "{\"kind\":\"Star\",\"sides\":7,\"innerRadius\":0.3}" });
    setter.Invoke(null, new object[] { shape, json });
    Check(shape.kind == DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind.Star && shape.sides == 7 && shape.innerRadius == .3f, "API partial update");
    Check(shape.fillColor.r == 2f, "API partial update retains unrelated fields");
    var copy = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    try
    {
        UnityEditor.EditorJsonUtility.FromJsonOverwrite(UnityEditor.EditorJsonUtility.ToJson(document), copy);
        var restored = copy.layers[0].Behaviour as DCFApixels.WhimTex.ShapeLayerBehaviour;
        Check(restored != null && restored.sides == 7 && restored.fillColor.r == 2f, "serialized behaviour survives round trip");
    }
    finally { UnityEngine.Object.DestroyImmediate(copy); }
    return "Shape smoke: " + checks + " checks passed; GPU render, transparency, stroke, transform, HDR and drag geometry.";
}
finally { UnityEngine.Object.DestroyImmediate(document); }
