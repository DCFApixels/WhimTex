using System;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal static class ShaderLayoutDirectives
    {
        internal static void Transition(StringBuilder source, ref ShaderLayoutContainer[] active, ShaderLayoutContainer[] next)
        {
            next ??= Array.Empty<ShaderLayoutContainer>();
            int shared = 0;
            while (shared < active.Length && shared < next.Length && active[shared].Equals(next[shared])) shared++;
            for (int i = active.Length - 1; i >= shared; i--)
                source.AppendLine("// @end" + active[i].kind.ToString().ToLowerInvariant());
            for (int i = shared; i < next.Length; i++)
            {
                var container = next[i];
                string arguments = container.title?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(container.headerParameter)) arguments += "; " + container.headerParameter;
                string directive = "// @" + container.kind.ToString().ToLowerInvariant();
                source.AppendLine(string.IsNullOrEmpty(arguments) ? directive : directive + "(" + arguments + ")");
            }
            active = next;
        }
    }
}
