// Unity Pipeline run_script, entry DocumentReliabilitySmoke.Run. Creates only a unique test folder;
// never edits the open scene/documents. All owned assets/objects are removed in finally.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentReliabilitySmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(TextureCompositor).Assembly;
    static readonly Type Serializer = Package.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
    static readonly Type Session = Package.GetType("DCFApixels.WhimTex.WhimTexDocumentSession");
    static readonly Type Carrier = Package.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier");
    static readonly List<Object> Owned = new List<Object>();
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); checks++; }
    static object Call(Type type, object instance, string name, params object[] args) => type.GetMethods(Any)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(instance, args);
    static TextureCompositor Document(Color color)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 64; doc.height = 32;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = color }));
        Owned.Add(doc);
        return doc;
    }
    static byte[] Encode(object value, WhimTexDocumentContainer container) => (byte[])Call(Serializer, null, "Serialize", value, container);
    // Optional C# arguments are not optional when selecting a method through reflection.
    static object Decode(byte[] bytes, WhimTexDocumentContainer container, Type type) =>
        Call(Serializer, null, "Deserialize", bytes, container, type, null, false);
    static bool Live(TextureCompositor doc) => (bool)Call(Session, null, "IsLiveFor", doc);
    static Color ReadGpu(Texture texture)
    {
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false; Graphics.Blit(texture, rt); RenderTexture.active = rt;
            cpu.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); cpu.Apply(false, false);
            return cpu.GetPixel(texture.width / 2, texture.height / 2);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cpu); }
    }
    static bool Near(Color a, Color b, float tolerance = .025f) => Mathf.Abs(a.r-b.r) < tolerance &&
        Mathf.Abs(a.g-b.g) < tolerance && Mathf.Abs(a.b-b.b) < tolerance && Mathf.Abs(a.a-b.a) < tolerance;
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception e) { rejected = e is WhimTexDocumentException || e.InnerException is WhimTexDocumentException; }
        Check(rejected, message);
    }
    public static string Run()
    {
        checks = 0;
        if ((bool)Session.GetProperty("IsLive", Any).GetValue(null)) return "SKIP: stop your Live Update session before running this test.";
        string folder = "Assets/WhimTexReliability_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        try
        {
            using (var container = new WhimTexDocumentContainer())
            {
                Check((Type)Call(Serializer, null, "ResolveType", "DCFApixels.SpriteEditor.DrawingLayerBehaviour") == typeof(DrawingLayerBehaviour), "MovedFrom namespace/class metadata");
                container.Set("document", new byte[500]); container.Set("carrier", new byte[] { 1 });
                byte[] payload = container.Serialize();
                Check(WhimTexDocumentContainer.TryReadBlock(payload, "carrier", out byte[] flags, out _) && flags.SequenceEqual(new byte[] { 1 }), "non-first directory block");
                var bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
                Check((Bounds)Decode(Encode(bounds, container), container, typeof(Bounds)) == bounds, "Bounds round-trip");
                var invalidList = new byte[] { 1, 0, 0, 0, 30, 255, 255, 255, 127 };
                Reject(() => Decode(invalidList, container, typeof(List<int>)), "oversized list rejected");
                var invalidString = new byte[] { 1, 0, 0, 0, 13, 255, 255, 255, 255, 127 };
                Reject(() => Decode(invalidString, container, typeof(string)), "oversized string rejected");
                Check(((List<string>)Decode(Encode(new List<string> { "a", null, "b" }, container), container, typeof(List<string>))).Count == 3, "null list slots preserved");
            }

            var doc = Document(new Color(.18f, .3f, .5f, .25f));
            var reference = doc.Compose(); Owned.Add(reference);
            Color expected = ReadGpu(reference);
            string path = WhimTexDocumentFile.Save(doc, folder + "/A.tiff");
            string guid = AssetDatabase.AssetPathToGUID(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer.sRGBTexture, "first save sRGB follows the carrier");
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.SaveAndReimport();
            var image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Check(Near(ReadGpu(image), expected), "LDR RGB and alpha survive save: " + ReadGpu(image) + " expected " + expected);
            DateTime unchanged = File.GetLastWriteTimeUtc(path);
            WhimTexDocumentFile.Save(doc, path);
            Check(File.GetLastWriteTimeUtc(path) == unchanged, "unchanged save does not rewrite carrier");
            var originalFormat = image.graphicsFormat;
            int originalMips = image.mipmapCount;
            string entity = image.GetEntityId().ToString();
            string meta = File.ReadAllText(path + ".meta");
            Check((bool)Call(Session, null, "Start", doc, path), "live start with temporary Read/Write");
            Check(image.width == 64 && image.height == 32, "rectangular live dimensions");
            Check(Near(ReadGpu(image), expected), "GPU live RGB/alpha");
            var other = Document(Color.green);
            WhimTexDocumentFile.Save(other, folder + "/B.tiff");
            Check(Live(doc) && !Live(other), "saving B does not stop/reassign live A");
            Call(Session, null, "Stop", "regression test");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored");
            Check(File.ReadAllText(path + ".meta") == meta, "original meta restored exactly");
            Check(image.graphicsFormat == originalFormat && image.mipmapCount == originalMips && image.width == 64 && image.height == 32, "original graphics format/dimensions/mips restored");
            Check(image.GetEntityId().ToString() == entity && AssetDatabase.AssetPathToGUID(path) == guid, "texture identity stable");
            Check((bool)Call(Session, null, "Start", doc, path), "live restart");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Check(!Live(doc) && image.graphicsFormat == originalFormat, "external import stops session without overwriting imported data");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "external import restores Read/Write");

            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = true;
            importer.SaveAndReimport();
            var compressedFormat = image.graphicsFormat;
            byte[] compressedPixels = image.GetRawTextureData();
            Check(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(compressedFormat), "native compressed test fixture");
            Check((bool)Call(Session, null, "Start", doc, path), "compressed rectangular live start");
            Call(Session, null, "Stop", "compressed regression");
            Check(image.graphicsFormat == compressedFormat && image.GetRawTextureData().SequenceEqual(compressedPixels), "compressed mip bytes/graphics format restored exactly");
            Check(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "pre-existing Read/Write stays enabled");

            // LDR linear data and transition to/from HDR on one asset path.
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = false; importer.SaveAndReimport();
            WhimTexDocumentFile.Save(doc, path);
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).sRGBTexture && Near(ReadGpu(image), expected), "linear data save");
            ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = new Color(2, .2f, .3f, .25f);
            WhimTexDocumentFile.Save(doc, path);
            var hdr = doc.Compose(); Owned.Add(hdr);
            Check(Near(ReadGpu(image), ReadGpu(hdr)), "HDR transition preserves RGB/alpha");
            Check(AssetDatabase.AssetPathToGUID(path) == guid, "HDR transition keeps GUID");
            using (var referenceStream = new MemoryStream())
            using (var container = new WhimTexDocumentContainer())
            {
                using (var writer = new BinaryWriter(referenceStream, Encoding.UTF8, true))
                { writer.Write(1); writer.Write((byte)27); writer.Write(guid); writer.Write(long.MaxValue); }
                Check(Decode(referenceStream.ToArray(), container, typeof(Texture2D)) == null, "missing subasset never falls back to the main texture");
                Check(((System.Collections.ICollection)Serializer.GetProperty("LastUnresolvedReferences", Any).GetValue(null)).Count == 1, "missing subasset is reported");
            }

            var paintDoc = Document(Color.clear);
            paintDoc.layers.Clear();
            var drawing = new DrawingLayerBehaviour { brushColor = Color.red, brushSize = 24, brushHardness = 1 };
            paintDoc.layers.Add(drawing);
            Call(typeof(TextureCompositor), paintDoc, "NormalizeModel");
            Call(typeof(DrawingLayerBehaviour), drawing, "PaintPoint", new Vector2(.5f, .5f), 64, 32,
                Call(typeof(DrawingLayerBehaviour), drawing, "GetStrokeParameters", false));
            // Deliberately do NOT finish the stroke or synchronize the CPU texture here.
            string paintPath = WhimTexDocumentFile.Save(paintDoc, folder + "/Painting.tiff");
            var paintLoaded = WhimTexDocumentFile.Load(paintPath); Owned.Add(paintLoaded);
            var drawn = paintLoaded.Compose(); Owned.Add(drawn);
            Check(ReadGpu(drawn).r > .9f && ReadGpu(drawn).a > .9f, "unfinished GPU Drawing pixels survive save/load");
            Texture2D pixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(drawing);
            pixels.filterMode = FilterMode.Point; pixels.wrapModeU = TextureWrapMode.Mirror; pixels.wrapModeV = TextureWrapMode.Clamp;
            using (var container = new WhimTexDocumentContainer())
            {
                var restored = (Texture2D)Decode(Encode(pixels, container), container, typeof(Texture2D)); Owned.Add(restored);
                Check(restored.filterMode == FilterMode.Point && restored.wrapModeU == TextureWrapMode.Mirror && restored.wrapModeV == TextureWrapMode.Clamp, "embedded texture sampling restored");
            }
            var effect = (ShaderFX)Call(typeof(TextureCompositor), paintDoc, "AddEmbeddedShaderFX", paintDoc.layers[0]);
            Call(typeof(ShaderFX), effect, "ApplyAgentDraft");
            paintPath = WhimTexDocumentFile.Save(paintDoc, paintPath);
            var fxLoaded = WhimTexDocumentFile.Load(paintPath); Owned.Add(fxLoaded);
            var restoredFx = (ShaderFX)fxLoaded.layers[0].modifiers[0];
            Check((TextureCompositor)typeof(ShaderFX).GetProperty("EmbeddedOwner", Any).GetValue(restoredFx) == fxLoaded, "embedded FX ownership restored");
            Check((string)typeof(ShaderFX).GetProperty("SourcePath", Any).GetValue(restoredFx) == paintPath, "embedded FX relative includes use the TIFF path");
            var embedded = (List<ShaderFX>)typeof(TextureCompositor).GetField("embeddedShaderFX", Any).GetValue(fxLoaded);
            Check(ReferenceEquals(restoredFx, embedded[0]), "FX object identity preserved");

            // Read an unknown field in a real carrier and prove a save cannot erase it.
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] model = Encode(doc, container);
                byte[] token = Encoding.UTF8.GetBytes("width");
                for (int i = 0; i < model.Length - token.Length; i++)
                    if (model.Skip(i).Take(token.Length).SequenceEqual(token)) { Buffer.BlockCopy(Encoding.UTF8.GetBytes("ghost"), 0, model, i, 5); break; }
                container.Set("document", model);
                byte[] carrier = (byte[])Call(Carrier, null, "Write", container, hdr, null);
                string incomplete = folder + "/Incomplete.tiff";
                File.WriteAllBytes(incomplete, carrier); AssetDatabase.ImportAsset(incomplete);
                var loaded = WhimTexDocumentFile.Load(incomplete); Owned.Add(loaded);
                Reject(() => WhimTexDocumentFile.Save(loaded, incomplete), "unknown fields block lossy save");
                Check(File.ReadAllBytes(incomplete).SequenceEqual(carrier), "incomplete document original remains intact");
            }
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] model = Encode(doc, container);
                byte[] token = Encoding.UTF8.GetBytes(typeof(ColorFillLayerBehaviour).FullName);
                for (int i = 0; i < model.Length - token.Length; i++)
                    if (model.Skip(i).Take(token.Length).SequenceEqual(token)) { model[i] = (byte)'X'; break; }
                container.Set("document", model);
                byte[] carrier = (byte[])Call(Carrier, null, "Write", container, hdr, null);
                string missingType = folder + "/MissingType.tiff";
                File.WriteAllBytes(missingType, carrier); AssetDatabase.ImportAsset(missingType);
                var loaded = WhimTexDocumentFile.Load(missingType); Owned.Add(loaded);
                Reject(() => WhimTexDocumentFile.Save(loaded, missingType), "unknown behaviour blocks lossy save");
                Check(File.ReadAllBytes(missingType).SequenceEqual(carrier), "unknown behaviour remains in original bytes");
            }

            // Window bindings are owned by a document and follow GUIDs through moves.
            var window = ScriptableObject.CreateInstance<TextureCompositorWindow>(); Owned.Add(window);
            var windowDoc = Document(Color.blue);
            string windowPath = WhimTexDocumentFile.Save(windowDoc, folder + "/Window.tiff");
            Call(typeof(TextureCompositorWindow), window, "SetCompositor", windowDoc);
            Call(typeof(TextureCompositorWindow), window, "BindDocumentFile", windowPath);
            string moved = folder + "/Renamed.tiff";
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(windowPath, moved)), "test asset move");
            object[] binding = { windowDoc, null };
            Check((bool)typeof(TextureCompositorWindow).GetMethod("TryGetDocumentFile", Any).Invoke(null, binding) && (string)binding[1] == moved, "binding follows GUID after move");
            int windows = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Length;
            var target = AssetDatabase.LoadAssetAtPath<Texture2D>(moved);
            Check((bool)Call(typeof(TextureCompositorWindow), null, "OpenWhimTexDocument", target.GetEntityId(), 0), "double-click handles existing document");
            Check(Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Length == windows, "double-click reuses the existing window");
            Call(typeof(TextureCompositorWindow), window, "RefreshDocumentTitle", true);
            Check(window.titleContent.text == "Renamed", "native document tab follows the file name");
            var replacement = Document(Color.red);
            Call(typeof(TextureCompositorWindow), window, "SetCompositor", replacement);
            binding = new object[] { replacement, null };
            Check(!(bool)typeof(TextureCompositorWindow).GetMethod("TryGetDocumentFile", Any).Invoke(null, binding), "replacement document never inherits old file");
            return "PASS: " + checks + " document reliability checks.";
        }
        finally
        {
            Call(Session, null, "Stop", "test cleanup");
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
