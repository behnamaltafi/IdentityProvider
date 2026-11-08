[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Baseline,
    [Parameter(Mandatory)] [string]$Current,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'

function Get-NamedProperties($value) {
    if ($null -eq $value) { return @() }
    @($value.PSObject.Properties)
}

function Get-RequiredNames($schema) {
    if ($null -eq $schema -or $null -eq $schema.required) { return @() }
    @($schema.required)
}

$breaking = [System.Collections.Generic.List[string]]::new()

function Compare-Schema($oldSchema, $newSchema, [string]$location) {
    if ($null -eq $oldSchema) { return }
    if ($null -eq $newSchema) {
        $breaking.Add("Schema removed at $location")
        return
    }

    if ($oldSchema.type -and $newSchema.type -and "$($oldSchema.type)" -ne "$($newSchema.type)") {
        $breaking.Add("Type changed at ${location}: $($oldSchema.type) -> $($newSchema.type)")
    }
    if ($oldSchema.'$ref' -and "$($oldSchema.'$ref')" -ne "$($newSchema.'$ref')") {
        $breaking.Add("Schema reference changed at $location")
    }

    foreach ($enumValue in @($oldSchema.enum)) {
        if ($null -ne $enumValue -and @($newSchema.enum) -notcontains $enumValue) {
            $breaking.Add("Enum value '$enumValue' removed at $location")
        }
    }

    $newProperties = @{}
    foreach ($property in Get-NamedProperties $newSchema.properties) { $newProperties[$property.Name] = $property.Value }
    foreach ($property in Get-NamedProperties $oldSchema.properties) {
        if (-not $newProperties.ContainsKey($property.Name)) {
            $breaking.Add("Property removed at $location.$($property.Name)")
        }
        else {
            Compare-Schema $property.Value $newProperties[$property.Name] "$location.$($property.Name)"
        }
    }

    $oldRequired = Get-RequiredNames $oldSchema
    foreach ($requiredName in Get-RequiredNames $newSchema) {
        if ($oldRequired -notcontains $requiredName) {
            $breaking.Add("Property became required at $location.$requiredName")
        }
    }

    if ($oldSchema.items) { Compare-Schema $oldSchema.items $newSchema.items "$location[]" }
}

function Resolve-Parameter($document, $parameter) {
    if ($parameter.'$ref' -match '^#/components/parameters/(?<name>.+)$') {
        return $document.components.parameters.PSObject.Properties[$Matches.name].Value
    }
    $parameter
}

function Get-Parameters($document, $pathItem, $operation) {
    $result = @{}
    foreach ($parameter in @($pathItem.parameters) + @($operation.parameters)) {
        if ($null -eq $parameter) { continue }
        $resolved = Resolve-Parameter $document $parameter
        $result["$($resolved.in):$($resolved.name)"] = $resolved
    }
    $result
}

function Compare-Content($oldContent, $newContent, [string]$location) {
    foreach ($media in Get-NamedProperties $oldContent) {
        $newMedia = $newContent.PSObject.Properties[$media.Name]
        if ($null -eq $newMedia) {
            $breaking.Add("Media type '$($media.Name)' removed at $location")
        }
        else {
            Compare-Schema $media.Value.schema $newMedia.Value.schema "$location content $($media.Name)"
        }
    }
}

$old = Get-Content -Raw -LiteralPath $Baseline | ConvertFrom-Json
$new = Get-Content -Raw -LiteralPath $Current | ConvertFrom-Json
$httpMethods = @('get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace')

foreach ($oldPathProperty in Get-NamedProperties $old.paths) {
    $path = $oldPathProperty.Name
    $oldPath = $oldPathProperty.Value
    $newPathProperty = $new.paths.PSObject.Properties[$path]
    if ($null -eq $newPathProperty) {
        $breaking.Add("Endpoint path removed: $path")
        continue
    }
    $newPath = $newPathProperty.Value

    foreach ($method in $httpMethods) {
        $oldOperation = $oldPath.$method
        if ($null -eq $oldOperation) { continue }
        $newOperation = $newPath.$method
        $operationLocation = "$($method.ToUpperInvariant()) $path"
        if ($null -eq $newOperation) {
            $breaking.Add("Operation removed: $operationLocation")
            continue
        }

        $oldParameters = Get-Parameters $old $oldPath $oldOperation
        $newParameters = Get-Parameters $new $newPath $newOperation
        foreach ($key in $oldParameters.Keys) {
            if (-not $newParameters.ContainsKey($key)) {
                $breaking.Add("Parameter removed from ${operationLocation}: $key")
            }
            else {
                Compare-Schema $oldParameters[$key].schema $newParameters[$key].schema "$operationLocation parameter $key"
            }
        }
        foreach ($key in $newParameters.Keys) {
            if (-not $oldParameters.ContainsKey($key) -and $newParameters[$key].required -eq $true) {
                $breaking.Add("Required parameter added to ${operationLocation}: $key")
            }
            elseif ($oldParameters.ContainsKey($key) -and $oldParameters[$key].required -ne $true -and $newParameters[$key].required -eq $true) {
                $breaking.Add("Parameter became required in ${operationLocation}: $key")
            }
        }

        if ($newOperation.requestBody.required -eq $true -and $oldOperation.requestBody.required -ne $true) {
            $breaking.Add("Request body became required in $operationLocation")
        }
        if ($oldOperation.requestBody.content) {
            Compare-Content $oldOperation.requestBody.content $newOperation.requestBody.content "$operationLocation request"
        }

        foreach ($response in Get-NamedProperties $oldOperation.responses) {
            $newResponse = $newOperation.responses.PSObject.Properties[$response.Name]
            if ($null -eq $newResponse) {
                $breaking.Add("Response $($response.Name) removed from $operationLocation")
            }
            elseif ($response.Value.content) {
                Compare-Content $response.Value.content $newResponse.Value.content "$operationLocation response $($response.Name)"
            }
        }
    }
}

foreach ($schema in Get-NamedProperties $old.components.schemas) {
    $newSchema = $new.components.schemas.PSObject.Properties[$schema.Name]
    if ($null -eq $newSchema) {
        $breaking.Add("Component schema removed: $($schema.Name)")
    }
    else {
        Compare-Schema $schema.Value $newSchema.Value "components.schemas.$($schema.Name)"
    }
}

$uniqueBreaking = @($breaking | Select-Object -Unique)
$report = if ($uniqueBreaking.Count -eq 0) {
    'No breaking OpenAPI changes detected.'
}
else {
    "Breaking OpenAPI changes detected ($($uniqueBreaking.Count)):`n- " + ($uniqueBreaking -join "`n- ")
}

if ($ReportPath) {
    $parent = Split-Path -Parent $ReportPath
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [System.IO.File]::WriteAllText($ReportPath, $report + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}
Write-Host $report
if ($uniqueBreaking.Count -gt 0) { exit 1 }
exit 0
