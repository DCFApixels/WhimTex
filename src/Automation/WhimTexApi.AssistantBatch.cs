using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public static string AssistantExecuteFile(string requestPath) => Respond(() => Parse(AssistantExecuteJson(ReadRequestFile(requestPath))));
        public static string AssistantExecuteJson(string json) => Respond(() =>
        {
            var request = Parse(json);
            Keys(request, "apiVersion", "sessionId", "expectedRevision", "dryRun", "operations", "workId", "requestId");
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            Require(request["sessionId"] != null, "An explicit sessionId is required.");
            var window = ResolveLiveBeginWindow(Text(request, "sessionId"));
            var document = window.AgentDocument;
            string workId = Text(request, "workId"), requestId = Text(request, "requestId"), hash = null;
            Require((workId == null) == (requestId == null), "workId and requestId must be supplied together.");
            if (workId != null)
            {
                ValidateWorkId(workId, "workId"); ValidateWorkId(requestId, "requestId"); hash = WorkRequestHash(request);
                var previous = RetryAgentRequest(document, workId, requestId, hash);
                if (previous != null)
                {
                    var replay = Success(); replay["workId"] = workId; replay["requestId"] = requestId;
                    replay["receipt"] = previous; replay["replayed"] = true; replay["applied"] = false; replay["saved"] = false;
                    replay["sessionId"] = window.AgentSessionId; replay["document"] = Snapshot(document, DocumentAssetPath(document)); return replay;
                }
            }
            LiveReady(document);
            Require(request["operations"] is JArray ops && ops.Count <= 256, "operations must contain at most 256 items.");
            var operations = (JArray)request["operations"];
            bool onlyComments = operations.All(o => o is JObject obj && ((string)obj["op"] == "comment" || (string)obj["op"] == "commentCreate"));
            Require(onlyComments || !liveJobs.Values.Any(j => j.document == document && j.state == "pending"),
                "Finish or cancel reservations/edit locks before a document batch.", "layer_locked");
            Require(!string.IsNullOrEmpty(Text(request, "expectedRevision")), "expectedRevision is required.", "revision_required");
            Require(Text(request, "expectedRevision") == Revision(document), "Document changed; inspect again.", "revision_conflict");
            using (var probe = WhimTexDocumentBuild.Copy(document))
            {
                var preview = RunCommonOperations(probe.Document, operations, false, DocumentAssetPath(document), workId, window.AgentSelection);
                AddAgentReceipt(probe.Document, workId, requestId, hash, new JObject { ["operations"] = preview });
            }
            var result = Success();
            bool dryRun = Bool(request, "dryRun");
            if (!dryRun)
            {
                RequireGraphics();
                LiveChange(document, UndoName, () =>
                {
                    result["operations"] = RunCommonOperations(document, operations, true, workId: workId, attachmentSelection: window.AgentSelection);
                    // Structural operations may finish their own Undo records. Keep the
                    // following receipt in the same outer transaction, including Redo.
                    if (workId != null) Undo.RegisterCompleteObjectUndo(document, UndoName);
                    AddAgentReceipt(document, workId, requestId, hash, new JObject { ["kind"] = "batch", ["operations"] = result["operations"].DeepClone() });
                });
                if (workId != null) SessionState.SetString(ReceiptMemoryKey(document, workId, requestId), hash);
                window.RefreshAgentLocks();
            }
            result["sessionId"] = window.AgentSessionId;
            result["applied"] = !dryRun;
            result["saved"] = false;
            result["dryRun"] = dryRun;
            result["workId"] = workId; result["requestId"] = requestId; result["replayed"] = false;
            result["document"] = Snapshot(document, DocumentAssetPath(document));
            return result;
        });

        private static JArray RunCommonOperations(WhimTexDocument document, JArray operations, bool execute, string documentPath = null, string workId = null, CanvasSelection attachmentSelection = null)
        {
            var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
            var results = new JArray();
            for (int i = 0; i < operations.Count; i++)
            {
                var operation = Obj(operations[i], "operation");
                if (execute && i > 0) Undo.RegisterCompleteObjectUndo(document, UndoName);
                var layer = ApplyOperation(document, operation, aliases, execute, workId, attachmentSelection);
                results.Add(OperationResult(document, operation, i, layer));
                document.RefreshTransformHierarchy();
            }
            ValidateTargets(document, documentPath ?? DocumentAssetPath(document));
            ValidateAgentBudget(document);
            return results;
        }

        private static void ValidateAgentBudget(WhimTexDocument document)
        {
            long pixels = 0;
            int count = 0;
            foreach (var layer in Enumerate(document.layers))
            {
                count++;
                if (layer.Behaviour is DrawingLayerBehaviour drawing)
                    pixels += drawing.StoredTexture != null ? (long)drawing.StoredTexture.width * drawing.StoredTexture.height : (long)document.width * document.height;
            }
            Require((long)document.width * document.height <= MaxCanvasPixels && count <= 1024 && pixels <= 67108864,
                "Document exceeds agent canvas/layer/Drawing limits.", "resource_limit");
        }
    }
}
