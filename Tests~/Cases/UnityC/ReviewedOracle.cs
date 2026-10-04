// Test-only, explicit path/hash registration. Never discover, generate or overwrite an oracle.
namespace WhimTex.Tests.UnityC
{
    public static class ReviewedOracle
    {
        [System.Serializable] public sealed class FileRecord
        {
            public string name, path, sha256, provenance;
        }
        [System.Serializable] sealed class Manifest
        {
            public int version;
            public FileRecord live4;
            public FileRecord[] seamlessOptimization;
            public FileRecord[] images;
        }
        const string ManifestPath = "Packages/com.dcfapixels.whimtex/Tests~/CoverageAudit/unity-cd-oracles.json";
        static Manifest Load()
        {
            // Pipeline's interpreted nested DTOs are not reliable JsonUtility targets.
            // Use only public Newtonsoft token APIs and explicitly construct our records.
            var json = System.Reflection.Assembly.Load("Newtonsoft.Json");
            var objectType = json.GetType("Newtonsoft.Json.Linq.JObject", true);
            var tokenType = json.GetType("Newtonsoft.Json.Linq.JToken", true);
            var kindProperty = tokenType.GetProperty("Type");
            var itemProperty = objectType.GetProperty("Item", new[] { typeof(string) });
            string Kind(object token) => token == null ? "Null" : kindProperty.GetValue(token).ToString();
            object At(object token, string key) => itemProperty.GetValue(token, new object[] { key });
            string Text(object token, string key, bool optional = false)
            {
                var value = At(token, key);
                if (optional && Kind(value) == "Null") return null;
                if (Kind(value) != "String")
                    throw new System.IO.InvalidDataException("Oracle field must be a string: " + key);
                return value.ToString();
            }
            FileRecord Record(object token)
            {
                if (Kind(token) != "Object")
                    throw new System.IO.InvalidDataException("Oracle registration must be an object");
                return new FileRecord { name = Text(token, "name", true), path = Text(token, "path"),
                    sha256 = Text(token, "sha256"), provenance = Text(token, "provenance") };
            }
            FileRecord[] Records(object token)
            {
                if (Kind(token) == "Null") return System.Array.Empty<FileRecord>();
                if (Kind(token) != "Array")
                    throw new System.IO.InvalidDataException("Oracle registrations must be an array");
                var result = new System.Collections.Generic.List<FileRecord>();
                foreach (object item in (System.Collections.IEnumerable)token) result.Add(Record(item));
                return result.ToArray();
            }
            var root = objectType.GetMethod("Parse", new[] { typeof(string) })
                .Invoke(null, new object[] { System.IO.File.ReadAllText(ManifestPath) });
            var version = At(root, "version");
            if (Kind(version) != "Integer" || version.ToString() != "1")
                throw new System.IO.InvalidDataException("Expected reviewed oracle manifest version 1");
            var live = At(root, "live4");
            return new Manifest { version = 1, live4 = Kind(live) == "Null" ? null : Record(live),
                seamlessOptimization = Records(At(root, "seamlessOptimization")), images = Records(At(root, "images")) };
        }
        static byte[] Read(FileRecord file, long bytes)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.path) ||
                string.IsNullOrWhiteSpace(file.provenance) || file.sha256 == null || file.sha256.Length != 64)
                throw new System.IO.InvalidDataException("Oracle requires an explicit path, SHA-256 and reviewed historical provenance");
            foreach (char c in file.sha256)
                if (!System.Uri.IsHexDigit(c)) throw new System.IO.InvalidDataException("Invalid oracle SHA-256");
            // Keep the authenticated bytes in memory, so reads cannot race a later file replacement.
            byte[] data = System.IO.File.ReadAllBytes(System.IO.Path.GetFullPath(file.path));
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actual = System.BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                if (!string.Equals(actual, file.sha256, System.StringComparison.OrdinalIgnoreCase))
                    throw new System.IO.InvalidDataException("Oracle hash mismatch: " + file.path);
            }
            if (bytes >= 0 && data.LongLength != bytes)
                throw new System.IO.InvalidDataException("Oracle byte length mismatch: " + file.path);
            return data;
        }
        public static string Live(string label, System.Func<UnityEngine.Texture2D, string> body, bool diagnostic = false)
        {
            try
            {
                var file = Load().live4;
                if (file == null || string.IsNullOrEmpty(file.path))
                    return TestContext.Result("skipped", 0, "External prerequisite missing: reviewed original live_4.bin path/hash/provenance in " + ManifestPath).ToJson();
                byte[] data = Read(file, -1);
                string Execute()
                {
                    using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(data, false)))
                    {
                        int w = reader.ReadInt32(), h = reader.ReadInt32();
                        if (w <= 0 || h <= 0 || w > UnityEngine.SystemInfo.maxTextureSize || h > UnityEngine.SystemInfo.maxTextureSize ||
                            data.LongLength != 8L + 16L * w * h)
                            throw new System.IO.InvalidDataException("Invalid original live_4.bin dimensions/length");
                        var pixels = new UnityEngine.Color[checked(w * h)];
                        for (int i = 0; i < pixels.Length; i++)
                            pixels[i] = new UnityEngine.Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var texture = FixtureContext.Scope.Own(new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBAFloat, false, true));
                        texture.SetPixels(pixels); texture.Apply();
                        return body(texture);
                    }
                }
                if (diagnostic) return FixtureContext.Diagnostic(label, Execute);
                return FixtureContext.RunReport(label, Execute);
            }
            catch (System.Exception error) { return TestContext.Result("failed", 0, label, error.ToString()).ToJson(); }
        }
        public static string Seamless(string label, System.Action<System.Collections.Generic.Dictionary<string, byte[]>> body)
        {
            try
            {
                var files = Load().seamlessOptimization;
                if (files == null || files.Length == 0)
                    return TestContext.Result("skipped", 0, "External prerequisite missing: 252 reviewed pre-change seamless snapshots in " + ManifestPath).ToJson();
                if (files.Length != 252) throw new System.IO.InvalidDataException("Expected exactly 252 original seamless snapshots");
                var records = new System.Collections.Generic.Dictionary<string, FileRecord>(System.StringComparer.Ordinal);
                foreach (var file in files) records.Add(file.name, file);
                var snapshots = new System.Collections.Generic.Dictionary<string, byte[]>(System.StringComparer.Ordinal);
                foreach (var size in new[] { new UnityEngine.Vector2Int(1, 7), new UnityEngine.Vector2Int(17, 13), new UnityEngine.Vector2Int(64, 48), new UnityEngine.Vector2Int(65, 63) })
                    for (int fixture = 0; fixture < 3; fixture++) for (int mode = 0; mode < 4; mode++)
                        for (int edge = 0; edge < 3; edge++) foreach (int strength in new[] { 0, 1 })
                        {
                            if (mode == 2 && strength == 0) continue;
                            string name = $"{size.x}-{size.y}-{fixture}-{mode}-{edge}-{strength}.bin";
                            if (!records.TryGetValue(name, out var file)) throw new System.IO.InvalidDataException("Missing original snapshot " + name);
                            snapshots.Add(name, Read(file, 16L * size.x * size.y));
                        }
                return FixtureContext.Run(label, () => body(snapshots));
            }
            catch (System.Exception error) { return TestContext.Result("failed", 0, label, error.ToString()).ToJson(); }
        }
        public static string Image(string label, string name, System.Func<byte[], string> body)
        {
            try
            {
                FileRecord match = null;
                foreach (var file in Load().images ?? System.Array.Empty<FileRecord>())
                    if (file.name == name)
                    {
                        if (match != null) throw new System.IO.InvalidDataException("Duplicate reviewed image " + name);
                        match = file;
                    }
                if (match == null) return TestContext.Result("skipped", 0,
                    "External prerequisite missing: reviewed original image " + name + " in " + ManifestPath).ToJson();
                byte[] data = Read(match, -1);
                // Explicit public tokens avoid interpreted DTO array serialization loss.
                var json = System.Reflection.Assembly.Load("Newtonsoft.Json");
                var objectType = json.GetType("Newtonsoft.Json.Linq.JObject", true);
                var arrayType = json.GetType("Newtonsoft.Json.Linq.JArray", true);
                var valueType = json.GetType("Newtonsoft.Json.Linq.JValue", true);
                var itemProperty = objectType.GetProperty("Item", new[] { typeof(string) });
                var stringConstructor = valueType.GetConstructor(new[] { typeof(string) });
                var add = arrayType.GetMethod("Add", new[] { typeof(object) });
                object Text(string value) => stringConstructor.Invoke(new object[] { value });
                void Set(object target, string key, object value) => itemProperty.SetValue(target, value, new object[] { key });
                object artifacts = System.Activator.CreateInstance(arrayType);
                string diagnostic = FixtureContext.Diagnostic(label, () =>
                {
                    var scope = FixtureContext.Scope;
                    string report = body(data);
                    if (!object.ReferenceEquals(scope, FixtureContext.Scope))
                        throw new System.IO.InvalidDataException("Image diagnostic changed its owned scope");
                    string root = System.IO.Path.GetFullPath(scope.Temp);
                    string leaf = System.IO.Path.GetFileName(root);
                    if (leaf.Length != 39 || !leaf.StartsWith("UnityC-", System.StringComparison.Ordinal) ||
                        !System.Guid.TryParseExact(leaf.Substring(7), "N", out _))
                        throw new System.IO.InvalidDataException("Expected current GUID diagnostic output scope");
                    string prefix = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                        + System.IO.Path.DirectorySeparatorChar;
                    string input = System.IO.Path.GetFullPath(match.path);
                    if (input.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                        throw new System.IO.InvalidDataException("Registered input cannot be a diagnostic output");
                    var pending = new System.Collections.Generic.Stack<string>();
                    var files = new System.Collections.Generic.List<string>();
                    pending.Push(root);
                    while (pending.Count > 0)
                    {
                        string directory = pending.Pop();
                        if ((System.IO.File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0)
                            throw new System.IO.IOException("Refusing linked diagnostic output directory");
                        foreach (string child in System.IO.Directory.GetFileSystemEntries(directory))
                        {
                            string absolute = System.IO.Path.GetFullPath(child);
                            var attributes = System.IO.File.GetAttributes(absolute);
                            if (!absolute.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) ||
                                (attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                                throw new System.IO.IOException("Diagnostic output escaped current owned scope");
                            if ((attributes & System.IO.FileAttributes.Directory) != 0) pending.Push(absolute);
                            else files.Add(absolute);
                        }
                    }
                    files.Sort(System.StringComparer.Ordinal);
                    foreach (string file in files)
                    {
                        object artifact = System.Activator.CreateInstance(objectType);
                        Set(artifact, "name", Text(System.IO.Path.GetRelativePath(root, file).Replace('\\', '/')));
                        Set(artifact, "encoding", Text("base64"));
                        Set(artifact, "content", Text(System.Convert.ToBase64String(System.IO.File.ReadAllBytes(file))));
                        add.Invoke(artifacts, new object[] { artifact });
                    }
                    return report; // Capture completes before Diagnostic disposes this exact scope.
                });
                object result = objectType.GetMethod("Parse", new[] { typeof(string) })
                    .Invoke(null, new object[] { diagnostic });
                var status = itemProperty.GetValue(result, new object[] { "status" });
                if (status == null || status.ToString() != "skipped") return diagnostic;
                // Never replace failed assertion/body/capture/cleanup results or create a pass.
                Set(result, "artifacts", artifacts);
                return result.ToString();
            }
            catch (System.Exception error) { return TestContext.Result("failed", 0, label, error.ToString()).ToJson(); }
        }
    }
}
