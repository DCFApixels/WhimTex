using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    // Shared discovery, identity, value retention and replacement transaction; hosts own resources.
    internal sealed partial class ShaderPresetLink : IDisposable
    {
        private static readonly List<WeakReference<ShaderPresetLink>> tracked = new List<WeakReference<ShaderPresetLink>>();
        private Action reload;
        private string hash;
        private ShaderProgram observedProgram;
        private bool refreshing;
        internal string SourcePath { get; private set; }
        internal string MenuPath { get; private set; }
        internal string Error { get; private set; }
        internal bool UsingFallback { get; private set; }
        internal bool ContentChanged { get; private set; }

        internal ShaderPresetLink(Action reload)
        {
            this.reload = reload ?? throw new ArgumentNullException(nameof(reload));
            tracked.Add(new WeakReference<ShaderPresetLink>(this));
        }

        internal static bool IsLinked(ShaderProgram program) => !string.IsNullOrEmpty(program?.presetId);
        internal static void ValidateReference(ShaderProgram program)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            var identity = ShaderPresetIO.ValidateIdentity(program.presetId, program.presetVersion);
            program.presetId = identity.id;
        }

        internal static ShaderProgram Load(ShaderPresetCatalog.Entry entry) => ShaderPresetIO.Load(entry).program;

        // Compile returns null on success. Rollback uses the same host path to restore derived resources.
        internal static bool ApplyCandidate(ShaderProgram previous, ShaderProgram candidate, string sourcePath,
            bool effectContext, Action<ShaderProgram> install, Func<string> compile, out string error)
        {
            ShaderProgramRuntime.ValidateInactiveProgram(candidate, sourcePath, effectContext);
            try
            {
                install(candidate);
                error = compile();
                if (string.IsNullOrEmpty(error)) return true;
            }
            catch (Exception failure) { error = failure.Message; }
            try
            {
                install(previous);
                string rollbackError = compile();
                if (!string.IsNullOrEmpty(rollbackError)) error += "\nCould not restore the saved shader: " + rollbackError;
            }
            catch (Exception failure) { error += "\nCould not restore the saved shader: " + failure.Message; }
            return false;
        }

        internal static bool TryResolve(ShaderProgram current, out ShaderProgram candidate, out string error)
        {
            candidate = null;
            try { candidate = Resolve(current).program; error = null; return true; }
            catch (Exception failure) { error = failure.Message; return false; }
        }

        private static ShaderPresetIO.Loaded Resolve(ShaderProgram current)
        {
            ValidateReference(current);
            if (!IsLinked(current)) throw new IOException("The shader program has no preset link.");
            var entry = ShaderPresetCatalog.FindPreset(current.presetId);
            if (entry == null) throw new IOException("The linked shader preset is missing, invalid or has a conflicting ID.");
            if (entry.version != current.presetVersion)
                throw new IOException($"Preset version {entry.version} differs from the saved version {current.presetVersion}.");
            var loaded = ShaderPresetIO.Load(entry);
            var next = loaded.program;
            // Mode selection is local to the host; switching it does not detach either stored mode.
            next.mode = current.mode;
            if (ShaderPresetIO.IsProgramPath(entry.path))
            {
                Preserve(next.draftGraph?.parameters, current.draftGraph?.parameters, next.draftGraph,
                    next.workingGraph?.parameters, next.workingGraph);
            }
            else
            {
                // An HLSL file has no graph payload; retain the host's inactive graph in full.
                next.draftGraph = current.draftGraph;
                next.workingGraph = current.workingGraph;
            }
            Preserve(next.draftCodeParameters, current.draftCodeParameters, null, next.workingCodeParameters, null);
            return loaded;
        }

        private static void Preserve(List<ShaderParameter> next, IReadOnlyList<ShaderParameter> previous, ShaderGraph graph,
            List<ShaderParameter> working, ShaderGraph workingGraph)
        {
            if (previous == null || next == null) return;
            foreach (var saved in previous)
                if (saved != null && !next.Exists(p => p != null && p.name == saved.name &&
                    p.ValueType == saved.ValueType && ShaderMetadata.Compatible(p.type, saved.type)))
                    throw new IOException("The preset no longer declares the saved parameter " + saved.name + " with the same type.");
            var ids = new string[next.Count];
            for (int i = 0; i < next.Count; i++) ids[i] = next[i].id;
            ShaderMetadata.PreserveValues(next, previous);
            WhimTexDocumentJson.PreserveShaderCodeParameterAssets(previous, next);
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < next.Count; i++)
                if (ids[i] != next[i].id) remap[ids[i]] = next[i].id;
            // Working is restored independently of Draft. Remap the same source identities
            // in both, without matching unrelated working parameters by name or replacing
            // its separately retained values and declarations.
            if (working != null && !ReferenceEquals(working, next))
                foreach (var parameter in working)
                    if (parameter != null && remap.TryGetValue(parameter.id, out string id)) parameter.id = id;
            RemapNodes(graph);
            if (!ReferenceEquals(graph, workingGraph)) RemapNodes(workingGraph);
            void RemapNodes(ShaderGraph owner)
            {
                if (owner == null) return;
                foreach (var node in owner.AllNodes())
                    if (node.kind == ShaderGraphNodeKind.Parameter && node.parameterId != null && remap.TryGetValue(node.parameterId, out string id))
                        node.parameterId = id;
            }
        }

        internal void Adopt(ShaderPresetIO.Loaded loaded)
        {
            SourcePath = loaded.path; MenuPath = loaded.menuPath; hash = loaded.hash;
            observedProgram = loaded.program;
            Error = null; UsingFallback = false;
        }

        // A false apply result must leave the host's original program and usable shader intact.
        internal bool Refresh(ShaderProgram current, Func<ShaderProgram, bool> apply, bool force = false)
        {
            ContentChanged = false;
            if (!IsLinked(current) || refreshing || reload == null) return true;
            refreshing = true;
            try
            {
                var loaded = Resolve(current);
                if (!force && !UsingFallback && ReferenceEquals(current, observedProgram) && hash == loaded.hash && SourcePath == loaded.path) return true;
                var previous = WhimTexDocumentJson.ShaderProgramSnapshot(current, null, SourcePath ?? loaded.path);
                if (!apply(loaded.program)) throw new InvalidOperationException("The linked shader preset could not be applied.");
                // Restoring HLSL populates Working parameters; compare the completed programs.
                bool changed = !JToken.DeepEquals(previous,
                    WhimTexDocumentJson.ShaderProgramSnapshot(loaded.program, null, loaded.path));
                Adopt(loaded);
                ContentChanged = changed;
                return true;
            }
            catch (Exception error)
            {
                Error = error.Message;
                UsingFallback = true;
                return false;
            }
            finally { refreshing = false; }
        }

        internal static void RefreshTracked()
        {
            // Snapshot first: callbacks may dispose or add links while hosts replace their programs.
            var live = new List<ShaderPresetLink>();
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                if (!tracked[i].TryGetTarget(out var link) || link.reload == null) tracked.RemoveAt(i);
                else live.Add(link);
            }
            foreach (var link in live) link.reload?.Invoke();
        }

        internal static void Detach(ShaderProgram program)
        {
            if (program == null) return;
            program.presetId = null;
            program.presetVersion = 0;
        }

        public void Dispose() => reload = null;
    }
}
