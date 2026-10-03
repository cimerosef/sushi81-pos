[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][string]$CurrentInstaller,
    [Parameter(Mandatory)][string]$PayloadManifest,
    [Parameter(Mandatory)][string]$ExpectedSourceSha,
    [Parameter(Mandatory)][string]$OutputSummary
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$approvedSource = '1dcedb0e0647e290bbc9ca86e3161919537b598a'
$previousSource = '7183c4798f6889fe10af8dfec90bed5b12a26529'
$preprodAppId = '67FB6B75-3C5E-44A5-98AD-305EA4C62D95'
$productionAppId = 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC'

# This environment gate precedes every filesystem/registry target inspection and write.
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or
    $env:RUNNER_OS -cne 'Windows' -or $env:GITHUB_REPOSITORY -cne 'cimerosef/sushi81-pos' -or
    $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_REF -cne 'refs/heads/codex/post-m14-production-maintenance-batch-01' -or
    $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or $ExpectedSourceSha -cne $approvedSource) {
    throw 'PREPROD print-upgrade verification requires the authorized hosted Windows manual-dispatch context and exact source.'
}
foreach ($name in @('RUNNER_TEMP','GITHUB_WORKSPACE','USERPROFILE','LOCALAPPDATA','APPDATA')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value) -or -not [IO.Path]::IsPathFullyQualified($value)) {
        throw "Hosted runner environment path '$name' is unavailable or not absolute."
    }
}
$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP)
$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
$userProfile = [IO.Path]::GetFullPath($env:USERPROFILE)
$localAppData = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$appData = [IO.Path]::GetFullPath($env:APPDATA)

function Test-PathWithin([string]$Path,[string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoReparseAncestor([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Refusing hosted upgrade verification through a reparse-point ancestor.'
            }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
if (-not (Test-PathWithin $localAppData $userProfile) -or -not (Test-PathWithin $appData $userProfile)) {
    throw 'Hosted profile paths are not isolated under the runner user profile.'
}
foreach ($path in @($runnerTemp,$workspace,$userProfile,$localAppData,$appData)) {
    Assert-NoReparseAncestor $path
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'A required hosted runner directory is unavailable.' }
}
$installRoot = Join-Path $localAppData 'Programs\Sushi81 POS PREPROD'
$dataRoot = Join-Path $localAppData 'Sushi81 POS PREPROD'
$shortcut = Join-Path $appData 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS PREPROD.lnk'
$productionPaths = @(
    (Join-Path $localAppData 'Programs\Sushi81 POS'),
    (Join-Path $localAppData 'Sushi81 POS'),
    (Join-Path $appData 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS.lnk')
)
if (-not (Test-PathWithin $installRoot $localAppData) -or -not (Test-PathWithin $dataRoot $localAppData) -or
    [string]::Equals($installRoot,$dataRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'PREPROD install and durable-data roots are not distinct hosted per-user paths.'
}
foreach ($path in @($installRoot,$dataRoot,$shortcut) + $productionPaths) {
    Assert-NoReparseAncestor $path
    if (Test-Path -LiteralPath $path) {
        throw 'Refusing hosted upgrade verification because a Production or PREPROD target already exists.'
    }
}

$uninstallRoots = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)
function Get-Sushi81UninstallEntries {
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction Stop) {
            $properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop
            $displayProperty = $properties.PSObject.Properties['DisplayName']
            $displayName = if ($null -ne $displayProperty) { [string]$displayProperty.Value } else { '' }
            if ($key.PSChildName -match [regex]::Escape($preprodAppId) -or
                $key.PSChildName -match [regex]::Escape($productionAppId) -or
                $displayName -ieq 'Sushi81 POS PREPROD' -or $displayName -ieq 'Sushi81 POS') {
                [pscustomobject]@{ Root=$root; KeyName=$key.PSChildName; DisplayName=$displayName; Properties=$properties }
            }
        }
    }
}
if (@(Get-Sushi81UninstallEntries).Count -ne 0) {
    throw 'Refusing hosted upgrade verification because a Production or PREPROD uninstall identity already exists.'
}
function Assert-ProductionUntouched([string]$Step) {
    foreach ($path in $productionPaths) {
        if (Test-Path -LiteralPath $path) { throw "$Step created a protected Production target." }
    }
    $productionEntries = @(Get-Sushi81UninstallEntries | Where-Object {
        $_.KeyName -match [regex]::Escape($productionAppId) -or $_.DisplayName -ieq 'Sushi81 POS'
    })
    if ($productionEntries.Count -ne 0) { throw "$Step changed the protected Production uninstall identity." }
}

function Resolve-HostedInput([string]$Path,[string]$Role) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "$Role must be an absolute hosted input path." }
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-PathWithin $full $runnerTemp) -and -not (Test-PathWithin $full $workspace)) {
        throw "$Role is outside the hosted runner temporary/workspace roots."
    }
    Assert-NoReparseAncestor $full
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "$Role is missing." }
    (Resolve-Path -LiteralPath $full).Path
}
$PreviousInstaller = Resolve-HostedInput $PreviousInstaller 'Previous PREPROD installer'
$CurrentInstaller = Resolve-HostedInput $CurrentInstaller 'Current PREPROD installer'
$PayloadManifest = Resolve-HostedInput $PayloadManifest 'Current payload manifest'
Import-Module (Join-Path $PSScriptRoot 'PreProdPrintCandidate.psm1') -Force
$previousFixture = Assert-AcceptedPreProdV102Installer -Path $PreviousInstaller
if ($previousFixture.sourceSha -cne $previousSource) { throw 'The accepted prior-version fixture has the wrong historical source.' }
if ([string]::Equals($PreviousInstaller,$CurrentInstaller,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Previous and current PREPROD installer inputs must be distinct.'
}
foreach ($spec in @(
    @{ Path=$PreviousInstaller; Version='1.0.2'; FileVersion='1.0.2.0' },
    @{ Path=$CurrentInstaller; Version='1.0.3'; FileVersion='1.0.3.0' }
)) {
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($spec.Path)
    if ($versionInfo.ProductName -cne 'Sushi81 POS PREPROD' -or
        $versionInfo.FileVersion -cne $spec.FileVersion -or $versionInfo.ProductVersion -cne $spec.Version) {
        throw 'An installer input does not identify the required PREPROD product/version before launch.'
    }
}
if (-not [IO.Path]::IsPathFullyQualified($OutputSummary)) { throw 'OutputSummary must be an absolute hosted output path.' }
$OutputSummary = [IO.Path]::GetFullPath($OutputSummary)
if ((-not (Test-PathWithin $OutputSummary $runnerTemp) -and -not (Test-PathWithin $OutputSummary $workspace)) -or
    (Test-PathWithin $OutputSummary $localAppData) -or (Test-PathWithin $OutputSummary $appData)) {
    throw 'OutputSummary is outside the isolated hosted output roots.'
}
Assert-NoReparseAncestor $OutputSummary
if (Test-Path -LiteralPath $OutputSummary) { throw 'Refusing to reuse an existing upgrade summary.' }

$manifest = Get-Content -LiteralPath $PayloadManifest -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.sourceHeadSha -cne $approvedSource -or
    $manifest.productVersion -cne '1.0.3' -or $manifest.runtimeIdentifier -cne 'win-x64' -or
    $manifest.profileEvidenceExcluded -cne 'deployment-profile.txt (root file; packaging-only profile identity)') {
    throw 'Upgrade manifest does not match the approved 1.0.3 source/version/runtime/schema.'
}
$manifestEntries = @($manifest.files | Sort-Object -Property path)
if ($manifestEntries.Count -eq 0) { throw 'The new application payload manifest is empty.' }
$seenPaths = @{}
foreach ($entry in $manifestEntries) {
    $relative = [string]$entry.path
    $segments = $relative.Split('/')
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
        $relative -match '[\\<>:"|?*\x00-\x1f]' -or $relative -ieq 'deployment-profile.txt' -or
        @($segments | Where-Object { $_ -in @('','.','..') }).Count -gt 0 -or
        $seenPaths.ContainsKey($relative) -or [long]$entry.bytes -lt 0 -or
        [string]$entry.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'The new application payload manifest contains an invalid, duplicate or packaging-only entry.'
    }
    $seenPaths[$relative] = $true
}
foreach ($required in @('Sushi81.Pos.Desktop.exe','Sushi81.Pos.Desktop.dll','release-provenance.json','zh-CN/Sushi81.Pos.Desktop.resources.dll')) {
    if (-not $seenPaths.ContainsKey($required)) { throw 'The new application manifest omits a required executable/provenance/localization payload.' }
}
$treeText = (($manifestEntries | ForEach-Object { "$($_.path)`t$($_.sha256)`t$($_.bytes)" }) -join "`n") + "`n"
$treeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($treeText))).ToLowerInvariant()
if ($treeHash -cne [string]$manifest.applicationPayloadTreeSha256) { throw 'The application payload tree hash does not match its manifest entries.' }
$previousInstallerHash = (Get-FileHash -LiteralPath $PreviousInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$currentInstallerHash = (Get-FileHash -LiteralPath $CurrentInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (Get-FileHash -LiteralPath $PayloadManifest -Algorithm SHA256).Hash.ToLowerInvariant()

function Assert-NoBackgroundComponents([string]$Step) {
    $services = @(Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object {
        $_.Name -match '(?i)sushi81' -or $_.DisplayName -match '(?i)sushi81'
    })
    if ($null -eq (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) { throw 'Hosted runner cannot enumerate scheduled tasks.' }
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        $_.TaskName -match '(?i)sushi81' -or $_.TaskPath -match '(?i)sushi81'
    })
    $updaters = @()
    if (Test-Path -LiteralPath $installRoot -PathType Container) {
        $updaters = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force | Where-Object {
            $_.Name -match '(?i)(updater|self-update|update-service)'
        })
    }
    if ($services.Count -ne 0 -or $tasks.Count -ne 0 -or $updaters.Count -ne 0) {
        throw "$Step found a Sushi81 service, scheduled task or updater."
    }
}
function Get-PreProdUninstallEntry {
    $entries = @(Get-Sushi81UninstallEntries | Where-Object {
        $_.KeyName -match [regex]::Escape($preprodAppId) -or $_.DisplayName -ieq 'Sushi81 POS PREPROD'
    })
    if ($entries.Count -ne 1 -or $entries[0].Root -cne $uninstallRoots[0] -or
        $entries[0].KeyName -inotmatch ('^\{' + [regex]::Escape($preprodAppId) + '\}_is1$') -or
        $entries[0].DisplayName -cne 'Sushi81 POS PREPROD') {
        throw 'Expected exactly one per-user PREPROD uninstall entry with the frozen AppId/name.'
    }
    $entries[0]
}
function Assert-PreProdInstalled([string]$Step,[string]$Version,[string]$Source) {
    Assert-NoReparseAncestor $installRoot
    if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) { throw "$Step did not create the PREPROD install root." }
    $exePath = Join-Path $installRoot 'Sushi81.Pos.Desktop.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "$Step omitted the installed PREPROD executable." }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    if ($versionInfo.FileVersion -cne "$Version.0" -or $versionInfo.ProductVersion -cne "$Version+$Source") {
        throw "$Step installed the wrong executable file/informational version."
    }
    $marker = [IO.File]::ReadAllBytes((Join-Path $installRoot 'deployment-profile.txt'))
    if ($marker.Length -ne 7 -or [Text.Encoding]::ASCII.GetString($marker) -cne 'preprod') { throw "$Step installed the wrong profile marker." }
    $provenance = Get-Content -LiteralPath (Join-Path $installRoot 'release-provenance.json') -Raw | ConvertFrom-Json
    if ($provenance.schemaVersion -ne 1 -or $provenance.productVersion -cne $Version -or
        $provenance.fileVersion -cne "$Version.0" -or $provenance.sourceHeadSha -cne $Source -or
        $provenance.informationalVersion -cne "$Version+$Source" -or $provenance.runtimeIdentifier -cne 'win-x64' -or
        $provenance.selfContained -isnot [bool] -or -not $provenance.selfContained) {
        throw "$Step installed provenance with the wrong source/version/runtime identity."
    }
    $uninstall = Get-PreProdUninstallEntry
    if ($uninstall.Properties.DisplayVersion -cne $Version -or
        -not [string]::Equals([IO.Path]::GetFullPath([string]$uninstall.Properties.InstallLocation).TrimEnd('\'),
            $installRoot.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)) {
        throw "$Step installed incorrect PREPROD uninstall version/location metadata."
    }
    if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) { throw "$Step omitted the PREPROD Start Menu shortcut." }
    $shell = New-Object -ComObject WScript.Shell
    $link = $null
    try {
        $link = $shell.CreateShortcut($shortcut)
        if (-not [string]::Equals([IO.Path]::GetFullPath([string]$link.TargetPath),$exePath,[StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([IO.Path]::GetFullPath([string]$link.WorkingDirectory),$installRoot,[StringComparison]::OrdinalIgnoreCase)) {
            throw "$Step installed a PREPROD shortcut outside the frozen install root."
        }
    } finally {
        if ($null -ne $link) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
    Assert-ProductionUntouched $Step
    Assert-NoBackgroundComponents $Step
    $uninstall.KeyName
}
function Invoke-PreProdInstaller([string]$Path,[string]$Step) {
    # Only the two validated PREPROD inputs are ever launched; no app or uninstaller is run.
    $process = Start-Process -FilePath $Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$installRoot`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$Step failed with exit code $($process.ExitCode)." }
    Assert-ProductionUntouched $Step
}

Assert-NoBackgroundComponents 'preflight'
$steps = [Collections.Generic.List[string]]::new()
# The orchestrating H18 workflow must authenticate the immutable v1.0.2 asset before this call.
Invoke-PreProdInstaller $PreviousInstaller 'install immutable PREPROD 1.0.2 fixture'
$originalUninstallKey = Assert-PreProdInstalled 'PREPROD 1.0.2 fixture' '1.0.2' $previousSource
$originalShortcutHash = (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash.ToLowerInvariant()
$steps.Add('actual PREPROD 1.0.2 fixture installed; stable AppId/name/roots/shortcut/marker and historical source/version verified')
if (Test-Path -LiteralPath $dataRoot) { throw 'The silent fixture install unexpectedly created a durable-data root before seeding.' }
$fixtures = [ordered]@{
    'Data\live.db' = 'H18 synthetic PREPROD database fixture; not an application database'
    'Config\local-settings.json' = '{"fixture":"H18-synthetic-preprod","selectedCulture":"fr-FR","profile":"preprod"}'
    'Config\device-identity.json' = '{"fixture":"H18-synthetic-preprod","deviceId":"11111111-1111-1111-1111-111111111118","displayName":"Synthetic hosted PREPROD device"}'
    'Config\printer-settings.json' = '{"fixture":"H18-synthetic-preprod","kitchenQueue":"Synthetic kitchen queue","customerQueue":"Synthetic customer queue"}'
    'Archive\synthetic-annual-archive.db' = 'H18 synthetic PREPROD archive fixture'
    'Recovery\synthetic-snapshot.fixture' = 'H18 synthetic PREPROD recovery fixture'
}
foreach ($entry in $fixtures.GetEnumerator()) {
    $target = Join-Path $dataRoot $entry.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    [IO.File]::WriteAllText($target,[string]$entry.Value,[Text.UTF8Encoding]::new($false))
}
function Get-SyntheticHashes {
    Assert-NoReparseAncestor $dataRoot
    if (-not (Test-Path -LiteralPath $dataRoot -PathType Container)) { throw 'The synthetic PREPROD durable-data root disappeared.' }
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $dataRoot -Recurse -Force -ErrorAction Stop) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'The synthetic durable tree contains a reparse point.' }
        if ($file.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($dataRoot,$file.FullName)
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $hashes
}
$expectedHashes = Get-SyntheticHashes
if ($expectedHashes.Count -ne $fixtures.Count) { throw 'The initial synthetic fixture file count is incorrect.' }
$steps.Add('six synthetic durable files seeded after the actual prior-version install and hash-inventoried')

# No uninstall occurs between the old installation and this in-place upgrade.
Invoke-PreProdInstaller $CurrentInstaller 'in-place PREPROD 1.0.2 to 1.0.3 upgrade'
$upgradedUninstallKey = Assert-PreProdInstalled 'PREPROD 1.0.3 upgrade' '1.0.3' $approvedSource
if ($upgradedUninstallKey -cne $originalUninstallKey) { throw 'The PREPROD upgrade changed its stable uninstall key.' }
# The shortcut's location/target/working directory remain fixed. Its shell-link
# timestamps may change when Inno recreates it for the upgraded executable.
$upgradedShortcutHash = (Get-FileHash -LiteralPath $shortcut -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($entry in $manifestEntries) {
    $relative = [string]$entry.path
    $target = Join-Path $installRoot $relative.Replace('/',[IO.Path]::DirectorySeparatorChar)
    Assert-NoReparseAncestor $target
    if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
        (Get-Item -LiteralPath $target).Length -ne [long]$entry.bytes -or
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
        throw 'The upgraded PREPROD payload differs from the new manifest byte length or SHA-256.'
    }
}
$actualHashes = Get-SyntheticHashes
if ($actualHashes.Count -ne $expectedHashes.Count) { throw 'The in-place upgrade added or removed synthetic durable files.' }
foreach ($relative in $expectedHashes.Keys) {
    if (-not $actualHashes.ContainsKey($relative) -or $actualHashes[$relative] -cne $expectedHashes[$relative]) {
        throw 'The in-place upgrade changed a synthetic durable-file SHA-256.'
    }
}
Assert-ProductionUntouched 'final verification'
Assert-NoBackgroundComponents 'final verification'
& (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $installRoot | Out-Null
$steps.Add('in-place PREPROD 1.0.3 upgrade retained stable identity/shortcut, exact new manifest hashes and every synthetic durable-file byte')
$steps.Add('Production install/data/shortcut/uninstall identity remained absent; no service/task/updater was installed')

$summary = [ordered]@{
    schemaVersion=1; passed=$true; upgradePreservationPassed=$true; productionIdentityUntouched=$true;
    verification='hosted synthetic PREPROD in-place upgrade';
    repository='cimerosef/sushi81-pos'; workflowHead=$env:GITHUB_SHA;
    previousVersion='1.0.2'; previousSourceSha=$previousSource; previousInstallerSha256=$previousInstallerHash;
    productVersion='1.0.3'; expectedSourceSha=$approvedSource; currentInstallerSha256=$currentInstallerHash;
    applicationPayloadManifestSha256=$manifestHash; installedManifestFiles=$manifestEntries.Count;
    applicationPayloadTreeSha256=$treeHash; installedExecutableFileVersion='1.0.3.0';
    preprodAppId=$preprodAppId; appName='Sushi81 POS PREPROD';
    installRoot='%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD'; durableDataRoot='%LOCALAPPDATA%\Sushi81 POS PREPROD';
    shortcut='%APPDATA%\Microsoft\Windows\Start Menu\Programs\Sushi81 POS PREPROD.lnk';
    stableUninstallKey=$upgradedUninstallKey; shortcutTargetUnchanged=$true;
    previousShortcutSha256=$originalShortcutHash; upgradedShortcutSha256=$upgradedShortcutHash;
    syntheticDurableFileCount=$expectedHashes.Count; syntheticDurableFileSha256=$expectedHashes;
    productionProtection='fail-closed preflight; Production roots/shortcut/uninstall identity remained absent';
    serviceCount=0; scheduledTaskCount=0; updaterCount=0; uninstallBetweenVersionsPerformed=$false;
    realBusinessDataUsed=$false; realProductionDeploymentPerformed=$false;
    interactiveWpfLaunchSmoke='NOT RUN'; physicalThermalAcceptance='NOT RUN'; steps=@($steps)
}
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputSummary) -Force | Out-Null
[IO.File]::WriteAllText($OutputSummary,($summary | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
[pscustomobject]$summary
