using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class GrdPresetTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("GRD preset reader/import", () =>
    {
        var t = WhimTex.Tests.UnityC.FixtureContext.Context;
        foreach (string method in new[] { "Gcls", "Lnr ", "Perc", "unknown" })
        {
            var result = WhimTexGradientPresetReader.Read(Modern(method, 2048));
            t.Equal(1, result.presets.Count, "One gradient");
            var preset = result.presets[0];
            t.Equal("Test 渐变", preset.name, "Unicode preset name");
            t.Equal(method == "Gcls" ? WhimTexGradientMode.Classic : method == "Lnr " ? WhimTexGradientMode.Linear : WhimTexGradientMode.Perceptual, preset.gradient.Mode, "Interpolation selection/fallback");
            t.Near(.5, preset.gradient.Smoothness, .00001, "Smoothness scale is independent of stop location");
            t.Near(1, preset.gradient.ColorKeys[1].time, .00001, "Endpoint always uses 4096 units");
            t.Near(.3, preset.gradient.GetMidpoint(false, 0), .00001, "Color midpoint");
            t.Near(.25, preset.gradient.AlphaKeys[0].alpha, .00001, "Separate opacity stops");
            t.True(result.warnings.Count == (method == "unknown" ? 1 : 0), "Unknown mode is reported once");
        }
        t.Near(1, WhimTexGradientPresetReader.Read(Modern(null, null)).presets[0].gradient.Smoothness, 0, "Absent smoothness defaults to 100%");
        t.Near(1, WhimTexGradientPresetReader.Read(Modern("Perc", -4)).presets[0].gradient.Smoothness, 0, "Invalid smoothness defaults to 100%");
        var dynamic = WhimTexGradientPresetReader.Read(Modern("Perc", 4096, true), Color.red, Color.blue);
        t.Equal(Color.red, dynamic.presets[0].gradient.ColorKeys[0].color, "Foreground stop resolved to explicit input");
        t.Equal(Color.blue, dynamic.presets[0].gradient.ColorKeys[1].color, "Background stop resolved to explicit input");
        t.Equal(1, dynamic.warnings.Count, "Dynamic resolution has a warning");
        var old = WhimTexGradientPresetReader.Read(VersionThree());
        t.Equal(WhimTexGradientMode.Perceptual, old.presets[0].gradient.Mode, "Version 3 mode fallback");
        t.Near(1, old.presets[0].gradient.Smoothness, 0, "Version 3 smoothness fallback");
        t.Equal(Color.red, old.presets[0].gradient.ColorKeys[0].color, "Version 3 RGB");
        byte[] bytes = Modern("Perc", 4096);
        for (int length = 0; length < bytes.Length; length += 19)
            Reject(t, new ArraySegment<byte>(bytes, 0, length).ToArray(), "Truncated descriptor at " + length);
        bytes[5] = 99; Reject(t, bytes, "Unknown format version");
        Reject(t, Modern("Perc", 4096, noise: true), "Noise gradient is not falsely converted to color stops");
        var scope = WhimTex.Tests.UnityC.FixtureContext.Scope;
        const string key = "DCFApixels.WhimTex.PresetsFolder";
        bool existed = EditorPrefs.HasKey(key); string previous = EditorPrefs.GetString(key);
        try
        {
            EditorPrefs.SetString(key, scope.Temp);
            string path = Path.Combine(scope.Temp, "library.grd");
            File.WriteAllBytes(path, Modern("Lnr ", 1024));
            var service = typeof(WhimTexGradientPresetReader).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientPresets");
            var import = service.GetMethod("Import", BindingFlags.Static | BindingFlags.NonPublic);
            import.Invoke(null, new object[] { path });
            string[] files = Directory.GetFiles(Path.Combine(scope.Temp, "Gradients"), "*.json");
            t.Equal(1, files.Length, "Import writes one independent native user preset");
            var clipboard = typeof(WhimTexGradientPresetReader).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientClipboard");
            var saved = (WhimTexGradient)clipboard.GetMethod("Read", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(null, new object[] { File.ReadAllText(files[0]) });
            t.Equal(WhimTexGradientMode.Linear, saved.Mode, "Native preset retains imported method");
            t.Near(.25, saved.Smoothness, 0, "Native preset retains smoothness");
            File.WriteAllBytes(path, Modern("Perc", 4096, noise: true));
            bool rejected = false;
            try { import.Invoke(null, new object[] { path }); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { rejected = true; }
            t.True(rejected && Directory.GetFiles(Path.Combine(scope.Temp, "Gradients"), "*.json").Length == 1,
                "Rejected input does not overwrite or remove existing presets");
        }
        finally { if (existed) EditorPrefs.SetString(key, previous); else EditorPrefs.DeleteKey(key); }
    });
    static void Reject(TestContext t, byte[] bytes, string message)
    {
        bool rejected = false;
        try { WhimTexGradientPresetReader.Read(bytes); } catch (InvalidDataException) { rejected = true; }
        t.True(rejected, message);
    }
    static byte[] Modern(string method, double? smoothness, bool dynamic = false, bool noise = false)
    {
        PsdWriter.Descriptor Stop(int location, bool second) => new PsdWriter.Descriptor("Clrt")
            .Enum("Type", "Clry", dynamic ? (second ? "BckC" : "FrgC") : "UsrS")
            .Int("Lctn", location).Int("Mdpn", second ? 50 : 30)
            .Object("Clr ", new PsdWriter.Descriptor("RGBC").Number("Rd  ", second ? 0 : 255).Number("Grn ", 0).Number("Bl  ", second ? 255 : 0));
        var grad = new PsdWriter.Descriptor("Grdn").Text("Nm  ", "Test 渐变").Enum("GrdF", "GrdF", noise ? "ClNs" : "CstS")
            .Objects("Clrs", new List<PsdWriter.Descriptor> { Stop(0, false), Stop(4096, true) })
            .Objects("Trns", new List<PsdWriter.Descriptor> {
                new PsdWriter.Descriptor("TrnS").Int("Lctn", 0).Int("Mdpn", 50).Unit("Opct", "#Prc", 25),
                new PsdWriter.Descriptor("TrnS").Int("Lctn", 4096).Int("Mdpn", 50).Unit("Opct", "#Prc", 100) });
        if (smoothness.HasValue) grad.Number("Intr", smoothness.Value);
        var wrapper = new PsdWriter.Descriptor().Object("Grad", grad);
        if (method != null) wrapper.Enum("gradientsInterpolationMethod", "gradientInterpolationMethodType", method);
        using var stream = new MemoryStream(); var writer = new PsdWriter.BigEndian(stream);
        writer.Code("8BGR"); writer.U16(5); writer.U32(16);
        new PsdWriter.Descriptor().Objects("GrdL", new List<PsdWriter.Descriptor> { wrapper }).Write(writer);
        return stream.ToArray();
    }
    static byte[] VersionThree()
    {
        using var stream = new MemoryStream(); var w = new PsdWriter.BigEndian(stream);
        w.Code("8BGR"); w.U16(3); w.U16(1); w.Byte(1); w.Byte('V'); w.U16(2);
        for (int i = 0; i < 2; i++)
        {
            w.U32((uint)i * 4096); w.U32(50); w.U16(0);
            w.U16((ushort)(i == 0 ? 65535 : 0)); w.U16(0); w.U16((ushort)(i == 1 ? 65535 : 0)); w.U16(0); w.U16(0);
        }
        w.U16(2);
        for (int i = 0; i < 2; i++) { w.U32((uint)i * 4096); w.U32(50); w.U16(255); }
        w.Zeros(6); return stream.ToArray();
    }
}
