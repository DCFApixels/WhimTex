using System;
using System.Collections;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;
using Object = UnityEngine.Object;

public static class DocumentWindowNamingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags All = Hidden | BindingFlags.Public | BindingFlags.Static;

    public static string Run()
    {
        bool cleanupUncertain = false;
        string result = TestContext.Run("Document/window names, script bindings, storage and rendering agree", test =>
    {
        try
        {
        UnityAScope.RunOwned(scope =>
        {
            Assembly assembly = typeof(WhimTexDocument).Assembly;
            string previous = "DCFApixels.WhimTex.Texture" + "Compositor";
            test.True(assembly.GetType(previous) == null && assembly.GetType(previous + "Window") == null, "No previous-type aliases remain");
            foreach (var binding in new[] {
                (name: "WhimTexDocument", type: typeof(WhimTexDocument), guid: "c18cb323350c98146a8080a538b44aa9"),
                (name: "WhimTexWindow", type: typeof(WhimTexWindow), guid: "c9961d034ac1c9144bb63f70c02a5855") })
            {
                string path = "Packages/com.dcfapixels.whimtex/src/" + binding.name + ".cs";
                test.Equal(binding.guid, AssetDatabase.AssetPathToGUID(path), "Script GUID preserved for " + binding.name);
                test.Equal(binding.type, AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass(), "Unity associates the renamed script with its class");
            }
            foreach (string shader in new[] { "Blend", "Hdr", "Transform", "Shape", "Noise", "GaussianBlur", "SmudgeBrush", "SmudgeTransport", "EffectCache" })
                test.True(Shader.Find("Hidden/WhimTex/" + shader) != null, "Product shader lookup resolves " + shader);

            WhimTexDocument document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 16; document.height = 8;
            document.layers.Add(new ColorFillLayerBehaviour { color = Color.red });
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Hidden).Invoke(document, null);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                test.True(!json.Contains("Texture" + "Compositor"), "JSON does not emit the previous model name");
                test.True(json.Contains("\"document\""), "JSON retains its document settings contract");
                using var opened = WhimTexDocumentJson.Read(json, false);
                test.Equal(typeof(WhimTexDocument), opened.Document.GetType(), "JSON reader creates the renamed model type");
                CheckDocument(test, opened.Document);
            }

            Type serializer = assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
            using var container = new WhimTexDocumentContainer();
            byte[] bytes = (byte[])serializer.GetMethod("Serialize", All).Invoke(null, new object[] { document, container });
            test.True(Encoding.UTF8.GetString(bytes).Contains("WhimTexDocument"), "TIFF model block emits the current type name");
            object read = serializer.GetMethod("Deserialize", All).Invoke(null, new object[] { bytes, container, typeof(WhimTexDocument), null, false });
            var decoded = (WhimTexDocument)read.GetType().GetProperty("Model", All).GetValue(read);
            scope.OwnObject(decoded);
            foreach (string diagnostic in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
                test.Equal(0, ((ICollection)read.GetType().GetProperty(diagnostic, All).GetValue(read)).Count, "No binary round-trip " + diagnostic);
            CheckDocument(test, decoded);

            var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
            typeof(WhimTexWindow).GetMethod("SetDocument", Hidden).Invoke(window, new object[] { decoded });
            test.True(ReferenceEquals(decoded, typeof(WhimTexWindow).GetField("activeDocument", Hidden).GetValue(window)), "Window binds the renamed document field");
            typeof(WhimTexWindow).GetMethod("CreateGUI", All).Invoke(window, null);
            test.True(window.rootVisualElement.childCount > 0, "Renamed window builds its existing interface");
            test.True(!window.hasUnsavedChanges, "Binding and building the interface do not modify the test document");
        });
        }
        catch (Exception error) when (error.Data.Contains("WhimTexCleanupFailure"))
        { cleanupUncertain = true; throw; }
    });
        return cleanupUncertain ? result.Substring(0, result.Length - 1) + ",\"recoveryRequired\":true}" : result;
    }

    private static void CheckDocument(TestContext test, WhimTexDocument document)
    {
        test.Equal(16, document.width, "Document width survives");
        test.Equal(8, document.height, "Document height survives");
        test.Equal(1, document.layers.Count, "Layer count survives");
        test.Equal(Color.red, ((ColorFillLayerBehaviour)document.layers[0].Behaviour).color, "Layer parameters survive");
        Texture2D image = document.ComposeCanvas();
        try
        {
            test.Equal(16, image.width, "Canvas rendering keeps width");
            test.Equal(8, image.height, "Canvas rendering keeps height");
            Color pixel = image.GetPixel(8, 4);
            test.Near(1, pixel.r, 0.002, "Rendered red");
            test.Near(0, pixel.g, 0.002, "Rendered green");
            test.Near(0, pixel.b, 0.002, "Rendered blue");
            test.Near(1, pixel.a, 0.002, "Rendered alpha");
        }
        finally { if (image != null) Object.DestroyImmediate(image); }
    }
}
