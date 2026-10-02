[CmdletBinding()]
param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
if (-not $SelfTest) { throw 'Promotion self-test requires -SelfTest.' }
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'M14-ProductionPromotion.psm1') -Force
$accepted = Get-M14AcceptedPromotionIdentity
if ($accepted.candidateId -cne 'C02' -or $accepted.tag -cne 'v1.0.1-preprod-c02' -or
    $accepted.releaseId -ne 399541988L -or $accepted.sourceSha -cne '0aa3a0282a432da38bff9b35b38ffa03cf3bbada' -or
    $accepted.version -cne '1.0.1' -or $accepted.runtime -cne 'win-x64' -or $accepted.fileCount -ne 418 -or
    $accepted.treeSha256 -cne '006f5db911b71c5ecf7e61ffcaf42b9950d9196b4e19c0f9da49b5bb4b32fb6c') {
    throw 'Promotion self-test requires the exact owner-accepted immutable C02 identity.'
}
$script:assertionCount = 0
function Assert-Throws([scriptblock]$Action,[string]$Name) {
    $script:assertionCount++
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw "Promotion self-test expected '$Name' to fail closed." }
}
function Assert-True([bool]$Value,[string]$Name) { $script:assertionCount++; if (-not $Value) { throw "Promotion self-test failed: $Name" } }
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
    @{id=7L},@{tag_name='v1.0.1-preprod-c01'},@{target_commitish=('b'*40)},
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
$bad = Clone $release; $bad.assets[1].name=$bad.assets[0].name
$call = $parameters.Clone(); $call.Release=$bad
Assert-Throws { Assert-M14AcceptedRelease @call } 'duplicate asset name'
$badRef = Clone $ref; $badRef.object.type='tag'
$call = $parameters.Clone(); $call.TagRef=$badRef
Assert-Throws { Assert-M14AcceptedRelease @call } 'annotated tag object'
$badRef = Clone $ref; $badRef.object.sha='b'*40
$call.TagRef=$badRef
Assert-Throws { Assert-M14AcceptedRelease @call } 'wrong tag source'
foreach ($mutation in @(
    @{Repository='elsewhere/other'},@{Tag='v1.0.1-preprod-c01'},@{ExpectedReleaseId=4L},@{ExpectedSourceSha=('b'*40)}
)) {
    $call = $parameters.Clone()
    foreach ($key in $mutation.Keys) { $call[$key]=$mutation[$key] }
    Assert-Throws { Assert-M14AcceptedRelease @call } "wrong accepted argument $($mutation.Keys -join ',')"
}
$metadataManifest = [pscustomobject]@{ schemaVersion=1;candidateId=$accepted.candidateId;sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';fileCount=418;
    applicationPayloadTreeSha256=$accepted.treeSha256;files=@(1..418) }
$metadataSummary = [pscustomobject]@{ candidateId=$accepted.candidateId;candidateTag=$accepted.tag;sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';payloadFileCount=418;payloadTreeSha256=$accepted.treeSha256;
    payloadManifestSha256=$accepted.assets[1].digest.Substring(7);
    applicationPayloadArchive=[pscustomobject]@{name=$accepted.assets[0].name;sha256=$accepted.assets[0].digest.Substring(7);bytes=$accepted.assets[0].size};
    preprodInstaller=[pscustomobject]@{name=$accepted.assets[4].name;sha256=$accepted.assets[4].digest.Substring(7);bytes=$accepted.assets[4].size};
    releaseProvenanceSha256=$accepted.assets[3].digest.Substring(7) }
$metadataProvenance = [pscustomobject]@{ candidateId=$accepted.candidateId;candidateTag=$accepted.tag;sourceHeadSha=$accepted.sourceSha;
    productVersion='1.0.1';runtimeIdentifier='win-x64';payloadFileCount=418;payloadTreeSha256=$accepted.treeSha256;
    payloadManifestSha256=$accepted.assets[1].digest.Substring(7);
    applicationPayloadArchive=[pscustomobject]@{name=$accepted.assets[0].name;sha256=$accepted.assets[0].digest.Substring(7);bytes=$accepted.assets[0].size};
    preprodInstaller=[pscustomobject]@{name=$accepted.assets[4].name;sha256=$accepted.assets[4].digest.Substring(7);bytes=$accepted.assets[4].size} }
Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $metadataSummary -Provenance $metadataProvenance
foreach ($mutation in @(@{candidateId='C01'},@{sourceHeadSha=('b'*40)},@{productVersion='1.0.2'},
    @{runtimeIdentifier='linux-x64'},@{fileCount=417},@{applicationPayloadTreeSha256=('0'*64)})) {
    $bad = Clone $metadataManifest
    foreach ($key in $mutation.Keys) { $bad.$key = $mutation[$key] }
    Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $bad -Summary $metadataSummary -Provenance $metadataProvenance } "manifest identity $($mutation.Keys -join ',')"
}
$bad = Clone $metadataSummary; $bad.payloadTreeSha256='0'*64
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $bad -Provenance $metadataProvenance } 'package summary tree mismatch'
$bad = Clone $metadataSummary; $bad.candidateTag='v1.0.1-preprod-c01'
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $bad -Provenance $metadataProvenance } 'package summary tag mismatch'
$bad = Clone $metadataSummary; $bad.preprodInstaller.bytes=1
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $bad -Provenance $metadataProvenance } 'package summary installer bytes mismatch'
$bad = Clone $metadataSummary; $bad.applicationPayloadArchive.sha256='0'*64
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $bad -Provenance $metadataProvenance } 'package summary archive digest mismatch'
$bad = Clone $metadataProvenance; $bad.sourceHeadSha='b'*40
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $metadataSummary -Provenance $bad } 'release provenance source mismatch'
$bad = Clone $metadataProvenance; $bad.preprodInstaller.sha256='0'*64
Assert-Throws { Assert-M14AcceptedPayloadMetadata -Manifest $metadataManifest -Summary $metadataSummary -Provenance $bad } 'release provenance installer digest mismatch'
$tmp = Join-Path ([IO.Path]::GetTempPath()) "sushi81-wp6-selftest-$([guid]::NewGuid().ToString('N'))"
$previousReadToken = [Environment]::GetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN','Process')
try {
    $payload = Join-Path $tmp 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload 'app.exe'),'synthetic-app',[Text.UTF8Encoding]::new($false))
    $manifestPath = Join-Path $tmp 'manifest.json'
    New-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -SourceSha $accepted.sourceSha -CandidateId $accepted.candidateId | Out-Null
    $staging = Join-Path $tmp 'staging'
    New-M14ProfileStagingPayload -PayloadDirectory $payload -ManifestPath $manifestPath -DestinationDirectory $staging -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId $accepted.candidateId -Profile prod | Out-Null
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
    $syntheticAsset = [pscustomobject]@{name='synthetic.bin';size=5L;digest="sha256:$((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant())"}
    Assert-M14DownloadedAsset -Path $download -Asset $syntheticAsset
    [IO.File]::WriteAllText($download,'other',[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14DownloadedAsset -Path $download -Asset $syntheticAsset } 'same-size downloaded digest mismatch'
    [IO.File]::WriteAllText($download,'wrong-size',[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14DownloadedAsset -Path $download -Asset $syntheticAsset } 'downloaded size mismatch'
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    Assert-M14PromotionBoundary -RepoRoot $repoRoot
    # Load only the real transport functions: offline mocks never contact GitHub or package an installer.
    $promotionScript = Join-Path $PSScriptRoot 'Promote-M14-Candidate.ps1'
    $parseTokens=$null; $parseErrors=$null
    $promotionAst = [Management.Automation.Language.Parser]::ParseFile($promotionScript,[ref]$parseTokens,[ref]$parseErrors)
    Assert-True ($parseErrors.Count -eq 0) 'promotion script parses'
    foreach ($name in @('Get-M14ReleaseReadHeaders','Read-M14ReleaseMetadata','Save-M14ReleaseAsset')) {
        $definition = $promotionAst.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$false)
        Assert-True (@($definition).Count -eq 1) 'one real transport function'
        . ([scriptblock]::Create($definition[0].Extent.Text))
    }
    $script:requests = [Collections.Generic.List[object]]::new()
    $script:failureStatus = 0
    $sentinel = 'synthetic-read-token-for-offline-selftest'
    function Invoke-RestMethod {
        param($Uri,$Headers,$MaximumRedirection,[switch]$Verbose,[switch]$Debug)
        $script:requests.Add([pscustomobject]@{uri=$Uri;headers=$Headers.Clone();redirects=$MaximumRedirection})
        if ($script:failureStatus) { throw "Synthetic $script:failureStatus $sentinel" }
        if ($Uri.EndsWith("/releases/$($accepted.releaseId)")) { $release } else { $ref }
    }
    function Invoke-WebRequest {
        param($Uri,$Headers,$OutFile,$MaximumRedirection,[switch]$Verbose,[switch]$Debug)
        $script:requests.Add([pscustomobject]@{uri=$Uri;headers=$Headers.Clone();redirects=$MaximumRedirection})
        if ($script:failureStatus) { throw "Synthetic $script:failureStatus $sentinel" }
        [IO.File]::WriteAllText($OutFile,'synthetic-asset',[Text.UTF8Encoding]::new($false))
        'Transport response must not reach promotion output'
    }
    function Assert-SanitizedFailure([scriptblock]$Action,[string]$Name) {
        $script:requests.Clear()
        $script:caught=$false
        $output = @(& { try { & $Action } catch { $script:caught=$true; $_ } } *>&1)
        Assert-True $script:caught $Name
        Assert-True (-not (($output | Out-String).Contains($sentinel))) 'transport failure does not expose credential'
        Assert-True ($script:requests.Count -eq 1) 'authenticated failure has no retry or anonymous fallback'
    }
    foreach ($missing in @($null,'','   ')) {
        [Environment]::SetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN',$missing,'Process')
        $script:requests.Clear()
        Assert-Throws { Get-M14ReleaseReadHeaders } 'missing/blank credential helper preflight'
        $notCreated = Join-Path $tmp 'missing-token-artifact'
        Assert-Throws { & $promotionScript -Repository $accepted.repository -CandidateTag $accepted.tag -ExpectedReleaseId $accepted.releaseId -ExpectedSourceSha $accepted.sourceSha -PromotionHead ('c'*40) -CompilerPath 'unused' -ArtifactDirectory $notCreated } 'missing/blank executable credential preflight'
        Assert-True ($script:requests.Count -eq 0 -and -not (Test-Path -LiteralPath $notCreated)) 'missing token prevents remote reads and artifact staging'
    }
    [Environment]::SetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN',$sentinel,'Process')
    $readHeaders = Get-M14ReleaseReadHeaders
    Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN','Process'))) 'child tools do not inherit credential'
    $base = "https://api.github.com/repos/$($accepted.repository)"
    $metadataUris = @("$base/releases/$($accepted.releaseId)","$base/git/ref/tags/$($accepted.tag)")
    $script:requests.Clear()
    foreach ($uri in $metadataUris) { Read-M14ReleaseMetadata -Uri $uri -Headers $readHeaders | Out-Null }
    foreach ($asset in $accepted.assets[0..3]) {
        $output = @(Save-M14ReleaseAsset -AssetId $asset.id -Headers $readHeaders -Path $download *>&1)
        Assert-True ($output.Count -eq 0) 'asset transport emits neither headers nor response'
        Assert-True ($script:requests[$script:requests.Count-1].uri -ceq "$base/releases/assets/$($asset.id)") 'asset download uses validated API ID'
    }
    Assert-True ($script:requests.Count -eq 6) 'Release/tag and four payload assets use authenticated requests'
    for ($i=0; $i -lt $script:requests.Count; $i++) {
        $request=$script:requests[$i]
        Assert-True ($request.headers.Authorization -ceq "Bearer $sentinel") 'request uses environment-supplied bearer only'
        $expectedAccept = if ($i -lt 2) { 'application/vnd.github+json' } else { 'application/octet-stream' }
        Assert-True ($request.headers.Accept -ceq $expectedAccept) 'metadata/asset Accept contract'
        Assert-True ($request.redirects -eq $(if ($i -lt 2) {0} else {5})) 'trusted metadata has no redirect; asset redirects bounded with default credential stripping'
    }
    foreach ($status in @(401,403,404)) {
        $script:failureStatus=$status
        foreach ($uri in $metadataUris) { Assert-SanitizedFailure { Read-M14ReleaseMetadata -Uri $uri -Headers $readHeaders } 'metadata auth failure fails closed' }
        Assert-SanitizedFailure { Save-M14ReleaseAsset -AssetId $accepted.assets[0].id -Headers $readHeaders -Path $download } 'asset auth failure fails closed'
    }
    $script:failureStatus=0; $script:requests.Clear()
    Assert-Throws { Read-M14ReleaseMetadata -Uri 'https://invalid.example/release' -Headers $readHeaders } 'untrusted metadata endpoint'
    Assert-Throws { Save-M14ReleaseAsset -AssetId 7L -Headers $readHeaders -Path $download } 'unvalidated asset ID'
    Assert-True ($script:requests.Count -eq 0) 'untrusted endpoint/asset fails before transport'
    $readHeaders.Clear()
    Remove-Item -LiteralPath Function:\Invoke-RestMethod,Function:\Invoke-WebRequest
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
    Copy-Item -LiteralPath (Join-Path $repoRoot '.github/workflows/m14-production-promotion.yml') -Destination $boundaryWorkflow -Force
    $original = Get-Content -LiteralPath $boundaryWorkflow -Raw
    [IO.File]::WriteAllText($boundaryWorkflow,$original.Replace('workflow_dispatch:','push:'),[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'automatic promotion trigger'
    [IO.File]::WriteAllText($boundaryWorkflow,$original.Replace($accepted.tag,'v1.0.1-preprod-c01'),[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'old candidate workflow identity'
    [IO.File]::WriteAllText($boundaryWorkflow,$original.Replace('refs/heads/codex/post-m14-production-maintenance-batch-01','refs/heads/codex/m14-preprod-foundation-authorized'),[Text.UTF8Encoding]::new($false))
    Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'old branch promotion guard'
    [IO.File]::WriteAllText($boundaryWorkflow,$original,[Text.UTF8Encoding]::new($false))
    foreach ($relative in @('.github/workflows/m14-production-promotion.yml','.github/workflows/ci.yml')) {
        $target=Join-Path $boundaryRoot $relative
        $baseline=Get-Content -LiteralPath (Join-Path $repoRoot $relative) -Raw
        foreach ($mutation in @(
            $baseline.Replace('contents: read','contents: write'),
            $baseline.Replace('contents: read','contents: none'),
            $baseline.Replace('permissions:','permissions: write-all'),
            $baseline.Replace('persist-credentials: false','persist-credentials: true'),
            $baseline.Replace('persist-credentials: false','fetch-depth: 1'),
            $baseline.Replace('SUSHI81_RELEASE_READ_TOKEN: ${{ github.token }}','SUSHI81_RELEASE_READ_TOKEN: ${{ secrets.ACCOUNT_TOKEN }}'),
            $baseline.Replace('SUSHI81_RELEASE_READ_TOKEN: ${{ github.token }}','UNUSED: value'),
            $baseline.Replace('-Repository ''cimerosef/sushi81-pos''','-Token ${{ github.token }} -Repository ''cimerosef/sushi81-pos''')
        )) {
            [IO.File]::WriteAllText($target,$mutation,[Text.UTF8Encoding]::new($false))
            Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'workflow permission/env-only credential mutation'
        }
        [IO.File]::WriteAllText($target,$baseline,[Text.UTF8Encoding]::new($false))
    }
    $target=Join-Path $boundaryRoot 'installer/scripts/Promote-M14-Candidate.ps1'
    $baseline=Get-Content -LiteralPath $promotionScript -Raw
    foreach ($mutation in @(
        $baseline.Replace('releases/assets/$AssetId','releases/download/$AssetId'),
        $baseline.Replace('-MaximumRedirection 5','-MaximumRedirection 5 -PreserveAuthorizationOnRedirect'),
        $baseline.Replace('schemaVersion=1; acceptedCandidateId=', 'credential=$headers.Authorization; schemaVersion=1; acceptedCandidateId='),
        $baseline.Replace('schemaVersion=1; candidate=$accepted.candidateId', 'credential=$headers.Authorization; schemaVersion=1; candidate=$accepted.candidateId'),
        $baseline.Replace('Authorization="Bearer $readToken"', 'Authorization="Bearer hard-coded-account-credential"'),
        ($baseline + "`n`$accountToken = 'ghp_" + ('X'*36) + "'")
    )) {
        [IO.File]::WriteAllText($target,$mutation,[Text.UTF8Encoding]::new($false))
        Assert-Throws { Assert-M14PromotionBoundary -RepoRoot $boundaryRoot } 'unsafe transport/persisted/hard-coded credential mutation'
    }
    [IO.File]::WriteAllText($target,$baseline,[Text.UTF8Encoding]::new($false))
    $config = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/release-config.json') -Raw | ConvertFrom-Json
    $iss = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/sushi81-pos.iss') -Raw
    Assert-True ($config.innoAppId -ceq 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -and
        $config.binaryInstallDirectory -ceq '{localappdata}\Programs\Sushi81 POS' -and
        $config.durableDataDirectory -ceq '%LOCALAPPDATA%\Sushi81 POS' -and
        $iss.Contains('AppId={{C7A1B9E2-1E62-4B4B-A2EA-7802814408FC}')) 'Production AppId/install/data identity must be frozen.'
    $packageSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Package-ProductionFromPayload.ps1') -Raw
    Assert-True ($packageSource.Contains('Sushi81POS-PROD-Setup-$($accepted.version)-$($accepted.candidateId)-$short') -and
        $packageSource.Contains('allManifestDescribedFilesMatchAcceptedCandidate=$true')) 'Production output name and equivalence proof must identify accepted C02.'
    $lifecycleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-ProductionPromotionLifecycle.ps1') -Raw
    Assert-True ($lifecycleSource.Contains('$accepted.candidateId') -and $lifecycleSource.Contains('Assert-Synthetic') -and
        $lifecycleSource.Contains('67FB6B75-3C5E-44A5-98AD-305EA4C62D95')) 'Hosted lifecycle must check accepted C02 and preserve PREPROD identity.'
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Promote-M14-Candidate.ps1') -Raw
    Assert-True ($source.Contains('applicationRestoreInvocationCount=0') -and $source.Contains('applicationBuildInvocationCount=0') -and
        $source.Contains('applicationPublishInvocationCount=0')) 'Promotion provenance must record zero application compilation.'
    Assert-True ($source.Contains('Verify-ProductionPromotionLifecycle.ps1') -and
        (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify-ProductionPromotionLifecycle.ps1') -Raw).Contains('Assert-Synthetic')) 'Hosted synthetic lifecycle is required.'
    Assert-True (-not ($source -match '(?i)github_pat_|gh[pousr]_[a-z0-9]{20,}|[a-f0-9]{64}[^a-f0-9]')) 'Promotion source must not carry hard-coded/account tokens.'
    "M14 WP6 promotion self-tests passed ($script:assertionCount assertions: authentication, immutable identity, assets, metadata, staging, boundary and synthetic lifecycle contract)."
} finally {
    [Environment]::SetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN',$previousReadToken,'Process')
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
}
