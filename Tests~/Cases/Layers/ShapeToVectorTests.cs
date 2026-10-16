using System;
using System.Collections.Generic;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

// Support: Framework/TestApi.cs. Geometry/model only; no rendering, imports or global Undo.
public static class ShapeToVectorTests
{
    static void Reject(TestContext t, ShapeLayerBehaviour shape, WhimTexDocument doc, string label)
    {
        var layer = shape.Owner; string before = JsonUtility.ToJson(shape); bool rejected = false;
        try { ShapeToVector.Convert(shape, doc); }
        catch (NotSupportedException error) { rejected = error.Message.StartsWith("Shape to Vector:"); }
        t.True(rejected, label + " has an explicit unsupported diagnostic");
        t.True(ReferenceEquals(layer.Behaviour, shape), label + " leaves source behaviour attached");
        t.Equal(before, JsonUtility.ToJson(shape), label + " leaves source data unchanged");
    }
    public static string Run() => TestContext.Run("Geometric Shape to Vector conversion", t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = 256; document.height = 192;
        try
        {
            foreach (ShapeLayerBehaviour.ShapeKind kind in Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)))
            {
                var shape = new ShapeLayerBehaviour { kind = kind, fillColor = new Color(.3f, .4f, .5f, .7f) };
                var layer = new Layer(shape); document.layers.Add(layer);
                string id = layer.Id, before = JsonUtility.ToJson(shape); var transform = layer.transform;
                var vector = ShapeToVector.Convert(shape, document);
                vector.Validate();
                t.Equal(new Vector2(256, 192), vector.referenceSize, kind + " keeps document reference size");
                t.True(vector.shapes.Count == 1 && vector.shapes[0].contours.Count > 0, kind + " exports closed geometry");
                foreach (var contour in vector.shapes[0].contours)
                {
                    t.True(contour.closed && contour.points.Count >= 3, kind + " has an editable closed contour");
                    foreach (var point in contour.points)
                        t.True(float.IsFinite(point.position.x + point.position.y + point.inTangent.x + point.outTangent.y), "Finite geometry");
                }
                t.True(ReferenceEquals(layer.Behaviour, shape), "Converter does not replace the source");
                t.Equal(before, JsonUtility.ToJson(shape), "Converter does not normalize the source in place");
                layer.SetBehaviour(vector);
                t.Equal(id, layer.Id, "Caller replacement preserves layer identity");
                t.Equal(transform, layer.transform, "Caller replacement preserves transform");
                document.layers.Clear();
            }
            var ellipse = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse };
            var ellipseVector = ShapeToVector.Convert(ellipse, document);
            var loop = ellipseVector.shapes[0].contours[0];
            for (int i = 0; i < loop.points.Count; i++)
            {
                var a = loop.points[i]; var b = loop.points[(i + 1) % loop.points.Count];
                for (int step = 0; step <= 8; step++)
                {
                    float u = step / 8f, v = 1 - u;
                    Vector2 p = v*v*v*a.position + 3*v*v*u*(a.position+a.outTangent) + 3*v*u*u*(b.position+b.inTangent) + u*u*u*b.position;
                    t.Near(.5, (p - Vector2.one*.5f).magnitude, 1e-5, "Cubic ellipse follows analytic normalized circle");
                }
            }
            var rounded = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle,
                rectangleCorners = new[] { new ShapeLayerBehaviour.Corner(.2f), new ShapeLayerBehaviour.Corner(.1f, ShapeLayerBehaviour.CornerStyle.Bevel), new ShapeLayerBehaviour.Corner(.3f), new ShapeLayerBehaviour.Corner(.05f) } };
            var roundedVector = ShapeToVector.Convert(rounded, document);
            t.True(roundedVector.shapes[0].contours[0].points.Exists(p => p.outTangent != Vector2.zero), "Rounded corners export curves");
            var arc = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Arc, sweepAngle = 360 };
            t.Equal(2, ShapeToVector.Convert(arc, document).shapes[0].contours.Count, "Full arc retains its hole as a second contour");
            arc.sweepAngle = 0;
            t.Equal(0, ShapeToVector.Convert(arc, document).shapes[0].contours.Count, "Zero sweep remains empty");
            var sector = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Sector, sweepAngle = 245,
                outerCorner = new ShapeLayerBehaviour.Corner(.2f), innerCorner = new ShapeLayerBehaviour.Corner(.15f) };
            t.True(ShapeToVector.Convert(sector, document).shapes[0].contours[0].points.Count > 6, "Reflex rounded sector exports all arcs");
            var stroke = new ShapeLayerBehaviour { stroke = true, strokeWidth = 3, strokePosition = ShapeLayerBehaviour.StrokePosition.Outside };
            t.Near(6, ShapeToVector.Convert(stroke, document).shapes[0].strokeWidth, 1e-6, "Canvas stroke width compensates initial half-scale transform");
            stroke.transform.scaleF = new Vector2(.5f, .25f); Reject(t, stroke, document, "Nonuniform stroke");
            stroke.transform.scaleF = Vector2.one*.5f; stroke.strokePosition = ShapeLayerBehaviour.StrokePosition.Inside; stroke.strokeColor = new Color(0,0,0,.5f);
            Reject(t, stroke, document, "Translucent overlapping stroke");
            var feather = new ShapeLayerBehaviour { feather = 2 }; Reject(t, feather, document, "Active feather");
            feather.edgeMode = ShapeLayerBehaviour.EdgeMode.Step;
            t.Equal(VectorEdgeMode.Step, ShapeToVector.Convert(feather, document).edgeMode, "Inactive Step feather is allowed");
            arc.sweepAngle = 120; arc.arcThickness = 8192; Reject(t, arc, document, "Arc offset cusp");
        }
        finally { UnityEngine.Object.DestroyImmediate(document); }
    });
}
