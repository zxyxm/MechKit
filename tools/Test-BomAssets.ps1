$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
Add-Type -AssemblyName System.Drawing
$type = $assembly.GetType('MechKit.Core.BomAssets', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function CallAsset($name, [object[]]$arguments) {
    $method = $type.GetMethod($name,$flags)
    $parameters = $method.GetParameters()
    for ($i=0; $i -lt $arguments.Length; $i++) {
        $arguments[$i] = [Management.Automation.LanguagePrimitives]::ConvertTo($arguments[$i],$parameters[$i].ParameterType)
    }
    $method.Invoke($null,$arguments)
}
$root = Join-Path $repo ('artifacts\assets-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$source = Join-Path $root 'fixture.sldprt'
[IO.File]::WriteAllText($source,'version1')
$path = CallAsset 'AssetPath' @($source,'Default','preview','.png')
$other = CallAsset 'AssetPath' @($source,'Other','preview','.png')
if ($path -eq $other -or -not $path.Contains('fixture-Default-preview-')) { throw 'Naming or configuration isolation failed' }
$bitmap = New-Object Drawing.Bitmap 40,20
try { $png = CallAsset 'Png' @($bitmap) } finally { $bitmap.Dispose() }
CallAsset 'Write' @($path,[byte[]]$png)
$stamp = CallAsset 'Stamp' @($source)
$openModel = [IO.File]::Open($source, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
try {
    $sharedStamp = CallAsset 'Stamp' @($source)
    if ($sharedStamp -ne $stamp) { throw 'Shared model handle changed the stamp' }
} finally { $openModel.Dispose() }
CallAsset 'MarkCurrent' @($path,$stamp)
if (-not (CallAsset 'IsCurrent' @($path,$stamp))) { throw 'Fresh cache rejected' }
$time = [IO.File]::GetLastWriteTimeUtc($source)
[IO.File]::WriteAllText($source,'version2')
[IO.File]::SetLastWriteTimeUtc($source,$time)
$changed = CallAsset 'Stamp' @($source)
if (CallAsset 'IsCurrent' @($path,$changed)) { throw 'Same-size same-time source mutation not detected' }
[IO.File]::WriteAllText($path,'corrupt')
if (CallAsset 'IsCurrent' @($path,$stamp)) { throw 'Corrupt asset not detected' }
CallAsset 'Write' @($path,[byte[]]$png)
$image = [Drawing.Image]::FromFile($path)
try {
    if ($image.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or $image.Width -ne 40 -or $image.Height -ne 20) { throw 'PNG conversion changed dimensions' }
} finally { $image.Dispose() }
Write-Output 'PASS: readable names, configuration isolation, content validation, corrupt cache rejection, atomic replacement, PNG dimensions'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$rowType = $assembly.GetType('MechKit.Features.PartListRow',$true)
$row = [Activator]::CreateInstance($rowType)
$row.FilePath = $source
$row.Configuration = 'Default'
$row.Classification = [string][char]0x6807 + [char]0x51c6 + [char]0x4ef6
$row.Quantity = 1
$kind = [string][char]0x5c3a + [char]0x5bf8 + [char]0x8bf4 + [char]0x660e + [char]0x56fe
$illustration = CallAsset 'AssetPath' @($source,'Default',$kind,'.png')
CallAsset 'Write' @($illustration,[byte[]]$png)
if ((CallAsset 'Illustration' @($source,'Default')) -ne $illustration) { throw 'Illustration discovery failed' }
$rows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
$rows.Add($row)
$options = [Activator]::CreateInstance($assembly.GetType('MechKit.Features.PartListOptions'))
$options.ColumnOrder = 'drawing,name'
$callbackType = [Func``2].MakeGenericType($rowType,[byte[]])
$callback = [Management.Automation.LanguagePrimitives]::ConvertTo({param($row) return $null},$callbackType)
$exporter = $assembly.GetType('MechKit.Features.BomExcelExporter')
$book = $exporter.GetMethod('ExportWithPreviews',$flags).Invoke($null,@($rows,[string]$root,'illustration',$options,$callback))
$zip = [IO.Compression.ZipFile]::OpenRead($book)
try {
    if (-not $zip.GetEntry('xl/media/image1.png')) { throw 'Illustration missing when model thumbnail absent' }
    $reader = New-Object IO.StreamReader ($zip.GetEntry('xl/drawings/drawing1.xml').Open())
    try { [xml]$drawing = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $anchor = $drawing.DocumentElement.FirstChild
    if ($anchor.from.col -ne '1' -or $anchor.from.row -ne '1') { throw 'Illustration attached to wrong BOM cell' }
} finally { $zip.Dispose() }
Write-Output 'PASS: persisted illustration discovery and Excel drawing-column embedding without model thumbnail'
