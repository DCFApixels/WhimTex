using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
        private static string HashAgentToken(JToken token)
        {
            JToken Canonical(JToken value) => value is JObject obj
                ? new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))))
                : value is JArray array ? new JArray(array.Select(Canonical)) : value.DeepClone();
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(Canonical(token).ToString(Formatting.None))));
        }

        private static string AgentMessageHash(WhimTexCommentMessage message)
        {
            var token = JObject.Parse(JsonUtility.ToJson(message)); token.Remove("agentContentHash");
            token.Remove("authorId"); token.Remove("authorColor");
            return HashAgentToken(token);
        }

        private static string FeedbackRevision(WhimTexComment comment)
        {
            var token = JObject.Parse(JsonUtility.ToJson(comment));
            token.Remove("color"); token.Remove("useAuthorColor"); token.Remove("colorAuthorId");
            // Unity's thread-wide table includes masks from replies excluded below.
            // Retained messages are serialized with their own complete reference tables.
            token.Remove("references");
            token["messages"] = new JArray(comment.messages
                .Where((m, i) => i == 0 || string.IsNullOrEmpty(m.agentContentHash) || m.agentContentHash != AgentMessageHash(m))
                .Select(m =>
                {
                    var message = JObject.Parse(JsonUtility.ToJson(m));
                    message.Remove("authorId"); message.Remove("authorColor");
                    return message;
                }));
            return HashAgentToken(token);
        }

        private static string WorkRequestHash(JObject request)
        {
            var value = (JObject)request.DeepClone();
            value.Remove("sessionId"); value.Remove("expectedRevision"); value.Remove("dryRun");
            return HashAgentToken(value);
        }

        private static string ReceiptMemoryKey(WhimTexDocument document, string workId, string requestId) =>
            "WhimTex.AgentReceipt." + UnityObjectID.FromObject(document) + "." + HashAgentToken(new JArray(workId, requestId));

        private static WhimTexAgentReceipt FindAgentReceipt(WhimTexDocument document, string workId, string requestId) =>
            document.agentReceipts?.FirstOrDefault(r => r.workId == workId && r.requestId == requestId);

        private static void ValidateWorkId(string value, string name)
        { Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 128, name + " must contain 1..128 characters."); }

        private static JObject RetryAgentRequest(WhimTexDocument document, string workId, string requestId, string hash)
        {
            var receipt = FindAgentReceipt(document, workId, requestId);
            if (receipt != null)
            {
                Require(receipt.requestHash == hash, "This work/request ID already belongs to different operations.", "request_conflict");
                return JObject.Parse(receipt.resultJson);
            }
            Require(string.IsNullOrEmpty(SessionState.GetString(ReceiptMemoryKey(document, workId, requestId), "")),
                "This request was applied, then undone or removed. Inspect the document; use a new request ID for an explicitly intended new edit.", "request_undone");
            return null;
        }

        private static void AddAgentReceipt(WhimTexDocument document, string workId, string requestId, string hash, JObject result)
        {
            if (workId == null) return;
            document.agentReceipts ??= new List<WhimTexAgentReceipt>();
            document.agentReceipts.Add(new WhimTexAgentReceipt { workId = workId, requestId = requestId,
                requestHash = hash, resultJson = result.ToString(Formatting.None), createdUtcTicks = DateTime.UtcNow.Ticks });
            document.ValidateAgentReceipts();
        }

        private static JArray AgentWorkSnapshot(WhimTexDocument document, string workId)
        {
            ValidateWorkId(workId, "workId"); document.ValidateAgentReceipts();
            return new JArray((document.agentReceipts ?? new List<WhimTexAgentReceipt>()).Where(r => r.workId == workId).Select(r =>
                new JObject { ["requestId"] = r.requestId, ["createdUtc"] = new DateTime(r.createdUtcTicks, DateTimeKind.Utc).ToString("O"),
                    ["result"] = JObject.Parse(r.resultJson) }));
        }

        private static void RecordLiveWork(LiveJob job, JObject request)
        {
            if (job.workId == null) return;
            AddAgentReceipt(job.document, job.workId, job.requestId, WorkRequestHash(JObject.Parse(job.request)), new JObject
            { ["jobId"] = job.id, ["layerId"] = job.targetId ?? job.layerId, ["state"] = "completed", ["kind"] = "live",
                ["completionHash"] = WorkRequestHash(request), ["commentChecks"] = job.commentChecks?.DeepClone() });
        }

        private static JObject RetryLiveWork(WhimTexDocument document, JObject request)
        {
            string workId = Text(request, "workId"); if (workId == null) return null;
            ValidateWorkId(workId, "workId");
            var prior = RetryAgentRequest(document, workId, Text(request, "requestId"), WorkRequestHash(request));
            if (prior == null) return null;
            prior["success"] = true; prior["apiVersion"] = ProtocolVersion; prior["replayed"] = true; prior["applied"] = false;
            prior["workId"] = workId; prior["requestId"] = Text(request, "requestId"); return prior;
        }

        private static List<WhimTexCommentAttachment> ParseCommentAttachments(WhimTexDocument document, JToken token, Dictionary<string, Layer> aliases, CanvasSelection selection = null)
        {
            var result = new List<WhimTexCommentAttachment>();
            if (token == null) return result;
            Require(token is JArray array && array.Count <= WhimTexDocument.MaximumCommentAttachments, "attachments must contain at most 32 entries.");
            foreach (var entry in (JArray)token)
            {
                var item = Obj(entry, "attachment");
                if (item["selection"] != null || item["maskRef"] != null || item["maskImagePath"] != null)
                { result.Add(WhimTexCommentAttachment.ForMask(ParseCommentMask(document, item, selection))); continue; }
                Keys(item, "layer", "assetPath", "assetGuid", "localId");
                int sources = (item["layer"] != null ? 1 : 0) + (item["assetPath"] != null ? 1 : 0) + (item["assetGuid"] != null ? 1 : 0);
                Require(sources == 1, "An attachment requires exactly one of layer, assetPath or assetGuid.");
                if (item["layer"] != null)
                {
                    Require(item["localId"] == null, "localId is only for asset attachments.");
                    result.Add(WhimTexCommentAttachment.ForLayer(Resolve(document, Text(item, "layer"), aliases)));
                }
                else
                {
                    string path = item["assetPath"] != null ? Text(item, "assetPath") : AssetDatabase.GUIDToAssetPath(Text(item, "assetGuid"));
                    Require(!string.IsNullOrEmpty(path) && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)),
                        "Asset attachments require an existing project asset.");
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    var attachment = WhimTexCommentAttachment.ForAsset(asset);
                    Require(attachment != null, "Attachment asset was not found.", "asset_not_found");
                    if (item["localId"] != null)
                    {
                        Require((item["localId"].Type == JTokenType.Integer || item["localId"].Type == JTokenType.String) &&
                            long.TryParse((string)item["localId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _), "localId must be a 64-bit integer or decimal string.");
                        attachment.localId = long.Parse((string)item["localId"], CultureInfo.InvariantCulture);
                        asset = attachment.ResolveAsset();
                        Require(asset != null, "Attachment sub-asset was not found.", "asset_not_found"); attachment.name = asset.name;
                    }
                    result.Add(attachment);
                }
            }
            return result;
        }

        private static JArray AttachmentSnapshots(WhimTexDocument document, WhimTexCommentMessage message) => new JArray(
            (message.attachments ?? new List<WhimTexCommentAttachment>()).Select(a =>
            {
                if (a.mask != null) return CommentMaskSnapshot(document, a.mask);
                var layer = a.IsLayer ? document.FindLayer(a.layerId) : null;
                var asset = a.assetGuid == null ? null : a.ResolveAsset();
                return new JObject { ["kind"] = a.IsLayer ? "layer" : "asset", ["layerId"] = a.layerId,
                    ["assetGuid"] = a.assetGuid, ["localId"] = a.localId.ToString(CultureInfo.InvariantCulture),
                    ["assetPath"] = a.assetGuid == null ? null : AssetDatabase.GUIDToAssetPath(a.assetGuid),
                    ["name"] = layer?.layerName ?? (asset != null ? asset.name : a.name), ["missing"] = layer == null && asset == null };
            }));
    }
}
