using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

// Package-owned reflection only; no user document, saved asset, or global Undo operation.
public static class PixelScaleBrushTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static WhimTex.Tests.TestContext T => FixtureContext.Context;
    private static Type SettingsType => typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
    private static object Call(object target, string name, params object[] args) =>
        (target as Type ?? target.GetType()).GetMethod(name, Hidden).Invoke(target is Type ? null : target, args);
    private static object Field(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static float Number(object value, string name) => (float)Field(value, name);
    private static Texture2D Pixels(DrawingLayerBehaviour drawing) =>
        (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Hidden).GetValue(drawing);

    public static string Run() => FixtureContext.Run("Document pixel scale painting", Body);

    private static object Settings(float size, int pencil = 3)
    {
        object settings = Activator.CreateInstance(SettingsType, true);
        Set(settings, "brushSize", size);
        Set(settings, "pencilSize", pencil);
        Set(settings, "brushHardness", 1f);
        Set(settings, "brushSpacing", .25f);
        Type shape = SettingsType.GetField("pencilShape").FieldType;
        Set(settings, "pencilShape", Enum.Parse(shape, "Square"));
        return settings;
    }

    private static object Parameters(object settings, bool pencil, float scale) => Call(settings,
        pencil ? "GetScaledPencilParameters" : "GetScaledStrokeParameters", false, (Color?)Color.white, scale);

    private static void Paint(DrawingLayerBehaviour drawing, int width, int height, Vector2 uv, object parameters)
    {
        Call(drawing, "BeginStroke", uv);
        try { Call(drawing, "PaintPoint", uv, width, height, parameters); }
        finally { Call(drawing, "EndStroke"); }
        Call(drawing, "SyncSurfaceToTexture");
    }

    private static void RepairApi()
    {
        MethodInfo repair = typeof(WhimTexApi).GetMethod("PaintRepair", Hidden);
        Type json = repair.GetParameters()[2].ParameterType;
        Color[][] results = new Color[2][];
        for (int scenario = 0; scenario < 2; scenario++)
        {
            var document = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            document.width = document.height = 64; document.pixelScale = scenario == 0 ? 2 : 1;
            var texture = FixtureContext.Scope.Own(new Texture2D(64, 64, TextureFormat.RGBAHalf, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point });
            var input = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                input[y * 64 + x] = x < 24 ? Color.red : Color.blue;
            texture.SetPixels(input); texture.Apply();
            var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", texture);
            document.layers.Add(drawing);
            Call(document, "NormalizeModel");
            int logicalSize = scenario == 0 ? 6 : 12;
            object operation = json.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] {
                "{\"op\":\"smudgeStroke\",\"points\":[[20,32],[36,32]],\"size\":" + logicalSize +
                ",\"hardness\":1,\"strength\":1,\"flow\":1,\"mixing\":1,\"source\":\"CurrentLayer\"}" });
            try
            {
                repair.Invoke(null, new object[] { document, drawing.Owner, operation, false });
                T.Equal(input[32 * 64 + 30], texture.GetPixel(30, 32), "Scaled repair dry run preserves native source pixels");
                repair.Invoke(null, new object[] { document, drawing.Owner, operation, true });
                results[scenario] = Pixels(drawing).GetPixels();
                T.True(results[scenario][32 * 64 + 30].r > .95f,
                    "Scaled API Smudge carries source color into the destination");
            }
            finally
            {
                PaintTileHistoryTests.ReleaseOwnedHistory(drawing);
                Undo.ClearUndo(document);
                if (Pixels(drawing) != null) Undo.ClearUndo(Pixels(drawing));
                Call(drawing, "ReleaseTransientResources");
            }
        }
        double error = 0;
        for (int i = 0; i < results[0].Length; i++)
        {
            Color a = results[0][i], b = results[1][i];
            error = Math.Max(error, Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) + Math.Abs(a.a - b.a));
        }
        T.True(error < .00001, "API logical Smudge size at pixel scale 2 equals the corresponding physical size at scale 1");
    }

    private static void ClipboardPixelScale()
    {
        var source = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        source.width = source.height = 64; source.pixelScale = 2;
        source.layers.Add(new Layer(new ShapeLayerBehaviour { fill = false, stroke = true, strokeWidth = 3 }));
        Call(source, "NormalizeModel");
        string clipboard = WhimTexDocumentJson.Write(source).Json;
        foreach (bool empty in new[] { true, false })
        {
            var destination = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            destination.width = destination.height = 64;
            destination.pixelScale = empty ? 1 : 3;
            if (!empty) destination.layers.Add(new ColorFillLayerBehaviour());
            var window = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
            try
            {
                Call(window, "SetDocument", destination);
                GUIUtility.systemCopyBuffer = clipboard;
                Call(window, "PasteAreaSelection");
                T.Near(empty ? 2 : 3, destination.pixelScale, 0,
                    empty ? "Same-sized empty canvas adopts clipboard pixel scale" : "Insertion preserves existing canvas pixel scale");
                T.Equal(empty ? 1 : 2, destination.layers.Count, "Clipboard inserts its layer once");
                T.Equal(64, destination.width, "Pixel-scale-only clipboard adoption preserves physical width");
                var inserted = (ShapeLayerBehaviour)destination.layers[0].Behaviour;
                T.Near(3, inserted.strokeWidth, 0, "Pasting preserves the authored logical stroke width");
            }
            finally
            {
                Undo.ClearUndo(destination); Undo.ClearUndo(window);
                FixtureContext.Scope.CloseWindow(window);
            }
        }
    }

    private static void Body()
    {
        using var ownedJournal = new PaintTileHistoryTests.DiskScope();
        foreach (float scale in new[] { .5f, 1f, 2f, 3.25f })
        {
            object settings = Settings(8f);
            string authored = JsonUtility.ToJson(settings);
            object brush = Parameters(settings, false, scale);
            T.Near(8f * scale, Number(brush, "Size"), .00001, "Brush diameter follows logical pixels");
            T.Near(2f * scale, Number(brush, "SpacingPixels"), .00001, "Spacing ratio follows scaled diameter");
            object copied = Call(Call(Call(brush, "WithPressure", .6f), "WithCanvasWrap"), "WithSelectionMask", (Texture)null);
            copied = Call(copied, "WithWriteProtection", 9);
            T.Near(8f * scale, Number(copied, "Size"), .00001, "Stroke-local copies do not multiply twice");
            T.Equal(authored, JsonUtility.ToJson(settings), "Resolved physical dimensions leave brush presets unchanged");
            T.Equal(9, (int)Field(copied, "WriteChannels"), "Scaled brush retains channel protection");
        }

        object fractional = Parameters(Settings(1f), false, .25f);
        T.Near(.25, Number(fractional, "Size"), .00001, "Smooth brush can be smaller than one physical pixel");
        T.Near(.5, Number(fractional, "SpacingPixels"), .00001, "Minimum sampling step remains a physical half pixel");

        foreach (var scenario in new[] { new Vector2(3, 2), new Vector2(6, .5f), new Vector2(3, 1), new Vector2(1, .5f) })
        {
            int authoredSize = (int)scenario.x;
            int actualSize = Math.Max(1, Mathf.RoundToInt(authoredSize * scenario.y));
            object settings = Settings(8f, authoredSize);
            object pencil = Parameters(settings, true, scenario.y);
            T.Near(actualSize, Number(pencil, "Size"), 0, "Pencil rounds the effective size on the physical grid");
            var drawing = new DrawingLayerBehaviour();
            try
            {
                Paint(drawing, 32, 32, new Vector2(16.5f / 32, 16.5f / 32), pencil);
                float center = actualSize % 2 == 0 ? 17 : 16.5f;
                int covered = 0;
                var pixels = Pixels(drawing).GetPixels();
                bool footprint = true, binary = true;
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    bool expected = Math.Abs(x + .5f - center) <= actualSize * .5f &&
                        Math.Abs(y + .5f - center) <= actualSize * .5f;
                    float alpha = pixels[y * 32 + x].a;
                    footprint &= Math.Abs(alpha - (expected ? 1 : 0)) < .005f;
                    binary &= alpha < .005f || alpha > .995f;
                    if (alpha > .5f) covered++;
                }
                T.True(footprint && binary, "Scaled Pencil has an exact unsmoothed square footprint");
                T.Equal(actualSize * actualSize, covered, "Scaled Pencil paints the expected physical pixel count");
            }
            finally { Call(drawing, "ReleaseTransientResources"); }
        }

        var scaledBrush = new DrawingLayerBehaviour();
        var ordinaryBrush = new DrawingLayerBehaviour();
        try
        {
            Paint(scaledBrush, 32, 32, Vector2.one * .5f, Parameters(Settings(4), false, 2));
            Paint(ordinaryBrush, 32, 32, Vector2.one * .5f, Parameters(Settings(8), false, 1));
            var a = Pixels(scaledBrush).GetPixels();
            var b = Pixels(ordinaryBrush).GetPixels();
            double error = 0;
            for (int i = 0; i < a.Length; i++) error = Math.Max(error, Math.Abs(a[i].a - b[i].a));
            T.True(error < .00001, "Scale 2 paints like the corresponding physical brush diameter");
        }
        finally
        {
            Call(scaledBrush, "ReleaseTransientResources");
            Call(ordinaryBrush, "ReleaseTransientResources");
        }

        var document = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.width = document.height = 64;
        document.pixelScale = 2;
        var source = FixtureContext.Scope.Own(new Texture2D(32, 32, TextureFormat.RGBA32, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point });
        source.SetPixels(new Color[32 * 32]); source.Apply();
        var transformed = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", source);
        transformed.Owner.transform.scale = new Vector2(.5f, 1f);
        document.layers.Add(transformed);
        Call(document, "NormalizeModel");
        Call(document, "GetPaintTransform", transformed.Owner);
        try
        {
            Paint(transformed, 64, 64, Vector2.one * .5f, Parameters(Settings(8, 4), true, document.pixelScale));
            var pixels = Pixels(transformed).GetPixels();
            int minX = 32, minY = 32, maxX = -1, maxY = -1;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                if (pixels[y * 32 + x].a > .5f)
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            T.Equal(8, maxX - minX + 1, "Scaled Pencil converts once through the narrower layer frame");
            T.Equal(4, maxY - minY + 1, "Source grid dimensions are independent of logical pixel multiplier");
            transformed.brushSize = 4;
            T.Near(8, Number(Call(transformed, "GetStrokeParameters", false), "Size"), .00001,
                "Document-bound model painting also resolves global pixel scale");
        }
        finally { Call(transformed, "ReleaseTransientResources"); }

        var window = FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        Call(window, "SetDocument", document);
        T.Near(24, (float)Call(window, "EffectivePaintSize", 12f), .00001, "Brush, Blur, Smudge and Healing use the same physical diameter");
        T.Equal(6, (int)Call(window, "EffectivePencilSize", 3), "Pencil cursor resolves the same size as paint");
        T.Equal(14, (int)Call(window, "EffectivePaintDistance", 7f), "Healing and fill margins use logical pixel distances");
        FixtureContext.Scope.CloseWindow(window);

        Type smoothingType = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.StrokeSmoothingSettings", true);
        Type smootherType = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.StrokeSmoother", true);
        object smoothing = Activator.CreateInstance(smoothingType, true);
        Set(smoothing, "distance", 8f);
        Set(smoothing, "mode", Enum.Parse(smoothingType.GetField("mode").FieldType, "Stabilizer"));
        object smoother = Activator.CreateInstance(smootherType);
        Call(smoother, "Begin", Vector2.zero, 1f);
        Call(smoother, "MoveScaled", new Vector2(40, 0), 1f, smoothing, 2f, false);
        var position = (Vector2)smootherType.GetProperty("Position", Hidden).GetValue(smoother);
        T.Near(24, position.x, .00001, "Stabilizer rope scales its authored distance without changing the physical pointer grid");
        T.Near(8, (float)Field(smoothing, "distance"), 0, "Smoothing presets retain their authored distance");

        Type healing = typeof(DrawingLayerBehaviour).GetNestedType("HealingStrokeBuffer", Hidden);
        Type projective = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ProjectiveMatrix", true);
        object identity = projective.GetProperty("Identity", Hidden).GetValue(null);
        var buffer = (IDisposable)Activator.CreateInstance(healing, Hidden, null,
            new object[] { 64, 64, 12f, 1f, false, identity, null }, null);
        using (buffer)
        {
            Call(buffer, "Add", new Vector2(32, 32));
            var bounds = (RectInt)Call(buffer, "Bounds", 6);
            T.Equal(new RectInt(20, 20, 24, 24), bounds, "Healing repair footprint plus search margin follows scaled artistic sizes");
        }
        RepairApi();
        ClipboardPixelScale();
    }
}
