// Opt-in eval body after manual Unity compilation and shader import.
// Temporary objects only; no saved assets, visible windows, preferences, or Undo changes.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
const System.Reflection.BindingFlags StaticHidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var layerType = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour);
var utility = layerType.Assembly.GetType("DCFApixels.WhimTex.TiledCanvasUtility", true);
object MathCall(string name, params object[] args) => utility.GetMethod(name, StaticHidden).Invoke(null, args);
object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
Vector2 ToSource(Vector2 uv, DCFApixels.WhimTex.TextureTransform transform, int width, int height) =>
    (Vector2)MathCall("ToSource", uv, transform, width, height);
Vector2 ToDocument(Vector2 uv, DCFApixels.WhimTex.TextureTransform transform, int width, int height) =>
    (Vector2)MathCall("ToDocument", uv, transform, width, height);
var transforms = new List<DCFApixels.WhimTex.TextureTransform>();
foreach (float angle in new[] { 0f, 37f, 90f, 173f })
foreach (var scale in new[] { Vector2.one, new Vector2(2f,0.6f), new Vector2(-1.5f,2f), new Vector2(0.1f,8f) })
{
    var transform = DCFApixels.WhimTex.TextureTransform.Default;
    transform.rotation = angle;
    transform.scale = scale;
    transform.position = new Vector2(11f,-7f);
    transform.pivot = new Vector2(0.3f,0.6f);
    transforms.Add(transform);
    foreach (var uv in new[] { new Vector2(-2.7f,4.3f), Vector2.zero, new Vector2(0.2f,0.8f), Vector2.one })
        Check((ToDocument(ToSource(uv, transform, 128,64), transform, 128,64) - uv).sqrMagnitude < 0.0000001f,
            "Transformed tiled coordinates round-trip outside the primary canvas");
    object[] basis = { transform, 128, 64, Vector2.zero, Vector2.zero };
    MathCall("GetPeriodBasis", basis);
    Vector2 u = (Vector2)basis[3], v = (Vector2)basis[4];
    Check(u.sqrMagnitude <= v.sqrMagnitude * 1.00001f && Mathf.Abs(Vector2.Dot(u,v)) <= u.sqrMagnitude * 0.50001f,
        "Period basis is reduced for reliable nearest-copy selection");
    foreach (var delta in new[] { new Vector2(5,7), new Vector2(-61,73), new Vector2(131,-27) })
    {
        Vector2 nearest = (Vector2)MathCall("NearestPeriodicDelta", delta, u, v);
        for (int a = -3; a <= 3; a++)
        for (int b = -3; b <= 3; b++)
            Check(nearest.sqrMagnitude <= (nearest + a*u + b*v).sqrMagnitude + 0.01f,
                "Nearest periodic point minimizes source-space brush distance");
    }
}

var previousActive = RenderTexture.active;
try
{
    foreach (bool erase in new[] { false, true })
    foreach (float hardness in new[] { 0f, 1f })
    foreach (float size in new[] { 18f, 300f })
    {
        var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
        {
            brushColor = new Color(1f,0.25f,0.1f,0.5f), brushSize = size, brushHardness = hardness
        };
        try
        {
            Call(layer, "InitializeCanvas", 64, 64);
            var pixels = (Texture2D)layerType.GetField("pixels", Hidden).GetValue(layer);
            if (erase)
            {
                var data = pixels.GetRawTextureData<Color32>();
                for (int i = 0; i < data.Length; i++) data[i] = new Color32(255,255,255,255);
                pixels.Apply(); Call(layer, "InvalidatePaintSurface");
            }
            var parameters = Call(layer, "GetStrokeParameters", erase);
            parameters = Call(parameters, "WithCanvasWrap");
            Call(layer, "PaintPoint", new Vector2(3f,-2f), 64, 64, parameters);
            Call(layer, "SyncSurfaceToTexture");
            var actual = pixels.GetPixels32();
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = Mathf.Min(x+0.5f,64-x-0.5f), dy = Mathf.Min(y+0.5f,64-y-0.5f);
                float radius = new Vector2(dx,dy).magnitude * 2f / size;
                float t = Mathf.Clamp01((radius - Mathf.Min(hardness,0.9999f)) / (1f - Mathf.Min(hardness,0.9999f)));
                float alpha = 0.5f * (1f - t*t*(3f-2f*t));
                if (erase) alpha = 1f-alpha;
                if (Mathf.Abs(actual[y*64+x].a/255f-alpha) > 0.012f)
                    throw new Exception($"Wrapped corner coverage differs at {x},{y}, size={size}, hardness={hardness}, erase={erase}.");
            }
            Check(actual[0].a == actual[63].a && actual[0].a == actual[63*64].a && actual[0].a == actual[4095].a,
                "Corner footprint reaches all four corners with equal coverage");
            Check(RenderTexture.active == previousActive, "Wrapped brush restores the active render target");
        }
        finally { Call(layer, "ReleaseTransientResources"); }
    }

    foreach (var transform in transforms)
    {
        var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
        { transform = transform, brushColor = Color.white, brushSize = 12f, brushHardness = 0f };
        try
        {
            var parameters = Call(Call(layer, "GetStrokeParameters", false), "WithCanvasWrap");
            Vector2 source = ToSource(new Vector2(2.98f,-1.98f), transform, 128,64);
            Call(layer, "PaintPoint", source, 128,64,parameters);
            Call(layer, "SyncSurfaceToTexture");
            var pixels = (Texture2D)layerType.GetField("pixels", Hidden).GetValue(layer);
            var actual = pixels.GetPixels32();
            object[] basis = { transform,128,64,Vector2.zero,Vector2.zero }; MathCall("GetPeriodBasis",basis);
            Vector2 u = (Vector2)basis[3], v = (Vector2)basis[4];
            for (int y=0; y<64; y++)
            for (int x=0; x<128; x++)
            {
                Vector2 uv = new Vector2((x+0.5f)/128f,(y+0.5f)/64f);
                Vector2 doc = ToDocument(uv,transform,128,64);
                float expected = 0;
                if (doc.x>=0 && doc.x<1 && doc.y>=0 && doc.y<1)
                {
                    Vector2 delta = Vector2.Scale(uv-source,new Vector2(128,64));
                    Vector2 nearest = (Vector2)MathCall("NearestPeriodicDelta",delta,u,v);
                    float t = Mathf.Clamp01(nearest.magnitude/6f);
                    expected = 1f-t*t*(3f-2f*t);
                }
                if (Mathf.Abs(actual[y*128+x].a/255f-expected)>0.025f)
                    throw new Exception($"Transformed seam coverage differs at {x},{y}; scale={transform.scale}, rotation={transform.rotation}.");
            }
            Check(layer.transform.scale == transform.scale && layer.transform.position == transform.position && layer.transform.rotation == transform.rotation,
                "Wrapped painting preserves editable transforms");
        }
        finally { Call(layer,"ReleaseTransientResources"); }
    }

    var line = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushSize=3, brushHardness=1, brushColor=Color.white };
    try
    {
        var parameters = Call(Call(line,"GetStrokeParameters",false),"WithCanvasWrap");
        Call(line,"PaintSegment",new Vector2(0.95f,0.5f),new Vector2(1.05f,0.5f),64,64,true,parameters);
        Call(line,"SyncSurfaceToTexture");
        var pixels = (Texture2D)layerType.GetField("pixels",Hidden).GetValue(line);
        Check(pixels.GetPixel(0,32).a>0.9f && pixels.GetPixel(63,32).a>0.9f,"Continuous strokes cross the seam");
        Check(pixels.GetPixel(32,32).a==0,"A seam crossing does not draw a diagonal through the source tile");
    }
    finally { Call(line,"ReleaseTransientResources"); }
}
finally { RenderTexture.active = previousActive; }
return $"Tiled canvas checks passed: {checks}, including per-pixel GPU comparisons.";
