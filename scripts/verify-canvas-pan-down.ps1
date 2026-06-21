# Focused pan test: open the canvas, then perform DOWN-pan on empty area
# to verify the vertical scroll changes. The horizontal pan can't show
# visible change because the initial scroll is at the leftmost position.
param(
    [string]$ExePath = "E:\Work\Code\Tools\AgentOrchestrator\src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.exe",
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-canvas-pan-down"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -Namespace Win32 -Name User32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetWindowRect(System.IntPtr hWnd, out RECT lpRect);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool BringWindowToTop(System.IntPtr hWnd);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr hWnd, System.IntPtr hdcBlt, uint nFlags);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetCursorPos(int X, int Y);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

function Capture-Window([System.IntPtr]$hWnd, [string]$path) {
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($hWnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok = [Win32.User32]::PrintWindow($hWnd, $hdc, 0x02)
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()
    if ($ok) { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    return $ok
}

function Client-To-Screen([System.IntPtr]$hWnd, [int]$cx, [int]$cy) {
    $r = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($hWnd, [ref]$r) | Out-Null
    return @{ X = $r.Left + $cx; Y = $r.Top + $cy }
}

function Click-At([System.IntPtr]$hWnd, [int]$cx, [int]$cy) {
    $p = Client-To-Screen $hWnd $cx $cy
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200
}

function Drag([System.IntPtr]$hWnd, [int]$x1, [int]$y1, [int]$x2, [int]$y2, [int]$steps = 20) {
    $p1 = Client-To-Screen $hWnd $x1 $y1
    $p2 = Client-To-Screen $hWnd $x2 $y2
    [Win32.User32]::SetCursorPos($p1.X, $p1.Y) | Out-Null
    Start-Sleep -Milliseconds 100
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    for ($i = 1; $i -le $steps; $i++) {
        $t = $i / $steps
        $x = [int]($p1.X + ($p2.X - $p1.X) * $t)
        $y = [int]($p1.Y + ($p2.Y - $p1.Y) * $t)
        [Win32.User32]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 20
    }
    Start-Sleep -Milliseconds 100
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    Start-Sleep -Seconds 6
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    Write-Host "Opening task graph..."
    Click-At $proc.MainWindowHandle 140 195
    Start-Sleep -Seconds 4
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "01-initial.png")
    Write-Host "Captured: 01-initial.png"

    # Pan DOWN: drag from (500, 300) to (500, 600). This should scroll
    # the canvas DOWN (reveal more content below).
    Write-Host "Pan DOWN: from (500, 300) to (500, 600)..."
    Drag $proc.MainWindowHandle 500 300 500 600
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "02-after-pan-down.png")
    Write-Host "Captured: 02-after-pan-down.png"

    # Pan UP: drag from (500, 600) to (500, 300). Scroll back up.
    Write-Host "Pan UP: from (500, 600) to (500, 300)..."
    Drag $proc.MainWindowHandle 500 600 500 300
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "03-after-pan-up.png")
    Write-Host "Captured: 03-after-pan-up.png"

    # Pan LEFT: drag from (700, 400) to (400, 400). Should scroll right.
    Write-Host "Pan LEFT: from (700, 400) to (400, 400)..."
    Drag $proc.MainWindowHandle 700 400 400 400
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "04-after-pan-left.png")
    Write-Host "Captured: 04-after-pan-left.png"

    Write-Host "Done. All captures saved to $OutputDir"
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}
