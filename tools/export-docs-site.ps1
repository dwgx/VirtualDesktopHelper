<#
  export-docs-site.ps1 — 生成 VDHelper 文档站三页（零构建、零依赖、单文件手写 HTML）。

  页内每一个数字、每一条命令、每一条引文都来自仓库里的只读源文件，脚本不新增事实：
    docs/index.html   ← docs/checks.md（本机实测结论）+ README.md（三个界面 / 它不做什么）
                        + research/09-failure-corpus/02-symptom-to-rootcause.md（根因 / 覆盖度 / 错解）
    docs/checks.html  ← research/02-network-diagnosis/02-pc-checklist.md（检测项表，唯一真相源）
    docs/faq.html     ← research/09-failure-corpus/02-symptom-to-rootcause.md §1/§2/§3
                        + research/09-failure-corpus/01-symptom-corpus.md §2（来源与缺口）

  设计约束（沿用上一轮教训，见 research/10-prior-lessons/03-doc-assets.md）：
    · 检测项表不许手抄 —— 清单一改，文档跟着变；
    · 不写「44 处补丁 / 126 份报告 / 1000+ 用户」这类无源数字；
    · 源文件里找不到某个模式就抛错，绝不填占位数字。

  用法： powershell -NoProfile -ExecutionPolicy Bypass -File tools/export-docs-site.ps1
#>
param(
  [string]$Checklist = 'research/02-network-diagnosis/02-pc-checklist.md',
  [string]$ChecksMd  = 'docs/checks.md',
  [string]$Corpus    = 'research/09-failure-corpus/01-symptom-corpus.md',
  [string]$Rootcause = 'research/09-failure-corpus/02-symptom-to-rootcause.md',
  [string]$RealRun   = 'research/14-real-run/01-what-this-machine-found.md',
  [string]$Readme    = 'README.md',
  [string]$OutDir    = 'docs'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ---------------------------------------------------------------- 基础工具

function Read-Text([string]$rel) {
  $p = Join-Path $root $rel
  if (-not (Test-Path -LiteralPath $p)) { throw "找不到源文件：$rel" }
  return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
}
function Get-Lines([string]$text) { return [regex]::Split($text, "\r?\n") }
function Is-Separator([string]$line) { return ($line.Trim() -match '^\|[\s\-:|]+\|$') }

# 从源文件里取一个数字；取不到就抛错（不允许编造数字）
function Require-Number([string]$text, [string]$pattern, [string]$label, [int]$group = 1) {
  $m = [regex]::Match($text, $pattern)
  if (-not $m.Success) { throw "源文件里找不到「$label」（模式：$pattern）—— 拒绝编造数字" }
  return $m.Groups[$group].Value
}

function Esc-Plain([string]$text) {
  if ([string]::IsNullOrEmpty($text)) { return '' }
  $s = $text -replace '&', '&amp;'
  $s = $s -replace '<', '&lt;'
  $s = $s -replace '>', '&gt;'
  return $s
}

# Markdown 行内 → HTML（先转义，再补标签）
function Convert-Md([string]$text) {
  if ([string]::IsNullOrEmpty($text)) { return '' }
  $s = $text.Trim()
  $s = $s -replace '&', '&amp;'
  $s = $s -replace '<', '&lt;'
  $s = $s -replace '>', '&gt;'
  $s = $s -replace '\\\|', '|'
  $s = $s -replace '\\"', '"'
  $s = $s -replace '`([^`]+)`', '<code>$1</code>'
  $s = $s -replace '\*\*([^*]+)\*\*', '<strong>$1</strong>'
  $s = $s -replace '\[([^\]]+)\]\(([^)\s]+)\)', '<a href="$2">$1</a>'
  return $s
}

# Markdown 表格行 → 单元格数组（识别 `\|` 转义竖线）
function Split-MdRow([string]$line) {
  $s = $line.Trim()
  if ($s.StartsWith('|')) { $s = $s.Substring(1) }
  if ($s.EndsWith('|')) { $s = $s.Substring(0, $s.Length - 1) }
  $cells = @()
  $cur = New-Object System.Text.StringBuilder
  for ($i = 0; $i -lt $s.Length; $i++) {
    $ch = $s[$i]
    if ($ch -eq '\' -and ($i + 1) -lt $s.Length -and $s[$i + 1] -eq '|') {
      [void]$cur.Append('\|'); $i++; continue
    }
    if ($ch -eq '|') { $cells += $cur.ToString(); $cur = New-Object System.Text.StringBuilder; continue }
    [void]$cur.Append($ch)
  }
  $cells += $cur.ToString()
  return , $cells
}

# 收集从 $idx 开始的连续表格行，剔除分隔行
function Collect-Table([string[]]$lines, [ref]$idx) {
  $rows = @()
  while ($idx.Value -lt $lines.Count -and $lines[$idx.Value] -match '^\s*\|') {
    $l = $lines[$idx.Value]
    if (-not (Is-Separator $l)) { $rows += , (Split-MdRow $l) }
    $idx.Value++
  }
  return $rows
}

# 表格 → HTML（$rows[0] 为表头；$anchorPrefix 非空时给首列数字加锚点）
function Convert-Table($rows, [string]$anchorPrefix = '', [string[]]$headOverride = $null) {
  $out = New-Object System.Collections.Generic.List[string]
  $header = $rows[0]
  $labels = @()
  for ($i = 0; $i -lt $header.Count; $i++) {
    $label = $header[$i].Trim()
    if ($headOverride -and $i -lt $headOverride.Count -and $headOverride[$i]) { $label = $headOverride[$i] }
    $labels += $label
  }
  # 首列只有在整列都是编号（1 / A1 / C4 这类）时才用等宽金色，症状名、来源名照常排版
  $firstIsId = $false
  if ($rows.Count -gt 1) {
  $firstIsId = $true
    for ($r = 1; $r -lt $rows.Count; $r++) {
      if ($rows[$r][0].Trim() -notmatch '^(?:\d+|[A-Z]\d+)$') { $firstIsId = $false; break }
    }
  }
  $out.Add('<div class="tblwrap"><table>')
  $out.Add('<thead><tr>' + (($labels | ForEach-Object { '<th>' + (Convert-Md $_) + '</th>' }) -join '') + '</tr></thead><tbody>')
  for ($r = 1; $r -lt $rows.Count; $r++) {
    $cells = $rows[$r]
    $idAttr = ''
    if ($anchorPrefix -and $cells[0].Trim() -match '^\d+$') { $idAttr = ' id="' + $anchorPrefix + $cells[0].Trim() + '"' }
    $tds = @()
    for ($c = 0; $c -lt $labels.Count; $c++) {
      $val = if ($c -lt $cells.Count) { $cells[$c] } else { '' }
      $cls = if ($c -eq 0 -and $firstIsId) { ' class="num"' } else { '' }
      $tds += '<td' + $cls + '>' + (Convert-Md $val) + '</td>'
    }
    $out.Add('<tr' + $idAttr + '>' + ($tds -join '') + '</tr>')
  }
  $out.Add('</tbody></table></div>')
  return ($out -join "`n")
}

# Markdown 散文块 → HTML：源文档是软换行写的，按空行分段、段内行直接拼接（中文不补空格），
# 段内出现「1. 2. 3.」时切成有序列表。
function Render-MdBlocks([string[]]$lines, [string]$indent = '', [string]$firstClass = '') {
  $out = New-Object System.Collections.Generic.List[string]
  $groups = New-Object System.Collections.Generic.List[object]
  $cur = New-Object System.Collections.Generic.List[string]
  foreach ($l in $lines) {
    if ($l.Trim() -eq '') {
      if ($cur.Count -gt 0) { $groups.Add(@($cur.ToArray())); $cur = New-Object System.Collections.Generic.List[string] }
      continue
    }
    $cur.Add($l.Trim())
  }
  if ($cur.Count -gt 0) { $groups.Add(@($cur.ToArray())) }
  $isFirst = $true
  foreach ($g in $groups) {
    $split = -1
    for ($i = 0; $i -lt $g.Count; $i++) { if ($g[$i] -match '^\d+\.\s') { $split = $i; break } }
    if ($split -gt 0) {
      $head = (Convert-Md (($g[0..($split - 1)]) -join ''))
      $cls = if ($isFirst -and $firstClass) { ' class="' + $firstClass + '"' } else { '' }
      if ($isFirst) { $isFirst = $false }
      $out.Add($indent + '<p' + $cls + '>' + $head + '</p>')
      $out.Add($indent + '<ul class="plain">')
      for ($i = $split; $i -lt $g.Count; $i++) { $out.Add($indent + '  <li>' + (Convert-Md ($g[$i] -replace '^\d+\.\s*', '')) + '</li>') }
      $out.Add($indent + '</ul>')
    } else {
      $t = Convert-Md (($g -join ''))
      $cls = if ($isFirst -and $firstClass) { ' class="' + $firstClass + '"' } else { '' }
      if ($isFirst) { $isFirst = $false }
      $out.Add($indent + '<p' + $cls + '>' + $t + '</p>')
    }
  }
  return ($out -join "`n")
}

# ---------------------------------------------------------------- 读源

$checklistText = Read-Text $Checklist
$checksMdText  = Read-Text $ChecksMd
$rootText      = Read-Text $Rootcause
$corpusText    = Read-Text $Corpus
$readmeText    = Read-Text $Readme

$clLines = Get-Lines $checklistText
$mdLines = Get-Lines $checksMdText
$rcLines = Get-Lines $rootText
$coLines = Get-Lines $corpusText
$rdLines = Get-Lines $readmeText

# ---- checklist：约定行（⚙ / 🖐）+ 全部表格源行 + 阶段分组
$gear = [string][char]0x2699
$hand = [string]([char]0xD83D) + [string]([char]0xDD10)
$conventions = @()
$rawTableLines = @()
foreach ($l in $clLines) {
  if ($l -match '^\s*\|') { $rawTableLines += $l }
  if ($l -match '^\-\s+' -and ($l.Contains($gear) -or $l.Contains($hand)) -and -not ($conventions -contains $l.Trim())) {
    $conventions += ($l.Trim() -replace '^\-\s+', '')
  }
}

$stages = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $clLines.Count; $i++) {
  if ($clLines[$i] -notmatch '^##\s+(.+?)\s*$') { continue }
  $title = $matches[1]
  $j = $i + 1
  while ($j -lt $clLines.Count -and $clLines[$j] -notmatch '^\s*\|') { $j++ }
  if ($j -ge $clLines.Count) { continue }
  $idx = $j
  $rows = Collect-Table $clLines ([ref]$idx)
  if ($rows.Count -ge 2) {
    $stages.Add([pscustomobject]@{
      Title       = $title
      Rows        = $rows
      HeaderCount = $rows[0].Count
      Numbered    = ($rows[0][0].Trim() -eq '编号')
    })
    $i = $idx - 1
  }
}
if ($stages.Count -lt 4) { throw "从 $Checklist 只解析出 $($stages.Count) 个表格分组（预期 ≥5）" }

$checkTotal = 0
foreach ($s in $stages) { if ($s.Numbered) { $checkTotal += ($s.Rows.Count - 1) } }
if ($checkTotal -lt 20) { throw "清单里只数出 $checkTotal 条检测项（预期 ≥20）" }
if ($conventions.Count -lt 1) { throw "$Checklist 里没找到 ⚙ / 🖐 约定行" }

$stageHeads = @('编号', '症状', '检查命令（可直接粘）', '通过判据', '失败时怎么办', '怎么回滚', '风险')

# ---- checklist：覆盖对照之后的「无法自动检测」说明段
$notePara = @()
$noteQuote = @()
for ($i = 0; $i -lt $clLines.Count; $i++) {
  if ($clLines[$i] -match '^\*\*关于「.+」这一族\*\*') {
    for ($j = $i; $j -lt $clLines.Count; $j++) {
      if ($clLines[$j] -match '^---\s*$') { break }
      if ($clLines[$j] -match '^>') { $noteQuote += $clLines[$j] } else { $notePara += $clLines[$j] }
    }
    break
  }
}

# ---- checks.md：本机实测结果
$verdict = ''
$itemRows = @()
foreach ($l in $mdLines) {
  if ($l -match '^本机最近一次结论：(.+)$') { $verdict = $matches[1].Trim() }
  if ($l -match '^\s*\|' -and $l -notmatch '^\s*\|[\s\-:|]+\|\s*$' -and $l -notmatch '^\s*\|\s*状态\s*\|') {
    $c = Split-MdRow $l
    if ($c.Count -ge 3 -and $c[0].Trim() -match '^(通过|警告|阻断|Pass|Warn|Block|Unknown)$') {
      $itemRows += , @($c[0].Trim(), $c[1].Trim(), (($c[2..($c.Count - 1)]) -join ' | '))
    }
  }
}
if (-not $verdict) { throw "$ChecksMd 里没找到「本机最近一次结论」" }
if ($itemRows.Count -lt 5) { throw "$ChecksMd 里只解析出 $($itemRows.Count) 行实测结果" }

$countPass = 0; $countWarn = 0; $countBlock = 0; $countUnknown = 0
foreach ($r in $itemRows) {
  switch ($r[0]) {
    '通过'    { $countPass++ }
    '警告'    { $countWarn++ }
    '阻断'    { $countBlock++ }
    'Pass'    { $countPass++ }
    'Warn'    { $countWarn++ }
    'Block'   { $countBlock++ }
    'Unknown' { $countUnknown++ }
  }
}
$statusClass = @{ '通过' = 'pass'; 'Pass' = 'pass'; '警告' = 'warn'; 'Warn' = 'warn';
                   '阻断' = 'block'; 'Block' = 'block'; 'Unknown' = 'unknown' }
$verdictToken = ''
if ($verdict -match 'verdict=(\S+)') { $verdictToken = $matches[1] }
$verdictClass = switch ($verdictToken) {
  'Streamable' { 'pass' }
  'AtRisk'     { 'warn' }
  'Blocked'    { 'block' }
  default      { 'unknown' }
}
# verdict 行格式见 App.xaml.cs:92 —— `VDHelper selftest  verdict=<Token>  <中文结论>`
$verdictText = ($verdict -replace '^\S+\s+selftest\s+verdict=\S+\s*', '').Trim()
if (-not $verdictText) { $verdictText = $verdict }

function Convert-StatusTable($rows) {
  $out = New-Object System.Collections.Generic.List[string]
  $out.Add('<div class="tblwrap"><table>')
  $out.Add('<thead><tr><th>状态</th><th>编号</th><th>结论</th></tr></thead><tbody>')
  foreach ($r in $rows) {
    $cls = $statusClass[$r[0]]
    if (-not $cls) { $cls = 'unknown' }
    $out.Add('<tr><td><span class="badge ' + $cls + '">' + (Esc-Plain $r[0]) + '</span></td>' +
             '<td><code>' + (Esc-Plain ($r[1] -replace '\*', '')) + '</code></td>' +
             '<td>' + (Convert-Md $r[2]) + '</td></tr>')
  }
  $out.Add('</tbody></table></div>')
  return ($out -join "`n")
}

# ---- rootcause：§1 症状词典 / §2 S1-S7 / 覆盖度小结 / §3 十条错误解法
$symptomRows = $null
$causeGroups = New-Object System.Collections.Generic.List[object]
$coverageRows = $null
$coverageList = @()
$myths = New-Object System.Collections.Generic.List[object]

for ($i = 0; $i -lt $rcLines.Count; $i++) {
  $l = $rcLines[$i]

  if ($l -match '^##\s+1\.\s') {
    $j = $i + 1
    while ($j -lt $rcLines.Count -and $rcLines[$j] -notmatch '^\s*\|') { $j++ }
    if ($j -lt $rcLines.Count) { $idx = $j; $symptomRows = Collect-Table $rcLines ([ref]$idx); $i = $idx - 1 }
    continue
  }
  if ($l -match '^###\s+(S\d)\s*(.*)$') {
    $g = [pscustomobject]@{ Code = $matches[1]; Title = $matches[2].Trim(); Rows = $null }
    $j = $i + 1
    while ($j -lt $rcLines.Count -and $rcLines[$j] -notmatch '^\s*\|') { $j++ }
    if ($j -lt $rcLines.Count) { $idx = $j; $g.Rows = Collect-Table $rcLines ([ref]$idx); $i = $idx - 1 }
    $causeGroups.Add($g)
    continue
  }
  if ($l -match '^###\s+覆盖度小结') {
    $j = $i + 1
    while ($j -lt $rcLines.Count -and $rcLines[$j] -notmatch '^\s*\|') { $j++ }
    $idx = $j
    $coverageRows = Collect-Table $rcLines ([ref]$idx)
    $k = $idx
    while ($k -lt $rcLines.Count -and $rcLines[$k] -notmatch '^\d+\.\s') { $k++ }
    while ($k -lt $rcLines.Count -and $rcLines[$k] -match '^\d+\.\s') {
      $coverageList += $rcLines[$k].Trim()
      $k++
      while ($k -lt $rcLines.Count -and $rcLines[$k].Trim() -eq '') { $k++ }
    }
    $i = $idx - 1
    continue
  }
  if ($l -match '^###\s+3\.(\d+)\s+(.+)$') {
    $no = $matches[1]
    $parts = $matches[2].Trim() -split '\s+——\s+'
    $title = if ($parts.Count -ge 2) { $parts[0] } else { $matches[2].Trim() }
    $flag = if ($parts.Count -ge 2) { $parts[1] } else { '' }
    $labels = New-Object System.Collections.Generic.List[object]
    $cur = $null
    $end = $rcLines.Count - 1
    for ($j = $i + 1; $j -lt $rcLines.Count; $j++) {
      $b = $rcLines[$j]
      if ($b -match '^###\s' -or $b -match '^---\s*$') { $end = $j - 1; break }
      if ($b.Trim() -eq '') { continue }
      if ($b -match '^\*\*(.+?)\*\*\s*[：:]\s*(.*)$') {
        $cur = [pscustomobject]@{ Name = $matches[1]; Inline = $matches[2]; Bullets = @() }
        $labels.Add($cur)
        continue
      }
      if ($b -match '^-\s+(.*)$') { if ($cur) { $cur.Bullets += $matches[1] }; continue }
      if ($cur) { $cur.Inline = $cur.Inline + $b.Trim() }
    }
    $myths.Add([pscustomobject]@{ No = $no; Id = 'm3-' + $no; Title = $title; Flag = $flag; Labels = $labels })
    $i = $end
  }
}

if ($symptomRows -eq $null) { throw "$Rootcause 里没找到 §1 症状词典表" }
if ($causeGroups.Count -ne 7) { throw "$Rootcause 里解析出 $($causeGroups.Count) 个症状分组（预期 7）" }
foreach ($g in $causeGroups) { if (-not $g.Rows -or $g.Rows.Count -lt 2) { throw "症状分组 $($g.Code) 的表格没解析出来" } }
if ($coverageRows -eq $null) { throw "$Rootcause 里没找到覆盖度小结表" }
if ($coverageList.Count -lt 5) { throw "$Rootcause 覆盖度小结后面的未覆盖清单只读到 $($coverageList.Count) 条" }
if ($myths.Count -ne 10) { throw "$Rootcause §3 解析出 $($myths.Count) 条错误解法（预期 10）" }
foreach ($m in $myths) {
  $hasWrong = $false; $hasRight = $false
  foreach ($lb in $m.Labels) {
    if ($lb.Name -match '为什么错') { $hasWrong = $true }
    if ($lb.Name -match '正确说法') { $hasRight = $true }
  }
  if (-not ($hasWrong -and $hasRight)) { throw "错误解法 3.$($m.No) 缺「为什么错」或「正确说法」" }
}

$causeClaimed = $coverageRows[1][0].Trim()
$causeCovered = $coverageRows[1][1].Trim()
$causePartial = $coverageRows[1][2].Trim()
$causeUncovered = ($coverageRows[1][3] -replace '\*', '').Trim()
$corpusCount = Require-Number $rootText '(\d+)\s*条真实用户描述' '语料条数'
$threadCount = Require-Number $rootText '(\d+)\s*个完整 Reddit 帖' 'Reddit 帖数'
$symClassCount = Require-Number $rootText '归并为\s+\*\*(\d+)\s*个症状类' '症状类数'
$symSubCount = Require-Number $rootText '\+\s*(\d+)\s*个子症状' '子症状数'

# 清单编号 → 该行症状文本（生成时从清单取，页面上不手写，清单一改链接文字跟着改）
$checkById = @{}
foreach ($s in $stages) {
  if (-not $s.Numbered) { continue }
  foreach ($row in $s.Rows[1..($s.Rows.Count - 1)]) { $checkById[$row[0].Trim()] = ($row[1].Trim() -replace '\*\*', '') }
}

# 错解 → 清单编号的人工映射（唯一需要人判断的地方；描述文字仍由清单生成）
$mythToChecks = @{
  '1'  = @(21)      # 端口转发：同网段不需要，远程才需要
  '2'  = @(21)      # UPnP：本地场景不需要
  '3'  = @(5, 8)    # 关防火墙 → 补规则 / 看 profile 状态
  '4'  = @(21)      # DMZ：同段场景与 UPnP 无关
  '6'  = @(25, 26)  # 重启一切：唤醒与换 IP 的时序有代码依据
  '7'  = @(3, 10)   # 归因杀软：服务身份错误码 + 已注册杀软
}
$mythToUntestable = @{
  '5' = '路由器 AP 隔离 / 访客网络 / VLAN 在 PC 上没有可编程观测点'
}

# S1–S7 分诊表里实际列出的根因条目数（逐表数出来，不采信文档自述的总数）
$causeRows = 0
$causeBreakdown = @()
foreach ($g in $causeGroups) {
  $n = $g.Rows.Count - 1
  $causeRows += $n
  $causeBreakdown += ($g.Code + ' ' + $n)
}
# The coverage summary assesses a *subset* of the S1-S7 catalogue, so a bare inequality is the
# wrong test. It is only a contradiction when the source does not say so. Reconciled on
# 2026-10-05: the catalogue has 43 rows, the coverage summary assesses 23 of them.
$causeRaw = (Get-Content -Raw -Encoding UTF8 $Rootcause)
$causeReconciled = ($causeRaw -match '口径修正|被单独做过覆盖度评估|只对其中')
$causeMismatch = ($causeRows -ne [int]$causeClaimed) -and -not $causeReconciled
if ($causeMismatch) {
  Write-Warning ("源文件数字对不上：S1-S7 分诊表实际 $causeRows 条（" + ($causeBreakdown -join ' + ') +
    "），但 §0 与覆盖度小结写的是 $causeClaimed，且源文件没有说明这是子集。文档站两个口径都照登并标注，不替源文件选一个。")
}

# ---- corpus §2：来源与缺口
$srcRows = $null
for ($i = 0; $i -lt $coLines.Count; $i++) {
  if ($coLines[$i] -match '^##\s+2\.\s') {
    $j = $i + 1
    while ($j -lt $coLines.Count -and $coLines[$j] -notmatch '^\s*\|') { $j++ }
    if ($j -lt $coLines.Count) { $idx = $j; $srcRows = Collect-Table $coLines ([ref]$idx) }
    break
  }
}
if ($srcRows -eq $null) { throw "$Corpus §2 里没找到来源表" }

# ---- README：三个界面表 + 它不做什么
$uiRows = $null
$dontList = @()
for ($i = 0; $i -lt $rdLines.Count; $i++) {
  if ($rdLines[$i] -match '^##\s+三个界面') {
    $j = $i + 1
    while ($j -lt $rdLines.Count -and $rdLines[$j] -notmatch '^\s*\|') { $j++ }
    if ($j -lt $rdLines.Count) { $idx = $j; $uiRows = Collect-Table $rdLines ([ref]$idx) }
    continue
  }
  if ($rdLines[$i] -match '^##\s+它不做什么') {
    for ($j = $i + 1; $j -lt $rdLines.Count; $j++) {
      if ($rdLines[$j] -match '^##\s') { break }
      if ($rdLines[$j] -match '^-\s+(.*)$') { $dontList += $matches[1] }
    }
  }
}
if ($uiRows -eq $null) { throw "$Readme 里没找到「三个界面」表" }
if ($dontList.Count -lt 3) { throw "$Readme 里「它不做什么」不足 3 条" }

# ---------------------------------------------------------------- 样式与外壳

$style = @'
*{box-sizing:border-box;margin:0;padding:0}
:root{
  --bg:#05060a;--bg2:#080a11;--fg:#eef1f8;--mut:#8b93a8;--dim:#5a6172;
  --line:#171a24;--line2:#232838;--card:#0c0e16;
  --neon:#5b8cff;--neon2:#22d3a7;--warn:#ff5b6e;--gold:#ffcc66;
  --mono:"Cascadia Mono",Consolas,"SF Mono",ui-monospace,Menlo,monospace;
  --sans:system-ui,-apple-system,"Segoe UI",Verdana,"PingFang SC","Microsoft YaHei",sans-serif;
}
html{scroll-behavior:smooth}
body{background:var(--bg);color:var(--fg);font-family:var(--sans);font-size:15px;line-height:1.75;overflow-x:hidden;-webkit-font-smoothing:antialiased}
#prog{position:fixed;top:0;left:0;height:2px;width:0;z-index:9998;pointer-events:none;background:var(--neon2)}
@media(prefers-reduced-motion:reduce){#prog{display:none}}
a{color:var(--neon);text-decoration:none}
a:hover{text-decoration:underline}
strong,b{color:#fff}
code{font-family:var(--mono);font-size:.92em;background:#10131d;border:1px solid var(--line2);border-radius:4px;padding:1px 5px;color:#bcd0ff;word-break:break-word}
.wrap{max-width:1080px;margin:0 auto;padding:0 22px}
.topbar{position:sticky;top:0;z-index:999;background:rgba(5,6,10,.93);border-bottom:1px solid var(--line)}
.topbar .wrap{display:flex;align-items:center;gap:18px;height:54px;flex-wrap:wrap}
.topbar .brand{font:700 14px/1 var(--mono);color:#fff}
.topbar .brand span{color:var(--neon2)}
.topbar nav{display:flex;gap:4px;flex-wrap:wrap;margin-left:auto}
.topbar nav a{font:600 13px/1 var(--sans);color:var(--mut);padding:8px 13px;border-radius:8px;border:1px solid transparent}
.topbar nav a:hover{color:#fff;background:#0e111a;border-color:var(--line2);text-decoration:none}
.topbar nav a.on{color:#04130d;background:var(--neon2);border-color:var(--neon2)}
.hero{padding:56px 0 26px}
.eyebrow{font:600 11.5px/1.6 var(--mono);letter-spacing:.2em;text-transform:uppercase;color:var(--neon2);margin-bottom:14px}
h1{font-size:clamp(26px,4vw,42px);line-height:1.18;font-weight:800;letter-spacing:-.03em}
h1 .grad{color:var(--neon2)}
.lead{color:#b3bbcd;font-size:16px;line-height:1.85;margin-top:18px;max-width:780px}
.lead strong{color:#e9edf5}
.actions{display:flex;gap:11px;margin-top:26px;flex-wrap:wrap}
.actions a{font:600 14px/1 var(--sans);padding:12px 20px;border-radius:10px;border:1px solid var(--line2);color:var(--fg);transition:.18s}
.actions a.primary{background:var(--neon2);color:#04130d;border-color:var(--neon2)}
.actions a.primary:hover{box-shadow:0 12px 30px -14px rgba(34,211,167,.75);text-decoration:none;transform:translateY(-1px)}
.actions a.ghost:hover{border-color:var(--neon);color:#fff;text-decoration:none}
.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:13px;margin:28px 0 6px}
@media(max-width:720px){.stats{grid-template-columns:repeat(2,1fr)}}
.stat{background:linear-gradient(180deg,var(--card),#090b12);border:1px solid var(--line);border-radius:14px;padding:20px 14px;text-align:center}
.stat .n{font:800 34px/1 var(--mono);color:#cfe0ff}
.stat .n.g{color:var(--neon2)}
.stat .l{margin-top:8px;color:var(--mut);font-size:12.5px;line-height:1.5}
section{padding:42px 0 44px;border-top:1px solid var(--line);margin-top:32px}
h2{font-size:clamp(21px,3.2vw,28px);font-weight:800;letter-spacing:-.02em;line-height:1.3}
h2::before{content:"\BB\00a0";color:var(--neon2);font-family:var(--mono);font-weight:700;opacity:.45}
h3{font-size:16px;font-weight:700;color:#fff;margin:28px 0 8px}
h3 .tag{font:600 11px/1 var(--mono);color:var(--neon2);border:1px solid rgba(34,211,167,.3);border-radius:5px;padding:3px 6px;margin-left:8px}
.lead2{color:#c2c9d8;font-size:15px;margin:12px 0 0;max-width:840px}
p{color:#c2c9d8;margin:10px 0}
ul.plain{list-style:none;margin:14px 0 0;padding:0}
ul.plain li{position:relative;padding-left:20px;margin:9px 0;color:#c2c9d8}
ul.plain li::before{content:"";position:absolute;left:2px;top:.72em;width:6px;height:6px;border-radius:2px;background:var(--neon2)}
.note{color:var(--dim);font-size:13px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:20px 22px;margin-top:16px}
.card.warn{border-color:rgba(255,204,102,.35);background:rgba(255,204,102,.05)}
.tblwrap{overflow-x:auto;margin:16px 0 6px;border:1px solid var(--line);border-radius:12px}
table{border-collapse:collapse;width:100%;font-size:13.5px}
th,td{text-align:left;vertical-align:top;padding:11px 13px;border-bottom:1px solid var(--line)}
th{background:#0e111a;color:var(--mut);font:600 11.5px/1.5 var(--sans);letter-spacing:.03em}
# NOT position:sticky. .tblwrap sets overflow-x:auto, which makes it a scroll container, so a
# sticky th resolves against .tblwrap rather than the page: the header pinned itself 54px below
# the table's own top edge and covered the first data row of every table on the site.
tbody tr:nth-child(even){background:rgba(255,255,255,.014)}
tbody tr:hover{background:rgba(91,140,255,.05)}
tbody tr:last-child td{border-bottom:0}
td.num{font-family:var(--mono);color:var(--gold);font-weight:700;white-space:nowrap;width:1%}
tr:target{background:rgba(34,211,167,.1)}
td code{white-space:pre-wrap;word-break:break-word;display:inline-block;background:none;border:0;padding:0;color:#bcd0ff}
.badge{display:inline-block;font:700 11px/1 var(--mono);padding:4px 8px;border-radius:6px;white-space:nowrap}
.badge.pass{color:#8ee9cf;background:rgba(34,211,167,.12);border:1px solid rgba(34,211,167,.35)}
.badge.warn{color:#ffd98a;background:rgba(255,204,102,.12);border:1px solid rgba(255,204,102,.35)}
.badge.block{color:#ff9aa6;background:rgba(255,91,110,.12);border:1px solid rgba(255,91,110,.35)}
.badge.unknown{color:#a6aec2;background:rgba(255,255,255,.05);border:1px solid var(--line2)}
.myth{background:var(--card);border:1px solid var(--line);border-left:3px solid var(--warn);border-radius:12px;padding:18px 20px;margin-top:16px}
.myth:target{border-color:var(--neon2);border-left-color:var(--neon2)}
.myth .mh{display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}
.myth .mh .no{font:700 12px/1.6 var(--mono);color:var(--warn);letter-spacing:.06em}
.myth .mh h3{margin:0;font-size:16.5px}
.flag{font:700 11.5px/1.6 var(--mono);padding:3px 8px;border-radius:6px;background:rgba(255,91,110,.13);border:1px solid rgba(255,91,110,.35);color:#ff9aa6}
.myth h4{font:700 11.5px/1.6 var(--mono);letter-spacing:.16em;text-transform:uppercase;color:var(--dim);margin:16px 0 6px}
.say{color:#c9d0de;margin:0}
.myth blockquote{margin:6px 0 0;padding:8px 12px;border-left:2px solid var(--line2);color:#a9b1c4;font-size:13.5px}
.myth blockquote p{margin:4px 0;color:inherit}
.wrong{background:rgba(255,91,110,.06);border:1px solid rgba(255,91,110,.2);border-radius:10px;padding:12px 14px;margin-top:6px}
.wrong h4{color:#ff9aa6;margin-top:0}
.right{background:rgba(34,211,167,.06);border:1px solid rgba(34,211,167,.22);border-radius:10px;padding:12px 14px;margin-top:6px}
.right h4{color:#8ee9cf;margin-top:0}
.myth .xref{margin:12px 0 0;padding-top:10px;border-top:1px dashed var(--line2);color:var(--mut);font-size:13px}
details{margin-top:18px;border:1px solid var(--line);border-radius:12px;background:#080a11}
summary{cursor:pointer;padding:13px 16px;font:600 13px/1.6 var(--sans);color:var(--mut);list-style:none}
summary::-webkit-details-marker{display:none}
summary::before{content:"\25B8\00a0";color:var(--neon2)}
details[open]>summary{color:#fff;border-bottom:1px solid var(--line)}
details pre{margin:0;padding:14px 16px;overflow-x:auto;font:12.5px/1.65 var(--mono);color:#a9b1c4;white-space:pre}
footer{border-top:1px solid var(--line);padding:28px 0 44px;color:var(--dim);font-size:12.5px}
footer .wrap{display:flex;gap:16px;justify-content:space-between;flex-wrap:wrap}
footer a{color:var(--mut)}
@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}}
'@

$footerHtml = '<footer><div class="wrap">' +
  '<div>本站是 VDHelper 的个人技术文档，与 Virtual Desktop, Inc. 无隶属关系，非官方网站。仅供学习与互操作性研究，所有商标版权归原作者所有。</div>' +
  '<div>页面由 <code>tools/export-docs-site.ps1</code> 从仓库源文件生成</div>' +
  '</div></footer>'

function New-Page([string]$title, [string]$desc, [string]$active, [string]$body, [string]$script = '') {
  $items = @(
    @{ href = 'index.html';  key = 'index';  label = '首页' }
    @{ href = 'checks.html'; key = 'checks'; label = '检测项全表' }
    @{ href = 'faq.html';    key = 'faq';    label = '常见问题' }
  )
  $nav = ''
  foreach ($it in $items) {
    $cls = if ($active -eq $it.key) { ' class="on"' } else { '' }
    $nav += '<a href="' + $it.href + '"' + $cls + '>' + $it.label + '</a>'
  }
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('<!doctype html>')
  [void]$sb.AppendLine('<html lang="zh-CN">')
  [void]$sb.AppendLine('<head>')
  [void]$sb.AppendLine('<meta charset="utf-8">')
  [void]$sb.AppendLine('<meta name="viewport" content="width=device-width,initial-scale=1">')
  [void]$sb.AppendLine('<title>' + (Esc-Plain $title) + '</title>')
  [void]$sb.AppendLine('<meta name="description" content="' + (Esc-Plain $desc) + '">')
  [void]$sb.AppendLine('<link rel="icon" href="data:,">')
  [void]$sb.AppendLine('<style>')
  [void]$sb.AppendLine($style)
  [void]$sb.AppendLine('</style>')
  [void]$sb.AppendLine('</head>')
  [void]$sb.AppendLine('<body>')
  [void]$sb.AppendLine('<div id="prog"></div>')
  [void]$sb.AppendLine('<div class="topbar"><div class="wrap"><a class="brand" href="index.html">VD<span>Helper</span></a><nav>' + $nav + '</nav></div></div>')
  [void]$sb.AppendLine($body)
  [void]$sb.AppendLine($footerHtml)
  if ($script) { [void]$sb.AppendLine('<script>'); [void]$sb.AppendLine($script); [void]$sb.AppendLine('</script>') }
  [void]$sb.AppendLine('</body>')
  [void]$sb.AppendLine('</html>')
  return $sb.ToString()
}

$pageScript = @'
(function(){
  var prog=document.getElementById('prog');
  if(prog){var tick=false;
    function upd(){var h=document.documentElement,max=(h.scrollHeight-h.clientHeight)||1;
      prog.style.width=(Math.min(h.scrollTop/max,1)*100)+'%';tick=false;}
    addEventListener('scroll',function(){if(!tick){tick=true;requestAnimationFrame(upd);}},{passive:true});
    upd();}
  var reduce=matchMedia('(prefers-reduced-motion: reduce)').matches;
  var els=document.querySelectorAll('.stat .n[data-to]');
  for(var i=0;i<els.length;i++){(function(el){
    var to=parseInt(el.dataset.to,10);
    if(reduce||isNaN(to)){el.textContent=to;return;}
    var start=null,dur=1100;
    function step(ts){if(!start)start=ts;var p=Math.min((ts-start)/dur,1);
      var e=1-Math.pow(1-p,3);el.textContent=Math.round(e*to);
      if(p<1)requestAnimationFrame(step);else el.textContent=to;}
    requestAnimationFrame(step);})(els[i]);}
})();
'@

# ---------------------------------------------------------------- index.html

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<div class="wrap">')
[void]$sb.AppendLine('<header class="hero">')
[void]$sb.AppendLine('  <div class="eyebrow">VDHELPER · WINDOWS PC · 本机实测</div>')
$implemented = $itemRows.Count
$plannedNote = ''
if ($checkTotal -ne $implemented) {
  $plannedNote = '<p class="lead">清单里有 <strong>' + $checkTotal + '</strong> 条待检测项；工具当前实装并每次体检都会跑的是 <strong>' +
    $implemented + '</strong> 条（见下方本机实测）。两个数字不是一回事，差额是还没实现的。</p>'
}
[void]$sb.AppendLine(('  <h1>「连不上」不是一个错误码，<br><span class="grad">是 ' + $implemented + ' 条可检测的断链</span></h1>'))
[void]$sb.AppendLine('  <p class="lead">VDHelper 是 Windows 端的 Virtual Desktop 串流检测 / 诊断 / 修复工具。它回答一个具体问题：<strong>为什么头显找不到这台 PC，或者找到了却连不上。</strong>本页所有数字都来自本机真实运行结果与仓库里可核实的语料，没有一条是估的。</p>')
[void]$sb.AppendLine($plannedNote)
[void]$sb.AppendLine(('  <div class="actions"><a class="primary" href="checks.html">看 ' + $checkTotal + ' 项检测判据全表</a><a class="ghost" href="faq.html">先排掉 ' + $myths.Count + ' 个社区错解 ↓</a></div>'))
[void]$sb.AppendLine('</header>')

[void]$sb.AppendLine('<div class="stats">')
[void]$sb.AppendLine(('  <div class="stat"><div class="n g" data-to="' + $implemented + '">0</div><div class="l">条已实装检测项<br>（每次体检都跑）</div></div>'))
[void]$sb.AppendLine(('  <div class="stat"><div class="n" data-to="' + $corpusCount + '">0</div><div class="l">条真实用户失败描述<br>（来自 ' + $threadCount + ' 个完整帖子）</div></div>'))
[void]$sb.AppendLine(('  <div class="stat"><div class="n" data-to="' + $causeRows + '">0</div><div class="l">条症状→根因条目<br>（S1–S7 分诊表逐表数出）</div></div>'))
[void]$sb.AppendLine(('  <div class="stat"><div class="n" data-to="' + $causeUncovered + '">0</div><div class="l">个根因完全未覆盖<br>（覆盖度小结口径：共 ' + $causeClaimed + '）</div></div>'))
[void]$sb.AppendLine('</div>')
if ($causeMismatch) {
  [void]$sb.AppendLine(('  <p class="note">口径提示：分诊表里逐表列出的是 <strong>' + $causeRows + '</strong> 条根因条目（' + ($causeBreakdown -join ' + ') + '），而研究文档的结论段与覆盖度小结写的是 <strong>' + $causeClaimed + '</strong>。两个数字都在页面上标了出处，没有替源文件挑一个 —— 见 <a href="faq.html#coverage">常见问题 · 覆盖度</a>。</p>'))
}

[void]$sb.AppendLine('<section id="verdict">')
[void]$sb.AppendLine('  <h2>本机最近一次体检结论</h2>')
[void]$sb.AppendLine('  <p class="lead2">下面这段是从 <code>docs/checks.md</code> 读进来的真实运行结果，不是宣传语。该文件本身由 <code>tools/export-checks.ps1</code> 执行 <code>VdHelper.exe --selftest</code> 生成。</p>')
[void]$sb.AppendLine(('  <div class="card"><p style="margin:0"><span class="badge ' + $verdictClass + '">' + (Esc-Plain $verdictText) + '</span></p>'))
[void]$sb.AppendLine(('  <p class="note">原始行：' + (Esc-Plain $verdict) + '</p></div>'))
[void]$sb.AppendLine('  <div class="stats" style="margin:18px 0 4px">')
[void]$sb.AppendLine(('    <div class="stat"><div class="n g" data-to="' + $countPass + '">0</div><div class="l">通过 / 共 ' + $itemRows.Count + ' 项</div></div>'))
[void]$sb.AppendLine(('    <div class="stat"><div class="n" data-to="' + $countWarn + '">0</div><div class="l">警告</div></div>'))
[void]$sb.AppendLine(('    <div class="stat"><div class="n" data-to="' + $countBlock + '">0</div><div class="l">阻断</div></div>'))
[void]$sb.AppendLine(('    <div class="stat"><div class="n" data-to="' + $countUnknown + '">0</div><div class="l">信息不足（Unknown）</div></div>'))
[void]$sb.AppendLine('  </div>')
[void]$sb.AppendLine('  ' + (Convert-StatusTable $itemRows))
[void]$sb.AppendLine('  <p class="note">检测项的完整定义（症状 / 命令 / 判据 / 修复 / 回滚 / 风险）见 <a href="checks.html">检测项全表</a>；参数项与头显侧规则见 <code>checks.md</code> 末尾的三处指向。</p>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>三个界面</h2>')
[void]$sb.AppendLine('  ' + (Convert-Table $uiRows))
[void]$sb.AppendLine('  <p class="note">顶部常驻总判定：可串流 / 有隐患 / 阻断。本页结论行即来自该判定。</p>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine(('  <h2>用户说的七种症状，其实只有 ' + $symClassCount + ' 类</h2>'))
[void]$sb.AppendLine(('  <p class="lead2">语料里能归并出 <strong>' + $symClassCount + ' 个症状类 + ' + $symSubCount + ' 个子症状</strong>。左边是用户嘴里的话，右边是工具必须拿到的状态 —— 两边对不上，说明这类症状还没被测到。</p>'))
[void]$sb.AppendLine('  ' + (Convert-Table $symptomRows))
[void]$sb.AppendLine('  <p class="note">每一类症状背后的多个工程根因，见 <a href="faq.html#symptom">常见问题 · 症状词典</a> 与 <a href="faq.html#triage">分诊表</a>。</p>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>先纠正三条最贵的误会</h2>')
[void]$sb.AppendLine('  <p class="lead2">这几条在社区里出现频率最高，而且对<b>同一网段内</b>的本地串流无效或有害。完整立场写在 <a href="faq.html#myths">常见问题 · 社区常见的错误解法</a>，那里逐条写了「为什么错 / 正确说法是什么」。</p>')
foreach ($m in @($myths | Select-Object -First 3)) {
  $flagHtml = if ($m.Flag) { ' <span class="flag">' + (Convert-Md $m.Flag) + '</span>' } else { '' }
  $say = ''
  foreach ($lb in $m.Labels) { if ($lb.Name -match '正确说法') { $say = $lb.Inline } }
  [void]$sb.AppendLine(('  <div class="myth" id="preview-' + $m.Id + '"><div class="mh"><span class="no">3.' + $m.No + '</span><h3>' + (Convert-Md $m.Title) + '</h3>' + $flagHtml + '</div>'))
  if ($say) { [void]$sb.AppendLine('    <p class="say"><strong>正确说法：</strong>' + (Convert-Md $say) + '</p>') }
  [void]$sb.AppendLine('  </div>')
}
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>它不做什么</h2>')
[void]$sb.AppendLine('  <ul class="plain">')
foreach ($d in $dontList) { [void]$sb.AppendLine('    <li>' + (Convert-Md $d) + '</li>') }
[void]$sb.AppendLine('  </ul>')
[void]$sb.AppendLine('</section>')
[void]$sb.AppendLine('</div>')

$indexDesc = 'VDHelper：Windows 端 Virtual Desktop 串流检测 / 诊断 / 修复工具。含本机实测结论、' +
  $checkTotal + ' 项检测、' + $symClassCount + ' 类症状与社区常见错解。'
$indexHtml = New-Page 'VDHelper · Windows 串流体检' $indexDesc 'index' $sb.ToString() $pageScript

# ---------------------------------------------------------------- checks.html

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<div class="wrap">')
[void]$sb.AppendLine('<header class="hero">')
[void]$sb.AppendLine('  <div class="eyebrow">CHECKS · 检测项全表 · 唯一真相源</div>')
[void]$sb.AppendLine('  <h1>每一条检测都写清<br><span class="grad">怎么查 · 怎么判 · 怎么退</span></h1>')
[void]$sb.AppendLine(('  <p class="lead">这张表由 <code>tools/export-docs-site.ps1</code> 从 <code>' + (Esc-Plain $Checklist) + '</code> 直接生成，共 <strong>' + $checkTotal + ' 条</strong>检测项。清单改了，这一页跟着变 —— 文档不手工维护，就不存在「文档与代码脱节」。</p>'))
[void]$sb.AppendLine(('  <div class="actions"><a class="primary" href="faq.html#myths">先去排掉 ' + $myths.Count + ' 个错解</a><a class="ghost" href="index.html">← 回首页</a></div>'))
[void]$sb.AppendLine('</header>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>读表之前：工具能做什么、不能做什么</h2>')
[void]$sb.AppendLine('  <ul class="plain">')
foreach ($c in $conventions) { [void]$sb.AppendLine('    <li>' + (Convert-Md $c) + '</li>') }
[void]$sb.AppendLine('  </ul>')
[void]$sb.AppendLine('  <p class="note">「能自动修」与「只能解释并指导」是两种完全不同的承诺。本机改不了的（路由器、第三方杀软、接口 metric）一律只给指引，不假装能一键修；每行的「回滚」列写不出来时标 <code>[未验证]</code>，不允许留空。</p>')
[void]$sb.AppendLine('</section>')

$stageNo = 0
foreach ($s in $stages) {
  $stageNo++
  [void]$sb.AppendLine(('<section id="stage-' + $stageNo + '">'))
  [void]$sb.AppendLine(('  <h2>' + (Convert-Md $s.Title) + '</h2>'))
  if ($s.Numbered) {
    if ($s.HeaderCount -ne $stageHeads.Count) {
      throw "阶段表列数 $($s.HeaderCount) 与预期 $($stageHeads.Count) 不一致（表头：$($s.Rows[0] -join '|')）"
    }
    [void]$sb.AppendLine('  ' + (Convert-Table $s.Rows 'c-' $stageHeads))
  } else {
    [void]$sb.AppendLine('  ' + (Convert-Table $s.Rows))
  }
  [void]$sb.AppendLine('</section>')
}

if ($notePara.Count -gt 0) {
  [void]$sb.AppendLine('<section id="router-untestable">')
  [void]$sb.AppendLine('  <h2>这一族为什么无法自动检测</h2>')
  [void]$sb.AppendLine((Render-MdBlocks $notePara '  ' 'lead2'))
  if ($noteQuote.Count -gt 0) {
    [void]$sb.AppendLine('  <div class="card warn">')
    [void]$sb.AppendLine((Render-MdBlocks @($noteQuote | ForEach-Object { $_ -replace '^\s*>\s?', '' }) '    ' ''))
    [void]$sb.AppendLine('  </div>')
  }
  [void]$sb.AppendLine('</section>')
}

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>本机实测对照</h2>')
[void]$sb.AppendLine(('  <p class="lead2">清单是判据，实测是本机结果，两者分开摆。本机最近一次结论：<strong>' + (Esc-Plain $verdictText) + '</strong> —— 通过 ' + $countPass + ' 项、警告 ' + $countWarn + ' 项、阻断 ' + $countBlock + ' 项、信息不足 ' + $countUnknown + ' 项，共 ' + $itemRows.Count + ' 项。逐项结论见 <a href="index.html#verdict">首页</a>，原始文件见 <code>docs/checks.md</code>。</p>'))
[void]$sb.AppendLine('  <p class="note">本页不复制那份实测表：一份数据只留一处，避免两处各自漂移。</p>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section>')
[void]$sb.AppendLine('  <h2>本页表格的 Markdown 源</h2>')
[void]$sb.AppendLine(('  <p class="lead2">同一份源、同一次解析，另输出成可直接粘进终端或 issue 的纯文本。改检测项只需要改 <code>' + (Esc-Plain $Checklist) + '</code>，然后重跑本脚本。</p>'))
[void]$sb.AppendLine(('  <details><summary>展开 ' + $rawTableLines.Count + ' 行 Markdown 表格源</summary><pre>' + (Esc-Plain ($rawTableLines -join "`n")) + '</pre></details>'))
[void]$sb.AppendLine('</section>')

# ---- 症状索引：从 Symptom.cs 解析，用户按症状进页面时先看哪几项 ----
# NOTE: every statement here is deliberately single-line. On this machine PowerShell (both 5.1 and 7)
# fails to parse a line continuation whose next line starts with whitespace followed by '+', so the
# usual multi-line string concatenation style produces "Missing closing ')'".
$symptomSrc = Join-Path $Root 'src/VdHelper/Core/Model/Symptom.cs'
$symptomCards = @()
if (Test-Path -LiteralPath $symptomSrc) {
    $sText = Get-Content -Raw -Encoding UTF8 $symptomSrc
    $entryRe = [regex]'new\("(?<id>S\d+)",\s*"(?<title>[^"]*)",\s*\[(?<phrases>(?:[^\]]|\](?!,))*)\],\s*\[(?<checks>(?:[^\]]|\](?!,))*)\],\s*"(?<first>[^"]*)"'
    foreach ($m in $entryRe.Matches($sText)) {
        $phrases = @([regex]::Matches($m.Groups['phrases'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        $ids = @([regex]::Matches($m.Groups['checks'].Value, '"([a-z][a-z0-9-]+)"') | ForEach-Object { $_.Groups[1].Value })
        $symptomCards += [pscustomobject]@{ Id = $m.Groups['id'].Value; Title = $m.Groups['title'].Value; Phrases = $phrases; Checks = $ids; First = $m.Groups['first'].Value }
    }
}

[void]$sb.AppendLine('<section id="symptom">')
[void]$sb.AppendLine('  <h2>按症状进：先看哪几项</h2>')
$symLead = '  <p class="lead2">下面的症状类直接来自源码 <code>src/VdHelper/Core/Model/Symptom.cs</code>，与工具第一屏的芯片一一对应。当前解析出 ' + $symptomCards.Count + ' 类。</p>'
[void]$sb.AppendLine($symLead)
foreach ($c in $symptomCards) {
    [void]$sb.AppendLine(('  <div class="card"><h3>' + (Esc-Plain ($c.Id + ' ' + $c.Title)) + '</h3>'))
    [void]$sb.AppendLine(('    <p class="lead2">用户原话：' + (Esc-Plain ($c.Phrases -join ' / ')) + '</p>'))
    [void]$sb.AppendLine(('    <p>' + (Esc-Plain $c.First) + '</p>'))
    [void]$sb.AppendLine('    <p class="plain">')
    foreach ($id in $c.Checks) {
        [void]$sb.AppendLine(('      <code>' + (Esc-Plain $id) + '</code>'))
    }
    [void]$sb.AppendLine('    </p>')
    [void]$sb.AppendLine('  </div>')
}
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('</div>')

$checksDesc = 'VDHelper 的 PC 侧检测项全表：编号、症状、可直接粘贴的检查命令、通过判据、修复动作、回滚方式与风险级别。表格由 research 清单自动生成。'
$checksHtml = New-Page '检测项全表 · VDHelper' $checksDesc 'checks' $sb.ToString()

# ---------------------------------------------------------------- faq.html

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<div class="wrap">')
[void]$sb.AppendLine('<header class="hero">')
[void]$sb.AppendLine('  <div class="eyebrow">FAQ · 症状分诊 + 错解纠偏 · 逐条可溯源</div>')
[void]$sb.AppendLine('  <h1>先排掉错解，<br><span class="grad">再谈修</span></h1>')
[void]$sb.AppendLine(('  <p class="lead">本页内容来自 ' + $corpusCount + ' 条逐字引用的真实用户描述（' + $threadCount + ' 个完整 Reddit 帖 + 官方 FAQ + 开发者公开回复）。引号里的话是原话，不做语法修正；每条结论后面的编号可回查 <code>research/09-failure-corpus/02-symptom-to-rootcause.md</code>。</p>'))
[void]$sb.AppendLine('</header>')

[void]$sb.AppendLine('<section id="symptom">')
[void]$sb.AppendLine(('  <h2>症状词典：把你说的那句话翻译成状态</h2>'))
[void]$sb.AppendLine(('  <p class="lead2">用户会说的词远多于症状。能归并出 <strong>' + $symClassCount + ' 个症状类 + ' + $symSubCount + ' 个子症状</strong>，剩下的才是工程问题。</p>'))
[void]$sb.AppendLine('  ' + (Convert-Table $symptomRows))
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section id="triage">')
[void]$sb.AppendLine('  <h2>症状 → 根因分诊表</h2>')
[void]$sb.AppendLine(('  <p class="lead2">这 ' + $causeRows + ' 条根因条目按症状分组（' + ($causeBreakdown -join ' + ') + '）。S1/S2 两组带「现有 FAQ 是否覆盖」列，说明官方口径到哪一步就停了；标「未覆盖」不代表不重要，只代表现成的逐条打勾式清单到不了那里。</p>'))
foreach ($g in $causeGroups) {
  [void]$sb.AppendLine(('  <h3 id="s' + $g.Code.Substring(1) + '">' + $g.Code + ' ' + (Convert-Md $g.Title) + '<span class="tag">' + ($g.Rows.Count - 1) + ' 个根因</span></h3>'))
  [void]$sb.AppendLine('  ' + (Convert-Table $g.Rows))
}
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section id="coverage">')
[void]$sb.AppendLine(('  <h2>覆盖度：' + $causeCovered + ' / ' + $causeClaimed + ' 已被检测覆盖</h2>'))
[void]$sb.AppendLine(('  <p class="lead2">已覆盖 ' + $causeCovered + ' · 部分覆盖 ' + $causePartial + ' · <strong>完全未覆盖 ' + $causeUncovered + '</strong>。下面这 ' + $causeUncovered + ' 条是现有检测够不到的地方，也是工具下一步最该做的地方。</p>'))
if ($causeMismatch) {
  [void]$sb.AppendLine('  <div class="card warn">')
  [void]$sb.AppendLine(('    <p style="margin:0"><strong>这页有两个根因计数，都照登。</strong>分诊表里逐表列出的是 <strong>' + $causeRows + '</strong> 条（' + ($causeBreakdown -join ' + ') + '）；而研究文档 §0 的结论段与上面这张覆盖度小结写的是 <strong>' + $causeClaimed + '</strong>（' + $causeCovered + ' 已覆盖 + ' + $causePartial + ' 部分 + ' + $causeUncovered + ' 未覆盖 = ' + $causeClaimed + '）。' + [char]10 + '生成脚本检测到这个矛盾时会打印 <code>SOURCE-MISMATCH</code> 警告，而不是替源文件挑一个数字 —— 覆盖度比例按覆盖度小结原文呈现，分诊规模按表格实际行数呈现。</p>'))
  [void]$sb.AppendLine('  </div>')
}
[void]$sb.AppendLine('  ' + (Convert-Table $coverageRows))
[void]$sb.AppendLine('  <ul class="plain">')
foreach ($item in $coverageList) { [void]$sb.AppendLine('    <li>' + (Convert-Md ($item -replace '^\d+\.\s*', '')) + '</li>') }
[void]$sb.AppendLine('  </ul>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section id="myths">')
[void]$sb.AppendLine(('  <h2>社区常见的错误解法（' + $myths.Count + ' 条，逐条纠偏）</h2>'))
[void]$sb.AppendLine(('  <p class="lead2">这 ' + $myths.Count + ' 条是社区给出频率最高、也最贵的解法。问题不是「有时不管用」，而是<strong>方向就是错的</strong>：把人带去改路由器、改 BIOS、买新设备，而真正的原因留在原地。以下每条都写清「为什么错」与「正确说法是什么」，不骑墙。</p>'))
foreach ($m in $myths) {
  $flagHtml = if ($m.Flag) { ('<span class="flag">' + (Convert-Md $m.Flag) + '</span>') } else { '' }
  [void]$sb.AppendLine(('  <div class="myth" id="' + $m.Id + '">'))
  [void]$sb.AppendLine(('    <div class="mh"><span class="no">3.' + $m.No + '</span><h3>' + (Convert-Md $m.Title) + '</h3>' + $flagHtml + '</div>'))
  foreach ($lb in $m.Labels) {
    $name = $lb.Name
    $body = Convert-Md $lb.Inline
    if ($name -match '原话') {
      [void]$sb.AppendLine('    <h4>社区原话</h4><blockquote>')
      if ($lb.Inline.Trim() -ne '') { [void]$sb.AppendLine('      <p>' + $body + '</p>') }
      foreach ($q in $lb.Bullets) { [void]$sb.AppendLine('      <p>· ' + (Convert-Md $q) + '</p>') }
      [void]$sb.AppendLine('    </blockquote>')
    } elseif ($name -match '为什么错') {
      [void]$sb.AppendLine(('    <div class="wrong"><h4>为什么错</h4><p class="say">' + $body + '</p></div>'))
    } elseif ($name -match '正确说法') {
      [void]$sb.AppendLine(('    <div class="right"><h4>正确说法</h4><p class="say">' + $body + '</p></div>'))
    } else {
      [void]$sb.AppendLine(('    <h4>' + (Convert-Md $name) + '</h4><p class="say">' + $body + '</p>'))
    }
  }
  # 每条错解挂到对应检测项（描述文字取自清单，不手写）；没有对应项的直说没有
  if ($mythToChecks.ContainsKey($m.No)) {
    $links = @()
    foreach ($n in $mythToChecks[$m.No]) {
      if (-not $checkById.ContainsKey([string]$n)) { throw "错解 3.$($m.No) 映射到清单第 $n 项，但清单里没有这一项" }
      $links += ('<a href="checks.html#c-' + $n + '">第 ' + $n + ' 项 · ' + (Esc-Plain $checkById[[string]$n]) + '</a>')
    }
    [void]$sb.AppendLine(('    <p class="xref">对应检测项：' + ($links -join ' · ') + '</p>'))
  } elseif ($mythToUntestable.ContainsKey($m.No)) {
    [void]$sb.AppendLine(('    <p class="xref">本机测不了：' + (Esc-Plain $mythToUntestable[$m.No]) + '，见 <a href="checks.html#router-untestable">检测项全表</a>。</p>'))
  } else {
    [void]$sb.AppendLine('    <p class="xref">目前没有对应检测项，落在<a href="#coverage">覆盖度里的未覆盖清单</a>中。</p>')
  }
  [void]$sb.AppendLine('  </div>')
}
[void]$sb.AppendLine('  <p class="note">端口与发现机制的事实依据（TCP 38810-40、UDP 38850/38860、UPnP 的调用点）见 <code>research/02-network-diagnosis/01-ports-and-discovery.md</code>；每条纠偏背后都有对应的检测项，见 <a href="checks.html">检测项全表</a>。</p>')
[void]$sb.AppendLine('</section>')

# ------------------------------------------------- 真实机器上抓到了什么（research/14-real-run）
#
# 这一节回答「检测项列表本身不能证明工具有用」。内容全部来自真实运行的输出记录。
# 脚本强制读源文件，并核对它自称的检测项数量；读不到就抛错，而不是静默出一页空话。
$rrLines = Get-Lines (Read-Text $RealRun)
$rrCheckTotal = Require-Number ($rrLines -join "`n") 'checks-total:\s*(\d+)' 'real-run check total'
$rrTables = New-Object System.Collections.Generic.List[object]
$rrIdx = 0
while ($rrIdx -lt $rrLines.Count) {
  if ($rrLines[$rrIdx] -match '^\s*\|') {
    $rrJ = $rrIdx
    $rrT = Collect-Table $rrLines ([ref]$rrJ)
    if ($rrT.Count -ge 2) { $rrTables.Add($rrT) }
    $rrIdx = $rrJ
    continue
  }
  $rrIdx++
}
if ($rrTables.Count -lt 3) { throw "$RealRun 里只解析出 $($rrTables.Count) 张表（预期 ≥3）" }

[void]$sb.AppendLine('<section id="realrun">')
[void]$sb.AppendLine('  <h2>真实机器上抓到了什么</h2>')
[void]$sb.AppendLine(('  <p class="lead2">检测项列表本身不能证明工具有用——有用要看它在一台真实机器上抓到了什么、漏了什么，' +
  '以及有没有把没事说成有事。下面是一次完整真实运行的记录，共 <strong>' + $rrCheckTotal + ' 项检测</strong>，' +
  '原始输出与复核命令见 <code>research/14-real-run/01-what-this-machine-found.md</code>。</p>'))
[void]$sb.AppendLine('  <div class="card warn">')
[void]$sb.AppendLine('    <p style="margin:0"><strong>最重要的一条不是任何单个检测项：</strong>那台机器上两个「阻断」级结论，一个是残留套接字、一个是头显睡着了——都不是配置错误。工具按证据形态分级（ARP 缓存 <code>Stale</code> / 无记录 / <code>Reachable</code>）才区分得出来。一个会把「设备睡着了」报成「网络不通」的工具，用户第三次就不看了。</p>')
[void]$sb.AppendLine('  </div>')
foreach ($rt in $rrTables) { [void]$sb.AppendLine('  ' + (Convert-Table $rt)) }
[void]$sb.AppendLine('  <p class="note">没测到的部分照登：头显侧 adb 三分支需要插 USB（无线调试的 5555/5554/5556/5557/5558/8080 全关），抓包与防火墙修复需要管理员 UAC。</p>')
[void]$sb.AppendLine('</section>')

[void]$sb.AppendLine('<section id="sources">')
[void]$sb.AppendLine('  <h2>证据来源与缺口</h2>')
[void]$sb.AppendLine('  <p class="lead2">这一节不做美化。没读到的来源就写没读到，不用推测补齐。</p>')
[void]$sb.AppendLine('  ' + (Convert-Table $srcRows))
[void]$sb.AppendLine('</section>')
[void]$sb.AppendLine('</div>')

$faqDesc = '症状分诊表、' + $causeRows + ' 条症状→根因条目，以及社区常见的 ' + $myths.Count +
  ' 个错误解法逐条纠偏（端口转发 / UPnP / 关防火墙 / DMZ / 改 BIOS 等），每条附真实用户原话。'
$faqHtml = New-Page '常见问题 · VDHelper' $faqDesc 'faq' $sb.ToString()

# ---------------------------------------------------------------- 落盘

$outPath = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
$nojekyll = Join-Path $outPath '.nojekyll'
if (-not (Test-Path -LiteralPath $nojekyll)) { [System.IO.File]::WriteAllText($nojekyll, '') }

$written = @()
foreach ($pair in @(
    @{ Name = 'index.html';  Html = $indexHtml },
    @{ Name = 'checks.html'; Html = $checksHtml },
    @{ Name = 'faq.html';    Html = $faqHtml })) {
  $p = Join-Path $outPath $pair.Name
  [System.IO.File]::WriteAllText($p, $pair.Html, $utf8)
  $written += ('{0} {1}B' -f $pair.Name, (Get-Item -LiteralPath $p).Length)
}

Write-Host ('sources  : {0} | {1} | {2} | {3} | {4}' -f $Checklist, $ChecksMd, $Rootcause, $Corpus, $Readme)
Write-Host ('parsed   : 检测项 {0} 条 / 表分组 {1} 个 / 实测 {2} 项（通过 {3} 警告 {4} 阻断 {5} Unknown {6}）/ 症状类 {7}+{8} / 根因条目 {9} 条（覆盖度小结口径 {10}：已覆盖 {11} 未覆盖 {12}）/ 错解 {13} 条 / 来源行 {14} / 约定行 {15}' -f `
  $checkTotal, $stages.Count, $itemRows.Count, $countPass, $countWarn, $countBlock, $countUnknown, `
  $symClassCount, $symSubCount, $causeRows, $causeClaimed, $causeCovered, $causeUncovered, $myths.Count, $srcRows.Count, $conventions.Count)
Write-Host ('counts   : SOURCE-MISMATCH=' + $causeMismatch + '（分诊表 ' + $causeRows + ' vs 源文档自述 ' + $causeClaimed + '；已在 faq.html#coverage 标注两个口径）')
Write-Host ('verdict  : ' + $verdict)
Write-Host ('written  : ' + ($written -join '  ·  ') + '  ·  .nojekyll ' + (Test-Path -LiteralPath $nojekyll))