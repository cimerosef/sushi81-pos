[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$PromotionHead
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'M14-ProductionPromotion.psm1') -Force
$accepted = Get-M14AcceptedPromotionIdentity
if ($PromotionHead -cnotmatch '^[0-9a-f]{40}$') { throw 'PromotionHead must be a canonical full SHA.' }
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../release-config.json') -Raw | ConvertFrom-Json
if ($config.productName -cne 'Sushi81 POS' -or $config.innoAppId -cne 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -or
    $config.binaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS' -or
    $config.durableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS' -or $config.innoSetupVersion -cne '6.7.3') {
    throw 'Production installer identity differs from the frozen configuration.'
}
$issPath = Join-Path $PSScriptRoot '../sushi81-pos.iss'
$iss = Get-Content -LiteralPath $issPath -Raw
if ($iss -cnotmatch 'AppId=\{\{C7A1B9E2-1E62-4B4B-A2EA-7802814408FC\}' -or
    $iss -cnotmatch 'DefaultDirName=\{localappdata\}\\Programs\\\{#ProfileInstallDirectory\}' -or
    $iss -cnotmatch 'Name: "\{autoprograms\}\\\{#ProfileAppName\}"') {
    throw 'Shared Inno Setup template has drifted from Production AppId/install/shortcut identity.'
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Pinned Inno Setup compiler is missing.' }
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
if (Test-Path -LiteralPath $packageRoot) { throw 'Refusing to reuse Production package directory.' }
New-Item -ItemType Directory -Path $packageRoot | Out-Null
$staging = Join-Path $packageRoot 'production-staging'
$marker = New-M14ProfileStagingPayload -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -DestinationDirectory $staging -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId $accepted.candidateId -Profile prod
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $staging | Out-Null
$manifest = Test-M14PayloadManifest -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId $accepted.candidateId
$stageFiles = @(Get-ChildItem -LiteralPath $staging -Recurse -File -Force)
$allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @($manifest.files)) { [void]$allowed.Add([string]$entry.path) }
foreach ($file in $stageFiles) {
    $relative = [IO.Path]::GetRelativePath($staging,$file.FullName).Replace('\','/')
    if ($relative -cne 'deployment-profile.txt' -and -not $allowed.Contains($relative)) { throw "Unexpected Production staging file '$relative'." }
}
if ($stageFiles.Count -ne $accepted.fileCount + 1 -or $manifest.applicationPayloadTreeSha256 -cne $accepted.treeSha256) {
    throw 'Production staging file count or accepted tree hash differs.'
}
$stageManifest = Test-M14PayloadManifest -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -ExpectedSourceSha $accepted.sourceSha -ExpectedCandidateId $accepted.candidateId
foreach ($entry in @($stageManifest.files)) {
    $file = Join-Path $staging ([string]$entry.path).Replace('/',[IO.Path]::DirectorySeparatorChar)
    if ((Get-Item -LiteralPath $file).Length -ne [long]$entry.bytes -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
        throw "Production staging changed accepted file '$($entry.path)'."
    }
}
$short = $PromotionHead.Substring(0,7)
$base = "Sushi81POS-PROD-Setup-1.0.1-C01-$short"
$output = Join-Path $packageRoot 'installer'
New-Item -ItemType Directory -Path $output | Out-Null
$values = @{
    SUSHI81_PRODUCT_VERSION='1.0.1'; SUSHI81_FILE_VERSION='1.0.1.0'; SUSHI81_SOURCE_SHORT=$short;
    SUSHI81_PUBLISH_DIR=$staging; SUSHI81_PACKAGE_OUT_DIR=$output; SUSHI81_PROFILE='prod';
    SUSHI81_APP_NAME='Sushi81 POS'; SUSHI81_INSTALL_DIRECTORY='Sushi81 POS'; SUSHI81_OUTPUT_BASE_NAME=$base
}
try {
    foreach ($key in $values.Keys) { [Environment]::SetEnvironmentVariable($key,[string]$values[$key],'Process') }
    & (Resolve-Path -LiteralPath $CompilerPath).Path $issPath "/O$output" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup Production compilation failed.' }
} finally {
    foreach ($key in $values.Keys) { [Environment]::SetEnvironmentVariable($key,$null,'Process') }
}
$installer = Join-Path $output "$base.exe"
$item = Get-Item -LiteralPath $installer -ErrorAction Stop
$afterFiles = @(Get-ChildItem -LiteralPath $staging -Recurse -File -Force)
if ($afterFiles.Count -ne $accepted.fileCount + 1 -or
    [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($marker)) -cne 'prod') {
    throw 'Production Inno staging changed during compilation.'
}
foreach ($entry in @($manifest.files)) {
    $file = Join-Path $staging ([string]$entry.path).Replace('/',[IO.Path]::DirectorySeparatorChar)
    if ((Get-Item -LiteralPath $file).Length -ne [long]$entry.bytes -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
        throw "Inno compilation changed accepted C01 payload '$($entry.path)'."
    }
}
$proof = [ordered]@{
    schemaVersion=1; acceptedCandidateId='C01'; acceptedSourceSha=$accepted.sourceSha;
    manifestDescribedFileCount=$accepted.fileCount; acceptedPayloadTreeSha256=$accepted.treeSha256;
    allManifestDescribedFilesMatchAcceptedC01=$true;
    extraPackagingFiles=@([ordered]@{ path='deployment-profile.txt'; asciiValue='prod'; bytes=4 });
    installerFileName=$item.Name; installerBytes=[long]$item.Length;
    installerSha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
}
$proofPath = Join-Path $packageRoot 'production-payload-equivalence.json'
[IO.File]::WriteAllText($proofPath,($proof | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{ installerPath=$installer; stagingPath=$staging; proofPath=$proofPath; proof=$proof }
