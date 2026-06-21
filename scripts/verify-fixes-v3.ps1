# Verification: uses --maximize-graph flag to ensure maximize is triggered.
# Tests the 5 UI fixes in TaskGraph workspace.
param(
    [string]$ExePath = "E:\Work\Code\Tools\AgentOrchestrator\src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.exe",
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\fix-verify3",
    [int]$WaitLaunch = 10,
    [int]$WaitAction = 3
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path $ExePath)) { throw "Exe not found: $ExePath" }
if (Test-Path $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Add-Type -Namespace W32 -Name U32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, int e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, int e);
public struct RECT { public int L; public int T; public int R; public int B; }
'@

$MD  = 0x0002
$MU  = 0x0004
$KEYUP = 0x0002

function Snap([System.Diagnostics.Process]$p, [string]$f) {
    $p.Refresh()
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { Write-Warning "No handle"; return }
    $r = New-Object W32.U32+RECT
    [W32.U32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { Write-Warning "Zero size"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [W32.U32]::PrintWindow($p.MainWindowHandle, $dc, 0x02) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved: $f"
}

function Click([System.Diagnostics.Process]$p, [int]$cx, [int]$cy, [int]$hold = 60) {
    $p.Refresh()
    $r = New-Object W32.U32+RECT
    [W32.U32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $sx = $r.L + $cx; $sy = $r.T + $cy
    [W32.U32]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 150
    [W32.U32]::mouse_event($MD, 0, 0, 0, 0)
    Start-Sleep -Milliseconds $hold
    [W32.U32]::mouse_event($MU, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 250
}

function DblClick([System.Diagnostics.Process]$p, [int]$cx, [int]$cy) {
    Click $p $cx $cy 30
    Start-Sleep -Milliseconds 80
    Click $p $cx $cy 30
    Start-Sleep -Milliseconds 400
}

# Use a graph token directly: --open-graph <token> --maximize-graph.
# The 19-node graph's id is "2c726922ad624e5ebff80b8f2f96ed84".
function Launch-App([string[]]$extraArgs) {
    Write-Host "Launching $ExePath $($extraArgs -join ' ')"
    $argsList = @($extraArgs)
    return Start-Process -FilePath $ExePath -ArgumentList $argsList -PassThru -WindowStyle Normal
}

# ============================================================
# Test 1: Normal (non-maximized) view, click and double-click
# ============================================================
Write-Host "=== Test 1: Normal view, click & double-click ==="
$proc = Launch-App @("--open-graph", "2c726922ad624e5ebff80b8f2f96ed84")
try {
    Start-Sleep -Seconds $WaitLaunch
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No window." }
    [W32.U32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [W32.U32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 1200

    Snap $proc (Join-Path $OutputDir "01-loaded-19node.png")

    # Single click on first node
    Click $proc 555 350
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "02-single-click-select-no-edit.png")

    # Double click to enter edit mode
    DblClick $proc 555 350
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "03-double-click-edit-mode.png")

    # Click empty area to deselect
    Click $proc 900 700
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "04-deselected.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

# ============================================================
# Test 2: Maximized view (--maximize-graph) tests Issue 3
# ============================================================
Write-Host "=== Test 2: Maximized view tests Issue 3 ==="
$proc = Launch-App @("--open-graph", "2c726922ad624e5ebff80b8f2f96ed84", "--maximize-graph")
try {
    Start-Sleep -Seconds $WaitLaunch
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No window." }
    [W32.U32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [W32.U32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 1200

    Snap $proc (Join-Path $OutputDir "05-maximized-empty-area.png")

    # Click on a node in the maximized view (nodes should be on the right)
    Click $proc 1000 400
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "06-maximized-node-selected.png")

    # Click empty area on the left
    Click $proc 400 500
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "07-maximized-left-click.png")

    # Click the resize grip on a selected node
    Click $proc 1000 400
    Start-Sleep -Milliseconds 800
    # Drag the resize grip down and to the right
    $p = $proc.MainWindowHandle
    $r = New-Object W32.U32+RECT
    [W32.U32]::GetWindowRect($p, [ref]$r) | Out-Null
    $sx = $r.L + 1180; $sy = $r.T + 520  # approximate grip location
    [W32.U32]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 200
    [W32.U32]::mouse_event($MD, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 100
    [W32.U32]::SetCursorPos($sx + 80, $sy + 30)
    Start-Sleep -Milliseconds 200
    [W32.U32]::mouse_event($MU, 0, 0, 0, 0)
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "08-resize-applied.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

# ============================================================
# Test 3: Test the edit button click and resize grip
# ============================================================
Write-Host "=== Test 3: Edit button + resize grip ==="
$proc = Launch-App @("--open-graph", "2c726922ad624e5ebff80b8f2f96ed84")
try {
    Start-Sleep -Seconds $WaitLaunch
    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No window." }
    [W32.U32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    [W32.U32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 1200

    # Single click on the first node
    Click $proc 555 350
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "09-single-click-shows-pencil-grip.png")

    # Click the pencil edit button in the lower-left of the node
    # The node is at approximately (487, 290), size 220x132.
    # The bottom row is at y=378 to 410. The button is 20x20, centered vertically,
    # so it's at (499, 384) to (519, 404). Center: (509, 394).
    Click $proc 509 394
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "10-edit-button-clicked.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

Write-Host "Verification complete. Output: $OutputDir"
