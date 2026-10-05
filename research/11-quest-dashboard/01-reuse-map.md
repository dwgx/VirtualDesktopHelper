# 01 — `dwgx/Quest-ADB-Dashboard` 复用评估（第三屏「头显诊断」）

> 审计对象：Owner 自有仓库 [`dwgx/Quest-ADB-Dashboard`](https://github.com/dwgx/Quest-ADB-Dashboard)
> 只读克隆到 `reference/quest-adb-dashboard/`，未修改仓库任何文件、未 commit、未下载 platform-tools。
> 审计日期 2026-10-05。所有元数据为 `gh api` 实查，所有行号为克隆后 `main`（`a845caa`）实测。

---

## 0. 结论速览（TL;DR）

| 问题 | 结论 |
|---|---|
| 许可证 | **MIT**，`LICENSE:1-3` 版权声明为 `Copyright (c) 2026 dwgx1337` |
| 能编译吗（`net10.0-windows`） | **能，实跑通过**：0 错误、2 个 `SYSLIB0014` 警告，**零 NuGet 包引用** |
| 它做了什么 | 头显侧 ADB 只读采集（29 条命令）+ WebUI + share-safe/private-full 双版 HTML 报告 + MCP 服务器 + APK 解析安装 |
| 它**没**做什么 | **零 PC 侧逻辑**。不读网卡、防火墙、ICS、`StreamerSettings.json`、不探 VD 端口、不做跨端归因 |
| 对 VDHelper 的价值 | **高，但只在「头显侧采集层 + 报告脱敏层」**；产品形态（第三屏）和诊断结论层**全部要自己写** |
| 搬运策略 | 采集器与脱敏器**改写后搬**（去掉 WebUI/安装/APK 解析）；报告模板**参考**；APK/安装/MCP/BAT **不搬** |

**一句话**：这个仓库把「头显侧怎么读」这件事做完了并且做得比我们会做得好，但它**不知道 PC 侧发生过什么**，
所以「端到端归因」——也正是 VDHelper 第三屏真正要卖的东西——必须我们自己做。

---

## 1. 仓库元数据（`gh api repos/dwgx/Quest-ADB-Dashboard` 实查）

```json
{"archived":false,"created_at":"2026-06-09T04:26:44Z","default_branch":"main",
 "description":"Meta Quest ADB diagnostics dashboard and share-safe HTML report exporter",
 "forks_count":0,"full_name":"dwgx/Quest-ADB-Dashboard","language":"C#",
 "license":"MIT","open_issues_count":0,"pushed_at":"2026-09-30T19:41:55Z",
 "size":978,"stargazers_count":4,"updated_at":"2026-09-30T19:42:00Z"}
```

- **languages**（`gh api .../languages`）：`{"C#":155492,"Python":76710,"HTML":73800,"PowerShell":23655}` 字节
- **6 个 release**，最新 `v0.3.0`（2026-08-27，「App library, headset settings, i18n」）
- **commits**：`git shortlog -sne --all` → `20 dwgx` + `4 dependabot[bot]` + `1 github-actions[bot]`
- **issue**：`open_issues_count: 0`（与 `research/01-community-repos/01-inventory.md:72` 记录一致，4 条全是 Dependabot PR）

**最新提交**：`a845caa37bd3ab21e81ccf6c1770dd79d2291a71  2026-10-01 04:41:53 +0900  dwgx`

---

## 2. 代码盘点

### 2.1 规模（`git ls-files | wc -l`，15604 行含二进制）

| 文件 | 行数 | 语言 | 职责 |
|---|---:|---|---|
| `dist/Quest_ADB_Tools.bat` | 7736 | batch | 端用户单文件入口 + 内嵌 base64 EXE payload + adb 发现 |
| `src/QuestAdbWebUi.cs` | **2769** | C# | **全部核心逻辑**：采集器 + HTTP 服务 + 报告生成 + APK 解析 + 安装 |
| `mcp/quest_adb_control_mcp.py` | 919 | Python | 可选 MCP（两阶段确认的写操作） |
| `src/QuestAdbWebUi.html` | 610 | HTML | WebUI 前端（base64 嵌进 .cs） |
| `mcp/quest_adb_safe_mcp.py` | 493 | Python | 只读 MCP（CI/agent 用） |
| `tests/test_control_mcp_policy.py` | 314 | Python | 控制 MCP 策略测试 |
| `scripts/generate-sample-reports.ps1` | 133 | PowerShell | 生成合成样例报告 |
| `tests/test_safe_mcp_policy.py` | 132 | Python | 只读 MCP 策略测试 |
| `scripts/smoke-safe-mcp.ps1` | 129 | PowerShell | MCP 协议冒烟 |
| `scripts/build-webui.ps1` | 67 | PowerShell | 构建：HTML→base64→EXE→BAT |
| `docs/*.md` ×10 | ~1000 | Markdown | 方法论、导出说明、安全边界 |

**架构形态**：单文件 C# `class QuestAdbWebUi`（`src/QuestAdbWebUi.cs:13`），**无 namespace**（全文件 `grep -c '^namespace'` = 0），
无依赖注入，全部是 `static` 方法。嵌入类型只有 7 个（`grep` 实测）：

```
QuestAdbWebUi:13   class CmdResult   :25   class Capture   :34   class Snapshot  :46
                  class ApkInfo     :786  class PkgListItem :2296 class PkgDetail :2305
```

### 2.2 框架与依赖：**没有任何项目文件**

```
$ find . -name "*.csproj" -o -name "*.sln" -o -name "*.props" -o -name "*.targets" -o -name "package.json"
（无输出，只有 mcp/requirements.txt）
```

构建方式在 `scripts/build-webui.ps1:13`：

```powershell
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
```

即 **Windows 自带的 .NET Framework 4.x C# 编译器**（C# 5 语言级别），然后
`:38` 把 HTML 读成 base64、`:33` 替换源码里的 `__HTML_BASE64_LINES__` 占位符（占位符在 `QuestAdbWebUi.cs:2267`），
`:39` 编译成 EXE，`:52-64` 再把 EXE base64 塞回 BAT 的 `:write_webui_payload` 标签。

**唯一的包引用**：`mcp/requirements.txt` → `mcp>=1.28.1`、`Pillow>=12.3.0`（Python 侧，与 C# 无关）。

C# 侧的 12 个 `using`（`QuestAdbWebUi.cs:1-12`）全部是 BCL：
`System`, `System.Collections.Generic`, `System.Diagnostics`, `System.Globalization`, `System.IO`,
`System.IO.Compression`, `System.Net`, `System.Net.Sockets`, `System.Text`, `System.Text.RegularExpressions`, `System.Threading`。

**语言特性探测**（实查）：字符串插值 `$"` = **0**、`nameof(` = **0**、lambda/表达式体 `=>` = 2、`??` = 26。
→ 代码本身刻意停留在 C# 5 可编译子集内，这也解释了为什么它在 .NET Framework 上能跑。

---

## 3. 兼容性核查（**实跑**，不是推断）

`net10.0-windows` 下复现其构建（把 `__HTML_BASE64_LINES__` 换成合法字面量，其余原样）：

```
$ dotnet --version
10.0.400
$ dotnet --list-sdks
10.0.400 [C:\Program Files\dotnet\sdk]
```

```
$ dotnet build -c Release
  Determining projects to restore...
  Restored probe.csproj (in 49 ms).
  probe -> ...\bin\Release\net10.0-windows\QuestAdbWebUi.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:00.86
```

去掉 `NoWarn` 后的**真实**告警只有 2 条：

```
QuestAdbWebUi.cs(2739,19): warning SYSLIB0014: 'ServicePointManager' is obsolete...
QuestAdbWebUi.cs(2740,35): warning SYSLIB0014: 'WebClient.WebClient()' is obsolete...
    0 Error(s)
    2 Warning(s)
```

两条都在 platform-tools **下载路径**（`:2739-2740`），与诊断采集无关。

**运行时自检也通过**：

```
$ dotnet bin/Release/net10.0-windows/QuestAdbWebUi.dll --self-test
QuestAdbWebUi self-test PASS
EXITCODE=0
```

`--self-test` 覆盖 `SelfTestRedaction`（`:126`，脱敏回归）与 `SelfTestApkParse`（`:148`，APK 解析回归），
是纯离线的，不碰 adb。

### 兼容性结论

| 维度 | 判定 | 依据 |
|---|---|---|
| `net10.0-windows` 编译 | ✅ 通过 | 上方 `dotnet build` 实跑输出 |
| NuGet 依赖 | ✅ 零 | `QuestAdbWebUi.cs:1-12` 全 BCL；仓库无任何 `PackageReference` |
| 语言级别 | ✅ 兼容 | 探测无 C#6+ 语法 |
| 唯一迁移点 | ⚠️ `WebClient`/`ServicePointManager` | `:2739-2740`，改 `HttpClient` 即可；VDHelper 若不需要下载 platform-tools，**整段不搬** |
| 反向不兼容 | ⚠️ 无 namespace + 全 `static` | 无法直接作为库引用；必须「抄代码进我们的命名空间」而非「加 ProjectReference」 |

**不要**为它新建 `.csproj` 并 `ProjectReference`——那会把一个无 namespace 的 2769 行静态类拖进我们的程序集。
**结论：代码级复用（复制函数体 + 改名），不做程序集级依赖。**

---

## 4. 模块盘点（逐模块判定）

图例：`直接搬` = 几乎原样复制；`改写后搬` = 取函数体，改命名/签名/去 WebUI 依赖；`参考` = 读思路不抄代码。

| 模块 | 文件:行 | 干什么 | 依赖 | 对 VDHelper 的价值 | 复用决策 |
|---|---|---|---|---|---|
| **进程执行器** | `src/QuestAdbWebUi.cs:1943-1967` `RunResult` | 起 `Process`，双线程抽 stdout/stderr，`WaitForExit(timeout)`，超时 `Kill()`，`TimedOut` 标记 | `System.Diagnostics` | **高**。VDHelper 第三屏所有 adb 调用都要它。⚠️ `psi.Arguments` 是手拼字符串 | **改写后搬**：`ArgumentList` 替代手拼（.NET Core 原生），`Clean()` 空串约定要去掉 |
| 流式执行器 | `:1973-2006` `RunStream` | 逐行回调 + `lock` 线程安全 | 同上 | 中。VDHelper 只需要「跑完拿结果」，无 APK 安装进度 | **不搬** |
| 参数拼接 | `:2008-2009` `JoinArgs`/`QuoteArg` | 按需加引号 | — | 低。`QuoteArg` 不转义 `\`、`%`、`!`，是**有缺陷**的实现 | **不搬**（用 `ArgumentList`） |
| **采集编排** | `:1719-1758` `CollectSnapshot` | 29 条 `AddShellCapture`/`AddCapture`，每条带独立超时（3s–12s） | `RunResult` | **最高**。这就是「一次跑完头显体检」的命令清单与超时预算 | **改写后搬**：命令清单重排（见 §5.2），超时改为并行 + 统一预算 |
| 采集结果模型 | `:34-44` `Capture` | `Name/Command/Output/Error/ExitCode/TimedOut/DurationMs` | — | **高**。「每个结论带产生它的命令+耗时+退出码」正是 VDHelper `CheckResult.Evidence` 想要的 | **改写后搬**（并入 `CheckResult.Evidence`） |
| 快照模型 | `:46-54` `Snapshot` | `Created/Serial/DeviceLine/Fields/Captures/Warnings` | — | **高** | **改写后搬** |
| **字段解析** | `:1790-1861` `FillSnapshotFields` | 把 29 份原始输出压成 ~45 个 `Dictionary<string,string>` 字段 | 各 `*Summary` 助手 | **高** | **改写后搬** |
| 解析助手 | `:2014-2032`（`AfterColon`/`AfterEquals`/`FindLine`/`Field`/`Between`/`RegexValue`/`MemGb`/`PropFrom`…） | 20+ 个单行 `static` 文本提取器 | `Regex` | **高**。和我们的 `CheckResult` 判读方式同构 | **改写后搬**（改 `static`→`internal static` + 加 `RegexOptions.Compiled`） |
| 摘要助手 | `:2068-2182`（`StorageSummary`/`MemorySummary`/`CpuSummary`/`DisplaySummary`/`ThermalSummary`/`UsbSummary`/`WifiSummary`/`BluetoothSummary`/`CameraSummary`/`FactorySummary`/`VirtualDesktopSummary`） | 每项一行可读摘要 | 同上 | **高** | **改写后搬** |
| **VD 识别** | `:1661-1671` `FillVirtualDesktop` + `:2177-2182` `VirtualDesktopSummary` | `dumpsys package VirtualDesktop.Android`，认 `Package [...]` 头，抽 `versionName=` | — | **中**。包名对（`research/06-adb-headset/01-adb-playbook.md` §3 证实），但**只做存在性+版本**，无权限/进程/签名判定 | **参考**（包名常量直接采信） |
| **脱敏器** | `:2199-2228` `RedactLoose`/`Redact` + `:2229-2233` `SerialMask` | 9 条正则：MAC、IPv6 三形态、IPv4、getprop 序列号、SSID/BSSID、fingerprint、session | `Regex` | **最高**。share-safe 报告的**核心资产**；VDHelper 报告导出直接要 | **改写后搬**（`Compiled` + 补 `user:pass@`、Wi-Fi 配网、`ro.boot.*`） |
| **报告生成器** | `:1863-1893` `BuildReportHtml` | 单文件 HTML：发票式审计单布局，内联 `<style>`，零外部依赖 | `WebUtility` | **高**。「表格 = 字段/值/**证据来源**」三列范式与我们的 `Evidence` 完全同构 | **参考**（结构照抄，字段清单换成 VD 的） |
| 报告分节 | `:1895-1909` `AddInvoiceFacts` | `"key|标签|证据来源"` 紧凑 DSL → `<table>` | — | **高**。这个三段式 DSL 很干净 | **改写后搬** |
| 报告附录 | `:1911-1923` `AddInvoiceRaw` | 每条 capture 一个 `<details>`，含耗时/exit/timeout；safe 版跳过 logcat；60000 字符截断 | — | **高** | **改写后搬** |
| 报告样式 | `:1873-1874`（`:root` CSS 变量） | `--page:#eef1f5 --paper:#fff --ink:#182033 --accent:#1d4ed8` | — | **中**。浅色发票风，与 VDHelper 官方 MetroDark **冲突** | **参考结构，不参考配色** |
| 报告标题/编号 | `:1866-1868` | `SHARE-SAFE` / `PRIVATE FULL` 印章 + `QADB-yyyyMMdd-HHmmss` 编号 | — | 中 | **参考** |
| 归因边界声明 | `:1887` | 「不能把 `location_id`/`station_id` 翻译成国家/城市/工厂」 | — | **中**。同一种「ADB 不能证明什么」的态度，值得抄进 VDHelper 报告 | **参考** |
| adb 发现（BAT） | `dist/Quest_ADB_Tools.bat:89-143`（`::find_adb`）+ `:232-257`（`:print_adb_candidate` 扫描输出） | 顺序候选表：`ADB_EXE` → 自身目录 → `for %%D in (C D E)` 扫 SDK → `%LOCALAPPDATA%` → `%ProgramFiles%` → SideQuest → Meta Quest Developer Hub → VIVE Hub（7 条） | 环境变量 | **高** | **参考**。比旧 VDH 的 18 条候选**更短但覆盖更全**（多了 Meta Quest Developer Hub）；⚠️ 它**不校验版本**、**不写回配置** |
| adb 发现（Python） | `mcp/quest_adb_safe_mcp.py:136-181` `_sdk_root_candidates`/`_find_adb` | 同上逻辑的 Python 版 + `shutil.which` + Oculus `oculus-diagnostics` 路径（`:164`） | `pathlib` | 中 | **参考**（`oculus-diagnostics` 那条路径是旧 VDH 没有的） |
| adb 来源标注 | `:2189-2198` `AdbSourceLabel` | 把路径翻译成人类标签（`VIVE Business Streaming ADB` / `Android platform-tools ADB`） | — | **中**。报告里显示「ADB 来自哪」是好习惯 | **改写后搬** |
| 设备选择 | `:1448-1536` `CurrentSerial`/`InitLog`/`ReadLogTail`/`SelectDevice` | 从 `adb devices -l` 挑设备 | — | 中 | **参考**（⚠️ 它的 `_select_device` `quest_adb_safe_mcp.py:282-304` 只挑 `pool[0]`，**没有多设备 UI**，而 playbook §2.3 要求必须处理多设备） |
| 单次 prop/settings 读 | `:1538-1543` `Prop`/`Setting`/`Sh`/`A`/`MustSh`/`MustA` | 6 个一行封装，`Clean()` 把空串变 `"-"` | `RunResult` | **高**。逐项检查（而不是全量快照）就用这套 | **改写后搬**（去掉 `"-"` 哨兵，改 `null`） |
| 日志 | `:1454-1502` `InitLog`/`Log`/`ReadLogTail` | 追加写 + 读尾，供 `/api/log` | — | 中 | **参考** |
| i18n | `:2272-2326` `DetectLang`/`NormalizeLang`/`T` | 按 Windows UI 语言 + 头显 locale 选语言 | — | 低（VDHelper 单语言） | **不搬** |
| WebUI HTTP 服务 | `:349-798` `Serve`/`ReadRequestHead`/`StreamBodyToFile`/`DrainBody` + `:56-101` `Main` | `TcpListener` 绑 `127.0.0.1`，8765–8785 端口扫描，token 鉴权（`:2256`） | `System.Net.Sockets` | **零**。VDHelper 是原生 WPF，不需要 WebUI | **不搬** |
| SSE | `:1209-1243` | 安装进度推送 | — | 零 | **不搬** |
| APK 解析（AXML） | `:799-1030` `ParseApk`/`ParseAxml`/`ReadStringPool`/`ExtractZipEntry` | 纯手写 ZIP+二进制 XML 解析，读包名/版本/权限 | `System.IO.Compression` | **零**（VDHelper 是诊断工具，不装包） | **不搬** |
| APK 安装 | `:1245-1332` `ApkInstallStream` + `:1333-1377` | SSE 流式 `adb install` + 错误翻译 | — | 零 | **不搬** |
| 应用库 / 包管理 | `:2367-2667` `ParsePmList`/`ParsePackageDump`/`IsUserPackage`/`AppOpNeedsConfirm` | 列包、详情、卸载/强停/清数据（确认门控） | — | 低 | **不搬** |
| 头显设置写入 | `:1429-1447` `DebugMode`/`Conservative` + `:2238-2252` `DangerousAction`/`DeniedSetting` | `settings put`、keyevent、wireless ADB；危险动作清单 + 拒写设置黑名单 | — | **中**。VDHelper 的 `FixAction` 需要同样的「危险动作白/黑名单」 | **参考**（`:2245-2252` 的 10 条 `DeniedSetting` 值得抄进我们的 `Fixes.cs`） |
| 备份/恢复 | `:1552-1599` `EnsureBackup`/`RestoreBackup`/`WriteBackupLine` | 改设置前逐条备份，可还原 | — | **中**。与 VDHelper `FixAction.Backup/Rollback` 同构 | **参考** |
| 危险命令拦截 | `mcp/quest_adb_safe_mcp.py:73-135` `BLOCKED_ADB_COMMANDS`/`BLOCKED_SHELL_PREFIXES` + `:212-233` `_refuse_if_dangerous` | 状态改动词前缀黑名单 + `cmd` 白名单 | — | **高**。我们做「只读第三屏」时的防线范式 | **参考** |
| BAT 启动器 | `dist/Quest_ADB_Tools.bat` 全文 7736 行 | 单文件分发 + 内嵌 EXE + SHA256 自校验（`build-webui.ps1:66-73`） | — | **零**（我们发 exe，不发 bat） | **不搬** |
| MCP 服务器 | `mcp/quest_adb_safe_mcp.py` / `quest_adb_control_mcp.py` | 5 / N 个 `@mcp.tool()` | `mcp`, `Pillow` | 零（VDHelper 无 MCP 需求） | **不搬** |
| CI | `.github/workflows/ci.yml:1-69` | 编译 MCP + 策略测试 + 构建 WebUI + `--self-test` + BAT 冒烟 + 「危险命令族仍在文档中」反向断言 | — | **中**。`:47-67` 那个「这些危险串必须仍出现在文档里」的反向断言很聪明，可作我们的回归护栏 | **参考** |

---

## 5. 关键判定：它做了什么、没做什么

### 5.1 它做了什么（已完成、质量高）

1. **头显侧只读采集器**：29 条命令一次跑完（`:1726-1754`），每条独立超时，输出原样留存 + 耗时 + 退出码 + 是否超时。
2. **字段压缩层**：把 29 份原始输出压成约 45 个可读字段（`:1790-1861`）。
3. **双档报告导出**：`share-safe`（9 类敏感信息遮蔽）与 `private-full`（含 logcat 尾 3000 行），单文件 HTML，零外部依赖，`window.print()` 可存 PDF。
4. **证据可追溯范式**：报告每一行都带「证据来源」列（`:1903` `source`），原始输出进 `<details>` 附录。
5. **自我克制的方法论**：`docs/METHODS.md` 明确列出「ADB 不能可靠证明什么」；`:1887` 在报告正文印出推断边界。
6. **两套 ADB 发现实现**（BAT + Python），覆盖 SideQuest / Meta Quest Developer Hub / VIVE Hub / Android SDK。
7. **离线自检**：`--self-test` 覆盖脱敏与 APK 解析，在 CI 里跑。

### 5.2 它**没**做什么（我们的增量就在这里）

**A. 零 PC 侧逻辑。** 实查证据：

```
$ grep -rniE 'StreamerSettings|ProgramData|Get-NetAdapter|Get-NetFirewallProfile|NetworkProfile|38850|38810|38860|ShowPairingRequests|ICS|SharedAccess' src/ docs/ mcp/ README.md
src/QuestAdbWebUi.cs:3:using System.Diagnostics;      ← 唯一命中是 using 行本身
（其余全部无命中）

$ grep -rniE 'powershell|pwsh|Get-CimInstance|Get-NetTCP|firewall' src/ mcp/
（0 命中）
```

它从头到尾**只把头显当设备看，从不把 PC 当被诊断对象**。

**B. 采集了归因需要的数据，但没用。** 这是最有价值的一条发现：

```
$ grep -n 'Cap(snap, "ip_route")\|Cap(snap, "connectivity")\|Cap(snap, "ip_addr")\|Cap(snap, "virtualdesktop")' src/QuestAdbWebUi.cs
1843: f["wifi"] = WifiSummary(wifi, Cap(snap, "ip_addr").Text);
1859: f["vd"]  = VirtualDesktopSummary(Cap(snap, "virtualdesktop").Text);
```

`ip_route`（`:1751` 采集）和 `dumpsys connectivity`（`:1737` 采集）**从未进入任何字段**，
只躺在原始附录里。它手上有「头显路由表」和「头显连通性」，却没有 PC 侧的 IP/网卡，
所以**做不出同网段比较**——而这恰恰是 VD 类故障的第一号原因。

**C. VD 只做到「装了没、什么版本」。** `VirtualDesktopSummary`（`:2177-2182`）全文 6 行：
存在性 + `versionName=`。没有：权限授予状态、进程、`exit-info`、签名、launchable activity、
`mWakefulness` 联动、Wi-Fi 频段/链路速率（VD 自己的 `IsSlow()` 阈值）。playbook §3.1–§3.8 列的这些它一条都没做。

**D. 没有判定层。** 全仓库**没有一处** Pass/Warn/Block 概念，**没有一处**阈值比较。
它的产物是「事实快照」，不是「诊断结论」。VDHelper 第一屏已经有 `CheckStatus` 四态
（`src/VdHelper/Core/Model/Health.cs:5-11`），第三屏必须沿用同一套，不能倒退成「给你看一堆字段」。

**E. 无归因。** 没有「PC 侧 X 失败 + 头显侧 Y 正常 ⇒ 结论 Z」这种推理结构。
`docs/METHODS.md` 是纯描述性的，没有判定规则。

**F. 无线 ADB 有一段安全说明但没有落地成产品。** `docs/` 有 `SAFE_MCP_CI.md`，但没有 playbook §7 那种
「无线调试是明文通道」的告警。playbook §4.3 要求的「**adb 通 ≠ VD 搜得到 PC，两者是两类故障**」，
它没有这个概念。

### 5.3 我们的增量（一句话一层）

| 增量 | 为什么它做不到 |
|---|---|
| **PC 侧 + 头显侧合并归因** | 它零 PC 侧逻辑（§5.2-A 实测） |
| **同网段 / 路由可达性判定** | 它采了 `ip_route` 但没有 PC 侧地址可比（§5.2-B 实测） |
| **VD 包完整体检**（版本对齐 / 7 项权限 / 进程 / `exit-info`） | 它只做存在性（§5.2-C） |
| **VD 自己的阈值**（`IsSlow() = freq<4000 \|\| link<450`） | 阈值在 decompiled 源码里，不在它仓库里；playbook §3.3 已抄出 |
| **Pass/Warn/Block 判定层** | 它没有判定层（§5.2-D） |
| **可执行修复**（`FixAction` 带 Backup/Rollback） | 它的写操作是「用户手动在 WebUI 点」，不是「工具负责回滚」 |
| **与第一屏同一份报告** | 它不知道第一屏存在 |

---

## 6. 许可证结论（MIT）

`LICENSE:1-3` 原文：

```
MIT License

Copyright (c) 2026 dwgx1337
```

`gh api` 的 `license.spdx_id` = `MIT`，与文件一致。

### 搬运时必须保留什么

MIT 的两个条件（`LICENSE:9-11`「The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software」）：

1. **版权声明**：`Copyright (c) 2026 dwgx1337` —— 必须原样保留。
2. **MIT 全文**：许可与免责声明整段必须随代码分发。

### VDHelper 的具体做法（本轮只给方案，未执行）

| 做法 | 内容 |
|---|---|
| 新增 | `src/VdHelper/ThirdParty/QuestAdbDashboard/LICENSE` = MIT 全文逐字副本 |
| 新增 | `src/VdHelper/ThirdParty/QuestAdbDashboard/NOTICE.md`，逐条列出**搬了哪些函数、来自哪个文件的哪一行**（如 `RunResult` ← `src/QuestAdbWebUi.cs:1943-1967`） |
| 每个搬运文件头 | 三行注释：`// Ported from dwgx/Quest-ADB-Dashboard, MIT License, Copyright (c) 2026 dwgx1337` + 原文件相对路径 + 原行号区间 |
| 应用的界面里 | 「关于」页加一行「头显采集部分改编自 Quest ADB Dashboard（MIT）」——这是 MIT 不强制但社区期望的 courtesy，成本为零 |
| 不做的事 | 不用它的 `examples/*.png`、`docs/assets/banner*.svg`、`dist/*.bat`——那些虽然同许可，但我们没搬运就不该带 |

**明确不做的事**：MIT **不要求**我们公开 VDHelper 自己的源码，也**不要求**产品开源；
但它**禁止**保留版权声明后再加限制（如「仅供内部使用」「不得二次分发」）。加了就是违约。

---

## 7. 复用决策汇总

| 决策 | 内容 | 预估搬运量 |
|---|---|---|
| **改写后搬（4 项）** | `RunResult`（→`ArgumentList`）、`Capture`/`Snapshot`（并入 `CheckResult.Evidence`）、`CollectSnapshot` 命令清单（重排）、`Redact*`+`SerialMask` | ~450 行 |
| **参考（6 项）** | 报告 HTML 三列结构、`AddInvoiceFacts` DSL、`AdbSourceLabel`、`DeniedSetting` 黑名单、危险命令前缀拦截、CI 反向断言 | 0 行（读思路） |
| **直接搬（0 项）** | —— | 0 |
| **不搬（约 1800 行）** | WebUI HTTP/SSE、APK 解析与安装、应用库、BAT 分发、MCP 服务器、i18n | 0 |

> **为什么没有一项「直接搬」**：整个仓库是「无 namespace 的单文件静态类 + WebUI 强耦合」，
> 其中任何可独立的部分都被 `Clean()` 的 `"-"` 哨兵约定和 `RunResult` 的字符串参数拼装绑住了。
> 抄函数体可以，直接引用一行都做不到。

---

## 8. 未验证 / 本轮未做

| 项 | 说明 |
|---|---|
| `[未验证]` 未接真机 | 本轮无头显连接。所有 `dumpsys` 输出形态引用自该仓库的 `examples/*.html` **合成数据**，**不是真机实测** |
| `[未验证]` 合成样例的保真度 | `examples/sample_quest3_*.html` 由 `scripts/generate-sample-reports.ps1` 生成，README 也自述「使用合成数据」，不能当真机证据引用 |
| `[未验证]` v0.3.0 release 的实际行为 | release 资产 `Quest_ADB_Tools_v0.3.0.zip` 未下载；本审计针对 `main` HEAD `a845caa`，**不等于** v0.3.0 发布点 |
| 未做 | 未下载 platform-tools；未连接任何 Android 设备；未运行该仓库的 BAT（它会改 `%TEMP%` 并起 WebUI） |
| 未做 | 未对其仓库做任何修改、未 commit、未开 PR |

---

## 9. 证据清单

| 内容 | 位置 |
|---|---|
| 元数据 | `gh api repos/dwgx/Quest-ADB-Dashboard`（2026-10-05） |
| 语言字节 | `gh api repos/dwgx/Quest-ADB-Dashboard/languages` |
| Release 列表 | `gh api repos/dwgx/Quest-ADB-Dashboard/releases`（6 条，v0.1.0 → v0.3.0） |
| 完整文件树 | `gh api repos/dwgx/Quest-ADB-Dashboard/git/trees/main?recursive=1` |
| 克隆 | `gh repo clone dwgx/Quest-ADB-Dashboard reference/quest-adb-dashboard`，HEAD = `a845caa` |
| 编译验证 | `%LOCALAPPDATA%\Temp\vdcompat-probe-11\`，`dotnet build -c Release` + `--self-test` |
| 采集清单 | `src/QuestAdbWebUi.cs:1719-1758` |
| PC 侧缺失 | `grep -rniE 'StreamerSettings|Get-NetAdapter|firewall|38850' src/ mcp/ docs/ README.md`（0 命中） |
| 上游对比 | `research/01-community-repos/01-inventory.md:58`（已登记此仓库，结论与本审计一致） |
| 上游 playbook | `research/06-adb-headset/01-adb-playbook.md`（头显侧命令与阈值真值来源） |
| 视觉语言 | `research/05-ui-reverse/01-ui-spec.md` |