using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public const int ProtocolVersion = 1;
        private const string UndoName = "WhimTex API Batch";
        private const long MaxCanvasPixels = 16777216;

        public static string ExecuteFile(string requestPath)
        {
            return Respond(() => Execute(Parse(ReadRequestFile(requestPath))));
        }

        public static string ExecuteJson(string requestJson) => Respond(() => Execute(Parse(requestJson)));

        private static string ReadRequestFile(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path), "An absolute request file path is required.");
            var file = new FileInfo(path);
            Require(file.Exists && file.Length <= 4 * 1024 * 1024, "Request file is missing or exceeds 4 MiB.");
            return File.ReadAllText(file.FullName);
        }

        private static string Respond(Func<JObject> action)
        {
            try
            {
                Require(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
                    "Use an idle Editor in Edit Mode. No refresh or compilation is started by this API.", "editor_busy");
                return action().ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                return Failure(exception).ToString(Formatting.None);
            }
        }

    private static JObject Failure(Exception exception)
    {
        var result = new JObject
        {
            ["apiVersion"] = ProtocolVersion, ["success"] = false,
            ["errorCode"] = exception is WhimTexApiException api ? api.Code : exception is JsonException ? "invalid_json" : "operation_failed",
            ["error"] = exception.Message,
            ["applied"] = exception is WhimTexApiException failed && failed.Code == "rollback_failed", ["saved"] = false
        };
        if (exception is WhimTexApiException phased && !string.IsNullOrEmpty(phased.Phase)) result["phase"] = phased.Phase;
        return result;
    }

        private static JObject Success() => new JObject { ["apiVersion"] = ProtocolVersion, ["success"] = true };

        private static JObject Execute(JObject request)
        {
            Keys(request, "apiVersion", "assetPath", "create", "width", "height", "expectedRevision", "dryRun", "save", "operations");
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string path = DocumentPath(Text(request, "assetPath"));
            bool tiff = IsTiffPath(path);
            Require(!tiff || !tiffLiveSessions.Values.Any(session => string.Equals(session.path, path, StringComparison.OrdinalIgnoreCase)),
                "This TIFF has an active independent live session. Complete or cancel it first.", "live_session_active");
            Require(!liveJobs.Values.Any(j => j.editing && j.state == "pending" && j.document != null &&
                string.Equals(AssetDatabase.GetAssetPath(j.document), path, StringComparison.OrdinalIgnoreCase)), "This document has a live edit lock. Use its live job or release the lock first.", "layer_locked");
            bool create = Bool(request, "create");
            bool dryRun = Bool(request, "dryRun");
            bool save = Bool(request, "save", true);
            Require(!create || save || dryRun, "Creating a document requires save=true.");
            Require(create || (request["width"] == null && request["height"] == null), "width/height are only accepted on create.");
            int width = Int(request, "width", 512, 1, 16384);
            int height = Int(request, "height", 512, 1, 16384);
            Require(request["operations"] is JArray, "operations must be an array (use [] for save only).");
            JArray operations = (JArray)request["operations"];
            Require(operations.Count <= 256, "A batch supports at most 256 operations.");
            TextureCompositor document = null;
            WhimTexDocumentBuild tiffBuild = null;
            if (create)
            {
                Require(!File.Exists(FullPath(path)) && !File.Exists(FullPath(path) + ".meta") &&
                    AssetDatabase.LoadMainAssetAtPath(path) == null, "The destination already exists; use create=false to edit it.", "already_exists");
                Require(request["expectedRevision"] == null, "expectedRevision cannot be used with create.");
            }
            else
            {
                if (tiff)
                {
                    tiffBuild = WhimTexDocumentBuild.Open(path);
                    document = tiffBuild.Document;
                }
                else document = Load(path);
                Require(!TextureCompositorWindow.IsDocumentBusyForApi(document), "Finish the current paint/transform gesture first.", "document_busy");
                string expected = Text(request, "expectedRevision");
                Require(!string.IsNullOrEmpty(expected), "Inspect first and supply expectedRevision when editing an existing document.", "revision_required");
                Require(expected == Revision(document), "The document changed. Inspect it again before retrying.", "revision_conflict");
            }
            Require((long)(document != null ? document.width : width) * (document != null ? document.height : height) <= MaxCanvasPixels,
                "Automation supports at most 16,777,216 canvas pixels per document.", "resource_limit");

            WhimTexDocumentBuild probeBuild = null;
            TextureCompositor probe;
            if (document == null)
                probe = ScriptableObject.CreateInstance<TextureCompositor>();
            else if (tiff)
            {
                // Object.Instantiate would leave a TIFF's non-serialized deferred descriptors behind
                // and could share a materialized Drawing texture. Build.Copy owns an independent model.
                probeBuild = WhimTexDocumentBuild.Copy(document);
                probe = probeBuild.Document;
            }
            else probe = Object.Instantiate(document);
            int operationIndex = -1;
            try
            {
                if (create) { probe.width = width; probe.height = height; }
                var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
                for (int i = 0; i < operations.Count; i++)
                {
                    operationIndex = i;
                    ApplyOperation(probe, Obj(operations[i], "operation"), aliases, false);
                }
                ValidateTargets(probe, path);
                long drawingPixels = 0;
                int layerCount = 0;
                foreach (Layer layer in Enumerate(probe.layers))
                {
                    layerCount++;
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                        drawingPixels += drawing.StoredTexture != null ? (long)drawing.StoredTexture.width * drawing.StoredTexture.height : (long)probe.width * probe.height;
                }
                Require(layerCount <= 1024 && drawingPixels <= 67108864,
                    "Automation supports at most 1024 layers and 67,108,864 owned Drawing pixels per document.", "resource_limit");
                if (dryRun)
                {
                    JObject result = Success();
                    result["dryRun"] = true;
                    result["applied"] = false;
                    result["saved"] = false;
                    result["operationCount"] = operations.Count;
                    return result;
                }
            }
            catch (Exception exception)
            {
                JObject result = Failure(exception);
                result["failedOperation"] = operationIndex;
                return result;
            }
            finally
            {
                if (probeBuild != null) probeBuild.Dispose();
                else
                {
                    probe.layers.Clear();
                    Object.DestroyImmediate(probe);
                }
            }

            RequireGraphics();
            if (create)
            {
                if (tiff)
                {
                    tiffBuild = WhimTexDocumentBuild.Create(width, height);
                    document = tiffBuild.Document;
                }
                else
                {
                    document = ScriptableObject.CreateInstance<TextureCompositor>();
                    document.name = Path.GetFileNameWithoutExtension(path);
                    document.width = width;
                    document.height = height;
                }
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            bool applied = false;
            bool saving = false;
            var results = new JArray();
            try
            {
                Undo.RegisterCompleteObjectUndo(document, UndoName);
                var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
                for (int i = 0; i < operations.Count; i++)
                {
                    operationIndex = i;
                    Layer layer = ApplyOperation(document, Obj(operations[i], "operation"), aliases, true);
                    results.Add(new JObject { ["index"] = i, ["layerId"] = layer.Id, ["name"] = layer.layerName });
                }
                document.MarkChanged();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                applied = true;
                if (save)
                {
                    saving = true;
                    EnsureAssetFolder(path);
                    if (tiff)
                    {
                        tiffBuild.Save(path);
                        tiffBuild.Dispose();
                        tiffBuild = WhimTexDocumentBuild.Open(path);
                        document = tiffBuild.Document;
                    }
                    else
                    {
                        document.SaveWithOutput(create ? path : null);
                        document = Load(path);
                    }
                }
                JObject result = Success();
                result["applied"] = true;
                result["saved"] = save;
                result["operations"] = results;
                result["document"] = Snapshot(document, path);
                return result;
            }
            catch (Exception exception)
            {
                string rollbackError = null;
                if (!saving)
                {
                    try
                    {
                        if (document != null) document.InvalidateDrawingLayerSurfaces();
                        Undo.RevertAllDownToGroup(undoGroup);
                        if (document != null)
                        {
                            document.InvalidateDrawingLayerSurfaces();
                            if (!create) document.MarkChanged();
                        }
                        applied = false;
                    }
                    catch (Exception rollbackException) { rollbackError = rollbackException.Message; applied = true; }
                }
                JObject result = Failure(exception);
                result["failedOperation"] = saving ? -1 : operationIndex;
                result["applied"] = applied;
                result["saveMayBePartial"] = saving;
                result["operations"] = results;
                result["recovery"] = saving ? "Do not replay additions or strokes. Inspect the document and save again; asset I/O is not transactional." : "The batch was reverted.";
                if (rollbackError != null)
                {
                    result["rollbackFailed"] = true;
                    result["rollbackError"] = rollbackError;
                    result["recovery"] = "Rollback failed; document state may be partial. Inspect before making further changes.";
                }
                return result;
            }
            finally
            {
                Undo.IncrementCurrentGroup();
                if (document != null) document.InvalidateDrawingLayerSurfaces();
                if (tiffBuild != null) tiffBuild.Dispose();
                else if (create && document != null && !AssetDatabase.Contains(document)) Object.DestroyImmediate(document);
            }
        }

        private static void RequireGraphics()
        {
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Rendering and painting require a graphics device; do not use -nographics.", "graphics_unavailable");
            foreach (string name in new[] { "Blend", "Transform", "PaintBrush", "AlphaConversion" })
            {
                Shader shader = Shader.Find("Hidden/TextureCompositor/" + name);
                Require(shader != null && shader.isSupported, "Required WhimTex shader is unavailable: " + name, "graphics_unavailable");
            }
        }

        private static void ValidateTargets(TextureCompositor document, string path)
        {
            foreach (Layer layer in Enumerate(document.layers))
            {
                if (layer?.Behaviour is FileLayerBehaviour file && file.sourceTexture != null)
                    Require(!string.Equals(AssetDatabase.GetAssetPath(file.sourceTexture), path, StringComparison.OrdinalIgnoreCase),
                        "A document cannot sample its own saved output texture.", "invalid_target");
                if (layer?.Behaviour is TargetedLayerBehaviour effect && effect.inputMode == EffectInputMode.Specific)
                    Require(document.IsUsableEffectTarget(effect, effect.TargetLayerId),
                        "Invalid or cyclic effect target for " + effect.layerName, "invalid_target");
            }
        }

        private static IEnumerable<Layer> Enumerate(List<Layer> layers)
        {
            foreach (Layer layer in layers)
            {
                if (layer == null) continue;
                yield return layer;
                if (layer?.AsGroup() is Layer group)
                    foreach (Layer child in Enumerate(group.layers)) yield return child;
            }
        }
    }
}
