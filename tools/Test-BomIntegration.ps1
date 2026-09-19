<#
.SYNOPSIS
    Offline integration checks for naming rules -> BOM mappings -> BOM columns.
#>
[CmdletBinding()]
param(
    [string] $DllPath,
    [string] $CaseFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $DllPath) {
    $DllPath = Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'
}
if (-not $CaseFile) {
    $CaseFile = Join-Path $PSScriptRoot 'testdata\bom-integration.json'
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DllPath).Path)
$case = (Get-Content -LiteralPath $CaseFile -Raw -Encoding UTF8) | ConvertFrom-Json
$settingsType = $assembly.GetType('MechKit.Core.AddinSettings', $true)
$serviceType = $assembly.GetType('MechKit.Features.PartListService', $true)
$settings = [Activator]::CreateInstance($settingsType)

function Set-Setting([string] $Name, $Value) {
    $settingsType.GetProperty($Name).SetValue($settings, $Value)
}

Set-Setting 'MachinedSegments' $case.layout
Set-Setting 'MachinedSegmentLabels' $case.labels
Set-Setting 'MachinedSegmentBomNameFlags' $case.flags
Set-Setting 'BomMachinedNameField' 'auto'
Set-Setting 'BomMachinedMaterialField' 'auto'
Set-Setting 'BomMachinedProcessField' 'auto'
Set-Setting 'BomMachinedAssemblyNoteField' 'rule:assemblynote'
Set-Setting 'BomNameHeader' $case.nameHeader
Set-Setting 'BomColumnOrder' $case.columnOrder
Set-Setting 'BomRequirePattern' $true

$createOptions = $serviceType.GetMethod('CreateOptions')
$options = $createOptions.Invoke($null, @($settings))
$optionsType = $options.GetType()
$standardMappings = @{
    StandardNameField = 'tail:3'
    StandardMaterialField = 'empty'
    StandardProcessField = 'segment:2'
    StandardSurfaceField = 'empty'
    StandardAssemblyNoteField = 'auto'
    StandardRemarkField = 'auto'
}
$naming = $optionsType.GetProperty('Naming').GetValue($options)
$namingType = $naming.GetType()
$segmentType = $assembly.GetType('MechKit.Core.MachinedSegmentKind', $true)
$resolveRule = $serviceType.GetMethod('ResolveRuleField',
    [System.Reflection.BindingFlags]'Static, NonPublic')
$resolveConfigured = $serviceType.GetMethod('ResolveConfiguredField',
    [System.Reflection.BindingFlags]'Static, NonPublic')
$resolveExpanded = $serviceType.GetMethod('ResolveConfiguredFieldWithNaming',
    [System.Reflection.BindingFlags]'Static, NonPublic')
$failures = New-Object System.Collections.Generic.List[string]
$checked = 0

function Assert-Equal([string] $Label, $Actual, $Expected) {
    $script:checked++
    if ([string]$Actual -ne [string]$Expected) {
        $script:failures.Add("$Label expected '$Expected' but got '$Actual'")
        Write-Host "[FAIL] $Label"
    } else {
        Write-Host "[PASS] $Label -> '$Actual'"
    }
}

foreach ($mapping in $standardMappings.GetEnumerator()) {
    $actual = $optionsType.GetProperty($mapping.Key).GetValue($options)
    Assert-Equal ("standard mapping " + $mapping.Key) $actual $mapping.Value
}

$dictionaryType = [System.Collections.Generic.Dictionary[string,string]]
$properties = [Activator]::CreateInstance($dictionaryType)
foreach ($property in $case.semanticFields.PSObject.Properties) {
    $semantic = $property.Name
    $mapped = $resolveRule.Invoke($null, @($semantic, $naming))
    Assert-Equal "semantic $semantic" $mapped $property.Value
    $invokeArguments = New-Object object[] 5
    $invokeArguments[0] = $mapped
    $invokeArguments[1] = ''
    $invokeArguments[2] = [string]$case.sample
    $invokeArguments[3] = $properties
    $invokeArguments[4] = $false
    $value = $resolveConfigured.Invoke($null, $invokeArguments)
    Assert-Equal "value $semantic" $value $case.expectedValues.$semantic
}

foreach ($expanded in @(
    @('machined2:material', [string]$case.expectedExpandedMaterial),
    @('machined2:process', [string]$case.expectedExpandedProcess),
    @('machined2:surface', [string]$case.expectedExpandedSurface)
)) {
    $expandedArguments = New-Object object[] 6
    $expandedArguments[0] = $expanded[0]
    $expandedArguments[1] = ''
    $expandedArguments[2] = [string]$case.sample
    $expandedArguments[3] = $properties
    $expandedArguments[4] = $false
    $expandedArguments[5] = $naming
    $value = $resolveExpanded.Invoke($null, $expandedArguments)
    Assert-Equal ("expanded " + $expanded[0]) $value $expanded[1]
}

$resolveName = $namingType.GetMethod('ResolveNameFromSegments')
Assert-Equal 'composite BOM name' ($resolveName.Invoke($naming, @($case.sample))) `
    $case.expectedCompositeName

$setSegment = $namingType.GetMethod('SetMachinedSegmentValue')
$materialKind = [Enum]::Parse($segmentType, 'Material')
$nameKind = [Enum]::Parse($segmentType, 'Name')
$afterLevel2 = $setSegment.Invoke($naming, @($case.sample, $materialKind, $case.level2Value))
Assert-Equal 'machined level 2 button' $afterLevel2 $case.expectedAfterLevel2
$afterLevel3 = $setSegment.Invoke($naming, @($afterLevel2, $nameKind, $case.level3Value))
Assert-Equal 'machined level 3 button' $afterLevel3 $case.expectedAfterLevel3

$parseOrder = $serviceType.GetMethod('ParseColumnOrder')
$columns = $parseOrder.Invoke($null, @($optionsType.GetProperty('ColumnOrder').GetValue($options)))
Assert-Equal 'first BOM column' $columns[0] $case.expectedFirstColumn
$columnHeader = $serviceType.GetMethod('ColumnHeader')
Assert-Equal 'first BOM header' ($columnHeader.Invoke($null, @($options, $columns[0]))) `
    $case.expectedFirstHeader

$defaultColumns = $parseOrder.Invoke($null, @(''))
Assert-Equal 'default column after location' $defaultColumns[2] 'fullname'

# Subassembly scope: prefixed assembly = standard part; dated assembly = expand; others = ignored
$resolveScope = $serviceType.GetMethod('ResolveSubassemblyAction',
    [System.Reflection.BindingFlags]'Static, NonPublic')
foreach ($scopeCase in $case.subassemblyScope) {
    $action = $resolveScope.Invoke($null, @($naming, [string]$scopeCase.name))
    Assert-Equal ("subassembly scope " + $scopeCase.name) $action $scopeCase.expect
}

# With the naming filter turned off, every subassembly is expanded again (legacy behaviour)
$offSettings = [Activator]::CreateInstance($settingsType)
$settingsType.GetProperty('BomRequirePattern').SetValue($offSettings, $false)
$offOptions = $createOptions.Invoke($null, @($offSettings))
$offNaming = $offOptions.GetType().GetProperty('Naming').GetValue($offOptions)
Assert-Equal 'subassembly scope (filter off)' `
    ($resolveScope.Invoke($null, @($offNaming, 'P80-02-01-01-0abc'))) 'Expand'

# Row disposition: reference parts stay hidden, unmatched rows move to the end and turn red
$resolveRow = $serviceType.GetMethod('ResolveRowDisposition',
    [System.Reflection.BindingFlags]'Static, NonPublic')
foreach ($rowCase in $case.rowDisposition) {
    $disposition = $resolveRow.Invoke($null, @($naming, [string]$rowCase.name))
    Assert-Equal ("row disposition " + $rowCase.name) $disposition $rowCase.expect
}

# Unmatched rows must be sorted behind the regular ones
$sortRowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$sortRows = $serviceType.GetMethod('SortRows', [System.Reflection.BindingFlags]'Static, NonPublic')
$rowListType = [System.Collections.Generic.List``1].MakeGenericType($sortRowType)
$sortedRows = [Activator]::CreateInstance($rowListType)
foreach ($spec in @(
    @('A', 'mmm', $false),
    @('', 'aaa', $true),
    @('B', 'zzz', $false)
)) {
    $item = [Activator]::CreateInstance($sortRowType)
    $sortRowType.GetProperty('Location').SetValue($item, [string]$spec[0])
    $sortRowType.GetProperty('Name').SetValue($item, [string]$spec[1])
    $sortRowType.GetProperty('IsUnmatched').SetValue($item, [bool]$spec[2])
    $sortRowType.GetProperty('Classification').SetValue($item,
        $(if ($spec[2]) { 'Unmatched' } else { 'Machined' }))
    [void]$sortedRows.Add($item)
}
$sortArguments = New-Object object[] 1
$sortArguments[0] = $sortedRows
$sortRows.Invoke($null, $sortArguments)
Assert-Equal 'unmatched row moved to the end' $sortedRows[2].Name 'aaa'
Assert-Equal 'regular rows keep their order' $sortedRows[0].Name 'mmm'

$fullNameHeader = $optionsType.GetProperty('FullNameHeader').GetValue($options)
Assert-Equal 'full name header' ($columnHeader.Invoke($null, @($options, 'fullname'))) $fullNameHeader
$rowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$fullNameRow = [Activator]::CreateInstance($rowType)
$rowType.GetProperty('FullName').SetValue($fullNameRow, '20260919-6061-upper-cover')
$columnValue = $serviceType.GetMethod('ColumnValue',
    [System.Reflection.BindingFlags]'Static, NonPublic')
Assert-Equal 'full name export value' `
    ($columnValue.Invoke($null, @($fullNameRow, 1, 'fullname'))) `
    '20260919-6061-upper-cover'

if ($failures.Count -gt 0) {
    Write-Host ''
    $failures | ForEach-Object { Write-Host $_ }
    throw "$($failures.Count) of $checked BOM integration checks failed."
}

Write-Host ''
Write-Host "All $checked BOM integration checks passed."
