using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static readonly Regex ShaderDiagnosticLocation = new Regex(
            "^(Warning|Error|Info):\\s*(.*):(\\d+):\\s*(.*)$", RegexOptions.CultureInvariant);

        /// <summary>Compiles a marked HLSL Shader FX preset in a transient Unity shader and returns compiler diagnostics without changing a document.</summary>
        public static string CompileFXPreset(string presetPath) => Respond(() => CompileFXPresetResult(presetPath));

        /// <summary>Compiles raw WhimTex ApplyFX HLSL in a transient Unity shader without changing a document.</summary>
        public static string CompileFXSource(string source, string includeBasePath = null) => Respond(() =>
        {
            Require(!string.IsNullOrWhiteSpace(source), "source is required.", "invalid_shader_source");
            string sourcePath = string.IsNullOrWhiteSpace(includeBasePath)
                ? "Assets/WhimTexAgentInput.hlsl"
                : ResolveShaderFXIncludeBasePath(includeBasePath);
            var result = CompileFXSourceResult(source, sourcePath, false);
            result["sourcePath"] = string.IsNullOrWhiteSpace(includeBasePath) ? null : sourcePath;
            return result;
        });

        /// <summary>Compiles exactly one of a marked preset path or raw WhimTex ApplyFX HLSL source.</summary>
        public static string CompileFX(string presetPath = null, string source = null, string includeBasePath = null) => Respond(() =>
        {
            bool hasPath = !string.IsNullOrWhiteSpace(presetPath);
            bool hasSource = !string.IsNullOrWhiteSpace(source);
            Require(hasPath != hasSource, "Supply exactly one of presetPath or source.", "invalid_request");
            if (hasPath) return CompileFXPresetResult(presetPath);
            Require(includeBasePath == null || !string.IsNullOrWhiteSpace(includeBasePath), "includeBasePath cannot be empty.", "invalid_preset_path");
            string sourcePath = string.IsNullOrWhiteSpace(includeBasePath)
                ? "Assets/WhimTexAgentInput.hlsl"
                : ResolveShaderFXIncludeBasePath(includeBasePath);
            JObject result = CompileFXSourceResult(source, sourcePath, false);
            result["sourcePath"] = string.IsNullOrWhiteSpace(includeBasePath) ? null : sourcePath;
            return result;
        });

        private static JObject CompileFXPresetResult(string presetPath)
        {
            string sourcePath = ResolveShaderFXPresetPath(presetPath);
            string source = File.ReadAllText(ShaderFXPresetPhysicalPath(sourcePath));
            JObject result = CompileFXSourceResult(source, sourcePath, true);
            result["presetPath"] = sourcePath;
            return result;
        }

        private static JObject CompileFXSourceResult(string source, string sourcePath, bool requireHeader)
        {
            var result = Success();
            var errors = new JArray();
            var warnings = new JArray();
            var diagnostics = new JArray();
            ShaderFX effect = null;
            string diagnosticText = null;
            bool compiled = false;
            try
            {
                if (string.IsNullOrWhiteSpace(source)) throw new FormatException("HLSL source is empty.");
                if (source.Length > 2 * 1024 * 1024) throw new FormatException("HLSL source exceeds 2 MiB.");
                if (!requireHeader && sourcePath == "Assets/WhimTexAgentInput.hlsl" && ShaderFXSourceBuilder.HasRelativeIncludes(source))
                    throw new FormatException("Raw HLSL with relative #include directives requires includeBasePath. Project, package and Unity includes can be used without it.");
                string menuPath;
                List<ShaderFXParameter> parameters = ShaderFXMetadata.Parse(source, requireHeader, out menuPath);
                effect = ShaderFX.CreateAgentDraft(null, source, parameters, sourcePath);
                effect.name = string.IsNullOrEmpty(menuPath) ? "Shader FX" : menuPath.Substring(menuPath.LastIndexOf('/') + 1);
                try
                {
                    effect.ApplyAgentDraft();
                    compiled = effect.HasAppliedShader && !effect.LastApplyFailed;
                }
                catch (Exception error)
                {
                    diagnosticText = effect.Diagnostics;
                    if (string.IsNullOrWhiteSpace(diagnosticText) || diagnosticText == "Not applied yet. Click Apply to compile this effect.")
                        diagnosticText = error.Message;
                }
                if (compiled) diagnosticText = effect.Diagnostics;
            }
            catch (Exception error)
            {
                diagnosticText = error.Message;
            }
            finally
            {
                if (effect != null) UnityEngine.Object.DestroyImmediate(effect);
            }

            AppendShaderDiagnostics(diagnosticText, diagnostics, warnings, errors, compiled);
            if (!compiled && errors.Count == 0)
            {
                var fallback = new JObject { ["severity"] = "Error", ["message"] = diagnosticText ?? "Shader compilation failed." };
                diagnostics.Add(fallback.DeepClone());
                errors.Add(fallback);
            }
            result["compiled"] = compiled;
            result["diagnostics"] = diagnostics;
            result["warnings"] = warnings;
            result["errors"] = errors;
            return result;
        }

        private static string ResolveShaderFXPresetPath(string requestedPath)
        {
            Require(!string.IsNullOrWhiteSpace(requestedPath), "presetPath is required.", "invalid_preset_path");
            string request = requestedPath.Trim().Replace('\\', '/');
            bool absolute = Path.IsPathRooted(requestedPath.Trim());
            string requestedFullPath = absolute ? Path.GetFullPath(requestedPath.Trim()) : null;
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in PresetLibraryPaths.ProjectFiles("hlsl")) candidates.Add(path.Replace('\\', '/'));
            foreach (string path in PresetLibraryPaths.UserFiles(ShaderFXCatalog.Folder, "hlsl")) candidates.Add(Path.GetFullPath(path));

            foreach (string candidate in candidates)
            {
                if (absolute)
                {
                    string physical = candidate.StartsWith("Assets/", StringComparison.Ordinal)
                        ? Path.Combine(Application.dataPath, candidate.Substring("Assets/".Length))
                        : PresetLibraryPaths.PhysicalPath(candidate);
                    if (PathsEqual(Path.GetFullPath(physical), requestedFullPath)) return candidate;
                }
                else if (string.Equals(candidate, request, StringComparison.Ordinal)) return candidate;
            }
            throw new WhimTexApiException("preset_not_found",
                "The HLSL file was not found in the Unity project or the configured Shader FX preset folder: " + requestedPath);
        }

        private static string ResolveShaderFXIncludeBasePath(string requestedPath)
        {
            Require(!string.IsNullOrWhiteSpace(requestedPath), "includeBasePath cannot be empty.", "invalid_preset_path");
            string request = requestedPath.Trim().Replace('\\', '/');
            if (Path.GetExtension(request).Equals(".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                string file = ResolveShaderFXPresetPath(requestedPath);
                return Path.GetDirectoryName(file).Replace('\\', '/') + "/WhimTexAgentInput.hlsl";
            }

            bool absolute = Path.IsPathRooted(requestedPath.Trim());
            if (absolute)
            {
                string fullPath = Path.GetFullPath(requestedPath.Trim());
                bool inAssets = PathsEqual(fullPath, Application.dataPath) || PresetLibraryPaths.IsInside(fullPath, Application.dataPath);
                bool inUserPresets = PathsEqual(fullPath, ShaderFXCatalog.Folder) || PresetLibraryPaths.IsInside(fullPath, ShaderFXCatalog.Folder);
                string packageFolder = null;
                if (!inAssets && !inUserPresets)
                    foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
                    {
                        if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal)) continue;
                        string packagePhysicalPath = Path.GetFullPath(PresetLibraryPaths.PhysicalPath(assetPath));
                        if (Directory.Exists(packagePhysicalPath) && PathsEqual(packagePhysicalPath, fullPath))
                        { packageFolder = assetPath; break; }
                    }
                Require((inAssets || inUserPresets || packageFolder != null) && Directory.Exists(fullPath),
                    "includeBasePath must be an existing directory inside Assets, Packages or the configured Shader FX preset folder, or an existing HLSL file.",
                    "invalid_include_base");
                if (inAssets)
                {
                    string assetPath = PathsEqual(fullPath, Application.dataPath) ? "Assets" : PresetLibraryPaths.AssetPath(fullPath);
                    Require(!string.IsNullOrEmpty(assetPath), "The absolute include base is outside Assets.", "invalid_include_base");
                    return assetPath.TrimEnd('/') + "/WhimTexAgentInput.hlsl";
                }
                if (packageFolder != null) return packageFolder.TrimEnd('/') + "/WhimTexAgentInput.hlsl";
                return Path.Combine(fullPath, "WhimTexAgentInput.hlsl").Replace('\\', '/');
            }

            Require(request == "Assets" || request.StartsWith("Assets/", StringComparison.Ordinal) ||
                request.StartsWith("Packages/", StringComparison.Ordinal),
                "A directory includeBasePath must be project-relative under Assets or Packages, absolute inside Assets or the configured user preset folder, or an existing HLSL file.",
                "invalid_include_base");
            foreach (string segment in request.Split('/'))
                Require(segment != "." && segment != "..", "includeBasePath cannot contain . or .. path segments.", "invalid_include_base");
            string physical = ShaderFXPresetPhysicalPath(request);
            Require(Directory.Exists(physical), "includeBasePath directory was not found: " + requestedPath, "invalid_include_base");
            if (request == "Assets" || request.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string fullPath = Path.GetFullPath(physical);
                Require(PathsEqual(fullPath, Application.dataPath) || PresetLibraryPaths.IsInside(fullPath, Application.dataPath),
                    "The include base is outside Assets.", "invalid_include_base");
            }
            return request.TrimEnd('/') + "/WhimTexAgentInput.hlsl";
        }

        private static bool PathsEqual(string left, string right)
        {
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison);
        }

        private static string ShaderFXPresetPhysicalPath(string path) => path == "Assets"
            ? Application.dataPath
            : path.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.Combine(Application.dataPath, path.Substring("Assets/".Length))
            : PresetLibraryPaths.PhysicalPath(path);

        private static void AppendShaderDiagnostics(string text, JArray all, JArray warnings, JArray errors, bool compiled)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "Applied successfully.") return;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line == "Applied successfully.") continue;
                Match match = ShaderDiagnosticLocation.Match(line);
                var item = new JObject();
                if (match.Success)
                {
                    item["severity"] = match.Groups[1].Value;
                    item["file"] = match.Groups[2].Value;
                    item["line"] = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    item["message"] = match.Groups[4].Value;
                }
                else
                {
                    string severity = line.StartsWith("Warning:", StringComparison.Ordinal) ? "Warning" :
                        line.StartsWith("Error:", StringComparison.Ordinal) ? "Error" : compiled ? "Info" : "Error";
                    item["severity"] = severity;
                    item["message"] = line;
                }
                all.Add(item);
                if ((string)item["severity"] == "Warning") warnings.Add(item.DeepClone());
                else if ((string)item["severity"] == "Error") errors.Add(item.DeepClone());
            }
        }

        /// <summary>Reads TIFF container metadata without creating a compositor or materializing Drawing pixels.</summary>
        public static string InspectStorage(string assetPath) => Respond(() =>
        {
            string path = TiffPath(assetPath);
            Require(File.Exists(FullPath(path)), "No TIFF document at " + path, "document_not_found");
            Require(WhimTexDocumentFile.IsDocument(path), "The file is not a WhimTex TIFF document: " + path, "invalid_document");
            var storage = WhimTexDocumentFile.InspectStorage(path);
            var blocks = new JArray();
            for (int i = 0; i < storage.names.Length; i++)
                blocks.Add(new JObject { ["name"] = storage.names[i], ["bytes"] = storage.sizes[i] });
            var result = Success();
            result["assetPath"] = path;
            result["format"] = "tiff";
            result["diskRevision"] = DiskRevision(FullPath(path));
            result["fileBytes"] = new FileInfo(FullPath(path)).Length;
            result["blockCount"] = storage.blocks;
            result["modelBytes"] = storage.modelBytes;
            result["embeddedTextureBytes"] = storage.textureBytes;
            result["blocks"] = blocks;
            return result;
        });

        /// <summary>Validates document structure, limits and embedded Shader FX without saving.</summary>
        public static string Validate(string assetPath, bool render = false) => Respond(() =>
        {
            string path = DocumentPath(assetPath);
            var result = Success();
            var errors = new JArray();
            var warnings = new JArray();
            var shaderFx = new JArray();
            result["assetPath"] = path;
            result["format"] = IsTiffPath(path) ? "tiff" : "asset";
            result["renderRequested"] = render;
            TextureCompositor document = null;
            Texture2D preview = null;
            try
            {
                bool loaded;
                string loadError = null;
                if (IsTiffPath(path)) loaded = WhimTexDocumentFile.TryLoad(path, out document, out loadError, true);
                else
                {
                    document = Load(path);
                    loaded = document != null;
                }
                if (!loaded)
                {
                    errors.Add(loadError ?? "The document could not be loaded.");
                    result["valid"] = false;
                    result["errors"] = errors;
                    result["warnings"] = warnings;
                    return result;
                }
                if (!string.IsNullOrEmpty(document.documentLoadWarning)) errors.Add(document.documentLoadWarning);
                try { WhimTexDocumentLimits.Validate(document); }
                catch (Exception error) { errors.Add(error.Message); }
                try { ValidateTargets(document, path); }
                catch (Exception error) { errors.Add(error.Message); }
                int layerCount = 0;
                int drawingCount = 0;
                foreach (Layer layer in Enumerate(document.layers))
                {
                    layerCount++;
                    if (layer?.Behaviour is DrawingLayerBehaviour) drawingCount++;
                    if (layer?.modifiers == null) continue;
                    foreach (UnityEngine.Object modifier in layer.modifiers)
                    {
                        if (!(modifier is ShaderFX effect)) continue;
                        var fx = new JObject { ["name"] = effect.name, ["pendingChanges"] = effect.HasPendingChanges,
                            ["lastApplyFailed"] = effect.LastApplyFailed, ["diagnostics"] = effect.Diagnostics ?? "" };
                        shaderFx.Add(fx);
                        if (effect.HasPendingChanges || effect.LastApplyFailed)
                            errors.Add("Shader FX '" + effect.name + "' is not ready: " + effect.Diagnostics);
                    }
                }
                result["width"] = document.width;
                result["height"] = document.height;
                result["layerCount"] = layerCount;
                result["drawingLayerCount"] = drawingCount;
                result["shaderFx"] = shaderFx;
                if (render && errors.Count == 0)
                {
                    try
                    {
                        RequireGraphics();
                        preview = document.ComposePreview(1024);
                        Require(preview != null, "The document produced no preview.", "render_failed");
                        result["rendered"] = true;
                        result["renderWidth"] = preview.width;
                        result["renderHeight"] = preview.height;
                    }
                    catch (Exception error) { errors.Add(error.Message); result["rendered"] = false; }
                }
                else result["rendered"] = false;
                result["valid"] = errors.Count == 0;
                result["errors"] = errors;
                result["warnings"] = warnings;
                return result;
            }
            finally
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                ReleaseTransientDocument(document);
            }
        });

        /// <summary>Reports disk identity and editor/session state for a document path.</summary>
        public static string Status(string assetPath) => Respond(() =>
        {
            string path = DocumentPath(assetPath);
            string full = FullPath(path);
            var info = new FileInfo(full);
            var displayed = WhimTexDocumentService.FindDisplayed(path);
            var result = Success();
            result["assetPath"] = path;
            result["format"] = IsTiffPath(path) ? "tiff" : "asset";
            result["exists"] = info.Exists;
            result["guid"] = AssetDatabase.AssetPathToGUID(path);
            result["imported"] = AssetDatabase.LoadMainAssetAtPath(path) != null;
            result["isDocument"] = info.Exists && (IsTiffPath(path) ? WhimTexDocumentFile.IsDocument(path) :
                TextureCompositor.FindDocument(AssetDatabase.LoadMainAssetAtPath(path)) != null);
            result["fileBytes"] = info.Exists ? info.Length : 0;
            result["lastWriteUtc"] = info.Exists ? info.LastWriteTimeUtc.ToString("O") : null;
            result["diskRevision"] = info.Exists ? DiskRevision(full) : null;
            result["importErrors"] = info.Exists && WhimTexDocumentFile.ImportHasErrors(path);
            result["hasOutputTexture"] = AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
            result["open"] = displayed != null;
            result["dirty"] = displayed != null && (EditorUtility.IsDirty(displayed) || displayed.documentBinding?.dirty == true);
            result["busy"] = displayed != null && TextureCompositorWindow.IsDocumentBusyForApi(displayed);
            result["liveJobs"] = PendingLiveJobs(path, out int editLocks);
            result["editLocks"] = editLocks;
            result["sessionIds"] = SessionIds(path);
            result["independentLiveSessionIds"] = IndependentLiveSessionIds(path);
            result["independentLive"] = IndependentLiveSessionIds(path).Count != 0;
            var staged = StagedFiles(full);
            result["stagedRecovery"] = staged.Count != 0;
            result["stagedFiles"] = new JArray(staged);
            return result;
        });

        private static string DiskRevision(string fullPath)
        {
            using var hash = SHA256.Create();
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static int PendingLiveJobs(string path, out int editLocks)
        {
            int pending = 0;
            editLocks = 0;
            foreach (LiveJob job in liveJobs.Values)
            {
                if (job.state != "pending" || job.document == null || !string.Equals(WhimTexDocumentService.PathOf(job.document), path, StringComparison.OrdinalIgnoreCase)) continue;
                pending++;
                if (job.editing) editLocks++;
            }
            return pending;
        }

        private static JArray SessionIds(string path)
        {
            var result = new JArray();
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window.AgentDocument != null && string.Equals(WhimTexDocumentService.PathOf(window.AgentDocument), path, StringComparison.OrdinalIgnoreCase))
                    result.Add(window.AgentSessionId);
            return result;
        }

        private static JArray IndependentLiveSessionIds(string path)
        {
            var result = new JArray();
            foreach (TiffLiveSession session in tiffLiveSessions.Values)
                if (string.Equals(session.path, path, StringComparison.OrdinalIgnoreCase)) result.Add(session.id);
            return result;
        }

        private static List<string> StagedFiles(string fullPath)
        {
            var result = new List<string>();
            string directory = Path.GetDirectoryName(fullPath);
            string name = Path.GetFileName(fullPath) + ".";
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return result;
            foreach (string file in Directory.GetFiles(directory, name + "*.whimtex-tmp"))
                result.Add(Path.GetRelativePath(ProjectRoot, file).Replace('\\', '/'));
            return result;
        }
    }
}
