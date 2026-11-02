using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        private string presetId { get => Program.presetId; set => Program.presetId = value; }
        private int presetVersion { get => Program.presetVersion; set => Program.presetVersion = value; }
        [NonSerialized] private string catalogSourcePath;
        [NonSerialized] private string presetOriginPath;
        [NonSerialized] private ShaderPresetLink presetLink;
        [NonSerialized] private bool catalogReloadPending;
        private ShaderPresetLink PresetLink => presetLink ??= new ShaderPresetLink(() => ReloadCatalogSource());
        internal bool IsCatalogLinked => ShaderPresetLink.IsLinked(Program);
        internal string PresetId => presetId;
        internal int PresetVersion => presetVersion;
        internal bool UsingPresetFallback => presetLink?.UsingFallback == true;
        internal string CatalogPath
        {
            get
            {
                var entry = IsCatalogLinked ? ShaderFXCatalog.FindPreset(presetId) : null;
                return entry?.version == presetVersion ? entry.path : null;
            }
        }
        internal void ValidatePresetReference() => ShaderPresetLink.ValidateReference(Program);
        internal string PresetSourcePath => IsCatalogLinked ? CatalogPath ?? catalogSourcePath : presetOriginPath;

        internal void ReleaseCatalogDependencies()
        {
            presetLink?.Dispose();
            presetLink = null;
        }

        private void RetryCatalogAfterUnlock()
        {
            if (catalogReloadPending) ReloadCatalogSource(true);
        }

        internal void PrepareParameterDeclarations()
        {
            if (Mode == ShaderLayerMode.Graph) return;
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
            var loaded = ShaderPresetIO.Load(entry);
            var effect = CreateAgentDraft(owner, loaded.program.code, loaded.program.codeParameters, loaded.path);
            effect.ReplaceProgram(loaded.program);
            effect.name = loaded.menuPath.Substring(loaded.menuPath.LastIndexOf('/') + 1);
            effect.catalogSourcePath = loaded.path;
            effect.presetOriginPath = loaded.path;
            try
            {
                effect.RestoreSavedProgram();
                if (effect.IsCatalogLinked) effect.PresetLink.Adopt(loaded);
                return effect;
            }
            catch { DestroyImmediate(effect); throw; }
        }

        internal void DetachCatalog()
        {
            string basePath = SourcePath;
            bool applied = code == appliedCode;
            code = WhimTexDocumentJson.PortableShaderCode(code, basePath, true);
            if (!string.IsNullOrEmpty(Program.compiledCode))
                Program.compiledCode = WhimTexDocumentJson.PortableShaderCode(Program.compiledCode, basePath, false);
            if (Mode == ShaderLayerMode.Code)
                appliedCode = applied ? code : string.IsNullOrEmpty(appliedCode) ? appliedCode :
                    ShaderFXSourceBuilder.ExportIncludes(ShaderFXIconMetadata.MakePortable(appliedCode, basePath), basePath);
            ReleaseCatalogDependencies();
            ShaderPresetLink.Detach(Program);
            presetOriginPath = null;
            catalogSourcePath = null;
            catalogReloadPending = false;
        }

        internal void ReloadCatalogSource(bool force = false)
        {
            if (!IsCatalogLinked) return;
            if (WhimTexApi.IsShaderFXContentLocked(this)) { catalogReloadPending = true; return; }
            catalogReloadPending = false;
            var link = PresetLink;
            bool restoreFallback = false;
            bool restored = link.Refresh(Program, candidate =>
            {
                var fallback = Program;
                string previousPath = catalogSourcePath;
                bool applied = false;
                try
                {
                    restoreFallback = true;
                    ReplaceProgram(candidate);
                    catalogSourcePath = ShaderFXCatalog.FindPreset(candidate.presetId)?.path;
                    RestoreCatalogProgram();
                    applied = true;
                    restoreFallback = false;
                    return true;
                }
                finally
                {
                    if (!applied) { ReplaceProgram(fallback); catalogSourcePath = previousPath; }
                }
            }, force || !HasAppliedShader || LastApplyFailed);
            if (restored)
            {
                catalogSourcePath = link.SourcePath;
                presetOriginPath = link.SourcePath;
                if (!string.IsNullOrEmpty(link.MenuPath)) name = link.MenuPath.Substring(link.MenuPath.LastIndexOf('/') + 1);
                if (link.ContentChanged)
                {
                    EditorUtility.SetDirty(this);
                    NotifyValuesChanged();
                }
                return;
            }
            // ReplaceProgram retains the same-mode shader and clears LastApplyFailed. Neither
            // flag proves that rollback restored the saved program's derived resources.
            if (restoreFallback || !HasAppliedShader || LastApplyFailed)
            {
                try { RestoreCatalogProgram(); }
                catch (Exception) { return; } // RestoreSavedProgram already recorded the failure.
            }
            SetDiagnostics(new[] { new ShaderFXDiagnostic(ShaderFXDiagnosticSeverity.Warning,
                link.Error + " Using the saved program; the preset link is retained.") }, false);
            QueueNotification(false);
        }

        private void RestoreCatalogProgram()
        {
            Shader previous = compiledShader;
            try { RestoreSavedProgram(); }
            finally
            {
                if (previous != null && previous != compiledShader && !AssetDatabase.Contains(previous))
                    DestroyImmediate(previous);
            }
        }
    }
}
