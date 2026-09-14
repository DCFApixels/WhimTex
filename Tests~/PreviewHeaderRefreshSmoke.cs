// Opt-in eval body after manual compilation. No saved assets or visible windows.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var windowType = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var readout = windowType.GetMethod("RefreshPreviewZoomReadout", Hidden);
if (readout == null) throw new Exception("Manually compile the preview header change before running this test.");
var window = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
int checks = 0, fullRefreshes = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}
try
{
    var canvasType = windowType.GetNestedType("SpritePreviewElement", System.Reflection.BindingFlags.NonPublic);
    var viewport = windowType.GetField("previewViewport", Hidden).GetValue(window);
    var canvas = Activator.CreateInstance(canvasType, new[] { viewport });
    windowType.GetField("toolkitPreviewCanvas", Hidden).SetValue(window, canvas);
    windowType.GetField("toolkitPreviewHeader", Hidden).SetValue(window, new UnityEngine.UIElements.VisualElement());
    windowType.GetMethod("BuildPreviewZoomTool", Hidden).Invoke(window, null);
    windowType.GetMethod("AddPreviewZoomSettings", Hidden).Invoke(window, null);
    var bindings = windowType.GetField("toolkitHeaderBindings", Hidden).GetValue(window);
    bindings.GetType().GetMethod("Add").Invoke(bindings, new object[] { (Action)(() => fullRefreshes++) });
    var field = (UnityEngine.UIElements.FloatField)windowType.GetField("previewZoomPercent", Hidden).GetValue(window);
    var imageRect = canvasType.GetProperty("ImageRect");
    var changed = (Action)canvasType.GetField("ViewChanged", Hidden).GetValue(canvas);
    canvasType.GetField("documentWidth", Hidden).SetValue(canvas, 100);
    imageRect.GetSetMethod(true).Invoke(canvas, new object[] { new Rect(0, 0, 200, 200) });
    changed();
    Check(field.value == 200f && field.isDelayed, "Viewport notifications refresh the editable zoom percentage");
    Check(fullRefreshes == 0, "Zoom does not refresh unrelated header bindings");
    string previousText = field.text;
    imageRect.GetSetMethod(true).Invoke(canvas, new object[] { new Rect(40, 20, 200, 200) });
    changed();
    Check(ReferenceEquals(previousText, field.text), "Panning at the same scale leaves the input text untouched");
    Check(fullRefreshes == 0, "Panning does not refresh unrelated header bindings");
    canvasType.GetField("documentWidth", Hidden).SetValue(canvas, 200);
    bindings.GetType().GetMethod("Refresh").Invoke(bindings, new object[] { false });
    Check(field.value == 100f && fullRefreshes == 1,
        "Document updates refresh scale even when the image rectangle is unchanged");
    bindings.GetType().GetMethod("Clear").Invoke(bindings, null);
    windowType.GetMethod("AddPreviewZoomSettings", Hidden).Invoke(window, null);
    bindings.GetType().GetMethod("Refresh").Invoke(bindings, new object[] { false });
    var rebuiltField = (UnityEngine.UIElements.FloatField)windowType.GetField("previewZoomPercent", Hidden).GetValue(window);
    Check(!ReferenceEquals(field, rebuiltField) && rebuiltField.value == 100f,
        "Rebuilt headers initialize their readout at unchanged scale");
}
finally
{
    window.DiscardChanges();
    UnityEngine.Object.DestroyImmediate(window);
}
return $"Preview header refresh checks passed: {checks}.";
