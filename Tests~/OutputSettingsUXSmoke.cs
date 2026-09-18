using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class OutputSettingsUXSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type Doc = typeof(TextureCompositor);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, F).Invoke(obj, args);
    static TextureCompositor.OutputSettings Settings(TextureCompositor document) => (TextureCompositor.OutputSettings)Doc.GetField("outputSettings", F).GetValue(document);

    public static async Task<string> Main()
    {
        var a = ScriptableObject.CreateInstance<TextureCompositor>();
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, true, true) { filterMode = FilterMode.Point };
        var previewType = Doc.Assembly.GetType("DCFApixels.WhimTex.WhimTexOutputPreview", true);
        var renderer = Activator.CreateInstance(previewType, true);
        EditorWindow window = null;
        RenderTexture oldActive = RenderTexture.active;
        bool oldSrgb = GL.sRGBWrite;
        try
        {
            a.layers.Add(new ColorFillLayerBehaviour());
            var layer = a.layers[0];
            Settings(a).storage = TextureCompositor.OutputStorage.LinearRgba32;
            Settings(a).maxSize = 1024;
            a.outputFilter = FilterMode.Point;
            string baseline = (string)Call(a, "CaptureOutputSettings");
            Doc.GetField("savedOutputSettings", F).SetValue(a, baseline);
            Settings(a).maxSize = 256; a.outputFilter = FilterMode.Trilinear;
            Check((bool)Doc.GetProperty("HasOutputSettingsChanges", F).GetValue(a), "Settings dirty comparison");
            Call(a, "RevertOutputSettings");
            Check(Settings(a).maxSize == 1024 && a.outputFilter == FilterMode.Point && ReferenceEquals(layer, a.layers[0]), "Revert changed layers or missed settings");
            Check(!(bool)Doc.GetProperty("HasOutputSettingsChanges", F).GetValue(a), "Reverted values still differ");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Check(Settings(a).maxSize == 256 && a.outputFilter == FilterMode.Trilinear, "Revert is not undoable");
            Undo.PerformRedo();
            Check(Settings(a).maxSize == 1024, "Revert redo failed");
            Settings(a).storage = TextureCompositor.OutputStorage.HdrHalf;
            Settings(a).compression = TextureCompositor.OutputCompression.BC7;
            var validateArgs = new object[] { null };
            string error = (string)Call(a, "ValidateOutputSettings", validateArgs);
            Check(error != null && ((string[])validateArgs[0]).Contains("compression"), "Compression validation has no field mapping");
            Settings(a).compression = TextureCompositor.OutputCompression.None;
            Settings(a).border = new Vector4(400, 0, 400, 0);
            error = (string)Call(a, "ValidateOutputSettings", validateArgs);
            Check(error != null && ((string[])validateArgs[0]).Contains("border"), "Border validation");

            var windowType = Doc.Assembly.GetType("DCFApixels.WhimTex.WhimTexOutputSettingsWindow", true);
            windowType.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { a });
            foreach (EditorWindow item in Resources.FindObjectsOfTypeAll(windowType))
                if ((TextureCompositor)windowType.GetField("document", F).GetValue(item) == a) window = item;
            Check(window != null, "Window not created");
            window.position = new Rect(100, 100, 420, 600);
            await Task.Delay(400);
            var root = window.rootVisualElement;
            var borderRow = root.Q<VisualElement>("output-row-Border (px)");
            Check(borderRow.ClassListContains("whimtex-output-row--invalid") && !string.IsNullOrEmpty(borderRow.Q<Label>(className: "whimtex-output-field-error").text), "No inline error");
            Check(!root.Q<Button>(className: "whimtex-output-apply").enabledSelf, "Invalid Apply enabled");
            foreach (var field in borderRow.Query<FloatField>().ToList())
                Check(field.worldBound.xMax <= borderRow.worldBound.xMax + 1 && field.worldBound.width > 60, "Border controls overflow narrow window");
            var pivot = root.Q<VisualElement>("output-row-Pivot Alignment").Q<PopupField<string>>();
            pivot.value = "Top Right";
            Check(Settings(a).pivot == Vector2.one, "Pivot preset did not apply");
            Settings(a).border = Vector4.zero;
            Settings(a).outputType = TextureCompositor.OutputType.Texture;
            Call(window, "CreateGUI");
            await Task.Delay(200);
            var warnings = window.rootVisualElement.Query<HelpBox>().ToList();
            Check(!warnings.Any(x => x.text.StartsWith("Texture creates no sprites") && x.resolvedStyle.display != DisplayStyle.None), "Spurious sprite removal warning");

            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int size = Math.Max(1, 8 >> mip);
                texture.SetPixels(Enumerable.Repeat(mip == 0 ? new Color(1, 0, 0, .25f) : new Color(0, 1, 0, .75f), size * size).ToArray(), mip);
            }
            texture.Apply(false, true);
            Check(ReferenceEquals(Call(renderer, "Get", texture, 0, 0), texture), "Default preview copied texture");
            var rgb = (RenderTexture)Call(renderer, "Get", texture, 1, 1);
            Check(rgb != null && rgb.width == 4 && rgb.height == 4, "Mip preview dimensions");
            Color sample = Read(rgb);
            Check(sample.g > .95f && sample.r < .05f && sample.a > .95f, "Explicit mip / RGB preview incorrect");
            Check(ReferenceEquals(rgb, Call(renderer, "Get", texture, 1, 1)), "Preview cache not reused");
            var alpha = (RenderTexture)Call(renderer, "Get", texture, 2, 0);
            sample = Read(alpha);
            Check(Mathf.Abs(sample.r - sample.g) < .01f && Mathf.Abs(sample.g - sample.b) < .01f && sample.r > .2f && sample.r < .6f && sample.a > .95f, "Alpha preview incorrect");
            Check(!texture.isReadable && texture.filterMode == FilterMode.Point && texture.mipmapCount == 4, "Preview modified source");
            Check(RenderTexture.active == oldActive && GL.sRGBWrite == oldSrgb, "GPU state leaked");
            return "PASS: scoped Revert + Undo/Redo, validation fields, narrow Border layout, Pivot positions, contextual warnings, unreadable texture channel/mip GPU preview and cache/state safety.";
        }
        finally
        {
            if (window != null) window.Close();
            ((IDisposable)renderer).Dispose();
            Undo.ClearUndo(a);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(a);
            RenderTexture.active = oldActive; GL.sRGBWrite = oldSrgb;
        }
    }

    static Color Read(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = texture;
            readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); readback.Apply();
            return readback.GetPixel(0, 0);
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
    }
}
