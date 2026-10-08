using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerRingTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static WhimTexColorPicker Picker() => Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x => x.name == (Scope.Tag + "-picker-layout"));

    private static string Verify()
    {
        var picker = Picker();
        var ring = picker.rootVisualElement.Q(className: "whimtex-picker-ring");
        T.True(!(!(ring is ImmediateModeElement)), "Ring is not an ImmediateModeElement");
        var field = ring.GetType().GetField("material", Fields);
        var material = (Material)field.GetValue(ring);
        T.True(!(material == null || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)), "Ring shader unavailable");
        var parent = ring.parent;
        int index = parent.IndexOf(ring);
        ring.RemoveFromHierarchy();
        T.True(!(material != null || field.GetValue(ring) != null), "Detached ring retained its material");
        parent.Insert(index, ring);
        T.True(!((Material)field.GetValue(ring) == null), "Reattached ring did not recreate its material");
        return null;
    }

    

    

private static string VerifyPlusCenter()
    {
        var tile = Picker().rootVisualElement.Q(className: "whimtex-picker-add-color");
        float scale = EditorGUIUtility.pixelsPerPoint;
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            image.LoadImage(File.ReadAllBytes(Scope.Temp + "/color-picker-ring.png"));
            var bounds = tile.worldBound;
            int x0 = Mathf.RoundToInt(bounds.xMin * scale), y0 = Mathf.RoundToInt(bounds.yMin * scale);
            int width = Mathf.RoundToInt(bounds.width * scale), height = Mathf.RoundToInt(bounds.height * scale);
            Vector2 grayMin = Vector2.one * float.PositiveInfinity, grayMax = Vector2.one * float.NegativeInfinity;
            Vector2 plusMin = grayMin, plusMax = grayMax;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Color c = image.GetPixel(x0 + x, image.height - 1 - y0 - y);
                var p = new Vector2(x + .5f, y + .5f);
                if (c.r > .4f) { grayMin = Vector2.Min(grayMin, p); grayMax = Vector2.Max(grayMax, p); }
                if (c.r > .7f) { plusMin = Vector2.Min(plusMin, p); plusMax = Vector2.Max(plusMax, p); }
            }
            Vector2 delta = (plusMin + plusMax - grayMin - grayMax) * .5f;
            T.True(!(float.IsNaN(delta.x) || float.IsNaN(delta.y) || delta.magnitude > .51f), "Plus is not centered in the rendered gray fill: " + delta);
            return null;
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }

    private static string ClipAndScale()
    {
        var picker = Picker();
        var ring = picker.rootVisualElement.Q(className: "whimtex-picker-ring");
        var parent = ring.parent;
        ring.RemoveFromHierarchy();
        var clip = new VisualElement { name = "ring-test-clip" };
        clip.style.position = Position.Absolute;
        clip.style.left = 0; clip.style.top = 0;
        clip.style.width = 220; clip.style.height = 110;
        clip.style.overflow = Overflow.Hidden;
        parent.Insert(0, clip);
        clip.Add(ring);
        ring.style.width = 220; ring.style.height = 220;
        ring.style.scale = new Scale(new Vector3(.75f, .75f, 1));
        picker.Repaint();
        return null;
    }

    

private static async Task<string> BodyRingScenario() {

T.True(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0, "Close the borrowed active picker first");
var doc = Scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
for(int i=0;i<20;i++) typeof(WhimTexDocument).GetMethod("RememberColor",flags).Invoke(doc,new object[]{Color.HSVToRGB(i/20f,.75f,.8f)});
var picker = Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",flags).Invoke(null,new object[]{new Color(.1f,.5f,.8f,1),true,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>{}),null,null}));
picker.name = Scope.Tag + "-picker-layout";
try {
 await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
 Verify();
 await WhimTex.Tests.UnityA.UnityACapture.Capture(picker, Scope.Temp + "/color-picker-ring.png", Cancellation);
 VerifyPlusCenter();
 ClipAndScale();
 await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
 await WhimTex.Tests.UnityA.UnityACapture.Capture(picker, Scope.Temp + "/clipped-ring.png", Cancellation);
} finally { if(picker!=null) typeof(WhimTexColorPicker).GetMethod("Finish",flags).Invoke(picker,new object[]{false}); }

return null;
}

public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyRingScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
