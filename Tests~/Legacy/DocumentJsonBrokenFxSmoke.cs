using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class DocumentJsonBrokenFxSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Good = "float4 ApplyFX(float2 uv,float4 color){return color;}";
    const string Broken = "float4 ApplyFX(float2 uv,float4 color){return missingFunction(color);}";
    const string Json = "{\"format\":\"whimtex.document\",\"version\":1,\"document\":{\"width\":16,\"height\":16},\"layers\":[{\"id\":\"fixture\",\"layerName\":\"Broken FX fixture\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"storedColor\":[0.2,0.4,0.6,1]},\"modifiers\":[{\"$type\":\"ShaderFX\",\"$name\":\"Broken fixture\",\"code\":\"SOURCE\",\"parameters\":[{\"name\":\"_Amount\",\"type\":\"Float\",\"floatValue\":0.37}]}]}]}";
    static int checks, warnings;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static bool Flag(ShaderFX fx, string name) => (bool)typeof(ShaderFX).GetProperty(name, F).GetValue(fx);
    static void Log(string text, string stack, LogType type) { if (type == LogType.Warning && text.StartsWith("WhimTex: Broken fixture:")) warnings++; }
    static Color Pixel(TextureCompositor document)
    {
        var texture = document.ComposeCanvas();
        try { return texture.GetPixel(8, 8); }
        finally { Object.DestroyImmediate(texture); }
    }
    public static string Run()
    {
        checks = warnings = 0;
        string path = "Assets/__WhimTexBrokenFx_" + Guid.NewGuid().ToString("N") + ".whimtex.json";
        string exportPath = path.Replace(".whimtex.json", "_export.whimtex.json");
        TextureCompositorWindow window = null;
        Application.logMessageReceived += Log;
        try
        {
            using var read = WhimTexDocumentJson.Read(Json.Replace("SOURCE", Broken));
            var doc = read.Document;
            var layer = doc.layers[0];
            var fx = (ShaderFX)layer.modifiers[0];
            Check(read.Warnings.Count == 1 && Flag(fx, "LastApplyFailed"), "Broken shader must open with a warning.");
            Check(fx.Active && layer.modifiers.Count == 1, "Failure changed enabled state or removed effect.");
            Check(warnings == 1, "Failure must log once.");
            for (int i = 0; i < 2; i++)
            {
                Call(fx, "TryPrepareDocumentEffect", new object[] { null });
                Call(fx, "RestoreDocumentShader");
            }
            Check(warnings == 1, "Repeated same failure spammed Console.");
            var actual = Pixel(doc);
            layer.modifiers.Clear();
            var expected = Pixel(doc);
            layer.modifiers.Add(fx);
            Check((actual - expected).maxColorComponent < .0001f && (expected - actual).maxColorComponent < .0001f, "Broken FX changed pixels.");

            window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            Call(window, "SetCompositor", doc);
            var row = (VisualElement)Call(window, "BuildToolkitLeafRow", layer, doc.layers, 0, 0);
            Check(!row.Q("fxWarning").ClassListContains("whimtex-hidden"), "Layer warning missing.");
            var root = new VisualElement();
            Call(window, "BuildToolkitLayerInspector", root, layer);
            var section = root.Q<Foldout>("fxSection");
            section.value = false;
            Check(!section.Q("fxWarning").ClassListContains("whimtex-hidden"), "Collapsed FX section warning missing.");
            var entry = root.Q(className: "whimtex-layer-fx-entry-toolbar");
            Check(!entry.Q("fxWarning").ClassListContains("whimtex-hidden"), "FX block warning missing.");
            Check(!section.contentContainer.Contains(section.Q(className: "whimtex-fx-section-warning")), "Section warning is hidden inside collapsed content.");
            var sectionWarning = section.Q(className: "whimtex-fx-section-warning");
            var sectionLabel = section.Q<Toggle>().Q<Label>(className: "unity-toggle__text");
            Check(sectionWarning.parent == sectionLabel.parent && sectionWarning.parent.IndexOf(sectionWarning) + 1 == sectionLabel.parent.IndexOf(sectionLabel),
                "Section warning must directly precede the FX label.");

            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var write = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode });
                Check(write.Json.Contains("missingFunction") && write.Json.Contains("0.37"), "Broken source or parameters lost.");
                using var again = WhimTexDocumentJson.Read(write.Json, false);
                Check(again.Document.JsonWriteMode == mode, "Write mode not restored.");
            }
            var service = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService");
            service.GetMethod("Detach", F).Invoke(null, new object[] { window });
            WhimTexDocumentFile.SaveJson(doc, path, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact });
            var loaded = WhimTexDocumentFile.Load(path);
            try
            {
                Check(loaded.JsonWriteMode == WhimTexJsonWriteMode.Compact, "Mode lost on file load.");
                WhimTexDocumentFile.Save(loaded, path);
                Check(File.ReadAllText(path).Contains("\"writeMode\": \"Compact\""), "Ordinary Save reset mode.");
                WhimTexDocumentFile.ExportJson(loaded, exportPath, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full });
                Check(loaded.JsonWriteMode == WhimTexJsonWriteMode.Compact, "Export changed source mode.");
            }
            finally { Object.DestroyImmediate(loaded); }

            Call(fx, "SetDraftCode", "// @param float _Amount\n" + Good);
            Check((bool)Call(fx, "Apply"), "Repair did not compile.");
            Check(!Flag(fx, "IsUnavailable"), "Repair left unavailable state.");
            var view = root.Q(className: "whimtex-layer-fx");
            Call(view, "Refresh");
            Check(entry.Q("fxWarning").ClassListContains("whimtex-hidden"), "Repair did not clear FX warning.");
            var bindings = typeof(TextureCompositorWindow).GetField("toolkitInspectorBindings", F).GetValue(window);
            Call(bindings, "Refresh", true);
            Check(section.Q(className: "whimtex-fx-section-warning").ClassListContains("whimtex-hidden"), "Repair did not clear section warning.");
            Call(fx, "SetDraftCode", "// @param float _Amount\n" + Broken);
            Check(!(bool)Call(fx, "Apply") && Flag(fx, "HasAppliedShader") && Flag(fx, "IsUnavailable"), "Failed edit must skip even a previously compiled FX.");

            Call(fx, "SetDraftCode", "// @param float _Amount = invalid\n" + Good);
            Check(!(bool)Call(fx, "Apply"), "Malformed declaration unexpectedly compiled.");
            var malformed = WhimTexDocumentJson.Write(doc).Json;
            using (var invalid = WhimTexDocumentJson.Read(malformed))
                Check(invalid.Warnings.Count == 1 && WhimTexDocumentJson.Write(invalid.Document).Json.Contains("0.37"), "Malformed declarations lost parameters or blocked open.");
            Call(fx, "SetDraftCode", "#include \"Assets/MissingBrokenFxFixture.hlsl\"\n" + Good);
            string missingInclude = WhimTexDocumentJson.Write(doc).Json;
            using (var invalid = WhimTexDocumentJson.Read(missingInclude))
                Check(invalid.Warnings.Count == 1 && missingInclude.Contains("MissingBrokenFxFixture.hlsl"), "Missing include blocked preservation.");
            return "PASS: " + checks + " broken-FX, rendering, UI warning and JSON mode checks.";
        }
        finally
        {
            Application.logMessageReceived -= Log;
            if (window != null) { Call(window, "SetCompositor", new object[] { null }); Object.DestroyImmediate(window); }
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            if (File.Exists(exportPath)) AssetDatabase.DeleteAsset(exportPath);
        }
    }
}
