param(
    [switch]$SelfTest,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This narrow path is reserved for deliberately synthetic security-scanner test vectors.
# Path-based business-data and credential rules still apply inside it.
$syntheticContentAllowlist = '^tests/(?:[^/]+/)*Fixtures/SyntheticSecurity/'
$maximumTextFileBytes = 5MB
$knownBinaryExtensions = @('.bmp', '.dll', '.exe', '.gif', '.ico', '.jpeg', '.jpg', '.nupkg', '.pdf', '.png', '.ttf', '.woff', '.woff2', '.zip')

function Get-RepositorySafetyFindings {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter()] [string]$Content = ''
    )

    $path = $RelativePath.Replace('\', '/')
    $leaf = [System.IO.Path]::GetFileName($path)
    $findings = [System.Collections.Generic.List[string]]::new()

    if ($leaf -eq '.env' -or ($leaf.StartsWith('.env.', [System.StringComparison]::OrdinalIgnoreCase) -and $leaf -ne '.env.example')) {
        $findings.Add('local-environment-file')
    }
    if ($leaf -match '(?i)^(?:id_(?:rsa|dsa|ecdsa|ed25519)|authorized_keys|.*\.(?:pfx|p12|key))$') {
        $findings.Add('private-key-file')
    }
    if ($leaf -match '(?i)\.(?:db|sqlite|sqlite3)(?:-(?:wal|shm))?$') {
        $findings.Add('business-database-file')
    }
    if ($leaf -match '(?i)\.(?:xls|xlsx|xlsm|csv|log)$') {
        $findings.Add('business-workbook-or-log-file')
    }
    if ($leaf -match '(?i)^(?:local-settings|appsettings\.local|authority-state|recovery-sequence|device-identity|transfer-state|machine-state)\.json$') {
        $findings.Add('machine-local-state-file')
    }

    $isSyntheticSecurityFixture = $path -match $syntheticContentAllowlist
    if (-not $isSyntheticSecurityFixture) {
        if ($Content -match '-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----') {
            $findings.Add('private-key-material')
        }
        if ($Content -match '(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,})(?![A-Za-z0-9_])') {
            $findings.Add('github-token')
        }
        if ($Content -match '(?i)(?<![A-Za-z0-9])xox[baprs]-[A-Za-z0-9-]{20,}(?![A-Za-z0-9])') {
            $findings.Add('slack-token')
        }
        if ($Content -match '(?<![A-Za-z0-9])AIza[A-Za-z0-9_-]{35}(?![A-Za-z0-9])') {
            $findings.Add('google-api-key')
        }
        if ($Content -match '(?<![A-Z0-9])AKIA[0-9A-Z]{16}(?![A-Z0-9])') {
            $findings.Add('aws-access-key')
        }
        if ($Content -match '(?i)\bhttps?://[^/\s:@]+:[^/\s@]+@') {
            $findings.Add('url-embedded-credentials')
        }
    }

    return $findings.ToArray()
}

function Invoke-RepositorySafetySelfTest {
    $syntheticPem = [string]::Concat('-----BEGIN ', 'PRIVATE KEY-----', "`n", 'synthetic-only', "`n", '-----END ', 'PRIVATE KEY-----')
    $syntheticGitHubToken = 'ghp_' + ('a' * 40)
    $syntheticSlackToken = 'xoxb-' + ('A' * 40)
    $syntheticGoogleKey = 'AIza' + ('A' * 35)
    $syntheticAwsKey = 'AKIA' + ('A' * 16)
    $syntheticUrlCredentials = 'https://' + 'synthetic-user:synthetic-password' + '@example.test/path'

    $cases = @(
        @{ Name = 'normal source'; Path = 'src/App.cs'; Content = 'public sealed class Safe { }'; Expected = @() },
        @{ Name = 'ordinary words and synthetic telephone'; Path = 'tests/Fake.Tests/Example.cs'; Content = 'ordinary token and 0612345678'; Expected = @() },
        @{ Name = 'synthetic security fixture'; Path = 'tests/Fake.Tests/Fixtures/SyntheticSecurity/private-key.txt'; Content = $syntheticPem; Expected = @() },
        @{ Name = 'synthetic fixture path still blocks business state'; Path = 'tests/Fake.Tests/Fixtures/SyntheticSecurity/live.db'; Content = ''; Expected = @('business-database-file') },
        @{ Name = 'private key content'; Path = 'src/accidental.txt'; Content = $syntheticPem; Expected = @('private-key-material') },
        @{ Name = 'GitHub credential'; Path = 'src/accidental.txt'; Content = $syntheticGitHubToken; Expected = @('github-token') },
        @{ Name = 'Slack credential'; Path = 'src/accidental.txt'; Content = $syntheticSlackToken; Expected = @('slack-token') },
        @{ Name = 'Google API key'; Path = 'src/accidental.txt'; Content = $syntheticGoogleKey; Expected = @('google-api-key') },
        @{ Name = 'cloud credential'; Path = 'src/accidental.txt'; Content = $syntheticAwsKey; Expected = @('aws-access-key') },
        @{ Name = 'URL credential'; Path = 'src/accidental.txt'; Content = $syntheticUrlCredentials; Expected = @('url-embedded-credentials') },
        @{ Name = 'business database'; Path = 'Data/live.db'; Content = ''; Expected = @('business-database-file') },
        @{ Name = 'local environment'; Path = '.env'; Content = ''; Expected = @('local-environment-file') },
        @{ Name = 'private key file'; Path = 'Config/device.pfx'; Content = ''; Expected = @('private-key-file') },
        @{ Name = 'device identity state'; Path = 'Config/device-identity.json'; Content = ''; Expected = @('machine-local-state-file') },
        @{ Name = 'local workbook'; Path = 'Exports/customer.xlsx'; Content = ''; Expected = @('business-workbook-or-log-file') },
        @{ Name = 'sanitized environment template'; Path = 'config/.env.example'; Content = 'SETTING=${VALUE}'; Expected = @() }
    )

    foreach ($case in $cases) {
        $actual = @(Get-RepositorySafetyFindings -RelativePath $case.Path -Content $case.Content | Sort-Object)
        $expected = @($case.Expected | Sort-Object)
        if (($actual -join '|') -ne ($expected -join '|')) {
            throw "Repository safety self-test '$($case.Name)' expected [$($expected -join ', ')] but got [$($actual -join ', ')]."
        }
    }

    Write-Output "Repository safety scanner self-test passed ($($cases.Count) synthetic cases)."
}

if ($SelfTest) {
    Invoke-RepositorySafetySelfTest
    return
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$trackedOrUnignoredFiles = @(& git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked and non-ignored repository files.'
}

$allFindings = [System.Collections.Generic.List[object]]::new()
foreach ($relativePath in $trackedOrUnignoredFiles) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }
    $fullPath = Join-Path $root $relativePath
        $content = ''
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $file = Get-Item -LiteralPath $fullPath
        if ($file.Length -gt $maximumTextFileBytes -and [System.IO.Path]::GetExtension($file.Name).ToLowerInvariant() -notin $knownBinaryExtensions) {
            throw "Refusing to skip oversized potentially textual file during repository safety scan: $relativePath"
        }
        if ($file.Length -le $maximumTextFileBytes) {
            try {
                $bytes = [System.IO.File]::ReadAllBytes($fullPath)
                if (-not ($bytes -contains 0)) {
                    $content = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
                }
            }
            catch [System.Text.DecoderFallbackException] {
                $content = '' # Binary or non-UTF8 assets are still subject to filename rules.
            }
        }
    }

    foreach ($rule in (Get-RepositorySafetyFindings -RelativePath $relativePath -Content $content)) {
        $allFindings.Add([pscustomobject]@{ Path = $relativePath; Rule = $rule })
    }
}

if ($allFindings.Count -gt 0) {
    foreach ($finding in $allFindings) {
        Write-Error "Repository safety finding: $($finding.Rule) at $($finding.Path)"
    }
    throw "Repository safety scan failed with $($allFindings.Count) finding(s)."
}

Write-Output "Repository safety scan passed for $($trackedOrUnignoredFiles.Count) tracked/non-ignored file(s)."
