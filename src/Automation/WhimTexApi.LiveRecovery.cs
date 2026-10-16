using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private const string LiveRecoveryKey = "WhimTex.LiveRecovery.v1";
        private static bool liveReloading;
        private static bool liveRecoveryReady;
        private static readonly Dictionary<string, JObject> recoverableLiveJobs = new Dictionary<string, JObject>();

        private static void InitializeLiveRecovery()
        {
            string saved = SessionState.GetString(LiveRecoveryKey, "");
            try
            {
                if (saved.Length > 0) foreach (JObject item in JArray.Parse(saved)) recoverableLiveJobs.Add((string)item["id"], item);
            }
            catch { recoverableLiveJobs.Clear(); SessionState.EraseString(LiveRecoveryKey); }
            AssemblyReloadEvents.beforeAssemblyReload += SaveLiveRecovery;
            EditorApplication.delayCall += () => { liveRecoveryReady = true; PruneLiveRecovery(); };
            EditorApplication.quitting += () => { recoverableLiveJobs.Clear(); SessionState.EraseString(LiveRecoveryKey); };
            WhimTexDocument.Changed += _ => PruneLiveRecovery();
            Undo.undoRedoPerformed += () =>
            {
                foreach (var id in recoverableLiveJobs.Where(p => (bool)p.Value["editing"]).Select(p => p.Key).ToArray()) ForgetRecoverableJob(id);
                PruneLiveRecovery();
            };
        }

        private static void StoreLiveRecovery() => SessionState.SetString(LiveRecoveryKey, new JArray(recoverableLiveJobs.Values).ToString(Formatting.None));
        private static void ForgetRecoverableJob(string id)
        { if (id != null && recoverableLiveJobs.Remove(id)) StoreLiveRecovery(); }
        private static void ForgetRecoverableDocument(WhimTexDocument document)
        {
            if (document == null) return;
            string key = UnityObjectID.FromObject(document).ToString();
            foreach (var id in recoverableLiveJobs.Where(p => (string)p.Value["documentId"] == key).Select(p => p.Key).ToArray()) ForgetRecoverableJob(id);
        }

        private static void PruneLiveRecovery()
        {
            if (liveReloading || !liveRecoveryReady || recoverableLiveJobs.Count == 0) return;
            var windows = Resources.FindObjectsOfTypeAll<WhimTexWindow>();
            foreach (var item in recoverableLiveJobs.Values.ToArray())
            {
                var document = windows.Select(w => w.AgentDocument).FirstOrDefault(d => d != null &&
                    UnityObjectID.FromObject(d).ToString() == (string)item["documentId"]);
                if (document == null || !RecoveryReservationExists(document, item)) ForgetRecoverableJob((string)item["id"]);
            }
        }

        private static bool RecoveryReservationExists(WhimTexDocument document, JObject item)
        {
            var layer = document.FindLayer((string)item["layerId"]);
            return (bool)item["editing"] ? layer != null : layer?.Behaviour is PendingLayerBehaviour pending && pending.jobId == (string)item["id"];
        }

        private static string LiveRecoveryRevision(WhimTexDocument document)
        {
            var layers = new JArray();
            void Walk(IEnumerable<Layer> source, string parent)
            {
                foreach (var layer in source)
                {
                    if (layer.Behaviour is PendingLayerBehaviour) continue;
                    layers.Add(new JObject { ["id"] = layer.Id, ["parent"] = parent,
                        ["enabled"] = layer.enabled, ["content"] = LiveLayerRevision(layer) });
                    if (layer.IsGroup) Walk(layer.layers, layer.Id);
                }
            }
            Walk(document.layers, null);
            return HashAgentToken(new JObject { ["width"] = document.width, ["height"] = document.height, ["layers"] = layers });
        }

        private static JObject SaveRecoveryMask(byte[] bytes, string jobId, string name)
        {
            if (bytes == null) return null;
            string path = FullPath("Temp/WhimTex/Agent/" + jobId + "/recovery-" + Guid.NewGuid().ToString("N") + "-" + name + ".bin");
            RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
            using var hash = SHA256.Create();
            return new JObject { ["path"] = path, ["hash"] = Convert.ToBase64String(hash.ComputeHash(bytes)) };
        }

        private static byte[] ReadRecoveryMask(JToken token, int length)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            string path = (string)token["path"]; RejectLinks(path);
            Require(File.Exists(path) && new FileInfo(path).Length == length, "Recovery mask is unavailable or changed.", "recovery_unavailable");
            byte[] bytes = File.ReadAllBytes(path);
            using var hash = SHA256.Create();
            Require(Convert.ToBase64String(hash.ComputeHash(bytes)) == (string)token["hash"], "Recovery mask changed.", "recovery_unavailable");
            return bytes;
        }

        private static void SaveLiveRecovery()
        {
            RefreshLiveJobs();
            foreach (var job in liveJobs.Values)
            {
                if (job.state != "pending") { ForgetRecoverableJob(job.id); continue; }
                try
                {
                    recoverableLiveJobs[job.id] = new JObject
                    {
                        ["id"] = job.id, ["requestId"] = job.requestId, ["request"] = job.request,
                        ["documentId"] = UnityObjectID.FromObject(job.document).ToString(),
                        ["layerId"] = job.layerId, ["targetId"] = job.targetId, ["targetRevision"] = job.targetRevision,
                        ["recoveryRevision"] = job.recoveryRevision, ["editing"] = job.editing,
                        ["width"] = job.width, ["height"] = job.height, ["region"] = LiveRect(job.region),
                        ["selectionMode"] = job.selectionMode, ["mask"] = SaveRecoveryMask(job.mask, job.id, "guide"),
                        ["applicationSharesGuide"] = job.mask != null && ReferenceEquals(job.mask, job.applicationMask),
                        ["applicationMask"] = ReferenceEquals(job.mask, job.applicationMask) ? null : SaveRecoveryMask(job.applicationMask, job.id, "application"),
                        ["capture"] = job.capture?.DeepClone(), ["context"] = job.context?.DeepClone(),
                        ["commentChecks"] = job.commentChecks?.DeepClone(), ["commentMaskRef"] = job.commentMaskRef?.DeepClone(),
                        ["applicationMaskRef"] = job.applicationMaskRef?.DeepClone(), ["workId"] = job.workId
                    };
                }
                catch (Exception error) { ForgetRecoverableJob(job.id); Debug.LogWarning("WhimTex could not retain an agent operation for reload: " + error.Message); }
            }
            StoreLiveRecovery(); liveReloading = true;
        }

        private static JObject InterruptedLiveStatus(JObject item) => new JObject
        {
            ["apiVersion"] = ProtocolVersion, ["success"] = true, ["jobId"] = item["id"],
            ["state"] = "interrupted", ["recoverable"] = true, ["layerId"] = item["layerId"],
            ["targetLayerId"] = item["targetId"], ["workId"] = item["workId"],
            ["documentId"] = item["documentId"],
            ["message"] = "Discover the original open document and call resume with jobId and its current sessionId. Source checks run before resuming.",
            ["applied"] = false, ["saved"] = false
        };

        private static JObject ResumeLiveJob(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "jobId");
            string id = Text(request, "jobId");
            var window = LiveWindow(Text(request, "sessionId")); var document = window.AgentDocument;
            LiveReady(document);
            if (id != null && liveJobs.TryGetValue(id, out var existing))
            {
                Require(existing.document == document && existing.session == window.AgentSessionId, "Operation belongs to another document.", "session_mismatch");
                RefreshLiveJob(existing); return LiveStatus(existing);
            }
            Require(id != null && recoverableLiveJobs.ContainsKey(id), "No recoverable operation. It may have been cancelled, removed or closed.", "recovery_unavailable");
            var item = recoverableLiveJobs[id];
            Require((string)item["documentId"] == UnityObjectID.FromObject(document).ToString(), "Operation belongs to a different document instance.", "session_mismatch");
            Require(RecoveryReservationExists(document, item), "The original reservation or target was removed.", "job_closed");
            Require((int)item["width"] == document.width && (int)item["height"] == document.height &&
                (string)item["recoveryRevision"] == LiveRecoveryRevision(document), "Source content changed. Keep the candidate file and recapture for a new edit.", "revision_conflict");
            JToken Optional(string key) => item[key]?.Type == JTokenType.Null ? null : item[key];
            var checks = (JArray)Optional("commentChecks"); CheckComments(document, checks);
            if (Optional("commentMaskRef") != null) CheckCommentMask(document, item["commentMaskRef"]);
            if (Optional("applicationMaskRef") != null) CheckCommentMask(document, item["applicationMaskRef"]);
            string target = (string)item["targetId"];
            Require(target == null || !IsLayerContentLocked(document, document.FindLayer(target)) &&
                !liveJobs.Values.Any(j => j.state == "pending" && j.document == document && j.targetId == target), "Target has another active operation.", "layer_locked");
            Require(target == null || LiveLayerRevision(document.FindLayer(target)) == (string)item["targetRevision"], "Target changed.", "revision_conflict");
            var rect = (JArray)item["region"];
            var job = new LiveJob { id = id, requestId = (string)item["requestId"], request = (string)item["request"],
                session = window.AgentSessionId, document = document, layerId = (string)item["layerId"],
                targetId = target, targetRevision = (string)item["targetRevision"], recoveryRevision = (string)item["recoveryRevision"],
                editing = (bool)item["editing"], width = document.width, height = document.height,
                region = new RectInt((int)rect[0], (int)rect[1], (int)rect[2], (int)rect[3]), selectionMode = (string)item["selectionMode"],
                mask = ReadRecoveryMask(item["mask"], document.width * document.height),
                applicationMask = ReadRecoveryMask(item["applicationMask"], document.width * document.height),
                capture = (JObject)Optional("capture"), context = (JObject)Optional("context"), commentChecks = checks,
                commentMaskRef = (JObject)Optional("commentMaskRef"), applicationMaskRef = (JObject)Optional("applicationMaskRef"), workId = (string)item["workId"] };
            if ((bool)item["applicationSharesGuide"]) job.applicationMask = job.mask;
            Require(liveJobs.Count < 512 && liveJobs.Values.Count(j => j.state == "pending") < 16, "Live job limit reached.", "resource_limit");
            RequireLiveMaskBudget(job); liveJobs.Add(id, job); ForgetRecoverableJob(id); NotifyLiveLockChanged(document);
            var result = LiveStatus(job); result["resumed"] = true; return result;
        }
    }
}
