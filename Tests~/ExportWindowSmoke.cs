using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class ExportWindowSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type WindowType = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexExportWindow");
    static readonly Type OptionsType = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexExportOptions");
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static object Call(object target, string method, params object[] args)
    {
        try { return target.GetType().GetMethod(method, F).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static object Get(object target, string field) => target.GetType().GetField(field, F).GetValue(target);
    static void Set(object target, string field, object value)
    {
        var f = target.GetType().GetField(field, F);
        f.SetValue(target, value is string text && f.FieldType.IsEnum ? Enum.Parse(f.FieldType, text) : value);
    }
    static void Add(TextureCompositor document, LayerBehaviour behaviour)
    {
        var layer = new Layer(behaviour); Call(layer, "AssignNewId"); document.layers.Add(layer);
    }
    static string Full(TextureCompositor document) => WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
    static void Reject(Action action, string text)
    { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, text); }
    public static string Run()
    {
        checks = 0;
        Check(typeof(TextureCompositorWindow).GetMethod("ShowSaveDocumentMenu", F) == null &&
            typeof(TextureCompositorWindow).GetMethod("AddJsonMenu", F) == null &&
            typeof(TextureCompositorWindow).GetMethod("SaveJsonFromWindow", F) == null, "Save As still exposes a JSON format picker.");
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var owner = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        var window = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
        string token = Guid.NewGuid().ToString("N");
        string folder = Path.GetFullPath("Temp/WhimTex/ExportSmoke-" + token);
        string asset = "Assets/__WhimTexExport_" + token + ".asset";
        string jsonPath = "Assets/__WhimTexExport_" + token + ".json";
        var options = Activator.CreateInstance(OptionsType);
        var pixels = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        Directory.CreateDirectory(folder);
        try
        {
            document.width = 32; document.height = 24;
            Add(document, new ColorFillLayerBehaviour { color = new Color(.2f, .4f, .6f, .5f) });
            Call(document, "NormalizeModel"); Call(owner, "SetCompositor", document);
            Set(window, "owner", owner); Set(window, "source", document); window.ShowUtility(); Call(window, "CreateGUI");
            var root = window.rootVisualElement;
            var dropdown = root.Q<DropdownField>("format");
            Check(dropdown.choices.Count == 7 && dropdown.index == 0, "Missing format/default PNG.");
            Check(dropdown.choices[6] == "WhimTex JSON (.json)" && WhimTexDocumentJson.Extension == ".json", "Wrong JSON extension.");
            Check((string)typeof(TextureCompositorWindow).GetMethod("GetExportExtension", F).Invoke(null,
                new[] { Enum.Parse(typeof(TextureCompositorWindow).GetNestedType("TextureExportFormat", F), "Json") }) == "json", "Path dialog extension is not json.");
            Check(root.Q<Button>("export").text == "Export…" && root.Q<Button>("cancel") != null, "Actions missing.");
            dropdown.index = 6;
            Check(root.Q<DropdownField>("jsonMode").index == 0, "JSON default is not Full Optimized.");
            Check(root.Q("drawingWarning").ClassListContains("whimtex-hidden"), "Drawing warning shown for procedural source.");
            root.Q<DropdownField>("jsonMode").index = 2;
            dropdown.index = 1;
            Check(root.Q("jsonMode") == null && root.Q<SliderInt>("jpegQuality").value == 95, "Wrong JPEG fields/default.");
            root.Q<SliderInt>("jpegQuality").value = 41;
            dropdown.index = 3;
            Check(root.Q("jpegQuality") == null && root.Q<DropdownField>("exrPrecision").index == 0 && root.Q<EnumField>("exrCompression") != null, "Wrong EXR fields.");
            dropdown.index = 6;
            Check(root.Q<DropdownField>("jsonMode").index == 2, "Switching formats reset JSON selection.");
            dropdown.index = 1;
            Check(root.Q<SliderInt>("jpegQuality").value == 41, "Switching formats reset JPEG quality.");
            string before = Full(document);
            bool dirty = EditorUtility.IsDirty(document);
            var active = RenderTexture.active;
            Check(!(bool)Call(owner, "ExportDocumentToPath", document, options, ""), "Canceled path reported success.");
            Check(before == Full(document) && dirty == EditorUtility.IsDirty(document), "Canceled path changed source.");
            Reject(() => Call(owner, "ExportDocumentToPath", document, options, Path.Combine(folder, "wrong.jpg")), "Wrong suffix accepted.");
            foreach (string format in new[] { "Png", "Jpeg", "Tga", "Exr", "Psd", "Asset", "Json" })
            {
                Set(options, "format", format);
                string path = format == "Asset" ? asset : format == "Json" ? jsonPath : Path.Combine(folder, format + (format == "Jpeg" ? ".jpg" : "." + format.ToLowerInvariant()));
                Check((bool)Call(owner, "ExportDocumentToPath", document, options, path), format + " export failed.");
                Check(File.Exists(path) && new FileInfo(path).Length > 0, format + " output missing.");
                if (format == "Png" || format == "Jpeg")
                {
                    var decoded = new Texture2D(2, 2);
                    try { Check(decoded.LoadImage(File.ReadAllBytes(path)) && decoded.width == 32 && decoded.height == 24, format + " dimensions changed."); }
                    finally { Object.DestroyImmediate(decoded); }
                }
                if (format == "Psd") Check(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4) == "8BPS", "Not a PSD.");
                if (format == "Json")
                {
                    using var read = WhimTexDocumentJson.Read(File.ReadAllText(path), false);
                    Check(read.Document.JsonWriteMode == WhimTexJsonWriteMode.FullOptimized, "Wrong JSON write mode.");
                }
                Check(before == Full(document) && dirty == EditorUtility.IsDirty(document), format + " changed source/model dirty state.");
                Check(RenderTexture.active == active, format + " changed the caller render target.");
            }
            Set(options, "format", "Json");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                Set(options, "jsonMode", mode);
                Check((bool)Call(owner, "ExportDocumentToPath", document, options, jsonPath), "JSON mode export failed.");
                using var read = WhimTexDocumentJson.Read(File.ReadAllText(jsonPath), false);
                Check(read.Document.JsonWriteMode == mode, "JSON mode not applied.");
                Check(document.JsonWriteMode == WhimTexJsonWriteMode.FullOptimized, "Export changed source JSON mode.");
            }
            Set(options, "format", "Jpeg"); Set(options, "jpegQuality", 0);
            Reject(() => Call(options, "Validate"), "Invalid JPEG quality accepted.");
            Set(options, "jpegQuality", 95);
            pixels.SetPixels(new[] { new Color(3, .5f, .2f, .7f), Color.black, Color.white, Color.red }); pixels.Apply();
            var encode = typeof(TextureCompositorWindow).GetMethod("EncodeExportTextureWithOptions", F);
            var formatType = typeof(TextureCompositorWindow).GetNestedType("TextureExportFormat", F);
            byte[] low = (byte[])encode.Invoke(null, new object[] { pixels, Enum.Parse(formatType, "Jpeg"), 10, Texture2D.EXRFlags.CompressZIP });
            byte[] high = (byte[])encode.Invoke(null, new object[] { pixels, Enum.Parse(formatType, "Jpeg"), 95, Texture2D.EXRFlags.CompressZIP });
            Check(Convert.ToBase64String(low) != Convert.ToBase64String(high), "JPEG quality does not reach encoder.");
            foreach (bool fullFloat in new[] { false, true })
            foreach (string compression in new[] { "ZIP", "RLE", "PIZ", "None" })
            {
                Set(options, "exrFloat32", fullFloat); Set(options, "exrCompression", compression);
                var flags = OptionsType.GetProperty("ExrFlags", F).GetValue(options);
                byte[] bytes = (byte[])encode.Invoke(null, new[] { pixels, Enum.Parse(formatType, "Exr"), (object)95, flags });
                Check(bytes.Length > 32 && bytes[0] == 0x76 && bytes[1] == 0x2f, "EXR encoding failed.");
            }
            var drawing = new DrawingLayerBehaviour(); Add(document, drawing); Call(drawing, "AdoptStoredTexture", pixels);
            dropdown.index = 6; Call(window, "RefreshState");
            Check(!root.Q("drawingWarning").ClassListContains("whimtex-hidden"), "Drawing loss warning missing.");
            dropdown.index = 0;
            Check(root.Q("drawingWarning").ClassListContains("whimtex-hidden"), "Drawing warning leaked to PNG.");
            Set(owner, "compositor", null); Call(window, "RefreshState");
            Check(!root.Q<Button>("export").enabledSelf && !root.Q("sourceWarning").ClassListContains("whimtex-hidden"), "Stale source can export.");
            Reject(() => Call(owner, "ExportDocumentToPath", document, options, jsonPath), "Changed source accepted.");
            return "PASS: " + checks + " export UI, settings, cancellation, source isolation and actual file encoding checks.";
        }
        finally
        {
            Call(owner, "SetCompositor", new object[] { null });
            Object.DestroyImmediate(window); Object.DestroyImmediate(owner);
            Object.DestroyImmediate(document); if (pixels != null) Object.DestroyImmediate(pixels);
            if (File.Exists(asset)) AssetDatabase.DeleteAsset(asset);
            if (File.Exists(jsonPath)) AssetDatabase.DeleteAsset(jsonPath);
        }
    }

    public static string ShowVisual()
    {
        var owner = ScriptableObject.CreateInstance<TextureCompositorWindow>(); owner.name = "__WhimTexExportVisualOwner";
        var document = ScriptableObject.CreateInstance<TextureCompositor>(); document.name = "__WhimTexExportVisualDocument"; document.width = document.height = 16;
        var drawing = new DrawingLayerBehaviour();
        var pixels = new Texture2D(2, 2); Add(document, drawing); Call(drawing, "AdoptStoredTexture", pixels);
        Call(owner, "SetCompositor", document);
        WindowType.GetMethod("Open", F).Invoke(null, new object[] { owner, document });
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(WindowType))
            if (ReferenceEquals(Get(window, "owner"), owner))
            {
                window.titleContent = new GUIContent("WhimTex Export Test");
                window.position = new Rect(200, 200, 420, 280);
                window.rootVisualElement.Q<DropdownField>("format").index = 6;
            }
        return "Temporary export window ready for capture; no user document changed.";
    }

    public static string CloseVisual()
    {
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(WindowType))
            if (Get(window, "owner") is TextureCompositorWindow owner && owner.name == "__WhimTexExportVisualOwner")
            {
                var document = (TextureCompositor)Get(window, "source");
                Call(owner, "SetCompositor", new object[] { null });
                window.Close(); Object.DestroyImmediate(owner); Object.DestroyImmediate(document);
            }
        return "Temporary export window closed.";
    }

    public static string ShowExrVisual()
    {
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(WindowType))
            if (Get(window, "owner") is TextureCompositorWindow owner && owner.name == "__WhimTexExportVisualOwner")
            {
                window.position = new Rect(200, 200, 360, 240);
                window.rootVisualElement.Q<DropdownField>("format").index = 3;
            }
        return "EXR at minimum window size.";
    }
}
