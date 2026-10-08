// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShapeFeatherTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShapeFeatherTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    private static void ExecuteRun()
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.width = doc.height = 128;
        var shape = new ShapeLayerBehaviour();
        var layer = new Layer(shape);
        doc.layers.Add(layer);
        int checks = 0;
        void Check(bool value, string label) { context.True(value, label); }
        float Coverage(float d, float width, ShapeLayerBehaviour.FeatherPosition position)
        {
            float shift = position == ShapeLayerBehaviour.FeatherPosition.Inside ? 0 : position == ShapeLayerBehaviour.FeatherPosition.Outside ? 1 : .5f;
            float t = Mathf.Clamp01((d + width * (1 - shift)) / width);
            return 1 - t * t * (3 - 2 * t);
        }
        Texture2D Render()
        {
            var tex = doc.ComposeCanvas();
            try { foreach (var c in tex.GetPixels()) Check(!float.IsNaN(c.a) && c.a >= 0 && c.a <= 1, "finite bounded alpha"); return tex; }
            catch { UnityEngine.Object.DestroyImmediate(tex); throw; }
        }
        try
        {
            var baseline = Render();
            try
            {
            foreach (ShapeLayerBehaviour.FeatherPosition pos in Enum.GetValues(typeof(ShapeLayerBehaviour.FeatherPosition)))
            {
                shape.featherPosition = pos;
                var zero = Render();
                try { Check(Array.TrueForAll(Array.ConvertAll(zero.GetPixels(), c => c.a), a => a >= 0), "zero valid");
                    var a = baseline.GetPixels(); var b = zero.GetPixels();
                    for (int i = 0; i < a.Length; i++) Check(a[i] == b[i], "zero unaffected by position");
                } finally { UnityEngine.Object.DestroyImmediate(zero); }
            }
            }
            finally { UnityEngine.Object.DestroyImmediate(baseline); }
            shape.feather = 16;
            foreach (ShapeLayerBehaviour.FeatherPosition pos in Enum.GetValues(typeof(ShapeLayerBehaviour.FeatherPosition)))
            {
                shape.featherPosition = pos;
                var tex = Render();
                try { for (int x = 64; x < 120; x++) Check(Mathf.Abs(tex.GetPixel(x,64).a - Coverage(x + .5f - 96, 16, pos)) < .002f, "rectangle profile " + pos); }
                finally { UnityEngine.Object.DestroyImmediate(tex); }
            }
            shape.fill = false; shape.stroke = true; shape.strokeWidth = 12;
            foreach (ShapeLayerBehaviour.FeatherPosition pos in Enum.GetValues(typeof(ShapeLayerBehaviour.FeatherPosition)))
            {
                shape.featherPosition = pos;
                var tex = Render();
                try { for (int x = 70; x < 120; x++)
                    {
                        float d = x + .5f - 96;
                        float expected = Mathf.Min(Coverage(d,16,pos), Coverage(-d-12,16,pos));
                        Check(Mathf.Abs(tex.GetPixel(x,64).a - expected) < .002f, "stroke profile " + pos);
                    }
                } finally { UnityEngine.Object.DestroyImmediate(tex); }
            }
            shape.fill = true; shape.stroke = false; shape.featherPosition = ShapeLayerBehaviour.FeatherPosition.Centered;
            foreach (var kind in (ShapeLayerBehaviour.ShapeKind[])Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)))
            {
                shape.kind = kind;
                var tex = Render(); UnityEngine.Object.DestroyImmediate(tex);
                shape.feather = .001f; tex = Render(); UnityEngine.Object.DestroyImmediate(tex);
                shape.feather = 8192; tex = Render(); UnityEngine.Object.DestroyImmediate(tex);
                shape.feather = 16;
            }
            shape.kind = ShapeLayerBehaviour.ShapeKind.Ellipse;
            layer.transform.scale = new Vector2(.8f, .15f);
            var ellipse = Render();
            try
            {
                float a = 51.2f, b = 9.6f;
                for (int y = 64; y < 90; y += 3) for (int x = 64; x < 126; x += 3)
                {
                    float px = x + .5f - 64, py = y + .5f - 64, best = float.MaxValue;
                    for (int i = 0; i <= 8192; i++)
                    {
                        float t = i * (Mathf.PI * .5f / 8192);
                        best = Mathf.Min(best, Vector2.Distance(new Vector2(px,py), new Vector2(a*Mathf.Cos(t),b*Mathf.Sin(t))));
                    }
                    if (px*px/(a*a) + py*py/(b*b) < 1) best = -best;
                    Check(Mathf.Abs(ellipse.GetPixel(x,y).a - Coverage(best,16,shape.featherPosition)) < .003f, "ellipse distance reference");
                }
            } finally { UnityEngine.Object.DestroyImmediate(ellipse); }
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var setter = typeof(WhimTexApi).GetMethod("SetShape", flags);
            var jsonType = setter.GetParameters()[1].ParameterType;
            var json = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { "{\"feather\":12,\"featherPosition\":\"Outside\"}" });
            setter.Invoke(null, new object[] { shape, json });
            Check(shape.feather == 12 && shape.featherPosition == ShapeLayerBehaviour.FeatherPosition.Outside, "API update");
            var copy = ScriptableObject.CreateInstance<WhimTexDocument>();
            try
            {
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(UnityEditor.EditorJsonUtility.ToJson(doc), copy);
                var restored = (ShapeLayerBehaviour)copy.layers[0].Behaviour;
                Check(restored.feather == 12 && restored.featherPosition == shape.featherPosition, "serialization");
            } finally { UnityEngine.Object.DestroyImmediate(copy); }
            shape.kind = ShapeLayerBehaviour.ShapeKind.Rectangle;
            shape.feather = 16; shape.featherPosition = ShapeLayerBehaviour.FeatherPosition.Centered;
            layer.transform.scale = Vector2.one * .5f;
            var full = Render();
            try
            {
                var preview = (Texture2D)typeof(WhimTexDocument).GetMethod("ComposeCanvas", flags).Invoke(doc, new object[] { 64 });
                try { for (int x = 32; x < 60; x++) Check(Mathf.Abs(preview.GetPixel(x,32).a - Coverage(2*x+1-96,16,shape.featherPosition)) < .002f, "preview canvas pixel width"); }
                finally { UnityEngine.Object.DestroyImmediate(preview); }
                var thumb = shape.GetPreviewTexture(128);
                Check(thumb.GetPixel(127,64).a > .5f && thumb.GetPixel(127,64).a < .8f, "untransformed thumbnail has soft edge");
                shape.feather = 0;
                var sharpThumb = shape.GetPreviewTexture(128);
                Check(sharpThumb.GetPixel(127,64).a > .99f, "thumbnail invalidation");
                shape.feather = 16;
                var group = new GroupLayerBehaviour();
                group.layers.Add(layer); doc.layers.Clear(); doc.layers.Add(group);
                var grouped = Render();
                try { Check(Mathf.Abs(grouped.GetPixel(96,64).a-full.GetPixel(96,64).a) < .002f, "group feather"); }
                finally { UnityEngine.Object.DestroyImmediate(grouped); }
                group.layers.Clear(); doc.layers.Clear(); doc.layers.Add(layer);
                var upper = new Layer(new ColorFillLayerBehaviour { color = Color.red });
                upper.clippingMask = true; doc.layers.Insert(0, upper);
                var clipped = Render();
                try { Check(Mathf.Abs(clipped.GetPixel(96,64).a-full.GetPixel(96,64).a) < .002f, "clipping preserves feather alpha"); }
                finally { UnityEngine.Object.DestroyImmediate(clipped); }
                doc.layers.Remove(upper);
                var rt = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", flags).Invoke(doc, new object[] { layer, 128 });
                var mini = new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = rt; mini.ReadPixels(new Rect(0,0,128,128),0,0); mini.Apply();
                    Check(Mathf.Abs(mini.GetPixel(96,64).a-full.GetPixel(96,64).a) < .002f, "mini preview");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(mini); }
            }
            finally { UnityEngine.Object.DestroyImmediate(full); }
            var ui = new VisualElement();
            var bindingsType = typeof(ShapeLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings");
            var bindings = Activator.CreateInstance(bindingsType, true);
            typeof(ShapeLayerEditorWindow).GetMethod("BuildFields",flags).Invoke(null, new object[] { ui, shape, doc, (Action<string,Action>)((_, change) => change()), bindings });
            var featherField = ui.Query<FloatField>().ToList().Find(f => f.label == "Feather (px)");
            Check(featherField != null && featherField.value == 16, "UI binding");
            var focus = UnityEditor.EditorWindow.focusedWindow;
            var testWindow = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
            try
            {
                testWindow.ShowUtility(); testWindow.rootVisualElement.Add(ui);
                featherField.value = 20;
                Check(shape.feather == 20, "UI edit");
            }
            finally { testWindow.Close(); if (focus != null) focus.Focus(); }
            var atlas = new Texture2D(512, 640, TextureFormat.RGBA32, false);
            try
            {
                int row = 0;
                foreach (ShapeLayerBehaviour.ShapeKind kind in Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)))
                {
                    shape.kind = kind;
                    for (int col = 0; col < 4; col++)
                    {
                        shape.feather = col == 0 ? 0 : 16;
                        shape.featherPosition = (ShapeLayerBehaviour.FeatherPosition)Mathf.Max(0,col-1);
                        var image = Render();
                        try
                        {
                            var pixels = image.GetPixels();
                            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.Lerp(new Color(.09f,.09f,.09f), Color.white, pixels[i].a);
                            atlas.SetPixels(col*128,(4-row)*128,128,128,pixels);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(image); }
                    }
                    row++;
                }
                atlas.Apply();
                string comparison = System.IO.Path.Combine(fixture.TempFolder(), "shape-feather-comparison.png");
                System.IO.File.WriteAllBytes(comparison, atlas.EncodeToPNG());
                Check(new System.IO.FileInfo(comparison).Length > 0, "Comparison atlas was encoded");
            }
            finally { UnityEngine.Object.DestroyImmediate(atlas); }
            Check(!UnityEditor.ShaderUtil.ShaderHasError(Shader.Find("Hidden/WhimTex/Shape")), "shader compiled");
            return;
        }
        finally { UnityEngine.Object.DestroyImmediate(doc); }
    }
}

