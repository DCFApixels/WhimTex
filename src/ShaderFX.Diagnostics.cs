using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        internal IReadOnlyList<ShaderDiagnostic> DiagnosticMessages => CompilationStatus.DiagnosticMessages;
        internal ShaderDiagnosticSeverity DiagnosticSeverity => ShaderDiagnostics.Severity(DiagnosticMessages);
        internal string DiagnosticNotice => IsUnavailable ? UnavailableReason :
            DiagnosticSeverity != ShaderDiagnosticSeverity.Info ? Diagnostics : null;
        private void SetDiagnostics(IReadOnlyList<ShaderDiagnostic> messages, bool failed)
        {
            CompilationStatus.SetDiagnostics(messages, failed);
            ShaderDiagnostics.Report(messages, SourcePath, this);
        }
        private void RecordCompileFailure(Exception error, bool draft = false)
        {
            CompilationStatus.Fail(error, draft, sourcePath: SourcePath);
            ShaderDiagnostics.Report(DiagnosticMessages, SourcePath, this);
        }
    }
}
