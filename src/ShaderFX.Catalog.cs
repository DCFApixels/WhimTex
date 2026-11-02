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
                var entry = IsCatalogLinked ? ShaderPresetCatalog.FindPreset(presetId) : null;
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
            if (Mode == ShaderProgramMode.Graph) return;
            parameters = Program.PrepareCodeParameters(draftCode, effectContext: true);
        }

        internal static ShaderFX FromCatalog(WhimTexDocument owner, ShaderPresetCatalog.Entry entry)
        {
            var loaded = ShaderPresetIO.Load(entry);
            var effect = CreateAgentDraft(owner, loaded.program.draftCode, loaded.program.draftCodeParameters, loaded.path);
            effect.ReplaceProgram(loaded.program);
            effect.name = loaded.menuPath.Substring(loaded.menuPath.LastIndexOf('/') + 1);
            effect.catalogSourcePath = loaded.path;
            effect.presetOriginPath = loaded.path;
            try
            {
                ShaderProgramRuntime.ValidateInactiveProgram(loaded.program, loaded.path, effectContext: true);
                effect.RestoreWorkingProgram();
                if (effect.IsCatalogLinked) effect.PresetLink.Adopt(loaded);
                return effect;
            }
            catch { DestroyImmediate(effect); throw; }
        }

        internal void DetachCatalog()
        {
            string basePath = SourcePath;
            bool applied = draftCode == workingProgramSource;
            draftCode = WhimTexDocumentJson.PortableShaderCode(draftCode, basePath, true);
            if (!string.IsNullOrEmpty(Program.workingCode))
                Program.workingCode = WhimTexDocumentJson.PortableShaderCode(Program.workingCode, basePath, false);
            if (Mode == ShaderProgramMode.Code)
                workingProgramSource = applied ? draftCode : string.IsNullOrEmpty(workingProgramSource) ? workingProgramSource :
                    ShaderSourceBuilder.ExportIncludes(ShaderIconMetadata.MakePortable(workingProgramSource, basePath), basePath);
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
            bool restored = link.Refresh(Program, candidate =>
            {
                string candidatePath = ShaderPresetCatalog.FindPreset(candidate.presetId)?.path;
                string previousPath = catalogSourcePath;
                bool applied = ShaderPresetLink.ApplyCandidate(Program, candidate, candidatePath, effectContext: true, value =>
                {
                    ReplaceProgram(value);
                    catalogSourcePath = ReferenceEquals(value, candidate) ? candidatePath : previousPath;
                }, () => { RestoreCatalogProgram(); return null; }, out string error);
                if (!applied) throw new InvalidOperationException(error);
                return true;
            }, force || !HasWorkingShader || LastCompileFailed);
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
            if (!HasWorkingShader || LastCompileFailed)
            {
                try { RestoreCatalogProgram(); }
                catch (Exception) { return; } // RestoreWorkingProgram already recorded the failure.
            }
            SetDiagnostics(new[] { new ShaderDiagnostic(ShaderDiagnosticSeverity.Warning,
                link.Error + " Using the saved program; the preset link is retained.") }, false);
            QueueNotification(false);
        }

        private void RestoreCatalogProgram()
        {
            Shader previous = compiledShader;
            try { RestoreWorkingProgram(); }
            finally
            {
                if (previous != null && previous != compiledShader && !AssetDatabase.Contains(previous))
                    DestroyImmediate(previous);
            }
        }
    }
}
