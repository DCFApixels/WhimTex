using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // A common envelope coordinates different Editors without sharing their pixel histories.
    internal sealed class PaintUndoDisk
    {
        internal const long GiB = 1024L * 1024 * 1024;
        internal const long FreeReserve = 512L * 1024 * 1024;
        internal const long CompactionReserve = 32L * 1024 * 1024;
        private const string Signature = "WhimTex.PaintUndo.1";
        [Serializable] internal sealed class Limits { public int version = 1; public int diskGiB = 8, steps = 50; }
        [Serializable] private sealed class Owner
        {
            public string format = Signature, machine;
            public int process;
            public long started, charge;
        }
        internal readonly string Home, Session, Root;
        private readonly Owner owner;
        internal string SettingsPath => Path.Combine(Home, "limits.json");
        private static PaintUndoDisk current;
        internal static PaintUndoDisk Current => current ??= OpenCurrent();
        internal static string DefaultHome
        {
            get
            {
                string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string directory;
                if (Application.platform == RuntimePlatform.OSXEditor) directory = Path.Combine(user, "Library", "Caches");
                else if (Application.platform == RuntimePlatform.LinuxEditor)
                {
                    directory = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                    if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory)) directory = Path.Combine(user, ".cache");
                }
                else directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(directory)) throw new IOException("The user cache directory is unavailable.");
                return Path.Combine(directory, "DCFApixels", "WhimTex", "PaintUndo");
            }
        }
        private static PaintUndoDisk OpenCurrent()
        {
            const string key = "WhimTex.PaintUndo.SharedSession";
            string id = SessionState.GetString(key, "");
            if (!Guid.TryParseExact(id, "N", out _)) { id = Guid.NewGuid().ToString("N"); SessionState.SetString(key, id); }
            return new PaintUndoDisk(DefaultHome, id);
        }
        internal PaintUndoDisk(string home, string session)
        {
            if (!Guid.TryParseExact(session, "N", out _)) throw new ArgumentException("Invalid journal session.");
            Home = Path.GetFullPath(home); Session = session; Root = Path.Combine(Home, Session);
            using var process = Process.GetCurrentProcess();
            owner = new Owner { machine = Environment.MachineName, process = process.Id, started = process.StartTime.ToUniversalTime().Ticks };
            Directory.CreateDirectory(Home); CheckPath(Home);
            using var gate = Lock();
            if (Directory.Exists(Root))
            {
                CheckPath(Root);
                var previous = ReadOwner(Root);
                if (previous == null || previous.process != owner.process || previous.started != owner.started || previous.machine != owner.machine)
                    throw new IOException("Paint history session belongs to another Editor.");
            }
            else Directory.CreateDirectory(Root);
            Recount();
            CleanupAbandonedLocked();
        }
        internal FileStream Lock()
        {
            CheckPath(Home);
            string path = Path.Combine(Home, "coordination.lock"); CheckPath(path);
            var watch = Stopwatch.StartNew();
            while (true)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (watch.ElapsedMilliseconds < 1000) { Thread.Sleep(10); }
            }
        }
        internal Limits ReadLimits()
        {
            CheckPath(SettingsPath);
            if (!File.Exists(SettingsPath)) return new Limits();
            var value = JsonUtility.FromJson<Limits>(File.ReadAllText(SettingsPath));
            if (value == null || value.version != 1 || value.diskGiB < 1 || value.diskGiB > 1024 || value.steps < 1 || value.steps > 10000)
                throw new InvalidDataException("Unsupported or invalid WhimTex Undo limits.");
            return value;
        }
        internal void SetLimits(int? diskGiB, int? steps)
        {
            using var gate = Lock();
            var value = ReadLimits();
            if (diskGiB.HasValue) value.diskGiB = Math.Clamp(diskGiB.Value, 1, 1024);
            if (steps.HasValue) value.steps = Math.Clamp(steps.Value, 1, 10000);
            AtomicText(SettingsPath, JsonUtility.ToJson(value));
        }
        internal long Usage()
        {
            long bytes = 0;
            foreach (string path in Directory.GetDirectories(Home))
            {
                if (!Guid.TryParseExact(Path.GetFileName(path), "N", out _)) continue;
                CheckPath(path);
                var entry = ReadOwner(path);
                bytes = checked(bytes + (entry == null ? Measure(path) : Math.Max(0, entry.charge)));
            }
            return bytes;
        }
        internal bool Reserve(string directory, long bytes, out string reason, bool checkpoint = false)
        {
            using var gate = Lock();
            RequireOwned(directory);
            long used = Usage(), limit = ReadLimits().diskGiB * GiB - 65536 - (checkpoint ? 0 : CompactionReserve);
            if (checkpoint)
            {
                // Lowering a quota must still allow existing data to be compacted. Across
                // all sessions, only one bounded page budget may be used for that work.
                long scratch = 0;
                foreach (string session in Directory.GetDirectories(Home))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _)) continue;
                    CheckPath(session);
                    foreach (string temporary in Directory.GetDirectories(session, "checkpoint-*")) scratch = checked(scratch + Measure(temporary));
                }
                if (bytes > CompactionReserve - scratch) { reason = "Another Editor is compacting paint history. Try again shortly."; return false; }
                limit = Math.Max(limit, checked(used + bytes));
            }
            if (bytes < 0 || used > limit - bytes) { reason = "The shared paint Undo disk limit is reached."; return false; }
            var drive = new DriveInfo(Path.GetPathRoot(Home));
            if (drive.AvailableFreeSpace < bytes + FreeReserve) { reason = "Paint Undo needs more free disk space (512 MiB reserve)."; return false; }
            Directory.CreateDirectory(directory);
            AtomicText(Path.Combine(directory, "reservation"), bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AdjustCharge(bytes); reason = null; return true;
        }
        internal void Complete(string directory)
        {
            using var gate = Lock(); RequireOwned(directory);
            string path = Path.Combine(directory, "reservation"); CheckPath(path);
            if (!File.Exists(path)) return;
            long previous = Measure(directory);
            File.Delete(path);
            AdjustCharge(Measure(directory) - previous);
        }
        internal void DeleteOwned(string directory)
        {
            using var gate = Lock(); RequireOwned(directory);
            long bytes = Measure(directory);
            DeleteTree(directory); AdjustCharge(-bytes);
        }
        internal void DeleteOwnedFile(string path)
        {
            using var gate = Lock(); RequireOwned(path);
            if (!File.Exists(path)) return;
            long bytes = new FileInfo(path).Length;
            File.Delete(path); AdjustCharge(-bytes);
        }
        internal void PublishCheckpointFile(string source, string target)
        {
            using var gate = Lock(); RequireOwned(source); RequireOwned(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            long delta = new FileInfo(source).Length - (File.Exists(target) ? new FileInfo(target).Length : 0);
            if (File.Exists(target)) File.Replace(source, target, null); else File.Move(source, target);
            // Source is covered by its temporary directory reservation until publication finishes.
            AdjustCharge(delta);
        }
        internal void PublishCheckpointPage(string temporary, string destination, string[] files)
        {
            RequireOwned(temporary); RequireOwned(destination);
            Directory.CreateDirectory(destination);
            long delta = 0;
            try
            {
                // The reservation covers both the files being prepared and their published
                // replacements. Avoid holding the shared quota lock across compression or I/O.
                foreach (string source in files)
                {
                    string target = Path.Combine(destination, Path.GetFileName(source));
                    CheckPath(source); CheckPath(target);
                    long change = new FileInfo(source).Length - (File.Exists(target) ? new FileInfo(target).Length : 0);
                    if (File.Exists(target)) File.Replace(source, target, null); else File.Move(source, target);
                    delta += change;
                }
            }
            finally
            {
                using var gate = Lock();
                long reserved = Measure(temporary);
                DeleteTree(temporary); AdjustCharge(delta - reserved);
            }
        }
        private void AdjustCharge(long delta)
        {
            // Reload the envelope: another instance can reopen this session after script reload.
            owner.charge = Math.Max(0, checked((ReadOwner(Root)?.charge ?? owner.charge) + delta));
            AtomicText(Path.Combine(Root, "session.json"), JsonUtility.ToJson(owner));
        }
        internal int CleanupAbandoned()
        { using var gate = Lock(); return CleanupAbandonedLocked(); }
        private int CleanupAbandonedLocked()
        {
            int count = 0;
            foreach (string path in Directory.GetDirectories(Home))
            {
                if (path == Root || !Guid.TryParseExact(Path.GetFileName(path), "N", out _)) continue;
                CheckPath(path);
                var entry = ReadOwner(path);
                if (entry == null || entry.machine != owner.machine || IsAlive(entry)) continue;
                DeleteTree(path); count++;
            }
            return count;
        }
        private static bool IsAlive(Owner value)
        {
            try
            {
                using var process = Process.GetProcessById(value.process);
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == value.started;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch { return true; } // Access failures do not prove that an owner is gone.
        }
        internal void Close()
        { using var gate = Lock(); DeleteTree(Root); }
        private void Recount()
        {
            owner.charge = Measure(Root);
            AtomicText(Path.Combine(Root, "session.json"), JsonUtility.ToJson(owner));
        }
        private static Owner ReadOwner(string path)
        {
            try
            {
                string file = Path.Combine(path, "session.json"); CheckPath(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var value = JsonUtility.FromJson<Owner>(reader.ReadToEnd());
                return value?.format == Signature && value.process > 0 && value.started > 0 ? value : null;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }
        internal static long Measure(string directory)
        {
            if (!Directory.Exists(directory)) return 0;
            CheckPath(directory);
            string reservation = Path.Combine(directory, "reservation"); CheckPath(reservation);
            if (File.Exists(reservation))
            {
                if (!long.TryParse(File.ReadAllText(reservation), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long reserved) || reserved < 0)
                    throw new InvalidDataException("Invalid paint history reservation.");
                return reserved;
            }
            long bytes = 0;
            foreach (var file in Directory.GetFiles(directory)) { CheckPath(file); bytes = checked(bytes + new FileInfo(file).Length); }
            foreach (var child in Directory.GetDirectories(directory)) bytes = checked(bytes + Measure(child));
            return bytes;
        }
        private void RequireOwned(string path)
        {
            path = Path.GetFullPath(path);
            if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("The journal path is outside the owned session.");
            CheckPath(path);
        }
        internal static void CheckPath(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Paint history does not follow symbolic links: " + current);
        }
        private static void DeleteTree(string directory)
        {
            if (!Directory.Exists(directory)) return;
            CheckPath(directory);
            // Validate every descendant before the first deletion.
            ValidateTree(directory);
            Directory.Delete(directory, true);
        }
        private static void ValidateTree(string directory)
        {
            CheckPath(directory);
            foreach (var file in Directory.GetFiles(directory)) CheckPath(file);
            foreach (var child in Directory.GetDirectories(directory)) ValidateTree(child);
        }
        internal static void AtomicText(string path, string value)
        {
            CheckPath(path); CheckPath(path + ".tmp");
            File.WriteAllText(path + ".tmp", value);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
    }
}
