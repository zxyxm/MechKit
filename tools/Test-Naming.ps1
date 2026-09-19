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
$setLeadingDate = $namingType.GetMethod('SetLeadingDate')
$insertTokenAfterDateOrStart = $namingType.GetMethod('InsertTokenAfterDateOrStart')
$insertReferenceMaterial = $namingType.GetMethod('InsertReferenceMaterialAfterDate')
$setReferenceMaterial = $namingType.GetMethod('SetReferenceMaterialAfterDate')
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
$resolvePresetSurface = $namingType.GetMethod('TryResolveMachinedMaterialProcessSurface')
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

    $arguments = New-Object 'object[]' 4
    $arguments[0] = Get-Field $case 'file'
    $arguments[1] = ''
    $arguments[2] = ''
    $arguments[3] = ''
    $matched = [bool] $resolvePresetSurface.Invoke($options, $arguments)
    Assert-Equal ("preset matched / " + $case.name) 'True' ([string] $matched)
    Assert-Equal ("preset material / " + $case.name) $case.expectedMaterial ([string] $arguments[1])
    Assert-Equal ("preset process / " + $case.name) $case.expectedProcess ([string] $arguments[2])
    Assert-Equal ("preset surface / " + $case.name) ([string](Get-Field $case 'expectedSurface')) ([string] $arguments[3])
}

# Remarks (prefix / second-level field notes) must survive the key=value round trip
$serializeDescriptions = $factoryType.GetMethod('SerializePrefixDescriptions')
$parseDescriptions = $factoryType.GetMethod('ParsePrefixDescriptions')
$remark = $cases.descriptionRoundTrip
$remarkNames = [string[]] $remark.names
$remarkValues = [string[]] $remark.values
$remarkBag = New-Object 'System.Collections.Generic.Dictionary[string,string]'
for ($remarkIndex = 0; $remarkIndex -lt $remarkNames.Count; $remarkIndex++) {
    $remarkBag[$remarkNames[$remarkIndex]] = $remarkValues[$remarkIndex]
}
$serializeArguments = New-Object 'object[]' 2
$serializeArguments.SetValue($remarkNames, 0)
$serializeArguments.SetValue($remarkBag, 1)
$remarkText = $serializeDescriptions.Invoke($null, $serializeArguments)
$remarkParsed = $parseDescriptions.Invoke($null, @($remarkText))
for ($remarkIndex = 0; $remarkIndex -lt $remarkNames.Count; $remarkIndex++) {
    Assert-Equal ("remark round trip / " + $remarkNames[$remarkIndex]) `
        $remarkValues[$remarkIndex] ([string] $remarkParsed[$remarkNames[$remarkIndex]])
}

# Material preset editor: the stored text must parse into the table rows
# (material segment + material + process + surface) without losing anything.
$namingFormType = $assembly.GetType('MechKit.UI.NamingRuleForm', $false)
if (-not $namingFormType) { throw 'Unexpected assembly layout: NamingRuleForm is missing.' }
$parsePresetRows = $namingFormType.GetMethod('ParseMaterialPresetLines',
    [System.Reflection.BindingFlags]'Static, NonPublic')
foreach ($tableCase in $cases.materialPresetTableCases) {
    $rows = $parsePresetRows.Invoke($null, @((Get-Field $tableCase 'rules')))
    $actualRows = @()
    foreach ($row in $rows) { $actualRows += ($row -join '|') }
    Assert-Equal ("material preset rows / " + $tableCase.name) `
        (($tableCase.expectedRows) -join "`n") ($actualRows -join "`n")

    # The editor writes the rows back with the same text format, so a table edit
    # round-trips through the stored text without losing the material segment.
    $buildPresetLines = $namingFormType.GetMethod('BuildMaterialPresetLines',
        [System.Reflection.BindingFlags]'Static, NonPublic')
    $buildArguments = New-Object 'object[]' 1
    $buildArguments[0] = $rows
    $lines = $buildPresetLines.Invoke($null, $buildArguments)
    Assert-Equal ("material preset lines / " + $tableCase.name) `
        ([string](Get-Field $tableCase 'expectedLines')) (@($lines) -join "`n")
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
    $componentRules = Get-Field $case 'rules'
    if ($componentRules) {
        $presetProperty.SetValue($options, $parsePresets.Invoke($null, @($componentRules)))
    }
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

# Quick command: insert the 6061 reference material immediately after a valid
# leading date, normalize legacy underscores, and never duplicate the material.
foreach ($case in @(
    @('date and name', '20260919-panel', '20260919-6061-panel'),
    @('legacy underscore', '20260919_panel', '20260919-6061-panel'),
    @('already inserted', '20260919-6061-panel', '20260919-6061-panel'),
    @('date only', '20260919', '20260919-6061-'),
    @('no date', 'panel', 'panel')
)) {
    $actual = $insertReferenceMaterial.Invoke($null, @($case[1], '6061'))
    Assert-Equal ("reference material / " + $case[0]) $case[2] $actual
}

$knownMaterials = [string[]]@('6061', '5052', '304')
$setArguments = New-Object 'object[]' 3
$setArguments[0] = '20260919-uppercover'
$setArguments[1] = '6061'
$setArguments[2] = $knownMaterials
Assert-Equal 'material shortcut inserts after date' '20260919-6061-uppercover' `
    ($setReferenceMaterial.Invoke($null, $setArguments))
$setArguments[0] = '20260919-5052-uppercover'
Assert-Equal 'material shortcut replaces known material' '20260919-6061-uppercover' `
    ($setReferenceMaterial.Invoke($null, $setArguments))

# Time is always the first segment. Assembly follows a leading date, or becomes
# the first segment when no date exists. Repeated clicks never duplicate tokens.
foreach ($case in @(
    @('time inserts first', 'uppercover-A', '20260919-uppercover-A'),
    @('time keeps assembly behind it', 'assembly-uppercover-A', '20260919-assembly-uppercover-A'),
    @('time repairs old reversed order', 'assembly-20260801-uppercover-A', '20260919-assembly-uppercover-A'),
    @('time replaces old leading date', '20260801-uppercover-A', '20260919-uppercover-A'),
    @('time replaces date-only name', '20260801', '20260919')
)) {
    $actual = $setLeadingDate.Invoke($null, @($case[1], '20260919'))
    Assert-Equal ("time shortcut / " + $case[0]) $case[2] $actual
}

foreach ($case in @(
    @('assembly follows date', '20260919-uppercover-A', '20260919-assembly-uppercover-A'),
    @('assembly inserts first without date', 'uppercover-A', 'assembly-uppercover-A'),
    @('assembly remains single', '20260919-assembly-uppercover-A', '20260919-assembly-uppercover-A'),
    @('assembly moves behind date', '20260919-uppercover-assembly-A', '20260919-assembly-uppercover-A'),
    @('assembly normalizes underscores', '20260919_uppercover_A', '20260919-assembly-uppercover-A')
)) {
    $actual = $insertTokenAfterDateOrStart.Invoke($null, @($case[1], 'assembly'))
    Assert-Equal ("assembly shortcut / " + $case[0]) $case[2] $actual
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
