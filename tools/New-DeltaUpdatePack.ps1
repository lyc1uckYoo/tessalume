[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BasisExecutable,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+\.\d+\.\d+$')]
    [string]$BasisVersion,

    [Parameter(Mandatory = $true)]
    [string]$TargetExecutable,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+\.\d+\.\d+$')]
    [string]$TargetVersion,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $PSScriptRoot 'Tessalume.UpdatePack\Tessalume.UpdatePack.csproj'
$toolAssembly = Join-Path $PSScriptRoot 'Tessalume.UpdatePack\bin\Release\net8.0\Tessalume.UpdatePack.dll'
$basisPath = [IO.Path]::GetFullPath($BasisExecutable)
$targetPath = [IO.Path]::GetFullPath($TargetExecutable)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)

foreach ($path in @($basisPath, $targetPath)) {
    if (-not [IO.File]::Exists($path)) {
        throw "Incremental update input is missing: $path"
    }
}
if (-not $outputPath.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Incremental update output must stay inside the repository: $outputPath"
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists($toolAssembly)) {
    throw 'Incremental update tool build failed.'
}

dotnet $toolAssembly `
    $basisPath $BasisVersion $targetPath $TargetVersion $outputPath
if ($LASTEXITCODE -ne 0) {
    throw 'Incremental update package generation failed.'
}
