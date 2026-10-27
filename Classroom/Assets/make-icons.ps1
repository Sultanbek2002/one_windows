$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
foreach ($role in @('teacher','student')) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot "$role.png"))
    $bitmap = New-Object System.Drawing.Bitmap(256,256)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $memory = New-Object System.IO.MemoryStream
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, [System.Drawing.Rectangle]::new(0,0,256,256), [System.Drawing.Rectangle]::new(250,125,755,725), [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $memory.ToArray()
        $file = [System.IO.File]::Create((Join-Path $PSScriptRoot "$role.ico"))
        $writer = New-Object System.IO.BinaryWriter($file)
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
        } finally { $writer.Dispose() }
    } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose() }
}
