# Launch the app, click the first task graph to open the canvas, then
# perform a series of interactive tests to verify the canvas behavior.
# Each test takes a screenshot of the state before/after the action.
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [int]$WaitAfterLaunchSeconds = 6,
    [int]$WaitAfterGraphOpenSeconds = 4
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
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004
$MOUSEEVENTF_WHEEL = 0x0800
$VK_CONTROL = 0x11
$VK_MENU = 0x12

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

function Get-WindowRect([System.IntPtr]$hWnd) {
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($hWnd, [ref]$rect) | Out-Null
    return $rect
}

function Client-To-Screen([System.IntPtr]$hWnd, [int]$cx, [int]$cy) {
    $r = Get-WindowRect $hWnd
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

function Wheel([System.IntPtr]$hWnd, [int]$cx, [int]$cy, [int]$delta, [bool]$ctrl) {
    $p = Client-To-Screen $hWnd $cx $cy
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 100
    if ($ctrl) {
        # Simulate Ctrl-down using keybd_event (Win32) — held until the
        # wheel event finishes, then Ctrl-up.
        [Win32.User32]::keybd_event(0x11, 0, 0, 0) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    # mouse_event's dwData is a UInt16 for WHEEL (positive = forward/up,
    # negative = backward/down). Cast carefully to avoid PowerShell's
    # signed-int marshalling.
    [uint32]$wheelDelta = 0
    if ($delta -lt 0) { $wheelDelta = [uint32]($delta -band 0xFFFF) }
    else { $wheelDelta = [uint32]$delta }
    [Win32.User32]::mouse_event($MOUSEEVENTF_WHEEL, 0, 0, $wheelDelta, 0) | Out-Null
    Start-Sleep -Milliseconds 200
    if ($ctrl) {
        [Win32.User32]::keybd_event(0x11, 0, 2, 0) | Out-Null
    }
    Start-Sleep -Milliseconds 300
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    Write-Host "Waiting for window ($WaitAfterLaunchSeconds s)..."
    Start-Sleep -Seconds $WaitAfterLaunchSeconds
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "Process did not create a top-level window in time."
    }
    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    # 1) Open task graph "压力测试 100 节点" by clicking the first sidebar entry.
    Write-Host "Opening task graph (click on sidebar at 140, 195)..."
    Click-At $proc.MainWindowHandle 140 195
    Start-Sleep -Seconds $WaitAfterGraphOpenSeconds
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "01-graph-opened.png")
    Write-Host "Captured: 01-graph-opened.png"

    # The canvas is visible roughly in the middle column. Capture pre-pan coords
    # of one of the visible cards for later comparison.

    # 2) Test: click on empty canvas — should NOT create a node.
    Write-Host "Test 2: click on empty canvas (should NOT create node)..."
    Click-At $proc.MainWindowHandle 600 350
    Start-Sleep -Milliseconds 800
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "02-click-on-empty.png")
    Write-Host "Captured: 02-click-on-empty.png"

    # 3) Test: drag on empty canvas — should PAN the canvas.
    Write-Host "Test 3: drag on empty canvas (should PAN)..."
    # From (400, 400) to (700, 400) — drag right; canvas should pan right
    # (content should slide right with the cursor, or equivalently, the
    # scroll offset should decrease so the visible window moves left).
    Drag $proc.MainWindowHandle 400 400 700 400
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "03-after-pan-right.png")
    Write-Host "Captured: 03-after-pan-right.png"

    # 4) Test: drag back to original position.
    Write-Host "Test 4: drag back..."
    Drag $proc.MainWindowHandle 700 400 400 400
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "04-after-pan-back.png")
    Write-Host "Captured: 04-after-pan-back.png"

    # 5) Test: wheel scroll without Ctrl — should just scroll the canvas
    # (ScrollViewer default behavior), not zoom.
    Write-Host "Test 5: wheel without Ctrl (should scroll, not zoom)..."
    Wheel $proc.MainWindowHandle 600 400 -120 $false
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "05-after-wheel-no-ctrl.png")
    Write-Host "Captured: 05-after-wheel-no-ctrl.png"

    # 5b) Test: Ctrl+wheel forward — should ZOOM IN.
    Write-Host "Test 5b: Ctrl+wheel forward (should zoom in)..."
    Wheel $proc.MainWindowHandle 600 400 120 $true
    Start-Sleep -Milliseconds 400
    Wheel $proc.MainWindowHandle 600 400 120 $true
    Start-Sleep -Milliseconds 400
    Wheel $proc.MainWindowHandle 600 400 120 $true
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "05b-ctrl-wheel-zoom-in.png")
    Write-Host "Captured: 05b-ctrl-wheel-zoom-in.png"

    # 5c) Test: Ctrl+wheel back — should ZOOM OUT.
    Write-Host "Test 5c: Ctrl+wheel back (should zoom out)..."
    Wheel $proc.MainWindowHandle 600 400 -120 $true
    Start-Sleep -Milliseconds 400
    Wheel $proc.MainWindowHandle 600 400 -120 $true
    Start-Sleep -Milliseconds 400
    Wheel $proc.MainWindowHandle 600 400 -120 $true
    Start-Sleep -Milliseconds 400
    Wheel $proc.MainWindowHandle 600 400 -120 $true
    Start-Sleep -Milliseconds 600
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "05c-ctrl-wheel-zoom-out.png")
    Write-Host "Captured: 05c-ctrl-wheel-zoom-out.png"

    # 6) Test: click on a node — should select it (not create anything).
    Write-Host "Test 6: click on a visible node card..."
    # We don't know exact node coords without analyzing the previous screenshot,
    # but a generic click on the canvas area should at least be safe.
    Click-At $proc.MainWindowHandle 700 550
    Start-Sleep -Milliseconds 800
    Capture-Window $proc.MainWindowHandle (Join-Path $OutputDir "06-after-click-on-node.png")
    Write-Host "Captured: 06-after-click-on-node.png"

    Write-Host "All captures saved to $OutputDir"
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) {
            $proc.Kill()
        }
    }
    $proc.Dispose()
}
