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
        internal string CatalogPath => IsCatalogLinked ? AssetDatabase.GUIDToAssetPath(catalogGuid) : null;

        private void SetCatalogDependencies(HashSet<string> dependencies)
        {
            ShaderFXCatalog.RemoveShaderDependencies(this, catalogDependencies);
            catalogDependencies = dependencies;
            if (IsCatalogLinked) ShaderFXCatalog.SetShaderDependencies(this, catalogDependencies);
        }

        internal void ReleaseCatalogDependencies() => SetCatalogDependencies(null);

        private void RetryCatalogAfterUnlock()
        {
            if (catalogReloadPending) ReloadCatalogSource(true);
        }

        private string CatalogHash(string path, string source = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                SetCatalogDependencies(null);
                return "missing";
            }
            var dependencies = ShaderFXSourceBuilder.GetDependencies(source ?? code, path);
            SetCatalogDependencies(dependencies);
            var sorted = new List<string>(dependencies);
            sorted.Sort(StringComparer.Ordinal);
            var hashSource = new System.Text.StringBuilder();
            foreach (string dependency in sorted)
                hashSource.Append(dependency).Append(':').Append(AssetDatabase.GetAssetDependencyHash(dependency)).Append('\n');
            return Hash128.Compute(hashSource.ToString()).ToString();
        }

        internal void PrepareParameterDeclarations()
        {
            var next = ShaderFXMetadata.Parse(code, IsCatalogLinked, out _);
            ShaderFXMetadata.PreserveValues(next, parameters);
            parameters = next;
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
            if (entry.assetPreset)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                var asset = AssetDatabase.LoadAssetAtPath<ShaderFX>(assetPath);
                if (asset == null || asset.EmbeddedOwner != null)
                    throw new InvalidOperationException("The Shader FX preset is no longer available.");
                var copy = asset.CloneForDocument(owner);
                copy.DetachCatalog();
                try
                {
                    if (!copy.HasAppliedShader) copy.ApplyAgentDraft();
                    return copy;
                }
                catch { DestroyImmediate(copy); throw; }
            }
            string source = ShaderFXCatalog.ReadSource(entry.path);
            ShaderFXMetadata.Parse(source, true, out _);
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
            ReleaseCatalogDependencies();
            catalogGuid = null;
            catalogDependencyHash = null;
        }

        internal void ReloadCatalogSource(bool force = false)
        {
            if (!IsCatalogLinked) return;
            if (WhimTexApi.IsShaderFXContentLocked(this)) { catalogReloadPending |= force; return; }
            catalogReloadPending = false;
            string path = CatalogPath;
            bool contentChanged = false;
            try
            {
                string source = ShaderFXCatalog.ReadSource(path);
                // TIFF stores path-resolved fallback code. Compare the actual catalog dependency
                // graph, including newly added includes, rather than the saved fallback text.
                string hash = CatalogHash(path, source);
                contentChanged = hash != catalogDependencyHash || path != catalogSourcePath;
                if (!force && !contentChanged) return;
                ShaderFXMetadata.Parse(source, true, out string menuPath);
                var upgraded = WhimTexFileCompatibility0125.UpgradeLinkedPresetParameters(catalogGuid, code, source, parameters);
                if (upgraded != null) parameters = upgraded;
                code = source;
                catalogSourcePath = path;
                catalogDependencyHash = hash;
                name = menuPath.Substring(menuPath.LastIndexOf('/') + 1);
                ApplyCore(contentChanged);
            }
            catch (Exception error)
            {
                lastApplyFailed = true;
                diagnostics = error.Message;
                if (contentChanged || CatalogHash(path) != catalogDependencyHash) NotifyValuesChanged();
                else QueueNotification(false);
            }
        }
    }
}
