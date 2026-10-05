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
    # Same treatment for the research topic count: it is another hand-written number in the same
    # file, and it had drifted too (11 while there were 14 directories).
    $topicDirs = @(Get-ChildItem -LiteralPath (Join-Path $root 'research') -Directory -ErrorAction SilentlyContinue)
    foreach ($m in [regex]::Matches($readme, '(\d+)\s*个主题')) {
        if ([int]$m.Groups[1].Value -ne $topicDirs.Count) {
            Write-Host ""
            Write-Host ("FAIL README 写的『" + $m.Value + "』与实际 " + $topicDirs.Count + " 个 research 主题目录不符") -ForegroundColor Red
            $readmeStale = $true
        }
    }
    if (-not $readmeStale) {
        Write-Host ("README 声明项数    : " + $real.Count + " 项检测 / " + $topicDirs.Count + " 个调研主题（一致）")
    }
}

# Emphasis markers that CommonMark will not open. "\u300c" and friends count as punctuation, so
# "**\u4e2a\u300c\u6b63\u6587\u300d\u201d" is not a valid left-flanking opener and GitHub prints the
# asterisks literally. Caught twice on the README by rendering it, never by reading the source.
$punct = @([char]0x300C, [char]0x300D, [char]0x300E, [char]0x300F, [char]0xFF08, [char]0xFF09,
           [char]0xFF1A, [char]0xFF0C, [char]0x3002, [char]0x3001, [char]0xFF1B, [char]0xFF01)
$emphBroken = $false
foreach ($md in @('README.md', 'docs/RELEASE-0.2.0.md')) {
    $mdPath = Join-Path $root $md
    if (-not (Test-Path -LiteralPath $mdPath)) { continue }
    $lineNo = 0
    $tick = [string][char]0x60
    foreach ($line in [System.IO.File]::ReadAllLines($mdPath, [System.Text.Encoding]::UTF8)) {
        $lineNo++
        $seen = 0
        $inCode = $false
        for ($c = 0; $c -lt $line.Length - 1; $c++) {
            if ($line[$c] -eq [char]$tick) { $inCode = -not $inCode; continue }
            # Asterisks inside a code span are literal by design: the release notes document a
            # rendering bug by writing the broken markup out.
            if ($inCode) { continue }
            if ($line[$c] -ne '*' -or $line[$c + 1] -ne '*') { continue }
            $isOpener = ($seen % 2) -eq 0
            $seen++
            if (-not $isOpener -or $c -eq 0) { continue }
            $prev = $line[$c - 1]
            $next = if ($c + 2 -lt $line.Length) { $line[$c + 2] } else { ' ' }
            # An opener needs a non-space on its left and a non-punctuation on its right. Text
            # running straight into a CJK bracket fails the second half and prints the asterisks.
            if ($punct -contains $next -and -not [char]::IsWhiteSpace($prev) -and -not ($punct -contains $prev)) {
                Write-Host ""
                Write-Host ("FAIL " + $md + ":" + $lineNo + " 的 ** 紧跟在文字后、又紧挨中文标点，GitHub 会原样输出星号") -ForegroundColor Red
                $emphBroken = $true
            }
        }
    }
}

if ($dangling -or $unreachable -or $readmeStale -or $emphBroken) { exit 1 }
Write-Host ""
Write-Host "OK  症状表与检测项一一对应，README 计数同步" -ForegroundColor Green
exit 0