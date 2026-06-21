# Capture baseline state of task graph view to confirm the 5 issues
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [int]$WaitAfterLaunchSeconds = 8,
    [int]$WaitAfterActionSeconds = 3,
    [int]$ProcessExitTimeoutMs = 4000
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath."
}
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

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

$MOUSEEVENTF_LEFTDOWN  = 0x0002
$MOUSEEVENTF_LEFTUP    = 0x0004
$MOUSEEVENTF_WHEEL     = 0x0800
$KEYEVENTF_KEYUP       = 0x0002

function Save-Screenshot([System.Diagnostics.Process]$proc, [string]$path) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "No window handle."
        return
    }
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdc, 0x02) | Out-Null
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $path"
}

function Get-ScreenPoint([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    return [pscustomobject]@{ X = [int]$rect.Left + $cx; Y = [int]$rect.Top + $cy }
}

function Click([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy, [int]$hold = 60) {
    $p = Get-ScreenPoint $proc $cx $cy
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds $hold
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200
}

function DoubleClick([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    Click $proc $cx $cy 50
    Start-Sleep -Milliseconds 80
    Click $proc $cx $cy 50
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    Start-Sleep -Seconds $WaitAfterLaunchSeconds
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No window." }
    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    # 1. Open "Bug 列表处理" graph (the one with 19 nodes that matches Image 2).
    Click $proc 140 270
    Start-Sleep -Seconds $WaitAfterActionSeconds
    Save-Screenshot $proc (Join-Path $OutputDir "01-graph-normal.png")

    # 2. Click a node to trigger edit mode (Issue 1 + Issue 2).
    # A 19-node graph has nodes laid out on the right side of the canvas.
    # Click in the middle-right area where nodes should be visible.
    Click $proc 950 400
    Start-Sleep -Seconds 2
    Save-Screenshot $proc (Join-Path $OutputDir "02-single-click-edit-mode.png")

    # 3. Click empty space to deselect.
    Click $proc 700 700
    Start-Sleep -Milliseconds 800

    # 4. Click on a node to test default-sized node resize grip (Issue 4+5).
    # We need to scroll to see the right side. Or just click where a default node should be.
    # Try clicking on a different node.
    Click $proc 950 600
    Start-Sleep -Seconds 2
    Save-Screenshot $proc (Join-Path $OutputDir "03-second-node-selected.png")

    # 5. Find the maximize/fullscreen button. Looking at the XAML, it's in
    # the graph editor's top-right toolbar (column 8). Approximate at
    # client (1280, 100) within the toolbar area.
    # Actually, the fullscreen button is in the secondary toolbar. The graph
    # editor's header is at row 0 of the inner Border, which sits inside the
    # workspace at the top of the graph surface.
    # Toolbar is at y ~ 95-130; the fullscreen button is the rightmost.
    Click $proc 1280 105
    Start-Sleep -Seconds 2
    Save-Screenshot $proc (Join-Path $OutputDir "04-graph-maximized.png")

    # 6. Click a node in the maximized view (nodes should be on the right).
    Click $proc 1100 350
    Start-Sleep -Seconds 2
    Save-Screenshot $proc (Join-Path $OutputDir "05-maximized-node-selected.png")

    # 7. Click empty area on the left in maximized view to test issue 3.
    Click $proc 400 400
    Start-Sleep -Milliseconds 500
    Save-Screenshot $proc (Join-Path $OutputDir "06-maximized-empty-clicked.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit($ProcessExitTimeoutMs)) { $proc.Kill() }
    }
    $proc.Dispose()
}
