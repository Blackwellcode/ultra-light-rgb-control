# Draws the application icon: the ring gauge of the Classic theme on a dark rounded square, in the
# temperature colours of the themes (cyan, green, amber, red). Every size is drawn natively instead of
# scaled down, so the small ones stay crisp.
# Output: src\UltraLightRgb\app.ico (16-256 px) and docs\images\icon.png (256 px).
# Windows PowerShell 5.1, no elevation: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

# same stops as Pal.Scale in Theme.cs
$stops = @(
    @(0.0, [Drawing.Color]::FromArgb(0, 200, 255)),
    @(0.45, [Drawing.Color]::FromArgb(80, 230, 140)),
    @(0.7, [Drawing.Color]::FromArgb(255, 190, 40)),
    @(1.0, [Drawing.Color]::FromArgb(255, 60, 60))
)

function Get-ScaleColor([double]$pos) {
    for ($i = 1; $i -lt $stops.Count; $i++) {
        if ($pos -le $stops[$i][0]) {
            $p0 = $stops[$i - 1][0]; $c0 = $stops[$i - 1][1]; $c1 = $stops[$i][1]
            $t = ($pos - $p0) / ($stops[$i][0] - $p0)
            return [Drawing.Color]::FromArgb(
                [int]($c0.R + ($c1.R - $c0.R) * $t), [int]($c0.G + ($c1.G - $c0.G) * $t), [int]($c0.B + ($c1.B - $c0.B) * $t))
        }
    }
    return $stops[-1][1]
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)

    # rounded square, a little inset so it does not touch the edge
    $inset = [Math]::Max(0.5, $s * 0.03)
    $box = New-Object Drawing.RectangleF $inset, $inset, ($s - 2 * $inset), ($s - 2 * $inset)
    $r = $s * 0.22
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
    $path.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
    $path.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush $box, ([Drawing.Color]::FromArgb(24, 28, 42)), ([Drawing.Color]::FromArgb(6, 7, 12)), 90.0
    $g.FillPath($bg, $path)

    $bg.Dispose(); $path.Dispose(); $g.Dispose()

    # ring: the full 270 degree scale on tiny sizes, a gauge at 80 % with track and tip on larger ones.
    # Drawn per pixel with analytic coverage: pen segments leave visible seams in the colour sweep.
    $small = $s -lt 32
    $geo = @{
        Width  = if ($small) { $s * 0.17 } else { $s * 0.13 }
        Radius = $s * 0.30
        Cx     = $s / 2.0
        Cy     = $s / 2.0 + $s * 0.02
        Start  = 135.0
        Sweep  = 270.0
    }
    $fill = if ($small) { 1.0 } else { 0.8 }
    if (-not $small) { Add-Arc $bmp $geo $geo.Sweep { param($p) [Drawing.Color]::FromArgb(44, 50, 68) } }
    Add-Arc $bmp $geo ($geo.Sweep * $fill) { param($p) Get-ScaleColor $p }
    if (-not $small) {
        # white tip, the needle of the gauge
        $a = ($geo.Start + $geo.Sweep * $fill) * [Math]::PI / 180
        Add-Disc $bmp ($geo.Cx + $geo.Radius * [Math]::Cos($a)) ($geo.Cy + $geo.Radius * [Math]::Sin($a)) ($geo.Width * 0.31) ([Drawing.Color]::White)
    }
    return $bmp
}

function Get-Coverage([double]$edgeDistance) { [Math]::Max(0.0, [Math]::Min(1.0, $edgeDistance + 0.5)) }

# Blends an arc with round caps over the bitmap; $colorOf maps the position along the full sweep (0..1).
function Add-Arc([Drawing.Bitmap]$bmp, [hashtable]$geo, [double]$end, [scriptblock]$colorOf) {
    $half = $geo.Width / 2
    $caps = foreach ($deg in $geo.Start, ($geo.Start + $end)) {
        $a = $deg * [Math]::PI / 180
        , @(($geo.Cx + $geo.Radius * [Math]::Cos($a)), ($geo.Cy + $geo.Radius * [Math]::Sin($a)))
    }
    Set-Pixels $bmp {
        param($x, $y)
        $dx = $x - $geo.Cx; $dy = $y - $geo.Cy
        $dist = [Math]::Sqrt($dx * $dx + $dy * $dy)
        if ([Math]::Abs($dist - $geo.Radius) -gt $half + 1) { return $null }
        $rel = ([Math]::Atan2($dy, $dx) * 180 / [Math]::PI - $geo.Start + 720) % 360
        if ($rel -le $end) {
            $cov = Get-Coverage ($half - [Math]::Abs($dist - $geo.Radius))
            $pos = $rel / $geo.Sweep
        }
        else {
            $cov = 0.0; $pos = 0.0
            for ($i = 0; $i -lt 2; $i++) {
                $cx = $caps[$i][0] - $x; $cy = $caps[$i][1] - $y
                $c = Get-Coverage ($half - [Math]::Sqrt($cx * $cx + $cy * $cy))
                if ($c -gt $cov) { $cov = $c; $pos = if ($i -eq 0) { 0.0 } else { $end / $geo.Sweep } }
            }
        }
        if ($cov -le 0) { return $null }
        return @((& $colorOf $pos), $cov)
    }
}

function Add-Disc([Drawing.Bitmap]$bmp, [double]$cx, [double]$cy, [double]$r, [Drawing.Color]$color) {
    Set-Pixels $bmp {
        param($x, $y)
        $dx = $x - $cx; $dy = $y - $cy
        $cov = Get-Coverage ($r - [Math]::Sqrt($dx * $dx + $dy * $dy))
        if ($cov -le 0) { return $null }
        return @($color, $cov)
    }
}

# Calls $shade for every pixel centre; a returned (colour, coverage) is blended source-over.
function Set-Pixels([Drawing.Bitmap]$bmp, [scriptblock]$shade) {
    $s = $bmp.Width
    $rect = New-Object Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadWrite, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $buf = New-Object byte[] ($data.Stride * $s)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
        for ($y = 0; $y -lt $s; $y++) {
            for ($x = 0; $x -lt $s; $x++) {
                $hit = & $shade ($x + 0.5) ($y + 0.5)
                if ($null -eq $hit) { continue }
                $col = $hit[0]; $a = [double]$hit[1]
                $o = $y * $data.Stride + $x * 4
                $da = $buf[$o + 3] / 255.0
                $oa = $a + $da * (1 - $a)
                if ($oa -le 0) { continue }
                # BGRA, straight alpha
                $buf[$o] = [byte][Math]::Round(($col.B * $a + $buf[$o] * $da * (1 - $a)) / $oa)
                $buf[$o + 1] = [byte][Math]::Round(($col.G * $a + $buf[$o + 1] * $da * (1 - $a)) / $oa)
                $buf[$o + 2] = [byte][Math]::Round(($col.R * $a + $buf[$o + 2] * $da * (1 - $a)) / $oa)
                $buf[$o + 3] = [byte][Math]::Round($oa * 255)
            }
        }
        [Runtime.InteropServices.Marshal]::Copy($buf, 0, $data.Scan0, $buf.Length)
    }
    finally { $bmp.UnlockBits($data) }
}

# one ICO entry as a classic DIB: BITMAPINFOHEADER, BGRA rows bottom-up, empty AND mask (alpha decides)
function Get-DibBytes([Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($s * $s * 4 + $maskRow * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $s, $s), [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $row = New-Object byte[] ($s * 4)
        for ($y = $s - 1; $y -ge 0; $y--) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
            $w.Write($row)
        }
    }
    finally { $bmp.UnlockBits($data) }
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return $ms.ToArray()
}

function Get-PngBytes([Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    # 256 px as PNG (the usual practice since Vista), the rest as DIB for every icon reader
    $bytes = if ($s -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    if ($s -eq 256) { [IO.File]::WriteAllBytes((Join-Path $root 'docs\images\icon.png'), $bytes) }
    $bmp.Dispose()
    , @($s, $bytes)
}

$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $len = $img[1].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$len); $w.Write([int]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
$out = Join-Path $root 'src\UltraLightRgb\app.ico'
[IO.File]::WriteAllBytes($out, $ico.ToArray())
"ico=$out bytes=$((Get-Item $out).Length) sizes=$($sizes -join ',')"
"png=$(Join-Path $root 'docs\images\icon.png')"
