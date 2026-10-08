using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ShaderFXParameterIdentityTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type Metadata = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true);
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)Metadata.GetMethod("Parse", Flags)
        .Invoke(null, new object[] { source, false, null });
    static void Preserve(List<ShaderFXParameter> next, List<ShaderFXParameter> previous) =>
        Metadata.GetMethod("PreserveValues", Flags).Invoke(null, new object[] { next, previous });

    public static string Run() => TestContext.Run("FX parameter identity without previous-name aliases", context =>
    {
        var previous = Parse("// @param float _Value = 0.2\n// @param float _Other = 0.1\n");
        previous[0].floatValue = .73f;
        var reordered = Parse("// @param float _Other = 0.5\n// @param float _Value = 0.5\n");
        Preserve(reordered, previous);
        context.Equal(previous[0].id, reordered[1].id, "Exact names preserve identity after reordering");
        context.Near(.73f, reordered[1].floatValue, 0, "Exact names preserve values");

        var renamed = Parse("// @param float _Renamed = 0.5\n// @param float _Other = 0.1\n");
        Preserve(renamed, previous);
        context.Equal(previous[0].id, renamed[0].id, "In-place editing still preserves identity");
        context.Near(.73f, renamed[0].floatValue, 0, "In-place editing still preserves values");

        var expanded = Parse("// @formerlyserializedas(_Value)\n// @param float _New = 0.5\n" +
            "// @param float _Other = 0.1\n// @param float _Added = 1\n");
        Preserve(expanded, previous);
        context.True(expanded[0].id != previous[0].id, "Removed migration comments do not supply identity");
        context.Near(.5f, expanded[0].floatValue, 0, "No value migration across old names");
        context.True(typeof(ShaderFXParameterControl).GetField("formerlySerializedAs") == null,
            "Previous-name metadata is not stored in current controls");

        var incompatible = Parse("// @param color _Value = #FF0000\n// @param float _Other = 0.1\n");
        Preserve(incompatible, previous);
        context.True(incompatible[0].id != previous[0].id, "Incompatible storage does not inherit identity");
        context.Equal(previous[1].id, incompatible[1].id, "Unchanged controls keep identity");

        var pixelate = Parse(System.IO.File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Pixelate.hlsl"));
        var colorMode = pixelate.Find(p => p.name == "_ColorMode");
        context.True(colorMode != null && colorMode.controls[0].type == ShaderFXParameterType.Enum,
            "Pixelate declares a Color Mode selector, not the retired OneBit toggle name");
        context.True(pixelate.Find(p => p.name == "_OneBit") == null, "No old Pixelate mode uniform");
    });
}
