$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = $null
foreach ($p in @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
  )) {
  if (Test-Path $p) { $csc = $p; break }
}
if (-not $csc) { throw "csc.exe not found (need .NET Framework 4.x)" }
$icon = Join-Path $dir "VDH.ico"
$iconArg = @()
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }
& $csc /nologo /target:winexe /optimize+ /out:"$dir\VDH.exe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
  @iconArg `
  "$dir\VDH.cs" "$dir\VDH.Extra.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Write-Host "VDH.exe" (Get-Item "$dir\VDH.exe").Length "bytes"
