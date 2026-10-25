using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class GradientReverseTests
{
    public static string Run() => TestContext.Run("Gradient reversal", context =>
    {
        foreach (var space in new[] { ColorSpace.Gamma, ColorSpace.Linear })
        foreach (var mode in new[] { WhimTexGradientColorMode.Gamma, WhimTexGradientColorMode.Linear, WhimTexGradientColorMode.Perceptual })
        foreach (float smoothness in new[] { 0f, .37f, 1f })
        {
            var gradient = Create();
            gradient.ColorSpace = space;
            gradient.ColorMode = mode;
            gradient.Smoothness = smoothness;
            var original = gradient.Clone();
            gradient.Evaluate(.35f);
            uint revision = gradient.Revision;
            gradient.Reverse();
            context.True(gradient.Revision != revision, "Reverse invalidates baked previews");
            context.Equal(mode, gradient.ColorMode, "Interpolation preserved");
            context.Equal(space, gradient.ColorSpace, "Color space preserved");
            context.Near(smoothness, gradient.Smoothness, 0, "Smoothness preserved");
            Compare(context, original, gradient, true, mode + "/" + space + "/" + smoothness);
            var serialized = JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(gradient));
            Compare(context, gradient, serialized, false, "Serialization");
            gradient.Reverse();
            Compare(context, original, gradient, false, "Double reversal");
        }

        var fixedGradient = Create();
        fixedGradient.Interpolation = WhimTexGradientInterpolation.Fixed;
        fixedGradient.WrapMode = WhimTexGradientWrapMode.Mirror;
        var fixedColors = fixedGradient.ColorKeys;
        var fixedAlpha = fixedGradient.AlphaKeys;
        fixedGradient.Reverse();
        context.Equal(WhimTexGradientInterpolation.Fixed, fixedGradient.Interpolation, "Fixed mode retained");
        context.Equal(WhimTexGradientWrapMode.Mirror, fixedGradient.WrapMode, "Wrap retained");
        for (int i = 0; i < fixedColors.Length; i++)
        {
            var actual = fixedGradient.ColorKeys[fixedColors.Length - 1 - i];
            context.Equal(fixedColors[i].color, actual.color, "Fixed key color");
            context.Near(1 - fixedColors[i].time, actual.time, .000001, "Fixed key position");
        }
        for (int i = 0; i < fixedAlpha.Length; i++)
        {
            var actual = fixedGradient.AlphaKeys[fixedAlpha.Length - 1 - i];
            context.Near(fixedAlpha[i].alpha, actual.alpha, 0, "Fixed key alpha");
            context.Near(1 - fixedAlpha[i].time, actual.time, .000001, "Fixed alpha position");
        }

        var single = new WhimTexGradient();
        single.SetKeys(new[] { new GradientColorKey(new Color(4, 2, .5f), .2f) },
            new[] { new GradientAlphaKey(.3f, .7f) });
        var singleOriginal = single.Clone();
        single.Reverse();
        Compare(context, singleOriginal, single, true, "Single key tracks");
    });

    private static WhimTexGradient Create()
    {
        var gradient = new WhimTexGradient();
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(.08f, .2f, .4f), 0),
            new GradientColorKey(new Color(.7f, .3f, .15f), .23f),
            new GradientColorKey(new Color(2, 3, 1), .68f),
            new GradientColorKey(new Color(.3f, .6f, .9f), 1)
        }, new[] { new GradientAlphaKey(.1f, .08f), new GradientAlphaKey(.8f, .61f), new GradientAlphaKey(.3f, .94f) });
        gradient.SetMidpoint(false, 0, .2f);
        gradient.SetMidpoint(false, 1, .7f);
        gradient.SetMidpoint(false, 2, .99f);
        gradient.SetMidpoint(true, 0, .01f);
        gradient.SetMidpoint(true, 1, .7f);
        return gradient;
    }

    private static void Compare(TestContext context, WhimTexGradient original, WhimTexGradient actual, bool reverse, string label)
    {
        for (int i = 0; i <= 256; i++)
        {
            float t = i / 256f;
            Color expected = original.Evaluate(reverse ? 1 - t : t);
            Color result = actual.Evaluate(t);
            for (int channel = 0; channel < 4; channel++)
                context.Near(expected[channel], result[channel], .0003, label + " at " + t + ", channel " + channel);
        }
    }
}
