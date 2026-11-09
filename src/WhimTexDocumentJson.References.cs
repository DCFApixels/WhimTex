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

    public sealed partial class WhimTexDocument
    {
        [SerializeField, HideInInspector] internal List<WhimTexJsonMissingAsset> jsonMissingAssets = new();
        [SerializeField, HideInInspector] internal WhimTexJsonWriteMode jsonWriteMode = WhimTexJsonWriteMode.FullOptimized;
        public WhimTexJsonWriteMode JsonWriteMode => jsonWriteMode;
    }

    public static partial class WhimTexDocumentJson
    {
        private static bool TryGetMissingAsset(object owner, string field, out JToken identity)
        {
            if (owner is ShaderParameter parameter && field == nameof(ShaderParameter.textureValue))
            {
                PreserveShaderParameterAssets(parameter, parameter);
                identity = string.IsNullOrEmpty(parameter.missingTextureAsset) ? null : JObject.Parse(parameter.missingTextureAsset);
                return identity != null;
            }
            identity = null;
            return MissingAssets.TryGetValue(owner, out var missing) && missing.TryGetValue(field, out identity);
        }

        private static void SetMissingAsset(object owner, string field, JToken identity)
        {
            if (owner is ShaderParameter parameter && field == nameof(ShaderParameter.textureValue))
                parameter.missingTextureAsset = identity?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty;
            else if (identity != null) MissingAssets.GetOrCreateValue(owner)[field] = identity.DeepClone();
            else if (MissingAssets.TryGetValue(owner, out var missing)) missing.Remove(field);
        }

        internal static void CopyMissingAssets(WhimTexDocument source, WhimTexDocument destination,
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

        private static void VisitReferenceOwners(WhimTexDocument document, Action<object, string> visit)
        {
            var seen = new HashSet<object>();
            void Walk(object value, string path)
            {
                if (IsNull(value) || value is string || value.GetType().IsValueType || !seen.Add(value)) return;
                if (value is Layer layer) path = "layer/" + layer.Id;
                if (value is ShaderFX fx) path = "fx/" + fx.ShaderKey;
                if (value is WhimTexLight light) path = "light/" + light.id;
                if (value is UnityEngine.Object && !(value is WhimTexDocument) && !(value is ShaderFX)) return;
                visit(value, path);
                if (value is IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        // Graph edits reorder lists. Keep the owning program/subgraph in the
                        // path, but address its editable items by identity rather than position.
                        string key = list[i] switch
                        {
                            ShaderParameter parameter => "@" + parameter.id,
                            ShaderGraphNode node => "@" + node.id,
                            ShaderGraphSubGraph subGraph => "@" + subGraph.id,
                            ShaderGraphInput input => "@" + input.port,
                            _ => i.ToString()
                        };
                        Walk(list[i], path + "/" + key);
                    }
                }
                else foreach (var field in Fields(value.GetType())) Walk(field.GetValue(value), path + "/" + field.Name);
            }
            Walk(document, "document");
        }

        internal static void CaptureMissingAssets(WhimTexDocument document)
        {
            document.jsonMissingAssets = new List<WhimTexJsonMissingAsset>();
            VisitReferenceOwners(document, (owner, path) =>
            {
                // Parameters carry their unresolved texture alongside the value, so moving
                // or copying them needs no document-relative recovery path.
                if (owner is ShaderParameter parameter) { PreserveShaderParameterAssets(parameter, parameter); return; }
                if (MissingAssets.TryGetValue(owner, out var missing))
                    foreach (var entry in missing) document.jsonMissingAssets.Add(new WhimTexJsonMissingAsset
                    { ownerPath = path, field = entry.Key, identity = entry.Value.ToString(Newtonsoft.Json.Formatting.None) });
            });
        }

        internal static void RestoreMissingAssets(WhimTexDocument document)
        {
            // Undo can reuse managed owners while restoring their serialized references.
            // Rebuild their weak lookup so Redo cannot resurrect a cleared texture.
            if (document.lights != null)
                foreach (var light in document.lights)
                    if (light != null) MissingAssets.Remove(light);
            ClearShaderInputs(document.layers);
            void ClearShaderInputs(List<Layer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer?.Behaviour is ShaderLayerBehaviour shader) MissingAssets.Remove(shader);
                    ClearShaderInputs(layer?.children);
                }
            }
            if (document.jsonMissingAssets == null || document.jsonMissingAssets.Count == 0) return;
            var owners = new Dictionary<string, object>(StringComparer.Ordinal);
            VisitReferenceOwners(document, (owner, path) => owners[path] = owner);
            foreach (var missing in document.jsonMissingAssets)
                if (owners.TryGetValue(missing.ownerPath, out var owner))
                {
                    // Retain records already held by an open document, but never override
                    // a parameter's own state, including an explicitly cleared reference.
                    if (owner is ShaderParameter parameter && missing.field == nameof(ShaderParameter.textureValue))
                    {
                        if (parameter.missingTextureAsset == null)
                            parameter.missingTextureAsset = IsNull(parameter.textureValue) ? missing.identity : string.Empty;
                    }
                    else SetMissingAsset(owner, missing.field, JObject.Parse(missing.identity));
                }
        }
    }
}
