$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$rowType=$a.GetType('MechKit.Features.PartListRow')
$rows=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
$row=[Activator]::CreateInstance($rowType)
$root=Join-Path $repo ('artifacts\linked-excel-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$row.FilePath=Join-Path $root 'part.sldprt';[IO.File]::WriteAllText($row.FilePath,'fixture')
$row.FullName='part';$row.Name='part';$row.Quantity=2;$row.Remark='original';$row.Configuration='Default';$row.MarkBomClean();$rows.Add($row)
$options=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListOptions'))
$path=$a.GetType('MechKit.Features.BomExcelExporter').GetMethod('Export').Invoke($null,@($null,$rows,[string]$root,'linked',$options))
$import=$a.GetType('MechKit.Features.BomImporter')
function Plan { $import.GetMethod('CreatePlanCore').Invoke($null,@([string]$path,$rows,$options,$rows,$true)) }
$plan=Plan
if($plan.Fields.Count -ne 0 -or $plan.Unmatched -ne 0){throw 'Unchanged workbook reported changes'}
# Simulate Excel saving changed cell values while keeping exported association metadata.
$zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Update)
try {
 $entry=$zip.GetEntry('xl/worksheets/sheet1.xml');$reader=[IO.StreamReader]::new($entry.Open());[xml]$xml=$reader.ReadToEnd();$reader.Dispose()
 $ns=[Xml.XmlNamespaceManager]::new($xml.NameTable);$ns.AddNamespace('s','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
 $headers=$xml.SelectNodes('//s:row[@r="1"]/s:c',$ns)
 $keys=$a.GetType('MechKit.Features.PartListService').GetMethod('ParseColumnOrder').Invoke($null,@($null))
 foreach($pair in @(@('remark','excel changed'),@('unfinished','pending'),@('quantity','99'))) {
  $idx=[Array]::IndexOf($keys,[string]$pair[0])+1
  $col=$headers[$idx].r -replace '1$',''
  $cell=$xml.SelectSingleNode('//s:row[@r="2"]/s:c[@r="'+$col+'2"]',$ns)
  $cell.RemoveAll();$cell.SetAttribute('r',$col+'2');$cell.SetAttribute('t','inlineStr')
  $is=$xml.CreateElement('is',$ns.LookupNamespace('s'));$text=$xml.CreateElement('t',$ns.LookupNamespace('s'));$text.InnerText=[string]$pair[1];$null=$is.AppendChild($text);$null=$cell.AppendChild($is)
 }
 $entry.Delete();$entry=$zip.CreateEntry('xl/worksheets/sheet1.xml');$writer=[IO.StreamWriter]::new($entry.Open());$writer.Write($xml.OuterXml);$writer.Dispose()
} finally {$zip.Dispose()}
$row.Remark='local changed';$row.MarkBomClean()
$plan=Plan
$conflict=@($plan.Fields|Where-Object RequiresConfirmation)[0]
if(!$conflict -or $conflict.Selected){throw 'Conflict not reserved for manual confirmation'}
$quantity=@($plan.Fields|Where-Object After -eq '99')[0]
if(!$quantity -or $quantity.CanApply){throw 'Generated quantity accepted'}
$null=$import.GetMethod('ApplyPlan').Invoke($null,@($plan))
if($row.Remark -ne 'local changed' -or $row.Unfinished -ne 'pending' -or $row.Quantity -ne 2){throw 'Apply ignored selection or read-only rules'}
$plan=Plan;$conflict=@($plan.Fields|Where-Object RequiresConfirmation)[0];$conflict.Selected=$true
$null=$import.GetMethod('ApplyPlan').Invoke($null,@($plan))
if($row.Remark -ne 'excel changed'){throw 'Confirmed conflict not applied'}
$store=$a.GetType('MechKit.Core.BomNoteStore');$notePath=$store.GetMethod('NotePath',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@([string]$row.FilePath))
if([IO.File]::ReadAllText($notePath) -notmatch 'excel changed'){throw 'Asset was not saved'}
Write-Output 'PASS: XLSX export metadata, unchanged sync, edited cells, read-only rules, conflict confirmation, asset persistence.'
