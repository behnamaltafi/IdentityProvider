[CmdletBinding()]
param(
    [Alias('MainOpenApiBaseline')][string]$MasterOpenApiBaseline,
    [string]$OpenApiBaseUrl,
    [string]$ArtifactsDirectory = 'artifacts',
    [string]$CommitFrom,
    [switch]$WorkingTreeValidation
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $root
try {
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $root $ArtifactsDirectory))
    $projectConfig = Get-Content (Join-Path $root 'cicd/project.json') -Raw | ConvertFrom-Json
    $solutionPath = $projectConfig.solution
    New-Item -ItemType Directory -Force $artifactRoot | Out-Null
    # Hooks are advisory; validate the entire available history by default.
    if ($WorkingTreeValidation) {
        git rev-parse --verify HEAD *> $null
        if ($LASTEXITCODE -eq 0) { throw 'WorkingTreeValidation is allowed only before the first commit.' }
        Write-Host 'Commit history NOT CHECKED: initial commit is awaiting user approval.'
    }
    else { & "$PSScriptRoot/Test-Commits.ps1" -From $CommitFrom }
    $version = & "$PSScriptRoot/../versioning/Get-Version.ps1"
    $version | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $artifactRoot 'version.json')
    $restoreArguments = @('restore', $solutionPath)
    if ($env:NUGET_LOCAL_SOURCE -and (Test-Path $env:NUGET_LOCAL_SOURCE)) {
        $restoreArguments += @('--source', $env:NUGET_LOCAL_SOURCE, '--source', 'https://api.nuget.org/v3/index.json')
        Write-Host 'Using the verified optional local NuGet bootstrap feed plus nuget.org.'
    }
    dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build $solutionPath -c Release --no-restore `
        -p:ContinuousIntegrationBuild=true "-p:Version=$($version.Version)" `
        "-p:PackageVersion=$($version.Version)" `
        "-p:AssemblyVersion=$($version.AssemblyVersion)" "-p:FileVersion=$($version.FileVersion)" `
        "-p:InformationalVersion=$($version.InformationalVersion)" -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    # Evaluate inherited MSBuild properties; test packages live in Tests/Directory.Build.props.
    [xml]$solution = Get-Content $solutionPath -Raw
    $testProjects = @(
        foreach ($project in $solution.SelectNodes('//Project')) {
            $projectPath = Join-Path $root $project.Path
            $isTest = dotnet msbuild $projectPath -nologo -getProperty:IsTestProject
            if ($LASTEXITCODE -ne 0) { throw "Project evaluation failed: $projectPath" }
            if (("$isTest").Trim() -eq 'true') { $projectPath }
        }
    )
    if ($testProjects.Count -eq 0) { throw 'No test projects were evaluated in the solution.' }
    $runRoot = Join-Path $artifactRoot ("test-results/" + [Guid]::NewGuid().ToString('N'))
    foreach ($project in $testProjects) {
        $resultPath = Join-Path $runRoot ([IO.Path]::GetFileNameWithoutExtension($project))
        dotnet test $project -c Release --no-build --no-restore --collect:'XPlat Code Coverage' `
            --settings (Join-Path $root $projectConfig.testSettings) `
            --logger 'trx;LogFileName=tests.trx' --results-directory $resultPath
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
        if (-not (Get-ChildItem $resultPath -Recurse -Filter coverage.cobertura.xml)) { throw "Coverage missing: $project" }
        $actualCoverage = Get-ChildItem $resultPath -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
        Copy-Item -LiteralPath $actualCoverage.FullName -Destination (Join-Path $resultPath 'coverage.cobertura.xml')
    }
    $priorityResults = Join-Path $runRoot 'priority'
    dotnet test $projectConfig.priorityTestProject `
        -c Release --no-build --no-restore --collect:'XPlat Code Coverage' `
        --settings $projectConfig.prioritySettings --results-directory $priorityResults `
        --logger 'trx;LogFileName=priority.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Priority tests failed.' }
    $reports = @(Get-ChildItem $priorityResults -Recurse -Filter coverage.cobertura.xml)
    if ($reports.Count -eq 0) { throw 'Fresh priority coverage report is missing.' }
    # VSTest may copy attachments into its TRX/In directory. Validate every copy.
    foreach ($report in $reports) {
        [xml]$coverage = Get-Content $report.FullName -Raw
        if ([int]$coverage.coverage.'lines-valid' -le 0) { throw 'Priority coverage matched no executable lines.' }
        $rate = [double]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
        if ($rate -lt $projectConfig.priorityCoverageThreshold) { throw "Priority line coverage $rate is below $($projectConfig.priorityCoverageThreshold)." }
    }
    Write-Host "Priority validator line coverage: $rate"
    if ($OpenApiBaseUrl) {
        $branch = if ($env:BRANCH_NAME) { $env:BRANCH_NAME } else { (git branch --show-current).Trim() }
        if ($branch -ne 'master' -and -not $MasterOpenApiBaseline) { throw 'Non-master OpenAPI checks require the latest successful master contract.' }
        & "$PSScriptRoot/../openapi/Test-OpenApiContract.ps1" -ApiBaseUrl $OpenApiBaseUrl `
            -MasterOpenApiBaseline $MasterOpenApiBaseline -ArtifactsDirectory $artifactRoot
    }
    else {
        Write-Host 'OpenAPI NOT CHECKED: run scripts/openapi/Test-OpenApiContract.ps1 against the running candidate API as a separate CI check (see docs/ci.md).'
    }
    Write-Host "Build/test CI passed for $($version.Version). Results: $runRoot"
}
finally {
    try {
        if ($runRoot -and (Test-Path $runRoot)) { & "$PSScriptRoot/Convert-TestResults.ps1" -Root $runRoot }
    } finally { Pop-Location }
}
