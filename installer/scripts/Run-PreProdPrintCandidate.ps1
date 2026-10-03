[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedHead,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceSha = '1dcedb0e0647e290bbc9ca86e3161919537b598a'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or
    $env:GITHUB_REPOSITORY -cne 'cimerosef/sushi81-pos' -or $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_REF -cne 'refs/heads/codex/post-m14-production-maintenance-batch-01' -or
    $ExpectedHead -cnotmatch '^[0-9a-f]{40}$' -or $env:GITHUB_SHA -cne $ExpectedHead -or
    [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
    throw 'H18 requires its manual exact-head GitHub-hosted runner.'
}
$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $ExpectedHead) { throw 'H18 checkout differs from the authorized dispatch head.' }
& git -C $repoRoot diff --exit-code $sourceSha HEAD -- src/ tests/
if ($LASTEXITCODE -ne 0) { throw 'H17 application source or tests changed.' }
Import-Module (Join-Path $PSScriptRoot 'PreProdPrintCandidate.psm1') -Force
Assert-PreProdPrintCandidateBoundary -RepoRoot $repoRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_WORKSPACE 'artifacts')).TrimEnd('\') + '\'
if (-not $OutputDirectory.StartsWith($runnerRoot,[StringComparison]::OrdinalIgnoreCase) -or
    -not $ArtifactDirectory.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'H18 outputs must stay under runner temporary and workspace artifact roots.' }
foreach ($directory in @($OutputDirectory,$ArtifactDirectory)) {
    if (Test-Path -LiteralPath $directory) { throw 'H18 outputs must be new directories.' }
}
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'Sushi81-H18-PreProd-Evidence' }
function Assert-NoV103Publication {
    foreach ($name in @('v1.0.3','v1.0.3-preprod','v1.0.3-preprod-c01')) {
        foreach ($route in @("releases/tags/$name","git/ref/tags/$name")) {
            try {
                $null = Invoke-RestMethod -Uri "https://api.github.com/repos/cimerosef/sushi81-pos/$route" -Headers $headers
                throw "Unexpected existing publication: $route"
            } catch {
                if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
            }
        }
    }
}
Assert-NoV103Publication
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cimerosef/sushi81-pos/releases/tags/v1.0.2-preprod' -Headers $headers
$fixture = Assert-AcceptedPreProdV102Release -Release $release
$tag = Invoke-RestMethod -Uri 'https://api.github.com/repos/cimerosef/sushi81-pos/git/ref/tags/v1.0.2-preprod' -Headers $headers
if ($tag.ref -cne 'refs/tags/v1.0.2-preprod' -or $tag.object.type -cne 'commit' -or $tag.object.sha -cne $fixture.sourceSha) { throw 'The PREPROD fixture tag no longer directly targets the accepted source.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$fixturePath = Join-Path $OutputDirectory $fixture.name
Invoke-WebRequest -Uri $fixture.url -OutFile $fixturePath
Assert-AcceptedPreProdV102Installer -Path $fixturePath | Out-Null

# Separate packaging head from exact H17 application checkout/provenance.
$sourceRoot = Join-Path $OutputDirectory 'h17-source'
& git -C $repoRoot worktree add --detach $sourceRoot $sourceSha
if ($LASTEXITCODE -ne 0) { throw 'Could not create the exact H17 application checkout.' }
$buildRoot = Join-Path $OutputDirectory 'build'
& (Join-Path $PSScriptRoot 'Build-InstallerPackage.ps1') -ExpectedSourceSha $sourceSha -SourceRoot $sourceRoot -OutputDirectory $buildRoot -CompilerPath $CompilerPath -ProductVersion '1.0.3' -IncludePreProduction | Out-Host
$packageRoot = Join-Path $buildRoot 'installer'
$summary = Get-Content -LiteralPath (Join-Path $packageRoot 'package-summary.json') -Raw | ConvertFrom-Json
if ($summary.sourceHeadSha -cne $sourceSha -or $summary.productVersion -cne '1.0.3' -or $summary.fileVersion -cne '1.0.3.0' -or
    $summary.payloadEquality.verified -ne $true -or $summary.payloadEquality.commonApplicationFileCount -lt 1 -or
    $summary.profilePackages.preprod.appId -cne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' -or
    $summary.profilePackages.prod.appId -cne 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC') { throw 'H18 package version/source/equivalence/profile identity failed.' }
$lifecyclePath = Join-Path $OutputDirectory 'preprod-upgrade-summary.json'
& (Join-Path $PSScriptRoot 'Verify-PreProdPrintUpgrade.ps1') -PreviousInstaller $fixturePath -CurrentInstaller $summary.profilePackages.preprod.installerPath -PayloadManifest $summary.applicationPayloadManifestPath -ExpectedSourceSha $sourceSha -OutputSummary $lifecyclePath | Out-Host
$lifecycle = Get-Content -LiteralPath $lifecyclePath -Raw | ConvertFrom-Json
if ($lifecycle.passed -ne $true -or $lifecycle.upgradePreservationPassed -ne $true -or $lifecycle.productionIdentityUntouched -ne $true) { throw 'H18 PREPROD upgrade verification failed.' }

New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
$preprod = $summary.profilePackages.preprod
$installerCopy = Join-Path $ArtifactDirectory $preprod.installerFileName
Copy-Item -LiteralPath $preprod.installerPath -Destination $installerCopy
if ((Get-Item -LiteralPath $installerCopy).Length -ne $preprod.installerBytes -or
    (Get-FileHash -LiteralPath $installerCopy -Algorithm SHA256).Hash.ToLowerInvariant() -cne $preprod.installerSha256) { throw 'Candidate installer copy differs from verified bytes.' }
foreach ($profile in @('prod','preprod')) { $summary.profilePackages.$profile.PSObject.Properties.Remove('installerPath') }
foreach ($name in @('application-payload-manifest.json','release-provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $packageRoot $name) -Destination (Join-Path $ArtifactDirectory $name)
}
foreach ($property in @('publishDirectory','installerPath','releaseProvenancePath','applicationPayloadManifestPath')) { $summary.PSObject.Properties.Remove($property) }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'package-summary.json') -Encoding utf8
Copy-Item -LiteralPath $lifecyclePath -Destination (Join-Path $ArtifactDirectory 'preprod-upgrade-summary.json')
& git -C $repoRoot diff --exit-code $sourceSha HEAD -- src/ tests/
if ($LASTEXITCODE -ne 0) { throw 'H17 source/tests immutability changed during packaging.' }
Assert-NoV103Publication
$evidence = [ordered]@{
    schemaVersion = 1
    handoffId = 'POST-M14-PM01-PREPROD-PRINT-R2-V103-PACKAGE-18'
    packagingHeadSha = $ExpectedHead
    applicationSourceHeadSha = $sourceSha
    sourceTree = (& git -C $repoRoot rev-parse "$($sourceSha):src").Trim()
    testsTree = (& git -C $repoRoot rev-parse "$($sourceSha):tests").Trim()
    applicationSourceAndTestsUnchanged = $true
    productVersion = '1.0.3'
    fileVersion = '1.0.3.0'
    informationalVersion = "1.0.3+$sourceSha"
    workflowRunId = $env:GITHUB_RUN_ID
    workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    applicationPublishInvocationCount = 1
    payloadFileCount = $summary.payloadEquality.commonApplicationFileCount
    payloadTreeSha256 = $summary.applicationPayloadTreeSha256
    payloadManifestSha256 = $summary.applicationPayloadManifestSha256
    profilePackages = $summary.profilePackages
    acceptedUpgradeFixture = $fixture
    fixtureDirectTagVerified = $true
    fixtureDownloadedBytesVerified = $true
    upgradePreservationPassed = $true
    productionIdentityUntouched = $true
    ownerFacingProfile = 'preprod'
    productionInstallerIncludedInArtifact = $false
    productionInstallerUsage = 'INTERNAL BYTE-EQUIVALENCE EVIDENCE ONLY; NOT OWNER OUTPUT'
    physicalThermalPrinterAcceptance = 'PHYSICAL THERMAL PRINTER ACCEPTANCE NOT RUN'
    productionDeployment = 'PRODUCTION DEPLOYMENT NOT AUTHORIZED'
    interactiveWpfAcceptance = 'NOT RUN'
    ownerMachineInstallationPerformed = $false
    realBusinessDataUsed = $false
    releaseOrTagPublished = $false
    v103PublicationAbsenceVerified = $true
}
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'candidate-evidence.json') -Encoding utf8
@(
    '# PREPROD 1.0.3 - H17 kitchen physical reserve candidate',
    '',
    'Only Sushi81POS-PREPROD-Setup-1.0.3-1dcedb0.exe is owner-facing.',
    'Production installer bytes are absent from this artifact.',
    'Built once from exact H17 source 1dcedb0e0647e290bbc9ca86e3161919537b598a.',
    'Common payload equivalence and immutable public PREPROD 1.0.2 -> 1.0.3 in-place upgrade verified on a GitHub-hosted runner using synthetic data only.',
    '',
    'PHYSICAL THERMAL PRINTER ACCEPTANCE NOT RUN',
    'PRODUCTION DEPLOYMENT NOT AUTHORIZED',
    'Interactive WPF and owner-machine installation are NOT RUN.',
    'No tag, GitHub Release or PR merge performed. Controller review and a distinct publication task are required before owner download.',
    'Computer A remains Production + PREPROD; B remains Production only. Real two-PC PREPROD acceptance remains Deferred under owner waiver / NOT PASSED. M12 populated real archive acceptance remains separately Deferred.'
) | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'README.md') -Encoding utf8
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $ArtifactDirectory | Out-Host
$files = @(Get-ChildItem -LiteralPath $ArtifactDirectory -File)
if ($files.Count -ne 7 -or @($files | Where-Object Extension -CEQ '.exe').Count -ne 1 -or
    -not (Test-Path -LiteralPath $installerCopy -PathType Leaf)) { throw 'Candidate root inventory is not exactly PREPROD installer plus six evidence files.' }
$evidence
