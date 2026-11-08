[CmdletBinding()]
param([string]$RepositoryPath = (Get-Location).Path, [string]$GitHubUrl = 'https://github.com/behnamaltafi/IdentityProvider.git')
$ErrorActionPreference = 'Stop'
$local = @(git -C $RepositoryPath for-each-ref '--format=%(objectname) %(refname)' refs/heads refs/tags)
if ($LASTEXITCODE -ne 0) { throw 'Local ref inventory failed.' }
$remote = @(git -C $RepositoryPath ls-remote --heads --tags $GitHubUrl)
if ($LASTEXITCODE -ne 0) { throw 'GitHub ref inventory failed.' }
$map = @{}
foreach ($line in $remote) { $pair = $line -split '\s+'; $map[$pair[1]] = $pair[0] }
foreach ($line in $local) {
    $pair = $line -split '\s+'
    if ($map[$pair[1]] -ne $pair[0]) { throw "Missing or differing GitHub ref: $($pair[1])" }
}
Write-Host "Verified $($local.Count) local branch/tag refs on GitHub; no refs were changed. Also test a fresh full-history clone and private Jenkins checkout."
