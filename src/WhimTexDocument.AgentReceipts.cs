using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class WhimTexAgentReceipt
    {
        public string workId, requestId, requestHash, resultJson;
        public long createdUtcTicks;
    }

    public sealed partial class WhimTexDocument
    {
        public List<WhimTexAgentReceipt> agentReceipts = new List<WhimTexAgentReceipt>();

        internal void ValidateAgentReceipts()
        {
            if (agentReceipts == null) return;
            if (agentReceipts.Count > 2048) throw CommentError("Agent receipt limit reached (2048).");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var item in agentReceipts)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.workId) || item.workId.Length > 128 ||
                    string.IsNullOrWhiteSpace(item.requestId) || item.requestId.Length > 128 ||
                    string.IsNullOrEmpty(item.requestHash) || item.requestHash.Length > 128 ||
                    string.IsNullOrEmpty(item.resultJson) || item.resultJson.Length > 262144 ||
                    item.createdUtcTicks < 0 || item.createdUtcTicks > DateTime.MaxValue.Ticks ||
                    !keys.Add(item.workId.Length + ":" + item.workId + item.requestId))
                    throw CommentError("Invalid or duplicate agent receipt.");
                bytes += CommentEncoding.GetByteCount(item.resultJson) + CommentEncoding.GetByteCount(item.workId) +
                    CommentEncoding.GetByteCount(item.requestId) + 128;
                if (bytes > 4 * 1024 * 1024) throw CommentError("Agent receipts exceed 4 MiB.");
                try { JObject.Parse(item.resultJson); }
                catch (JsonException) { throw CommentError("Invalid agent receipt result JSON."); }
            }
        }
    }
}
