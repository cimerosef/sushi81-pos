[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$CandidateTag,
    [Parameter(Mandatory)][long]$ExpectedReleaseId,
    [Parameter(Mandatory)][string]$ExpectedSourceSha,
    [Parameter(Mandatory)][string]$PromotionHead,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'M14-ProductionPromotion.psm1') -Force
$accepted = Get-M14AcceptedPromotionIdentity
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Assert-M14PromotionBoundary -RepoRoot $repoRoot
if ($Repository -cne $accepted.repository -or $CandidateTag -cne $accepted.tag -or
    $ExpectedReleaseId -ne $accepted.releaseId -or $ExpectedSourceSha -cne $accepted.sourceSha) {
    throw "Promotion arguments do not identify the accepted immutable $($accepted.candidateId)."
}
if ($PromotionHead -cnotmatch '^[0-9a-f]{40}$' -or (git -C $repoRoot rev-parse HEAD).Trim() -cne $PromotionHead) {
    throw 'Promotion checkout does not match the exact requested head.'
}
$headers = @{ Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'; 'User-Agent'='Sushi81-M14-WP6-Promotion' }
$api = "https://api.github.com/repos/$Repository"
# Preflight all remote identity before creating any package staging.
$release = Invoke-RestMethod -Uri "$api/releases/$ExpectedReleaseId" -Headers $headers
$tagRef = Invoke-RestMethod -Uri "$api/git/ref/tags/$CandidateTag" -Headers $headers
$checked = Assert-M14AcceptedRelease -Release $release -TagRef $tagRef -Repository $Repository -Tag $CandidateTag -ExpectedReleaseId $ExpectedReleaseId -ExpectedSourceSha $ExpectedSourceSha
$tempParent = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$work = Join-Path $tempParent "sushi81-m14-wp6-$([guid]::NewGuid().ToString('N'))"
$download = Join-Path $work 'download'
New-Item -ItemType Directory -Path $download -Force | Out-Null
try {
    foreach ($name in @('application-payload.zip','payload-manifest.json','package-summary.json','release-provenance.json')) {
        $asset = @($release.assets | Where-Object { $_.name -ceq $name })[0]
        $path = Join-Path $download $name
        Invoke-WebRequest -Uri ([string]$asset.browser_download_url) -Headers @{ 'User-Agent'='Sushi81-M14-WP6-Promotion' } -OutFile $path
        Assert-M14DownloadedAsset -Path $path -Asset $asset
    }
    $manifestPath = Join-Path $download 'payload-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $summary = Get-Content -LiteralPath (Join-Path $download 'package-summary.json') -Raw | ConvertFrom-Json
    $provenance = Get-Content -LiteralPath (Join-Path $download 'release-provenance.json') -Raw | ConvertFrom-Json
    Assert-M14AcceptedPayloadMetadata -Manifest $manifest -Summary $summary -Provenance $provenance
    $extracted = Join-Path $work 'extracted'
    $verified = Test-M14PayloadArchive -ArchivePath (Join-Path $download 'application-payload.zip') -ManifestPath $manifestPath -ExtractionDirectory $extracted -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId $accepted.candidateId
    if ($verified.fileCount -ne $accepted.fileCount -or $verified.applicationPayloadTreeSha256 -cne $accepted.treeSha256) {
        throw "Extracted $($accepted.candidateId) payload does not match accepted file count/tree hash."
    }
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $extracted | Out-Null
    $package = & (Join-Path $PSScriptRoot 'Package-ProductionFromPayload.ps1') -PayloadDirectory $extracted -ManifestPath $manifestPath -PackageDirectory (Join-Path $work 'package') -CompilerPath $CompilerPath -PromotionHead $PromotionHead
    if ($null -eq $package -or $null -eq $package.proof) { throw 'Production package returned no byte-equivalence proof.' }
    $lifecycle = & (Join-Path $PSScriptRoot 'Verify-ProductionPromotionLifecycle.ps1') -InstallerPath $package.installerPath -PayloadManifest $manifestPath -ExpectedSourceSha $accepted.sourceSha
    if ($null -eq $lifecycle -or -not $lifecycle.passed) { throw 'Hosted Production installer lifecycle did not pass.' }
    $destination = [IO.Path]::GetFullPath($ArtifactDirectory)
    if (Test-Path -LiteralPath $destination) { throw 'Refusing to reuse WP6 artifact directory.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local' }
    $branch = if ($env:GITHUB_HEAD_REF) { $env:GITHUB_HEAD_REF } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { (git -C $repoRoot branch --show-current).Trim() }
    $record = [ordered]@{
        schemaVersion=1; acceptedCandidateId=$accepted.candidateId; acceptedCandidateTag=$accepted.tag;
        acceptedCandidateReleaseId=$accepted.releaseId; acceptedSourceSha=$accepted.sourceSha;
        acceptedPayloadFileCount=$accepted.fileCount; acceptedPayloadTreeSha256=$accepted.treeSha256;
        acceptedPayloadManifestSha256=$accepted.assets[1].digest.Substring(7);
        acceptedPayloadArchiveSha256=$accepted.assets[0].digest.Substring(7);
        candidateAssets=@($accepted.assets | ForEach-Object { [ordered]@{id=$_.id;name=$_.name;bytes=$_.size;digest=$_.digest} });
        promotionSourceBranch=$branch; promotionSourceHead=$PromotionHead; promotionRunId=$runId;
        productionVersion='1.0.1'; productionAppId='C7A1B9E2-1E62-4B4B-A2EA-7802814408FC';
        productionAppName='Sushi81 POS'; productionProfile='prod';
        productionInstallRoot='%LOCALAPPDATA%\Programs\Sushi81 POS'; productionDurableDataRoot='%LOCALAPPDATA%\Sushi81 POS';
        innoSetupVersion='6.7.3'; productionInstallerFileName=$package.proof.installerFileName;
        productionInstallerBytes=$package.proof.installerBytes; productionInstallerSha256=$package.proof.installerSha256;
        applicationRestoreInvocationCount=0; applicationBuildInvocationCount=0; applicationPublishInvocationCount=0;
        allManifestDescribedProductionFilesMatchAcceptedCandidate=$true;
        syntheticLifecyclePassed=$true; realProductionDeploymentPerformed=$false
    }
    Copy-Item -LiteralPath $package.installerPath -Destination (Join-Path $destination $package.proof.installerFileName)
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'payload-manifest.json')
    Copy-Item -LiteralPath $package.proofPath -Destination (Join-Path $destination 'production-payload-equivalence.json')
    [IO.File]::WriteAllText((Join-Path $destination 'promotion-provenance.json'),($record | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $destination 'production-lifecycle-summary.json'),($lifecycle | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    $packageSummary = [ordered]@{ schemaVersion=1; candidate=$accepted.candidateId; sourceSha=$accepted.sourceSha; promotionHead=$PromotionHead;
        installerFileName=$package.proof.installerFileName; installerBytes=$package.proof.installerBytes;
        installerSha256=$package.proof.installerSha256; payloadFiles=$accepted.fileCount;
        payloadTreeSha256=$accepted.treeSha256; evidenceOnly=$true }
    [IO.File]::WriteAllText((Join-Path $destination 'production-package-summary.json'),($packageSummary | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $destination | Out-Null
    [pscustomobject]@{ artifactDirectory=$destination; summary=$packageSummary; lifecycle=$lifecycle }
} finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
