using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class WhimTexJsonMissingAsset
    {
        public string ownerPath;
        public string field;
        public string identity;
    }

    public sealed partial class TextureCompositor
    {
        [SerializeField, HideInInspector] internal List<WhimTexJsonMissingAsset> jsonMissingAssets = new();
        [SerializeField, HideInInspector] internal WhimTexJsonWriteMode jsonWriteMode = WhimTexJsonWriteMode.FullOptimized;
        public WhimTexJsonWriteMode JsonWriteMode => jsonWriteMode;
    }

    public static partial class WhimTexDocumentJson
    {
        internal static void CopyMissingAssets(TextureCompositor source, TextureCompositor destination,
            Dictionary<string, string> ids, Dictionary<ShaderFX, ShaderFX> effects)
        {
            if (source.jsonMissingAssets == null) return;
            destination.jsonMissingAssets ??= new List<WhimTexJsonMissingAsset>();
            var copies = new List<WhimTexJsonMissingAsset>();
            foreach (var entry in source.jsonMissingAssets)
            {
                string path = entry.ownerPath;
                bool matched = false;
                foreach (var pair in ids)
                    if (path.StartsWith("layer/" + pair.Key + "/", StringComparison.Ordinal))
                    { path = "layer/" + pair.Value + path.Substring(6 + pair.Key.Length); matched = true; break; }
                foreach (var pair in effects)
                    if (path.StartsWith("fx/" + pair.Key.ShaderKey + "/", StringComparison.Ordinal))
                    { path = "fx/" + pair.Value.ShaderKey + path.Substring(3 + pair.Key.ShaderKey.Length); matched = true; break; }
                if (matched) copies.Add(new WhimTexJsonMissingAsset { ownerPath = path, field = entry.field, identity = entry.identity });
            }
            destination.jsonMissingAssets.AddRange(copies);
            RestoreMissingAssets(destination);
        }

        private static void VisitReferenceOwners(TextureCompositor document, Action<object, string> visit)
        {
            var seen = new HashSet<object>();
            void Walk(object value, string path)
            {
                if (IsNull(value) || value is string || value.GetType().IsValueType || !seen.Add(value)) return;
                if (value is Layer layer) path = "layer/" + layer.Id;
                if (value is ShaderFX fx) path = "fx/" + fx.ShaderKey;
                if (value is UnityEngine.Object && !(value is TextureCompositor) && !(value is ShaderFX)) return;
                visit(value, path);
                if (value is IList list)
                {
                    for (int i = 0; i < list.Count; i++) Walk(list[i], path + "/" + (list[i] is ShaderFXParameter parameter ? "@" + parameter.name : i.ToString()));
                }
                else foreach (var field in Fields(value.GetType())) Walk(field.GetValue(value), path + "/" + field.Name);
            }
            Walk(document, "document");
        }

        internal static void CaptureMissingAssets(TextureCompositor document)
        {
            document.jsonMissingAssets = new List<WhimTexJsonMissingAsset>();
            VisitReferenceOwners(document, (owner, path) =>
            {
                if (MissingAssets.TryGetValue(owner, out var missing))
                    foreach (var entry in missing) document.jsonMissingAssets.Add(new WhimTexJsonMissingAsset
                    { ownerPath = path, field = entry.Key, identity = entry.Value.ToString(Newtonsoft.Json.Formatting.None) });
            });
        }

        private static void RestoreMissingAssets(TextureCompositor document)
        {
            if (document.jsonMissingAssets == null || document.jsonMissingAssets.Count == 0) return;
            var owners = new Dictionary<string, object>(StringComparer.Ordinal);
            VisitReferenceOwners(document, (owner, path) => owners[path] = owner);
            foreach (var missing in document.jsonMissingAssets)
                if (owners.TryGetValue(missing.ownerPath, out var owner))
                    MissingAssets.GetOrCreateValue(owner)[missing.field] = JObject.Parse(missing.identity);
        }
    }
}
