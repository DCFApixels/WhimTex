using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

// Frozen pre-region shaders: no production-region uniforms or tests that reconstruct the same bounds.
public static class BrushFullAssemblyReference
{
    const string Package = "Packages/com.dcfapixels.whimtex/";
    const string Root = "Tests~/Framework/Painting/BrushFullAssembly/";
    public static Shader Compile(string file)
    {
        string source = File.ReadAllText(Package + Root + file)
            .Replace("Hidden/WhimTex/", "Hidden/WhimTex/FullAssemblyReference/")
            .Replace("\"BrushComposite.cginc\"", "\"" + Package + Root + "BrushComposite.cginc\"")
            .Replace("\"HdrColor.cginc\"", "\"" + Package + "src/Shaders/HdrColor.cginc\"")
            .Replace("\"ColorBlend.cginc\"", "\"" + Package + "src/Shaders/ColorBlend.cginc\"");
        // Inline the frozen include so its own sibling includes resolve explicitly too.
        string include = File.ReadAllText(Package + Root + "BrushComposite.cginc")
            .Replace("\"HdrColor.cginc\"", "\"" + Package + "src/Shaders/HdrColor.cginc\"")
            .Replace("\"ColorBlend.cginc\"", "\"" + Package + "src/Shaders/ColorBlend.cginc\"");
        source = source.Replace("#include \"" + Package + Root + "BrushComposite.cginc\"", include);
        var shader = ShaderUtil.CreateShaderAsset(source, true);
        if (shader == null) throw new InvalidOperationException("Cannot compile full assembly reference.");
        shader.hideFlags = HideFlags.HideAndDontSave;
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
            { UnityEngine.Object.DestroyImmediate(shader); throw new InvalidOperationException(message.message); }
        return shader;
    }
    public static void Validate(TestContext t)
    {
        // Public managed JSON; ephemeral test DTOs need no Unity native registration.
        var json = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var manifest = (Manifest)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { File.ReadAllText(Package + Root + "provenance.json"), typeof(Manifest) });
        foreach (var file in manifest.files)
        {
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(File.ReadAllText(Package + file.path).Replace("\r\n", "\n")))).Replace("-", "").ToLowerInvariant();
            t.Equal(file.sha256, hash, "Frozen full assembly reference: " + file.path);
        }
    }
    public sealed class Manifest { public Entry[] files; }
    public sealed class Entry { public string path, sha256; }
}
