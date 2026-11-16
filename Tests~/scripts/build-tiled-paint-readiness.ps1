param(
    [string] $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
)

# Produces a run_script input only. Compilation and execution are explicitly left
# to the connected Unity Editor; this script never starts a compiler or Editor.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$package = Join-Path $project 'Packages/com.dcfapixels.whimtex'
$adapter = Join-Path $package 'Tests~/Framework/Painting/TiledPaintBrushBatch.cs'
$test = Join-Path $package 'Tests~/Cases/Painting/TiledPaintReadinessTests.cs'
if (!(Test-Path -LiteralPath $adapter -PathType Leaf)) { throw "Missing simple-tip test adapter: $adapter" }
if (!(Test-Path -LiteralPath $test -PathType Leaf)) { throw "Missing readiness tests: $test" }
$files = @($adapter, $test)
$usings = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$bodies = [Collections.Generic.List[string]]::new()
$usingPattern = '(?m)^using[ \t]+[^;\r\n]+;[ \t]*\r?$'
foreach ($file in $files) {
    $text = [IO.File]::ReadAllText($file)
    foreach ($match in [regex]::Matches($text, $usingPattern)) {
        $directive = [regex]::Replace($match.Value.Trim(), '\s*=\s*', ' = ')
        [void]$usings.Add($directive)
    }
    $body = [regex]::Replace($text, $usingPattern, '')
    $relative = $file.Substring($project.Length).TrimStart('\', '/').Replace('\', '/')
    $bodies.Add("#line 1 `"$relative`"`n$body`n#line default`n")
}
[void]$usings.Add('using BlendMode = DCFApixels.WhimTex.BlendMode;')
$outputDirectory = Join-Path $project 'Temp/WhimTex/tiled-paint-readiness'
[void][IO.Directory]::CreateDirectory($outputDirectory)
$output = Join-Path $outputDirectory 'TiledPaintReadiness.Run.cs'
$bundle = ($usings -join "`n") + "`n`n" + ($bodies -join "`n")
[IO.File]::WriteAllText($output, $bundle, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    file = $output
    entry = 'TiledPaintReadinessTests.Run'
    scenarios = @('core', 'dimensions', 'native', 'all')
    sources = $files.Count
    compiled = $false
} | ConvertTo-Json -Depth 3
