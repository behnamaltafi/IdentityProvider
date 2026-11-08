[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path $root ('artifacts/failing-ci-' + [Guid]::NewGuid().ToString('N'))
git clone --no-hardlinks $root $fixture
if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated failure fixture.' }
Push-Location $fixture
try {
    npm ci --no-audit --no-fund | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Fixture tooling installation failed.' }
    $test = @'
namespace IdentityProvider.Application.UnitTests;
public class DeliberatePipelineFailure {
    [Xunit.Fact] public void FailureMustStopCi() => Xunit.Assert.Fail("Intentional isolated CI failure propagation test");
}
'@
    [IO.File]::WriteAllText((Join-Path $fixture 'Tests/IdentityProvider.Application.UnitTests/DeliberatePipelineFailure.cs'), $test)
    & (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File scripts/ci/Invoke-CI.ps1 *> failure.log
    $exit = $LASTEXITCODE
    if ($exit -eq 0) { throw 'CI incorrectly accepted a required failing test.' }
    $reports = @(Get-ChildItem artifacts/test-results -Filter junit.xml -Recurse)
    if (-not ($reports | Where-Object { ([xml](Get-Content $_.FullName -Raw)).testsuite.failures -gt 0 })) { throw 'No real failing JUnit evidence was generated.' }
    Write-Host "PASS: actual failing unit test caused CI exit $exit; failing JUnit preserved at $fixture"
} finally { Pop-Location }
