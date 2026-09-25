using System;
using System.Collections.Generic;
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
        private sealed class LiveJob
        {
            internal string id, requestId, request, session, layerId, state = "pending", error;
            internal string targetId, targetRevision, completion;
            internal string selectionMode = "strict";
            internal bool editing;
            internal TextureCompositor document;
            internal int width, height;
            internal byte[] mask;
            internal RectInt region;
            internal JObject capture;
            internal JObject context;
        }

        private static readonly Dictionary<string, LiveJob> liveJobs = new Dictionary<string, LiveJob>();

        static WhimTexApi()
        {
            Undo.undoRedoPerformed += RefreshLiveJobs;
            Undo.undoRedoPerformed += CancelLiveEditLocks;
            TextureCompositor.Changed += _ => RefreshLiveJobs();
        }

        private static void RefreshLiveJobs()
        {
            bool releasedEdit = false;
            foreach (var job in liveJobs.Values)
                if (job.state == "pending" && (job.document == null ||
                    (job.editing ? job.document.FindLayer(job.layerId) == null :
                    !(job.document.FindLayer(job.layerId)?.Behaviour is PendingLayerBehaviour pending) || pending.jobId != job.id)))
                { job.state = "cancelled"; job.mask = null; releasedEdit |= job.editing; }
            if (releasedEdit) LiveEditLocksChanged?.Invoke();
        }

        internal static void CloseLiveSession(string session)
        {
            foreach (var job in liveJobs.Values)
                if (job.session == session && job.state == "pending")
                { job.state = "cancelled"; job.mask = null; }
            LiveEditLocksChanged?.Invoke();
        }

        internal static void TransferLiveDocument(string session, TextureCompositor previous, TextureCompositor next)
        {
            foreach (var job in liveJobs.Values)
            {
                if (job.session != session || job.document != previous || job.state != "pending") continue;
                if (job.targetId != null && previous.FindLayer(job.targetId) is Layer before &&
                    next.FindLayer(job.targetId) is Layer after && LiveLayerRevision(before) == job.targetRevision)
                    job.targetRevision = LiveLayerRevision(after);
                job.document = next;
            }
        }

        public static string LiveSessions() => Respond(() =>
        {
            var sessions = new JArray();
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
            {
                var document = window.AgentDocument;
                if (document == null) continue;
                sessions.Add(new JObject { ["sessionId"] = window.AgentSessionId,
                    ["name"] = document.name, ["assetPath"] = DocumentAssetPath(document),
                    ["focused"] = EditorWindow.focusedWindow == window,
                    ["focusOrder"] = window.AgentFocusOrder,
                    ["width"] = document.width, ["height"] = document.height });
            }
            var result = Success(); result["sessions"] = sessions; return result;
        });

        public static string LiveFile(string requestPath) => Respond(() => Live(Parse(ReadRequestFile(requestPath))));
        public static string LiveJson(string json) => Respond(() => Live(Parse(json)));

        public static string LiveBegin(string requestId, string name = "Generating…", string source = "none",
            string area = "canvas", string sessionId = null, string sourceLayerId = null,
            string selectionMode = "strict", int padding = -1) => Respond(() =>
        {
            var request = new JObject { ["apiVersion"] = ProtocolVersion, ["op"] = "begin",
                ["requestId"] = requestId, ["name"] = name, ["source"] = source, ["area"] = area };
            if (sessionId != null) request["sessionId"] = sessionId;
            if (sourceLayerId != null) request["sourceLayerId"] = sourceLayerId;
            if (area == "selection" || selectionMode != "strict") request["selectionMode"] = selectionMode;
            if (padding != -1) request["padding"] = padding;
            return Live(request);
        });

        private static TextureCompositorWindow ResolveLiveBeginWindow(string session)
        {
            if (session != null) return LiveWindow(session);
            var windows = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>()
                .Where(w => w.AgentDocument != null).ToArray();
            Require(windows.Length > 0, "Open a WhimTex document first.", "session_required");
            if (windows.Length == 1) return windows[0];
            var focused = windows.FirstOrDefault(w => EditorWindow.focusedWindow == w);
            if (focused != null) return focused;
            long newest = 0;
            bool tied = false;
            foreach (var window in windows)
            {
                if (window.AgentFocusOrder > newest)
                {
                    newest = window.AgentFocusOrder;
                    focused = window;
                    tied = false;
                }
                else if (window.AgentFocusOrder == newest) tied = true;
            }
            Require(focused != null && !tied, "Several documents are open with no unambiguous focus history. Focus one or specify sessionId; no reservation was created.", "session_ambiguous");
            return focused;
        }

        private static TextureCompositorWindow LiveWindow(string session)
        {
            Require(!string.IsNullOrEmpty(session), "Discover sessions first and specify sessionId.");
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window.AgentDocument != null && window.AgentSessionId == session) return window;
            throw new WhimTexApiException("session_closed", "The document session closed or scripts reloaded. Discover sessions again; do not deliver to a different document.");
        }

        private static void LiveReady(TextureCompositor document)
        {
            Require(document != null, "Document no longer exists.", "session_closed");
            Require(!TextureCompositorWindow.IsDocumentBusyForLiveApi(document), "Wait for the current gesture or uncommitted text edit to finish.", "document_busy");
            Require((long)document.width * document.height <= MaxCanvasPixels, "Live editing supports at most 16,777,216 canvas pixels.", "resource_limit");
        }

        private static JObject Live(JObject request)
        {
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string op = Text(request, "op");
            if (op == "inspect")
            {
                Keys(request, "apiVersion", "op", "sessionId");
                var window = LiveWindow(Text(request, "sessionId"));
                LiveReady(window.AgentDocument);
                var result = Success();
                result["sessionId"] = window.AgentSessionId;
                result["document"] = Snapshot(window.AgentDocument, DocumentAssetPath(window.AgentDocument));
                result["activeLayerId"] = window.AgentSelectedLayer?.Id;
                result["selectedLayerIds"] = new JArray(window.AgentSelectedIds);
                var selection = window.AgentSelection;
                result["selection"] = new JObject { ["active"] = selection.Active,
                    ["bounds"] = LiveRect(selection.Bounds), ["coordinates"] = "bottom-left canvas pixels" };
                return result;
            }
            if (op == "begin") return BeginLiveJob(request);
            if (op == "lock") return LockLiveLayer(request);
            if (op == "fork") return ForkLiveJob(request);
            if (op == "render") return RenderLiveSession(request);
            if (op == "cancel")
            {
                Keys(request, "apiVersion", "op", "sessionId", "layerId");
                var window = LiveWindow(Text(request, "sessionId"));
                LiveReady(window.AgentDocument);
                var selected = window.AgentDocument.FindLayer(Text(request, "layerId"));
                if (IsLayerContentLocked(window.AgentDocument, selected))
                { CancelLayerEdit(window.AgentDocument, selected); return Success(); }
                var pending = selected?.Behaviour as PendingLayerBehaviour;
                Require(pending != null, "Reservation not found.", "layer_not_found");
                CancelLiveReservation(window.AgentDocument, pending);
                return Success();
            }
            string id = Text(request, "jobId");
            Require(id != null && liveJobs.ContainsKey(id), "Job not found; it may have been interrupted by a script reload. Inspect the reservation before starting again.", "job_not_found");
            LiveJob job = liveJobs[id];
            RefreshLiveJob(job);
            if (op == "status")
            {
                Keys(request, "apiVersion", "op", "jobId");
                return LiveStatus(job);
            }
            if (op == "unlock")
            {
                Keys(request, "apiVersion", "op", "jobId");
                Require(job.editing, "unlock requires an existing-layer edit job.");
                if (job.state == "pending") { job.state = "cancelled"; NotifyLiveLockChanged(job.document); }
                return LiveStatus(job);
            }
            Require(job.state == "pending" || op == "complete" && job.state == "completed",
                "Job is " + job.state + ". Its result will not be inserted.", "job_closed");
            if (op == "complete" && job.state == "completed")
            {
                Require(job.completion == request.ToString(Formatting.None), "Job already completed with a different request.", "job_closed");
                return LiveStatus(job);
            }
            LiveReady(job.document);
            Require(job.width == job.document.width && job.height == job.document.height,
                "Canvas dimensions changed. Cancel and capture again.", "revision_conflict");
            if (job.editing && (op == "preview" || op == "complete")) return ApplyLiveEdit(job, request, op == "preview");
            if (op == "preview") return PreviewLiveCandidate(job, request);
            if (op == "complete") return CompleteLiveJob(job, request);
            if (op == "fail")
            {
                Keys(request, "apiVersion", "op", "jobId", "message");
                job.error = Text(request, "message", "Generation stopped. Cancel this reservation or retry completion.");
                if (job.editing) job.state = "cancelled";
                job.document.MarkChanged();
                if (job.editing) NotifyLiveLockChanged(job.document);
                return LiveStatus(job);
            }
            throw new WhimTexApiException("invalid_request", "Unknown live operation: " + op);
        }

        private static JObject BeginLiveJob(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "name", "parent", "index", "source", "sourceLayerId", "area", "selectionMode", "padding", "destination", "targetLayerId");
            string requestId = Text(request, "requestId");
            Require(!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128, "requestId must be a unique caller-generated string (1..128 characters). Reuse it only to retry this same begin request.");
            string canonical = request.ToString(Formatting.None);
            foreach (var existing in liveJobs.Values)
                if (existing.requestId == requestId &&
                    (request["sessionId"] == null || existing.session == Text(request, "sessionId")))
                {
                    Require(existing.request == canonical, "requestId was already used for a different request.", "request_conflict");
                    RefreshLiveJob(existing); return LiveStatus(existing);
                }
            var window = ResolveLiveBeginWindow(Text(request, "sessionId"));
            var document = window.AgentDocument;
            LiveReady(document);
            Require(liveJobs.Count < 512, "Live job limit reached for this script session. Finish work before reloading scripts.", "resource_limit");
            RefreshLiveJobs();
            Require(liveJobs.Values.Count(j => j.state == "pending") < 16 &&
                liveJobs.Values.Sum(j => j.mask == null ? 0L : j.mask.Length) + (long)document.width * document.height <= 67108864,
                "Too many pending jobs or selection masks. Finish or cancel some jobs first.", "resource_limit");
            Require(Enumerate(document.layers).Count() < 1024, "Layer limit reached.", "resource_limit");
            string destination = Text(request, "destination", "newLayer");
            Require(destination == "newLayer" || destination == "replacePixels", "destination must be newLayer or replacePixels.");
            string targetId = Text(request, "targetLayerId");
            Require(destination == "replacePixels" || targetId == null, "targetLayerId is only used for replacePixels.");
            var target = targetId == null ? null : document.FindLayer(targetId)?.Behaviour as DrawingLayerBehaviour;
            Require(!IsLayerContentLocked(document, target), "Target is locked by another job.", "layer_locked");
            Require(destination != "replacePixels" || target != null, "replacePixels requires an explicit Drawing layer target. Use newLayer for other layer types.");
            if (target != null) Require(TiledCanvasUtility.IsInvertible(target.transform) &&
                (target.transform.tiling == TransformTilingMode.Clip || target.transform.tiling == TransformTilingMode.Source),
                "Pixel replacement requires an invertible, non-repeating Drawing transform. Use newLayer for repeated sources.");
            var job = new LiveJob { id = Guid.NewGuid().ToString("N"), requestId = requestId, request = canonical,
                session = window.AgentSessionId, document = document, width = document.width, height = document.height,
                targetId = targetId, targetRevision = target == null ? null : LiveLayerRevision(target) };
            job.context = new JObject { ["name"] = document.name, ["assetPath"] = DocumentAssetPath(document),
                ["activeLayerId"] = window.AgentSelectedLayer?.Id,
                ["selectedLayerIds"] = new JArray(window.AgentSelectedIds),
                ["selectionActive"] = window.AgentSelection.Active,
                ["selectionBounds"] = LiveRect(window.AgentSelection.Bounds) };
            JObject captureRequest = request;
            if (Text(request, "source", "none") == "layer" && request["sourceLayerId"] == null)
            {
                Require(window.AgentSelectedLayer != null, "Select a source layer or specify sourceLayerId.", "layer_not_found");
                captureRequest = (JObject)request.DeepClone();
                captureRequest["sourceLayerId"] = window.AgentSelectedLayer.Id;
            }
            CaptureLiveInput(job, window, captureRequest);
            List<Layer> container = Container(document, Text(request, "parent"), new Dictionary<string, Layer>());
            int index = Int(request, "index", 0, 0, container.Count);
            var reservation = new PendingLayerBehaviour { layerName = Text(request, "name", "Generating…"), jobId = job.id };
            reservation.AssignNewId(); job.layerId = reservation.Id;
            liveJobs.Add(job.id, job);
            try { LiveChange(document, "Reserve Agent Layer", () => container.Insert(index, reservation)); }
            catch { liveJobs.Remove(job.id); throw; }
            return LiveStatus(job);
        }

        private static JObject ForkLiveJob(JObject request)
        {
            Keys(request, "apiVersion", "op", "jobId", "requestId", "name");
            string requestId = Text(request, "requestId"), sourceId = Text(request, "jobId");
            Require(!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128,
                "requestId must be a unique caller-generated string (1..128 characters).");
            string canonical = request.ToString(Formatting.None);
            foreach (var existing in liveJobs.Values)
                if (existing.requestId == requestId)
                {
                    Require(existing.request == canonical, "requestId was already used for a different request.", "request_conflict");
                    RefreshLiveJob(existing); return LiveStatus(existing);
                }
            Require(sourceId != null && liveJobs.ContainsKey(sourceId), "Source job not found.", "job_not_found");
            LiveJob source = liveJobs[sourceId];
            RefreshLiveJob(source);
            Require(source.state == "pending", "Only pending reservations can be forked.", "job_closed");
            Require(source.targetId == null, "Fork supports newLayer jobs, not competing pixel replacements.");
            LiveReady(source.document);
            Require(source.width == source.document.width && source.height == source.document.height,
                "Canvas dimensions changed. Cancel and capture again.", "revision_conflict");
            RefreshLiveJobs();
            Require(liveJobs.Count < 512 && liveJobs.Values.Count(j => j.state == "pending") < 16 &&
                liveJobs.Values.Sum(j => j.mask == null ? 0L : j.mask.Length) + (source.mask?.Length ?? 0) <= 67108864,
                "Too many pending jobs or selection masks.", "resource_limit");
            Require(Enumerate(source.document.layers).Count() < 1024, "Layer limit reached.", "resource_limit");
            var pending = source.document.FindLayer(source.layerId)?.Behaviour as PendingLayerBehaviour;
            Require(pending != null && source.document.TryFindLayer(pending, out _, out _), "Reservation is missing.", "job_closed");
            source.document.TryFindLayer(pending, out var container, out int index);
            var fork = new LiveJob { id = Guid.NewGuid().ToString("N"), requestId = requestId, request = canonical,
                session = source.session, document = source.document, width = source.width, height = source.height,
                region = source.region, mask = source.mask, selectionMode = source.selectionMode,
                capture = (JObject)source.capture.DeepClone(),
                context = (JObject)source.context.DeepClone() };
            var reservation = new PendingLayerBehaviour { layerName = Text(request, "name", pending.layerName), jobId = fork.id };
            reservation.AssignNewId(); fork.layerId = reservation.Id;
            liveJobs.Add(fork.id, fork);
            try { LiveChange(fork.document, "Reserve Agent Layer", () => container.Insert(index + 1, reservation)); }
            catch { liveJobs.Remove(fork.id); throw; }
            return LiveStatus(fork);
        }

        private static void RefreshLiveJob(LiveJob job)
        {
            if (job.state != "pending") return;
            bool open = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Any(w =>
                w.AgentDocument == job.document && w.AgentSessionId == job.session);
            var pending = job.document == null ? null : job.document.FindLayer(job.layerId)?.Behaviour as PendingLayerBehaviour;
            if (!open || (job.editing ? job.document.FindLayer(job.layerId) == null : pending == null || pending.jobId != job.id))
            {
                job.state = "cancelled"; job.mask = null;
            }
        }

        private static JObject LiveStatus(LiveJob job)
        {
            var result = Success();
            result["jobId"] = job.id; result["sessionId"] = job.session;
            result["layerId"] = job.layerId; result["state"] = job.state;
            result["message"] = job.error; result["capture"] = job.capture?.DeepClone();
            result["context"] = job.context?.DeepClone();
            result["targetLayerId"] = job.targetId;
            result["editing"] = job.editing;
            result["contentLocked"] = job.editing && job.state == "pending";
            result["saved"] = false;
            return result;
        }

        internal static string LiveReservationStatus(PendingLayerBehaviour pending)
        {
            if (pending.jobId == null || !liveJobs.TryGetValue(pending.jobId, out var job))
                return "Generation interrupted. Cancel this reservation and ask the agent to start again.";
            if (job.document == null || !ReferenceEquals(job.document.FindLayer(job.layerId), pending.Owner))
                return "Generation belongs to another document copy. Cancel this reservation to remove it.";
            RefreshLiveJob(job);
            if (job.state != "pending") return "Generation " + job.state + ". Cancel this reservation to remove it.";
            return job.error ?? "Waiting for the agent. You can rename, hide or move this layer.";
        }

        internal static bool ContainsReservation(Layer layer) => layer?.Behaviour is PendingLayerBehaviour || IsLiveLockedLayer(layer) ||
            layer?.AsGroup() is Layer group && group.layers.Any(ContainsReservation);

        internal static void CancelLiveReservation(TextureCompositor document, PendingLayerBehaviour pending)
        {
            if (!document.TryFindLayer(pending, out var container, out _)) return;
            LiveChange(document, "Cancel Agent Layer", () => container.Remove(pending));
            if (pending.jobId != null && liveJobs.TryGetValue(pending.jobId, out var job) && job.document == document && job.layerId == pending.Id)
            { job.state = "cancelled"; job.mask = null; }
        }

        private static void LiveChange(TextureCompositor document, string name, Action action)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RegisterCompleteObjectUndo(document, name);
            try
            {
                action(); document.MarkChanged();
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            }
            catch (Exception original)
            {
                try
                {
                    Undo.RevertAllDownToGroup(group); document.InvalidateDrawingLayerSurfaces();
                    document.MarkChanged();
                }
                catch (Exception rollback)
                {
                    throw new WhimTexApiException("rollback_failed", "Document may be partially changed. Inspect before retrying. " +
                        original.Message + " Rollback: " + rollback.Message);
                }
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }

        private static JArray LiveRect(RectInt rect) => new JArray(rect.x, rect.y, rect.width, rect.height);
    }
}
