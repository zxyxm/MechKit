$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$t=$a.GetType('MechKit.Features.PartListService',$true)
$f=[Reflection.BindingFlags]'Static,NonPublic'
$m=$t.GetMethod('ApplyConfiguredFields',$f)
$options=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListOptions'))
$cases=@(
 @('淘宝-五金-7型把手M8X20','7型把手','M8X20'),
 @('淘宝-五金-7型把手8X20加长','7型把手','8X20加长'),
 @('淘宝-五金-7型把手','7型把手',''),
 @('淘宝-五金-7型M8','7型','M8'),
 @('淘宝-五金-7M8','','7M8'),
 @('淘宝-五金-双头外螺纹d6L25M5','双头外螺纹','d6L25M5'),
 @('淘宝-五金-右边ZNC334-3扭力2','右边','ZNC334-3扭力2'),
 @('代理-传感器-接近开关E2E-X5E1-Z','接近开关','E2E-X5E1-Z'),
 @('淘宝_五金_直线轴承LMK20LUU','直线轴承','LMK20LUU'),
 @('淘宝-五金-接头12毫米-加长','接头','12毫米-加长'),
 @('淘宝-五金-弹簧','弹簧',''),
 @('代理-电气-RMH20X600S-0-+','', 'RMH20X600S-0-+'),
 @('淘宝-五金-MYT-STH6A嘉立创FA（加长）','','MYT-STH6A嘉立创FA（加长）')
)
foreach($case in $cases){
 $row=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListRow'))
 $row.Classification='标准件';$row.FilePath=[string]$case[0]+'.sldprt';$row.FullName=$case[0]
 $m.Invoke($null,@($row,[string]$row.FilePath,$null,$options))|Out-Null
 if($row.Name -ne $case[1] -or $row.Material -ne $case[2]){throw ('Wrong split: '+$case[0]+' => '+$row.Name+' / '+$row.Material)}
 $name=$t.GetMethod('BuildComponentBaseName',$f).Invoke($null,@($row,$options.Naming))
 if($name -ne ([string]$case[0]).Replace('_','-')){throw ('Rename lost prefix or model: '+$name)}
}
Write-Output 'PASS: configured mapping, Chinese names, letter/digit models, suffix Chinese, separators, no model, model-only, rename roundtrip.'
