// Opt-in after manual compilation. Temporary document/window, no saved assets.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var window = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
var windowType = window.GetType();
var document = (DCFApixels.WhimTex.TextureCompositor)windowType.GetField("compositor", Hidden).GetValue(window);
object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
try
{
    document.width = document.height = 64;
    var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour
    {
        brushSize = 16,
        brushHardness = 1,
        brushColor = UnityEngine.Color.red
    };
    document.layers.Add(drawing);
    Call(document, "NormalizeModel");
    Call(drawing, "InitializeCanvas", 64, 64);
    var point = new UnityEngine.Vector2(-.05f, .5f);
    object[] args = { new UnityEngine.Vector2(-5, 50), new UnityEngine.Rect(0, 0, 100, 100), drawing, null, true };
    Check((bool)Call(window, "TryMapPreviewToLayerUv", args), "Brush accepts a center outside canvas");
    Check(UnityEngine.Vector2.Distance((UnityEngine.Vector2)args[3], point) < .0001f, "Outside coordinates are not clamped to the edge");
    args[4] = false;
    Check(!(bool)Call(window, "TryMapPreviewToLayerUv", args), "Fill retains bounded sampling");
    var parameters = Call(drawing, "GetStrokeParameters", false);
    Call(drawing, "BeginStroke", point);
    Call(drawing, "PaintPoint", point, 64, 64, parameters);
    Call(drawing, "EndStroke");
    var image = document.Compose();
    try
    {
        Check(image.GetPixel(0, 32).a > .9f, "Overlapping brush tip paints at the edge");
        Check(image.GetPixel(20, 32).a < .01f, "Outside brush does not move to the canvas center");
    }
    finally { UnityEngine.Object.DestroyImmediate(image); }
    Call(drawing, "ClearSurface", 64, 64);
    point = new UnityEngine.Vector2(-.5f, .5f);
    Call(drawing, "BeginStroke", point);
    Call(drawing, "PaintPoint", point, 64, 64, parameters);
    Call(drawing, "EndStroke");
    image = document.Compose();
    try
    {
        bool clear = true;
        foreach (var pixel in image.GetPixels32()) clear &= pixel.a == 0;
        Check(clear, "A fully outside stamp leaves the canvas untouched");
    }
    finally { UnityEngine.Object.DestroyImmediate(image); }
    return new { success = true, checks };
}
finally
{
    UnityEditor.Undo.ClearUndo(document);
    window.DiscardChanges();
    UnityEngine.Object.DestroyImmediate(window);
}
