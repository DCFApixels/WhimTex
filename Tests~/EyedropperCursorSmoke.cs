// Opt-in after manual compilation. Temporary textures only; requires graphics, never sets the OS cursor or writes assets.
var factory = typeof(DCFApixels.WhimTex.TextureCompositorWindow).GetMethod("CreateScreenEyedropperCursor",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new System.Exception(message);
    checks++;
}
Check(factory != null, "Cursor texture factory exists");
Check(factory.Invoke(null, new object[] { null }) == null, "Missing icon uses the standard cursor fallback");
foreach (bool readable in new[] { true, false })
{
    var source = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.ARGB32, true, true)
        { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
    UnityEngine.Texture2D copy = null;
    var previous = UnityEngine.RenderTexture.active;
    bool previousSrgb = UnityEngine.GL.sRGBWrite;
    try
    {
        var pixels = new UnityEngine.Color32[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new UnityEngine.Color32(90, 170, 230, 128);
        source.SetPixels32(pixels);
        source.Apply(true, !readable);
        copy = (UnityEngine.Texture2D)factory.Invoke(null, new object[] { source });
        Check(copy != null && copy != source, "Cursor owns a separate copy");
        Check(copy.format == UnityEngine.TextureFormat.RGBA32, "Cursor is RGBA32");
        Check(copy.isReadable && copy.mipmapCount == 1 && copy.alphaIsTransparency, "All native cursor requirements hold");
        Check(copy.width == source.width && copy.height == source.height, "Icon dimensions are retained");
        var pixel = copy.GetPixels32()[36];
        Check(System.Math.Abs(pixel.r - 90) <= 2 && System.Math.Abs(pixel.g - 170) <= 2 &&
            System.Math.Abs(pixel.b - 230) <= 2 && System.Math.Abs(pixel.a - 128) <= 2, "RGBA survives copying");
        Check(source.format == UnityEngine.TextureFormat.ARGB32 && source.mipmapCount > 1 &&
            source.isReadable == readable && !source.alphaIsTransparency, "Source icon stays unchanged");
        Check(UnityEngine.RenderTexture.active == previous && UnityEngine.GL.sRGBWrite == previousSrgb, "Graphics state is restored");
    }
    finally
    {
        if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
        UnityEngine.Object.DestroyImmediate(source);
    }
}
return "Cursor texture GPU/CPU checks passed: " + checks + "; native cursor/input not exercised.";
