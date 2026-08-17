[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version,

    [string]$ChangelogPath,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path $PSScriptRoot '..\CHANGELOG.md'
}
$changelog = [IO.Path]::GetFullPath($ChangelogPath)
if (-not [IO.File]::Exists($changelog)) {
    throw "Changelog not found: $changelog"
}

$content = [IO.File]::ReadAllText($changelog)
$escapedVersion = [Regex]::Escape($Version)
$match = [Regex]::Match(
    $content,
    "(?ms)^##\s+$escapedVersion\s*\r?\n(?<body>.*?)(?=^##\s+|\z)")
if (-not $match.Success) {
    throw "CHANGELOG.md does not contain a section for $Version."
}

$notes = $match.Groups['body'].Value.Trim()
$emptyPlaceholder = [string]::Concat([char]0x6682, [char]0x65E0)
$emptyPlaceholderPattern = '^[-*]\s*' + [Regex]::Escape($emptyPlaceholder) + '(?:\u3002|\.)?$'
if ([string]::IsNullOrWhiteSpace($notes) -or $notes -match $emptyPlaceholderPattern) {
    throw "CHANGELOG.md section $Version does not contain release notes."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $notes
    return
}

$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText(
    $destination,
    $notes + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
$destination
