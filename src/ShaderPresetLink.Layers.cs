using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderPresetLink
    {
        private static readonly ConditionalWeakTable<ShaderLayerBehaviour, LayerBinding> layerLinks = new();
        private static readonly HashSet<ShaderLayerBehaviour> applyingLayers = new();

        private sealed class LayerBinding
        {
            internal readonly ShaderLayerBehaviour shader;
            internal readonly WhimTexDocument document;
            internal readonly ShaderPresetLink link;
            internal ShaderProgram observed;
            internal string observedId;
            internal int observedVersion;
            internal bool pending;

            internal LayerBinding(ShaderLayerBehaviour shader, WhimTexDocument document)
            {
                this.shader = shader; this.document = document;
                link = new ShaderPresetLink(Refresh);
            }

            internal void Refresh()
            {
                if (document == null || !IsLinked(shader.program)) return;
                if (WhimTexApi.IsLayerContentLocked(document, shader.Owner)) { pending = true; return; }
                pending = false;
                bool success = link.Refresh(shader.program, candidate =>
                {
                    if (ApplyLayerCandidate(shader, document, candidate, out string error)) return true;
                    throw new InvalidOperationException(error);
                });
                observed = shader.program;
                observedId = observed.presetId;
                observedVersion = observed.presetVersion;
                if (success && link.ContentChanged)
                {
                    EditorUtility.SetDirty(document);
                    document.MarkChanged();
                }
            }
        }

        // Call from the host's preparation/render path; unchanged programs require no file reads.
        internal static void EnsureLayer(ShaderLayerBehaviour shader, WhimTexDocument document)
        {
            if (shader == null || document == null || applyingLayers.Contains(shader)) return;
            if (!IsLinked(shader.program)) { ReleaseLayer(shader); return; }
            if (!layerLinks.TryGetValue(shader, out var binding) || binding.document != document)
            {
                ReleaseLayer(shader);
                binding = new LayerBinding(shader, document);
                layerLinks.Add(shader, binding);
            }
            if (!ReferenceEquals(binding.observed, shader.program) || binding.observedId != shader.program.presetId ||
                binding.observedVersion != shader.program.presetVersion || binding.pending) binding.Refresh();
        }

        internal static bool ApplyLayer(ShaderLayerBehaviour shader, WhimTexDocument document, ShaderFXCatalog.Entry entry, out string error)
        {
            error = null;
            try
            {
                if (shader == null || document == null) throw new ArgumentNullException("A Shader layer and its document are required.");
                if (WhimTexApi.IsLayerContentLocked(document, shader.Owner)) throw new InvalidOperationException("The Shader layer is locked.");
                var loaded = ShaderPresetIO.Load(entry);
                if (!ApplyLayerCandidate(shader, document, loaded.program, out error)) return false;
                ReleaseLayer(shader);
                if (IsLinked(shader.program))
                {
                    var binding = new LayerBinding(shader, document)
                    {
                        observed = shader.program, observedId = shader.program.presetId, observedVersion = shader.program.presetVersion
                    };
                    binding.link.Adopt(loaded);
                    layerLinks.Add(shader, binding);
                }
                return true;
            }
            catch (Exception failure) { error = failure.Message; return false; }
        }

        private static bool ApplyLayerCandidate(ShaderLayerBehaviour shader, WhimTexDocument document, ShaderProgram candidate, out string error)
        {
            WhimTexDocumentJson.ValidateShaderProgramInputs(document, shader.Owner, candidate, false);
            var previous = shader.program;
            bool success = false;
            error = null;
            try
            {
                applyingLayers.Add(shader);
                shader.program = candidate;
                shader.InvalidateGraph();
                // Restoring a preset must retain pending Code edits. CompileGraph restores the
                // selected mode's saved source; a never-compiled Code program has only its draft.
                success = candidate.mode == ShaderLayerMode.Code && string.IsNullOrEmpty(candidate.compiledCode)
                    ? shader.CompileCode(document) : shader.CompileGraph(document);
                if (!success) error = shader.Diagnostics;
                return success;
            }
            finally
            {
                try
                {
                    if (!success)
                    {
                        shader.program = previous;
                        shader.InvalidateGraph();
                        // CompileGraph restores the saved working source when the selected mode is Code.
                        shader.CompileGraph(document);
                    }
                }
                finally { applyingLayers.Remove(shader); }
            }
        }

        internal static string LayerSourcePath(ShaderLayerBehaviour shader)
        {
            if (shader == null || !IsLinked(shader.program)) return null;
            var entry = ShaderFXCatalog.FindPreset(shader.program.presetId);
            if (entry?.version == shader.program.presetVersion) return entry.path;
            return layerLinks.TryGetValue(shader, out var binding) ? binding.link.SourcePath : null;
        }

        internal static string LayerError(ShaderLayerBehaviour shader)
            => shader != null && layerLinks.TryGetValue(shader, out var binding) ? binding.link.Error : null;

        internal static bool LayerUsingFallback(ShaderLayerBehaviour shader)
            => shader != null && layerLinks.TryGetValue(shader, out var binding) && binding.link.UsingFallback;

        internal static void DetachLayer(ShaderLayerBehaviour shader)
        {
            if (shader == null) return;
            if (IsLinked(shader.program))
            {
                layerLinks.TryGetValue(shader, out var binding);
                string sourcePath = shader.CodeSourcePath(binding?.document);
                string draft = WhimTexDocumentJson.PortableShaderCode(shader.program.code, sourcePath, true);
                string saved = WhimTexDocumentJson.PortableShaderCode(shader.program.compiledCode, sourcePath, false);
                // Commit both strings only after the saved executable's includes are embedded.
                shader.program.code = draft;
                shader.program.compiledCode = saved;
            }
            Detach(shader.program);
            ReleaseLayer(shader);
        }

        internal static void ReleaseLayer(ShaderLayerBehaviour shader)
        {
            if (shader == null || !layerLinks.TryGetValue(shader, out var binding)) return;
            binding.link.Dispose();
            layerLinks.Remove(shader);
        }
    }
}
