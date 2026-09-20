using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
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
