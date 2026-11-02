param(
    [string]$OutputPath = "CtrlZed.generated.ico"
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(4,15,24))
$cyan = [System.Drawing.Color]::FromArgb(55,228,240)
$cyanPale = [System.Drawing.Color]::FromArgb(185,249,255)
$gold = [System.Drawing.Color]::FromArgb(255,193,55)
$steel = [System.Drawing.Color]::FromArgb(214,235,241)

$g.FillEllipse($bg,8,8,240,240)

function Draw-Arc($rect,$color,$width,$start,$sweep) {
    $pen = New-Object System.Drawing.Pen $color,$width
    $pen.StartCap=[System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap=[System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen,$rect,$start,$sweep)
    $pen.Dispose()
}

Draw-Arc (New-Object System.Drawing.Rectangle 22,22,212,212) $cyanPale 7 195 116
Draw-Arc (New-Object System.Drawing.Rectangle 22,22,212,212) $gold 7 15 116
Draw-Arc (New-Object System.Drawing.Rectangle 45,45,166,166) $cyan 9 206 96
Draw-Arc (New-Object System.Drawing.Rectangle 45,45,166,166) $gold 9 26 96

$nodeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(6,25,34))
$nodePen = New-Object System.Drawing.Pen $cyan,4
foreach($p in @(@(32,128),@(224,128),@(128,32),@(128,224))) {
    $g.FillEllipse($nodeBrush,$p[0]-8,$p[1]-8,16,16)
    $g.DrawEllipse($nodePen,$p[0]-8,$p[1]-8,16,16)
}

# Metallic angular Z.
$z = New-Object System.Drawing.Drawing2D.GraphicsPath
$pts=[System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 70,70),
    (New-Object System.Drawing.PointF 190,70),
    (New-Object System.Drawing.PointF 163,100),
    (New-Object System.Drawing.PointF 120,143),
    (New-Object System.Drawing.PointF 174,143),
    (New-Object System.Drawing.PointF 198,179),
    (New-Object System.Drawing.PointF 67,179),
    (New-Object System.Drawing.PointF 96,145),
    (New-Object System.Drawing.PointF 144,98),
    (New-Object System.Drawing.PointF 99,98)
)
$z.AddPolygon($pts)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0,50)),((New-Object System.Drawing.Point 0,190)),$steel,([System.Drawing.Color]::FromArgb(70,155,172))
$g.FillPath($grad,$z)
$outline=New-Object System.Drawing.Pen $cyanPale,3
$g.DrawPath($outline,$z)

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
    $outline.Dispose(); $grad.Dispose(); $z.Dispose()
    $nodePen.Dispose(); $nodeBrush.Dispose(); $bg.Dispose()
    $g.Dispose(); $bmp.Dispose()
}
