# Verifies node-to-node connection drag interaction in TaskGraphWorkspaceControl.
# Captures 6 screenshots proving each step of the workflow:
#   01-baseline.png       — graph view with visible connection points (Ellipses)
#   02-hover.png          — cursor over a connection point, hover color (#FF6A00) visible
#   03-drag-empty-mid.png — mid-drag showing preview line extending into empty area
#   04-after-empty-release.png — line gone, no new node, no extra edge
#   05-drag-node-mid.png  — mid-drag pointing at another node, target highlight visible
#   06-after-node-release.png — new edge visible between source and target nodes

param(
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\connection-drag",
    [string]$ExePath   = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-build\AgentOrchestrator.App.exe"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath. Run 'dotnet build' first."
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
[System.Runtime.InteropServices.DllImport("gdi32.dll")]
public static extern System.IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, System.IntPtr devMode);
[System.Runtime.InteropServices.DllImport("gdi32.dll")]
public static extern int GetPixel(System.IntPtr hdc, int nXPos, int nYPos);
[System.Runtime.InteropServices.DllImport("gdi32.dll")]
public static extern bool DeleteDC(System.IntPtr hdc);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN  = 0x0002
$MOUSEEVENTF_LEFTUP    = 0x0004

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

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdcWindow = [Win32.User32]::GetWindowDC($proc.MainWindowHandle)
    $hdcMem = $gfx.GetHdc()
    [Win32.User32]::PrintWindow($proc.MainWindowHandle, $hdcMem, 0x02) | Out-Null
    $gfx.ReleaseHdc($hdcMem)
    [Win32.User32]::ReleaseDC($proc.MainWindowHandle, $hdcWindow) | Out-Null
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $path"
}

function Get-ScreenPoint([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $sx = [int]$rect.Left + [int]$cx
    $sy = [int]$rect.Top + [int]$cy
    return [pscustomobject]@{ X = $sx; Y = $sy }
}

function MoveTo($proc, [int]$x, [int]$y) {
    $p = Get-ScreenPoint $proc $x $y
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
    Start-Sleep -Milliseconds 150
}

function LeftClick($proc, [int]$x, [int]$y) {
    MoveTo $proc $x $y
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
}

function Get-PixelColorAtScreen([int]$x, [int]$y) {
    $hdc = [Win32.User32]::CreateDC("DISPLAY", $null, $null, [IntPtr]::Zero)
    if ($hdc -eq [IntPtr]::Zero) { return $null }
    try {
        $colorRef = [Win32.User32]::GetPixel($hdc, $x, $y)
        return $colorRef
    } finally {
        [Win32.User32]::DeleteDC($hdc) | Out-Null
    }
}

function ColorRefToRGB([int]$colorRef) {
    if ($colorRef -eq -1) { return @{ R=0; G=0; B=0 } }
    $b = $colorRef -band 0xFF
    $g = ($colorRef -shr 8) -band 0xFF
    $r = ($colorRef -shr 16) -band 0xFF
    return @{ R=$r; G=$g; B=$b }
}

# Read pixel color at window-relative coords and report.
function Report-PixelColor($proc, [int]$wx, [int]$wy) {
    $sp = Get-ScreenPoint $proc $wx $wy
    $colorRef = Get-PixelColorAtScreen $sp.X $sp.Y
    if ($null -eq $colorRef) { return @{ R=-1; G=-1; B=-1 } }
    $c = ColorRefToRGB $colorRef
    Write-Host "  Pixel at window($wx,$wy) [screen $($sp.X),$($sp.Y)]: R=$($c.R) G=$($c.G) B=$($c.B)"
    return $c
}

# ---- Kill any lingering process ----
Get-Process -Name "AgentOrchestrator.App" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 1

# ---- Launch app ----
Write-Host "`nLaunching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    # Wait for window
    Start-Sleep -Seconds 8
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

    # ---- Open the test task graph (click sidebar entry at ~140,195) ----
    Write-Host "`n=== Opening test task graph ==="
    LeftClick $proc 140 195
    Start-Sleep -Seconds 3

    # Click on a node to select it.
    Write-Host "Selecting first node at (600, 380)..."
    LeftClick $proc 600 380
    Start-Sleep -Milliseconds 800

    # ---- Step 1: Baseline screenshot ----
    Write-Host "`n=== Capturing 01-baseline.png ==="
    Save-Screenshot $proc (Join-Path $OutputDir "01-baseline.png")
    
    # Analyze baseline image to find connection point dot position.
    $baselinePath = Join-Path $OutputDir "01-baseline.png"
    if (Test-Path $baselinePath) {
        $img = [System.Drawing.Image]::FromFile($baselinePath)
        Write-Host "  Baseline dimensions: $($img.Width)x$($img.Height)"
        $bmp = New-Object System.Drawing.Bitmap($img)
        $img.Dispose()

        # Scan for dark dot pixels on right edge of first node (x=700..745, y=350..480).
        $foundDots = @()
        for ($sy = 350; $sy -le 480; $sy += 2) {
            if ($sy -ge $bmp.Height) { break }
            for ($sx = 700; $sx -le 745; $sx += 1) {
                if ($sx -ge $bmp.Width) { continue }
                $px = $bmp.GetPixel($sx, $sy)
                # Dark pixels: R<60 G<70 B<70 (connection dot fill or stroke).
                if ($px.R -lt 60 -and $px.G -lt 70 -and $px.B -lt 70) {
                    $foundDots += [pscustomobject]@{ X=$sx; Y=$sy }
                }
            }
        }
        $bmp.Dispose()

        if ($foundDots.Count -gt 0) {
            # Find the center of the dot cluster.
            $avgX = [math]::Round(($foundDots | Measure-Object -Property X -Average).Average)
            $avgY = [math]::Round(($foundDots | Measure-Object -Property Y -Average).Average)
            Write-Host "  Found $($foundDots.Count) dark pixels; center at ($avgX, $avgY)"
        } else {
            # Default position based on node geometry: right edge of node centered at (600,380), width=240.
            # Node left=480, right=720 => connection dot at x~715-720, y~380.
            $avgX = 720; $avgY = 380
            Write-Host "  No dark pixels found in scan range; using default ($avgX, $avgY)"
        }

        # Connection point is on the right edge of node content area (inside the Border).
        # The Ellipse is horizontally aligned Right inside its Grid cell, so it should be at ~718.
        $connPointX = 720
        $connPointY = $avgY

        Write-Host "  Connection point coords: ($connPointX, $connPointY)"

        # Report pixel color BEFORE hover (should be dark dot color #1F2328).
        Write-Host "  Pre-hover pixel check:"
        Report-PixelColor $proc $connPointX $connPointY | Out-Null
    } else {
        $connPointX = 720; $connPointY = 380
    }

    # ---- Step 2: Hover connection point ----
    Write-Host "`n=== Capturing 02-hover.png ==="
    MoveTo $proc $connPointX $connPointY
    Start-Sleep -Milliseconds 400   # Give Avalonia time to render :pointerover style
    
    # Report pixel color AFTER hover.
    Write-Host "  Post-hover pixel check:"
    $hoverPixel = Report-PixelColor $proc $connPointX $connPointY
    if ($hoverPixel.R -gt 200 -and $hoverPixel.G -lt 150 -and $hoverPixel.B -lt 60) {
        Write-Host "  -> Hover color #FF6A00 DETECTED!"
    } else {
        Write-Warning "  -> Hover color NOT detected at connection point."
    }

    Save-Screenshot $proc (Join-Path $OutputDir "02-hover.png")

    # ---- Step 3: Drag to empty area, capture mid-drag ----
    Write-Host "`n=== Capturing 03-drag-empty-mid.png ==="
    MoveTo $proc $connPointX $connPointY
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 150
    MoveTo $proc 900 600   # Empty area below/right of nodes
    Start-Sleep -Milliseconds 300
    Save-Screenshot $proc (Join-Path $OutputDir "03-drag-empty-mid.png")

    # Release in empty area.
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 500

    # ---- Step 4: After empty release ----
    Write-Host "`n=== Capturing 04-after-empty-release.png ==="
    Save-Screenshot $proc (Join-Path $OutputDir "04-after-empty-release.png")

    # ---- Step 5: Drag to another node, capture mid-drag ----
    # Second node position: layer spacing is 280px horizontally from first node center.
    # First node center ~600 -> second node center ~(600+280)=880, y~380.
    Write-Host "`n=== Capturing 05-drag-node-mid.png ==="
    MoveTo $proc $connPointX $connPointY
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 150
    # Drag toward second node center.
    MoveTo $proc 880 380
    Start-Sleep -Milliseconds 400   # Wait for target highlight to render
    Save-Screenshot $proc (Join-Path $OutputDir "05-drag-node-mid.png")

    # ---- Step 6: Release on target node ----
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 800   # Wait for edge to be created and rendered

    Write-Host "`n=== Capturing 06-after-node-release.png ==="
    Save-Screenshot $proc (Join-Path $OutputDir "06-after-node-release.png")

    # ---- Summary ----
    Write-Host ""
    Write-Host "========== VERIFICATION SUMMARY =========="
    
    $screenshotNames = @(
        "01-baseline.png"
        "02-hover.png"
        "03-drag-empty-mid.png"
        "04-after-empty-release.png"
        "05-drag-node-mid.png"
        "06-after-node-release.png"
    )

    foreach ($name in $screenshotNames) {
        $fp = Join-Path $OutputDir $name
        if (Test-Path $fp) {
            $info = Get-Item $fp
            $sizeKB = [math]::Round($info.Length / 1024.0, 1)
            
            try {
                $img = [System.Drawing.Image]::FromFile($fp)
                $dimStr = "$($img.Width)x$($img.Height)"
                $img.Dispose()
            } catch {
                $dimStr = "unknown"
            }

            if ($info.Length -lt 10240) {
                Write-Warning "  SMALL: $fp ($sizeKB KB, $dimStr) — may be empty or failed capture!"
            } else {
                Write-Host "  OK:    $fp ($sizeKB KB, $dimStr)"
            }
        } else {
            Write-Warning "  MISSING: $fp"
        }
    }

} finally {
    if (-not $proc.HasExited) {
        Write-Host "`nClosing app..."
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) {
            $proc.Kill()
        }
    }
    $proc.Dispose()
}
