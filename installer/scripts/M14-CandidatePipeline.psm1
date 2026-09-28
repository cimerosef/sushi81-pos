Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ManifestProfileExclusion = 'deployment-profile.txt (root file; packaging-only profile identity)'
$script:ExpectedProductVersion = '1.0.1'
$script:ExpectedRuntimeIdentifier = 'win-x64'

function Get-M14CandidateIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$CandidateId,[string]$ProductVersion = $script:ExpectedProductVersion)
    if ($ProductVersion -cne $script:ExpectedProductVersion) { throw "M14 product version must be $script:ExpectedProductVersion." }
    if ($CandidateId -notmatch '^C[0-9]{2}$') { throw "CandidateId must use the stable C01-C99 form; found '$CandidateId'." }
    $number = [int]$CandidateId.Substring(1)
    if ($number -lt 1 -or $number -gt 99) { throw "CandidateId '$CandidateId' is outside C01-C99." }
    $suffix = $number.ToString('00',[Globalization.CultureInfo]::InvariantCulture)
    [pscustomobject]@{ CandidateId = "C$suffix"; ProductVersion = $ProductVersion; Tag = "v$ProductVersion-preprod-c$suffix" }
}

function Get-M14DispatchTagIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Ref)
    if ($Ref -cnotmatch '^refs/tags/m14-preprod-dispatch-c(0[1-9]|[1-9][0-9])$') {
        throw "Dispatch ref '$Ref' must use the canonical m14-preprod-dispatch-c01 through c99 namespace."
    }
    $candidateId = "C$($Matches[1])"
    $identity = Get-M14CandidateIdentity -CandidateId $candidateId
    if ($identity.Tag -cne "v1.0.1-preprod-c$($Matches[1])") {
        throw "Dispatch ref '$Ref' does not map exactly to the candidate release identity."
    }
    $identity
}

function Assert-M14SafePayloadPath {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or $Path.Contains('\') -or $Path.Contains(':')) { throw "Invalid application payload path '$Path'." }
    foreach ($segment in $Path.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -in @('.', '..') -or $segment -match '[<>:"|?*\x00-\x1f]' -or $segment.EndsWith('.') -or $segment.EndsWith(' ')) {
            throw "Invalid application payload path '$Path'."
        }
        if ($segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw "Application payload path uses a reserved Windows name '$Path'." }
    }
    if ($Path -ieq 'deployment-profile.txt') { throw 'The root deployment-profile.txt is packaging-only and cannot be in the application payload.' }
}

function Sort-M14PayloadEntries {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object[]]$Entries)
    $sorted = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) { $sorted.Add([string]$entry.path,$entry) }
    @($sorted.Values)
}

function Sort-M14PayloadNames {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string[]]$Names)
    $sorted = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $Names) { if (-not $sorted.Add($name)) { throw "Payload archive contains a duplicate path '$name'." } }
    @($sorted)
}

function Get-M14PayloadEntries {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$PayloadDirectory)
    $root = (Resolve-Path -LiteralPath $PayloadDirectory).Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Payload directory is missing: '$PayloadDirectory'." }
    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Payload root must not be a reparse point.' }
    $items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Application payload must not contain reparse points: '$($item.FullName)'." }
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    if ($files.Count -eq 0) { throw 'Application payload must not be empty.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
        Assert-M14SafePayloadPath -Path $relative
        if (-not $seen.Add($relative)) { throw "Application payload contains case-colliding or duplicate path '$relative'." }
        $entries.Add([pscustomobject]@{ path = $relative; bytes = [long]$file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
    Sort-M14PayloadEntries -Entries $entries.ToArray()
}

function Get-M14PayloadTreeHash {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object[]]$Files)
    $ordered = @(Sort-M14PayloadEntries -Entries $Files)
    $lines = @($ordered | ForEach-Object { "$($_.path)$([char]9)$($_.sha256)$([char]9)$($_.bytes)" })
    $text = ($lines -join [string][char]10) + [string][char]10
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function New-M14PayloadManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$SourceSha,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [string]$ProductVersion = $script:ExpectedProductVersion,
        [string]$RuntimeIdentifier = 'win-x64'
    )
    if ($SourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'SourceSha must be a full 40-character commit SHA.' }
    $identity = Get-M14CandidateIdentity -CandidateId $CandidateId -ProductVersion $ProductVersion
    if ($RuntimeIdentifier -cne 'win-x64') { throw 'RuntimeIdentifier must be win-x64.' }
    $files = @(Get-M14PayloadEntries -PayloadDirectory $PayloadDirectory)
    $manifest = [ordered]@{
        schemaVersion = 1
        candidateId = $identity.CandidateId
        sourceHeadSha = $SourceSha.ToLowerInvariant()
        productVersion = $ProductVersion
        runtimeIdentifier = $RuntimeIdentifier
        profileEvidenceExcluded = $script:ManifestProfileExclusion
        fileCount = $files.Count
        applicationPayloadTreeSha256 = Get-M14PayloadTreeHash -Files $files
        files = $files
    }
    $path = [IO.Path]::GetFullPath($ManifestPath)
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($path, ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $manifest
}

function Test-M14PayloadManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [string]$ExpectedSourceSha,
        [string]$ExpectedCandidateId,
        [string]$ExpectedProductVersion = $script:ExpectedProductVersion
    )
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.productVersion -cne $ExpectedProductVersion -or $manifest.runtimeIdentifier -cne 'win-x64' -or $manifest.profileEvidenceExcluded -cne $script:ManifestProfileExclusion) {
        throw 'Application payload manifest has an unsupported schema, version, runtime, or profile exclusion.'
    }
    if ([string]$manifest.sourceHeadSha -notmatch '^[0-9a-f]{40}$') { throw 'Manifest sourceHeadSha is not a canonical full SHA.' }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -and $manifest.sourceHeadSha -cne $ExpectedSourceSha.ToLowerInvariant()) { throw 'Application payload manifest source SHA does not match the expected source.' }
    $identity = Get-M14CandidateIdentity -CandidateId ([string]$manifest.candidateId) -ProductVersion ([string]$manifest.productVersion)
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCandidateId) -and $identity.CandidateId -cne $ExpectedCandidateId) { throw 'Application payload manifest candidate ID does not match the expected candidate.' }
    $entries = @($manifest.files)
    if ($entries.Count -eq 0 -or $manifest.fileCount -ne $entries.Count) { throw 'Application payload manifest must describe a non-empty, correctly counted payload.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        $path = [string]$entry.path
        Assert-M14SafePayloadPath -Path $path
        if (-not $seen.Add($path)) { throw "Manifest has duplicate or case-colliding path '$path'." }
        if ($entry.bytes -lt 0 -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') { throw "Manifest has invalid byte count or SHA-256 for '$path'." }
    }
    $treeHash = Get-M14PayloadTreeHash -Files $entries
    if ($treeHash -cne [string]$manifest.applicationPayloadTreeSha256) { throw 'Application payload tree hash does not match its manifest entries.' }
    $actual = @(Get-M14PayloadEntries -PayloadDirectory $PayloadDirectory)
    $orderedEntries = @(Sort-M14PayloadEntries -Entries $entries)
    if ($actual.Count -ne $orderedEntries.Count) { throw 'Application payload file count differs from the manifest.' }
    for ($index = 0; $index -lt $actual.Count; $index++) {
        if ($actual[$index].path -cne [string]$orderedEntries[$index].path -or $actual[$index].bytes -ne [long]$orderedEntries[$index].bytes -or $actual[$index].sha256 -cne [string]$orderedEntries[$index].sha256) {
            throw "Application payload differs from the manifest at '$($orderedEntries[$index].path)'."
        }
    }
    $manifest
}

function New-M14PayloadArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [string]$ExpectedSourceSha,
        [string]$ExpectedCandidateId
    )
    $manifest = Test-M14PayloadManifest -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $ExpectedCandidateId
    $archive = [IO.Path]::GetFullPath($ArchivePath)
    if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite existing payload archive '$archive'." }
    $parent = Split-Path -Parent $archive
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = (Resolve-Path -LiteralPath $PayloadDirectory).Path
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in @(Sort-M14PayloadEntries -Entries $manifest.files)) {
            $relative = [string]$entry.path
            $source = Join-Path $root ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
            $zipEntry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $zipEntry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($source)
            $outputStream = $zipEntry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    } finally { $zip.Dispose() }
    $item = Get-Item -LiteralPath $archive
    [pscustomobject]@{ path = $archive; bytes = [long]$item.Length; sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function Test-M14PayloadArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$ExtractionDirectory,
        [string]$ExpectedSourceSha,
        [string]$ExpectedCandidateId
    )
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $expected = @(Sort-M14PayloadEntries -Entries $manifest.files)
    if ($expected.Count -eq 0) { throw 'Cannot validate an archive against an empty manifest.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
    try {
        $zipFiles = [Collections.Generic.List[string]]::new()
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { throw "Payload archive contains an unexpected directory entry '$($entry.FullName)'." }
            Assert-M14SafePayloadPath -Path $entry.FullName
            if (-not $seen.Add($entry.FullName)) { throw "Payload archive contains duplicate or case-colliding path '$($entry.FullName)'." }
            $zipFiles.Add($entry.FullName)
        }
        $archiveNames = @(Sort-M14PayloadNames -Names $zipFiles.ToArray())
        $manifestNames = @($expected | ForEach-Object { [string]$_.path })
        if ($archiveNames.Count -ne $manifestNames.Count) { throw 'Payload archive file count differs from the manifest.' }
        for ($index = 0; $index -lt $archiveNames.Count; $index++) {
            if ($archiveNames[$index] -cne $manifestNames[$index]) { throw "Payload archive has unexpected or missing path '$($archiveNames[$index])'." }
        }
    } finally { $zip.Dispose() }
    $destination = [IO.Path]::GetFullPath($ExtractionDirectory)
    if (Test-Path -LiteralPath $destination) { throw "Refusing to reuse archive extraction directory '$destination'." }
    [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $ArchivePath).Path, $destination)
    Test-M14PayloadManifest -PayloadDirectory $destination -ManifestPath $ManifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $ExpectedCandidateId
}

function New-M14PreProdStagingPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [string]$ExpectedSourceSha,
        [string]$ExpectedCandidateId
    )
    $manifest = Test-M14PayloadManifest -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $ExpectedCandidateId
    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    if (Test-Path -LiteralPath $destination) { throw "Refusing to reuse PreProd staging directory '$destination'." }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $sourceRoot = (Resolve-Path -LiteralPath $PayloadDirectory).Path
    foreach ($entry in @($manifest.files)) {
        $relative = [string]$entry.path
        $source = Join-Path $sourceRoot ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        $target = Join-Path $destination ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        $targetParent = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetParent -PathType Container)) { New-Item -ItemType Directory -Path $targetParent -Force | Out-Null }
        Copy-Item -LiteralPath $source -Destination $target
    }
    $markerPath = Join-Path $destination 'deployment-profile.txt'
    [IO.File]::WriteAllBytes($markerPath, [Text.Encoding]::ASCII.GetBytes('preprod'))
    $actualFiles = @(Get-ChildItem -LiteralPath $destination -Recurse -File -Force)
    if ($actualFiles.Count -ne @($manifest.files).Count + 1) { throw 'PreProd packaging added or omitted an unexpected file.' }
    $marker = [IO.File]::ReadAllBytes($markerPath)
    if ([Text.Encoding]::ASCII.GetString($marker) -cne 'preprod' -or $marker.Length -ne 7) { throw 'PreProd profile marker must contain exactly the ASCII value preprod.' }
    foreach ($entry in @($manifest.files)) {
        $target = Join-Path $destination ([string]$entry.path.Replace('/',[IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Item -LiteralPath $target).Length -ne [long]$entry.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
            throw "PreProd packaging changed application payload file '$($entry.path)'."
        }
    }
    $markerPath
}

function Assert-M14CandidateNotPublished {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$CandidateTag,[switch]$TagExists,[switch]$ReleaseExists)
    if ($TagExists -or $ReleaseExists) { throw "Candidate '$CandidateTag' already has a tag or release; candidate identities are single-use and cannot be overwritten." }
}

Export-ModuleMember -Function Get-M14CandidateIdentity,Get-M14DispatchTagIdentity,Assert-M14SafePayloadPath,Get-M14PayloadEntries,Get-M14PayloadTreeHash,New-M14PayloadManifest,Test-M14PayloadManifest,New-M14PayloadArchive,Test-M14PayloadArchive,New-M14PreProdStagingPayload,Assert-M14CandidateNotPublished
