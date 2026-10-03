Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AcceptedPreProdV102Installer {
    [pscustomobject]@{
        releaseId = 402567122L
        releaseName = 'Sushi81 POS PREPROD 1.0.2'
        assetId = 608021301L
        tag = 'v1.0.2-preprod'
        sourceSha = '7183c4798f6889fe10af8dfec90bed5b12a26529'
        name = 'Sushi81POS-PREPROD-Setup-1.0.2-7183c47.exe'
        bytes = 49822849L
        sha256 = 'a80acb102767887e1e06826ca82bfc655b34ff215dc7bbb89a159245db0e007c'
        url = 'https://github.com/cimerosef/sushi81-pos/releases/download/v1.0.2-preprod/Sushi81POS-PREPROD-Setup-1.0.2-7183c47.exe'
    }
}

function Assert-AcceptedPreProdV102Release {
    param([Parameter(Mandatory)][object]$Release)
    $expected = Get-AcceptedPreProdV102Installer
    if ($Release.id -ne $expected.releaseId -or $Release.name -cne $expected.releaseName -or
        $Release.tag_name -cne $expected.tag -or $Release.target_commitish -cne $expected.sourceSha -or
        $Release.draft -isnot [bool] -or $Release.draft -ne $false -or
        $Release.prerelease -isnot [bool] -or $Release.prerelease -ne $true -or
        $Release.immutable -isnot [bool] -or $Release.immutable -ne $true) {
        throw 'The upgrade fixture Release is not the accepted immutable PREPROD v1.0.2.'
    }
    $assets = @($Release.assets)
    if ($assets.Count -ne 1 -or $assets[0].id -ne $expected.assetId -or
        $assets[0].name -cne $expected.name -or $assets[0].size -ne $expected.bytes -or
        $assets[0].digest -cne "sha256:$($expected.sha256)" -or
        $assets[0].browser_download_url -cne $expected.url) {
        throw 'The PREPROD upgrade fixture must have exactly its one accepted asset, identity, size, digest and download route.'
    }
    return $expected
}

function Assert-AcceptedPreProdV102Installer {
    param([Parameter(Mandatory)][string]$Path)
    $expected = Get-AcceptedPreProdV102Installer
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file.PSIsContainer -or $file.Name -cne $expected.name -or $file.Length -ne $expected.bytes -or
        (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected.sha256) {
        throw 'The local upgrade fixture is not the exact accepted PREPROD v1.0.2 installer bytes.'
    }
    return $expected
}

function Assert-PreProdPrintCandidateBoundary {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $workflow = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/ci.yml') -Raw
    if ($workflow -notmatch '(?ms)^  preprod-print-103-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)') {
        throw 'The H18 PREPROD print candidate job is missing.'
    }
    $job = $Matches['job']
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
        if (-not $job.Contains($required)) { throw "H18 job lacks required boundary: $required" }
    }
    if ($workflow -match '(?im)\bcontents:\s*write\b|^\s*permissions:\s*write-all\s*$' -or
        $job -match '(?i)\bgh\s+release\b|\bgit\s+tag\b|\bgit\s+push\b|Publish-M14-Candidate') {
        throw 'H18 packaging must remain read-only and must not create tags, Releases or pushes.'
    }
}

Export-ModuleMember -Function Get-AcceptedPreProdV102Installer,Assert-AcceptedPreProdV102Release,Assert-AcceptedPreProdV102Installer,Assert-PreProdPrintCandidateBoundary
