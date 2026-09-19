<#
.SYNOPSIS
    Generates the CommandManager icon set for MechKit.

.DESCRIPTION
    SOLIDWORKS needs the add-in icons as files on disk, grouped by pixel size
    (see swImageSizeToUse_e: 20 / 32 / 40 / 64 / 96 / 128):
      * strip<size>.png  all command icons side by side (one cell per command)
      * main<size>.png   the command group main icon (single icon)
      * taskpane.png     the task pane tab icon

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Generate-Icons.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'src\MechKit\Resources\icons'
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$script:AccentColor = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)
$script:AccentDark = [System.Drawing.Color]::FromArgb(255, 12, 82, 143)

function New-RoundedPath {
    param(
        [single] $X, [single] $Y, [single] $Width, [single] $Height, [single] $Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    if ($diameter -gt $Width) { $diameter = $Width }
    if ($diameter -gt $Height) { $diameter = $Height }

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconCanvas {
    param([int] $Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    return $bitmap
}

function Get-IconGraphics {
    param([System.Drawing.Bitmap] $Bitmap)

    $graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    return $graphics
}

# Command 0: batch export (sheet + arrow leaving it)
function Draw-ExportIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentColor)

    try {
        $m = $Size * 0.16
        $w = $Size * 0.40
        $h = $Size * 0.62
        $Graphics.DrawLine($pen, $Size - $m - $w, $Size - $m, $Size - $m - $w, $Size - $m - $h)
        $Graphics.DrawLine($pen, $Size - $m - $w, $Size - $m - $h, $m + $w * 0.4, $Size - $m - $h)

        $midY = $Size * 0.50
        $x1 = $Size * 0.42
        $x2 = $Size * 0.88
        $Graphics.DrawLine($pen, $x1, $midY, $x2, $midY)

        $points = New-Object 'System.Drawing.PointF[]' 3
        $points[0] = New-Object System.Drawing.PointF(([single]$x2), ([single]$midY))
        $points[1] = New-Object System.Drawing.PointF(([single]($x2 - $Size * 0.20)), ([single]($midY - $Size * 0.17)))
        $points[2] = New-Object System.Drawing.PointF(([single]($x2 - $Size * 0.20)), ([single]($midY + $Size * 0.17)))
        $Graphics.FillPolygon($brush, $points)
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command: one-click BOM (checklist)
function Draw-BomIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.3, $Size * 0.085))))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $left = $Size * 0.14
        $box = $Size * 0.17
        $rows = @(0.22, 0.44, 0.66)

        for ($i = 0; $i -lt $rows.Count; $i++) {
            $top = $Size * $rows[$i]
            $Graphics.DrawRectangle($pen, $left, $top, $box, $box)
            if ($i -lt 2) {
                $Graphics.FillRectangle($brush, $left + $box * 0.28, $top + $box * 0.28, $box * 0.44, $box * 0.44)
            }

            $lineStart = $left + $box + $Size * 0.10
            $lineWidth = if ($i -eq 2) { 0.30 } else { 0.52 }
            $Graphics.DrawLine($pen, $lineStart, $top + $box / 2, $lineStart + $Size * $lineWidth, $top + $box / 2)
        }
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command: personal settings (sliders)
function Draw-SettingsIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.3, $Size * 0.085))))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $left = $Size * 0.14
        $right = $Size * 0.86
        $rows = @(0.26, 0.50, 0.74)
        $knobs = @(0.62, 0.34, 0.72)

        for ($i = 0; $i -lt $rows.Count; $i++) {
            $y = $Size * $rows[$i]
            $Graphics.DrawLine($pen, $left, $y, $right, $y)

            $knob = $Size * 0.13
            $x = $left + ($right - $left) * $knobs[$i] - $knob / 2
            $Graphics.FillEllipse($brush, $x, $y - $knob / 2, $knob, $knob)
        }
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command: machined-part naming rule (calendar, because names start with a date)
function Draw-CalendarIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.3, $Size * 0.085))))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $m = $Size * 0.14
        $w = $Size - 2 * $m
        $h = $Size - 2 * $m
        $path = New-RoundedPath -X $m -Y ($m + $Size * 0.06) -Width $w -Height ($h - $Size * 0.06) -Radius ($Size * 0.14)
        try { $Graphics.DrawPath($pen, $path) } finally { $path.Dispose() }

        # Top band
        $band = New-RoundedPath -X $m -Y ($m + $Size * 0.06) -Width $w -Height ($Size * 0.20) -Radius ($Size * 0.14)
        try { $Graphics.FillPath($brush, $band) } finally { $band.Dispose() }

        # Two rings on top
        $Graphics.DrawLine($pen, $Size * 0.34, $Size * 0.12, $Size * 0.34, $Size * 0.26)
        $Graphics.DrawLine($pen, $Size * 0.66, $Size * 0.12, $Size * 0.66, $Size * 0.26)

        # Day dots
        $dot = $Size * 0.09
        foreach ($x in @(0.32, 0.50, 0.68)) {
            $Graphics.FillEllipse($brush, $Size * $x - $dot / 2, $Size * 0.58 - $dot / 2, $dot, $dot)
            $Graphics.FillEllipse($brush, $Size * $x - $dot / 2, $Size * 0.76 - $dot / 2, $dot, $dot)
        }
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command: standard-part prefix rule (label tag)
function Draw-TagIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.3, $Size * 0.085))))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $points = New-Object 'System.Drawing.PointF[]' 5
        $points[0] = New-Object System.Drawing.PointF(([single]($Size * 0.12)), ([single]($Size * 0.32)))
        $points[1] = New-Object System.Drawing.PointF(([single]($Size * 0.46)), ([single]($Size * 0.12)))
        $points[2] = New-Object System.Drawing.PointF(([single]($Size * 0.88)), ([single]($Size * 0.44)))
        $points[3] = New-Object System.Drawing.PointF(([single]($Size * 0.54)), ([single]($Size * 0.88)))
        $points[4] = New-Object System.Drawing.PointF(([single]($Size * 0.12)), ([single]($Size * 0.62)))
        $Graphics.DrawPolygon($pen, $points)

        $dot = $Size * 0.13
        $Graphics.FillEllipse($brush, $Size * 0.30 - $dot / 2, $Size * 0.32 - $dot / 2, $dot, $dot)
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command: parts list (table with header row)
function Draw-PartListIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $m = $Size * 0.13
        $w = $Size - 2 * $m
        $h = $Size - 2 * $m
        $path = New-RoundedPath -X $m -Y $m -Width $w -Height $h -Radius ($Size * 0.14)
        try { $Graphics.DrawPath($pen, $path) } finally { $path.Dispose() }

        # Header band
        $header = New-RoundedPath -X ($m + $Size * 0.05) -Y ($m + $Size * 0.08) -Width ($w - $Size * 0.10) -Height ($Size * 0.16) -Radius ($Size * 0.06)
        try { $Graphics.FillPath($brush, $header) } finally { $header.Dispose() }

        $linePen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.1, $Size * 0.065))))
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            $left = $m + $Size * 0.10
            $right = $Size - $m - $Size * 0.10
            $rows = @(0.44, 0.62, 0.80)
            foreach ($ratio in $rows) {
                $Graphics.DrawLine($linePen, $left, $Size * $ratio, $right, $Size * $ratio)
            }
            # Vertical split between part number and quantity columns
            $Graphics.DrawLine($linePen, $Size * 0.66, $Size * 0.38, $Size * 0.66, $Size * 0.86)
        }
        finally {
            $linePen.Dispose()
        }
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command 1: custom properties (card + text lines)
function Draw-PropertyIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    try {
        $m = $Size * 0.15
        $path = New-RoundedPath -X $m -Y $m -Width ($Size - 2 * $m) -Height ($Size - 2 * $m) -Radius ($Size * 0.14)
        try { $Graphics.DrawPath($pen, $path) } finally { $path.Dispose() }

        $linePen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.2, $Size * 0.075))))
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            $inner = $Size * 0.28
            $ratios = @(0.0, 0.28, 0.56)
            for ($i = 0; $i -lt $ratios.Count; $i++) {
                $ratio = $ratios[$i]
                $y = $inner + ($Size - 2 * $inner) * $ratio
                $width = 0.44
                if ($i -eq 2) { $width = 0.20 }
                $Graphics.DrawLine($linePen, $inner, $y, $inner + $Size * $width, $y)
            }
        }
        finally {
            $linePen.Dispose()
        }
    }
    finally {
        $pen.Dispose()
    }
}

# Command 5: undo (left-turning arrow)
function Draw-UndoIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    try {
        $points = New-Object 'System.Drawing.PointF[]' 3
        $points[0] = New-Object System.Drawing.PointF([single]($Size * 0.38), [single]($Size * 0.22))
        $points[1] = New-Object System.Drawing.PointF([single]($Size * 0.18), [single]($Size * 0.42))
        $points[2] = New-Object System.Drawing.PointF([single]($Size * 0.38), [single]($Size * 0.62))
        $Graphics.DrawLines($pen, $points)
        $Graphics.DrawLine($pen, $Size * 0.19, $Size * 0.42, $Size * 0.58, $Size * 0.42)
        $rect = New-Object System.Drawing.RectangleF([single]($Size * 0.35), [single]($Size * 0.30), [single]($Size * 0.46), [single]($Size * 0.46))
        $Graphics.DrawArc($pen, $rect, 250, 205)
    }
    finally {
        $pen.Dispose()
    }
}

# Command 2: task pane (window with a highlighted side bar)
function Draw-TaskPaneIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($script:AccentDark)

    try {
        $m = $Size * 0.14
        $w = $Size - 2 * $m
        $h = $Size - 2 * $m
        $path = New-RoundedPath -X $m -Y $m -Width $w -Height $h -Radius ($Size * 0.16)
        try { $Graphics.DrawPath($pen, $path) } finally { $path.Dispose() }

        $barWidth = $w * 0.30
        $barPath = New-RoundedPath -X ($m + $w * 0.06) -Y ($m + $h * 0.14) -Width $barWidth -Height ($h * 0.72) -Radius ($Size * 0.08)
        try { $Graphics.FillPath($brush, $barPath) } finally { $barPath.Dispose() }
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command 3: about (ring + info mark)
function Draw-AboutIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.4, $Size * 0.09))))
    $brush = New-Object System.Drawing.SolidBrush($script:AccentColor)

    try {
        $m = $Size * 0.14
        $Graphics.DrawEllipse($pen, $m, $m, $Size - 2 * $m, $Size - 2 * $m)

        $cx = $Size / 2.0
        $Graphics.DrawLine($pen, $cx, $Size * 0.44, $cx, $Size * 0.72)

        $dot = $Size * 0.075
        $Graphics.FillEllipse($brush, $cx - $dot / 2, $Size * 0.28, $dot, $dot)
    }
    finally {
        $pen.Dispose()
        $brush.Dispose()
    }
}

# Command group main icon: hex nut
function Draw-MainIcon {
    param([System.Drawing.Graphics] $Graphics, [int] $Size)

    $pen = New-Object System.Drawing.Pen($script:AccentColor, ([single]([Math]::Max(1.6, $Size * 0.10))))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    try {
        $cx = $Size / 2.0
        $cy = $Size / 2.0
        $radius = $Size * 0.40
        $points = New-Object 'System.Drawing.PointF[]' 6
        for ($i = 0; $i -lt 6; $i++) {
            $angle = [Math]::PI / 180.0 * (60 * $i - 30)
            $px = [single]($cx + $radius * [Math]::Cos($angle))
            $py = [single]($cy + $radius * [Math]::Sin($angle))
            $points[$i] = New-Object System.Drawing.PointF($px, $py)
        }
        $Graphics.DrawPolygon($pen, $points)
        $Graphics.DrawEllipse($pen, $cx - $Size * 0.14, $cy - $Size * 0.14, $Size * 0.28, $Size * 0.28)
    }
    finally {
        $pen.Dispose()
    }
}

$commandNames = @('Bom', 'PartList', 'PartRule', 'TagRule', 'Export', 'Undo', 'TaskPane', 'Settings', 'About')
$sizes = @(20, 32, 40, 64, 96, 128)

# 命令图标之后还要留两段：
#   1 格空白  —— 给命令组里的分隔线（spacer）占位
#   12 格标签牌 —— 给「常用前缀」按钮（电机 / 电气 / 淘宝 / 气动 …）
$prefixCellCount = 12
$commandCount = $commandNames.Count + 1 + $prefixCellCount

foreach ($size in $sizes) {
    $strip = New-Object System.Drawing.Bitmap(($size * $commandCount), $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $strip.SetResolution(96, 96)
    $stripGraphics = [System.Drawing.Graphics]::FromImage($strip)
    $stripGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $stripGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $stripGraphics.Clear([System.Drawing.Color]::Transparent)

    try {
        for ($i = 0; $i -lt $commandCount; $i++) {
            $cell = New-IconCanvas -Size $size
            $cellGraphics = Get-IconGraphics -Bitmap $cell
            try {
                if ($i -lt $commandNames.Count) {
                    switch ($commandNames[$i]) {
                        'Bom' { Draw-BomIcon $cellGraphics $size }
                        'PartList' { Draw-PartListIcon $cellGraphics $size }
                        'Export' { Draw-ExportIcon $cellGraphics $size }
                        'Undo' { Draw-UndoIcon $cellGraphics $size }
                        'TaskPane' { Draw-TaskPaneIcon $cellGraphics $size }
                        'Settings' { Draw-SettingsIcon $cellGraphics $size }
                        'PartRule' { Draw-CalendarIcon $cellGraphics $size }
                        'TagRule' { Draw-TagIcon $cellGraphics $size }
                        'About' { Draw-AboutIcon $cellGraphics $size }
                    }
                }
                elseif ($i -eq $commandNames.Count) {
                    # 分隔线占位：保持透明
                }
                else {
                    Draw-TagIcon $cellGraphics $size
                }
            }
            finally {
                $cellGraphics.Dispose()
            }

            try {
                $stripGraphics.DrawImage($cell, ($i * $size), 0, $size, $size)
            }
            finally {
                $cell.Dispose()
            }
        }
    }
    finally {
        $stripGraphics.Dispose()
    }

    $strip.Save((Join-Path $OutputDirectory "strip$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $strip.Dispose()

    $main = New-IconCanvas -Size $size
    $mainGraphics = Get-IconGraphics -Bitmap $main
    try {
        Draw-MainIcon $mainGraphics $size
    }
    finally {
        $mainGraphics.Dispose()
    }

    $main.Save((Join-Path $OutputDirectory "main$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $main.Dispose()
}

$taskPane = New-IconCanvas -Size 20
$taskPaneGraphics = Get-IconGraphics -Bitmap $taskPane
try {
    Draw-MainIcon $taskPaneGraphics 20
}
finally {
    $taskPaneGraphics.Dispose()
}
$taskPane.Save((Join-Path $OutputDirectory 'taskpane.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$taskPane.Dispose()

$generated = Get-ChildItem -Path $OutputDirectory -Filter '*.png'
Write-Host ("Generated {0} icons in {1}" -f $generated.Count, $OutputDirectory)
$generated | Sort-Object Name | ForEach-Object { Write-Host ("  {0} ({1} bytes)" -f $_.Name, $_.Length) }
