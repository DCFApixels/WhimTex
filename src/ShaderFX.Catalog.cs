using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [SerializeField, HideInInspector] private string presetId;
        [SerializeField, HideInInspector] private int presetVersion;
        [NonSerialized] private string catalogSourcePath;
        [NonSerialized] private string presetOriginPath;
        [NonSerialized] private string catalogDependencyHash;
        [NonSerialized] private bool usingPresetFallback;
        [NonSerialized] private HashSet<string> catalogDependencies;
        [NonSerialized] private bool catalogReloadPending;
        internal bool IsCatalogLinked => !string.IsNullOrEmpty(presetId);
        internal string PresetId => presetId;
        internal int PresetVersion => presetVersion;
        internal bool UsingPresetFallback => usingPresetFallback;
        internal string CatalogPath => IsCatalogLinked ? ShaderFXCatalog.FindPreset(presetId)?.path : null;
        internal void ValidatePresetReference()
        {
            if (!IsCatalogLinked && presetVersion == 0) return;
            if (!(Guid.TryParseExact(presetId, "N", out var guid) || Guid.TryParseExact(presetId, "D", out guid)) ||
                guid == Guid.Empty || presetVersion < 1)
                throw new FormatException("An FX preset reference requires both presetId (GUID) and presetVersion (positive integer).");
            presetId = guid.ToString("N");
        }
        internal string PresetSourcePath
        {
            get
            {
                if (!IsCatalogLinked) return presetOriginPath;
                string path = CatalogPath;
                return string.IsNullOrEmpty(path) ? catalogSourcePath : path;
            }
        }

        private void SetCatalogDependencies(HashSet<string> dependencies)
        {
            ShaderFXCatalog.RemoveShaderDependencies(this, catalogDependencies);
            catalogDependencies = dependencies;
            if (IsCatalogLinked) ShaderFXCatalog.SetShaderDependencies(this, catalogDependencies);
        }

        internal void ReleaseCatalogDependencies()
        {
            SetCatalogDependencies(null);
            ShaderFXCatalog.UntrackLinked(this);
        }

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
            hashSource.Append(source ?? code);
            foreach (string dependency in sorted)
            {
                if (string.Equals(dependency, path, StringComparison.Ordinal)) continue;
                hashSource.Append(dependency).Append(':');
                string physical = PresetLibraryPaths.PhysicalPath(dependency);
                if (File.Exists(physical))
                {
                    if (new FileInfo(physical).Length > 2 * 1024 * 1024) throw new IOException("Included HLSL exceeds 2 MiB.");
                    hashSource.Append(Hash128.Compute(File.ReadAllText(physical)));
                }
                else hashSource.Append("missing");
                hashSource.Append('\n');
            }
            return Hash128.Compute(hashSource.ToString()).ToString();
        }

        internal void PrepareParameterDeclarations()
        {
            // The stored fallback is an embedded shader, not necessarily a catalog file.
            // Catalog sources are separately validated with requireHeader before attachment.
            var next = ShaderFXMetadata.Parse(code, false, out _);
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

        internal static ShaderFX FromCatalog(WhimTexDocument owner, ShaderFXCatalog.Entry entry)
        {
            if (entry == null || entry.HasError || string.IsNullOrEmpty(entry.path) || !entry.path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An FX preset must be a marked .hlsl file.");
            if (!string.IsNullOrEmpty(entry.id) && ShaderFXCatalog.FindPreset(entry.id)?.path != entry.path)
                throw new InvalidOperationException("The preset ID is ambiguous or its owner changed. Refresh the preset catalog.");
            string source = ShaderFXCatalog.ReadSource(entry.path);
            string originalSource = source;
            ShaderFXMetadata.Parse(source, true, out _);
            var identity = ShaderFXPresetIdentity.Read(source);
            if (identity.id != entry.id || identity.version != entry.version)
                throw new InvalidOperationException("The preset identity changed. Refresh the preset catalog.");
            source = ShaderFXSourceBuilder.ExportIncludes(ShaderFXIconMetadata.MakePortable(source, entry.path), entry.path);
            var effect = CreateAgentDraft(owner, source, new List<ShaderFXParameter>());
            effect.name = entry.menuPath.Substring(entry.menuPath.LastIndexOf('/') + 1);
            effect.presetId = entry.id;
            effect.presetVersion = entry.version;
            effect.catalogSourcePath = entry.path;
            effect.presetOriginPath = entry.path;
            try
            {
                effect.catalogDependencyHash = effect.IsCatalogLinked ? effect.CatalogHash(entry.path, originalSource) : null;
                ShaderFXCatalog.TrackLinked(effect);
                effect.ApplyAgentDraft();
                return effect;
            }
            catch { DestroyImmediate(effect); throw; }
        }

        internal void DetachCatalog()
        {
            string basePath = SourcePath;
            bool applied = code == appliedCode;
            code = ShaderFXSourceBuilder.ExportIncludes(ShaderFXIconMetadata.MakePortable(code, basePath), basePath);
            appliedCode = applied ? code : string.IsNullOrEmpty(appliedCode) ? appliedCode :
                ShaderFXSourceBuilder.ExportIncludes(ShaderFXIconMetadata.MakePortable(appliedCode, basePath), basePath);
            ReleaseCatalogDependencies();
            presetId = null;
            presetVersion = 0;
            usingPresetFallback = false;
            presetOriginPath = null;
            catalogDependencyHash = null;
        }

        internal void ReloadCatalogSource(bool force = false)
        {
            if (!IsCatalogLinked) return;
            ShaderFXCatalog.TrackLinked(this);
            if (WhimTexApi.IsShaderFXContentLocked(this)) { catalogReloadPending = true; return; }
            catalogReloadPending = false;
            string fallback = code;
            string previousPath = catalogSourcePath;
            try
            {
                var entry = ShaderFXCatalog.FindPreset(presetId);
                if (entry == null) throw new IOException("The linked preset is missing, invalid or has a conflicting ID.");
                if (entry.version != presetVersion)
                    throw new IOException($"Preset version {entry.version} differs from the saved version {presetVersion}.");
                string path = entry.path;
                string source = ShaderFXCatalog.ReadSource(path);
                var identity = ShaderFXPresetIdentity.Read(source);
                if (identity.id != presetId || identity.version != presetVersion)
                    throw new IOException("The preset identity changed during restoration.");
                string hash = CatalogHash(path, source);
                if (!force && !usingPresetFallback && HasAppliedShader && hash == catalogDependencyHash && path == catalogSourcePath) return;
                var declarations = ShaderFXMetadata.Parse(source, true, out string menuPath);
                foreach (var value in parameters)
                    if (value != null && !declarations.Exists(p => p.name == value.name && p.ValueType == value.ValueType))
                        throw new IOException("The preset no longer declares the saved parameter " + value.name + " with the same type.");
                code = ShaderFXSourceBuilder.ExportIncludes(ShaderFXIconMetadata.MakePortable(source, path), path);
                catalogSourcePath = path;
                bool contentChanged = code != fallback;
                // Publish content changes only after the candidate compiles. A failed
                // source refresh must not dirty the document that keeps its fallback.
                if (!ApplyCore(false)) throw new InvalidOperationException("The linked preset could not be compiled.");
                presetOriginPath = path;
                catalogDependencyHash = hash;
                usingPresetFallback = false;
                name = menuPath.Substring(menuPath.LastIndexOf('/') + 1);
                if (contentChanged)
                {
                    EditorUtility.SetDirty(this);
                    NotifyValuesChanged();
                }
            }
            catch (Exception error)
            {
                code = fallback;
                catalogSourcePath = previousPath;
                usingPresetFallback = true;
                if (!HasAppliedShader && !ApplyCore(false)) return;
                SetDiagnostics(new[] { new ShaderFXDiagnostic(ShaderFXDiagnosticSeverity.Warning,
                    error.Message + " Using the saved code; the preset link is retained.") }, false);
                QueueNotification(false);
            }
        }
    }
}
