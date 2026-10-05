# 01 — 回溯：头显上线前后，这个工具到底是什么

> 目的：给一个决定提供依据——**头显接上来之后，接下来五件事做哪五件**。
> 本文只做一件事：把当前代码里**已经被证据支持的**、**只在纸面上成立的**、和**从来没被执行过**的三类东西分开。
>
> **写作期间头显上线了**：本文读完所有文件之后、写到一半时，`a2fa03a` 这个提交落地并改掉了 4 个文件。
> 全部影响、两处被调整的 `file:line`、以及对结论的影响，逐条写在 **§7**。
>
> 方法：通读 `AGENTS.md` / `WORKFLOW.md` / `README.md` / `notes/`（10 篇）/ `handoff/`（8 篇）/
> `research/09-failure-corpus/02-symptom-to-rootcause.md` / `research/06-adb-headset/`（3 篇）/
> `research/12-coverage-audit/`（2 篇）/ `research/14-real-run/`，
> 以及 `src/VdHelper/Core/Health/`（14 个文件）、`src/VdHelper/Core/Adb/`（3 个文件）、
> `src/VdHelper/App.xaml.cs`、`tools/` 下 12 个脚本、`docs/` 下 10 份文档。
>
> **本文没有执行任何会改变机器状态的命令。** 按 `WORKFLOW.md:74-91`（§10「清扫不等于执行」），
> `--quit-streamer` / `--apply <id>` / `--set-param` 写路径 / `--deep`（会向网络发包）一律不跑。
> 只跑了两道**只读闸门**（见 §0.1）。
>
> 凡本文说「实测」，指的都是本轮或此前某轮留下的、可以被复核的输出原文；凡是推断，标 `[未验证]` 或 `[INFERENCE]`。

## 0. 本文用到的可复核基线

### 0.1 本轮实跑的两条只读命令

```powershell
# 在 D:/Project/VirtualDesktopHelper 下
python tools/check-citations.py
#   citations: 157 distinct in 728 files
#   tree streamer: 11015 .cs files
#   tree vd: 935 .cs files
#   OK   all 157 citations resolve

powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; & ./tools/check-symptom-map.ps1"
#   real checks      : 36  (体检屏 + 头显屏；headset-deep 只在头显屏跑)
#   referenced by S* : 36
#   症状类            : 7 个
#   README 声明项数    : 36 项检测 / 14 个调研主题（一致）
#   OK  症状表与检测项一一对应，README 计数同步
#   exit=0
```

两条都通过。**第一条的数字（157）与 `handoff/NOW.md:4` 写的「156 处」不一致**——NOW.md 是最新一篇
交接，写错了；`docs/RELEASE-0.5.0.md:57` 的 157 才是对的。

### 0.2 本机体检基线（读 `%AppData%\VirtualDesktopHelper\history\run-20261006-050838.json`）

> **⚠️ 本文写作期间仓库动了。** 本文第一遍读取这些文件时，`HEAD` 是 `3264adf`；写作过程中
> 新的提交 `a2fa03a`（2026-10-06 05:29 +09:00）落地，把 `session-stale` 从 Block 改成 Warn 并去掉了修复项，
> 给 `lan-reach` 加了本段 /24 主机发现，同时改了 `README.md:160`（调研主题 14 → 15）。
> 本文所有 `file:line` 引用**已按 `a2fa03a` 之后的代码重新核对**；受影响的只有两处，见文末 §7。

```
（a2fa03a 之前，读自 run-20261006-050838.json）
verdict=Blocked  logic=2  headline=阻断：有检查项失败，串流很可能起不来
35 项：Pass 20 / Warn 12 / Block 1 / Unknown 2
唯一 Block：lan-reach（头显 192.168.11.14 ping 不通，ARP 缓存里也没有它）
两个 Unknown：nic-powersave、wifi-quality
```

`a2fa03a` 的提交信息自报：**21 pass / 12 warn / 0 block / 2 unknown，exit 3**，
并写明「不是机器变好了，是工具不再凭空造故障」——该提交把「跑了半小时的正常串流」与
「头显早已退出的残留套接字」判成同一个状态，改成 Warn + 不给修复项（现 `HealthChecks.cs:303-319`）。
`docs/checks.md` 与 `docs/index.html` 是**已提交的生成物，仍停留在改动前的那一份数据**，本文引用它们时按原样注明。

历史目录里 `run-*.json` 恰好 40 个，与 `Core/Health/HealthHistory.cs:18` 的 `MaxRuns = 40` 一致，保留策略在正常工作。

配置现状（`%AppData%\VirtualDesktopHelper\config.json`）：

```json
{ "headsetIp": "192.168.11.14",
  "adbPath": "D:\\Software\\VIVE Hub\\VIVE Hub\\CommonTools\\ADB\\adb.exe" }
```

**本轮实跑文件系统核对**：`ls "D:/Software/VIVE Hub/VIVE Hub/CommonTools/ADB/adb.exe"` → **存在**；
`where adb` → `Could not find files for the given pattern(s)`；
`ANDROID_HOME` 与 `ANDROID_SDK_ROOT` 均为空。
即 `research/06-adb-headset/01-adb-playbook.md:20-45` 的实测结论至今成立。

---

## 1. 这个工具今天到底是什么

### 1.1 形状：一个数据驱动的检查项表 + 七个修复动作 + 三个界面

| 部件 | 事实 | 位置 |
| --- | --- | --- |
| 检查项注册表 | `HealthChecks.Create()` 返回 **35** 项 PC 侧检查 | `src/VdHelper/Core/Health/HealthChecks.cs:30-59` |
| 第三屏额外 | `HeadsetProbe`（结果 id `adb`）+ `HeadsetDeepProbe`（结果 id `headset-deep`） | `Views/HeadsetViewModel.cs:99-112` |
| 判定 | Block → Blocked；否则 Warn → AtRisk；否则「测到的必须多于没测到的」才 Streamable | `Core/Model/Health.cs:88-100` |
| 并发 | `MaxConcurrency = 8` 的信号量，**无全局超时** | `Core/Health/HealthEngine.cs:14` |
| 修复动作 | **7 个**会被任何检查产出：`streamer-launch` / `enable-pairing-requests` / `svc-start` / `streamer-restart` / `fw-restore-vd` / `svc-repair` / `headset-grant`（第 8 个 `disable-adapter:<name>` 已不可达，见 §6.1 第 4 条） | `Core/Health/Fixes.cs`、`Core/Health/WindowsStateChecks.cs:101-136`、`Core/Adb/HeadsetProbe.cs:148-159` |
| 历史 | 每次体检写 `run-<ts>.json`，保留 40 次，带 `LogicVersion = 2` | `Core/Health/HealthHistory.cs:18,56` |

### 1.2 它能证明什么（每条都有一次真实运行或一次真实命令背书）

| 能证明的事 | 判据形态 | 位置 |
| --- | --- | --- |
| Streamer 进程在不在、它的 exe 真的在哪 | 进程表 + `MainModule` 回退默认路径 | `StreamerChecks.cs:110-136`、`:38-57` |
| 38810/20/30/40 上**每一个套接字的状态、对端、持有进程** | 一次 `Get-NetTCPConnection` 全状态查询 | `HealthChecks.cs:167-214`（注释记录了此前「串流时报空闲」的 bug 与成因） |
| 现在是不是**真在串流**，而不是连着官方服务器 | 判对端是否落在本机 /24；官方服务器端点是公网 IP | `HealthChecks.cs:256-292` |
| 到头显的通道是否真的断了 | `Established` 拆成本网段 / 非本网段两组，再按 2 分钟分「刚建立」与「较旧」——**且较旧这一档现在只报 Warn、不给修复项**（`a2fa03a` 改的） | `HealthChecks.cs:154-155,246-247,303-319,321-334` |
| UDP 38850/38860 现在有没有监听、**被谁占着** | `IPGlobalProperties` + 按 PID 反查 | `StreamerChecks.cs:204-246` |
| 防火墙规则成对性 + 规则指向的 exe 是否还存在 | `Get-NetFirewallRule` + `Test-Path`（先展开环境变量） | `FirewallPairChecks.cs:34-41`、`:94-121` |
| profile 默认入站是 Allow 还是 Block | `Get-NetFirewallProfile.DefaultInboundAction` | `PerformanceChecks.cs:40-50` |
| 配对信息有没有真的落盘、显示驱动是不是 ERROR_DISABLED | 读 JSON / `Win32_VideoController` 分隔符输出 | `FirewallPairChecks.cs:276`、`PerformanceChecks.cs:29-31,142-155` |
| GPU 当前频率 / 温度 / NVENC 会话数 | `nvidia-smi` | `GpuRuntimeChecks.cs:20-22` |
| 无线频段/信道/协商速率/信号，以及**是不是 DFS 信道** | 单次 `netsh wlan show interfaces` | `WifiQualityCheck.cs:138-139` |
| 头显在不在、能 ping 不通、ARP 条目是不是陈旧 | ping + `Get-NetNeighbor`，**只认 `Reachable`** | `ReachabilityCheck.cs:52-63` |
| 丢包与抖动，且**区分「没人应答」和「丢包」** | 8 次快采样 / 20 次深度 | `LossProbe.cs:82-140`、`:204-249` |
| NAT 类型与外网 IP 是否落在私有段 | UPnP 只读控制面，**从不调 `CreatePortMapAsync`** | `NatChecks.cs:39-41` |
| 111 个 Streamer 配置键的真值，18 个 DPAPI blob 不落盘 | 读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json` | `Core/Config/ParameterCatalog.cs`、`src/VdHelper/Resources/parameters.json` |

### 1.3 它能修什么

| 修复 id | 做什么 | 有没有执行证据 |
| --- | --- | --- |
| `streamer-launch` | 启动 Streamer | ✅ **真跑过一轮完整 round trip**：`notes/2026-10-05-fix-verification.md:9-41` 记了「修复前 Block → 执行 → 复测 Pass → 回滚 → 又 Block」四个环节 |
| `enable-pairing-requests` | 把 `ShowPairingRequests` 写成 `true`，带后置回读 | ❌ 只验证过**拒绝分支**（Streamer 在跑时拒绝，`WindowsStateChecks.cs:113-114`） |
| `svc-start` | 启动服务 + 复查状态 | ❌ 只在代码层（`Fixes.cs:71-83`） |
| `streamer-restart` | 提权结束再拉起 + **PID 必须真的变** | ✅ 跑成过一次：`handoff/2026-10-05-round6-honest-fixes.md:21-31`，PID 11120 → 36784，UAC 等 98 秒 |
| `fw-restore-vd` | 按**实测到的 exe 路径**重建入站放行规则 | ❌ 卡 UAC（`notes/2026-10-05-fix-verification.md:63`） |
| `svc-repair` | 用 Streamer 同目录的 MSI 重装服务 | ❌ 卡 UAC |
| `headset-grant` | 「授予缺失的运行时权限」 | ❌ **它其实什么都不做**，见 §2.3 |
| `--set-param <key> <json>` | 直接写一个配置键 + 回读确认 | ⚠️ **成功路径在真实配置副本上验过**（`notes/2026-10-06-config-write-path-verified.md:17-25`：写入落地 / 备份存旧值 / 14 键一个没丢 / 4 个 DPAPI blob 逐字节相同 / 坏 JSON 被拒），**真机上只跑过拒绝路径**（exit 8） |

配置写入路径这条尤其值得记：它是全工具唯一会改用户真实配置的路径，
本轮之前只有「拒绝路径」的证据；`notes/2026-10-06-config-write-path-verified.md:8` 自己写得很准——
「一个只验证过拒绝路径的写入路径，等于没验证过写入路径」。

### 1.4 它拒绝碰什么（这部分是这个工具的骨气，值得原样保留）

- **不伪造鉴权**：`AGENTS.md:34`、README:150。
- **不改显示驱动**：`PerformanceChecks.cs:87-89` 给的是指引不是动作，理由写在 detail 里（改错可能连画面都出不来）。
- **不自动禁用离线 APIPA 网卡**：`HealthChecks.cs:112-128` 里有一段很长的注释解释为什么**故意不提供**这个修复。
- **不自动停厂商调校工具**：`MachineStateChecks.cs:101-102`（停 Armoury Crate 会连带关掉风扇控制）。
- **不改防火墙 profile 默认入站**：`PerformanceChecks.cs:48-50` 给可复制命令与回滚，但只提示。
- **不碰 ICS / 路由 metric / 路由器 / 杀软**：全是 `Array.Empty<FixAction>()` + 指引。
- **不写假开关**：读不到的值显示「由头显决定」（`ParameterCatalog.LivesOnPc`，`App.xaml.cs:387-391`）。

### 1.5 一句话总结这一节

**它今天是一个「本机网络与配置的只读体检仪 + 六个已声明的修复动作」。**
它最强的地方是把「没测到」和「没问题」分开（`Health.cs:96-98`、`PowerShellCheck.cs:44-50`、`WifiQualityCheck.cs:107-115`）；
它最弱的地方是**关于头显的一切从未执行过**，而且第三屏的实现与项目自己的调研文档在权限这一项上直接冲突（§2.3）。

---

## 2. 比证据更强的说法

这一节是本文最要紧的部分。判据只有一条：**把一个「已实测」的说法和一个「其实没测过」的说法摆在一起，看前者是否比后者多走了一步。**

我把已经修好的那几处先划掉，避免重复劳动——它们在 `docs/RELEASE-0.5.0.md:6-12` 有正式记录，本文不复述：
`net-loss` 把睡着的头显报成 100% 丢包、`session-stale` 在有残留套接字的分支上说「也没有残留套接字」、
`proc-tuner` 声称测了 CPU 与线程优先级、`av` 把 `productState` 当已解码、面板把不可修的阻断项标成「先修」。
**这五处的根因（文案比实测多走一步）都已被修掉，且代码里留下了不再复发所需的注释。**
本节只列**还活着**的实例。

### 2.1 存活实例 A（高）：`--adb` 被文档说成第三屏的等价物，但它跑的是另一个东西

| 项 | 内容 |
| --- | --- |
| 文件与行 | `README.md:16`、`README.md:74` |
| 原文 | `README.md:16`「↑ **第三屏也可以直接命令行跑**：`VdHelper.exe --adb`」<br>`README.md:74`「`VdHelper.exe --adb`　REM 头显侧，命令行版（**第三屏的等价物**）」 |
| 实际 | CLI 分支只构造 `HeadsetDeepProbe`（`src/VdHelper/App.xaml.cs:468`）；第三屏同时跑 `HeadsetProbe` **和** `HeadsetDeepProbe`（`Views/HeadsetViewModel.cs:99,111`）。全仓库里 `new HeadsetProbe` 只出现一次，就在 `HeadsetViewModel.cs:99`。 |
| 差在哪 | `HeadsetProbe` 负责的是：包名识别、7 项运行时权限、**进程存活判定**、Wi-Fi 地址、全局代理。`HeadsetDeepProbe` 负责的是：A6 MAC 随机化 / F1 头显本地设置 / 头显 VPN。命令行版本拿不到前一半。 |
| 为什么越界 | `README.md:15` 明写第三屏「读头显的包、权限、网络，**并判断应用进程是否还活着**」——这三件事**没有一件**能从 `--adb` 得到。而这恰恰是 §2.3 里那个最贵的缺陷所在。 |
| 附带 | 症状表里也找不到它：`Core/Model/Symptom.cs:24-68` 七类症状引用的是 `headset-deep`，**没有一项引用 `adb`**。 |

### 2.2 存活实例 B（高）：「36 项」这个数字包含了第三屏的一项，还漏掉了另一项

| 项 | 内容 |
| --- | --- |
| 文件与行 | `README.md:13`、`README.md:21`、`README.md:81`；反漂移闸门 `tools/check-symptom-map.ps1:17-27` |
| 原文 | `README.md:13`「本机体检 \| PC 侧网卡 / 防火墙 / 服务 / 配置 / GPU 有没有断链（**36 项**）」<br>`README.md:21`「判定本身永远由**全部 36 项**算出」 |
| 实际 | PC 侧体检跑 **35** 项（`HealthChecks.cs:30-59`）。判定由这 35 个 `CheckResult` 算出（`Health.cs:88-100`），`headset-deep` **根本不在这个 report 里**。顶部总判定条也只吃 PC 侧结果：`ShellWindow.xaml.cs:59,96` 从 `HealthReport` 取，`HeadsetViewModel.Status` 从不参与合并。 |
| 更糟的一层 | 第三屏实际产出**两个**结果 id：`headset-deep` 和 `adb`。闸门在 Adb 目录里只用 `Id = "…"` 匹配（`check-symptom-map.ps1:26`），而 `HeadsetProbe` 是用 `new CheckResult("adb", …)`（`HeadsetProbe.cs:47,51,100,109,123`）构造的，**匹配不上**。于是「36」既把第三屏的一项算进了第一屏，又把第三屏的另一项完全排除在闸门视野之外。 |
| 证据 | 闸门实跑输出「real checks : 36（体检屏 + 头显屏；headset-deep 只在头显屏跑）」——脚本自己知道 36 是两个屏合起来的，`README.md:13` 却把它挂在「本机体检」这一行上。 |
| 历史对照 | `docs/product-spec.md:19` 写的是「来自屏 1 与屏 3 的结果合并」——同样与现状不符（该文件顶部已自标「历史文档」，但这句话仍然会在读者心里留下错误印象）。 |

### 2.3 存活实例 C（最高）：第三屏的「7 项运行时权限」既数错了，也**用错了判据**

这一条是本轮新发现的，它会在头显插上的第一秒就发作。

| 项 | 内容 |
| --- | --- |
| 文件与行 | `src/VdHelper/Core/Adb/HeadsetProbe.cs:9-10`（注释）、`:23-32`（权限表）、`:130-143`（读取）、`:119-127`（结论） |
| 注释原文 | `:9-10`「the shipped package is `VirtualDesktop.Android` … and the **seven** runtime permissions are HorizonOS scene/face/eye **pairs** plus notifications.」 |
| 三处不一致 | **(a) 表里有 7 行、4 个不同的权限串。** `USE_SCENE` / `FACE_TRACKING` / `EYE_TRACKING` 各出现两次，第二次的 Symptom 写「同上（**第二个授权位**）」，`POST_NOTIFICATIONS` 一次。`:139-141` 用 `seen` 去重，所以 `granted.Count` 永远是 **4**，`HeadsetProbe.cs:122` 就会对用户说「**4 项**权限齐全」。<br>**(b) 「第二个授权位」根本不是同一个权限的第二个槽位。** 本项目自己的调研写得很清楚——`research/06-adb-headset/03-symptom-decision-table.md:70-79` 逐字抄了 7 条 `pm grant`：<br>`com.oculus.horizonos.permission.USE_SCENE` **和** `horizonos.permission.USE_SCENE`<br>`com.oculus.horizonos.permission.FACE_TRACKING` **和** `horizonos.permission.FACE_TRACKING`<br>`com.oculus.horizonos.permission.EYE_TRACKING` **和** `horizonos.permission.EYE_TRACKING`<br>外加 `android.permission.POST_NOTIFICATIONS`。<br>理由同页 `:78-79`：「`com.oculus.*` 与 `horizonos.*` **成对出现**：老 Quest OS 只认 `com.oculus.*`，新系统只认 `horizonos.*`，两条都要授」。<br>**代码里那 3 个「第二个授权位」就是这 3 个 `horizonos.permission.*`，被写成了前一个的重复项。** 结果是：新系统（只认 `horizonos.*`）上，那 3 条真正的权限**一个都没查**。<br>**(c) 判据用错了命令。** `:133` 跑的是 `shell pm list permissions`，然后 `:140` 对整段输出做**子串包含**。<br>但 `pm list permissions` 列的是**设备已知的权限目录**，不是已授予状态；真值在 `dumpsys package <PKG>` 的 `runtime permissions:` 段的 `granted=true/false`——`research/06-adb-headset/01-adb-playbook.md:245-247` 与 `03-symptom-decision-table.md:46,60,168` 两处都指定了这条命令，后者还把 `granted=true` 的形态明确列为 `[未验证]`（`:168`）。<br>子串包含一个**权限目录**意味着：这 4 个串只要在系统里存在（一定存在），`missing` 恒为空 → `HeadsetProbe.cs:119-127` 恒走 `Pass` 分支，文案恒为「权限齐全」。**这是一次由判据本身制造的假通过**，形状与 `net-loss`/`session-stale` 完全同类。 |
| 违反的是项目自己定的纪律 | `research/06-adb-headset/03-symptom-decision-table.md:179-181`：「本表里任何一行，只要『期望』列写的是形态而不是本机实测输出，**UI 上就必须标 `[未验证]`**。」<br>`HeadsetProbe.cs:124` 对用户断言「权限缺失的表现：30 秒后 VR 焦点被系统收回、注视点串流被自动关闭、面部追踪分支不执行」——这三条在决策表 `:167-168` 里全被列为 `[未验证]`，代码里一个 `[未验证]` 字样都没有。 |
| 文档侧也被带偏 | `docs/product-spec.md:62`「权限（7 项 runtime 权限逐项 ✓/✗）」；`research/12-coverage-audit/01-coverage-matrix.md:49`「`headset` \| 包名识别 + **7 项**运行时权限」。两处都跟着实现走，而不是跟着决策表走。 |

### 2.4 存活实例 D（最高）：`pidof` 的空结果不是空的，「进程还活着吗」这一项大概率恒为「活着」

| 项 | 内容 |
| --- | --- |
| 文件与行 | `src/VdHelper/Core/Adb/HeadsetProbe.cs:88-93`、`:107-117` |
| 代码 | `var pidText = pid.Ok ? pid.StdOut.Trim() : "";`<br>`if (pidText.Length > 0) running.Add($"{pkg} (pid {pidText})");`<br>`else ev["进程 " + pkg] = "未在运行";` |
| 问题 | 判据是「输出非空」，不是「输出是纯数字」。 |
| 本项目自己的证据 | `research/06-adb-headset/01-adb-playbook.md:255-260`：「`pidof` 的空结果在 `adb shell` 里会变成**字面量 `no process`**」；`03-symptom-decision-table.md:186` 把「`pidof` 的 `no process`」列为**上一轮真机记录**，属第 2 类可用证据。<br>若 stdout 是 `no process`，`pidText.Length == 9 > 0` → 记成「进程在跑」。 |
| 后果 | `HeadsetProbe.cs:107-117` 那条 `Block`（"客户端装了，但没有进程在运行" ——注释里自称是「『列表空』和『网络不通』的分水岭」）**永远不会触发**；同时 `:95` 的 `VD 进程存活数` 会显示 `1 / 1`，用户看到的是一个假的健康信号。 |
| 声明强度 | `README.md:15` 向用户承诺第三屏能「**判断应用进程是否还活着**」。以现有判据，它做不到。<br>`[未验证]`：HorizonOS 上 `pidof` 无进程时是否逐字返回 `no process`，本轮无法实跑；上面引的是本项目上一轮的真机记录。**但无论返回 `no process` 还是别的非空串，"非空即活着" 这个判据都不成立**，改成 `int.TryParse` 是零成本的。 |

### 2.5 存活实例 E（高）：`gpu-throttle` 把「频率比不高」直接归因成「功耗墙」，而它一个瓦特都没读

| 项 | 内容 |
| --- | --- |
| 文件与行 | `src/VdHelper/Core/Health/GpuRuntimeChecks.cs:20-22`（采集什么）、`:82-88`（说什么）、`README.md:36-37`、`research/14-real-run/01-what-this-machine-found.md:85-94` |
| 采集字段 | `clocks.current.graphics, clocks.max.graphics, temperature.gpu, utilization.gpu, encoder.stats.sessionCount`——**没有 `power.draw`，没有 `clocks_throttle_reasons.active`** |
| 结论原文 | `:84-86`「温度不高却上不了满频，通常是**功耗墙**而不是过热：混合输出、独显没接在满功耗档、或者驱动限了。」 |
| 为什么越界 | (a) 缺 RTT 就没有降频原因——`clocks_throttle_reasons.active` 才是那一位数字，而调研侧**知道有这条命令**：`research/12-coverage-audit/01-coverage-matrix.md:120` 自己实测过 `nvidia-smi --query-gpu=temperature.gpu,clocks_event_reasons.active,power.draw → 54, 0x0000000000000000, 35.71 W`，并写下「无降频」。**信息可得，只是没读。**<br>(b) 更根本的问题：空闲态下显卡本来就不跑在最高频率。当前基线 `utilization` 没被用于判断，而 `docs/checks.md:44` 显示这一项就是靠 62% 触发 Warn 的。<br>(c) `README.md:36-37` 把同一个推断升格成了「真正查出来的东西」板块的第一条硬结论：「**GPU 没跑在满频，原因是功耗墙不是温度**……两者的修法完全不同」。`research/14-real-run/01-what-this-machine-found.md:92-94` 同样这么写。 |
| 文档自己留了后路 | `README.md:31` 那一节的标题是「都是本机实测，不是推测」——而这一条是推测。 |
| 附带：`gpu-pick` 的 headline 是空的 | `PerformanceChecks.cs:83-84` 的 Warn 摘要只有 `string.Join("、", unhealthy.Select(a => a.Name))`。所以 `docs/checks.md:38` 里这一行读作「警告 \| **gpu-pick** \| Virtual Desktop Monitor」——一句话没说发生了什么。而这一项的定义问的是「VR 跑在核显还是独显？」（`:55`），报出来的却是虚拟显示器驱动被禁用，标题与结论对不上。 |

### 2.6 存活实例 F（中高）：`av` 在「读不到」的状态下报 Pass

| 项 | 内容 |
| --- | --- |
| 文件与行 | `src/VdHelper/Core/Health/WindowsStateChecks.cs:167-182` |
| 判据 | `:170-171` `e => Lines(e).Count == 0 \|\| Lines(e).All(l => l.Contains("Windows Defender"))` → **零行也算通过** |
| 摘要原文 | `:175` `"已注册杀软：(读不到 SecurityCenter2)"` |
| 为什么越界 | 状态徽标是**通过**，结论里却写着「**读不到**」。`PowerShellCheck.cs:44-50` 那道「stderr 非空且 stdout 为空 → Unknown」的兜底在这里不生效——`Get-CimInstance` 在命名空间缺失时是可以安静返回零行的。这正是 `docs/RELEASE-0.5.0.md:8-12` 那一栏里「假通过」的同一种形状，只是没被登记。 |
| 已知残留缺口 | `research/12-coverage-audit/01-coverage-matrix.md:91` 已把 B5 判为「部分」，理由是只覆盖**已注册**产品；那一栏没提「零行也通过」这一条。 |

### 2.7 存活实例 G（中）：`--adb` 找不到 adb 时返回 3，而 README 说 3 是「有隐患」

| 项 | 内容 |
| --- | --- |
| 文件与行 | `src/VdHelper/App.xaml.cs:460-466`、`:485-487`；`README.md:105` |
| 代码 | 找不到 adb → `:465 return 3;`<br>有 adb 时：`Pass → 0`，`Warn → 3`，**其余（含 Unknown）→ 4** |
| 文档 | `README.md:105`：「`--adb` … `0` 正常 / `3` 有隐患 / `4` 未连上或不可用」 |
| 后果 | 「本机没装 adb」按文档属于「未连上或不可用」= 4，代码给 3，而 3 在同一张表里被定义为「有隐患」。**脚本按退出码分流会把「工具没法工作」读成「机器有隐患」。** 本机碰不到这一支（VIVE Hub 的 adb 在位），所以它从没被跑到过。 |
| 同类但无害的一处 | `--selftest --symptom ZZ` 是**先跑完体检、存了历史**才返回 2（`App.xaml.cs:150-164`）；`--report f.md --symptom ZZ` 是**先校验**再返回 2，一次体检都不跑（`Reports/ReportExport.cs:34-49`）。同一份文档 `README.md:104,107,110` 把两条都写成「参数写错 = 2」，但它们的副作用不同。 |

### 2.8 存活实例 H（中）：生成的文档里写死了一个错的数字，而闸门不看它

| 项 | 内容 |
| --- | --- |
| 文件与行 | `docs/checks.md:53` ← `tools/export-checks.ps1:48` |
| 原文 | `参数项（**111 个，含 18 个只读**）` |
| 实测 | `src/VdHelper/Resources/parameters.json`：**111 项，`readOnly` 19 项，其中 `secret` 18 项，非密文只读键 1 个（`HotKeysEnabled`）** |
| 真相来源 | `docs/RELEASE-0.5.0.md:29-30` 已经写对了：「19 个只读键里 18 个确实是配对密文，`HotKeysEnabled` 不是」 |
| 为什么重要 | `docs/checks.md:3` 的抬头写着「由 `tools/export-checks.ps1` 从**真实运行结果**生成，**不要手工编辑**」——而 `:48` 是一行**手写的常量字符串**，跟运行结果毫无关系。反漂移闸门 `check-symptom-map.ps1` 只核对 README 的检测项数与 `VERSION.txt`（`:60-86`、`:125-136`），不核对这一行。 |
| 同类 | `docs/report-export.md:11`「不带 `--symptom`：按 7 个症状类分组输出全部 **25 项**」——实际输出 35 项（`README.md:81` 写的是 36，同样有 §2.2 的问题）。 |

### 2.9 存活实例 I（中）：`--deep` 的耗时说明与代码里的 100 ms 间隔对不上

| 项 | 内容 |
| --- | --- |
| 文件与行 | `README.md:27-28`；`src/VdHelper/Core/Health/LossProbe.cs:44-61` |
| 原文 | `README.md:27-28`「两个目标都秒回时约 **1–2 秒**；有目标不应答时，每目标约 11–12 秒（单次 ping 的上限是 800 ms……）」 |
| 代码 | `:60` 每次采样之后**无条件** `await Task.Delay(100, ct)`。默认 20 次采样（`:22 DefaultSamples = 20`），两个目标（网关 + 已填的头显 IP，`:147-157`）。 |
| 算出来的下界 | 每个目标 ≥ 20 × 100 ms = **2.02 秒**，两个目标 ≥ **4.04 秒**。即使 RTT 为 0，「1–2 秒」也不可能成立——除非指的是**单个目标**，而 README 写的是「两个目标」。 |
| 另一侧 | 不应答时每目标上界是 20 × (800 + 100) ms = **18 秒**；「11–12 秒」落在 2–18 秒之间，**可能**成立（取决于 OS 何时回 ICMP unreachable），所以这一半我不判为越界。 |
| 状态 | `[未验证]`：我没有跑 `--deep`（`WORKFLOW.md:86` 把会发包的探测排除在自动核对之外）。这一条是**纯代码下界推导**，可由任何人读 `LossProbe.cs:44-61` 复核。<br>旁证：`git log` 第一条是 `3264adf docs: "--deep takes about 20 seconds" was wrong in both directions`——这个数字已经改过一次，仍未对上。 |

### 2.10 存活实例 J（低，但值得记）：README「真正查出来的东西」板块有一条已被工具自己的输出推翻

| 项 | 内容 |
| --- | --- |
| 文件与行 | `README.md:49-51` |
| 原文 | 「**服务在跑 ≠ Streamer 起得来**：本机 `ServiceLog.txt` 反复记录 `HRESULT -2147024891 configured identity is incorrect`。此时所有网络项都显示正常，但 **PC 永远不广播**。」 |
| 现状 | `docs/checks.md:21,23`：`streamer-proc` **通过**（进程运行中）、`udp-discovery` **通过**（UDP 38850 正在监听）。`docs/checks.md:22` 显示最近一条 ERROR 是 **2026-09-18**，而检查本身已经因为「历史错误 + Streamer 在跑」把它降级成 Warn（`StreamerChecks.cs:184-189`）。 |
| 也就是说 | 这条现在是一条**历史事实**，被写在标题为「都是本机实测，不是推测」的板块里、用现在时陈述，且与同一份 README 下面的「检测项全表」矛盾。 |

### 2.11 我查过、但**不是**问题的（避免下一个人重查）

| 疑似问题 | 复核结果 |
| --- | --- |
| `docs/index.html` 首页计数器全是 `0` | 不是 bug。`:122-125,133-136` 是 `<div class="n" data-to="35">0</div>` 形态，`:238-244` 的脚本把它们从 0 动画到 `data-to`。关 JS 才会停在 0。 |
| 根因数 43 与 23 两个口径 | 已处理。源文件 `research/09-failure-corpus/02-symptom-to-rootcause.md:10-12` 自带「口径修正」，`:121-124` 明写「§2 共列出 43 条；本小结只对其中 23 条做过逐条判定」。`tools/export-docs-site.ps1:419-427` 检测到并打印 `SOURCE-MISMATCH`。文档站把两个数并排展示（`docs/index.html:124-125`）。**不过**：`docs/faq.html#coverage` 的覆盖度小结（`15/23`）与 `research/12-coverage-audit/01-coverage-matrix.md:130-133`（14 完全 / 24 完全无覆盖，43 条口径）摆在一起时，前者明显更漂亮，而站内没有一句话说「那 20 条从未逐条评估」。建议补一句，成本是改一行生成脚本。 |
| `HeadsetDeepProbe` 的子判定标成 `B4` | **是问题，但只是引用错号**。`HeadsetDeepProbe.cs:14` 与 `:295` 把「头显侧 VPN」标成 `B4`；语料里 `B4` 是 PC 侧 VPN 后台进程（`research/09-failure-corpus/02-symptom-to-rootcause.md:56`，R19），头显侧 VPN 是 **`A5`**（`:43`，R03）。代码、覆盖矩阵（`:84`）、报告文档（`docs/report-export.md:66`）三方不一致，两个对。**这个标签会直接出现在用户能看到的证据键名里**（`ev["B4 Quest 侧 VPN"]`）。 |

---

## 3. 已知未验证清单

**分类标准**：区分「**被硬件挡住**」与「**从来没被执行过**」。前者只差一次物理动作，后者差一次设计决定或一次改代码——两者的后续完全不同。

### 3.1 被硬件挡住（条件具备就能验，不需要改代码）

| # | 事项 | 证据 | 挡住它的具体条件 |
| --- | --- | --- | --- |
| 1 | 头显侧 adb 三个子判定的**真实输出形态** | `research/06-adb-headset/03-symptom-decision-table.md:160-174` 有一整节 `[未验证]` 汇总，共 9 行 | 头显 USB 连接 + 在头显里点「允许 USB 调试」 |
| 2 | `headset-deep` 在真机上跑出非 Unknown | 同上；`handoff/2026-10-05-round9-coverage-and-finds.md:50-51` 记「头显在 LAN（192.168.11.14，ping 2ms），但 5555/5554/5556/5557/8080/5558 全关 → 没开无线调试」 | 同上。**本轮实跑核对：该 IP 仍在配置里（`config.json`），`adb devices -l` 仍无设备** |
| 3 | `pm grant` 的三种返回（成功空输出 / `not a changeable` / `has not requested`） | `03-symptom-decision-table.md:167`（判据抄自 `VDH.Extra.cs:501-505`，**本轮未在真机复跑**） | 同上 |
| 4 | `dumpsys package` 里 `runtime permissions:` 段的 `granted=true` 形态 | `03-symptom-decision-table.md:168` | 头显连接 + 已执行 `pm grant` |
| 5 | 广播实发抓包 | `notes/2026-10-05-symptom-entry.md:34-50`；`tools/capture-discovery.ps1:1-42` | 以管理员跑一次 `capture-discovery.ps1`（脚本会先检查 Streamer 在不在，`:118-128`） |
| 6 | `--set-param` 的成功路径（真机） | `notes/2026-10-06-config-write-path-verified.md:54`「仍然需要**退出 Streamer**」 | 退出 Streamer（会弹 UAC，且断当前串流） |
| 7 | `fw-restore-vd` / `svc-repair` 的执行 | `notes/2026-10-05-fix-verification.md:60-67` | UAC + 本机规则已存在，重建要先删后建 |
| 8 | `docs/report-export.md` 声称的「点击展开」在 HTML 里真能展开 | `docs/report-export.md:28` | 浏览器（无风险，只缺一次人工确认） |

### 3.2 从来没被执行过（**不是硬件问题**，是设计/实现没做完）

| # | 事项 | 证据 | 为什么它不属于「被硬件挡住」 |
| --- | --- | --- | --- |
| 9 | **`--adb` 从未跑过一次**（本轮之前） | 全仓库 `new HeadsetProbe` 只在 `HeadsetViewModel.cs:99`；`App.xaml.cs:468` 只构造 `HeadsetDeepProbe` | 它在**没有头显**时也能跑：会走 `HeadsetDeepProbe.cs:92-94` 的 Unknown 分支并如实返回。**本轮就可以跑，而且应该先跑一次**——把「adb 发现成功、探针降级路径正常」这两件事先钉死 |
| 10 | `headset-grant` 这个「修复」 | `HeadsetProbe.cs:148-159`：执行体是 `Task.FromResult(new FixResult(true, "用 pm grant 逐条授予：" + …))`——**它一条命令都不执行，永远返回成功**。而且它给的命令串 `adb -s <serial> shell pm grant <package> <permission>` 里 `<package>` 从来没被填过（`:126` 传进去的只有权限名列表） | 这不是硬件问题。这是一个**报成功却什么都没做**的修复动作，与 `handoff/2026-10-05-round6-honest-fixes.md:3-17` 记录的那次「谎报成功」是同一类 |
| 11 | `--set-param` 的成功分支、参数页「切换」的成功分支 | `App.xaml.cs:397-408` 只在 Streamer 在跑时返回 8；`ParametersViewModel` 同理 | 同上，要退出 Streamer；但**副本验证已经做完了**（§1.3），所以真机上只剩「进程不在时那一段代码」的确认 |
| 12 | `--quit-streamer` 的**提权分支** | `Fixes.cs:181-195`：注释明写「在本机上普通权限被拒，所以下面这个提权不是理论回退，它是必须走通的那条路」；`notes/2026-10-06-do-not-sweep-state-changing-commands.md:16-20` 记录它在清扫里**意外执行成功过**（exit 0），随后 Streamer 被停掉 | 它执行过，但那是事故不是验证 |
| 13 | 症状芯片与按钮的「点击 → Command」 | `notes/2026-10-05-ui-input-limits.md:5-20`：合成鼠标输入到不了本窗口，UIA 树里只有 2 个按钮。绕行证据在 `:24-30`（`--deep-ui` 走同一个 `AsyncRelayCommand`） | 环境限制，不是缺陷。但「按钮真的能被点到」这件事**至今没有任何证据** |
| 14 | `ad-…` 之外的成功修复只有 `streamer-launch` 与 `streamer-restart` | `notes/2026-10-05-fix-verification.md:60-67` 的「还没验证的修复」表 | — |
| 15 | **7 项权限的判据本身**（`pm list permissions` + 子串包含） | §2.3 | 这条从一开始就没被执行过，也**不可能**被执行对 |

### 3.3 一句话

**「被硬件挡住」有 8 条，补一个头显就能一次性打开。**
**「从没执行过」有 7 条，其中 4 条（#9、#10、#15，加上 §2.7 的退出码）根本不需要头显就能暴露，而它们现在还活着。**

---

## 4. 头显一插上，第一次真跑会先撞到哪儿

> 本节全部是**读代码 + 读本项目自己的调研**得出的，不是推测；凡是必须实跑才能定论的，标 `[未验证]`。

### 4.1 好消息：adb 发现这一段已经通了

| 环节 | 事实 |
| --- | --- |
| adb 存在 | ✅ 本轮 `ls` 实测在位：`D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe` |
| 候选表能命中它 | ✅ `AdbClient.cs:25` 正是这一条（候选表第 4 项） |
| 顺序 | `RememberedPath` 第一（`:55-56`）→ 候选表（`:58-72`）→ 仓库自带（`:74`）→ PATH 最后（`:77-82`，注释说明 PATH 上的可能是过期 wrapper，所以排在最后）。**这个顺序是对的**，与 `research/06-adb-headset/01-adb-playbook.md:83-85` 抄的旧 VDH 结论一致 |
| 版本 | `01-adb-playbook.md:35-38` 实测 `1.0.41 / 30.0.4-6686687`，`adb pair` 存在（`:108-116`） |

### 4.2 第一次真跑会撞的地方，按可能性排序

#### ① `HeadsetProbe.cs:133` 的权限判据 —— 几乎肯定会假通过（详见 §2.3）

这是**最先发作**的一处，因为它在 `adb` 分支里跑得最早（`:79`），而且失败方式是**静默的假通过**：
`pm list permissions` 的子串包含恒命中 → `missing` 为空 → `:119-122` 走 Pass 分支 → 用户看到
「N 个客户端包在运行，4 项权限齐全」。**而新系统上真正要授的 `horizonos.permission.*` 那 3 条一次都没被查过。**

> `[未验证]` HorizonOS 上 `pm list permissions` 的确切输出形态我无法实跑。但无论它输出什么，
> 「对目录做子串包含来推断授权状态」这一步本身就不成立——本项目自己的 playbook 已经指定了正确命令。

#### ② `HeadsetProbe.cs:88-93` 的进程存活判据 —— 大概率恒为「活着」（详见 §2.4）

`pidof` 无进程时按本项目上一轮的真机记录返回字面量 `no process`，长度 9 > 0 → 记成在跑。
后果是 `notRunning` 那个 `Block` 分支（`:107-117`）永远不触发，`VD 进程存活数`（`:95`）永远好看。

#### ③ `HeadsetProbe.cs:118-127` 的结论文案 —— 未标 `[未验证]`

`:124` 断言「权限缺失的表现：30 秒后 VR 焦点被系统收回、注视点串流被自动关闭、面部追踪分支不执行」。
这三条在 `03-symptom-decision-table.md:167-168` 里明确是 `[未验证]`。项目在 §I（`:179-181`）给自己定的纪律是
「只要期望列写的是形态而不是本机实测输出，UI 上就必须标 `[未验证]`」——**这一条没被执行**。

#### ④ `HeadsetFixes.GrantPermissions`（`HeadsetProbe.cs:148-159`）—— 一旦命中就报假成功

如果 ①② 恰好导致 `missing` 非空（比如将来改对了判据），第三屏会出现一个绿色按钮
「授予缺失的运行时权限」，点下去打印「结果：成功 — 用 pm grant 逐条授予：…」，**一条命令都没执行**。
命令行 `--apply headset-grant` 同理（`App.xaml.cs:321-328` 会照单全收并打印「结果：成功」）。
这正是 `handoff/2026-10-05-round6-honest-fixes.md:3-11` 花一整节描述的那一类事故。

#### ⑤ 参数采集：`HeadsetProbe.cs:59-66` 的五条 `adb shell` 全部 8 秒超时，逐条串行

5 条串行 × 最多 8 秒 = 最坏 40 秒，再加 `ReadPermissionsAsync` 8 秒与每个包的 `pidof` 8 秒。
本轮实测 PC 侧完整体检约 5 秒（`research/12-coverage-audit/02-next-additions.md:64-66`，`WALL_MS=4837`）；
**第三屏在无响应设备上的最坏路径与它完全不是一个量级**，且没有进度反馈。
`HeadsetDeepProbe` 更长：`ProbeMacAsync` 2 条 + `ProbeHeadsetSettingsAsync` 最多 2 文件 × 3 目录 × 2 方式 = 12 条
+ `ProbeQuestVpnAsync` 的 `ps -A` 加最多 25 次 `pidof` 兜底（`:307-311`，`VpnProcesses` 共 25 个名字，`HeadsetDeepProbe.cs:34-61`）。

> 这是**性能/体验**问题，不是正确性问题；但它是「第一次插上头显时用户看到什么」的第一印象。

#### ⑥ `run-as` / `su` 读设置文件：`HeadsetDeepProbe.cs:235-248`

`ReadAsPackageAsync` 先试 `exec-out run-as <pkg> cat <path>`，再试 `exec-out su -c "cat <path>"`。
官方 APK 两样都没有（注释 `:455-457` 自己写了：需要 `android:debuggable=true` 或 root），
所以 **F1 子判定在官方版本上永远是 Unknown**。这一点代码说得很诚实，属于「已知前置条件缺失」，**不是缺陷**——
但它意味着：插上头显之后，`headset-deep` 最多只能回答 A6（MAC）与 B4/A5（VPN）两件事，
第三件要等 APK 进 `analysis/apk_patch/`（`handoff/NOW.md:6` 的第 ② 项，本轮未复核数量）。

#### ⑦ 多设备：`AdbClient.RunAsync` 用的是 `-s <serial>`（`HeadsetProbe.cs:70,88,133`），纪律是对的

`research/06-adb-headset/01-adb-playbook.md:174-181` 明确要求「一律带 `-s`，禁止依赖 `-e`/`-d`」——代码遵守了。
但 `HeadsetProbe.cs:40-44,56` 是 `serials[0]`，**多设备时静默选第一个**；
`HeadsetDeepProbe.cs:96` 同样 `online.Keys.First()`。单设备没问题，两台就悄悄取错。

#### ⑧ 无线调试：本项目自己的调研要求弹警告，代码没弹，反而在推荐它

`research/06-adb-headset/03-symptom-decision-table.md:142` 原文：
「**检测页读到串号形如 `<ip>:5555`（而非 USB 序列号）时必须主动弹警告**」，
理由是「无线 ADB 是**明文**通道……HorizonOS 上 ADB 拿的是 shell(uid 2000)，足以 `pm grant` 提权与装任意 APK」。

代码里**没有任何一处**判断串号形态。而 `HeadsetDeepProbe.cs:435-439` 的 `WiringGuidance`
反而在教用户怎么开无线调试（`adb pair` + `adb connect`），`HeadsetProbe.cs:54` 同样。
**建议被写成了指引，警告被落在了调研里。**

### 4.3 参数解析这一层本身：基本没问题，但有两个坑

| 坑 | 位置 | 说明 |
| --- | --- | --- |
| 分派用 `Contains`、取值用 `IndexOf`，两套 | `App.xaml.cs:45,57,69,77,84,91,110,117` + `Array.IndexOf(args,"--symptom"/"--out"/"--serial"/"--samples"/"--tab")` | 派发顺序是刻意的：`--report` 必须赢 `--selftest`（`:38-44` 有注释记录这是修过的 bug），`--report-html` 必须赢 `--report`，`--deep-ui` 必须赢 `--deep`。顺序本身是对的 |
| **`lan-reach` 在无头模式下改不了** | `ReachabilityCheck.cs:30` 从 `ConfigFile.Read("headsetIp")` 读，而 `headsetIp` 只有第三屏的输入框能写（`HeadsetViewModel.cs:31-42`） | 第一次无头端到端跑之前，`%AppData%\VirtualDesktopHelper\config.json` 里必须已经有 `headsetIp`。**本机已有**（192.168.11.14）。若没有，`lan-reach` 返回 Unknown（`:38-43`），`LossProbe.Targets()`（`:147-157`）也会少一个目标 |

---

## 5. 建议的五件事

排序标准：**能解锁测量的，优先于加功能的**；每件都写「改什么 / 怎么用执行验证 / 风险」。

### 第 1 名：把第三屏的三个判据改成能证伪的（权限 / 进程 / 修复动作）

| | |
| --- | --- |
| **改什么** | ① `HeadsetProbe.cs:23-32` 的权限表按 `03-symptom-decision-table.md:70-79` 重写成 **7 个真实不同的权限串**（`com.oculus.*` 与 `horizonos.*` 成对）；② `:133` 换成 `shell dumpsys package <PKG>` 并解析 `runtime permissions:` 段的 `granted=true/false`；③ `:88-93` 的存活判据改成 `int.TryParse(pidText, out _)`；④ `HeadsetFixes.GrantPermissions`（`:148-159`）要么真的逐条执行 `pm grant` 并检查返回（`not a changeable` / `has not requested` / `Exception` 三种判据见 `03-symptom-decision-table.md:59,167`），要么降级成 `Guidance` 而不是 `FixAction`；⑤ `:124` 的三条断言按 §I（`:179-181`）加 `[未验证]` |
| **怎么用执行验证** | 无需头显即可验证前三项的**解析逻辑**：把 `dumpsys package` 与 `pidof` 的真机输出贴成字符串喂进去，断言 `no process` 不再被判成在跑、`horizonos.permission.*` 能被查到。真机验证 = 插头显后跑 `--adb` + 第三屏，看 `VD 进程存活数` 那一行在 App 关掉时会变成 `0 / 1` |
| **风险** | 极低。全部是本地改动，不碰系统状态。**唯一真正的风险是不做**——这四处目前会输出假通过 |
| **为什么排第一** | 它是 §2 里价值最高的四条，且**不依赖任何新硬件就能先把解析逻辑钉死** |

### 第 2 名：把 `--adb` 变成真正的第三屏等价物，并修掉退出码

| | |
| --- | --- |
| **改什么** | ① `App.xaml.cs:468` 改成先跑 `HeadsetProbe` 再跑 `HeadsetDeepProbe`（照抄 `HeadsetViewModel.cs:99-112` 的两段），或反过来给 `--adb` 一个 `--deep-only` 开关并把 `README.md:16,74` 改成准确说法；② `:465` 的 `return 3` 改成 `4`（与 `README.md:105` 对齐）；③ `HeadsetViewModel.cs:70-125` 与 `App.xaml.cs:453-488` 抽成同一个函数，让 UI 与 CLI 不可能再分叉 |
| **怎么用执行验证** | 现在就能跑：`VdHelper.exe --adb` 在**没有头显**的情况下应当打印 adb 路径、`adb devices -l` 的原文、以及 `headset-deep` 的 Unknown + 接线指引，退出码 **4**。这就是一次不需要硬件的真回归 |
| **风险** | 低。纯重构 + 一行退出码。风险是改完之后 README 的两行描述要同步改，别只改代码 |

### 第 3 名：把「36 项」「25 项」「18 个只读」这类数字交给闸门

| | |
| --- | --- |
| **改什么** | ① 决定 `README.md:13,21,81` 的口径并改对（35 还是 36，说清楚 `headset-deep` 在第三屏）；② `tools/check-symptom-map.ps1:24-27` 的 Adb 目录匹配放宽到能看见 `new CheckResult("adb"`（或让 `HeadsetProbe` 也声明一个 `public const string Id`），这样第三屏的两项都进闸门；③ `tools/export-checks.ps1:48` 的「含 18 个只读」改成从 `src/VdHelper/Resources/parameters.json` 现算；④ `docs/report-export.md:11` 的「25 项」改成不再写死（`docs/report-export.md:37-40` 已经有一次「不再写死项数」的先例，照抄那个处理）；⑤ 顺手修 `HeadsetDeepProbe.cs:14,295` 的 `B4` → `A5` |
| **怎么用执行验证** | 改完跑 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-symptom-map.ps1`，输出里的 `real checks` 与 README 声明项数应当都变成同一个数；`python tools/check-citations.py` 应当仍然是 `OK all 157 citations resolve` |
| **风险** | 低。要小心的是别把闸门改成「永远通过」——`docs/report-export.md:37-40` 记过一次教训：阈值一旦编码了「还有很多缺口」，工具长大后它就会反过来把构建搞挂 |
| **为什么排第三** | 这是本项目**唯一被反复证明有效**的防漂移手段（`docs/RELEASE-0.5.0.md:54-59` 两条新闸门、`WORKFLOW.md:59-70` 发布纪律）。往里加，比加新检测项回报大 |

### 第 4 名：头显插上后，第一件事是跑 `capture-discovery.ps1`，不是跑体检

| | |
| --- | --- |
| **改什么** | 代码不改。改的是**顺序**：`handoff/NOW.md:6` 已经把这一条列为「三件事任一即可解锁」的第一件，但它是三项里唯一一件**现在就能做且不需要 Owner 额外授权**的（`-Analyze` 分支 `:97-112` 连管理员都不要） |
| **怎么用执行验证** | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-discovery.ps1 -Analyze <已有 .txt>` 先单独验判读逻辑（脚本注释 `:29-32` 明说「判读才是容易写错的地方，而它不需要头显、不需要抓包窗口、不需要管理员」）；再在头显在线、Streamer 在跑、且**人在键盘前能点 UAC** 的前提下跑完整抓包 |
| **风险** | pktmon 全局只能有一个会话（脚本 `:18` 自己检查了）；抓包期间会短暂增加网络栈开销。除此之外只读 |
| **为什么排第四** | 它回答的是**唯一一个工具目前结构上答不了的问题**：`research/13-endpoints/02-discovery-protocol.md` 的静态结论说补丁基线不会广播，而 Owner 的一手经验说能。这一条分不出来，「同网段连不上」就永远不该归到网络上（`capture-discovery.ps1:6-12` 原话） |

### 第 5 名：给「没人应答」定一个明确的降级路径（而不是继续等）

| | |
| --- | --- |
| **改什么** | ① `HeadsetProbe.cs:50-54` 的 `Warn "没有连着的头显"` 与 `HeadsetDeepProbe.cs:92-94` 的 `Unknown "没有连着的头显"` 口径不一致（一个 Warn 一个 Unknown），统一；② 给第三屏加一个显式的「我现在知道该做什么」区块——接线四步（`HeadsetDeepProbe.cs:435-439` 的 `WiringGuidance` 已经写好了，直接复用）；③ 把串号形态检测补上：串号形如 `<ip>:5555` 就弹无线 ADB 的明文通道警告（`03-symptom-decision-table.md:142` 的明确要求）；④ `HeadsetProbe.cs:56` 与 `HeadsetDeepProbe.cs:96` 的「静默取第一个设备」改成有第二台就要求选 |
| **怎么用执行验证** | 拔掉/插上头显各跑一次第三屏，两种状态下的文案与退出码必须不同且都说得清「下一步做什么」。这是一次不需要新功能、只需要观察的验证 |
| **风险** | 低。风险是把「Warn」改成「Unknown」之后，verdict 的证据比（`Health.cs:96-98`）被影响——但 `headset-deep` 根本不进 PC 侧 report，所以实际影响为零 |

**明确不建议现在做的**：新增检测项。`research/12-coverage-audit/01-coverage-matrix.md:143` 那 16 条「PC 侧可查」的空白里，
`gpu-throttle`（§2.5）、`gpu-pick` 的驱动版本段（E6）、`display-inventory` 的 `AllScreens` 对照（P4）都**已经在代码路径上**，
先把这几处的判据修对，收益高于加第 36 项。

---

## 6. 应该删掉的东西

> 判据：**要么已经不成立，要么代码路径不可达，要么与实测矛盾**。只列证据，不列感觉。

### 6.1 代码

| # | 删什么 | 位置 | 证据 |
| --- | --- | --- | --- |
| 1 | **Unity Hub 那条 adb 候选，以及为它写的那段通配符分支** | `src/VdHelper/Core/Adb/AdbClient.cs:27` 与 `:60-67` | 该 glob 含字面量 `*`（`...\Editor\*\Editor\...`）。`Path.GetDirectoryName` 得到的 `dir` 里带着这个 `*`，`parent` 因此也是一条**含字面量 `*` 的路径**，`Directory.Exists(parent)` 恒为 false → `:64 continue` → **循环体一次都不会执行**。即使执行了，`Directory.EnumerateFiles(parent, "platform-tools")` 返回的是**文件**而不是目录，再 `Path.Combine(file, "adb.exe")` 也拼不对。这是一段 100% 不可达的代码 |
| 2 | **两条指向不存在目录的 RepoGlobs** | `AdbClient.cs:32-33` | 本轮实测：`D:/Project/VirtualDesktop/_upstream/quest_adb_tools/adb.exe` 与 `D:/Project/VirtualDesktopHelper/tools/platform-tools/adb.exe` **都不存在**。`research/06-adb-headset/01-adb-playbook.md:67-69` 对应的旧 VDH 候选 4/5/6 本机也全是 miss。既然本机的命中项是 VIVE Hub（第 4 个候选），这两条要么补上真实路径，要么删 |
| 3 | **`LossProbe.Sample.Completed` 字段** | `LossProbe.cs:32`、`:79` | `completed` 在 `:42` 声明为 0，**全函数没有任何一处 `completed++`**，所以 `completed >= count` 恒为 false。且 grep 全仓库，`Completed` **只在这一处被写、没有任何一处被读**。纯死字段 |
| 4 | **`Fixes.DisableAdapters(...)`** | `src/VdHelper/Core/Health/Fixes.cs:15-31` | 全仓库唯一调用点是 `Fixes.DisableUnusableAdapters()`（`HealthChecks.cs:87`），而那个函数 `:38-39` **明确返回空数组**并注释「Kept deliberately unused」。于是 `disable-adapter:<name>` 这个 fixId **今天不可能被任何检查产出**。<br>连带的后果：`docs/RELEASE-0.5.0.md:25-26` 把 `disable-adapter` 列为「三个需要管理员的修复不再一声不吭」之一——**发布说明在讲一个工具已经给不出来的动作** |
| 5 | **`Fixes.DisableUnusableAdapters()` 这个空壳** | `Fixes.cs:38-39` | 它被调用（`HealthChecks.cs:87`），但恒返回 `Array.Empty<FixAction>()`。留着的价值只是让 `HealthChecks.cs:112-128` 那段长注释有个挂载点。**建议**：把注释移到 `net-apipa` 的定义处，直接删掉这个方法，调用点改成 `Array.Empty<FixAction>()` |
| 6 | **`RuntimePermissions` 里的三条重复项** | `HeadsetProbe.cs:26,28,30` | 「同上（第二个授权位）」这种权限不存在；真正的第二个授权位是**另一个命名空间**的权限串（§2.3）。删掉重复，补上 `horizonos.permission.*` |
| 7 | ~~整个 `HeadsetProbe`~~ — **不删** | `Core/Adb/HeadsetProbe.cs` | 我本来想把它整个删掉（理由：`--adb` 跑不到它，见 §2.1/§2.2）。但第 1、2 名动作的方向恰恰相反——把它接进 CLI 并修好判据。**方向冲突，撤回这条** |

### 6.2 文档与数字

| # | 删/改什么 | 位置 | 证据 |
| --- | --- | --- | --- |
| 8 | `--deep` 的「两个目标都秒回时约 1–2 秒」 | `README.md:28` | `LossProbe.cs:60` 每次采样后无条件 `Delay(100)`，20 采样 × 2 目标 ⇒ 下界 4.04 秒（§2.9） |
| 9 | 「含 18 个只读」 | `docs/checks.md:53` ← `tools/export-checks.ps1:48` | 实测 19 个只读（`parameters.json`），18 个是密文 |
| 10 | 「全部 25 项」 | `docs/report-export.md:11` | 实际 35 项 |
| 11 | 「PC 永远不广播」 | `README.md:50` | 与 `docs/checks.md:21,23`（`streamer-proc` 通过、`udp-discovery` 通过）直接矛盾 |
| 12 | 「**原因是功耗墙不是温度**」的断言 | `README.md:36-37`、`research/14-real-run/01-what-this-machine-found.md:85-94` | `GpuRuntimeChecks.cs:20-22` 没有采 `power.draw` 也没有采 `clocks_throttle_reasons.active`（§2.5）。**改成**「温度不高而频率偏低；原因未判定，需要 `clocks_throttle_reasons.active` 才能定性」 |
| 13 | `proc-tuner` 定义里的「抢 CPU」 | `MachineStateChecks.cs:64` | 判据是 `StartsWith` 进程名匹配（`:202-213`）。结论文字已经诚实地写了「未测 CPU 占用或线程优先级，只按名字匹配」（`:96`），**但定义里的问句还在承诺 CPU**。用户看到的是标题栏 |

### 6.3 不建议删（我查过，明确反对）

| 东西 | 为什么留 |
| --- | --- |
| `notes/2026-10-05-rejected-loopback-probe.md` | 它记的是一次**被否决的方案**和否决理由（含「挂住 100 秒原因没查出来」这种诚实的未解项）。这是本仓库最有价值的文档类型 |
| `docs/product-spec.md` | 顶部已自标「历史文档」，并写明「留在这里是为了记录当初是怎么定范围的」。**但它里面 `:19`「来自屏 1 与屏 3 的结果合并」这句是错的**，要么改要么删这一句 |
| `research/05-ui-reverse/build/bin|obj` | 已被 `.gitignore` 的 `bin/` `obj/` 排除（`git ls-files` 只跟踪 5 个源文件）。磁盘上留着无害 |
| 仓库根的 `_al.txt` / `_v11.txt` / `_deep.txt` / `_shot*.png` | 已被 `.gitignore` 末尾的 `/_*` 排除（`git status --porcelain` 为空）。是磁盘杂物不是仓库内容；**不影响任何闸门**，不值得为它开一条清理流程 |
| `references/`、`F:` 树 | Owner 的树，AGENTS.md:23-31 明写不要写 |

---

## 附：本文所有「实跑」过的命令清单

```powershell
# 只读，无副作用
ls -d "D:/Software/VIVE Hub/VIVE Hub/CommonTools/ADB/adb.exe"      # 存在
where adb                                                          # 找不到
echo $ANDROID_HOME / $ANDROID_SDK_ROOT                             # 均为空
cat "$APPDATA/VirtualDesktopHelper/config.json"                    # headsetIp + adbPath
ls "$APPDATA/VirtualDesktopHelper/history" | wc -l                 # 40 个 run-*.json
python tools/check-citations.py                                    # OK all 157
powershell -File tools/check-symptom-map.ps1                       # exit 0
git status --porcelain / git log --oneline -15 / git ls-files | wc -l   # 干净 / 15 条 / 142 个跟踪文件
```

**没有跑**：`--selftest` 之外的任何可执行文件、`--deep`（发包）、`--apply`（改状态）、`--set-param`（写配置）、
`--quit-streamer`（杀进程）、`capture-discovery.ps1` 的抓包分支（要管理员且会改抓包会话）。
理由见 `WORKFLOW.md:74-91` 与 `notes/2026-10-06-do-not-sweep-state-changing-commands.md`。
`--selftest` 本身虽然只读，但它的耗时基线（4.84 秒）来自 `research/12-coverage-audit/02-next-additions.md:64-66`
的既有实测，本文直接引用而没有重跑，以避免多写一份历史快照。

---

## 7. 写作期间仓库发生的变化（必须交代）

本文第一遍读完所有文件时 `HEAD` = `3264adf`。写到一半时提交 **`a2fa03a`** 落地
（2026-10-06 05:29 +09:00，作者 dwgx），提交信息自述「Found the moment the headset came online」。

### 7.1 那个提交改了什么

| 文件 | 改动 |
| --- | --- |
| `src/VdHelper/Core/Health/HealthChecks.cs` | `session-stale` 的「残留套接字」分支：`Block` + `Fixes.RestartStreamer()` → **`Warn` + 无修复项**，文案改成「这不等于串流已经断了……只看套接字年龄分辨不出来」。理由是 Windows 在对端发 FIN 或 TCP 超时之前不改状态，**一次跑了半小时的正常串流与一次残留套接字在这张表里长得一样** |
| `src/VdHelper/Core/Health/ReachabilityCheck.cs` | `lan-reach` 加了**有界的本段 /24 主机发现**：配置的地址不应答时扫一遍、点名谁答了、明确拒绝替用户选；6 秒上限、64 个并发 ping、排除自己与网关 |
| `README.md:160` | 调研主题数 14 → 15（因为本文这个目录出现了；提交信息说闸门自己抓到的） |

判定从 `Blocked`（exit 4）变成 0 阻断（exit 3），21 pass / 12 warn / 0 block / 2 unknown。
提交信息自己写了一句很准的话：**「不是机器变好了，是工具不再凭空造故障。」**
这与本文 §2 全节的判断是同一条线。

### 7.2 本文为此做的三处调整

1. §0.2 的基线改为**明确标注两个时点**（`a2fa03a` 之前的 20/12/1/2 与之后的 21/12/0/2），
   不把旧数字当作现状。
2. §1.2 里 `session-stale` 那一行的 `file:line` 已从 `HealthChecks.cs:303-310` 改为
   `HealthChecks.cs:303-319`（新代码块），并补上 `:321-334`（非本网段那一档）。
3. §1.2 里 ` lan-reach` 相关的引用重新核对过：`ReachabilityCheck.cs:30`（读 `headsetIp`）与
   `:38-43`（「还没填头显 IP」→ Unknown）**行号未变**，新增的主机发现在其后，本文未引用具体行号，
   因此不受影响。

**其余全部 `file:line` 引用不受影响**：`a2fa03a` 只碰了上述 4 个文件，而本文引用的
`src/VdHelper/Core/Adb/`（3 个文件）、`App.xaml.cs`、`Core/Health/` 其余 12 个文件、
`tools/`、`docs/`、`notes/`、`handoff/`、`research/` 全部未变（`git diff HEAD~1 HEAD --name-only` 只列出 4 个文件）。

### 7.3 这对 §3（已知未验证）意味着什么

`a2fa03a` 的提交信息说「the headset came online」。如果头显此刻真的在线并在串流，
那么 §3.1 里第 1、2、3、4 条（头显侧 adb 的真实输出形态、`pm grant` 的三种返回、
`dumpsys package` 的 `granted=true` 形态）**很可能已经不再需要物理动作就能验**——
插一根 USB、点一次「允许 USB 调试」即可。

**这恰好是本文第 1 名建议的价值所在**：那四条的判据（`pm list permissions` 子串包含、
`pidof` 非空即活、`headset-grant` 空执行）**在真机上跑起来只会给出「看起来正常」的结果**，
因为它们本来就是恒真的。先修判据、再接硬件，顺序反了就会把一次「跑通了」记成验证通过。

另外，`docs/checks.md` 与 `docs/index.html` 是已提交的生成产物，**仍停留在 `a2fa03a` 之前的数据**
（其中 `session-stale` 那行还写着旧的 Pass 文案）。§2.8 与 §2.10 引用它们时按原样注明，
但这本身也是一条**漂移**：`tools/export-docs-site.ps1` 与 `tools/export-checks.ps1` 需要重跑一次。
