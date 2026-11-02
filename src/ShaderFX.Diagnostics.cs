using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [NonSerialized] private string diagnosticSnapshot;
        [NonSerialized] private bool diagnosticSnapshotFailed;
        [NonSerialized] private IReadOnlyList<ShaderDiagnostic> diagnosticMessages;

        internal IReadOnlyList<ShaderDiagnostic> DiagnosticMessages
        {
            get
            {
                if (diagnosticMessages == null || diagnosticSnapshot != diagnostics || diagnosticSnapshotFailed != lastCompileFailed)
                {
                    diagnosticMessages = ShaderDiagnostics.Parse(diagnostics,
                        lastCompileFailed ? ShaderDiagnosticSeverity.Error : ShaderDiagnosticSeverity.Info, SourcePath).AsReadOnly();
                    diagnosticSnapshot = diagnostics;
                    diagnosticSnapshotFailed = lastCompileFailed;
                }
                return diagnosticMessages;
            }
        }
        internal ShaderDiagnosticSeverity DiagnosticSeverity => ShaderDiagnostics.Severity(DiagnosticMessages);
        internal string DiagnosticNotice => IsUnavailable ? UnavailableReason :
            DiagnosticSeverity != ShaderDiagnosticSeverity.Info ? Diagnostics : null;

        private void SetDiagnostics(IReadOnlyList<ShaderDiagnostic> messages, bool failed)
        {
            lastCompileFailed = failed;
            diagnosticMessages = new List<ShaderDiagnostic>(messages).AsReadOnly();
            diagnostics = messages.Count == 0 ? "Compiled successfully." : ShaderDiagnostics.Format(messages);
            Status(Mode).SetDiagnostics(diagnostics, failed);
            diagnosticSnapshot = diagnostics;
            diagnosticSnapshotFailed = failed;
            ShaderDiagnostics.Report(diagnosticMessages, SourcePath, this);
        }

        private void RecordCompileFailure(Exception error, IReadOnlyList<ShaderDiagnostic> collected = null, bool draft = false)
        {
            CompilationStatus.Fail(error, draft);
            SetDiagnostics(ShaderDiagnostics.Failure(error, SourcePath, collected), true);
        }
    }
}
