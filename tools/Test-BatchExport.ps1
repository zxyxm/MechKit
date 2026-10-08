[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'))
$service = $assembly.GetType('MechKit.Features.ExportService', $true)
$formats = $assembly.GetType('MechKit.Features.ExportFormats', $true)
$optionsType = $assembly.GetType('MechKit.Features.BatchExportOptions', $true)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MechKit-export-' + [Guid]::NewGuid())
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$log = [Action[string]] { param($message) }
function Assert($condition, $message) {
    if (-not $condition) { throw $message }
}
try {
    $part = Join-Path $fixture 'bracket.SLDPRT'
    $drawing = Join-Path $fixture 'bracket.SLDDRW'
    $assemblyFile = Join-Path $fixture 'frame.sldasm'
    $assemblyDrawing = Join-Path $fixture 'frame.slddrw'
    foreach ($path in @($part, $drawing, $assemblyFile, $assemblyDrawing)) {
        [IO.File]::WriteAllText($path, '')
    }
    $resolve = $service.GetMethod('ResolveFiles')
    $paired = $resolve.Invoke($null, @([string[]]@($part, $drawing, $part.ToLowerInvariant()), $true, $log))
    Assert ($paired.Count -eq 2 -and $paired -contains $drawing) 'Part pairing or case-insensitive deduplication failed.'
    $reverse = $resolve.Invoke($null, @([string[]]@($drawing), $true, $log))
    Assert ($reverse.Count -eq 2 -and $reverse.Contains([IO.Path]::ChangeExtension($drawing, '.sldprt'))) 'Drawing-to-model pairing failed.'
    $unpaired = $resolve.Invoke($null, @([string[]]@($part), $false, $log))
    Assert ($unpaired.Count -eq 1) 'Disabled pairing added a companion.'
    $asmPair = $resolve.Invoke($null, @([string[]]@($assemblyDrawing), $true, $log))
    Assert ($asmPair.Count -eq 2 -and $asmPair.Contains($assemblyFile)) 'Assembly pairing failed.'
    $orphan = Join-Path $fixture 'orphan.sldprt'
    [IO.File]::WriteAllText($orphan, '')
    $missing = $resolve.Invoke($null, @([string[]]@($orphan), $true, $log))
    Assert ($missing.Count -eq 1) 'Missing companion was fabricated.'
    $step = $formats.GetMethod('Find').Invoke($null, @('.step'))
    Assert ($step.Extension -eq '.stp') 'Legacy STEP setting was not migrated.'
    $pdf = $formats.GetField('Pdf').GetValue($null)
    Assert ($pdf.Supports(3) -and -not $pdf.Supports(1) -and -not $pdf.Supports(2)) 'PDF must export drawings only.'
    Assert ($step.Supports(1) -and $step.Supports(2) -and -not $step.Supports(3)) 'STP must export models only.'
    $options = [Activator]::CreateInstance($optionsType, $true)
    $options.SourceRoot = $fixture
    $options.OutputFolder = Join-Path $fixture 'out'
    $options.KeepTree = $true
    $targetMethod = $service.GetMethod('BuildTargetPath', [Reflection.BindingFlags]'NonPublic,Static')
    $target = $targetMethod.Invoke($null, @($options, [string]$part, $step))
    Assert ($target -eq (Join-Path $options.OutputFolder 'bracket.stp')) 'Root file gained an extra output directory.'
    $nested = Join-Path $fixture 'sub\bracket.sldprt'
    $nestedTarget = $targetMethod.Invoke($null, @($options, [string]$nested, $step))
    Assert ($nestedTarget -eq (Join-Path $options.OutputFolder 'sub\bracket.stp')) 'Nested directory was not preserved.'
    $options.Files.Add($part)
    $options.Formats.Add($step)
    $options.AutoPair = $false
    $options.Overwrite = $false
    $options.CreateReport = $false
    [IO.Directory]::CreateDirectory($options.OutputFolder) | Out-Null
    [IO.File]::WriteAllText($target, 'existing output')
    $report = $service.GetMethod('Run').Invoke($null, @($null, $options, $log, [Func[bool]] { $false }, $null))
    Assert ($report.Skipped -eq 1 -and $report.Failed -eq 0 -and $report.Exported -eq 0) 'Existing output was counted as failure.'
    Assert ([IO.File]::ReadAllText($target) -eq 'existing output') 'Existing output was modified.'
    Add-Type -AssemblyName System.Windows.Forms
    $formType = $assembly.GetType('MechKit.UI.BatchExportForm', $true)
    $tree = [System.Windows.Forms.TreeView]::new()
    $rootNode = [System.Windows.Forms.TreeNode]::new('assembly')
    $rootNode.Tag = $assemblyFile
    $rootNode.Checked = $true
    $childNode = [System.Windows.Forms.TreeNode]::new('part')
    $childNode.Tag = $part
    $childNode.Checked = $true
    $duplicateNode = [System.Windows.Forms.TreeNode]::new('second instance')
    $duplicateNode.Tag = $part
    $duplicateNode.Checked = $true
    $rootNode.Nodes.Add($childNode) | Out-Null
    $rootNode.Nodes.Add($duplicateNode) | Out-Null
    $tree.Nodes.Add($rootNode) | Out-Null
    $checkedFiles = [System.Collections.Generic.List[string]]::new()
    $collectTree = $formType.GetMethod('CollectCheckedFiles', [Reflection.BindingFlags]'NonPublic,Static')
    $collectTree.Invoke($null, @($tree.Nodes, $checkedFiles))
    Assert ($checkedFiles.Count -eq 2) 'Assembly tree instances were not deduplicated.'
    $setTree = $formType.GetMethod('SetChildrenChecked', [Reflection.BindingFlags]'NonPublic,Static')
    $setTree.Invoke($null, @($rootNode, $false))
    $checkedFiles.Clear()
    $collectTree.Invoke($null, @($tree.Nodes, $checkedFiles))
    Assert ($checkedFiles.Count -eq 1 -and -not $childNode.Checked) 'Tree child selection did not propagate.'
    $tree.Dispose()
    Write-Host 'PASS: assembly tree selection and instance deduplication.'
    Write-Host 'PASS: pairing, deduplication, missing companion, format routing, legacy settings, output paths, overwrite skipping.'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup path escaped the temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
