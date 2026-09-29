Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Accepted = [pscustomobject]@{
    repository = 'cimerosef/sushi81-pos'
    tag = 'v1.0.1-preprod-c01'
    releaseId = 398945249L
    sourceSha = '95e2ead7d95af5fb47f6b162fde4d22d8ea47d82'
    candidateId = 'C01'
    version = '1.0.1'
    runtime = 'win-x64'
    fileCount = 418
    treeSha256 = 'aa0cf2c1aa2c2292c2bd49bec542e7db3a262b1d8dd09c08f4419107e6e14cc5'
    assets = @(
        [pscustomobject]@{ id=597676924L; name='application-payload.zip'; size=70025059L; digest='sha256:e533fbd50064902da9b38d7110ded955dfe78deb6648db352917077881e7b1f1' },
        [pscustomobject]@{ id=597676923L; name='payload-manifest.json'; size=72773L; digest='sha256:9ba12e4ecca0241d41c66dea8a253707f3c2ee07f56c717b7b6c19070a29288a' },
        [pscustomobject]@{ id=597676921L; name='package-summary.json'; size=2506L; digest='sha256:c5e86730ec016d9b78dc66ff3313f861d9a99d0ca36af2f3d3537aaffc4e8226' },
        [pscustomobject]@{ id=597676922L; name='release-provenance.json'; size=1700L; digest='sha256:20e3b0eac98362e602c4e226831906186f0cc1b93d28df2d4b40c0bc3f986f68' },
        [pscustomobject]@{ id=597676920L; name='Sushi81POS-PREPROD-Setup-1.0.1-95e2ead.exe'; size=49816320L; digest='sha256:a895d9b464484c1de669aaf31484b9020a179583f9e35c9495d5d4d75bf691f6' }
    )
}

function Get-M14AcceptedPromotionIdentity { $script:Accepted }

function Assert-M14AcceptedRelease {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Release,[Parameter(Mandatory)][object]$TagRef,
          [Parameter(Mandatory)][string]$Repository,[Parameter(Mandatory)][string]$Tag,
          [Parameter(Mandatory)][long]$ExpectedReleaseId,[Parameter(Mandatory)][string]$ExpectedSourceSha)
    $a = $script:Accepted
    if ($Repository -cne $a.repository -or $Tag -cne $a.tag -or $ExpectedReleaseId -ne $a.releaseId -or
        $ExpectedSourceSha -cne $a.sourceSha) { throw 'Promotion input differs from the approved C01 identity.' }
    if ($null -eq $Release -or [long]$Release.id -ne $a.releaseId -or $Release.tag_name -cne $a.tag -or
        [string]$Release.target_commitish -cne $a.sourceSha -or $Release.draft -isnot [bool] -or $Release.draft -or
        $Release.prerelease -isnot [bool] -or -not $Release.prerelease -or
        $Release.immutable -isnot [bool] -or -not $Release.immutable) {
        throw 'C01 Release ID, state, tag or source identity is not the accepted immutable candidate.'
    }
    if ($null -eq $TagRef -or $TagRef.ref -cne "refs/tags/$($a.tag)" -or
        $TagRef.object.type -cne 'commit' -or $TagRef.object.sha -cne $a.sourceSha) {
        throw 'C01 Git tag is not a direct commit ref to the accepted source.'
    }
    $assets = @($Release.assets)
    if ($assets.Count -ne $a.assets.Count) { throw 'C01 Release has a missing or extra asset.' }
    $seenIds = [Collections.Generic.HashSet[long]]::new()
    $seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $assets) {
        if (-not $seenIds.Add([long]$asset.id) -or -not $seenNames.Add([string]$asset.name)) { throw 'C01 Release contains duplicate asset identity.' }
        $expected = @($a.assets | Where-Object { $_.id -eq [long]$asset.id })
        if ($expected.Count -ne 1 -or $asset.name -cne $expected[0].name -or [long]$asset.size -ne $expected[0].size -or
            $asset.digest -cne $expected[0].digest -or $asset.state -cne 'uploaded' -or
            [string]$asset.browser_download_url -cne "https://github.com/$Repository/releases/download/$Tag/$($expected[0].name)") {
            throw "C01 Release asset ID/name/size/digest/state/URL differs from accepted evidence: $($asset.name)."
        }
    }
    $a
}

function Assert-M14AcceptedPayloadMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Manifest,[Parameter(Mandatory)][object]$Summary,[Parameter(Mandatory)][object]$Provenance)
    $a = $script:Accepted
    if ($Manifest.schemaVersion -ne 1 -or $Manifest.candidateId -cne $a.candidateId -or
        $Manifest.sourceHeadSha -cne $a.sourceSha -or $Manifest.productVersion -cne $a.version -or
        $Manifest.runtimeIdentifier -cne $a.runtime -or [int]$Manifest.fileCount -ne $a.fileCount -or
        @($Manifest.files).Count -ne $a.fileCount -or $Manifest.applicationPayloadTreeSha256 -cne $a.treeSha256) {
        throw 'C01 payload manifest is not the accepted source/candidate/product/runtime/tree.'
    }
    if ($Summary.candidateId -cne $a.candidateId -or $Summary.sourceHeadSha -cne $a.sourceSha -or
        $Summary.productVersion -cne $a.version -or $Summary.runtimeIdentifier -cne $a.runtime -or
        [int]$Summary.payloadFileCount -ne $a.fileCount -or $Summary.payloadTreeSha256 -cne $a.treeSha256 -or
        $Summary.payloadManifestSha256 -cne $a.assets[1].digest.Substring(7) -or
        $Summary.applicationPayloadArchive.sha256 -cne $a.assets[0].digest.Substring(7) -or
        [long]$Summary.applicationPayloadArchive.bytes -ne $a.assets[0].size -or
        $Summary.preprodInstaller.sha256 -cne $a.assets[4].digest.Substring(7) -or
        $Summary.releaseProvenanceSha256 -cne $a.assets[3].digest.Substring(7)) {
        throw 'C01 package summary does not match accepted payload identity.'
    }
    if ($Provenance.candidateId -cne $a.candidateId -or $Provenance.sourceHeadSha -cne $a.sourceSha -or
        $Provenance.productVersion -cne $a.version -or $Provenance.runtimeIdentifier -cne $a.runtime -or
        [int]$Provenance.payloadFileCount -ne $a.fileCount -or $Provenance.payloadTreeSha256 -cne $a.treeSha256 -or
        $Provenance.payloadManifestSha256 -cne $a.assets[1].digest.Substring(7) -or
        $Provenance.applicationPayloadArchive.sha256 -cne $a.assets[0].digest.Substring(7) -or
        $Provenance.preprodInstaller.sha256 -cne $a.assets[4].digest.Substring(7)) {
        throw 'C01 release provenance does not match accepted source/candidate/product/runtime.'
    }
}

function Assert-M14DownloadedAsset {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][object]$Asset)
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -ne [long]$Asset.size -or
        "sha256:$((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant())" -cne [string]$Asset.digest) {
        throw "Downloaded C01 asset differs in byte length or SHA-256: $($Asset.name)."
    }
}

function Assert-M14PromotionBoundary {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    $paths = @('installer/scripts/Promote-M14-Candidate.ps1','installer/scripts/M14-ProductionPromotion.psm1',
        'installer/scripts/Package-ProductionFromPayload.ps1','installer/scripts/Verify-ProductionPromotionLifecycle.ps1',
        '.github/workflows/m14-production-promotion.yml')
    foreach ($relative in $paths) {
        $path = Join-Path $RepoRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing promotion boundary file '$relative'." }
        $content = Get-Content -LiteralPath $path -Raw
        # Keep the executable promotion path free of application project tool invocations.
        if ($content -match '(?i)\bdotnet(?:\.exe)?\b') {
            throw "Promotion boundary contains an application restore/build/publish invocation: '$relative'."
        }
    }
    $workflow = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/m14-production-promotion.yml') -Raw
    if ($workflow -match '(?im)^\s*contents:\s*write\s*$' -or $workflow -match '(?im)^\s*(?:push|release|create):\s*$') {
        throw 'Promotion workflow must be read-only and manually dispatched.'
    }
    $ci = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/ci.yml') -Raw
    if ($ci -cnotmatch '(?ms)^  m14-wp6-promotion-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)') {
        throw 'Ordinary PR CI lacks the controlled WP6 promotion evidence job.'
    }
    $job = $Matches['job']
    if ($job -match '(?i)\bdotnet(?:\.exe)?\b' -or $job -match '(?im)^\s*contents:\s*write\s*$') {
        throw 'PR promotion evidence job must neither rebuild the application nor use a write token.'
    }
}

Export-ModuleMember -Function Get-M14AcceptedPromotionIdentity,Assert-M14AcceptedRelease,Assert-M14AcceptedPayloadMetadata,Assert-M14DownloadedAsset,Assert-M14PromotionBoundary
