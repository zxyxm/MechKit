$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$settings=[Activator]::CreateInstance($a.GetType('MechKit.Core.AddinSettings'))
$factory=$a.GetType('MechKit.Core.NamingOptionsFactory').GetMethod('FromSettings')
$category=$a.GetType('MechKit.Core.PartCategory')
function Rules { return $factory.Invoke($null,@($settings)) }
function Check($condition,$message) { if(-not $condition) {throw $message} }
$n=Rules
Check ($n.RecognizeDesignNode('备件-轴') -eq 'Unmatched') 'Spare must be removed by default'
Check ($n.RecognizeDesignNode('参考-轴') -eq 'Reference') 'Reference migration'
Check ($n.RecognizeDesignNode('库存-轴') -eq 'Stock') 'Stock migration'
$settings.Set('EditableCategoriesV1','<categories><category name="外购总成" prefixes="BUY-" exclude="false"/><category name="辅助" prefixes="AUX-" exclude="true"/></categories>')
$n=Rules
Check ($n.RecognizeDesignNode('BUY-电机') -eq 'ExternalDrawing') 'New whole-item classification'
Check ($n.RecognizeDesignNode('AUX-支架') -eq 'Reference') 'Excluded custom category'
Check ($n.RecognizeDesignNode('参考-轴') -eq 'Unmatched') 'Deleted reference still matched'
Check ($n.RecognizeDesignNode('库存-轴') -eq 'Unmatched') 'Deleted stock still matched'
Check ($n.RecognizeDesignNode('淘宝-电机') -eq 'Standard') 'Fixed standard changed'
Check ($n.RecognizeDesignNode('20261014-6061-板') -eq 'Machined') 'Fixed machined changed'
$m=$n.GetType().GetMethod('MatchCategory',[Reflection.BindingFlags]'Instance,NonPublic')
Check ($m.Invoke($n,@('BUY-电机')).Name -eq '外购总成') 'Custom name lost'
$loaded=$category.GetMethod('Load').Invoke($null,@($settings))
$category.GetMethod('Save').Invoke($null,@($settings,$loaded))
Check ($settings.Get('EditableCategoriesV1','').Contains('外购总成')) 'Roundtrip lost category'
$settings.Set('EditableCategoriesV1','<categories/>')
$n=Rules
Check ($n.RecognizeDesignNode('BUY-电机') -eq 'Unmatched') 'Delete all failed'
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$panel=[Activator]::CreateInstance($a.GetType('MechKit.UI.CategoryRulesPanel'),@($hostObject,-1))
$f=New-Object Windows.Forms.Form;$f.ClientSize=New-Object Drawing.Size 900,460;$f.Controls.Add($panel)
try {
 $grid=$panel.GetType().GetField('_grid',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($panel)
 $grid.Rows.Clear();$grid.Rows.Add('自制模块','MOD-',$false)|Out-Null
 $panel.SaveToSettings()
 Check ($factory.Invoke($null,@($hostObject.Settings)).RecognizeDesignNode('MOD-轴') -eq 'ExternalDrawing') 'UI category not saved'
 $grid.Rows[0].Cells[0].Value='加工件'
 $rejected=$false;try {$panel.SaveToSettings()}catch{$rejected=$true}
 Check $rejected 'Fixed category was editable'
 $grid.Rows.Clear();$panel.SaveToSettings()
 Check ($category.GetMethod('Load').Invoke($null,@($hostObject.Settings)).Count -eq 0) 'UI delete all restored defaults'
 $panel.ResetDefaults()
 $f.Opacity=0;$f.ShowInTaskbar=$false;$f.Show();[Windows.Forms.Application]::DoEvents()
 $b=New-Object Drawing.Bitmap $f.Width,$f.Height
 $f.DrawToBitmap($b,(New-Object Drawing.Rectangle 0,0,$f.Width,$f.Height));$b.Save("$repo\artifacts\categories.png");$b.Dispose()
} finally {$f.Dispose()}
Write-Host 'Category regression passed: legacy migration, add/delete, exclusions, fixed types, serialization, UI rendering.'
