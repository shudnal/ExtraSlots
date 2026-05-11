# Updates Thunderstore package manifest version_number.
# Called from MSBuild after package files are prepared and before ZipThunderstore.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$ProjectRoot = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $ProjectRoot
$CommonScript = Join-Path $RepositoryRoot "API\CommonPublish.ps1"
. $CommonScript

Assert-FileExists -Path $ManifestPath
$PackageVersion = Normalize-PackageVersion -Version $Version -Name "Thunderstore version_number"

$Manifest = Get-JsonFile -Path $ManifestPath
$OldVersion = [string]$Manifest.version_number
$Manifest.version_number = $PackageVersion
Save-JsonFile -Object $Manifest -Path $ManifestPath

if ($OldVersion -eq $PackageVersion) {
    Write-Host "Thunderstore manifest version is already $PackageVersion"
}
else {
    Write-Host "Thunderstore manifest version updated: $OldVersion -> $PackageVersion"
}
