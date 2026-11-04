using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum ShaderDiagnosticSeverity { Info, Warning, Error }

    [Serializable]
    internal struct ShaderDiagnostic
    {
        [SerializeField] internal ShaderDiagnosticSeverity Severity;
        [SerializeField] internal string Message, File;
        [SerializeField] internal int Line;

        internal ShaderDiagnostic(ShaderDiagnosticSeverity severity, string message, string file = null, int line = 0)
        {
            Severity = severity;
            Message = message;
            File = file;
            Line = line;
        }

        public override string ToString() => Severity + ": " +
            (Line > 0 ? (string.IsNullOrEmpty(File) ? "Line " + Line + ": " : File + ":" + Line + ": ") : "") + Message;
    }

    internal static class ShaderDiagnostics
    {
        private static readonly Regex Location = new Regex(@"^(.*):(\d+):\s*(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex SourceLine = new Regex(@"^Line (\d+):\s*(.*)$", RegexOptions.CultureInvariant);
        private static readonly HashSet<Hash128> reportedMessages = new HashSet<Hash128>();

        internal static List<ShaderDiagnostic> Parse(string text, ShaderDiagnosticSeverity fallback = ShaderDiagnosticSeverity.Info,
            string sourcePath = null)
        {
            var result = new List<ShaderDiagnostic>();
            using var reader = new StringReader(text ?? "");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line == "Applied successfully." || line == "Compiled successfully.") continue;
                var severity = fallback;
                foreach (ShaderDiagnosticSeverity candidate in Enum.GetValues(typeof(ShaderDiagnosticSeverity)))
                {
                    string prefix = candidate + ":";
                    if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    severity = candidate;
                    line = line.Substring(prefix.Length).TrimStart();
                    break;
                }
                Match location = Location.Match(line), sourceLine = SourceLine.Match(line);
                if (location.Success && int.TryParse(location.Groups[2].Value, out int number))
                    result.Add(new ShaderDiagnostic(severity, location.Groups[3].Value, location.Groups[1].Value, number));
                else if (sourceLine.Success && int.TryParse(sourceLine.Groups[1].Value, out number))
                    result.Add(new ShaderDiagnostic(severity, sourceLine.Groups[2].Value, sourcePath, number));
                else result.Add(new ShaderDiagnostic(severity, line));
            }
            return result;
        }

        internal static List<ShaderDiagnostic> CollectProgram(Shader shader, ShaderSourceAnalysis analysis, string code,
            IReadOnlyList<ShaderParameter> parameters, string sourcePath, bool usable)
        {
            var result = ReadCompiler(shader);
            if (analysis.UnityTimeWarning != null)
                result.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Warning, analysis.UnityTimeWarning));
            if (analysis.LocalityStatus == "unverified")
                result.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Info, ShaderEntryPoint.UnverifiedInfo));
            result.AddRange(Parse(ShaderMetadata.ControlWarnings(code, parameters), ShaderDiagnosticSeverity.Warning, sourcePath));
            if (!usable && !HasErrors(result))
                result.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "The shader is not supported on this graphics device."));
            return result;
        }

        internal static List<ShaderDiagnostic> ReadCompiler(Shader shader)
        {
            var result = new List<ShaderDiagnostic>();
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                Enum.TryParse(message.severity.ToString(), out ShaderDiagnosticSeverity severity);
                if (IsNoiseLoopSpecialization(message, severity)) continue;
                result.Add(new ShaderDiagnostic(severity, message.message, message.file, message.line));
            }
            return result;
        }

        static bool IsNoiseLoopSpecialization(ShaderMessage message, ShaderDiagnosticSeverity severity)
        {
            // WT shares bounded, rolled kernels across dimensions and fractal modes.
            // Specializing an unused axis or a single octave legitimately leaves one
            // iteration. Keep the loop hint for larger cases without flagging the node.
            // Do not suppress other library warnings or the same warning in user code.
            if (severity != ShaderDiagnosticSeverity.Warning ||
                message.message != "loop only executes for 1 iteration(s), consider removing [loop]") return false;
            string file = message.file?.Replace('\\', '/');
            const string root = "Packages/com.dcfapixels.whimtex/src/Shaders/";
            if (file == null) return false;
            int start = file.IndexOf(root, StringComparison.Ordinal);
            if (start < 0 || start > 0 && file[start - 1] != '/') return false;
            return file.Substring(start + root.Length) is "WTNoiseBasis.hlsl" or "WTNoiseFields.hlsl" or
                "WTNoiseDerivatives.hlsl" or "WTNoiseCellular.hlsl" or "WTNoiseGabor.hlsl" or "WTNoiseSpots.hlsl" or "WTNoiseShard.hlsl" or
                "WTNoiseTriangleVoronoi.hlsl";
        }

        internal static List<ShaderDiagnostic> CollectShader(Shader shader, string code,
            IReadOnlyList<ShaderParameter> parameters, string sourcePath, bool usable,
            IReadOnlyList<ShaderDiagnostic> compilerMessages = null)
        {
            var result = compilerMessages == null ? ReadCompiler(shader) : new List<ShaderDiagnostic>(compilerMessages);
            result.AddRange(Parse(ShaderMetadata.ControlWarnings(code, parameters), ShaderDiagnosticSeverity.Warning, sourcePath));
            if (!usable && !HasErrors(result))
                result.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "The shader is not supported on this graphics device."));
            return result;
        }

        internal static bool HasErrors(IReadOnlyList<ShaderDiagnostic> messages)
            => Severity(messages) == ShaderDiagnosticSeverity.Error;

        internal static ShaderDiagnosticSeverity Severity(IReadOnlyList<ShaderDiagnostic> messages)
        {
            var result = ShaderDiagnosticSeverity.Info;
            foreach (var message in messages) if (message.Severity > result) result = message.Severity;
            return result;
        }

        internal static string Format(IReadOnlyList<ShaderDiagnostic> messages)
        {
            var result = new StringBuilder();
            foreach (var message in messages) result.AppendLine(message.ToString());
            return result.ToString().TrimEnd();
        }

        internal static List<ShaderDiagnostic> Failure(Exception error, string sourcePath,
            IReadOnlyList<ShaderDiagnostic> collected = null)
        {
            collected ??= (error as ShaderGraphException)?.diagnostics;
            var result = collected == null ? new List<ShaderDiagnostic>() : new List<ShaderDiagnostic>(collected);
            if (collected == null || error.Message != Format(collected))
                foreach (var message in Parse(error.Message, ShaderDiagnosticSeverity.Error, sourcePath))
                    if (!result.Contains(message)) result.Add(message);
            if (!HasErrors(result)) result.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Shader FX could not be applied."));
            return result;
        }

        internal static void Report(IReadOnlyList<ShaderDiagnostic> messages, string sourcePath, UnityEngine.Object context = null)
        {
            foreach (var message in messages)
            {
                if (message.Severity == ShaderDiagnosticSeverity.Info) continue;
                string identity = sourcePath + "\n" + message;
                if (!reportedMessages.Add(Hash128.Compute(identity))) continue;
                string text = "WhimTex: " + identity;
                if (message.Severity == ShaderDiagnosticSeverity.Error) Debug.LogError(text, context);
                else Debug.LogWarning(text, context);
            }
        }
    }

}
