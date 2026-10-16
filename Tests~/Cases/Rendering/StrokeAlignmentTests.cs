using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Only own internal geometry is reflected; no Unity internals or persistent assets.
public static class StrokeAlignmentTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly Type Style = typeof(VectorShape).Assembly.GetType("DCFApixels.WhimTex.StrokeStyle", true);
    static readonly Type Geometry = typeof(VectorShape).Assembly.GetType("DCFApixels.WhimTex.StrokeGeometry", true);
    static readonly MethodInfo From = Style.GetMethod("From", Flags, null, new[] { typeof(VectorShape) }, null);
    static readonly MethodInfo Outline = Geometry.GetMethod("Outline", Flags);
    static readonly MethodInfo Offset = Geometry.GetMethod("OffsetTrajectories", Flags);

    public static string Run() => TestContext.Run("Offset stroke trajectories", t =>
    {
        foreach (var position in new[] { VectorStrokePosition.Inside, VectorStrokePosition.Outside })
        foreach (float width in new[] { 4f, 8f, 16f })
        foreach (float dash in new[] { .01f, 1.5f })
        foreach (var join in new[] { VectorStrokeJoin.Miter, VectorStrokeJoin.Round, VectorStrokeJoin.Bevel })
        {
            var paths = new List<List<Vector2>> { Rect(20, 20, 220, 160) };
            var shape = new VectorShape { strokeWidth = width, strokePosition = position, join = join,
                cap = VectorStrokeCap.Round, dashPatternRelative = true, dashPattern = new List<float> { dash, 4 } };
            var fill = Fill(paths);
            var trajectories = (List<List<Vector2>>)Offset.Invoke(null, new object[] { fill, From.Invoke(null, new object[] { shape }), .025f });
            var polygons = Build(paths, null, fill, shape);
            t.Equal(1, trajectories.Count, "Rectangle offset retains one trajectory");
            // Pick a complete dash on the long lower flank, not at the seam or a join.
            var path = trajectories[0]; float distance = 0; bool tested = false;
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 a = path[i], b = path[(i + 1) % path.Count]; float length = (b - a).magnitude;
                float period = (dash + 4) * width, dashLength = dash * width;
                float start = Mathf.Ceil((distance + width) / period) * period;
                if (length > width * 5 && start + dashLength + width < distance + length)
                {
                    Vector2 direction = (b - a) / length, normal = new Vector2(-direction.y, direction.x);
                    Vector2 first = a + direction * (start - distance), last = first + direction * dashLength;
                    Vector2 middle = (first + last) * .5f;
                    string label = $"{position}/{join}/width={width}/dash={dash}";
                    t.True(BoundaryDistance(polygons, middle) >= width * .5f - .03f, label + " complete round section, no cut or crack");
                    t.True(Contains(polygons, first - direction * width * .4f), label + " full start cap");
                    t.True(Contains(polygons, last + direction * width * .4f), label + " full end cap");
                    t.True(Contains(polygons, middle + normal * width * .4f) && Contains(polygons, middle - normal * width * .4f), label + " both dot halves");
                    t.True(!Contains(polygons, middle + normal * width * .65f), label + " width is not doubled");
                    tested = true; break;
                }
                distance += length;
            }
            t.True(tested, "Straight dash oracle was exercised");
        }

        foreach (bool reverse in new[] { false, true })
        foreach (var position in new[] { VectorStrokePosition.Inside, VectorStrokePosition.Outside })
        {
            var paths = new List<List<Vector2>> { Rect(20, 20, 180, 140), Rect(60, 50, 140, 110) };
            if (reverse) foreach (var path in paths) path.Reverse();
            var shape = new VectorShape { strokeWidth = 8, strokePosition = position, join = VectorStrokeJoin.Miter };
            var polygons = Build(paths, null, Fill(paths), shape);
            bool inside = position == VectorStrokePosition.Inside;
            t.True(Contains(polygons, new Vector2(inside ? 24 : 16, 80)), "Outer boundary follows fill side");
            t.True(!Contains(polygons, new Vector2(inside ? 16 : 24, 80)), "Outer opposite side excluded");
            t.True(Contains(polygons, new Vector2(inside ? 56 : 64, 80)), "Hole boundary reverses offset side");
            t.True(!Contains(polygons, new Vector2(inside ? 64 : 56, 80)), "Hole opposite side excluded");
        }
        var narrow = new List<List<Vector2>> { Rect(20, 20, 100, 26) };
        var inset = new VectorShape { strokeWidth = 8, strokePosition = VectorStrokePosition.Inside,
            cap = VectorStrokeCap.Round, join = VectorStrokeJoin.Miter, dashPattern = new List<float> { .01f, 4 }, dashPatternRelative = true };
        t.Equal(0, Build(narrow, null, Fill(narrow), inset).Count, "Collapsed inset does not fall back to cut dots");
        var neck = new List<List<Vector2>> { new List<Vector2> { new Vector2(0,0), new Vector2(30,0), new Vector2(30,12),
            new Vector2(50,12), new Vector2(50,0), new Vector2(80,0), new Vector2(80,30), new Vector2(50,30),
            new Vector2(50,18), new Vector2(30,18), new Vector2(30,30), new Vector2(0,30) } };
        var split = (List<List<Vector2>>)Offset.Invoke(null, new object[] { Fill(neck), From.Invoke(null, new object[] { inset }), .025f });
        t.Equal(2, split.Count, "Narrow inset neck splits without an invalid connecting trajectory");

        var open = new List<List<Vector2>> { new List<Vector2> { new Vector2(20,20), new Vector2(180,20) } };
        foreach (var position in new[] { VectorStrokePosition.Center, VectorStrokePosition.Inside, VectorStrokePosition.Outside })
        {
            var shape = new VectorShape { strokeWidth = 8, strokePosition = position, cap = VectorStrokeCap.Round };
            var polygons = Build(new List<List<Vector2>>(), open, new Paths64(), shape);
            t.Near(4, BoundaryDistance(polygons, new Vector2(100,20)), .03, "Open path remains centered " + position);
            t.True(Contains(polygons, new Vector2(17,20)), "Open round cap remains whole " + position);
        }
        Preview();
    });

    static List<Vector2> Rect(float x0, float y0, float x1, float y1) => new List<Vector2>
        { new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x1,y1), new Vector2(x0,y1) };
    static Paths64 Fill(List<List<Vector2>> paths)
    {
        var input = new Paths64();
        foreach (var points in paths)
        { var path = new Path64(); foreach (var p in points) path.Add(new Point64(Math.Round(p.x*4096d), Math.Round(p.y*4096d))); input.Add(path); }
        var engine = new Clipper64(); engine.AddSubject(input); var output = new Paths64();
        if (!engine.Execute(ClipType.Union, FillRule.EvenOdd, output)) throw new InvalidOperationException("Fixture fill resolution failed");
        return output;
    }
    static Paths64 Build(List<List<Vector2>> closed, List<List<Vector2>> open, Paths64 fill, VectorShape shape)
        => (Paths64)Outline.Invoke(null, new object[] { closed, open, fill, From.Invoke(null, new object[] { shape }), .025f });
    static bool Contains(Paths64 polygons, Vector2 p)
    {
        bool inside = false;
        foreach (var path in polygons) for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        {
            double ax = path[i].X/4096d, ay = path[i].Y/4096d, bx = path[j].X/4096d, by = path[j].Y/4096d;
            if ((ay > p.y) != (by > p.y) && p.x < (bx-ax)*(p.y-ay)/(by-ay)+ax) inside = !inside;
        }
        return inside;
    }
    static float BoundaryDistance(Paths64 polygons, Vector2 point)
    {
        float nearest = float.MaxValue;
        foreach (var path in polygons) for (int i = 0; i < path.Count; i++)
        {
            var next = path[(i+1)%path.Count];
            Vector2 a = new Vector2((float)(path[i].X/4096d), (float)(path[i].Y/4096d));
            Vector2 b = new Vector2((float)(next.X/4096d), (float)(next.Y/4096d)), edge = b-a;
            nearest = Mathf.Min(nearest, Vector2.Distance(point, a + edge*Mathf.Clamp01(Vector2.Dot(point-a,edge)/Mathf.Max(edge.sqrMagnitude,1e-20f))));
        }
        return nearest;
    }
    static void Preview()
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var output = new Texture2D(1024,512,TextureFormat.RGBA32,false,true);
        try
        {
            for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++)
            {
                var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 256;
                Texture2D a = null, b = null;
                try
                {
                    var shape = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Star, sides = 5, innerRadius = .48f,
                        fill = true, fillColor = Color.white, stroke = true, strokeColor = Color.black, strokeWidth = 8,
                        strokePosition = row == 0 ? ShapeLayerBehaviour.StrokePosition.Outside : ShapeLayerBehaviour.StrokePosition.Inside,
                        strokeCap = VectorStrokeCap.Round, strokeJoin = VectorStrokeJoin.Round, dashPattern = new List<float> { col == 0 ? .01f : 1.5f, 2 } };
                    var layer = new Layer(shape); layer.transform.scaleF = Vector2.one*.72f;
                    doc.layers.Add(layer); doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = new Color(.65f,.8f,.7f) }));
                    a = doc.ComposeCanvas(); var vector = ShapeToVector.Convert(shape,doc); layer.SetBehaviour(vector);
                    layer.transform.tiling = TransformTilingMode.Unbounded; b = doc.ComposeCanvas();
                    output.SetPixels(col*512,row*256,256,256,a.GetPixels()); output.SetPixels(col*512+256,row*256,256,256,b.GetPixels());
                }
                finally { if (a != null) Object.DestroyImmediate(a); if (b != null) Object.DestroyImmediate(b); Object.DestroyImmediate(doc); }
            }
            output.Apply(); Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/stroke-alignment.png",output.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; Object.DestroyImmediate(output); }
    }
}
