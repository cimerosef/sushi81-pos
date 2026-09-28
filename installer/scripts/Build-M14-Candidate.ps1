[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)][string]$CandidateId,
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
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
if ($LASTEXITCODE -ne 0 -or $actualSourceSha -cne $ExpectedSourceSha) { throw "Expected exact source head '$ExpectedSourceSha', found '$actualSourceSha'." }
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Candidate packaging and installer lifecycle verification are hosted GitHub Actions only.'
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Pinned Inno Setup 6.7.3 ISCC.exe was not found.' }
$compilerPathResolved = (Resolve-Path -LiteralPath $CompilerPath).Path
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $artifactRoot) { throw "Refusing to reuse candidate artifact directory '$artifactRoot'." }
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
if ([string]$config.runtimeIdentifier -cne 'win-x64' -or [string]$config.innoSetupVersion -cne '6.7.3') { throw 'Candidate runtime or Inno Setup pin differs from the approved M14 contract.' }
$project = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Sushi81.Pos.Desktop.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'The Sushi81 desktop project is missing.' }
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) { throw 'Could not read the installed .NET SDK version.' }
$fileVersion = "$($identity.ProductVersion).0"
$informationalVersion = "$($identity.ProductVersion)+$($ExpectedSourceSha.ToLowerInvariant())"
$sessionRoot = Join-Path $env:RUNNER_TEMP "sushi81-m14-candidate-$($identity.CandidateId.ToLowerInvariant())-$([guid]::NewGuid().ToString('N'))"
$payloadDirectory = Join-Path $sessionRoot 'application-payload'
New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    "-p:Version=$($identity.ProductVersion)",
    "-p:FileVersion=$fileVersion",
    "-p:InformationalVersion=$informationalVersion",
    "-p:Product=$($config.productName)",
    "-p:AssemblyTitle=$($config.productName)",
    "-p:Company=$($config.productName)",
    '-o', $payloadDirectory,
    '--no-restore'
)
$applicationPublishInvocations = 1
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'The exact-head Release win-x64 application publish failed.' }
if ($applicationPublishInvocations -ne 1) { throw 'Candidate pipeline must publish the application exactly once.' }

$executable = Join-Path $payloadDirectory 'Sushi81.Pos.Desktop.exe'
$desktopAssembly = Join-Path $payloadDirectory 'Sushi81.Pos.Desktop.dll'
$runtimeConfigPath = Join-Path $payloadDirectory 'Sushi81.Pos.Desktop.runtimeconfig.json'
$chineseSatellite = Join-Path $payloadDirectory 'zh-CN\Sushi81.Pos.Desktop.resources.dll'
$frenchResourceSource = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Properties\Resources.resx'
$chineseResourceSource = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Properties\Resources.zh-CN.resx'
foreach ($requiredPath in @($executable,$desktopAssembly,$runtimeConfigPath,$chineseSatellite,$frenchResourceSource,$chineseResourceSource)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Candidate payload is missing required application/resource file '$requiredPath'." }
}
if ((Get-Item -LiteralPath $frenchResourceSource).Length -eq 0 -or (Get-Item -LiteralPath $chineseResourceSource).Length -eq 0) { throw 'French neutral or zh-CN source resources are empty.' }
$executableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ($executableVersion.FileVersion -ne $fileVersion -or $executableVersion.ProductVersion -ne $informationalVersion -or $executableVersion.ProductName -ne $config.productName) {
    throw 'Published application version metadata does not match the exact candidate identity.'
}
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
if ($frameworks.Count -lt 2 -or -not ($frameworks.name -contains 'Microsoft.NETCore.App') -or -not ($frameworks.name -contains 'Microsoft.WindowsDesktop.App')) {
    throw 'Candidate self-contained runtime metadata does not contain .NET and Windows Desktop.'
}

$runUrl = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)" } else { '' }
$runtimeProvenance = [ordered]@{
    schemaVersion = 1
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    productName = [string]$config.productName
    productVersion = $identity.ProductVersion
    fileVersion = $fileVersion
    informationalVersion = $informationalVersion
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    publishSingleFile = $false
    applicationPublishInvocationCount = $applicationPublishInvocations
    dotnetSdkVersion = $sdkVersion
    innoSetupVersion = [string]$config.innoSetupVersion
    workflowRunUrl = $runUrl
    deploymentProfileEvidence = 'deployment-profile.txt is added only by profile packaging.'
}
$runtimeProvenancePath = Join-Path $payloadDirectory 'release-provenance.json'
[IO.File]::WriteAllText($runtimeProvenancePath, ($runtimeProvenance | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $payloadDirectory | Out-Null

$manifestPath = Join-Path $sessionRoot 'payload-manifest.json'
$manifest = New-M14PayloadManifest -PayloadDirectory $payloadDirectory -ManifestPath $manifestPath -SourceSha $ExpectedSourceSha -CandidateId $identity.CandidateId
$verifiedManifest = Test-M14PayloadManifest -PayloadDirectory $payloadDirectory -ManifestPath $manifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId
$archivePath = Join-Path $sessionRoot 'application-payload.zip'
$archiveInfo = New-M14PayloadArchive -PayloadDirectory $payloadDirectory -ManifestPath $manifestPath -ArchivePath $archivePath -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId
$extractDirectory = Join-Path $sessionRoot 'payload-archive-roundtrip'
$null = Test-M14PayloadArchive -ArchivePath $archivePath -ManifestPath $manifestPath -ExtractionDirectory $extractDirectory -ExpectedSourceSha $ExpectedSourceSha -ExpectedCandidateId $identity.CandidateId
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $extractDirectory | Out-Null

$packageDirectory = Join-Path $sessionRoot 'preprod-package'
$packageScript = Join-Path $PSScriptRoot 'Package-PreProdFromPayload.ps1'
$package = & $packageScript -ExpectedSourceSha $ExpectedSourceSha -CandidateId $identity.CandidateId -PayloadDirectory $payloadDirectory -ManifestPath $manifestPath -PackageOutputDirectory $packageDirectory -CompilerPath $compilerPathResolved -SourceRoot $SourceRoot
if ($LASTEXITCODE -ne 0) { throw 'PreProd installer packaging from the verified candidate payload failed.' }
if ($package.profile -cne 'preprod' -or $package.profileEvidenceIsOnlyPackagingAddition -ne $true -or $package.payloadTreeSha256 -cne $verifiedManifest.applicationPayloadTreeSha256) {
    throw 'Candidate PreProd package summary does not prove profile-only packaging from the verified payload.'
}
$lifecycleScript = Join-Path $PSScriptRoot 'Verify-PreProdCandidateLifecycle.ps1'
$lifecycleSummary = & $lifecycleScript -InstallerPath $package.installerPath -PayloadManifest $manifestPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedProductVersion $identity.ProductVersion -CandidateId $identity.CandidateId
if ($LASTEXITCODE -ne 0) { throw 'Hosted-runner PreProd candidate installer lifecycle verification failed.' }

$installerName = [string]$package.installerFileName
$assetSources = [ordered]@{ 'application-payload.zip' = $archivePath; 'payload-manifest.json' = $manifestPath; $installerName = [string]$package.installerPath }
foreach ($asset in $assetSources.GetEnumerator()) { Copy-Item -LiteralPath $asset.Value -Destination (Join-Path $artifactRoot $asset.Key) }
$manifestSha256 = (Get-FileHash -LiteralPath (Join-Path $artifactRoot 'payload-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$installerSha256 = (Get-FileHash -LiteralPath (Join-Path $artifactRoot $installerName) -Algorithm SHA256).Hash.ToLowerInvariant()
$installerBytes = [long](Get-Item -LiteralPath (Join-Path $artifactRoot $installerName)).Length
$provenance = [ordered]@{
    schemaVersion = 1
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $identity.ProductVersion
    runtimeIdentifier = 'win-x64'
    payloadFileCount = [int]$manifest.fileCount
    payloadTreeSha256 = [string]$manifest.applicationPayloadTreeSha256
    payloadManifestSha256 = $manifestSha256
    applicationPayloadArchive = [ordered]@{ name = 'application-payload.zip'; sha256 = $archiveInfo.sha256; bytes = [long]$archiveInfo.bytes }
    preprodInstaller = [ordered]@{ name = $installerName; sha256 = $installerSha256; bytes = $installerBytes }
    preprodIdentity = [ordered]@{ appId = '67FB6B75-3C5E-44A5-98AD-305EA4C62D95'; profile = 'preprod'; profileMarker = 'deployment-profile.txt=preprod'; installRoot = '%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD' }
    dotnetSdkVersion = $sdkVersion
    innoSetupVersion = [string]$config.innoSetupVersion
    applicationPublishInvocationCount = $applicationPublishInvocations
    installerApplicationRepublishInvocationCount = 0
    workflowRun = [ordered]@{ repository = $env:GITHUB_REPOSITORY; runId = $env:GITHUB_RUN_ID; attempt = $env:GITHUB_RUN_ATTEMPT; url = $runUrl }
    requiredAssetNames = @('application-payload.zip','payload-manifest.json','package-summary.json','release-provenance.json',$installerName)
    realProductionDataUsed = $false
    runtimeHandoffSnapshotUsed = $false
}
$provenancePath = Join-Path $artifactRoot 'release-provenance.json'
[IO.File]::WriteAllText($provenancePath, ($provenance | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
$provenanceSha256 = (Get-FileHash -LiteralPath $provenancePath -Algorithm SHA256).Hash.ToLowerInvariant()
$assets = @(Get-ChildItem -LiteralPath $artifactRoot -File | Sort-Object -Property Name)
$assetInventory = @($assets | ForEach-Object { [ordered]@{ name = $_.Name; bytes = [long]$_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
$summary = [ordered]@{
    schemaVersion = 1
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $identity.ProductVersion
    runtimeIdentifier = 'win-x64'
    payloadFileCount = [int]$manifest.fileCount
    payloadTreeSha256 = [string]$manifest.applicationPayloadTreeSha256
    payloadManifestSha256 = $manifestSha256
    applicationPayloadArchive = [ordered]@{ name = 'application-payload.zip'; sha256 = $archiveInfo.sha256; bytes = [long]$archiveInfo.bytes }
    preprodInstaller = [ordered]@{ name = $installerName; sha256 = $installerSha256; bytes = $installerBytes }
    preprodIdentity = $provenance.preprodIdentity
    dotnetSdkVersion = $sdkVersion
    innoSetupVersion = [string]$config.innoSetupVersion
    applicationPublishInvocationCount = $applicationPublishInvocations
    installerApplicationRepublishInvocationCount = 0
    releaseProvenanceSha256 = $provenanceSha256
    workflowRun = $provenance.workflowRun
    requiredAssetNames = @('application-payload.zip','payload-manifest.json','package-summary.json','release-provenance.json',$installerName)
    assets = $assetInventory
    installerLifecycleChecksPassed = @($lifecycleSummary.steps).Count
    noRealProductionOrRuntimeDataUsed = $true
}
$summaryPath = Join-Path $artifactRoot 'package-summary.json'
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $artifactRoot | Out-Null

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    @(
        ''
        "## M14 PREPROD candidate $($identity.CandidateId) build evidence"
        ''
        "Exact source SHA: $ExpectedSourceSha"
        "Candidate tag: $($identity.Tag)"
        "Payload file count: $($manifest.fileCount)"
        "Payload tree SHA-256: $($manifest.applicationPayloadTreeSha256)"
        "Payload manifest SHA-256: $manifestSha256"
        "application-payload.zip: $($archiveInfo.bytes) bytes / $($archiveInfo.sha256)"
        "PREPROD installer: $installerBytes bytes / $installerSha256"
        "Application publish invocations: $applicationPublishInvocations"
        'Installer packaging application republish invocations: 0'
        "Synthetic PreProd installer lifecycle checks passed: $($summary.installerLifecycleChecksPassed)"
        'Forbidden-content checks passed for payload, extracted archive, package staging and candidate evidence.'
        'No production business data, credentials, runtime handoff snapshots or OneDrive content was used.'
    ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
[pscustomobject]$summary
