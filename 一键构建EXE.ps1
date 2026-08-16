[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^win-(x64|x86|arm64)$')]
    [string]$Runtime = 'win-x64',

    [ValidateRange(1, 100)]
    [int]$ThemeImageQuality = 90,

    [switch]$NoLaunch,

    [switch]$FullValidation
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$globalJsonPath = Join-Path $root 'global.json'
$nugetConfig = Join-Path $root 'NuGet.Config'
$solution = Join-Path $root 'Tessalume.sln'
$project = Join-Path $root 'src\Tessalume.App\Tessalume.App.csproj'
$tests = Join-Path $root 'tests\Tessalume.Tests\Tessalume.Tests.csproj'

if (-not [IO.File]::Exists($globalJsonPath)) {
    throw "Missing SDK configuration: $globalJsonPath"
}
if (-not [IO.File]::Exists($project)) {
    throw "Missing application project: $project"
}
if (-not [IO.File]::Exists($nugetConfig)) {
    throw "Missing repository NuGet configuration: $nugetConfig"
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
$sourcePets = Join-Path $root 'pets'
$builtInPetPackageNames = @('flying-snowfluff')
$themeOptimizer = Join-Path $root 'tools\optimize-theme-assets.py'
$windowsTargetProperties = @(
    '-p:TargetPlatformDisplayName=Windows'
)

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

function Test-PetPathIsReparsePoint([string]$Path) {
    return (([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-PetRelativePath([string]$BasePath, [string]$Path) {
    $baseFullPath = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pathFullPath = [IO.Path]::GetFullPath($Path)
    $relativeUri = ([Uri]$baseFullPath).MakeRelativeUri([Uri]$pathFullPath)
    return [Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function Assert-SafeBuiltInPetTree([string]$Directory) {
    if (-not [IO.Directory]::Exists($Directory)) {
        throw "Missing built-in pet directory: $Directory"
    }
    if (Test-PetPathIsReparsePoint $Directory) {
        throw "Built-in pet release paths cannot be links or reparse points: $Directory"
    }

    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop) {
        if (Test-PetPathIsReparsePoint $entry.FullName) {
            throw "Built-in pet release paths cannot be links or reparse points: $($entry.FullName)"
        }
        if ($entry.PSIsContainer) {
            Assert-SafeBuiltInPetTree $entry.FullName
        }
    }
}

function Test-SafePetRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        [IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or
        $Path.Contains(':') -or
        $Path.StartsWith('/')) {
        return $false
    }
    $segments = @($Path.Split('/'))
    return $segments.Count -gt 0 -and
        @($segments | Where-Object { [string]::IsNullOrWhiteSpace($_) -or $_ -in @('.', '..') }).Count -eq 0
}

function Get-PetPngMetadata([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = [byte[]](0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
    $hasValidSignature = $bytes.Length -ge 26
    for ($index = 0; $hasValidSignature -and $index -lt $signature.Length; $index++) {
        $hasValidSignature = $bytes[$index] -eq $signature[$index]
    }
    if (-not $hasValidSignature) {
        throw "Built-in pet preview is not a valid PNG: $Path"
    }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return [pscustomobject]@{ Width = $width; Height = $height; ColorType = [int]$bytes[25] }
}

function Get-PetWebPMetadata([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream, [Text.Encoding]::ASCII, $true)
    try {
        if ($stream.Length -lt 20 -or
            [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -cne 'RIFF') {
            throw "Built-in pet spritesheet is not a RIFF WebP: $Path"
        }
        $declaredLength = [long]$reader.ReadUInt32() + 8
        if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -cne 'WEBP' -or
            $declaredLength -ne $stream.Length) {
            throw "Built-in pet spritesheet has an invalid WebP container length: $Path"
        }

        while ($stream.Position + 8 -le $declaredLength) {
            $chunkName = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
            $chunkSize = [long]$reader.ReadUInt32()
            $dataStart = $stream.Position
            $dataEnd = $dataStart + $chunkSize
            if ($dataEnd -gt $declaredLength) {
                throw "Built-in pet spritesheet contains an out-of-bounds WebP chunk: $Path"
            }
            if ($chunkName -ceq 'VP8L') {
                if ($chunkSize -lt 5 -or $reader.ReadByte() -ne 0x2f) {
                    throw "Built-in pet spritesheet has an invalid VP8L header: $Path"
                }
                $b1 = [int]$reader.ReadByte()
                $b2 = [int]$reader.ReadByte()
                $b3 = [int]$reader.ReadByte()
                $b4 = [int]$reader.ReadByte()
                return [pscustomobject]@{
                    Encoding = 'VP8L'
                    Width = 1 + $b1 + (($b2 -band 0x3f) -shl 8)
                    Height = 1 + (($b2 -band 0xc0) -shr 6) + ($b3 -shl 2) + (($b4 -band 0x0f) -shl 10)
                    HasAlpha = (($b4 -band 0x10) -ne 0)
                }
            }
            $stream.Position = $dataEnd + ($chunkSize -band 1)
        }
        throw "Built-in pet spritesheet must contain a VP8L image chunk: $Path"
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-BuiltInPetPackages([string]$PetsRoot, [string[]]$ExpectedPackageNames) {
    $PetsRoot = Assert-InProject $PetsRoot
    if ($ExpectedPackageNames.Count -eq 0 -or
        $ExpectedPackageNames.Count -ne @($ExpectedPackageNames | Select-Object -Unique).Count) {
        throw 'The built-in pet package allowlist must be explicit, unique, and non-empty.'
    }
    Assert-SafeBuiltInPetTree $PetsRoot

    $rootEntries = @(Get-ChildItem -LiteralPath $PetsRoot -Force -ErrorAction Stop)
    if (@($rootEntries | Where-Object { -not $_.PSIsContainer }).Count -ne 0) {
        throw 'The built-in pets root may contain only allowlisted package directories.'
    }
    $actualPackageNames = @($rootEntries | ForEach-Object { $_.Name } | Sort-Object -CaseSensitive)
    $expectedNames = @($ExpectedPackageNames | Sort-Object -CaseSensitive)
    if (($actualPackageNames -join "`n") -cne ($expectedNames -join "`n")) {
        throw "Built-in pet packages must exactly match the allowlist. Expected [$($expectedNames -join ', ')], found [$($actualPackageNames -join ', ')]."
    }

    $expectedStates = @(
        [pscustomobject]@{ Key = 'idle'; Row = 0; Frames = 7 },
        [pscustomobject]@{ Key = 'move-right'; Row = 1; Frames = 8 },
        [pscustomobject]@{ Key = 'move-left'; Row = 2; Frames = 8 },
        [pscustomobject]@{ Key = 'wave-touch'; Row = 3; Frames = 4 },
        [pscustomobject]@{ Key = 'jump'; Row = 4; Frames = 5 },
        [pscustomobject]@{ Key = 'blocked'; Row = 5; Frames = 8 },
        [pscustomobject]@{ Key = 'needs-input'; Row = 6; Frames = 6 },
        [pscustomobject]@{ Key = 'running'; Row = 7; Frames = 6 },
        [pscustomobject]@{ Key = 'ready'; Row = 8; Frames = 6 },
        [pscustomobject]@{ Key = 'gaze-upper'; Row = 9; Frames = 8 },
        [pscustomobject]@{ Key = 'gaze-lower'; Row = 10; Frames = 8 }
    )
    $expectedPreviews = @(
        [pscustomobject]@{ Path = 'previews/idle.png'; Kind = 'primary'; StateKey = 'idle'; Width = 192; Height = 208; ColorType = 6 },
        [pscustomobject]@{ Path = 'previews/running.png'; Kind = 'state'; StateKey = 'running'; Width = 192; Height = 208; ColorType = 6 },
        [pscustomobject]@{ Path = 'previews/needs-input.png'; Kind = 'state'; StateKey = 'needs-input'; Width = 192; Height = 208; ColorType = 6 },
        [pscustomobject]@{ Path = 'previews/ready.png'; Kind = 'state'; StateKey = 'ready'; Width = 192; Height = 208; ColorType = 6 },
        [pscustomobject]@{ Path = 'previews/blocked.png'; Kind = 'state'; StateKey = 'blocked'; Width = 192; Height = 208; ColorType = 6 },
        [pscustomobject]@{ Path = 'previews/showcase-grid.png'; Kind = 'showcase'; StateKey = 'showcase'; Width = 1152; Height = 1248; ColorType = 2 }
    )
    $packages = @()
    foreach ($expectedName in $expectedNames) {
        $package = Get-Item -LiteralPath (Join-Path $PetsRoot $expectedName) -Force
        if (-not $package.PSIsContainer -or $package.Name -cne $expectedName) {
            throw "Built-in pet package directory does not exactly match its allowlisted name: $expectedName"
        }
        $packages += $package

        $catalogPath = Join-Path $package.FullName 'catalog.json'
        $manifestPath = Join-Path $package.FullName 'pet.json'
        if (-not [IO.File]::Exists($catalogPath) -or -not [IO.File]::Exists($manifestPath)) {
            throw "Built-in pet package '$expectedName' must contain catalog.json and pet.json."
        }
        $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $catalogPath | ConvertFrom-Json
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
        $parsedVersion = $null
        if ([int]$catalog.schemaVersion -ne 1 -or
            [string]$catalog.id -cne $expectedName -or
            [string]$catalog.id -cne [string]$manifest.id -or
            [string]$catalog.displayName -cne [string]$manifest.displayName -or
            -not [Version]::TryParse([string]$catalog.productVersion, [ref]$parsedVersion)) {
            throw "Built-in pet package '$expectedName' has invalid catalog identity or product version metadata."
        }
        if ([string]::IsNullOrWhiteSpace([string]$catalog.author.name) -or
            [string]::IsNullOrWhiteSpace([string]$catalog.license.kind) -or
            [string]::IsNullOrWhiteSpace([string]$catalog.license.spdx) -or
            [string]::IsNullOrWhiteSpace([string]$catalog.license.name) -or
            [string]::IsNullOrWhiteSpace([string]$catalog.rights.kind) -or
            [string]::IsNullOrWhiteSpace([string]$catalog.rights.notice)) {
            throw "Built-in pet package '$expectedName' must retain non-empty author, license, and rights metadata."
        }

        $protocol = $catalog.protocol
        if ([int]$protocol.spriteVersionNumber -ne 2 -or
            [int]$protocol.atlasWidth -ne 1536 -or [int]$protocol.atlasHeight -ne 2288 -or
            [int]$protocol.columns -ne 8 -or [int]$protocol.rows -ne 11 -or
            [int]$protocol.cellWidth -ne 192 -or [int]$protocol.cellHeight -ne 208 -or
            [int]$protocol.usedFrameCount -ne 74) {
            throw "Built-in pet package '$expectedName' does not use the exact desktop atlas protocol."
        }
        $states = @($protocol.states)
        if ($states.Count -ne $expectedStates.Count) {
            throw "Built-in pet package '$expectedName' must declare all 11 protocol rows."
        }
        for ($index = 0; $index -lt $expectedStates.Count; $index++) {
            if ([string]$states[$index].key -cne $expectedStates[$index].Key -or
                [int]$states[$index].row -ne $expectedStates[$index].Row -or
                [int]$states[$index].frames -ne $expectedStates[$index].Frames) {
                throw "Built-in pet package '$expectedName' has an invalid protocol row at index $index."
            }
        }
        if (($states | Measure-Object -Property frames -Sum).Sum -ne 74 -or
            [int]$manifest.spriteVersionNumber -ne 2 -or
            [string]$manifest.spritesheetPath -cne 'spritesheet.webp') {
            throw "Built-in pet package '$expectedName' has inconsistent manifest or protocol totals."
        }

        $files = @($catalog.files)
        if ($files.Count -ne 8) {
            throw "Built-in pet package '$expectedName' must declare exactly two runtime files and six previews."
        }
        $filesByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
        $packagePrefix = $package.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in $files) {
            $relativePath = [string]$file.path
            $role = [string]$file.role
            if (-not (Test-SafePetRelativePath $relativePath) -or
                $filesByPath.ContainsKey($relativePath) -or
                $role -cnotin @('codex-manifest', 'codex-spritesheet', 'preview') -or
                [string]$file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
                [long]$file.size -le 0) {
                throw "Built-in pet package '$expectedName' has invalid or duplicate file metadata: '$relativePath'."
            }
            $filesByPath.Add($relativePath, $file)
            if ($role -ceq 'codex-manifest' -and
                ($relativePath -cne 'pet.json' -or [long]$file.size -gt 64KB) -or
                $role -ceq 'codex-spritesheet' -and
                ($relativePath -cne 'spritesheet.webp' -or [long]$file.size -gt 32MB) -or
                $role -ceq 'preview' -and
                ($relativePath -cnotin $expectedPreviews.Path -or [long]$file.size -gt 2MB)) {
                throw "Built-in pet package '$expectedName' violates its file-role boundary: '$relativePath'."
            }

            $assetPath = [IO.Path]::GetFullPath((Join-Path $package.FullName ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)))
            if (-not $assetPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not [IO.File]::Exists($assetPath) -or
                (Test-PetPathIsReparsePoint $assetPath)) {
                throw "Built-in pet package '$expectedName' is missing or escapes through: '$relativePath'."
            }
            $asset = Get-Item -LiteralPath $assetPath -Force
            $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($asset.Length -ne [long]$file.size -or $actualHash -cne [string]$file.sha256) {
                throw "Built-in pet package '$expectedName' failed size or SHA-256 validation: '$relativePath'."
            }
        }

        $manifestFiles = @($files | Where-Object { [string]$_.role -ceq 'codex-manifest' })
        $spritesheetFiles = @($files | Where-Object { [string]$_.role -ceq 'codex-spritesheet' })
        $previewFiles = @($files | Where-Object { [string]$_.role -ceq 'preview' })
        if ($manifestFiles.Count -ne 1 -or [string]$manifestFiles[0].path -cne 'pet.json' -or
            $spritesheetFiles.Count -ne 1 -or [string]$spritesheetFiles[0].path -cne 'spritesheet.webp' -or
            $previewFiles.Count -ne $expectedPreviews.Count) {
            throw "Built-in pet package '$expectedName' must have one manifest, one spritesheet, and six bounded previews."
        }

        $previews = @($catalog.previews)
        if ($previews.Count -ne $expectedPreviews.Count) {
            throw "Built-in pet package '$expectedName' must declare five state previews and one product preview."
        }
        $seenPreviews = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($expectedPreview in $expectedPreviews) {
            $matches = @($previews | Where-Object { [string]$_.path -ceq $expectedPreview.Path })
            if ($matches.Count -ne 1 -or
                -not $seenPreviews.Add([string]$matches[0].path) -or
                [string]$matches[0].kind -cne $expectedPreview.Kind -or
                [string]$matches[0].stateKey -cne $expectedPreview.StateKey -or
                [string]::IsNullOrWhiteSpace([string]$matches[0].label) -or
                -not $filesByPath.ContainsKey($expectedPreview.Path) -or
                [string]$filesByPath[$expectedPreview.Path].role -cne 'preview') {
                throw "Built-in pet package '$expectedName' has invalid preview metadata: '$($expectedPreview.Path)'."
            }
            $png = Get-PetPngMetadata (Join-Path $package.FullName ($expectedPreview.Path -replace '/', [IO.Path]::DirectorySeparatorChar))
            if ($png.Width -ne $expectedPreview.Width -or $png.Height -ne $expectedPreview.Height -or
                $png.ColorType -ne $expectedPreview.ColorType) {
                throw "Built-in pet package '$expectedName' has an invalid preview image boundary: '$($expectedPreview.Path)'."
            }
        }

        $webp = Get-PetWebPMetadata (Join-Path $package.FullName 'spritesheet.webp')
        if ($webp.Encoding -cne 'VP8L' -or $webp.Width -ne 1536 -or $webp.Height -ne 2288 -or
            -not $webp.HasAlpha) {
            throw "Built-in pet package '$expectedName' has an invalid runtime spritesheet."
        }

        $actualFiles = @(
            Get-ChildItem -LiteralPath $package.FullName -File -Recurse -Force |
                ForEach-Object { (Get-PetRelativePath $package.FullName $_.FullName).Replace('\', '/') } |
                Sort-Object -CaseSensitive
        )
        $expectedFiles = @('catalog.json') + @($filesByPath.Keys)
        $expectedFiles = @($expectedFiles | Sort-Object -CaseSensitive)
        if (($actualFiles -join "`n") -cne ($expectedFiles -join "`n")) {
            throw "Built-in pet package '$expectedName' contains undeclared or missing release files."
        }
        if (@($catalog.recommendedThemeIds).Count -ne 1 -or
            [string]$catalog.recommendedThemeIds[0] -cne 'aemeath.star-voyage') {
            throw "Built-in pet package '$expectedName' must retain its exact paired theme metadata."
        }
    }

    return $packages
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
$petPackages = @(Assert-BuiltInPetPackages $sourcePets $builtInPetPackageNames)

Write-Host '1/5 Restoring source dependencies...' -ForegroundColor Cyan
& $dotnet restore $solution --configfile $nugetConfig --ignore-failed-sources $windowsTargetProperties
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testProfile = if ($FullValidation) { '--full' } else { '--build' }
$testProfileLabel = if ($FullValidation) { 'full release validation' } else { 'core build' }
Write-Host "2/5 Building and running $testProfileLabel checks..." -ForegroundColor Cyan
& $dotnet build $solution --configuration $Configuration --no-restore $windowsTargetProperties
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousTessalumeTargetPlatformMoniker = $env:TargetPlatformMoniker
$previousTessalumeTargetPlatformDisplayName = $env:TargetPlatformDisplayName
$testExitCode = 0
try {
    $env:TargetPlatformMoniker = 'Windows,Version=7.0'
    $env:TargetPlatformDisplayName = 'Windows'
    & $dotnet run --project $tests --configuration $Configuration --no-build -- $testProfile
    $testExitCode = $LASTEXITCODE
}
finally {
    if ($null -eq $previousTessalumeTargetPlatformMoniker) {
        Remove-Item Env:TargetPlatformMoniker -ErrorAction SilentlyContinue
    }
    else {
        $env:TargetPlatformMoniker = $previousTessalumeTargetPlatformMoniker
    }
    if ($null -eq $previousTessalumeTargetPlatformDisplayName) {
        Remove-Item Env:TargetPlatformDisplayName -ErrorAction SilentlyContinue
    }
    else {
        $env:TargetPlatformDisplayName = $previousTessalumeTargetPlatformDisplayName
    }
}
if ($testExitCode -ne 0) { exit $testExitCode }

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
& $dotnet restore $project --runtime $Runtime --configfile $nugetConfig --ignore-failed-sources $windowsTargetProperties
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
    $windowsTargetProperties `
    "-p:BuiltInThemesRoot=$optimizedThemes" `
    "-p:BuiltInPetsRoot=$sourcePets" `
    -p:SatelliteResourceLanguages=zh-Hans
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedExe = Join-Path $staging $executableName
if (-not [IO.File]::Exists($publishedExe)) {
    throw "Publish completed without $executableName."
}

$stagedPets = Join-Path $staging 'pets'
[IO.Directory]::CreateDirectory($stagedPets) | Out-Null
foreach ($package in $petPackages) {
    Copy-Item -LiteralPath $package.FullName -Destination $stagedPets -Recurse
}

foreach ($sourceFile in Get-ChildItem -LiteralPath $sourcePets -File -Recurse) {
    $relativePath = Get-PetRelativePath $sourcePets $sourceFile.FullName
    $publishedPath = Join-Path $stagedPets $relativePath
    if (-not [IO.File]::Exists($publishedPath) -or
        $sourceFile.Length -ne (Get-Item -LiteralPath $publishedPath).Length -or
        (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $publishedPath -Algorithm SHA256).Hash) {
        throw "Published pet asset does not match its source: $relativePath"
    }
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

# Themes and pets are embedded in the single-file executable. Pet packages are
# also staged in the portable output so their release hashes can be audited
# without launching or writing to the user's real Codex pet directory.

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
$checksumPath = Join-Path $output 'SHA256SUMS.txt'
$checksum = (Get-FileHash -LiteralPath $finalExe -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$checksum *$executableName$([Environment]::NewLine)"
[IO.File]::WriteAllText($checksumPath, $checksumLine, [Text.UTF8Encoding]::new($false))
Write-Host "Build complete: $finalExe ($size MB)" -ForegroundColor Green
Write-Host "Checksum: $checksumPath" -ForegroundColor Green
Write-Host 'Built-in themes are embedded and will be extracted on first launch.' -ForegroundColor Green
Write-Host 'Built-in pet packages are embedded and verified in the portable pets directory.' -ForegroundColor Green

if (-not $NoLaunch) {
    Write-Host "Launching: $finalExe" -ForegroundColor Cyan
    Start-Process -FilePath $finalExe -WorkingDirectory $output
}
