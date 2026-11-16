using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DCFApixels.WhimTex;
using UnityEngine;
using Unity.Collections;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Support: Framework/TestApi.cs. Detached models and in-memory container data only.
public static class TiledDrawingPersistenceTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type Limits = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentLimits", true);
    static object Call(object target, string method, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var member = type.GetMethods(Flags).Single(m => m.Name == method && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
        try { return member.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object Get(object target, string name) => target.GetType().GetProperty(name, Flags)?.GetValue(target)
        ?? target.GetType().GetField(name, Flags)?.GetValue(target);
    static void Reject(TestContext t, Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (WhimTexDocumentException) { rejected = true; }
        t.True(rejected, label);
    }
    static string Revision(DrawingLayerBehaviour drawing)
    {
        using var hash = SHA256.Create(); var text = new StringBuilder();
        Call(typeof(WhimTexApi), "AppendDrawingRevision", text, hash, drawing);
        return text.ToString();
    }
    static long CheckBudget(long bytes, long total)
    {
        object[] args = { bytes, total, "Test Drawing" };
        try { Limits.GetMethod("CheckTexture", Flags).Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        return (long)args[1];
    }

    // Native streamed TIFF payload at real 8K source size. The carrier uses the
    // known original pixels; composition and spatial consumers have separate tests.
    public static string Run8K() => TestContext.Run("8K HDR sparse Drawing native TIFF persistence", t =>
    {
        const int side = 8192;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = side;
        var owned = new HashSet<Object> { document };
        string directory = Path.GetFullPath("Temp/WhimTex/tiled-drawing-integration");
        string path = Path.Combine(directory, "persistence-8k-" + Guid.NewGuid().ToString("N") + ".tiff");
        var carrier = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(directory);
            var original = new Texture2D(side, side, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(original);
            var chunk = new ushort[4096];
            for (int i = 0; i < chunk.Length; i += 4)
            { chunk[i] = 0xb400; chunk[i + 1] = 0x4400; chunk[i + 2] = 0x3800; chunk[i + 3] = 0x3a00; }
            var raw = original.GetRawTextureData<ushort>();
            for (int at = 0; at < raw.Length; at += chunk.Length) NativeArray<ushort>.Copy(chunk, 0, raw, at, chunk.Length);
            original.Apply(false, false);
            var drawing = new DrawingLayerBehaviour(); Set(drawing, "pixels", original);
            Set(drawing, "tiledSourceEnabled", true); Set(drawing, "tiledSourceWidth", side); Set(drawing, "tiledSourceHeight", side);
            var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            document.layers.Add(layer); Call(document, "NormalizeModel");
            var listType = drawing.GetType().GetField("tiledPixels", Flags).FieldType;
            var tiles = (IList)Activator.CreateInstance(listType); var tileType = listType.GetGenericArguments()[0];
            foreach (var key in new[] { new Vector2Int(-1, 3), new Vector2Int(32, 17) })
            {
                var texture = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(texture); var data = texture.GetRawTextureData<ushort>();
                for (int at = 0; at < data.Length; at += chunk.Length) NativeArray<ushort>.Copy(chunk, 0, data, at, chunk.Length);
                texture.Apply(false, false); var tile = Activator.CreateInstance(tileType, true);
                Set(tile, "key", key); Set(tile, "texture", texture); tiles.Add(tile);
            }
            Set(drawing, "tiledPixels", tiles); Call(drawing, "ValidateTiledPixels");
            Call(document, "SyncDrawingLayerTextures");
            t.Equal(512L * 1024 * 1024 + 2L * 256 * 256 * 8, (long)Call(Limits, "Validate", document), "Real 8K Half source and tile storage fit limits");
            using (var container = new WhimTexDocumentContainer())
            {
                container.Set("document", (byte[])Call(Serializer, "Serialize", document, container), System.IO.Compression.CompressionLevel.Fastest);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                Call(carrier, "WriteTo", stream, container, original, (bool?)false, WhimTexOutputPrecision.Float32);
            }
            t.True(File.Exists(path) && new FileInfo(path).Length > 0, "Native streamed TIFF exists");
            t.True(timer.Elapsed.TotalSeconds < 50, "8K save stays within diagnostic time budget");
            using (var container = (WhimTexDocumentContainer)Call(carrier, "OpenContainer", path))
            {
                Call(Limits, "ValidateDirectory", container);
                var result = Call(Serializer, "Deserialize", container.Get("document"), container, typeof(WhimTexDocument), path, false);
                var loaded = (WhimTexDocument)Get(result, "Model"); owned.Add(loaded);
                var restored = (DrawingLayerBehaviour)loaded.layers[0].Behaviour;
                foreach (var texture in (IEnumerable<Texture2D>)Get(restored, "StoredPixelTextures")) owned.Add(texture);
                t.Equal(side, (int)Get(restored, "TiledSourceWidth"), "8K UV width preserved");
                t.Equal(side, (int)Get(restored, "TiledSourceHeight"), "8K UV height preserved");
                var restoredOriginal = (Texture2D)Get(restored, "StoredTexture");
                t.Equal(TextureFormat.RGBAHalf, restoredOriginal.format, "8K original keeps Half precision");
                var left = original.GetRawTextureData<ulong>(); var right = restoredOriginal.GetRawTextureData<ulong>();
                t.Equal(left.Length, right.Length, "Complete 512 MiB base length");
                int different = 0;
                for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) different++;
                t.Equal(0, different, "Every original Half bit survives native TIFF");
                var restoredTiles = (IList)Get(restored, "tiledPixels"); t.Equal(tiles.Count, restoredTiles.Count, "Signed tile count preserved");
                for (int i = 0; i < tiles.Count; i++)
                {
                    t.Equal((Vector2Int)Get(tiles[i], "key"), (Vector2Int)Get(restoredTiles[i], "key"), "Signed key " + i);
                    t.True(((Texture2D)Get(tiles[i], "texture")).GetRawTextureData().SequenceEqual(
                        ((Texture2D)Get(restoredTiles[i], "texture")).GetRawTextureData()), "Exact Half tile " + i);
                }
                t.Equal(JsonUtility.ToJson(layer.transform), JsonUtility.ToJson(loaded.layers[0].transform), "Original Transform unchanged");
            }
        }
        finally
        {
            foreach (var doc in owned.OfType<WhimTexDocument>().ToArray()) if (doc != null) Object.DestroyImmediate(doc);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            if (File.Exists(path)) File.Delete(path);
        }
    });

    public static string Run() => TestContext.Run("Sparse Drawing ownership, serialization and revisions", t =>
    {
        var owned = new HashSet<Object>();
        Texture2D Texture(int width, int height, Color color, bool half = true)
        {
            var texture = new Texture2D(width, height, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, half)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            owned.Add(texture);
            var colors = new Color[width * height]; Array.Fill(colors, color);
            texture.SetPixels(colors); texture.Apply(false, false); return texture;
        }
        WhimTexDocument Decode(byte[] bytes, WhimTexDocumentContainer container)
        {
            var read = Call(Serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
            foreach (string name in new[] { "MissingTypes", "SkippedFields", "UnresolvedReferences" })
                t.Equal(0, ((IReadOnlyList<string>)Get(read, name)).Count, "No " + name);
            var result = (WhimTexDocument)Get(read, "Model"); owned.Add(result);
            foreach (var layer in result.layers)
                foreach (var texture in (IEnumerable<Texture2D>)Get((DrawingLayerBehaviour)layer.Behaviour, "StoredPixelTextures")) owned.Add(texture);
            return result;
        }
        try
        {
            const long half8KBytes = 8192L * 8192 * 8;
            t.Equal(half8KBytes, CheckBudget(half8KBytes, 0), "8K Half original frame fits embedded texture budget");
            t.Equal(2 * half8KBytes, CheckBudget(half8KBytes, half8KBytes), "Base and a complete 8K tile overlay fit total budget");
            Reject(t, () => CheckBudget(half8KBytes + 1, 0), "Rejects an oversized embedded texture");
            Reject(t, () => CheckBudget(1, 2 * half8KBytes), "Retains one GiB total pixel limit");
            var document = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(document);
            document.width = 512; document.height = 384;
            var drawing = new DrawingLayerBehaviour(); var layer = new Layer(drawing); document.layers.Add(layer);
            Call(document, "NormalizeModel");
            Set(drawing, "pixels", Texture(32, 24, new Color(.2f, .3f, .4f, .6f), false));
            Set(drawing, "tiledSourceEnabled", true);
            Set(drawing, "tiledSourceWidth", 32); Set(drawing, "tiledSourceHeight", 24);
            var tilesField = drawing.GetType().GetField("tiledPixels", Flags);
            var tiles = (IList)Activator.CreateInstance(tilesField.FieldType);
            var tileType = tilesField.FieldType.GetGenericArguments()[0];
            foreach (var key in new[] { new Vector2Int(-1, 0), new Vector2Int(2, -2) })
            {
                var tile = Activator.CreateInstance(tileType, true);
                Set(tile, "key", key); Set(tile, "texture", Texture(256, 256, new Color(-.25f, 4f + key.x, .125f, .5f)));
                tiles.Add(tile);
            }
            tilesField.SetValue(drawing, tiles);
            Call(drawing, "ValidateTiledPixels");
            t.Equal(32L * 24 + 2L * 256 * 256, (long)Get(drawing, "StoredPixelCount"), "Counts only allocated storage");
            t.Equal(32L * 24 * 4 + 2L * 256 * 256 * 8, (long)Call(Limits, "Validate", document), "Sparse byte accounting includes base and both tiles");
            using var container = new WhimTexDocumentContainer();
            byte[] bytes = (byte[])Call(Serializer, "Serialize", document, container);
            t.Equal(3, BitConverter.ToInt32(bytes, 0), "Sparse fields retain the tagged payload encoding");
            var restored = (DrawingLayerBehaviour)Decode(bytes, container).layers[0].Behaviour;
            var restoredTiles = (IList)Get(restored, "tiledPixels");
            t.Equal(2, restoredTiles.Count, "Retains sparse tile count");
            t.Equal(JsonUtility.ToJson(layer.transform), JsonUtility.ToJson(restored.Owner.transform), "Original Transform preserved");
            for (int i = 0; i < tiles.Count; i++)
            {
                t.Equal((Vector2Int)Get(tiles[i], "key"), (Vector2Int)Get(restoredTiles[i], "key"), "Signed tile key " + i);
                var a = (Texture2D)Get(tiles[i], "texture"); var b = (Texture2D)Get(restoredTiles[i], "texture");
                t.True(a != b, "Roundtrip owns tile " + i);
                t.True(a.GetRawTextureData().SequenceEqual(b.GetRawTextureData()), "Exact Half/HDR tile bytes " + i);
            }
            var baseBefore = ((Texture2D)Get(drawing, "StoredTexture")).GetRawTextureData();
            string before = Revision(drawing);
            var changed = (Texture2D)Get(tiles[0], "texture"); changed.SetPixel(0, 0, new Color(6, -.5f, .25f, .75f)); changed.Apply(false, false);
            t.True(before != Revision(drawing), "Revision sees overflow pixel changes");
            t.True(baseBefore.SequenceEqual(((Texture2D)Get(drawing, "StoredTexture")).GetRawTextureData()), "Overflow edit does not rewrite base frame");
            before = Revision(drawing); Set(tiles[0], "key", new Vector2Int(-2, 0));
            t.True(before != Revision(drawing), "Revision sees tile position changes");
            Set(tiles[0], "key", new Vector2Int(-1, 0));

            Reject(t, () => WhimTexDocumentJson.Write(document), "JSON refuses silent Drawing data loss");
            var json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { AllowDrawingOmission = true });
            t.True(json.DrawingPixelsOmitted && json.Warnings.Count == 1, "One explicit omission warning for all pixel storage");
            foreach (var field in new[] { "pixels", "tiledPixels", "tiledSourceEnabled", "tiledBaseEmpty", "tiledSourceWidth", "tiledSourceHeight" })
            {
                t.True(!json.Json.Contains("\"" + field + "\""), "JSON omits raster storage " + field);
                string value = field == "tiledPixels" ? "[]" : field.EndsWith("Width") || field.EndsWith("Height") ? "32" : "true";
                string injected = json.Json.Replace("\"contentOmitted\": true", "\"contentOmitted\": true, \"" + field + "\": " + value);
                Reject(t, () => { using var invalid = WhimTexDocumentJson.Read(injected, false); }, "JSON rejects raster storage " + field);
            }
            using (var omitted = WhimTexDocumentJson.Read(json.Json, false))
            {
                var empty = (DrawingLayerBehaviour)omitted.Document.layers[0].Behaviour;
                t.True(!(bool)Get(empty, "HasTiledSource"), "Omitted Drawing starts without a raster source");
                var clean = WhimTexDocumentJson.Write(omitted.Document);
                t.True(!clean.DrawingPixelsOmitted, "Empty omitted Drawing needs no new omission approval");
                Call(empty, "PrepareBufferedStroke", 512, 384, "Owned JSON repaint check");
                owned.Add((Texture2D)Get(empty, "StoredTexture"));
                t.Equal(512, (int)Get(empty, "TiledSourceWidth"), "Repaint initializes fresh canvas width, not old 32px source");
                t.Equal(384, (int)Get(empty, "TiledSourceHeight"), "Repaint initializes fresh canvas height, not old 24px source");
                var point = new Vector2(.5f, .5f);
                Call(empty, "BeginStroke", point);
                Call(empty, "PaintPoint", point, 512, 384, Call(empty, "GetStrokeParameters", false));
                t.True((bool)Get(Get(empty, "tiledSurface"), "Pending"), "Imported omitted Drawing accepts an actual new stroke");
                Call(empty, "CancelStroke");
            }

            var clone = JsonUtility.FromJson<DrawingLayerBehaviour>(JsonUtility.ToJson(drawing));
            var cloneLayer = new Layer(clone);
            Call(clone, "CloneStoredTexture");
            foreach (var texture in (IEnumerable<Texture2D>)Get(clone, "StoredPixelTextures")) owned.Add(texture);
            var cloneTiles = (IList)Get(clone, "tiledPixels");
            t.True(!ReferenceEquals(tiles, cloneTiles), "Copy owns tile list");
            for (int i = 0; i < tiles.Count; i++)
            {
                var a = (Texture2D)Get(tiles[i], "texture"); var b = (Texture2D)Get(cloneTiles[i], "texture");
                t.True(a != b && a.GetRawTextureData().SequenceEqual(b.GetRawTextureData()), "Copy owns exact tile " + i);
            }
            Call(clone, "ReleaseTransientResources");
            t.True((Texture2D)Get(tiles[0], "texture") != null, "Disposing copy leaves source tile alive");

            var savedKey = (Vector2Int)Get(tiles[1], "key"); Set(tiles[1], "key", (Vector2Int)Get(tiles[0], "key"));
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects duplicate keys"); Set(tiles[1], "key", savedKey);
            Set(tiles[1], "key", new Vector2Int(int.MaxValue, 0));
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects overflowing signed coordinates"); Set(tiles[1], "key", savedKey);
            var savedTexture = Get(tiles[1], "texture"); Set(tiles[1], "texture", Texture(256, 256, Color.white, false));
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects non-Half tiles"); Set(tiles[1], "texture", savedTexture);
            Set(tiles[1], "texture", Get(tiles[0], "texture"));
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects aliased mutable tile textures"); Set(tiles[1], "texture", savedTexture);
            Set(drawing, "tiledSourceWidth", -1);
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects negative source frame metadata");
            Set(drawing, "tiledSourceWidth", 16385);
            Reject(t, () => Call(drawing, "ValidateTiledPixels"), "Rejects oversized source frame metadata");
            Set(drawing, "tiledSourceWidth", 0);
            Call(drawing, "ValidateTiledPixels");
            var old = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(2), old, 4);
            Reject(t, () => Decode(old, container), "Rejects previous payload encoding explicitly");
            using var malformedContainer = new WhimTexDocumentContainer();
            var malformed = (byte[])Call(Serializer, "Serialize", document, malformedContainer);
            var name = Encoding.UTF8.GetBytes("tiledSourceEnabled");
            int at = -1;
            for (int i = 0; i + name.Length + 2 < malformed.Length; i++)
                if (malformed.Skip(i).Take(name.Length).SequenceEqual(name)) { at = i + name.Length + 1; break; }
            t.True(at >= 0 && malformed[at] == 1, "Found serialized tiled source flag"); malformed[at] = 0;
            Reject(t, () => Decode(malformed, malformedContainer), "Reader refuses tiles under disabled source flag");
        }
        finally
        {
            foreach (var value in owned.Where(v => v is WhimTexDocument).ToArray()) if (value != null) Object.DestroyImmediate(value);
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
        }
    });
}
