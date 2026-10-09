using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using static DCFApixels.WhimTex.ShapeLayerBehaviour;
using LineCap = DCFApixels.WhimTex.ShapeLayerBehaviour.LineCap;

public static class ShapeDesignTests
{
    public static string Run() => TestContext.Run("Shape contour, constraints, pixel edges and current JSON", RunChecks);
    private static void RunChecks(TestContext test)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 128;
        var shape = new ShapeLayerBehaviour(); var layer = new Layer(shape); doc.layers.Add(layer);
        void Render(Action<Texture2D> check)
        {
            Texture2D image = doc.ComposeCanvas();
            try { check(image); } finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        try
        {
            // Both straight cut and circular fillet consume the same perpendicular rectangle edges,
            // but their interior coverage is different.
            shape.rectangleCorners[0] = new Corner(1, CornerStyle.Round);
            Render(image => test.True(image.GetPixel(54,74).a > .99f, "Rounded corner keeps circular interior"));
            shape.rectangleCorners[0] = new Corner(1, CornerStyle.Bevel);
            Render(image => test.True(image.GetPixel(54,74).a < .01f, "Bevel cuts a straight diagonal"));
            shape.rectangleCorners[1] = new Corner(.5f, CornerStyle.Round);
            shape.rectangleCorners[2] = new Corner(.75f, CornerStyle.Bevel);
            shape.feather = 4;
            Render(image => { foreach (var pixel in image.GetPixels()) test.True(float.IsFinite(pixel.a) && pixel.a >= 0 && pixel.a <= 1, "Mixed corner SDF is bounded"); });
            shape.feather = 0; shape.rectangleCorners = new Corner[4]; shape.fill = false; shape.stroke = true; shape.strokeWidth = 8;
            foreach (StrokePosition position in Enum.GetValues(typeof(StrokePosition)))
            {
                shape.strokePosition = position;
                Render(image =>
                {
                    test.True((image.GetPixel(89,64).a > .99f) == (position == StrokePosition.Inside), "Inside stroke location " + position);
                    test.True((image.GetPixel(98,64).a > .99f) == (position != StrokePosition.Inside), "Outside stroke location " + position);
                });
            }
            shape.kind = ShapeKind.Line; shape.fill = true; shape.stroke = false; layer.transform.scale = new Vector2(.5f,.125f);
            foreach (LineCap cap in Enum.GetValues(typeof(LineCap)))
            {
                shape.lineCap = cap;
                Render(image =>
                {
                    test.True((image.GetPixel(98,64).a > .99f) == (cap != LineCap.Butt), "Line end extension " + cap);
                    test.True((image.GetPixel(102,70).a > .99f) == (cap == LineCap.Square), "Square cap corner " + cap);
                });
            }
            layer.transform.scale = Vector2.one * .5f; shape.kind = ShapeKind.Arc; shape.arcThickness = 8; shape.lineCap = LineCap.Butt;
            shape.startAngle = 0; shape.sweepAngle = 90;
            Render(image => { test.True(image.GetPixel(86,86).a > .99f, "Arc passes through first quadrant"); test.True(image.GetPixel(64,64).a == 0, "Arc has no filled center"); test.True(image.GetPixel(41,41).a == 0, "Arc excludes other quadrants"); });
            ArcBodyAndOutline(test);
            shape.kind = ShapeKind.Sector;
            Render(image => { test.True(image.GetPixel(74,74).a > .99f, "Sector fills its wedge"); test.True(image.GetPixel(54,54).a == 0, "Sector excludes opposite wedge"); });
            foreach (float sweep in new[] { 0f, 10f, 90f, 180f, 270f, 359f, 360f })
                foreach (CornerStyle style in Enum.GetValues(typeof(CornerStyle)))
                {
                    shape.sweepAngle = sweep; shape.outerCorner = new Corner(.8f, style); shape.innerCorner = new Corner(.8f, style); shape.NormalizeCorners(Vector2.one);
                    layer.transform.scale = new Vector2(.7f,.4f);
                    Render(image => { foreach (var pixel in image.GetPixels()) test.True(float.IsFinite(pixel.a) && pixel.a >= 0 && pixel.a <= 1, "Sector finite " + sweep + " " + style); });
                }
            // Step is evaluated on the canvas grid after a non-integral rotated placement, and ignores Feather.
            shape.edgeMode = EdgeMode.Step; shape.feather = 100; layer.transform.scale = new Vector2(.51f,.33f); layer.transform.rotation = 23;
            layer.transform.position = new Vector2(.35f,-.7f); shape.fill = true; shape.stroke = false;
            foreach (ShapeKind kind in Enum.GetValues(typeof(ShapeKind)))
            {
                shape.kind = kind; shape.sweepAngle = 230;
                shape.outerCorner = new Corner(.1f); shape.innerCorner = new Corner(.1f, CornerStyle.Bevel);
                shape.NormalizeCorners(new Vector2(32,20));
                Render(image => { int covered = 0; foreach (var pixel in image.GetPixels()) { test.True(pixel.a == 0 || pixel.a == 1, "Binary transformed Step " + kind); if (pixel.a == 1) covered++; } test.True(covered > 0, "Step remains visible " + kind); });
            }
            shape.edgeMode = EdgeMode.Antialiased; shape.feather = 0; layer.transform = TextureTransform.Default; layer.transform.scale = Vector2.one * .5f;
            Constraints(test);
            // Persisted and agent settings use the same corner descriptors, without the old shortcut keys.
            shape.kind = ShapeKind.Star; shape.sides = 7; shape.innerRadius = .4f;
            shape.outerCorner = new Corner(.12f, CornerStyle.Bevel); shape.innerCorner = new Corner(.1f, CornerStyle.Round);
            shape.NormalizeCorners(Vector2.one);
            foreach (ShapeKind kind in new[] { ShapeKind.Star, ShapeKind.Sector })
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                shape.kind = kind;
                var write = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode });
                test.True(!write.Json.Contains("cornerRoundness"), "Current JSON has no old corner key");
                using var restored = WhimTexDocumentJson.Read(write.Json, false);
                var copy = (ShapeLayerBehaviour)restored.Document.layers[0].Behaviour;
                test.Equal(shape.outerCorner, copy.outerCorner, "Outer corner roundtrip " + mode);
                test.Equal(shape.innerCorner, copy.innerCorner, "Inner corner roundtrip " + mode);
                test.Equal(mode == WhimTexJsonWriteMode.Full ? shape.lineCap : LineCap.Round, copy.lineCap, "Inactive fields use their defaults in pruned JSON " + mode);
            }
            using (var defaults = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"arc\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\",\"kind\":\"Arc\"}}]}", false))
            {
                var arc = (ShapeLayerBehaviour)defaults.Document.layers[0].Behaviour;
                test.Equal(LineCap.Round, arc.lineCap, "Omitted caps use current Shape defaults"); test.Equal(90f, arc.sweepAngle, "Omitted arc sweep uses current default");
                test.Equal(8f, arc.arcThickness, "Omitted body thickness uses current Arc default");
            }
            test.True(!UnityEditor.ShaderUtil.ShaderHasError(Shader.Find("Hidden/WhimTex/Shape")), "Shape shader has no compilation errors");
        }
        finally { UnityEngine.Object.DestroyImmediate(doc); }
    }
    private static void ArcBodyAndOutline(TestContext test)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 128;
        var shape = new ShapeLayerBehaviour { kind = ShapeKind.Arc, arcThickness = 24, fillColor = Color.red,
            strokeColor = Color.blue, strokeWidth = 8, edgeMode = EdgeMode.Step, sweepAngle = 360 };
        doc.layers.Add(new Layer(shape));
        void Render(Action<Texture2D> check)
        {
            var image = doc.ComposeCanvas();
            try { check(image); } finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        try
        {
            Render(image =>
            {
                test.Equal(Color.red, image.GetPixel(94,64), "Arc body uses Fill Color");
                test.True(image.GetPixel(102,64).r == 1 && image.GetPixel(110,64).a == 0, "Body thickness is independent of Stroke Width");
                test.Equal(0f, image.GetPixel(64,64).a, "Full Arc remains a ring, not a filled ellipse");
            });
            shape.stroke = true;
            foreach (StrokePosition position in Enum.GetValues(typeof(StrokePosition)))
            {
                shape.strokePosition = position;
                Render(image =>
                {
                    test.Equal(Color.red, image.GetPixel(94,64), "Arc interior retains Fill " + position);
                    test.Equal(position == StrokePosition.Inside ? Color.blue : Color.red, image.GetPixel(102,64), "Arc inner outline placement " + position);
                    test.Equal(position == StrokePosition.Inside ? 0f : 1f, image.GetPixel(110,64).a, "Arc outer outline extent " + position);
                    test.Equal(position == StrokePosition.Outside ? Color.red : Color.blue, image.GetPixel(85,64), "Arc hole-side outline " + position);
                    test.Equal(0f, image.GetPixel(64,64).a, "Outline does not fill the ring center " + position);
                });
            }
            shape.strokePosition = StrokePosition.Inside; shape.fill = false;
            Render(image =>
            {
                test.Equal(0f, image.GetPixel(94,64).a, "Outline-only Arc has no body fill");
                test.Equal(Color.blue, image.GetPixel(102,64), "Outline-only Arc keeps its rim");
            });
            shape.stroke = false;
            Render(image => { foreach (var pixel in image.GetPixels()) test.Equal(0f, pixel.a, "No Fill/Stroke leaves Arc empty"); });
            shape.fill = true; shape.arcThickness = 8; shape.sweepAngle = 90;
            foreach (LineCap cap in Enum.GetValues(typeof(LineCap)))
            {
                shape.lineCap = cap;
                Render(image =>
                {
                    test.Equal(cap == LineCap.Butt ? 0f : 1f, image.GetPixel(96,61).a, "Arc cap extension " + cap);
                    test.Equal(cap == LineCap.Square ? 1f : 0f, image.GetPixel(99,60).a, "Arc square cap corner " + cap);
                    test.Equal(cap == LineCap.Butt ? 0f : 1f, image.GetPixel(61,96).a, "Arc end cap extension " + cap);
                });
            }
            shape.stroke = true; shape.fill = false; shape.lineCap = LineCap.Round;
            shape.strokePosition = StrokePosition.Outside; shape.arcThickness = 0;
            Render(image => { foreach (var pixel in image.GetPixels()) test.Equal(0f, pixel.a, "Zero body thickness leaves Arc empty"); });
            shape.arcThickness = 24; shape.sweepAngle = 0;
            Render(image => { foreach (var pixel in image.GetPixels()) test.Equal(0f, pixel.a, "Zero sweep leaves Arc empty"); });
            shape.sweepAngle = 230; shape.startAngle = -20; shape.strokeWidth = 3; shape.fill = true;
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var write = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode });
                using var restored = WhimTexDocumentJson.Read(write.Json, false);
                var copy = (ShapeLayerBehaviour)restored.Document.layers[0].Behaviour;
                test.Equal(24f, copy.arcThickness, "Arc body roundtrip " + mode);
                test.Equal(3f, copy.strokeWidth, "Arc outline roundtrip " + mode);
                test.True(copy.fill && copy.stroke && copy.fillColor == Color.red && copy.strokeColor == Color.blue, "Arc fill and outline survive optimized JSON " + mode);
                test.Equal(StrokePosition.Outside, copy.strokePosition, "Arc stroke position roundtrip " + mode);
                using var originalImage = new OwnedImage(doc.ComposeCanvas());
                using var copyImage = new OwnedImage(restored.Document.ComposeCanvas());
                test.True(Array.TrueForAll(Array.ConvertAll(originalImage.Value.GetPixels(), c => c.a), a => a == 0 || a == 1), "Arc Step with outline is binary " + mode);
                var a = originalImage.Value.GetPixels(); var b = copyImage.Value.GetPixels();
                for (int i = 0; i < a.Length; i++) test.Equal(a[i], b[i], "Restored Arc pixels " + mode);
            }
            shape.stroke = false; shape.fill = false;
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                using var restored = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json, false);
                var copy = (ShapeLayerBehaviour)restored.Document.layers[0].Behaviour;
                test.True(!copy.fill && !copy.stroke, "Disabled Arc toggles survive JSON " + mode);
                test.Equal(24f, copy.arcThickness, "Body geometry stays available without Fill/Stroke " + mode);
            }
            shape.fill = true; shape.stroke = true; shape.startAngle = 0; shape.sweepAngle = 360;
            using (var baseline = new OwnedImage(doc.ComposeCanvas()))
            {
                var layer = doc.layers[0];
                var group = new GroupLayerBehaviour(); group.layers.Add(layer);
                doc.layers.Clear(); doc.layers.Add(group);
                try { Render(image => test.Equal(baseline.Value.GetPixel(98,64), image.GetPixel(98,64), "Group preserves Arc body and outline")); }
                finally { group.layers.Clear(); doc.layers.Clear(); doc.layers.Add(layer); }
                var clipping = new Layer(new ColorFillLayerBehaviour { color = Color.white }) { clippingMask = true };
                doc.layers.Insert(0, clipping);
                try { Render(image => { var a = baseline.Value.GetPixels(); var b = image.GetPixels();
                    for (int i = 0; i < a.Length; i++) test.Equal(a[i].a, b[i].a, "Clipping preserves Arc coverage"); }); }
                finally { doc.layers.Remove(clipping); }
                using var decoded = new OwnedImage(new Texture2D(2,2,TextureFormat.RGBA32,false,true));
                test.True(decoded.Value.LoadImage(baseline.Value.EncodeToPNG()), "Arc composite exports to PNG");
                var original = baseline.Value.GetPixels(); var exported = decoded.Value.GetPixels();
                for (int i = 0; i < original.Length; i++) test.Equal(original[i], exported[i], "PNG retains Arc fill, outline and alpha");
            }
            shape.stroke = false;
            // Thumbnails render at twice the display resolution before reduction.
            test.Equal(0f, shape.GetPreviewTexture(128).GetPixel(120,64).a, "Thin Arc thumbnail leaves its inner gap");
            shape.arcThickness = 36;
            test.Equal(1f, shape.GetPreviewTexture(128).GetPixel(120,64).a, "Body thickness invalidates Arc thumbnail");
        }
        finally { UnityEngine.Object.DestroyImmediate(doc); }
    }
    private sealed class OwnedImage : IDisposable
    {
        internal readonly Texture2D Value;
        internal OwnedImage(Texture2D value) => Value = value;
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Value);
    }
    private static void Constraints(TestContext test)
    {
        var polygon = new ShapeLayerBehaviour { kind = ShapeKind.Polygon, sides = 3, linkCorners = false };
        polygon.NormalizeCorners(Vector2.one);
        for (int i = 0; i < 3; i++) polygon.polygonCorners[i] = new Corner(.15f);
        polygon.SetCorner(0, new Corner(.95f), Vector2.one);
        test.Near(.95, polygon.polygonCorners[0].amount, 1e-5, "Edited corner has priority");
        test.True(polygon.polygonCorners[1].amount < .051 && polygon.polygonCorners[2].amount < .051, "Both stored neighbours decrease");
        float neighbour = polygon.polygonCorners[1].amount;
        polygon.SetCorner(0, new Corner(.2f), Vector2.one);
        test.Near(neighbour, polygon.polygonCorners[1].amount, 1e-6, "Decreasing active amount does not restore neighbours");
        var star = new ShapeLayerBehaviour { kind = ShapeKind.Star, innerRadius = .4f, outerCorner = new Corner(.1f), innerCorner = new Corner(.2f) };
        star.NormalizeCorners(Vector2.one); float oldInner = star.innerCorner.amount;
        star.SetCorner(0, new Corner(1), Vector2.one);
        test.True(star.outerCorner.amount > .1 && star.innerCorner.amount < oldInner, "Star outer consumes inner group");
        float savedInner = star.innerCorner.amount; star.SetCorner(0, new Corner(.05f), Vector2.one);
        test.Near(savedInner, star.innerCorner.amount, 1e-6, "Star neighbours stay reduced");
        var sector = new ShapeLayerBehaviour { kind = ShapeKind.Sector, sweepAngle = 90,
            outerCorner = new Corner(.8f, CornerStyle.Bevel), innerCorner = new Corner(.3f) };
        sector.SetCorner(1, new Corner(1, CornerStyle.Round), Vector2.one);
        test.Near(1, sector.innerCorner.amount, 1e-5, "Sector edited inner corner keeps priority");
        test.Equal(CornerStyle.Bevel, sector.outerCorner.style, "Sector outer style stays independent");
        sector.sweepAngle = 20; sector.outerCorner = new Corner(.3f, CornerStyle.Bevel); sector.innerCorner = new Corner(.25f);
        sector.NormalizeCorners(Vector2.one); float priorOuter = sector.outerCorner.amount;
        sector.SetCorner(1, new Corner(.35f), Vector2.one);
        test.True(sector.outerCorner.amount < priorOuter && sector.innerCorner.amount > .25f, "Sector inner edit consumes outer group");
        float reducedOuter = sector.outerCorner.amount; sector.SetCorner(1, new Corner(.1f), Vector2.one);
        test.Near(reducedOuter, sector.outerCorner.amount, 1e-6, "Sector neighbours do not restore automatically");
        sector.outerCorner = new Corner(.1f, CornerStyle.Bevel); sector.innerCorner = new Corner(.3f);
        sector.SetCorner(0, new Corner(1, CornerStyle.Bevel), Vector2.one);
        test.True(sector.innerCorner.amount < .3f && sector.outerCorner.amount > .1f, "Sector outer edit consumes inner group");
        var largeRectangle = new ShapeLayerBehaviour { linkCorners = false };
        largeRectangle.rectangleCorners[1] = new Corner(.4f); largeRectangle.rectangleCorners[3] = new Corner(.4f);
        largeRectangle.SetCorner(0, new Corner(.8f), Vector2.one);
        test.Near(.8, largeRectangle.rectangleCorners[0].amount, 1e-6, "Rectangle supports more than half a side");
        test.Near(.2, largeRectangle.rectangleCorners[1].amount, 1e-6, "Rectangle consumes conflicting right corner");
        test.Near(.2, largeRectangle.rectangleCorners[3].amount, 1e-6, "Rectangle consumes conflicting bottom corner");
        // Runtime constraints replace the former source-extracted arithmetic checks.
        for (int corner = 0; corner < 4; corner++) for (int n = 0; n <= 100; n++) for (int shift = 0; shift < 20; shift++)
        {
            var rectangle = new ShapeLayerBehaviour();
            float[] amounts = { .05f + shift * .01f, .25f, .5f, .85f };
            for (int i = 0; i < 4; i++) rectangle.rectangleCorners[i] = new Corner(amounts[i], i % 2 == 0 ? CornerStyle.Bevel : CornerStyle.Round);
            rectangle.SetCorner(corner, new Corner(n / 100f, rectangle.rectangleCorners[corner].style), Vector2.one);
            for (int i = 0; i < 4; i++)
            {
                test.True(rectangle.rectangleCorners[i].amount >= 0 && rectangle.rectangleCorners[i].amount <= 1.000001f, "Linked amounts bounded");
                test.Near(rectangle.rectangleCorners[0].amount * amounts[i], rectangle.rectangleCorners[i].amount * amounts[0], 1e-6, "Linked ratios retained");
                test.Equal(i % 2 == 0 ? CornerStyle.Bevel : CornerStyle.Round, rectangle.rectangleCorners[i].style, "Styles are never linked");
            }
        }
        polygon.linkCorners = true;
        for (int i = 0; i < 3; i++) polygon.polygonCorners[i] = new Corner((i + 1) * .1f);
        polygon.SetCorner(0, new Corner(.3f), Vector2.one);
        test.Near(.2, polygon.polygonCorners[0].amount, 1e-5, "Linked geometry limit, not only 100 percent");
        test.Near(.6, polygon.polygonCorners[2].amount, 1e-5, "Linked geometry keeps ratios");
        polygon.polygonCorners[0].style = CornerStyle.Bevel;
        polygon.NormalizeCorners(new Vector2(.1f,1));
        test.Near(polygon.polygonCorners[0].amount * 3, polygon.polygonCorners[2].amount, 1e-5, "Geometry change restricts amounts proportionally");
        var zero = new ShapeLayerBehaviour(); zero.SetCorner(0, new Corner(.3f), Vector2.one);
        foreach (var corner in zero.rectangleCorners) test.Near(.3, corner.amount, 1e-6, "Linked all-zero fallback");
        zero.SetCorner(0, new Corner(float.NaN), Vector2.one);
        foreach (var corner in zero.rectangleCorners) test.True(float.IsFinite(corner.amount), "Non-finite input remains bounded");
    }
}
