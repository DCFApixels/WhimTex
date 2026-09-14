// Opt-in eval body after manual Unity compilation; requires an active graphics device.
// Creates temporary objects only. Does not save assets or touch Editor Undo.

int checks = 0;
const System.Reflection.BindingFlags InstanceHidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var renderPreview = type.GetMethod("RenderPreview", InstanceHidden);
var composePreview = type.GetMethod("ComposePreview", InstanceHidden);
var copy = type.GetMethod("CopyToTexture2D", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void EqualPixels(Texture2D a, Texture2D b)
{
    Check(a.width == b.width && a.height == b.height, "Preview dimensions differ");
    var left = a.GetRawTextureData<Color32>();
    var right = b.GetRawTextureData<Color32>();
    for (int i = 0; i < left.Length; i++)
    {
        Color32 x = left[i], y = right[i];
        if (Math.Abs(x.r - y.r) > 1 || Math.Abs(x.g - y.g) > 1 ||
            Math.Abs(x.b - y.b) > 1 || Math.Abs(x.a - y.a) > 1)
            throw new Exception($"Preview pixels differ at {i}: {x} / {y}");
    }
    checks++;
}

var document = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
Texture2D source = null;
RenderTexture sentinel = null;
RenderTexture originalActive = RenderTexture.active;
try
{
    document.hideFlags = HideFlags.HideAndDontSave;
    document.width = 32;
    document.height = 16;
    source = new Texture2D(32, 16, TextureFormat.RGBA32, false)
        { hideFlags = HideFlags.HideAndDontSave };
    var pixels = source.GetRawTextureData<Color32>();
    for (int y = 0; y < 16; y++)
        for (int x = 0; x < 32; x++)
            pixels[y * 32 + x] = new Color32((byte)(x * 8), (byte)(y * 16), 93,
                (byte)(x > 3 && x < 28 && y > 2 && y < 13 ? 180 : 0));
    source.Apply(false, false);
    var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", InstanceHidden).SetValue(drawing, source);
    sentinel = RenderTexture.GetTemporary(2, 2);
    RenderTexture.active = sentinel;

    for (int mode = 0; mode < 4; mode++)
    {
        document.layers.Clear();
        if (mode == 2) document.layers.Add(new DCFApixels.WhimTex.SDFLayerBehaviour());
        if (mode == 3) document.layers.Add(new DCFApixels.WhimTex.OutlineLayerBehaviour());
        if (mode != 0) document.layers.Add(drawing);
        foreach (int size in new[] { 32, 16, 1, 32 })
        {
            RenderTexture gpu = null;
            Texture2D cpuOnly = null, uploaded = null, legacy = null;
            try
            {
                gpu = (RenderTexture)renderPreview.Invoke(document, new object[] { size });
                Check(gpu != null && gpu.IsCreated(), "GPU preview is unavailable");
                Check(RenderTexture.active == sentinel, "Rendering changed the active target");
                cpuOnly = (Texture2D)copy.Invoke(null, new object[] { gpu, false });
                uploaded = (Texture2D)copy.Invoke(null, new object[] { gpu, true });
                EqualPixels(cpuOnly, uploaded);
                var linear = (Texture2D)composePreview.Invoke(document, new object[] { size });
                try
                {
                    var toLdr = type.Assembly.GetType("DCFApixels.WhimTex.HdrUtility")
                        .GetMethod("ToLdr", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    legacy = (Texture2D)toLdr.Invoke(null, new object[] { linear, false });
                }
                finally { UnityEngine.Object.DestroyImmediate(linear); }
                EqualPixels(cpuOnly, legacy);
                Check(RenderTexture.active == sentinel, "Readback changed the active target");
            }
            finally
            {
                RenderTexture.active = sentinel;
                if (gpu != null) RenderTexture.ReleaseTemporary(gpu);
                if (cpuOnly != null) UnityEngine.Object.DestroyImmediate(cpuOnly);
                if (uploaded != null) UnityEngine.Object.DestroyImmediate(uploaded);
                if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
            }
        }
    }
}
finally
{
    RenderTexture.active = originalActive;
    if (sentinel != null) RenderTexture.ReleaseTemporary(sentinel);
    UnityEngine.Object.DestroyImmediate(document);
    if (source != null) UnityEngine.Object.DestroyImmediate(source);
}
return $"GPU preview checks passed: {checks}.";
