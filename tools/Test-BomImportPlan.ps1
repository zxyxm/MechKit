param([string]$Workbook)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$type=$a.GetType('MechKit.Features.BomImporter')
$rowType=$a.GetType('MechKit.Features.PartListRow')
$options=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListOptions'))
$root=Join-Path $repo 'artifacts\bom-import-plan'
[IO.Directory]::CreateDirectory($root)|Out-Null
function Rows {return ,([Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType)))}
function Row($name,$location,$quantity,$file='C:\models\plate.sldprt') {
    $r=[Activator]::CreateInstance($rowType)
    $r.FullName=$name;$r.Name=$name;$r.Location=$location;$r.Quantity=$quantity
    $r.FilePath=$file;$r.Configuration='Default';$r.Material='6061';$r.MarkBomClean()
    return $r
}
function Plan($text,$rows) {
    [string]$path=Join-Path $root 'input.csv'
    [IO.File]::WriteAllText($path,$text)
    return $type.GetMethod('CreatePlan').Invoke($null,@($path,$rows,$options,$rows))
}
function Apply($plan) {$type.GetMethod('ApplyPlan').Invoke($null,@($plan))}
$rows=Rows;$rows.Add((Row 'Plate' 'A' 2));$rows.Add((Row 'Plate' 'B' 3))
$separator=[string][char]0xff1b
$plan=Plan "fullname,location,quantity,material,remark,purchaselink`nPlate,A${separator}B,5,304,note,Taobao" $rows
if($rows[0].Material -ne '6061') {throw 'Preview mutated rows'}
$quantity=@($plan.Fields | Where-Object Field -eq $options.QuantityHeader)[0]
if($quantity.Before -ne '5' -or $quantity.CanApply) {throw 'Merged quantity did not validate aggregate'}
$result=Apply $plan
if($rows[0].Quantity -ne 2 -or $rows[1].Quantity -ne 3 -or $rows[1].PurchaseLink -ne 'Taobao' -or $rows[0].Material -ne '304') {throw 'Merged update failed'}
$rows=Rows;$rows.Add((Row 'Plate' 'A' 2));$rows[0].Remark='manual'
$plan=Plan "fullname,quantity,remark,purchaselink`nPlate,999,imported,channel" $rows
$result=Apply $plan
if($rows[0].Remark -ne 'manual' -or $rows[0].PurchaseLink -ne 'channel' -or $rows[0].Quantity -ne 2) {throw 'Independent dirty/quantity conflicts failed'}
$rows=Rows;$rows.Add((Row 'Plate' 'A' 2));$rows.Add((Row 'Plate' 'B' 3))
$plan=Plan "fullname,location,remark,purchaselink`nPlate,A,one,channel`nPlate,B,two,channel" $rows
$result=Apply $plan
if($rows[0].Remark -or $rows[1].Remark -or $rows[0].PurchaseLink -ne 'channel') {throw 'Conflicting duplicate field blocked other valid field'}
$rows=Rows;$rows.Add((Row 'AliasA' 'A' 2));$rows.Add((Row 'AliasB' 'B' 3))
$plan=Plan "fullname,location,quantity,purchaselink`nAliasA${separator}AliasB,A${separator}B,5,channel" $rows
if($plan.Unmatched -ne 0) {throw 'Merged aliases of same file did not match'}
$rows[1].FilePath='C:\models\other.sldprt'
$plan=Plan "fullname,purchaselink`nAliasA${separator}AliasB,channel" $rows
if($plan.Unmatched -ne 1) {throw 'Different files merged by alias'}
$rows=Rows;$rows.Add((Row 'Plate' 'A' 2))
$plan=Plan "fullname,material,purchaselink`nPlate,304,javascript:alert(1)" $rows
$result=Apply $plan
if($rows[0].Material -ne '304' -or $rows[0].PurchaseLink) {throw 'Invalid link discarded valid material field'}
$rows=Rows;$rows.Add((Row 'Plate' 'A' 2))
$plan=Plan "fullname,remark`nPlate,new" $rows
$rows[0].Remark='changed after preview';$result=Apply $plan
if($rows[0].Remark -ne 'changed after preview' -or $result.Conflicts -ne 1) {throw 'Stale preview overwritten'}
Write-Output 'PASS: dry run, merged instances, quantity aggregate, dirty field isolation, text purchase info, duplicate-field conflicts, alias identity, invalid-field isolation, stale review'
if($Workbook) {
    $raw=$type.GetMethod('ReadWorkbook',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@($Workbook))
    $rows=Rows
    foreach($source in $raw) {
        $n=$source.GetType().GetField('Number').GetValue($source)
        if($n -lt 2 -or $n -gt 141) {continue}
        $cells=$source.GetType().GetField('Cells').GetValue($source)
        $locations=$cells[2].Split([char]0xff1b)
        $names=$cells[3].Split([char]0xff1b)
        $remaining=[int]$cells[10]
        for($i=0;$i -lt $locations.Length;$i++) {
            $qty=if($i -eq $locations.Length-1){$remaining}else{1}
            $remaining-=$qty
            $row=Row $names[[Math]::Min($i,$names.Length-1)] $locations[$i] $qty ("C:\fixture\row-$n.sldprt")
            $row.Material=$cells[7];$row.Process=$cells[8];$row.SurfaceTreatment=$cells[9];$row.MarkBomClean()
            $rows.Add($row)
        }
    }
    $plan=$type.GetMethod('CreatePlan').Invoke($null,@($Workbook,$rows,$options,$rows))
    if($plan.Unmatched) {throw ('Workbook rows unmatched: '+($plan.Messages -join ';'))}
    $formType=$a.GetType('MechKit.UI.BomImportReviewForm')
    $form=[Activator]::CreateInstance($formType,[Reflection.BindingFlags]'Instance,NonPublic',$null,@($plan),$null)
    try {
        $form.ShowInTaskbar=$false;$form.Opacity=0;$form.Show();[Windows.Forms.Application]::DoEvents()
        $bitmap=New-Object Drawing.Bitmap $form.Width,$form.Height
        try {$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle 0,0,$form.Width,$form.Height));$bitmap.Save((Join-Path $root 'review.png'))}finally{$bitmap.Dispose()}
    }finally{$form.Dispose()}
    $result=Apply $plan
    foreach($n in @(3,6,18,39,96)) {
        $source=@($raw | Where-Object {$_.GetType().GetField('Number').GetValue($_) -eq $n})[0]
        $expected=$source.GetType().GetField('Cells').GetValue($source)[13]
        $actual=@($rows | Where-Object {$_.FilePath -eq "C:\fixture\row-$n.sldprt"})
        foreach($row in $actual) {if($row.PurchaseLink -ne $expected) {throw "Purchase text lost at row $n"}}
    }
    Write-Output ('PASS: supplied workbook simulated roundtrip; unmatched='+$plan.Unmatched+' updated instances='+$result.Updated+'; all five purchase text markers retained. No live model data used.')
    Write-Output (Join-Path $root 'review.png')
}
