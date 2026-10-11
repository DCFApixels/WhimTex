using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Package-owned reflection only. No windows, assets, prefs, scene or project changes.
public static class BrushInputProtectionTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(DrawingLayerBehaviour).Assembly;
    static object New(string name) => Activator.CreateInstance(Product.GetType("DCFApixels.WhimTex." + name, true), true);
    static object Call(object owner, string name, params object[] args) =>
        (owner as Type ?? owner.GetType()).GetMethod(name, Flags).Invoke(owner is Type ? null : owner, args);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags)?.GetValue(owner)
        ?? owner.GetType().GetProperty(name, Flags)?.GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);

    public static string Smoothing() => TestContext.Run("Brush input smoothing", t =>
    {
        object options = New("StrokeSmoothingSettings");
        var modeType = options.GetType().GetField("mode").FieldType;
        void Mode(string name) => Set(options, "mode", Enum.Parse(modeType, name));
        object Filter() { var s = New("StrokeSmoother"); Call(s, "Begin", Vector2.zero, .2f); return s; }
        void Move(object s, Vector2 point, float pressure = .8f, bool exact = false) => Call(s, "Move", point, pressure, options, exact);
        Vector2 Position(object s) => (Vector2)Get(s, "Position");
        t.Equal("None", Get(options, "mode").ToString(), "Smoothing starts disabled");
        t.True(!(bool)Get(options, "smoothPressure"), "Pressure filter starts disabled");
        var none = Filter(); Move(none, new Vector2(40, 8));
        t.Equal(new Vector2(40, 8), Position(none), "None follows pointer exactly");
        t.Near(.8, (float)Get(none, "Pressure"), .00001, "Unfiltered pressure passes through");
        Mode("Smooth"); Set(options, "distance", 12f);
        var whole = Filter(); Move(whole, new Vector2(120, 0));
        var pieces = Filter();
        for (int i = 1; i <= 120; i++) Move(pieces, new Vector2(i, 0));
        t.Near(Position(whole).x, Position(pieces).x, .001, "Collinear subdivision preserves filtered endpoint");
        t.True(Position(whole).x > 105 && Position(whole).x < 120, "Smooth follows with a bounded lag");
        var jitter = Filter(); float sum = 0;
        for (int i = 1; i <= 80; i++) { Move(jitter, new Vector2(i, i % 2 == 0 ? 1 : -1)); sum += Mathf.Abs(Position(jitter).y); }
        t.True(sum / 80 < .3f, "Smooth attenuates alternating one-pixel jitter");
        Move(jitter, new Vector2(81, 12), exact: true);
        t.Equal(new Vector2(81, 12), Position(jitter), "Exact lines/snapping bypass path filter");
        Mode("Stabilizer"); var rope = Filter();
        Move(rope, new Vector2(6, 0)); t.Equal(Vector2.zero, Position(rope), "Rope absorbs motion inside its radius");
        Move(rope, new Vector2(40, 0)); t.Equal(new Vector2(28, 0), Position(rope), "Rope follows at chosen distance");
        Call(rope, "Finish", false); t.Equal(new Vector2(28, 0), Position(rope), "No finish preserves lagged endpoint");
        Call(rope, "Finish", true); t.Equal(new Vector2(40, 0), Position(rope), "Finish reaches pointer");
        Mode("None"); Set(options, "smoothPressure", true); var independent = Filter();
        Move(independent, new Vector2(.25f, 0), .2f);
        Move(independent, new Vector2(.5f, 0), .2f);
        Move(independent, new Vector2(1, 0), 1);
        t.Equal(new Vector2(1, 0), Position(independent), "Pressure filtering does not require path filtering");
        t.True((float)Get(independent, "Pressure") > .2f && (float)Get(independent, "Pressure") < .6f, "History-weighted pressure spike is attenuated");
        float afterSpike = (float)Get(independent, "Pressure");
        Move(independent, new Vector2(1, 0), 0f);
        t.True((float)Get(independent, "Pressure") < afterSpike, "Stationary pressure updates are not frozen");
        Move(independent, new Vector2(2, 0), .7f, true); t.Near(.7, (float)Get(independent, "Pressure"), .00001, "Exact mode preserves explicit pressure");
        Call(independent, "Begin", new Vector2(3, 7), .4f);
        t.Equal(new Vector2(3, 7), Position(independent), "New stroke resets position");
        t.Near(.4, (float)Get(independent, "Pressure"), .00001, "New stroke resets pressure");
        var input = Product.GetType("DCFApixels.WhimTex.BrushPressureInput", true);
        t.Near(1, (float)Call(input, "Read", "mouse", .5f), .00001, "Mouse input is full pressure, not synthetic button pressure");
        t.Near(0, (float)Call(input, "Read", "pen", 0f), .00001, "Zero pen pressure stays zero");
        t.Near(.0001, (float)Call(input, "Read", "pen", .0001f), .000001, "Small pen pressure is not replaced by full pressure");
        t.Near(.45, (float)Call(input, "Read", "pen", .45f), .00001, "Pen pressure passes through");
        t.Near(0, (float)Call(input, "Read", "pen", float.NaN), .00001, "Invalid pen samples cannot become full pressure");
        float PreviewPressure(float at) => (float)Call(typeof(DrawingLayerBehaviour), "BrushPreviewPressure", at);
        t.Near(0, PreviewPressure(0f), 0, "Preview starts with zero pressure");
        t.Near(0, PreviewPressure(1f), 0, "Preview ends with zero pressure");
        t.Near(1, PreviewPressure(.5f), 0, "Preview reaches full pressure in the middle");
        t.True(PreviewPressure(.05f) < PreviewPressure(.1f) && PreviewPressure(.1f) < PreviewPressure(.2f),
            "Preview pressure rises smoothly");
        t.Near(PreviewPressure(.1f), PreviewPressure(.9f), .000001, "Preview pressure has symmetric ends");
        t.True(PreviewPressure(.2f) < 1f && PreviewPressure(.8f) < 1f,
            "Preview weak-pressure ends extend beyond one fifth of the stroke");
        t.Near(1, PreviewPressure(.3f), .000001, "Preview start reaches full pressure after the wider ramp");
        t.Near(1, PreviewPressure(.7f), .000001, "Preview end retains full pressure before the wider ramp");
        Vector2 PreviewPoint(float at) => (Vector2)Call(typeof(DrawingLayerBehaviour), "BrushPreviewPoint", at, 256, 96, 1f);
        Vector2 firstStep = PreviewPoint(.04f) - PreviewPoint(0f);
        Vector2 lastStep = PreviewPoint(1f) - PreviewPoint(.96f);
        t.Near(0, (firstStep - (PreviewPoint(.08f) - PreviewPoint(.04f))).magnitude, .000001,
            "Preview start continues in a straight direction");
        t.Near(0, (lastStep - (PreviewPoint(.96f) - PreviewPoint(.92f))).magnitude, .000001,
            "Preview end continues in a straight direction");
        const float join = .12f / 1.24f;
        Vector2 startTangent = PreviewPoint(join + .002f) - PreviewPoint(join);
        Vector2 endTangent = PreviewPoint(1f - join) - PreviewPoint(1f - join - .002f);
        t.True(Vector2.Dot(firstStep.normalized, startTangent.normalized) > .9999f,
            "Preview start tail joins the curve tangentially");
        t.True(Vector2.Dot(lastStep.normalized, endTangent.normalized) > .9999f,
            "Preview end tail joins the curve tangentially");
        Set(options, "distance", float.NaN); Call(options, "Normalize"); t.Equal(12f, Get(options, "distance"), "Invalid distance normalizes");
        var restored = JsonUtility.FromJson(JsonUtility.ToJson(options), options.GetType());
        t.True((bool)Get(restored, "smoothPressure"), "Pressure preference roundtrips");
        foreach (bool protectedValues in new[] { false, true })
        {
            string values = protectedValues ? "\"writeChannels\":5" : "";
            string json = "{\"format\":\"whimtex.document\",\"version\":" + WhimTexDocumentJson.Version + ",\"layers\":[{\"id\":\"ink\",\"behaviour\":{\"$type\":\"DrawingLayerBehaviour\",\"brushDynamics\":{" + values + "}}}]}";
            using var read = WhimTexDocumentJson.Read(json, false);
            object dynamics = Get(read.Document.layers[0].Behaviour, "brushDynamics");
            t.Equal(protectedValues ? 5 : 15, Get(dynamics, "writeChannels"), "Sparse document reads correct write defaults");
            t.True((bool)Get(Get(dynamics, "pressureFlow"), "enabled"), "Sparse document enables pressure Flow by default");
            t.True(!(bool)Get(Get(dynamics, "pressureSize"), "enabled"), "Sparse document leaves pressure Size disabled");
            Set(Get(dynamics, "pressureOpacity"), "enabled", true);
            Set(Get(dynamics, "pressureOpacity"), "curve", AnimationCurve.Linear(0, .3f, 1, .7f));
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string written = WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode, AllowDrawingOmission = true }).Json;
                using var reopened = WhimTexDocumentJson.Read(written, false);
                var reopenedDynamics = Get(reopened.Document.layers[0].Behaviour, "brushDynamics");
                t.Equal(protectedValues ? 5 : 15, Get(reopenedDynamics, "writeChannels"), "Write mask document roundtrip " + mode);
                t.True((bool)Get(Get(reopenedDynamics, "pressureOpacity"), "enabled"), "Pressure binding document roundtrip " + mode);
                t.Near(.5, (float)Call(Get(reopenedDynamics, "pressureOpacity"), "Evaluate", .5f), .00001,
                    "Pressure response curve document roundtrip " + mode);
            }
        }
    });

    public static string Protection() => TestContext.Run("Real GPU brush write protection", t =>
    {
        RenderTexture previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var center = new Vector2(.5f, .5f);
        DrawingLayerBehaviour Layer(Color value)
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBAHalf, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            try
            {
                var pixels = new Color[32 * 32]; Array.Fill(pixels, value); texture.SetPixels(pixels); texture.Apply();
                var layer = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                Call(layer, "AdoptStoredTexture", texture); return layer;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }
        Color Pixel(DrawingLayerBehaviour layer, int x = 16) => ((Texture2D)Get(layer, "StoredTexture")).GetPixel(x, 16);
        object Settings(int mask)
        {
            object settings = New("PaintToolSettings");
            Set(settings, "brushSize", 16f); Set(settings, "brushHardness", 1f);
            Set(settings, "brushColor", new Color(.8f, .9f, .6f, 1));
            Set(Get(settings, "dynamics"), "writeChannels", mask);
            return settings;
        }
        void Near(Color expected, Color actual, string label)
        {
            for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .005, label + " channel " + c);
        }
        void Paint(DrawingLayerBehaviour layer, object settings, bool erase = false, bool pencil = false, Texture selection = null, bool wrap = false)
        {
            object parameters = Call(settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", erase, (Color?)null);
            if (selection != null) parameters = Call(parameters, "WithSelectionMask", selection);
            if (wrap) parameters = Call(parameters, "WithCanvasWrap");
            Call(layer, "BeginStroke", center);
            try { Call(layer, "PaintPoint", center, 32, 32, parameters); Call(layer, "SyncSurfaceToTexture"); }
            finally { Call(layer, "EndStroke"); }
            t.True(Get(layer, "paintWriteBefore") == null && Get(layer, "paintWriteScratch") == null, "Protection buffers released");
        }
        var before = new Color(-.25f, 2, .15f, .4f);
        try
        {
            GL.sRGBWrite = false;
            for (int mask = 0; mask <= 15; mask++)
            {
                var layer = Layer(before); var control = Layer(before);
                try
                {
                    var settings = Settings(mask);
                    Paint(control, Settings(15)); Color candidate = Pixel(control);
                    Color expected = (Color)Call(typeof(DrawingLayerBehaviour), "ProtectPaintColor", before, candidate, mask);
                    Paint(layer, settings); Near(expected, Pixel(layer), "Mask " + mask);
                    Near(before, Pixel(layer, 0), "Pixels outside brush remain unchanged");
                }
                finally { Call(layer, "ReleaseTransientResources"); Call(control, "ReleaseTransientResources"); }
            }
            foreach (bool pencil in new[] { false, true })
            foreach (bool erase in new[] { false, true })
            {
                var layer = Layer(before);
                try
                {
                    Paint(layer, Settings(7), erase, pencil, wrap: true);
                    t.Near(before.a, Pixel(layer).a, .001, "Write A off preserves alpha for wrapped brush/pencil/eraser");
                    if (erase) Near(before, Pixel(layer), "Alpha-protected erasing does not darken colors");
                }
                finally { Call(layer, "ReleaseTransientResources"); }
            }
            foreach (int mask in new[] { 7, 8, 15 })
            {
                var transparent = Layer(new Color(-.25f, 2, .15f, 0));
                try
                {
                    Paint(transparent, Settings(mask));
                    Color result = Pixel(transparent);
                    if (mask == 8) { Near(new Color(-.25f, 2, .15f, result.a), result, "Alpha-only write preserves hidden straight RGB"); t.True(result.a > .9, "Alpha-only actually paints"); }
                    else if (mask == 7) { t.Near(0, result.a, 0, "Protected transparent pixel stays transparent"); t.Near(2, result.g, .005, "Hidden RGB survives alpha-protected paint"); }
                    else { t.True(result.a > .9, "Write A on paints transparent pixels"); t.True(result.g < 1, "Write RGB on replaces hidden color"); }
                }
                finally { Call(transparent, "ReleaseTransientResources"); }
            }
            foreach (string tool in new[] { "Blur", "Smudge" })
            foreach (float mixing in new[] { 0f, .5f, 1f })
            {
                var layer = Layer(before);
                try
                {
                    var texture = (Texture2D)Get(layer, "StoredTexture");
                    var input = texture.GetPixels(); for (int y = 0; y < 32; y++) for (int x = 0; x < 14; x++) input[y * 32 + x] = new Color(.8f, .9f, .6f, 1);
                    texture.SetPixels(input); texture.Apply();
                    Call(layer, "EnsurePaintSurface", 32, 32); Call(layer, "BeginStroke", new Vector2(.3f, .5f));
                    Call(layer, "ConfigureStrokeWriteProtection", 1);
                    if (tool == "Smudge") { Call(layer, "BeginSmudgeStroke", new Vector2(.3f, .5f), 32, 32, 12f, null, false, mixing); Call(layer, "SmudgeSegment", new Vector2(.3f, .5f), new Vector2(.7f, .5f), 32, 32, .8f, 1f, 1f, null); }
                    else Call(layer, "BlurSegment", center, center, 32, 32, 16f, .8f, 1f, null, false);
                    Call(layer, "SyncSurfaceToTexture"); var actual = ((Texture2D)Get(layer, "StoredTexture")).GetPixels();
                    double changed = 0;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        changed += Math.Abs(input[i].r - actual[i].r);
                        for (int c = 1; c < 4; c++) t.Near(input[i][c], actual[i][c], .005, tool + " protects G/B/A at pixel " + i);
                    }
                    t.True(changed > .1, tool + " actually changes enabled R");
                    Call(layer, "EndStroke"); t.True(Get(layer, "paintWriteBefore") == null, tool + " releases protection snapshot");
                }
                finally { Call(layer, "ReleaseTransientResources"); }
            }
            var defaults = Get(New("PaintToolSettings"), "dynamics");
            t.Equal(15, Get(defaults, "writeChannels"), "All channels enabled by default");
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(Get(Settings(5), "dynamics")), defaults.GetType());
            t.Equal(5, Get(restored, "writeChannels"), "Write mask roundtrips");
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Caller render state restored");
    });
}
