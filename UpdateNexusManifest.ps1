# Updates Nexus sidecar manifest before publishing.
# Called from MSBuild after package files are prepared. Only description remains manual per release.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Name = "",
    [int]$DescriptionMaxLength = 255
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$ProjectRoot = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $ProjectRoot
$CommonScript = Join-Path $RepositoryRoot "API\CommonPublish.ps1"
. $CommonScript

Assert-FileExists -Path $ManifestPath
$PackageVersion = Normalize-PackageVersion -Version $Version -Name "Nexus file version"

$Manifest = Get-JsonFile -Path $ManifestPath

$OldVersion = [string]$Manifest.version
$Manifest.version = $PackageVersion

if (-not [string]::IsNullOrWhiteSpace($Name)) {
    $OldName = [string]$Manifest.name
    $Manifest.name = $Name
}
else {
    $OldName = [string]$Manifest.name
}

$Description = ""
if ($null -ne $Manifest.description) {
    $Description = [string]$Manifest.description
}
Assert-MaxLength -Value $Description -Name "Nexus file description" -MaxLength $DescriptionMaxLength

Save-JsonFile -Object $Manifest -Path $ManifestPath

if ($OldVersion -eq $PackageVersion) {
    Write-Host "Nexus manifest version is already $PackageVersion"
}
else {
    Write-Host "Nexus manifest version updated: $OldVersion -> $PackageVersion"
}

if (-not [string]::IsNullOrWhiteSpace($Name) -and $OldName -ne $Name) {
    Write-Host "Nexus manifest name updated: $OldName -> $Name"
}

Write-Host "Nexus description length: $($Description.Length)/$DescriptionMaxLength"
