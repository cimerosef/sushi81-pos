[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)][string]$CandidateId,
    [Parameter(Mandatory = $true)][string]$PayloadDirectory,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$PackageOutputDirectory,
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = $repoRoot }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path

Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
$identity = Get-M14CandidateIdentity -CandidateId $CandidateId
if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedSourceSha must be a full 40-character commit SHA.' }
$actualSourceSha = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSourceSha -cne $ExpectedSourceSha) { throw "Source checkout does not match the expected candidate head '$ExpectedSourceSha'." }
$manifest = Test-M14PayloadManifest -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId

$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
if ([string]$config.preprodProductName -cne 'Sushi81 POS PREPROD' -or
    [string]$config.preprodInnoAppId -cne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' -or
    [string]$config.preprodBinaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS PREPROD' -or
    [string]$config.preprodDurableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS PREPROD' -or
    [string]$config.innoSetupVersion -cne '6.7.3') {
    throw 'PreProd packaging configuration differs from its frozen identity or install/data roots.'
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Pinned Inno Setup 6.7.3 ISCC.exe was not found.'
}
$compilerPathResolved = (Resolve-Path -LiteralPath $CompilerPath).Path
$packageRoot = [IO.Path]::GetFullPath($PackageOutputDirectory)
if (Test-Path -LiteralPath $packageRoot) { throw "Refusing to reuse existing package directory '$packageRoot'." }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$staging = Join-Path $packageRoot 'preprod-payload'
$markerPath = New-M14PreProdStagingPayload -PayloadDirectory $PayloadDirectory -ManifestPath $ManifestPath -DestinationDirectory $staging -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $staging | Out-Null

$sourceShort = $ExpectedSourceSha.Substring(0,7).ToLowerInvariant()
$fileVersion = "$($identity.ProductVersion).0"
$outputBaseName = "Sushi81POS-PREPROD-Setup-$($identity.ProductVersion)-$sourceShort"
$installerOutput = Join-Path $packageRoot 'installer'
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
$issFile = Join-Path $PSScriptRoot '..\sushi81-pos.iss'
$env:SUSHI81_PRODUCT_VERSION = $identity.ProductVersion
$env:SUSHI81_FILE_VERSION = $fileVersion
$env:SUSHI81_SOURCE_SHORT = $sourceShort
$env:SUSHI81_PUBLISH_DIR = $staging
$env:SUSHI81_PACKAGE_OUT_DIR = $installerOutput
$env:SUSHI81_PROFILE = 'preprod'
$env:SUSHI81_APP_NAME = [string]$config.preprodProductName
$env:SUSHI81_INSTALL_DIRECTORY = 'Sushi81 POS PREPROD'
$env:SUSHI81_OUTPUT_BASE_NAME = $outputBaseName
& $compilerPathResolved $issFile "/O$installerOutput"
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation from the verified PreProd payload failed.' }

$installerPath = Join-Path $installerOutput "$outputBaseName.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw 'Compiled PreProd candidate installer is missing.' }
$installerItem = Get-Item -LiteralPath $installerPath
$result = [ordered]@{
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $identity.ProductVersion
    runtimeIdentifier = 'win-x64'
    profile = 'preprod'
    appId = [string]$config.preprodInnoAppId
    appName = [string]$config.preprodProductName
    installRoot = [string]$config.preprodBinaryInstallDirectory
    markerFile = 'deployment-profile.txt'
    markerValue = [IO.File]::ReadAllText($markerPath)
    payloadFileCount = [int]$manifest.fileCount
    payloadTreeSha256 = [string]$manifest.applicationPayloadTreeSha256
    installerFileName = $installerItem.Name
    installerPath = $installerPath
    installerBytes = [long]$installerItem.Length
    installerSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    profileEvidenceIsOnlyPackagingAddition = $true
}
$summaryPath = Join-Path $packageRoot 'preprod-package-summary.json'
[IO.File]::WriteAllText($summaryPath, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
[pscustomobject]$result
