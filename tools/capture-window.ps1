<#
  capture-window.ps1 — 截图某个进程的主窗口，用于人工核对 UI 真的画出来了。
  用法： powershell -NoProfile -ExecutionPolicy Bypass -File capture-window.ps1 -Process VdHelper -Out _shot.png
#>
param(
  [Parameter(Mandatory = $true)][string]$Process,
  [string]$Out = '_shot.png'
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@

$p = Get-Process -Name $Process -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw "no main window for $Process" }

[void][Win]::ShowWindow($p.MainWindowHandle, 9)   # SW_RESTORE
[void][Win]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 700

$r = New-Object Win+RECT
if (-not [Win]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { throw 'GetWindowRect failed' }
$w = $r.R - $r.L; $h = $r.B - $r.T
"window=$($p.MainWindowTitle) rect=$($r.L),$($r.T),${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved=$Out ($((Get-Item $Out).Length) bytes)"