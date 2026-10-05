<#
.SYNOPSIS
  打开 VDHelper 的指定标签页并截一张图。

.DESCRIPTION
  存在的理由：UI 改动必须看到真实界面才算验证过，而「先启动、再等体检跑完、再截图」
  这套流程每次手敲都会漂。WPF 启动后要等几秒让体检落地，直接截会拍到空列表。

  参数 Tab：0 = 本机体检，1 = 串流参数，2 = 头显诊断。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-tab.ps1 -Tab 1
#>
param(
  [int]$Tab = 0,
  [int]$SettleSeconds = 11,
  [string]$Out = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\VdHelper\bin\Release\net10.0-windows\VdHelper.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到 $exe —— 先 dotnet build -c Release" }
if ($Out -eq '') { $Out = Join-Path $root ('_tab' + $Tab + '.png') }

Get-Process -Name VdHelper -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Seconds 2

if ($Tab -eq 0) {
  Start-Process $exe
} else {
  Start-Process $exe -ArgumentList '--tab', $Tab
}

Start-Sleep -Seconds $SettleSeconds
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\capture-window.ps1') -Process VdHelper -Out $Out
Get-Process -Name VdHelper -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }