[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Path).Path
$forbiddenFiles = @(
    '(?i)(^|[\\/])[^\\/]*\.(?:db|sqlite|sqlite3|sqlite-shm|sqlite-wal)$',
    '(?i)(^|[\\/])[^\\/]*\.(?:xlsx|xlsm|csv|log(?:\..*)?|pdb|cs|xaml|resx|csproj|sln|props|targets)$',
    '(?i)(^|[\\/])[^\\/]*(?:secret|token|credential|password)[^\\/]*\.(?:json|txt|xml|config|yaml|yml|env|pem|key|pfx|p12)$',
    '(?i)(^|[\\/])[^\\/]*(?:local-settings|device-identity|authority|pairing|handoff|transfer-state|printer-settings|archive|recovery|snapshot|backup)[^\\/]*\.(?:json|db|bak|zip|txt|yaml|yml|xml|config)$',
    '(?i)(^|[\\/])\.env(?:\..*)?$'
)
$forbiddenDirectories = '(?i)(^|[\\/])(?:Data|Archive|Recovery|Config|tests?|fixtures?|samples?|src|obj|bin)(?:[\\/]|$)'
$matches = [System.Collections.Generic.List[string]]::new()

foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
    $relative = [System.IO.Path]::GetRelativePath($root, $item.FullName)
    if ($item.PSIsContainer -and $relative -match $forbiddenDirectories) {
        $matches.Add($relative)
        continue
    }
    if (-not $item.PSIsContainer) {
        foreach ($pattern in $forbiddenFiles) {
            if ($relative -match $pattern) {
                $matches.Add($relative)
                break
            }
        }
    }
}

if ($matches.Count -gt 0) {
    throw "Forbidden content found under '$root': $($matches -join ', ')"
}

$fileCount = @(Get-ChildItem -LiteralPath $root -Recurse -File).Count
Write-Output "Forbidden-content scan passed ($fileCount files)."
