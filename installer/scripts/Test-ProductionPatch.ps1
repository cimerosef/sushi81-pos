[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ProductionPatch.psm1') -Force
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$accepted = Get-AcceptedV101Installer
$release = [pscustomobject]@{
    id = $accepted.releaseId; tag_name = $accepted.tag; target_commitish = $accepted.sourceSha
    draft = $false; prerelease = $false; immutable = $true
    assets = @([pscustomobject]@{ id=$accepted.assetId; name=$accepted.name; size=$accepted.bytes; digest="sha256:$($accepted.sha256)"; browser_download_url=$accepted.url })
}
$script:checks = 0
function Expect-Rejection([scriptblock]$Action, [string]$Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
    $script:checks++
}
Assert-AcceptedV101Release -Release $release | Out-Null
$script:checks++
foreach ($mutation in @(
    @{path='id';value=1L}, @{path='tag_name';value='v1.0.0'}, @{path='target_commitish';value=('0'*40)},
    @{path='draft';value=$true}, @{path='prerelease';value=$true}, @{path='immutable';value=$false}
)) {
    $changed = $release | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $changed.($mutation.path) = $mutation.value
    Expect-Rejection { Assert-AcceptedV101Release -Release $changed } $mutation.path
}
foreach ($mutation in @(
    @{path='id';value=1L}, @{path='name';value='substitute.exe'}, @{path='size';value=1L},
    @{path='digest';value=('sha256:' + ('0'*64))}, @{path='browser_download_url';value='https://example.invalid/substitute.exe'}
)) {
    $changed = $release | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $changed.assets[0].($mutation.path) = $mutation.value
    Expect-Rejection { Assert-AcceptedV101Release -Release $changed } ('asset ' + $mutation.path)
}
$changed = $release | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$changed.assets = @()
Expect-Rejection { Assert-AcceptedV101Release -Release $changed } 'missing asset'
$changed.assets = @($release.assets[0],$release.assets[0])
Expect-Rejection { Assert-AcceptedV101Release -Release $changed } 'duplicate asset'
Assert-ProductionPatchBoundary -RepoRoot $repoRoot
$script:checks++
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('sushi81-patch-selftest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $badFixture = Join-Path $tmp $accepted.name
    [IO.File]::WriteAllText($badFixture, 'synthetic wrong installer')
    Expect-Rejection { Assert-AcceptedV101Installer -Path $badFixture } 'wrong installer length'
    $stream = [IO.File]::OpenWrite($badFixture)
    try { $stream.SetLength($accepted.bytes) } finally { $stream.Dispose() }
    Expect-Rejection { Assert-AcceptedV101Installer -Path $badFixture } 'same-size wrong installer hash'
    $rejectedOutput = Join-Path $tmp 'must-not-exist'
    Expect-Rejection {
        & (Join-Path $PSScriptRoot 'Run-ProductionPatch.ps1') -ExpectedSourceSha ('0'*40) -CompilerPath 'missing' -OutputDirectory $rejectedOutput -ArtifactDirectory (Join-Path $tmp 'must-not-exist-artifact')
    } 'unauthorized host or wrong dispatch SHA before packaging'
    if (Test-Path -LiteralPath $rejectedOutput) { throw 'Rejected dispatch created output.' }
    Expect-Rejection {
        & (Join-Path $PSScriptRoot 'Verify-InstallerLifecycle.ps1') -CurrentInstaller $badFixture -PreviousInstaller $badFixture -PreProductionInstaller $badFixture -ExpectedSourceSha ('0'*40) -ExpectedProductVersion '1.0.2' -PayloadManifest 'missing' -OutputSummary $rejectedOutput
    } 'wrong historical installer before lifecycle fixture creation'
    $testWorkflow = Join-Path $tmp '.github/workflows/ci.yml'
    New-Item -ItemType Directory -Path (Split-Path -Parent $testWorkflow) -Force | Out-Null
    $original = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
    foreach ($pair in @(
        @("github.event_name == 'workflow_dispatch'", "github.event_name == 'push'"),
        @("github.ref == 'refs/heads/codex/post-m14-production-maintenance-batch-01'", "github.ref == 'refs/heads/main'"),
        @('needs: build-and-test','needs: missing'), @('contents: read','contents: write'),
        @('inputs.expected_head','inputs.unchecked'), @('persist-credentials: false','persist-credentials: true'),
        @('git diff --exit-code b516aba0652e0e3be3c65adcac7f6e41ef706dbe HEAD -- src/','git status'),
        @('Get-AuthenticodeSignature','UnverifiedSignature')
    )) {
        [IO.File]::WriteAllText($testWorkflow, $original.Replace($pair[0],$pair[1]))
        Expect-Rejection { Assert-ProductionPatchBoundary -RepoRoot $tmp } ('workflow mutation: ' + $pair[0])
    }
    foreach ($name in @('Run-ProductionPatch.ps1','ProductionPatch.psm1','Verify-InstallerLifecycle.ps1','Test-ProductionPatch.ps1')) {
        $parseTokens = $null; $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$parseTokens, [ref]$parseErrors)
        if ($parseErrors.Count -ne 0) { throw "PowerShell parse errors in ${name}: $parseErrors" }
        $script:checks++
    }
    "Production patch safeguards passed ($script:checks checks)."
} finally {
    # Only the unique synthetic directory allocated above may be removed.
    $resolved = [IO.Path]::GetFullPath($tmp)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^sushi81-patch-selftest-[0-9a-f]{32}$') { throw 'Unexpected self-test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
