// One-off probe: attributes the document model serialization time. It hashes every pixel block the same
// way the serializer's block cache does, so the cost of the cache key can be compared with the whole call.
// Expects Assets/GGG.tiff to exist; run with Unity Pipeline eval_file.
const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
string path = "Assets/GGG.tiff";
if (!System.IO.File.Exists(path)) return "SKIP: " + path + " is missing";
var serializer = typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly
    .GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
var serialize = serializer.GetMethod("Serialize", Any);
var hash64 = serializer.GetNestedType("Writer", System.Reflection.BindingFlags.NonPublic)
    .GetMethod("Hash64", Any);
if (hash64 == null) return "FAIL: Hash64 was not found";

byte[] file = System.IO.File.ReadAllBytes(path);
long length = System.BitConverter.ToInt64(file, file.Length - 16);
var payload = new byte[length];
System.Buffer.BlockCopy(file, (int)(file.Length - 16 - length), payload, 0, (int)length);
var parsed = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(payload);

var document = DCFApixels.WhimTex.WhimTexDocumentFile.Load(path);
var modelWatch = System.Diagnostics.Stopwatch.StartNew();
using (var container = new DCFApixels.WhimTex.WhimTexDocumentContainer())
    serialize.Invoke(null, new object[] { document, container });
long modelMs = modelWatch.ElapsedMilliseconds;

long hashMs = 0, rawBytes = 0;
int blocks = 0;
foreach (string name in parsed.Names)
{
    if (!name.StartsWith("texture:", StringComparison.Ordinal)) continue;
    byte[] raw = parsed.Get(name);
    rawBytes += raw.Length;
    var native = new Unity.Collections.NativeArray<byte>(raw.Length, Unity.Collections.Allocator.Persistent);
    Unity.Collections.NativeArray<byte>.Copy(raw, native);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    hash64.Invoke(null, new object[] { native });
    hashMs += watch.ElapsedMilliseconds;
    native.Dispose();
    blocks++;
}
return "PROBE: model=" + modelMs + "ms pixelHash=" + hashMs + "ms for " + blocks + " blocks ("
    + (rawBytes / 1024 / 1024) + "MB) => hash share " + (hashMs * 100 / System.Math.Max(1, modelMs)) + "%";
