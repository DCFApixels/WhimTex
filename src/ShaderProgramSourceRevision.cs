using System;
using System.IO;
using System.Threading;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    // Reusing an in-memory executable must not hide changes to its include dependencies.
    internal sealed class ShaderProgramSourceRevision : AssetPostprocessor
    {
        static int current;
        internal static int Current => Volatile.Read(ref current);
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (HasSource(imported) || HasSource(deleted) || HasSource(moved) || HasSource(movedFrom)) Interlocked.Increment(ref current);
        }
        static bool HasSource(string[] paths)
        {
            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path);
                if (extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cginc", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".shader", StringComparison.OrdinalIgnoreCase) || extension.Equals(".compute", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
