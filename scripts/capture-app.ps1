# Launches AgentOrchestrator.App, optionally clicks at a client coordinate,
# then screenshots the window to PNG. Returns the screen rect used so the
# caller can compute follow-up click coordinates.
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    # Client-space coordinates (relative to window's client area top-left).
    # Use @() to skip the click and screenshot the initial view.
    [int[]]$ClickXY,

    [int]$WaitAfterLaunchSeconds = 8,

    [int]$WaitAfterClickSeconds = 3,

    [int]$ProcessExitTimeoutMs = 4000
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath. Run 'dotnet build -c Debug' first."
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
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

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

    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    if ($ClickXY -and $ClickXY.Count -eq 2) {
        $rect = New-Object Win32.User32+RECT
        [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
        $screenX = $rect.Left + $ClickXY[0]
        $screenY = $rect.Top + $ClickXY[1]
        Write-Host "Clicking client ($($ClickXY[0]),$($ClickXY[1])) -> screen ($screenX,$screenY)"
        [Win32.User32]::SetCursorPos($screenX, $screenY) | Out-Null
        Start-Sleep -Milliseconds 150
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 60
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        Write-Host "Waiting $WaitAfterClickSeconds s for view to switch..."
        Start-Sleep -Seconds $WaitAfterClickSeconds
    }

    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    Write-Host "Window rect: $($rect.Left),$($rect.Top) ${width}x${height}"

    if ($width -gt 0 -and $height -gt 0) {
        $bmp = New-Object System.Drawing.Bitmap $width, $height
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $gfx.GetHdc()
        $ok = [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdc, 0x02)
        $gfx.ReleaseHdc($hdc)
        $gfx.Dispose()

        if ($ok) {
            $bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "Saved screenshot to $OutputPath"
        } else {
            Write-Warning "PrintWindow returned false."
        }
        $bmp.Dispose()
    } else {
        Write-Warning "Window has zero size; skipping screenshot."
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
