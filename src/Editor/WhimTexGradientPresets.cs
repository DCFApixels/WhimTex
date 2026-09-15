using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexGradientPresets
    {
        internal sealed class Entry
        {
            internal string Path;
            internal WhimTexGradient Gradient;
            internal DateTime CreatedUtc;
        }
        internal static string Folder => System.IO.Path.GetFullPath(System.IO.Path.Combine(WhimTexUserSettings.PresetsFolder, "Gradients"));

        internal static List<Entry> Read(out string warning)
        {
            var entries = new List<Entry>();
            warning = null;
            try
            {
                if (!Directory.Exists(Folder)) return entries;
                string[] files = Directory.GetFiles(Folder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (string file in files)
                {
                    if (!Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > 65536) throw new IOException("Gradient preset exceeds 64 KiB.");
                        entries.Add(new Entry { Path = file, CreatedUtc = info.CreationTimeUtc,
                            Gradient = WhimTexGradientClipboard.Read(File.ReadAllText(file)) });
                    }
                    catch (Exception error) { warning = "Some presets could not be read: " + error.GetBaseException().Message; }
                }
            }
            catch (Exception error) { warning = "Presets unavailable: " + error.Message; }
            entries.Sort((a, b) =>
            {
                int order = b.CreatedUtc.CompareTo(a.CreatedUtc);
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.Path, b.Path);
            });
            return entries;
        }

        internal static string Save(WhimTexGradient gradient)
        {
            string json = WhimTexGradientClipboard.Write(gradient);
            Directory.CreateDirectory(Folder);
            string path = System.IO.Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".json");
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }

        internal static void Remove(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            if (!string.Equals(System.IO.Path.GetDirectoryName(full), Folder, StringComparison.OrdinalIgnoreCase) ||
                System.IO.Path.GetExtension(full) != ".json" ||
                !Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(full), "N", out _))
                throw new IOException("Only user gradient presets can be removed.");
            string trash = System.IO.Path.Combine(Folder, ".trash");
            Directory.CreateDirectory(trash);
            File.Move(full, System.IO.Path.Combine(trash, Guid.NewGuid().ToString("N") + ".json"));
        }
    }
}
