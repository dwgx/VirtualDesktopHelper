<#
  check-symptom-map.ps1 — 反漂移闸门：症状表和检测项必须互相对得上。
  两条都要成立才算过：
    1) 症状表里引用的每个 check id 都真实存在（否则用户选了一个症状，列表却是空的）
    2) 每个真实存在的 check id 都被至少一个症状引用（否则有检测项用户永远看不到）
  CI 与本地都能跑；任何一条不成立就非零退出。
#>
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
Set-Location $Root

$healthDir = Join-Path $Root 'src/VdHelper/Core/Health'
$adbDir    = Join-Path $Root 'src/VdHelper/Core/Adb'
$symptom   = Join-Path $Root 'src/VdHelper/Core/Model/Symptom.cs'

# 1) real check ids, straight from the places they are declared
$real = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($f in Get-ChildItem $healthDir -Filter *.cs) {
    $t = Get-Content -Raw -Encoding UTF8 $f.FullName
    foreach ($m in [regex]::Matches($t, 'new\("([a-z][a-z0-9-]+)"')) { [void]$real.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($t, 'PsCheck\.Create\("([a-z][a-z0-9-]+)"')) { [void]$real.Add($m.Groups[1].Value) }
}
foreach ($f in Get-ChildItem $adbDir -Filter *.cs) {
    $t = Get-Content -Raw -Encoding UTF8 $f.FullName
    foreach ($m in [regex]::Matches($t, 'Id = "([a-z][a-z0-9-]+)"')) { [void]$real.Add($m.Groups[1].Value) }
}

# 2) ids referenced by the symptom map.
# SymptomClass is constructed positionally: new("S1", "title", [userPhrases], [checkIds], "firstLook")
# so the checks are the SECOND array literal in each entry, not a named argument.
$st = Get-Content -Raw -Encoding UTF8 $symptom
$referenced = New-Object 'System.Collections.Generic.HashSet[string]'
$entryPattern = 'new\("S\d+"\s*,\s*"[^"]*"\s*,\s*\[(.*?)\]\s*,\s*\[(.*?)\]'
$matched = [regex]::Matches($st, $entryPattern, 'Singleline')
foreach ($m in $matched) {
    foreach ($id in [regex]::Matches($m.Groups[2].Value, '"([a-z][a-z0-9-]+)"')) {
        [void]$referenced.Add($id.Groups[1].Value)
    }
}

$dangling  = $referenced | Where-Object { -not $real.Contains($_) } | Sort-Object
$unreachable = $real | Where-Object { -not $referenced.Contains($_) } | Sort-Object

Write-Host ("real checks      : " + $real.Count + "  (体检屏 + 头显屏；headset-deep 只在头显屏跑)")
Write-Host ("referenced by S* : " + $referenced.Count)
Write-Host ("症状类            : " + $matched.Count + " 个")

if ($dangling) {
    Write-Host ""
    Write-Host "FAIL 症状表引用了不存在的检测项：" -ForegroundColor Red
    $dangling | ForEach-Object { Write-Host ("     " + $_) -ForegroundColor Red }
}
if ($unreachable) {
    Write-Host ""
    Write-Host "FAIL 这些检测项没有被任何症状类引用（用户选症状时看不到它们）：" -ForegroundColor Red
    $unreachable | ForEach-Object { Write-Host ("     " + $_) -ForegroundColor Red }
}

# README 里手写的检测项数量必须与实际一致。这个数字已经漂过两次（工具涨到 27 项时、
# 34 项时都没跟上），所以把它变成一条会失败的检查，而不是靠记性。
$readmePath = Join-Path $root 'README.md'
$readmeStale = $false
if (Test-Path -LiteralPath $readmePath) {
    $readme = [System.IO.File]::ReadAllText($readmePath, [System.Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($readme, '(\d+)\s*项')) {
        if ([int]$m.Groups[1].Value -ne $real.Count) {
            Write-Host ""
            Write-Host ("FAIL README 写的『" + $m.Value + "』与实际 " + $real.Count + " 项检测不符") -ForegroundColor Red
            $readmeStale = $true
        }
    }
    if (-not $readmeStale) {
        Write-Host ("README 声明项数    : " + $real.Count + "（一致）")
    }
}

if ($dangling -or $unreachable -or $readmeStale) { exit 1 }
Write-Host ""
Write-Host "OK  症状表与检测项一一对应，README 计数同步" -ForegroundColor Green
exit 0