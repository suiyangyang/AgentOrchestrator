# Verification: captures screenshots for the 5 UI fixes in TaskGraph.
# Uses correct coordinates based on the 1314x808 window layout.
param(
    [string]$ExePath = "E:\Work\Code\Tools\AgentOrchestrator\src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.exe",
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\fix-verify2",
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

function Click([System.Diagnostics.Process]$p, [int]$cx, [int]$cy, [int]$hold = 50) {
    $p.Refresh()
    $r = New-Object W32.U32+RECT
    [W32.U32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $sx = $r.L + $cx; $sy = $r.T + $cy
    [W32.U32]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 100
    [W32.U32]::mouse_event($MD, 0, 0, 0, 0)
    Start-Sleep -Milliseconds $hold
    [W32.U32]::mouse_event($MU, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 200
}

function DblClick([System.Diagnostics.Process]$p, [int]$cx, [int]$cy) {
    Click $p $cx $cy 30
    Start-Sleep -Milliseconds 100
    Click $p $cx $cy 30
    Start-Sleep -Milliseconds 300
}

Write-Host "Launching $ExePath..."
$proc = Start-Process -FilePath $ExePath -PassThru -WindowStyle Normal
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
    Start-Sleep -Milliseconds 800

    # 1. Open the "Bug 列表处理" graph (19 nodes, matches Image 2).
    # Sidebar layout: y=240 is the first row in the graph list.
    Click $proc 140 240
    Start-Sleep -Seconds $WaitAction
    Snap $proc (Join-Path $OutputDir "01-buggraph-loaded.png")

    # 2. Click on a node to test single-click selection (Issue 2).
    # The 19-node graph layout has nodes starting around x=480 and going right.
    # The first node should be near the top-left of the canvas area.
    # Click a node area to select it. Should show selection chrome but NOT edit inputs.
    Click $proc 555 350
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "02-single-click-select.png")

    # 3. Double-click the same node to enter edit mode (Issue 2 - double-click).
    DblClick $proc 555 350
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "03-double-click-edit-mode.png")

    # 4. Click empty space to deselect.
    Click $proc 900 650
    Start-Sleep -Milliseconds 800

    # 5. Single-click on a node, then click the new edit button (Issue 2 - edit button).
    Click $proc 555 350
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "04-selected-shows-pencil-and-grip.png")
    # The pencil edit button is at the lower-left of the node. For a 220x132
    # node starting at (487, 290), the button is at approximately
    # (487 + 12 + 4, 290 + 132 - 12 - 4) = (503, 406). Use coordinates that
    # target the lower-left area of the selected node.
    Click $proc 505 405
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "05-edit-button-clicked.png")

    # 6. Click empty area to deselect.
    Click $proc 900 650
    Start-Sleep -Milliseconds 800

    # 7. Click the resize grip (Issue 4, Issue 5).
    # The grip is at the bottom-right of the selected node. For a node at
    # (487, 290) with size 220x132, the grip is at approximately
    # (487 + 220 - 18, 290 + 132 - 18) = (689, 404).
    # First select the node again.
    Click $proc 555 350
    Start-Sleep -Milliseconds 600
    Snap $proc (Join-Path $OutputDir "06-selected-grip-visible.png")

    # 8. Click the maximize button (Issue 3).
    # The fullscreen button is in the graph editor's top-right toolbar.
    # At column 8 of a 9-column grid, the rightmost position.
    # The toolbar is at y=213 based on the previous screenshots.
    # The fullscreen icon is at approximately x=1015, y=213.
    Click $proc 1015 213
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "07-graph-maximized.png")

    # 9. Click a node in the maximized view (nodes should be on the right).
    Click $proc 1100 400
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "08-maximized-node-selected.png")

    # 10. Click empty area on the LEFT in maximized view (Issue 3 - empty area).
    Click $proc 500 500
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "09-maximized-empty-area-clicked.png")

    # 11. Click maximize again to exit.
    Click $proc 1015 213
    Start-Sleep -Seconds 2
    Snap $proc (Join-Path $OutputDir "10-exit-maximize.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

Write-Host "Verification complete. Output: $OutputDir"
