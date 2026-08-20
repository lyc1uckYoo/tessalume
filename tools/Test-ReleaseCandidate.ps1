[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'Tessalume.sln'
$buildScript = Get-ChildItem -LiteralPath $repositoryRoot -File -Filter '*EXE.ps1' |
    Select-Object -First 1 -ExpandProperty FullName
$runtimeBuilder = Join-Path $PSScriptRoot 'Build-CompatibilityRuntime.ps1'
$compatibilityBuilder = Join-Path $PSScriptRoot 'New-CompatibilityPack.ps1'
$validationRoot = Join-Path $repositoryRoot 'artifacts\release-candidate'
$runtimePath = Join-Path $validationRoot 'theme-runtime-v2.js'
$compatibilityOutput = Join-Path $validationRoot 'compatibility'
$portableRoot = Join-Path $repositoryRoot 'dist\portable-win-x64'
$executablePath = Join-Path $portableRoot 'Tessalume.exe'
$checksumPath = Join-Path $portableRoot 'SHA256SUMS.txt'

Push-Location $repositoryRoot
try {
    if ([string]::IsNullOrWhiteSpace($buildScript) -or -not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
        throw 'The one-click build script is missing.'
    }
    dotnet restore $solutionPath --ignore-failed-sources --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet format $solutionPath --verify-no-changes --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Source formatting verification failed.' }

    if ($SkipPublish) {
        dotnet build $solutionPath --configuration Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
        dotnet run --project tests\Tessalume.Tests\Tessalume.Tests.csproj --configuration Release --no-build -- --full
        if ($LASTEXITCODE -ne 0) { throw 'Regression suite failed.' }
    }
    else {
        & $buildScript -Configuration Release -Runtime win-x64 -NoLaunch -FullValidation
        if ($LASTEXITCODE -ne 0) { throw 'Complete release build failed.' }
    }

    $runtimeManifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\Tessalume.App\Compatibility\Runtime\runtime-bundle.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json
    foreach ($fragmentName in @($runtimeManifest.fragments)) {
        $fragmentPath = Join-Path $repositoryRoot "src\Tessalume.App\Compatibility\Runtime\$fragmentName"
        node --check $fragmentPath
        if ($LASTEXITCODE -ne 0) { throw "Compatibility runtime fragment syntax is invalid: $fragmentName" }
    }

    & $runtimeBuilder -Destination $runtimePath
    node --check $runtimePath
    if ($LASTEXITCODE -ne 0) { throw 'Assembled compatibility runtime syntax is invalid.' }
    $compatibility = & $compatibilityBuilder -Version 3.0.6 -OutputDirectory $compatibilityOutput

    if (-not $SkipPublish) {
        if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
            throw 'The portable release output is incomplete.'
        }
        $hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        $manifest = Get-Content -LiteralPath $checksumPath -Raw -Encoding UTF8
        if ($manifest.IndexOf($hash, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw 'The portable SHA-256 manifest does not match Tessalume.exe.'
        }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).FileVersion
        [pscustomobject]@{
            Version = $version
            Executable = $executablePath
            Size = (Get-Item -LiteralPath $executablePath).Length
            Sha256 = $hash
            CompatibilityArchive = $compatibility.Archive
            CompatibilitySha256 = $compatibility.Sha256
        }
    }
}
finally {
    Pop-Location
}
