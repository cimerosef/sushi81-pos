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
$previousSourceSha = 'e40a1f882d4c0557fc0fe4e30cb88395be290c89'
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { throw 'RUNNER_TEMP is required; lifecycle automation is hosted-runner-only.' }
$runnerTemp = [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
$sessionRoot = Join-Path $runnerTemp "sushi81-m13-wp4-$([guid]::NewGuid().ToString('N'))"
$previousRoot = Join-Path $sessionRoot 'wp3-source'
$currentOutput = Join-Path $sessionRoot 'current-package'
$previousOutput = Join-Path $sessionRoot 'previous-package'
$artifactRoot = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$worktreeAdded = $false

New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
try {
    & git -C $repoRoot worktree add --detach $previousRoot $previousSourceSha
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the WP3-source installer-mechanics worktree.' }
    $worktreeAdded = $true

    $buildScript = Join-Path $PSScriptRoot 'Build-InstallerPackage.ps1'
    & $buildScript -ExpectedSourceSha $ExpectedSourceSha -SourceRoot $repoRoot -OutputDirectory $currentOutput -CompilerPath $CompilerPath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Current production installer packaging failed.' }
    & $buildScript -ExpectedSourceSha $previousSourceSha -SourceRoot $previousRoot -OutputDirectory $previousOutput -CompilerPath $CompilerPath -ProductVersion '0.9.0' | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'WP3-source installer-mechanics fixture packaging failed.' }

    $current = Get-Content -LiteralPath (Join-Path $currentOutput 'installer\package-summary.json') -Raw | ConvertFrom-Json
    $previous = Get-Content -LiteralPath (Join-Path $previousOutput 'installer\package-summary.json') -Raw | ConvertFrom-Json

    $lifecycleSummary = Join-Path $sessionRoot 'installer-lifecycle-summary.json'
    & (Join-Path $PSScriptRoot 'Verify-InstallerLifecycle.ps1') `
        -CurrentInstaller $current.installerPath `
        -PreviousInstaller $previous.installerPath `
        -ExpectedSourceSha $ExpectedSourceSha `
        -PreviousSourceSha $previousSourceSha `
        -OutputSummary $lifecycleSummary | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Hosted-runner installer lifecycle verification failed.' }

    Copy-Item -LiteralPath $current.installerPath -Destination $artifactRoot
    Copy-Item -LiteralPath $current.releaseProvenancePath -Destination (Join-Path $artifactRoot 'release-provenance.json')
    Copy-Item -LiteralPath $lifecycleSummary -Destination (Join-Path $artifactRoot 'installer-lifecycle-summary.json')
    $publishedSummary = Join-Path $artifactRoot 'package-summary.json'
    $current | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $publishedSummary -Encoding utf8

    $publishedInstaller = Join-Path $artifactRoot (Split-Path -Leaf $current.installerPath)
    $actualInstallerHash = (Get-FileHash -LiteralPath $publishedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualInstallerHash -ne $current.installerSha256) { throw 'Artifact copy SHA-256 differs from the compiled installer.' }
    $lifecycle = Get-Content -LiteralPath $lifecycleSummary -Raw | ConvertFrom-Json
    @(
        '',
        '## M13 WP4 production installer',
        '',
        "Source head: $ExpectedSourceSha",
        "Product/file version: $($current.productVersion) / $($current.fileVersion)",
        "RID/self-contained/single-file: $($current.runtimeIdentifier) / $($current.selfContained) / $($current.publishSingleFile)",
        "Inno Setup: $($current.innoSetupVersion)",
        "Installer: $($current.installerFileName); bytes: $($current.installerBytes); SHA-256: $($current.installerSha256)",
        "Provenance SHA-256: $($current.releaseProvenanceSha256)",
        "Previous-version package: WP3 source $($previous.sourceHeadSha) built as 0.9.0 for installer-mechanics evidence only.",
        "Lifecycle steps passed: $($lifecycle.steps.Count)",
        'Forbidden-content scan: passed for publish and installed program trees.',
        'Interactive WPF launch smoke: deferred to owner acceptance; no headless UI acceptance claimed.'
    ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    $current | ConvertTo-Json -Depth 8
}
finally {
    if ($worktreeAdded) {
        & git -C $repoRoot worktree remove --force $previousRoot
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove temporary WP3 worktree '$previousRoot'." }
    }
}
