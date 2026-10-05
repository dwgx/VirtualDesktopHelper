param([string]$Page = 'index', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Out -eq '') { $Out = Join-Path $root ('_p_' + $Page + '.png') }
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path -LiteralPath $chrome)) { throw "Chrome not found at $chrome" }
$url = 'file:///' + ((Join-Path $root ('docs\' + $Page + '.html')) -replace '\\', '/')
& $chrome --headless=new --disable-gpu --no-sandbox --hide-scrollbars --window-size=1280,1100 --screenshot=$Out $url | Out-Null
Get-Item -LiteralPath $Out | ForEach-Object { "saved=$($_.Name) $($_.Length)B" }