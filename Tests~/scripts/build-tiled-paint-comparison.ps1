param(
    [string] $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
)

# Builds a run_script input, never compiles or starts Unity. All dependencies are
# package-owned; no experimental renderer or external Temp source is included.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$package = Join-Path $project 'Packages/com.dcfapixels.whimtex'
$oracle = Join-Path $package 'Tests~/Framework/Painting/TiledPaintNativeOracle.cs'
$adapter = Join-Path $package 'Tests~/Framework/Painting/TiledPaintBrushBatch.cs'
$readiness = Join-Path $package 'Tests~/Cases/Painting/TiledPaintReadinessTests.cs'
$comparison = Join-Path $package 'Tests~/Cases/Painting/TiledPaintNativeComparisonTests.cs'
foreach ($file in @($oracle, $adapter, $readiness, $comparison)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing comparison input: $file" }
}
$files = @($oracle, $adapter, $readiness, $comparison)
$usings = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$bodies = [Collections.Generic.List[string]]::new()
$manifest = [Collections.Generic.List[object]]::new()
$usingPattern = '(?m)^using[ \t]+[^;\r\n]+;[ \t]*\r?$'
foreach ($file in $files) {
    $text = [IO.File]::ReadAllText($file)
    foreach ($match in [regex]::Matches($text, $usingPattern)) {
        $directive = [regex]::Replace($match.Value.Trim(), '\s*=\s*', ' = ')
        [void]$usings.Add($directive)
    }
    $relative = $file.Substring($project.Length).TrimStart('\', '/').Replace('\', '/')
    $body = [regex]::Replace($text, $usingPattern, '')
    $bodies.Add("#line 1 `"$relative`"`n$body`n#line default`n")
    $manifest.Add([pscustomobject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() })
}
# Production dependencies are reviewed and fingerprinted, never compiled again
# into the diagnostic assembly alongside their already loaded public types.
$reviewFiles = @(Get-ChildItem -LiteralPath (Join-Path $package 'src/Layers/TiledPaint') -Filter '*.cs' -File) +
    @(Get-ChildItem -LiteralPath (Join-Path $package 'src/Shaders') -Filter 'TiledPaint*' -File | Where-Object Extension -ne '.meta')
foreach ($file in $reviewFiles) {
    $relative = $file.FullName.Substring($project.Length).TrimStart('\', '/').Replace('\', '/')
    $manifest.Add([pscustomobject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
}
[void]$usings.Add('using BlendMode = DCFApixels.WhimTex.BlendMode;')
$outputDirectory = Join-Path $project 'Temp/WhimTex/tiled-paint-readiness'
[void][IO.Directory]::CreateDirectory($outputDirectory)
$output = Join-Path $outputDirectory 'TiledPaintNativeComparison.Run.cs'
[IO.File]::WriteAllText($output, ($usings -join "`n") + "`n`n" + ($bodies -join "`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $outputDirectory 'native-comparison-inputs.json'),
    ($manifest | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    file = $output
    entry = 'TiledPaintNativeComparisonTests.Run'
    defaults = @{ diameter = 2048; spacing = 0.005; repeats = 3; comparePixels = $true }
    sources = $files.Count
    compiled = $false
} | ConvertTo-Json -Depth 3
