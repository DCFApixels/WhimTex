using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex;

public static class PortableIncludesSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var builder = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder");
        var expand = builder.GetMethod("ExportPortableIncludes", F);
        var validate = builder.GetMethod("ValidatePortableSource", F);
        string folder = "Packages/com.dcfapixels.whimtex/Tests~/portable-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        var effects = new List<ShaderFX>();
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        string Expand(string code) => (string)expand.Invoke(null, new object[] { code, folder + "/effect.hlsl" });
        void Reject(Action action, string label)
        {
            try { action(); } catch (TargetInvocationException e) when (e.InnerException is IOException || e.InnerException is FormatException || e.InnerException is InvalidOperationException) { checks++; return; }
            throw new Exception("Accepted " + label);
        }
        try
        {
            File.WriteAllText(folder + "/leaf.hlsl", "#ifndef PORTABLE_LEAF\n#define PORTABLE_LEAF\nfloat Helper(float x) { return x * 0.5; }\n#endif\n");
            File.WriteAllText(folder + "/parent.hlsl", "#include \"leaf.hlsl\"\nfloat Twice(float x) { return Helper(x) + Helper(x); }\n");
            string code = Expand("#include \"parent.hlsl\"\n#include \"leaf.hlsl\"\nfloat4 ApplyFX(float2 uv, float4 color) { return Twice(color.r); }");
            Check(code.Contains("float Helper(float x) { return x * 0.5; }"), "Separate helper definition");
            Check(code.Contains("return Helper(x) + Helper(x);"), "Function calls must not be inlined");
            Check(!code.Contains("#include"), "Nested includes expanded");
            Check(code.Contains("#ifndef PORTABLE_LEAF"), "Include guards retained");
            validate.Invoke(null, new object[] { "#define MULTIPLY(x) \\\n ((x) * 2)\n#if 1\n#endif\n" }); checks++;
            string builtins = "#include \"UnityCG.cginc\"\n#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl\"\n#include \"Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc\"\n";
            Check(Expand(builtins) == builtins, "Built-ins remain references");
            string original = "#include \"" + folder + "/parent.hlsl\"\nfloat4 ApplyFX(float2 uv, float4 color) { return Twice(color.r); }";
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { doc, original, new List<ShaderFXParameter>() });
            effects.Add(fx);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null); checks++;
            Layer layer = new ColorFillLayerBehaviour(); doc.layers.Add(layer); layer.modifiers.Add(fx);
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
            string json = (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, doc.layers });
            Check(!json.Contains(folder) && json.Contains("float Helper"), "Writer expands original file references");
            using (var data = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 32, 32 }))
            { data.GetType().GetMethod("Compile", F).Invoke(data, null); checks++; }
            foreach (string invalid in new[] { "#include \"Assets/private.hlsl\"", "#include FILE", "#include_with_pragmas \"UnityCG.cginc\"", "#pragma target 5.0", "#line 1 \"Assets/private.hlsl\"", "#inc\\\nlude \"Assets/private.hlsl\"", "#include <../file.hlsl>" })
                Reject(() => validate.Invoke(null, new object[] { invalid }), invalid);
            File.WriteAllText(folder + "/cycle.hlsl", "#include \"cycle.hlsl\"\n");
            Reject(() => Expand("#include \"cycle.hlsl\""), "cycle");
            Reject(() => Expand("#include \"missing.hlsl\""), "missing include");
            File.WriteAllText(folder + "/large.hlsl", new string(' ', 65537));
            Reject(() => Expand("#include \"large.hlsl\""), "large file");
            var repeats = new StringBuilder(); for (int i = 0; i < 1000; i++) repeats.AppendLine("#include \"leaf.hlsl\"");
            Reject(() => Expand(repeats.ToString()), "aggregate size");
            for (int i = 0; i < 9; i++) File.WriteAllText(folder + "/depth" + i + ".hlsl", i == 8 ? "float Leaf;" : "#include \"depth" + (i+1) + ".hlsl\"");
            Reject(() => Expand("#include \"depth0.hlsl\""), "depth");
            Reject(() => validate.Invoke(null, new object[] { "//" + new string('界', 22000) }), "UTF-8 size");
            return "PASS: " + checks + " include expansion, preserved calls, built-ins, directives, roundtrip compilation and limits.";
        }
        catch (TargetInvocationException error) { throw error.GetBaseException(); }
        finally
        {
            foreach (var fx in effects) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
            // This unique test-owned folder contains only the fixtures created above.
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
