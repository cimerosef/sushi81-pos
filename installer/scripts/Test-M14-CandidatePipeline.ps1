[CmdletBinding()]
param([switch]$SelfTest)

$ErrorActionPreference = 'Stop'
if (-not $SelfTest) { throw 'This deterministic test entry point requires -SelfTest.' }
$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
Import-Module (Join-Path $scriptRoot 'M14-CandidatePipeline.psm1') -Force

function Assert-Condition([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw "M14 candidate self-test failed: $Message" }
}
function Assert-Throws([scriptblock]$Action,[string]$Name) {
    $thrown = $false
    try { & $Action | Out-Null } catch { $thrown = $true }
    if (-not $thrown) { throw "M14 candidate self-test expected '$Name' to fail closed." }
}
function Write-SyntheticFile([string]$Path,[string]$Content) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($Path,$Content,[Text.UTF8Encoding]::new($false))
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "sushi81-m14-pipeline-selftest-$([guid]::NewGuid().ToString('N'))"
$payload = Join-Path $tempRoot 'payload'
$manifestPath = Join-Path $tempRoot 'payload-manifest.json'
$archivePath = Join-Path $tempRoot 'application-payload.zip'
$extractRoot = Join-Path $tempRoot 'archive-roundtrip'
$stageRoot = Join-Path $tempRoot 'preprod-staging'
$sha = 'a' * 40
$candidate = Get-M14CandidateIdentity -CandidateId 'C01'

try {
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Write-SyntheticFile (Join-Path $payload 'Sushi81.Pos.Desktop.exe') 'synthetic executable payload'
    Write-SyntheticFile (Join-Path $payload 'zh-CN\Sushi81.Pos.Desktop.resources.dll') 'synthetic localized resource'
    $manifest = New-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -SourceSha $sha -CandidateId 'C01'
    $verified = Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -ExpectedSourceSha $sha -ExpectedCandidateId 'C01'
    Assert-Condition ($candidate.Tag -ceq 'v1.0.1-preprod-c01') 'C01 tag must resolve to the frozen candidate tag.'
    Assert-Condition ($verified.fileCount -eq 2 -and $verified.applicationPayloadTreeSha256 -match '^[0-9a-f]{64}$') 'manifest must record file count and tree SHA-256.'

    $original = [IO.File]::ReadAllText((Join-Path $payload 'Sushi81.Pos.Desktop.exe'))
    Write-SyntheticFile (Join-Path $payload 'Sushi81.Pos.Desktop.exe') 'changed payload'
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'changed file'
    Write-SyntheticFile (Join-Path $payload 'Sushi81.Pos.Desktop.exe') $original
    [IO.File]::Delete((Join-Path $payload 'Sushi81.Pos.Desktop.exe'))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'missing file'
    Write-SyntheticFile (Join-Path $payload 'Sushi81.Pos.Desktop.exe') $original
    Write-SyntheticFile (Join-Path $payload 'unexpected.txt') 'extra payload'
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath } 'extra file'
    [IO.File]::Delete((Join-Path $payload 'unexpected.txt'))

    $document = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $caseCollision = @($document.files) + @([pscustomobject]@{ path = 'sushi81.pos.desktop.exe'; bytes = 1; sha256 = ('b' * 64) })
    $caseDocument = [ordered]@{
        schemaVersion = $document.schemaVersion
        candidateId = $document.candidateId
        sourceHeadSha = $document.sourceHeadSha
        productVersion = $document.productVersion
        runtimeIdentifier = $document.runtimeIdentifier
        profileEvidenceExcluded = $document.profileEvidenceExcluded
        fileCount = $caseCollision.Count
        applicationPayloadTreeSha256 = '0' * 64
        files = $caseCollision
    }
    $caseManifestPath = Join-Path $tempRoot 'case-collision-manifest.json'
    [IO.File]::WriteAllText($caseManifestPath,($caseDocument | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $caseManifestPath } 'case-colliding paths'

    $pathDocument = [ordered]@{
        schemaVersion = $document.schemaVersion
        candidateId = $document.candidateId
        sourceHeadSha = $document.sourceHeadSha
        productVersion = $document.productVersion
        runtimeIdentifier = $document.runtimeIdentifier
        profileEvidenceExcluded = $document.profileEvidenceExcluded
        fileCount = 1
        applicationPayloadTreeSha256 = '0' * 64
        files = @([pscustomobject]@{ path = '../escape.txt'; bytes = 1; sha256 = ('b' * 64) })
    }
    $pathManifestPath = Join-Path $tempRoot 'path-traversal-manifest.json'
    [IO.File]::WriteAllText($pathManifestPath,($pathDocument | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $pathManifestPath } 'path traversal'
    Assert-Throws { New-M14PayloadManifest -PayloadDirectory $payload -ManifestPath (Join-Path $tempRoot 'bad-sha.json') -SourceSha 'short' -CandidateId 'C01' } 'wrong source SHA'
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -ExpectedSourceSha ('c' * 40) } 'wrong expected source SHA'
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -ExpectedCandidateId 'C02' } 'wrong candidate ID'
    Assert-Throws { Test-M14PayloadManifest -PayloadDirectory $payload -ManifestPath $manifestPath -ExpectedProductVersion '1.0.2' } 'wrong product version'
    Assert-Throws { Get-M14CandidateIdentity -CandidateId 'C00' } 'candidate number zero'
    Assert-Throws { Get-M14CandidateIdentity -CandidateId 'C01' -ProductVersion '2.0.0' } 'wrong product semantic version'
    Assert-Throws { Assert-M14CandidateNotPublished -CandidateTag $candidate.Tag -TagExists } 'existing candidate tag'
    Assert-Throws { Assert-M14CandidateNotPublished -CandidateTag $candidate.Tag -ReleaseExists } 'existing candidate release'

    $archiveInfo = New-M14PayloadArchive -PayloadDirectory $payload -ManifestPath $manifestPath -ArchivePath $archivePath -ExpectedSourceSha $sha -ExpectedCandidateId 'C01'
    $roundTrip = Test-M14PayloadArchive -ArchivePath $archivePath -ManifestPath $manifestPath -ExtractionDirectory $extractRoot -ExpectedSourceSha $sha -ExpectedCandidateId 'C01'
    Assert-Condition ($archiveInfo.bytes -gt 0 -and $archiveInfo.sha256 -match '^[0-9a-f]{64}$' -and $roundTrip.applicationPayloadTreeSha256 -ceq $manifest.applicationPayloadTreeSha256) 'archive must independently round-trip against the manifest.'

    $markerPath = New-M14PreProdStagingPayload -PayloadDirectory $payload -ManifestPath $manifestPath -DestinationDirectory $stageRoot -ExpectedSourceSha $sha -ExpectedCandidateId 'C01'
    $markerBytes = [IO.File]::ReadAllBytes($markerPath)
    Assert-Condition ($markerBytes.Length -eq 7 -and [Text.Encoding]::ASCII.GetString($markerBytes) -ceq 'preprod') 'PreProd packaging must add exactly its seven-byte profile marker.'
    foreach ($entry in @($manifest.files)) {
        $path = Join-Path $stageRoot ([string]$entry.path.Replace('/',[IO.Path]::DirectorySeparatorChar))
        Assert-Condition ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq [string]$entry.sha256) 'PreProd packaging must preserve every manifest file hash.'
    }

    $buildScript = Get-Content -LiteralPath (Join-Path $scriptRoot 'Build-M14-Candidate.ps1') -Raw
    $packageScript = Get-Content -LiteralPath (Join-Path $scriptRoot 'Package-PreProdFromPayload.ps1') -Raw
    $publisherScript = Get-Content -LiteralPath (Join-Path $scriptRoot 'Publish-M14-Candidate.ps1') -Raw
    $lifecycleScript = Get-Content -LiteralPath (Join-Path $scriptRoot 'Verify-PreProdCandidateLifecycle.ps1') -Raw
    $workflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github\workflows\m14-preprod-candidate.yml') -Raw
    Assert-Condition ([regex]::Matches($buildScript,'(?m)^\s*''publish'',\s*\$project,').Count -eq 1) 'the candidate build helper must contain exactly one application publish command.'
    Assert-Condition ($buildScript.Contains('& dotnet @publishArgs')) 'the one publish command must be invoked by the candidate build helper.'
    Assert-Condition (-not [regex]::IsMatch($packageScript,'(?i)\bdotnet\s+(publish|build|restore)\b')) 'installer packaging from a payload must not compile or republish application binaries.'
    Assert-Condition ($packageScript.Contains('New-M14PreProdStagingPayload') -and $packageScript.Contains('Test-ForbiddenContent.ps1')) 'PreProd packaging must verify profile-only addition and scan its staging tree.'
    Assert-Condition ($workflow.Contains('workflow_dispatch:') -and $workflow.Contains('github.sha') -and $workflow.Contains('contents: write')) 'candidate workflow must be explicit, exact-SHA and release-write scoped.'
    Assert-Condition ($workflow.Contains('contents: read') -and $workflow.Contains('actions/checkout@v7')) 'candidate build must use ordinary checkout permissions and a pinned source checkout.'
    Assert-Condition ($publisherScript.Contains('--draft') -and $publisherScript.Contains('--prerelease') -and $publisherScript.Contains('--latest=false')) 'release must be drafted, pre-release and excluded from latest.'
    Assert-Condition ($publisherScript.IndexOf('$verifiedAssets = Assert-CandidateAssets') -lt $publisherScript.IndexOf('$createOutput = & gh @createArgs')) 'all candidate assets must pass validation before a draft release is created.'
    Assert-Condition ($publisherScript.IndexOf('Assert-DraftReleaseAssets') -lt $publisherScript.IndexOf('draft=false')) 'assets must be verified while the release is still draft before prerelease visibility.'
    Assert-Condition ($publisherScript.Contains('Assert-M14CandidateNotPublished') -and -not $publisherScript.Contains('--clobber')) 'the release publisher must fail closed on an existing identity and never replace assets.'
    Assert-Condition ($publisherScript.Contains('/immutable-releases') -and $publisherScript.Contains('immutable')) 'publication must require and verify GitHub release immutability.'
    Assert-Condition ($lifecycleScript.Contains('foreach ($path in @($installRoot,$dataRoot,$shortcut))') -and
        $lifecycleScript.Contains('[string]::Equals($installRoot,$prodInstallRoot') -and
        $lifecycleScript.Contains('[string]::Equals($dataRoot,$prodDataRoot')) 'hosted lifecycle checks must isolate PREPROD targets without requiring production folders to be absent.'
    Assert-Condition ($workflow.Contains('m14-wp2-dual-installer') -or (Get-Content -LiteralPath (Join-Path $repoRoot '.github\workflows\ci.yml') -Raw).Contains('m14-wp2-dual-installer')) 'ordinary exact-head CI must retain the WP2 lifecycle regression.'
    Assert-Condition ((Test-Path -LiteralPath (Join-Path $repoRoot 'tests\Sushi81.Pos.Infrastructure.IntegrationTests\M14InitialProductionSeedTests.cs')) -and
        (Test-Path -LiteralPath (Join-Path $repoRoot 'tests\Sushi81.Pos.Infrastructure.IntegrationTests\M14PreProductionIsolationTests.cs'))) 'the retained WP3/WP4 regression suites must remain in the solution.'
    Write-Output 'M14 candidate pipeline self-test passed: publish/package boundary, manifest/zip/staging invariants, identity guards, immutable-release workflow and retained WP2-WP4 regressions.'
} finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedTemp.StartsWith($systemTemp,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTemp) -notmatch '^sushi81-m14-pipeline-selftest-[0-9a-f]{32}$') {
        throw 'Self-test cleanup target did not pass its generated-temp-path guard.'
    }
    if (Test-Path -LiteralPath $resolvedTemp) { Remove-Item -LiteralPath $resolvedTemp -Recurse -Force }
}
