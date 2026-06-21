# Launches the app, navigates to the task graph, performs a sequence of
# interactions (right-click, ctrl+wheel, delete key), and captures one
# screenshot per step. Each step is configurable via -Steps.
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string[]]$Steps = @(),

    [int]$WaitBefore = 8,

    [int]$WaitBetween = 3,

    [int]$ProcessExitTimeoutMs = 4000
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath. Run 'dotnet build -c Debug' first."
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
public static extern System.IntPtr GetWindowDC(System.IntPtr hWnd);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern int ReleaseDC(System.IntPtr hWnd, System.IntPtr hDC);
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
$MOUSEEVENTF_RIGHTDOWN = 0x0008
$MOUSEEVENTF_RIGHTUP   = 0x0010
$MOUSEEVENTF_WHEEL     = 0x0800
$KEYEVENTF_KEYUP       = 0x0002

# Virtual key codes
$VK_DELETE = 0x2E
$VK_CONTROL = 0x11

function Save-Screenshot([System.Diagnostics.Process]$proc, [string]$path) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "No window handle; skipping screenshot."
        return
    }
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { Write-Warning "Zero-sized window."; return }

    # Use a window-DC BitBlt of the window region, including any owned
    # popups (ContextMenu, modal confirm dialog) that PrintWindow may miss.
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdcWindow = [Win32.User32]::GetWindowDC($proc.MainWindowHandle)
    $hdcMem = $gfx.GetHdc()
    $src = New-Object Win32.User32+RECT
    $src.Left = 0; $src.Top = 0; $src.Right = $w; $src.Bottom = $h
    [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdcMem, 0x02) | Out-Null
    $gfx.ReleaseHdc($hdcMem)
    [Win32.User32]::ReleaseDC($proc.MainWindowHandle, $hdcWindow) | Out-Null
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $path"
}

function Save-ScreenshotWithPopups([System.Diagnostics.Process]$proc, [string]$path) {
    # Capture the entire virtual screen so child windows (context menus,
    # modal confirm dialogs) are included. Useful when PrintWindow misses
    # them because they live in their own top-level windows.
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved (screen) $path"
}

function Get-ScreenPoint([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $sx = [int]$rect.Left + [int]$cx
    $sy = [int]$rect.Top + [int]$cy
    return [pscustomobject]@{ X = $sx; Y = $sy }
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    Start-Sleep -Seconds $WaitBefore
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "Process did not create a window."
    }
    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    # 1. Always: open the test task graph by clicking the sidebar entry.
    $p = Get-ScreenPoint $proc 140 195
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Write-Host "Opened task graph."
    Start-Sleep -Seconds 3

    # Click on 任务 1 node to select it (so Delete / right-click target).
    $p = Get-ScreenPoint $proc 600 380
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 100
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 50
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Write-Host "Selected 任务 1."
    Start-Sleep -Milliseconds 800

    # 2. Save a "baseline" screenshot of the graph view (selected node visible).
    Save-Screenshot $proc (Join-Path $OutputDir "01-graph-view.png")

    # 3. Run each step.
    $stepIndex = 1
    foreach ($step in $Steps) {
        $stepIndex++
        Write-Host "--- Step: $step ---"
        switch ($step) {
            "rightclick" {
                # First make sure 任务 1 is selected (re-click in case focus
                # shifted). Left click to select, then release capture.
                $p = Get-ScreenPoint $proc 600 380
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 150
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 80
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 400
                # Now right-click. Longer hold so Avalonia recognises the tap.
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 100
                [Win32.User32]::mouse_event($MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 120
                [Win32.User32]::mouse_event($MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 1000
                # Use screen-level capture so the popup is included.
                Save-ScreenshotWithPopups $proc (Join-Path $OutputDir "02-context-menu.png")
                # Dismiss menu with Escape so subsequent steps aren't blocked.
                [Win32.User32]::keybd_event(0x1B, 0, 0, 0)
                Start-Sleep -Milliseconds 80
                [Win32.User32]::keybd_event(0x1B, 0, $KEYEVENTF_KEYUP, 0)
                Start-Sleep -Milliseconds 500
            }
            "ctrlwheel-zoom-in" {
                $p = Get-ScreenPoint $proc 700 600
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 100
                # Hold Ctrl, wheel up (delta 120), release Ctrl
                [Win32.User32]::keybd_event($VK_CONTROL, 0, 0, 0)
                Start-Sleep -Milliseconds 50
                [Win32.User32]::mouse_event($MOUSEEVENTF_WHEEL, 0, 0, [uint32]120, 0)
                [Win32.User32]::mouse_event($MOUSEEVENTF_WHEEL, 0, 0, [uint32]120, 0)
                Start-Sleep -Milliseconds 100
                [Win32.User32]::keybd_event($VK_CONTROL, 0, $KEYEVENTF_KEYUP, 0)
                Start-Sleep -Milliseconds 600
                Save-Screenshot $proc (Join-Path $OutputDir "03-zoomed-in.png")
            }
            "ctrlwheel-zoom-out" {
                $p = Get-ScreenPoint $proc 700 600
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 100
                [Win32.User32]::keybd_event($VK_CONTROL, 0, 0, 0)
                Start-Sleep -Milliseconds 50
                $negDelta = [uint32]([math]::Pow(2, 32) - 120)
                [Win32.User32]::mouse_event($MOUSEEVENTF_WHEEL, 0, 0, $negDelta, 0)
                [Win32.User32]::mouse_event($MOUSEEVENTF_WHEEL, 0, 0, $negDelta, 0)
                Start-Sleep -Milliseconds 100
                [Win32.User32]::keybd_event($VK_CONTROL, 0, $KEYEVENTF_KEYUP, 0)
                Start-Sleep -Milliseconds 600
                Save-Screenshot $proc (Join-Path $OutputDir "04-zoomed-out.png")
            }
            "delete-key" {
                # Select 任务 1 again (focus may have shifted). Left-click
                # on the node then release so the border's pointer capture
                # is cleaned up.
                $p = Get-ScreenPoint $proc 600 380
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 150
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 80
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 500
                # Click on the canvas surface (an empty area inside the
                # graph surface but not on a node) so the UserControl
                # itself receives focus and not just a child Border.
                $p = Get-ScreenPoint $proc 600 540
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 100
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 50
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 300
                # Re-select the node now that focus is on the workspace.
                $p = Get-ScreenPoint $proc 600 380
                [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
                Start-Sleep -Milliseconds 100
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 50
                [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
                Start-Sleep -Milliseconds 300
                # Press Delete (use scan code 0x53 for VK_DELETE).
                [Win32.User32]::keybd_event($VK_DELETE, 0x53, 0, 0)
                Start-Sleep -Milliseconds 120
                [Win32.User32]::keybd_event($VK_DELETE, 0x53, $KEYEVENTF_KEYUP, 0)
                Start-Sleep -Milliseconds 1000
                # Use screen-level capture so the modal dialog is included.
                Save-ScreenshotWithPopups $proc (Join-Path $OutputDir "05-delete-confirm.png")
            }
            default {
                Write-Warning "Unknown step: $step"
            }
        }
        Start-Sleep -Seconds $WaitBetween
    }
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit($ProcessExitTimeoutMs)) {
            $proc.Kill()
        }
    }
    $proc.Dispose()
}
