// Unity Pipeline eval_file; read-only checks, no user preferences or windows are changed.
var assembly = typeof(DCFApixels.SpriteEditor.TextureCompositor).Assembly;
var settings = assembly.GetType("DCFApixels.SpriteEditor.SpriteEditorUserSettings", true);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var normalize = settings.GetMethod("NormalizeSnapRadius", flags);
int checks = 0;
void Check(bool ok) { if (!ok) throw new System.Exception("Guide setting check failed: " + checks); checks++; }
float Normalize(float value) => (float)normalize.Invoke(null, new object[] { value });
Check(Normalize(float.NaN) == 8f);
Check(Normalize(float.PositiveInfinity) == 8f);
Check(Normalize(float.NegativeInfinity) == 8f);
Check(Normalize(-2f) == 1f);
Check(Normalize(0f) == 1f);
Check(Normalize(100f) == 64f);
Check(Normalize(12.5f) == 12.5f);
float radius = (float)settings.GetProperty("SnapRadius", flags).GetValue(null);
Check(radius >= 1f && radius <= 64f);
var window = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
Check((float)window.GetProperty("GuideSnapPixels", flags).GetValue(null) == radius);
foreach (var nested in window.GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
    foreach (var name in new[] { "PivotSnapDistance", "CanvasSnapDistance" })
    {
        var property = nested.GetProperty(name, flags);
        if (property != null) Check((float)property.GetValue(null) == radius);
    }
Check(checks == 11);
return "Guide settings: " + checks + " read-only Unity checks passed.";
