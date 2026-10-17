using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Transient GPU fixtures only; reflection accesses package-owned types.
public static class BrushBufferedStrokeTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Size = 32;
    static readonly Vector2 Center = new Vector2(.5f, .5f);
    static bool forceRaster, requireCompute;
    static RenderTexture EnsureSurface(DrawingLayerBehaviour layer, int width, int height)
    {
        var surface = (RenderTexture)Call(layer, "EnsurePaintSurface", width, height);
        if (forceRaster && surface.enableRandomWrite)
        {
            var previous = RenderTexture.active;
            var descriptor = surface.descriptor; descriptor.enableRandomWrite = false;
            var raster = new RenderTexture(descriptor) { filterMode = surface.filterMode, wrapMode = surface.wrapMode, hideFlags = HideFlags.HideAndDontSave };
            raster.Create(); Graphics.Blit(surface, raster);
            Set(layer, "paintSurface", raster);
            RenderTexture.active = previous == surface ? raster : previous;
            Object.DestroyImmediate(surface); surface = raster;
        }
        if (requireCompute && !surface.enableRandomWrite) throw new InvalidOperationException("Compute path was not enabled.");
        return surface;
    }
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, F).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, F)?.GetValue(target) ?? target.GetType().GetProperty(name, F)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    static object New(string type) => Activator.CreateInstance(typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + type), true);
    static Color Premul(Color c) => new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
    static void Near(TestContext t, Color expected, Color actual, string label, float tolerance = .006f)
    {
        for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], tolerance * Mathf.Max(1, Mathf.Abs(expected[c])), label + " RGBA[" + c + "]");
    }
    static Texture2D Texture(Color color, bool hdr = true)
    {
        var texture = new Texture2D(Size, Size, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var data = new Color[Size * Size]; Array.Fill(data, color); texture.SetPixels(data); texture.Apply(); return texture;
    }
    sealed class Fixture : IDisposable
    {
        public readonly DrawingLayerBehaviour Layer;
        public readonly object Settings, Dynamics;
        public Fixture(Color before, bool hdr = true)
        {
            Layer = new DrawingLayerBehaviour { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard, blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
            Call(Layer, "AdoptStoredTexture", Texture(before, hdr));
            EnsureSurface(Layer, Size, Size);
            Settings = New("PaintToolSettings"); Dynamics = Get(Settings, "dynamics");
            Set(Settings, "brushSize", 24f); Set(Settings, "brushHardness", 1f); Set(Settings, "brushSpacing", .25f);
            Set(Settings, "brushColor", new Color(.7f, .8f, .3f, .8f));
            Set(Dynamics, "flow", .3f);
        }
        public void Begin() => Call(Layer, "BeginStroke", Center);
        public object Parameters(Texture selection = null, bool erase = false, bool pencil = false)
        {
            var p = Call(Settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", erase, (Color?)null);
            return selection == null ? p : Call(p, "WithSelectionMask", selection);
        }
        public void Dabs(object parameters, int count = 6)
        { for (int i = 0; i < count; i++) Call(Layer, "PaintPoint", Center, Size, Size, parameters); }
        public Color[] Live()
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try { Call(Layer, "BlitStraightSurface", rt, 1f); return Read(rt); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }
        public Color[] Base() => Read((RenderTexture)Get(Layer, "paintSurface"));
        public void Dispose()
        {
            Call(Layer, "ReleaseTransientResources");
            var texture = (Texture2D)Get(Layer, "StoredTexture");
            if (texture != null) Object.DestroyImmediate(texture);
        }
    }
    static Color[] Read(RenderTexture rt)
    {
        var old = RenderTexture.active;
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false); return image.GetPixels(); }
        finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
    }
    static int Probe => 16 * Size + 16;
    public static string Run() => RunCore(false, false);
    public static string RunCompute() => RunCore(false, true);
    public static string RunRaster() => RunCore(true, false);
    static string RunCore(bool raster, bool compute) => TestContext.Run("Buffered brush: " + (raster ? "raster fallback" : compute ? "compute commit" : "automatic commit"), t =>
    {
        var old = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D mask = null;
        try
        {
            forceRaster = raster; requireCompute = compute;
            if (compute)
            {
                var helper = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.BrushStrokeCompute");
                t.True((bool)helper.GetProperty("Supported", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Compute commit is supported; no silent fallback");
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/BrushCommit.compute");
                foreach (var message in ShaderUtil.GetComputeShaderMessages(shader))
                    t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            }
            GL.sRGBWrite = false;
            foreach (string name in new[] { "Blend", "AlphaConversion", "PaintBrush" })
            {
                var shader = Shader.Find("Hidden/WhimTex/" + name);
                t.True(shader != null && shader.isSupported, "Shader supported: " + name);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, name + ": " + message.message);
            }
            mask = Texture(new Color(.25f, 0, 0, 1));
            SelectionAndLifetime(t, mask);
            BlendMatrix(t, mask);
            AnalyticBlend(t);
            PressureAndErase(t, mask);
            SpatialSelection(t);
            TransparentChannelProtection(t);
            RegionAssembly(t);
            TiledRegionAssembly(t);
            DocumentConsumers(t);
            RenderState(t, mask);
        }
        finally { forceRaster = requireCompute = false; if (mask != null) Object.DestroyImmediate(mask); RenderTexture.active = old; GL.sRGBWrite = srgb; }
    });
    static void SelectionAndLifetime(TestContext t, Texture mask)
    {
        using var f = new Fixture(Color.clear);
        Set(f.Settings, "brushColor", Color.white); Set(f.Dynamics, "flow", 1f);
        var original = f.Base(); f.Begin(); var p = f.Parameters(mask); f.Dabs(p, 1);
        Near(t, new Color(1, 1, 1, .25f), f.Live()[Probe], "First dab is clipped once");
        f.Dabs(p, 25); var live = f.Live();
        Near(t, new Color(1, 1, 1, .25f), live[Probe], "Repeated dabs retain soft selection coverage");
        var source = f.Base();
        for (int i = 0; i < source.Length; i++) Near(t, original[i], source[i], "Paint source remains immutable", 0);
        t.True(Get(f.Layer, "advancedStroke") != null, "Simple brush uses stroke buffer");
        t.True(Get(f.Layer, "paintWriteBefore") == null && Get(f.Layer, "paintWriteScratch") == null, "No write-protection copies for buffered brush");
        t.True(typeof(DrawingLayerBehaviour).GetField("advancedStrokeBase", F) == null, "No separate stroke base allocation");
        Call(f.Layer, "CancelStroke");
        Near(t, Color.clear, f.Live()[Probe], "Cancel discards the pending stroke");
        f.Begin(); f.Dabs(p); live = f.Live(); Call(f.Layer, "EndStroke");
        Near(t, Premul(live[Probe]), f.Base()[Probe], "Commit agrees with live result");
        t.True(Get(f.Layer, "advancedStroke") == null, "Commit releases stroke buffer");
        Call(f.Layer, "SyncSurfaceToTexture");
        Near(t, live[Probe], ((Texture2D)Get(f.Layer, "StoredTexture")).GetPixel(16, 16), "Stored HDR pixels agree with preview");
        f.Begin(); f.Dabs(p); Call(f.Layer, "SyncSurfaceToTexture");
        t.Near(.4375, ((Texture2D)Get(f.Layer, "StoredTexture")).GetPixel(16, 16).a, .003, "Independent strokes still build coverage");
        Call(f.Layer, "EndStroke");
    }
    static void BlendMatrix(TestContext t, Texture mask)
    {
        foreach (bool hdr in new[] { false, true })
        foreach (BlendMode mode in Enum.GetValues(typeof(BlendMode)))
        foreach (string application in new[] { "Stroke", "Stamp" })
        {
            Color before = hdr ? new Color(-.12f, 1.8f, .3f, .4f) : new Color(.15f, .5f, .7f, .6f);
            using var masked = new Fixture(before, hdr); using var full = new Fixture(before, hdr);
            foreach (var f in new[] { masked, full })
            {
                Set(f.Dynamics, "blend", mode); Set(f.Dynamics, "opacity", .6f);
                Set(f.Dynamics, "blendApplication", Enum.Parse(Get(f.Dynamics, "blendApplication").GetType(), application));
                if (hdr) Set(f.Settings, "brushColor", new Color(.7f, 1.2f, -.1f, .8f));
                f.Begin();
            }
            full.Dabs(full.Parameters()); masked.Dabs(masked.Parameters(mask));
            Color expected = Color.LerpUnclamped(masked.Base()[Probe], Premul(full.Live()[Probe]), .25f);
            var live = masked.Live(); string label = mode + " " + application + " HDR=" + hdr;
            Near(t, expected, Premul(live[Probe]), label + " selection clips final change");
            Call(masked.Layer, "EndStroke");
            Near(t, Premul(live[Probe]), masked.Base()[Probe], label + " commit matches live");
            Call(masked.Layer, "SyncSurfaceToTexture");
            Color stored = ((Texture2D)Get(masked.Layer, "StoredTexture")).GetPixel(16, 16);
            if (hdr) Near(t, live[Probe], stored, label + " signed HDR storage");
            else
            {
                t.True(stored.r >= 0 && stored.r <= 1 && stored.g >= 0 && stored.g <= 1 && stored.b >= 0 && stored.b <= 1, "Standard storage stays bounded");
                Near(t, live[Probe], stored.linear, label + " 8-bit storage agrees after sRGB decoding", .012f);
            }
        }
    }
    static void AnalyticBlend(TestContext t)
    {
        foreach (BlendMode mode in new[] { BlendMode.Normal, BlendMode.Multiply, BlendMode.Add, BlendMode.Screen })
        foreach (string application in new[] { "Stroke", "Stamp" })
        foreach (float opacity in new[] { 1f, .4f })
        {
            Color before = new Color(.2f, .4f, .7f, .35f);
            using var f = new Fixture(before); Set(f.Settings, "brushColor", new Color(.5f, .5f, .5f, 1));
            Set(f.Dynamics, "flow", .3f); Set(f.Dynamics, "opacity", opacity); Set(f.Dynamics, "blend", mode);
            Set(f.Dynamics, "blendApplication", Enum.Parse(Get(f.Dynamics, "blendApplication").GetType(), application));
            f.Begin(); f.Dabs(f.Parameters(), 4);
            float gray = Mathf.GammaToLinearSpace(.5f);
            Color expected = Premul(before), source = new Color(gray, gray, gray, 1);
            if (application == "Stamp" && mode != BlendMode.Normal)
            {
                for (int i = 0; i < 4; i++) expected = Over(expected, source, .3f, mode);
                expected = Color.LerpUnclamped(Premul(before), expected, opacity);
            }
            else expected = Over(expected, source, opacity * (1 - Mathf.Pow(.7f, 4)), mode);
            Near(t, expected, Premul(f.Live()[Probe]), "Independent CPU blend " + mode + " " + application + " " + opacity);
            Call(f.Layer, "EndStroke");
            Near(t, expected, f.Base()[Probe], "Independent CPU blend after commit " + mode + " " + application);
        }
    }
    static Color Over(Color before, Color source, float alpha, BlendMode mode)
    {
        Color result = Color.clear; result.a = alpha + before.a * (1 - alpha);
        for (int c = 0; c < 3; c++)
        {
            float b = before.a > 0 ? before[c] / before.a : 0, s = source[c];
            float mixed = mode == BlendMode.Multiply ? b * s : mode == BlendMode.Add ? b + s : mode == BlendMode.Screen ? 1 - (1 - b) * (1 - s) : s;
            result[c] = alpha * (1 - before.a) * s + alpha * before.a * mixed + (1 - alpha) * before[c];
        }
        return result;
    }
    static void PressureAndErase(TestContext t, Texture mask)
    {
        foreach (bool erase in new[] { false, true })
        foreach (bool pressure in new[] { false, true })
        foreach (float flow in new[] { 1f, .15f })
        {
            Color before = erase ? new Color(2, -.1f, .4f, .8f) : Color.clear;
            using var f = new Fixture(before); Set(f.Settings, "brushColor", Color.white);
            Set(f.Dynamics, "flow", flow); Set(f.Dynamics, "opacity", .4f); Set(f.Dynamics, "pressure", pressure);
            Set(Get(f.Dynamics, "pressureFlow"), "enabled", false);
            Set(Get(f.Dynamics, "pressureOpacity"), "enabled", true);
            Set(Get(f.Dynamics, "pressureOpacity"), "curve", AnimationCurve.Linear(0, 0, 1, 1));
            f.Begin(); var p = Call(f.Parameters(mask, erase), "WithPressure", .5f); f.Dabs(p, 8);
            float amount = .25f * .4f * (pressure ? .5f : 1f) * (1 - Mathf.Pow(1 - flow, 8));
            float expected = erase ? before.a * (1 - amount) : amount;
            t.Near(expected, f.Live()[Probe].a, .004, "Flow/Opacity/pressure/selection erase=" + erase);
            var live = f.Live(); Call(f.Layer, "EndStroke");
            Near(t, Premul(live[Probe]), f.Base()[Probe], "Pressure/eraser commit agrees with preview");
        }
    }
    static void SpatialSelection(TestContext t)
    {
        var mask = Texture(Color.clear);
        try
        {
            var values = new Color[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                values[y * Size + x] = new Color(((x / 4 + 2 * (y / 4)) % 5) * .25f, 0, 0, 1);
            mask.SetPixels(values); mask.Apply();
            foreach (bool wrap in new[] { false, true })
            foreach (var matrix in new[] { ProjectiveMatrix.Identity, ProjectiveMatrix.Translate(.4, -.25),
                new ProjectiveMatrix { m00 = .8, m01 = -.3, m02 = .27, m10 = .3, m11 = 1.1, m12 = -.17, m20 = .2, m21 = -.1, m22 = 1 } })
            {
                using var masked = new Fixture(new Color(1.4f, -.2f, .5f, .3f));
                using var full = new Fixture(new Color(1.4f, -.2f, .5f, .3f));
                foreach (var f in new[] { masked, full })
                {
                    var transform = TextureTransform.Default;
                    t.True(transform.TrySetMatrix(matrix), "Valid selection test transform");
                    f.Layer.Owner.transform = transform;
                    Set(f.Settings, "brushSize", 128f); Set(f.Dynamics, "flow", 1f);
                    f.Begin(); var p = f.Parameters(f == masked ? mask : null);
                    if (wrap) p = Call(p, "WithCanvasWrap");
                    f.Dabs(p, 8);
                }
                var before = masked.Base(); var selected = masked.Live(); var unmasked = full.Live();
                int checkedPixels = 0;
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    var uv = matrix.Point(new Double2((x + .5) / Size, (y + .5) / Size));
                    if (wrap) { uv.x -= Math.Floor(uv.x); uv.y -= Math.Floor(uv.y); }
                    float coverage = 0;
                    if (uv.x >= 0 && uv.x < 1 && uv.y >= 0 && uv.y < 1)
                    {
                        // Do not rely on either side of a float-rounded point-sampling boundary.
                        if (Math.Abs(uv.x * Size - Math.Round(uv.x * Size)) < .0001 || Math.Abs(uv.y * Size - Math.Round(uv.y * Size)) < .0001) continue;
                        coverage = values[(int)(uv.y * Size) * Size + (int)(uv.x * Size)].r;
                    }
                    int i = y * Size + x;
                    Near(t, Color.LerpUnclamped(before[i], Premul(unmasked[i]), coverage), Premul(selected[i]), "Canvas-space selection, wrap=" + wrap);
                    checkedPixels++;
                }
                t.True(checkedPixels > 800, "Spatial mask checks cover the source");
                Call(masked.Layer, "EndStroke"); var committed = masked.Base();
                for (int i = 0; i < committed.Length; i++) Near(t, Premul(selected[i]), committed[i], "Spatial mask commit " + i, .002f);
            }
        }
        finally { Object.DestroyImmediate(mask); }
    }
    static void TransparentChannelProtection(TestContext t)
    {
        foreach (bool hdr in new[] { false, true })
        foreach (float alpha in new[] { 0f, .000001f, .4f })
        for (int channels = 0; channels < 16; channels++)
        {
            using var f = new Fixture(new Color(hdr ? 2f : .7f, hdr ? -.2f : .3f, .4f, alpha), hdr);
            Set(f.Dynamics, "writeChannels", channels); Set(f.Dynamics, "opacity", .6f);
            f.Begin(); f.Dabs(f.Parameters());
            var live = f.Live(); Call(f.Layer, "EndStroke"); var actual = f.Base();
            for (int i = 0; i < actual.Length; i++) Near(t, Premul(live[i]), actual[i], "Hidden RGB / channel mask " + channels + "/" + alpha + "/" + i, .002f);
        }
    }
    static void RegionAssembly(TestContext t)
    {
        BrushFullAssemblyReference.Validate(t);
        var shader = BrushFullAssemblyReference.Compile("AlphaConversion.shader");
        var full = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        var reference = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var mask = Texture(Color.clear); var tip = Texture(Color.clear);
        try
        {
            var values = new Color[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                values[y * Size + x] = new Color((x + y) / 62f, 0, 0, 1);
            mask.SetPixels(values); mask.Apply();
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                values[y * Size + x] = new Color(1, 1, 1, x < 7 || y < 12 ? 1f : .2f);
            tip.SetPixels(values); tip.Apply();
            for (int variant = 0; variant < 24; variant++)
            {
                using var f = new Fixture(new Color(1.8f, -.2f, .4f, .65f), variant % 2 == 0);
                var originalPixels = (Texture2D)Get(f.Layer, "StoredTexture");
                // Nonuniform source reveals UV shifts or a vertically flipped region copy.
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    values[y * Size + x] = new Color(.1f + x / 35f, .1f + y / 40f, .2f, .15f + (x + y) / 85f);
                originalPixels.SetPixels(values); originalPixels.Apply();
                Call(f.Layer, "ReleasePaintSurface"); EnsureSurface(f.Layer, Size, Size);
                Set(f.Settings, "brushSize", variant % 3 == 0 ? 3f : 9f);
                Set(f.Settings, "brushHardness", .4f); Set(f.Dynamics, "opacity", .43f);
                Set(f.Dynamics, "blend", variant % 3 == 0 ? BlendMode.Multiply : BlendMode.Normal);
                Set(f.Dynamics, "blendApplication", Enum.Parse(Get(f.Dynamics, "blendApplication").GetType(), variant % 4 == 0 ? "Stamp" : "Stroke"));
                Set(f.Dynamics, "writeChannels", variant % 5 == 0 ? 5 : 15);
                if (variant % 4 == 1) { Set(f.Dynamics, "tip", tip); Set(f.Dynamics, "angleOffset", 37f); }
                if (variant >= 12)
                {
                    var matrix = variant % 2 == 0
                        ? new ProjectiveMatrix { m00 = .8, m01 = -.3, m02 = .2, m10 = .3, m11 = 1.1, m12 = -.17, m20 = .2, m21 = -.1, m22 = 1 }
                        : new ProjectiveMatrix { m00 = -1, m02 = 1, m11 = 1, m22 = 1 };
                    var transform = TextureTransform.Default; t.True(transform.TrySetMatrix(matrix), "Region transform"); f.Layer.Owner.transform = transform;
                }
                var before = f.Base(); f.Begin();
                var p = f.Parameters(mask, variant % 6 == 0, variant % 6 == 1);
                if (variant % 3 == 2) p = Call(p, "WithCanvasWrap");
                var points = new[] { new Vector2(.18f, .77f), new Vector2(.03f, .96f), new Vector2(.7f, .2f) };
                for (int dab = 0; dab < points.Length; dab++)
                {
                    Call(f.Layer, "PaintPoint", points[dab], Size, Size, p);
                    Call(f.Layer, "ConfigureBrushComposite", full); full.SetFloat("_Mode", 1);
                    Graphics.Blit((RenderTexture)Get(f.Layer, "paintSurface"), reference, full);
                    var expected = Read(reference); var actual = f.Live();
                    for (int i = 0; i < expected.Length; i++) Near(t, expected[i], actual[i], "Region preview " + variant + "/" + dab + "/" + i, .001f);
                }
                var bounds = (RectInt)Get(f.Layer, "brushDirtyBounds");
                t.True(bounds.width > 0 && bounds.height > 0, "Nonempty bounded stroke");
                var live = f.Live(); Call(f.Layer, "EndStroke"); var committed = f.Base();
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    if (!bounds.Contains(new Vector2Int(x, y))) Near(t, before[i], committed[i], "Outside commit remains bit-exact", 0);
                    else Near(t, Premul(live[i]), committed[i], "Region commit " + variant + "/" + i, .002f);
                }
            }
            // Exercise the portable draw-copy fallback directly, irrespective of this GPU's CopyTexture support.
            // A partial edge tile uses only the lower-left portion of a larger scratch texture.
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                values[y * Size + x] = new Color(x * .125f - 1f, y * .125f, .25f, .5f);
            tip.SetPixels(values); tip.Apply();
            Color untouched = new Color(.125f, .25f, .5f, .75f);
            RenderTexture.active = reference; GL.Clear(false, true, untouched);
            var boundsCopy = new RectInt(9, 17, 13, 7);
            var copy = typeof(DrawingLayerBehaviour).GetMethod("CopyBrushRegionWithShader", BindingFlags.Static | BindingFlags.NonPublic);
            copy.Invoke(null, new object[] { tip, reference, boundsCopy });
            var pixelsCopy = Read(reference);
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                Color expected = boundsCopy.Contains(new Vector2Int(x, y))
                    ? values[(y - boundsCopy.y) * Size + x - boundsCopy.x] : untouched;
                Near(t, expected, pixelsCopy[y * Size + x], "Partial tile fallback pixel " + x + "/" + y, .001f);
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(reference); Object.DestroyImmediate(full); Object.DestroyImmediate(shader);
            Object.DestroyImmediate(mask); Object.DestroyImmediate(tip);
        }
    }

    static void TiledRegionAssembly(TestContext t)
    {
        // Non-square, non-power-of-two image crosses two tile seams and leaves partial edge tiles.
        const int width = 2053, height = 1157;
        var layer = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var stored = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        var mask = new Texture2D(width, height, TextureFormat.R8, false, true);
        var readback = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        var reference = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var shader = BrushFullAssemblyReference.Compile("Blend.shader"); var material = new Material(shader);
        try
        {
            var input = new Color[width * height]; var coverage = new byte[input.Length];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                input[y * width + x] = new Color(-.2f + 2f * x / width, 1.4f * y / height, .3f, .1f + .8f * x / width);
                coverage[y * width + x] = (byte)(40 + (x + y) % 180);
            }
            stored.SetPixels(input); stored.Apply(); mask.LoadRawTextureData(coverage); mask.Apply();
            Call(layer, "AdoptStoredTexture", stored); EnsureSurface(layer, width, height);
            var settings = New("PaintToolSettings"); var dynamics = Get(settings, "dynamics");
            Set(settings, "brushSize", 2100f); Set(settings, "brushHardness", .65f); Set(settings, "brushColor", new Color(1.3f, -.12f, .2f, .8f));
            Set(dynamics, "opacity", .65f); Set(dynamics, "blend", BlendMode.Multiply); Set(dynamics, "writeChannels", 13);
            var parameters = Call(Call(settings, "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", mask);
            Call(layer, "BeginStroke", Center);
            Call(layer, "PaintPoint", new Vector2(.35f, .6f), width, height, parameters);
            Call(layer, "PaintPoint", new Vector2(.85f, .2f), width, height, parameters);
            Call(layer, "ConfigureBrushComposite", material);
            var surface = (RenderTexture)Get(layer, "paintSurface"); Graphics.Blit(surface, reference, material, 1);
            Color[] ReadAll(RenderTexture target)
            { RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); return readback.GetPixels(); }
            var expected = ReadAll(reference);
            Call(layer, "EndStroke"); var actual = ReadAll(surface);
            float maximum = 0f; int worst = 0;
            for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
            {
                float error = Mathf.Abs(expected[i][c] - actual[i][c]);
                if (error > maximum) { maximum = error; worst = i; }
            }
            t.Near(0, maximum, .003, "All pixels including tile seams and partial tiles agree: worst=" + worst);
        }
        finally
        {
            RenderTexture.active = null;
            Call(layer, "ReleaseTransientResources");
            if (stored != null) Object.DestroyImmediate(stored);
            Object.DestroyImmediate(mask); Object.DestroyImmediate(readback); Object.DestroyImmediate(material); Object.DestroyImmediate(shader);
            RenderTexture.ReleaseTemporary(reference);
        }
    }

    static void DocumentConsumers(TestContext t)
    {
        using var f = new Fixture(new Color(1.6f, -.2f, .3f, .4f));
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = Size;
        var mask = Texture(new Color(.35f, 0, 0, 1));
        var group = new GroupLayerBehaviour { compositing = GroupCompositing.Isolated };
        var clip = new ColorFillLayerBehaviour { color = new Color(.3f, .6f, 1.2f, .25f), clippingMask = true, colorRange = LayerColorRange.HDR };
        var effect = new BlurLayerBehaviour { radius = 2, inputMode = EffectInputMode.Specific, colorRange = LayerColorRange.HDR };
        group.layers.Add(clip); group.layers.Add(f.Layer); doc.layers.Add(effect); doc.layers.Add(group);
        Call(doc, "NormalizeModel"); effect.TargetLayerId = group.Id;
        Color[][] Capture()
        {
            var image = doc.ComposeCanvas();
            try
            {
                var composite = image.GetPixels();
                var thumbnail = (Texture2D)Call(doc, "GetLayerThumbnail", effect.Owner, Size, false);
                t.True(thumbnail != null, "Target-group thumbnail exists");
                var encoder = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexRasterEncoder");
                var format = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.RasterImageFormat");
                byte[] Encode(string name) => (byte[])encoder.GetMethod("Encode", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { image, Enum.Parse(format, name), 95, Texture2D.EXRFlags.CompressZIP });
                var exr = Encode("Exr");
                t.True(exr.Length > 100 && exr[0] == 0x76 && exr[1] == 0x2f && exr[2] == 0x31 && exr[3] == 0x01, "HDR export produces EXR bytes");
                var png = new Texture2D(2, 2);
                try
                {
                    t.True(png.LoadImage(Encode("Png")) && png.width == Size && png.height == Size, "PNG export roundtrip dimensions");
                    return new[] { composite, thumbnail.GetPixels(), png.GetPixels() };
                }
                finally { Object.DestroyImmediate(png); }
            }
            finally { Object.DestroyImmediate(image); }
        }
        try
        {
            Set(f.Settings, "brushColor", new Color(1.8f, -.1f, .5f, .8f)); Set(f.Dynamics, "opacity", .65f);
            f.Begin(); f.Dabs(f.Parameters(mask)); var before = Capture();
            t.True(Get(f.Layer, "advancedStroke") != null, "Rendering/export does not prematurely commit the stroke");
            Call(f.Layer, "EndStroke");
            // Invalidate the detached document's caches just as a completed user stroke does.
            Call(doc, "MarkChanged"); var after = Capture();
            for (int kind = 0; kind < before.Length; kind++)
            {
                t.Equal(before[kind].Length, after[kind].Length, "Consumer dimensions");
                for (int i = 0; i < before[kind].Length; i++) Near(t, before[kind][i], after[kind][i], "Group / clipping / Target / thumbnail / export " + kind + "/" + i, .008f);
            }
        }
        finally { Object.DestroyImmediate(mask); Object.DestroyImmediate(doc); }
    }

    static void RenderState(TestContext t, Texture mask)
    {
        using var f = new Fixture(new Color(2, -.1f, .5f, .4f));
        var old = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var sentinel = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        try
        {
            f.Begin(); GL.sRGBWrite = true; RenderTexture.active = sentinel;
            f.Dabs(f.Parameters(mask));
            t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Accumulation preserves caller GPU state");
            f.Live();
            t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Live assembly preserves caller GPU state");
            Call(f.Layer, "EndStroke");
            t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Commit preserves caller GPU state");
        }
        finally { RenderTexture.active = old; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(sentinel); }
    }
}
