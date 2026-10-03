// One-off probe: attributes the document model serialization time. It hashes every pixel block the same
// way the serializer's block cache does, so the cost of the cache key can be compared with the whole call.
// Uses the package-owned BASE_Heart fixture; run with Unity Pipeline eval_file.
const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
string path = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
    "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BASE_Heart.tiff");
if (!System.IO.File.Exists(path)) return "SKIP: package fixture is missing";
var serializer = typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly
    .GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
var serialize = serializer.GetMethod("Serialize", Any);
// The current pixel cache uses a native Hash128 fingerprint, followed by exact byte comparison.

byte[] file = System.IO.File.ReadAllBytes(path);
long length = System.BitConverter.ToInt64(file, file.Length - 16);
var payload = new byte[length];
System.Buffer.BlockCopy(file, (int)(file.Length - 16 - length), payload, 0, (int)length);
using var parsed = DCFApixels.WhimTex.WhimTexDocumentContainer.Parse(payload);

var document = DCFApixels.WhimTex.WhimTexDocumentFile.Load(path);
try
{
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
    using var native = new Unity.Collections.NativeArray<byte>(raw.Length, Unity.Collections.Allocator.Persistent);
    Unity.Collections.NativeArray<byte>.Copy(raw, native);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    UnityEngine.Hash128.Compute(native);
    hashMs += watch.ElapsedMilliseconds;
    blocks++;
}
return "PROBE: model=" + modelMs + "ms pixelHash=" + hashMs + "ms for " + blocks + " blocks ("
    + (rawBytes / 1024 / 1024) + "MB) => Hash128 fingerprint share " + (hashMs * 100 / System.Math.Max(1, modelMs)) + "%";
}
finally { UnityEngine.Object.DestroyImmediate(document); }
