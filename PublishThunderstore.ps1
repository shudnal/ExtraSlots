# Manual Thunderstore upload script.

param(
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

# ---- Mod-specific values -----------------------------------------------------
$ModName = "ExtraSlots"

# Thunderstore team / namespace under which the package is published.
$AuthorName = "shudnal"

# Community URL slug. For Valheim this is normally "valheim" from thunderstore.io/c/valheim/.
$Communities = @("valheim")

# Optional category slugs. You can leave it empty for updates if categories are already set on the site.
$Categories = @()

# Optional per-community categories, e.g. @{ valheim = @("mods") }. Usually not needed for updates.
$CommunityCategories = @{}

$HasNsfwContent = $false
$ThunderstoreBaseUrl = "https://thunderstore.io"
$ThunderstoreTokenFileName = "thunderstore-token.txt"

# After upload has completed, wait a little and print current package review status.
# Thunderstore malware/review checks can mark a package as rejected shortly after submission.
$PackageStatusCheckDelaySeconds = 5

# Service-account tokens currently work as Bearer in practice.
# Fallbacks are kept because the generated docs historically exposed multiple schemes.
$ThunderstoreAuthorizationSchemesToTry = @("Bearer", "Token", "Basic")
# -----------------------------------------------------------------------------

$ProjectRoot = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $ProjectRoot
$ApiDir = Join-Path $RepositoryRoot "API"
$CommonScript = Join-Path $ApiDir "CommonPublish.ps1"

. $CommonScript

Initialize-CurlProxyFromApiDirectory -ApiDir $ApiDir

$Token = Read-SecretFile -Path (Join-Path $ApiDir $ThunderstoreTokenFileName)

$ArchivePath = Join-Path $ProjectRoot "package\thunderstore\$ModName.zip"
Assert-FileExists -Path $ArchivePath
$ArchiveInfo = Get-Item -LiteralPath $ArchivePath

Test-ZipContainsRootEntries -ZipPath $ArchivePath -RequiredEntries @("manifest.json", "README.md", "icon.png", "CHANGELOG.md")

$ThunderstoreManifestText = Get-ZipRootTextEntry -ZipPath $ArchivePath -EntryName "manifest.json"
$ThunderstoreManifest = $ThunderstoreManifestText | ConvertFrom-Json
$PackageVersion = Normalize-PackageVersion -Version ([string]$ThunderstoreManifest.version_number) -Name "Thunderstore package version_number"

$ChangelogText = Get-ZipRootTextEntry -ZipPath $ArchivePath -EntryName "CHANGELOG.md"
if ([string]::IsNullOrWhiteSpace($ChangelogText)) {
    throw "Thunderstore CHANGELOG.md is empty in archive: $ArchivePath"
}

function Test-JsonProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $false
    }

    return @($Object.PSObject.Properties.Name) -contains $Name
}

function New-ThunderstoreAuthHeaders {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Scheme
    )

    if ($Scheme -eq "Basic") {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Token + ":")
        return @{
            "Authorization" = "Basic " + [Convert]::ToBase64String($bytes)
            "Accept"        = "application/json"
        }
    }

    return @{
        "Authorization" = "$Scheme $Token"
        "Accept"        = "application/json"
    }
}

function Invoke-ThunderstoreJsonWithAuthFallback {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST", "PUT", "PATCH", "DELETE")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Url,
        $Body = $null
    )

    $lastResponse = $null

    foreach ($scheme in $ThunderstoreAuthorizationSchemesToTry) {
        $headers = New-ThunderstoreAuthHeaders -Token $Token -Scheme $scheme
        $response = Invoke-CurlJson -Method $Method -Url $Url -Headers $headers -Body $Body -AllowHttpError
        $lastResponse = $response

        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
            $script:ThunderstoreAuthHeaders = $headers
            $script:ThunderstoreAuthScheme = $scheme
            return $response
        }

        if ($response.StatusCode -ne 401 -and $response.StatusCode -ne 403) {
            throw "HTTP $($response.StatusCode) $Method $Url`n$($response.Body)"
        }
    }

    throw "Thunderstore authentication failed. Last response: HTTP $($lastResponse.StatusCode)`n$($lastResponse.Body)"
}

function Confirm-ThunderstorePublish {
    param(
        [switch]$Yes,
        [Parameter(Mandatory = $true)][string]$Prompt
    )

    if ($Yes) {
        return "upload"
    }

    Write-Host ""
    Write-Host $Prompt
    $answer = (Read-Host "Type YES to continue, SKIP to skip Thunderstore").Trim()

    if ($answer -ieq "YES") {
        return "upload"
    }

    if ($answer -ieq "SKIP") {
        Write-Warning "Thunderstore upload skipped by user. Continuing with next publisher if any."
        return "skip"
    }

    throw "Publishing cancelled."
}

function ConvertTo-NormalizedSubmissionStatus {
    param([AllowEmptyString()][string]$Status)

    if ([string]::IsNullOrWhiteSpace($Status)) {
        return ""
    }

    return ([regex]::Replace($Status.Trim().ToUpperInvariant(), '[^A-Z0-9_]', ''))
}

function Test-ThunderstoreDuplicateVersionError {
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $value = $Text.ToLowerInvariant()

    return (
        $value.Contains("same namespace, name and version already exists") -or
        ($value.Contains("package of the same namespace") -and $value.Contains("already exists")) -or
        ($value.Contains("manifest.json") -and $value.Contains("already exists") -and $value.Contains("version"))
    )
}

function Write-ThunderstorePackageStatusSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Namespace,
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    try {
        Write-ThunderstorePackageStatus -Namespace $Namespace -PackageName $PackageName -ExpectedVersion $ExpectedVersion
    }
    catch {
        Write-Warning "Could not read Thunderstore package status."
        Write-Warning $_.Exception.Message
    }
}

function Write-ThunderstorePackageStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Namespace,
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    Write-Host ""
    Write-Host "Waiting $PackageStatusCheckDelaySeconds seconds before Thunderstore status check..."
    Start-Sleep -Seconds $PackageStatusCheckDelaySeconds

    $packageUrl = "$ThunderstoreBaseUrl/api/experimental/package/$Namespace/$PackageName/"
    $packageResponse = Invoke-CurlJson -Method GET -Url $packageUrl -Headers $script:ThunderstoreAuthHeaders -AllowHttpError

    if ($packageResponse.StatusCode -lt 200 -or $packageResponse.StatusCode -ge 300) {
        Write-Warning "Could not read Thunderstore package status. HTTP $($packageResponse.StatusCode) $packageUrl"
        if (-not [string]::IsNullOrWhiteSpace($packageResponse.Body)) {
            Write-Host $packageResponse.Body
        }
        return
    }

    if ($null -eq $packageResponse.Json) {
        Write-Warning "Could not parse Thunderstore package status response."
        Write-Host $packageResponse.Body
        return
    }

    $package = $packageResponse.Json
    $hasRejectedListing = $false

    Write-Host "Thunderstore current package status:"
    Write-Host "  package:   $Namespace-$PackageName"

    if (Test-JsonProperty -Object $package -Name "package_url") {
        Write-Host "  url:       $($package.package_url)"
    }

    if (Test-JsonProperty -Object $package -Name "is_deprecated") {
        Write-Host "  deprecated: $($package.is_deprecated)"
    }

    if ((Test-JsonProperty -Object $package -Name "latest") -and $null -ne $package.latest) {
        $latest = $package.latest
        $latestVersion = ""
        $latestActive = ""

        if (Test-JsonProperty -Object $latest -Name "version_number") {
            $latestVersion = [string]$latest.version_number
        }

        if (Test-JsonProperty -Object $latest -Name "is_active") {
            $latestActive = [string]$latest.is_active
        }

        Write-Host "  latest:   version=$latestVersion is_active=$latestActive"
    }

    if (Test-JsonProperty -Object $package -Name "community_listings") {
        $listings = @($package.community_listings)

        if ($listings.Count -eq 0) {
            Write-Host "  community listings: empty"
        }
        else {
            Write-Host "  community listings:"

            foreach ($listing in $listings) {
                $community = ""
                $reviewStatus = ""
                $hasNsfw = ""
                $categories = ""

                if (Test-JsonProperty -Object $listing -Name "community") {
                    $community = [string]$listing.community
                }

                if (Test-JsonProperty -Object $listing -Name "review_status") {
                    $reviewStatus = [string]$listing.review_status
                }

                if (Test-JsonProperty -Object $listing -Name "has_nsfw_content") {
                    $hasNsfw = [string]$listing.has_nsfw_content
                }

                if (Test-JsonProperty -Object $listing -Name "categories") {
                    $categories = [string]$listing.categories
                }

                Write-Host "    community=$community review_status=$reviewStatus nsfw=$hasNsfw categories=$categories"

                if ($reviewStatus -eq "rejected") {
                    $hasRejectedListing = $true
                }
            }
        }
    }
    else {
        Write-Host "  community listings: not returned"
    }

    $versionUrl = "$ThunderstoreBaseUrl/api/experimental/package/$Namespace/$PackageName/$ExpectedVersion/"
    $versionResponse = Invoke-CurlJson -Method GET -Url $versionUrl -Headers $script:ThunderstoreAuthHeaders -AllowHttpError

    if ($versionResponse.StatusCode -ge 200 -and $versionResponse.StatusCode -lt 300 -and $null -ne $versionResponse.Json) {
        $version = $versionResponse.Json
        $versionActive = ""
        $versionCreated = ""

        if (Test-JsonProperty -Object $version -Name "is_active") {
            $versionActive = [string]$version.is_active
        }

        if (Test-JsonProperty -Object $version -Name "date_created") {
            $versionCreated = [string]$version.date_created
        }

        Write-Host "  uploaded version: version=$ExpectedVersion is_active=$versionActive date_created=$versionCreated"
    }
    else {
        Write-Warning "Uploaded version '$ExpectedVersion' is not readable yet. HTTP $($versionResponse.StatusCode) $versionUrl"
    }

    if ($hasRejectedListing) {
        Write-Warning "Thunderstore package listing status is rejected. Check the package page and Thunderstore moderation/Discord."
    }
}

Write-Host "Thunderstore upload prepared:"
Write-Host "  archive:     $ArchivePath"
Write-Host "  size:        $($ArchiveInfo.Length) bytes"
Write-Host "  package:     $($ThunderstoreManifest.name)"
Write-Host "  version:     $PackageVersion"
Write-Host "  author/team: $AuthorName"
Write-Host "  communities: $($Communities -join ', ')"
Write-Host "  categories:  $($Categories -join ', ')"
Write-Host "  changelog:   $($ChangelogText.Length) chars"

$ThunderstorePublishAction = Confirm-ThunderstorePublish -Yes:$Yes -Prompt "This will upload $($ArchiveInfo.Name) to Thunderstore as version $PackageVersion."
if ($ThunderstorePublishAction -eq "skip") {
    exit 0
}

Write-Host "Initiating Thunderstore usermedia upload..."
$InitiateBody = @{
    filename        = $ArchiveInfo.Name
    file_size_bytes = [Int64]$ArchiveInfo.Length
}

$Initiate = Invoke-ThunderstoreJsonWithAuthFallback -Method POST -Url "$ThunderstoreBaseUrl/api/experimental/usermedia/initiate-upload/" -Body $InitiateBody
Write-Host "Thunderstore auth scheme accepted: $script:ThunderstoreAuthScheme"

$UserMedia = $Initiate.Json.user_media
$UploadUuid = [string]$UserMedia.uuid
$UploadUrls = @($Initiate.Json.upload_urls)

if ([string]::IsNullOrWhiteSpace($UploadUuid) -or $UploadUrls.Count -eq 0) {
    throw "Thunderstore did not return user_media.uuid or upload_urls. Response: $($Initiate.Body)"
}

Write-Host "Uploading archive parts..."
$TempDir = New-TempDirectory
$CompletedParts = @()

try {
    foreach ($part in $UploadUrls) {
        $partNumber = [int]$part.part_number
        $offset = [Int64]$part.offset
        $length = [Int64]$part.length
        $url = [string]$part.url
        $partPath = Join-Path $TempDir ("part-{0}.bin" -f $partNumber)

        Write-Host "  part $partNumber offset=$offset length=$length"

        New-FilePart -SourcePath $ArchivePath -Offset $offset -Length $length -DestinationPath $partPath
        $uploadResponse = Invoke-CurlUploadFile -Method PUT -Url $url -FilePath $partPath
        $etag = Get-HeaderValue -HeadersText $uploadResponse.Headers -Name "ETag"

        if ([string]::IsNullOrWhiteSpace($etag)) {
            throw "ETag was not returned for Thunderstore upload part $partNumber. Headers: $($uploadResponse.Headers)"
        }

        $CompletedParts += [ordered]@{
            ETag       = $etag
            PartNumber = $partNumber
        }
    }
}
finally {
    Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Finishing Thunderstore usermedia upload..."
$FinishBody = @{
    parts = @($CompletedParts)
}

$Finish = Invoke-CurlJson -Method POST -Url "$ThunderstoreBaseUrl/api/experimental/usermedia/$UploadUuid/finish-upload/" -Headers $script:ThunderstoreAuthHeaders -Body $FinishBody

if ([string]$Finish.Json.status -ne "upload_complete") {
    Write-Host "Thunderstore finish-upload response: $($Finish.Body)"
}

Write-Host "Submitting Thunderstore package asynchronously..."
$SubmitBody = [ordered]@{
    author_name      = $AuthorName
    communities      = @($Communities)
    has_nsfw_content = [bool]$HasNsfwContent
    upload_uuid      = $UploadUuid
}

if ($Categories.Count -gt 0) {
    $SubmitBody["categories"] = @($Categories)
}

if ($CommunityCategories.Count -gt 0) {
    $SubmitBody["community_categories"] = $CommunityCategories
}

$Submit = Invoke-CurlJson -Method POST -Url "$ThunderstoreBaseUrl/api/experimental/submission/submit-async/" -Headers $script:ThunderstoreAuthHeaders -Body $SubmitBody
$SubmissionId = [string]$Submit.Json.id

if ([string]::IsNullOrWhiteSpace($SubmissionId)) {
    Write-Host "submit-async response did not include id. Response: $($Submit.Body)"
    Write-Host "Trying synchronous submit endpoint as fallback..."

    $SyncSubmit = Invoke-CurlJson -Method POST -Url "$ThunderstoreBaseUrl/api/experimental/submission/submit/" -Headers $script:ThunderstoreAuthHeaders -Body $SubmitBody -AllowHttpError

    if ($SyncSubmit.StatusCode -ge 200 -and $SyncSubmit.StatusCode -lt 300) {
        Write-Host "Thunderstore upload completed."
        Write-Host ($SyncSubmit.Body)
        Write-ThunderstorePackageStatusSafe -Namespace $AuthorName -PackageName ([string]$ThunderstoreManifest.name) -ExpectedVersion $PackageVersion
        exit 0
    }

    if (Test-ThunderstoreDuplicateVersionError -Text $SyncSubmit.Body) {
        Write-Warning "Thunderstore package version already exists. Upload will be treated as skipped, not failed."
        Write-Warning "Package: $AuthorName-$($ThunderstoreManifest.name), version: $PackageVersion"
        Write-Warning "Thunderstore response: $($SyncSubmit.Body)"
        Write-ThunderstorePackageStatusSafe -Namespace $AuthorName -PackageName ([string]$ThunderstoreManifest.name) -ExpectedVersion $PackageVersion
        exit 0
    }

    throw "Thunderstore synchronous submit failed. HTTP $($SyncSubmit.StatusCode)`n$($SyncSubmit.Body)"
}

Write-Host "Thunderstore submission id: $SubmissionId"
Write-Host "Polling Thunderstore submission result..."

$LastPrintedStatus = ""

for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Seconds 3

    $Poll = Invoke-CurlJson `
        -Method GET `
        -Url "$ThunderstoreBaseUrl/api/experimental/submission/poll-async/$SubmissionId/" `
        -Headers $script:ThunderstoreAuthHeaders `
        -AllowHttpError

    if ($Poll.StatusCode -eq 404) {
        Write-Host "  poll returned 404, submission may still be initializing"
        continue
    }

    if ($Poll.StatusCode -lt 200 -or $Poll.StatusCode -ge 300) {
        throw "HTTP $($Poll.StatusCode) while polling Thunderstore submission.`n$($Poll.Body)"
    }

    if ($null -eq $Poll.Json) {
        Write-Host "  response: $($Poll.Body)"
        continue
    }

    $PollPropertyNames = @($Poll.Json.PSObject.Properties.Name)

    $Status = ""
    if ($PollPropertyNames -contains "status") {
        $Status = [string]$Poll.Json.status
    }

    $NormalizedStatus = ConvertTo-NormalizedSubmissionStatus -Status $Status

    if (-not [string]::IsNullOrWhiteSpace($NormalizedStatus) -and $NormalizedStatus -ne $LastPrintedStatus) {
        Write-Host "  status: $NormalizedStatus"
        $LastPrintedStatus = $NormalizedStatus
    }
    elseif ([string]::IsNullOrWhiteSpace($NormalizedStatus)) {
        Write-Host "  response: $($Poll.Body)"
    }

    $FormErrorsText = ""
    $HasFormErrors = $false

    if ($PollPropertyNames -contains "form_errors" -and $null -ne $Poll.Json.form_errors) {
        $FormErrorsText = $Poll.Json.form_errors | ConvertTo-Json -Depth 20 -Compress
        $HasFormErrors = -not [string]::IsNullOrWhiteSpace($FormErrorsText) -and $FormErrorsText -ne "{}" -and $FormErrorsText -ne "null"
    }

    if ($HasFormErrors) {
        $CombinedFormErrorText = "$FormErrorsText`n$($Poll.Body)"

        if (Test-ThunderstoreDuplicateVersionError -Text $CombinedFormErrorText) {
            Write-Warning "Thunderstore package version already exists. Upload will be treated as skipped, not failed."
            Write-Warning "Package: $AuthorName-$($ThunderstoreManifest.name), version: $PackageVersion"
            Write-Warning "Thunderstore response: $($Poll.Body)"

            Write-ThunderstorePackageStatusSafe `
                -Namespace $AuthorName `
                -PackageName ([string]$ThunderstoreManifest.name) `
                -ExpectedVersion $PackageVersion

            exit 0
        }

        throw "Thunderstore submission returned form_errors. Response: $($Poll.Body)"
    }

    if ($PollPropertyNames -contains "task_error" -and $null -ne $Poll.Json.task_error -and [bool]$Poll.Json.task_error) {
        throw "Thunderstore submission task_error=true. Response: $($Poll.Body)"
    }

    if ($PollPropertyNames -contains "package_version") {
        Write-Host "Thunderstore upload completed."
        Write-Host ($Poll.Body)

        Write-ThunderstorePackageStatusSafe `
            -Namespace $AuthorName `
            -PackageName ([string]$ThunderstoreManifest.name) `
            -ExpectedVersion $PackageVersion

        exit 0
    }

    if ($NormalizedStatus -eq "FINISHED" -or $NormalizedStatus -eq "COMPLETED" -or $NormalizedStatus -eq "SUCCESS" -or $NormalizedStatus -eq "SUCCEEDED") {
        Write-Host "Thunderstore upload completed."

        if ($PollPropertyNames -contains "result" -and -not [string]::IsNullOrWhiteSpace([string]$Poll.Json.result)) {
            Write-Host "Thunderstore submission result:"
            Write-Host ([string]$Poll.Json.result)
        }

        Write-ThunderstorePackageStatusSafe `
            -Namespace $AuthorName `
            -PackageName ([string]$ThunderstoreManifest.name) `
            -ExpectedVersion $PackageVersion

        exit 0
    }

    if ($NormalizedStatus -eq "FAILED" -or $NormalizedStatus -eq "FAILURE" -or $NormalizedStatus -eq "ERROR" -or $NormalizedStatus -eq "ERRORED" -or $NormalizedStatus -eq "CANCELLED" -or $NormalizedStatus -eq "CANCELED" -or $NormalizedStatus -eq "REVOKED") {
        throw "Thunderstore submission failed with status '$Status'. Response: $($Poll.Body)"
    }
}

throw "Thunderstore submission did not finish in time. Submission id: $SubmissionId"