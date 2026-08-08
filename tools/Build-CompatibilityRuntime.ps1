param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '..\src\Tessalume.App\Compatibility'),

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$sourceRoot = [System.IO.Path]::GetFullPath($SourceDirectory)
$manifestPath = Join-Path $sourceRoot 'Runtime\runtime-bundle.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.output -ne 'theme-runtime-v2.js') {
    throw 'Compatibility runtime bundle manifest is invalid.'
}

$fragmentNames = @($manifest.fragments)
if ($fragmentNames.Count -eq 0 -or $fragmentNames.Count -gt 8) {
    throw 'Compatibility runtime fragment count is invalid.'
}

$runtimeDirectory = Split-Path -Parent $manifestPath
$parts = foreach ($fragmentName in $fragmentNames) {
    if ([System.IO.Path]::GetFileName($fragmentName) -ne $fragmentName -or
        [System.IO.Path]::GetExtension($fragmentName) -ne '.js') {
        throw "Compatibility runtime fragment path is invalid: $fragmentName"
    }
    $fragmentPath = Join-Path $runtimeDirectory $fragmentName
    $fragmentSource = Get-Content -LiteralPath $fragmentPath -Raw -Encoding UTF8
    $fragmentSource = [regex]::Replace(
        $fragmentSource,
        '(?ms)^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_START\s*\r?\n.*?^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_END\s*(?:\r?\n)?',
        '')
    if ($fragmentSource.Contains('TESSALUME_STANDALONE_ENVELOPE')) {
        throw "Compatibility runtime standalone envelope is incomplete: $fragmentName"
    }
    $fragmentSource.TrimEnd("`r", "`n")
}

$source = ($parts -join "`n") + "`n"
if ($source.Length -gt 2MB -or
    -not $source.Contains('mountCanonicalTheme') -or
    -not $source.Contains('syncAdaptiveVisibility') -or
    -not $source.Contains('decorateSharedSurfaces') -or
    -not $source.TrimEnd().EndsWith('})()')) {
    throw 'Compatibility runtime assembly is incomplete.'
}

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = Split-Path -Parent $destinationPath
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
[System.IO.File]::WriteAllText(
    $destinationPath,
    $source,
    [System.Text.UTF8Encoding]::new($false))
