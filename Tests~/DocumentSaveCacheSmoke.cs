// run_script entry DocumentSaveCacheSmoke.Run. Memory-only; no user assets/windows/scenes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentSaveCacheSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Serializer = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
    static int checks;
    static void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks++; }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Any).Invoke(target, args);
    static string Signature(WhimTexDocumentContainer c) => (string)Call(c, "ContentSignature");
    static bool Reuse(WhimTexDocumentContainer c, string key, NativeArray<byte> bytes) => (bool)Call(c, "TryReusePixels", "texture:0", key, bytes);
    static byte[] Data(bool compressed)
    {
        var bytes = new byte[1024 * 1024];
        if (!compressed) new System.Random(71).NextBytes(bytes);
        else for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 7);
        return bytes;
    }
    static byte[] TextureRoundtrip(Texture2D texture, out string signature, out int hits)
    {
        using var c = new WhimTexDocumentContainer();
        byte[] model = (byte[])Serializer.GetMethod("Serialize", Any).Invoke(null, new object[] { texture, c });
        c.Set("document", model);
        hits = ((IDictionary)typeof(WhimTexDocumentContainer).GetField("_reused", Any).GetValue(c)).Count;
        signature = Signature(c);
        using var parsed = WhimTexDocumentContainer.Parse(c.Serialize());
        // Optional C# arguments are explicit when invoking through reflection.
        var restored = (Texture2D)Serializer.GetMethod("Deserialize", Any).Invoke(null,
            new object[] { parsed.Get("document"), parsed, typeof(Texture2D), null, false });
        try { return restored.GetRawTextureData<byte>().ToArray(); }
        finally { Object.DestroyImmediate(restored); }
    }
    public static string Run()
    {
        checks = 0;
        foreach (bool compressed in new[] { true, false })
        {
            byte[] raw = Data(compressed); byte[] expected = (byte[])raw.Clone();
            string key = Guid.NewGuid().ToString("N");
            using var first = new WhimTexDocumentContainer();
            first.SetCompressed("texture:0", key, raw);
            raw[0] ^= 19;
            string signature = Signature(first);
            using (var read = WhimTexDocumentContainer.Parse(first.Serialize()))
                Check(read.Get("texture:0").SequenceEqual(expected), "Set takes independent snapshot");
            using var native = new NativeArray<byte>(expected, Allocator.Persistent);
            using var cached = new WhimTexDocumentContainer();
            Check(Reuse(cached, key, native), "cached compressed AND uncompressed blocks reusable");
            Check(Signature(cached) == signature, "signature stable on cache hit");
            Check(first.Serialize().SequenceEqual(cached.Serialize()), "warm save remains byte deterministic");
            var mutated = native; mutated[mutated.Length - 1] ^= 1;
            using var collision = new WhimTexDocumentContainer();
            Check(!Reuse(collision, key, native), "forced fingerprint collision rejected, including final byte");
            collision.SetNative("texture:0", key, new NativeArray<byte>(native, Allocator.Persistent));
            Check(Signature(collision) != signature, "same key with different data changes SHA signature");
            using (var read = WhimTexDocumentContainer.Parse(collision.Serialize()))
                Check(read.Get("texture:0").AsSpan().SequenceEqual(native.AsSpan()), "collision fallback writes actual pixels");

            // A caller may retain a mutable Get buffer between serializations.
            byte[] exposed = cached.Get("texture:0");
            string before = Signature(cached);
            exposed[1] ^= 7;
            Check(Signature(cached) != before, "retained Get buffer cannot leave stale prepared checksum");
            using (var read = WhimTexDocumentContainer.Parse(cached.Serialize()))
                Check(read.Get("texture:0").SequenceEqual(exposed), "mutable raw access has valid new integrity");
            using (var read = WhimTexDocumentContainer.Parse(first.Serialize()))
                Check(read.Get("texture:0").SequenceEqual(expected), "mutable raw Get cannot poison shared cache");
            cached.Set("texture:0", new byte[] { 9, 8 });
            Check(Signature(cached) != before, "Set invalidates prepared record");
            Check(cached.Remove("texture:0") && !cached.Contains("texture:0"), "Remove invalidates prepared record");
        }

        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.RGBAHalf })
        {
            var texture = new Texture2D(32, 32, format, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var bytes = texture.GetRawTextureData<byte>();
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 119);
                texture.Apply(false, false);
                byte[] original = texture.GetRawTextureData<byte>().ToArray();
                Check(TextureRoundtrip(texture, out string originalSignature, out _).SequenceEqual(original), "cold pixel roundtrip " + format);
                Check(TextureRoundtrip(texture, out string warmSignature, out int hits).SequenceEqual(original) && hits == 1 && originalSignature == warmSignature, "warm pixel reuse " + format);

                // No dirty flag, no Apply, no updateCount change required.
                bytes = texture.GetRawTextureData<byte>(); bytes[bytes.Length / 2] ^= 0x11;
                byte[] changed = texture.GetRawTextureData<byte>().ToArray();
                Check(TextureRoundtrip(texture, out string editedSignature, out hits).SequenceEqual(changed) && editedSignature != originalSignature && hits == 0, "direct CPU change detected " + format);
                texture.LoadRawTextureData(original); texture.Apply(false, false);
                Check(TextureRoundtrip(texture, out string restoredSignature, out hits).SequenceEqual(original) && restoredSignature == originalSignature && hits == 1, "restored content reuses previous snapshot " + format);

                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(texture, "WhimTex cache regression");
                texture.SetPixel(0, 0, Color.red); texture.Apply(false, false);
                TextureRoundtrip(texture, out string redSignature, out _);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(TextureRoundtrip(texture, out string undoSignature, out _).SequenceEqual(original) && undoSignature == originalSignature, "Undo restores original cache identity " + format);
                Undo.PerformRedo();
                TextureRoundtrip(texture, out string redoSignature, out _);
                Check(redoSignature == redSignature, "Redo restores changed cache identity " + format);
                Undo.PerformUndo();
                texture.SetPixel(0, 0, Color.blue); texture.Apply(false, false);
                TextureRoundtrip(texture, out string branchSignature, out _);
                Check(branchSignature != redSignature && branchSignature != originalSignature, "edit after Undo cannot reuse previous branch " + format);
            }
            finally { Undo.ClearUndo(texture); Object.DestroyImmediate(texture); }
        }
        DrawingMutations();
        LruBudget();
        return "PASS: " + checks + " save cache checks.";
    }

    static void DrawingMutations()
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 32;
        var drawing = new DrawingLayerBehaviour { brushColor = Color.red, brushSize = 12, brushHardness = 1 };
        doc.layers.Add(new Layer(drawing));
        Texture2D Pixels() => (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(drawing);
        var owned = new List<Texture2D>();
        string Snapshot(string label)
        {
            Call(doc, "SyncDrawingLayerTextures");
            var pixels = Pixels(); if (!owned.Contains(pixels)) owned.Add(pixels);
            byte[] expected = pixels.GetRawTextureData<byte>().ToArray();
            Check(TextureRoundtrip(pixels, out string signature, out _).SequenceEqual(expected), label + " roundtrip");
            return signature;
        }
        try
        {
            Call(drawing, "InitializeCanvas", 32, 32);
            string empty = Snapshot("empty Drawing");
            Call(drawing, "PaintPoint", new Vector2(.5f, .5f), 32, 32, Call(drawing, "GetStrokeParameters", false));
            string painted = Snapshot("pending GPU stroke");
            Check(painted != empty && Pixels().GetPixel(16, 16).a > .9f, "GPU readback invalidates pixel identity");
            Call(drawing, "ClearSurface", 32, 32);
            Check(Snapshot("clear") == empty && Pixels().GetPixel(16, 16).a == 0, "clear reuses only matching empty pixels");
            using (var fill = new NativeArray<Color>(32 * 32, Allocator.Temp))
            {
                var view = fill; for (int i = 0; i < view.Length; i++) view[i] = Color.green;
                typeof(DrawingLayerBehaviour).GetMethod("ApplyFillPixels", Any, null,
                    new[] { typeof(NativeArray<Color>), typeof(int), typeof(int), typeof(string) }, null)
                    .Invoke(drawing, new object[] { fill, 32, 32, "WhimTex cache fill" });
            }
            string filled = Snapshot("fill");
            Check(filled != painted && filled != empty, "fill updates content identity");
            Call(drawing, "SetColorRange", LayerColorRange.HDR);
            Check(Snapshot("HDR conversion") != filled && Pixels().format == TextureFormat.RGBAHalf, "layout/format conversion changes identity");
            var replacement = new Texture2D(16, 8, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(replacement); replacement.SetPixel(0, 0, Color.blue); replacement.Apply(false, false);
            Call(drawing, "AdoptStoredTexture", replacement);
            Check(Snapshot("replacement/resize") != filled && Pixels().width == 16 && Pixels().height == 8, "replacement keeps its own pixels/dimensions");
        }
        finally
        {
            foreach (var texture in owned) if (texture != null) Undo.ClearUndo(texture);
            Object.DestroyImmediate(doc);
            foreach (var texture in owned) if (texture != null) Object.DestroyImmediate(texture);
        }
    }

    static void LruBudget()
    {
        string prefix = Guid.NewGuid().ToString("N");
        var raw = new byte[] { 31, 9, 2, 1 };
        void Seed(string key)
        {
            using var c = new WhimTexDocumentContainer(); c.SetCompressed("texture:0", key, raw); Signature(c);
        }
        for (int i = 0; i < 1024; i++) Seed(prefix + i);
        using var view = new NativeArray<byte>(raw, Allocator.Persistent);
        using var first = new WhimTexDocumentContainer();
        Check(Reuse(first, prefix + 0, view), "touch old cache entry");
        Seed(prefix + 1024);
        using var probe = new WhimTexDocumentContainer();
        Check(Reuse(probe, prefix + 0, view) && !Reuse(probe, prefix + 1, view), "LRU keeps touched entry, evicts least recently used");
        for (int i = 1025; i < 2050; i++) Seed(prefix + i);
        Check(!Reuse(probe, prefix + 0, view), "original entry eventually evicted");
        var cache = (IDictionary)typeof(WhimTexDocumentContainer).GetField("CompressedCache", Any).GetValue(null);
        long bytes = (long)typeof(WhimTexDocumentContainer).GetField("_compressedCacheBytes", Any).GetValue(null);
        long actualBytes = 0;
        foreach (var record in cache.Values) actualBytes += ((byte[])record.GetType().GetField("bytes", Any).GetValue(record)).Length;
        Check(cache.Count <= 1024 && bytes <= 256L * 1048576 && bytes == actualBytes, "cache count/byte accounting bounded and exact");
        Check(first.Get("texture:0").SequenceEqual(raw), "in-flight container keeps valid pinned bytes across eviction");
    }
}
