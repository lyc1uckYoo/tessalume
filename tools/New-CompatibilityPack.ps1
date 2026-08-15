[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version,

    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$MinimumAppVersion,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\compatibility')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceDirectory = Join-Path $repositoryRoot 'src\Tessalume.App\Compatibility'
$projectPath = Join-Path $repositoryRoot 'src\Tessalume.App\Tessalume.App.csproj'
$profilePath = Join-Path $sourceDirectory 'compatibility-profile-v3.json'
$bundlePath = Join-Path $sourceDirectory 'Runtime\runtime-bundle.json'
$runtimeBuilderPath = Join-Path $repositoryRoot 'tools\Build-CompatibilityRuntime.ps1'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$archivePath = Join-Path $outputRoot 'Tessalume-Compatibility.zip'
$checksumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
$staging = Join-Path $outputRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $runtimeBuilderPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $profilePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw 'Compatibility runtime sources are incomplete.'
}

if ([string]::IsNullOrWhiteSpace($MinimumAppVersion)) {
    $project = [xml](Get-Content -Raw -LiteralPath $projectPath)
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    $MinimumAppVersion = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
    if ([string]::IsNullOrWhiteSpace($MinimumAppVersion) -or
        $MinimumAppVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw 'The application project does not define a valid minimum version.'
    }
}

$profile = Get-Content -Raw -Encoding UTF8 -LiteralPath $profilePath | ConvertFrom-Json
if ($profile.schemaVersion -ne 1 -or $profile.runtimeContractVersion -ne 4) {
    throw 'Compatibility profile does not match runtime contract v4.'
}
$sourceProfileVersion = [string]$profile.profileVersion
if ([string]::IsNullOrWhiteSpace($sourceProfileVersion) -or
    $sourceProfileVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw 'The source compatibility profile does not define a valid profileVersion.'
}
if (-not [string]::Equals(
        $Version,
        $sourceProfileVersion,
        [StringComparison]::Ordinal)) {
    throw "Compatibility pack version '$Version' does not match source profileVersion '$sourceProfileVersion'. Update the source profile or pass -Version $sourceProfileVersion."
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $runtimeDestination = Join-Path $staging 'theme-runtime-v2.js'
    $profileDestination = Join-Path $staging 'compatibility-profile-v3.json'
    & $runtimeBuilderPath `
        -SourceDirectory $sourceDirectory `
        -Destination $runtimeDestination

    $profileJson = $profile | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $profileDestination,
        $profileJson + "`r`n",
        $utf8WithoutBom)

    $runtimeHash = Get-Sha256Hex -Path $runtimeDestination
    $profileHash = Get-Sha256Hex -Path $profileDestination
    $manifest = [ordered]@{
        schemaVersion = 1
        packVersion = $Version
        minimumAppVersion = $MinimumAppVersion
        runtimeContractVersion = 4
        runtime = 'theme-runtime-v2.js'
        profile = 'compatibility-profile-v3.json'
        files = [ordered]@{
            'theme-runtime-v2.js' = $runtimeHash
            'compatibility-profile-v3.json' = $profileHash
        }
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        (Join-Path $staging 'compatibility-pack.json'),
        $manifestJson + "`r`n",
        $utf8WithoutBom)

    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue

    # Compress-Archive copies the staging timestamps into the ZIP, so identical
    # sources produce a different release hash on every build. Write the three
    # contract files in a fixed order with a fixed timestamp instead. This keeps
    # compatibility releases reproducible and makes their SHA-256 auditable.
    $archiveStream = [System.IO.File]::Open(
        $archivePath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $fixedTimestamp = [DateTimeOffset]::new(
                2000,
                1,
                1,
                0,
                0,
                0,
                [TimeSpan]::Zero)
            foreach ($fileName in @(
                    'compatibility-pack.json',
                    'compatibility-profile-v3.json',
                    'theme-runtime-v2.js')) {
                $sourcePath = Join-Path $staging $fileName
                $entry = $archive.CreateEntry(
                    $fileName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $input = [System.IO.File]::OpenRead($sourcePath)
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }

    $archiveHash = Get-Sha256Hex -Path $archivePath
    [System.IO.File]::WriteAllText(
        $checksumPath,
        "$archiveHash *Tessalume-Compatibility.zip`r`n",
        [System.Text.Encoding]::ASCII)

    [pscustomobject]@{
        Version = $Version
        Archive = $archivePath
        Sha256 = $archiveHash
        Checksums = $checksumPath
        ReleaseTag = "compat-v$Version"
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
