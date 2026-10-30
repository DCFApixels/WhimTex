using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ShaderFXIconTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Assembly = typeof(ShaderFX).Assembly;
    static Type Type(string name) => Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(string type, string method, params object[] args) => Type(type).GetMethod(method, Flags).Invoke(null, args);
    static object Field(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static object Icon(string source) => Call("ShaderFXIconMetadata", "Read", source);
    static string Code(ShaderFX effect) => (string)typeof(ShaderFX).GetProperty("Code", Flags).GetValue(effect);
    static void Drain()
    {
        for (int i = 0; i < 300; i++)
        {
            var pending = (ICollection)Type("ShaderFXIconCache").GetField("pending", Flags).GetValue(null);
            if (pending.Count == 0) return;
            Call("ShaderFXIconCache", "Update");
        }
        throw new Exception("Icon preparation did not complete.");
    }
    static Texture2D Texture(object icon, string source)
    {
        var args = new object[] { icon, source, null };
        Call("ShaderFXIconCache", "Get", args);
        Drain();
        var result = (Texture2D)Call("ShaderFXIconCache", "Get", args);
        if (args[2] != null) throw new Exception((string)args[2]);
        return result;
    }
    static Color32[] Pixels(Texture2D texture)
    {
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            return copy.GetPixels32();
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(copy); }
    }

    public static string Run() => TestContext.Run("FX icons: metadata, sharing, rendering, portability, UI and menu preparation", context =>
    {
        string directory = Path.GetFullPath("Temp/WhimTex/fx-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var owned = new List<UnityEngine.Object>();
        var renderState = RenderTexture.active;
        bool srgbState = GL.sRGBWrite;
        try
        {
            Call("ShaderFXIconCache", "Clear");
            const string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl";
            const string file = "Packages/com.dcfapixels.whimtex/src/Editor/Icons/FX/Color.svg";
            const string body = "\nfloat4 ApplyFX(float4 color) { return color; }\n";
            string source = "// @whimtex-effect Tests/Icon\n// @icon(\"../Editor/Icons/FX/Color.svg\")" + body;
            var relative = Icon(source);
            context.True(relative != null && Field(relative, "error") == null, "Relative parent path parsed");
            var original = Texture(relative, path);
            var guid = Icon("// @icon(\"guid:" + AssetDatabase.AssetPathToGUID(file) + "\")" + body);
            context.True(ReferenceEquals(original, Texture(guid, path)), "GUID and parent path share one texture");
            string embedded = (string)Call("ShaderFXIconMetadata", "MakePortable", source, path);
            context.True(embedded.Contains("/* @icon-svg") && embedded.EndsWith(body), "Portable SVG retains shader body");
            context.True(ReferenceEquals(original, Texture(Icon(embedded), "Assets/Other.hlsl")), "Inline SVG shares the original texture across source locations");
            context.True(Icon("/* // @icon(\"ignored.png\") */" + body) == null, "Nested comment is not a declaration");
            context.True(Icon(body + "// @icon(\"ignored.png\")") == null, "Only leading metadata is read");
            context.True(Field(Icon("// @icon(\"a.png\")\n// @icon(\"b.png\")" + body), "error") != null, "Duplicate icon has a diagnostic");
            var broken = Icon("// @icon(\"missing.svg\")" + body);
            var brokenArgs = new object[] { broken, path, null };
            Call("ShaderFXIconCache", "Get", brokenArgs); Drain();
            context.True(ReferenceEquals(Call("ShaderFXIconCache", "Get", brokenArgs), Type("ShaderFXIconCache").GetProperty("Default", Flags).GetValue(null)) && brokenArgs[2] != null, "Missing icon uses fallback with a diagnostic");

            var png = new Texture2D(4, 4, TextureFormat.RGBA32, false); owned.Add(png);
            png.SetPixels(Enumerable.Repeat(new Color(1, 0.2f, 0.4f, 0.5f), 16).ToArray()); png.Apply();
            var pngBytes = png.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(directory, "shared.png"), pngBytes);
            string pngSource = "// @icon(\"../shared.png\")" + body;
            string pngBase = Path.Combine(directory, "effects/custom.hlsl");
            var pngTexture = Texture(Icon(pngSource), pngBase);
            string portablePng = (string)Call("ShaderFXIconMetadata", "MakePortable", pngSource, pngBase);
            context.True(portablePng.Contains("data:image/png;base64,"), "PNG is embedded for transport");
            File.Delete(Path.Combine(directory, "shared.png"));
            context.True(ReferenceEquals(pngTexture, Texture(Icon(portablePng), "Assets/Moved.hlsl")), "Embedded PNG works after source file removal");

            var effect = ScriptableObject.CreateInstance<ShaderFX>(); owned.Add(effect);
            typeof(ShaderFX).GetField("code", Flags).SetValue(effect, source);
            typeof(ShaderFX).GetField("documentIncludeBasePath", Flags).SetValue(effect, path);
            string preset = (string)Call("ShaderFXPresetWriter", "BuildSource", effect, "Tests/Export");
            context.True(Field(Icon(preset), "svg") != null, "Exported preset retains portable icon in its header");
            var copy = (ShaderFX)typeof(ShaderFX).GetMethod("CloneForClipboard", Flags).Invoke(effect, null); owned.Add(copy);
            context.True(Field(Icon(Code(copy)), "svg") != null && Code(effect) == source, "Clipboard copy embeds icon without changing original");
            var container = Activator.CreateInstance(Type("WhimTexDocumentContainer"));
            try
            {
                byte[] bytes = (byte[])Call("WhimTexDocumentSerializer", "Serialize", effect, container);
                var result = Call("WhimTexDocumentSerializer", "Deserialize", bytes, container, typeof(ShaderFX), null, false);
                var loaded = (ShaderFX)result.GetType().GetProperty("Model", Flags).GetValue(result); owned.Add(loaded);
                context.True(Field(Icon(Code(loaded)), "svg") != null, "TIFF model payload restores embedded icon");
            }
            finally { ((IDisposable)container).Dispose(); }
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(doc);
            doc.width = doc.height = 16;
            Layer layer = new ColorFillLayerBehaviour(); doc.layers.Add(layer); layer.fx.Add(effect);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(doc, null);
            string json = WhimTexDocumentJson.Write(doc).Json;
            context.True(json.Contains("@icon-svg") && Code(effect) == source, "JSON stores portable icon without mutating original source");
            var view = (VisualElement)Activator.CreateInstance(Type("LayerShaderFXView"), Flags, null, new object[] { layer, doc, (Action<string, Action>)((_, action) => action()) }, null);
            var image = view.Q<Image>("fxIcon");
            var toggle = view.Q<Toggle>(className: "whimtex-fx-visibility-toggle");
            var name = view.Q<Label>(className: "whimtex-layer-fx-name");
            context.True(image != null && image.parent == toggle.parent && image.parent.IndexOf(image) < image.parent.IndexOf(toggle) && image.parent.IndexOf(toggle) < image.parent.IndexOf(name), "Header order is icon, enabled, name");

            var entries = (IList)Call("ShaderFXCatalog", "GetEntries");
            var unique = new HashSet<Texture2D>();
            var iconFailures = new List<string>();
            var diagnosticIcons = new List<Texture2D>();
            int builtin = 0;
            foreach (var entry in entries)
            {
                string entryPath = (string)Field(entry, "path");
                if (!entryPath.StartsWith("Packages/com.dcfapixels.whimtex/src/FXPresets/")) continue;
                var icon = Field(entry, "icon");
                context.True(icon != null, "Built-in preset has icon: " + entryPath);
                var texture = Texture(icon, entryPath);
                var pixels = Pixels(texture);
                if (unique.Add(texture))
                {
                    diagnosticIcons.Add(texture);
                    var readArgs = new object[] { icon, entryPath, false };
                    byte[] svg = (byte[])Call("ShaderFXIconMetadata", "ReadImage", readArgs);
                    var fresh = (Texture2D)Call("ShaderFXIconCache", "RenderSvg", System.Text.Encoding.UTF8.GetString(svg), texture.width, EditorGUIUtility.isProSkin);
                    owned.Add(fresh); diagnosticIcons.Add(fresh);
                    var freshPixels = Pixels(fresh);
                    if (!pixels.Any(p => p.a > 192) || !pixels.Any(p => p.a == 0) ||
                        !freshPixels.Any(p => p.a > 192) || !freshPixels.Any(p => p.a == 0))
                        iconFailures.Add(entryPath + ": cached alpha " + pixels.Min(p => p.a) + ".." + pixels.Max(p => p.a) +
                            ", fresh alpha " + freshPixels.Min(p => p.a) + ".." + freshPixels.Max(p => p.a));
                }
                builtin++;
            }
            SavePreview(diagnosticIcons);
            context.True(iconFailures.Count == 0, "Cached/fresh icon visibility: " + string.Join("; ", iconFailures));
            context.True(builtin >= 31 && unique.Count == 9, "Built-in presets share nine rendered textures");
            int effectsBefore = Resources.FindObjectsOfTypeAll<ShaderFX>().Length;
            int shadersBefore = Resources.FindObjectsOfTypeAll<Shader>().Length;
            var dropdown = Activator.CreateInstance(Type("ShaderFXAddDropdown"), Flags, null, new object[] {null, (Action)(() => {})}, null);
            var build = dropdown.GetType().GetMethod("BuildRoot", Flags);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) build.Invoke(dropdown, null);
            timer.Stop();
            context.Equal(effectsBefore, Resources.FindObjectsOfTypeAll<ShaderFX>().Length, "Opening picker creates no FX objects");
            context.Equal(shadersBefore, Resources.FindObjectsOfTypeAll<Shader>().Length, "Opening picker compiles no shaders");
            context.True(timer.Elapsed.TotalMilliseconds / 100 < 20, "Cached menu preparation stays below 20 ms");
            SavePreview(unique);
            context.True(RenderTexture.active == renderState && GL.sRGBWrite == srgbState, "Icon work restores render state");
        }
        finally
        {
            RenderTexture.active = renderState; GL.sRGBWrite = srgbState;
            foreach (var item in owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            string pngPath = Path.Combine(directory, "shared.png"); if (File.Exists(pngPath)) File.Delete(pngPath);
            Directory.Delete(directory);
        }
    });

    static void SavePreview(IEnumerable<Texture2D> icons)
    {
        var list = icons.ToList();
        var sheet = new Texture2D(list.Count * 56, 80, TextureFormat.RGBA32, false);
        try
        {
            sheet.SetPixels(Enumerable.Repeat(new Color(0.19f, 0.19f, 0.19f, 1), sheet.width * sheet.height).ToArray());
            for (int i = 0; i < list.Count; i++)
            {
                var icon = list[i]; var pixels = Pixels(icon);
                for (int scale = 1; scale <= 2; scale++)
                    for (int y = 0; y < 16 * scale; y++) for (int x = 0; x < 16 * scale; x++)
                    {
                        Color c = pixels[(y * icon.height / (16 * scale)) * icon.width + x * icon.width / (16 * scale)];
                        Color background = new Color(0.19f, 0.19f, 0.19f, 1);
                        Color blended = Color.Lerp(background, new Color(c.r, c.g, c.b, 1), c.a);
                        sheet.SetPixel(i * 56 + (56 - 16 * scale) / 2 + x, scale == 1 ? 56 + y : 8 + y, blended);
                    }
            }
            sheet.Apply();
            File.WriteAllBytes("Temp/WhimTex/fx-icons-preview.png", sheet.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(sheet); }
    }
}
