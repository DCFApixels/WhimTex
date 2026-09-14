// Opt-in eval after manual compilation. Pure contour/UI-local checks; no assets, windows, GPU or Undo changes.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
const System.Reflection.BindingFlags StaticHidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.WhimTex.TextureCompositorWindow).Assembly.GetType("DCFApixels.WhimTex.PencilCursorElement", true);
var build = type.GetMethod("BuildContour", StaticHidden);
var shapeType = build.GetParameters()[2].ParameterType;
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new System.Exception(label);
    checks++;
}
string Edge(int x, int y, int a, int b)
{
    if (a < x || (a == x && b < y)) return $"{a},{b}:{x},{y}";
    return $"{x},{y}:{a},{b}";
}
foreach (string shape in new[] { "Circle", "Square", "Diamond" })
{
    var mode = System.Enum.Parse(shapeType, shape);
    for (int size = 1; size <= 128; size++)
    {
        var points = new System.Collections.Generic.List<UnityEngine.Vector2>();
        bool detail = (bool)build.Invoke(null, new object[] { points, size, mode, true, 64 });
        Check(points.Count >= 4 && points.Count <= 512, "Bounded contour vertex count");
        if (!detail)
        {
            Check(size > 40, "Small tips must retain their pixel boundary");
            continue;
        }
        var expected = new System.Collections.Generic.HashSet<string>();
        var actual = new System.Collections.Generic.HashSet<string>();
        double radius = size * 0.5;
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return false;
            double dx = System.Math.Abs(x + 0.5 - radius), dy = System.Math.Abs(y + 0.5 - radius);
            return shape == "Square" || (shape == "Diamond" ? dx + dy <= radius * 1.00001
                : dx * dx + dy * dy <= radius * radius * 1.00001 * 1.00001);
        }
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            if (!Inside(x, y)) continue;
            if (!Inside(x, y - 1)) expected.Add(Edge(x, y, x + 1, y));
            if (!Inside(x + 1, y)) expected.Add(Edge(x + 1, y, x + 1, y + 1));
            if (!Inside(x, y + 1)) expected.Add(Edge(x, y + 1, x + 1, y + 1));
            if (!Inside(x - 1, y)) expected.Add(Edge(x, y, x, y + 1));
        }
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i] + UnityEngine.Vector2.one * (float)radius;
            var b = points[(i + 1) % points.Count] + UnityEngine.Vector2.one * (float)radius;
            int x = (int)a.x, y = (int)a.y, endX = (int)b.x, endY = (int)b.y;
            Check(a.x == x && a.y == y && b.x == endX && b.y == endY, "Vertices lie on pixel corners");
            Check((x == endX) != (y == endY), "Nonzero axis-aligned edge");
            int sx = System.Math.Sign(endX - x), sy = System.Math.Sign(endY - y);
            while (x != endX || y != endY)
            {
                Check(actual.Add(Edge(x, y, x + sx, y + sy)), "No duplicated outline edge");
                x += sx; y += sy;
            }
        }
        Check(actual.SetEquals(expected), $"Exact {shape} boundary, size {size}");
    }
    var lod = new System.Collections.Generic.List<UnityEngine.Vector2>();
    bool hugeDetail = (bool)build.Invoke(null, new object[] { lod, 4096, mode, true, 128 });
    Check(lod.Count <= 128 && (shape == "Square" || !hugeDetail), "Huge contours use bounded LOD");
    bool tinyDetail = (bool)build.Invoke(null, new object[] { lod, 15, mode, false, 32 });
    Check(lod.Count <= 32 && (shape == "Square" || !tinyDetail), "Small on-screen pixels use smooth LOD");
}
var cursor = System.Activator.CreateInstance(type, true);
var state = type.GetMethod("SetState", Hidden);
var version = type.GetField("geometryVersion", Hidden);
var circle = System.Enum.Parse(shapeType, "Circle");
state.Invoke(cursor, new object[] { 15, circle, new UnityEngine.Vector2(100, 100),
    new UnityEngine.Vector2(4, 0), new UnityEngine.Vector2(0, -4), false, 1f });
int initialVersion = (int)version.GetValue(cursor);
for (int i = 0; i < 100; i++)
    state.Invoke(cursor, new object[] { 15, circle, new UnityEngine.Vector2(100 + i, 200),
        new UnityEngine.Vector2(4, 0), new UnityEngine.Vector2(0, -4), false, 1f });
Check((int)version.GetValue(cursor) == initialVersion, "Mouse motion never rebuilds contour geometry");
state.Invoke(cursor, new object[] { 15, circle, new UnityEngine.Vector2(200, 200),
    new UnityEngine.Vector2(1, 0), new UnityEngine.Vector2(0, -1), false, 1f });
Check((int)version.GetValue(cursor) > initialVersion, "Zoom invalidates geometry and LOD");
return $"Pencil cursor checks passed: {checks}. No asset or Undo changes.";
