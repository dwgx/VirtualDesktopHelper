<#
  probe-ports.ps1 — 只读 LAN 探测：谁在线、VD 端口是否在听、发现广播能否到达。
  用途：既是研究工具，也是未来诊断引擎的命令行内核原型。
  用法： powershell -NoProfile -ExecutionPolicy Bypass -File probe-ports.ps1 [-Subnet 192.168.11] [-Ports 38810,38820,38830,38840]
#>
param(
  [string]$Subnet = '',
  [int[]]$Ports = @(38810, 38820, 38830, 38840),
  [int]$TimeoutMs = 800,
  [int]$PingTimeoutMs = 300
)

$ErrorActionPreference = 'Continue'

function Get-PrimaryIPv4 {
  Get-NetIPConfiguration |
    Where-Object { $_.NetAdapter.Status -eq 'Up' -and $_.IPv4Address -and $_.IPv4DefaultGateway } |
    Select-Object -First 1 -ExpandProperty IPv4Address |
    Select-Object -ExpandProperty IPAddress
}

function Test-TcpPort([string]$host_, [int]$port, [int]$ms) {
  $c = New-Object System.Net.Sockets.TcpClient
  try {
    $ar = $c.BeginConnect($host_, $port, $null, $null)
    if (-not $ar.AsyncWaitHandle.WaitOne($ms, $false)) { return $false }
    $c.EndConnect($ar)
    return $true
  } catch { return $false } finally { $c.Close() }
}

$local = Get-PrimaryIPv4
if (-not $local) { Write-Error 'no primary IPv4 with gateway'; exit 2 }
if (-not $Subnet) { $Subnet = ($local -replace '\.\d+$', '') }

Write-Output "LOCAL_IP=$local"
Write-Output "SUBNET=$Subnet.0/24"
Write-Output "PORTS=$($Ports -join ',')"
Write-Output ''

# 1) 本机是否有端口在听（VD Streamer 或服务没起是最常见原因）
Write-Output '## listening-on-host'
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
  Where-Object { $Ports -contains $_.LocalPort } |
  Select-Object LocalAddress, LocalPort, OwningProcess |
  ForEach-Object {
    $p = (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName
    "  $($_.LocalAddress):$($_.LocalPort) pid=$($_.OwningProcess) proc=$p"
  }
Write-Output ''

# 2) 邻居（ARP）+ 端口探测：头显通常在这里被找到
Write-Output '## neighbors'
$neighbors = (arp -a | Select-String '^\s+(\d+\.\d+\.\d+\.\d+)\s+([0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2})' |
  ForEach-Object { [pscustomobject]@{ IP = $_.Matches[0].Groups[1].Value; MAC = $_.Matches[0].Groups[2].Value } } |
  Where-Object { $_.IP -like "$Subnet.*" -and $_.IP -notmatch '\.(0|255)$' -and $_.IP -ne $local } |
  Sort-Object IP -Unique)

foreach ($n in $neighbors) {
  $open = @()
  foreach ($p in $Ports) { if (Test-TcpPort $n.IP $p $TimeoutMs) { $open += $p } }
  $rand = if ($n.MAC -match '^(02|06|0a|0e|12|16|1a|1e|22|26|2a|2e|32|36|3a|3e|42|46|4a|4e|52|56|5a|5e|62|66|6a|6e|72|76|7a|7e|82|86|8a|8e|92|96|9a|9e|a2|a6|aa|ae|b2|b6|ba|be|c2|c6|ca|ce|d2|d6|da|de|e2|e6|ea|ee|f2|f6|fa|fe)-') { 'locally-administered/randomized-MAC' } else { '' }
  "  $($n.IP)  $($n.MAC)  open=[$($open -join ',')] $rand"
}
Write-Output ''
Write-Output '## done'