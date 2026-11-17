using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Small exact readbacks only. Private inspection belongs to WhimTex, never Unity.
public static class FrozenPaintInputsTests
{
    const long MiB = 1024 * 1024;
    static RenderTexture Frozen(TiledPaintSurface surface, string name)
    {
        object value = typeof(TiledPaintSurface).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(surface);
        return value == null ? null : (RenderTexture)value.GetType().GetField("Texture").GetValue(value);
    }
    static void Equal(TestContext test, byte[] expected, RenderTexture actual, string label)
    {
        test.True(actual.width * actual.height <= 4096, "Readback remains bounded");
        var read = AsyncGPUReadback.Request(actual, 0); read.WaitForCompletion();
        test.True(!read.hasError, label + " readback");
        var bytes = read.GetData<byte>(); test.Equal(expected.Length, bytes.Length, label + " size");
        for (int i = 0; i < bytes.Length; i++) test.Equal(expected[i], bytes[i], label + " byte " + i);
    }
    static void Fill(Texture2D input, int seed)
    {
        if (input.format == TextureFormat.R8)
        {
            var bytes = input.GetRawTextureData<byte>();
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 37 + seed);
        }
        else
        {
            var colors = new Color[input.width * input.height];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color((i % 7 - 2) * .25f + seed, -.5f + seed * .125f, i % 3 * 1.5f, i % 4 * .25f);
            input.SetPixels(colors);
        }
        input.Apply(false, false);
    }
    static void Throws<T>(TestContext test, Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { test.True(true, label); return; }
        test.True(false, label);
    }
    public static string RunAcrossSurfaces() => TestContext.Run("One idle snapshot owner; interleaved active strokes stay immutable", test =>
    {
        var shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"),
            AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.shader"));
        var frame = new TiledPaintFrame(256, 256, 256, 256, ProjectiveMatrix.Identity);
        var input = new Texture2D(17, 11, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
        using var a = new TiledPaintSurface(frame, shaders);
        using var b = new TiledPaintSurface(frame, shaders);
        using var c = new TiledPaintSurface(frame, shaders);
        try
        {
            Fill(input, 1); var expectedA = input.GetRawTextureData<byte>().ToArray();
            a.Begin(new TiledStrokeSettings(originalStraight: input)); var firstA = Frozen(a, "originalStraight");
            Fill(input, 2); var expectedB = input.GetRawTextureData<byte>().ToArray();
            b.Begin(new TiledStrokeSettings(originalStraight: input)); var firstB = Frozen(b, "originalStraight");
            Equal(test, expectedA, firstA, "Beginning B preserves active A");
            b.Cancel(); a.Validate(); Equal(test, expectedA, firstA, "Cancel B preserves active A");
            a.Cancel(); test.True(firstB == null && firstA != null, "Completing A evicts only idle B");
            b.Begin(new TiledStrokeSettings(originalStraight: input)); var secondB = Frozen(b, "originalStraight");
            test.True(firstA == null, "Switching back drops previous idle allocation");
            c.Begin(default); c.Cancel(); b.Validate();
            Equal(test, expectedB, secondB, "Another surface without inputs preserves active B");
            b.Cancel(); a.Dispose(); c.Dispose();
            test.True(secondB != null, "Disposing unrelated idle surfaces preserves B's cache");
            b.Begin(new TiledStrokeSettings(originalStraight: input));
            test.True(ReferenceEquals(secondB, Frozen(b, "originalStraight")), "Same-owner warm reuse remains");
            b.Cancel(); b.Dispose(); test.True(secondB == null, "Disposing owner releases its cache");
            test.Equal(0L, a.AllocatedBytes + b.AllocatedBytes + c.AllocatedBytes, "All snapshot charges released");
        }
        finally { Object.DestroyImmediate(input); }
    });
    public static string Run() => TestContext.Run("Frozen input buffer reuse, exact bytes, failures and cap", test =>
    {
        test.True(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlaying, "Idle editor");
        var shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"),
            AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.shader"));
        var frame = new TiledPaintFrame(1024, 1024, 1024, 1024, ProjectiveMatrix.Identity);
        var owned = new List<Object>(); var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D Input(int w, int h, TextureFormat format, bool linear = true)
        {
            var input = new Texture2D(w, h, format, false, linear) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(input); return input;
        }
        try
        {
            using (var surface = new TiledPaintSurface(frame, shaders))
            {
                var mask = Input(19, 13, TextureFormat.R8); Fill(mask, 1);
                var original = Input(33, 19, TextureFormat.RGBAHalf); Fill(original, 1);
                var settings = new TiledStrokeSettings(selection: mask, originalStraight: original, writeChannels: 9);
                surface.Begin(settings);
                var firstMask = Frozen(surface, "selection"); var firstOriginal = Frozen(surface, "originalStraight");
                var oldMask = mask.GetRawTextureData<byte>().ToArray(); var oldOriginal = original.GetRawTextureData<byte>().ToArray();
                Fill(mask, 5); Fill(original, 5);
                Equal(test, oldMask, firstMask, "Pending mask remains frozen");
                Equal(test, oldOriginal, firstOriginal, "Pending signed HDR and hidden RGB remain frozen");
                Throws<InvalidOperationException>(test, () => surface.Begin(settings), "Nested Begin rejected");
                surface.Cancel(); surface.TrimIdle(); long retained = surface.AllocatedBytes;
                original.filterMode = FilterMode.Point; original.wrapModeU = TextureWrapMode.Repeat; original.wrapModeV = TextureWrapMode.Mirror;
                surface.Begin(settings);
                test.True(ReferenceEquals(firstMask, Frozen(surface, "selection")), "Mask allocation reused after Cancel/TrimIdle");
                test.True(ReferenceEquals(firstOriginal, Frozen(surface, "originalStraight")), "Original allocation reused");
                test.Equal(retained, surface.AllocatedBytes, "No duplicate charge");
                test.Equal(original.filterMode, firstOriginal.filterMode, "Filter refreshed");
                test.Equal(original.wrapModeU, firstOriginal.wrapModeU, "U wrap refreshed");
                test.Equal(original.wrapModeV, firstOriginal.wrapModeV, "V wrap refreshed");
                Equal(test, mask.GetRawTextureData<byte>().ToArray(), firstMask, "Changed mask recopied");
                Equal(test, original.GetRawTextureData<byte>().ToArray(), firstOriginal, "Changed original recopied");
                using (surface.Commit()) { }
                surface.TrimIdle(); surface.Begin(settings);
                test.True(ReferenceEquals(firstOriginal, Frozen(surface, "originalStraight")), "Commit retains reusable allocation");
                surface.Cancel(); firstOriginal.Release(); surface.Begin(settings);
                Equal(test, original.GetRawTextureData<byte>().ToArray(), Frozen(surface, "originalStraight"), "Lost idle allocation recreated and filled");
                surface.Cancel();
                foreach (var format in new[] { TextureFormat.RGBAFloat, TextureFormat.RGBA32, TextureFormat.RGBAHalf })
                {
                    var changed = Input(17, 11, format, format != TextureFormat.RGBA32); Fill(changed, 2);
                    var next = new TiledStrokeSettings(selection: mask, originalStraight: changed);
                    surface.Begin(next); var current = Frozen(surface, "originalStraight");
                    test.Equal(changed.graphicsFormat, current.graphicsFormat, "Exact replacement format including sRGB");
                    Equal(test, changed.GetRawTextureData<byte>().ToArray(), current, "Size/format replacement");
                    surface.Cancel(); surface.Begin(next);
                    test.True(ReferenceEquals(current, Frozen(surface, "originalStraight")), "Replacement also reusable");
                    surface.Cancel();
                }
                surface.Begin(settings);
                var savedMask = Frozen(surface, "selection"); var savedOriginal = Frozen(surface, "originalStraight");
                Object.DestroyImmediate(mask);
                Equal(test, original.GetRawTextureData<byte>().ToArray(), savedOriginal, "Caller lifetime independent");
                surface.Cancel();
                Throws<TiledPaintResourceLostException>(test, () => surface.Begin(settings), "Destroyed input rejected even with cached allocation");
                test.True(!surface.Pending, "Failed Begin never publishes a stroke");
                surface.Dispose(); surface.Dispose();
                test.True(savedMask == null && savedOriginal == null, "Dispose releases idle snapshots");
                test.Equal(0L, surface.AllocatedBytes, "Disposed empty surface releases its entire charge");
            }
            // 12 MiB cached snapshots must not prevent a 16 MiB page reservation.
            using (var surface = new TiledPaintSurface(frame, shaders, new TiledPaintOptions { GpuBudgetBytes = 16 * MiB, IdleBudgetBytes = 16 * MiB }))
            {
                var mask = Input(2048, 2048, TextureFormat.R8);
                var original = Input(1024, 1024, TextureFormat.RGBAHalf);
                surface.Begin(new TiledStrokeSettings(selection: mask, originalStraight: original));
                var cachedMask = Frozen(surface, "selection"); var cachedOriginal = Frozen(surface, "originalStraight");
                surface.Cancel(); test.Equal(12 * MiB, surface.AllocatedBytes, "Idle snapshots remain accounted");
                surface.Prepare(2);
                test.True(cachedMask == null && cachedOriginal == null, "Allocation pressure evicts only unused snapshots");
                test.Equal(16 * MiB, surface.AllocatedBytes, "Existing cap unchanged");
            }
            using (var surface = new TiledPaintSurface(frame, shaders, new TiledPaintOptions { GpuBudgetBytes = 16 * MiB, IdleBudgetBytes = 0 }))
            {
                var mask = Input(2048, 2048, TextureFormat.R8);
                var tooLarge = Input(2048, 2048, TextureFormat.RGBAHalf);
                Throws<TiledPaintMemoryLimitException>(test, () => surface.Begin(new TiledStrokeSettings(selection: mask, originalStraight: tooLarge)), "Second capture cannot exceed cap");
                test.True(!surface.Pending, "Second capture failure is atomic");
                test.Equal(0L, surface.AllocatedBytes, "Failed pair releases first lease");
                surface.Begin(new TiledStrokeSettings(selection: mask));
                surface.Cancel(); test.Equal(0L, surface.AllocatedBytes, "Zero idle retention also disables snapshot retention");
            }
        }
        finally
        {
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });
}
