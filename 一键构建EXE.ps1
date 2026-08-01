[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^win-(x64|x86|arm64)$')]
    [string]$Runtime = 'win-x64',

    [ValidateRange(1, 100)]
    [int]$ThemeImageQuality = 90
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$globalJsonPath = Join-Path $root 'global.json'
$solution = Join-Path $root 'Tessalume.sln'
$project = Join-Path $root 'src\Tessalume.App\Tessalume.App.csproj'
$tests = Join-Path $root 'tests\Tessalume.Core.Tests\Tessalume.Core.Tests.csproj'

if (-not [IO.File]::Exists($globalJsonPath)) {
    throw "Missing SDK configuration: $globalJsonPath"
}
if (-not [IO.File]::Exists($project)) {
    throw "Missing application project: $project"
}

$sdkConfiguration = Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json
$sdkVersion = [string]$sdkConfiguration.sdk.version
if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw "global.json does not define sdk.version."
}

$projectDocument = [xml](Get-Content -Raw -LiteralPath $project)
$assemblyNameNode = $projectDocument.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
$assemblyName = if ($assemblyNameNode) { $assemblyNameNode.InnerText.Trim() } else { [IO.Path]::GetFileNameWithoutExtension($project) }
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
    throw "The application project does not define a usable assembly name."
}

$executableName = "$assemblyName.exe"
$safeAssemblyName = ($assemblyName -replace '[^A-Za-z0-9._-]', '-').ToLowerInvariant()
$sdkArchitecture = ($Runtime -split '-')[-1]
$cacheRoot = Join-Path $env:LOCALAPPDATA 'dotnet-sdk-cache'
$dotnetRoot = Join-Path $cacheRoot "$sdkVersion-$sdkArchitecture"
$cachedDotnet = Join-Path $dotnetRoot 'dotnet.exe'
$dotnet = $null
$distRoot = Join-Path $root 'dist'
$output = Join-Path $distRoot "portable-$Runtime"
$staging = Join-Path $distRoot ".publish-$Runtime"
$replacementBackup = Join-Path $distRoot ".previous-portable-$Runtime"
$sourceThemes = Join-Path $root 'themes'
$optimizedThemes = Join-Path $root 'optimized-themes'
$themeOptimizer = Join-Path $root 'tools\optimize-theme-assets.py'

function Test-MatchingDotnet([string]$Path) {
    if (-not [IO.File]::Exists($Path)) {
        return $false
    }

    try {
        return (& $Path --version 2>$null) -eq $sdkVersion
    }
    catch {
        return $false
    }
}

$dotnetCandidates = [Collections.Generic.List[string]]::new()
$dotnetCandidates.Add($cachedDotnet)
if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
    $dotnetCandidates.Add((Join-Path $env:DOTNET_ROOT 'dotnet.exe'))
}
$systemDotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if ($systemDotnet) {
    $dotnetCandidates.Add($systemDotnet)
}
Get-ChildItem -LiteralPath $env:LOCALAPPDATA -Directory -Filter '*Build' -ErrorAction SilentlyContinue |
    ForEach-Object { $dotnetCandidates.Add((Join-Path $_.FullName 'dotnet\dotnet.exe')) }

foreach ($candidate in $dotnetCandidates | Select-Object -Unique) {
    if (Test-MatchingDotnet $candidate) {
        $dotnet = $candidate
        break
    }
}

function Assert-InProject([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the project: $fullPath"
    }
    return $fullPath
}

function Remove-GeneratedDirectory([string]$Path) {
    $fullPath = Assert-InProject $Path
    if ([IO.Directory]::Exists($fullPath)) {
        [IO.Directory]::Delete($fullPath, $true)
    }
}

function Move-GeneratedDirectory([string]$Source, [string]$Destination) {
    $sourcePath = Assert-InProject $Source
    $destinationPath = Assert-InProject $Destination
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            [IO.Directory]::Move($sourcePath, $destinationPath)
            return
        }
        catch [IO.IOException], [UnauthorizedAccessException] {
            if ($attempt -eq 20) {
                throw
            }

            Start-Sleep -Milliseconds 300
        }
    }
}

if (-not $dotnet) {
    Write-Host "First build: preparing .NET SDK $sdkVersion (downloaded once)..." -ForegroundColor Cyan
    [IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
    $installer = Join-Path ([IO.Path]::GetTempPath()) "$safeAssemblyName-dotnet-install-$PID.ps1"
    try {
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installer `
            -Version $sdkVersion -Architecture $sdkArchitecture -InstallDir $dotnetRoot -NoPath
        if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists($cachedDotnet)) {
            throw 'Portable .NET SDK installation failed. Check the network and try again.'
        }
    }
    finally {
        if ([IO.File]::Exists($installer)) {
            [IO.File]::Delete($installer)
        }
    }

    $dotnet = $cachedDotnet
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
[IO.Directory]::CreateDirectory($distRoot) | Out-Null
Remove-GeneratedDirectory $staging
Remove-GeneratedDirectory $replacementBackup

Write-Host '1/5 Restoring source dependencies...' -ForegroundColor Cyan
& $dotnet restore $solution --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '2/5 Building and running all checks...' -ForegroundColor Cyan
& $dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet run --project $tests --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '3/5 Updating the incremental optimized theme cache...' -ForegroundColor Cyan
$localThemeManifests = if ([IO.Directory]::Exists($sourceThemes)) {
    @(Get-ChildItem -LiteralPath $sourceThemes -Directory -ErrorAction SilentlyContinue |
        Where-Object { [IO.File]::Exists((Join-Path $_.FullName 'manifest.json')) })
}
else {
    @()
}

if ($localThemeManifests.Count -gt 0) {
    $python = (Get-Command python.exe -ErrorAction SilentlyContinue).Source
    if (-not $python) {
        throw 'Python 3 is required to optimize built-in theme images during publishing.'
    }

    & $python -c 'import PIL'
    if ($LASTEXITCODE -ne 0) {
        throw 'Python Pillow is required to optimize built-in theme images. Run: python -m pip install Pillow'
    }

    & $python $themeOptimizer $sourceThemes --output $optimizedThemes --quality $ThemeImageQuality
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Only direct children of themes/ are publishable packages. Verify the
    # generated library before embedding it so historical or nested manifests
    # can never silently enter the executable.
    $sourceThemeNames = @($localThemeManifests | ForEach-Object { $_.Name } | Sort-Object)
    $optimizedThemeManifests = @(
        Get-ChildItem -LiteralPath $optimizedThemes -Filter 'manifest.json' -File -Recurse
    )
    $optimizedRootManifests = @(
        Get-ChildItem -LiteralPath $optimizedThemes -Directory -ErrorAction SilentlyContinue |
            Where-Object { [IO.File]::Exists((Join-Path $_.FullName 'manifest.json')) }
    )
    $nestedManifests = @(
        $optimizedThemeManifests |
            Where-Object { $_.Directory.Parent.FullName -ne $optimizedThemes }
    )
    $optimizedThemeNames = @($optimizedRootManifests | ForEach-Object { $_.Name } | Sort-Object)
    $themeSetDifference = @(
        Compare-Object -ReferenceObject $sourceThemeNames -DifferenceObject $optimizedThemeNames
    )

    if ($nestedManifests.Count -gt 0 -or $themeSetDifference.Count -gt 0) {
        $nestedList = @($nestedManifests | ForEach-Object { $_.FullName }) -join ', '
        $differenceList = @($themeSetDifference | ForEach-Object { "$($_.InputObject) $($_.SideIndicator)" }) -join ', '
        throw "Optimized theme library does not match direct source packages. Nested: [$nestedList]. Difference: [$differenceList]."
    }
}
else {
    # Public clones intentionally contain no private/local theme packages.
    # Keep an empty generated root so the application still publishes cleanly.
    Remove-GeneratedDirectory $optimizedThemes
    [IO.Directory]::CreateDirectory($optimizedThemes) | Out-Null
    Write-Host 'No local theme packages found; publishing without built-in themes.' -ForegroundColor DarkGray
}

Write-Host '4/5 Publishing a self-contained single-file EXE...' -ForegroundColor Cyan
& $dotnet restore $project --runtime $Runtime --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $staging `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "-p:BuiltInThemesRoot=$optimizedThemes" `
    -p:SatelliteResourceLanguages=zh-Hans
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedExe = Join-Path $staging $executableName
if (-not [IO.File]::Exists($publishedExe)) {
    throw "Publish completed without $executableName."
}

Write-Host '5/5 Replacing dist and removing generated files...' -ForegroundColor Cyan
Get-Process -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.Path -and $_.Path.StartsWith($output, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    } |
    ForEach-Object {
        $null = $_.CloseMainWindow()
        $null = $_.WaitForExit(5000)
        if (-not $_.HasExited) {
            $_.Kill()
            $null = $_.WaitForExit(5000)
        }
        if (-not $_.HasExited) {
            throw "A previous application build could not be stopped from $output. Close it and build again."
        }
    }

# Themes are embedded in the single-file executable. The application creates
# its portable folders and extracts built-in themes only on its first launch.

# Keep the previous release intact until the new directory has been moved into
# place. Antivirus and Explorer can briefly lock a newly published executable;
# deleting the old release first would otherwise leave no runnable build.
$hadPreviousOutput = [IO.Directory]::Exists($output)
if ($hadPreviousOutput) {
    Move-GeneratedDirectory $output $replacementBackup
}

try {
    Move-GeneratedDirectory $staging $output
}
catch {
    if ($hadPreviousOutput -and -not [IO.Directory]::Exists($output) -and [IO.Directory]::Exists($replacementBackup)) {
        Move-GeneratedDirectory $replacementBackup $output
    }

    throw
}

if ([IO.Directory]::Exists($replacementBackup)) {
    Remove-GeneratedDirectory $replacementBackup
}

foreach ($sourceRoot in @((Join-Path $root 'src'), (Join-Path $root 'tests'))) {
    Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { Remove-GeneratedDirectory $_.FullName }
}
Remove-GeneratedDirectory (Join-Path $root '.test-output')

$finalExe = Join-Path $output $executableName
$size = [Math]::Round((Get-Item -LiteralPath $finalExe).Length / 1MB, 1)
Write-Host "Build complete: $finalExe ($size MB)" -ForegroundColor Green
Write-Host 'Built-in themes are embedded and will be extracted on first launch.' -ForegroundColor Green
