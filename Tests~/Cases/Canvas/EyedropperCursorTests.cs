using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

public static class EyedropperCursorTests
{
    static string Execute()
    {
// Opt-in after manual compilation. Reads the cursor asset and owns temporary copies; never sets the OS cursor or writes assets.
var factory = typeof(DCFApixels.WhimTex.WhimTexWindow).GetMethod("CreateScreenEyedropperCursor",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var create = typeof(WhimTexWindow).GetMethod("CreateEyedropperCursor",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var hotspotMethod = typeof(WhimTexWindow).GetMethod("EyedropperCursorHotspot",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
int checks = 0;
void Check(bool value, string message)
{
    UnityBRun.Check(!(!value), message);
    checks++;
}
Check(factory != null, "Cursor texture factory exists");
Check(create != null && hotspotMethod != null, "Shared asset factory and hotspot exist");
Check(factory.Invoke(null, new object[] { null }) == null, "Missing icon uses the standard cursor fallback");
var source = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.dcfapixels.whimtex/src/Editor/Icons/EyedropperCursor.png");
Check(source != null && source.isReadable, "Vector-derived cursor asset is available and readable");
var sourcePixels = source.GetPixels32();
for (int iteration = 0; iteration < 2; iteration++)
{
    UnityEngine.Texture2D copy = null;
    var previous = UnityEngine.RenderTexture.active;
    bool previousSrgb = UnityEngine.GL.sRGBWrite;
    try
    {
        copy = (Texture2D)create.Invoke(null, null);
        Check(copy != null && copy != source, "Cursor owns a separate copy");
        Check(!EditorUtility.IsPersistent(copy) && copy.hideFlags == HideFlags.HideAndDontSave, "Cursor copy is transient");
        Check(copy.format == UnityEngine.TextureFormat.RGBA32, "Cursor is RGBA32");
        Check(copy.isReadable && copy.mipmapCount == 1 && copy.alphaIsTransparency, "All native cursor requirements hold");
        Check(copy.width == 32 && copy.height == 32 && copy.filterMode == FilterMode.Point, "Cursor retains native resolution and point sampling");
        var pixels = copy.GetPixels32();
        Check(pixels.SequenceEqual(sourcePixels), "Vector-derived RGBA is copied without padding or another outline pass");
        var hotspot = (Vector2)hotspotMethod.Invoke(null, new object[] { copy });
        Check(hotspot == new Vector2(2, 26), "Hotspot is the lower-left pipette tip, not a separate arrow");
        var tip = pixels[(copy.height - 1 - (int)hotspot.y) * copy.width + (int)hotspot.x];
        Check(tip.a == 255, "Sampled pixel is covered by the opaque visible tip");
        int minX = copy.width, minY = copy.height, maxX = -1, maxY = -1;
        for (int y = 0; y < copy.height; y++) for (int x = 0; x < copy.width; x++)
        {
            if (pixels[y * copy.width + x].a < 128) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        Check(maxX - minX + 1 <= 26 && maxY - minY + 1 <= 26, "Visible silhouette fits the cursor with a clear margin");
        Check(UnityEngine.RenderTexture.active == previous && UnityEngine.GL.sRGBWrite == previousSrgb, "Graphics state is restored");
    }
    finally
    {
        if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
    }
    Check(source != null && EditorUtility.IsPersistent(source) && source.GetPixels32().SequenceEqual(sourcePixels),
        "Destroying the session copy preserves the imported cursor asset");
}
return "Vector-derived cursor asset checks passed: " + checks + "; native cursor/input not exercised.";
    }
    public static string Run() => UnityBRun.Run("EyedropperCursorSmoke", () => Execute());
}

