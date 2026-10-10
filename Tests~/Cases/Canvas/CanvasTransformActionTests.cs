using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class CanvasTransformActionTests
{
    public static string Run() => WhimTex.Tests.TestContext.Run("Transform toolbar actions", context =>
    {
        var window = typeof(WhimTexWindow);
        var actionType = window.Assembly.GetType("DCFApixels.WhimTex.CanvasTransformAction", true);
        var apply = window.GetMethod("TryCanvasTransformAction", BindingFlags.Static | BindingFlags.NonPublic);
        Vector2 size = new Vector2(320, 160);
        TextureTransform Action(TextureTransform value, string name)
        {
            object[] args = { value, size, Enum.Parse(actionType, name), default(TextureTransform) };
            context.True((bool)apply.Invoke(null, args), name + " accepts a valid transform");
            var next = (TextureTransform)args[3];
            context.Equal(value.tiling, next.tiling, name + " preserves tiling");
            context.Equal(value.storage, next.storage, name + " preserves transform storage");
            return next;
        }
        void Point(Double2 expected, Double2 actual, string name)
        {
            context.Near(expected.x, actual.x, 1e-10, name + " X");
            context.Near(expected.y, actual.y, 1e-10, name + " Y");
        }
        var samples = new[] { new Double2(0, 0), new Double2(1, 0), new Double2(1, 1), new Double2(0, 1), new Double2(.3, .7) };
        void Same(TextureTransform a, TextureTransform b, string name)
        {
            foreach (var sample in samples) Point(a.ToMatrix(size.x, size.y).Point(sample), b.ToMatrix(size.x, size.y).Point(sample), name);
        }
        var toCanvasPoint = window.GetNestedType("CanvasTransformManipulator", BindingFlags.NonPublic)
            .GetMethod("ToCanvasPoint", BindingFlags.Static | BindingFlags.NonPublic);
        Double2 Screen(TextureTransform value, Vector2 uv)
        {
            Vector2 pixels = Vector2.Scale(value.Map(uv, size), size);
            return (Vector2)toCanvasPoint.Invoke(null, new object[] { pixels, new Rect(Vector2.zero, size), size });
        }
        foreach (bool projective in new[] { false, true })
        {
            var neutral = TextureTransform.Default;
            if (projective) context.True(neutral.TrySetMatrix(neutral.ToMatrix(size.x, size.y)), "Screen-direction projective input is valid");
            Vector2 above = new Vector2(.5f, .75f), right = new Vector2(.625f, .5f);
            Point(new Double2(160, 40), Screen(neutral, above), "Top marker starts above the screen pivot");
            Point(new Double2(200, 80), Screen(neutral, right), "Right marker starts to the right of the screen pivot");
            var leftTurn = Action(neutral, "RotateLeft");
            Point(new Double2(120, 80), Screen(leftTurn, above), "Left turn moves the top marker left on screen");
            Point(new Double2(160, 40), Screen(leftTurn, right), "Left turn moves the right marker up on screen");
            var rightTurn = Action(neutral, "RotateRight");
            Point(new Double2(200, 80), Screen(rightTurn, above), "Right turn moves the top marker right on screen");
            Point(new Double2(160, 120), Screen(rightTurn, right), "Right turn moves the right marker down on screen");
        }
        var regular = TextureTransform.Default;
        regular.pivot = new Double2(.2, .8);
        regular.position = new Double2(23, -17);
        regular.scale = new Double2(.7, -.4);
        regular.rotation = 31;
        regular.tiling = TransformTilingMode.Mirror;
        var perspective = regular;
        var distorted = regular.ToMatrix(size.x, size.y);
        distorted.m20 = .12; distorted.m21 = -.08;
        context.True(perspective.TrySetMatrix(distorted), "Perspective input is valid");
        var nested = perspective;
        var parent = TextureTransform.Default;
        parent.rotation = -23; parent.scale = new Double2(1.2, .6);
        context.True(nested.TrySetMatrix(parent.ToMatrix(size.x, size.y) * distorted), "Nested world transform is valid");
        foreach (var original in new[] { regular, perspective, nested })
        {
            var matrix = original.ToMatrix(size.x, size.y);
            var pivot = matrix.Point(original.pivot);
            var reset = Action(original, "CenterPivot");
            context.Equal(new Double2(.5, .5), reset.pivot, "Pivot returns to frame center");
            Same(original, reset, "Reset pivot does not move any image point");
            var centered = Action(original, "CenterOnCanvas");
            Point(new Double2(.5, .5), centered.ToMatrix(size.x, size.y).Point(centered.pivot), "Centering places pivot at canvas center");
            foreach (var name in new[] { "CenterOnCanvas", "FlipHorizontal", "FlipVertical", "RotateLeft", "RotateRight" })
            {
                var next = Action(original, name);
                context.Equal(original.pivot, next.pivot, name + " preserves local pivot");
                if (name != "CenterOnCanvas") Point(pivot, next.ToMatrix(size.x, size.y).Point(next.pivot), name + " keeps world pivot fixed");
                foreach (var sample in samples)
                {
                    var p = matrix.Point(sample);
                    double x = (p.x - pivot.x) * size.x, y = (p.y - pivot.y) * size.y;
                    Double2 expected;
                    switch (name)
                    {
                        case "CenterOnCanvas": expected = p + new Double2(.5 - pivot.x, .5 - pivot.y); break;
                        case "FlipHorizontal": expected = new Double2(2 * pivot.x - p.x, p.y); break;
                        case "FlipVertical": expected = new Double2(p.x, 2 * pivot.y - p.y); break;
                        case "RotateLeft": expected = pivot + new Double2(-y / size.x, x / size.y); break;
                        default: expected = pivot + new Double2(y / size.x, -x / size.y); break;
                    }
                    Point(expected, next.ToMatrix(size.x, size.y).Point(sample), name + " follows canvas axes, not local axes");
                }
            }
            Same(original, Action(Action(original, "FlipHorizontal"), "FlipHorizontal"), "Two horizontal flips restore image");
            Same(original, Action(Action(original, "FlipVertical"), "FlipVertical"), "Two vertical flips restore image");
            Same(original, Action(Action(original, "RotateLeft"), "RotateRight"), "Opposite quarter turns restore image");
            var turn = original;
            for (int i = 0; i < 4; i++) turn = Action(turn, "RotateRight");
            Same(original, turn, "Four quarter turns restore image");
        }
        foreach (var invalidSize in new[] { Vector2.zero, new Vector2(float.NaN, 160), new Vector2(320, float.PositiveInfinity) })
        {
            object[] args = { regular, invalidSize, Enum.Parse(actionType, "RotateLeft"), default(TextureTransform) };
            context.True(!(bool)apply.Invoke(null, args), "Invalid canvas dimensions are rejected");
        }
        var singular = regular; singular.scale.x = 0;
        object[] bad = { singular, size, Enum.Parse(actionType, "CenterPivot"), default(TextureTransform) };
        context.True(!(bool)apply.Invoke(null, bad), "Singular transform is rejected without mutation");
        var alreadyCentered = regular; alreadyCentered.pivot = new Double2(.5, .5);
        context.Equal(alreadyCentered, Action(alreadyCentered, "CenterPivot"), "Repeated pivot reset is an exact no-op");
    });
}
