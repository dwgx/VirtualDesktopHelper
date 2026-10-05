<#
.SYNOPSIS
    被动抓 60 秒，看头显到底有没有把发现包发出来。

.DESCRIPTION
    这是整个项目唯一还开着的那个问题的实测入口：

      补丁基线到底还会不会广播到 255.255.255.255:38850？

    静态链说不会（UserSettings.cs:1509-1520 → NetworkManager.cs:637），但 Owner 的
    HANDOFF.md 一手经验说能自动发现并串流。两者只能靠实测分。这一条分出来之前，工具
    不对「同网段连不上」下网络结论。

    纯被动：只抓不发。之前试过向 127.0.0.1:38850 打 17 字节探测包，那个包在协议里是连接前的
    单播预告包，PC 侧命中该分支会关闭本次运行的 38850 监听（-.112.cs:354），所以那条路已经
    撤回，见 notes/2026-10-05-rejected-loopback-probe.md。

    不覆盖别人的抓包：pktmon 全局只能有一个会话，本脚本 start 前会检查。

.PARAMETER Seconds
    抓包时长，默认 60 秒。期间请在头显里点一次搜索。

.PARAMETER OutDir
    ETL 与 txt 的输出目录，默认 .\capture。

.PARAMETER Keep
    保留原始 ETL（默认转换完就删）。

.PARAMETER Analyze
    跳过抓包，直接判读已有的 .txt（pktmon etl2txt 的输出）或 .etl。
    抓包与判读分开，是为了让判读这一段能被单独验证——它才是容易写错的地方，
    而它不需要头显、不需要抓包窗口、不需要管理员。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-discovery.ps1
    然后在 60 秒内点一次头显里的搜索。

.NOTES
    抓包需要管理员权限；-Analyze 不需要。
    语法按本机 pktmon help 核对过：start 用 --capture --comp nics --pkt-size 0 --file-name；
    转换是 pktmon etl2txt 子命令（不是独立可执行文件）。
#>
param(
    [int]$Seconds = 60,
    [string]$OutDir = "capture",
    [switch]$Keep,
    [string]$Analyze = ''
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object System.Security.Principal.WindowsPrincipal($id)).IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Reads a pktmon etl2txt dump and reports what it can and cannot prove. Only 38850 is inspected:
# the port proves a packet existed, not what was inside it, and this function must not imply more.
function Show-Findings([string]$path) {
    $lines = @(Get-Content -LiteralPath $path)
    Write-Host ''

    # Judged on every line in the file, so a pktmon header that prints the capture filter
    # -- "# Filter: --port-range 38850-38860" -- counted as two sightings, and this then said it
    # had seen the PC's presence broadcast. A file containing nothing but that header produced
    # "看到 2 行目的/来源端口 38860 —— 那是 PC 侧的「我在」广播". That manufactures the answer to the
    # one question the capture exists to ask.
    #
    # Only records count. etl2txt prefixes each captured packet with a [nn] timestamp; comments
    # and the banner do not have one.
    # [未验证] the exact etl2txt text has never been seen here -- no capture has been taken,
    # because pktmon needs elevation -- so this rule is checked against the header and empty
    # cases below, not against a real dump.
    $records = @($lines | Where-Object { $_ -match '^\s*\[\d+\]' })
    $skipped = $lines.Count - $records.Count

    # One captured packet is several consecutive lines sharing a [nn] index -- one per protocol
    # layer. Judging each line on its own breaks twice over: a port on the UDP line while the
    # address is on the IPv4 line meant the broadcast test could never fire. Verified on a sample
    # shaped like this one: it counted the 38850 packet and still reported 广播 0.
    #
    # So the unit is the record block, not the line. [未验证] against real etl2txt output -- that
    # still needs the elevated capture.
    $blocks = New-Object System.Collections.Generic.List[object]
    $current = $null
    $currentIdx = ''
    foreach ($l in $records) {
        $idx = ([regex]::Match($l, '^\s*\[(\d+)\]')).Groups[1].Value
        if ($null -eq $current -or $idx -ne $currentIdx) {
            $current = New-Object System.Collections.Generic.List[string]
            $blocks.Add($current)
            $currentIdx = $idx
        }
        $current.Add($l)
    }

    $presence = @($blocks | Where-Object { ($_ -join ' ') -match '\b38860\b' })
    $hits = @($blocks | Where-Object { ($_ -join ' ') -match '\b38850\b' })
    $broadcast = @($hits | Where-Object { ($_ -join ' ') -match '255\.255\.255\.255' })

    Write-Host ("共 {0} 行，其中 {1} 个抓包记录，来自 {2}" -f $lines.Count, $records.Count, $path) -ForegroundColor Cyan


    if ($records.Count -eq 0) {
        Write-Host ''
        Write-Host '这份文件里没有任何抓包记录行（etl2txt 的记录行以 [nn] 开头）。' -ForegroundColor Yellow
        Write-Host '所以这次什么都没测到——不是「没广播」。也可能是这份文件根本不是 etl2txt 的输出。' -ForegroundColor Yellow
        if ($skipped -gt 0) {
            Write-Host ("（{0} 行是注释或抬头，已跳过；只有 [nn] 开头的记录行算数。）" -f $skipped)
        }
        return
    }
    if ($skipped -gt 0) {
        Write-Host ("（另有 {0} 行是注释或抬头，未计入。）" -f $skipped)
    }
    if ($presence.Count -gt 0) {
        Write-Host ("另外看到 {0} 个包的端口是 38860 —— 那是 PC 侧的" -f $presence.Count) -ForegroundColor Cyan
        Write-Host '「我在」广播：Streamer 启动后会往 255.255.255.255:38860 发空包（0 字节载荷）。'
        Write-Host '看到它就说明 PC 这一侧在广播；它和 38850（发现/配对协议）是两回事。'
    }
    if ($hits.Count -eq 0) {
        Write-Host "这份抓包里没有一个提到端口 38850 的包。" -ForegroundColor Yellow
        Write-Host '读法：这段时间没有相关流量，或流量没到这块网卡。不要据此判「网络坏了」。'
        Write-Host '下一步：确认抓包窗口内确实点过搜索；再抓一次作对照（头显断开 Wi-Fi 那一档）。'
        return
    }

    $sent = @($hits | Where-Object { $_ -match 'UdpSend|Sending|\bout\b' })
    $recv = @($hits | Where-Object { $_ -match 'UdpRecv|Receiving|\bin\b' })

    Write-Host ("命中 {0} 行（发出 {1} / 收到 {2}）" -f $hits.Count, $sent.Count, $recv.Count) -ForegroundColor Cyan
    if ($broadcast.Count -gt 0) {
        Write-Host ("其中目的地址是广播的有 {0} 个包 —— 发现包就是广播，这一段有实据。" -f $broadcast.Count) -ForegroundColor Green
    }
    else {
        Write-Host '没有目的地址为广播的行。发现包应当发往 255.255.255.255:38850。' -ForegroundColor Yellow
        Write-Host '只看到端口不足以说「头显广播了」；要确认是发现包，看目的地址是不是广播。'
    }
    Write-Host ''
    Write-Host '前 25 行原文（贴进 issue 时带上这一段）：'
    $hits | Select-Object -First 25 | ForEach-Object { Write-Host "    $_" }
}

# ---- analysis-only path: no admin, no headset, no capture window ----
if ($Analyze -ne '') {
    $src = $Analyze
    if ($src.EndsWith('.etl')) {
        $conv = Join-Path $OutDir 'discovery.txt'
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        & pktmon etl2txt $src --out $conv | Out-Null
        if (-not (Test-Path -LiteralPath $conv)) {
            Write-Host "转换失败：$src" -ForegroundColor Red
            exit 2
        }
        $src = $conv
    }
    Show-Findings $src
    exit 0
}

# ---- capture path ----
# Prerequisite, and it is the one people skip: discovery only happens while the Streamer is up.
# Capturing with it stopped produces a clean capture that proves nothing, and reading that as
# "the headset sent nothing" would be exactly backwards.
$streamer = @(Get-Process -Name 'VirtualDesktop.Streamer' -ErrorAction SilentlyContinue)
if ($streamer.Count -eq 0) {
    Write-Host 'Virtual Desktop Streamer 现在没有在跑。先把它开起来再抓。' -ForegroundColor Yellow
    Write-Host '原因：发现包是 PC 在回。Streamer 没跑就没有 38850 监听，'
    Write-Host '      那种情况下抓到的空白只能说明"没人在听"，不能说明"头显没发"。'
    Write-Host ''
    Write-Host '开起来之后，先确认顺序：先开 PC 上的 Streamer，再在头显里点搜索。'
    Write-Host '启动它（本工具不会替你做这一件事，它会断开现有串流）：'
    Write-Host '  & "$env:ProgramFiles\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe"'
    exit 1
}
Write-Host ("前置检查通过：Streamer 进程在跑（{0} 个）。" -f $streamer.Count) -ForegroundColor DarkGray

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$etl = Join-Path $OutDir 'discovery.etl'
$txt = Join-Path $OutDir 'discovery.txt'


if (-not (Test-Admin)) {
    Write-Host '需要管理员权限：pktmon 抓的是原始包，普通权限起不来。' -ForegroundColor Red
    Write-Host '请用管理员身份重开 PowerShell（开始菜单 -> PowerShell -> 以管理员身份运行）再跑。'
    Write-Host '如果只是要判读一份已有的抓包，加 -Analyze <文件>，那条路不需要管理员。'
    exit 1
}


$status = & pktmon status 2>&1 | Out-String
if ($status -match 'Running') {
    Write-Host 'pktmon 已经在抓了。先确认这是谁的会话：' -ForegroundColor Yellow
    Write-Host $status
    Write-Host '确认无关后执行： pktmon stop'
    exit 1
}

Write-Host "抓包 $Seconds 秒。请在倒计时内，于头显里点一次搜索。" -ForegroundColor Cyan
Write-Host '顺序有讲究：先开 PC 上的 Streamer，再点搜索（先后反了最容易搜不到）。'

& pktmon start --capture --pkt-size 0 --comp nics --file-name $etl | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "pktmon start 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
    exit 2
}

try {
    for ($i = $Seconds; $i -gt 0; $i--) {
        Write-Host ("`r  剩余 {0,3} 秒" -f $i) -NoNewline
        Start-Sleep -Seconds 1
    }
    Write-Host "`r  抓包结束，正在停止…"
}
finally {
    & pktmon stop | Out-Null
}

if (-not (Test-Path -LiteralPath $etl)) {
    Write-Host '没有生成 ETL 文件，pktmon 可能被别的策略挡住了。' -ForegroundColor Red
    exit 2
}

& pktmon etl2txt $etl --out $txt | Out-Null
if (-not (Test-Path -LiteralPath $txt)) {
    Write-Host "转换失败：pktmon etl2txt 没有写出 $txt" -ForegroundColor Red
    exit 2
}

Show-Findings $txt

if ($Keep) {
    Write-Host "`n原始包：$etl"
}
else {
    Remove-Item -LiteralPath $etl -Force
    Write-Host "`n已删除原始 ETL（要留原始包加 -Keep）。"
}

exit 0