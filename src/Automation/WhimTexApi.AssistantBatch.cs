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
            Keys(request, "apiVersion", "sessionId", "expectedRevision", "dryRun", "operations");
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            Require(request["sessionId"] != null, "An explicit sessionId is required.");
            var window = ResolveLiveBeginWindow(Text(request, "sessionId"));
            var document = window.AgentDocument;
            LiveReady(document);
            Require(!liveJobs.Values.Any(j => j.document == document && j.state == "pending"),
                "Finish or cancel reservations/edit locks before a document batch.", "layer_locked");
            Require(!string.IsNullOrEmpty(Text(request, "expectedRevision")), "expectedRevision is required.", "revision_required");
            Require(Text(request, "expectedRevision") == Revision(document), "Document changed; inspect again.", "revision_conflict");
            Require(request["operations"] is JArray ops && ops.Count <= 256, "operations must contain at most 256 items.");
            var operations = (JArray)request["operations"];
            using (var probe = WhimTexDocumentBuild.Copy(document))
                RunCommonOperations(probe.Document, operations, false, DocumentAssetPath(document));
            var result = Success();
            bool dryRun = Bool(request, "dryRun");
            if (!dryRun)
            {
                RequireGraphics();
                LiveChange(document, UndoName, () => result["operations"] = RunCommonOperations(document, operations, true));
                window.RefreshAgentLocks();
            }
            result["sessionId"] = window.AgentSessionId;
            result["applied"] = !dryRun;
            result["saved"] = false;
            result["dryRun"] = dryRun;
            result["document"] = Snapshot(document, DocumentAssetPath(document));
            return result;
        });

        private static JArray RunCommonOperations(TextureCompositor document, JArray operations, bool execute, string documentPath = null)
        {
            var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
            var results = new JArray();
            for (int i = 0; i < operations.Count; i++)
            {
                var layer = ApplyOperation(document, Obj(operations[i], "operation"), aliases, execute);
                results.Add(new JObject { ["index"] = i, ["layerId"] = layer.Id, ["name"] = layer.layerName });
                document.RefreshTransformHierarchy();
            }
            ValidateTargets(document, documentPath ?? DocumentAssetPath(document));
            ValidateAgentBudget(document);
            return results;
        }

        private static void ValidateAgentBudget(TextureCompositor document)
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
