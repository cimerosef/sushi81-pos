[CmdletBinding()]
param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
if (-not $SelfTest) { throw 'Promotion self-test requires -SelfTest.' }
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'M14-ProductionPromotion.psm1') -Force
$accepted = Get-M14AcceptedPromotionIdentity
function Assert-Throws([scriptblock]$Action,[string]$Name) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw "Promotion self-test expected '$Name' to fail closed." }
}
function Assert-True([bool]$Value,[string]$Name) { if (-not $Value) { throw "Promotion self-test failed: $Name" } }
function Clone($Object) { ($Object | ConvertTo-Json -Depth 20 | ConvertFrom-Json) }
$assets = @($accepted.assets | ForEach-Object {
    [pscustomobject]@{ id=$_.id; name=$_.name; size=$_.size; digest=$_.digest; state='uploaded';
        browser_download_url="https://github.com/$($accepted.repository)/releases/download/$($accepted.tag)/$($_.name)" }
})
$release = [pscustomobject]@{ id=$accepted.releaseId; tag_name=$accepted.tag;
    target_commitish=$accepted.sourceSha; draft=$false; prerelease=$true; immutable=$true; assets=$assets }
$ref = [pscustomobject]@{ ref="refs/tags/$($accepted.tag)"; object=[pscustomobject]@{type='commit';sha=$accepted.sourceSha} }
$parameters = @{ Release=$release;TagRef=$ref;Repository=$accepted.repository;Tag=$accepted.tag;
    ExpectedReleaseId=$accepted.releaseId;ExpectedSourceSha=$accepted.sourceSha }
Assert-True ($null -ne (Assert-M14AcceptedRelease @parameters)) 'accepted immutable Release must pass.'
foreach ($change in @(
    @{id=7L},@{tag_name='v1.0.1-preprod-c02'},@{target_commitish=('b'*40)},
    @{draft=$true},@{prerelease=$false},@{immutable=$false},@{assets=@($assets[0..3])},
    @{assets=@($assets + $assets[0])}
)) {
    $bad = Clone $release
    foreach ($key in $change.Keys) { $bad.$key = $change[$key] }
    $call = $parameters.Clone(); $call.Release=$bad
    Assert-Throws { Assert-M14AcceptedRelease @call } "Release mutation $($change.Keys -join ',')"
}
foreach ($mutation in @(
    @{name='wrong-name'},@{id=7L},@{size=4L},@{digest='sha256:' + ('0'*64)},
    @{state='starter'},@{browser_download_url='https://invalid.example/payload.zip'}
)) {
    $bad = Clone $release
    foreach ($key in $mutation.Keys) { $bad.assets[0].$key = $mutation[$key] }
    $call = $parameters.Clone(); $call.Release=$bad
    Assert-Throws { Assert-M14AcceptedRelease @call } "asset mutation $($mutation.Keys -join ',')"
}
$badRef = Clone $ref; $badRef.object.type='tag'
$call = $parameters.Clone(); $call.TagRef=$badRef
Assert-Throws { Assert-M14AcceptedRelease @call } 'annotated tag object'
$badRef = Clone $ref; $badRef.object.sha='b'*40
$call.TagRef=$badRef
Assert-Throws { Assert-M14AcceptedRelease @call } 'wrong tag source'
foreach ($mutation in @(
    @{Repository='elsewhere/other'},@{Tag='v1.0.1-preprod-c02'},@{ExpectedReleaseId=4L},@{ExpectedSourceSha=('b'*40)}
)) {
    $call = $parameters.Clone()
    foreach ($key in $mutation.Keys) { $call[$key]=$mutation[$key] }
    Assert-Throws { Assert-M14AcceptedRelease @call } "wrong accepted argument $($mutation.Keys -join ',')"
}
$metadataManifest = [pscustomobject]@{ schemaVersion=1;candidateId='C01';sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';fileCount=418;
    applicationPayloadTreeSha256=$accepted.treeSha256;files=@(1..418) }
$metadataSummary = [pscustomobject]@{ candidateId='C01';sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';payloadFileCount=418;payloadTreeSha256=$accepted.treeSha256;
    payloadManifestSha256=$accepted.assets[1].digest.Substring(7);
    applicationPayloadArchive=[pscustomobject]@{sha256=$accepted.assets[0].digest.Substring(7);bytes=$accepted.assets[0].size};
    preprodInstaller=[pscustomobject]@{sha256=$accepted.assets[4].digest.Substring(7)};
    releaseProvenanceSha256=$accepted.assets[3].digest.Substring(7) }
$metadataProvenance = [pscustomobject]@{ candidateId='C01';sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';payloadFileCount=418;payloadTreeSha256=$accepted.treeSha256;
    payloadManifestSha256=$accepted.assets[1].digest.Substring(7);
    applicationPayloadArchive=[pscustomobject]@{sha256=$accepted.assets[0].digest.Substring(7)};
    preprodInstaller=[pscustomobject]@{sha256=$accepted.assets[4].digest.Substring(7)} }
Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $metadataSummary -Provenance $metadataProvenance
foreach ($mutation in @(@{candidateId='C02'},@{sourceHeadSha=('b'*40)},@{productVersion='1.0.2'},
    @{runtimeIdentifier='linux-x64'},@{fileCount=417},@{applicationPayloadTreeSha256=('0'*64)})) {
    $bad = Clone $metadataManifest
    foreach ($key in $mutation.Keys) { $bad.$key = $mutation[$key] }
    Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $bad -Summary $metadataSummary -Provenance $metadataProvenance } "manifest identity $($mutation.Keys -join ',')"
}
$bad = Clone $metadataSummary; $bad.payloadTreeSha256='0'*64
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $bad -Provenance $metadataProvenance } 'package summary tree mismatch'
$bad = Clone $metadataProvenance; $bad.sourceHeadSha='b'*40
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $metadataSummary -Provenance $bad } 'release provenance source mismatch'
$tmp = Join-Path ([IO.Path]::GetTempPath()) "sushi81-wp6-selftest-$([guid]::NewGuid().ToString('N'))"
try {
    $payload = Join-Path $tmp 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload 'app.exe'),'synthetic-app',[Text.UTF8Encoding]::new($false))
    $manifestPath = Join-Path $tmp 'manifest.json'
    New-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -SourceSha $accepted.sourceSha -CandidateId C01 | Out-Null
    $staging = Join-Path $tmp 'staging'
    New-M14ProfileStagingPayload -PayloadDirectory $payload -ManifestPath $manifestPath -DestinationDirectory $staging -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId C01 -Profile prod | Out-Null
    Assert-True (([IO.File]::ReadAllBytes((Join-Path $staging 'deployment-profile.txt')).Length -eq 4) -and
        ([IO.File]::ReadAllText((Join-Path $staging 'deployment-profile.txt')) -ceq 'prod')) 'Production marker must be exact ASCII prod.'
    Assert-True (@(Get-ChildItem -LiteralPath $staging -Recurse -File).Count -eq 2) 'Production stage adds exactly one marker.'
    [IO.File]::WriteAllText((Join-Path $staging 'app.exe'),'altered',[Text.UTF8Encoding]::new($false))
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $staging 'app.exe') -Algorithm SHA256).Hash.ToLowerInvariant() -cne
        (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).files[0].sha256) 'Altered Production file must be detectable.'
    [IO.File]::WriteAllText((Join-Path $payload 'app.exe'),'altered',[Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'altered accepted payload'
    [IO.File]::Delete((Join-Path $payload 'app.exe'))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'missing accepted payload'
    [IO.File]::WriteAllText((Join-Path $payload 'app.exe'),'synthetic-app',[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $payload 'extra.txt'),'extra',[Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'extra accepted payload'
    $download = Join-Path $tmp 'download.bin'
    [IO.File]::WriteAllText($download,'wrong',[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14DownloadedAsset -Path $download -Asset $accepted.assets[0] } 'downloaded bytes/digest mismatch'
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    Assert-M14PromotionBoundary -RepoRoot $repoRoot
    $boundaryRoot = Join-Path $tmp 'boundary-copy'
    foreach ($relative in @('installer/scripts/Promote-M14-Candidate.ps1','installer/scripts/M14-ProductionPromotion.psm1',
        'installer/scripts/Package-ProductionFromPayload.ps1','installer/scripts/Verify-ProductionPromotionLifecycle.ps1',
        '.github/workflows/m14-production-promotion.yml','.github/workflows/ci.yml')) {
        $target = Join-Path $boundaryRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination $target
    }
    $boundaryScript = Join-Path $boundaryRoot 'installer/scripts/Package-ProductionFromPayload.ps1'
    Add-Content -LiteralPath $boundaryScript -Value "`ndotnet publish app.csproj" -Encoding utf8
    Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'application publish invocation in promotion script'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer/scripts/Package-ProductionFromPayload.ps1') -Destination $boundaryScript -Force
    $boundaryWorkflow = Join-Path $boundaryRoot '.github/workflows/m14-production-promotion.yml'
    Add-Content -LiteralPath $boundaryWorkflow -Value "`n  contents: write" -Encoding utf8
    Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'write-scoped promotion workflow'
    $config = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/release-config.json') -Raw | ConvertFrom-Json
    $iss = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/sushi81-pos.iss') -Raw
    Assert-True ($config.innoAppId -ceq 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -and
        $config.binaryInstallDirectory -ceq '{localappdata}\Programs\Sushi81 POS' -and
        $config.durableDataDirectory -ceq '%LOCALAPPDATA%\Sushi81 POS' -and
        $iss.Contains('AppId={{C7A1B9E2-1E62-4B4B-A2EA-7802814408FC}')) 'Production AppId/install/data identity must be frozen.'
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Promote-M14-Candidate.ps1') -Raw
    Assert-True ($source.Contains('applicationRestoreInvocationCount=0') -and $source.Contains('applicationBuildInvocationCount=0') -and
        $source.Contains('applicationPublishInvocationCount=0')) 'Promotion provenance must record zero application compilation.'
    Assert-True ($source.Contains('Verify-ProductionPromotionLifecycle.ps1') -and
        (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-ProductionPromotionLifecycle.ps1') -Raw).Contains('Assert-Synthetic')) 'Hosted synthetic lifecycle is required.'
    Assert-True (-not ($source -match '(?i)GH_TOKEN|github\.token|[a-f0-9]{64}[^a-f0-9]')) 'Promotion source must not carry account tokens.'
    'M14 WP6 promotion self-tests passed (immutable identity, assets, metadata, staging, boundary and synthetic lifecycle contract).'
} finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
}
