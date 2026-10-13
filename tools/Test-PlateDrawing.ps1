param([string[]]$Parts, [string[]]$RejectParts, [switch]$Live, [switch]$Mirror)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $repo 'src\MechKit\bin\Release'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'MechKit.dll'))
$math = $assembly.GetType('MechKit.Features.PlateMath', $true)
function Invoke-Math($name, [object[]]$values) { $math.GetMethod($name).Invoke($null, $values) }
if (-not (Invoke-Math 'PlateDimensions' @(0.1, 0.06, 0.01))) { throw 'Flat plate rejected' }
if (Invoke-Math 'PlateDimensions' @(0.01, 0.01, 0.01)) { throw 'Cube accepted' }
if (Invoke-Math 'PlateDimensions' @(0.1, 0.005, 0.01)) { throw 'Wrong thickness axis accepted' }
if (Invoke-Math 'PlateDimensions' @(0.1, 0.06, 0.0)) { throw 'Zero thickness accepted' }
$scale = Invoke-Math 'ChooseScale' @(0.450, 0.046, 0.01)
if (0.450*$scale -gt 0.205 -or (2*0.046+0.01)*$scale -gt 0.145) { throw 'Scale does not fit' }
$service = $assembly.GetType('MechKit.Features.PlateDrawingService', $true)
$supports = $service.GetMethod('SupportsDocumentType')
foreach ($kind in @(1, 2)) {
    if (-not $supports.Invoke($null, @($kind))) { throw 'Part or assembly command disabled' }
}
foreach ($kind in @(0, 3)) {
    if ($supports.Invoke($null, @($kind))) { throw 'Unsupported document command enabled' }
}
$template = $service.GetMethod('ExtractTemplate').Invoke($null, @())
if (-not (Test-Path -LiteralPath $template)) { throw 'Embedded template missing' }
$addin = $assembly.GetType('MechKit.MechKitAddin', $true)
if (-not $addin.GetMethod('OnPlateDrawing') -or -not $addin.GetMethod('OnPlateDrawingEnable')) { throw 'Menu callback missing' }
Write-Host 'Offline plate drawing checks passed.'
if ($Live) {
    if (-not $Parts -and -not $RejectParts) { throw 'Live test requires -Parts or -RejectParts with saved fixture source files. Only copies are edited.' }
    Add-Type -Path (Join-Path $bin 'SolidWorks.Interop.sldworks.dll')
    Add-Type -Path (Join-Path $bin 'SolidWorks.Interop.swconst.dll')
    $refs = @((Join-Path $bin 'SolidWorks.Interop.sldworks.dll'), (Join-Path $bin 'SolidWorks.Interop.swconst.dll'), (Join-Path $bin 'SolidWorks.Interop.swpublished.dll'), (Join-Path $bin 'MechKit.dll'), 'System.Core')
    Add-Type -Path (Join-Path $PSScriptRoot 'testdata\PlateDrawingLiveTest.cs') -ReferencedAssemblies $refs
    if ($Parts) {
        if ($Mirror) { [PlateDrawingLiveTest]::Mirror($Parts, (Join-Path $repo 'artifacts\plate-drawing-tests')) }
        else { [PlateDrawingLiveTest]::Run($Parts, (Join-Path $repo 'artifacts\plate-drawing-tests')) }
    }
    if ($RejectParts) { [PlateDrawingLiveTest]::Reject($RejectParts, (Join-Path $repo 'artifacts\plate-drawing-tests')) }
}
