using System.Collections.Generic;
using System.IO;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphSharedSource
    {
        static readonly Dictionary<string, string> sources = new();
        static int revision = -1;

        internal static string Read(string module)
        {
            lock (sources)
            {
                int current = ShaderProgramSourceRevision.Current;
                if (current != revision) { sources.Clear(); revision = current; }
                if (!sources.TryGetValue(module, out var source))
                {
                    string path = "Packages/com.dcfapixels.whimtex/src/Shaders/Graph" + module + ".hlsl";
                    sources.Add(module, source = File.ReadAllText(ShaderGraphSources.PhysicalPath(path)) + "\n");
                }
                return source;
            }
        }
    }
}
