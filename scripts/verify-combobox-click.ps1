# Verification for the ComboBox-click-doesn't-exit-edit-mode fix.
# Flow per the user's note:
#   1. App auto-selects the first node on load.
#   2. Click the pencil edit button at the lower-left of the node to enter edit mode.
#   3. Click the Plan ComboBox area. Expected: dropdown opens, edit mode persists.
param(
    [string]$ExePath = "E:\Work\Code\Tools\AgentOrchestrator\src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.exe",
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-combobox-click",
    [int]$WaitLaunch = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

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
public struct RECT { public int L; public int T; public int R; public int B; }
'@

$MD = 0x0002
$MU = 0x0004

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
    Start-Sleep -Milliseconds 300
}

$graphId = "2c726922ad624e5ebff80b8f2f96ed84"

Write-Host "=== ComboBox click verification ==="
$proc = Start-Process -FilePath $ExePath -ArgumentList @("--open-graph", $graphId) -PassThru
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
    Start-Sleep -Milliseconds 1500

    # 01: initial state. First node should be auto-selected on load.
    Snap $proc (Join-Path $OutputDir "01-loaded-selected.png")

    # 02: click the pencil edit button at the lower-left of the first node.
    # Looking at the captured screenshot, the pencil icon center sits at
    # roughly (462, 425) — slightly offset from the verify-fixes-v3
    # coords because the window position differs.
    Click $proc 462 425
    Start-Sleep -Milliseconds 1500
    Snap $proc (Join-Path $OutputDir "02-edit-mode-active.png")

    # 03: click the Plan ComboBox area (the kind row, just below the title).
    # In edit mode the node chrome is title(TextBox) / kind(ComboBox) /
    # description(TextBox) / detail(Button). With the node top at y=305
    # and a 20px title row, the kind ComboBox center sits around y=335.
    Click $proc 555 335
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "03-combobox-clicked.png")

    # 04: try clicking closer to the dropdown arrow on the right side.
    Click $proc 615 335
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "04-combobox-clicked-arrow.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

Write-Host "Verification complete. Output: $OutputDir"
