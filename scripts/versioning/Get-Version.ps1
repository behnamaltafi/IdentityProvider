[CmdletBinding()]
param([string]$ReleaseVersion = $env:RELEASE_VERSION)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sha = git -C $repositoryRoot rev-parse --verify HEAD 2>$null
if ($LASTEXITCODE -ne 0) { $sha = 'uncommitted' }
else { $sha = "$sha".Trim() }
if ($ReleaseVersion -and $ReleaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'ReleaseVersion must be an exact MAJOR.MINOR.PATCH value supplied by semantic-release.'
}
$prefix = if ($ReleaseVersion) { $ReleaseVersion } else { '0.0.0' }
$version = if ($ReleaseVersion) { $prefix } else { "$prefix-ci.$($sha.Substring(0, [Math]::Min(12, $sha.Length)))" }
[pscustomobject]@{
    Version = $version
    VersionPrefix = $prefix
    AssemblyVersion = "$prefix.0"
    FileVersion = "$prefix.0"
    InformationalVersion = if ($sha -eq 'uncommitted') { $version } else { "$version+$sha" }
    Sha = $sha
    IsRelease = [bool]$ReleaseVersion
}
