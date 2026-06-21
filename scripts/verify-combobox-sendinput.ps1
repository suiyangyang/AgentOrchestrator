# Verification using SendInput (more reliable than mouse_event) to click
# the pencil edit button then the Plan ComboBox in edit mode.
param(
    [string]$ExePath = "E:\Work\Code\Tools\AgentOrchestrator\src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.exe",
    [string]$OutputDir = "E:\Work\Code\Tools\AgentOrchestrator\artifacts\verify-combobox-sendinput",
    [int]$WaitLaunch = 10
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
[DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int cb);
public struct RECT { public int L; public int T; public int R; public int B; }
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
    public int dx; public int dy; public uint mouseData;
    public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
}
[StructLayout(LayoutKind.Explicit)] public struct INPUT {
    [FieldOffset(0)] public uint type;
    [FieldOffset(8)] public MOUSEINPUT mi;
}
public const uint INPUT_MOUSE = 0;
public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
public const uint MOUSEEVENTF_LEFTUP = 0x0004;
public const uint MOUSEEVENTF_MOVE = 0x0001;
public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
'@

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

function SendClick([System.Diagnostics.Process]$p, [int]$cx, [int]$cy) {
    $p.Refresh()
    $r = New-Object W32.U32+RECT
    [W32.U32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $sx = $r.L + $cx; $sy = $r.T + $cy

    # Normalize to 0..65535 absolute coords for MOUSEEVENTF_ABSOLUTE.
    $screenW = [System.Windows.Forms.SystemInformation]::VirtualScreen.Width
    $screenH = [System.Windows.Forms.SystemInformation]::VirtualScreen.Height
    $absX = [int](($sx * 65536) / $screenW)
    $absY = [int](($sy * 65536) / $screenH)

    # Move cursor to target first (triggers PointerEntered on the target).
    $inputs = New-Object W32.U32+INPUT[] 3
    $inputs[0] = New-Object W32.U32+INPUT
    $inputs[0].type = [W32.U32]::INPUT_MOUSE
    $inputs[0].mi.dx = $absX; $inputs[0].mi.dy = $absY
    $inputs[0].mi.dwFlags = [W32.U32]::MOUSEEVENTF_MOVE -bor [W32.U32]::MOUSEEVENTF_ABSOLUTE
    $inputs[1] = New-Object W32.U32+INPUT
    $inputs[1].type = [W32.U32]::INPUT_MOUSE
    $inputs[1].mi.dwFlags = [W32.U32]::MOUSEEVENTF_LEFTDOWN
    $inputs[2] = New-Object W32.U32+INPUT
    $inputs[2].type = [W32.U32]::INPUT_MOUSE
    $inputs[2].mi.dwFlags = [W32.U32]::MOUSEEVENTF_LEFTUP
    [W32.U32]::SendInput(3, $inputs, 40) | Out-Null
    Start-Sleep -Milliseconds 400
}

$graphId = "2c726922ad624e5ebff80b8f2f96ed84"

Write-Host "=== SendInput verification ==="
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

    Snap $proc (Join-Path $OutputDir "01-loaded-selected.png")

    # Step 1: Click the pencil edit button (lower-left of first node).
    SendClick $proc 460 425
    Start-Sleep -Milliseconds 1500
    Snap $proc (Join-Path $OutputDir "02-edit-mode-active.png")

    # Step 2: Click the Plan ComboBox area.
    SendClick $proc 555 335
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "03-combobox-clicked.png")

    # Step 3: Click closer to the dropdown arrow.
    SendClick $proc 615 335
    Start-Sleep -Milliseconds 800
    Snap $proc (Join-Path $OutputDir "04-combobox-arrow.png")
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(4000)) { $proc.Kill() }
    }
    $proc.Dispose()
}

Write-Host "Verification complete. Output: $OutputDir"
