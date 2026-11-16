param(
    [string] $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
)

# Builds a run_script input, never compiles or starts Unity. All dependencies are
# package-owned; no experimental renderer or external Temp source is included.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$package = Join-Path $project 'Packages/com.dcfapixels.whimtex'
$oracle = Join-Path $package 'Tests~/Framework/Painting/TiledPaintNativeOracle.cs'
$source = Join-Path $package 'Staging~/TiledPaint/Source'
$readiness = Join-Path $package 'Tests~/Cases/Painting/TiledPaintReadinessTests.cs'
$comparison = Join-Path $package 'Tests~/Cases/Painting/TiledPaintNativeComparisonTests.cs'
foreach ($file in @($oracle, $readiness, $comparison)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing comparison input: $file" }
}
if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Missing staged sources: $source" }
$files = @($oracle) + @(Get-ChildItem -LiteralPath $source -Filter '*.cs' -File -Recurse |
    Sort-Object FullName | ForEach-Object { $_.FullName }) + @($readiness, $comparison)
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
