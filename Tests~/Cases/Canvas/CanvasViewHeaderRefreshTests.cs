using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class CanvasViewHeaderRefreshTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

private static string BodyRun()
{
// Opt-in eval body after manual compilation. No saved assets or visible windows.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var windowType = typeof(DCFApixels.WhimTex.WhimTexWindow);
var readout = windowType.GetMethod("RefreshCanvasZoomReadout", Hidden);
T.True(!(readout == null), "Manually compile the preview header change before running this test.");
var window = Scope.OwnWindow(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexWindow>());
int checks = 0, fullRefreshes = 0;
void Check(bool value, string message)
{ T.True(value, message); }
try
{
    var canvasType = windowType.GetNestedType("CanvasElement", System.Reflection.BindingFlags.NonPublic);
    var viewport = windowType.GetField("canvasViewport", Hidden).GetValue(window);
    var canvas = Activator.CreateInstance(canvasType, new[] { viewport });
    windowType.GetField("toolkitCanvas", Hidden).SetValue(window, canvas);
    windowType.GetField("toolkitCanvasViewHeader", Hidden).SetValue(window, new UnityEngine.UIElements.VisualElement());
    windowType.GetMethod("BuildCanvasZoomTool", Hidden).Invoke(window, null);
    windowType.GetMethod("AddCanvasZoomSettings", Hidden).Invoke(window, null);
    var bindings = windowType.GetField("toolkitHeaderBindings", Hidden).GetValue(window);
    bindings.GetType().GetMethod("Add").Invoke(bindings, new object[] { (Action)(() => fullRefreshes++) });
    var field = (UnityEngine.UIElements.FloatField)windowType.GetField("canvasZoomPercent", Hidden).GetValue(window);
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
    windowType.GetMethod("AddCanvasZoomSettings", Hidden).Invoke(window, null);
    bindings.GetType().GetMethod("Refresh").Invoke(bindings, new object[] { false });
    var rebuiltField = (UnityEngine.UIElements.FloatField)windowType.GetField("canvasZoomPercent", Hidden).GetValue(window);
    Check(!ReferenceEquals(field, rebuiltField) && rebuiltField.value == 100f,
        "Rebuilt headers initialize their readout at unchanged scale");
}
finally
{
    window.DiscardChanges();
    WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
}
return null;


}
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
