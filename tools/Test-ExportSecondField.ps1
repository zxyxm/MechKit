param([string]$PreviewPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$t=$a.GetType('MechKit.UI.BatchExportForm',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$parse=$t.GetMethod('FileSecondField',[Reflection.BindingFlags]'NonPublic,Static')
function Check($ok,$message){if(-not $ok){throw $message}}
foreach($case in @(@('20260929-p6061-part.SLDPRT','p6061'),@('20260929_6061_part.SLDDRW','6061'),@('vendor-hardware-bolt.SLDPRT','hardware'),@('unnamed.SLDPRT',''),@('date--part.SLDPRT',''),@('date- spcc -part.SLDPRT','spcc'))){
 Check ($parse.Invoke($null,@([string]$case[0])) -eq $case[1]) ('Parse failed: '+$case[0])
}
$root=Join-Path $repo ('artifacts\export-field-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$files=[Collections.Generic.List[string]]::new()
foreach($name in @('20260929-p6061-part.SLDPRT','20260929-p6061-part.SLDDRW','20260929-6061-part.SLDPRT','20260929-P6061-other.SLDPRT','20260929-p304-part.SLDPRT','unnamed.SLDPRT')){
 $file=Join-Path $root $name
 [IO.File]::WriteAllText($file,'fixture only')
 $files.Add($file)
}
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost',$true),$true)
$form=[Activator]::CreateInstance($t,@($hostObject,$files))
try {
 $form.Show()
 [Windows.Forms.Application]::DoEvents()
 $filter=$t.GetField('_secondFieldFilter',$flags).GetValue($form)
 $tree=$t.GetField('_fileTree',$flags).GetValue($form)
 $getSelected=$t.GetMethod('SelectedExportFiles',$flags)
 $filter.SelectedItem='p6061'
 $selected=$getSelected.Invoke($form,@())
 Check ($selected.Count -eq 3) 'Case-insensitive p6061 filter or pairing failed'
 Check (-not ($selected | Where-Object {$_ -match '-6061-'})) 'p6061 merged with 6061'
 # Even manual checking outside the active filter must not leak into export.
 $node=$tree.Nodes | Where-Object {$_.Tag -match '-6061-'} | Select-Object -First 1
 $node.Checked=$true
 Check ($getSelected.Invoke($form,@()).Count -eq 3) 'Manual mismatched selection leaked into export'
 $filter.SelectedItem='6061'
 Check ($getSelected.Invoke($form,@()).Count -eq 1) '6061 matching failed'
 $filter.SelectedIndex=1
 Check ($getSelected.Invoke($form,@()).Count -eq 1) 'Unsegmented file filter failed'
 $filter.SelectedItem='p6061'
 # Output routing keeps the explicitly chosen folder.
 $service=$a.GetType('MechKit.Features.ExportService',$true)
 $options=[Activator]::CreateInstance($a.GetType('MechKit.Features.BatchExportOptions',$true),$true)
 $options.OutputFolder=Join-Path $root 'chosen-folder'
 $options.KeepTree=$false
 $format=$a.GetType('MechKit.Features.ExportFormats',$true).GetMethod('Find').Invoke($null,@('.stp'))
 $target=$service.GetMethod('BuildTargetPath',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@($options,[string]$files[0],$format))
 Check ($target -eq (Join-Path $options.OutputFolder '20260929-p6061-part.stp')) 'Selected output folder lost'
 $form.ClientSize=New-Object Drawing.Size 840,620
 [Windows.Forms.Application]::DoEvents()
 Check ($filter.Height -ge $filter.PreferredHeight) 'Filter clipped at minimum window size'
 if($PreviewPath){
 $bitmap=New-Object Drawing.Bitmap $form.Width,$form.Height
 try{$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle 0,0,$form.Width,$form.Height));$bitmap.Save([IO.Path]::GetFullPath($PreviewPath))}finally{$bitmap.Dispose()}
 }
 Write-Output 'PASS: parsing, exact second field, case handling, paired files, manual mismatch exclusion, missing field, chosen output folder, minimum layout'
}finally{$form.Dispose()}
