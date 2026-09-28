[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$PayloadManifest,
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)][string]$ExpectedProductVersion,
    [Parameter(Mandatory = $true)][string]$CandidateId
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Candidate installer lifecycle verification is restricted to a GitHub Actions runner.'
}
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
$identity = Get-M14CandidateIdentity -CandidateId $CandidateId -ProductVersion $ExpectedProductVersion
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$PayloadManifest = (Resolve-Path -LiteralPath $PayloadManifest).Path
$manifest = Get-Content -LiteralPath $PayloadManifest -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.sourceHeadSha -cne $ExpectedSourceSha.ToLowerInvariant() -or
    $manifest.candidateId -cne $identity.CandidateId -or $manifest.productVersion -cne $ExpectedProductVersion -or
    $manifest.runtimeIdentifier -cne 'win-x64') {
    throw 'Candidate lifecycle manifest does not match the exact source, candidate, version and runtime.'
}

$localAppData = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$appData = [IO.Path]::GetFullPath($env:APPDATA)
$userProfile = [IO.Path]::GetFullPath($env:USERPROFILE)
$installRoot = Join-Path $localAppData 'Programs\Sushi81 POS PREPROD'
$dataRoot = Join-Path $localAppData 'Sushi81 POS PREPROD'
$prodInstallRoot = Join-Path $localAppData 'Programs\Sushi81 POS'
$prodDataRoot = Join-Path $localAppData 'Sushi81 POS'
$shortcut = Join-Path $appData 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS PREPROD.lnk'
$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
foreach ($path in @($installRoot,$dataRoot,$shortcut,$prodInstallRoot,$prodDataRoot)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing candidate lifecycle test because target already exists: '$path'." }
}
if (-not $installRoot.StartsWith($localAppData,[StringComparison]::OrdinalIgnoreCase) -or
    -not $dataRoot.StartsWith($localAppData,[StringComparison]::OrdinalIgnoreCase) -or
    -not $localAppData.StartsWith($userProfile,[StringComparison]::OrdinalIgnoreCase) -or
    $installRoot -eq $dataRoot) { throw 'PreProd program and durable roots are not isolated per-user paths.' }
$existingUninstall = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' })
if ($existingUninstall.Count -gt 0) { throw 'Refusing candidate lifecycle test because the PreProd uninstall identity already exists.' }

New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$synthetic = [ordered]@{
    'Data\live.db' = 'synthetic-preprod-candidate-live-database'
    'Archive\synthetic-annual-archive.db' = 'synthetic-preprod-candidate-archive'
    'Recovery\synthetic-snapshot.fixture' = 'synthetic-preprod-candidate-recovery'
    'Config\local-settings.fixture' = 'synthetic-preprod-candidate-settings'
}
foreach ($entry in $synthetic.GetEnumerator()) {
    $path = Join-Path $dataRoot $entry.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,[string]$entry.Value,[Text.UTF8Encoding]::new($false))
}
function Get-SyntheticTreeHashes {
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $dataRoot -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($dataRoot,$file.FullName)
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $hashes
}
$expectedDataHashes = Get-SyntheticTreeHashes
function Assert-SyntheticTreeUnchanged([string]$Step) {
    $actual = Get-SyntheticTreeHashes
    if ($actual.Count -ne $expectedDataHashes.Count) { throw "Synthetic PreProd durable file count changed after '$Step'." }
    foreach ($relative in $expectedDataHashes.Keys) {
        if (-not $actual.ContainsKey($relative) -or $actual[$relative] -cne $expectedDataHashes[$relative]) {
            throw "Synthetic PreProd durable file '$relative' changed after '$Step'."
        }
    }
}
function Get-PreProdUninstallEntry {
    $entries = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | ForEach-Object {
        $properties = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
        if ($properties.DisplayName -eq 'Sushi81 POS PREPROD') { [pscustomobject]@{ KeyName = $_.PSChildName; Properties = $properties } }
    })
    if ($entries.Count -ne 1 -or $entries[0].KeyName -notmatch '67FB6B75-3C5E-44A5-98AD-305EA4C62D95') {
        throw "Expected exactly one PREPROD uninstall entry under its stable AppId; found $($entries.Count)."
    }
    $entries[0]
}
function Assert-PreProdInstalled([string]$Step) {
    if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) { throw "$Step did not create the PREPROD install root." }
    $markerPath = Join-Path $installRoot 'deployment-profile.txt'
    $markerBytes = [IO.File]::ReadAllBytes($markerPath)
    if ([Text.Encoding]::ASCII.GetString($markerBytes) -cne 'preprod' -or $markerBytes.Length -ne 7) { throw "$Step installed incorrect profile evidence." }
    $provenance = Get-Content -LiteralPath (Join-Path $installRoot 'release-provenance.json') -Raw | ConvertFrom-Json
    if ($provenance.productVersion -cne $ExpectedProductVersion -or $provenance.sourceHeadSha -cne $ExpectedSourceSha.ToLowerInvariant() -or
        $provenance.runtimeIdentifier -cne 'win-x64' -or $provenance.applicationPublishInvocationCount -ne 1) {
        throw "$Step installed payload provenance does not match the candidate source/version or one-publish evidence."
    }
    foreach ($entry in @($manifest.files)) {
        $relative = [string]$entry.path
        $path = Join-Path $installRoot ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
            throw "$Step installed payload hash mismatch for '$relative'."
        }
    }
    $uninstall = Get-PreProdUninstallEntry
    if ($uninstall.Properties.DisplayVersion -cne $ExpectedProductVersion -or
        [IO.Path]::GetFullPath([string]$uninstall.Properties.InstallLocation).TrimEnd('\') -cne $installRoot.TrimEnd('\')) {
        throw "$Step has incorrect PREPROD uninstall version or location."
    }
    if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) { throw "$Step did not create the PREPROD Start Menu shortcut." }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($shortcut)
        $expectedExe = Join-Path $installRoot 'Sushi81.Pos.Desktop.exe'
        if ([IO.Path]::GetFullPath([string]$link.TargetPath) -cne [IO.Path]::GetFullPath($expectedExe)) { throw "$Step PREPROD shortcut targets the wrong executable." }
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $installRoot | Out-Null
    Assert-SyntheticTreeUnchanged $Step
}
function Invoke-PreProdInstaller([string]$Step,[switch]$Uninstall) {
    $target = if ($Uninstall) { Join-Path $installRoot 'unins000.exe' } else { $InstallerPath }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "$Step executable is missing." }
    $process = Start-Process -FilePath $target -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-') -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$Step failed with exit code $($process.ExitCode)." }
    Assert-SyntheticTreeUnchanged $Step
}

$steps = [Collections.Generic.List[string]]::new()
Invoke-PreProdInstaller 'clean PREPROD install'
Assert-PreProdInstalled 'clean PREPROD install'
$steps.Add('clean PREPROD install: profile marker, stable AppId, install path, shortcut, manifest hashes and forbidden content passed')
Invoke-PreProdInstaller 'same-version PREPROD repair/reinstall'
Assert-PreProdInstalled 'same-version PREPROD repair/reinstall'
$steps.Add('same-version PREPROD repair/reinstall preserved the synthetic durable tree')
Invoke-PreProdInstaller 'PREPROD uninstall' -Uninstall
if (Test-Path -LiteralPath $installRoot) { throw 'PREPROD uninstall left installer-owned program files behind.' }
if (Test-Path -LiteralPath $shortcut) { throw 'PREPROD uninstall left its Start Menu shortcut behind.' }
$remaining = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '67FB6B75-3C5E-44A5-98AD-305EA4C62D95' })
if ($remaining.Count -gt 0) { throw 'PREPROD uninstall left its uninstall identity behind.' }
Assert-SyntheticTreeUnchanged 'PREPROD uninstall'
$steps.Add('PREPROD uninstall removed only program identity and preserved the durable synthetic tree')
Invoke-PreProdInstaller 'PREPROD reinstall after uninstall'
Assert-PreProdInstalled 'PREPROD reinstall after uninstall'
$steps.Add('PREPROD reinstall after uninstall passed')

$services = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)sushi81' -or $_.DisplayName -match '(?i)sushi81' })
if ($null -eq (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) { throw 'Hosted runner cannot enumerate scheduled tasks.' }
$tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match '(?i)sushi81' -or $_.TaskPath -match '(?i)sushi81' })
$updaters = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force | Where-Object { $_.Name -match '(?i)(updater|self-update|update-service)' })
if ($services.Count -gt 0 -or $tasks.Count -gt 0 -or $updaters.Count -gt 0) { throw 'Candidate lifecycle found a Sushi81 background service, scheduled task or updater.' }
Assert-SyntheticTreeUnchanged 'final verification'
$steps.Add('no service, scheduled task or background updater; durable synthetic data stayed unchanged')
[pscustomobject]@{
    candidateId = $identity.CandidateId
    candidateTag = $identity.Tag
    expectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
    productVersion = $ExpectedProductVersion
    preprodAppId = '67FB6B75-3C5E-44A5-98AD-305EA4C62D95'
    installRoot = $installRoot
    durableDataRoot = $dataRoot
    syntheticDurableFiles = @($expectedDataHashes.Keys | Sort-Object)
    steps = @($steps)
    interactiveWpfLaunchSmoke = 'deferred to owner acceptance; no headless UI acceptance claimed'
}
