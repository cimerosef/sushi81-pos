[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$SourceRoot,
    [string]$CompilerPath,
    [string]$ProductVersion
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = $repoRoot }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
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
$compilerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($compilerPathResolved).FileVersion
if ($compilerVersion -notlike "$($config.innoSetupVersion)*") {
    throw "Inno Setup compiler version '$compilerVersion' does not match pinned $($config.innoSetupVersion)."
}

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
    innoAppId = [string]$config.innoAppId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
$provenanceJson = $provenance | ConvertTo-Json -Depth 8
$publishProvenance = Join-Path $publishDirectory 'release-provenance.json'
Set-Content -LiteralPath $publishProvenance -Value $provenanceJson -Encoding utf8
Copy-Item -LiteralPath $publishProvenance -Destination (Join-Path $packageDirectory 'release-provenance.json')

$env:SUSHI81_PRODUCT_VERSION = $ProductVersion
$env:SUSHI81_FILE_VERSION = $fileVersion
$env:SUSHI81_SOURCE_SHORT = $sourceShort
$env:SUSHI81_PUBLISH_DIR = $publishDirectory
$env:SUSHI81_PACKAGE_OUT_DIR = $packageDirectory
$issFile = Join-Path $PSScriptRoot '..\sushi81-pos.iss'
& $compilerPathResolved $issFile "/O$packageDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installerPath = Join-Path $packageDirectory "Sushi81POS-Setup-$ProductVersion-$sourceShort.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw "Compiled installer '$installerPath' is missing." }
$installerFile = Get-Item -LiteralPath $installerPath
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$exeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$provenanceHash = (Get-FileHash -LiteralPath $publishProvenance -Algorithm SHA256).Hash.ToLowerInvariant()
$summary = [ordered]@{
    sourceHeadSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $ProductVersion
    fileVersion = $fileVersion
    runtimeIdentifier = [string]$config.runtimeIdentifier
    selfContained = $true
    publishSingleFile = $false
    innoSetupVersion = [string]$compilerVersion
    installerFileName = $installerFile.Name
    installerBytes = [long]$installerFile.Length
    installerSha256 = $installerHash
    executableSha256 = $exeHash
    releaseProvenanceSha256 = $provenanceHash
    publishDirectory = $publishDirectory
    installerPath = $installerPath
    releaseProvenancePath = (Join-Path $packageDirectory 'release-provenance.json')
}
$summaryPath = Join-Path $packageDirectory 'package-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$summary
