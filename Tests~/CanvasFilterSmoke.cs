// Unity Pipeline eval_file; transient objects only, no saved assets or user documents.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var type = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
var window = ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositorWindow>();
window.name = "Canvas Filter Smoke";
var document = (DCFApixels.SpriteEditor.TextureCompositor)type.GetField("compositor", Hidden).GetValue(window);
var source = new Texture2D(4, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
var previous = RenderTexture.active;
bool previousSrgb = GL.sRGBWrite;
var render = typeof(DCFApixels.SpriteEditor.TextureCompositor).GetMethod("RenderPreview", Hidden);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
float SampleUpscaled(Texture texture)
{
    var target = RenderTexture.GetTemporary(8, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
    var pixels = new Texture2D(8, 4, TextureFormat.RGBAFloat, false, true);
    try
    {
        GL.sRGBWrite = false;
        Graphics.Blit(texture, target);
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 8, 4), 0, 0, false);
        return pixels.GetPixel(3, 1).r;
    }
    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(pixels); }
}
try
{
    Check(document.outputFilter == FilterMode.Bilinear, "Default remains Bilinear");
    document.width = 4; document.height = 2;
    source.SetPixels(new[] { Color.black, Color.black, Color.white, Color.white, Color.black, Color.black, Color.white, Color.white }); source.Apply();
    document.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.FileLayerBehaviour { sourceTexture = source }));
    var toolbar = new UnityEngine.UIElements.VisualElement();
    type.GetField("toolkitCanvasToolbar", Hidden).SetValue(window, toolbar);
    type.GetMethod("BuildToolkitCanvasToolbar", Hidden).Invoke(window, null);
    var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.EnumField>(toolbar, "canvasOutputFilter");
    Check(field != null && field.label == "Filter", "Canvas filter control exists");
    float pointSample = 0, bilinearSample = 0;
    foreach (FilterMode mode in new[] { FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear })
    {
        document.outputFilter = mode;
        var texture = document.Compose();
        var preview = (RenderTexture)render.Invoke(document, new object[] { 4 });
        try
        {
            Check(texture.filterMode == mode, "Compose sampling " + mode);
            Check(preview.filterMode == mode, "Preview sampling " + mode);
            Check(texture.mipmapCount == 1 && !preview.useMipMap, "No extra mipmaps allocated");
            if (mode == FilterMode.Point) pointSample = SampleUpscaled(texture);
            if (mode == FilterMode.Bilinear) bilinearSample = SampleUpscaled(texture);
            var clone = UnityEngine.Object.Instantiate(document);
            try { Check(clone.outputFilter == mode, "Cloning retains filter " + mode); }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
            using (var serialized = new SerializedObject(document))
                Check(serialized.FindProperty("outputFilter").intValue == (int)mode, "Serialized document setting");
            var sessionType = typeof(DCFApixels.SpriteEditor.TextureCompositor).Assembly.GetType("DCFApixels.SpriteEditor.LiveOutputSession");
            var session = (IDisposable)Activator.CreateInstance(sessionType, Hidden, null, new object[] { texture }, null);
            try
            {
                sessionType.GetMethod("SetFilter", Hidden).Invoke(session, new object[] { FilterMode.Point });
                Check(texture.filterMode == FilterMode.Point, "Live sampling changes");
                sessionType.GetMethod("Publish", Hidden).Invoke(session, new object[] { preview });
            }
            finally { session.Dispose(); }
            Check(texture.filterMode == mode, "Live off restores saved filter");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); RenderTexture.ReleaseTemporary(preview); }
    }
    Check(pointSample < .05f && bilinearSample > .1f && bilinearSample < .5f, "GPU Point/Bilinear produce distinct edge sampling");
    var previewTexture = RenderTexture.GetTemporary(4, 2);
    var channel = new RenderTexture(4, 2, 0);
    var post = new RenderTexture(4, 2, 0);
    try
    {
        type.GetField("previewTexture", Hidden).SetValue(window, previewTexture);
        type.GetField("channelPreviewTexture", Hidden).SetValue(window, channel);
        type.GetField("postFxTexture", Hidden).SetValue(window, post);
        var toolField = type.GetField("previewTool", Hidden);
        document.outputFilter = FilterMode.Trilinear;
        toolField.SetValue(window, Enum.Parse(toolField.FieldType, "Pencil"));
        type.GetMethod("ApplyPreviewTextureFilter", Hidden).Invoke(window, null);
        Check(previewTexture.filterMode == FilterMode.Point && channel.filterMode == FilterMode.Point && post.filterMode == FilterMode.Point, "Pencil preview override");
        Check(document.outputFilter == FilterMode.Trilinear, "Pencil leaves output setting intact");
        toolField.SetValue(window, Enum.Parse(toolField.FieldType, "Brush"));
        type.GetMethod("ApplyPreviewTextureFilter", Hidden).Invoke(window, null);
        Check(previewTexture.filterMode == FilterMode.Trilinear && channel.filterMode == FilterMode.Trilinear && post.filterMode == FilterMode.Trilinear, "Return to document filtering");
    }
    finally
    {
        type.GetField("previewTexture", Hidden).SetValue(window, null);
        type.GetField("channelPreviewTexture", Hidden).SetValue(window, null);
        type.GetField("postFxTexture", Hidden).SetValue(window, null);
        RenderTexture.ReleaseTemporary(previewTexture); UnityEngine.Object.DestroyImmediate(channel); UnityEngine.Object.DestroyImmediate(post);
    }
    document.outputFilter = (FilterMode)123;
    typeof(DCFApixels.SpriteEditor.TextureCompositor).GetMethod("NormalizeModel", Hidden).Invoke(document, null);
    Check(document.outputFilter == FilterMode.Bilinear, "Invalid filter normalized");
    return "Canvas filtering passed: " + checks + " checks. Point=" + pointSample + ", Bilinear=" + bilinearSample;
}
finally
{
    RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
    UnityEngine.Object.DestroyImmediate(window);
    UnityEngine.Object.DestroyImmediate(source);
}
