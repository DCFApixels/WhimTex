using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    /// <summary>A data contract with ordered upgrades and lossless historical exports.</summary>
    public sealed class WhimTexDocumentContract
    {
        public Type Type { get; }
        public string Id { get; }
        public int CurrentVersion { get; }
        internal bool BuiltIn { get; }
        private readonly Dictionary<int, Action<JObject>> upgrades = new();
        private readonly SortedDictionary<int, Func<JObject, bool>> exports = new();

        internal WhimTexDocumentContract(Type type, string id, int version, bool builtIn)
        { Type = type; Id = id; CurrentVersion = version; BuiltIn = builtIn; }

        public WhimTexDocumentContract UpgradeFrom(int fromVersion, Action<JObject> upgrade)
        {
            CheckHistoricalVersion(fromVersion);
            if (upgrade == null) throw new ArgumentNullException(nameof(upgrade));
            if (upgrades.ContainsKey(fromVersion)) throw new ArgumentException("An upgrade is already registered.");
            upgrades.Add(fromVersion, upgrade);
            return this;
        }

        public WhimTexDocumentContract ExportTo(int version, Func<JObject, bool> export)
        {
            CheckHistoricalVersion(version);
            if (export == null) throw new ArgumentNullException(nameof(export));
            if (exports.ContainsKey(version)) throw new ArgumentException("An export is already registered.");
            exports.Add(version, export);
            return this;
        }

        private void CheckHistoricalVersion(int version)
        {
            if (version < 1 || version >= CurrentVersion)
                throw new ArgumentOutOfRangeException(nameof(version));
        }

        public JObject Upgrade(JObject fields, int version)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (version < 1 || version > CurrentVersion)
                throw new WhimTexDocumentException("Unsupported version " + version + " for " + Id + ".");
            var result = (JObject)fields.DeepClone();
            for (int step = version; step < CurrentVersion; step++)
            {
                if (!upgrades.TryGetValue(step, out var upgrade))
                    throw new WhimTexDocumentException("Missing migration for " + Id + " from version " + step + ".");
                upgrade(result);
            }
            return result;
        }

        public JObject SelectVersion(JObject fields, out int version)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            foreach (var export in exports)
            {
                var candidate = (JObject)fields.DeepClone();
                if (!export.Value(candidate)) continue;
                var restored = Upgrade(candidate, export.Key);
                if (!JToken.DeepEquals(restored, fields))
                    throw new WhimTexDocumentException("Export of " + Id + " to version " + export.Key + " would lose data.");
                version = export.Key;
                return candidate;
            }
            version = CurrentVersion;
            return (JObject)fields.DeepClone();
        }
    }

    /// <summary>Shared durable identities and contracts for file, JSON and agent readers.</summary>
    public sealed class WhimTexDocumentTypes
    {
        public static WhimTexDocumentTypes Default { get; } = CreateDefault();
        public WhimTexDocumentContract Document { get; private set; }
        public WhimTexDocumentContract Layer { get; private set; }
        private readonly Dictionary<string, WhimTexDocumentContract> ids = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<WhimTexDocumentContract>> aliases = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, WhimTexDocumentContract> types = new();

        private static WhimTexDocumentTypes CreateDefault()
        {
            var registry = new WhimTexDocumentTypes();
            registry.Document = registry.Register(typeof(WhimTexDocument), "whimtex.document", WhimTexDocumentJson.Version, builtIn: true);
            registry.Layer = registry.Register(typeof(Layer), "whimtex.layer", builtIn: true);
            // Storage IDs are explicit: renaming a menu, API alias or C# class must
            // never silently change an existing document's type identity.
            registry.Register(typeof(DrawingLayerBehaviour), "whimtex.drawing", builtIn: true);
            registry.Register(typeof(FileLayerBehaviour), "whimtex.file", builtIn: true);
            registry.Register(typeof(ColorFillLayerBehaviour), "whimtex.color", builtIn: true);
            registry.Register(typeof(GradientLayerBehaviour), "whimtex.gradient", builtIn: true);
            registry.Register(typeof(NoiseLayerBehaviour), "whimtex.noise", builtIn: true);
            registry.Register(typeof(ShaderLayerBehaviour), "whimtex.shader", builtIn: true);
            registry.Register(typeof(ShapeLayerBehaviour), "whimtex.shape", builtIn: true);
            registry.Register(typeof(VectorLayerBehaviour), "whimtex.vector", builtIn: true);
            registry.Register(typeof(TextLayerBehaviour), "whimtex.text", builtIn: true);
            registry.Register(typeof(OutlineLayerBehaviour), "whimtex.outline", builtIn: true);
            registry.Register(typeof(SDFLayerBehaviour), "whimtex.sdf", builtIn: true);
            registry.Register(typeof(NormalMapLayerBehaviour), "whimtex.normalmap", builtIn: true);
            registry.Register(typeof(BlurLayerBehaviour), "whimtex.blur", builtIn: true);
            registry.Register(typeof(SharpenLayerBehaviour), "whimtex.sharpen", builtIn: true);
            registry.Register(typeof(MakeSeamlessLayerBehaviour), "whimtex.makeseamless", builtIn: true);
            registry.Register(typeof(GroupLayerBehaviour), "whimtex.group", builtIn: true);
            registry.Register(typeof(BooleanGroupLayerBehaviour), "whimtex.booleangroup", builtIn: true);
            registry.Register(typeof(MorphGroupLayerBehaviour), "whimtex.morphgroup", builtIn: true);
            registry.Register(typeof(PendingLayerBehaviour), "whimtex.pending", builtIn: true);
            registry.Register(typeof(ShaderProcessorLayerBehaviour), "whimtex.shaderprocessor", builtIn: true);
            return registry;
        }

        public WhimTexDocumentContract Register(Type type, string id, int currentVersion = 1, bool builtIn = false)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type.IsAbstract || type.ContainsGenericParameters ||
                type != typeof(WhimTexDocument) && type != typeof(Layer) && !typeof(LayerBehaviour).IsAssignableFrom(type))
                throw new ArgumentException("Only document, layer and concrete layer behaviours have storage contracts.", nameof(type));
            if (currentVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentVersion));
            ValidateId(id);
            if (types.ContainsKey(type)) throw new ArgumentException("The type already has a storage contract.");
            bool permanent = id.IndexOf('.') >= 0;
            if (permanent && ids.ContainsKey(id)) throw new ArgumentException("Duplicate permanent type ID: " + id);
            string alias = id.Substring(id.LastIndexOf('.') + 1);
            if (aliases.TryGetValue(alias, out var matches) && builtIn && matches.Exists(c => c.BuiltIn))
                throw new ArgumentException("Duplicate built-in type alias: " + alias);
            var contract = new WhimTexDocumentContract(type, id, currentVersion, builtIn);
            types.Add(type, contract);
            if (permanent) ids.Add(id, contract);
            if (matches == null) aliases.Add(alias, matches = new List<WhimTexDocumentContract>());
            matches.Add(contract);
            if (type == typeof(WhimTexDocument)) Document = contract;
            else if (type == typeof(Layer)) Layer = contract;
            return contract;
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] == '.' || id[id.Length - 1] == '.' || id.Contains(".."))
                throw new ArgumentException("A type ID requires nonempty lowercase components.", nameof(id));
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '.'))
                    throw new ArgumentException("Invalid type ID: " + id, nameof(id));
        }

        public WhimTexDocumentContract Resolve(string id)
        {
            ValidateId(id);
            if (ids.TryGetValue(id, out var exact)) return exact;
            if (!aliases.TryGetValue(id, out var matches))
                throw new WhimTexDocumentException("Unknown document type: " + id);
            if (matches.Count == 1) return matches[0];
            foreach (var candidate in matches) if (candidate.BuiltIn) return candidate;
            throw new WhimTexDocumentException("Ambiguous type alias: " + id + ". Use a permanent ID.");
        }

        public WhimTexDocumentContract ForType(Type type)
        {
            if (!types.TryGetValue(type, out var contract))
                throw new WhimTexDocumentException("No storage contract registered for " + type + ".");
            if (!ReferenceEquals(Resolve(contract.Id), contract))
                throw new WhimTexDocumentException("The type alias cannot round-trip: " + contract.Id);
            return contract;
        }
    }
}
