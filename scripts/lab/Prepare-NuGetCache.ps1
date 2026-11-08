[CmdletBinding()]
param([string]$LabRoot = 'D:/Work/DevOpsLab')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$destination = Join-Path $LabRoot 'artifacts/bootstrap/nuget'
New-Item -ItemType Directory -Force $destination | Out-Null
$copied = 0
$seen = @{}
Get-ChildItem $repository -Filter project.assets.json -Recurse | Where-Object { $_.FullName -notmatch '\\artifacts\\' } | ForEach-Object {
    $assets = Get-Content $_.FullName -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $seen.ContainsKey($library.Name)) { continue }
        $seen[$library.Name] = $true
        $parts = $library.Name.Split('/')
        $file = "$($parts[0].ToLowerInvariant()).$($parts[1]).nupkg"
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $source = Join-Path $folder "$($library.Name.ToLowerInvariant())/$file"
            if (-not (Test-Path $source)) { continue }
            $expected = (Get-Content "$source.sha512" -Raw).Trim()
            $actual = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($source)))
            if ($actual -ne $expected) { throw "Cached NuGet package checksum failed: $file" }
            $target = Join-Path $destination $file
            if (Test-Path $target) {
                if ((Get-FileHash $target).Hash -ne (Get-FileHash $source).Hash) { throw "Local feed collision: $file" }
            } else { Copy-Item -LiteralPath $source -Destination $target; $copied++ }
            break
        }
    }
}
Write-Host "Prepared verified local feed: $($seen.Count) referenced packages considered, $copied packages copied; no credentials or NuGet configuration copied."
