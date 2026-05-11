# Manual Nexus Mods upload script.

param(
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

# ---- Mod-specific values -----------------------------------------------------
$ModName = "ExtraSlots"

# Nexus game domain from the URL, for Valheim it is always "valheim".
# Example: https://www.nexusmods.com/valheim/mods/1234
$GameDomain = "valheim"

# Nexus mod id from the mod page URL.
# Example: https://www.nexusmods.com/valheim/mods/1234 -> 1234
$GameScopedModId = "2901"

# Optional. Nexus file update group name.
$FileGroupName = "ExtraSlots.zip"

# Optional escape hatch. Leave empty for automatic lookup.
# If Nexus changes the API or lookup is ambiguous, set the group id here.
$FileGroupIdOverride = ""

# If uploaded version is newer than current primary/main Nexus version,
# mark the uploaded file as the primary mod-manager download.
$SetUploadedFileAsPrimaryModManagerDownloadWhenNewer = $true

$NexusApiBase = "https://api.nexusmods.com/v3"
$NexusApiKeyFileName = "nexus-api-key.txt"
$NexusDescriptionMaxLength = 255
$NexusApplicationName = "shudnal-valheim-publish"
$NexusApplicationVersion = "1.0.0"

# Nexus presigned R2 upload for this flow expects application/octet-stream.
$NexusUploadContentType = "application/octet-stream"
# -----------------------------------------------------------------------------

$ProjectRoot = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $ProjectRoot
$ApiDir = Join-Path $RepositoryRoot "API"
$CommonScript = Join-Path $ApiDir "CommonPublish.ps1"

. $CommonScript

Initialize-CurlProxyFromApiDirectory -ApiDir $ApiDir

function Get-NexusInternalModId {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$GameDomain,
        [Parameter(Mandatory = $true)][string]$GameScopedModId
    )

    if ([string]::IsNullOrWhiteSpace($GameDomain)) {
        throw "Set `$GameDomain in PublishNexus.ps1. For Valheim use 'valheim'."
    }

    if ([string]::IsNullOrWhiteSpace($GameScopedModId) -or $GameScopedModId -eq "PUT_NEXUS_MOD_ID_FROM_URL_HERE") {
        throw "Set `$GameScopedModId in PublishNexus.ps1. Example: https://www.nexusmods.com/valheim/mods/1234 -> 1234."
    }

    Write-Host "Resolving Nexus internal mod id..."
    $response = Invoke-CurlJson `
        -Method GET `
        -Url "$BaseUrl/games/$GameDomain/mods/$GameScopedModId" `
        -Headers $Headers

    if ($null -eq $response.Json -or $null -eq $response.Json.data) {
        throw "Nexus did not return mod data. Response: $($response.Body)"
    }

    $internalModId = [string]$response.Json.data.id
    if ([string]::IsNullOrWhiteSpace($internalModId)) {
        throw "Nexus mod response does not contain data.id. Response: $($response.Body)"
    }

    return $internalModId
}

function Format-NexusFileGroupList {
    param([Parameter(Mandatory = $true)][object[]]$Groups)

    if ($Groups.Count -eq 0) {
        return "<no groups>"
    }

    return (($Groups | ForEach-Object {
        $id = [string]$_.id
        $name = [string]$_.name
        $isActive = [string]$_.is_active
        $versionsCount = [string]$_.versions_count
        $lastUploadedAt = [string]$_.last_file_uploaded_at
        "  $id | '$name' | active=$isActive | versions=$versionsCount | last=$lastUploadedAt"
    }) -join [Environment]::NewLine)
}

function ConvertTo-VersionParts {
    param([Parameter(Mandatory = $true)][string]$Version)

    $normalized = Normalize-PackageVersion -Version $Version -Name "version"
    $parts = $normalized.Split(".")

    return @(
        [int]$parts[0],
        [int]$parts[1],
        [int]$parts[2]
    )
}

function Compare-PackageVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    $leftParts = ConvertTo-VersionParts -Version $Left
    $rightParts = ConvertTo-VersionParts -Version $Right

    for ($i = 0; $i -lt 3; $i++) {
        if ($leftParts[$i] -gt $rightParts[$i]) {
            return 1
        }

        if ($leftParts[$i] -lt $rightParts[$i]) {
            return -1
        }
    }

    return 0
}

function Get-NexusFileGroupId {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$InternalModId,
        [Parameter(Mandatory = $true)][string]$RequestedGroupName
    )

    Write-Host "Resolving Nexus file update group id..."
    $response = Invoke-CurlJson `
        -Method GET `
        -Url "$BaseUrl/mods/$InternalModId/file-update-groups" `
        -Headers $Headers

    if ($null -eq $response.Json -or $null -eq $response.Json.data) {
        throw "Nexus did not return file update group data. Response: $($response.Body)"
    }

    $groups = @($response.Json.data.groups)
    if ($groups.Count -eq 0) {
        throw "Nexus file update groups were not found for internal mod id '$InternalModId'. Upload the first file manually or add a separate create-file flow."
    }

    $activeGroups = @($groups | Where-Object { $true -eq [bool]$_.is_active })
    $inactiveExactGroups = @($groups | Where-Object {
        ([string]$_.name).Equals($RequestedGroupName, [System.StringComparison]::OrdinalIgnoreCase) -and -not ([bool]$_.is_active)
    })

    $exactActiveGroups = @($activeGroups | Where-Object {
        ([string]$_.name).Equals($RequestedGroupName, [System.StringComparison]::OrdinalIgnoreCase)
    })

    if ($exactActiveGroups.Count -eq 1) {
        return [string]$exactActiveGroups[0].id
    }

    if ($exactActiveGroups.Count -gt 1) {
        throw "More than one active Nexus file update group matched '$RequestedGroupName':$([Environment]::NewLine)$(Format-NexusFileGroupList -Groups $exactActiveGroups)"
    }

    if ($inactiveExactGroups.Count -gt 0) {
        throw "Nexus file update group '$RequestedGroupName' exists, but it is not active:$([Environment]::NewLine)$(Format-NexusFileGroupList -Groups $inactiveExactGroups)"
    }

    if ($activeGroups.Count -eq 1) {
        Write-Warning "File update group '$RequestedGroupName' was not found, but there is exactly one active group. Using it: '$($activeGroups[0].name)'."
        return [string]$activeGroups[0].id
    }

    throw "Nexus file update group '$RequestedGroupName' was not found or selection is ambiguous. Set `$FileGroupName to one of available active group names, or set `$FileGroupIdOverride manually.$([Environment]::NewLine)Available groups:$([Environment]::NewLine)$(Format-NexusFileGroupList -Groups $groups)"
}

function Get-NexusCurrentPrimaryVersion {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$FileGroupId
    )

    $response = Invoke-CurlJson `
        -Method GET `
        -Url "$BaseUrl/file-update-groups/$FileGroupId/versions" `
        -Headers $Headers

    if ($null -eq $response.Json -or $null -eq $response.Json.data) {
        throw "Nexus did not return file update group versions. Response: $($response.Body)"
    }

    $versions = @($response.Json.data.versions)
    if ($versions.Count -eq 0) {
        return ""
    }

    $primaryVersions = @($versions | Where-Object {
        $null -ne $_.file -and $true -eq [bool]$_.file.is_primary
    })

    if ($primaryVersions.Count -gt 0) {
        $primary = $primaryVersions |
            Sort-Object { [datetime]$_.file.uploaded_at } -Descending |
            Select-Object -First 1

        return [string]$primary.file.version
    }

    $mainVersions = @($versions | Where-Object {
        $null -ne $_.file -and ([string]$_.file.category) -eq "main"
    })

    if ($mainVersions.Count -gt 0) {
        $latestMain = $mainVersions |
            Sort-Object { [datetime]$_.file.uploaded_at } -Descending |
            Select-Object -First 1

        return [string]$latestMain.file.version
    }

    $latest = $versions |
        Where-Object { $null -ne $_.file } |
        Sort-Object { [datetime]$_.file.uploaded_at } -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return ""
    }

    return [string]$latest.file.version
}

function Invoke-NexusPresignedUpload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    Write-Host "Uploading archive to presigned URL..."

    $response = Invoke-CurlUploadFile `
        -Method PUT `
        -Url $Url `
        -FilePath $FilePath `
        -Headers @{ "Content-Type" = $NexusUploadContentType } `
        -AllowHttpError

    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
        return $response
    }

    throw "HTTP $($response.StatusCode) upload to Nexus presigned URL.`n$($response.Body)`n$($response.Headers)"
}

$ApiKey = Read-SecretFile -Path (Join-Path $ApiDir $NexusApiKeyFileName)

$ArchivePath = Join-Path $ProjectRoot "package\nexus\$ModName.zip"
$ManifestPath = Join-Path $ProjectRoot "package\nexus\manifest.json"

Assert-FileExists -Path $ArchivePath
Assert-FileExists -Path $ManifestPath

$ArchiveInfo = Get-Item -LiteralPath $ArchivePath
$Manifest = Get-JsonFile -Path $ManifestPath

$FileName = [string]$Manifest.name

$Description = ""
if ($null -ne $Manifest.description) {
    $Description = [string]$Manifest.description
}

$Version = Normalize-PackageVersion -Version ([string]$Manifest.version) -Name "Nexus file version"
$FileCategory = [string]$Manifest.file_category

Assert-MaxLength -Value $FileName -Name "Nexus file name" -MaxLength 50
Assert-RegexMatch -Value $FileName -Name "Nexus file name" -Pattern "^[a-zA-Z0-9 _'().-]+$"
Assert-MaxLength -Value $Version -Name "Nexus file version" -MaxLength 50
Assert-MaxLength -Value $Description -Name "Nexus file description" -MaxLength $NexusDescriptionMaxLength

if (@("main", "optional", "miscellaneous") -notcontains $FileCategory) {
    throw "Nexus file_category must be one of: main, optional, miscellaneous. Current value: $FileCategory"
}

if ([string]::IsNullOrWhiteSpace($FileGroupName)) {
    $FileGroupName = $FileName
}

$RequestHeaders = @{
    "apikey"              = $ApiKey
    "Accept"              = "application/json"
    "Application-Name"    = $NexusApplicationName
    "Application-Version" = $NexusApplicationVersion
}

$FileGroupId = ""

if (-not [string]::IsNullOrWhiteSpace($FileGroupIdOverride)) {
    $FileGroupId = $FileGroupIdOverride.Trim()
    Write-Warning "Using manual Nexus file update group id override: $FileGroupId"
}
else {
    $InternalModId = Get-NexusInternalModId `
        -BaseUrl $NexusApiBase `
        -Headers $RequestHeaders `
        -GameDomain $GameDomain `
        -GameScopedModId $GameScopedModId

    $FileGroupId = Get-NexusFileGroupId `
        -BaseUrl $NexusApiBase `
        -Headers $RequestHeaders `
        -InternalModId $InternalModId `
        -RequestedGroupName $FileGroupName
}

if ([string]::IsNullOrWhiteSpace($FileGroupId)) {
    throw "Nexus file update group id is empty."
}

$CurrentNexusPrimaryVersion = ""

if ($SetUploadedFileAsPrimaryModManagerDownloadWhenNewer) {
    $CurrentNexusPrimaryVersion = Get-NexusCurrentPrimaryVersion `
        -BaseUrl $NexusApiBase `
        -Headers $RequestHeaders `
        -FileGroupId $FileGroupId
}

$SetUploadedFileAsPrimaryModManagerDownload = $false

if ($SetUploadedFileAsPrimaryModManagerDownloadWhenNewer) {
    if ([string]::IsNullOrWhiteSpace($CurrentNexusPrimaryVersion)) {
        $SetUploadedFileAsPrimaryModManagerDownload = $true
    }
    elseif ((Compare-PackageVersion -Left $Version -Right $CurrentNexusPrimaryVersion) -gt 0) {
        $SetUploadedFileAsPrimaryModManagerDownload = $true
    }
}

Write-Host "Nexus upload prepared:"
Write-Host "  archive:          $ArchivePath"
Write-Host "  size:             $($ArchiveInfo.Length) bytes"
Write-Host "  game domain:      $GameDomain"
Write-Host "  nexus mod id:     $GameScopedModId"
Write-Host "  file group name:  $FileGroupName"
Write-Host "  file group id:    $FileGroupId"
Write-Host "  file name:        $FileName"
Write-Host "  version:          $Version"
Write-Host "  category:         $FileCategory"
Write-Host "  description:      $($Description.Length)/$NexusDescriptionMaxLength chars"

if ($SetUploadedFileAsPrimaryModManagerDownloadWhenNewer) {
    if ([string]::IsNullOrWhiteSpace($CurrentNexusPrimaryVersion)) {
        Write-Host "  current primary:  <not found>"
    }
    else {
        Write-Host "  current primary:  $CurrentNexusPrimaryVersion"
    }

    Write-Host "  make primary:     $SetUploadedFileAsPrimaryModManagerDownload"
}

Confirm-ManualPublish -Yes:$Yes -Prompt "This will upload $($ArchiveInfo.Name) to Nexus Mods as version $Version."

$MaxSinglePartBytes = 100MB
if ($ArchiveInfo.Length -gt $MaxSinglePartBytes) {
    throw "Archive is larger than 100 MiB. This script intentionally handles only Nexus single-part uploads. Size: $($ArchiveInfo.Length) bytes."
}

Write-Host "Creating Nexus upload session..."
$CreateUploadBody = @{
    size_bytes = [Int64]$ArchiveInfo.Length
    filename   = $ArchiveInfo.Name
}

$CreateUpload = Invoke-CurlJson -Method POST -Url "$NexusApiBase/uploads" -Headers $RequestHeaders -Body $CreateUploadBody
$Upload = $CreateUpload.Json.data
$UploadId = [string]$Upload.id
$PresignedUrl = [string]$Upload.presigned_url

if ([string]::IsNullOrWhiteSpace($UploadId) -or [string]::IsNullOrWhiteSpace($PresignedUrl)) {
    throw "Nexus did not return upload id or presigned_url. Response: $($CreateUpload.Body)"
}

Invoke-NexusPresignedUpload -Url $PresignedUrl -FilePath $ArchivePath | Out-Null

Write-Host "Finalising Nexus upload..."
Invoke-CurlJson -Method POST -Url "$NexusApiBase/uploads/$UploadId/finalise" -Headers $RequestHeaders | Out-Null

Write-Host "Waiting until Nexus upload becomes available..."
$UploadState = $null

for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2

    $GetUpload = Invoke-CurlJson -Method GET -Url "$NexusApiBase/uploads/$UploadId" -Headers $RequestHeaders
    $UploadState = [string]$GetUpload.Json.data.state
    Write-Host "  state: $UploadState"

    if ($UploadState -eq "available") {
        break
    }
}

if ($UploadState -ne "available") {
    throw "Nexus upload did not become available in time. Upload id: $UploadId, last state: $UploadState"
}

$CreateVersionBody = [ordered]@{
    upload_id                    = $UploadId
    name                         = $FileName
    description                  = $Description
    version                      = $Version
    file_category                = $FileCategory
    primary_mod_manager_download = [bool]$SetUploadedFileAsPrimaryModManagerDownload
    allow_mod_manager_download   = [bool]$Manifest.allow_mod_manager_download
    show_requirements_pop_up     = [bool]$Manifest.show_requirements_pop_up
    archive_existing_file        = [bool]$Manifest.archive_existing_file
}

Write-Host "Creating Nexus file update group version..."

if ($SetUploadedFileAsPrimaryModManagerDownload) {
    Write-Host "Uploaded file will be marked as primary mod-manager download."
}

$CreateVersion = Invoke-CurlJson `
    -Method POST `
    -Url "$NexusApiBase/mod-file-update-groups/$FileGroupId/versions" `
    -Headers $RequestHeaders `
    -Body $CreateVersionBody

Write-Host "Nexus upload completed."
Write-Host ($CreateVersion.Body)