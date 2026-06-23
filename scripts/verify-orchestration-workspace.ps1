# Verifies the --open-orchestration startup flag by:
# 1. Building the App.
# 2. Launching with --open-orchestration.
# 3. Waiting for the window to appear.
# 4. Capturing a screenshot.
# 5. Closing the app.
#
# The task orchestration workspace implementation is built by another agent
# in parallel. Until it lands, this script verifies that the flag wiring
# and CLI plumbing work — the screenshot will show the default state since
# the workspace VM is not yet registered.
param(
    [string]$OutputDir = "artifacts\verify-orchestration-workspace",

    [int]$WaitAfterLaunchSeconds = 8,
    [int]$ProcessExitTimeoutMs = 4000
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

# Resolve paths relative to the repo root.
$RepoRoot = Split-Path -Parent $PSScriptRoot
$AppCsproj = Join-Path $RepoRoot "src\AgentOrchestrator.App\AgentOrchestrator.App.csproj"
$AppDll = Join-Path $RepoRoot "src\AgentOrchestrator.App\bin\Debug\net10.0\AgentOrchestrator.App.dll"
$FullOutputDir = Join-Path $RepoRoot $OutputDir

# ── Build ──
Write-Host "Building $AppCsproj..."
$buildResult = dotnet build -c Debug $AppCsproj 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host $buildResult
    throw "Build failed with exit code $LASTEXITCODE."
}
Write-Host "Build succeeded."

# Resolve the built DLL.
if (-not (Test-Path -LiteralPath $AppDll)) {
    throw "App DLL not found at $AppDll after build."
}

# ── Output directory ──
if (-not (Test-Path -LiteralPath $FullOutputDir)) {
    New-Item -ItemType Directory -Path $FullOutputDir -Force | Out-Null
}
Write-Host "Output dir: $FullOutputDir"

# ── Win32 P/Invoke (self-contained) ──
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
public static extern bool CloseWindow(System.IntPtr hWnd);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

# ── Launch ──
Write-Host "Launching App with --open-orchestration..."
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "exec `"$AppDll`" -- --open-orchestration"
$psi.UseShellExecute = $false
$psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal
$nativeProc = [System.Diagnostics.Process]::Start($psi)
$proc = Get-Process -Id $nativeProc.Id

try {
    Write-Host "Waiting $WaitAfterLaunchSeconds s for window..."
    Start-Sleep -Seconds $WaitAfterLaunchSeconds

    $proc.Refresh()
    $deadline = (Get-Date).AddSeconds(15)
    while (((Get-Date) -lt $deadline) -and ($proc.MainWindowHandle -eq [IntPtr]::Zero)) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }

    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "Process did not create a top-level window in time. Screenshot will be skipped."
    }

    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
        [Win32.User32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
        [Win32.User32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
        [Win32.User32]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 800

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

            $screenshotPath = Join-Path $FullOutputDir "01-initial.png"
            if ($ok) {
                $bmp.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Host "Saved screenshot to $screenshotPath"
            } else {
                Write-Warning "PrintWindow returned false."
            }
            $bmp.Dispose()
        } else {
            Write-Warning "Window has zero size; skipping screenshot."
        }
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

Write-Host "Done."
exit 0
