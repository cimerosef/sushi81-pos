[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$SourceRoot,
    [string]$CompilerPath,
    [string]$ProductVersion,
    [switch]$IncludePreProduction
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = $repoRoot }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
if ([string]$config.productName -cne 'Sushi81 POS' -or
    [string]$config.preprodProductName -cne 'Sushi81 POS PREPROD' -or
    [string]$config.innoAppId -cne 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -or
    [string]$config.preprodInnoAppId -cne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' -or
    [string]$config.binaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS' -or
    [string]$config.preprodBinaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS PREPROD' -or
    [string]$config.durableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS' -or
    [string]$config.preprodDurableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS PREPROD') {
    throw 'Packaging configuration differs from the frozen Prod/PreProd identities or paths.'
}
if ([string]::IsNullOrWhiteSpace($ProductVersion)) { $ProductVersion = [string]$config.productVersion }
if ($ProductVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid product version '$ProductVersion'." }
$fileVersion = if ($ProductVersion -eq $config.productVersion) { [string]$config.fileVersion } else { "$ProductVersion.0" }

$actualSourceSha = (& git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the source checkout HEAD.' }
if ($actualSourceSha -ne $ExpectedSourceSha) { throw "Source SHA mismatch: expected $ExpectedSourceSha, found $actualSourceSha." }
if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedSourceSha must be a full 40-character commit SHA.' }

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $compilerCommand) { $CompilerPath = $compilerCommand.Source }
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Pinned Inno Setup 6 compiler ISCC.exe was not found.'
}
$compilerPathResolved = (Resolve-Path -LiteralPath $CompilerPath).Path
# The Inno compiler's executable has no useful Windows file-version resource.
# The committed .iss checks ISPP's built-in Ver at compile time against this
# pinned release, so successful compilation is the version verification.
$compilerVersion = [string]$config.innoSetupVersion

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $OutputDirectory 'publish'
$packageDirectory = Join-Path $OutputDirectory 'installer'
if (Test-Path -LiteralPath $publishDirectory) { throw "Refusing to reuse existing publish directory '$publishDirectory'." }
if (Test-Path -LiteralPath $packageDirectory) { throw "Refusing to reuse existing package directory '$packageDirectory'." }
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$project = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Sushi81.Pos.Desktop.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Desktop project is missing from '$SourceRoot'." }
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not read the installed .NET SDK version.' }
$informationalVersion = "$ProductVersion+$($ExpectedSourceSha.ToLowerInvariant())"
$restoreArgs = @('restore', $project, '-r', $config.runtimeIdentifier)
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Desktop project restore failed.' }
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $config.runtimeIdentifier,
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    "-p:Version=$ProductVersion",
    "-p:FileVersion=$fileVersion",
    "-p:InformationalVersion=$informationalVersion",
    "-p:Product=$($config.productName)",
    "-p:AssemblyTitle=$($config.productName)",
    "-p:Company=$($config.productName)",
    '-o', $publishDirectory,
    '--no-restore'
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Release win-x64 self-contained publish failed.' }

$executable = Join-Path $publishDirectory 'Sushi81.Pos.Desktop.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published Sushi81.Pos.Desktop.exe is missing.' }
$executableVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ($executableVersion.FileVersion -ne $fileVersion -or $executableVersion.ProductVersion -ne $informationalVersion -or
    $executableVersion.ProductName -ne $config.productName) {
    throw "Published executable metadata mismatch: file=$($executableVersion.FileVersion), product=$($executableVersion.ProductName), informational=$($executableVersion.ProductVersion)."
}
$desktopAssembly = Join-Path $publishDirectory 'Sushi81.Pos.Desktop.dll'
$frenchResourceSource = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Properties\Resources.resx'
$chineseResourceSource = Join-Path $SourceRoot 'src\Sushi81.Pos.Desktop\Properties\Resources.zh-CN.resx'
$chineseSatellite = Join-Path $publishDirectory 'zh-CN\Sushi81.Pos.Desktop.resources.dll'
if (-not (Test-Path -LiteralPath $desktopAssembly -PathType Leaf)) { throw 'Published Desktop assembly is missing.' }
if (-not (Test-Path -LiteralPath $frenchResourceSource -PathType Leaf) -or (Get-Item -LiteralPath $frenchResourceSource).Length -eq 0) {
    throw 'French neutral UI resources must remain embedded in the Desktop assembly for fr-FR fallback.'
}
if (-not (Test-Path -LiteralPath $chineseResourceSource -PathType Leaf) -or -not (Test-Path -LiteralPath $chineseSatellite -PathType Leaf)) {
    throw 'Required zh-CN localization resources or published satellite are missing.'
}
$runtimeConfigPath = Join-Path $publishDirectory 'Sushi81.Pos.Desktop.runtimeconfig.json'
if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) { throw 'Published runtime configuration is missing.' }
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$bundledFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
if ($bundledFrameworks.Count -lt 2 -or -not ($bundledFrameworks.name -contains 'Microsoft.NETCore.App') -or -not ($bundledFrameworks.name -contains 'Microsoft.WindowsDesktop.App')) {
    throw 'Self-contained runtime configuration does not list both .NET and Windows Desktop runtime bundles.'
}

& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $publishDirectory

$sourceShort = $ExpectedSourceSha.Substring(0, 7).ToLowerInvariant()
$provenance = [ordered]@{
    schemaVersion = 1
    productName = [string]$config.productName
    productVersion = $ProductVersion
    fileVersion = $fileVersion
    informationalVersion = $informationalVersion
    executableProductName = [string]$executableVersion.ProductName
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    runtimeIdentifier = [string]$config.runtimeIdentifier
    selfContained = $true
    publishSingleFile = $false
    dotnetSdkVersion = $sdkVersion
    bundledRuntimeFrameworks = @($bundledFrameworks | ForEach-Object { [ordered]@{ name = $_.name; version = $_.version } })
    localizationPayloads = [ordered]@{
        'fr-FR' = 'French Resources.resx is embedded in Sushi81.Pos.Desktop.dll and supplies the neutral-resource fallback.'
        'zh-CN' = 'zh-CN/Sushi81.Pos.Desktop.resources.dll satellite.'
    }
    innoSetupVersion = [string]$compilerVersion
    deploymentProfileEvidence = 'deployment-profile.txt is added by profile-specific packaging and is excluded from application-payload equality.'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
$provenanceJson = $provenance | ConvertTo-Json -Depth 8
$publishProvenance = Join-Path $publishDirectory 'release-provenance.json'
Set-Content -LiteralPath $publishProvenance -Value $provenanceJson -Encoding utf8
Copy-Item -LiteralPath $publishProvenance -Destination (Join-Path $packageDirectory 'release-provenance.json')

$profilePayloadRoot = Join-Path $OutputDirectory 'profile-payloads'
if (Test-Path -LiteralPath $profilePayloadRoot) { throw "Refusing to reuse profile payload directory '$profilePayloadRoot'." }
$existingProfileMarkers = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Force -Filter 'deployment-profile.txt')
if ($existingProfileMarkers.Count -gt 0) { throw 'The application publish already contains deployment-profile.txt; refusing to overwrite payload content.' }
New-Item -ItemType Directory -Path $profilePayloadRoot -Force | Out-Null
$profileSpecs = [System.Collections.Generic.List[object]]::new()
$profileSpecs.Add([pscustomobject]@{
    profile = 'prod'
    appId = [string]$config.innoAppId
    appName = [string]$config.productName
    installDirectory = 'Sushi81 POS'
    shortcutName = 'Sushi81 POS'
})
if ($IncludePreProduction) {
    $profileSpecs.Add([pscustomobject]@{
        profile = 'preprod'
        appId = [string]$config.preprodInnoAppId
        appName = [string]$config.preprodProductName
        installDirectory = 'Sushi81 POS PREPROD'
        shortcutName = 'Sushi81 POS PREPROD'
    })
}
if ($profileSpecs.Count -eq 2 -and $profileSpecs[0].appId -eq $profileSpecs[1].appId) {
    throw 'Prod and PreProd must have distinct stable Inno AppIds.'
}
foreach ($spec in $profileSpecs) {
    if ($spec.appId -notmatch '^[0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}$') {
        throw "Invalid Inno AppId for profile '$($spec.profile)'."
    }
    $staging = Join-Path $profilePayloadRoot $spec.profile
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $publishDirectory -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $staging $item.Name) -Recurse -Force
    }
    $markerPath = Join-Path $staging 'deployment-profile.txt'
    [System.IO.File]::WriteAllText($markerPath, $spec.profile, [System.Text.Encoding]::ASCII)
    $markerBytes = [System.IO.File]::ReadAllBytes($markerPath)
    if ([System.Text.Encoding]::ASCII.GetString($markerBytes) -cne $spec.profile) {
        throw "Profile evidence content is not exact for '$($spec.profile)'."
    }
    $stagedMarkers = @(Get-ChildItem -LiteralPath $staging -Recurse -File -Force -Filter 'deployment-profile.txt')
    if ($stagedMarkers.Count -ne 1 -or $stagedMarkers[0].FullName -ne $markerPath) {
        throw "Expected exactly one root deployment-profile.txt for '$($spec.profile)'."
    }
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $staging | Out-Null
}

function Get-ApplicationPayloadEntries([string]$Root) {
    $entries = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        if ($relative -cne 'deployment-profile.txt') {
            [pscustomobject]@{
                path = $relative
                bytes = [long]$_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    } | Sort-Object -Property path)
    return ,$entries
}

$payloadEntries = Get-ApplicationPayloadEntries (Join-Path $profilePayloadRoot 'prod')
if ($payloadEntries.Count -eq 0) { throw 'The application payload manifest cannot be empty.' }
$payloadTreeText = (($payloadEntries | ForEach-Object { "$($_.path)`t$($_.sha256)`t$($_.bytes)" }) -join "`n") + "`n"
$payloadTreeHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($payloadTreeText))).ToLowerInvariant()
$payloadManifest = [ordered]@{
    schemaVersion = 1
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $ProductVersion
    runtimeIdentifier = [string]$config.runtimeIdentifier
    profileEvidenceExcluded = 'deployment-profile.txt (root file; packaging-only profile identity)'
    applicationPayloadTreeSha256 = $payloadTreeHash
    files = $payloadEntries
}
$payloadManifestPath = Join-Path $packageDirectory 'application-payload-manifest.json'
$payloadManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $payloadManifestPath -Encoding utf8
$payloadManifestHash = (Get-FileHash -LiteralPath $payloadManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

$payloadEquality = [ordered]@{
    verified = $false
    commonApplicationFileCount = [int]$payloadEntries.Count
    excludedPackagingOnlyFiles = @('deployment-profile.txt')
    applicationPayloadTreeSha256 = $payloadTreeHash
    note = 'PreProd comparison is performed only when IncludePreProduction is requested.'
}
if ($IncludePreProduction) {
    $preprodEntries = Get-ApplicationPayloadEntries (Join-Path $profilePayloadRoot 'preprod')
    if ($payloadEntries.Count -ne $preprodEntries.Count) { throw 'Prod and PreProd application payload file counts differ.' }
    for ($index = 0; $index -lt $payloadEntries.Count; $index++) {
        if ($payloadEntries[$index].path -cne $preprodEntries[$index].path -or
            $payloadEntries[$index].bytes -ne $preprodEntries[$index].bytes -or
            $payloadEntries[$index].sha256 -cne $preprodEntries[$index].sha256) {
            throw "Prod and PreProd application payload differs at '$($payloadEntries[$index].path)'."
        }
    }
    $payloadEquality.verified = $true
    $payloadEquality.note = 'Every common application file is byte-identical after excluding only the root packaging-only deployment-profile.txt.'
}

$profilePackages = [ordered]@{}
$issFile = Join-Path $PSScriptRoot '..\sushi81-pos.iss'
foreach ($spec in $profileSpecs) {
    $staging = Join-Path $profilePayloadRoot $spec.profile
    $installerOutputDirectory = if ($spec.profile -eq 'prod') { $packageDirectory } else { Join-Path $packageDirectory 'preprod' }
    New-Item -ItemType Directory -Path $installerOutputDirectory -Force | Out-Null
    $outputBaseName = if ($spec.profile -eq 'prod') {
        "Sushi81POS-Setup-$ProductVersion-$sourceShort"
    } else {
        "Sushi81POS-PREPROD-Setup-$ProductVersion-$sourceShort"
    }
    $env:SUSHI81_PRODUCT_VERSION = $ProductVersion
    $env:SUSHI81_FILE_VERSION = $fileVersion
    $env:SUSHI81_SOURCE_SHORT = $sourceShort
    $env:SUSHI81_PUBLISH_DIR = $staging
    $env:SUSHI81_PACKAGE_OUT_DIR = $installerOutputDirectory
    $env:SUSHI81_PROFILE = $spec.profile
    $env:SUSHI81_APP_ID = $spec.appId
    $env:SUSHI81_APP_NAME = $spec.appName
    $env:SUSHI81_INSTALL_DIRECTORY = $spec.installDirectory
    $env:SUSHI81_OUTPUT_BASE_NAME = $outputBaseName
    & $compilerPathResolved $issFile "/O$installerOutputDirectory"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed for '$($spec.profile)'." }

    $installerPath = Join-Path $installerOutputDirectory "$outputBaseName.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw "Compiled '$($spec.profile)' installer '$installerPath' is missing." }
    $installerFile = Get-Item -LiteralPath $installerPath
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $profilePackages[$spec.profile] = [ordered]@{
        profile = $spec.profile
        appId = $spec.appId
        appName = $spec.appName
        installDirectoryName = $spec.installDirectory
        shortcutName = $spec.shortcutName
        deploymentProfileEvidence = $spec.profile
        installerFileName = $installerFile.Name
        installerBytes = [long]$installerFile.Length
        installerSha256 = $installerHash
        installerPath = $installerPath
    }
}

$exeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$provenanceHash = (Get-FileHash -LiteralPath $publishProvenance -Algorithm SHA256).Hash.ToLowerInvariant()
$prodPackage = $profilePackages.prod
$summary = [ordered]@{
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $ProductVersion
    fileVersion = $fileVersion
    runtimeIdentifier = [string]$config.runtimeIdentifier
    selfContained = $true
    publishSingleFile = $false
    dotnetSdkVersion = $sdkVersion
    innoSetupVersion = [string]$compilerVersion
    innoAppId = [string]$config.innoAppId
    installerFileName = $prodPackage.installerFileName
    installerBytes = $prodPackage.installerBytes
    installerSha256 = $prodPackage.installerSha256
    executableSha256 = $exeHash
    releaseProvenanceSha256 = $provenanceHash
    applicationPayloadManifestPath = $payloadManifestPath
    applicationPayloadManifestSha256 = $payloadManifestHash
    applicationPayloadTreeSha256 = $payloadTreeHash
    payloadEquality = $payloadEquality
    profilePackages = $profilePackages
    publishDirectory = $publishDirectory
    installerPath = $prodPackage.installerPath
    releaseProvenancePath = (Join-Path $packageDirectory 'release-provenance.json')
}
$summaryPath = Join-Path $packageDirectory 'package-summary.json'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$summary
