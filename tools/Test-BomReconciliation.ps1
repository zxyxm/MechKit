$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$rowType=$a.GetType('MechKit.Features.PartListRow')
$options=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListOptions'))
$method=$a.GetType('MechKit.Features.BomImporter').GetMethod('ImportValidated')
$root=Join-Path $repo 'artifacts\bom-reconciliation'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Rows { return ,([Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))) }
function Row($location='A') {
    $r=[Activator]::CreateInstance($rowType)
    $r.FullName='Plate';$r.Name='Plate';$r.FilePath='C:\models\Plate.sldprt';$r.Configuration='Default'
    $r.Location=$location;$r.Quantity=2;$r.Material='6061';$r.MarkBomClean()
    return $r
}
function Import($csv,$targets,$all) {
    [string]$path=Join-Path $root 'input.csv'
    [IO.File]::WriteAllText($path,$csv)
    return $method.Invoke($null,@($path,$targets,$options,$all))
}
$rows=Rows;$rows.Add((Row))
$result=Import "fullname,quantity,remark`nPlate,3,new" $rows $rows
if($result.Conflicts -ne 1 -or $rows[0].Quantity -ne 2 -or $rows[0].Remark) {throw 'Quantity conflict partially applied'}
$result=Import "fullname,material,remark`nPlate,304,new" $rows $rows
if($result.Conflicts -ne 1 -or $rows[0].Material -ne '6061' -or $rows[0].Remark) {throw 'Material conflict silently overwrote'}
$result=Import "fullname,quantity,material,remark`nPlate,2,6061,new" $rows $rows
if($result.Updated -ne 1 -or $rows[0].Remark -ne 'new') {throw 'Consistent import failed to fill blank'}
$result=Import "fullname,remark`nPlate,other" $rows $rows
if($result.Conflicts -ne 1 -or $rows[0].Remark -ne 'new') {throw 'Staged edits not protected'}
$rows=Rows;$rows.Add((Row 'A'));$rows.Add((Row 'B'))
$targets=Rows;$targets.Add($rows[0])
$result=Import "fullname,location,remark`nPlate,A,new" $targets $rows
if($result.Conflicts -ne 1 -or $rows[0].Remark) {throw 'Hidden duplicate-file conflict ignored'}
$result=Import "fullname,location,remark`nPlate,A,new`nPlate,B,new" $rows $rows
if($result.Updated -ne 2 -or $rows[1].Remark -ne 'new') {throw 'Consistent shared-file updates rejected'}
$rows=Rows;$rows.Add((Row 'A'));$rows.Add((Row 'B'))
$result=Import "fullname,location,remark`nPlate,A,one`nPlate,B,two" $rows $rows
if($result.Conflicts -ne 2 -or $rows[0].Remark -or $rows[1].Remark) {throw 'Divergent file updates partially applied'}
Write-Output 'PASS: quantity and attribute conflicts, row atomicity, blank fill, dirty edits, hidden peers, consistent shared updates, conflicting shared updates'
