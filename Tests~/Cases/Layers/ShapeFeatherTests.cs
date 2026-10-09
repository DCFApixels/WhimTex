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
            var refresh = bindingsType.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Instance);
            typeof(ShapeLayerEditorWindow).GetMethod("BuildFields",flags).Invoke(null, new object[] { ui, shape, doc,
                (Action<string,Action>)((_, change) => { change(); refresh.Invoke(bindings, new object[] { true }); }), bindings });
            var featherField = ui.Query<FloatField>().ToList().Find(f => f.label == "Feather (px)");
            Check(featherField != null && featherField.value == 16, "UI binding");
            var focus = UnityEditor.EditorWindow.focusedWindow;
            var testWindow = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
            try
            {
                testWindow.ShowUtility(); testWindow.rootVisualElement.Add(ui);
                featherField.value = 20;
                Check(shape.feather == 20, "UI edit");
                var kindField = ui.Query<EnumField>().ToList().Find(f => f.label == "Shape");
                var edgeField = ui.Query<EnumField>().ToList().Find(f => f.label == "Edge Mode");
                edgeField.value = ShapeLayerBehaviour.EdgeMode.Step;
                Check(!featherField.enabledSelf, "Step disables Feather in UI");
                edgeField.value = ShapeLayerBehaviour.EdgeMode.Antialiased;
                Check(featherField.enabledSelf, "Antialiased enables Feather in UI");
                kindField.value = ShapeLayerBehaviour.ShapeKind.Arc;
                var fillField = ui.Query<Toggle>().ToList().Find(f => f.label == "Fill");
                var strokeField = ui.Query<Toggle>().ToList().Find(f => f.label == "Stroke");
                var thicknessField = ui.Query<FloatField>().ToList().Find(f => f.label == "Thickness (px)");
                var widthField = ui.Query<FloatField>().ToList().Find(f => f.label == "Stroke Width (px)");
                var positionField = ui.Query<EnumField>().ToList().Find(f => f.label == "Stroke Position");
                Check(!fillField.ClassListContains("whimtex-hidden") && !strokeField.ClassListContains("whimtex-hidden"), "Arc shows Fill and Stroke independently");
                Check(thicknessField != null && !thicknessField.ClassListContains("whimtex-hidden"), "Arc shows body thickness");
                Check(!widthField.enabledSelf, "Arc outline width is disabled without Stroke");
                thicknessField.value = 24;
                Check(shape.arcThickness == 24 && shape.strokeWidth == 12, "Arc body edit leaves outline width unchanged");
                strokeField.value = true;
                Check(widthField.enabledSelf && !positionField.ClassListContains("whimtex-hidden"), "Arc enables outline width and position with Stroke");
                widthField.value = 3;
                Check(shape.strokeWidth == 3 && shape.arcThickness == 24, "Arc outline edit leaves body unchanged");
                fillField.value = false;
                Check(!shape.fill && shape.stroke, "Arc supports outline without Fill");
                fillField.value = true; strokeField.value = false;
                Check(!ui.Query<EnumField>().ToList().Find(f => f.label == "Line Caps").ClassListContains("whimtex-hidden"), "Arc shows caps");
                kindField.value = ShapeLayerBehaviour.ShapeKind.Star;
                Check(thicknessField.ClassListContains("whimtex-hidden"), "Arc thickness is hidden for other shapes");
                Check(ui.Query<FloatField>().ToList().FindAll(f => f.name.StartsWith("cornerAmount")).Count == 2, "Star has two corner groups");
                Check(ui.Q<VisualElement>("cornerDiagram") != null, "Corners have a shape diagram");
                var cornerStyle = ui.Q<EnumField>("cornerStyle0");
                Check(string.IsNullOrEmpty(cornerStyle.label) && cornerStyle.Q<VisualElement>(className: "whimtex-shape-corner-icon") != null,
                    "Corner style uses an icon, not a text label");
                cornerStyle.value = ShapeLayerBehaviour.CornerStyle.Bevel;
                Check(shape.outerCorner.style == ShapeLayerBehaviour.CornerStyle.Bevel && shape.innerCorner.style == ShapeLayerBehaviour.CornerStyle.Round,
                    "Star corner styles stay independent");
                kindField.value = ShapeLayerBehaviour.ShapeKind.Sector;
                Check(ui.Query<FloatField>().ToList().FindAll(f => f.name.StartsWith("cornerAmount")).Count == 2,
                    "Sector has independent outer and inner corner groups");
                ui.Q<EnumField>("cornerStyle1").value = ShapeLayerBehaviour.CornerStyle.Bevel;
                Check(shape.innerCorner.style == ShapeLayerBehaviour.CornerStyle.Bevel, "Sector inner corner icon edits its style");
                kindField.value = ShapeLayerBehaviour.ShapeKind.Polygon;
                var sidesField = ui.Query<SliderInt>().ToList().Find(f => f.label == "Sides / Points");
                sidesField.value = 12;
                Check(ui.Query<FloatField>().ToList().FindAll(f => f.name.StartsWith("cornerAmount")).Count == 1 && ui.Q<VisualElement>("cornerDiagram").focusable,
                    "Many-sided polygons edit the selected corner on the diagram");
                ui.Q<VisualElement>("cornerDiagram").Focus();
                using (var nextCorner = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow }))
                    ui.Q<VisualElement>("cornerDiagram").SendEvent(nextCorner);
                Check(ui.Q<FloatField>("cornerAmount1") != null && ui.Q<FloatField>("cornerAmount0") == null,
                    "Diagram selection rebuilds the active corner field");
                var linkButton = ui.Q<Button>("linkCorners"); linkButton.Focus();
                using (var toggleLink = NavigationSubmitEvent.GetPooled()) linkButton.SendEvent(toggleLink);
                Check(!shape.linkCorners, "Chain button toggles linked corner amounts");
                ui.Q<FloatField>("cornerAmount1").value = 10;
                Check(Mathf.Abs(shape.polygonCorners[1].amount - .1f) < .0001f && shape.polygonCorners[0].amount == 0,
                    "Selected vertex edits its own corner");
                sidesField.value = 3;
                Check(ui.Query<FloatField>().ToList().FindAll(f => f.name.StartsWith("cornerAmount")).Count == 3, "Polygon rebuilds per-vertex controls");
                for (int i = 0; i < 3; i++) shape.polygonCorners[i] = new ShapeLayerBehaviour.Corner(.15f);
                refresh.Invoke(bindings, new object[] { true });
                UnityEditor.Undo.IncrementCurrentGroup();
                UnityEditor.Undo.RecordObject(doc, "Test Shape Corner Neighbours");
                ui.Q<FloatField>("cornerAmount0").value = 95;
                Check(shape.polygonCorners[1].amount < .051f && shape.polygonCorners[2].amount < .051f, "UI corner edit consumes stored neighbours");
                Check(Mathf.Abs(ui.Q<FloatField>("cornerAmount1").value - shape.polygonCorners[1].amount * 100) < .001f, "Neighbour UI refreshes canonical value");
                UnityEditor.Undo.FlushUndoRecordObjects();
                UnityEditor.Undo.PerformUndo();
                shape = (ShapeLayerBehaviour)doc.layers[0].Behaviour;
                Check(Mathf.Abs(shape.polygonCorners[0].amount - .15f) < .0001f && Mathf.Abs(shape.polygonCorners[1].amount - .15f) < .0001f &&
                    Mathf.Abs(shape.polygonCorners[2].amount - .15f) < .0001f, "One Undo restores active and consumed neighbour values");
            }
            finally { UnityEditor.Undo.ClearUndo(doc); testWindow.Close(); if (focus != null) focus.Focus(); }
            int kindCount = Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)).Length;
            var atlas = new Texture2D(512, 128 * kindCount, TextureFormat.RGBA32, false);
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
                            atlas.SetPixels(col*128,(kindCount-1-row)*128,128,128,pixels);
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
