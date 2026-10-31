param(
    [string]$OutputPath = "CtrlZed.generated.ico"
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$navy = [System.Drawing.Color]::FromArgb(34,50,76)
$gold = [System.Drawing.Color]::FromArgb(229,163,65)
$white = [System.Drawing.Color]::White

$g.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$radius = 36
$rect = New-Object System.Drawing.RectangleF 8,8,240,240
$diameter = $radius*2
$path.AddArc($rect.X,$rect.Y,$diameter,$diameter,180,90)
$path.AddArc($rect.Right-$diameter,$rect.Y,$diameter,$diameter,270,90)
$path.AddArc($rect.Right-$diameter,$rect.Bottom-$diameter,$diameter,$diameter,0,90)
$path.AddArc($rect.X,$rect.Bottom-$diameter,$diameter,$diameter,90,90)
$path.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush $navy),$path)

$pen = New-Object System.Drawing.Pen $gold, 24
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($pen,48,48,160,160,35,285)

$arrow = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 72,52),
    (New-Object System.Drawing.PointF 72,94),
    (New-Object System.Drawing.PointF 34,76)
)
$g.FillPolygon((New-Object System.Drawing.SolidBrush $gold),$arrow)

$trail1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120,229,163,65)), 10
$trail1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$trail1.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($trail1,66,48,154,154,292,52)

$trail2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60,229,163,65)), 5
$trail2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$trail2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($trail2,76,52,148,148,295,48)

$font = New-Object System.Drawing.Font "Segoe UI", 92, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString("Z",$font,(New-Object System.Drawing.SolidBrush $white),(New-Object System.Drawing.RectangleF 54,56,150,150),$format)

$full = [System.IO.Path]::GetFullPath($OutputPath)
$dir = [System.IO.Path]::GetDirectoryName($full)
if($dir -and !(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$hIcon = $bmp.GetHicon()
try {
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $fs = [System.IO.File]::Create($full)
    try { $icon.Save($fs) } finally { $fs.Dispose(); $icon.Dispose() }
}
finally {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeIcon {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@
    [NativeIcon]::DestroyIcon($hIcon) | Out-Null
    $g.Dispose()
    $bmp.Dispose()
}
