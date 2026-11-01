using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class GradientBezierTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public sealed class Owner : ScriptableObject { public WhimTexGradient gradient; }

    public static string Run() => TestContext.Run("Custom weighted Bezier gradients", test =>
    {
        NativeParity(test);
        Editing(test);
        Persistence(test);
        Rendering(test);
    });

    static WhimTexGradient Create()
    {
        var keys = new List<WhimTexGradientCurveKey>();
        for (int c = 0; c < 4; c++)
        for (int i = 0; i < 3; i++)
            keys.Add(new WhimTexGradientCurveKey { id = c * 3 + i + 1, channel = c,
                time = i == 0 ? 0 : i == 1 ? .375f : 1,
                value = c == 3 ? (i == 1 ? .8f : .2f) : (i == 0 ? -.2f : i == 1 ? 2.4f : .3f),
                inTangent = i == 1 ? 1.5f : -.6f, outTangent = i == 0 ? 2.5f : -.7f,
                inWeight = .18f, outWeight = .72f, broken = true, weightedMode = WeightedMode.Both });
        var g = new WhimTexGradient();
        g.SetCustomData(keys.ToArray(), WhimTexGradientColorMode.Gamma);
        g.Interpolation = WhimTexGradientInterpolation.Custom;
        return g;
    }
    static void Compare(TestContext t, WhimTexGradient a, WhimTexGradient b, double tolerance, bool reverse = false)
    {
        for (int i = 0; i <= 512; i++)
        for (int c = 0; c < 4; c++)
            t.Near(a.EvaluateCustomChannel(c, reverse ? 1 - i / 512f : i / 512f),
                b.EvaluateCustomChannel(c, i / 512f), tolerance, "Channel shape " + c + "/" + i);
    }
    static void NativeParity(TestContext t)
    {
        foreach (WeightedMode weights in Enum.GetValues(typeof(WeightedMode)))
        foreach (var mode in new[] { WhimTexGradientTangentMode.Free, WhimTexGradientTangentMode.ClampedAuto,
            WhimTexGradientTangentMode.Linear, WhimTexGradientTangentMode.Constant })
        {
            var g = Create(); var keys = g.CustomKeys;
            for (int i = 0; i < keys.Length; i++) { keys[i].weightedMode = weights; keys[i].inMode = keys[i].outMode = mode; }
            g.SetCustomData(keys, g.CustomColorMode);
            for (int c = 0; c < 4; c++)
            {
                var native = new Keyframe[3];
                for (int i = 0; i < 3; i++)
                {
                    var k = keys[c * 3 + i];
                    native[i] = new Keyframe(k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight) { weightedMode = weights };
                }
                var curve = new AnimationCurve(native);
                var nativeMode = mode == WhimTexGradientTangentMode.Free ? AnimationUtility.TangentMode.Free :
                    mode == WhimTexGradientTangentMode.ClampedAuto ? AnimationUtility.TangentMode.ClampedAuto :
                    mode == WhimTexGradientTangentMode.Linear ? AnimationUtility.TangentMode.Linear : AnimationUtility.TangentMode.Constant;
                for (int i = 0; i < 3; i++)
                {
                    AnimationUtility.SetKeyBroken(curve, i, true);
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, nativeMode);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, nativeMode);
                }
                for (int i = 0; i <= 512; i++)
                    t.Near(curve.Evaluate(i / 512f), g.EvaluateCustomChannel(c, i / 512f), 0, "Native weighted curve parity");
            }
        }
        var asymmetric = Create(); var data = asymmetric.CustomKeys;
        data[1].inMode = WhimTexGradientTangentMode.Linear;
        data[1].outMode = WhimTexGradientTangentMode.Constant;
        asymmetric.SetCustomData(data, asymmetric.CustomColorMode);
        t.Near((data[1].value - data[0].value) / (data[1].time - data[0].time), asymmetric.CustomKeyTangent(2, true), .000001, "Independent left Linear");
        t.Near(data[1].value, asymmetric.EvaluateCustomChannel(0, .7f), 0, "Independent right Constant holds previous key");
        var narrow = Create(); data = narrow.CustomKeys; data[1].time = .000002f;
        narrow.SetCustomData(data, narrow.CustomColorMode);
        for (int i = 0; i <= 20; i++) t.True(float.IsFinite(narrow.EvaluateCustomChannel(0, .000002f * i / 20)), "Narrow interval stays finite");
    }
    static void Editing(TestContext t)
    {
        var g = Create(); var initial = g.Clone();
        g.SetCustomHandle(2, false, .8f, 3.25f);
        t.True(g.TryGetCustomKey(2, out var key), "Edited key retained");
        t.Near((.8f - .375f) / .625f, key.outWeight, .000001, "Handle position stores actual length");
        t.Near((3.25f - 2.4f) / (.8f - .375f), key.outTangent, .000001, "Handle position stores slope");
        t.Near(initial.CustomKeyTangent(2, true), g.CustomKeyTangent(2, true), 0, "Broken left side unchanged");
        key.broken = false; g.UpdateCustomKey(key);
        g.SetCustomHandle(2, true, .1f, 2);
        g.TryGetCustomKey(2, out key);
        t.Near(key.inTangent, key.outTangent, 0, "Free Smooth shares slope");
        t.Near((.8f - .375f) / .625f, key.outWeight, .000001, "Free Smooth keeps opposite length");
        g.SetCustomHandle(2, true, .2f, 1, true); g.TryGetCustomKey(2, out key);
        t.True(key.broken && key.inTangent != key.outTangent, "Alt breaks tangent relation");
        g.SetCustomHandle(2, true, 5, 3); g.TryGetCustomKey(2, out key);
        t.True(float.IsFinite(key.inTangent) && key.inWeight > 0, "Wrong-side drag stays finite and on correct side");
        t.Near(0, g.CustomHandleSpan(1, true), 0, "No phantom handle before first key");
        t.Near(0, g.CustomHandleSpan(3, false), 0, "No phantom handle after last key");

        var split = Create(); var before = split.Clone();
        split.AddCustomKey(0, .1875f); split.AddCustomKey(0, .5625f);
        Compare(t, before, split, .0001);
        var interior = Create(); var interiorKeys = interior.CustomKeys;
        for (int i = 0; i < interiorKeys.Length; i++) interiorKeys[i].time = .2f + .6f * interiorKeys[i].time;
        interior.SetCustomData(interiorKeys, interior.CustomColorMode); var interiorBefore = interior.Clone();
        interior.AddCustomKey(0, .1f); interior.AddCustomKey(0, .9f);
        Compare(t, interiorBefore, interior, .00005);
        var reversed = before.Clone(); reversed.Reverse(); Compare(t, before, reversed, .00003, true);
        reversed.Reverse(); Compare(t, before, reversed, .00003);
        foreach (var mode in new[] { WhimTexGradientTangentMode.Constant, WhimTexGradientTangentMode.ConstantNext })
        {
            var step = Create(); var keys = step.CustomKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].inMode = keys[i].outMode = mode;
            step.SetCustomData(keys, step.CustomColorMode); var copy = step.Clone(); step.Reverse(); Compare(t, copy, step, .000001, true);
        }
        var fixedGradient = new WhimTexGradient { Interpolation = WhimTexGradientInterpolation.Fixed };
        fixedGradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.green, .5f), new GradientColorKey(Color.blue, 1) },
            new[] { new GradientAlphaKey(.1f, 0), new GradientAlphaKey(.9f, 1) });
        var fixedOriginal = fixedGradient.Clone(); fixedGradient.Interpolation = WhimTexGradientInterpolation.Custom;
        for (int i = 0; i <= 256; i++) for (int c = 0; c < 4; c++)
            t.Near(fixedOriginal.Evaluate(i / 256f)[c], fixedGradient.Evaluate(i / 256f)[c], .00001, "Fixed conversion retains next-key steps");

        before.Evaluate(.3f); long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) before.Evaluate(i / 10000f);
        t.Equal(bytes, GC.GetAllocatedBytesForCurrentThread(), "Warm evaluation allocates no managed memory");
    }
    static void Persistence(TestContext t)
    {
        var g = Create();
        var unityCopy = JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(g));
        t.True(g.Equals(unityCopy), "Unity serialization retains all handle metadata"); Compare(t, g, unityCopy, 0);
        var payload = typeof(WhimTexApi).GetMethod("WriteGradient", Flags).Invoke(null, new object[] { g });
        var apiCopy = (WhimTexGradient)typeof(WhimTexApi).GetMethod("ReadGradient", Flags).Invoke(null,
            new object[] { payload, WhimTexGradientColorMode.Perceptual, 107f });
        t.True(g.Equals(apiCopy), "Agent read/write preserves weights and side modes"); Compare(t, g, apiCopy, 0);
        var serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        using (var container = new WhimTexDocumentContainer())
        {
            var bytes = (byte[])serializer.GetMethod("Serialize", Flags).Invoke(null, new object[] { g, container });
            var result = serializer.GetMethod("Deserialize", Flags).Invoke(null, new object[] { bytes, container, typeof(WhimTexGradient), null, false });
            var copy = (WhimTexGradient)result.GetType().GetProperty("Model", Flags).GetValue(result);
            t.True(g.Equals(copy), "Native TIFF model retains weighted handles"); Compare(t, g, copy, 0);
        }
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        var owner = ScriptableObject.CreateInstance<Owner>();
        try
        {
            doc.layers.Add(new GradientLayerBehaviour { gradient = g.Clone() });
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(doc, null);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using (var copy = WhimTexDocumentJson.Read(json, false))
                    t.True(g.Equals(((GradientLayerBehaviour)copy.Document.layers[0].Behaviour).gradient), "Document JSON roundtrip " + mode);
            }
            var obsolete = JsonUtility.ToJson(g).Replace("\"customCurveFormat\":1", "\"customCurveFormat\":0");
            bool rejected = false;
            try { JsonUtility.FromJson<WhimTexGradient>(obsolete).Evaluate(.5f); } catch (ArgumentException) { rejected = true; }
            t.True(rejected, "Old Custom format is rejected explicitly");
            owner.gradient = g.Clone(); Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(owner, "Test weighted handle");
            owner.gradient.SetCustomHandle(2, false, .7f, 1.2f); var after = owner.gradient.Clone();
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); t.True(g.Equals(owner.gradient), "Undo restores all key data");
            Undo.PerformRedo(); t.True(after.Equals(owner.gradient), "Redo restores all key data");
        }
        finally { Undo.ClearUndo(owner); UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(doc); Undo.IncrementCurrentGroup(); }
    }
    static void Rendering(TestContext t)
    {
        var g = Create();
        using (var lut = new WhimTexGradientTexture())
        {
            var texture = lut.GetTexture(g, ColorSpace.Gamma);
            for (int i = 0; i < texture.width; i++) for (int c = 0; c < 4; c++)
                t.Near(g.Evaluate(i / (float)(texture.width - 1))[c], texture.GetPixel(i, 0)[c], .003, "HDR/alpha LUT agrees with curve");
            int count = lut.BakeCount; var before = g.Clone();
            var key = g.CustomKeys[0]; key.outWeight = .2f; g.UpdateCustomKey(key);
            t.True(!before.Equals(g) && before.GetHashCode() != g.GetHashCode(), "Weight participates in identity");
            lut.GetTexture(g, ColorSpace.Gamma); t.Equal(count + 1, lut.BakeCount, "Weight change invalidates LUT");
        }
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = 48; doc.height = 32;
        var behavior = new GradientLayerBehaviour { gradient = g, gradientType = GradientLayerBehaviour.GradientType.Linear };
        Layer source = behavior, group = new GroupLayerBehaviour();
        var effect = new BlurLayerBehaviour();
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Color[] Pixels(Texture2D texture) { try { return texture.GetPixels(); } finally { UnityEngine.Object.DestroyImmediate(texture); } }
        void Same(Color[] a, Color[] b, bool encoded = false)
        {
            t.Equal(a.Length, b.Length, "Render dimensions");
            for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++)
                t.Near((encoded ? a[i].gamma : a[i])[c], b[i][c], .006, "Composite/export/Target parity");
        }
        try
        {
            doc.layers.Add(source); var baseline = Pixels(doc.ComposeCanvas());
            var export = typeof(WhimTexDocument).GetMethod("RenderPsdPixels", Flags);
            Same(baseline, Pixels((Texture2D)export.Invoke(doc, new object[] { source, false })), true);
            group.children.Add(source); doc.layers.Clear(); doc.layers.Add(group);
            group.compositing = GroupCompositing.Isolated; Same(baseline, Pixels(doc.ComposeCanvas()));
            group.compositing = GroupCompositing.PassThrough; Same(baseline, Pixels(doc.ComposeCanvas()));
            doc.layers.Clear(); doc.layers.Add(effect); doc.layers.Add(source);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(doc, null);
            effect.inputMode = EffectInputMode.Specific; effect.TargetLayerId = source.Id;
            Same(baseline, Pixels((Texture2D)export.Invoke(doc, new object[] { (Layer)effect, true })), true);
            doc.layers.Clear(); doc.layers.Add(source); source.clippingMask = true;
            doc.layers.Add(new ColorFillLayerBehaviour { color = new Color(0, 0, 0, .5f) });
            foreach (var pixel in Pixels(doc.ComposeCanvas())) t.True(pixel.a >= 0 && pixel.a <= .501f, "Clipping retains alpha bounds");
            source.clippingMask = false;
            var thumb = behavior.GetPreviewTexture(32); var key = g.CustomKeys[0]; key.outWeight = .9f; g.UpdateCustomKey(key);
            t.True(behavior.GetPreviewTexture(32) != thumb, "Handle change invalidates layer thumbnail");
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            typeof(GradientLayerBehaviour).GetMethod("ReleaseTransientResources", Flags).Invoke(behavior, null);
            typeof(BlurLayerBehaviour).GetMethod("ReleaseTransientResources", Flags).Invoke(effect, null);
            doc.layers.Clear(); group.children.Clear(); UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
