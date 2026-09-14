// Opt-in after manual compilation. Runs against the real GPU brush; no asset saves or imports.

var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var assembly = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).Assembly;
var settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
object Call(object target, string name, params object[] args) =>
    target.GetType().GetMethod(name, flags).Invoke(target, args);
void Set(object target, string name, object value)
{
    var field = target.GetType().GetField(name, flags);
    if (field != null) field.SetValue(target, value);
    else target.GetType().GetProperty(name, flags).SetValue(target, value);
}
object Get(object target, string name)
{
    var field = target.GetType().GetField(name, flags);
    return field != null ? field.GetValue(target) : target.GetType().GetProperty(name, flags).GetValue(target);
}
object Settings()
{
    var result = System.Activator.CreateInstance(settingsType, true);
    Set(result, "brushSize", 16f);
    Set(result, "brushHardness", 1f);
    Set(result, "brushSpacing", .25f);
    return result;
}
void Stroke(DCFApixels.WhimTex.DrawingLayerBehaviour layer, object settings, int stamps, bool erase = false)
{
    object parameters = Call(settings, "GetStrokeParameters", erase, (UnityEngine.Color?)UnityEngine.Color.white);
    var center = new UnityEngine.Vector2(.5f, .5f);
    Call(layer, "BeginStroke", center);
    try
    {
        for (int i = 0; i < stamps; i++) Call(layer, "PaintPoint", center, 32, 32, parameters);
        Call(layer, "SyncSurfaceToTexture");
    }
    finally { Call(layer, "EndStroke"); }
}
UnityEngine.Color Pixel(DCFApixels.WhimTex.DrawingLayerBehaviour layer, int x = 16, int y = 16) =>
    ((UnityEngine.Texture2D)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetProperty("StoredTexture", flags).GetValue(layer)).GetPixel(x, y);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Near(float actual, float expected, string message) =>
    Check(UnityEngine.Mathf.Abs(actual - expected) < .016f, message + ": " + actual + " != " + expected);
void Release(DCFApixels.WhimTex.DrawingLayerBehaviour layer) => Call(layer, "ReleaseTransientResources");

foreach (float opacity in new[] { 1f, .4f })
foreach (float flow in new[] { 1f, .1f })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    object settings = Settings(), dynamics = Get(settings, "dynamics");
    Set(dynamics, "opacity", opacity); Set(dynamics, "flow", flow);
    try
    {
        Stroke(layer, settings, 10);
        float alpha = opacity * (1f - UnityEngine.Mathf.Pow(1f - flow, 10));
        Near(Pixel(layer).a, alpha, "Opacity caps accumulated Flow");
        Check(Get(layer, "advancedStroke") == null && Get(layer, "advancedStrokeBase") == null, "Stroke buffers released");
        Stroke(layer, settings, 10);
        Near(Pixel(layer).a, 1f - (1f - alpha) * (1f - alpha), "Separate strokes build up");
        Set(dynamics, "flow", 1f); Set(dynamics, "opacity", .5f);
        float before = Pixel(layer).a;
        Stroke(layer, settings, 10, true);
        Near(Pixel(layer).a, before * .5f, "Eraser opacity applies once");
    }
    finally { Release(layer); }
}

float SurfaceRed(DCFApixels.WhimTex.DrawingLayerBehaviour layer)
{
    var previous = UnityEngine.RenderTexture.active;
    var readback = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBAFloat, false, true);
    try
    {
        UnityEngine.RenderTexture.active = (UnityEngine.RenderTexture)Get(layer, "paintSurface");
        readback.ReadPixels(new UnityEngine.Rect(0, 0, 32, 32), 0, 0, false);
        return readback.GetPixel(16, 16).r;
    }
    finally { UnityEngine.RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
}
foreach (string application in new[] { "Stroke", "Stamp" })
foreach (float opacity in new[] { 1f, .5f })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    object settings = Settings(), dynamics = Get(settings, "dynamics");
    try
    {
        Set(layer, "blendRange", System.Enum.Parse(Get(layer, "blendRange").GetType(), "HDR"));
        Stroke(layer, settings, 1);
        var tint = new UnityEngine.Gradient();
        var gray = new UnityEngine.Color(.5f, .5f, .5f, 1f);
        tint.SetKeys(new[] { new UnityEngine.GradientColorKey(gray, 0f), new UnityEngine.GradientColorKey(gray, 1f) },
            new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
        Set(dynamics, "tintGradient", tint);
        Set(dynamics, "blend", DCFApixels.WhimTex.BlendMode.Multiply);
        Set(dynamics, "blendApplication", System.Enum.Parse(Get(dynamics, "blendApplication").GetType(), application));
        Set(dynamics, "opacity", opacity);
        Stroke(layer, settings, 2);
        float linear = UnityEngine.Mathf.GammaToLinearSpace(.5f);
        Near(SurfaceRed(layer), UnityEngine.Mathf.Lerp(1f, application == "Stamp" ? linear * linear : linear, opacity),
            application + " blends overlapping stamps with whole-stroke opacity");
        Check(Get(layer, "advancedStroke") == null && Get(layer, "advancedStrokeBase") == null, "Blend stroke buffers released");
    }
    finally { Release(layer); }
}

// Real C# sampler checks after manual compilation (no rendering required for this block).
{
    object dynamics = Get(Settings(), "dynamics");
    Check((float)Get(dynamics, "scatterBias") == 0f, "Default scatter bias preserves uniform area distribution");
    var radiusMethod = dynamics.GetType().GetMethod("ScatterRadius", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    foreach (float bias in new[] { -1f, 0f, 1f })
    {
        Set(dynamics, "scatterBias", bias);
        float exponent = (float)Call(dynamics, "GetScatterExponent");
        float radius = (float)radiusMethod.Invoke(null, new object[] { .25f, exponent });
        Check(bias < 0f ? radius < .5f : bias > 0f ? radius > .5f : radius == .5f, "Scatter bias changes radial concentration");
    }
    Set(dynamics, "scatterBias", float.NaN);
    Call(dynamics, "Normalize");
    Check((float)Get(dynamics, "scatterBias") == 0f, "Nonfinite bias resets to default");
    Set(dynamics, "scatterBias", .75f);
    var algorithmType = dynamics.GetType().GetField("randomAlgorithm", flags).FieldType;
    Check(Get(dynamics, "randomAlgorithm").ToString() == "Random", "Default random algorithm");
    Set(dynamics, "randomAlgorithm", System.Enum.Parse(algorithmType, "Sobol"));
    foreach (int seed in new[] { 1, 123, int.MaxValue })
    {
        Set(dynamics, "seed", seed);
        for (int dimension = 0; dimension < 5; dimension++)
        {
            var bins = new System.Collections.Generic.HashSet<int>();
            for (uint index = 0; index < 256; index++)
            {
                object[] args = { 789u, index, dimension };
                float value = (float)Call(dynamics, "SampleRandom", args);
                Check(value >= 0f && value < 1f, "Sobol sample range");
                Check((uint)args[0] == 789u, "Sobol does not consume the Random state");
                Check(value == (float)Call(dynamics, "SampleRandom", args), "Sobol seeded repeatability");
                bins.Add((int)(value * 256f));
            }
            Check(bins.Count == 256, "Sobol fills every dyadic bin once");
        }
    }
    string json = UnityEngine.JsonUtility.ToJson(dynamics);
    object restored = System.Activator.CreateInstance(dynamics.GetType(), true);
    UnityEngine.JsonUtility.FromJsonOverwrite(json, restored);
    Check(Get(restored, "randomAlgorithm").ToString() == "Sobol", "Algorithm survives settings serialization");
    Check((float)Get(restored, "scatterBias") == .75f, "Scatter bias survives settings serialization");
}

{
    object settings = Settings(), dynamics = Get(settings, "dynamics");
    Set(settings, "brushColor", UnityEngine.Color.cyan);
    Set(settings, "brushHardness", .2f);
    Set(settings, "brushSpacing", 2f);
    Set(settings, "brushTipGuid", "stale-guid");
    Set(settings, "brushTipLocalId", 123L);
    Set(settings, "brushTipPresetPath", "old.sebrush");
    Set(dynamics, "tipChannel", System.Enum.Parse(Get(dynamics, "tipChannel").GetType(), "Color"));
    Set(dynamics, "tipSdf", true);
    Set(dynamics, "proceduralMode", System.Enum.Parse(Get(dynamics, "proceduralMode").GetType(), "SdfGradient"));
    var sdfGradient = new UnityEngine.Gradient();
    sdfGradient.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.red, 0f), new UnityEngine.GradientColorKey(UnityEngine.Color.blue, 1f) },
        new[] { new UnityEngine.GradientAlphaKey(0f, .2f), new UnityEngine.GradientAlphaKey(1f, .7f) });
    Set(dynamics, "tipGradient", sdfGradient);
    Set(dynamics, "randomAlgorithm", System.Enum.Parse(Get(dynamics, "randomAlgorithm").GetType(), "Sobol"));
    Set(dynamics, "opacity", .4f); Set(dynamics, "flow", .3f);
    Set(dynamics, "scatter", 2f); Set(dynamics, "scatterBias", .75f);
    Set(dynamics, "sizeJitter", .5f); Set(dynamics, "angleJitter", 90f);
    Set(dynamics, "angleOffset", -90f);
    var restoredOffset = UnityEngine.JsonUtility.FromJson(UnityEngine.JsonUtility.ToJson(dynamics), dynamics.GetType());
    Near((float)Get(restoredOffset, "angleOffset"), -90f, "Angle Offset survives serialization");
    Check(((UnityEngine.Gradient)Get(restoredOffset, "tipGradient")).Equals(sdfGradient), "SDF gradient survives serialization");
    Check(Get(restoredOffset, "proceduralMode").ToString() == "SdfGradient", "Procedural mode survives serialization");
    Set(dynamics, "rotationMode", System.Enum.Parse(Get(dynamics, "rotationMode").GetType(), "StrokeDirection"));
    Set(dynamics, "blend", DCFApixels.WhimTex.BlendMode.Multiply);
    Set(dynamics, "blendApplication", System.Enum.Parse(Get(dynamics, "blendApplication").GetType(), "Stamp"));
    Call(settings, "ResetBrushTip");
    Near((float)Get(settings, "brushHardness"), .8f, "Tip resets hardness");
    Check(Get(dynamics, "tip") == null && Get(dynamics, "tipChannel").ToString() == "Alpha", "Tip resets texture/channel");
    Check(!(bool)Get(dynamics, "tipSdf"), "Tip disables SDF");
    Check(Get(dynamics, "proceduralMode").ToString() == "Hardness", "Tip resets procedural mode");
    var resetSdf = (UnityEngine.Gradient)Get(dynamics, "tipGradient");
    Near(resetSdf.Evaluate(0f).a, 0f, "Tip resets SDF outside opacity");
    Near(resetSdf.Evaluate(.5f).a, .5f, "Tip resets SDF transition");
    Check(resetSdf.Evaluate(1f) == UnityEngine.Color.white, "Tip resets SDF inside color");
    Check((string)Get(settings, "brushTipGuid") == "", "Tip clears saved reference");
    Check((long)Get(settings, "brushTipLocalId") == 0L, "Tip clears saved subasset reference");
    Check((string)Get(settings, "brushTipPresetPath") == "", "Tip clears saved preset reference");
    Near((float)Get(dynamics, "scatter"), 2f, "Tip preserves Stamps");
    Check(Get(dynamics, "blend").ToString() == "Multiply", "Tip preserves Color");
    Call(settings, "ResetBrushStamps");
    Near((float)Get(settings, "brushSpacing"), 2f, "Stamps preserves spacing");
    foreach (string field in new[] { "scatter", "scatterBias", "sizeJitter", "angleJitter", "angleOffset" })
        Check((float)Get(dynamics, field) == 0f, "Stamps resets " + field);
    Check(Get(dynamics, "randomAlgorithm").ToString() == "Random", "Stamps resets randomization");
    Check(Get(dynamics, "rotationMode").ToString() == "Fixed", "Stamps resets rotation mode");
    Check(Get(dynamics, "blend").ToString() == "Multiply", "Stamps preserves Color");
    Call(settings, "ResetBrushColor");
    Check(Get(dynamics, "blendApplication").ToString() == "Stroke", "Color resets blend application");
    Check(Get(dynamics, "blend").ToString() == "Normal", "Color resets Blend");
    Check(!(bool)dynamics.GetType().GetProperty("HasTint", flags).GetValue(dynamics), "Color resets Tint");
    Near((float)Get(dynamics, "opacity"), .4f, "Section resets preserve Opacity");
    Near((float)Get(dynamics, "flow"), .3f, "Section resets preserve Flow");
    Check((UnityEngine.Color)Get(settings, "brushColor") == UnityEngine.Color.cyan, "Section resets preserve palette");
    Check((float)Get(settings, "brushSize") == 16f, "Section resets preserve size");
}

var tinted = new DCFApixels.WhimTex.DrawingLayerBehaviour();
try
{
    var settings = Settings();
    var dynamics = Get(settings, "dynamics");
    var gradient = new UnityEngine.Gradient();
    gradient.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.red, 0f), new UnityEngine.GradientColorKey(UnityEngine.Color.red, 1f) },
        new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
    Set(dynamics, "tintGradient", gradient);
    Stroke(tinted, settings, 1);
    Near(Pixel(tinted).a, 1f, "Dynamic attributes preserve alpha");
    Near(Pixel(tinted).r, 1f, "Tint red");
    Near(Pixel(tinted).g, 0f, "Identical gradient keys give constant red");
    object[] sampleArgs = { 123u, 0u };
    Call(dynamics, "SampleTint", sampleArgs);
    Check((uint)sampleArgs[0] == 123u, "Constant gradient does not consume random samples");
    gradient.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.red, 0f), new UnityEngine.GradientColorKey(UnityEngine.Color.blue, 1f) },
        new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
    Call(dynamics, "PrepareTint");
    Call(dynamics, "SampleTint", sampleArgs);
    Check((uint)sampleArgs[0] != 123u, "Different color keys enable random sampling");
    gradient.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.white, 0f), new UnityEngine.GradientColorKey(UnityEngine.Color.white, 1f) },
        new[] { new UnityEngine.GradientAlphaKey(0f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
    Call(dynamics, "PrepareTint");
    uint previousState = (uint)sampleArgs[0];
    Call(dynamics, "SampleTint", sampleArgs);
    Check((uint)sampleArgs[0] != previousState, "Different alpha keys enable random sampling");
    Call(dynamics, "ResetTint");
    Check(!(bool)dynamics.GetType().GetProperty("HasTint", flags).GetValue(dynamics), "White reset restores neutral fast path");
    Stroke(tinted, settings, 1);
    Near(Pixel(tinted).g, 1f, "Reset gradient leaves palette color unchanged");
}
finally { Release(tinted); }

var tip = new UnityEngine.Texture2D(4, 4, UnityEngine.TextureFormat.RGBA32, false, true);
tip.filterMode = UnityEngine.FilterMode.Point;
var pixels = new UnityEngine.Color[16];
for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) pixels[y * 4 + x] = x < 2 ? UnityEngine.Color.red : UnityEngine.Color.clear;
tip.SetPixels(pixels); tip.Apply(false, false);
try
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    object settings = Settings(), dynamics = Get(settings, "dynamics");
    Set(dynamics, "tip", tip);
    Set(dynamics, "tipChannel", System.Enum.Parse(Get(dynamics, "tipChannel").GetType(), "Color"));
    try
    {
        Stroke(layer, settings, 1);
        Near(Pixel(layer, 12).r, 1f, "Color tip red");
        Near(Pixel(layer, 12).g, 0f, "Color tip tint");
        Near(Pixel(layer, 20).a, 0f, "Tip alpha cuts footprint");
        var rotated = new DCFApixels.WhimTex.DrawingLayerBehaviour();
        try
        {
            Set(dynamics, "angleJitter", 180f);
            Set(dynamics, "seed", 1);
            Stroke(rotated, settings, 1);
            Near(Pixel(rotated, 12).a, 0f, "Rotated texture empty side");
            Near(Pixel(rotated, 20).r, 1f, "Seeded near-180-degree texture rotation");
            Near(Pixel(rotated, 20).a, 1f, "Rotated texture visible side");
        }
        finally { Release(rotated); }
        Set(dynamics, "tip", null);
        Set(dynamics, "blend", System.Enum.Parse(Get(dynamics, "blend").GetType(), "Multiply"));
        Stroke(layer, settings, 1);
        Near(Pixel(layer, 12).r, 1f, "White Multiply preserves red");
        Near(Pixel(layer, 12).g, 0f, "White Multiply does not replace red");
    }
    finally { Release(layer); }
}
finally { UnityEngine.Object.DestroyImmediate(tip); }

// Exercise actual C# spacing with different pointer event densities.
var spacingType = assembly.GetType("DCFApixels.WhimTex.BrushSpacingState", true);
int Samples(double[] lengths)
{
    object state = System.Activator.CreateInstance(spacingType);
    int total = 0;
    for (int i = 0; i < lengths.Length; i++)
    {
        object[] args = { lengths[i], 4d, i == 0, 0d };
        total += (int)spacingType.GetMethod("Sample", flags).Invoke(state, args);
    }
    return total;
}
Check(Samples(new[] { 20d }) == 6, "Uniform stamp spacing");
Check(Samples(new[] { 0d, 1d, 2d, 3d, 5d, 9d }) == 6, "Spacing survives pointer segmentation");
return "Brush dynamics checks passed: " + checks;
