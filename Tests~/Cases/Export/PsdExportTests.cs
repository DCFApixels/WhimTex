using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static class PsdExportTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("PsdExportTests", Body);
    static void Body()
    {
        // Opt-in after manual compilation. Requires graphics; never builds or recompiles the project.
        // Creates temporary in-memory documents and PSDs under Temp/WhimTex only.
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>());
        string folder = WhimTex.Tests.UnityC.FixtureContext.Scope.Temp;
        System.IO.Directory.CreateDirectory(folder);
        int checks = 0;
        void Check(bool condition, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(condition, message); checks++; }
        try
        {
            document.width = 32; document.height = 24;
            var fill = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "Color", color = new Color(1, 0, 0.25f, 0.6f) };
            fill.transform.scale = new Vector2(0.7f, 0.8f);
            fill.transform.rotation = 20;
            var gradient = new DCFApixels.WhimTex.GradientLayerBehaviour { layerName = "Gradient", gradientType = DCFApixels.WhimTex.GradientLayerBehaviour.GradientType.Horizontal };
            gradient.transform.rotation = 15;
            gradient.gradient.Mode = DCFApixels.WhimTex.WhimTexGradientMode.Classic;
            gradient.gradient.ColorSpace = ColorSpace.Gamma;
            gradient.gradient.Smoothness = 0;
            gradient.transform.storage = DCFApixels.WhimTex.TransformStorage.TRS;
            gradient.transform.tiling = DCFApixels.WhimTex.TransformTilingMode.Clip;
            gradient.transform.scale = new Vector2(0.7f, 1f);
            document.layers = new List<DCFApixels.WhimTex.Layer>
            {
                new DCFApixels.WhimTex.OutlineLayerBehaviour { layerName = "Outline", outlineWidth = 3 },
                new DCFApixels.WhimTex.GroupLayerBehaviour { layerName = "Группа 💗", layers = new List<DCFApixels.WhimTex.Layer>
                {
                    new DCFApixels.WhimTex.SDFLayerBehaviour { layerName = "SDF" }, fill,
                    new DCFApixels.WhimTex.GroupLayerBehaviour { layerName = "Nested", layers = new List<DCFApixels.WhimTex.Layer>
                    {
                        gradient, new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "Hidden", enabled = false, color = Color.blue }
                    } }
                } }
            };
            // A nested gradient uses a composed projective transform and must be rasterized.
            var fallback = DCFApixels.WhimTex.WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "nested-fallback.psd"));
            Check(fallback.editableFillCount == 2, "Nested gradient uses the raster fallback");
            // Test the separate supported native path with the same gradient at the root.
            var outer = (DCFApixels.WhimTex.GroupLayerBehaviour)document.layers[1].Behaviour;
            var nested = (DCFApixels.WhimTex.GroupLayerBehaviour)outer.layers[2].Behaviour;
            var gradientLayer = nested.layers[0];
            nested.layers.RemoveAt(0); document.layers.Insert(1, gradientLayer);
            string before = EditorJsonUtility.ToJson(document);
            bool dirty = EditorUtility.IsDirty(document);
            RenderTexture active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            string path = System.IO.Path.Combine(folder, "composition.psd");
            var report = DCFApixels.WhimTex.WhimTexPsdExporter.Export(document, path);
            Check(report.layerCount == 5 && report.groupCount == 2, "Layer/group counts");
            Check(report.editableFillCount == 3 && report.editableOutlineCount == 1, "Native fills and stroke: fills=" + report.editableFillCount + ", strokes=" + report.editableOutlineCount);
            Check(before == EditorJsonUtility.ToJson(document), "Source serialization unchanged");
            Check(dirty == EditorUtility.IsDirty(document), "Source dirty state unchanged");
            Check(active == RenderTexture.active, "Active render target restored");
            Check(srgb == GL.sRGBWrite, "Output color-space state restored");
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Check(bytes.Length > 100 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "8BPS", "Real rendered export");
            bool refused = false;
            try { DCFApixels.WhimTex.WhimTexPsdExporter.Export(document, path); }
            catch (System.IO.IOException) { refused = true; }
            Check(refused, "Overwrite requires opt-in");
            bool canceled = false;
            try
            {
                DCFApixels.WhimTex.WhimTexPsdExporter.Export(document, path, true,
                    (name, fraction) => { if (fraction > 0.2f) throw new OperationCanceledException(); });
            }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Cancellation propagated");
            Check(Convert.ToBase64String(bytes) == Convert.ToBase64String(System.IO.File.ReadAllBytes(path)), "Canceled export preserves destination");
            Check(System.IO.Directory.GetFiles(folder, "*.tmp").Length == 0, "No temporary siblings after cancellation");
            Check(before == EditorJsonUtility.ToJson(document), "Canceled export preserves source");
            DCFApixels.WhimTex.WhimTexPsdExporter.Export(document, path, true);
            Check(System.IO.File.ReadAllBytes(path).Length == bytes.Length, "Successful replacement");
            document.layers = new List<Layer> { gradientLayer };
            gradient.transform.tiling = TransformTilingMode.Unbounded;
            foreach (var mode in new[] { WhimTexGradientMode.Classic, WhimTexGradientMode.Linear, WhimTexGradientMode.Perceptual })
            {
                gradient.gradient.Mode = mode; gradient.gradient.Smoothness = .8f;
                var native = WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, mode + ".psd"));
                Check(native.editableFillCount == 1 && !native.usesBakedComposite, mode + " with Smoothness remains editable");
                string data = System.Text.Encoding.ASCII.GetString(System.IO.File.ReadAllBytes(System.IO.Path.Combine(folder, mode + ".psd")));
                Check(data.Contains("gradientsInterpolationMethod") && data.Contains(mode == WhimTexGradientMode.Classic ? "Gcls" : mode == WhimTexGradientMode.Linear ? "Lnr " : "Perc"), "Gradient method descriptor " + mode);
            }
            gradient.gradient.Mode = WhimTexGradientMode.Fixed;
            Check(WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "fixed.psd")).editableFillCount == 0, "Fixed has no native equivalent");
            gradient.gradient.Mode = WhimTexGradientMode.Perceptual;
            var ordinary = gradient.gradient.Clone();
            gradient.gradient.SetKeys(new[] { new GradientColorKey(new Color(2, 0, 0), 0), new GradientColorKey(Color.black, 1) }, ordinary.AlphaKeys);
            Check(WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "hdr-gradient.psd")).editableFillCount == 0, "HDR stops are clamped after rendering, not before interpolation");
            gradient.gradient = ordinary;
            gradientLayer.blendMode = DCFApixels.WhimTex.BlendMode.Negation;
            Check(WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "negation.psd")).usesBakedComposite, "Unsupported active blending preserves a faithful visible result");
            gradientLayer.enabled = false;
            Check(!WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "disabled.psd")).usesBakedComposite, "Disabled unsupported blending does not bake the visible stack");
            gradientLayer.enabled = true; gradientLayer.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
            var outline = new OutlineLayerBehaviour { outlineWidth = 3, outlineSoftness = 8 };
            document.layers.Insert(0, outline);
            Check(WhimTexPsdExporter.Export(document, System.IO.Path.Combine(folder, "soft-outline.psd")).editableOutlineCount == 0, "Soft outline stays raster rather than becoming a sharp stroke");
            Debug.Log($"PSD Editor smoke: {checks} checks passed. Output: {path}");
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document); }
        
    }
}
