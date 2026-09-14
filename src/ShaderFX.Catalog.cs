using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [SerializeField, HideInInspector] private string catalogGuid;
        [SerializeField, HideInInspector] private string catalogSourcePath;
        [SerializeField, HideInInspector] private string catalogDependencyHash;
        [NonSerialized] private HashSet<string> catalogDependencies;
        [NonSerialized] private bool catalogReloadPending;
        internal bool IsCatalogLinked => !string.IsNullOrEmpty(catalogGuid);
        internal bool UsesCodeParameters => IsCatalogLinked || ShaderFXMetadata.HasDeclarations(code) || parameters.Exists(p => p != null && p.declaredInCode);
        internal string CatalogPath => IsCatalogLinked ? AssetDatabase.GUIDToAssetPath(catalogGuid) : null;

        internal void OnCatalogFilesChanged(HashSet<string> paths)
        {
            if (!IsCatalogLinked) return;
            catalogDependencies ??= ShaderFXSourceBuilder.GetDependencies(code, catalogSourcePath);
            if (!catalogDependencies.Overlaps(paths)) return;
            catalogDependencies = null;
            ReloadCatalogSource(true);
        }

        private void RetryCatalogAfterUnlock()
        {
            if (catalogReloadPending) ReloadCatalogSource(true);
        }

        private string CatalogHash(string path)
        {
            if (string.IsNullOrEmpty(path)) return "missing";
            var dependencies = ShaderFXSourceBuilder.GetDependencies(code, path);
            var sorted = new List<string>(dependencies);
            sorted.Sort(StringComparer.Ordinal);
            var hashSource = new System.Text.StringBuilder();
            foreach (string dependency in sorted)
                hashSource.Append(dependency).Append(':').Append(AssetDatabase.GetAssetDependencyHash(dependency)).Append('\n');
            return Hash128.Compute(hashSource.ToString()).ToString();
        }

        internal void PrepareParameterDeclarations()
        {
            if (UsesCodeParameters)
            {
                var next = ShaderFXMetadata.Parse(code, IsCatalogLinked, out _);
                ShaderFXMetadata.PreserveValues(next, parameters);
                // Explicit legacy/API parameters may initialize declarations, but may not silently disappear.
                foreach (var old in parameters)
                    if (old != null && !old.declaredInCode && !next.Exists(p => p.name == old.name && p.type == old.type))
                        throw new FormatException("Code declarations must include the existing parameter: " + old.name);
                parameters = next;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parameters)
            {
                if (p == null) continue;
                if (!Guid.TryParseExact(p.id, "N", out _) || !ids.Add(p.id))
                {
                    p.id = Guid.NewGuid().ToString("N");
                    ids.Add(p.id);
                }
            }
        }

        internal static ShaderFX FromCatalog(TextureCompositor owner, ShaderFXCatalog.Entry entry)
        {
            if (entry.asset != null)
            {
                var copy = entry.asset.CloneForDocument(owner);
                copy.DetachCatalog();
                try
                {
                    if (!copy.HasAppliedShader) copy.ApplyAgentDraft();
                    return copy;
                }
                catch { DestroyImmediate(copy); throw; }
            }
            string source = ShaderFXCatalog.ReadSource(entry.path);
            if (entry.user) source = ShaderFXSourceBuilder.ExportIncludes(source, entry.path);
            var effect = CreateAgentDraft(owner, source, new List<ShaderFXParameter>());
            effect.name = entry.menuPath.Substring(entry.menuPath.LastIndexOf('/') + 1);
            effect.catalogGuid = entry.user ? null : entry.guid;
            effect.catalogSourcePath = entry.user ? null : entry.path;
            effect.catalogDependencyHash = entry.user ? null : effect.CatalogHash(entry.path);
            try { effect.ApplyAgentDraft(); return effect; }
            catch { DestroyImmediate(effect); throw; }
        }

        internal void DetachCatalog()
        {
            // Keep the source path as the include base when embedding a copy.
            catalogGuid = null;
            catalogDependencyHash = null;
        }

        internal void ReloadCatalogSource(bool force = false)
        {
            if (!IsCatalogLinked) return;
            if (WhimTexApi.IsShaderFXContentLocked(this)) { catalogReloadPending |= force; return; }
            catalogReloadPending = false;
            string path = CatalogPath;
            string hash = CatalogHash(path);
            if (!force && hash == catalogDependencyHash && path == catalogSourcePath) return;
            catalogDependencyHash = hash;
            try
            {
                string source = ShaderFXCatalog.ReadSource(path);
                ShaderFXMetadata.Parse(source, true, out string menuPath);
                code = source;
                catalogSourcePath = path;
                catalogDependencies = null;
                catalogDependencyHash = CatalogHash(path);
                name = menuPath.Substring(menuPath.LastIndexOf('/') + 1);
                Apply();
            }
            catch (Exception error)
            {
                lastApplyFailed = true;
                diagnostics = error.Message;
                NotifyValuesChanged();
            }
        }
    }
}
