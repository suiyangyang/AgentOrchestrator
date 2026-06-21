# Verifies the "click task graph card freezes UI" fix by:
# 1. Launching the App.
# 2. Screenshotting the initial state to confirm the sidebar is visible.
# 3. Recording the start time, clicking the named task graph card.
# 4. Polling the screen (with a short sample of the canvas region) until
#    the task graph workspace appears (or a long timeout).
# 5. Saving a few mid-flight screenshots so we can see WHEN the UI became
#    responsive (or whether it stayed blank the whole time).
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    # Client-space coords for the task graph card. With default 1300x800
    # window and 300px sidebar, the first task-graph card ("压力测试 100
    # 节点") sits at roughly (175, 152).
    [int]$CardClickX = 175,
    [int]$CardClickY = 152,

    # If the workspace has not switched after this many seconds, treat it
    # as frozen and exit non-zero so CI / harness sees the regression.
    [int]$SwitchTimeoutSeconds = 30,

    # Stop polling once we've seen the switch for this many seconds; we
    # keep sampling so we can confirm the UI stays responsive.
    [int]$PostSwitchObserveSeconds = 3,

    [int]$WaitAfterLaunchSeconds = 8,
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
public static extern void mouse_event(uint dwFlags, uint dx, int dy, int dwData, int dwExtraInfo);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP   = 0x0004

function Save-Screenshot([System.Diagnostics.Process]$proc, [string]$path) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        return $null
    }
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdc, 0x02) | Out-Null
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return @{ Width = $w; Height = $h }
}

function Send-Click([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $sx = [int]$rect.Left + $cx
    $sy = [int]$rect.Top + $cy
    [Win32.User32]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
}

function Get-CanvasStripSignature([System.Diagnostics.Process]$proc) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $null }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdc, 0x02) | Out-Null
    $gfx.ReleaseHdc($hdc)

    # Sample a strip that contains the graph editor title bar — when the
    # task graph is shown this strip shows "图编辑" (dark pixels). When
    # chat is shown, this strip is the empty chat area (white).
    $startX = [int]($w * 0.45)
    $endX   = [int]($w * 0.85)
    $y      = [int]($h * 0.28)
    $yEnd   = $y + 30
    if ($endX -le $startX -or $yEnd -ge $h) { $bmp.Dispose(); return $null }

    $sumR = 0L; $sumG = 0L; $sumB = 0L
    $count = 0
    for ($x = $startX; $x -lt $endX; $x += 3) {
        for ($yy = $y; $yy -lt $yEnd; $yy += 3) {
            $c = $bmp.GetPixel($x, $yy)
            $sumR += [int]$c.R
            $sumG += [int]$c.G
            $sumB += [int]$c.B
            $count++
        }
    }
    $bmp.Dispose()
    if ($count -eq 0) { return $null }
    return @{
        AvgR = [int]($sumR / $count)
        AvgG = [int]($sumG / $count)
        AvgB = [int]($sumB / $count)
    }
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
$exitCode = 0

try {
    Write-Host "Waiting $WaitAfterLaunchSeconds s for window..."
    Start-Sleep -Seconds $WaitAfterLaunchSeconds
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "Process did not create a window." }

    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    $baselineSize = Save-Screenshot $proc (Join-Path $OutputDir "01-before-click.png")
    $baselineSig = Get-CanvasStripSignature $proc
    Write-Host "Baseline size: $($baselineSize.Width)x$($baselineSize.Height)"
    Write-Host "Baseline (chat) strip RGB: $($baselineSig.AvgR),$($baselineSig.AvgG),$($baselineSig.AvgB)"

    Write-Host "Clicking task graph card at ($CardClickX,$CardClickY)..."
    $clickStart = [DateTime]::UtcNow
    Send-Click $proc $CardClickX $CardClickY

    $switchedMs = -1
    $lastShotAt = [DateTime]::UtcNow
    $screenshotIndex = 2
    $pollDeadline = $clickStart.AddSeconds($SwitchTimeoutSeconds)
    while ((Get-Date) -lt $pollDeadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) {
            Write-Host "ERROR: process exited unexpectedly."
            break
        }

        $now = [DateTime]::UtcNow
        if (($now - $lastShotAt).TotalMilliseconds -ge 1000) {
            Save-Screenshot $proc (Join-Path $OutputDir ("{0:D2}-t{1:0000}ms.png" -f $screenshotIndex, [int]($now - $clickStart).TotalMilliseconds)) | Out-Null
            $lastShotAt = $now
            $screenshotIndex++
        }

        $sig = Get-CanvasStripSignature $proc
        if ($null -ne $sig -and $null -ne $baselineSig) {
            $dR = [Math]::Abs($sig.AvgR - $baselineSig.AvgR)
            $dG = [Math]::Abs($sig.AvgG - $baselineSig.AvgG)
            $dB = [Math]::Abs($sig.AvgB - $baselineSig.AvgB)
            $delta = $dR + $dG + $dB
            if ($delta -ge 10) {
                $switchedMs = [int]($now - $clickStart).TotalMilliseconds
                Write-Host "Workspace switched after ${switchedMs} ms (RGB delta = $delta)."
                break
            }
        }
    }

    if ($switchedMs -lt 0) {
        Write-Host "TIMEOUT: workspace did not switch within $SwitchTimeoutSeconds s."
        $exitCode = 5
    }

    $observeDeadline = ([DateTime]::UtcNow).AddSeconds($PostSwitchObserveSeconds)
    $stableSigCount = 0
    $lastStableSig = $null
    while ((Get-Date) -lt $observeDeadline) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        $sig = Get-CanvasStripSignature $proc
        if ($null -ne $sig) {
            $sigKey = "$($sig.AvgR),$($sig.AvgG),$($sig.AvgB)"
            if ($sigKey -eq $lastStableSig) { $stableSigCount++ } else { $stableSigCount = 0; $lastStableSig = $sigKey }
        }
        Save-Screenshot $proc (Join-Path $OutputDir ("{0:D2}-observe.png" -f $screenshotIndex)) | Out-Null
        $screenshotIndex++
    }

    Save-Screenshot $proc (Join-Path $OutputDir "99-final.png") | Out-Null

    $verdict = if ($switchedMs -lt 0) {
        "FROZEN_OR_TIMEOUT"
    } elseif ($switchedMs -lt 1500) {
        "SNAPPY"
    } elseif ($switchedMs -lt 5000) {
        "OK"
    } else {
        "SLOW"
    }

    Write-Host "Switched: $($switchedMs -ge 0)   ElapsedMs: $($switchedMs)   Verdict: $verdict"

    $summary = @{
        ExePath              = $ExePath
        CardClickX           = $CardClickX
        CardClickY           = $CardClickY
        SwitchTimeoutSeconds = $SwitchTimeoutSeconds
        BaselineStrip        = $baselineSig
        Switched             = ($switchedMs -ge 0)
        ElapsedMs            = $switchedMs
        StableSigSamples     = $stableSigCount
        Verdict              = $verdict
    } | ConvertTo-Json -Depth 3
    Set-Content -LiteralPath (Join-Path $OutputDir "summary.json") -Value $summary -Encoding UTF8
    Write-Host "Summary: $(Join-Path $OutputDir 'summary.json')"
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

exit $exitCode
