[CmdletBinding()]
param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PreProdPrintCandidate.psm1') -Force
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$accepted = Get-AcceptedPreProdV102Installer
# Literal metadata keeps the test independent of the module's pinned constants.
$release = [pscustomobject]@{
    id = 402567122L
    name = 'Sushi81 POS PREPROD 1.0.2'
    tag_name = 'v1.0.2-preprod'
    target_commitish = '7183c4798f6889fe10af8dfec90bed5b12a26529'
    draft = $false
    prerelease = $true
    immutable = $true
    assets = @([pscustomobject]@{
        id = 608021301L
        name = 'Sushi81POS-PREPROD-Setup-1.0.2-7183c47.exe'
        size = 49822849L
        digest = 'sha256:a80acb102767887e1e06826ca82bfc655b34ff215dc7bbb89a159245db0e007c'
        browser_download_url = 'https://github.com/cimerosef/sushi81-pos/releases/download/v1.0.2-preprod/Sushi81POS-PREPROD-Setup-1.0.2-7183c47.exe'
    })
}
$script:checks = 0
function Expect-Rejection([scriptblock]$Action, [string]$Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
    $script:checks++
}
function Copy-FixtureRelease {
    $release | ConvertTo-Json -Depth 8 | ConvertFrom-Json
}
Assert-AcceptedPreProdV102Release -Release $release | Out-Null
$script:checks++
foreach ($mutation in @(
    @{path='id';value=1L}, @{path='name';value='Substitute PREPROD Release'},
    @{path='tag_name';value='v1.0.2'}, @{path='target_commitish';value=('0'*40)},
    @{path='draft';value=$true}, @{path='prerelease';value=$false}, @{path='immutable';value=$false},
    @{path='draft';value='false'}, @{path='prerelease';value='true'}, @{path='immutable';value='true'}
)) {
    $changed = Copy-FixtureRelease
    $changed.($mutation.path) = $mutation.value
    Expect-Rejection { Assert-AcceptedPreProdV102Release -Release $changed } ('release ' + $mutation.path)
}
foreach ($mutation in @(
    @{path='id';value=1L}, @{path='name';value='Sushi81POS-Setup-1.0.2-7183c47.exe'},
    @{path='size';value=1L}, @{path='digest';value=('sha256:' + ('0'*64))},
    @{path='browser_download_url';value='https://example.invalid/substitute.exe'}
)) {
    $changed = Copy-FixtureRelease
    $changed.assets[0].($mutation.path) = $mutation.value
    Expect-Rejection { Assert-AcceptedPreProdV102Release -Release $changed } ('asset ' + $mutation.path)
}
$changed = Copy-FixtureRelease
$changed.assets = @()
Expect-Rejection { Assert-AcceptedPreProdV102Release -Release $changed } 'missing asset'
$changed = Copy-FixtureRelease
$changed.assets = @($release.assets[0],$release.assets[0])
Expect-Rejection { Assert-AcceptedPreProdV102Release -Release $changed } 'duplicate accepted asset'
$changed = Copy-FixtureRelease
$changed.assets = @($release.assets[0], [pscustomobject]@{ id=1L; name='unexpected-production-installer.exe'; size=1L; digest=('sha256:' + ('0'*64)); browser_download_url='https://example.invalid/extra.exe' })
Expect-Rejection { Assert-AcceptedPreProdV102Release -Release $changed } 'extra non-PREPROD asset'
# Check the approved workflow before creating isolated synthetic workflow mutations.
Assert-PreProdPrintCandidateBoundary -RepoRoot $repoRoot
$script:checks++
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('sushi81-preprod-print-selftest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $badFixture = Join-Path $tmp $accepted.name
    [IO.File]::WriteAllText($badFixture, 'synthetic wrong installer')
    Expect-Rejection { Assert-AcceptedPreProdV102Installer -Path $badFixture } 'wrong installer length'
    $stream = [IO.File]::OpenWrite($badFixture)
    try { $stream.SetLength($accepted.bytes) } finally { $stream.Dispose() }
    Expect-Rejection { Assert-AcceptedPreProdV102Installer -Path $badFixture } 'same-size wrong installer hash'
    $wrongName = Join-Path $tmp 'substitute.exe'
    [IO.File]::WriteAllText($wrongName, 'synthetic substitute')
    Expect-Rejection { Assert-AcceptedPreProdV102Installer -Path $wrongName } 'wrong installer filename'
    Expect-Rejection { Assert-AcceptedPreProdV102Installer -Path (Join-Path $tmp 'missing.exe') } 'missing installer'
    $workflowRoot = Join-Path $tmp 'workflow-root'
    $testWorkflow = Join-Path $workflowRoot '.github/workflows/ci.yml'
    New-Item -ItemType Directory -Path (Split-Path -Parent $testWorkflow) -Force | Out-Null
    $originalWorkflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
    $jobMatch = [regex]::Match($originalWorkflow, '(?ms)^  preprod-print-103-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)')
    if (-not $jobMatch.Success) { throw 'Current H18 workflow job is missing from the self-test fixture.' }
    $originalJob = $jobMatch.Groups['job'].Value
    $jobPrefix = $originalWorkflow.Substring(0, $jobMatch.Groups['job'].Index)
    $jobSuffix = $originalWorkflow.Substring($jobMatch.Groups['job'].Index + $jobMatch.Groups['job'].Length)
    function Write-WorkflowFixture([string]$Workflow) {
        [IO.File]::WriteAllText($testWorkflow, $Workflow, [Text.UTF8Encoding]::new($false))
    }
    Write-WorkflowFixture $originalWorkflow
    Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot
    $script:checks++
    foreach ($required in @(
        "github.event_name == 'workflow_dispatch'",
        "inputs.evidence_package == 'preprod-print-103'",
        "github.ref == 'refs/heads/codex/post-m14-production-maintenance-batch-01'",
        'needs: build-and-test', 'contents: read', 'inputs.expected_head', '^[0-9a-f]{40}$',
        'persist-credentials: false',
        'git diff --exit-code 1dcedb0e0647e290bbc9ca86e3161919537b598a HEAD -- src/',
        'Run-PreProdPrintCandidate.ps1', 'Test-PreProdPrintCandidate.ps1',
        '$version = ''6.7.3''', 'Get-AuthenticodeSignature'
    )) {
        if (-not $originalJob.Contains($required)) { throw "Self-test fixture lacks required H18 text: $required" }
        Write-WorkflowFixture ($jobPrefix + $originalJob.Replace($required, 'REMOVED_H18_BOUNDARY') + $jobSuffix)
        Expect-Rejection { Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot } ('missing H18 workflow boundary: ' + $required)
    }
    Write-WorkflowFixture ($originalWorkflow.Replace('  preprod-print-103-evidence:', '  removed-preprod-print-103-evidence:'))
    Expect-Rejection { Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot } 'missing H18 job'
    $topPermissions = [regex]::new('(?m)^permissions:\r?\n  contents: read[ \t]*\r?\n')
    if (-not $topPermissions.IsMatch($originalWorkflow)) { throw 'Top-level read-only permission fixture is missing.' }
    Write-WorkflowFixture ($topPermissions.Replace($originalWorkflow, "permissions:`n  contents: write`n", 1))
    Expect-Rejection { Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot } 'top-level contents write permission'
    Write-WorkflowFixture ($topPermissions.Replace($originalWorkflow, "permissions: write-all`n", 1))
    Expect-Rejection { Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot } 'top-level write-all permission'
    foreach ($command in @('gh release create forbidden-fixture','git tag forbidden-fixture','git push origin forbidden-fixture','Publish-M14-Candidate.ps1')) {
        # These commands are text in an isolated fixture; they are never executed.
        $unsafeStep = "`n      - name: Forbidden synthetic publication step`n        run: $command`n"
        Write-WorkflowFixture ($jobPrefix + $originalJob + $unsafeStep + $jobSuffix)
        Expect-Rejection { Assert-PreProdPrintCandidateBoundary -RepoRoot $workflowRoot } ('forbidden H18 workflow command: ' + $command)
    }
    function Assert-H12DispatchSelector([string]$Workflow) {
        $h12Match = [regex]::Match($Workflow, '(?ms)^  production-patch-102-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)')
        if (-not $h12Match.Success -or
            -not $h12Match.Groups['job'].Value.Contains("inputs.evidence_package == 'production-patch-102'")) {
            throw 'The historical H12 job must retain its separate manual package selector.'
        }
    }
    # This additional routing assertion stays in the self-test; the module is unchanged.
    Assert-H12DispatchSelector $originalWorkflow
    $script:checks++
    $h12Changed = $originalWorkflow.Replace("inputs.evidence_package == 'production-patch-102'", 'REMOVED_H12_SELECTOR')
    Expect-Rejection { Assert-H12DispatchSelector $h12Changed } 'missing historical H12 package selector'
    "PREPROD print candidate safeguards passed ($script:checks checks; fixture metadata/file rejection, isolated H18 workflow mutations and H12 selector retention)."
} finally {
    # Only the unique synthetic directory allocated above may be removed.
    $resolved = [IO.Path]::GetFullPath($tmp)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^sushi81-preprod-print-selftest-[0-9a-f]{32}$') {
        throw 'Unexpected PREPROD self-test cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
