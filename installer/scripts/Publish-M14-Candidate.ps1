[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)][string]$CandidateId,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$CandidateDirectory,
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = $repoRoot }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
$identity = Get-M14CandidateIdentity -CandidateId $CandidateId
if ($Repository -cne 'cimerosef/sushi81-pos') { throw 'Candidate release publishing is restricted to the approved source/build repository.' }
if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedSourceSha must be a full 40-character commit SHA.' }
$actualSourceSha = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSourceSha -cne $ExpectedSourceSha) { throw "Publish checkout does not match exact candidate source '$ExpectedSourceSha'." }
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GH_TOKEN is required for minimum-permission GitHub release publication.' }
if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI is required for release publication.' }
$candidateRoot = (Resolve-Path -LiteralPath $CandidateDirectory).Path

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments,[switch]$AllowNotFound)
    $result = & gh @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($result | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        if ($AllowNotFound -and $text -match '(?i)(HTTP )?404|Not Found') { return $null }
        throw "GitHub API request failed ($($Arguments -join ' ')): $text"
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $text | ConvertFrom-Json
}
function Get-TagTargetCommit([string]$Tag) {
    $reference = Invoke-GhJson -Arguments @('api',"repos/$Repository/git/ref/tags/$Tag")
    $object = $reference.object
    while ($object.type -ceq 'tag') {
        $tagObject = Invoke-GhJson -Arguments @('api',"repos/$Repository/git/tags/$($object.sha)")
        $object = $tagObject.object
    }
    if ($object.type -cne 'commit') { throw "Candidate tag '$Tag' does not resolve to a commit." }
    [string]$object.sha
}
function Assert-CandidateAssets {
    $summaryPath = Join-Path $candidateRoot 'package-summary.json'
    $provenancePath = Join-Path $candidateRoot 'release-provenance.json'
    $manifestPath = Join-Path $candidateRoot 'payload-manifest.json'
    $archivePath = Join-Path $candidateRoot 'application-payload.zip'
    foreach ($path in @($summaryPath,$provenancePath,$manifestPath,$archivePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Candidate evidence asset is missing: '$path'." }
    }
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    if ($summary.schemaVersion -ne 1 -or $provenance.schemaVersion -ne 1 -or
        $summary.candidateId -cne $identity.CandidateId -or $provenance.candidateId -cne $identity.CandidateId -or
        $summary.candidateTag -cne $identity.Tag -or $provenance.candidateTag -cne $identity.Tag -or
        $summary.sourceHeadSha -cne $ExpectedSourceSha.ToLowerInvariant() -or $provenance.sourceHeadSha -cne $ExpectedSourceSha.ToLowerInvariant() -or
        $summary.productVersion -cne $identity.ProductVersion -or $provenance.productVersion -cne $identity.ProductVersion -or
        $summary.runtimeIdentifier -cne 'win-x64' -or $provenance.runtimeIdentifier -cne 'win-x64') {
        throw "Candidate summary/provenance identity does not match the exact authorized $($identity.CandidateId) source and version."
    }
    if ($summary.applicationPublishInvocationCount -ne 1 -or $provenance.applicationPublishInvocationCount -ne 1 -or
        $summary.installerApplicationRepublishInvocationCount -ne 0 -or $provenance.installerApplicationRepublishInvocationCount -ne 0) {
        throw 'Candidate evidence does not prove one application publish and zero installer-side republishes.'
    }
    if ($summary.preprodIdentity.appId -cne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' -or
        $summary.preprodIdentity.profile -cne 'preprod' -or
        $summary.preprodIdentity.installRoot -cne '%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD' -or
        $provenance.preprodIdentity.appId -cne $summary.preprodIdentity.appId -or
        $provenance.preprodIdentity.profileMarker -cne 'deployment-profile.txt=preprod') {
        throw 'Candidate evidence does not match the frozen PREPROD installer identity.'
    }
    if ($provenance.realProductionDataUsed -ne $false -or $provenance.runtimeHandoffSnapshotUsed -ne $false -or
        $summary.noRealProductionOrRuntimeDataUsed -ne $true) { throw 'Candidate evidence does not assert the required data-safety boundary.' }

    $installerName = [string]$summary.preprodInstaller.name
    if ([string]::IsNullOrWhiteSpace($installerName) -or $installerName -notmatch '^Sushi81POS-PREPROD-Setup-1\.0\.1-[0-9a-f]{7}\.exe$') {
        throw 'Candidate PREPROD installer filename is not bound to version 1.0.1 and the exact source SHA.'
    }
    $requiredNames = @('application-payload.zip','payload-manifest.json','package-summary.json','release-provenance.json',$installerName)
    $actualFiles = @(Get-ChildItem -LiteralPath $candidateRoot -File -Force)
    if ($actualFiles.Count -ne $requiredNames.Count) { throw 'Candidate release directory must contain exactly the five authorized assets.' }
    foreach ($name in $requiredNames) {
        if (@($actualFiles | Where-Object { $_.Name -ceq $name }).Count -ne 1) { throw "Candidate release asset '$name' is missing or duplicated." }
    }
    $declared = @($summary.requiredAssetNames | Sort-Object -Culture en-US -CaseSensitive)
    $expected = @($requiredNames | Sort-Object -Culture en-US -CaseSensitive)
    if ($declared.Count -ne $expected.Count) { throw 'Candidate summary has an incomplete release asset set.' }
    for ($index = 0; $index -lt $expected.Count; $index++) {
        if ($declared[$index] -cne $expected[$index]) { throw 'Candidate summary asset names do not match the required set.' }
    }
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$summary.payloadManifestSha256 -or
        (Get-FileHash -LiteralPath $provenancePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$summary.releaseProvenanceSha256 -or
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$summary.applicationPayloadArchive.sha256 -or
        (Get-Item -LiteralPath $archivePath).Length -ne [long]$summary.applicationPayloadArchive.bytes) {
        throw 'Candidate manifest, provenance or application archive differs from its recorded SHA-256/byte identity.'
    }
    $installerPath = Join-Path $candidateRoot $installerName
    if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$summary.preprodInstaller.sha256 -or
        (Get-Item -LiteralPath $installerPath).Length -ne [long]$summary.preprodInstaller.bytes) {
        throw 'Candidate PREPROD installer differs from its recorded SHA-256/byte identity.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.fileCount -ne $summary.payloadFileCount -or
        $manifest.applicationPayloadTreeSha256 -cne $summary.payloadTreeSha256 -or
        $manifest.applicationPayloadTreeSha256 -cne $provenance.payloadTreeSha256) {
        throw 'Candidate manifest file count or tree hash differs from summary/provenance.'
    }
    $extractionPath = Join-Path $env:RUNNER_TEMP "sushi81-candidate-publish-verify-$([guid]::NewGuid().ToString('N'))"
    $null = Test-M14PayloadArchive -ArchivePath $archivePath -ManifestPath $manifestPath -ExtractionDirectory $extractionPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $extractionPath | Out-Null
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $candidateRoot | Out-Null
    [pscustomobject]@{ Names = $requiredNames; Summary = $summary; Provenance = $provenance; ExtractionPath = $extractionPath }
}
function Assert-DraftReleaseAssets([object]$Release,[long]$ReleaseId,[string[]]$Names,[string]$VerificationDirectory) {
    if ([long]$Release.id -ne $ReleaseId -or $Release.tag_name -cne $identity.Tag -or
        $Release.target_commitish -cne $ExpectedSourceSha -or
        $Release.draft -ne $true -or $Release.prerelease -ne $true) {
        throw 'Candidate release must remain a draft prerelease until exact tag and asset verification completes.'
    }
    $assets = @($Release.assets)
    if ($assets.Count -ne $Names.Count) { throw 'Draft release does not contain exactly the five required candidate assets.' }
    $orderedNames = @($Names | Sort-Object -Culture en-US -CaseSensitive)
    $orderedAssets = @($assets | Sort-Object -Property name -Culture en-US -CaseSensitive)
    for ($index = 0; $index -lt $orderedNames.Count; $index++) {
        if ($orderedAssets[$index].name -cne $orderedNames[$index]) { throw 'Draft release asset names differ from the complete validated candidate set.' }
        $local = Get-Item -LiteralPath (Join-Path $candidateRoot $orderedNames[$index])
        if ([long]$orderedAssets[$index].size -ne [long]$local.Length) { throw "Uploaded candidate asset size differs for '$($orderedNames[$index])'." }
        $localHash = (Get-FileHash -LiteralPath $local.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::IsNullOrWhiteSpace([string]$orderedAssets[$index].digest) -and
            [string]$orderedAssets[$index].digest -cne "sha256:$localHash") {
            throw "GitHub's uploaded asset digest differs for '$($orderedNames[$index])'."
        }
        if ([string]::IsNullOrWhiteSpace([string]$orderedAssets[$index].digest)) {
            $downloadPath = Join-Path $VerificationDirectory $orderedNames[$index]
            Invoke-WebRequest -Uri ([string]$orderedAssets[$index].url) -Headers @{
                Authorization = "Bearer $env:GH_TOKEN"
                Accept = 'application/octet-stream'
                'X-GitHub-Api-Version' = '2026-03-10'
            } -OutFile $downloadPath
            if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $localHash) {
                throw "Downloaded GitHub asset hash differs for '$($orderedNames[$index])'."
            }
        }
    }
    $Release
}

$verifiedAssets = Assert-CandidateAssets
$verificationDirectory = Join-Path $env:RUNNER_TEMP "sushi81-candidate-assets-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
$immutableSetting = Invoke-GhJson -Arguments @('api',"repos/$Repository/immutable-releases") -AllowNotFound
if ($null -eq $immutableSetting -or $immutableSetting.enabled -ne $true) {
    throw 'GitHub repository release immutability is not enabled; no candidate release was created.'
}

$ref = Invoke-GhJson -Arguments @('api',"repos/$Repository/git/ref/tags/$($identity.Tag)") -AllowNotFound
$existingRelease = Invoke-GhJson -Arguments @('api',"repos/$Repository/releases/tags/$($identity.Tag)") -AllowNotFound
Assert-M14CandidateNotPublished -CandidateTag $identity.Tag -TagExists:($null -ne $ref) -ReleaseExists:($null -ne $existingRelease)
$preflightReleasePages = @(Invoke-GhJson -Arguments @('api',"repos/$Repository/releases?per_page=100",'--paginate','--slurp'))
Assert-M14NoExistingRelease -ReleasePages $preflightReleasePages -CandidateTag $identity.Tag

$assetPaths = @($verifiedAssets.Names | ForEach-Object { Join-Path $candidateRoot $_ })
$notes = @(
    "Owner-testable M14 PREPROD candidate $($identity.CandidateId)."
    ''
    "Exact source SHA: $ExpectedSourceSha"
    "Payload tree SHA-256: $($verifiedAssets.Summary.payloadTreeSha256)"
    "Payload manifest SHA-256: $($verifiedAssets.Summary.payloadManifestSha256)"
    "PREPROD installer SHA-256: $($verifiedAssets.Summary.preprodInstaller.sha256)"
    ''
    'The release contains application/build artifacts only. It contains no runtime handoff snapshots, business data, credentials or OneDrive data.'
) -join [Environment]::NewLine
$createArgs = @('release','create',$identity.Tag,'--repo',$Repository,'--target',$ExpectedSourceSha,'--title',"Sushi81 POS PREPROD $($identity.ProductVersion) $($identity.CandidateId)",'--notes',$notes,'--draft','--prerelease','--latest=false') + $assetPaths
$createOutput = & gh @createArgs 2>&1
if ($LASTEXITCODE -ne 0) { throw "Draft candidate release creation/upload failed: $(($createOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine)" }
# The tag endpoint only returns published releases. An authenticated release list includes drafts.
$releasePages = @(Invoke-GhJson -Arguments @('api',"repos/$Repository/releases?per_page=100",'--paginate','--slurp'))
$draftId = Get-M14DraftReleaseId -ReleasePages $releasePages -CandidateTag $identity.Tag -ExpectedSourceSha $ExpectedSourceSha
$draftRelease = Invoke-GhJson -Arguments @('api',"repos/$Repository/releases/$draftId")
$null = Assert-DraftReleaseAssets -Release $draftRelease -ReleaseId $draftId -Names $verifiedAssets.Names -VerificationDirectory $verificationDirectory

$published = Invoke-GhJson -Arguments @('api','--method','PATCH',"repos/$Repository/releases/$draftId",'-F','draft=false','-F','prerelease=true','-f','make_latest=false')
if ([long]$published.id -ne $draftId -or $published.draft -ne $false -or
    $published.prerelease -ne $true -or $published.tag_name -cne $identity.Tag -or
    $published.target_commitish -cne $ExpectedSourceSha) {
    throw 'GitHub did not publish the fully verified candidate as a prerelease.'
}
$publishedAgain = Invoke-GhJson -Arguments @('api',"repos/$Repository/releases/$draftId")
if ([long]$publishedAgain.id -ne $draftId -or $publishedAgain.tag_name -cne $identity.Tag -or
    $publishedAgain.target_commitish -cne $ExpectedSourceSha) {
    throw 'Final candidate release identity differs from the verified draft.'
}
if ($publishedAgain.immutable -ne $true) {
    throw 'GitHub published the candidate but did not mark it immutable; no release/tag/asset mutation will be attempted.'
}
$null = Assert-M14ReleaseAssetsUnchanged -DraftAssets @($draftRelease.assets) -PublishedAssets @($publishedAgain.assets) -ExpectedNames $verifiedAssets.Names
$tagCommit = Get-TagTargetCommit -Tag $identity.Tag
Assert-M14PublishedTagTarget -TagCommit $tagCommit -ExpectedSourceSha $ExpectedSourceSha
$latestRelease = Invoke-GhJson -Arguments @('api',"repos/$Repository/releases/latest") -AllowNotFound
$isLatest = $null -ne $latestRelease -and [long]$latestRelease.id -eq [long]$publishedAgain.id
if ($publishedAgain.draft -ne $false -or $publishedAgain.prerelease -ne $true -or $isLatest) {
    throw 'Final candidate release state is not published prerelease/non-latest.'
}
$publishedAssets = @($publishedAgain.assets | Sort-Object -Property name -Culture en-US -CaseSensitive)
$expectedNames = @($verifiedAssets.Names | Sort-Object -Culture en-US -CaseSensitive)
if ($publishedAssets.Count -ne $expectedNames.Count) { throw 'Final immutable candidate has an incomplete asset set.' }
$finalAssetInventory = @()
for ($index = 0; $index -lt $expectedNames.Count; $index++) {
    if ($publishedAssets[$index].name -cne $expectedNames[$index]) { throw 'Final immutable release assets differ from the verified draft assets.' }
    $finalAssetInventory += [pscustomobject]@{
        name = [string]$publishedAssets[$index].name
        id = [long]$publishedAssets[$index].id
        bytes = [long]$publishedAssets[$index].size
        sha256 = ([string]$publishedAssets[$index].digest -replace '^sha256:','')
        browserDownloadUrl = [string]$publishedAssets[$index].browser_download_url
    }
}
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    @(
        ''
        "## Immutable M14 PREPROD candidate $($identity.CandidateId) published"
        ''
        "Release: $($publishedAgain.html_url)"
        "Tag target: $tagCommit"
        "Is prerelease: $($publishedAgain.prerelease)"
        "Is latest: $isLatest"
        "Is immutable: $($publishedAgain.immutable)"
        'Assets (name, ID, bytes, SHA-256):'
        ($finalAssetInventory | ForEach-Object { "- $($_.name) — $($_.id), $($_.bytes), $($_.sha256)" })
    ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
[pscustomobject]@{
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    releaseId = [long]$publishedAgain.id
    releaseUrl = [string]$publishedAgain.html_url
    prerelease = [bool]$publishedAgain.prerelease
    latest = [bool]$isLatest
    immutable = [bool]$publishedAgain.immutable
    tagTargetSha = $tagCommit
    assets = $finalAssetInventory
}
