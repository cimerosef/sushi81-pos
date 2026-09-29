[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerPath,[Parameter(Mandatory)][string]$PayloadManifest,
      [Parameter(Mandatory)][string]$ExpectedSourceSha)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true' -or -not $env:RUNNER_TEMP) { throw 'Production lifecycle is restricted to a hosted Actions runner.' }
Import-Module (Join-Path $PSScriptRoot 'M14-CandidatePipeline.psm1') -Force
$manifest = Get-Content -LiteralPath $PayloadManifest -Raw | ConvertFrom-Json
if ($manifest.candidateId -cne 'C01' -or $manifest.sourceHeadSha -cne $ExpectedSourceSha -or
    $manifest.productVersion -cne '1.0.1' -or $manifest.runtimeIdentifier -cne 'win-x64' -or
    [int]$manifest.fileCount -ne 418) { throw 'Lifecycle manifest is not the accepted C01.' }
$local = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$app = [IO.Path]::GetFullPath($env:APPDATA)
$profile = [IO.Path]::GetFullPath($env:USERPROFILE)
if (-not $local.StartsWith($profile,[StringComparison]::OrdinalIgnoreCase)) { throw 'Hosted profile root is not isolated.' }
$install = Join-Path $local 'Programs\Sushi81 POS'
$data = Join-Path $local 'Sushi81 POS'
$preInstall = Join-Path $local 'Programs\Sushi81 POS PREPROD'
$preData = Join-Path $local 'Sushi81 POS PREPROD'
$shortcut = Join-Path $app 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS.lnk'
$preShortcut = Join-Path $app 'Microsoft\Windows\Start Menu\Programs\Sushi81 POS PREPROD.lnk'
$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$appId = 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC'
$preAppId = '67FB6B75-3C5E-44A5-98AD-305EA4C62D95'
foreach ($path in @($install,$data,$preInstall,$preData,$shortcut,$preShortcut)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing hosted lifecycle test because target already exists: '$path'." }
}
foreach ($id in @($appId,$preAppId)) {
    if (@(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match [regex]::Escape($id) }).Count) {
        throw 'Refusing hosted lifecycle test because a Production or PREPROD uninstall identity already exists.'
    }
}
$fixtures = [ordered]@{
    'Data\live.db'='synthetic-production-live'; 'Archive\annual.db'='synthetic-production-archive';
    'Recovery\snapshot.fixture'='synthetic-production-recovery'; 'Config\settings.fixture'='synthetic-production-settings'
}
foreach ($root in @($data,$preData)) {
    foreach ($entry in $fixtures.GetEnumerator()) {
        $path = Join-Path $root $entry.Key
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path,"$root/$($entry.Value)",[Text.UTF8Encoding]::new($false))
    }
}
New-Item -ItemType Directory -Path $preInstall -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $preInstall 'synthetic-preprod-program.fixture'),'synthetic-preprod-program',[Text.UTF8Encoding]::new($false))
function Get-Tree([string]$Root) {
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($Root,$file.FullName)
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $hashes
}
$expectedProdData = Get-Tree $data
$expectedPreData = Get-Tree $preData
$expectedPreInstall = Get-Tree $preInstall
function Assert-Tree([string]$Root,[hashtable]$Expected,[string]$Step) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "$Step removed synthetic tree '$Root'." }
    $actual = Get-Tree $Root
    if ($actual.Count -ne $Expected.Count) { throw "$Step changed synthetic file count in '$Root'." }
    foreach ($key in $Expected.Keys) {
        if (-not $actual.ContainsKey($key) -or $actual[$key] -cne $Expected[$key]) { throw "$Step changed synthetic file '$key'." }
    }
}
function Assert-Synthetic([string]$Step) {
    Assert-Tree $data $expectedProdData $Step
    Assert-Tree $preData $expectedPreData $Step
    Assert-Tree $preInstall $expectedPreInstall $Step
    if (Test-Path -LiteralPath $preShortcut) { throw "$Step unexpectedly created PREPROD shortcut." }
    if (@(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match [regex]::Escape($preAppId) }).Count) {
        throw "$Step unexpectedly created PREPROD uninstall identity."
    }
}
function Get-ProdUninstall {
    $entries = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | ForEach-Object {
        $properties = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
        if ($properties.DisplayName -ceq 'Sushi81 POS') { [pscustomobject]@{key=$_.PSChildName;properties=$properties} }
    })
    if ($entries.Count -ne 1 -or $entries[0].key -notmatch [regex]::Escape($appId)) { throw 'Production uninstall AppId/identity differs.' }
    $entries[0]
}
function Assert-Installed([string]$Step) {
    $marker = [IO.File]::ReadAllBytes((Join-Path $install 'deployment-profile.txt'))
    if ($marker.Length -ne 4 -or [Text.Encoding]::ASCII.GetString($marker) -cne 'prod') { throw "$Step has wrong Production profile marker." }
    foreach ($entry in @($manifest.files)) {
        $path = Join-Path $install ([string]$entry.path).Replace('/',[IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
            throw "$Step installed file does not match accepted C01: $($entry.path)."
        }
    }
    $uninstall = Get-ProdUninstall
    if ($uninstall.properties.DisplayVersion -cne '1.0.1' -or
        [IO.Path]::GetFullPath([string]$uninstall.properties.InstallLocation).TrimEnd('\') -cne $install.TrimEnd('\')) {
        throw "$Step has wrong Production uninstall version/location."
    }
    if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) { throw "$Step did not create Production shortcut." }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($shortcut)
        if ([IO.Path]::GetFullPath([string]$link.TargetPath) -cne (Join-Path $install 'Sushi81.Pos.Desktop.exe')) {
            throw "$Step shortcut target differs from Production executable."
        }
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $install | Out-Null
    Assert-Synthetic $Step
}
function Invoke-Installer([string]$Step,[switch]$Uninstall) {
    $target = if ($Uninstall) { Join-Path $install 'unins000.exe' } else { $InstallerPath }
    $process = Start-Process -FilePath $target -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-') -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$Step failed with exit code $($process.ExitCode)." }
    Assert-Synthetic $Step
}
$steps = [Collections.Generic.List[string]]::new()
Invoke-Installer 'clean Production install'
Assert-Installed 'clean Production install'
$steps.Add('clean Production install and 418 accepted C01 file hashes passed')
Invoke-Installer 'same-version Production repair'
Assert-Installed 'same-version Production repair'
$steps.Add('same-version repair preserved Production/PREPROD synthetic trees')
Invoke-Installer 'Production uninstall' -Uninstall
if (Test-Path -LiteralPath $install) { throw 'Production uninstall left installer-owned program files.' }
if (Test-Path -LiteralPath $shortcut) { throw 'Production uninstall left shortcut.' }
if (@(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match [regex]::Escape($appId) }).Count) {
    throw 'Production uninstall left AppId entry.'
}
$steps.Add('Production uninstall preserved Production/PREPROD synthetic durable trees')
Invoke-Installer 'Production reinstall'
Assert-Installed 'Production reinstall'
$steps.Add('Production reinstall and C01 file hashes passed')
$services = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)sushi81' -or $_.DisplayName -match '(?i)sushi81' })
$tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskName -match '(?i)sushi81' -or $_.TaskPath -match '(?i)sushi81' })
$updaters = @(Get-ChildItem -LiteralPath $install -Recurse -File -Force | Where-Object { $_.Name -match '(?i)(updater|self-update|update-service)' })
if ($services.Count -or $tasks.Count -or $updaters.Count) { throw 'Production installer introduced a service, scheduled task or updater.' }
Assert-Synthetic 'final verification'
[pscustomobject]@{ passed=$true; acceptedFileCount=418; productionDataFiles=$expectedProdData.Count;
    preprodDataFiles=$expectedPreData.Count; preprodInstallFixtureFiles=$expectedPreInstall.Count;
    productionDurableDataPreserved=$true; preprodSyntheticTreesPreserved=$true;
    noServiceScheduledTaskOrUpdater=$true; steps=@($steps);
    interactiveWpfLaunchSmoke='deferred to owner acceptance; no headless UI acceptance claimed' }
