[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedSourceSha,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or
    $env:GITHUB_REPOSITORY -cne 'cimerosef/sushi81-pos' -or $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_REF -cne 'refs/heads/codex/post-m14-production-maintenance-batch-01' -or
    $ExpectedSourceSha -cnotmatch '^[0-9a-f]{40}$' -or $env:GITHUB_SHA -cne $ExpectedSourceSha) {
    throw 'Patch packaging requires the authorized manual exact-head hosted runner.'
}
$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $ExpectedSourceSha) { throw 'Patch checkout does not match the authorized head.' }
& git -C $repoRoot diff --exit-code b516aba0652e0e3be3c65adcac7f6e41ef706dbe HEAD -- src/
if ($LASTEXITCODE -ne 0) { throw 'Application source changed since accepted H11.' }
Import-Module (Join-Path $PSScriptRoot 'ProductionPatch.psm1') -Force
Assert-ProductionPatchBoundary -RepoRoot $repoRoot
foreach ($directory in @($OutputDirectory, $ArtifactDirectory)) {
    if (Test-Path -LiteralPath $directory) { throw 'Patch output must be a new directory.' }
}
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'Sushi81-Patch-Evidence' }
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cimerosef/sushi81-pos/releases/tags/v1.0.1' -Headers $headers
$fixture = Assert-AcceptedV101Release -Release $release
$tag = Invoke-RestMethod -Uri 'https://api.github.com/repos/cimerosef/sushi81-pos/git/ref/tags/v1.0.1' -Headers $headers
if ($tag.ref -cne 'refs/tags/v1.0.1' -or $tag.object.type -cne 'commit' -or $tag.object.sha -cne $fixture.sourceSha) {
    throw 'The historical Production tag no longer points directly to the accepted source.'
}
# A transport/auth error must not be interpreted as absence.
try {
    $null = Invoke-RestMethod -Uri 'https://api.github.com/repos/cimerosef/sushi81-pos/releases/tags/v1.0.2' -Headers $headers
    throw 'Production v1.0.2 Release already exists.'
} catch {
    if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
$existingTag = @(& git -C $repoRoot ls-remote --refs origin refs/tags/v1.0.2)
if ($LASTEXITCODE -ne 0 -or $existingTag.Count -ne 0) { throw 'Could not prove v1.0.2 tag absent.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$fixturePath = Join-Path $OutputDirectory $fixture.name
Invoke-WebRequest -Uri $fixture.url -OutFile $fixturePath
Assert-AcceptedV101Installer -Path $fixturePath | Out-Null

$buildRoot = Join-Path $OutputDirectory 'build'
& (Join-Path $PSScriptRoot 'Build-InstallerPackage.ps1') -ExpectedSourceSha $ExpectedSourceSha -SourceRoot $repoRoot -OutputDirectory $buildRoot -CompilerPath $CompilerPath -ProductVersion '1.0.2' -IncludePreProduction | Out-Host
$packageRoot = Join-Path $buildRoot 'installer'
$summary = Get-Content -LiteralPath (Join-Path $packageRoot 'package-summary.json') -Raw | ConvertFrom-Json
$lifecyclePath = Join-Path $OutputDirectory 'lifecycle-summary.json'
& (Join-Path $PSScriptRoot 'Verify-InstallerLifecycle.ps1') -CurrentInstaller $summary.profilePackages.prod.installerPath -PreviousInstaller $fixturePath -PreProductionInstaller $summary.profilePackages.preprod.installerPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedProductVersion '1.0.2' -PayloadManifest $summary.applicationPayloadManifestPath -OutputSummary $lifecyclePath | Out-Host
$lifecycle = Get-Content -LiteralPath $lifecyclePath -Raw | ConvertFrom-Json
if ($lifecycle.passed -ne $true -or $lifecycle.acceptedProductionUpgrade.passed -ne $true -or $summary.payloadEquality.verified -ne $true) { throw 'Patch package/lifecycle verification did not pass.' }

New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
foreach ($profile in @('prod','preprod')) {
    $package = $summary.profilePackages.$profile
    Copy-Item -LiteralPath $package.installerPath -Destination (Join-Path $ArtifactDirectory $package.installerFileName)
    $copy = Get-Item -LiteralPath (Join-Path $ArtifactDirectory $package.installerFileName)
    if ($copy.Length -ne $package.installerBytes -or
        (Get-FileHash -LiteralPath $copy.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $package.installerSha256) { throw 'Artifact installer copy differs from the verified package.' }
    $package.PSObject.Properties.Remove('installerPath')
}
foreach ($name in @('application-payload-manifest.json','release-provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $packageRoot $name) -Destination (Join-Path $ArtifactDirectory $name)
}
foreach ($property in @('publishDirectory','installerPath','releaseProvenancePath','applicationPayloadManifestPath')) { $summary.PSObject.Properties.Remove($property) }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'package-summary.json') -Encoding utf8
$lifecycle.prodInstallRoot = '%LOCALAPPDATA%\Programs\Sushi81 POS'
$lifecycle.preprodInstallRoot = '%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD'
$lifecycle.prodDurableDataRoot = '%LOCALAPPDATA%\Sushi81 POS'
$lifecycle.preprodDurableDataRoot = '%LOCALAPPDATA%\Sushi81 POS PREPROD'
$lifecycle | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'lifecycle-summary.json') -Encoding utf8
$evidence = [ordered]@{
    schemaVersion = 1
    sourceHeadSha = $ExpectedSourceSha
    acceptedRuntimeBaseline = 'b516aba0652e0e3be3c65adcac7f6e41ef706dbe'
    applicationSourceUnchanged = $true
    productVersion = '1.0.2'
    fileVersion = '1.0.2.0'
    informationalVersion = "1.0.2+$ExpectedSourceSha"
    workflowRunId = $env:GITHUB_RUN_ID
    workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    applicationPublishCount = 1
    profilePackages = $summary.profilePackages
    payloadFileCount = $summary.payloadEquality.commonApplicationFileCount
    payloadTreeSha256 = $summary.applicationPayloadTreeSha256
    payloadManifestSha256 = $summary.applicationPayloadManifestSha256
    acceptedUpgradeFixture = $fixture
    upgradePassed = $true
    lifecyclePassed = $true
    realBusinessDataUsed = $false
    realProductionDeploymentPerformed = $false
    preprodUsage = 'AUTOMATED ISOLATION EVIDENCE ONLY - NOT FOR OWNER MANUAL ACCEPTANCE'
    ownerFacingProfile = 'prod'
    releaseOrTagPublished = $false
    interactiveWpfAcceptance = 'NOT RUN - B owner acceptance required'
}
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'patch-evidence.json') -Encoding utf8
@'
# Production patch 1.0.2 - packaging evidence only

The owner-facing installer is Sushi81POS-Setup-1.0.2-<source>.exe (Production).
The PREPROD installer is AUTOMATED ISOLATION EVIDENCE ONLY - NOT FOR OWNER MANUAL ACCEPTANCE.
These files have not been published as a Release or deployed to A/B. Controller review is required before a distinct publication task.
The hosted lifecycle uses only synthetic durable files. It does not establish interactive WPF acceptance on computer B.
'@ | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'README.md') -Encoding utf8
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $ArtifactDirectory
