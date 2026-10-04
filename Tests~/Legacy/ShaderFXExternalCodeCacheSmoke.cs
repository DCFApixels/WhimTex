using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;

// Standalone Pipeline run_script test. Only owns a unique Library test folder.
public static class ShaderFXExternalCodeCacheSmoke
{
    public static string Main()
    {
        string parent = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Library", "WhimTex"));
        string folder = Path.Combine(parent, "ExternalCodeCacheTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var method = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXExternalCode", true)
            .GetMethod("CleanupCacheDirectory", BindingFlags.Static | BindingFlags.NonPublic);
        DateTime now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        DateTime old = now.AddDays(-2);
        var active = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            string stale = Stem(1), fresh = Stem(2), opened = Stem(3), saved = Stem(4), pending = Stem(5);
            string live = Stem(6), boundary = Stem(7), future = Stem(8), orphan = Stem(9);
            foreach (string stem in new[] { stale, fresh, opened, saved, pending, live, boundary, future })
                foreach (string suffix in new[] { ".hlsl", ".baseline", ".hlsl.apply" })
                    Write(folder, stem + suffix, old);
            foreach (string suffix in new[] { ".hlsl", ".baseline", ".hlsl.apply" })
            {
                File.SetLastWriteTimeUtc(Path.Combine(folder, fresh + suffix), now.AddHours(-23));
                File.SetLastWriteTimeUtc(Path.Combine(folder, boundary + suffix), now.AddDays(-1));
                File.SetLastWriteTimeUtc(Path.Combine(folder, future + suffix), now.AddDays(1));
            }
            // Any recent member preserves the complete set, even if drafts differ.
            File.SetLastWriteTimeUtc(Path.Combine(folder, opened + ".baseline"), now);
            File.SetLastWriteTimeUtc(Path.Combine(folder, saved + ".hlsl"), now);
            File.SetLastWriteTimeUtc(Path.Combine(folder, pending + ".hlsl.apply"), now);
            active.Add(live);
            Write(folder, orphan + ".baseline", old);
            Write(folder, "notes.hlsl", old);
            Write(folder, stale + ".other", old);
            Write(folder, new string('z', 32) + ".hlsl", old);
            string nested = Path.Combine(folder, "VSCodeProfile");
            Directory.CreateDirectory(nested);
            Write(nested, stale + ".hlsl", old);

            int deleted = (int)method.Invoke(null, new object[] { folder, now, active });
            Check(deleted == 4, "Only the stale set and orphan baseline should expire: " + deleted);
            foreach (string suffix in new[] { ".hlsl", ".baseline", ".hlsl.apply" })
            {
                Check(!File.Exists(Path.Combine(folder, stale + suffix)), "Stale file survived");
                foreach (string stem in new[] { fresh, opened, saved, pending, live, boundary, future })
                    Check(File.Exists(Path.Combine(folder, stem + suffix)), "Protected file removed: " + stem + suffix);
            }
            Check(!File.Exists(Path.Combine(folder, orphan + ".baseline")), "Orphan survived");
            Check(File.Exists(Path.Combine(folder, "notes.hlsl")), "Unrelated file removed");
            Check(File.Exists(Path.Combine(folder, stale + ".other")), "Unknown suffix removed");
            Check(File.Exists(Path.Combine(folder, new string('z', 32) + ".hlsl")), "Non-hash name removed");
            Check(File.Exists(Path.Combine(nested, stale + ".hlsl")), "Subfolder was scanned");
            Check((int)method.Invoke(null, new object[] { folder, now, active }) == 0, "Sweep not idempotent");
            Check((int)method.Invoke(null, new object[] { Path.Combine(folder, "missing"), now, active }) == 0,
                "Missing cache should be harmless");
            return "PASS: 24h expiry, recent open/save/apply, active sessions, boundary/future dates, orphans, ownership, no recursion and idempotence.";
        }
        finally
        {
            // Delete only this invocation's generated fixtures, never the actual ExternalCode cache.
            if (Path.GetDirectoryName(Path.GetFullPath(folder)) == parent &&
                Path.GetFileName(folder).StartsWith("ExternalCodeCacheTest-", StringComparison.Ordinal))
                Directory.Delete(folder, true);
        }
    }

    private static string Stem(int value) => value.ToString("x32");
    private static void Write(string folder, string name, DateTime time)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "test " + name);
        File.SetLastWriteTimeUtc(path, time);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
