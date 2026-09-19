// Document container smoke test: in-memory round trip, PNG and EXR carriers through Unity's importer,
// survival of import settings and platform overrides across a document rewrite, atomic write behaviour,
// and rejection of corrupted or truncated documents.
// Run with Unity Pipeline eval_file; the report is returned as the result string.
// Leaves Assets/WhimTexSpike/container-test.png and .exr for inspection in the Inspector;
// delete that folder when it is no longer useful.
var report = new System.Text.StringBuilder();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception("FAIL: " + message); checks++; }
DCFApixels.WhimTex.WhimTexDocumentContainer New() => new DCFApixels.WhimTex.WhimTexDocumentContainer();
byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);
byte[] Random(int size, int seed)
{
    var bytes = new byte[size];
    new System.Random(seed).NextBytes(bytes);
    return bytes;
}
bool Same(byte[] a, byte[] b)
{
    if (a == null || b == null || a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
    return true;
}
UnityEngine.Texture2D Solid(int size, UnityEngine.Color color, UnityEngine.TextureFormat format, bool linear)
{
    var texture = new UnityEngine.Texture2D(size, size, format, false, linear);
    var pixels = new UnityEngine.Color[size * size];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
    texture.SetPixels(pixels);
    texture.Apply();
    return texture;
}

// --- 1. in-memory round trip, including a block that does not compress ---
var source = New();
byte[] document = Bytes("{\"version\":1,\"layers\":[]}");
byte[] large = Random(1024 * 1024, 7);
source.Set(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock, document);
source.Set(source.PixelBlockName("layer-a"), large, System.IO.Compression.CompressionLevel.Fastest);
source.Set(source.PixelBlockName("layer-b"), Random(64, 8));
byte[] payload = source.Serialize();
var parsed = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(payload);
Check(parsed.Count == 3, "all blocks round-tripped");
Check(parsed.HasDocument, "document block recognised");
Check(Same(parsed.Get(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock), document), "document block bytes");
Check(Same(parsed.Get(parsed.PixelBlockName("layer-a")), large), "incompressible block bytes");
Check(Same(parsed.Get(parsed.PixelBlockName("layer-b")), source.Get(source.PixelBlockName("layer-b"))), "small block bytes");
var sw = System.Diagnostics.Stopwatch.StartNew();
var heavy = New();
heavy.Set("document", document);
heavy.Set(heavy.PixelBlockName("big"), Random(4 * 1024 * 1024, 9), System.IO.Compression.CompressionLevel.Fastest);
byte[] heavyPayload = heavy.Serialize();
long serialize = sw.ElapsedMilliseconds; sw.Restart();
DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(heavyPayload);
report.Append("container=").Append(payload.Length / 1024).Append("KB heavy=").Append(heavyPayload.Length / 1024)
    .Append("KB serialize4MB=").Append(serialize).Append("ms parse=").Append(sw.ElapsedMilliseconds).Append("ms");

// --- 2. PNG carrier through the real importer ---
string dir = "Assets/WhimTexSpike";
string pngPath = dir + "/container-test.png";
string exrPath = dir + "/container-test.exr";
System.IO.Directory.CreateDirectory(dir);
var pngTexture = Solid(64, new UnityEngine.Color(.2f, .4f, .6f, 1f), UnityEngine.TextureFormat.RGBA32, false);
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(pngPath, source.WritePng(pngTexture.EncodeToPNG()));
UnityEditor.AssetDatabase.Refresh();
Check(UnityEditor.AssetImporter.GetAtPath(pngPath) is UnityEditor.TextureImporter, "PNG carrier imported by TextureImporter");
byte[] onDisk = System.IO.File.ReadAllBytes(pngPath);
Check(DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(onDisk, out byte[] carried, out string error), "PNG payload readable: " + error);
var carriedContainer = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(carried);
Check(Same(carriedContainer.Get(carriedContainer.PixelBlockName("layer-a")), large), "PNG carries the large block");
report.Append(" || pngBytes=").Append(onDisk.Length / 1024).Append("KB");

// --- 3. import settings and platform overrides survive a rewrite ---
var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(pngPath);
importer.mipmapEnabled = true;
importer.isReadable = true;
importer.spriteBorder = new UnityEngine.Vector4(1f, 2f, 3f, 4f);
var android = new UnityEditor.TextureImporterPlatformSettings();
android.name = "Android";
android.overridden = true;
android.format = UnityEditor.TextureImporterFormat.ASTC_6x6;
android.maxTextureSize = 256;
importer.SetPlatformTextureSettings(android);
importer.SaveAndReimport();

var edited = New();
edited.Set(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock, Bytes("{\"version\":1,\"edited\":true}"));
edited.Set(edited.PixelBlockName("layer-a"), large);
var editedTexture = Solid(64, new UnityEngine.Color(.9f, .3f, .1f, 1f), UnityEngine.TextureFormat.RGBA32, false);
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(pngPath, edited.WritePng(editedTexture.EncodeToPNG()));
UnityEditor.AssetDatabase.ImportAsset(pngPath, UnityEditor.ImportAssetOptions.ForceUpdate);
var importer2 = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(pngPath);
Check(importer2.mipmapEnabled, "mipmaps survived the rewrite");
Check(importer2.isReadable, "Read/Write survived the rewrite");
Check(importer2.spriteBorder == new UnityEngine.Vector4(1f, 2f, 3f, 4f), "sprite border survived the rewrite");
var android2 = importer2.GetPlatformTextureSettings("Android");
Check(android2.overridden && android2.format == UnityEditor.TextureImporterFormat.ASTC_6x6 && android2.maxTextureSize == 256,
    "platform override survived the rewrite");
var rewrittenTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(pngPath);
Check(rewrittenTexture != null && rewrittenTexture.isReadable && rewrittenTexture.GetPixel(0, 0).r > .8f, "rewritten pixels are live");
Check(!System.IO.File.Exists(pngPath + ".whimtex-tmp"), "atomic write left no temporary file");
byte[] rewritten = System.IO.File.ReadAllBytes(pngPath);
Check(DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(rewritten, out byte[] carriedAgain, out string errorAgain),
    "edited payload readable: " + errorAgain);
Check(System.Text.Encoding.UTF8.GetString(DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(carriedAgain)
    .Get(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock)).Contains("edited"), "edited document content");

// --- 4. EXR carrier keeps HDR values ---
var hdrTexture = Solid(32, new UnityEngine.Color(4.5f, .25f, .5f, 1f), UnityEngine.TextureFormat.RGBAFloat, true);
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(exrPath, edited.WriteExr(hdrTexture.EncodeToEXR(UnityEngine.Texture2D.EXRFlags.OutputAsFloat)));
UnityEditor.AssetDatabase.Refresh();
var exrImporter = UnityEditor.AssetImporter.GetAtPath(exrPath) as UnityEditor.TextureImporter;
Check(exrImporter != null, "EXR carrier imported by TextureImporter");
exrImporter.isReadable = true;
exrImporter.SaveAndReimport();
var hdrLoaded = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(exrPath);
Check(hdrLoaded != null && hdrLoaded.isReadable && hdrLoaded.GetPixel(0, 0).r > 4f, "HDR values survived the EXR carrier");
byte[] exrBytes = System.IO.File.ReadAllBytes(exrPath);
Check(DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(exrBytes, out byte[] exrPayload, out string exrError),
    "EXR payload readable: " + exrError);
Check(Same(DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(exrPayload).Get(edited.PixelBlockName("layer-a")), large),
    "EXR carries the large block");
report.Append(" || exrBytes=").Append(exrBytes.Length / 1024).Append("KB format=").Append(hdrLoaded.format);

// --- 5. rejection of files without a document and of damaged documents ---
Check(!DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(pngTexture.EncodeToPNG(), out _, out string plainError) &&
    !string.IsNullOrEmpty(plainError), "plain PNG rejected with a reason");
Check(!DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(hdrTexture.EncodeToEXR(), out _, out _), "plain EXR rejected");
byte[] damaged = (byte[])rewritten.Clone();
int chunkStart = -1;
int scan = 8;
while (scan + 8 <= damaged.Length)
{
    int length = damaged[scan] << 24 | damaged[scan + 1] << 16 | damaged[scan + 2] << 8 | damaged[scan + 3];
    string type = "" + (char)damaged[scan + 4] + (char)damaged[scan + 5] + (char)damaged[scan + 6] + (char)damaged[scan + 7];
    if (type == "whTX") { chunkStart = scan; break; }
    scan += 12 + length;
}
Check(chunkStart > 0, "document chunk located for the damage test");
damaged[chunkStart + 40] ^= 0xFF;
Check(!DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(damaged, out _, out string damagedError) &&
    damagedError.Contains("corrupted"), "damaged document chunk detected: " + damagedError);
try
{
    DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(new byte[payload.Length / 2]);
    Check(false, "truncated document accepted");
}
catch (DCFApixels.WhimTex.WhimTexDocumentException) { checks++; }
try
{
    DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(Random(64, 3));
    Check(false, "garbage accepted as a document");
}
catch (DCFApixels.WhimTex.WhimTexDocumentException) { checks++; }

// --- 6. a failed write must not damage the previous document ---
byte[] beforeFailure = System.IO.File.ReadAllBytes(pngPath);
try
{
    DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(pngPath + "/nested.png", Bytes("nope"));
    Check(false, "write into a file path accepted");
}
catch (DCFApixels.WhimTex.WhimTexDocumentException) { checks++; }
Check(Same(System.IO.File.ReadAllBytes(pngPath), beforeFailure), "previous document intact after a failed write");

// --- 7. multi-sprite slicing with per-sprite borders survives a document rewrite ---
var sliceImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(pngPath);
sliceImporter.textureType = UnityEditor.TextureImporterType.Sprite;
sliceImporter.spriteImportMode = UnityEditor.SpriteImportMode.Multiple;
var factories = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
factories.Init();
var provider = factories.GetSpriteEditorDataProviderFromObject(sliceImporter);
provider.InitSpriteEditorDataProvider();
var spriteRects = new UnityEditor.SpriteRect[2];
spriteRects[0] = new UnityEditor.SpriteRect
{
    name = "alpha", rect = new UnityEngine.Rect(0, 0, 32, 32), alignment = UnityEngine.SpriteAlignment.Center,
    pivot = new UnityEngine.Vector2(.5f, .5f), border = new UnityEngine.Vector4(1f, 2f, 3f, 4f)
};
spriteRects[1] = new UnityEditor.SpriteRect
{
    name = "beta", rect = new UnityEngine.Rect(32, 32, 32, 32), alignment = UnityEngine.SpriteAlignment.Center,
    pivot = new UnityEngine.Vector2(.25f, .75f), border = UnityEngine.Vector4.zero
};
provider.SetSpriteRects(spriteRects);
provider.Apply();
sliceImporter.SaveAndReimport();

UnityEngine.Sprite[] Slices()
{
    var found = new System.Collections.Generic.List<UnityEngine.Sprite>();
    foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(pngPath))
        if (asset is UnityEngine.Sprite sprite) found.Add(sprite);
    return found.ToArray();
}
var sliced = New();
sliced.Set(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock, Bytes("{\"sliced\":true}"));
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(pngPath,
    sliced.WritePng(Solid(64, new UnityEngine.Color(.5f, .5f, .5f, 1f), UnityEngine.TextureFormat.RGBA32, false).EncodeToPNG()));
UnityEditor.AssetDatabase.ImportAsset(pngPath, UnityEditor.ImportAssetOptions.ForceUpdate);
var slices = Slices();
Check(slices.Length == 2, "sprite count survived the rewrite, got " + slices.Length);
UnityEngine.Sprite alphaSlice = null;
foreach (var slice in slices) if (slice.name == "alpha") alphaSlice = slice;
Check(alphaSlice != null && alphaSlice.border == new UnityEngine.Vector4(1f, 2f, 3f, 4f), "per-sprite border survived the rewrite");

// --- 8. saving during a Live Update style session keeps identity and references ---
string livePath = dir + "/live-test.png";
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(livePath,
    New().WritePng(Solid(64, new UnityEngine.Color(1f, 0f, 0f, 1f), UnityEngine.TextureFormat.RGBA32, false).EncodeToPNG()));
UnityEditor.AssetDatabase.Refresh();
var liveImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(livePath);
liveImporter.isReadable = true;
liveImporter.SaveAndReimport();
var liveTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(livePath);
string liveEntity = liveTexture.GetEntityId().ToString();
var liveMaterial = new UnityEngine.Material(UnityEngine.Shader.Find("Unlit/Texture"));
liveMaterial.mainTexture = liveTexture;
liveTexture.Reinitialize(64, 64, UnityEngine.TextureFormat.RGBA32, false);
liveTexture.Apply(false, false);
Check(liveTexture.format == UnityEngine.TextureFormat.RGBA32, "session switches the texture to an uncompressed surface");
var saved = New();
saved.Set(DCFApixels.WhimTex.WhimTexDocumentContainer.DocumentBlock, Bytes("{\"live\":true}"));
DCFApixels.WhimTex.WhimTexDocumentContainer.WriteFileAtomic(livePath,
    saved.WritePng(Solid(64, new UnityEngine.Color(0f, 1f, 0f, 1f), UnityEngine.TextureFormat.RGBA32, false).EncodeToPNG()));
UnityEditor.AssetDatabase.ImportAsset(livePath, UnityEditor.ImportAssetOptions.ForceUpdate);
var liveAfter = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(livePath);
Check(liveAfter != null && liveAfter.GetEntityId().ToString() == liveEntity, "identity survives a save during a session");
Check(liveMaterial.mainTexture != null && liveMaterial.mainTexture.GetEntityId().ToString() == liveEntity,
    "material reference survives a save during a session");
Check(liveAfter.isReadable && liveAfter.GetPixel(0, 0).g > .8f, "saved pixels reach the material's texture");
Check(liveAfter.format != UnityEngine.TextureFormat.RGBA32, "reimport restores the imported representation, got " + liveAfter.format);
liveAfter.Reinitialize(64, 64, UnityEngine.TextureFormat.RGBA32, true);
liveAfter.Apply(false, false);
Check(liveAfter.mipmapCount > 1, "mip chain can be re-enabled for the next session");
Check(DCFApixels.WhimTex.WhimTexDocumentContainer.TryReadPayload(System.IO.File.ReadAllBytes(livePath), out _, out _),
    "document payload survived a save during a session");

// --- 9. compressed block cache: a reused deflate result must equal a fresh one byte for byte ---
byte[] cacheBytes = Random(512 * 1024, 21);
var cacheMiss = New();
cacheMiss.SetCompressed("pixels:cached", "k1", cacheBytes, System.IO.Compression.CompressionLevel.Fastest);
byte[] cacheMissBytes = cacheMiss.Serialize();
var cacheHit = New();
cacheHit.SetCompressed("pixels:cached", "k1", cacheBytes, System.IO.Compression.CompressionLevel.Fastest);
Check(Same(cacheMissBytes, cacheHit.Serialize()), "a cached deflate result equals the freshly deflated one");
var cachePlain = New();
cachePlain.Set("pixels:cached", cacheBytes, System.IO.Compression.CompressionLevel.Fastest);
Check(Same(cacheMissBytes, cachePlain.Serialize()), "the cache does not change the serialized bytes");
byte[] changedBytes = Random(512 * 1024, 22);
var cacheChanged = New();
cacheChanged.SetCompressed("pixels:cached", "k2", changedBytes, System.IO.Compression.CompressionLevel.Fastest);
Check(!Same(cacheMissBytes, cacheChanged.Serialize()), "a new cache key produces new bytes");
var cacheEvicted = New();
cacheEvicted.SetCompressed("pixels:cached", "k1", cacheBytes, System.IO.Compression.CompressionLevel.Fastest);
cacheEvicted.Remove("pixels:cached");
cacheEvicted.SetCompressed("pixels:cached", "k1", cacheBytes, System.IO.Compression.CompressionLevel.Fastest);
Check(Same(cacheMissBytes, cacheEvicted.Serialize()), "a removed block can be stored again under the same key");

return "PASS: container checks=" + checks + ", " + report;
