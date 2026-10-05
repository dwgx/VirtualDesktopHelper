<#
  export-checks.ps1 — 从真实运行结果生成 docs/checks.md。
  上一轮工程最大的教训之一是「文档与代码脱节」；这里让文档由产物生成，而不是手写。
  用法： powershell -NoProfile -ExecutionPolicy Bypass -File tools/export-checks.ps1
#>
param(
  [string]$Exe = 'src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe',
  [string]$Out = 'docs/checks.md'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$tmp = Join-Path $env:TEMP 'vdhelper-selftest.txt'
& $Exe --selftest --out $tmp | Out-Null
$exit = $LASTEXITCODE

$lines = Get-Content -Encoding UTF8 $tmp
$summary = $lines[0]

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 检测项清单（自动生成）')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> 由 `tools/export-checks.ps1` 从 `VdHelper.exe --selftest` 的真实运行结果生成，**不要手工编辑**。')
[void]$sb.AppendLine("> 生成时间以 git 提交为准；本机实测退出码 $exit（0=可串流 3=有隐患 4=阻断）。")
[void]$sb.AppendLine()
[void]$sb.AppendLine("本机最近一次结论：$summary")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 本机实测结果')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 状态 | 编号 | 结论 |')
[void]$sb.AppendLine('| --- | --- | --- |')
foreach ($line in $lines) {
    if ($line -match '^\[(\w+)\s*\]\s+(\S+)\s+(.*)$') {
        $state = switch ($matches[1]) {
            'Pass' { '通过' } 'Warn' { '警告' } 'Block' { '阻断' } default { $matches[1] }
        }
        [void]$sb.AppendLine("| $state | **$($matches[2])** | $($matches[3]) |")
    }
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('## 检查项定义与来源')
[void]$sb.AppendLine()
[void]$sb.AppendLine('检测项的完整定义（症状 / 检查命令 / 判据 / 修复动作 / 回滚 / 风险）见')
[void]$sb.AppendLine('`research/02-network-diagnosis/02-pc-checklist.md`，实现见 `src/VdHelper/Core/Health/`。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('参数项（111 个，含 18 个只读）见 `research/04-streamer-settings/01-config-keys.md`，')
[void]$sb.AppendLine('由 `tools/extract-parameters.py` 生成到 `src/VdHelper/Resources/parameters.json`。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('头显侧判定规则见 `research/06-adb-headset/03-symptom-decision-table.md`。')

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
[System.IO.File]::WriteAllText((Join-Path $root $Out), $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host "wrote $Out ($((Get-Item $Out).Length) bytes), selftest exit=$exit"