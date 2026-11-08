[CmdletBinding()]
param(
    [string]$RepositoryPath = (Get-Location).Path,
    [string]$GitHubUrl = 'https://github.com/behnamaltafi/IdentityProvider.git',
    [switch]$ApprovedRemoteChange
)
$ErrorActionPreference = 'Stop'
if ($GitHubUrl -notmatch '^https://github\.com/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+\.git$') { throw 'Use a credential-free GitHub HTTPS URL.' }
$original = git -C $RepositoryPath remote get-url origin
if ($LASTEXITCODE -ne 0) { throw 'Existing origin is required for preservation.' }
$refs = @(git -C $RepositoryPath ls-remote $GitHubUrl)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read GitHub; authenticate locally without pasting credentials into chat.' }
Write-Host "Current origin: $original"
Write-Host "Proposed origin: $GitHubUrl; GitHub advertised refs: $($refs.Count)"
if ($original -eq $GitHubUrl) { Write-Host 'Origin already matches; run Verify-GitHub.ps1 to compare retained refs.'; return }
if ($refs.Count -gt 0) { throw 'GitHub has history. Fetch into an isolated audit clone and reconcile ancestry before changing remotes or pushing.' }
if (-not $ApprovedRemoteChange) { Write-Host 'PLAN ONLY. Obtain explicit approval, then rerun with -ApprovedRemoteChange. No push is performed.'; return }
git -C $RepositoryPath remote get-url legacy-local *> $null
if ($LASTEXITCODE -eq 0) { throw 'legacy-local already exists. Review its URL before proceeding.' }
git -C $RepositoryPath remote add legacy-local $original
if ($LASTEXITCODE -ne 0) { throw 'Preserving local origin failed.' }
git -C $RepositoryPath remote set-url origin $GitHubUrl
if ($LASTEXITCODE -ne 0) { throw 'Origin update failed; legacy-local retained.' }
Write-Host 'Origin changed; local repository and legacy remote preserved. Push requires separate explicit approval.'
