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
        [NonSerialized] private (string id, int version) catalogBinding;
        [NonSerialized] private ShaderPresetLink presetLink;
        [NonSerialized] private bool catalogReloadPending;
        private ShaderPresetLink PresetLink => presetLink ??= new ShaderPresetLink(() => ReloadCatalogSource());
        internal bool IsCatalogLinked => ShaderPresetLink.IsLinked(Program);
        internal string PresetId => presetId;
        internal int PresetVersion => presetVersion;
        private bool HasCurrentCatalogBinding => catalogBinding == (presetId, presetVersion);
        private string CurrentCatalogSourcePath => HasCurrentCatalogBinding ? catalogSourcePath : null;
        internal bool UsingPresetFallback => IsCatalogLinked && HasCurrentCatalogBinding && presetLink?.UsingFallback == true;
        internal string CatalogPath
        {
            get
            {
                var entry = IsCatalogLinked ? ShaderPresetCatalog.FindPreset(presetId) : null;
                return entry?.version == presetVersion ? entry.path : null;
            }
        }
        internal void ValidatePresetReference() => ShaderPresetLink.ValidateReference(Program);
        internal string PresetSourcePath => IsCatalogLinked ? CatalogPath ?? CurrentCatalogSourcePath : null;

        internal void ReleaseCatalogDependencies()
        {
            presetLink?.Dispose();
            presetLink = null;
        }

        private void ResetCatalogBinding()
        {
            ReleaseCatalogDependencies();
            catalogBinding = (presetId, presetVersion);
            catalogSourcePath = null;
            catalogReloadPending = false;
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
            try
            {
                effect.InstallPreset(loaded);
                return effect;
            }
            catch { DestroyImmediate(effect); throw; }
        }

        internal bool ApplyPreset(ShaderPresetCatalog.Entry entry, out string error)
        {
            error = null;
            if (WhimTexApi.IsShaderFXContentLocked(this)) return false;
            try
            {
                if (IsCatalogLinked && entry?.id == presetId && entry.version == presetVersion) return true;
                InstallPreset(ShaderPresetIO.Load(entry));
                EditorUtility.SetDirty(this);
                NotifyValuesChanged();
                return true;
            }
            catch (Exception failure) { error = failure.Message; return false; }
        }

        private void InstallPreset(ShaderPresetIO.Loaded loaded)
        {
            ShaderProgramRuntime.ValidateInactiveProgram(loaded.program, loaded.path, effectContext: true);
            ShaderProgramRuntime candidate = ShaderProgramRuntime.RestoreWorking(loaded.program, loaded.program.mode,
                loaded.path, effectContext: true);
            try
            {
                ReplaceProgram(loaded.program);
                documentIncludeBasePath = loaded.path;
                ResetCatalogBinding();
                name = loaded.menuPath.Substring(loaded.menuPath.LastIndexOf('/') + 1);
                AcceptProgram(candidate, working: true);
                candidate = null;
                if (IsCatalogLinked)
                {
                    catalogSourcePath = loaded.path;
                    PresetLink.Adopt(loaded);
                }
            }
            finally { candidate?.Dispose(); }
        }

        internal void DetachCatalog()
        {
            string basePath = SourcePath;
            draftCode = WhimTexDocumentJson.PortableShaderCode(draftCode, basePath, true);
            if (!string.IsNullOrEmpty(Program.workingCode))
                Program.workingCode = WhimTexDocumentJson.PortableShaderCode(Program.workingCode, basePath, false);
            ShaderPresetLink.Detach(Program);
            ResetCatalogBinding();
        }

        internal void ClearPreset()
        {
            if (!IsCatalogLinked) return;
            var replacement = ShaderProgram.CreateEffect();
            replacement.mode = Mode;
            replacement.workingCode = replacement.draftCode;
            if (replacement.mode == ShaderProgramMode.Code)
                replacement.draftCodeParameters = replacement.PrepareCodeParameters(replacement.draftCode, effectContext: true);
            ReplaceProgram(replacement);
            documentIncludeBasePath = null;
            ResetCatalogBinding();
            EditorUtility.SetDirty(this);
            NotifyValuesChanged();
        }

        internal void ReloadCatalogSource(bool force = false)
        {
            if (!IsCatalogLinked) return;
            if (WhimTexApi.IsShaderFXContentLocked(this)) { catalogReloadPending = true; return; }
            if (!HasCurrentCatalogBinding) ResetCatalogBinding();
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

        private void RestoreCatalogProgram() => RestoreWorkingProgram();
    }
}
