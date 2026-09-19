<#
.SYNOPSIS
    Offline checks for the part-number / name / material naming rules.

.DESCRIPTION
    Loads MechKit.dll, drives MechKit.Core.NamingOptions through reflection
    and compares the results against tools\testdata\naming-cases.json.
    No SOLIDWORKS session is required.

    The expected values live in the JSON file because this script is ASCII-only:
    Windows PowerShell 5.1 reads BOM-less scripts using the ANSI code page,
    which would corrupt non-ASCII literals.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-Naming.ps1
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
    foreach ($configuration in @('Release', 'Debug')) {
        $candidate = Join-Path $repoRoot "src\MechKit\bin\$configuration\MechKit.dll"
        if (Test-Path $candidate) { $DllPath = $candidate; break }
    }
}

if (-not $DllPath -or -not (Test-Path $DllPath)) {
    throw 'MechKit.dll not found. Build first: powershell -File build.ps1'
}

if (-not $CaseFile) {
    $CaseFile = Join-Path $PSScriptRoot 'testdata\naming-cases.json'
}

if (-not (Test-Path $CaseFile)) {
    throw "Case file not found: $CaseFile"
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DllPath).Path)
$cases = (Get-Content -LiteralPath $CaseFile -Raw -Encoding UTF8) | ConvertFrom-Json

$namingType = $assembly.GetType('MechKit.Core.NamingOptions', $false)
$sourceEnum = $assembly.GetType('MechKit.Core.PartNumberSource', $false)
$cutEnum = $assembly.GetType('MechKit.Core.FileNameCutRule', $false)
$materialEnum = $assembly.GetType('MechKit.Core.MaterialSource', $false)
$segmentEnum = $assembly.GetType('MechKit.Core.MachinedSegmentKind', $false)
$factoryType = $assembly.GetType('MechKit.Core.NamingOptionsFactory', $false)
$partListRowType = $assembly.GetType('MechKit.Features.PartListRow', $false)
$partListServiceType = $assembly.GetType('MechKit.Features.PartListService', $false)

foreach ($type in @($namingType, $sourceEnum, $cutEnum, $materialEnum, $segmentEnum, $factoryType,
        $partListRowType, $partListServiceType)) {
    if (-not $type) { throw 'Unexpected assembly layout: MechKit.Core types are missing.' }
}

$sourceProperty = $namingType.GetProperty('Source')
$cutProperty = $namingType.GetProperty('Cut')
$patternProperty = $namingType.GetProperty('Pattern')
$materialProperty = $namingType.GetProperty('Material')

$resolvePartNumber = $namingType.GetMethod('ResolvePartNumber')
$resolveName = $namingType.GetMethod('ResolveName')
$resolveMaterial = $namingType.GetMethod('ResolveMaterial')
$resolveSegmentMaterial = $namingType.GetMethod('ResolveMaterialFromSegments')
$matchesBomPattern = $namingType.GetMethod('MatchesBomPattern')
$startsWithDate = $namingType.GetMethod('StartsWithDate')
$prefixProperty = $namingType.GetProperty('BomPrefixes')
$requirePatternProperty = $namingType.GetProperty('RequireBomPattern')
$machinedSegmentsProperty = $namingType.GetProperty('MachinedSegments')
$machinedBomNameFlagsProperty = $namingType.GetProperty('MachinedSegmentBomNameFlags')
$parsePrefixes = $factoryType.GetMethod('ParsePrefixes')
$parseMachinedSegments = $factoryType.GetMethod('ParseMachinedSegments')
$parseMachinedBomNameFlags = $factoryType.GetMethod('ParseMachinedSegmentBomNameFlags')
$serializeMachinedSegments = $factoryType.GetMethod('SerializeMachinedSegments')
$isMachinedName = $namingType.GetMethod('IsMachinedName')
$presetProperty = $namingType.GetProperty('MachinedMaterialProcessPresets')
$parsePresets = $factoryType.GetMethod('ParseMaterialProcessPresets')
$resolvePreset = $namingType.GetMethod('TryResolveMachinedMaterialProcess')
$buildComponentName = $partListServiceType.GetMethod('BuildComponentBaseName',
    [System.Reflection.BindingFlags]'Static, NonPublic')

$failures = New-Object System.Collections.Generic.List[string]
$checked = 0

function New-NamingOptions {
    param([string] $Source, [string] $Cut, [string] $Pattern)

    $options = [Activator]::CreateInstance($namingType)
    $sourceProperty.SetValue($options, [Enum]::Parse($sourceEnum, $Source))
    $cutProperty.SetValue($options, [Enum]::Parse($cutEnum, $Cut))
    if ($Pattern) { $patternProperty.SetValue($options, $Pattern) }
    return $options
}

function Get-Field {
    param($Object, [string] $Name)

    if ($Object -and ($Object.PSObject.Properties.Name -contains $Name) -and $null -ne $Object.$Name) {
        return $Object.$Name
    }

    return ''
}

function New-Lookup {
    param($Properties)

    $table = @{}
    if ($Properties) {
        foreach ($property in $Properties.PSObject.Properties) {
            $table[$property.Name] = [string] $property.Value
        }
    }

    # GetNewClosure() is required: without it the scriptblock would resolve
    # $table in the caller's scope when the delegate is invoked.
    $block = {
        param([string] $key)
        if ($table.ContainsKey($key)) { return $table[$key] }
        return ''
    }.GetNewClosure()

    return [Func[string, string]] $block
}

function Assert-Equal {
    param([string] $Label, [string] $Expected, [string] $Actual)

    $script:checked++
    if ($Expected -ceq $Actual) {
        Write-Host ("[PASS] {0} -> '{1}'" -f $Label, $Actual)
        return
    }

    Write-Host ("[FAIL] {0} -> '{1}' (expected '{2}')" -f $Label, $Actual, $Expected)
    $script:failures.Add($Label)
}

Write-Host "Assembly: $DllPath"
Write-Host "Cases   : $CaseFile"
Write-Host ''

foreach ($case in $cases.partNumberCases) {
    $options = New-NamingOptions (Get-Field $case 'source') (Get-Field $case 'cut') (Get-Field $case 'pattern')
    $lookup = New-Lookup (Get-Field $case 'properties')
    $actual = $resolvePartNumber.Invoke($options, @((Get-Field $case 'file'), $lookup))
    Assert-Equal ("part number / " + $case.name) $case.expected $actual
}

foreach ($case in $cases.nameCases) {
    $options = New-NamingOptions (Get-Field $case 'source') (Get-Field $case 'cut') (Get-Field $case 'pattern')
    $lookup = New-Lookup (Get-Field $case 'properties')
    $actual = $resolveName.Invoke($options, @((Get-Field $case 'file'), $lookup))
    Assert-Equal ("name / " + $case.name) $case.expected $actual
}

foreach ($case in $cases.materialCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace'
    $materialProperty.SetValue($options, [Enum]::Parse($materialEnum, (Get-Field $case 'source')))
    $lookup = New-Lookup (Get-Field $case 'properties')
    $actual = $resolveMaterial.Invoke($options, @((Get-Field $case 'modelMaterial'), $lookup))
    Assert-Equal ("material / " + $case.name) $case.expected $actual
}

# Segment parsing: name = last segment, material = second to last
foreach ($case in $cases.segmentCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace' ''
    $namingType.GetProperty('UseNameSegments').SetValue($options, $true)
    $namingType.GetProperty('SegmentSeparator').SetValue($options, '_-')
    $namingType.GetProperty('NameSegment').SetValue($options, -1)
    $namingType.GetProperty('MaterialSegment').SetValue($options, -2)

    $emptyLookup = New-Lookup $null
    $file = Get-Field $case 'file'

    $name = $resolveName.Invoke($options, @($file, $emptyLookup))
    Assert-Equal ("segment name / " + $case.name) $case.expectedName $name

    $material = $resolveSegmentMaterial.Invoke($options, @($file))
    Assert-Equal ("segment material / " + $case.name) $case.expectedMaterial $material
}

# Ordered machining segments: the configured row order controls name/material
# extraction. The date segment is normalized to one immutable first row.
foreach ($case in $cases.orderedSegmentCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace' ''
    $namingType.GetProperty('UseNameSegments').SetValue($options, $true)
    $namingType.GetProperty('SegmentSeparator').SetValue($options, '_-')
    $segments = $parseMachinedSegments.Invoke($null, @((Get-Field $case 'layout')))
    $machinedSegmentsProperty.SetValue($options, $segments)
    $flagArguments = New-Object 'object[]' 2
    $flagArguments[0] = [string](Get-Field $case 'bomNameFlags')
    $flagArguments[1] = $segments
    $machinedBomNameFlagsProperty.SetValue($options,
        $parseMachinedBomNameFlags.Invoke($null, $flagArguments))

    $emptyLookup = New-Lookup $null
    $file = Get-Field $case 'file'
    $name = $resolveName.Invoke($options, @($file, $emptyLookup))
    Assert-Equal ("ordered name / " + $case.name) $case.expectedName $name

    $material = $resolveSegmentMaterial.Invoke($options, @($file))
    Assert-Equal ("ordered material / " + $case.name) $case.expectedMaterial $material

    $invokeArguments = New-Object 'object[]' 1
    $invokeArguments[0] = $segments
    $normalized = $serializeMachinedSegments.Invoke($null, $invokeArguments)
    Assert-Equal ("ordered layout / " + $case.name) $case.expectedLayout $normalized

    $script:checked++
    $actualMachined = [bool] $isMachinedName.Invoke($options, @($file))
    if ($actualMachined -eq [bool] $case.isMachined) {
        Write-Host ("[PASS] ordered BOM / {0} -> machined={1}" -f $case.name, $actualMachined)
    }
    else {
        Write-Host ("[FAIL] ordered BOM / {0} -> machined={1} (expected {2})" -f `
            $case.name, $actualMachined, [bool] $case.isMachined)
        $script:failures.Add("ordered BOM / " + $case.name)
    }
}

# Material/process presets are resolved from machining segment 2 for either
# supported whole-name separator.
foreach ($case in $cases.presetCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace' ''
    $namingType.GetProperty('UseNameSegments').SetValue($options, $true)
    $namingType.GetProperty('SegmentSeparator').SetValue($options, '_-')
    $presets = $parsePresets.Invoke($null, @((Get-Field $case 'rules')))
    $presetProperty.SetValue($options, $presets)

    $arguments = New-Object 'object[]' 3
    $arguments[0] = Get-Field $case 'file'
    $arguments[1] = ''
    $arguments[2] = ''
    $matched = [bool] $resolvePreset.Invoke($options, $arguments)
    Assert-Equal ("preset matched / " + $case.name) 'True' ([string] $matched)
    Assert-Equal ("preset material / " + $case.name) $case.expectedMaterial ([string] $arguments[1])
    Assert-Equal ("preset process / " + $case.name) $case.expectedProcess ([string] $arguments[2])
}

# A BOM name edit changes only the assembly component instance label. The
# generated label keeps machining date/material/tail segments or uses the
# standard-part prefix_middle_model layout.
foreach ($case in $cases.componentRenameCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace' ''
    $namingType.GetProperty('UseNameSegments').SetValue($options, $true)
    $namingType.GetProperty('SegmentSeparator').SetValue($options, '_-')
    $segments = $parseMachinedSegments.Invoke($null, @((Get-Field $case 'layout')))
    $machinedSegmentsProperty.SetValue($options, $segments)
    $flagArguments = New-Object 'object[]' 2
    $flagArguments[0] = [string](Get-Field $case 'bomNameFlags')
    $flagArguments[1] = $segments
    $machinedBomNameFlagsProperty.SetValue($options,
        $parseMachinedBomNameFlags.Invoke($null, $flagArguments))

    $row = [Activator]::CreateInstance($partListRowType, $true)
    $partListRowType.GetProperty('Classification').SetValue($row, (Get-Field $case 'classification'))
    $partListRowType.GetProperty('FilePath').SetValue($row, (Get-Field $case 'file'))
    $partListRowType.GetProperty('Name').SetValue($row, (Get-Field $case 'partName'))
    $partListRowType.GetProperty('Material').SetValue($row, (Get-Field $case 'material'))
    $partListRowType.GetProperty('Process').SetValue($row, (Get-Field $case 'process'))

    $actual = $buildComponentName.Invoke($null, @($row, $options))
    Assert-Equal ("component name / " + $case.name) $case.expected $actual
}

# BOM rule: machined parts (date first) and standard parts (prefix first) are
# listed; every other component is treated as a sub-part and skipped.
foreach ($case in $cases.bomCases) {
    $options = New-NamingOptions 'FileName' 'FirstSpace' ''
    $prefixes = $parsePrefixes.Invoke($null, @((Get-Field $case 'prefixes')))
    $prefixProperty.SetValue($options, $prefixes)
    $requirePatternProperty.SetValue($options, $true)

    $part = Get-Field $case 'part'
    $inBom = $matchesBomPattern.Invoke($options, @($part))
    $expected = [bool] $case.inBom

    $kindCode = ''
    if ($inBom) {
        $kindCode = if ($startsWithDate.Invoke($null, @($part))) { 'MACHINED' } else { 'STANDARD' }
    }

    $ok = ($inBom -eq $expected) -and ($kindCode -ceq [string](Get-Field $case 'kindCode'))
    $script:checked++
    if ($ok) {
        Write-Host ("[PASS] BOM / {0} -> inBom={1} kind={2}" -f $case.name, $inBom, $(if($kindCode){$kindCode}else{'-'}))
    }
    else {
        Write-Host ("[FAIL] BOM / {0} -> inBom={1} kind={2} (expected inBom={3} kind={4})" -f `
            $case.name, $inBom, $kindCode, $expected, (Get-Field $case 'kindCode'))
        $script:failures.Add("BOM / " + $case.name)
    }
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host ("All {0} naming checks passed." -f $checked)
}
else {
    Write-Host ("{0} of {1} checks failed: {2}" -f $failures.Count, $checked, ($failures -join ', '))
    exit 1
}
