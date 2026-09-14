// Opt-in eval body after manual compilation. Uses temporary GPU resources and Editor Undo.
// No saved assets or preference changes. Run only when no edits are in progress.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
const System.Reflection.BindingFlags StaticHidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var drawingType = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour);
var settingsType = drawingType.Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
var apiType = typeof(DCFApixels.WhimTex.WhimTexApi);
var setBrush = apiType.GetMethod("SetBrush", StaticHidden);
var jsonType = setBrush.GetParameters()[1].ParameterType;
object Json(string value) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
int checks = 0;
object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
T Read<T>(object parameters, string field) => (T)parameters.GetType().GetField(field, Hidden).GetValue(parameters);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
object Preferences(float size, float hardness, float spacing, Color color)
{
    var settings = Activator.CreateInstance(settingsType, true);
    settingsType.GetField("brushSize").SetValue(settings, size);
    settingsType.GetField("brushHardness").SetValue(settings, hardness);
    settingsType.GetField("brushSpacing").SetValue(settings, spacing);
    settingsType.GetField("brushColor").SetValue(settings, color);
    return settings;
}
var legacy = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushSize = 15, brushHardness = 0.4f, brushSpacing = 0.3f, brushColor = Color.red };
var preferences = Preferences(15, 0.4f, 0.3f, Color.red);
var interactive = Call(preferences, "GetStrokeParameters", false, null);
var saved = Call(legacy, "GetStrokeParameters", false);
Check(Read<float>(interactive, "Size") == Read<float>(saved, "Size") &&
    Read<float>(interactive, "Hardness") == Read<float>(saved, "Hardness") &&
    Read<float>(interactive, "SpacingPixels") == Read<float>(saved, "SpacingPixels") &&
    Read<Color>(interactive, "Color") == Read<Color>(saved, "Color"), "UI and legacy settings resolve identically");
settingsType.GetField("brushSize").SetValue(preferences, 99f);
legacy.brushSize = 77;
Check(Read<float>(interactive, "Size") == 15 && Read<float>(saved, "Size") == 15, "Parameters are independent value snapshots");
var masked = Call(preferences, "GetStrokeParameters", true, (Color?)Color.clear);
Check(Read<Color>(masked, "Color") == Color.clear && Read<bool>(masked, "Erase"), "Explicit color masks and erase survive parameter creation");
Check((Color)settingsType.GetField("brushColor").GetValue(preferences) == Color.red, "Color overrides do not modify preferences");
var clamped = Call(Preferences(0, 2, 0, Color.white), "GetStrokeParameters", false, null);
Check(Read<float>(clamped, "Size") == 1 && Read<float>(clamped, "Hardness") == 1 && Read<float>(clamped, "SpacingPixels") == 1,
    "Shared limits preserve the minimum one-pixel stamp spacing");
legacy.brushSpacing = 0;
Check(Mathf.Abs(Read<float>(Call(legacy, "GetStrokeParameters", false), "SpacingPixels") - 77 * 0.16f) < 0.0001f,
    "Legacy zero spacing retains its default migration");
setBrush.Invoke(null, new object[] { legacy, Json("{\"size\":12}") });
Check(legacy.brushSize == 12 && legacy.brushColor == Color.red && legacy.brushHardness == 0.4f,
    "Partial API brush commands preserve omitted saved values");

void EqualPixels(Texture2D left, Texture2D right)
{
    Check(left != null && right != null && left.width == right.width && left.height == right.height, "Both paths produce matching dimensions");
    var a = left.GetRawTextureData<Color32>();
    var b = right.GetRawTextureData<Color32>();
    for (int i = 0; i < a.Length; i++)
        if (Math.Abs(a[i].r - b[i].r) > 1 || Math.Abs(a[i].g - b[i].g) > 1 || Math.Abs(a[i].b - b[i].b) > 1 || Math.Abs(a[i].a - b[i].a) > 1)
            throw new Exception($"UI/API pixels differ at {i}: {a[i]} / {b[i]}");
    checks++;
}
Undo.FlushUndoRecordObjects();
Undo.IncrementCurrentGroup();
RenderTexture previousActive = RenderTexture.active;
for (int mode = 0; mode < 4; mode++)
{
    var document = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    var uiLayer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    var apiLayer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    Texture2D Pixels(object layer) => (Texture2D)drawingType.GetField("pixels", Hidden).GetValue(layer);
    try
    {
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 32;
        bool erase = mode == 2;
        float hardness = mode == 1 ? 0 : 1;
        float spacing = mode == 1 ? 0.7f : 0.16f;
        Color color = mode == 3 ? Color.clear : new Color(0.8f, 0.2f, 0.4f, 0.7f);
        var tool = Preferences(9, hardness, spacing, color);
        apiLayer.brushColor = color;
        apiLayer.brushSize = 9;
        apiLayer.brushHardness = hardness;
        apiLayer.brushSpacing = spacing;
        foreach (var layer in new[] { uiLayer, apiLayer })
        {
            Call(layer, "InitializeCanvas", 32, 32);
            if (erase)
            {
                var pixels = Pixels(layer);
                var data = pixels.GetRawTextureData<Color32>();
                for (int i = 0; i < data.Length; i++) data[i] = new Color32(255, 255, 255, 255);
                pixels.Apply();
                Call(layer, "InvalidatePaintSurface");
            }
        }
        object parameters = Call(tool, "GetStrokeParameters", erase, null);
        Vector2 start = new Vector2(0.2f, 0.25f), end = new Vector2(0.75f, 0.7f);
        Call(uiLayer, "BeginStroke", start);
        Call(uiLayer, "PaintPoint", start, 32, 32, parameters);
        Call(uiLayer, "PaintSegment", start, end, 32, 32, false, parameters);
        Call(uiLayer, "SyncSurfaceToTexture");
        Call(uiLayer, "EndStroke");
        var operation = Json("{\"space\":\"layerUv\",\"points\":[[0.2,0.25],[0.75,0.7]],\"erase\":" + (erase ? "true" : "false") + "}");
        apiType.GetMethod("Paint", StaticHidden).Invoke(null, new object[] { document, apiLayer, operation, true });
        EqualPixels(Pixels(uiLayer), Pixels(apiLayer));
        Check(uiLayer.brushSize == 32 && uiLayer.brushColor == Color.white, "Interactive painting ignores and preserves legacy brush values");
        Check(RenderTexture.active == previousActive, $"Painting restores the active render target (mode {mode})");
    }
    finally
    {
        foreach (var layer in new[] { uiLayer, apiLayer })
        {
            var pixels = Pixels(layer);
            if (pixels != null) Undo.ClearUndo(pixels);
            Call(layer, "ReleaseTransientResources");
        }
        Undo.ClearUndo(document);
        UnityEngine.Object.DestroyImmediate(document);
        RenderTexture.active = previousActive;
    }
}
Undo.IncrementCurrentGroup();
return $"Paint stroke parameter checks passed: {checks}.";
