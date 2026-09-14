// Opt-in after manual compilation. Pure CPU helpers only; no screen capture, windows, preferences or asset writes.
var windowType = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var sample = windowType.GetMethod("PreviewScreenSample", Hidden);
var place = windowType.GetMethod("PreviewEyedropperLensRect", Hidden);
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new System.Exception(message);
    checks++;
}
foreach (int side in new[] { 1, 3, 11 })
{
    var pixels = new UnityEngine.Color[side * side];
    var expected = new UnityEngine.Color(.15f, .6f, .9f, 1f);
    pixels[(side / 2) * side + side / 2] = expected;
    foreach (float alpha in new[] { 0f, .25f, 1f })
    {
        var result = (UnityEngine.Color)sample.Invoke(null, new object[] { pixels, side, alpha });
        Check(result.r == expected.r && result.g == expected.g && result.b == expected.b, "Screen RGB is not re-encoded");
        Check(result.a == alpha, "Brush alpha is preserved");
    }
}
foreach (var bounds in new[] {
    new UnityEngine.Rect(0, 0, 200, 96), new UnityEngine.Rect(0, 0, 1200, 900),
    new UnityEngine.Rect(-1920, 0, 1920, 1080), new UnityEngine.Rect(1920, -1440, 2560, 1440)
})
for (int y = 0; y < 5; y++)
for (int x = 0; x < 5; x++)
{
    var point = new UnityEngine.Vector2(bounds.x + bounds.width * x / 5f, bounds.y + bounds.height * y / 5f);
    var rect = (UnityEngine.Rect)place.Invoke(null, new object[] { point, bounds });
    Check(!rect.Overlaps(new UnityEngine.Rect(point.x - 7f, point.y - 7f, 14f, 14f)), "Magnifier stays clear of the source pixels");
}
foreach (var bounds in new[] { new UnityEngine.Rect(0, 0, 1920, 1080), new UnityEngine.Rect(-1920, -400, 1920, 1080) })
{
    for (int dx = -3; dx <= 3; dx++)
    {
        var point = bounds.center + new UnityEngine.Vector2(dx, 0);
        var rect = (UnityEngine.Rect)place.Invoke(null, new object[] { point, bounds });
        Check(rect.position - point == new UnityEngine.Vector2(24, 24), "Offset stays fixed across the monitor center");
    }
}
const System.Reflection.BindingFlags InstanceHidden = System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GUIView");
Check(typeof(UnityEditor.EditorWindow).GetField("m_Parent", InstanceHidden) != null, "Editor view accessor is available");
Check(viewType?.GetMethod("StealMouseCapture", InstanceHidden) != null, "Desktop mouse capture is available");
Check(viewType?.GetMethod("SetEyeDropperOpen", InstanceHidden) != null, "Desktop eyedropper flag is available");
Check(viewType?.GetProperty("window", InstanceHidden) != null, "Capture view container is available");
var containerType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
Check(containerType?.GetMethod("SetInvisible", InstanceHidden) != null, "Native capture window can be hidden independently of the lens");
Check(typeof(UnityEditor.EditorGUIUtility).GetMethod("SetCurrentViewCursor", Hidden) != null, "Native cursor setter is available");
var mousePosition = typeof(UnityEditor.Editor).GetMethod("GetCurrentMousePosition", Hidden);
Check(mousePosition != null && mousePosition.ReturnType == typeof(UnityEngine.Vector2), "Independent desktop pointer reader is available");
return "Screen eyedropper CPU/API availability checks passed: " + checks + "; desktop input capture not exercised.";
