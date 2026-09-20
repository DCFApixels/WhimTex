using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        private sealed class TiffLiveTerminalResponse
        {
            internal string sessionId, operation, requestId, canonical, canonicalHash;
            internal JObject response;
        }

        private static readonly Dictionary<string, TiffLiveSession> tiffLiveSessions =
            new Dictionary<string, TiffLiveSession>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TiffLiveTerminalResponse> tiffLiveTerminalResponses =
            new Dictionary<string, TiffLiveTerminalResponse>(StringComparer.Ordinal);
        private static readonly Queue<TiffLiveTerminalResponse> tiffLiveTerminalOrder = new Queue<TiffLiveTerminalResponse>();
        private const int MaxRememberedTiffLiveResponses = 32;
        private static bool tiffLiveTerminalStoreLoaded;
        private static string TiffLiveTerminalStorePath => Path.Combine(ProjectRoot, "Library", "WhimTex", "tiff-live-receipts.json");

        public static string TiffLiveFile(string requestPath) => Respond(() => RunTiffLive(Parse(ReadRequestFile(requestPath))));
        public static string TiffLiveJson(string json) => Respond(() => RunTiffLive(Parse(json)));

        private static JObject RunTiffLive(JObject request)
        {
            string phase = null;
            try
            {
                phase = Text(request, "op");
                return TiffLive(request);
            }
            catch (WhimTexApiException error)
            {
                if (string.IsNullOrEmpty(error.Phase)) error.Phase = phase;
                throw;
            }
        }

        private static JObject TiffLive(JObject request)
        {
            EnsureTiffLiveTerminalStoreLoaded();
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string op = Text(request, "op");
            switch (op)
            {
                case "begin": return BeginTiffLive(request);
                case "list": return ListTiffLive(request);
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

            // A session ID may be intentionally reused after a completed/cancelled session.
            // Its old terminal replay is no longer relevant once a new session starts.
            ForgetTiffLiveTerminalResponse(id);
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
            var result = TiffLiveSnapshot(session);
            result["phase"] = "status";
            return result;
        }

        private static JObject ListTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "assetPath");
            string path = null;
            if (request["assetPath"] != null) path = TiffPath(Text(request, "assetPath"));
            var sessions = new JArray();
            foreach (TiffLiveSession session in tiffLiveSessions.Values)
            {
                if (path != null && !string.Equals(session.path, path, StringComparison.OrdinalIgnoreCase)) continue;
                sessions.Add(TiffLiveSummary(session));
            }
            var result = Success();
            result["sessions"] = sessions;
            result["count"] = sessions.Count;
            return result;
        }

        private static JObject TiffLiveSummary(TiffLiveSession session)
        {
            return new JObject
            {
                ["sessionId"] = session.id,
                ["assetPath"] = session.path,
                ["create"] = session.create,
                ["state"] = "pending",
                ["baseRevision"] = session.baseRevision,
                ["diskRevision"] = session.diskRevision,
                ["workingRevision"] = Revision(session.working.Document),
                ["width"] = session.width,
                ["height"] = session.height,
                ["lastRequestId"] = session.lastRequestId
            };
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
            result["lastRequestId"] = session.lastRequestId;
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
            AddTiffLiveContext(replay, session, request, render ? "render" : "preview");
            return replay;
        }

        private static JObject CompleteTiffLive(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "operations");
            if (TryReplayTiffLiveTerminal(request, "complete", out JObject replay)) return replay;
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
                AddTiffLiveContext(result, session, request, "complete");
                RememberTiffLiveTerminalResponse(request, "complete", result);
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
            Keys(request, "apiVersion", "op", "sessionId", "requestId");
            if (TryReplayTiffLiveTerminal(request, "cancel", out JObject replay)) return replay;
            string id = Text(request, "sessionId");
            TiffLiveSession session = GetTiffLiveSession(id);
            var result = new JObject { ["apiVersion"] = ProtocolVersion, ["success"] = true, ["sessionId"] = id, ["cancelled"] = true,
                ["assetPath"] = session.path, ["state"] = "cancelled", ["phase"] = "cancel" };
            result["requestId"] = Text(request, "requestId");
            result["baseRevision"] = session.baseRevision;
            result["diskRevision"] = session.diskRevision;
            result["workingRevision"] = Revision(session.working.Document);
            RememberTiffLiveTerminalResponse(request, "cancel", result);
            RemoveTiffLiveSession(id);
            return result;
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
                AddTiffLiveContext(result, session, request, render ? "render" : "preview");
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

        private static void AddTiffLiveContext(JObject result, TiffLiveSession session, JObject request, string phase)
        {
            result["phase"] = phase;
            result["baseRevision"] = session.baseRevision;
            result["diskRevision"] = session.diskRevision;
            result["workingRevision"] = Revision(session.working.Document);
            string requestId = Text(request, "requestId");
            if (!string.IsNullOrWhiteSpace(requestId)) result["requestId"] = requestId;
        }

        private static bool TryReplayTiffLiveTerminal(JObject request, string operation, out JObject response)
        {
            EnsureTiffLiveTerminalStoreLoaded();
            response = null;
            string id = Text(request, "sessionId");
            if (string.IsNullOrWhiteSpace(id) || !tiffLiveTerminalResponses.TryGetValue(id, out TiffLiveTerminalResponse remembered) ||
                !string.Equals(remembered.operation, operation, StringComparison.Ordinal)) return false;
            string canonical = request.ToString(Formatting.None);
            Require(string.Equals(remembered.requestId, Text(request, "requestId"), StringComparison.Ordinal) &&
                string.Equals(remembered.canonicalHash, HashRequest(canonical), StringComparison.Ordinal),
                "requestId was already completed with different TIFF live arguments.", "request_conflict");
            response = (JObject)remembered.response.DeepClone();
            response["replayed"] = true;
            return true;
        }

        private static void RememberTiffLiveTerminalResponse(JObject request, string operation, JObject response)
        {
            EnsureTiffLiveTerminalStoreLoaded();
            string id = Text(request, "sessionId");
            if (string.IsNullOrWhiteSpace(id)) return;
            ForgetTiffLiveTerminalResponse(id);
            var remembered = new TiffLiveTerminalResponse
            {
                sessionId = id,
                operation = operation,
                requestId = Text(request, "requestId"),
                canonical = request.ToString(Formatting.None),
                canonicalHash = HashRequest(request.ToString(Formatting.None)),
                response = (JObject)response.DeepClone()
            };
            tiffLiveTerminalResponses[id] = remembered;
            tiffLiveTerminalOrder.Enqueue(remembered);
            while (tiffLiveTerminalOrder.Count > MaxRememberedTiffLiveResponses)
            {
                TiffLiveTerminalResponse expired = tiffLiveTerminalOrder.Dequeue();
                if (tiffLiveTerminalResponses.TryGetValue(expired.sessionId, out TiffLiveTerminalResponse current) && ReferenceEquals(current, expired))
                    tiffLiveTerminalResponses.Remove(expired.sessionId);
            }
            PersistTiffLiveTerminalStore();
        }

        private static void ForgetTiffLiveTerminalResponse(string id)
        {
            EnsureTiffLiveTerminalStoreLoaded();
            tiffLiveTerminalResponses.Remove(id);
            PersistTiffLiveTerminalStore();
        }

        private static string HashRequest(string canonical)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }

        private static void EnsureTiffLiveTerminalStoreLoaded()
        {
            if (tiffLiveTerminalStoreLoaded) return;
            tiffLiveTerminalStoreLoaded = true;
            try
            {
                string path = TiffLiveTerminalStorePath;
                if (!File.Exists(path)) return;
                var items = JArray.Parse(File.ReadAllText(path));
                foreach (JToken item in items)
                {
                    if (!(item is JObject entry)) continue;
                    string id = Text(entry, "sessionId");
                    string operation = Text(entry, "operation");
                    string requestId = entry["requestId"] == null || entry["requestId"].Type == JTokenType.Null ? null : Text(entry, "requestId");
                    string canonicalHash = Text(entry, "canonicalHash");
                    if (string.IsNullOrWhiteSpace(id) || (operation != "complete" && operation != "cancel") ||
                        string.IsNullOrWhiteSpace(canonicalHash) || !(entry["response"] is JObject response)) continue;
                    var remembered = new TiffLiveTerminalResponse { sessionId = id, operation = operation,
                        requestId = requestId, canonicalHash = canonicalHash, response = (JObject)response.DeepClone() };
                    tiffLiveTerminalResponses[id] = remembered;
                    tiffLiveTerminalOrder.Enqueue(remembered);
                }
                TrimTiffLiveTerminalStore();
            }
            catch (Exception error)
            {
                tiffLiveTerminalResponses.Clear();
                tiffLiveTerminalOrder.Clear();
                Debug.LogWarning("WhimTex: could not load TIFF live receipts: " + error.Message);
            }
        }

        private static void PersistTiffLiveTerminalStore()
        {
            if (!tiffLiveTerminalStoreLoaded) return;
            try
            {
                TrimTiffLiveTerminalStore();
                var items = new JArray();
                foreach (TiffLiveTerminalResponse remembered in tiffLiveTerminalOrder)
                    if (tiffLiveTerminalResponses.TryGetValue(remembered.sessionId, out TiffLiveTerminalResponse current) && ReferenceEquals(current, remembered))
                    {
                        var entry = new JObject { ["sessionId"] = remembered.sessionId, ["operation"] = remembered.operation,
                            ["canonicalHash"] = remembered.canonicalHash, ["response"] = remembered.response.DeepClone() };
                        if (!string.IsNullOrEmpty(remembered.requestId)) entry["requestId"] = remembered.requestId;
                        items.Add(entry);
                    }
                string path = TiffLiveTerminalStorePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, items.ToString(Formatting.None), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception error)
            {
                try { if (File.Exists(TiffLiveTerminalStorePath + ".tmp")) File.Delete(TiffLiveTerminalStorePath + ".tmp"); } catch { }
                Debug.LogWarning("WhimTex: could not persist TIFF live receipt: " + error.Message);
            }
        }

        private static void TrimTiffLiveTerminalStore()
        {
            while (tiffLiveTerminalOrder.Count > MaxRememberedTiffLiveResponses)
            {
                TiffLiveTerminalResponse expired = tiffLiveTerminalOrder.Dequeue();
                if (tiffLiveTerminalResponses.TryGetValue(expired.sessionId, out TiffLiveTerminalResponse current) && ReferenceEquals(current, expired))
                    tiffLiveTerminalResponses.Remove(expired.sessionId);
            }
        }

        private static void RemoveTiffLiveSession(string id)
        {
            if (!tiffLiveSessions.TryGetValue(id, out TiffLiveSession session)) return;
            tiffLiveSessions.Remove(id);
            session.Dispose();
        }
    }
}
