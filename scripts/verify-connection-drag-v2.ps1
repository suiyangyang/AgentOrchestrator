#!/usr/bin/env pwsh
# verify-connection-drag-v2.ps1
# Verifies the 4 user requirements for node-to-node connection drag interaction.
# Improvements over v1:
#   - Correct coordinates (found by pixel-scanning the actual rendered UI):
#       Source node connection-point dot: window-relative (668, 644)
#       (Ellipse is 12x12 dark #1F2328, right-aligned inside the node's bottom actions row)
#   - Uses CopyFromScreen (VirtualScreen) instead of PrintWindow for captures —
#     this reliably captures the actual rendered hover state.
#   - Reads pixel color at the connection point and detects #FF6A00 hover state.

param(
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\connection-drag-v2",
    [string]$ExePath   = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-build\AgentOrchestrator.App.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath. Run 'dotnet build' first."
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

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
public static extern bool SetCursorPos(int X, int Y);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void mouse_event(uint dwFlags, uint dx, int dy, int dwData, int dwExtraInfo);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN  = 0x0002
$MOUSEEVENTF_LEFTUP    = 0x0004

# Source node connection point: window-relative (668, 644), verified by pixel scan.
$SRC_X = 668
$SRC_Y = 644
# Empty area to drag into (no node should be there): far-right area in the canvas.
$EMPTY_X = 1180
$EMPTY_Y = 700

function Get-ScreenPoint([System.Diagnostics.Process]$proc, [int]$cx, [int]$cy) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    return [pscustomobject]@{ X = ([int]$rect.Left + $cx); Y = ([int]$rect.Top + $cy) }
}

function MoveTo($proc, [int]$x, [int]$y) {
    $p = Get-ScreenPoint $proc $x $y
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
}

function Save-Screen([string]$path, [System.Diagnostics.Process]$proc) {
    # Get window rect and capture only the window area (not the full virtual screen).
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { Write-Warning "Zero-sized window."; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen([System.Drawing.Point]::new($rect.Left, $rect.Top), [System.Drawing.Point]::Empty, [System.Drawing.Size]::new($w, $h))
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  Saved $path ($w x $h)"
}

function Read-Pixel($path, [int]$x, [int]$y) {
    $img = [System.Drawing.Image]::FromFile($path)
    $bmp = New-Object System.Drawing.Bitmap($img)
    $img.Dispose()
    $px = $bmp.GetPixel($x, $y)
    $bmp.Dispose()
    return $px
}

function Report-Pixel($path, [int]$x, [int]$y, [string]$label) {
    $px = Read-Pixel $path $x $y
    Write-Host ("  [{0,-22}] ({1,4},{2,4}) R={3,3} G={4,3} B={5,3}" -f $label, $x, $y, $px.R, $px.G, $px.B)
    return $px
}

# Kill any lingering app
Get-Process -Name "AgentOrchestrator.App" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 1

Write-Host "`nLaunching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
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

    # Open task graph (click sidebar entry).
    Write-Host "`n=== Opening task graph ==="
    MoveTo $proc 140 195
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Seconds 3

    # Click source node to select.
    MoveTo $proc 600 600
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 800

    # === Step 1: baseline ===
    Write-Host "`n=== Step 1: baseline ==="
    # Move cursor away from the connection point first to ensure no hover state.
    MoveTo $proc 100 100
    Start-Sleep -Milliseconds 400
    $baselinePath = Join-Path $OutputDir "01-baseline.png"
    Save-Screen $baselinePath $proc
    Write-Host "  Pixel readings (no hover):"
    # Sample multiple pixels around expected dot center to find the actual dark core.
    # The Ellipse is 12x12, dark fill #1F2328 surrounded by 2px white stroke.
    $foundDark = $false
    for ($dy = -3; $dy -le 5; $dy += 1) {
        for ($dx = -3; $dx -le 5; $dx += 1) {
            $px = Read-Pixel $baselinePath ($SRC_X + $dx) ($SRC_Y + $dy)
            if ($px.R -le 50 -and $px.G -le 55 -and $px.B -le 60) {
                Write-Host ("    Dark dot found at offset ({0},{1}): actual=({2},{3}) R={4} G={5} B={6}" -f $dx, $dy, $SRC_X + $dx, $SRC_Y + $dy, $px.R, $px.G, $px.B)
                $foundDark = $true
            }
        }
    }
    if (-not $foundDark) {
        Write-Host "    WARNING: no dark pixel found near ($SRC_X, $SRC_Y). Adjusting by scanning wider..." -ForegroundColor Yellow
        for ($sy = 600; $sy -le 700; $sy += 2) {
            for ($sx = 640; $sx -le 720; $sx += 2) {
                $px = Read-Pixel $baselinePath $sx $sy
                if ($px.R -le 50 -and $px.G -le 55 -and $px.B -le 60) {
                    Write-Host ("    Wider scan: dark at ({0},{1}) R={2} G={3} B={4}" -f $sx, $sy, $px.R, $px.G, $px.B)
                }
            }
        }
    }

    # === Step 2: hover ===
    Write-Host "`n=== Step 2: hover on connection point ==="
    MoveTo $proc $SRC_X $SRC_Y
    Start-Sleep -Milliseconds 500
    $hoverPath = Join-Path $OutputDir "02-hover.png"
    Save-Screen $hoverPath $proc
    Write-Host "  Pixel readings at offset near dot center (look for orange #FF6A00 = R~255 G~106 B~0):"
    $hoverPx = $null
    $isHoverOrange = $false
    for ($dy = -3; $dy -le 5; $dy += 1) {
        for ($dx = -3; $dx -le 5; $dx += 1) {
            $px = Read-Pixel $hoverPath ($SRC_X + $dx) ($SRC_Y + $dy)
            $isOrange = ($px.R -ge 230 -and $px.G -ge 80 -and $px.G -le 150 -and $px.B -le 60)
            if ($isOrange) {
                Write-Host ("    ORANGE at offset ({0},{1}): actual=({2},{3}) R={4} G={5} B={6}" -f $dx, $dy, $SRC_X + $dx, $SRC_Y + $dy, $px.R, $px.G, $px.B) -ForegroundColor Green
                $isHoverOrange = $true
                $hoverPx = $px
            }
        }
    }
    if ($isHoverOrange) {
        Write-Host "  >>> HOVER STATE DETECTED (orange #FF6A00) — requirement 1 OK" -ForegroundColor Green
    } else {
        Write-Host "  >>> NO ORANGE HOVER PIXEL FOUND near ($SRC_X, $SRC_Y)" -ForegroundColor Yellow
        Write-Host "  Sampling broader area for any orange pixels..."
        for ($sy = 620; $sy -le 680; $sy += 2) {
            for ($sx = 640; $sx -le 720; $sx += 2) {
                $px = Read-Pixel $hoverPath $sx $sy
                if ($px.R -ge 200 -and $px.G -ge 60 -and $px.G -le 160 -and $px.B -le 80) {
                    Write-Host ("    orange-ish at ({0},{1}) R={2} G={3} B={4}" -f $sx, $sy, $px.R, $px.G, $px.B)
                }
            }
        }
    }

    # === Step 3: drag to empty area, capture mid-drag ===
    Write-Host "`n=== Step 3: drag from connection point to empty area ==="
    MoveTo $proc $SRC_X $SRC_Y
    Start-Sleep -Milliseconds 200
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 150
    # Move slowly through a few waypoints so Avalonia's PointerMoved gets each one.
    MoveTo $proc 900 700
    Start-Sleep -Milliseconds 200
    MoveTo $proc $EMPTY_X $EMPTY_Y
    Start-Sleep -Milliseconds 400   # Let preview line render
    $midDragPath = Join-Path $OutputDir "03-drag-empty-mid.png"
    Save-Screen $midDragPath $proc

    # Sample pixels along the expected line path to detect #2459B8 (blue dashed preview).
    Write-Host "  Sampling for blue dashed preview line (#2459B8):"
    $blueFound = $false
    for ($sx = 700; $sx -le 1100; $sx += 20) {
        for ($sy = 620; $sy -le 720; $sy += 20) {
            $px = Read-Pixel $midDragPath $sx $sy
            if ($px.R -le 80 -and $px.G -ge 70 -and $px.G -le 130 -and $px.B -ge 150) {
                Write-Host ("    blue at ({0},{1}) RGB=({2},{3},{4})" -f $sx, $sy, $px.R, $px.G, $px.B)
                $blueFound = $true
            }
        }
    }
    if ($blueFound) {
        Write-Host "  >>> PREVIEW LINE DETECTED — requirement 2 OK (mid-drag)" -ForegroundColor Green
    } else {
        Write-Host "  >>> NO BLUE LINE — preview line not visible" -ForegroundColor Yellow
    }

    # Release in empty area.
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 800

    # === Step 4: after empty release ===
    Write-Host "`n=== Step 4: after empty release ==="
    # Move cursor away to avoid any incidental hover.
    MoveTo $proc 100 100
    Start-Sleep -Milliseconds 400
    $afterEmptyPath = Join-Path $OutputDir "04-after-empty-release.png"
    Save-Screen $afterEmptyPath $proc
    $afterEmptyHash = (Get-FileHash -LiteralPath $afterEmptyPath -Algorithm SHA256).Hash
    $baselineHash   = (Get-FileHash -LiteralPath $baselinePath   -Algorithm SHA256).Hash
    if ($afterEmptyHash -eq $baselineHash) {
        Write-Host "  >>> PIXEL-PERFECT MATCH WITH BASELINE — no new node, no edge (requirement 3 OK)" -ForegroundColor Green
    } else {
        Write-Host "  >>> Image differs from baseline (expected only if a stray render artifact exists)" -ForegroundColor Yellow
        # Spot-check: are there any new edges? Sample several positions.
        Write-Host "  Spot-checking several expected-background pixels for content changes:"
        for ($sx = 200; $sx -le 1200; $sx += 100) {
            for ($sy = 300; $sy -le 750; $sy += 100) {
                $b = Read-Pixel $baselinePath   $sx $sy
                $a = Read-Pixel $afterEmptyPath $sx $sy
                $dr = [math]::Abs($a.R - $b.R); $dg = [math]::Abs($a.G - $b.G); $db = [math]::Abs($a.B - $b.B)
                if ($dr + $dg + $db -gt 30) {
                    Write-Host ("    DIFF at ({0},{1}): baseline=({2},{3},{4}) now=({5},{6},{7})" -f $sx, $sy, $b.R, $b.G, $b.B, $a.R, $a.G, $a.B)
                }
            }
        }
    }

    # === Step 5: drag from connection point to another node ===
    # Only attempt this if the graph has >=2 nodes visible.
    # Look for any second node by scanning the baseline image for white node bodies
    # away from the source node (src is at x=438..732).
    Write-Host "`n=== Step 5: drag to second node (if exists) ==="
    $img = [System.Drawing.Image]::FromFile($baselinePath)
    $bmp = New-Object System.Drawing.Bitmap($img)
    $img.Dispose()
    $foundSecondNode = $false
    $secondCenter = $null
    for ($sy = 100; $sy -le 750; $sy += 20) {
        for ($sx = 100; $sx -le 1300; $sx += 20) {
            # Skip the source node region.
            if ($sx -ge 430 -and $sx -le 740 -and $sy -ge 510 -and $sy -le 690) { continue }
            $px = $bmp.GetPixel($sx, $sy)
            if ($px.R -ge 252 -and $px.G -ge 252 -and $px.B -ge 252) {
                # Found a white area away from source — could be a node.
                $foundSecondNode = $true
                $secondCenter = @{ X = $sx; Y = $sy }
                Write-Host "  Candidate second node white at ($sx, $sy)"
                break
            }
        }
        if ($foundSecondNode) { break }
    }
    $bmp.Dispose()

    if (-not $foundSecondNode) {
        Write-Host "  No second node visible in baseline. Skipping requirement 4 verification (test data has only one node)." -ForegroundColor Yellow
    } else {
        # Drag from source connection point to second node.
        $tgtX = $secondCenter.X
        $tgtY = $secondCenter.Y
        Write-Host "  Dragging from ($SRC_X, $SRC_Y) to ($tgtX, $tgtY)..."
        MoveTo $proc $SRC_X $SRC_Y
        Start-Sleep -Milliseconds 200
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 150
        MoveTo $proc $tgtX $tgtY
        Start-Sleep -Milliseconds 400
        $midDragNodePath = Join-Path $OutputDir "05-drag-node-mid.png"
        Save-Screen $midDragNodePath $proc
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 1000

        MoveTo $proc 100 100
        Start-Sleep -Milliseconds 400
        $afterNodePath = Join-Path $OutputDir "06-after-node-release.png"
        Save-Screen $afterNodePath $proc
        # Look for a new edge by checking for #B8C1CC (gray) or #2459B8 lines that weren't there before.
        $diffFound = $false
        for ($sx = 100; $sx -le 1300; $sx += 10) {
            for ($sy = 100; $sy -le 750; $sy += 10) {
                $b = Read-Pixel $baselinePath   $sx $sy
                $a = Read-Pixel $afterNodePath $sx $sy
                $dr = [math]::Abs($a.R - $b.R); $dg = [math]::Abs($a.G - $b.G); $db = [math]::Abs($a.B - $b.B)
                if ($dr + $dg + $db -gt 30) {
                    $diffFound = $true
                    Write-Host ("    New content at ({0},{1}): baseline=({2},{3},{4}) now=({5},{6},{7})" -f $sx, $sy, $b.R, $b.G, $b.B, $a.R, $a.G, $a.B)
                    if ($sx -gt 200) { break }
                }
            }
            if ($diffFound) { break }
        }
        if ($diffFound) {
            Write-Host "  >>> NEW EDGE DETECTED — requirement 4 OK" -ForegroundColor Green
        } else {
            Write-Host "  >>> NO NEW EDGE — connection not created" -ForegroundColor Yellow
        }
    }

    Write-Host "`n========== SUMMARY =========="
    foreach ($name in @("01-baseline.png","02-hover.png","03-drag-empty-mid.png","04-after-empty-release.png","05-drag-node-mid.png","06-after-node-release.png")) {
        $fp = Join-Path $OutputDir $name
        if (Test-Path $fp) {
            $info = Get-Item $fp
            Write-Host ("  {0,-35} {1,7:N1} KB" -f $name, ($info.Length / 1024.0))
        } else {
            Write-Host "  $name  MISSING" -ForegroundColor Red
        }
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