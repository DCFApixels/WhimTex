var type = typeof(DCFApixels.WhimTex.TextureCompositorWindow).Assembly.GetType("DCFApixels.WhimTex.PreviewViewport", true);
var viewport = System.Activator.CreateInstance(type, true);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var bounds = new UnityEngine.Rect(0, 0, 808, 408);
var dimensions = new UnityEngine.Vector2(200, 100);
int checks = 0;
void Call(string method, params object[] args) => type.GetMethod(method, flags).Invoke(viewport, args);
UnityEngine.Rect Image(UnityEngine.Rect area) =>
    (UnityEngine.Rect)type.GetMethod("ImageRect", flags).Invoke(viewport, new object[] { area, dimensions });
UnityEngine.Vector2 UV(UnityEngine.Rect image, UnityEngine.Vector2 point) =>
    new UnityEngine.Vector2((point.x - image.x) / image.width, (point.y - image.y) / image.height);
void Check(bool value, string message)
{
    if (!value) throw new System.Exception(message);
    checks++;
}
bool Close(UnityEngine.Vector2 a, UnityEngine.Vector2 b) => (a - b).sqrMagnitude < 0.00001f;

var fitted = Image(bounds);
Check(fitted == new UnityEngine.Rect(68, 36, 672, 336), "Initial view reserves fixed tool-independent padding");
Check(fitted.yMin - 24f - 9f >= bounds.yMin, "Default rotation handle and its hit area fit inside the viewport");
float fittedScale = fitted.width / dimensions.x;
var anchor = new UnityEngine.Vector2(204, 104);
var uv = UV(fitted, anchor);
Call("ZoomAt", bounds, dimensions, fitted, anchor, fittedScale * 2f);
var zoomed = Image(bounds);
Check(UnityEngine.Mathf.Approximately(zoomed.width, fitted.width * 2f), "Click doubles image scale without imposing Fit padding");
Check(Close(UV(zoomed, anchor), uv), "Pixel under click stays under cursor");
Call("ZoomAt", bounds, dimensions, zoomed, anchor, fittedScale);
Check(Close(Image(bounds).position, fitted.position), "Zoom-out reverses anchored zoom-in");
float Wheel(float scale, float delta) => (float)type.GetMethod("WheelScale",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { scale, delta });
Check(UnityEngine.Mathf.Approximately(Wheel(1f, -3f), 1.2f), "Wheel up zooms in by twenty percent per notch");
Check(UnityEngine.Mathf.Approximately(Wheel(Wheel(1f, -3f), 3f), 1f), "Wheel directions reverse each other");
var emptyAnchor = new UnityEngine.Vector2(12, 12);
var emptyUv = UV(Image(bounds), emptyAnchor);
Call("ZoomAt", bounds, dimensions, Image(bounds), emptyAnchor, Wheel(fittedScale, -3f));
Check(Close(UV(Image(bounds), emptyAnchor), emptyUv), "Wheel zoom anchors correctly outside the physical canvas");
var wheelImage = Image(bounds);
Call("Pan", bounds, dimensions, wheelImage, new UnityEngine.Vector2(15, 21));
Check(Close(Image(bounds).position, wheelImage.position + new UnityEngine.Vector2(15, 21)), "Pan after wheel zoom follows pointer displacement");
Check(Wheel(64f, -3f) == 64f && Wheel(1f / 1024f, 3f) == 1f / 1024f, "Wheel respects scale limits");
Check(Wheel(1f, float.NaN) == 1f && Wheel(1f, float.PositiveInfinity) == 1f, "Invalid wheel deltas do not change zoom");
Call("Reset");
var selection = new UnityEngine.Rect(204, 104, 200, 100);
Call("Frame", bounds, dimensions, Image(bounds), selection);
var framed = Image(bounds);
Check(Close(UV(framed, bounds.center), UV(fitted, selection.center)), "Area is centered in the viewport");
Check(UnityEngine.Mathf.Approximately(framed.width / fitted.width, 4f), "Area uses the limiting axis to fit");
Call("Pan", bounds, dimensions, framed, new UnityEngine.Vector2(37, -19));
var panned = Image(bounds);
Check(Close(panned.position, framed.position + new UnityEngine.Vector2(37, -19)), "Panning follows pointer displacement");
var resizedBounds = new UnityEngine.Rect(0, 0, 1200, 700);
var resized = Image(resizedBounds);
Check(Close(resized.size, panned.size), "Window resize preserves manual pixel scale");
Check(Close(UV(resized, resizedBounds.center), UV(panned, bounds.center)), "Window resize preserves viewed image center");
Call("ZoomAt", bounds, dimensions, panned, bounds.center, 1f);
Check(Close(Image(bounds).size, dimensions), "100 percent uses one UI unit per source pixel");
Call("ZoomAt", bounds, dimensions, Image(bounds), bounds.center, 100000f);
Check(UnityEngine.Mathf.Approximately(Image(bounds).width, dimensions.x * 64f), "Maximum scale is bounded");
Call("ZoomAt", bounds, dimensions, Image(bounds), bounds.center, -1f);
Check(Image(bounds).width > 0f, "Minimum scale stays positive");
Call("Reset");
Check(Image(bounds) == fitted, "Fit restores initial framing");
Call("Frame", bounds, dimensions, fitted, new UnityEngine.Rect(20, 20, 0, 1));
Check(Image(bounds) == fitted, "Degenerate area does not change the view");
Check(Image(new UnityEngine.Rect(0, 0, 0, 0)).size == UnityEngine.Vector2.zero, "Zero-size viewport stays finite");
Check(Image(new UnityEngine.Rect(0, 0, float.NaN, float.NaN)) == UnityEngine.Rect.zero,
    "Unresolved layout does not write non-finite image geometry");
UnityEngine.Vector2 Map(string method, UnityEngine.Vector2 point) =>
    (UnityEngine.Vector2)type.GetMethod(method, flags).Invoke(viewport, new object[] { bounds, point });
float Rotation() => (float)type.GetProperty("Rotation", flags).GetValue(viewport);
foreach (float angle in new[] { 0f, 17f, 45f, 90f, -90f, 179f, -178f })
{
    Call("Reset");
    Call("SetRotation", angle, false);
    var before = Image(bounds);
    Check(before == fitted, "Rotation leaves the canonical image rect untouched");
    Check(Close(Map("ToCanvas", Map("ToView", anchor)), anchor), "Pointer mapping round-trips");
    var pixel = UV(before, Map("ToCanvas", anchor));
    Call("ZoomAt", bounds, dimensions, before, anchor, fittedScale * 1.7f);
    Check(Close(UV(Image(bounds), Map("ToCanvas", anchor)), pixel), "Rotated zoom preserves the anchor pixel");
    var beforePan = Map("ToView", Image(bounds).position);
    var delta = new UnityEngine.Vector2(37, -21);
    Call("Pan", bounds, dimensions, Image(bounds), delta);
    Check(Close(Map("ToView", Image(bounds).position), beforePan + delta), "Rotated pan follows the screen delta");
    var framePixel = UV(Image(bounds), Map("ToCanvas", selection.center));
    Call("Frame", bounds, dimensions, Image(bounds), selection);
    Check(Close(UV(Image(bounds), bounds.center), framePixel), "Rotated box zoom centers its selected pixel");
    var visible = (UnityEngine.Rect)type.GetMethod("VisibleCanvasBounds", flags).Invoke(viewport, new object[] { bounds });
    foreach (var corner in new[] { bounds.min, bounds.max,
        new UnityEngine.Vector2(bounds.xMax, bounds.yMin), new UnityEngine.Vector2(bounds.xMin, bounds.yMax) })
    {
        var p = Map("ToCanvas", corner);
        Check(p.x >= visible.xMin - .001f && p.x <= visible.xMax + .001f &&
            p.y >= visible.yMin - .001f && p.y <= visible.yMax + .001f, "Visible tile range covers every rotated corner");
    }
    var preserved = Image(bounds);
    Call("SetRotation", 0f, false);
    Check(Image(bounds) == preserved, "Angle-only reset preserves zoom and pan");
}
Call("SetRotation", 88f, true);
Check(Rotation() == 90f, "Light snapping catches right angles");
Call("SetRotation", 88f, false);
Check(Rotation() == 88f, "Ctrl bypasses snapping");
Call("SetRotation", 85f, true);
Check(Rotation() == 85f, "Outside snap tolerance rotation remains continuous");
Call("SetRotation", float.NaN, false);
Call("SetRotation", float.PositiveInfinity, false);
Check(Rotation() == 85f, "Non-finite rotations are ignored");
Call("Reset");
Check(Rotation() == 0f && Image(bounds) == fitted, "Fit resets rotation and framing together");
return "Preview zoom checks passed: " + checks + ". No assets or GPU resources created.";
