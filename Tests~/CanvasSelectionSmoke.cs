// Opt-in after manual compilation. Pure selection data; no assets, windows or Undo changes.
var assembly = typeof(DCFApixels.SpriteEditor.TextureCompositor).Assembly;
var type = assembly.GetType("DCFApixels.SpriteEditor.CanvasSelection", true);
var combine = assembly.GetType("DCFApixels.SpriteEditor.SelectionCombine", true);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var selection = System.Activator.CreateInstance(type, flags, null, new object[] { 8, 8 }, null);
int checks = 0;
object Mode(string name) => System.Enum.Parse(combine, name);
object Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(selection, args);
byte[] Mask() => (byte[])type.GetProperty("Coverage", flags).GetValue(selection);
int Count() { int n = 0; foreach (byte b in Mask()) if (b != 0) n++; return n; }
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Rect(float x0, float y0, float x1, float y1, string mode = "Replace", bool wrap = false) =>
    Call("Rectangle", new UnityEngine.Vector2(x0, y0), new UnityEngine.Vector2(x1, y1), Mode(mode), wrap);
float Sample(float x, float y, bool wrap = false) => (float)Call("Sample", new UnityEngine.Vector2(x, y), wrap);
Check(Sample(.5f, .5f) == 1f, "Inactive selection permits painting");
Rect(1, 1, 5, 5);
Check(Count() == 16 && Sample(.2f, .2f) == 1 && Sample(.9f, .9f) == 0, "Rectangle uses canvas pixels");
Check(Sample(-.1f, .2f) == 0 && Sample(float.NaN, .2f) == 0, "Outside and invalid sample coordinates are rejected");
Rect(4, 4, 7, 7, "Add"); Check(Count() == 24, "Add unions coverage");
Rect(0, 0, 4, 8, "Intersect"); Check(Count() == 12, "Intersect preserves overlap");
Rect(0, 0, 8, 8, "Subtract"); Check(Count() == 0 && Sample(.5f, .5f) == 0, "Empty active selection blocks painting");
Call("Clear"); Check(Sample(.5f, .5f) == 1, "Deselect removes painting restriction");
Call("All"); Check(Count() == 64, "Select All");
Call("Invert"); Check(Count() == 0, "Invert full selection");
Rect(-5, -5, 2, 3); Check(Count() == 6, "Ordinary selection clamps to canvas");
Rect(7, 2, 9, 4, "Replace", true);
Check(Count() == 4 && Mask()[2 * 8 + 7] == 255 && Mask()[2 * 8] == 255, "Wrapped rectangle crosses the seam");
Rect(-1, -1, 1, 1, "Replace", true);
Check(Count() == 4 && Mask()[63] == 255 && Mask()[0] == 255, "Negative repeat coordinates wrap");
Rect(-100, -100, 100, 100, "Replace", true); Check(Count() == 64, "Large wrapped rectangle selects the whole canvas");
var points = new System.Collections.Generic.List<UnityEngine.Vector2> {
    new UnityEngine.Vector2(1,1), new UnityEngine.Vector2(5,1), new UnityEngine.Vector2(1,5) };
Call("Polygon", points, Mode("Replace"), false); Check(Count() == 6, "Triangle uses pixel-center coverage");
points = new System.Collections.Generic.List<UnityEngine.Vector2> {
    new UnityEngine.Vector2(0,0), new UnityEngine.Vector2(4,0), new UnityEngine.Vector2(4,2),
    new UnityEngine.Vector2(2,2), new UnityEngine.Vector2(2,4), new UnityEngine.Vector2(0,4) };
Call("Polygon", points, Mode("Replace"), false); Check(Count() == 12, "Concave polygon");
points = new System.Collections.Generic.List<UnityEngine.Vector2> {
    new UnityEngine.Vector2(7,2), new UnityEngine.Vector2(9,2), new UnityEngine.Vector2(9,4), new UnityEngine.Vector2(7,4) };
Call("Polygon", points, Mode("Replace"), true); Check(Count() == 4, "Polygon crosses repeated canvas seam");
var soft = new byte[64]; soft[0] = 128;
Call("Set", soft, Mode("Replace"));
Check(System.Math.Abs(Sample(.01f,.01f) - 128f / 255f) < .001f, "Alpha-derived selection retains soft coverage");
var other = new byte[64]; other[0] = 128;
Call("Set", other, Mode("Intersect")); Check(Mask()[0] == 64, "Soft intersection multiplies coverage");
other = new byte[64]; other[0] = 128;
Call("Set", other, Mode("Subtract")); Check(Mask()[0] == 32, "Soft subtraction");
void Ellipse(float x0, float y0, float x1, float y1, string mode = "Replace", bool wrap = false) =>
    Call("Ellipse", new UnityEngine.Vector2(x0, y0), new UnityEngine.Vector2(x1, y1), Mode(mode), wrap);
Ellipse(0, 0, 8, 8);
Check(Count() == 52 && Mask()[0] == 0 && Sample(.5f, .5f) == 1f, "Ellipse excludes bounding-box corners");
var circle = (byte[])Mask().Clone();
Ellipse(8, 8, 0, 0);
Check(System.Linq.Enumerable.SequenceEqual(circle, Mask()), "Ellipse is independent of drag direction");
Ellipse(0, 0, 8, 8, "Subtract"); Check(Count() == 0, "Ellipse subtraction");
Ellipse(0, 0, 8, 8, "Add"); Check(Count() == 52, "Ellipse addition");
Rect(0, 0, 4, 8);
Ellipse(0, 0, 8, 8, "Intersect"); Check(Count() == 26, "Ellipse intersection");
Ellipse(4, 0, 4, 8); Check(Count() == 0, "Zero-width ellipse is empty");
Ellipse(0, 4, 8, 4); Check(Count() == 0, "Zero-height ellipse is empty");
for (int n = 0; n < 80; n++)
{
    float x0 = n % 9 - 4, y0 = n % 7 - 3;
    float rx = 1 + n % 6, ry = 1 + n % 5;
    bool wrap = (n & 1) != 0;
    Ellipse(x0, y0, x0 + rx * 2, y0 + ry * 2, "Replace", wrap);
    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
    {
        bool expected = false;
        for (int ty = wrap ? -3 : 0; ty <= (wrap ? 3 : 0); ty++)
        for (int tx = wrap ? -3 : 0; tx <= (wrap ? 3 : 0); tx++)
        {
            float dx = (x + .5f + tx * 8 - x0 - rx) / rx;
            float dy = (y + .5f + ty * 8 - y0 - ry) / ry;
            expected |= dx * dx + dy * dy < 1f;
        }
        Check((Mask()[y * 8 + x] != 0) == expected, "Ellipse matches analytic coverage, including clipping and repeats");
    }
}
return "Canvas selection checks passed: " + checks;
