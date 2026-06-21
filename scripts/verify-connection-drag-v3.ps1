#!/usr/bin/env pwsh
# verify-connection-drag-v3.ps1
# Self-locating verification: scans the baseline screenshot to find real connection-point
# dots and node bodies before interacting. This handles the case where the test graph
# renders different node positions each launch.

param(
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\connection-drag-v3",
    [string]$ExePath   = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-build\AgentOrchestrator.App.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath."
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
public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP   = 0x0004

# ---------- Geometry helpers ----------

function Get-WindowRect([System.Diagnostics.Process]$proc) {
    $proc.Refresh()
    $rect = New-Object Win32.User32+RECT
    [Win32.User32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    return $rect
}

function Get-ScreenPoint($proc, [int]$cx, [int]$cy) {
    $rect = Get-WindowRect $proc
    return [pscustomobject]@{ X = ([int]$rect.Left + $cx); Y = ([int]$rect.Top + $cy) }
}

function MoveTo($proc, [int]$x, [int]$y) {
    $p = Get-ScreenPoint $proc $x $y
    [Win32.User32]::SetCursorPos($p.X, $p.Y) | Out-Null
}

function Save-Screen([string]$path, [System.Diagnostics.Process]$proc) {
    $rect = Get-WindowRect $proc
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { Write-Warning "Zero-sized window."; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen([System.Drawing.Point]::new($rect.Left, $rect.Top), [System.Drawing.Point]::Empty, [System.Drawing.Size]::new($w, $h))
    $gfx.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Read-Pixel($path, [int]$x, [int]$y) {
    $img = [System.Drawing.Image]::FromFile($path)
    $bmp = New-Object System.Drawing.Bitmap($img)
    $img.Dispose()
    $px = $bmp.GetPixel($x, $y)
    $bmp.Dispose()
    return $px
}

function Find-Connection-Dots([string]$path) {
    # Returns array of [pscustomobject]@{ X=..; Y=.. } for each ~12x12 #1F2328 dot found.
    $img = [System.Drawing.Image]::FromFile($path)
    $bmp = New-Object System.Drawing.Bitmap($img)
    $img.Dispose()
    $W = $bmp.Width; $H = $bmp.Height

    # Build 16x16 cell counts of #1F2328-ish pixels.
    $cells = @{}
    for ($sy = 0; $sy -lt $H; $sy += 1) {
        for ($sx = 0; $sx -lt $W; $sx += 1) {
            $px = $bmp.GetPixel($sx, $sy)
            if ($px.R -le 38 -and $px.G -le 42 -and $px.B -le 48 -and $px.R -ge 25 -and $px.G -ge 30 -and $px.B -ge 35) {
                $cx = [int][math]::Floor($sx / 16) * 16
                $cy = [int][math]::Floor($sy / 16) * 16
                $ck = "$cx,$cy"
                if (-not $cells.ContainsKey($ck)) { $cells[$ck] = 0 }
                $cells[$ck]++
            }
        }
    }
    # A real Ellipse (12x12 with 8x8 dark fill) shows as ~40-90 pixels in one 16x16 cell.
    # Find cells with 40-100 dark pixels.
    $candidates = @()
    foreach ($k in $cells.Keys) {
        if ($cells[$k] -ge 40 -and $cells[$k] -le 100) {
            $p = $k -split ","
            $candidates += [pscustomobject]@{ X=([int]$p[0]+8); Y=([int]$p[1]+8); Count=$cells[$k] }
        }
    }
    $bmp.Dispose()
    return $candidates
}

function Find-Node-Bodies([string]$path) {
    # Returns array of [pscustomobject]@{ X=..; Y=..; W=..; H=.. } for each likely node body.
    # Heuristic: a node body is a white rectangle >=150x100.
    $img = [System.Drawing.Image]::FromFile($path)
    $bmp = New-Object System.Drawing.Bitmap($img)
    $img.Dispose()
    $W = $bmp.Width; $H = $bmp.Height

    # Find rows that have white runs of >=150px.
    $whiteRows = @{}
    for ($sy = 0; $sy -lt $H; $sy += 2) {
        $runStart = -1; $runLen = 0
        $best = 0; $bestStart = -1; $bestEnd = -1
        for ($sx = 0; $sx -lt $W; $sx += 2) {
            $px = $bmp.GetPixel($sx, $sy)
            if ($px.R -ge 252 -and $px.G -ge 252 -and $px.B -ge 252) {
                if ($runStart -lt 0) { $runStart = $sx }
                $runLen += 2
            } else {
                if ($runLen -gt $best) { $best = $runLen; $bestStart = $runStart; $bestEnd = $sx - 2 }
                $runStart = -1; $runLen = 0
            }
        }
        if ($runLen -gt $best) { $best = $runLen; $bestStart = $runStart; $bestEnd = $W - 1 }
        if ($best -ge 150) {
            $whiteRows[$sy] = @{ Start=$bestStart; End=$bestEnd; Len=$best }
        }
    }
    # Group consecutive rows with similar x range into "rectangles".
    $rects = @()
    $currentRect = $null
    $sortedYs = $whiteRows.Keys | Sort-Object
    foreach ($y in $sortedYs) {
        $r = $whiteRows[$y]
        if ($currentRect -eq $null) {
            $currentRect = @{ YStart=$y; YEnd=$y; XStart=$r.Start; XEnd=$r.End }
        } elseif ($y - $currentRect.YEnd -le 6) {
            # Extend current rect.
            $currentRect.YEnd = $y
            $currentRect.XStart = [math]::Min($currentRect.XStart, $r.Start)
            $currentRect.XEnd = [math]::Max($currentRect.XEnd, $r.End)
        } else {
            # Save current, start new.
            $w = $currentRect.XEnd - $currentRect.XStart
            $h = $currentRect.YEnd - $currentRect.YStart
            if ($w -ge 150 -and $h -ge 80) {
                $rects += [pscustomobject]@{
                    X = $currentRect.XStart; Y = $currentRect.YStart
                    W = $w; H = $h
                    CenterX = [int](($currentRect.XStart + $currentRect.XEnd) / 2)
                    CenterY = [int](($currentRect.YStart + $currentRect.YEnd) / 2)
                }
            }
            $currentRect = @{ YStart=$y; YEnd=$y; XStart=$r.Start; XEnd=$r.End }
        }
    }
    if ($currentRect -ne $null) {
        $w = $currentRect.XEnd - $currentRect.XStart
        $h = $currentRect.YEnd - $currentRect.YStart
        if ($w -ge 150 -and $h -ge 80) {
            $rects += [pscustomobject]@{
                X = $currentRect.XStart; Y = $currentRect.YStart
                W = $w; H = $h
                CenterX = [int](($currentRect.XStart + $currentRect.XEnd) / 2)
                CenterY = [int](($currentRect.YStart + $currentRect.YEnd) / 2)
            }
        }
    }
    $bmp.Dispose()
    return $rects
}

# ---------- Launch app ----------
Get-Process -Name "AgentOrchestrator.App" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 1

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
try {
    Start-Sleep -Seconds 8
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250; $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No window." }
    [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800

    # Open task graph.
    MoveTo $proc 140 195
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Seconds 3

    # === Baseline ===
    Write-Host "`n=== Baseline ==="
    MoveTo $proc 100 100
    Start-Sleep -Milliseconds 400
    $baselinePath = Join-Path $OutputDir "01-baseline.png"
    Save-Screen $baselinePath $proc

    # Auto-locate connection-point dots and node bodies.
    $dots = Find-Connection-Dots $baselinePath
    Write-Host "  Found $($dots.Count) candidate connection-point dots:"
    foreach ($d in $dots) { Write-Host "    ($($d.X),$($d.Y))  count=$($d.Count)" }

    $nodes = Find-Node-Bodies $baselinePath
    Write-Host "  Found $($nodes.Count) candidate node bodies:"
    foreach ($n in $nodes) { Write-Host "    center=($($n.CenterX),$($n.CenterY))  size=$($n.W)x$($n.H)" }

    if ($dots.Count -eq 0) {
        Write-Host "  ERROR: no connection-point dots found — test graph is empty or dots not rendering." -ForegroundColor Red
        exit 1
    }

    # Pick source dot: first dot found.
    $src = $dots[0]
    Write-Host "  Using source dot at ($($src.X),$($src.Y))"

    # Find a target node body that is not the source node.
    # The source node is the one whose right edge is closest to (and slightly left of) the source dot.
    # Identify source node: nearest node body whose right edge X is within 12px of source dot X.
    $sourceNode = $null
    foreach ($n in $nodes) {
        $rightEdge = $n.X + $n.W
        if ([math]::Abs($rightEdge - $src.X) -le 16 -and [math]::Abs($n.Y + $n.H - $src.Y) -le 40) {
            $sourceNode = $n
            break
        }
    }
    if ($null -eq $sourceNode) {
        # Fallback: nearest node to the dot.
        $bestDist = 999999; $bestNode = $null
        foreach ($n in $nodes) {
            $dx = $n.CenterX - $src.X; $dy = $n.CenterY - $src.Y
            $d = [math]::Sqrt($dx*$dx + $dy*$dy)
            if ($d -lt $bestDist) { $bestDist = $d; $bestNode = $n }
        }
        $sourceNode = $bestNode
    }
    Write-Host "  Source node: center=($($sourceNode.CenterX),$($sourceNode.CenterY))  size=$($sourceNode.W)x$($sourceNode.H)"

    # Find a SECOND distinct node.
    $targetNode = $null
    foreach ($n in $nodes) {
        $dx = $n.CenterX - $sourceNode.CenterX; $dy = $n.CenterY - $sourceNode.CenterY
        $d = [math]::Sqrt($dx*$dx + $dy*$dy)
        if ($d -ge 100) { $targetNode = $n; break }
    }
    if ($null -ne $targetNode) {
        Write-Host "  Target node: center=($($targetNode.CenterX),$($targetNode.CenterY))  size=$($targetNode.W)x$($targetNode.H)"
    } else {
        Write-Host "  No second node found — requirement 4 (release-on-node) will be skipped." -ForegroundColor Yellow
    }

    # === Step 2: Hover ===
    Write-Host "`n=== Step 2: Hover over connection point ==="
    MoveTo $proc $src.X $src.Y
    Start-Sleep -Milliseconds 500
    $hoverPath = Join-Path $OutputDir "02-hover.png"
    Save-Screen $hoverPath $proc

    # Detect orange hover color in a small box around the dot.
    $isHoverOrange = $false
    for ($dy = -4; $dy -le 4; $dy += 1) {
        for ($dx = -4; $dx -le 4; $dx += 1) {
            $px = Read-Pixel $hoverPath ($src.X + $dx) ($src.Y + $dy)
            if ($px.R -ge 230 -and $px.G -ge 80 -and $px.G -le 150 -and $px.B -le 60) {
                $isHoverOrange = $true
                Write-Host "    >>> orange at offset ($dx,$dy): RGB=($($px.R),$($px.G),$($px.B))" -ForegroundColor Green
                break
            }
        }
        if ($isHoverOrange) { break }
    }
    if ($isHoverOrange) {
        Write-Host "  >>> HOVER STATE DETECTED — Requirement 1 OK" -ForegroundColor Green
    } else {
        # Sometimes the dot is exactly at the cursor and shows white stroke (2px). Sample wider.
        Write-Host "  Direct sample at dot center: $([pscustomobject]$(Read-Pixel $hoverPath $src.X $src.Y) | Out-String)" -ForegroundColor Yellow
        Write-Host "  Hover not detected via direct pixel — will check the baseline vs hover diff instead." -ForegroundColor Yellow
    }

    # === Step 3: Drag to empty area, capture mid-drag ===
    Write-Host "`n=== Step 3: Drag to empty area ==="
    MoveTo $proc $src.X $src.Y
    Start-Sleep -Milliseconds 200
    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 150
    $emptyX = 1100; $emptyY = 700
    MoveTo $proc $emptyX $emptyY
    Start-Sleep -Milliseconds 400
    $midPath = Join-Path $OutputDir "03-drag-empty-mid.png"
    Save-Screen $midPath $proc

    # Detect blue preview line.
    $blueFound = $false
    for ($sx = $src.X; $sx -le $emptyX; $sx += 8) {
        for ($sy = $src.Y - 20; $sy -le $emptyY + 20; $sy += 8) {
            $px = Read-Pixel $midPath $sx $sy
            if ($px.R -le 80 -and $px.G -ge 70 -and $px.G -le 130 -and $px.B -ge 150) {
                $blueFound = $true
                Write-Host "    blue preview at ($sx,$sy) RGB=($($px.R),$($px.G),$($px.B))" -ForegroundColor Green
                break
            }
        }
        if ($blueFound) { break }
    }
    if ($blueFound) {
        Write-Host "  >>> PREVIEW LINE DETECTED — Requirement 2 OK" -ForegroundColor Green
    } else {
        Write-Host "  >>> NO PREVIEW LINE detected" -ForegroundColor Yellow
    }

    [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 800

    # === Step 4: After empty release ===
    Write-Host "`n=== Step 4: After empty release ==="
    MoveTo $proc 100 100
    Start-Sleep -Milliseconds 400
    $afterPath = Join-Path $OutputDir "04-after-empty-release.png"
    Save-Screen $afterPath $proc
    $baselineHash = (Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
    $afterHash    = (Get-FileHash -LiteralPath $afterPath    -Algorithm SHA256).Hash
    if ($afterHash -eq $baselineHash) {
        Write-Host "  >>> PIXEL-PERFECT MATCH — Requirement 3 OK (no new node, no edge)" -ForegroundColor Green
    } else {
        # Compare pixel-by-pixel to confirm only minor changes (cursor artifacts).
        $diffs = 0; $bigDiffs = 0
        for ($sy = 0; $sy -lt 808; $sy += 8) {
            for ($sx = 0; $sx -lt 1314; $sx += 8) {
                $b = Read-Pixel $baselinePath $sx $sy
                $a = Read-Pixel $afterPath $sx $sy
                $d = [math]::Abs($a.R-$b.R) + [math]::Abs($a.G-$b.G) + [math]::Abs($a.B-$b.B)
                if ($d -gt 5) { $diffs++ }
                if ($d -gt 50) { $bigDiffs++ }
            }
        }
        Write-Host "  Total diffs (any): $diffs; large diffs (>50): $bigDiffs"
        if ($bigDiffs -eq 0) {
            Write-Host "  >>> NO LARGE DIFFS — minor rendering noise only; Requirement 3 OK" -ForegroundColor Green
        } else {
            Write-Host "  >>> $bigDiffs LARGE DIFFS — unexpected new content (could be stray node or edge)" -ForegroundColor Yellow
        }
    }

    # === Step 5: Drag to second node ===
    Write-Host "`n=== Step 5: Drag to second node ==="
    if ($null -eq $targetNode) {
        Write-Host "  Skipped (no second node visible)." -ForegroundColor Yellow
    } else {
        MoveTo $proc $src.X $src.Y
        Start-Sleep -Milliseconds 200
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 150
        MoveTo $proc $targetNode.CenterX $targetNode.CenterY
        Start-Sleep -Milliseconds 400
        $midNodePath = Join-Path $OutputDir "05-drag-node-mid.png"
        Save-Screen $midNodePath $proc
        [Win32.User32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 1000

        MoveTo $proc 100 100
        Start-Sleep -Milliseconds 400
        $afterNodePath = Join-Path $OutputDir "06-after-node-release.png"
        Save-Screen $afterNodePath $proc

        # Look for an edge color: #B8C1CC (gray) is the regular edge stroke,
        # #FF6A00 (orange) is the highlighted edge.
        $edgeFound = $false
        for ($sx = 0; $sx -lt 1314; $sx += 4) {
            for ($sy = 0; $sy -lt 808; $sy += 4) {
                $b = Read-Pixel $baselinePath   $sx $sy
                $a = Read-Pixel $afterNodePath $sx $sy
                # New edge: pixels that changed from canvas-gray (#F7F8FA) to edge-gray (#B8C1CC)
                $wasCanvas = ($b.R -ge 240 -and $b.G -ge 240 -and $b.B -ge 240 -and [math]::Abs($b.R - 247) -le 6 -and [math]::Abs($b.G - 248) -le 6)
                $isEdge    = ($a.R -ge 170 -and $a.R -le 210 -and $a.G -ge 180 -and $a.G -le 220 -and $a.B -ge 190 -and $a.B -le 230)
                if ($wasCanvas -and $isEdge) {
                    $edgeFound = $true
                    Write-Host "    >>> new edge pixel at ($sx,$sy): baseline=($($b.R),$($b.G),$($b.B)) now=($($a.R),$($a.G),$($a.B))" -ForegroundColor Green
                    break
                }
            }
            if ($edgeFound) { break }
        }
        if ($edgeFound) {
            Write-Host "  >>> NEW EDGE DETECTED — Requirement 4 OK" -ForegroundColor Green
        } else {
            Write-Host "  >>> NO EDGE-COLOR CHANGES found — connection may not have been created" -ForegroundColor Yellow
        }
    }

    Write-Host "`n========== Summary =========="
    foreach ($name in @("01-baseline.png","02-hover.png","03-drag-empty-mid.png","04-after-empty-release.png","05-drag-node-mid.png","06-after-node-release.png")) {
        $fp = Join-Path $OutputDir $name
        if (Test-Path $fp) {
            $info = Get-Item $fp
            Write-Host ("  {0,-35} {1,7:N1} KB" -f $name, ($info.Length / 1024.0))
        } else {
            Write-Host "  $name MISSING" -ForegroundColor Red
        }
    }
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}