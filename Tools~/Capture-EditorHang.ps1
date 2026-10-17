# Windows-only, explicitly opt-in diagnostics. Never launches or terminates Unity.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet('Capture', 'Arm', 'Disarm')][string]$Mode = 'Capture',
    [string]$ProcDumpPath,
    [int]$UnityProcessId = 0,
    [switch]$MiniOnly,
    [switch]$AcceptEula
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This capture helper requires Windows.' }
$project = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) { throw 'Not a Unity project.' }
$root = Join-Path $project 'Temp/WhimTex/HangDiagnostics'
$flag = Join-Path $root 'vector-brush.enabled'
New-Item -ItemType Directory -Path $root -Force | Out-Null
if ($Mode -eq 'Arm') {
    [IO.File]::WriteAllText($flag, 'Local vector-brush hang breadcrumbs enabled. No dump has been started.')
    Write-Host 'Armed. After scripts reload, finish a vector-brush stroke to begin the journal. No test or capture was run.'
    return
}
if ($Mode -eq 'Disarm') {
    if (Test-Path -LiteralPath $flag) { Remove-Item -LiteralPath $flag }
    Write-Host 'Disarmed. Existing journals and dumps are retained.'
    return
}

function Get-ArgumentValue([string]$commandLine, [string]$name) {
    $match = [regex]::Match($commandLine, '(?i)(?:^|\s)-' + [regex]::Escape($name) + '\s+(?:"([^"]+)"|([^\s]+))')
    if (-not $match.Success) { return $null }
    if ($match.Groups[1].Success) { return $match.Groups[1].Value }
    return $match.Groups[2].Value
}

function Test-EditorProject($candidate) {
    if ($candidate.Name -ine 'Unity.exe' -or [string]::IsNullOrWhiteSpace($candidate.CommandLine)) { return $false }
    if ($candidate.CommandLine -match '(?i)(?:^|\s)-(?:batchmode|assetImportWorker)(?:\s|$)' -or
        $candidate.CommandLine -match 'AssetImportWorker') { return $false }
    $path = Get-ArgumentValue $candidate.CommandLine 'projectPath'
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    try { return [IO.Path]::GetFullPath($path).TrimEnd('\', '/') -ieq $project } catch { return $false }
}

$editors = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { Test-EditorProject $_ })
if ($UnityProcessId -gt 0) { $editors = @($editors | Where-Object { $_.ProcessId -eq $UnityProcessId }) }
if ($editors.Count -ne 1) { throw 'Expected exactly one live Editor for the explicit project. Specify -UnityProcessId if needed; workers and other projects are never selected.' }
$editor = $editors[0]
$editorProcessId = [int]$editor.ProcessId
$created = $editor.CreationDate.ToUniversalTime()

function Assert-EditorIdentity {
    $currentEditor = Get-CimInstance Win32_Process -Filter "ProcessId = $editorProcessId"
    if ($null -eq $currentEditor -or -not (Test-EditorProject $currentEditor) -or
        $currentEditor.CreationDate.ToUniversalTime() -ne $created) {
        throw 'Editor exited/restarted or PID was reused. Capture stopped; no replacement process will be targeted.'
    }
}

if ([string]::IsNullOrWhiteSpace($ProcDumpPath)) { $ProcDumpPath = Join-Path $root 'tools/procdump64.exe' }
$dumpTool = (Resolve-Path -LiteralPath $ProcDumpPath).Path
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($dumpTool).ProductName -ne 'ProcDump') { throw 'The selected executable is not ProcDump.' }
$signature = Get-AuthenticodeSignature -LiteralPath $dumpTool
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
    throw 'ProcDump must have a valid Microsoft Authenticode signature. No executable was launched.'
}
if (-not $AcceptEula) {
    $license = Get-ItemProperty -LiteralPath 'HKCU:\Software\Sysinternals\ProcDump' -ErrorAction SilentlyContinue
    if ($null -eq $license -or $null -eq $license.PSObject.Properties['EulaAccepted'] -or $license.EulaAccepted -ne 1) {
        throw 'Read the Microsoft Sysinternals license, then explicitly pass -AcceptEula or accept it by running ProcDump yourself. Capture has not started.'
    }
}
# Do not overlap another dump/debugger helper. This is a read-only check.
if (@(Get-CimInstance Win32_Process -Filter "Name = 'procdump.exe' OR Name = 'procdump64.exe' OR Name = 'procdump64a.exe'").Count -ne 0) {
    throw 'Another ProcDump is running. Finish that capture before starting this one.'
}
$editorProcess = Get-Process -Id $editorProcessId
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($root))
$requiredBytes = if ($MiniOnly) { 1GB } else { [Math]::Max(4GB, 2 * $editorProcess.PrivateMemorySize64 + 1GB) }
if ($drive.AvailableFreeSpace -lt $requiredBytes) { throw 'Insufficient disk headroom for a dump. Free space or explicitly choose -MiniOnly (less useful for Mono/JIT inspection).' }

$capture = Join-Path $root ('capture-{0}-{1}-{2}' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), $editorProcessId, [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $capture | Out-Null
$captureLock = $null
try {
    # FileShare.None prevents two copies of this helper racing for the same project.
    $captureLock = [IO.File]::Open((Join-Path $root 'capture.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $metadata = [ordered]@{
        capturedUtc = [DateTime]::UtcNow.ToString('O'); project = $project; processId = $editorProcessId
        processStartedUtc = $created.ToString('O'); editorExecutable = $editor.ExecutablePath
        editorVersion = $editorProcess.MainModule.FileVersionInfo.FileVersion
        privateBytes = $editorProcess.PrivateMemorySize64; workingSetBytes = $editorProcess.WorkingSet64
        dumpMode = $(if ($MiniOnly) { 'two mini dumps' } else { 'one full and one mini dump' })
        procDumpSha256 = (Get-FileHash -LiteralPath $dumpTool -Algorithm SHA256).Hash
        note = 'Command line intentionally omitted. Entry stacks in the journal are breadcrumbs, not sampled hang stacks.'
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $capture 'capture.json') -Encoding UTF8

    # Preserve the most recent journal for this exact Editor instance. It may still be open.
    $trace = Get-ChildItem -LiteralPath $root -Filter "trace-$editorProcessId-*.jsonl" |
        Where-Object { $_.LastWriteTimeUtc -ge $created } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -ne $trace) {
        $source = [IO.File]::Open($trace.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $destination = [IO.File]::Open((Join-Path $capture 'trace-before.jsonl'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try { $source.CopyTo($destination) } finally { $destination.Dispose() }
        } finally { $source.Dispose() }
    }
    foreach ($name in @('DCFApixels.WhimTex.dll', 'DCFApixels.WhimTex.pdb', 'DCFApixels.WhimTex.dll.mdb')) {
        $sourceFile = Join-Path $project ('Library/ScriptAssemblies/' + $name)
        if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $capture $name) }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorHang.windbg.txt') -Destination (Join-Path $capture 'EditorHang.windbg.txt')
    $editorLog = Get-ArgumentValue $editor.CommandLine 'logFile'
    if ([string]::IsNullOrWhiteSpace($editorLog)) { $editorLog = Join-Path $project 'Logs/Editor.log' }
    if ([IO.Path]::IsPathRooted($editorLog) -and (Test-Path -LiteralPath $editorLog -PathType Leaf)) {
        Get-Content -LiteralPath $editorLog -Tail 400 | Set-Content -LiteralPath (Join-Path $capture 'Editor-tail.log') -Encoding UTF8
    }

    Write-Host "Capturing only project '$project', Editor PID $editorProcessId. Unity may pause while a dump is written. Nothing is uploaded."
    for ($sample = 1; $sample -le 2; $sample++) {
        Assert-EditorIdentity
        $dumpFile = Join-Path $capture ("threads-$sample.dmp")
        $arguments = @('-at', '45')
        if ($AcceptEula) { $arguments += '-accepteula' }
        $arguments += $(if ($sample -eq 1 -and -not $MiniOnly) { '-ma' } else { '-mm' })
        $arguments += @([string]$editorProcessId, $dumpFile)
        # No -k (kill), -r (memory-heavy clone), -wer (upload), -i (install), -w or -x (launch).
        & $dumpTool @arguments 2>&1 | Tee-Object -FilePath (Join-Path $capture "procdump-$sample.log")
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dumpFile) -or (Get-Item -LiteralPath $dumpFile).Length -eq 0) {
            throw "Dump $sample failed or timed out. Partial evidence retained in $capture."
        }
        if ($sample -eq 1) { Start-Sleep -Seconds 5 }
    }
    if ($null -ne $trace -and (Test-Path -LiteralPath $trace.FullName)) {
        $source = [IO.File]::Open($trace.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $destination = [IO.File]::Open((Join-Path $capture 'trace-after.jsonl'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try { $source.CopyTo($destination) } finally { $destination.Dispose() }
        } finally { $source.Dispose() }
    }
    Get-ChildItem -LiteralPath $capture -File | Get-FileHash -Algorithm SHA256 |
        Select-Object @{Name='file';Expression={[IO.Path]::GetFileName($_.Path)}},Hash |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $capture 'sha256.json') -Encoding UTF8
    Write-Host "Capture complete: $capture"
} finally {
    if ($null -ne $captureLock) { $captureLock.Dispose() }
    $editorProcess.Dispose()
}
