$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$json = & dotnet list Sushi81.Pos.sln package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw 'NuGet vulnerability audit failed to restore or inspect the package graph.'
}

$audit = ($json -join "`n") | ConvertFrom-Json -AsHashtable
$problems = @()
if ($audit.ContainsKey('problems')) {
    $problems = @($audit.problems | Where-Object { $_.level -in @('error', 'warning') })
}
if ($problems.Count -gt 0) {
    throw "NuGet vulnerability audit returned $($problems.Count) problem(s); inspect the command output before accepting this source head."
}
if (@($audit.projects).Count -eq 0) {
    throw 'NuGet vulnerability audit returned no project graph.'
}

$vulnerabilities = [System.Collections.Generic.List[object]]::new()
function Find-Vulnerabilities($node) {
    if ($null -eq $node) { return }
    if ($node -is [System.Collections.IDictionary]) {
        foreach ($key in $node.Keys) {
            if ([string]$key -ieq 'vulnerabilities' -and $node[$key]) {
                foreach ($item in $node[$key]) { $vulnerabilities.Add($item) }
            }
            else { Find-Vulnerabilities $node[$key] }
        }
    }
    elseif ($node -is [System.Collections.IEnumerable] -and $node -isnot [string]) {
        foreach ($item in $node) { Find-Vulnerabilities $item }
    }
}

Find-Vulnerabilities $audit
if ($vulnerabilities.Count -gt 0) {
    foreach ($item in $vulnerabilities) {
        Write-Error "Vulnerable NuGet package detected: $($item.severity) $($item.advisoryurl)"
    }
    throw "NuGet vulnerability audit found $($vulnerabilities.Count) vulnerable package reference(s)."
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    'Direct and transitive NuGet vulnerability audit: no findings.' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
Write-Output 'Direct and transitive NuGet vulnerability audit passed.'
