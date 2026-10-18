using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class DrawingPixelHistory
    {
        [SerializeField] private string documentIdentity;
        internal string Identity => identity;
        internal string DocumentIdentity => string.IsNullOrEmpty(documentIdentity) ? identity : documentIdentity;
        private string StateDirectory => Path.Combine(PaintHistoryStore.Root, "owners", identity);
        private string StatePath => Path.Combine(StateDirectory, "state.json");
        [Serializable] private sealed class RetentionState { public string boundary = ""; public List<string> pending = new(); }
        [NonSerialized] private RetentionState retention;
        [NonSerialized] private string retentionRoot;
        [NonSerialized] private string checkpointStep;
        [NonSerialized] private int nextCheckpointPage;
        private RetentionState ReadState()
        {
            if (retention != null && retentionRoot == PaintHistoryStore.Root) return retention;
            string path = StatePath;
            PaintUndoDisk.CheckPath(path);
            var state = File.Exists(path) ? JsonUtility.FromJson<RetentionState>(File.ReadAllText(path)) : new RetentionState();
            if (state == null || state.pending == null || (state.boundary != "" && !Guid.TryParseExact(state.boundary, "N", out _)))
                throw new InvalidDataException("Invalid paint history retention state.");
            retentionRoot = PaintHistoryStore.Root; return retention = state;
        }
        private void WriteState(RetentionState state)
        { Directory.CreateDirectory(StateDirectory); PaintUndoDisk.AtomicText(StatePath, JsonUtility.ToJson(state)); retention = state; retentionRoot = PaintHistoryStore.Root; }
        private RetentionState CopyState()
        { var state = ReadState(); return new RetentionState { boundary = state.boundary, pending = new List<string>(state.pending) }; }
        internal string Boundary => ReadState().boundary;
        internal bool IsCheckpoint(string id) => ReadState().pending.Contains(id);
        internal string Applied => applied ?? "";
        internal void BindDocument(Layer layer)
        {
            if (!string.IsNullOrEmpty(documentIdentity)) return;
            foreach (var document in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
                if (document.TryFindLayer(layer, out _, out _))
                {
                    string key = "WhimTex.PaintUndo.Document." + UnityObjectID.FromObject(document);
                    documentIdentity = SessionState.GetString(key, "");
                    if (!Guid.TryParseExact(documentIdentity, "N", out _))
                    { documentIdentity = Guid.NewGuid().ToString("N"); SessionState.SetString(key, documentIdentity); }
                    EditorUtility.SetDirty(this); return;
                }
        }
        internal bool TryPath(string revisionId, out List<string> path)
        {
            path = new List<string>(); string boundary = Boundary;
            var visited = new HashSet<string>();
            for (string id = revisionId; id != boundary;)
            {
                if (string.IsNullOrEmpty(id) || !visited.Add(id) || !PaintHistoryStore.TryGet(id, out var step) || step.owner != identity) return false;
                path.Add(id); id = step.parent;
            }
            return true;
        }
        internal bool Retire(PaintHistoryStore.Step step, bool background = false)
        {
            if (!background) CompactCheckpoints();
            if (!TryPath(Applied, out var path) || path.Count == 0 || path[path.Count - 1] != step.id) return false;
            if (!background) PaintHistoryStore.Validate(step);
            if (!step.reservationReleased)
            { PaintUndoDisk.Current.Complete(PaintHistoryStore.StepDirectory(step.id)); step.reservationReleased = true; }
            var state = CopyState(); state.boundary = step.id; state.pending.Add(step.id);
            using (new PaintHistoryStore.MaintenanceScope("retire-state")) WriteState(state);
            using (new PaintHistoryStore.MaintenanceScope("retire-native")) ForgetUndoRecord(step.id);
            if (!background) CompactCheckpoints();
            return true;
        }
        internal void CompactCheckpoints()
        {
            PaintHistoryStore.FinishCheckpointWork(this);
            var state = CopyState();
            foreach (var id in state.pending.ToArray())
            {
                var step = PaintHistoryStore.Get(id);
                WriteBaseline(step);
                state.pending.Remove(id); WriteState(state);
                PaintHistoryStore.DeleteStep(step);
            }
        }
        internal void ClearHistory()
        {
            if (NeedsRestore) throw new InvalidOperationException("Finish Undo/Redo before clearing paint history.");
            if (!TryPath(Applied, out var path)) throw new IOException("Paint history is incomplete; cleanup preserved the current history.");
            var retained = new HashSet<string>(path);
            foreach (var step in PaintHistoryStore.Snapshot())
                if (step.owner == identity && !retained.Contains(step.id)) PaintHistoryStore.DeleteStep(step);
            for (int i = path.Count - 1; i >= 0; i--) Retire(PaintHistoryStore.Get(path[i]));
            CompactCheckpoints();
            foreach (var step in PaintHistoryStore.Snapshot()) if (step.owner == identity) PaintHistoryStore.DeleteStep(step);
            Undo.ClearUndo(this);
        }
        private void WriteBaseline(PaintHistoryStore.Step step)
        {
            for (int page = 0; page < step.pages; page++)
            {
                if (!File.Exists(PaintHistoryStore.PagePath(step, page)) && step.payload[page]?.blocks == null) continue;
                string temporary = Path.Combine(PaintHistoryStore.Root, "checkpoint-" + Guid.NewGuid().ToString("N"));
                long bytes = 65536 + PaintTileTransfer.TilesPerPage * (long)PaintTileSet.Size * PaintTileSet.Size * (8 + step.bytesPerPixel);
                if (!PaintUndoDisk.Current.Reserve(temporary, bytes, out var reason, true)) throw new IOException(reason + " Half checkpoint compaction is pending.");
                try
                {
                    PaintHistoryStore.VisitPageForRetention(step, page, b =>
                    {
                    string path = Path.Combine(temporary, BlockName(b.rect));
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    using var writer = new BinaryWriter(file);
                    writer.Write(0x57544231); writer.Write(step.width); writer.Write(step.height); writer.Write(step.bytesPerPixel);
                    writer.Write(b.rect.x); writer.Write(b.rect.y); writer.Write(b.rect.width); writer.Write(b.rect.height);
                    var hash = new Hash128(); hash.Append(b.afterHalf); hash.Append(b.afterStored);
                    writer.Write(hash.ToString());
                    using var zip = new System.IO.Compression.DeflateStream(file, System.IO.Compression.CompressionLevel.Fastest, true);
                    zip.Write(b.afterHalf, 0, b.afterHalf.Length); zip.Write(b.afterStored, 0, b.afterStored.Length);
                    });
                    Directory.CreateDirectory(StateDirectory);
                    foreach (var file in Directory.GetFiles(temporary, "*.bin"))
                    {
                        string target = Path.Combine(StateDirectory, Path.GetFileName(file)); PaintUndoDisk.CheckPath(target);
                        PaintUndoDisk.Current.PublishCheckpointFile(file, target);
                    }
                    PaintUndoDisk.Current.DeleteOwnedFile(PaintHistoryStore.PagePath(step, page));
                }
                finally { PaintUndoDisk.Current.DeleteOwned(temporary); }
            }
        }
        internal bool HasPendingCheckpoint => ReadState().pending.Count != 0;
        internal bool StartCheckpointPage()
        {
            var state = CopyState();
            if (state.pending.Count == 0) return false;
            var step = PaintHistoryStore.Get(state.pending[0]);
            if (checkpointStep != step.id) { checkpointStep = step.id; nextCheckpointPage = 0; }
            for (int page = nextCheckpointPage; page < step.pages; page++)
            {
                nextCheckpointPage = page;
                if (File.Exists(PaintHistoryStore.PagePath(step, page)) || step.payload[page]?.blocks != null)
                { PaintHistoryStore.StartCheckpointWork(this, step, page, StateDirectory); return true; }
            }
            state.pending.RemoveAt(0); WriteState(state); PaintHistoryStore.DeleteStep(step);
            return true;
        }
        internal void CompleteCheckpointPage(string step, int page)
        { checkpointStep = step; nextCheckpointPage = page + 1; }
        private static string BlockName(RectInt rect) => "base-" + rect.x + "-" + rect.y + ".bin";
        private void VisitBaseline(Action<PaintHistoryStore.Block> visit)
        {
            var seen = new HashSet<Vector2Int>();
            var state = ReadState();
            for (int i = state.pending.Count - 1; i >= 0; i--)
            {
                var step = PaintHistoryStore.Get(state.pending[i]);
                if (step.width != texture.width || step.height != texture.height || step.bytesPerPixel != (HdrUtility.IsHdr(texture) ? 8 : 4)) continue;
                for (int page = 0; page < step.pages; page++)
                    if (File.Exists(PaintHistoryStore.PagePath(step, page)) || step.payload[page]?.blocks != null)
                        PaintHistoryStore.VisitPageForRetention(step, page, b =>
                        { if (seen.Add(new Vector2Int(b.rect.x, b.rect.y))) visit(b); });
            }
            if (!Directory.Exists(StateDirectory)) return;
            foreach (var path in Directory.GetFiles(StateDirectory, "base-*.bin"))
            {
                string[] coordinate = Path.GetFileNameWithoutExtension(path).Split('-');
                if (coordinate.Length == 3 && int.TryParse(coordinate[1], out int x) && int.TryParse(coordinate[2], out int y) &&
                    seen.Contains(new Vector2Int(x, y))) continue;
                PaintUndoDisk.CheckPath(path);
                using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
                if (reader.ReadInt32() != 0x57544231) throw new InvalidDataException("Invalid paint checkpoint.");
                int width = reader.ReadInt32(), height = reader.ReadInt32(), bpp = reader.ReadInt32();
                if (width != texture.width || height != texture.height || bpp != (HdrUtility.IsHdr(texture) ? 8 : 4)) continue;
                var rect = new RectInt(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                if (rect.width < 1 || rect.height < 1 || rect.width > PaintTileSet.Size || rect.height > PaintTileSet.Size ||
                    rect.x < 0 || rect.y < 0 || rect.xMax > width || rect.yMax > height) throw new InvalidDataException("Invalid checkpoint bounds.");
                string expected = reader.ReadString(); int area = rect.width * rect.height;
                using var zip = new System.IO.Compression.DeflateStream(file, System.IO.Compression.CompressionMode.Decompress, true);
                using var payload = new BinaryReader(zip);
                var block = new PaintHistoryStore.Block { rect = rect, afterHalf = payload.ReadBytes(area * 8), afterStored = payload.ReadBytes(area * bpp) };
                var hash = new Hash128(); hash.Append(block.afterHalf); hash.Append(block.afterStored);
                if (block.afterHalf.Length != area * 8 || block.afterStored.Length != area * bpp || hash.ToString() != expected || payload.Read() != -1)
                    throw new InvalidDataException("Damaged paint checkpoint.");
                if (seen.Add(new Vector2Int(rect.x, rect.y))) visit(block);
            }
        }
    }

    internal static partial class PaintHistoryStore
    {
        private static bool loaded;
        private static double nextMaintenance;
        private static double nextCleanup;
        internal static string LastMaintenanceError { get; private set; }
        internal static string StepDirectory(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid paint revision.");
            string path = Path.Combine(Root, "steps", id); PaintUndoDisk.CheckPath(path); return path;
        }
        internal static string PagePath(Step step, int index) => Path.Combine(StepDirectory(step.id), index + ".bin");
        internal static bool TryGet(string id, out Step step)
        {
            step = null;
            if (!Guid.TryParseExact(id, "N", out _)) return false;
            // This session owns deletion and removes its cached entry with the files. Walking
            // an already loaded chain need not stat every manifest and ancestor directory.
            if (steps.TryGetValue(id, out step)) return true;
            if (!File.Exists(Path.Combine(StepDirectory(id), "manifest.json"))) return false;
            step = Get(id); return true;
        }
        private static void EnsureLoaded()
        {
            if (loaded) return;
            string directory = Path.Combine(Root, "steps");
            if (Directory.Exists(directory)) foreach (var path in Directory.GetDirectories(directory))
                if (Guid.TryParseExact(Path.GetFileName(path), "N", out _)) TryGet(Path.GetFileName(path), out _);
            loaded = true;
        }
        internal static List<Step> Snapshot()
        {
            EnsureLoaded(); var result = new List<Step>();
            var owners = Resources.FindObjectsOfTypeAll<DrawingPixelHistory>();
            foreach (var step in steps.Values)
            {
                bool checkpoint = false;
                foreach (var owner in owners) if (owner.Identity == step.owner && owner.IsCheckpoint(step.id)) { checkpoint = true; break; }
                if (!checkpoint) result.Add(step);
            }
            return result;
        }
        internal static void VisitPageForRetention(Step step, int page, Action<Block> visitor, Block scratch = null) => VisitPage(step, page, visitor, scratch);
        internal static void DeleteStep(Step step)
        {
            foreach (var history in Resources.FindObjectsOfTypeAll<DrawingPixelHistory>())
                if (history.Identity == step.owner) history.ForgetUndoRecord(step.id);
            foreach (var page in step.payload)
            {
                WaitForWrite(page);
                if (page?.blocks == null) continue;
                foreach (var block in page.blocks)
                { ReturnBuffer(block.beforeHalf); ReturnBuffer(block.afterHalf); ReturnBuffer(block.beforeStored); ReturnBuffer(block.afterStored); }
                Interlocked.Add(ref pendingBytes, -page.bytes); page.blocks = null;
            }
            PaintUndoDisk.Current.DeleteOwned(StepDirectory(step.id)); steps.Remove(step.id);
        }
        private static void DiscardBranches(DrawingPixelHistory history)
        {
            if (!history.TryPath(history.Revision, out var path)) throw new IOException("Paint history cursor is unavailable.");
            var keep = new HashSet<string>(path);
            var removed = Snapshot().FindAll(s => s.owner == history.Identity && !keep.Contains(s.id));
            if (removed.Count == 0) return;
            PaintTileTransfer.Drain(); FlushWrites();
            foreach (var step in removed) DeleteStep(step);
        }
        private static bool RetireOldest(string document = null, bool background = false)
        {
            var owners = Resources.FindObjectsOfTypeAll<DrawingPixelHistory>();
            var candidates = Snapshot(); candidates.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var step in candidates)
            {
                if (document != null && step.document != document) continue;
                foreach (var history in owners)
                {
                    if (history.Identity != step.owner || history.texture == null || history.NeedsRestore) continue;
                    if (history.Retire(step, background)) return true;
                }
            }
            // If only future states remain, discard the farthest Redo branch first.
            candidates.Reverse();
            foreach (var step in candidates)
            {
                if (document != null && step.document != document) continue;
                foreach (var history in owners)
                    if (history.Identity == step.owner && history.TryPath(history.Applied, out var path) && !path.Contains(step.id))
                    { DeleteStep(step); return true; }
            }
            return false;
        }
        internal static bool HasActiveStroke()
        {
            foreach (var doc in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
            {
                bool found = false;
                void Visit(List<Layer> layers)
                {
                    foreach (var layer in layers)
                    {
                        if (layer.Behaviour is DrawingLayerBehaviour drawing && drawing.HasBufferedStroke) found = true;
                        if (layer.AsGroup() is Layer group) Visit(group.layers);
                    }
                }
                Visit(doc.layers); if (found) return true;
            }
            return false;
        }
        internal static void Maintenance(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup < nextMaintenance) return;
            nextMaintenance = EditorApplication.timeSinceStartup + 2;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            MaintenanceSlowestStage = null; MaintenanceSlowestStageMs = 0;
            int collections = GC.CollectionCount(0);
            try
            {
                using (new MaintenanceScope("active-strokes"))
                {
                    if (HasActiveStroke() || PaintTileTransfer.PendingCount != 0) return;
                    foreach (var page in writing) if (!page.write.IsCompleted) return;
                }
                using (new MaintenanceScope("flush")) { FlushWrites(); EnsureLoaded(); }
                if (checkpointWork == null && EditorApplication.timeSinceStartup >= nextCleanup)
                {
                    using (new MaintenanceScope("cleanup"))
                    {
                        RemoveUnusedHistories();
                        PaintUndoDisk.Current.CleanupAbandoned(); nextCleanup = EditorApplication.timeSinceStartup + 60;
                    }
                }
                using (new MaintenanceScope("publish")) PublishCheckpointWork();
                if (checkpointWork != null) nextMaintenance = 0;
                if (budget.Elapsed.TotalMilliseconds >= 4) return;
                List<Step> snapshot;
                using (new MaintenanceScope("snapshot")) snapshot = Snapshot();
                foreach (var step in snapshot)
                {
                    if (step.reservationReleased) continue;
                    bool complete = true;
                    foreach (var page in step.payload) if (page != null && (page.write == null || !page.write.IsCompletedSuccessfully)) complete = false;
                    if (complete) { using (new MaintenanceScope("complete-reservation")) PaintUndoDisk.Current.Complete(StepDirectory(step.id)); step.reservationReleased = true; }
                    if (budget.Elapsed.TotalMilliseconds >= 4) { nextMaintenance = 0; return; }
                }
                int limit;
                using (new MaintenanceScope("read-limits")) limit = PaintUndoDisk.Current.ReadLimits().steps;
                using (new MaintenanceScope("count-retire"))
                {
                    var documents = new HashSet<string>(); foreach (var step in steps.Values) documents.Add(step.document);
                    foreach (string document in documents)
                        if (snapshot.FindAll(s => s.document == document).Count > limit && RetireOldest(document, true))
                        { nextMaintenance = 0; break; }
                }
                if (budget.Elapsed.TotalMilliseconds >= 4) { nextMaintenance = 0; return; }
                using (new MaintenanceScope("start-page")) if (checkpointWork == null)
                    foreach (var owner in Resources.FindObjectsOfTypeAll<DrawingPixelHistory>())
                        if (owner.texture != null && owner.HasPendingCheckpoint)
                        { owner.StartCheckpointPage(); nextMaintenance = 0; break; }
                if (budget.Elapsed.TotalMilliseconds >= 4) { nextMaintenance = 0; return; }
                // A worker is changing the charged envelope. Do not repeatedly scan quota while
                // compaction is in flight; reservations still enforce it before every new stroke.
                if (checkpointWork != null || nextMaintenance == 0) return;
                using (new MaintenanceScope("quota"))
                {
                    bool overQuota = PaintUndoDisk.Current.Usage() > PaintUndoDisk.Current.ReadLimits().diskGiB * PaintUndoDisk.GiB;
                    if (overQuota && RetireOldest(null, true)) nextMaintenance = 0;
                    LastMaintenanceError = overQuota
                        ? "The current exact images and other active Editors exceed this limit. New strokes need more Undo disk space." : null;
                }
            }
            catch (Exception error)
            {
                if (LastMaintenanceError != error.Message) Debug.LogWarning("WhimTex paint history: " + error.Message);
                LastMaintenanceError = error.Message;
            }
            finally
            {
                if (GC.CollectionCount(0) != collections) MaintenanceSlowestStage += " (GC)";
            }
        }
        internal static void ClearCurrentEditor()
        {
            if (HasActiveStroke()) throw new InvalidOperationException("Finish the active stroke before clearing history.");
            PaintTileTransfer.Drain(); FlushWrites(); EnsureLoaded();
            foreach (var owner in Resources.FindObjectsOfTypeAll<DrawingPixelHistory>())
                if (owner.texture != null) owner.ClearHistory();
            PaintUndoDisk.Current.CleanupAbandoned();
            Maintenance(true);
        }
        private static void RemoveUnusedHistories()
        {
            var live = new HashSet<string>();
            foreach (var owner in Resources.FindObjectsOfTypeAll<DrawingPixelHistory>())
                if (owner.texture != null) live.Add(owner.Identity);
            foreach (var step in new List<Step>(steps.Values)) if (!live.Contains(step.owner)) DeleteStep(step);
            string stepsDirectory = Path.Combine(Root, "steps");
            if (Directory.Exists(stepsDirectory)) foreach (var path in Directory.GetDirectories(stepsDirectory))
                if (Guid.TryParseExact(Path.GetFileName(path), "N", out _) && !File.Exists(Path.Combine(path, "manifest.json")))
                    PaintUndoDisk.Current.DeleteOwned(path);
            string directory = Path.Combine(Root, "owners");
            if (Directory.Exists(directory)) foreach (var path in Directory.GetDirectories(directory))
                if (Guid.TryParseExact(Path.GetFileName(path), "N", out _) && !live.Contains(Path.GetFileName(path))) PaintUndoDisk.Current.DeleteOwned(path);
        }
    }
}
