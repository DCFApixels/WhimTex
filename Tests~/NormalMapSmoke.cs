// Opt-in after manual compilation. Transient objects only; no asset saves/imports or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 32;
var texture = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBAFloat, false, true);
texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
texture.filterMode = UnityEngine.FilterMode.Point;
var file = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = texture };
var normal = new DCFApixels.WhimTex.NormalMapLayerBehaviour
{
    inputSpace = DCFApixels.WhimTex.NormalMapLayerBehaviour.InputSpace.Linear,
    encoding = DCFApixels.WhimTex.NormalMapLayerBehaviour.OutputEncoding.LinearData,
    smoothing = 0f, strength = 4f
};
document.layers.Add(normal); document.layers.Add(file);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
bool Near(float a, float b, float epsilon = .015f) => System.Math.Abs(a-b) < epsilon;
void Source(System.Func<int,int,UnityEngine.Color> color)
{
    var pixels = new UnityEngine.Color[1024];
    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) pixels[y*32+x] = color(x,y);
    texture.SetPixels(pixels); texture.Apply(false,false);
}
UnityEngine.Color Pixel(int x = 16, int y = 16, int size = 32)
{
    var rt = (UnityEngine.RenderTexture)document.GetType().GetMethod("RenderLayerPreview", flags)
        .Invoke(document, new object[] { normal.Owner, size });
    var previous = UnityEngine.RenderTexture.active;
    var readback = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGBAFloat, false, true);
    try
    {
        UnityEngine.RenderTexture.active = rt;
        readback.ReadPixels(new UnityEngine.Rect(0,0,rt.width,rt.height),0,0,false);
        return readback.GetPixel(x,y);
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(readback); UnityEngine.RenderTexture.ReleaseTemporary(rt);
    }
}
try
{
    Source((x,y) => new UnityEngine.Color(.4f,.4f,.4f,1));
    foreach (DCFApixels.WhimTex.NormalMapLayerBehaviour.GenerationMode mode in System.Enum.GetValues(typeof(DCFApixels.WhimTex.NormalMapLayerBehaviour.GenerationMode)))
    {
        normal.mode = mode;
        var flat = Pixel();
        Check(Near(flat.r,.5f) && Near(flat.g,.5f) && Near(flat.b,1f), "Constant height gives a flat normal in " + mode);
    }
    normal.mode = DCFApixels.WhimTex.NormalMapLayerBehaviour.GenerationMode.HeightMap;
    Source((x,y) => new UnityEngine.Color(x/31f,x/31f,x/31f,1));
    foreach (DCFApixels.WhimTex.NormalMapLayerBehaviour.DerivativeFilter filter in System.Enum.GetValues(typeof(DCFApixels.WhimTex.NormalMapLayerBehaviour.DerivativeFilter)))
    {
        normal.derivative = filter;
        var slope = Pixel();
        Check(slope.r < .49f && Near(slope.g,.5f), "Horizontal derivative in " + filter);
        float nx = 2*slope.r-1, ny = 2*slope.g-1, nz = 2*slope.b-1;
        Check(Near(nx*nx+ny*ny+nz*nz,1), "Unit normal");
    }
    var original = Pixel();
    normal.flipX = true; Check(Near(Pixel().r,1-original.r), "Flip X"); normal.flipX = false;
    normal.inverted = true; Check(Near(Pixel().r,1-original.r), "Invert height"); normal.inverted = false;
    var reduced = Pixel(8,8,16); Check(Near(reduced.r,original.r), "Slope amplitude survives reduced preview resolution");
    normal.strength = 0; Check(Near(Pixel().r,.5f), "Zero strength"); normal.strength = 4;
    normal.encoding = DCFApixels.WhimTex.NormalMapLayerBehaviour.OutputEncoding.PackedColor;
    var packed = Pixel();
    Check(Near(packed.r,UnityEngine.Mathf.GammaToLinearSpace(original.r)), "Packed encoding follows the image export boundary");
    normal.encoding = DCFApixels.WhimTex.NormalMapLayerBehaviour.OutputEncoding.LinearData;
    var group = new DCFApixels.WhimTex.GroupLayerBehaviour(); group.layers.Add(file);
    document.layers[1] = group;
    Check(Near(Pixel().r,original.r), "Group supplies color, not only alpha");
    document.layers[1] = file;
    Source((x,y) => new UnityEngine.Color(y/31f,y/31f,y/31f,1));
    float green = Pixel().g; Check(green < .49f, "Vertical derivative");
    normal.flipY = true; Check(Near(Pixel().g,1-green), "Flip Y"); normal.flipY = false;
    Source((x,y) => new UnityEngine.Color(.7f,.7f,.7f,x < 16 ? 1 : 0));
    normal.smoothing = 2;
    var edge = Pixel(15,16); Check(Near(edge.r,.5f), "Transparent RGB does not create a rim");
    normal.alphaMode = DCFApixels.WhimTex.NormalMapLayerBehaviour.AlphaMode.Source;
    Check(Pixel(24,16).a < .001f, "Preserve source alpha");
    normal.alphaMode = DCFApixels.WhimTex.NormalMapLayerBehaviour.AlphaMode.Opaque;
    Check(Near(Pixel(24,16).a,1f), "Opaque normal outside source coverage");
    normal.sourceChannel = DCFApixels.WhimTex.NormalMapLayerBehaviour.HeightChannel.Alpha;
    Check(Pixel(15,16).r > .51f, "Alpha can itself be height");
    var clone = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.NormalMapLayerBehaviour>(UnityEngine.JsonUtility.ToJson(normal));
    Check(clone.sourceChannel == normal.sourceChannel && clone.smoothing == normal.smoothing, "Layer settings serialization");
    return "Normal Map checks passed: " + checks;
}
finally
{
    UnityEngine.Object.DestroyImmediate(document);
    UnityEngine.Object.DestroyImmediate(texture);
}
