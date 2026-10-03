Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AcceptedV101Installer {
    [pscustomobject]@{
        releaseId = 401752460L
        assetId = 605444162L
        tag = 'v1.0.1'
        sourceSha = '0aa3a0282a432da38bff9b35b38ffa03cf3bbada'
        name = 'Sushi81POS-PROD-Setup-1.0.1-C02-2a8958a.exe'
        bytes = 49815089L
        sha256 = '0fa482c28c62a5d7e225f5a8796b2febe241b2e26b102f3a01beeb149546db3e'
        url = 'https://github.com/cimerosef/sushi81-pos/releases/download/v1.0.1/Sushi81POS-PROD-Setup-1.0.1-C02-2a8958a.exe'
    }
}

function Assert-AcceptedV101Release {
    param([Parameter(Mandatory)][object]$Release)
    $expected = Get-AcceptedV101Installer
    if ($Release.id -ne $expected.releaseId -or $Release.tag_name -cne $expected.tag -or
        $Release.target_commitish -cne $expected.sourceSha -or $Release.draft -ne $false -or
        $Release.prerelease -ne $false -or $Release.immutable -ne $true) { throw 'The upgrade fixture Release is not the accepted immutable Production v1.0.1.' }
    $assets = @($Release.assets | Where-Object name -CEQ $expected.name)
    if ($assets.Count -ne 1 -or $assets[0].id -ne $expected.assetId -or $assets[0].size -ne $expected.bytes -or
        $assets[0].digest -cne "sha256:$($expected.sha256)" -or $assets[0].browser_download_url -cne $expected.url) {
        throw 'The upgrade fixture asset identity, size, digest or download route differs.'
    }
    return $expected
}

function Assert-AcceptedV101Installer {
    param([Parameter(Mandatory)][string]$Path)
    $expected = Get-AcceptedV101Installer
    $file = Get-Item -LiteralPath $Path
    if ($file.Name -cne $expected.name -or $file.Length -ne $expected.bytes -or
        (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected.sha256) {
        throw 'The local upgrade fixture is not the exact accepted v1.0.1 installer bytes.'
    }
    return $expected
}

function Assert-ProductionPatchBoundary {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $workflow = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/ci.yml') -Raw
    if ($workflow -notmatch '(?ms)^  production-patch-102-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)') { throw 'Patch job is missing.' }
    $job = $Matches['job']
    foreach ($required in @("github.event_name == 'workflow_dispatch'", "github.ref == 'refs/heads/codex/post-m14-production-maintenance-batch-01'",
        'needs: build-and-test', 'contents: read', 'inputs.expected_head', 'persist-credentials: false', 'git diff --exit-code b516aba0652e0e3be3c65adcac7f6e41ef706dbe HEAD -- src/',
        'Run-ProductionPatch.ps1', 'Test-ProductionPatch.ps1', '$version = ''6.7.3''', 'Get-AuthenticodeSignature')) {
        if (-not $job.Contains($required)) { throw "Patch job lacks required boundary: $required" }
    }
    if ($workflow -match '(?im)^\s*contents:\s*write\s*$' -or $job -match '(?i)gh\s+release|git\s+tag|Publish-M14-Candidate') { throw 'Patch workflow must not publish or gain write permission.' }
}

Export-ModuleMember -Function Get-AcceptedV101Installer,Assert-AcceptedV101Release,Assert-AcceptedV101Installer,Assert-ProductionPatchBoundary
