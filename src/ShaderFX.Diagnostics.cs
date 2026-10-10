using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum ShaderFXDiagnosticSeverity { Info, Warning, Error }

    internal readonly struct ShaderFXDiagnostic
    {
        internal readonly ShaderFXDiagnosticSeverity Severity;
        internal readonly string Message, File;
        internal readonly int Line;

        internal ShaderFXDiagnostic(ShaderFXDiagnosticSeverity severity, string message, string file = null, int line = 0)
        {
            Severity = severity;
            Message = message;
            File = file;
            Line = line;
        }

        public override string ToString() => Severity + ": " +
            (Line > 0 ? (string.IsNullOrEmpty(File) ? "Line " + Line + ": " : File + ":" + Line + ": ") : "") + Message;
    }

    internal static class ShaderFXDiagnostics
    {
        private static readonly Regex Location = new Regex(@"^(.*):(\d+):\s*(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex SourceLine = new Regex(@"^Line (\d+):\s*(.*)$", RegexOptions.CultureInvariant);
        private static readonly HashSet<Hash128> reportedMessages = new HashSet<Hash128>();

        internal static List<ShaderFXDiagnostic> Parse(string text, ShaderFXDiagnosticSeverity fallback = ShaderFXDiagnosticSeverity.Info,
            string sourcePath = null)
        {
            var result = new List<ShaderFXDiagnostic>();
            using var reader = new StringReader(text ?? "");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line == "Applied successfully.") continue;
                var severity = fallback;
                foreach (ShaderFXDiagnosticSeverity candidate in Enum.GetValues(typeof(ShaderFXDiagnosticSeverity)))
                {
                    string prefix = candidate + ":";
                    if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    severity = candidate;
                    line = line.Substring(prefix.Length).TrimStart();
                    break;
                }
                Match location = Location.Match(line), sourceLine = SourceLine.Match(line);
                if (location.Success && int.TryParse(location.Groups[2].Value, out int number))
                    result.Add(new ShaderFXDiagnostic(severity, location.Groups[3].Value, location.Groups[1].Value, number));
                else if (sourceLine.Success && int.TryParse(sourceLine.Groups[1].Value, out number))
                    result.Add(new ShaderFXDiagnostic(severity, sourceLine.Groups[2].Value, sourcePath, number));
                else result.Add(new ShaderFXDiagnostic(severity, line));
            }
            return result;
        }

        internal static List<ShaderFXDiagnostic> Collect(Shader shader, string source, string code,
            IReadOnlyList<ShaderFXParameter> parameters, string sourcePath, bool usable)
            => CollectShader(shader, source, code, parameters, sourcePath, usable, true);

        internal static List<ShaderFXDiagnostic> CollectShader(Shader shader, string source, string code,
            IReadOnlyList<ShaderFXParameter> parameters, string sourcePath, bool usable, bool includeDeterminism)
        {
            var result = new List<ShaderFXDiagnostic>();
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                Enum.TryParse(message.severity.ToString(), out ShaderFXDiagnosticSeverity severity);
                result.Add(new ShaderFXDiagnostic(severity, message.message, message.file, message.line));
            }
            if (includeDeterminism)
                result.AddRange(Parse(ShaderFXSourceBuilder.GetDeterminismWarning(source), ShaderFXDiagnosticSeverity.Warning));
            result.AddRange(Parse(ShaderFXMetadata.ControlWarnings(code, parameters), ShaderFXDiagnosticSeverity.Warning, sourcePath));
            if (!usable && !HasErrors(result))
                result.Add(new ShaderFXDiagnostic(ShaderFXDiagnosticSeverity.Error, "The shader is not supported on this graphics device."));
            return result;
        }

        internal static bool HasErrors(IReadOnlyList<ShaderFXDiagnostic> messages)
            => Severity(messages) == ShaderFXDiagnosticSeverity.Error;

        internal static ShaderFXDiagnosticSeverity Severity(IReadOnlyList<ShaderFXDiagnostic> messages)
        {
            var result = ShaderFXDiagnosticSeverity.Info;
            foreach (var message in messages) if (message.Severity > result) result = message.Severity;
            return result;
        }

        internal static string Format(IReadOnlyList<ShaderFXDiagnostic> messages)
        {
            var result = new StringBuilder();
            foreach (var message in messages) result.AppendLine(message.ToString());
            return result.ToString().TrimEnd();
        }

        internal static List<ShaderFXDiagnostic> Failure(Exception error, string sourcePath,
            IReadOnlyList<ShaderFXDiagnostic> collected = null)
        {
            var result = collected == null ? new List<ShaderFXDiagnostic>() : new List<ShaderFXDiagnostic>(collected);
            if (collected == null || error.Message != Format(collected))
                foreach (var message in Parse(error.Message, ShaderFXDiagnosticSeverity.Error, sourcePath))
                    if (!result.Contains(message)) result.Add(message);
            if (!HasErrors(result)) result.Add(new ShaderFXDiagnostic(ShaderFXDiagnosticSeverity.Error, "Shader FX could not be applied."));
            return result;
        }

        internal static void Report(IReadOnlyList<ShaderFXDiagnostic> messages, string sourcePath, UnityEngine.Object context = null)
        {
            foreach (var message in messages)
            {
                if (message.Severity == ShaderFXDiagnosticSeverity.Info) continue;
                string identity = sourcePath + "\n" + message;
                if (!reportedMessages.Add(Hash128.Compute(identity))) continue;
                string text = "WhimTex: " + identity;
                if (message.Severity == ShaderFXDiagnosticSeverity.Error) Debug.LogError(text, context);
                else Debug.LogWarning(text, context);
            }
        }
    }

    public sealed partial class ShaderFX
    {
        [NonSerialized] private string diagnosticSnapshot;
        [NonSerialized] private bool diagnosticSnapshotFailed;
        [NonSerialized] private IReadOnlyList<ShaderFXDiagnostic> diagnosticMessages;

        internal IReadOnlyList<ShaderFXDiagnostic> DiagnosticMessages
        {
            get
            {
                if (diagnosticMessages == null || diagnosticSnapshot != diagnostics || diagnosticSnapshotFailed != lastApplyFailed)
                {
                    diagnosticMessages = ShaderFXDiagnostics.Parse(diagnostics,
                        lastApplyFailed ? ShaderFXDiagnosticSeverity.Error : ShaderFXDiagnosticSeverity.Info, SourcePath).AsReadOnly();
                    diagnosticSnapshot = diagnostics;
                    diagnosticSnapshotFailed = lastApplyFailed;
                }
                return diagnosticMessages;
            }
        }
        internal ShaderFXDiagnosticSeverity DiagnosticSeverity => ShaderFXDiagnostics.Severity(DiagnosticMessages);
        internal string DiagnosticNotice => IsUnavailable ? UnavailableReason :
            DiagnosticSeverity != ShaderFXDiagnosticSeverity.Info ? Diagnostics : null;

        private void SetDiagnostics(IReadOnlyList<ShaderFXDiagnostic> messages, bool failed)
        {
            lastApplyFailed = failed;
            diagnosticMessages = new List<ShaderFXDiagnostic>(messages).AsReadOnly();
            diagnostics = messages.Count == 0 ? "Applied successfully." : ShaderFXDiagnostics.Format(messages);
            diagnosticSnapshot = diagnostics;
            diagnosticSnapshotFailed = failed;
            ShaderFXDiagnostics.Report(diagnosticMessages, SourcePath, this);
        }

        private void RecordApplyFailure(Exception error, IReadOnlyList<ShaderFXDiagnostic> collected = null)
            => SetDiagnostics(ShaderFXDiagnostics.Failure(error, SourcePath, collected), true);
    }
}
