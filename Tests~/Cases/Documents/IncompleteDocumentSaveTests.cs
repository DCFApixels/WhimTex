using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class IncompleteDocumentSaveTests
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(WhimTexDocument).Assembly;
    static readonly FieldInfo Warning = typeof(WhimTexDocument).GetField("documentLoadWarning", Any);
    static object Call(string type, string method, params object[] args)
        => Product.GetType("DCFApixels.WhimTex." + type, true).GetMethod(method, Any).Invoke(null, args);
    static void Reject(TestContext context, Action action, string label)
    {
        bool rejected = false;
        try { action(); }
        catch (WhimTexDocumentException) { rejected = true; }
        context.True(rejected, label);
    }
    public static string Run() => TestContext.Run("Incomplete document save choices", context =>
    {
        using var shared = new UnityBSharedState();
        using var owned = new UnityBOwned();
        var source = owned.Track(ScriptableObject.CreateInstance<WhimTexDocument>());
        source.hideFlags = HideFlags.HideAndDontSave;
        source.width = source.height = 8;
        source.layers.Add(new ColorFillLayerBehaviour { color = new Color(.2f, .4f, .6f, 1f) });
        typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(source, null);
        using var container = new WhimTexDocumentContainer();
        byte[] model = (byte[])Call("WhimTexDocumentSerializer", "Serialize", source, container);
        using (var input = new BinaryReader(new MemoryStream(model)))
        using (var stream = new MemoryStream())
        using (var output = new BinaryWriter(stream))
        {
            context.True(input.ReadInt32() == 1 && input.ReadByte() == 29, "Current tagged root");
            input.ReadString();
            int countPosition = checked((int)input.BaseStream.Position);
            int count = input.ReadInt32();
            output.Write(model, 0, countPosition);
            output.Write(count + 1);
            output.Write("unreadTestField"); output.Write((byte)10); output.Write(7.5f);
            output.Write(model, countPosition + 4, model.Length - countPosition - 4);
            container.Set("document", stream.ToArray());
        }
        var image = owned.Track(source.ComposeCanvas());
        byte[] original = (byte[])Call("WhimTexTiffCarrier", "Write", container, image, null);
        string originalPath = owned.AssetPath("Original.tiff");
        File.WriteAllBytes(originalPath, original);
        AssetDatabase.ImportAsset(originalPath, ImportAssetOptions.ForceSynchronousImport);
        var document = owned.Track(WhimTexDocumentFile.Load(originalPath));
        context.Equal("WhimTexDocument.unreadTestField", (string)Warning.GetValue(document), "Load diagnostics retained");
        ((ColorFillLayerBehaviour)document.layers[0].Behaviour).color = new Color(.75f, .25f, .5f, 1f);
        Reject(context, () => WhimTexDocumentFile.Save(document, originalPath), "Default TIFF Save stays protected");
        context.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(originalPath)), "Rejected save leaves original untouched");
        Reject(context, () => WhimTexDocumentJson.Write(document), "Default JSON writer stays protected");
        var options = new WhimTexJsonWriteOptions { AllowDataLoss = true };
        var json = WhimTexDocumentJson.Write(document, options);
        context.True(!json.Json.Contains("unreadTestField"), "Explicit JSON output omits unread data");
        context.True(Warning.GetValue(document) != null, "Serialization does not acknowledge loss in the open model");
        Reject(context, () => WhimTexDocumentFile.Save(document, string.Empty, allowDataLoss: true), "Override does not bypass destination validation");
        context.True(Warning.GetValue(document) != null, "Failed write retains warning");

        var window = owned.Track(ScriptableObject.CreateInstance<WhimTexWindow>());
        typeof(WhimTexWindow).GetMethod("SetDocument", Any).Invoke(window, new object[] { document });
        window.CreateGUI();
        var banner = window.rootVisualElement.Q<HelpBox>("documentLoadWarning");
        context.True(banner != null && !banner.ClassListContains("whimtex-hidden"), "Warning visible before edits or Save");
        context.True(banner.text.Contains("unreadTestField") && banner.text.Contains("save a copy"), "Warning names unread data and safe recovery choice");
        var saveButton = (Button)typeof(WhimTexWindow).GetField("toolkitSaveButton", Any).GetValue(window);
        context.True(saveButton.enabledSelf, "Incomplete document offers Save even without a dirty flag");

        string exportedPath = owned.AssetPath("Export.json");
        WhimTexDocumentFile.ExportJson(document, exportedPath, options);
        context.True(Warning.GetValue(document) != null, "Export does not dismiss source warning");
        context.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(originalPath)), "Export retains original bytes");
        string copyPath = owned.AssetPath("Recovered.tiff");
        WhimTexDocumentFile.Save(document, copyPath, allowDataLoss: true);
        context.True(string.IsNullOrEmpty((string)Warning.GetValue(document)), "Successful accepted save acknowledges discarded data");
        context.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(originalPath)), "Recovery copy preserves original bytes");
        var restored = owned.Track(WhimTexDocumentFile.Load(copyPath));
        context.True(string.IsNullOrEmpty((string)Warning.GetValue(restored)), "Recovered TIFF reopens completely");
        context.Equal(new Color(.75f, .25f, .5f, 1f), ((ColorFillLayerBehaviour)restored.layers[0].Behaviour).color, "Recovery copy keeps edits");
        typeof(WhimTexWindow).GetMethod("RefreshDocumentLoadWarning", Any).Invoke(window, null);
        context.True(banner.ClassListContains("whimtex-hidden"), "Banner clears only after accepted save");

        var jsonSource = owned.Track(WhimTexDocumentFile.Load(originalPath));
        string jsonPath = owned.AssetPath("Recovered.json");
        Reject(context, () => WhimTexDocumentFile.Save(jsonSource, jsonPath), "Generic Save to JSON stays protected by default");
        WhimTexDocumentFile.Save(jsonSource, jsonPath, allowDataLoss: true);
        context.True(string.IsNullOrEmpty((string)Warning.GetValue(jsonSource)), "Accepted JSON Save clears warning");
        var jsonRestored = owned.Track(WhimTexDocumentFile.Load(jsonPath));
        context.True(string.IsNullOrEmpty((string)Warning.GetValue(jsonRestored)), "Accepted JSON reopens completely");
        var overwritten = owned.Track(WhimTexDocumentFile.Load(originalPath));
        WhimTexDocumentFile.Save(overwritten, originalPath, allowDataLoss: true);
        context.True(!original.AsSpan().SequenceEqual(File.ReadAllBytes(originalPath)), "Explicit overwrite replaces unread source data");
        context.True(string.IsNullOrEmpty((string)Warning.GetValue(overwritten)), "Explicit overwrite acknowledged");
    });
}
