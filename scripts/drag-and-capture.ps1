# Performs a press-drag-release sequence at given screen coordinates and
# then screenshots the window. Used to verify pan-on-empty-drag and
# node-drag-distance behavior.
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    # Start point (client-space, relative to window top-left).
    [Parameter(Mandatory = $true)]
    [int]$StartX,

    [Parameter(Mandatory = $true)]
    [int]$StartY,

    # End point (client-space).
    [Parameter(Mandatory = $true)]
    [int]$EndX,

    [Parameter(Mandatory = $true)]
    [int]$EndY,

    [int]$Steps = 20,

    [int]$StepDelayMs = 25,

    [int]$WaitBefore = 6,

    [int]$WaitAfter = 2
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
    Start-Sleep -Seconds $WaitBefore
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

    # Open the test task graph by clicking the sidebar entry.
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    Write-Host "Opening task graph via sidebar click..."
    [Win32.User32]::SetCursorPos($rect.Left + 140, $rect.Top + 195) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Seconds 3

    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $startScreenX = $rect.Left + $StartX
    $startScreenY = $rect.Top + $StartY
    $endScreenX = $rect.Left + $EndX
    $endScreenY = $rect.Top + $EndY

    Write-Host "Dragging from ($StartX,$StartY) to ($EndX,$EndY) in $Steps steps..."
    [Win32.User32]::SetCursorPos($startScreenX, $startScreenY) | Out-Null
    Start-Sleep -Milliseconds 100
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 80

    for ($i = 1; $i -le $Steps; $i++) {
        $t = $i / $Steps
        $x = [int]($startScreenX + ($endScreenX - $startScreenX) * $t)
        $y = [int]($startScreenY + ($endScreenY - $startScreenY) * $t)
        [Win32.User32]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds $StepDelayMs
    }
    Start-Sleep -Milliseconds 100
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Write-Host "Drag released."
    Start-Sleep -Seconds $WaitAfter

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
    }
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
