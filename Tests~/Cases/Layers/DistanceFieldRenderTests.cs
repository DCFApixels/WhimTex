using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceFieldRenderTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Size = 128;

    public static string Run() => TestContext.Run("Six distance modes: native render and data output", t =>
    {
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T item) where T : UnityEngine.Object { objects.Add(item); return item; }
        var previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        var input = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        RenderTexture rendered = null;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            doc.width = doc.height = Size;
            var source = Own(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var read = Own(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                float alpha = 0;
                // Rotated polygon, ring, acute triangle, and concave cut. Supersampling is
                // an independent coverage source, not an expected distance implementation.
                for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                {
                    float px = x + (sx + .5f) / 4 - .5f, py = y + (sy + .5f) / 4 - .5f;
                    float dx = px - 37, dy = py - 37;
                    float u = .8660254f * dx + .5f * dy, v = -.5f * dx + .8660254f * dy;
                    float r = Mathf.Sqrt((px - 94) * (px - 94) + (py - 36) * (py - 36));
                    bool square = Mathf.Abs(u) < 17 && Mathf.Abs(v) < 17;
                    bool ring = r < 18 && r > 9;
                    bool triangle = py > 80 && py < 116 && Mathf.Abs(px - 35) < (116 - py) * .4f;
                    bool concave = px > 76 && px < 110 && py > 78 && py < 110 && !(px > 88 && py > 90);
                    if (square || ring || triangle || concave) alpha += 1f / 16;
                }
                pixels[y * Size + x] = new Color(1, 1, 1, alpha);
            }
            source.SetPixels(pixels); source.Apply();
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            var contextType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, doc, input, Size, Size, 1f, false, false, null);
            var sheet = Own(new Texture2D(Size * 6, Size * 2, TextureFormat.RGBA32, false, true));
            int column = 0;
            foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
            {
                var outline = new OutlineLayerBehaviour { metric = mode, outlineWidth = 7, outlineSoftness = 0,
                    fillCenter = true, outlineColor = new Color(.1f, .65f, 1, 1), fillColor = new Color(.7f, .7f, .7f, 1) };
                doc.layers.Add(outline.Owner);
                RenderTexture.active = input; GL.sRGBWrite = false;
                rendered = (RenderTexture)typeof(OutlineLayerBehaviour).GetMethod("Render", Hidden).Invoke(outline, new[] { context });
                t.True(rendered != null && rendered.width == Size, "Outline renders: " + mode);
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "Outline preserves caller GPU state: " + mode);
                RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                var actual = read.GetPixels();
                int partial = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    for (int c = 0; c < 4; c++) t.True(float.IsFinite(actual[i][c]), "Finite Outline channel: " + mode);
                    if (actual[i].a > .01f && actual[i].a < .99f) partial++;
                    actual[i] = Color.Lerp(new Color(.025f, .025f, .025f, 1), new Color(actual[i].r, actual[i].g, actual[i].b, 1), actual[i].a);
                }
                t.True(partial > 40, "Crisp Outline still has subpixel coverage: " + mode);
                sheet.SetPixels(column * Size, Size, Size, Size, actual);
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(rendered); rendered = null;

                var sdf = new SDFLayerBehaviour { metric = mode, encoding = SDFLayerBehaviour.OutputEncoding.LinearData,
                    maxDistanceNormalization = 12 };
                doc.layers.Add(sdf.Owner);
                rendered = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(sdf, new[] { context });
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "SDF preserves caller GPU state: " + mode);
                RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                actual = read.GetPixels();
                foreach (Color color in actual)
                {
                    t.True(float.IsFinite(color.r) && color.r >= 0 && color.r <= 1, "Finite remapped SDF: " + mode);
                    t.Near(1, color.a, .001, "Scalar SDF output is opaque: " + mode);
                }
                sheet.SetPixels(column * Size, 0, Size, Size, actual);
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(rendered); rendered = null;
                column++;
            }
            t.Equal(6, column, "Exactly six artist modes");
            sheet.Apply();
            string directory = Path.Combine(Application.dataPath, "../Temp/WhimTex/distance-fields");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "six-modes.png"), sheet.EncodeToPNG());

            var mtsdf = new SDFLayerBehaviour { metric = DistanceMetric.MTSDF, encoding = SDFLayerBehaviour.OutputEncoding.MultiChannelData,
                maxDistanceNormalization = 12, inverted = true, contourOffset = 5,
                distancePosition = SDFLayerBehaviour.DistancePosition.Inside, profile = AnimationCurve.Linear(0, 0, 1, 0) };
            doc.layers.Add(mtsdf.Owner);
            RenderTexture.active = input;
            rendered = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(mtsdf, new[] { context });
            RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            var channels = read.GetPixels();
            int different = 0, distanceAlpha = 0;
            foreach (Color color in channels)
            {
                if (Mathf.Abs(color.r - color.g) > .002f || Mathf.Abs(color.g - color.b) > .002f) different++;
                if (color.a > .01f && color.a < .99f) distanceAlpha++;
                for (int c = 0; c < 4; c++) t.True(float.IsFinite(color[c]) && color[c] >= 0 && color[c] <= 1, "Normalized linear MTSDF channel");
            }
            t.True(different > 100, "MTSDF has genuinely different RGB channels, not replicated grayscale");
            t.True(distanceAlpha > 100, "MTSDF alpha stores signed distance, not opacity");
            t.True(channels[37 * Size + 37].a < .5f, "MTSDF bypasses artistic inversion/profile/position");
            CheckComposition(t, source, objects);
            CheckMtsdfStarFalloff(t, objects);
            var shader = Shader.Find("Hidden/WhimTex/SdfGradient");
            t.True(shader != null && !ShaderUtil.ShaderHasError(shader), "SDF shader compiles");
            CheckControls(t, doc);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });

    static void CheckMtsdfStarFalloff(TestContext t, List<UnityEngine.Object> owned)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        owned.Add(doc); doc.width = doc.height = 512;
        var star = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Star,
            sides = 5, innerRadius = .5f, fill = true, edgeMode = ShapeLayerBehaviour.EdgeMode.Antialiased };
        star.transform.positionF = new Vector2(-105.23746f, 41.058594f);
        star.transform.scaleF = Vector2.one * .35042733f;
        doc.layers.Add(star.Owner);
        var initialized = doc.ComposeCanvas();
        UnityEngine.Object.DestroyImmediate(initialized);
        var sdf = new SDFLayerBehaviour { metric = DistanceMetric.MTSDF,
            encoding = SDFLayerBehaviour.OutputEncoding.LinearData, maxDistanceNormalization = 32.6f,
            inputMode = EffectInputMode.Specific, TargetLayerId = star.Owner.Id };
        doc.layers.Insert(0, sdf.Owner);
        foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
        foreach (byte threshold in new byte[] { 64, 96, 117, 118, 119, 128, 143, 160, 192, 224, 228 })
        {
            sdf.metric = mode;
            sdf.threshold = threshold;
            var canvas = doc.ComposeCanvas();
            try
            {
                var pixels = canvas.GetPixels();
                double farError = 0;
                for (int y = 0; y < 512; y++) for (int x = 400; x < 512; x++)
                    farError = Math.Max(farError, Math.Abs(1 - pixels[y * 512 + x].r));
                t.Near(0, farError, .002, $"Native {mode} star has no distant false gradient; threshold={threshold}");
                t.True(canvas.EncodeToPNG().Length > 200, "Native star regression remains exportable: " + mode);
                if (mode == DistanceMetric.Bevel && threshold >= 117 && threshold <= 119)
                    t.Near(1, pixels[213 * 512 + 216].r, .002,
                        "Native Bevel facet exceeds the 32.6px range at the former overlapping-loop bulge; threshold=" + threshold);
                if ((mode == DistanceMetric.MTSDF || mode == DistanceMetric.Bevel) && threshold >= 128)
                {
                    // The native unrotated star is symmetric about x~=150.25. A diagonal
                    // cap at the upper tip previously made one side's exterior ramp much
                    // steeper. Check native composition, not a captured output golden.
                    for (int y = 395; y <= 405; y += 2)
                    for (int halfWidth = 5; halfWidth <= 10; halfWidth += 5)
                        t.Near(pixels[y * 512 + 150 - halfWidth].r, pixels[y * 512 + 150 + halfWidth].r, .018,
                            $"{mode} native star tip remains balanced at y={y}, halfWidth={halfWidth}, threshold={threshold}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }
    }

    static void CheckComposition(TestContext t, Texture2D source, List<UnityEngine.Object> owned)
    {
        foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            owned.Add(doc); doc.width = doc.height = Size;
            var group = new GroupLayerBehaviour().Owner;
            group.enabled = false;
            group.children.Add(new FileLayerBehaviour { sourceTexture = source }.Owner);
            doc.layers.Add(group);
            var initialized = doc.ComposeCanvas();
            UnityEngine.Object.DestroyImmediate(initialized);
            t.True(!string.IsNullOrEmpty(group.Id), "Source IDs exist before assigning an explicit Target");
            var outline = new OutlineLayerBehaviour { metric = mode, outlineWidth = 6, fillCenter = true,
                inputMode = EffectInputMode.Specific, TargetLayerId = group.Id };
            var sdf = new SDFLayerBehaviour { metric = mode, encoding = SDFLayerBehaviour.OutputEncoding.LinearData,
                maxDistanceNormalization = 12, inputMode = EffectInputMode.Specific, TargetLayerId = group.Id };
            foreach (TargetedLayerBehaviour effect in new TargetedLayerBehaviour[] { outline, sdf })
            {
                doc.layers.Clear(); doc.layers.Add(effect.Owner); doc.layers.Add(group);
                Texture2D canvas = doc.ComposeCanvas();
                try
                {
                    int visible = 0;
                    foreach (Color p in canvas.GetPixels())
                    {
                        t.True(float.IsFinite(p.r) && float.IsFinite(p.a), "Finite group Target composition: " + mode);
                        if (p.a > .1f) visible++;
                    }
                    t.True(visible > 100, "Disabled group remains a usable explicit effect source: " + mode);
                    t.True(canvas.EncodeToPNG().Length > 200, "Composed effect is exportable: " + mode);
                }
                finally { UnityEngine.Object.DestroyImmediate(canvas); }
                var preview = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", Hidden)
                    .Invoke(doc, new object[] { effect.Owner, 64 });
                try { t.True(preview != null && preview.width == 64 && preview.height == 64, "Effect preview at reduced resolution: " + mode); }
                finally { if (preview != null) RenderTexture.ReleaseTemporary(preview); }
                effect.clippingMask = true;
                doc.layers.Insert(1, new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .5f) }.Owner);
                canvas = doc.ComposeCanvas();
                try
                {
                    foreach (Color p in canvas.GetPixels())
                        t.Near(.5, p.a, .006, "Clipping retains soft base coverage: " + mode);
                }
                finally { UnityEngine.Object.DestroyImmediate(canvas); }
                effect.clippingMask = false;
                // Both behaviors were rendered even though only one is in the final list.
                typeof(SDFLayerBehaviour).GetMethod("ReleaseTransientResources", Hidden).Invoke(sdf, null);
            }
        }
    }

    static void CheckControls(TestContext t, WhimTexDocument doc)
    {
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var bindingsType = assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings", true);
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<DistanceFieldControlWindow>();
        window.hideFlags = HideFlags.HideAndDontSave;
        window.titleContent = new GUIContent("SDF Controls Test");
        try
        {
            window.Show();
            var root = window.rootVisualElement;
            object bindings = Activator.CreateInstance(bindingsType, true);
            int changes = 0;
            Action<string, Action> apply = (label, action) => { changes++; action(); };
            Action<VisualElement, TargetedLayerBehaviour> target = (element, behavior) => { };
            var sdf = new SDFLayerBehaviour();
            typeof(SDFLayerEditorWindow).GetMethod("BuildFields", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { root, sdf, doc, apply, bindings, target });
            var algorithm = root.Query<EnumField>().ToList().Find(field => field.label == "Distance Algorithm");
            var miter = root.Query<FloatField>().ToList().Find(field => field.label == "Miter Limit");
            var output = root.Query<PopupField<SDFLayerBehaviour.OutputEncoding>>().ToList().Find(field => field.label == "Output");
            t.True(algorithm != null && miter != null && output != null, "Artist controls are built without a window");
            t.True(miter.ClassListContains("whimtex-hidden"), "Miter Limit is hidden for Round");
            t.Equal(2, output.choices.Count, "Round has only scalar and gradient outputs");
            Edit(algorithm, (Enum)DistanceMetric.Sharp);
            t.Equal(DistanceMetric.Sharp, sdf.metric, "Algorithm field edits the actual model");
            t.True(!miter.ClassListContains("whimtex-hidden"), "Miter Limit appears for Sharp");
            Edit(miter, 8f);
            t.Near(8, sdf.miterLimit, 0, "Miter field edits the model");
            Edit(algorithm, (Enum)DistanceMetric.MTSDF);
            t.Equal(3, output.choices.Count, "MTSDF exposes Multi Channel Data");
            Edit(output, SDFLayerBehaviour.OutputEncoding.MultiChannelData);
            t.Equal(SDFLayerBehaviour.OutputEncoding.MultiChannelData, sdf.encoding, "Data output edits the model");
            t.True(!root.Query<CurveField>().ToList().Find(field => field.label == "Profile").enabledSelf,
                "Data output disables artistic Profile");
            int before = changes;
            Edit(algorithm, (Enum)DistanceMetric.EuclideanAntialiased);
            t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "Leaving MTSDF restores a valid scalar output");
            t.Equal(before + 1, changes, "Algorithm and output transition is one change transaction");
            t.Equal(2, output.choices.Count, "Non-MTSDF cannot select data encoding");
        }
        finally
        {
            if (window != null)
            {
                window.DiscardChanges();
                window.Close();
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
            }
            if (previousFocus != null && EditorWindow.focusedWindow != previousFocus) previousFocus.Focus();
        }
    }

    static void Edit<T>(BaseField<T> field, T value)
    {
        // Use the public value-change event, without simulating physical input.
        using var change = ChangeEvent<T>.GetPooled(field.value, value);
        field.SetValueWithoutNotify(value);
        change.target = field;
        field.SendEvent(change);
    }
}

public sealed class DistanceFieldControlWindow : EditorWindow { }
