[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$CompilerPath,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { throw 'RUNNER_TEMP is required; dual-installer lifecycle automation is hosted-runner-only.' }
$actualHead = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualHead -ne $ExpectedSourceSha) {
    throw "Expected exact source head '$ExpectedSourceSha', found '$actualHead'."
}
$sessionRoot = Join-Path $env:RUNNER_TEMP "sushi81-m14-wp2-$([guid]::NewGuid().ToString('N'))"
$packageOutput = Join-Path $sessionRoot 'package'
$lifecycleSummary = Join-Path $sessionRoot 'installer-lifecycle-summary.json'
$artifactRoot = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$buildScript = Join-Path $PSScriptRoot 'Build-InstallerPackage.ps1'
& $buildScript -ExpectedSourceSha $ExpectedSourceSha -SourceRoot $repoRoot `
    -OutputDirectory $packageOutput -CompilerPath $CompilerPath -ProductVersion '1.0.1' `
    -IncludePreProduction | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'M14 WP2 dual-profile package build failed.' }

$packageSummaryPath = Join-Path $packageOutput 'installer\package-summary.json'
$manifestPath = Join-Path $packageOutput 'installer\application-payload-manifest.json'
$package = Get-Content -LiteralPath $packageSummaryPath -Raw | ConvertFrom-Json
if ($package.sourceHeadSha -ne $ExpectedSourceSha.ToLowerInvariant() -or $package.productVersion -ne '1.0.1') {
    throw 'Dual-profile package summary does not match the exact WP2 source/version.'
}
if ($package.payloadEquality.verified -ne $true -or
    $package.payloadEquality.excludedPackagingOnlyFiles.Count -ne 1 -or
    $package.payloadEquality.excludedPackagingOnlyFiles[0] -cne 'deployment-profile.txt') {
    throw 'Prod/PreProd payload equality evidence is missing or excludes an unexpected file.'
}
$prod = $package.profilePackages.prod
$preprod = $package.profilePackages.preprod
if ($null -eq $prod -or $null -eq $preprod -or
    $prod.appId -ne 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -or
    $preprod.appId -ne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' -or
    $prod.deploymentProfileEvidence -cne 'prod' -or $preprod.deploymentProfileEvidence -cne 'preprod') {
    throw 'Dual-profile package summary does not contain the frozen AppIds and profile markers.'
}

$lifecycleScript = Join-Path $PSScriptRoot 'Verify-InstallerLifecycle.ps1'
& $lifecycleScript -CurrentInstaller $prod.installerPath `
    -PreProductionInstaller $preprod.installerPath `
    -ExpectedSourceSha $ExpectedSourceSha `
    -ExpectedProductVersion '1.0.1' `
    -PayloadManifest $manifestPath `
    -OutputSummary $lifecycleSummary | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Hosted-runner dual-installer lifecycle verification failed.' }

foreach ($path in @(
    $prod.installerPath,
    $preprod.installerPath,
    (Join-Path $packageOutput 'installer\release-provenance.json'),
    $manifestPath,
    $packageSummaryPath,
    $lifecycleSummary
)) {
    Copy-Item -LiteralPath $path -Destination $artifactRoot
}
$publishedProd = Join-Path $artifactRoot (Split-Path -Leaf $prod.installerPath)
$publishedPreProd = Join-Path $artifactRoot (Split-Path -Leaf $preprod.installerPath)
if ((Get-FileHash -LiteralPath $publishedProd -Algorithm SHA256).Hash.ToLowerInvariant() -ne $prod.installerSha256 -or
    (Get-FileHash -LiteralPath $publishedPreProd -Algorithm SHA256).Hash.ToLowerInvariant() -ne $preprod.installerSha256) {
    throw 'Uploaded installer copy hash differs from its compiled package summary.'
}

$lifecycle = Get-Content -LiteralPath $lifecycleSummary -Raw | ConvertFrom-Json
@(
    '',
    '## M14 WP2 dual Prod/PreProd installer verification',
    '',
    "Exact source head: $ExpectedSourceSha",
    'Product version: 1.0.1',
    'Inno Setup: 6.7.3',
    "Prod AppId/install identity: $($prod.appId) / Sushi81 POS / %LOCALAPPDATA%\\Programs\\Sushi81 POS",
    "PreProd AppId/install identity: $($preprod.appId) / Sushi81 POS PREPROD / %LOCALAPPDATA%\\Programs\\Sushi81 POS PREPROD",
    "Prod installer SHA-256: $($prod.installerSha256)",
    "PreProd installer SHA-256: $($preprod.installerSha256)",
    "Common application payload: $($package.payloadEquality.commonApplicationFileCount) byte-identical files; only deployment-profile.txt excluded; tree SHA-256 $($package.applicationPayloadTreeSha256)",
    "Lifecycle steps passed: $($lifecycle.steps.Count)",
    'Both durable roots were populated with synthetic fixtures and remained byte-identical through install, repair, uninstall and reinstall.',
    'No real production/customer/order/payment data was used.',
    'Interactive WPF launch smoke: deferred to owner acceptance; no headless UI acceptance claimed.'
) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8

$package
