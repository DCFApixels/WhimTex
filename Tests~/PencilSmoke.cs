// Opt-in eval after manual compilation. Uses temporary textures only; no asset writes or Undo operations.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var drawingType = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour);
var settingsType = drawingType.Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
var shapeType = drawingType.Assembly.GetType("DCFApixels.WhimTex.PencilShape", true);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new System.Exception(message);
    checks++;
}
object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
object Parameters(int size, string shape, bool erase = false, UnityEngine.Color? color = null)
{
    var settings = System.Activator.CreateInstance(settingsType, true);
    settingsType.GetField("pencilSize").SetValue(settings, size);
    settingsType.GetField("pencilShape").SetValue(settings, System.Enum.Parse(shapeType, shape));
    settingsType.GetField("brushHardness").SetValue(settings, 0f);
    settingsType.GetField("brushSpacing").SetValue(settings, 4f);
    return Call(settings, "GetPencilParameters", erase, color ?? UnityEngine.Color.red);
}
UnityEngine.Texture2D Pixels(DCFApixels.WhimTex.DrawingLayerBehaviour layer) =>
    (UnityEngine.Texture2D)drawingType.GetProperty("StoredTexture", Hidden).GetValue(layer);
void Point(DCFApixels.WhimTex.DrawingLayerBehaviour layer, UnityEngine.Vector2 uv, object parameters)
{
    Call(layer, "BeginStroke", uv);
    try { Call(layer, "PaintPoint", uv, 16, 16, parameters); }
    finally { Call(layer, "EndStroke"); }
    Call(layer, "SyncSurfaceToTexture");
}
bool Covered(float x, float y, float radius, string shape) => shape == "Square"
    ? System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)) <= radius + 0.0001f
    : shape == "Diamond" ? System.Math.Abs(x) + System.Math.Abs(y) <= radius + 0.0001f
    : x * x + y * y <= radius * radius + 0.0001f;
var previous = UnityEngine.RenderTexture.active;
foreach (string shape in new[] { "Circle", "Square", "Diamond" })
foreach (int size in new[] { 1, 2, 3, 4, 7, 8 })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    try
    {
        var uv = new UnityEngine.Vector2(8.5f / 16f, 8.5f / 16f);
        Point(layer, uv, Parameters(size, shape));
        float center = size % 2 == 0 ? 9f : 8.5f;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float expected = Covered(x + 0.5f - center, y + 0.5f - center, size * 0.5f, shape) ? 1f : 0f;
            Check(System.Math.Abs(Pixels(layer).GetPixel(x, y).a - expected) < 0.005f,
                $"{shape} size {size}: exact binary footprint at {x},{y}");
        }
        Point(layer, uv, Parameters(size, shape, true));
        foreach (var pixel in Pixels(layer).GetPixels()) Check(pixel.a < 0.005f, "Eraser uses identical footprint");
    }
    finally { Call(layer, "ReleaseTransientResources"); }
}
foreach (var end in new[] { new UnityEngine.Vector2(14, 11), new UnityEngine.Vector2(11, 14),
    new UnityEngine.Vector2(2, 5), new UnityEngine.Vector2(5, 2), new UnityEngine.Vector2(14, 5),
    new UnityEngine.Vector2(5, 14), new UnityEngine.Vector2(2, 11), new UnityEngine.Vector2(11, 2) })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    try
    {
        var startUv = new UnityEngine.Vector2(8.5f / 16, 8.5f / 16);
        var endUv = (end + UnityEngine.Vector2.one * 0.5f) / 16;
        var parameters = Parameters(1, "Circle");
        Call(layer, "BeginStroke", startUv);
        Call(layer, "PaintPoint", startUv, 16, 16, parameters);
        Call(layer, "PaintSegment", startUv, endUv, 16, 16, false, parameters);
        Call(layer, "EndStroke"); Call(layer, "SyncSurfaceToTexture");
        int count = 0;
        foreach (var pixel in Pixels(layer).GetPixels())
        {
            Check(pixel.a < 0.005f || pixel.a > 0.995f, "Line remains unsmoothed");
            if (pixel.a > 0.5f) count++;
        }
        Check(count == (int)System.Math.Max(System.Math.Abs(end.x - 8), System.Math.Abs(end.y - 8)) + 1,
            "One-pixel line covers every grid step in each octant");
        Check(Pixels(layer).GetPixel((int)end.x, (int)end.y).a > 0.99f, "Line reaches endpoint");
    }
    finally { Call(layer, "ReleaseTransientResources"); }
}
foreach (string shape in new[] { "Circle", "Square", "Diamond" })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour { colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
    try
    {
        var parameters = Call(Parameters(5, shape, false, new UnityEngine.Color(2, 0, 0, 1)), "WithCanvasWrap");
        Point(layer, new UnityEngine.Vector2(0.5f / 16, 0.5f / 16), parameters);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int dx = System.Math.Min(x, 16 - x), dy = System.Math.Min(y, 16 - y);
            bool expected = Covered(dx, dy, 2.5f, shape);
            var pixel = Pixels(layer).GetPixel(x, y);
            Check(System.Math.Abs(pixel.a - (expected ? 1 : 0)) < 0.005f, "Pencil wraps at all canvas edges");
            if (expected) Check(pixel.r > 4f, "Pencil retains HDR color");
        }
    }
    finally { Call(layer, "ReleaseTransientResources"); }
}
foreach (int size in new[] { 1, 2, 4 })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
    { repeatMode = DCFApixels.WhimTex.PaintRepeatMode.Mirror, mirrorAcrossVerticalAxis = true };
    try
    {
        Point(layer, new UnityEngine.Vector2(3.5f / 16, 8.5f / 16), Parameters(size, "Square"));
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 8; x++)
            Check(System.Math.Abs(Pixels(layer).GetPixel(x, y).a - Pixels(layer).GetPixel(15 - x, y).a) < 0.005f,
                "Mirror preserves odd and even pencil footprints");
    }
    finally { Call(layer, "ReleaseTransientResources"); }
}
Check(UnityEngine.RenderTexture.active == previous, "Pencil restores active render target");
return $"Pencil checks passed: {checks}. No persistent assets or Undo changes.";
