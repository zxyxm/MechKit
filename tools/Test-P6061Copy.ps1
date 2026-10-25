$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$type = $assembly.GetType('MechKit.Features.P6061CopyService', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$match = $type.GetMethod('Matches', $flags)
$copy = $type.GetMethod('Copy', $flags)
foreach ($case in @(@('20261017-p6061-板.sldprt',$true),@('20261017_P6061_板.SLDPRT',$true),@('20261017-6061-板.sldprt',$false),@('20261017-p6061x-板.sldprt',$false),@('20261017-p6061-板.sldasm',$false))) {
    if ($match.Invoke($null,@($case[0])) -ne $case[1]) { throw "Match failed: $($case[0])" }
}
$root = Join-Path $repo ('artifacts\p6061-copy-' + [Guid]::NewGuid().ToString('N'))
$a = New-Item -ItemType Directory -Path "$root\a"
$b = New-Item -ItemType Directory -Path "$root\b"
$dest = New-Item -ItemType Directory -Path "$root\out"
$part = "$a\20261017-p6061-板.sldprt"
$drawing = [IO.Path]::ChangeExtension($part,'.slddrw')
[IO.File]::WriteAllText($part,'part fixture')
[IO.File]::WriteAllText($drawing,'drawing fixture')
$missing = "$a\20261017_p6061_缺图.sldprt"
[IO.File]::WriteAllText($missing,'missing drawing')
$collisionA = "$a\20261017-p6061-重名.sldprt"
$collisionB = "$b\20261017-p6061-重名.sldprt"
[IO.File]::WriteAllText($collisionA,'a')
[IO.File]::WriteAllText($collisionB,'b')
$sources = [string[]]@($part,$part,$missing,$collisionA,$collisionB)
$result = $copy.Invoke($null,@($sources,$dest.FullName))
if ([IO.File]::ReadAllText("$dest\20261017-p6061-板.sldprt") -ne 'part fixture') { throw 'Part copy failed' }
if ([IO.File]::ReadAllText("$dest\20261017-p6061-板.slddrw") -ne 'drawing fixture') { throw 'Drawing copy failed' }
if (Test-Path "$dest\20261017-p6061-重名.sldprt") { throw 'Collision was not blocked' }
if ($result -notmatch '零件 2 个、工程图 1 个') { throw $result }
[IO.File]::WriteAllText("$dest\20261017-p6061-板.sldprt",'keep existing')
$null = $copy.Invoke($null,@($sources,$dest.FullName))
if ([IO.File]::ReadAllText("$dest\20261017-p6061-板.sldprt") -ne 'keep existing') { throw 'Existing file overwritten' }
Write-Output 'PASS: exact p6061 matching, duplicates, drawings, missing drawings, same-name conflicts, no overwrite.'
