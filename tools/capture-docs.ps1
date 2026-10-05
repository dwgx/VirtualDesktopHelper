param([string]$Page = 'index', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Out -eq '') { $Out = Join-Path $root ('_p_' + $Page + '.png') }
if (-not [System.IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).Path $Out }
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path -LiteralPath $chrome)) { throw "Chrome not found at $chrome" }
$url = 'file:///' + ((Join-Path $root ('docs\' + $Page + '.html')) -replace '\\', '/')
# --force-prefers-reduced-motion matters: the stat cards count up over ~1.1s, so a screenshot taken
# straight after load catches them mid-animation. It once made 35 look like 3 and looked like a
# stale-number bug. With this flag the page renders its final values.
& $chrome --headless=new --disable-gpu --no-sandbox --hide-scrollbars --force-prefers-reduced-motion --window-size=1280,1100 --screenshot=$Out $url | Out-Null
Get-Item -LiteralPath $Out | ForEach-Object { "saved=$($_.Name) $($_.Length)B" }