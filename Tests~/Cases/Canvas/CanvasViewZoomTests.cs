using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class CanvasViewZoomTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

private static string BodyRun()
{
var type = typeof(DCFApixels.WhimTex.WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.CanvasViewport", true);
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
{ T.True(value, message); }
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
var navigation = type.Assembly.GetType("DCFApixels.WhimTex.CanvasNavigationMetrics", true);
object Metric(string method, params object[] args) => navigation.GetMethod(method,
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, args);
float previousStep = float.PositiveInfinity;
foreach (float pixelScale in new[] { 1f / 1024f, .01f, .1f, .25f, 1f, 4f, 16f, 64f })
{
    float step = (float)Metric("MajorStep", pixelScale);
    Check(step * pixelScale >= 71.99f, "Ruler labels keep readable spacing at every supported zoom");
    Check(step <= previousStep && step >= 1f, "Ruler density increases with zoom without fractional pixel ticks");
    previousStep = step;
}
Check((float)Metric("MajorStep", 0f) == 1f && (float)Metric("MajorStep", float.NaN) == 1f,
    "Unresolved ruler scale has a finite fallback");
var fitRange = (Vector3)Metric("ScrollRange", 0f, 800f, 80f, 640f);
Check(fitRange == new Vector3(0f, 0f, 800f), "Fitted centered canvas fills the scrollbar track");
Vector2 Thumb(Vector3 range, float length) => (Vector2)Metric("ScrollThumb", range, length);
float DragScale(Vector3 range, float length) => (float)Metric("ScrollDragScale", range, length);
Check(Thumb(fitRange, 800f) == new Vector2(0f, 800f), "Fitted canvas has a full-width thumb");
Check(DragScale(fitRange, 800f) == 1f, "Full-width thumb still has a positive drag scale");
Check(Mathf.Approximately(DragScale(fitRange, 764f), 800f / 764f),
    "Arrow buttons reduce track length without disabling full-thumb dragging");
float previousSize = 800f;
foreach (float offset in new[] { 1f, 50f, 400f, 1600f, 8000f })
{
    var forward = (Vector3)Metric("ScrollRange", 0f, 800f, 80f - offset, 640f);
    var backward = (Vector3)Metric("ScrollRange", 0f, 800f, 80f + offset, 640f);
    Check(forward.x == offset && forward.y == offset && backward.x == offset && backward.y == 0f,
        "Workspace expands immediately in both directions even when canvas fits");
    Vector2 forwardThumb = Thumb(forward, 800f), backwardThumb = Thumb(backward, 800f);
    Check(forwardThumb.y < previousSize && Mathf.Approximately(forwardThumb.y, backwardThumb.y),
        "Thumb shrinks progressively and symmetrically outside the fitted workspace");
    Check(Mathf.Approximately(forwardThumb.x + forwardThumb.y, 800f) && backwardThumb.x == 0f,
        "Overscroll thumb remains attached to the corresponding end of the track");
    Check(DragScale(forward, 800f) > 0f && !float.IsInfinity(DragScale(forward, 800f)),
        "Overscroll keeps a finite positive drag scale");
    previousSize = forwardThumb.y;
}
Check(Thumb(fitRange, 800f).y == 800f, "Returning to the fitted workspace restores thumb size");
var farRange = (Vector3)Metric("ScrollRange", 0f, 800f, -100000f, 640f);
Check(Thumb(farRange, 800f).y == 24f, "Distant pan preserves a usable minimum thumb size");
Check(Thumb(farRange, 12f).y == 12f, "Minimum thumb size never exceeds a short track");
Check(DragScale(farRange, 12f) > 0f, "Short tracks with no thumb travel still allow panning");
Check(Thumb(fitRange, 0f) == Vector2.zero && DragScale(fitRange, 0f) == 0f,
    "Unresolved track geometry remains finite");
var largeRange = (Vector3)Metric("ScrollRange", 0f, 800f, -600f, 2000f);
Check(largeRange == new Vector3(1600f, 800f, 800f), "Large canvas has proportional range and working margins");
var movedRange = (Vector3)Metric("ScrollRange", 0f, 800f, -650f, 2000f);
Check(movedRange.y == largeRange.y + 50f, "Dragging scrollbar forward pans image backward");
Check(Thumb(largeRange, 800f).y == 800f / 3f &&
    Mathf.Approximately(DragScale(largeRange, 800f), 3f), "Normal scrolling retains proportional thumb size and speed");
var largeOutside = (Vector3)Metric("ScrollRange", 0f, 800f, -2600f, 2000f);
Check(Thumb(largeOutside, 800f).y < Thumb(largeRange, 800f).y,
    "Scrolling past a large canvas shrinks its thumb too");
var outsideRange = (Vector3)Metric("ScrollRange", 0f, 800f, 1600f, 100f);
Check(outsideRange.x > 0f && outsideRange.y == 0f, "Canvas panned outside view remains reachable through scrollbar");
foreach (float angle in new[] { 0f, 17f, 45f, 90f, -90f, 179f })
{
    Call("Reset");
    Call("SetRotation", angle, false);
    Rect projected = (Rect)Metric("PresentedBounds", viewport, bounds, Image(bounds));
    foreach (Vector2 corner in new[] { fitted.min, fitted.max,
        new Vector2(fitted.xMax, fitted.yMin), new Vector2(fitted.xMin, fitted.yMax) })
    {
        Vector2 point = Map("ToView", corner);
        Check(point.x >= projected.xMin - .001f && point.x <= projected.xMax + .001f &&
            point.y >= projected.yMin - .001f && point.y <= projected.yMax + .001f,
            "Scrollbar content bounds cover every rotated canvas corner");
    }
    Vector2 delta = new Vector2(37f, -21f);
    Call("Pan", bounds, dimensions, Image(bounds), delta);
    Rect pannedBounds = (Rect)Metric("PresentedBounds", viewport, bounds, Image(bounds));
    Check(Close(pannedBounds.position, projected.position + delta), "Scrollbar pan remains in view axes at every rotation");
}
return null;


}
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
