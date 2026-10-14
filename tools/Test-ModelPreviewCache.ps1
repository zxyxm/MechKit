$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$t=$a.GetType('MechKit.UI.ModelPreviewCache',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$cache=[Activator]::CreateInstance($t,$flags,$null,@([int]2),$null)
$read=$t.GetMethod('Read',$flags)
$root=Join-Path $repo ('artifacts\preview-cache-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$file=Join-Path $root 'fixture.sldprt'
[IO.File]::WriteAllText($file,'fixture only')
$script:calls=0
$reader=[Func[byte[]]]{ $script:calls++;return ,([byte[]]@(1,2,3)) }
function ReadCache($configuration){return $read.Invoke($cache,@([string]$file,[string]$configuration,$reader))}
[void](ReadCache 'Default');[void](ReadCache 'Default')
if($script:calls -ne 1){throw 'Repeated hover read SW twice'}
[void](ReadCache 'Other')
if($script:calls -ne 2){throw 'Configuration cache mixed previews'}
[IO.File]::WriteAllText($file,'changed fixture bytes')
[void](ReadCache 'Default')
if($script:calls -ne 3){throw 'Changed file did not invalidate preview'}
[void](ReadCache 'Third');[void](ReadCache 'Other')
if($script:calls -ne 5){throw 'Bounded cache did not evict old preview'}
$t.GetMethod('Clear',$flags).Invoke($cache,@())|Out-Null
$script:missing=0
$empty=[Func[byte[]]]{$script:missing++;return $null}
[void]$read.Invoke($cache,@([string]$file,'None',$empty))
[void]$read.Invoke($cache,@([string]$file,'None',$empty))
if($script:missing -ne 1){throw 'Missing preview retried on every hover'}
Write-Output 'PASS: hover reuse, configuration isolation, saved-file invalidation, bounded eviction, missing-preview caching'
