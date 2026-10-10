using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class SystemFontCatalog
    {
        private static readonly string[] preferred = { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica", "Noto Sans" };
        private static string[] names;
        private static HashSet<string> available;
        internal static int Revision { get; private set; }
        internal static IReadOnlyList<string> Names { get { Ensure(); return names; } }
        internal static bool Contains(string name) { Ensure(); return !string.IsNullOrEmpty(name) && available.Contains(name); }
        internal static string DefaultName
        {
            get
            {
                Ensure();
                foreach (string candidate in preferred)
                    if (available.Contains(candidate)) return candidate;
                return names.Length == 0 ? null : names[0];
            }
        }
        internal static void Refresh() { names = null; available = null; Revision++; Ensure(); }
        private static void Ensure()
        {
            if (names != null) return;
            available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Font.GetOSInstalledFontNames())
                if (!string.IsNullOrWhiteSpace(name)) available.Add(name);
            names = new string[available.Count];
            available.CopyTo(names);
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        }
    }
}
