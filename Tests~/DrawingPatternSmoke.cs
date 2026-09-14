var checks = 0;
var type = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var normalize = type.GetMethod("NormalizeSettings", flags);
var build = type.GetMethod("BuildPatternStamps", flags);
var stampsField = type.GetField("patternStamps", flags);
var begin = type.GetMethod("BeginStroke", flags);
var clipped = type.GetField("clipStrokeToInitialShape", flags);
var point = new UnityEngine.Vector2(0.61f, 0.72f);

void Check(bool condition, string message)
{
    if (!condition) throw new System.Exception(message);
    checks++;
}
void Normalize(DCFApixels.WhimTex.DrawingLayerBehaviour layer) => normalize.Invoke(layer, null);
System.Collections.IList Stamps(DCFApixels.WhimTex.DrawingLayerBehaviour layer)
{
    build.Invoke(layer, new object[] { point, 512, 256 });
    return (System.Collections.IList)stampsField.GetValue(layer);
}
UnityEngine.Vector2 Center(object stamp) =>
    (UnityEngine.Vector2)stamp.GetType().GetField("center").GetValue(stamp);

var inactiveMirror = new DCFApixels.WhimTex.DrawingLayerBehaviour { mirrorAcrossVerticalAxis = true };
Normalize(inactiveMirror);
Check(inactiveMirror.repeatMode == DCFApixels.WhimTex.PaintRepeatMode.None, "Stored axis flags do not enable Mirror");

var mirror = new DCFApixels.WhimTex.DrawingLayerBehaviour
{
    repeatMode = DCFApixels.WhimTex.PaintRepeatMode.Mirror,
    mirrorAcrossVerticalAxis = true,
    mirrorAcrossHorizontalAxis = true,
    patternCenter = new UnityEngine.Vector2(0.4f, 0.6f),
    repeatBoundaryMode = DCFApixels.WhimTex.PaintRepeatBoundaryMode.Clip
};
Normalize(mirror);
var mirrored = Stamps(mirror);
Check(mirrored.Count == 4, "Mirror XY produces four stamps");
var expected = new[]
{
    point,
    new UnityEngine.Vector2(0.19f, 0.72f),
    new UnityEngine.Vector2(0.61f, 0.48f),
    new UnityEngine.Vector2(0.19f, 0.48f)
};
for (int i = 0; i < expected.Length; i++)
    Check((Center(mirrored[i]) - expected[i]).sqrMagnitude < 0.00000001f, "Mirror uses movable center");
begin.Invoke(mirror, new object[] { point });
Check((bool)clipped.GetValue(mirror), "Mirror Clip anchors to its initial region");
var restored = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.DrawingLayerBehaviour>(
    UnityEngine.JsonUtility.ToJson(mirror));
Normalize(restored);
Check(restored.repeatMode == mirror.repeatMode && Stamps(restored).Count == 4, "Mirror survives JSON round-trip");

foreach (DCFApixels.WhimTex.PaintRepeatMode mode in System.Enum.GetValues(typeof(DCFApixels.WhimTex.PaintRepeatMode)))
{
    if (mode == DCFApixels.WhimTex.PaintRepeatMode.Mirror) continue;
    foreach (DCFApixels.WhimTex.PaintRepeatElementMode elements in System.Enum.GetValues(typeof(DCFApixels.WhimTex.PaintRepeatElementMode)))
    {
        var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
        {
            repeatMode = mode, repeatCount = 8, repeatSecondaryCount = 3,
            repeatElementMode = elements
        };
        Normalize(layer);
        var baseline = Stamps(layer);
        var centers = new UnityEngine.Vector2[baseline.Count];
        for (int i = 0; i < centers.Length; i++) centers[i] = Center(baseline[i]);
        layer.mirrorAcrossVerticalAxis = true;
        layer.mirrorAcrossHorizontalAxis = true;
        layer.mirrorAngle = 37f;
        Normalize(layer);
        var actual = Stamps(layer);
        Check(actual.Count == centers.Length, mode + ": hidden mirrors cannot add stamps");
        for (int i = 0; i < centers.Length; i++)
            Check(Center(actual[i]) == centers[i], mode + ": hidden mirrors cannot change stamp positions");
        int expectedCount = mode == DCFApixels.WhimTex.PaintRepeatMode.None ? 1 :
            mode == DCFApixels.WhimTex.PaintRepeatMode.Grid ? 24 : 8;
        Check(actual.Count == expectedCount, mode + ": repeat count");
        bool underCursor = false;
        foreach (var center in centers) underCursor |= (center - point).sqrMagnitude < 0.00000001f;
        Check(underCursor, mode + ": source stamp stays under cursor");
    }
}
var legacyRadial = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.DrawingLayerBehaviour>(
    "{\"mirrorAcrossVerticalAxis\":true,\"mirrorAcrossHorizontalAxis\":true,\"repeatMode\":4,\"repeatCount\":8}");
Normalize(legacyRadial);
Check(legacyRadial.repeatMode == DCFApixels.WhimTex.PaintRepeatMode.Radial && Stamps(legacyRadial).Count == 8,
    "Legacy Radial+Mirror keeps Radial without extra reflections");
var sectorMethod = type.GetMethod("GetRadialRepeatSector", flags);
foreach (float degrees in new[] { 0f, 17.5f, 90f, 359f, 360f })
foreach (int count in new[] { 3, 8 })
foreach (DCFApixels.WhimTex.PaintRepeatElementMode elements in System.Enum.GetValues(typeof(DCFApixels.WhimTex.PaintRepeatElementMode)))
{
    var radial = new DCFApixels.WhimTex.DrawingLayerBehaviour
    {
        repeatMode = DCFApixels.WhimTex.PaintRepeatMode.Radial,
        repeatCount = count, radialStartAngle = degrees, repeatElementMode = elements,
        repeatBoundaryMode = DCFApixels.WhimTex.PaintRepeatBoundaryMode.Clip
    };
    Normalize(radial);
    var rotatedStamps = Stamps(radial);
    Check(rotatedStamps.Count == count, "Rotated Radial stamp count");
    bool underCursor = false;
    foreach (var stamp in rotatedStamps)
    {
        var stampCenter = Center(stamp);
        underCursor |= (stampCenter - point).sqrMagnitude < 0.00000001f;
        var delta = UnityEngine.Vector2.Scale(stampCenter - radial.patternCenter, new UnityEngine.Vector2(512f, 256f));
        float clipCenter = (float)stamp.GetType().GetField("clipAngleCenter").GetValue(stamp);
        float halfWidth = (float)stamp.GetType().GetField("clipAngleHalfWidth").GetValue(stamp);
        float angleDelta = UnityEngine.Mathf.Atan2(delta.y, delta.x) - clipCenter;
        float wrappedDelta = UnityEngine.Mathf.Atan2(UnityEngine.Mathf.Sin(angleDelta), UnityEngine.Mathf.Cos(angleDelta));
        Check(UnityEngine.Mathf.Abs(wrappedDelta) <= halfWidth + 0.00001f, "Rotated stamp inside GPU clip wedge");
    }
    Check(underCursor, "Rotated Radial stays under cursor");
    float start = -UnityEngine.Mathf.PI + UnityEngine.Mathf.Repeat(degrees, 360f) * UnityEngine.Mathf.Deg2Rad;
    int[] sectors = new int[3];
    float[] offsets = { 0.15f, 0.85f, 1.15f };
    for (int i = 0; i < offsets.Length; i++)
    {
        float angle = start + offsets[i] * UnityEngine.Mathf.PI * 2f / count;
        var uv = radial.patternCenter + new UnityEngine.Vector2(
            UnityEngine.Mathf.Cos(angle) * 0.1f, UnityEngine.Mathf.Sin(angle) * 0.2f);
        sectors[i] = (int)sectorMethod.Invoke(radial, new object[] { uv, count, 512, 256 });
    }
    Check(sectors[0] == sectors[1] && sectors[0] != sectors[2], "CPU Clip follows rotated boundaries");
    var copy = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.DrawingLayerBehaviour>(UnityEngine.JsonUtility.ToJson(radial));
    Check(copy.radialStartAngle == degrees, "Start angle survives JSON round-trip");
}
UnityEngine.Vector2 Rotate(UnityEngine.Vector2 value, float angle)
{
    float sine = UnityEngine.Mathf.Sin(angle), cosine = UnityEngine.Mathf.Cos(angle);
    return new UnityEngine.Vector2(cosine * value.x - sine * value.y, sine * value.x + cosine * value.y);
}
foreach (float degrees in new[] { 0f, 17.5f, 45f, 90f, 360f })
foreach (var size in new[] { new UnityEngine.Vector2Int(512, 256), new UnityEngine.Vector2Int(256, 512) })
foreach (int axes in new[] { 1, 2, 3 })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
    {
        repeatMode = DCFApixels.WhimTex.PaintRepeatMode.Mirror,
        mirrorAcrossVerticalAxis = (axes & 1) != 0,
        mirrorAcrossHorizontalAxis = (axes & 2) != 0,
        mirrorAngle = degrees,
        radialStartAngle = 123f,
        patternCenter = new UnityEngine.Vector2(0.4f, 0.6f)
    };
    Normalize(layer);
    build.Invoke(layer, new object[] { point, size.x, size.y });
    var actual = (System.Collections.IList)stampsField.GetValue(layer);
    Check(actual.Count == (axes == 3 ? 4 : 2), "Rotated Mirror stamp count");
    Check(Center(actual[0]) == point, "Rotated Mirror keeps the original stamp under the cursor");
    float angle = UnityEngine.Mathf.Repeat(degrees, 360f) * UnityEngine.Mathf.Deg2Rad;
    var pixels = UnityEngine.Vector2.Scale(point - layer.patternCenter, (UnityEngine.Vector2)size);
    var local = Rotate(pixels, -angle);
    int index = 1;
    foreach (int axis in new[] { 1, 2, 3 })
    {
        if ((axes & axis) != axis) continue;
        var reflected = Rotate(new UnityEngine.Vector2((axis & 1) != 0 ? -local.x : local.x,
            (axis & 2) != 0 ? -local.y : local.y), angle);
        var expectedUv = layer.patternCenter + new UnityEngine.Vector2(reflected.x / size.x, reflected.y / size.y);
        Check((Center(actual[index++]) - expectedUv).sqrMagnitude < 0.00000001f,
            "Rotated Mirror reflects in pixels rather than stretched UV coordinates");
    }
    var copy = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.DrawingLayerBehaviour>(UnityEngine.JsonUtility.ToJson(layer));
    Check(copy.mirrorAngle == degrees && copy.radialStartAngle == 123f, "Mirror and Radial angles persist independently");
    var axisDirection = (UnityEngine.Vector2)type.GetMethod("GetMirrorAxisDirection", flags).Invoke(layer, new object[] { true });
    var onAxis = layer.patternCenter + new UnityEngine.Vector2(axisDirection.x * 10f / size.x, axisDirection.y * 10f / size.y);
    layer.mirrorAcrossVerticalAxis = true;
    layer.mirrorAcrossHorizontalAxis = false;
    build.Invoke(layer, new object[] { onAxis, size.x, size.y });
    Check(((System.Collections.IList)stampsField.GetValue(layer)).Count == 1, "Points on the rotated axis are not painted twice");
}
var insideMethod = type.GetMethod("IsStrokePointInsideRepeatShape", flags);
var clipMethod = type.GetMethod("TryClipStrokeSegmentToRepeatShape", flags);
foreach (float degrees in new[] { 0f, 37f, 90f })
foreach (int axes in new[] { 0, 1, 2, 3 })
{
    var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
    {
        repeatMode = DCFApixels.WhimTex.PaintRepeatMode.Mirror,
        mirrorAngle = degrees,
        mirrorAcrossVerticalAxis = (axes & 1) != 0,
        mirrorAcrossHorizontalAxis = (axes & 2) != 0,
        repeatBoundaryMode = DCFApixels.WhimTex.PaintRepeatBoundaryMode.Clip
    };
    Normalize(layer);
    UnityEngine.Vector2 ToUv(float x, float y)
    {
        var offset = Rotate(new UnityEngine.Vector2(x, y), degrees * UnityEngine.Mathf.Deg2Rad);
        return layer.patternCenter + new UnityEngine.Vector2(offset.x / 512f, offset.y / 256f);
    }
    bool Inside(UnityEngine.Vector2 uv) => (bool)insideMethod.Invoke(layer, new object[] { uv, 512, 256 });
    var start = ToUv(25, 30);
    begin.Invoke(layer, new object[] { start });
    Check(Inside(start), "Mirror Clip accepts the starting point");
    Check(Inside(ToUv(-25, 30)) == ((axes & 1) == 0), "Only enabled X reflection clips crossing its rotated axis");
    Check(Inside(ToUv(25, -30)) == ((axes & 2) == 0), "Only enabled Y reflection clips crossing its rotated axis");
    if (axes != 0)
    {
        var target = (axes & 1) != 0 ? ToUv(-25, 30) : ToUv(25, -30);
        object[] args = { start, target, 512, 256, UnityEngine.Vector2.zero };
        Check((bool)clipMethod.Invoke(layer, args) && Inside((UnityEngine.Vector2)args[4]), "Mirror segment ends at the initial region boundary");
        var endPixels = UnityEngine.Vector2.Scale((UnityEngine.Vector2)args[4] - layer.patternCenter, new UnityEngine.Vector2(512, 256));
        var local = Rotate(endPixels, -degrees * UnityEngine.Mathf.Deg2Rad);
        Check(UnityEngine.Mathf.Abs((axes & 1) != 0 ? local.x : local.y) < 0.01f, "Clipped endpoint reaches the rotated axis");
        build.Invoke(layer, new object[] { start, 512, 256 });
        var copies = (System.Collections.IList)stampsField.GetValue(layer);
        Check(copies.Count == (axes == 3 ? 4 : 2), "Mirror Clip retains every reflected copy");
        foreach (float x in new[] { -40f, 0f, 40f })
        foreach (float y in new[] { -40f, 0f, 40f })
        {
            var sample = UnityEngine.Vector2.Scale(ToUv(x, y) - layer.patternCenter, new UnityEngine.Vector2(512, 256));
            int owners = 0;
            foreach (var stamp in copies)
            {
                Check((int)stamp.GetType().GetField("clipMode").GetValue(stamp) == 3, "Mirror stamps use rotated half-plane masks");
                var mask = (UnityEngine.Vector4)stamp.GetType().GetField("clipRect").GetValue(stamp);
                float sx = sample.x * mask.x + sample.y * mask.y;
                float sy = -sample.x * mask.y + sample.y * mask.x;
                if ((mask.z == 0 || (sx >= 0) == (mask.z > 0)) && (mask.w == 0 || (sy >= 0) == (mask.w > 0))) owners++;
            }
            Check(owners == 1, "Mirror masks partition the canvas without overlapping seams");
        }
    }
    layer.repeatBoundaryMode = DCFApixels.WhimTex.PaintRepeatBoundaryMode.Continue;
    begin.Invoke(layer, new object[] { start });
    Check(Inside(ToUv(-25, -30)), "Mirror Continue crosses both axes");
}
return "Drawing pattern checks passed: " + checks + ". No assets or GPU resources created.";
