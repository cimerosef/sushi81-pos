Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Accepted = [pscustomobject]@{
    repository = 'cimerosef/sushi81-pos'
    tag = 'v1.0.1-preprod-c02'
    releaseId = 399541988L
    sourceSha = '0aa3a0282a432da38bff9b35b38ffa03cf3bbada'
    candidateId = 'C02'
    version = '1.0.1'
    runtime = 'win-x64'
    fileCount = 418
    treeSha256 = '006f5db911b71c5ecf7e61ffcaf42b9950d9196b4e19c0f9da49b5bb4b32fb6c'
    assets = @(
        [pscustomobject]@{ id=599265273L; name='application-payload.zip'; size=70028491L; digest='sha256:c814e7b9a17fcf31cbf3337e770b8c98327d49704f4b3eabb09d50f8a9edfa07' },
        [pscustomobject]@{ id=599265276L; name='payload-manifest.json'; size=72773L; digest='sha256:9cb7f8b13480e1b1664062bd1045f1ba759ab09fb65aafc8f6c3bf2ccaa668a6' },
        [pscustomobject]@{ id=599265277L; name='package-summary.json'; size=2506L; digest='sha256:65c0549979a7f1632e3fce0224a4dffaf4d8783a35fefd45bb4e84b0b9502cea' },
        [pscustomobject]@{ id=599265278L; name='release-provenance.json'; size=1700L; digest='sha256:cf56025d91bbe4184f878cb92da30ec23084d6524b2c850c211f23c72c17e5ed' },
        [pscustomobject]@{ id=599265272L; name='Sushi81POS-PREPROD-Setup-1.0.1-0aa3a02.exe'; size=49822179L; digest='sha256:3ef1346e857914e9898dacafe522c9308d5d740a9db00413c19b9415daa44e4a' }
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
        $ExpectedSourceSha -cne $a.sourceSha) { throw "Promotion input differs from the approved $($a.candidateId) identity." }
    if ($null -eq $Release -or [long]$Release.id -ne $a.releaseId -or $Release.tag_name -cne $a.tag -or
        [string]$Release.target_commitish -cne $a.sourceSha -or $Release.draft -isnot [bool] -or $Release.draft -or
        $Release.prerelease -isnot [bool] -or -not $Release.prerelease -or
        $Release.immutable -isnot [bool] -or -not $Release.immutable) {
        throw "$($a.candidateId) Release ID, state, tag or source identity is not the accepted immutable candidate."
    }
    if ($null -eq $TagRef -or $TagRef.ref -cne "refs/tags/$($a.tag)" -or
        $TagRef.object.type -cne 'commit' -or $TagRef.object.sha -cne $a.sourceSha) {
        throw "$($a.candidateId) Git tag is not a direct commit ref to the accepted source."
    }
    $assets = @($Release.assets)
    if ($assets.Count -ne $a.assets.Count) { throw "$($a.candidateId) Release has a missing or extra asset." }
    $seenIds = [Collections.Generic.HashSet[long]]::new()
    $seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $assets) {
        if (-not $seenIds.Add([long]$asset.id) -or -not $seenNames.Add([string]$asset.name)) { throw "$($a.candidateId) Release contains duplicate asset identity." }
        $expected = @($a.assets | Where-Object { $_.id -eq [long]$asset.id })
        if ($expected.Count -ne 1 -or $asset.name -cne $expected[0].name -or [long]$asset.size -ne $expected[0].size -or
            $asset.digest -cne $expected[0].digest -or $asset.state -cne 'uploaded' -or
            [string]$asset.browser_download_url -cne "https://github.com/$Repository/releases/download/$Tag/$($expected[0].name)") {
            throw "$($a.candidateId) Release asset ID/name/size/digest/state/URL differs from accepted evidence: $($asset.name)."
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
        throw "$($a.candidateId) payload manifest is not the accepted source/candidate/product/runtime/tree."
    }
    if ($Summary.candidateId -cne $a.candidateId -or $Summary.sourceHeadSha -cne $a.sourceSha -or
        $Summary.productVersion -cne $a.version -or $Summary.runtimeIdentifier -cne $a.runtime -or
        [int]$Summary.payloadFileCount -ne $a.fileCount -or $Summary.payloadTreeSha256 -cne $a.treeSha256 -or
        $Summary.candidateTag -cne $a.tag -or $Summary.payloadManifestSha256 -cne $a.assets[1].digest.Substring(7) -or
        $Summary.applicationPayloadArchive.name -cne $a.assets[0].name -or
        $Summary.applicationPayloadArchive.sha256 -cne $a.assets[0].digest.Substring(7) -or
        [long]$Summary.applicationPayloadArchive.bytes -ne $a.assets[0].size -or
        $Summary.preprodInstaller.name -cne $a.assets[4].name -or
        $Summary.preprodInstaller.sha256 -cne $a.assets[4].digest.Substring(7) -or
        [long]$Summary.preprodInstaller.bytes -ne $a.assets[4].size -or
        $Summary.releaseProvenanceSha256 -cne $a.assets[3].digest.Substring(7)) {
        throw "$($a.candidateId) package summary does not match accepted payload identity."
    }
    if ($Provenance.candidateId -cne $a.candidateId -or $Provenance.sourceHeadSha -cne $a.sourceSha -or
        $Provenance.productVersion -cne $a.version -or $Provenance.runtimeIdentifier -cne $a.runtime -or
        [int]$Provenance.payloadFileCount -ne $a.fileCount -or $Provenance.payloadTreeSha256 -cne $a.treeSha256 -or
        $Provenance.candidateTag -cne $a.tag -or $Provenance.payloadManifestSha256 -cne $a.assets[1].digest.Substring(7) -or
        $Provenance.applicationPayloadArchive.name -cne $a.assets[0].name -or
        $Provenance.applicationPayloadArchive.sha256 -cne $a.assets[0].digest.Substring(7) -or
        [long]$Provenance.applicationPayloadArchive.bytes -ne $a.assets[0].size -or
        $Provenance.preprodInstaller.name -cne $a.assets[4].name -or
        $Provenance.preprodInstaller.sha256 -cne $a.assets[4].digest.Substring(7) -or
        [long]$Provenance.preprodInstaller.bytes -ne $a.assets[4].size) {
        throw "$($a.candidateId) release provenance does not match accepted source/candidate/product/runtime."
    }
}

function Assert-M14DownloadedAsset {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][object]$Asset)
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -ne [long]$Asset.size -or
        "sha256:$((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant())" -cne [string]$Asset.digest) {
        throw "Downloaded $($script:Accepted.candidateId) asset differs in byte length or SHA-256: $($Asset.name)."
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
    if ($workflow -match '(?im)^\s*contents:\s*write\s*$' -or
        $workflow -cnotmatch '(?ms)^on:\s*\r?\n(?<triggers>.*?)(?=^permissions:|\z)' -or
        $Matches['triggers'].Trim() -cne 'workflow_dispatch:' -or
        $workflow -cnotmatch '(?ms)^permissions:\s*\r?\n(?<scope>.*?)(?=^jobs:|\z)' -or
        $Matches['scope'].Trim() -cne 'contents: read' -or
        -not $workflow.Contains('refs/heads/codex/post-m14-production-maintenance-batch-01')) {
        throw 'Promotion workflow must be manually dispatched, read-only and on the authorized branch.'
    }
    $identityCall = "-CandidateTag '$($script:Accepted.tag)' -ExpectedReleaseId $($script:Accepted.releaseId) -ExpectedSourceSha '$($script:Accepted.sourceSha)'"
    if (-not $workflow.Contains($identityCall)) {
        throw 'Promotion workflow does not name the exact accepted candidate.'
    }
    $ci = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/ci.yml') -Raw
    if ($ci -cnotmatch '(?ms)^permissions:\s*\r?\n(?<scope>.*?)(?=^[a-z][a-z-]*:|\z)' -or
        $Matches['scope'].Trim() -cne 'contents: read') { throw 'Ordinary CI requires exactly contents: read.' }
    if ($ci -cnotmatch '(?ms)^  m14-wp6-promotion-evidence:\s*\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)') {
        throw 'Ordinary PR CI lacks the controlled WP6 promotion evidence job.'
    }
    $job = $Matches['job']
    if ($job -match '(?i)\bdotnet(?:\.exe)?\b' -or $job -match '(?im)^\s*contents:\s*write\s*$') {
        throw 'PR promotion evidence job must neither rebuild the application nor use a write token.'
    }
    if (-not $job.Contains("github.head_ref == 'codex/post-m14-production-maintenance-batch-01'") -or
        -not $job.Contains('github.event.pull_request.head.repo.full_name == github.repository') -or
        -not $job.Contains($identityCall)) {
        throw 'PR promotion evidence job does not target the authorized branch and candidate.'
    }
    if ($job -cnotmatch '(?ms)^    permissions:\s*\r?\n(?<scope>.*?)(?=^    steps:|\z)' -or
        $Matches['scope'].Trim() -cne 'contents: read') { throw 'PR promotion job requires exactly contents: read.' }
    foreach ($text in @($workflow,$ci)) {
        # Every explicit permission block is constrained; scalar/all/write scopes are forbidden.
        $lines = $text -split '\r?\n'
        for ($i=0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -cmatch '^(?<indent> *)permissions:\s*(?<value>.*)$') {
                $indent = $Matches['indent'].Length
                if ($Matches['value'].Trim()) { throw 'Promotion workflows cannot use scalar permission scopes.' }
                $scope = @()
                for ($j=$i+1; $j -lt $lines.Count; $j++) {
                    if (-not $lines[$j].Trim()) { continue }
                    if ($lines[$j] -match '^ *' -and $Matches[0].Length -le $indent) { break }
                    $scope += $lines[$j].Trim()
                }
                if (($scope -join "`n") -cne 'contents: read') { throw 'Promotion workflows permit only contents: read.' }
            }
        }
        if ($text -match '(?im)^\s*[^\s:]+:\s*write\s*$') { throw 'Promotion workflows cannot grant write permissions.' }
        $tokenMapping = '          SUSHI81_RELEASE_READ_TOKEN: ${{ github.token }}'
        $steps = @([regex]::Matches($text,'(?ms)^      - name:.*?(?=^      - name:|\z)') |
            Where-Object { $_.Value.Contains('Promote-M14-Candidate.ps1') })
        if ($steps.Count -ne 1) { throw 'Promotion requires exactly one authenticated execution step.' }
        if ($steps[0].Value -cnotmatch '(?ms)^        env:\s*\r?\n(?<env>.*?)(?=^        run:)' -or
            -not $Matches['env'].Contains($tokenMapping) -or
            ([regex]::Matches($text,'SUSHI81_RELEASE_READ_TOKEN')).Count -ne 1 -or
            ([regex]::Matches($text,'\$\{\{ github\.token \}\}')).Count -ne 1 -or
            $text -match '(?i)secrets\.|GITHUB_ENV|-(?:Token|Authorization|Headers)\b') {
            throw 'Promotion credential must use only the ephemeral job token through step env.'
        }
    }
    $source = Get-Content -LiteralPath (Join-Path $RepoRoot 'installer/scripts/Promote-M14-Candidate.ps1') -Raw
    $parseTokens = $null; $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($source,[ref]$parseTokens,[ref]$parseErrors)
    if ($parseErrors.Count -or @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -match '(?i)token|headers|authorization' }).Count -or
        $source -match '(?i)github_pat_|gh[pousr]_[a-z0-9]{20,}|[a-f0-9]{64}[^a-f0-9]|GITHUB_ENV|browser_download_url|-(?:PreserveAuthorizationOnRedirect|AllowInsecureRedirect|SkipCertificateCheck|SkipHttpErrorCheck)\b') {
        throw 'Promotion source contains forbidden credential/anonymous/unsafe transport behavior.'
    }
    foreach ($required in @(
        "GetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN', 'Process')",
        "SetEnvironmentVariable('SUSHI81_RELEASE_READ_TOKEN', `$null, 'Process')",
        '$headers = Get-M14ReleaseReadHeaders',
        'Read-M14ReleaseMetadata -Uri "$api/releases/$ExpectedReleaseId" -Headers $headers',
        'Read-M14ReleaseMetadata -Uri "$api/git/ref/tags/$CandidateTag" -Headers $headers',
        'Save-M14ReleaseAsset -AssetId ([long]$asset.id) -Headers $headers -Path $path',
        'releases/assets/$AssetId',"'application/octet-stream'",'$headers.Clear()')) {
        if (-not $source.Contains($required)) { throw 'Promotion authenticated read boundary is incomplete.' }
    }
    if ($source.IndexOf('$headers = Get-M14ReleaseReadHeaders') -gt $source.IndexOf('Import-Module') -or
        $source -cnotmatch '(?s)\$record =(?<record>.*?)(?=    Copy-Item)' -or
        $Matches['record'] -match '(?i)token|headers|authorization|GetEnvironmentVariable') {
        throw 'Promotion must preflight credentials and exclude them from evidence records.'
    }
    if ($source -cnotmatch '(?s)\$packageSummary =(?<summary>.*?)(?=    \[IO.File\]::WriteAllText)' -or
        $Matches['summary'] -match '(?i)token|headers|authorization|GetEnvironmentVariable' -or
        @($ast.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -and
            $node.Value -match '(?i)^Bearer\s+\S+'},$true)).Count) {
        throw 'Promotion summaries and bearer credentials cannot contain persisted or literal tokens.'
    }
}

Export-ModuleMember -Function Get-M14AcceptedPromotionIdentity,Assert-M14AcceptedRelease,Assert-M14AcceptedPayloadMetadata,Assert-M14DownloadedAsset,Assert-M14PromotionBoundary
