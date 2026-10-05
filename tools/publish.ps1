<#
  publish.ps1 — 构建自包含单文件产物，并生成发布三件套。
  沿用旧 VDH 的发布约定：产物 = VdHelper.exe + SHA256SUMS.txt + VERSION.txt。
  用法： powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1
#>
param(
  [string]$Version = '',
  [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = (Get-Content (Join-Path $root 'VERSION.txt') -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION.txt must be x.y.z only, got '$Version'"
}

if (-not $OutDir) {
    $OutDir = Join-Path $root "dist/v$Version"
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force -LiteralPath $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "publishing v$Version -> $OutDir"
dotnet publish src/VdHelper/VdHelper.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:Version="$Version" -o $OutDir

$exe = Join-Path $OutDir 'VdHelper.exe'
if (-not (Test-Path $exe)) { throw "publish produced no VdHelper.exe in $OutDir" }

# 冒烟：无头模式跑一次体检。退出码 0/3/4 都是正常结论，5 才是运行失败。
& $exe --selftest --out (Join-Path $OutDir 'selftest.txt') | Out-Null
$code = $LASTEXITCODE
if ($code -eq 5 -or $code -gt 5) { throw "self test failed with exit code $code" }
Write-Host "self test exit=$code (0=streamable 3=at-risk 4=blocked)"

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
"$hash  VdHelper.exe" | Set-Content -Encoding ascii (Join-Path $OutDir 'SHA256SUMS.txt')
"$Version`n" | Set-Content -Encoding ascii (Join-Path $OutDir 'VERSION.txt')

Write-Host "---"
Get-Content (Join-Path $OutDir 'SHA256SUMS.txt')
Write-Host "artifacts in ${OutDir}:"
Get-ChildItem $OutDir | Select-Object Name, Length | Format-Table -AutoSize | Out-String -Width 120