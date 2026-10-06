#requires -Version 5.1
<#
.SYNOPSIS
  核对 README 承诺的退出码契约 —— 只跑只读命令。

.DESCRIPTION
  退出码是本工具唯一的机器可读契约：一篇 issue 贴上来，别人靠 `rc` 判断问题。
  契约在 README.md:104-117，清单每次改代码都要对一次。

  这份脚本存在，是因为对退出码出过两次事：

  1. `notes/2026-10-06-do-not-sweep-state-changing-commands.md` —— 为了核对退出码，把
     `--quit-streamer` 放进了清扫，它把 Owner 正在跑的 Streamer 杀了。规矩后来写进
     WORKFLOW.md §10。
  2. `docs/checks.md:53` 的「111 个，含 18 个只读」是一个手写常量，跟真值差了 1。

  所以这个脚本**结构上不可能**跑到会改机器状态的命令：

    · 只读清单是硬编码的数组，逐条列出，没有通配、没有「把参数透传进来」的入口。
    · `--quit-streamer` / `--apply <id>` / `--set-param <key> <value>` 一律**不出现**。
      它们的退出码靠读代码确认（README §9），或在副本 / 临时路径上验证
      （配置写入路径当年就是这么验的，见 notes/2026-10-06-config-write-path-verified.md）。
    · `--deep` 会发 ICMP 包（WORKFLOW.md §10 第 2 条禁止任何向网络发包的探测）。
      它默认排除，用 -IncludeDeep 才跑，且跑前先打印一行说明它不在默认清单里。

  退出码的期望值直接写在下面，和 README 对齐；README 改了这里也要改，反之亦然。

.PARAMETER TimeoutSeconds
  Per-command deadline. A command that has not exited by then is killed and counted as a failure.
  Default 90s: generous for a machine with a headset, short enough that nine network-probing
  invocations cannot run away on a runner that has neither.

.PARAMETER IncludeDeep
  额外跑 `--deep`。它向网关与头显 IP 发 ICMP 包，会被防火墙计数。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-exit-codes.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-exit-codes.ps1 -IncludeDeep
#>
param(
    [switch]$IncludeDeep,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\VdHelper\bin\Release\net10.0-windows\VdHelper.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "找不到 $exe —— 先跑 tools\publish.ps1 或 dotnet build -c Release。" -ForegroundColor Red
    exit 1
}

$tmp = Join-Path $env:TEMP ("vdhelper-rc-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

# 只读清单。每一行：说明 / 参数 / 期望退出码。
# 期望码写死在这里而不是从 README 解析，是因为 README 可能被改坏 —— 那正是要发现的。
$cases = @(
    @{ Name = 'selftest';                Args = @('--selftest');                          Want = $null; Doc = '0 可串流 / 3 有隐患 / 4 阻断 / 5 运行失败' }
    @{ Name = 'report';                  Args = @('--report', "$tmp\a.md");              Want = $null; Doc = '同 selftest' }
    @{ Name = 'report-html';             Args = @('--report-html', "$tmp\a.html");        Want = $null; Doc = '同 selftest' }
    @{ Name = 'report --symptom S1';     Args = @('--report', "$tmp\b.md", '--symptom', 'S1'); Want = $null; Doc = '同 selftest' }
    @{ Name = 'report --symptom ZZ';     Args = @('--report', "$tmp\c.md", '--symptom', 'ZZ'); Want = 2; Doc = '2 参数写错' }
    @{ Name = 'adb';                     Args = @('--adb');                              Want = $null; Doc = '0 正常 / 3 有隐患 / 4 未连上或不可用' }
    @{ Name = 'adb --serial 不存在的设备'; Args = @('--adb', '--serial', 'nosuchdevice01'); Want = 4; Doc = '4 未连上或不可用' }
    @{ Name = 'apply --list';            Args = @('--apply', '--list');                   Want = 0; Doc = '0 成功' }
    @{ Name = 'apply 不存在的 id';        Args = @('--apply', 'no-such-fix-id-zzz');     Want = 9; Doc = '9 没有匹配的修复项' }
)

if ($IncludeDeep) {
    Write-Host '注意：--deep 会向网关与头显 IP 发 ICMP 包。' -ForegroundColor Yellow
    $cases += @{ Name = 'deep --samples 4'; Args = @('--deep', '--samples', '4'); Want = $null; Doc = '0 完成' }
}

Write-Host ''
Write-Host ("退出码契约核对 —— 只跑 {0} 条只读命令" -f $cases.Count) -ForegroundColor Cyan
Write-Host '不包含 --quit-streamer / --apply <id> / --set-param <key> <value>：WORKFLOW.md §10' -ForegroundColor DarkGray
Write-Host ''

$fail = 0
foreach ($c in $cases) {
    $out = Join-Path $tmp 'out.txt'
    $err = Join-Path $tmp 'err.txt'
    $proc = Start-Process -FilePath $exe -ArgumentList $c.Args -PassThru `
        -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
    # Touch .Handle before it exits. Without it Process.ExitCode stays null once the process is
    # gone — the handle has to have been cached while it was alive. This is why the original
    # -Wait version was used and why removing -Wait without adding this made every rc print blank.
    $null = $proc.Handle
    # Wait with a deadline. Start-Process -Wait blocks forever, and these commands run the whole
    # health engine: on a CI runner there is no headset at 192.168.11.14, so every reachability probe
    # waits out its full timeout — and this step runs the engine nine times. Observed on
    # GitHub-hosted windows-latest: the job sat in_progress for over an hour past the step before
    # this one, with no conclusion on any step after it. A gate that can hang protects nothing.
    if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
        try { $proc.Kill($true) } catch { }
        $fail++
        Write-Host ("  TIMEOUT  {0,-28} 超过 {1}s 未结束" -f $c.Name, $TimeoutSeconds) -ForegroundColor Red
        continue
    }
    $rc = $proc.ExitCode

    # selftest 类的期望码随机器状态在 0/3/4 之间浮动，所以只校验「属于文档允许的集合」；
    # 固定码的命令才逐个比对。
    if ($null -eq $c.Want) {
        $ok = $rc -in @(0, 3, 4, 5)
        $note = "应为 $($c.Doc)"
    }
    else {
        $ok = $rc -eq $c.Want
        $note = "应为 $($c.Want)"
    }
    if (-not $ok) { $fail++ }
    $mark = if ($ok) { 'OK  ' } else { 'FAIL' }
    $col = if ($ok) { 'DarkGray' } else { 'Red' }
    Write-Host ("  {0}  {1,-28} rc={2}   {3}" -f $mark, $c.Name, $rc, $note) -ForegroundColor $col
}

Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($fail -gt 0) {
    Write-Host ("FAIL {0} 条与 README 不符。README.md:104-117 与本脚本的期望值要一起改。" -f $fail) -ForegroundColor Red
    exit 1
}
Write-Host 'OK  只读命令的退出码全部与文档一致。' -ForegroundColor Green
exit 0
