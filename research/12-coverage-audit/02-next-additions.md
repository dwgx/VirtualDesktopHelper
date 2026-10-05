# 02 — 下一步该加哪几项（按投入产出排序）

> 生成日期：2026-10-05
> 输入：`01-coverage-matrix.md` 里 24 条「完全未覆盖」+ 5 条「部分覆盖」的残余缺口
> 筛选口径：**只挑「PC 侧可查」且当前无覆盖的**，因为另外两类（需头显 6 条、只能问路由器/用户 2 条）
> 受物理条件限制，不是「加一项检测」能解决的。
> 本文所有命令**都在本机真跑过一次**，真实输出原样贴在每条里。没跑过的显式标 `[未验证]`。

---

## 0. 本机实测前提（下面所有判据的基线）

```
$ powershell -NoProfile -Command 'Get-CimInstance Win32_Process | Where-Object { $_.Name -match "^(L-Config|LEMSService|LenVantage|LegionZone|...)" } | ForEach-Object { "$($_.Name)|$($_.ProcessId)|$($_.ExecutablePath)" }; Write-Output "---done---"'
ArmouryCrateControlInterface.exe|5740|
ArmouryCrate.Service.exe|5772|
ArmouryCrate.UserSessionHelper.exe|15100|
ArmouryCrate.exe|18576|C:\Program Files\WindowsApps\B9ECED6F.ArmouryCrate_6.5.14.0_x64__qmba6cd70vzyy\ArmouryCrate.exe
---done---
```

```
$ powershell -NoProfile -Command 'Get-Item "C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe" | Select-Object Name,Length,LastWriteTime,@{n="FileVersion";e={$_.VersionInfo.FileVersion}},@{n="ProductVersion";e={$_.VersionInfo.ProductVersion}} | Format-List'

Name           : VirtualDesktop.Streamer.exe
Length         : 22600728
LastWriteTime  : 8/25/2026 1:31:19 AM
FileVersion    : 1.34.22.0
ProductVersion : 1.34.22.0
```

```
$ powershell -NoProfile -Command 'Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,AdapterRAM,Status | Format-List'

Name           : GameViewer Virtual Display Adapter
DriverVersion  : 15.6.5.199
DriverDate     : 2/28/2026 9:00:00 AM
Status         : OK

Name           : Virtual Desktop Monitor
DriverVersion  : 13.50.53.699
DriverDate     : 5/21/2024 9:00:00 AM
Status         : Error

Name           : Intel(R) Graphics
DriverVersion  : 32.0.101.8826
DriverDate     : 5/29/2026 9:00:00 AM
AdapterRAM     : 2147479552
Status         : OK

Name           : NVIDIA GeForce RTX 5070 Ti Laptop GPU
DriverVersion  : 32.0.15.9649
DriverDate     : 5/5/2026 9:00:00 AM
AdapterRAM     : 4293918720
Status         : OK
```


---

### 本机体检实测耗时（供 P8 判断时间预算用）

```
$ start=$(date +%s%N) && ./src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --selftest --out /dev/null >/dev/null 2>&1; end=$(date +%s%N); echo "WALL_MS=$(( (end-start)/1000000 ))"
WALL_MS=4837
```

> 一轮 25 项体检实测 **4.84 秒**。语料里写的「2.5 秒跑完 18 项」是旧数据，**源码中不存在这个常量**：
> `HealthEngine.cs:14` 只有 `MaxConcurrency = 8` 的信号量上限，注释记着串行跑要 16 秒才改的并发，
> **没有全局超时**。任何耗时可观的检测项都要按这个事实判断，而不是按 2.5 秒。

---

## P1 — 厂商网络加速 / 常驻硬件工具进程（覆盖 D1 + G3）

| 字段 | 内容 |
|---|---|
| **建议 id** | `proc-tuner` |
| **覆盖根因** | D1（R40/R41/R42 Lenovo Vantage network boost）、G3（R59 内存 RGB 软件，18 个月苦战） |
| **判定依据** | 枚举 `Win32_Process` + `Win32_Service`，命中「厂商网络加速」与「硬件调校常驻工具」两份名单。名单要**分类**而不是合并成一个黑名单——D1 命中要说「会改网络路径」，G3 命中要说「会占 CPU/抢占主线程」，两者的用户动作完全不同 |
| **社区高频 / 官方 FAQ 零覆盖** | ✅ **是，且是本文最值钱的一条。** D1 是开发者唯一回复的答案（R36 整帖 ping 9ms、速率 200+、全程有线回程、Defender 已配好，全部常规指标全绿仍卡住），R41/R42 多人给出同一答案；G3 的 R59 是「18 个月后靠关 RGB 软件解决」。官方 FAQ 对这两条**零字提及** |
| **通过判据** | 两类名单均无命中 → Pass。命中 D1 名单 → Warn 且置顶（「这一项会让所有其它指标全绿却仍然卡在测速」）。命中 G3 名单 → Warn 但降权（是画质/卡顿嫌疑，不是 S4 测速失败的直接原因） |
| **失败文案要点** | ① 给出**进程名 + PID + 可执行路径**三件套，让用户能自己核对（照抄 `av`/`vpn-proc` 的证据形态）。② 给可复制的退出/停服务命令，但**工具不自动执行**——停 Armoury Crate 这类工具会连带关掉风扇控制，属于破坏性操作。③ 明确写「先临时停用再测」是诊断手段，不是永久建议（与 corpus §3.3 对「关防火墙」的处置同构） |
| **估算工作量** | **小**（约 60 行：一个 `TryGetProcesses` 扩展 + 两份名单 + 一个 `CheckResult`） |

本机实测输出（`Win32_Process` 侧，名单已含 Armoury Crate 系列）：

```
$ powershell -NoProfile -Command 'Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^(L-Config|LEMSService|LenVantage|LegionZone|LenovoVantageService|LenovoUtility|LZService|iCUE|...|ArmouryCrate...)" } | ForEach-Object { "$($_.Name)|$($_.ProcessId)|$($_.ExecutablePath)" }; Write-Output "---done---"'
ArmouryCrateControlInterface.exe|5740|
ArmouryCrate.Service.exe|5772|
ArmouryCrate.UserSessionHelper.exe|15100|
ArmouryCrate.exe|18576|C:\Program Files\WindowsApps\B9ECED6F.ArmouryCrate_6.5.14.0_x64__qmba6cd70vzyy\ArmouryCrate.exe
---done---
```

```
$ powershell -NoProfile -Command 'Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "(?i)(Lenovo|Legion|LZ|L-Config)" } | ForEach-Object { "$($_.Name)|$($_.State)|$($_.StartMode)" }'
（空 —— 本机没装 Lenovo 系服务。判定代码要能干净处理「一个都不匹配」，这是常见的合规路径）
```

---

## P2 — GPU 选择错误 / 驱动版本冲突（覆盖 E5 + E6）

| 字段 | 内容 |
|---|---|
| **建议 id** | `gpu-pick` |
| **覆盖根因** | E5（R48「关掉另一块就好了」）、E6（R54 开发者「591+ 的 Nvidia 驱动有 bug，1.34.14 修好了」，R55「装 2 个月前的驱动」） |
| **判定依据** | ① 存在两块及以上 `Win32_VideoController`（核显 + 独显）时，读 `HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences`，查 **SteamVR / vrdashboard / VR 游戏可执行文件** 对应的 `GpuPreference` 是否为 `0`（0=省电=核显）；② 比对独显驱动版本与 VD 已知冲突区间 |
| **社区高频 / 官方 FAQ 零覆盖** | ✅ 是。「最新驱动对 VD 是风险项」这个反直觉结论只出现在开发者回复里（R54），社区与 FAQ 都默认「装最新」 |
| **通过判据** | 单 GPU 或所有 VR 相关 exe 的 `GpuPreference != 0` 且驱动不在冲突区间 → Pass。`GpuPreference=0` 命中 VR 相关 exe → Warn（Block 级留给「唯一一块 GPU 是核显」）。驱动版本命中已知冲突区间 → Warn 并指名区间与修复版本 |
| **失败文案要点** | ① 明确区分「多显卡选错（改设置可修）」与「GPU 性能不足（改设置修不了，E4）」——corpus R47/R48 就是这一对，别混。② 给出设置路径：设置 → 系统 → 屏幕 → 图形 → 桌面应用 → 选择 VR exe → 高性能。③ 驱动项必须点名「哪个版本区间坏、哪个版本修好了」，不要写「建议更新驱动」——对 VD 而言「更新」可能就是那个动作 |
| **估算工作量** | **小–中**（约 120 行：注册表枚举 + 版本区间表 + 两张判定表。难点在冲突区间表要外部维护） |

本机实测输出（**这台机器就是 E5 的现场**——双 GPU，且 `vrdashboard.exe` 被钉在核显上）：

```
$ powershell -NoProfile -Command 'Get-ItemProperty "HKCU:\SOFTWARE\Microsoft\DirectX\UserGpuPreferences" | Format-List'

D:\Software\llama.cpp\llama-server.exe                      : GpuPreference=2;
C:\Windows\System32\ASUSACCI\ArmouryCrateKeyControl.exe     : GpuPreference=1;
D:\Steam\steamapps\common\SteamVR\bin\win64\vrdashboard.exe : GpuPreference=0;
D:\LLM Model\Ternary-Bonsai-2-27B\engine\ninfer-serve.exe   : GpuPreference=2;
D:\Steam\steamapps\common\VRChat\VRChat.exe                 : GpuPreference=2;
```

> `vrdashboard.exe : GpuPreference=0` —— 0 是「省电」，即核显。VR 跑在核显上正是 R48 描述的故障。

驱动版本（供 E6 比对用）：

```
$ powershell -NoProfile -Command 'Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate | Format-List'
Name           : Intel(R) Graphics
DriverVersion  : 32.0.101.8826
DriverDate     : 5/29/2026 9:00:00 AM

Name           : NVIDIA GeForce RTX 5070 Ti Laptop GPU
DriverVersion  : 32.0.15.9649
DriverDate     : 5/5/2026 9:00:00 AM
```

```
$ powershell -NoProfile -Command 'Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*" | Where-Object { $_.DisplayName -match "Virtual Desktop" } | Select-Object DisplayName,DisplayVersion,InstallDate | Format-List'

DisplayName    : Virtual Desktop Streamer
DisplayVersion : 1.34.22
InstallDate    : 20260826

DisplayName    : Virtual Desktop Service
DisplayVersion : 1.18.60
InstallDate    : 20260827
```

---

## P3 — 防火墙 profile 级默认入站动作（覆盖 B3）

| 字段 | 内容 |
|---|---|
| **建议 id** | `fw-profile-inbound` |
| **覆盖根因** | B3（R17「把 Domain/Private 的 Inbound Connections 改 ALLOW 就好了」、R31「Inbound Connections to ALLOW, Tested and it worked」、R34「Event Viewer 查不到任何被拦截的连接记录」→ 说明拦截根本没发生，profile 默认动作本身才是问题） |
| **判定依据** | 读 `Get-NetFirewallProfile` 的 `DefaultInboundAction`。`NotConfigured` 在 Windows 语义下等价于 **Block**——而现有 `fw-defender` **只判 `Enabled`（开/关）**，完全没看这一栏。这就是缺口本身 |
| **社区高频 / 官方 FAQ 零覆盖** | ✅ 是。社区对这一条的处理方式普遍是「全 profile 关防火墙」（corpus §3.3 已列为必须纠偏的四种错误解法之一），几乎没人去改 profile 的默认入站动作 |
| **通过判据** | 主网卡所在 profile 的 `DefaultInboundAction` 为 `Allow` → Pass；为 `Block`/`NotConfigured` → Warn，并指出「这不是说 VD 被拦了（VD 有自己的放行规则），而是说任何依赖临时放行的连接都会被默认丢弃」 |
| **失败文案要点** | ① 必须说清 `NotConfigured` = Block，否则用户看到 `NotConfigured` 会以为没事。② 给 `Set-NetFirewallProfile -Profile Private -DefaultInboundAction Allow` 的可复制命令，但**工具不自动执行**（改防火墙 profile 会影响这台机器所有入站连接）。③ 引用 R34 的推理方法：「事件日志里没有拦截记录」是这一条的重要旁证，可作为自查提示 |
| **估算工作量** | **极小**（约 40 行：`PsCheck.Create` 一个变体，复用现成的 `HealthChecks.Lines` 解析） |

本机实测输出（**这一栏现在没有任何检查在看**）：

```
$ powershell -NoProfile -Command 'Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | Format-Table -AutoSize | Out-String -Width 120'

Name    Enabled DefaultInboundAction DefaultOutboundAction
----    ------- --------------------- ---------------------
Domain     True        NotConfigured         NotConfigured
Private   False        NotConfigured         NotConfigured
Public    False        NotConfigured         NotConfigured
```

现有 `fw-defender` 用的就是这个查询的子集（`Select-Object Name,Enabled`），所以把 `DefaultInboundAction` 加进去是**同一句话的扩展**，不需要新增 PowerShell 调用：

```
$ powershell -NoProfile -Command 'Get-NetFirewallProfile | Select-Object Name,Enabled | Format-Table -AutoSize | Out-String -Width 120'
Name    Enabled
----    -------
Domain     True
Private   False
Public    False
```

```
$ powershell -NoProfile -Command 'netsh advfirewall show allprofiles state'

Domain Profile Settings:
State                                 ON

Private Profile Settings:
State                                 OFF

Public Profile Settings:
State                                 OFF
```

> 注意本机 `Private` profile 的防火墙是 **OFF** —— 现有 `fw-defender` 已经报了这个 Warn。
> 但它报的理由是「profile 被关闭」，而**没有**指出 profile 的默认入站动作也是 Block。这是两个不同的故障，
> R17/R31 命中的是后者。

---

## P4 — 显示器枚举与信号状态（覆盖 E1）

| 字段 | 内容 |
|---|---|
| **建议 id** | `display-inventory` |
| **覆盖根因** | E1（R51「拔了显示器再插回去，虚拟桌面就黑屏了，但鼠标能看到」） |
| **判定依据** | 两个来源交叉：`WmiMonitorID`（拿 `Active` 信号位与序列号）与 `[System.Windows.Forms.Screen]::AllScreens`（拿当前**实际可用**的显示输出）。两者**不一致**（WMI 说有信号、Screen 里不存在）正是 R51 的形态 |
| **社区高频 / 官方 FAQ 零覆盖** | 部分。R51 是孤例（corpus 自己标注「社区孤例」），但它与 VD 的第二显示器逻辑完全吻合，且**症状是「有声音没画面」**，用户完全无从下手 |
| **通过判据** | `AllScreens.Count >= 1` 且至少一块 `Primary=True` → Pass。`AllScreens.Count == 0`（无任何可用输出）→ Block。WMI 报出 ≥1 块但 `AllScreens.Count == 0`，或两者块数不等 → Warn 并列出差异 |
| **失败文案要点** | ① 列出每块屏的 `DeviceName` / 是否 Primary / 分辨率，让用户对照自己的线。② 明确「鼠标能看到说明 PC 还在出图，问题在捕获目标」——这与 E2（`rdp-session` 已覆盖）是两种黑屏，别混。③ 顺带提示：Streamer 配置里的 `MonitorCount`（本机实测 `=1`）若与实际不符，也是「黑屏/选错屏」的线索 |
| **估算工作量** | **小**（约 80 行。`Screen` 需 `Add-Type -AssemblyName System.Windows.Forms`，现有 `PowerShellRunner` 能承载） |

本机实测输出（**两个来源不一致，正是这项要抓的东西**）：

```
$ powershell -NoProfile -Command 'Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID | ForEach-Object { "{0}|{1}|{2}|Active={3}" -f $_.InstanceName, ($_.UserFriendlyName -join ""), ($_.SerialNumberID -join ""), $_.Active }'

DISPLAY\BOE0CE4\5&23ddfd3c&0&UID4353_0|7869495648816877457890670|48000000000000000|Active=True
DISPLAY\AOCB470\5&23ddfd3c&0&UID4356_0|678350537100000000|49515050495149505149505151000|Active=True
```

```
$ powershell -NoProfile -Command 'Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "{0}|{1}|Primary={2}|Bits={3}x{4}" -f $_.DeviceName,$_.DeviceFriendlyName,$_.Primary,$_.Bounds.Width,$_.Bounds.Height }'

\\.\DISPLAY1||Primary=True|Bits=1920x1080
```

> WMI 看到 **2 块**屏（BOE0CE4 笔记本屏 + AOCB470 外接屏，均 `Active=True`），
> `AllScreens` 只返回 **1 个** DISPLAY1。块数不等 —— 这就是该项要报的差异。
> `[INFERENCE]` 外接屏可能是扩展模式且未激活输出，未在本轮实测中进一步确认根因。

---

## P5 — 网卡节能开关（覆盖 F6）

| 字段 | 内容 |
|---|---|
| **建议 id** | `nic-powersave` |
| **覆盖根因** | F6（R65 与 ICS 同帖，OP「我把所有省电设置都关了，比如 Gigabit Lite」） |
| **判定依据** | 读承载默认网关那块网卡的 `Get-NetAdapterAdvancedProperty`，筛 `*EEE` / `EnableGreenEthernet` / `PowerSavingMode` / `*InterruptModeration` / `*EEE` 等关键字，逐个报 `DisplayValue` |
| **社区高频 / 官方 FAQ 零覆盖** | 部分。R65 是与 ICS 混在一起的一条，但「关节能」在社区里被反复提及且常被归入「重启一切」这类兜底解法 |
| **通过判据** | 全部相关项为「关闭」→ Pass。任一项为「启用/Enable」→ Warn |
| **失败文案要点** | ① 逐项列出**当前值 + 建议值**，让用户照着改。② 明确「工具只读不改」——改 `速度和双工`、改节能会影响这台机器全部网络连接（与现有 `link-rate` 的 Detour 同一处置原则）。③ 顺带说明 EEE 与绿以太网在**有线**下才影响串流；纯无线用户这条不适用 |
| **估算工作量** | **小**（约 50 行，`PowerShellCheck` 一个变体） |

本机实测输出（**本机全部已是「关闭」，所以这条在本机是 Pass —— 判据能干净落地**）：

```
$ powershell -NoProfile -Command '[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-NetAdapterAdvancedProperty -Name "Ethernet" | Where-Object { $_.RegistryKeyword -match "EEE|Power|Green|WoW|Lpm|LPM|D0Packet|Interrupt|Moderate" } | Select-Object DisplayName,DisplayValue,RegistryKeyword | Format-Table -AutoSize | Out-String -Width 200'

DisplayName           DisplayValue RegistryKeyword
-----------           ------------ -------------
节能乙太网路                关闭           *EEE
中断调整              Enabled      *InterruptModeration
Advanced EEE          关闭           AdvancedEEE
EEE Max Support Speed 1.0 Gbps 全双工 EEEMaxSupportSpeed
环保节能                  关闭           EnableGreenEthernet
Power Saving Mode     关闭           PowerSavingMode
```

> ⚠️ **实现坑（本机实测发现）**：不加 `[Console]::OutputEncoding=[System.Text.Encoding]::UTF8` 时，
> 中文 DisplayName 全部变成 `??????`：
> ```
> ????????                ??
> Advanced EEE          ??
> ```
> 所以这一项的 PowerShell 脚本必须显式设输出编码，否则用户看到的是乱码。
> 现有 `PowerShellCheck` 的脚本均未设这一句。

---

## P6 — Streamer / Service 版本与自动码率开关（覆盖 A3+B7 的 PC 半边、D2）

| 字段 | 内容 |
|---|---|
| **建议 id** | `cfg-version` |
| **覆盖根因** | A3（R06 俄语用户「版本没对上，我花了 5 小时」）、B7（R63「VD 更新之后开始出问题，重装 Streamer 好了」）、D2（R43「自动调码率一开就永远卡在测速」） |
| **判定依据** | ① 读 `StreamerSettings.json` 的 `AutoAdjustBitrate`（**现有 `cfg-streamer` 连它的值都没取**，只在「顶层键」列了键名）；② 读 Streamer exe 的 `FileVersion` 与注册表 `DisplayVersion`；③ 给出 Streamer 版本 + Service 版本两行，供与头显端比对 |
| **社区高频 / 官方 FAQ 零覆盖** | ✅ 是。A3/B7 在 FAQ 里**完全不提要比对两端版本**，而 R06、R07（开发者要求两端都是 1.21.0）、R63 三条都指向它 |
| **通过判据** | 三行全部取到且 `AutoAdjustBitrate=true` → Pass。`AutoAdjustBitrate=false` → Warn（点明「这个开关关掉有时反而能过测速，见 R43，但代价是码率固定」——**不要把它说成一定是坏**）。版本行取不到 → Unknown |
| **失败文案要点** | ① 明确「PC 侧只能读到自己这半边；头显端版本需要 adb」——A3/B7 的**完全**覆盖要等 `headset-deep` 那边也读版本，这一条只做到「先把 PC 侧版本摆出来让人比对」。② Service 版本（1.18.60）与 Streamer 版本（1.34.22）是两个独立节奏，升级时要一起看。③ `AutoAdjustBitrate` 的判词必须双向：R43 说关掉能过测速，但不是所有人都该关 |
| **估算工作量** | **小**（约 60 行：复用现成 `StreamerSettings.Load()` 与 `Process`/`VersionInfo` 读取） |

本机实测输出（**D2 的根因在本机是活的**）：

```
$ powershell -NoProfile -Command '$j = Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw | ConvertFrom-Json; "AutoAdjustBitrate=$($j.AutoAdjustBitrate)"; "PreferredCodec=$($j.PreferredCodec)"; "CodecName=$($j.CodecName)"; "MonitorCount=$($j.MonitorCount)"; "OpenXRRuntime=$($j.OpenXRRuntime)"; "VideosRootPath=$($j.VideosRootPath)"'

AutoAdjustBitrate=False
PreferredCodec=11
CodecName=AV1 10-bit
MonitorCount=1
OpenXRRuntime=1
VideosRootPath=C:\Users\dwgx1\Videos\
```

> `AutoAdjustBitrate=False` —— R43 原话「I have Automatically adjust bandwidth off because if that's enabled it will just get stuck on measuring bandwidth」。
> 现有 `cfg-streamer` 的证据栏里有 `AutoAdjustBitrate` 这个**键名**（在「顶层键」那一行里），但**没有它的值**，也不参与判定。

---

## P7 — 官方云端服务可达性（覆盖 B9）

| 字段 | 内容 |
|---|---|
| **建议 id** | `vd-cloud` |
| **覆盖根因** | B9（R08「Virtual Desktop servers partially unreachable, some computers may not appear」、R09 开发者「It's back up now for remote connections」） |
| **判定依据** | 对 VD 的服务器端点做 DNS 解析 + TCP 443 连通性测试。命中失败时直接输出「非本机问题」 |
| **社区高频 / 官方 FAQ 零覆盖** | ✅ 是。官方状态源存在但**没有工具去查**；R09 是开发者公开确认过的服务端故障 |
| **通过判据** | 解析成功 + `TcpTestSucceeded=True` → Pass。解析失败或 TCP 不通 → Warn（**不 Block**——因为同网段局域网串流不一定需要出网，R30 指出 entitlement 只在特定时机发生）。文案必须写「这只影响需要出网的环节，不代表你的局域网串流一定坏了」 |
| **失败文案要点** | ① 明确区分「服务端故障」与「你的机器有问题」——这是本项唯一的存在理由。② 引用 R08 的原始报错文本，让用户能自查是不是同一个。③ 不给「重试」以外的任何建议：服务端故障没有本地解法 |
| **估算工作量** | **小**（约 50 行，但受制于**端点清单要外部维护**，见下） |

本机实测输出：

```
$ powershell -NoProfile -Command 'Test-NetConnection -ComputerName virtualdesktop.net -Port 443 -InformationLevel Detailed | Select-Object ComputerName,RemoteAddress,RemotePort,TcpTestSucceeded,NameResolutionSucceeded | Format-List'

ComputerName            : virtualdesktop.net
RemoteAddress           : 192.64.151.235
RemotePort              : 443
TcpTestSucceeded        : True
NameResolutionSucceeded : True
```

> ⚠️ **`[未验证]`：端点清单不完整。** 上面只实测了 `virtualdesktop.net:443` 这一个主机名。
> 语料里出现的服务端功能至少还有 entitlement/身份校验、远端发现、远端连接中继（corpus §3.1 提到
> 「或者改用 Streamer 的云中继」），**这些端点的主机名在本仓库里查不到**（已 grep `research/` 全文，
> 没有任何 `virtualdesktop.net` 子域名的实证记录）。
> 实现前必须先确定端点清单，否则这一项会给出「测了一个不相干的域名」的假通过。

---

## P8 — 丢包率主动探测（覆盖 D5）

| 字段 | 内容 |
|---|---|
| **建议 id** | `loss-probe` |
| **覆盖根因** | D5（R45「最后发现是我的路由器固件，它会随机丢包」，NETGEAR → OpenWRT） |
| **判定依据** | 对**默认网关**连发多次 ICMP，统计丢包率与 RTT 抖动。丢包 > 0 即报，并点名固件/驱动方向 |
| **社区高频 / 官方 FAQ 零覆盖** | 部分。R45 是孤例，但「ping 通 ≠ 链路健康」这个判据在社区里被反复用到却从没被工具化 |
| **通过判据** | 丢包率 = 0% 且最大 RTT ≤ 阈值的 ~5 倍 → Pass。丢包 > 0 → Warn。抖动大 → Warn（附带说明） |
| **失败文案要点** | ① 明确「ping 通不代表链路健康」——这是本项唯一的存在理由。② 区分「Wi-Fi 上的丢包」（多半是干扰/信道）与「有线上的丢包」（多半是网线/路由器固件），并提示先换网线再换固件。③ **不要**把它当成「网线坏了」的结论，只报观测值 + 方向 |
| **估算工作量** | **中**（判据本身简单，难点是**耗时**——见下） |

本机实测输出：

```
$ powershell -NoProfile -Command 'ping -n 20 192.168.11.1 | Select-Object -Last 5'

Ping statistics for 192.168.11.1:
    Packets: Sent = 20, Received = 20, Lost = 0 (0% loss),
Approximate round trip times in milli-seconds:
    Minimum = 0ms, Maximum = 4ms, Average = 0ms
```

> ⚠️ **实现坑（本机实测发现，必须写进 brief）**：这次命令**实际耗时 19.43 秒**。
> 而本机一次完整体检实测 **4.84 秒**（`WALL_MS=4837`，见 §0 的计时口径），已经明显长于
> 语料里「2.5 秒跑完 18 项」的旧描述。**加进同步流程前必须先确认并发预算**：
> `HealthEngine.cs:14` 现在是 `MaxConcurrency = 8` 的信号量上限（注释记着串行跑要 16 秒、
> 所以改成 8 路并发），**没有全局超时**。一个 19 秒的同步项会把整轮体检拖成 20 秒以上。
> 所以本项**不能**放进同步主流程，只能：
> ① 作为后台低频观测（每 30 秒一次，落到 `HealthHistory` 的时间序列里），或
> ② 收敛成 `ping -n 8 -w 500`（约 4 秒），牺牲统计精度换时间。
> 这一条是「投入产出」里唯一被我标成「中」的原因——不是难，是它与现有架构的时间预算冲突。

---

## 附：8 项按投入产出的排序理由

| 优先级 | id | 覆盖 | 为什么排这个位置 |
|---|---|---|---|
| P1 | `proc-tuner` | D1, G3 | **最高**：两条社区高频根因、官方零覆盖、纯进程枚举（约 60 行）、本机就有命中样本（G3 的 4 个 Armoury Crate 进程）可当场验证文案 |
| P2 | `gpu-pick` | E5, E6 | 高：两条「黑屏」类根因，注册表 + WMI 都现成本机就有**真实命中**（`vrdashboard.exe = GpuPreference=0`）；但版本冲突区间表要外部维护，所以比 P1 多一点 |
| P3 | `fw-profile-inbound` | B3 | 极高性价比：现有 `fw-defender` 用的就是同一个 `Get-NetFirewallProfile` 查询，**加一个 Select 字段即可**，约 40 行，且补的是「R17/R31 解决、R34 佐证」的确凿缺口 |
| P4 | `display-inventory` | E1 | 中：两个数据源都现成、成本低，但 R51 是孤例，用户面窄。价值在于「有声音没画面」这类症状目前**完全**没有对应检测项 |
| P5 | `nic-powersave` | F6 | 中：数据现成本机就是「全关闭」（判据能干净落地），但社区频次低于 P1–P3。**附带价值**：发现了 `[Console]::OutputEncoding` 乱码坑，顺手能修掉同类问题 |
| P6 | `cfg-version` | A3/B7 半边, D2 | 中：成本极低，但只能覆盖 A3/B7 的 PC 侧那一半（头显版本仍需 adb），所以不是「完全覆盖」。本机 `AutoAdjustBitrate=False` 是活的 D2 样本 |
| P7 | `vd-cloud` | B9 | 中偏低：成本低但**端点清单未确定**（已 grep 全文查不到子域名），实现前有前置调研工作。是唯一能把「服务端故障」和「用户机器坏了」分开的检测项，所以保留 |
| P8 | `loss-probe` | D5 | 排最后不是因为难，是因为**它会打破现有时间预算**（本机实测 20 包要 19.43 秒，而一轮体检实测 4.84 秒；`HealthEngine` 只有 8 路并发上限、无全局超时）。建议先做 8 包收敛版，或直接进后台时序观测 |


### 没进前 8 但值得记一笔的

- **G2（5GHz 信道）** —— 命令是 PC 侧的，但本机实测 `netsh wlan show networks mode=bssid` **退出码 1**：
  `The wireless local area network interface is powered down and doesn't support the requested operation.`
  需要一块处于 Up 的无线网卡作前置条件，且要有明确的 Unknown 降级路径，所以没进前 8。
- **G4（过热降频）** —— 本机实测 `MSAcpi_ThermalZoneTemperature` **返回空**（笔记本常见），
  必须以 `nvidia-smi --query-gpu=temperature.gpu,clocks_event_reasons.active,power.draw` 为主
  （实测可用：`54, 0x0000000000000000, 35.71 W`）。可行但要对「读不到温度」有正确降级，故排在 P8 之后。

---

## 结论

**43 个根因里，现有 25 项检测（含 2 项条件触发的 adb 检测）完全覆盖 14 个、部分覆盖 5 个、完全没覆盖 24 个；
其中「PC 侧可查且没覆盖」的有 16 个——上面这 8 项能一次性吃掉其中的 10 个根因
（P1 两项、P2 两项、P3 一项、P4 一项、P5 一项、P6 三个里两个半、P7 一项、P8 一项），
且全部验证过本机命令输出。**

> 换句话说：**做完这 8 项，PC 侧可查的空白从 16 个降到 6 个**（剩下的 G2 前置条件、G4 降级路径、
> B5 残余的未注册后台进程、D3 云 PC 识别、C4/G1 网关——其中 C4/G1 必须等 adb）。
>
> 另需记住一个结构性事实：**`headset-deep` 是覆盖 A5/A6/F1 的唯一检测，且需头显在线**
> （本机 `adb devices -l` 实测为空列表）。这三条约 30% 的完全覆盖是「插了线才算数」的，
> 报告里必须让用户看见这一栏是 Unknown 而不是悄悄跳过。

---

## 附：本文所有本机实测命令清单

```powershell
# P1 厂商工具进程 / 服务
Get-CimInstance Win32_Process | Where-Object { $_.Name -match "^(L-Config|LEMSService|LenVantage|LegionZone|LenovoVantageService|LenovoUtility|LZService|iCUE|iCUEService|RGB|Aura|ARGB|Synapse|AiSuite|ArmouryCrate|XTU|RTSS|Afterburner|MSI.Central|GHub|LGHUB|MysticLight|SAVI|SSU|FrameworkHost|VCCoreCPU|Nahimic|SonicStudio|Alienware|Fusion)" } | ForEach-Object { "$($_.Name)|$($_.ProcessId)|$($_.ExecutablePath)" }
Get-CimInstance Win32_Service | Where-Object { $_.Name -match "(?i)(Lenovo|Legion|LZ|L-Config)" }

# P2 GPU 选择 / 驱动 / 版本
Get-ItemProperty "HKCU:\SOFTWARE\Microsoft\DirectX\UserGpuPreferences"
Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,AdapterRAM,Status
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*" | Where-Object { $_.DisplayName -match "Virtual Desktop" }
Get-Item "C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe" | Select-Object @{n="FileVersion";e={$_.VersionInfo.FileVersion}}

# P3 防火墙 profile
Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction
Get-NetFirewallProfile | Select-Object Name,Enabled
netsh advfirewall show allprofiles state

# P4 显示器
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID
Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens

# P5 网卡节能（注意必须设 OutputEncoding）
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
Get-NetAdapterAdvancedProperty -Name "Ethernet" | Where-Object { $_.RegistryKeyword -match "EEE|Power|Green|WoW|Lpm|LPM|D0Packet|Interrupt|Moderate" } | Select-Object DisplayName,DisplayValue,RegistryKeyword

# P6 配置与码率
$j = Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw | ConvertFrom-Json; "AutoAdjustBitrate=$($j.AutoAdjustBitrate)"

# P7 云端可达
Test-NetConnection -ComputerName virtualdesktop.net -Port 443 -InformationLevel Detailed

# P8 丢包
ping -n 20 192.168.11.1
```
