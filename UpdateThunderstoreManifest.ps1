# Updates Thunderstore package manifest version_number.
# Called from MSBuild after package files are prepared and before the archive is created.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest file was not found: $ManifestPath"
}

$RawVersion = $Version.Trim()
$ParsedVersion = $null
if (-not [System.Version]::TryParse($RawVersion, [ref]$ParsedVersion)) {
    throw "Invalid assembly version: $Version"
}

if ($ParsedVersion.Major -lt 0 -or $ParsedVersion.Minor -lt 0 -or $ParsedVersion.Build -lt 0) {
    throw "Thunderstore version must contain major, minor and patch components: $Version"
}

$PackageVersion = "{0}.{1}.{2}" -f $ParsedVersion.Major, $ParsedVersion.Minor, $ParsedVersion.Build
$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$ManifestText = [System.IO.File]::ReadAllText($ManifestPath)
$Manifest = $ManifestText | ConvertFrom-Json
$OldVersion = [string]$Manifest.version_number
if ($OldVersion -eq $PackageVersion) {
    Write-Host "Thunderstore manifest version is already $PackageVersion"
    exit 0
}

$Manifest.version_number = $PackageVersion
$Json = $Manifest | ConvertTo-Json -Depth 20
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$TemporaryPath = $ManifestPath + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
try {
    [System.IO.File]::WriteAllText($TemporaryPath, $Json + [Environment]::NewLine, $Utf8NoBom)

    # File.Replace is not supported by every filesystem used for source trees and can
    # fail even though a normal overwrite is valid. Copy the completed temporary file
    # over the manifest instead; the temporary file still prevents partial JSON output.
    [System.IO.File]::Copy($TemporaryPath, $ManifestPath, $true)
}
finally {
    if ([System.IO.File]::Exists($TemporaryPath)) {
        [System.IO.File]::Delete($TemporaryPath)
    }
}
Write-Host "Thunderstore manifest version updated: $OldVersion -> $PackageVersion"
