[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CurrentInstaller,
    [Parameter(Mandatory = $true)]
    [string]$PreviousInstaller,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$PreviousSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$OutputSummary
)

$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
$CurrentInstaller = (Resolve-Path -LiteralPath $CurrentInstaller).Path
$PreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstaller).Path
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Installer lifecycle verification is restricted to a GitHub Actions runner.'
}
if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) { throw 'Hosted runner user profile is unavailable.' }
$userProfile = [System.IO.Path]::GetFullPath($env:USERPROFILE)
$localAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
$dataRoot = Join-Path $localAppData 'Sushi81 POS'
$installRoot = Join-Path $localAppData 'Programs\Sushi81 POS'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS.lnk'

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw 'Hosted runner user profile paths are unavailable.'
}
foreach ($path in @($dataRoot, $installRoot, $shortcut)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing lifecycle test because target already exists: '$path'." }
}
if (-not $installRoot.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase) -or
    -not $dataRoot.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase) -or
    -not $localAppData.StartsWith($userProfile, [StringComparison]::OrdinalIgnoreCase) -or
    $installRoot -eq $dataRoot) {
    throw 'Install and durable data roots must be distinct directories under the hosted runner user profile.'
}

New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$fixtures = [ordered]@{
    'Data\live.db' = 'synthetic-live-database-v1'
    'Archive\synthetic-annual-archive.db' = 'synthetic-archive-database-v1'
    'Recovery\synthetic-snapshot.fixture' = 'synthetic-recovery-snapshot-v1'
    'Config\local-settings.fixture' = 'synthetic-local-settings-v1'
    'Config\device-identity.fixture' = 'synthetic-device-identity-v1'
    'Config\authority-state.fixture' = 'synthetic-authority-state-v1'
    'Config\transfer-state.fixture' = 'synthetic-transfer-state-v1'
    'Config\printer-settings.fixture' = 'synthetic-printer-settings-v1'
}
foreach ($entry in $fixtures.GetEnumerator()) {
    $target = Join-Path $dataRoot $entry.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    [System.IO.File]::WriteAllBytes($target, [System.Text.Encoding]::UTF8.GetBytes($entry.Value))
}

$expectedHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $dataRoot -Recurse -File) {
    $relative = [System.IO.Path]::GetRelativePath($dataRoot, $file.FullName)
    $expectedHashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-SyntheticDataUnchanged([string]$Step) {
    $actual = @{}
    foreach ($file in Get-ChildItem -LiteralPath $dataRoot -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($dataRoot, $file.FullName)
        $actual[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ($actual.Count -ne $expectedHashes.Count) { throw "Durable synthetic dataset file count changed after $Step." }
    foreach ($relative in $expectedHashes.Keys) {
        if (-not $actual.ContainsKey($relative) -or $actual[$relative] -ne $expectedHashes[$relative]) {
            throw "Durable synthetic file '$relative' changed after $Step."
        }
    }
}

function Invoke-Installer([string]$Path, [string]$Step, [switch]$Uninstall) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
    if (-not $Uninstall) { $arguments += "/DIR=`"$installRoot`"" }
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$Step failed with exit code $($process.ExitCode)." }
    Assert-SyntheticDataUnchanged $Step
}

function Assert-InstalledVersion([string]$Step, [string]$Version, [string]$SourceSha) {
    $exe = Join-Path $installRoot 'Sushi81.Pos.Desktop.exe'
    $desktopAssembly = Join-Path $installRoot 'Sushi81.Pos.Desktop.dll'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "$Step did not leave the installed executable." }
    if (-not (Test-Path -LiteralPath $desktopAssembly -PathType Leaf)) { throw "$Step did not leave the Desktop assembly with embedded French neutral resources." }
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'zh-CN\Sushi81.Pos.Desktop.resources.dll') -PathType Leaf)) {
        throw "$Step is missing the zh-CN localization satellite."
    }
    $provenance = Get-Content -LiteralPath (Join-Path $installRoot 'release-provenance.json') -Raw | ConvertFrom-Json
    if ($provenance.productVersion -ne $Version -or $provenance.sourceHeadSha -ne $SourceSha.ToLowerInvariant() -or
        $provenance.runtimeIdentifier -ne 'win-x64' -or $provenance.selfContained -ne $true) {
        throw "$Step installed provenance does not match the expected source/version/RID."
    }
    if (-not $provenance.localizationPayloads.'fr-FR' -or -not $provenance.localizationPayloads.'zh-CN') {
        throw "$Step installed provenance does not declare the fr-FR fallback and zh-CN satellite payloads."
    }
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $installRoot
}

function Assert-PerUserUninstallEntry([string]$ExpectedVersion) {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    $matches = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
    } | Where-Object { $_.DisplayName -eq 'Sushi81 POS' })
    if ($matches.Count -ne 1) { throw 'Expected one Sushi81 POS per-user uninstall entry under HKCU.' }
    if ($matches[0].DisplayVersion -ne $ExpectedVersion) { throw "Uninstall metadata version mismatch; expected $ExpectedVersion." }
    if ([System.IO.Path]::GetFullPath([string]$matches[0].InstallLocation).TrimEnd('\') -ne $installRoot.TrimEnd('\')) {
        throw 'Uninstall entry points outside the expected per-user binary install root.'
    }
}

function Assert-Uninstalled([string]$Step) {
    if (Test-Path -LiteralPath $installRoot) { throw "$Step left installer-owned program files behind." }
    if (Test-Path -LiteralPath $shortcut) { throw "$Step left the installer-owned Start Menu shortcut behind." }
    Assert-SyntheticDataUnchanged $Step
}

$steps = [System.Collections.Generic.List[string]]::new()
Invoke-Installer $CurrentInstaller 'clean per-user install'
Assert-InstalledVersion 'clean install' '1.0.0' $ExpectedSourceSha
Assert-PerUserUninstallEntry '1.0.0'
$steps.Add('clean install: passed')

Invoke-Installer $CurrentInstaller 'same-version repair/reinstall'
Assert-InstalledVersion 'same-version repair' '1.0.0' $ExpectedSourceSha
$steps.Add('same-version repair/reinstall: passed')

$uninstaller = Join-Path $installRoot 'unins000.exe'
if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'Inno Setup uninstaller is missing.' }
Invoke-Installer $uninstaller 'ordinary uninstall' -Uninstall
Assert-Uninstalled 'ordinary uninstall'
$steps.Add('ordinary uninstall preserves all synthetic durable data and removes program files/shortcut: passed')

Invoke-Installer $CurrentInstaller 'reinstall after uninstall'
Assert-InstalledVersion 'reinstall after uninstall' '1.0.0' $ExpectedSourceSha
$steps.Add('reinstall after uninstall: passed')

$uninstaller = Join-Path $installRoot 'unins000.exe'
Invoke-Installer $uninstaller 'prepare previous-version mechanics fixture uninstall' -Uninstall
Assert-Uninstalled 'prepare previous-version mechanics fixture'
Invoke-Installer $PreviousInstaller 'install prior-version installer-mechanics fixture'
Assert-InstalledVersion 'prior-version fixture' '0.9.0' $PreviousSourceSha
Assert-PerUserUninstallEntry '0.9.0'
$steps.Add('WP3 source built as 0.9.0 installer-mechanics fixture: passed; not a claim of a historical installer')

Invoke-Installer $CurrentInstaller 'in-place upgrade from prior-version mechanics fixture'
Assert-InstalledVersion 'in-place upgrade' '1.0.0' $ExpectedSourceSha
Assert-PerUserUninstallEntry '1.0.0'
$steps.Add('in-place upgrade retains stable AppId and every synthetic durable-file hash: passed')

$uninstaller = Join-Path $installRoot 'unins000.exe'
Invoke-Installer $uninstaller 'post-upgrade ordinary uninstall' -Uninstall
Assert-Uninstalled 'post-upgrade ordinary uninstall'
Invoke-Installer $CurrentInstaller 'final reinstall after upgrade uninstall'
Assert-InstalledVersion 'final reinstall' '1.0.0' $ExpectedSourceSha
Assert-SyntheticDataUnchanged 'final reinstall'
$steps.Add('post-upgrade uninstall and reinstall preserve all synthetic durable data: passed')

$serviceMatches = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)sushi81' -or $_.DisplayName -match '(?i)sushi81' })
$scheduledTaskCommand = Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue
if ($null -eq $scheduledTaskCommand) { throw 'Hosted runner cannot enumerate scheduled tasks.' }
$taskMatches = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match '(?i)sushi81' -or $_.TaskPath -match '(?i)sushi81' })
if ($serviceMatches.Count -gt 0 -or $taskMatches.Count -gt 0) { throw 'A Sushi81 service or scheduled task was found on the hosted runner.' }
$updaterMatches = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object { $_.Name -match '(?i)(updater|self-update|update-service)' })
if ($updaterMatches.Count -gt 0) { throw 'A background updater component was found in the installed program tree.' }
$steps.Add('no Sushi81 service, scheduled task, or background updater: passed')
$steps.Add('interactive WPF launch smoke: deferred; final launch/UI acceptance remains owner-controlled')

$summary = [ordered]@{
    expectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
    previousSourceSha = $PreviousSourceSha.ToLowerInvariant()
    appId = [string]$config.innoAppId
    installRoot = $installRoot
    durableDataRoot = $dataRoot
    syntheticFiles = @($expectedHashes.Keys | Sort-Object)
    syntheticFileSha256 = $expectedHashes
    steps = @($steps)
    interactiveLaunchSmoke = 'deferred to owner acceptance; no headless UI acceptance claimed'
}
$OutputSummary = [System.IO.Path]::GetFullPath($OutputSummary)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputSummary) -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputSummary -Encoding utf8
$summary
