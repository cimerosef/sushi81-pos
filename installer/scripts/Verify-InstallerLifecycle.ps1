[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CurrentInstaller,
    [string]$PreviousInstaller,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourceSha,
    [string]$PreviousSourceSha,
    [Parameter(Mandatory = $true)]
    [string]$OutputSummary,
    [string]$PreProductionInstaller,
    [string]$PayloadManifest,
    [string]$ExpectedProductVersion
)

$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-config.json') -Raw | ConvertFrom-Json
$CurrentInstaller = (Resolve-Path -LiteralPath $CurrentInstaller).Path
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Installer lifecycle verification is restricted to a GitHub Actions runner.'
}

if (-not [string]::IsNullOrWhiteSpace($PreProductionInstaller)) {
    if ([string]::IsNullOrWhiteSpace($PayloadManifest)) { throw 'PayloadManifest is required for dual-installer lifecycle verification.' }
    if ([string]::IsNullOrWhiteSpace($ExpectedProductVersion)) { throw 'ExpectedProductVersion is required for dual-installer lifecycle verification.' }
    if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedSourceSha must be a full 40-character commit SHA.' }
    $PreProductionInstaller = (Resolve-Path -LiteralPath $PreProductionInstaller).Path
    $PayloadManifest = (Resolve-Path -LiteralPath $PayloadManifest).Path
    $manifest = Get-Content -LiteralPath $PayloadManifest -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceHeadSha -ne $ExpectedSourceSha.ToLowerInvariant() -or
        $manifest.profileEvidenceExcluded -cne 'deployment-profile.txt (root file; packaging-only profile identity)') {
        throw 'Application payload manifest has the wrong schema, source head, or profile-evidence exclusion.'
    }
    $manifestEntries = @($manifest.files | Sort-Object -Property path)
    if ($manifestEntries.Count -eq 0) { throw 'Application payload manifest must contain at least one file.' }
    $seenManifestPaths = @{}
    foreach ($entry in $manifestEntries) {
        $path = [string]$entry.path
        if ([string]::IsNullOrWhiteSpace($path) -or [System.IO.Path]::IsPathRooted($path) -or
            $path -ceq 'deployment-profile.txt' -or $path.Split('/') -contains '..' -or
            $path -match '\\' -or $path -match '(^|/)\./') {
            throw "Application payload manifest contains an invalid or packaging-only path '$path'."
        }
        $key = $path.ToLowerInvariant()
        if ($seenManifestPaths.ContainsKey($key)) { throw "Application payload manifest repeats path '$path'." }
        $seenManifestPaths[$key] = $true
        if ($entry.bytes -lt 0 -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Application payload manifest has invalid size/hash for '$path'."
        }
    }
    $manifestTreeText = (($manifestEntries | ForEach-Object { "$($_.path)`t$($_.sha256)`t$($_.bytes)" }) -join "`n") + "`n"
    $manifestTreeHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($manifestTreeText))).ToLowerInvariant()
    if ($manifestTreeHash -cne [string]$manifest.applicationPayloadTreeSha256) {
        throw 'Application payload tree hash does not match its manifest entries.'
    }
    $expectedAppIds = @([string]$config.innoAppId, [string]$config.preprodInnoAppId)
    if ($expectedAppIds[0] -eq $expectedAppIds[1] -or
        $expectedAppIds[0] -ne 'C7A1B9E2-1E62-4B4B-A2EA-7802814408FC' -or
        $expectedAppIds[1] -ne '67FB6B75-3C5E-44A5-98AD-305EA4C62D95') {
        throw 'Production or PreProd AppId differs from its frozen WP2 identity.'
    }
    if ([string]$config.binaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS' -or
        [string]$config.preprodBinaryInstallDirectory -cne '{localappdata}\Programs\Sushi81 POS PREPROD' -or
        [string]$config.durableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS' -or
        [string]$config.preprodDurableDataDirectory -cne '%LOCALAPPDATA%\Sushi81 POS PREPROD') {
        throw 'Packaging configuration changed a frozen install or durable-data root.'
    }

    if ([string]::IsNullOrWhiteSpace($env:USERPROFILE) -or [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or
        [string]::IsNullOrWhiteSpace($env:APPDATA)) { throw 'Hosted runner profile paths are unavailable.' }
    $userProfile = [System.IO.Path]::GetFullPath($env:USERPROFILE)
    $localAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    $profiles = @(
        [pscustomobject]@{
            Key = 'prod'; Marker = 'prod'; AppId = $expectedAppIds[0]; AppName = 'Sushi81 POS';
            InstallRoot = Join-Path $localAppData 'Programs\Sushi81 POS';
            DataRoot = Join-Path $localAppData 'Sushi81 POS'; Shortcut = Join-Path $startMenu 'Sushi81 POS.lnk'; Installer = $CurrentInstaller
        },
        [pscustomobject]@{
            Key = 'preprod'; Marker = 'preprod'; AppId = $expectedAppIds[1]; AppName = 'Sushi81 POS PREPROD';
            InstallRoot = Join-Path $localAppData 'Programs\Sushi81 POS PREPROD';
            DataRoot = Join-Path $localAppData 'Sushi81 POS PREPROD'; Shortcut = Join-Path $startMenu 'Sushi81 POS PREPROD.lnk'; Installer = $PreProductionInstaller
        }
    )
    if ([string]$config.productName -cne 'Sushi81 POS' -or [string]$config.preprodProductName -cne 'Sushi81 POS PREPROD') {
        throw 'Production or PreProd AppName differs from the approved installer identity.'
    }
    foreach ($profile in $profiles) {
        if (-not $profile.InstallRoot.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase) -or
            -not $profile.DataRoot.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase) -or
            -not $localAppData.StartsWith($userProfile, [StringComparison]::OrdinalIgnoreCase) -or
            $profile.InstallRoot -eq $profile.DataRoot) {
            throw "Profile '$($profile.Key)' install/data roots are not distinct per-user paths."
        }
    }
    foreach ($profile in $profiles) {
        foreach ($path in @($profile.InstallRoot, $profile.DataRoot, $profile.Shortcut)) {
            if (Test-Path -LiteralPath $path) { throw "Refusing dual-installer lifecycle test because target already exists: '$path'." }
        }
        $existingUninstall = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match [regex]::Escape($profile.AppId) })
        if ($existingUninstall.Count -gt 0) { throw "Refusing lifecycle test because an uninstall entry already exists for '$($profile.Key)'." }
    }

    function New-SyntheticDurableTree($profile) {
        New-Item -ItemType Directory -Path $profile.DataRoot -Force | Out-Null
        $fixtures = [ordered]@{
            'Data\live.db' = "synthetic-$($profile.Key)-live-database-v1"
            'Archive\synthetic-annual-archive.db' = "synthetic-$($profile.Key)-archive-v1"
            'Recovery\synthetic-snapshot.fixture' = "synthetic-$($profile.Key)-recovery-v1"
            'Config\local-settings.fixture' = "synthetic-$($profile.Key)-settings-v1"
            'Config\device-identity.fixture' = "synthetic-$($profile.Key)-device-v1"
            'Config\authority-state.fixture' = "synthetic-$($profile.Key)-authority-v1"
        }
        foreach ($entry in $fixtures.GetEnumerator()) {
            $target = Join-Path $profile.DataRoot $entry.Key
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [System.IO.File]::WriteAllBytes($target, [System.Text.Encoding]::UTF8.GetBytes($entry.Value))
        }
        $hashes = @{}
        foreach ($file in Get-ChildItem -LiteralPath $profile.DataRoot -Recurse -File) {
            $relative = [System.IO.Path]::GetRelativePath($profile.DataRoot, $file.FullName)
            $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        return $hashes
    }

    $durableHashes = @{}
    foreach ($profile in $profiles) { $durableHashes[$profile.Key] = New-SyntheticDurableTree $profile }
    function Assert-DurableTreesUnchanged([string]$Step) {
        foreach ($profile in $profiles) {
            $expected = $durableHashes[$profile.Key]
            $actual = @{}
            if (-not (Test-Path -LiteralPath $profile.DataRoot -PathType Container)) { throw "Durable synthetic tree disappeared after ${Step}: $($profile.DataRoot)." }
            foreach ($file in Get-ChildItem -LiteralPath $profile.DataRoot -Recurse -File) {
                $relative = [System.IO.Path]::GetRelativePath($profile.DataRoot, $file.FullName)
                $actual[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            if ($actual.Count -ne $expected.Count) { throw "Durable synthetic file count changed for $($profile.Key) after $Step." }
            foreach ($relative in $expected.Keys) {
                if (-not $actual.ContainsKey($relative) -or $actual[$relative] -ne $expected[$relative]) {
                    throw "Durable synthetic file '$($profile.Key)\$relative' changed after $Step."
                }
            }
        }
    }
    function Invoke-ProfileInstaller($profile, [string]$Step, [switch]$Uninstall) {
        $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
        $target = if ($Uninstall) { Join-Path $profile.InstallRoot 'unins000.exe' } else { $profile.Installer }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "$Step executable is missing: '$target'." }
        $process = Start-Process -FilePath $target -ArgumentList $arguments -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw "$Step failed with exit code $($process.ExitCode)." }
        Assert-DurableTreesUnchanged $Step
    }
    function Get-ProfileUninstallEntry($profile) {
        $entries = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | ForEach-Object {
            $properties = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
            if ($properties.DisplayName -eq $profile.AppName) {
                [pscustomobject]@{ KeyName = $_.PSChildName; Properties = $properties }
            }
        })
        if ($entries.Count -ne 1) { throw "Expected one distinct '$($profile.AppName)' uninstall entry, found $($entries.Count)." }
        if ($entries[0].KeyName -notmatch [regex]::Escape($profile.AppId)) { throw "Uninstall entry for '$($profile.Key)' does not contain its stable AppId." }
        return $entries[0]
    }
    function Assert-ProfileInstalled($profile, [string]$Step) {
        $exe = Join-Path $profile.InstallRoot 'Sushi81.Pos.Desktop.exe'
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "$Step did not leave the '$($profile.Key)' executable." }
        $markerPath = Join-Path $profile.InstallRoot 'deployment-profile.txt'
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw "$Step is missing the '$($profile.Key)' profile evidence." }
        $markerBytes = [System.IO.File]::ReadAllBytes($markerPath)
        if ([System.Text.Encoding]::ASCII.GetString($markerBytes) -cne $profile.Marker -or
            $markerBytes.Length -ne $profile.Marker.Length) { throw "$Step installed malformed '$($profile.Key)' profile evidence." }
        $provenance = Get-Content -LiteralPath (Join-Path $profile.InstallRoot 'release-provenance.json') -Raw | ConvertFrom-Json
        if ($provenance.productVersion -ne $ExpectedProductVersion -or
            $provenance.sourceHeadSha -ne $ExpectedSourceSha.ToLowerInvariant() -or
            $provenance.runtimeIdentifier -ne 'win-x64' -or $provenance.selfContained -ne $true) {
            throw "$Step installed provenance does not match the exact source/version/RID."
        }
        foreach ($entry in $manifest.files) {
            $payloadPath = Join-Path $profile.InstallRoot ([string]$entry.path.Replace('/', '\'))
            if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) { throw "$Step is missing payload file '$($entry.path)'." }
            $hash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -cne [string]$entry.sha256) { throw "$Step payload hash mismatch for '$($entry.path)'." }
        }
        $uninstall = Get-ProfileUninstallEntry $profile
        if ($uninstall.Properties.DisplayVersion -ne $ExpectedProductVersion -or
            [System.IO.Path]::GetFullPath([string]$uninstall.Properties.InstallLocation).TrimEnd('\') -ne $profile.InstallRoot.TrimEnd('\')) {
            throw "$Step has incorrect uninstall version/location for '$($profile.Key)'."
        }
        if (-not (Test-Path -LiteralPath $profile.Shortcut -PathType Leaf)) { throw "$Step is missing '$($profile.Key)' Start Menu shortcut." }
        $shell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $shell.CreateShortcut($profile.Shortcut)
            if ([System.IO.Path]::GetFullPath([string]$shortcut.TargetPath) -ne [System.IO.Path]::GetFullPath($exe)) {
                throw "$Step shortcut for '$($profile.Key)' targets the wrong executable."
            }
        } finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
        & (Join-Path $PSScriptRoot 'Test-ForbiddenContent.ps1') -Path $profile.InstallRoot | Out-Null
    }
    function Assert-ProfileUninstalled($profile, $other, [string]$Step) {
        if (Test-Path -LiteralPath $profile.InstallRoot) { throw "$Step left '$($profile.Key)' program files behind." }
        if (Test-Path -LiteralPath $profile.Shortcut) { throw "$Step left '$($profile.Key)' shortcut behind." }
        $remaining = @(Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match [regex]::Escape($profile.AppId) })
        if ($remaining.Count -gt 0) { throw "$Step left '$($profile.Key)' uninstall entry behind." }
        Assert-ProfileInstalled $other "$Step (other profile remains installed)"
        Assert-DurableTreesUnchanged $Step
    }

    $steps = [System.Collections.Generic.List[string]]::new()
    $prod = $profiles[0]
    $preprod = $profiles[1]
    Invoke-ProfileInstaller $prod 'clean Prod install'
    Assert-ProfileInstalled $prod 'clean Prod install'
    $steps.Add('clean Prod install: exact path, shortcut, uninstall identity, prod marker and payload verified')

    Invoke-ProfileInstaller $preprod 'clean PreProd install while Prod remains installed'
    Assert-ProfileInstalled $prod 'coexistence check'
    Assert-ProfileInstalled $preprod 'clean PreProd install'
    $steps.Add('clean PreProd install beside Prod: distinct identities/paths and common payload hashes verified')

    Invoke-ProfileInstaller $prod 'same-version Prod repair/reinstall'
    Assert-ProfileInstalled $prod 'same-version Prod repair/reinstall'
    Assert-ProfileInstalled $preprod 'Prod repair isolation check'
    $steps.Add('same-version Prod repair/reinstall preserves PreProd and both durable trees')

    Invoke-ProfileInstaller $preprod 'same-version PreProd repair/reinstall'
    Assert-ProfileInstalled $prod 'PreProd repair isolation check'
    Assert-ProfileInstalled $preprod 'same-version PreProd repair/reinstall'
    $steps.Add('same-version PreProd repair/reinstall preserves Prod and both durable trees')

    Invoke-ProfileInstaller $preprod 'PreProd uninstall' -Uninstall
    Assert-ProfileUninstalled $preprod $prod 'PreProd uninstall'
    $steps.Add('PreProd uninstall removes only its program identity and preserves Prod plus both durable trees')

    Invoke-ProfileInstaller $preprod 'PreProd reinstall after uninstall'
    Assert-ProfileInstalled $prod 'PreProd reinstall isolation check'
    Assert-ProfileInstalled $preprod 'PreProd reinstall after uninstall'
    $steps.Add('PreProd reinstall after uninstall: passed')

    Invoke-ProfileInstaller $prod 'Prod uninstall' -Uninstall
    Assert-ProfileUninstalled $prod $preprod 'Prod uninstall'
    $steps.Add('Prod uninstall removes only its program identity and preserves PreProd plus both durable trees')

    Invoke-ProfileInstaller $prod 'Prod reinstall after uninstall'
    Assert-ProfileInstalled $prod 'Prod reinstall after uninstall'
    Assert-ProfileInstalled $preprod 'Prod reinstall isolation check'
    $steps.Add('Prod reinstall after uninstall: passed')

    $serviceMatches = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)sushi81' -or $_.DisplayName -match '(?i)sushi81' })
    $scheduledTaskCommand = Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue
    if ($null -eq $scheduledTaskCommand) { throw 'Hosted runner cannot enumerate scheduled tasks.' }
    $taskMatches = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match '(?i)sushi81' -or $_.TaskPath -match '(?i)sushi81' })
    if ($serviceMatches.Count -gt 0 -or $taskMatches.Count -gt 0) { throw 'A Sushi81 service or scheduled task was found on the hosted runner.' }
    foreach ($profile in $profiles) {
        $updaters = @(Get-ChildItem -LiteralPath $profile.InstallRoot -Recurse -File | Where-Object { $_.Name -match '(?i)(updater|self-update|update-service)' })
        if ($updaters.Count -gt 0) { throw "A background updater component was found under '$($profile.Key)'." }
    }
    $steps.Add('no installer writes to either durable data root; no service, scheduled task or background updater introduced')
    Assert-DurableTreesUnchanged 'final verification'

    $dualSummary = [ordered]@{
        expectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
        productVersion = $ExpectedProductVersion
        prodAppId = $prod.AppId
        preprodAppId = $preprod.AppId
        prodInstallRoot = $prod.InstallRoot
        preprodInstallRoot = $preprod.InstallRoot
        prodDurableDataRoot = $prod.DataRoot
        preprodDurableDataRoot = $preprod.DataRoot
        prodMarker = $prod.Marker
        preprodMarker = $preprod.Marker
        applicationPayloadTreeSha256 = [string]$manifest.applicationPayloadTreeSha256
        commonApplicationFileCount = @($manifest.files).Count
        excludedPackagingOnlyFile = 'deployment-profile.txt'
        syntheticDurableFiles = [ordered]@{ prod = $durableHashes.prod; preprod = $durableHashes.preprod }
        steps = @($steps)
        interactiveLaunchSmoke = 'deferred to owner acceptance; no headless UI acceptance claimed'
    }
    $OutputSummary = [System.IO.Path]::GetFullPath($OutputSummary)
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputSummary) -Force | Out-Null
    $dualSummary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputSummary -Encoding utf8
    $dualSummary
    return
}

if ([string]::IsNullOrWhiteSpace($PreviousInstaller) -or [string]::IsNullOrWhiteSpace($PreviousSourceSha)) {
    throw 'PreviousInstaller and PreviousSourceSha are required for legacy single-profile lifecycle verification.'
}
$PreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstaller).Path
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
