$ErrorActionPreference = 'Stop'
$legacyTarget = 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tests~\Legacy'
$legacyBackup = 'D:\DCFA\Projects\Test6.6\Temp\WhimTex\legacy-retirement-backup-20261005-7efd0ad3-6faf-408b-980b-b499c422412d'
$legacyManifest = 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tests~\legacy-manifest.json'
$readyFile = 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tests~\ArchiveRetirementBeforeDeletionApproved20261005.results.json'
$deletionProof = 'D:\DCFA\Projects\Test6.6\Temp\WhimTex\legacy-physical-removal-20261005.json'
$runnerLock = 'D:\DCFA\Projects\Test6.6\Temp\WhimTex\test-runs\runner.lock'
function Assert-NoRedirection([string] $literalPath) {
    $current = [System.IO.Path]::GetFullPath($literalPath)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Redirected path: $current" }
        }
        $current = [System.IO.Path]::GetDirectoryName($current)
    }
}
foreach ($literalPath in @($legacyTarget,$legacyBackup,$legacyManifest,$readyFile,$deletionProof,$runnerLock)) { Assert-NoRedirection $literalPath }
if ((Get-Item -LiteralPath $legacyTarget -Force).FullName -ne $legacyTarget -or
    (Get-Item -LiteralPath $legacyBackup -Force).FullName -ne $legacyBackup) { throw 'Wrong resolved literal archive/backup path' }
if (Test-Path -LiteralPath $deletionProof) { throw 'Existing deletion evidence: inspect instead of replay' }
if (Test-Path -LiteralPath $runnerLock) { throw 'A test runner still owns work' }
$ready = Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json
if ($ready.finalSuiteComplete -ne $true -or $ready.readyForAuthorizedPhysicalDeletion -ne $true -or
    $ready.archive.physicalArchive.path -ne $legacyTarget -or $ready.archive.backup.tree.path -ne $legacyBackup -or
    $ready.archive.backup.current -ne $true -or $ready.archive.authenticatePinnedGit.bytesChecked -ne 466 -or
    $ready.projectSettings.approvedSettingsDeviation -ne $true -or $ready.projectSettings.changes.Count -ne 1) { throw 'Final readiness not established' }
foreach ($gate in $ready.gates.PSObject.Properties) { if ($gate.Value -ne $true) { throw "Gate failed: $($gate.Name)" } }
if ((Get-FileHash -LiteralPath 'D:\DCFA\Projects\Test6.6\ProjectSettings\ProjectSettings.asset' -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    'd65b6a9cddbf563a6c57d430916eb00b87294d0386e0eec0276d9d646e26be23') { throw 'Approved settings state changed' }
$manifest = Get-Content -LiteralPath $legacyManifest -Raw | ConvertFrom-Json
if ($manifest.files.Count -ne 466) { throw 'Frozen inventory count differs' }
function Test-FrozenTree([string] $rootPath) {
    Assert-NoRedirection $rootPath
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($rootPath)
    $found = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    while ($pending.Count -gt 0) {
        $parent = $pending.Pop()
        foreach ($child in Get-ChildItem -LiteralPath $parent -Force) {
            if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Redirected archive entry: $($child.FullName)" }
            if (-not $child.FullName.StartsWith($rootPath + '\',[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Tree path escaped exact root' }
            if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            else {
                $relative = [System.IO.Path]::GetRelativePath($rootPath,$child.FullName).Replace('\','/')
                $found.Add($relative,$child)
            }
        }
    }
    if ($found.Count -ne 466) { throw 'Archive has extra/missing files' }
    foreach ($entry in $manifest.files) {
        if (-not $found.ContainsKey($entry.file)) { throw "Missing archive entry: $($entry.file)" }
        $item = $found[$entry.file]
        if ($item.Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
            throw "Frozen archive bytes differ: $($entry.file)"
        }
    }
}
Test-FrozenTree $legacyTarget
Test-FrozenTree $legacyBackup
$readyHash = (Get-FileHash -LiteralPath $readyFile -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (Get-FileHash -LiteralPath $legacyManifest -Algorithm SHA256).Hash.ToLowerInvariant()
if (Test-Path -LiteralPath $runnerLock) { throw 'Runner started before deletion' }
# The only recursive removal: the explicitly named, resolved, fully verified archive.
Remove-Item -LiteralPath $legacyTarget -Recurse -Force
if (Test-Path -LiteralPath $legacyTarget) { throw 'Archive removal incomplete' }
Test-FrozenTree $legacyBackup
$proof = [ordered]@{
    version=1; removed=$true; path=$legacyTarget; files=466; backup=$legacyBackup; backupVerifiedAfterRemoval=$true
    pinnedRecoveryCommit='ca8603c0961ce36064280f952259f8a6142d46cc'; userAuthorization='Да, оставь настройки и удаляй Legacy'
    readyReport=$readyFile; readyReportSha256=$readyHash; frozenManifestSha256=$manifestHash
    settingsNotModifiedByRemoval=$true; commitOrPushPerformed=$false
}
$proofBytes = [System.Text.Encoding]::UTF8.GetBytes(($proof | ConvertTo-Json -Depth 8) + "`n")
$proofStream = [System.IO.FileStream]::new($deletionProof,[System.IO.FileMode]::CreateNew,[System.IO.FileAccess]::Write)
try { $proofStream.Write($proofBytes,0,$proofBytes.Length); $proofStream.Flush($true) } finally { $proofStream.Dispose() }
$proof | ConvertTo-Json -Depth 8
