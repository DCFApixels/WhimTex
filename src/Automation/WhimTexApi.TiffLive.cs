using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private sealed class TiffLiveSession : IDisposable
        {
            internal string id, path, baseRevision, diskRevision, lastRequestId, lastRequest;
            internal bool create;
            internal int width, height;
            internal WhimTexDocumentBuild baseline, working;
            internal JObject lastResponse;

            public void Dispose()
            {
                working?.Dispose();
                working = null;
                baseline?.Dispose();
                baseline = null;
            }
        }

        private static readonly Dictionary<string, TiffLiveSession> tiffLiveSessions =
            new Dictionary<string, TiffLiveSession>(StringComparer.Ordinal);

        public static string TiffLiveFile(string requestPath) => Respond(() => TiffLive(Parse(ReadRequestFile(requestPath))));
        public static string TiffLiveJson(string json) => Respond(() => TiffLive(Parse(json)));

        private static JObject TiffLive(JObject request)
        {
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string op = Text(request, "op");
            switch (op)
            {
                case "begin": return BeginTiffLive(request);
                case "status": return StatusTiffLive(request);
                case "preview": return PreviewTiffLive(request, false);
                case "render": return PreviewTiffLive(request, true);
                case "complete": return CompleteTiffLive(request);
                case "cancel": return CancelTiffLive(request);
                default: throw new WhimTexApiException("invalid_request", "Unknown TIFF live operation: " + op);
            }
        }

        private static JObject BeginTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "assetPath", "create", "width", "height", "expectedRevision");
            string id = Text(request, "sessionId");
            Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 128, "sessionId must be 1..128 characters.");
            Require(!tiffLiveSessions.ContainsKey(id), "A TIFF live session with this sessionId already exists.", "session_conflict");
            Require(tiffLiveSessions.Count < 8, "Too many TIFF live sessions. Complete or cancel an existing session.", "resource_limit");
            string path = TiffPath(Text(request, "assetPath"));
            Require(!tiffLiveSessions.Values.Any(session => string.Equals(session.path, path, StringComparison.OrdinalIgnoreCase)),
                "This TIFF already has an independent live session. Reuse its sessionId or cancel it first.", "live_session_active");
            Require(WhimTexDocumentService.FindDisplayed(path) == null,
                "This TIFF is open in WhimTex. Close the window before starting an independent live session.", "live_session_active");
            bool create = Bool(request, "create");
            int width = Int(request, "width", 512, 1, 16384);
            int height = Int(request, "height", 512, 1, 16384);
            Require((long)width * height <= MaxCanvasPixels, "TIFF live canvas exceeds the supported pixel limit.", "resource_limit");
            Require(!create || (request["expectedRevision"] == null && !File.Exists(FullPath(path)) && !File.Exists(FullPath(path) + ".meta") &&
                AssetDatabase.LoadMainAssetAtPath(path) == null), "A new TIFF destination must not exist and cannot use expectedRevision.", "already_exists");
            Require(create || request["width"] == null && request["height"] == null, "width/height are only accepted on create.");
            Require(create || !string.IsNullOrEmpty(Text(request, "expectedRevision")), "expectedRevision is required for an existing TIFF.", "revision_required");

            var session = new TiffLiveSession { id = id, path = path, create = create, width = width, height = height };
            try
            {
                if (create)
                {
                    session.baseline = WhimTexDocumentBuild.Create(width, height);
                    session.baseRevision = null;
                    session.diskRevision = null;
                }
                else
                {
                    Require(File.Exists(FullPath(path)) && WhimTexDocumentFile.IsDocument(path), "No WhimTex TIFF document at " + path, "document_not_found");
                    session.baseline = WhimTexDocumentBuild.Open(path);
                    session.width = session.baseline.Document.width;
                    session.height = session.baseline.Document.height;
                    session.baseRevision = Revision(session.baseline.Document);
                    Require(Text(request, "expectedRevision") == session.baseRevision,
                        "The document changed. Inspect it again before starting a TIFF live session.", "revision_conflict");
                    session.diskRevision = DiskRevision(FullPath(path));
                }
                Require((long)session.width * session.height <= MaxCanvasPixels, "TIFF live canvas exceeds the supported pixel limit.", "resource_limit");
                session.working = WhimTexDocumentBuild.Copy(session.baseline.Document);
                tiffLiveSessions.Add(id, session);
                return StatusTiffLive(new JObject { ["apiVersion"] = ProtocolVersion, ["op"] = "status", ["sessionId"] = id });
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private static JObject StatusTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId");
            TiffLiveSession session = GetTiffLiveSession(Text(request, "sessionId"));
            return TiffLiveSnapshot(session);
        }

        private static JObject TiffLiveSnapshot(TiffLiveSession session)
        {
            var result = Success();
            result["sessionId"] = session.id;
            result["assetPath"] = session.path;
            result["create"] = session.create;
            result["state"] = "pending";
            result["baseRevision"] = session.baseRevision;
            result["diskRevision"] = session.diskRevision;
            result["workingRevision"] = Revision(session.working.Document);
            result["width"] = session.width;
            result["height"] = session.height;
            result["document"] = Snapshot(session.working.Document, session.path);
            return result;
        }

        private static JObject PreviewTiffLive(JObject request, bool render)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "operations", "outputPath", "overwrite", "maxSize");
            TiffLiveSession session = GetTiffLiveSession(Text(request, "sessionId"));
            JObject replay = request["operations"] is JArray operations && operations.Count > 0
                ? ReplayTiffLiveRequest(session, request, render)
                : TiffLiveSnapshot(session);
            if (render)
            {
                string output = Text(request, "outputPath");
                Require(!string.IsNullOrWhiteSpace(output), "outputPath is required for render.");
                replay["preview"] = WriteTiffLivePreview(session.working.Document, output, Bool(request, "overwrite"), Int(request, "maxSize", 1024, 1, 4096));
            }
            return replay;
        }

        private static JObject CompleteTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "operations");
            TiffLiveSession session = GetTiffLiveSession(Text(request, "sessionId"));
            if (request["operations"] is JArray completeOperations && completeOperations.Count > 0)
                ReplayTiffLiveRequest(session, request, false);
            if (!session.create)
            {
                Require(File.Exists(FullPath(session.path)) && DiskRevision(FullPath(session.path)) == session.diskRevision,
                    "The TIFF changed outside this live session. Inspect it and start a new session.", "revision_conflict");
            }
            try
            {
                session.working.Save(session.path);
                var result = Success();
                result["sessionId"] = session.id;
                result["assetPath"] = session.path;
                result["saved"] = true;
                result["document"] = Snapshot(session.working.Document, session.path);
                RemoveTiffLiveSession(session.id);
                return result;
            }
            catch
            {
                // Keep the session available for status/cancel. Save itself is atomic, but a post-commit
                // import error must not make an agent unknowingly replay the batch.
                throw;
            }
        }

        private static JObject CancelTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId");
            string id = Text(request, "sessionId");
            TiffLiveSession session = GetTiffLiveSession(id);
            RemoveTiffLiveSession(id);
            return new JObject { ["apiVersion"] = ProtocolVersion, ["success"] = true, ["sessionId"] = id, ["cancelled"] = true };
        }

        private static JObject ReplayTiffLiveRequest(TiffLiveSession session, JObject request, bool render)
        {
            string requestId = Text(request, "requestId");
            string canonical = request.ToString(Formatting.None);
            if (!string.IsNullOrWhiteSpace(requestId) && session.lastRequestId == requestId)
            {
                Require(session.lastRequest == canonical, "requestId was already used for a different TIFF live request.", "request_conflict");
                return (JObject)session.lastResponse.DeepClone();
            }
            JArray operations = request["operations"] as JArray;
            Require(operations != null, "operations must be an array.");
            Require(operations.Count <= 256, "A TIFF live batch supports at most 256 operations.");
            WhimTexDocumentBuild candidate = WhimTexDocumentBuild.Copy(session.baseline.Document);
            try
            {
                var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
                var applied = new JArray();
                for (int i = 0; i < operations.Count; i++)
                {
                    Layer layer = ApplyOperation(candidate.Document, Obj(operations[i], "operation"), aliases, true);
                    applied.Add(new JObject { ["index"] = i, ["layerId"] = layer.Id, ["name"] = layer.layerName });
                }
                ValidateTargets(candidate.Document, session.path);
                candidate.Document.MarkChanged();
                session.working?.Dispose();
                session.working = candidate;
                candidate = null;
                var result = Success();
                result["sessionId"] = session.id;
                result["preview"] = true;
                result["operations"] = applied;
                result["document"] = Snapshot(session.working.Document, session.path);
                session.lastRequestId = requestId;
                session.lastRequest = canonical;
                session.lastResponse = (JObject)result.DeepClone();
                return result;
            }
            finally { candidate?.Dispose(); }
        }

        private static JObject WriteTiffLivePreview(TextureCompositor document, string outputPath, bool overwrite, int maxSize)
        {
            outputPath = outputPath.Replace('\\', '/');
            Require(outputPath.StartsWith("Temp/WhimTex/", StringComparison.Ordinal), "Preview output must be project-relative Temp/WhimTex/*.png.", "invalid_path");
            ValidateSegments(outputPath);
            Require(string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase), "Preview output must be PNG.");
            string full = FullPath(outputPath);
            RejectLinks(full);
            Require(overwrite || !File.Exists(full), "Preview exists; set overwrite=true or choose a new path.", "already_exists");
            RequireGraphics();
            Texture2D preview = null;
            try
            {
                preview = document.ComposePreview(maxSize);
                Require(preview != null, "The document produced no preview.", "render_failed");
                Texture2D encoded = HdrUtility.ToLdr(preview);
                byte[] bytes;
                try { bytes = encoded.EncodeToPNG(); }
                finally { UnityEngine.Object.DestroyImmediate(encoded); }
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (var stream = new FileStream(full, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                    stream.Write(bytes, 0, bytes.Length);
                return new JObject { ["outputPath"] = full, ["width"] = preview.width, ["height"] = preview.height };
            }
            finally { if (preview != null) UnityEngine.Object.DestroyImmediate(preview); }
        }

        private static TiffLiveSession GetTiffLiveSession(string id)
        {
            TiffLiveSession session = null;
            bool found = !string.IsNullOrWhiteSpace(id) && tiffLiveSessions.TryGetValue(id, out session);
            Require(found, "TIFF live session not found; it may have been interrupted by a script reload.", "session_closed");
            return session;
        }

        private static void RemoveTiffLiveSession(string id)
        {
            if (!tiffLiveSessions.TryGetValue(id, out TiffLiveSession session)) return;
            tiffLiveSessions.Remove(id);
            session.Dispose();
        }
    }
}
