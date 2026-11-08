[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$To = 'HEAD',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$previousTag = git -C $repositoryRoot describe --tags --abbrev=0 --match 'v[0-9]*' "$To^" 2>$null
if ($LASTEXITCODE -ne 0) {
    $rootCommit = git -C $repositoryRoot rev-list --max-parents=0 $To | Select-Object -First 1
    $range = $To
}
else {
    $range = "$previousTag..$To"
}

$sections = [ordered]@{
    'Breaking Changes' = [System.Collections.Generic.List[string]]::new()
    'Features' = [System.Collections.Generic.List[string]]::new()
    'Bug Fixes' = [System.Collections.Generic.List[string]]::new()
    'Performance' = [System.Collections.Generic.List[string]]::new()
    'Other' = [System.Collections.Generic.List[string]]::new()
}

$records = (git -C $repositoryRoot log $range --no-merges '--format=%s%x1f%b%x1e') -join "`n"
foreach ($record in ($records -split [char]0x1e)) {
    if ([string]::IsNullOrWhiteSpace($record)) { continue }
    $parts = $record.Trim() -split [char]0x1f, 2
    $subject = $parts[0].Trim()
    $body = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $match = [regex]::Match($subject, '^(?<type>[a-z]+)(?:\([^)]+\))?(?<breaking>!)?:\s*(?<text>.+)$')
    if (-not $match.Success) { continue }

    $entry = $match.Groups['text'].Value
    if ($match.Groups['breaking'].Success -or $body -match '(?m)^BREAKING CHANGE:\s+') {
        $sections['Breaking Changes'].Add($entry)
    }

    switch ($match.Groups['type'].Value) {
        'feat' { $sections['Features'].Add($entry) }
        'fix' { $sections['Bug Fixes'].Add($entry) }
        'perf' { $sections['Performance'].Add($entry) }
        'revert' { $sections['Bug Fixes'].Add($entry) }
        { $_ -in @('refactor', 'migration') } { $sections['Other'].Add($entry) }
    }
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# IdentityProvider $Version")
foreach ($section in $sections.GetEnumerator()) {
    if ($section.Value.Count -eq 0) { continue }
    $lines.Add('')
    $lines.Add("## $($section.Key)")
    foreach ($entry in ($section.Value | Select-Object -Unique)) {
        $lines.Add("- $entry")
    }
}
$content = ($lines -join [Environment]::NewLine) + [Environment]::NewLine

if ($OutputPath) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [System.IO.File]::WriteAllText($OutputPath, $content, [System.Text.UTF8Encoding]::new($false))
}
else {
    $content
}
