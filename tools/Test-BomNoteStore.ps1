$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$t=$a.GetType('MechKit.Core.BomNoteStore',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$root=Join-Path $repo ('artifacts\note-store-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$model=Join-Path $root 'part.sldprt'
[IO.File]::WriteAllText($model,'fixture')
function NewRow { $row=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListRow'));$row.FilePath=$model;return $row }
$row=NewRow;$row.Remark='automatic A'
$null=$t.GetMethod('Apply',$flags).Invoke($null,@($row))
foreach($pair in @(@('remark','manual note'),@('assemblynote','install'),@('purchaselink','https://example.com/item'),@('unfinished','follow up'))) {
 $null=$t.GetMethod('Save',$flags).Invoke($null,@([string]$model,[string]$pair[0],[string]$pair[1]))
}
$next=NewRow;$next.Remark='automatic B'
$null=$t.GetMethod('Apply',$flags).Invoke($null,@($next))
if($next.Remark -ne 'manual note' -or $next.AssemblyNote -ne 'install' -or $next.Unfinished -ne 'follow up' -or $next.PurchaseLink -ne 'https://example.com/item'){throw 'Manual values did not survive regeneration'}
$path=$t.GetMethod('NotePath',$flags).Invoke($null,@([string]$model))
[xml]$xml=[IO.File]::ReadAllText($path)
if($xml.bomNotes.remark.generated -ne 'automatic B' -or $xml.bomNotes.remark.manual -ne 'manual note'){throw 'Generation/source separation failed'}
$null=$t.GetMethod('Save',$flags).Invoke($null,@([string]$model,'remark',''))
$next.Remark='automatic C';$null=$t.GetMethod('Apply',$flags).Invoke($null,@($next))
if($next.Remark -ne ''){throw 'Explicit blank was overwritten'}
Write-Output 'PASS: asset persistence, four fields, generated/manual isolation, refresh, explicit clearing.'
