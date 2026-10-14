param([string]$PreviewPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$t=$a.GetType('MechKit.UI.ModelPreviewPopup',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$popup=[Activator]::CreateInstance($t)
$owner=New-Object Windows.Forms.Form
$owner.ShowInTaskbar=$false
$image=New-Object Drawing.Bitmap 320,240
$graphics=[Drawing.Graphics]::FromImage($image)
$graphics.Clear([Drawing.Color]::LightSteelBlue)
$graphics.Dispose()
$location=('Main assembly > Subassembly with a long name > '*5)+'Final subassembly'
try {
 $owner.Show();$owner.Activate()
 [Windows.Forms.Application]::DoEvents()
 $active=[Windows.Forms.Form]::ActiveForm
 $arguments=New-Object object[] 3
 $arguments[0]='Model preview';$arguments[1]=$location;$arguments[2]=$image.PSObject.BaseObject
 $t.GetMethod('SetPreview',$flags).Invoke($popup,$arguments)|Out-Null
 if($popup.Height -le 438){throw 'Full location did not expand popup height'}
 if(-not $t.GetField('_location',$flags).GetValue($popup).EndsWith('Final subassembly')){throw 'Location was truncated'}
 $popup.Show($owner)
 [Windows.Forms.Application]::DoEvents()
 if([Windows.Forms.Form]::ActiveForm -ne $active){throw 'Preview took focus from owner'}
 $timer=New-Object Windows.Forms.Timer
 $timer.Interval=600
 $script:done=$false
 $timer.add_Tick({$timer.Stop();$script:done=$true})
 $timer.Start()
 while(-not $script:done){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}
 $timer.Dispose()
 if(-not $popup.Visible){throw 'Preview vanished while hovered'}
 if($PreviewPath){
 $bitmap=New-Object Drawing.Bitmap $popup.Width,$popup.Height
 try{$popup.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle 0,0,$popup.Width,$popup.Height));$bitmap.Save([IO.Path]::GetFullPath($PreviewPath))}finally{$bitmap.Dispose()}
 }
 $t.GetMethod('ClearPreview',$flags).Invoke($popup,@())|Out-Null
 if($popup.Visible){throw 'Preview did not hide when cleared'}
 Write-Output 'PASS: persistent popup, owner focus preserved, full location wraps below image, explicit hide'
}finally{$popup.Dispose();$owner.Dispose();$image.Dispose()}
