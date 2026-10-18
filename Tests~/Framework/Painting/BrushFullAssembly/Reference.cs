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
    // Test adapter only: feed the unchanged full-image assembly oracle from sparse storage.
    // This checks assembly, not accumulation; independent full-target stamp tests check accumulation.
    public static RenderTexture MaterializeStroke(object layer)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object stroke = layer.GetType().GetField("advancedStroke", f).GetValue(layer);
        var type = stroke.GetType();
        int width = (int)type.GetField("width", f).GetValue(stroke), height = (int)type.GetField("height", f).GetValue(stroke);
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            var source = (RenderTexture)layer.GetType().GetField("paintSurface", f).GetValue(layer);
            if ((bool)type.GetField("backdrop", f).GetValue(stroke))
            {
                if ((bool)type.GetField("pressureOpacity", f).GetValue(stroke))
                {
                    var material = new Material(Shader.Find("Hidden/WhimTex/Blend"));
                    try { Graphics.Blit(source, target, material, 2); }
                    finally { UnityEngine.Object.DestroyImmediate(material); }
                }
                else Graphics.Blit(source, target);
            }
            else { RenderTexture.active = target; GL.Clear(false, true, Color.clear); }
            RenderTexture.active = null;
            int columns = (width + 255) / 256, rows = (height + 255) / 256;
            for (int y = 0; y < height; y += 256) for (int x = 0; x < width; x += 256)
            {
                var tile = new RectInt(x, y, Math.Min(256, width - x), Math.Min(256, height - y));
                var texture = (RenderTexture)type.GetMethod("Get", f).Invoke(stroke, new object[] { tile });
                if (texture == null) continue;
                var slot = (RectInt)type.GetMethod("Slot", f).Invoke(stroke, new object[] { tile });
                Graphics.CopyTexture(texture, 0, 0, slot.x, slot.y, tile.width, tile.height, target, 0, 0, x, y);
            }
            return target;
        }
        catch { RenderTexture.ReleaseTemporary(target); throw; }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }
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
