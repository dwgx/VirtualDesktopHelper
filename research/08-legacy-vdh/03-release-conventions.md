# 03 — CI / 发布 / OTA 约定沿用清单

来源：`F:/Project/VirtualDesktop/_upstream/vdh/.github/workflows/ci.yml`、`F:/Project/VirtualDesktop/_upstream/vdh/README.md`、`_upstream/vdh/CHANGELOG.md`、`_upstream/vdh/VDH.bat`、`_upstream/vdh/SHA256SUMS.txt`、`_upstream/vdh/VERSION.txt`、`_upstream/vdh/.gitattributes`，以及客户端侧实现 `reference/legacy_vdh/VDH.Extra.cs:739-856`。

---

## 1. 旧版 CI 现状（逐字）

`F:/Project/VirtualDesktop/_upstream/vdh/.github/workflows/ci.yml` 全文：

```yaml
name: build

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup MSBuild
        uses: microsoft/setup-msbuild@v2
      - name: Build
        run: dotnet build VirtualDesktopHelper.sln -c Release
```

- 触发：`main` 的 push 与 PR。
- 平台：`windows-latest`。
- 只有一个 job、一个 step 是编译。
- **没有**：测试、artifact 上传、Release 创建、tag 触发、依赖缓存、`--no-restore`、警告即错误。

`VIRTUALDESKTOPHELPER.sln` 是老的 VS 格式 sln（`F:/Project/VirtualDesktop/_upstream/vdh/VirtualDesktopHelper.sln`，934 字节），因此需要 `microsoft/setup-msbuild@v2` 才有 MSBuild —— 新工程 `dotnet publish` 一条命令即可，不需要这一步。

## 2. 旧版发布约定（客户端代码与 README 双向印证）

### 2.1 产物清单

`README.md:47-49`：

```
Release assets are `VDH.exe` + `SHA256SUMS.txt` + `VERSION.txt`.
```

实测（`gh api repos/dwgx/VirtualDesktopHelper/releases`）：

| tag | 发布时间 (UTC) | assets |
| --- | --- | --- |
| v0.4.7 | 2026-08-28T10:08:01Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.6 | 2026-08-28T09:30:11Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.5 | 2026-08-28T09:12:26Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.3 | 2026-08-28T08:41:36Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.2 | 2026-08-28T08:26:12Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.1 | 2026-08-28T08:24:58Z | `SHA256SUMS.txt`, `VDH.exe`, `VERSION.txt` |
| v0.4.0 | 2026-08-28T08:20:35Z | `SHA256SUMS.txt`, `VDH.exe` ← **缺 `VERSION.txt`** |

仓库根的 `SHA256SUMS.txt` 格式：

```
59FA441B70FCB30108DB5CC1DBCA0B690AC1B5412352CA16EBC8595BBBE458A0  VDH.exe
```
（标准 `sha256sum` 格式：大写 hex + 两个空格 + 文件名）

`VERSION.txt`（仓库根，1 行）：

```
0.4.7
```

### 2.2 OTA 客户端约定（`VDH.Extra.cs`）

```csharp
// :739-741
// Do not use api.github.com — unauthenticated calls 403 when the rate limit is hit.
// /releases/latest/download/ is a static GitHub redirect, no API quota.
const string OtaVersionUrl = "https://raw.githubusercontent.com/dwgx/VirtualDesktopHelper/main/VERSION.txt";
```

```csharp
// :754-765  下载主机白名单（必须 HTTPS）
api.github.com | github.com | objects.githubusercontent.com
| release-assets.githubusercontent.com | *.githubusercontent.com
```

```csharp
// :826-842  更新流程
// 1. 询问用户
// 2. tmp = %TEMP%\VDH-<remoteVer>.exe
var tagBase = "https://github.com/dwgx/VirtualDesktopHelper/releases/download/v" + remoteVer + "/";
// 3. 从 tagBase 取 SHA256SUMS.txt
var sums = HttpGet(tagBase + "SHA256SUMS.txt", 20000);
// 4. 从 tagBase 取 VDH.exe
HttpDownload(tagBase + "VDH.exe", tmp, 120000);
// 5. 校验：算出的 SHA-256 必须出现在 SHA256SUMS.txt 里
var got = Sha256File(tmp);
if (sums.IndexOf(got, StringComparison.OrdinalIgnoreCase) < 0) { 删除 tmp; 中止; }
```

`:832` 的注释是关键工程经验：

```
// Pin the tag URL. /latest/download/SHA256SUMS.txt is CDN-cached across releases.
```

即：**不能用 `/releases/latest/download/SHA256SUMS.txt`**，因为 GitHub CDN 跨 release 缓存，会拿到旧版本的 sums。必须用 `/releases/download/vX.Y.Z/`。

### 2.3 版本号约定

`README.md:41`：

> `VERSION.txt` must be only `x.y.z` plus newline. Release must include `VDH.exe` + `SHA256SUMS.txt`.

客户端对此有防御：`SanitizeVersion()`（`:743-752`）只保留数字与 `.`、去 `v` 前缀、去首尾 `.`。
CHANGELOG 0.4.5 记录了踩坑：「OTA: strip junk from VERSION.txt」—— 也就是曾经往 `VERSION.txt` 里塞了别的东西导致比较错乱。

**但代码里版本号有 4 处**：`AppCfg.Version = "0.4.7"`（`VDH.cs:146`）、`csproj:11 <Version>`、`app.manifest:3 assemblyIdentity version="0.4.7.0"`、仓库根 `VERSION.txt`。手工同步。

### 2.4 OTA 替换自身

```csharp
// :843-849
File.WriteAllText(bat,
  "@echo off\r\nping 127.0.0.1 -n 2 >nul\r\ncopy /y \"" + tmp + "\" \"" + self + "\"\r\nstart \"\" \"" + self + "\"\r\n",
  Encoding.ASCII);
Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true });
Application.Exit();
```

### 2.5 adb 下载的来源约定（与发布无关但同属「下载安全」约定）

```csharp
// :364-367
const string url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
if (!Uri.TryCreate(url, …, out u) || u.Host.ToLowerInvariant() != "dl.google.com" || u.Scheme != Uri.UriSchemeHttps) return null;
```

CHANGELOG 0.4.4：

> adb: search SDK / PATH / VIVE / repo `quest_adb_tools`, never fall back to a bare `adb` on PATH.
> If missing: browse, or download Google `platform-tools` from `dl.google.com` only.

### 2.6 `.gitattributes`

```
* text=auto
*.cs linguist-language=C#
*.csproj linguist-language=C#
*.sln linguist-language=C#
*.md linguist-documentation
*.ps1 linguist-vendored
*.bat linguist-vendored
*.py linguist-vendored
*.ico binary
```

### 2.7 `VDH.bat`（本地便捷，非发布）

```bat
@echo off
cd /d "%~dp0"
set EXE=%~dp0VirtualDesktopHelper\bin\Release\VDH.exe
if not exist "%EXE%" (
  dotnet build "%~dp0VirtualDesktopHelper.sln" -c Release
  if errorlevel 1 ( echo Install Visual Studio 2022 with .NET desktop development, or the .NET SDK. & pause & exit /b 1 )
)
start "" "%EXE%"
```

---

## 3. 沿用 / 改掉 清单

### 3.1 **必须保留**（Owner 自己踩出来的、有理由的约定）

| # | 约定 | 证据 / 理由 |
| --- | --- | --- |
| K1 | **产物三件套**：主 EXE + `SHA256SUMS.txt` + `VERSION.txt` | `README.md:47-49` + 7 个 release 的实测 assets |
| K2 | **`SHA256SUMS.txt` 用标准 `sha256sum` 格式**（`<大写HEX>␠␠<文件名>`） | `_upstream/vdh/SHA256SUMS.txt:1` |
| K3 | **下载后必须核对 SHA-256，且必须出现在 `SHA256SUMS.txt` 里** | `VDH.Extra.cs:836-842`；README「SHA-256 of `VDH.exe` must appear in `SHA256SUMS.txt`」 |
| K4 | **SHA-256 不符 → 删临时文件 + 中止 + 明示**，绝不「提示一下然后照装」 | `VDH.Extra.cs:838-841` |
| K5 | **只从 `github.com/…/releases/download/vX.Y.Z/` 下载**，不跟随 `latest` | `VDH.Extra.cs:832` 注释 + CHANGELOG 0.4.1→0.4.5 的修复史 |
| K6 | **不用 `api.github.com`**（匿名调用撞限流 403） | `VDH.Extra.cs:739-740` 注释 + CHANGELOG 0.4.1「OTA no longer calls api.github.com」 |
| K7 | **主机白名单，每一跳重定向都重新校验** | `VDH.Extra.cs:754-765`（`HostAllowed`）+ `:779`、`:800` |
| K8 | **只允许 HTTPS** | `VDH.Extra.cs:758` |
| K9 | **没有「自定义更新源」输入框** | README「No URL box」；`VDH.Extra.cs:330` 的按钮文案也写「不会跟你填的网址走」 |
| K10 | **`VERSION.txt` 内容只能是 `x.y.z` 加一个换行** | `README.md:41` + `SanitizeVersion` 防御 |
| K11 | **`AppendTargetFrameworkToOutputPath=false`** —— 产物平铺在 `bin\Release\VDH.exe` | `csproj:14`。这是 OTA 能按固定文件名下载的前提；换 TFM 后不加这一条路径会变成 `bin\Release\net10.0-windows\VDH.exe` |
| K12 | **`asInvoker` 不提权** + PerMonitorV2 DPI | `app.manifest:5`、`:21-22` |
| K13 | **CI 触发 = `main` push + `main` PR，runner = `windows-latest`** | `ci.yml:3-6`、`:11` |
| K14 | **adb 只从 Google `dl.google.com` 下载，不从第三方** | `VDH.Extra.cs:364-367` + CHANGELOG 0.4.4 |
| K15 | **`app.manifest` / `.gitattributes` 沿用** | 见 §2.6 |
| K16 | **CHANGELOG.md 随包发布**（旧版没做到，新版补上） | 旧版 `VDH.cs:530` 期望但 assets 里没有 → 用户永远看 0.2 日志 |

### 3.2 **必须改掉**

| # | 旧做法 | 新做法 | 理由 |
| --- | --- | --- | --- |
| C1 | 只 `dotnet build` | `dotnet test` + `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` + 上传 artifact | 2160 行代码零测试；发布物应当由 CI 产出而非手工 |
| C2 | `microsoft/setup-msbuild@v2` | 删除。`dotnet publish` 自带 MSBuild | .NET 10 SDK 已装（实测 `10.0.400`），不需要 VS 工作负载 |
| C3 | 旧式 `.sln`（`dotnet build X.sln`） | 单 csproj 或新式 `.slnx`，`dotnet build src/` | 少一层间接 |
| C4 | 版本号 4 处手写（`AppCfg.Version` / `csproj` / `app.manifest` / `VERSION.txt`） | **单一来源**：`<Version>` → `AssemblyInformationalVersion` → 构建脚本写 `VERSION.txt`；`app.manifest` 的 `assemblyIdentity` 由 MSBuild 属性注入 | 漏改一处就 OTA 错乱（0.4.5 已踩） |
| C5 | `SHA256SUMS.txt` 判定用 `IndexOf` 子串（`:837`） | 逐行解析 `<hex>␠␠<name>`，比对**文件名 + hex** | 支持多产物；语义正确 |
| C6 | 版本解析失败退回字符串比较（`:818-820`） | 解析失败 → 明确报错，不猜 | 会提示升级到不存在的版本 |
| C7 | 手写 `vdh-swap.bat` 覆盖自身 | 自带 updater 模式：`VDH.exe --apply-update <tmp>`；先 `MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)` 或退到后台进程再替换；失败保留原文件并报错 | bat 的 `ping -n 2` 是猜的等待；copy 失败 = 用户既没更新也没退出 |
| C8 | `HttpWebRequest` 手工跟重定向，无跳数上限 | `HttpClient` + `AllowAutoRedirect=false` + 自写跳转循环，**上限 5 跳** | 自指 `Location` 会栈溢出 |
| C9 | `WebRequest`/`WebClient`（`SYSLIB0014` 已废弃） | `HttpClient` 单例注入 | 实测 `net10.0-windows` 下 `WebRequest.Create` 只剩 `warning SYSLIB0014` |
| C10 | 从 `main` 分支 raw 读 `VERSION.txt` | 从 `https://github.com/<owner>/<repo>/releases/latest` 或一个**专用 branch**（如 `release`）读；**更好**：用 `https://github.com/<owner>/<repo>/releases/latest/download/VERSION.txt`（GitHub 每次 redirect 到具体 tag，不跨 release 缓存 —— 与 K5 的 `SHA256SUMS.txt` 不同） | tag 内容与 main 内容可不一致 |
| C11 | Release 全手工上传（7 个全是手工） | tag push 触发 workflow：`gh release create` + `gh release upload` + 自动生成 `SHA256SUMS.txt` | 手工 = 漏传（v0.4.0 漏 `VERSION.txt`）+ 手算 SHA |
| C12 | 单 EXE（靠 `EnsureLz4` 内嵌 dll 补救） | 自包含单文件 `PublishSingleFile=true`（原生支持，不需 `AssemblyResolve`） | `EnsureLz4`（`VDH.cs:969-1007`）整套机制在 .NET 10 不需要 |
| C13 | `LangVersion=7.3` / `Nullable=disable` | 现代默认 / `Nullable=enable` | 放弃新语言特性是可维护性负债 |
| C14 | `net48` | `net10.0-windows` + `UseWPF=true` | 项目已定技术栈（AGENTS.md §3） |
| C15 | 白名单含 `api.github.com`（`:760`）但代码从不用 | 从 `HostAllowed` 移除 | 缩小可下载源面 |
| C16 | adb 下载不校验 SHA-256、不限大小、先删后解 | 流式下载 + 大小上限 + **SHA-256 校验**（Google 为 `platform-tools_rXX.Y.Z-windows.zip` 发布官方 SHA256）+ 解到临时目录 → 校验 → 备份旧目录 → 原子切换 | 下载链与自更新链同等对待 |
| C17 | `app.manifest` 里 `assemblyIdentity version="0.4.7.0"` 手写 | MSBuild 注入 | 同 C4 |

### 3.3 **新增**（旧版没有，新工程该有）

| # | 新增项 | 说明 |
| --- | --- | --- |
| N1 | **PR / push 跑测试** | `dotnet test` 必须在 CI 里是硬门（`--no-restore` + 覆盖率可选） |
| N2 | **CI 上传 artifact** | 每次 `main` 构建产出 zip（含 EXE + sums + VERSION + CHANGELOG），不必等发版 |
| N3 | **tag 触发自动 Release** | `on: push: tags: 'v*'` → build → 算 SHA → `gh release create` |
| N4 | **`SHA256SUMS.txt` 由构建生成** | 不手算。`Get-FileHash -Algorithm SHA256` 或 bash `sha256sum` |
| N5 | **`VERSION.txt` 由构建生成** | 从 csproj `<Version>` 读，写入 artifact；内容严格 `x.y.z\n`（加一个断言检查） |
| N6 | **tag 内容 == VERSION.txt 内容断言** | CI 里校验 `git tag` 与 csproj `<Version>` 一致，不一致就 fail。旧版没有这道闸，0.4.5 踩过 |
| N7 | **发布 smoke test** | 构建产物能否启动（`--version` 参数打印版本后退出） |
| N8 | **OTA 回滚路径** | Release 失败可重发同 tag（GitHub 支持）；`VERSION.txt` 回滚即可让所有客户端停止推送 |
| N9 | **`--diagnostics` CLI 参数** | 让 CI / 用户在无 UI 环境导出诊断（对应新工程的核心价值） |

---

## 4. 建议的 `.github/workflows/` 骨架（沿用 K13 / C1 / C2 / C3 / N1-N7）

> 以下是**约定草案**，不是已验证可跑的 workflow（未在本轮跑过）。落地时按仓库实际 csproj 路径调整。

```yaml
name: build
on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:
    branches: [main]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Assert tag matches project version
        shell: pwsh
        run: |
          if ('${{ github.ref_type }}' -eq 'tag') {
            $tag = '${{ github.ref_name }}' -replace '^v',''
            $proj = (Select-String -Path src/*/*.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
            if ($tag -ne $proj) { throw "tag $tag != project version $proj" }
          }
      - name: Test
        run: dotnet test src/ -c Release
      - name: Publish
        run: >
          dotnet publish src/VDHelper.csproj -c Release -r win-x64 --self-contained true
          -p:PublishSingleFile=true -p:PublishReadyToRun=true
          -p:AppendTargetFrameworkToOutputPath=false -o artifacts
      - name: Generate VERSION.txt and SHA256SUMS.txt
        shell: pwsh
        run: |
          $v = (Select-String -Path src/*/*.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
          "$v" | Out-File -Encoding ascii artifacts/VERSION.txt
          Copy-Item CHANGELOG.md artifacts/
          $lines = Get-ChildItem artifacts -File | Where-Object Name -ne 'SHA256SUMS.txt' |
                   ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.Name)" }
          $lines | Out-File -Encoding ascii artifacts/SHA256SUMS.txt
      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with: { name: VDHelper, path: artifacts/ }
      - name: Publish release
        if: startsWith(github.ref, 'refs/tags/v')
        env: { GH_TOKEN: ${{ secrets.GITHUB_TOKEN }} }
        run: gh release create '${{ github.ref_name }}' artifacts/* --generate-notes
```

要点：
- `AppendTargetFrameworkToOutputPath=false` 必须带上（K11）。
- `VERSION.txt` 与 `SHA256SUMS.txt` 用 PowerShell 生成（runner 是 Windows，path 分隔符天然对）。
- `SHA256SUMS.txt` 里排除自己。
- tag ≠ 项目版本 → 直接 fail（N6）。

---

## 5. OTA 客户端沿用清单（新工程照着实现）

| 步骤 | 沿用 | 改动 |
| --- | --- | --- |
| 版本源 URL | K10 格式约束 | C10：改用 release asset 或专用 branch，不用 `main` raw |
| 下载主机白名单 | K7 K8 | C15：去掉 `api.github.com`；C8：加跳数上限 |
| 比较版本 | `Version.TryParse` 优先 | C6：解析失败报错，不退化字符串比较 |
| 取 sums + exe | K5（tag 固定 URL） | 保留 |
| 校验 | K3 | C5：逐行解析并比对文件名 |
| 失败处理 | K4 | 保留（删临时文件 + 明示 + 中止） |
| 替换自身 | — | C7：改自带 updater，不用 bat |
| 失败回滚 | — | N8：保留原文件并报错；不产生半更新状态 |
| 启动时检查开关 | `cfg.CheckUpdates`（默认 true，`VDH.cs:143`） | 保留；改为设置项 + 记住选择 |
| 无更新时的行为 | 交互模式弹「已是当前版本 X」（`:824`） | 保留 |

---

## 6. 未验证项

- `[未验证]` `https://github.com/<owner>/<repo>/releases/latest/download/VERSION.txt` 是否与 `SHA256SUMS.txt` 一样受 CDN 跨 release 缓存影响。旧版只对 `SHA256SUMS.txt` 断言了会缓存（`:832` 注释），未对 `VERSION.txt` 断言。验证条件：连续发两个 release，各自查 `latest/download/VERSION.txt` 是否立刻返回新值。
- `[未验证]` `dotnet publish -p:PublishSingleFile=true` 产出的 WPF 单文件 EXE 在 .NET 10 下能否正常启动（WPF + 单文件有已知的历史坑，如 `Assembly.Location` 为空、`Extract` 行为）。验证条件：本机构建一次并启动。当前旧版的 `EnsureLz4`（`VDH.cs:969-1007`）说明旧版是靠自建解包解决的，新版要确认原生单文件是否已够。
- `[未验证]` GitHub Actions `windows-latest` 上 `PublishReadyToRun` + WPF 的交叉裁剪（`PublishTrimmed`）是否可行。WPF 不支持 trimming，若要减小体积只能靠 R2R 或 ReadyToRun + 手动排除。
- `[未验证]` `actions/setup-dotnet@v4` 装 `10.0.x` 在 runner 上的可用性（本机是 `10.0.400`，需实测 CI 上能解析到 `10.0.x`）。
