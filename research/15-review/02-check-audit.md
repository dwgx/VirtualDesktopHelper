# 02 — 检测项审计：「结论跑在证据前面」还剩哪些实例

> 范围：`src/VdHelper/Core/Health/`（35 个 PC 侧检测项）、`src/VdHelper/Core/Adb/`（`adb` / `headset-deep` 两个第三屏结果），
> 外加 `Core/Model/Health.cs`、`Core/Checks/`、`Reports/ReportWriter.cs` 的四条输出面。
> 索引：`docs/checks.md`（由 `tools/export-checks.ps1` 从一次真实 `--selftest` 生成）。
>
> **本轮没有执行任何改变机器状态的命令。** 只跑了只读查询（`Get-Service`、`Get-NetFirewallRule`、
> PowerShell 的 `-ErrorAction SilentlyContinue` 行为探测）与 `git log` / `git show`。
>
> 排除项：本会话已修的 12 处（`gpu-throttle` 功耗墙、`av` 读不到却 Pass、`lan-reach` 过期邻居记录、
> `--deep` 耗时、`docs/checks.md` 的只读项数、权限面板的 `[未验证]`、`session-stale`、`net-loss`（部分）、
> `proc-tuner` CPU/线程、`av` 的 `productState`、`HeadsetProbe` 的串号切分、四处状态色对比度），
> 以及 `research/15-review/01-retrospective.md` §2.1–§2.11 已登记的 11 处，均不重复上报。
> 下文凡是与它们**同根因但上一轮没修到的那一支**，会明确写出「上一轮修了 A，这里是漏掉的 B」。

**本轮找到 22 处存活实例**（6 处高 / 10 处中 / 6 处低），另有 4 处跨输出面不一致。
**26 个检测项逐条读过、判定干净**，清单在文末。

---

## 1. 高：结论的方向是错的（Pass / Block 给在「什么都没测到」的机器上）

### 1.1 七个检测项：查询一条都没返回时，Pass 与 Block 照常发出

上一轮修的是 `av`（`WindowsStateChecks.cs:168`）：读不到就别装成通过。**根因没被修掉**——
`av` 是唯一一个把脚本改成 `-ErrorAction Stop` + 显式 `Write-Error` 的检测项，其余七个仍然带着
`-ErrorAction SilentlyContinue`，于是「查询失败」在证据里长得和「查询成功但没有匹配项」一模一样。

**实测（本轮只读探测，两条命令都在本机跑过）**：

```
$ powershell -NoProfile -Command '$ErrorActionPreference="SilentlyContinue"; $e=$null;
    Get-NetNoSuchCmdletHere -ErrorVariable e; "errorrecords=" + $e.Count; exit 0'
errorrecords=0                      ← stderr 一行都没有，stdout 只有我自己的 echo

$ powershell -NoProfile -Command '$ErrorActionPreference="SilentlyContinue";
    Get-NetFirewallRule -DisplayName "ZZZ_NoSuchRule*" | ForEach-Object { $_.DisplayName }; exit 0'
（stdout 为空）                      ← 「没有规则」和「cmdlet 不存在」输出完全相同
```

所以 `PowerShellCheck.cs:44-45` 的守卫

```csharp
var errLines = evidence.GetValueOrDefault("_errLines") ?? "";
if (errLines.Length > 0 && (evidence.GetValueOrDefault("_lines") ?? "").Length == 0)
```

对这些脚本**永远不会触发**：`_errLines` 恒为空。它自己的文档注释（`PowerShellCheck.cs:31-35`）
写的是「PowerShell 即使 cmdlet 不存在也退出 0……把它交给 judge 会让每个『什么都没匹配到』的谓词读成通过」——
描述准确，但**判据选错了通道**：该判据只在脚本不吞错误时有效，而 `PsService`、`PsIcs`、`PsOutbound`、
`PsVdRule`、`PsFwRules` 全部自己吞掉了。

| 检测项 | 脚本（都带静默） | 判据 | 零行时的结论 |
| --- | --- | --- | --- |
| `fw-vd` | `HealthChecks.cs:21` `Get-NetFirewallRule … -ErrorAction SilentlyContinue` | `HealthChecks.cs:351` `Any("Virtual Desktop" && "Allow")` → false | **阻断**：「未找到 Virtual Desktop 入站规则」，并挂上 `fw-restore-vd` 修复按钮（`HealthChecks.cs:356`） |
| `fw-pair` | `FirewallPairChecks.cs:35` `$ErrorActionPreference='SilentlyContinue'` + 末尾 `exit 0` | `FirewallPairChecks.cs:225` `rules.Count == 0` → `NoRules` | **阻断**：「一条 Virtual Desktop 防火墙规则都没有」（`:125`），并挂 `fw-restore-vd` |
| `fw-defender` | `HealthChecks.cs:24` `Get-NetFirewallProfile` | `HealthChecks.cs:362` `!Any("False")` → true | **通过**，摘要退化成 `Defender 防火墙：`（后面什么都没有） |
| `fw-outbound` | `WindowsStateChecks.cs:24` `Get-NetFirewallProfile` | `WindowsStateChecks.cs:160` `!Any("Block")` → true | **通过**，摘要 `出站策略：` |
| `fw-profile-inbound` | `PerformanceChecks.cs:22` | `PerformanceChecks.cs:43` `!Any("NotConfigured" \|\| "Block")` → true | **通过**，摘要 `profile 默认入站：` |
| `ics` | `WindowsStateChecks.cs:19` `Get-Service SharedAccess -ErrorAction SilentlyContinue` | `WindowsStateChecks.cs:150` `!Any("Running")` → true | **通过**，摘要 `SharedAccess(ICS)：Stopped` |
| `svc-vd` | `HealthChecks.cs:27` `Get-Service -Name 'VirtualDesktop*' -ErrorAction SilentlyContinue` | `HealthChecks.cs:370` `Any("Running")` → false | **警告**，摘要 `服务未安装`（`:371` 的字面量） |

**最贵的是前两行**：它们在「工具读不到防火墙」时给出**阻断**，并附一个会去改真实防火墙规则的按钮
（`Fixes.RestoreVdRule()`）。这正是 `01-retrospective.md` §2.6 记的 `av` 那一族的完整形态，
只是这次发生在两个会动手的检测项上。

**修法**（三处，最小改动）：

1. `PowerShellCheck.Create` 增加一个 `bool requireRows = true` 参数，表示「零行 = 没测到」；
   在 `PowerShellCheck.cs:45` 的守卫之后追加一条 `if (requireRows && _lines 为空) → Unknown`。
   六个 Pass 分支的检测项（`fw-defender` / `fw-outbound` / `fw-profile-inbound` / `ics` /
   `net-profile` / `svc-vd`）全部默认开启——它们的问题问的都是「有没有这个东西」，零行只可能是读不到。
2. 把 `PsService` / `PsIcs` / `PsVdRule` 里的 `-ErrorAction SilentlyContinue` 去掉，
   让失败真的落到 stderr，现有的 `_errLines` 守卫就能像 `av` 那样生效（`Get-Service` / `Get-NetFirewallProfile`
   在本机都存在，去掉之后本轮这次实测的输出不变——上面第二条命令已经证明 `Get-NetFirewallRule` 正常时
   对不匹配的过滤条件本来就是空 stdout + exit 0，所以零行的语义仍然是「没有匹配项」，
   区别只在**能不能区分**「没有」和「报错」）。
3. `fw-pair`（`FirewallPairChecks.cs:118-125`）：它是唯一不走 `PowerShellCheck` 的，
   `ev["stderr"]` 在 `:116` 已经采集却从不被查询。加一句
   `if (rules.Count == 0 && stderr 非空) → Unknown`；若同时 stderr 为空，
   摘要应写成「查不到任何 Virtual Desktop 规则（查询成功返回 0 条）」而不是断言「一条都没有」。

---

### 1.2 `net-loss`：混合情形仍然报 100% 丢包，且与 `--deep` 的判定口径不一致

上一轮修的是「睡着的头显被报成 100% 丢包」。**修的是两条分支，不是这一类**：
`LossProbe.cs:116` 的条件是 `silent.Count > 0 && (clean.Count > 0 || silent.Count == seen.Count)`，
第三种组合没被覆盖。

```csharp
// LossProbe.cs:113-131
var silent = seen.Where(r => r.Received == 0).ToList();
var clean  = seen.Where(r => r.Received > 0 && r.Loss < 5).ToList();

if (silent.Count > 0 && (clean.Count > 0 || silent.Count == seen.Count))   // :116
    … "完全不应答（0 收到）"
if (worst >= 5)                                                             // :126
    … $"测到 {worst:F0}% 丢包（快速采样 {InPassSamples} 次）"                // :127
```

`silent=1, clean=0, seen=2` 这一格——**网关有部分丢包 + 头显完全不应答**——既不满足 `:116`
（`clean.Count == 0` 且 `silent.Count != seen.Count`），又满足 `:126`（`worst` 被那个 0 收到的目标抬到 100）。
于是界面/报告打印「**测到 100% 丢包**」，并附上「网关也丢 → 问题在 PC 到路由器这一段」的指引。

同一次采样、同一个 `Sample`，`--deep` 走的是另一套分类（`LossProbe.cs:224-249`），
它把 `dead`（0 收到）和 `partial`（收到但 ≥5%）分开，于是打印：

```
默认网关 192.168.11.1 部分丢包 25%（6/8 收到）——这一段是通的，但不稳。
头显 192.168.11.14 完全不应答（0/8）——这不是丢包，是它此刻不在应答。
```

**同一个测量，两个输出面给出互相矛盾的数字与归因**，而真实丢包是 25% 而不是 100%。
`Verdict()` 的分类（`dead` / `partial` / `clean`）本来就是正确的那一套。

**修法**：把 `:113-126` 整段换成 `Verdict()` 已有的三分类：`partial.Count > 0` 单独出一条
「测到丢包 X%（网关 …）」，`dead.Count > 0` 单独出一条「… 完全不应答」，
摘要里的数字取 `partial` 的最大值而不是 `worst`（`worst` 现在把「不应答」也算成 100% 丢包）。
这样 `--selftest`、`--report`、`--deep` 三条面共用同一个判定函数。

---

### 1.3 `adb`：权限 dump 全部失败 → 7 条权限全部被说成「系统不认识」→ **通过**

```csharp
// HeadsetProbe.cs:217-219
var r = await adb.RunAsync(["-s", serial, "shell", "dumpsys", "package", pkg], 15000, ct);
if (!r.Ok) continue;                       // :218  ← 失败被静默跳过，什么都不留
…
if (!knownSet.Contains(perm)) { Absent.Add(perm); continue; }   // :237
```

`dumpsys package` 对所有包都失败（超时、SELinux 拒绝、镜像裁剪）时，`knownSet` 为空，
**7 条权限全部落进 `Absent`**，证据里逐条写下 `该系统不认识这条（非故障）`（`:137`），
`missing` 为空，于是 `:181-183` 给出：

```
通过 · {installed.Count} 个客户端包在运行，0 项权限齐全
```

两个断言都没有测量支撑：**「权限齐全」没测过**（一条都没读），**「该系统不认识这条」是错的**
（认识，只是没读到）。这是「nothing measured → Pass」的最干净形态，而且证据栏主动把
「没读到」写成了「不存在」。

**修法**：`ReadPermissionsAsync` 返回一个「至少成功读过几个包」的计数；计数为 0 时，
`RunAsync` 走 Unknown 分支（`权限状态读不到：dumpsys package 对 N 个包都失败，这一项没有测到任何权限`），
并把失败原因写进证据，而不是写进 `Absent`。

---

### 1.4 `wifi-quality`：把「信道 149 通常是较空闲的选择」算成「可疑之处」

```csharp
// WifiQualityCheck.cs:140-152  (判定入口 :143)
if (channelNum is >= 149 and <= 177)
    notes.Add($"信道 {channelNum} 在 5 GHz 高信道段，通常是较空闲的选择。");   // :141
…
if (notes.Count == 0) → Pass "无线链路正常（…）"                              // :143-145
→ Warn  "无线链路有可疑之处：{band}，信道 {channel}，信号 {signal}"            // :149
```

`notes` 是**警告清单**，但 `:141` 往里塞了一条正面评价。于是 5 GHz 高信道（149/153/157/161，
**正是本工具自己在 `:153` 的指引里推荐的信道**）上信号良好的机器会拿到：

```
警告 · 无线链路有可疑之处：5 GHz (Wi-Fi 6)，信道 149，信号 97%
为什么：信道 149 在 5 GHz 高信道段，通常是较空闲的选择。
```

判定分支（`:143`）和证据内容直接互相打脸。这个检测项存在的理由（类注释 `:6-8`）是
「5 GHz 信道太窄或太挤导致卡顿」，而它把最推荐的信道判成了要怀疑的。

**同一个判据还有反向的一格**：2.4 GHz 连上、信号 90%、协商 72 Mbps 时，
`:137-139` 的 2.4 GHz 分支只在 `rx > 100`（自相矛盾）时才说话，所以 `notes` 为空 → **通过「无线链路正常」**。
类注释里点名的失败模式（2.4 GHz / 拥挤信道）恰好落在这个「干净」的格子里，
而 detail 写的「这几项没有明显的丢包来源」（`:146`）从未测过任何丢包。

**修法**：`notes` 拆成 `warnings` 与 `notes`（后者只进证据与 detail，不参与 `notes.Count == 0` 的判定）；
另加一条真正的警告：`band` 以 `2.4 GHz` 开头时 Warn「连的是 2.4 GHz——官方要求 5GHz」。

---

### 1.5 `headset-deep`：`pidof` 兜底仍然是「非空即活着」

上一轮把 `HeadsetProbe.cs:88-93` 的 `pidText.Length > 0` 改成了 `int.TryParse`，
**同一份判据在 `HeadsetDeepProbe` 里没改**：

```csharp
// HeadsetDeepProbe.cs:334-337
bool live = names.Count > 0
    ? names.Any(n => IsVpnProcess(n, name))
    : (await TryAsync(["-s", serial, "shell", "pidof", name], 6000, ct))
        is { Ok: true, StdOut: { Length: > 0 } };
```

`ps -A` 读不到时（降级点 5）走 `pidof`，判据是**stdout 非空**。
本项目自己的实测记录（`research/06-adb-headset/01-adb-playbook.md:257-259`，由 `01-retrospective.md` §2.4 引用）
写着 `pidof` 无进程时返回字面量 `no process`；无论它返回什么，**「非空即活着」都不成立**：
shell 警告、错误信息都在 stdout 里。

后果：头显上**没有 VPN** 的机器，只要 `ps -A` 恰好读不到，就会命中 25 个名字里的若干个，
报出 `警告 · Quest 上挂着 VPN/代理进程` 并给「去头显里把 VPN 退掉」的指引——
一条凭空捏造的根因，且指向用户最不该动的设置。

**修法**：与 `HeadsetProbe` 对齐——`.Where(x => int.TryParse(x, out _))` 后再判空；
若某个名字的 `pidof` 返回非数字，计入证据「读不到（输出：…）」。

---

## 2. 中：措辞里的原因、数量或状态，判据不支持

### 2.1 `gpu-throttle`：温度分支断言「频率上不去」，而它只看温度

```csharp
// GpuRuntimeChecks.cs:83-88
if (f.TemperatureC >= 85)
    return new CheckResult("gpu-throttle", CheckStatus.Warn,
        $"GPU 温度 {f.TemperatureC}°C，当前频率只有最高频率的 {ratio:P0}",   // :85
        "高温 + 频率上不去 = 典型的降频。…",
```

判据是 `TemperatureC >= 85`，**完全不看频率**。一张 85°C 且正跑在 `3090/3090 MHz`（ratio = 100%）的卡
会打印「当前频率只有最高频率的 **100%**」——这句话断言的正是它没测到的那件事，且用的是
「只有」这个明确表示不足的词。同一文件 `:93-95` 的注释写着「判据要落在驱动说了什么上面」，
这个分支没跟上。

**修法**：分支条件加上 `ratio < 0.9`，或把文案改成只说温度
（「GPU 温度 {T}°C，已达降频区间；当前频率 {current}/{max} MHz（{ratio:P0}）」），两者取其一。

### 2.2 `nat-type`：NAT 类型是从「没有 38810 映射」推出来的，而这个映射本来就不该存在

```csharp
// NatChecks.cs:93-102
// 判定：映射拿得到 = Open；设备在、但没有 38810 的入站映射 = Cone/Restricted。
var natType = mapping is not null ? "Open" : "Cone/Restricted";                    // :97
var summary = $"路由器支持 UPnP（{ev["设备类型"]}），NAT 类型 {natType}";            // :99
```

同一个函数在 `:104-107` 自己写着：Streamer 没勾 "Allow remote connections" 或根本没在跑时，
**本来就不会有这条映射**。也就是说，`:97` 把「没勾远程连接」翻译成了「Cone/Restricted NAT」。
这条探测从头到尾没有做过任何 NAT 映射行为测试（`GetSpecificMappingAsync` 只读一条映射，
`GetExternalIPAsync` 只读一个 IP），Cone / Port Restricted / Symmetric 三者一个都没被测到。

另外两处：

- `:64-65` `devices.FirstOrDefault(d => … AddressFamily == InterNetwork)` —— 取的是
  SSDP 回应里的**第一个 IPv4 设备**，不是默认网关。证据栏 `:50` 已经把「本机网关」读出来了却没用来选设备。
  多路由器 / mesh 组网（Google Nest、Eero）时，选中的可能是网 Mesh 节点，
  摘要仍然写「路由器支持 UPnP」。
- `:101` `外网 IP 是 {ext}（私有段），上级还有一层 NAT` —— 若上面选错了设备，这个私有 IP
  是 mesh 节点的上联 LAN 口地址，「上级还有一层 NAT」就成了假命题。

**修法**：把 `:97` 改成只报可测的事实（`路由器响应 UPnP 控制面` / `路由器没有 38810 的入站映射`），
NAT 类型要判定就得做映射行为探测或打 STUN；`:64` 改成优先选 `device.LocalAddress` 与本机同网段的那台；
`:101` 的双层 NAT 结论加一句「（若选中的 UPnP 设备不是默认网关，这一条不成立）」或直接降级为证据行。

### 2.3 `udp-discovery`：把到官方服务器的 Established 也算成「串流中」

```csharp
// StreamerChecks.cs:284-310
var live = tcp.Where(p => p.State == PortState.Established).ToList();   // :284  ← 不区分对端
…
if (live.Count > 0)
    return new CheckResult("udp-discovery", CheckStatus.Pass,
        "串流中，Streamer 已释放发现端口（正常）", …);
```

`session-stale` 花了一整段注释证明同一件事（`HealthChecks.cs:257-292`）：
Streamer 启动后会主动连 `40.89.161.236:38812` 这类公网服务器端点，
**那是 Established，但它不是串流**。同一份报告里，`session-stale` 明确写
「**这是「没有在串流」，不是「串流中」**」，而 `udp-discovery` 在同一时刻说「串流中」。
两个检测项读同一个 `PortState.Established` 集合，得出相反的结论。

**修法**：抽出 `NetworkInventory` 侧的 `IsLanPeer(p)`（`session-stale` 里已有 `SameNet` 逻辑，
`HealthChecks.cs:223-233`），`udp-discovery` 复用它，只把 LAN 的 Established 当作会话。

### 2.4 `headset-deep` 的 F1 子判定：键没读到 → 通过；证据写「= 开」而值是 false

```csharp
// HeadsetDeepProbe.cs:245-253
if (read == 0) return new Sub(key, CheckStatus.Unknown, …);
var summary = $"头显设置读到 {read}/{SettingsFiles.Length} 个文件";
if (handKeySeen && handTracking) return new Sub(key, CheckStatus.Warn, …);
return new Sub(key, CheckStatus.Pass, summary);          // :253  ← handKeySeen == false 也走这里
```

- `handKeySeen == false`（文件读到了，但里面根本没有 `HandTracking` / `UseMultiModal` 键）
  与 `handKeySeen && !handTracking`（明确读到「关」）被合并成同一个 **Pass**。
  前者对 F1（R57 的根因）来说等于没测。
- `read == 1`（两个候选文件只读到 `UserSettings.json`）也报 Pass，
  摘要写着「读到 1/2 个文件」，但没说是哪一个没读到。

还有一处证据与判定相反：

```csharp
// HeadsetDeepProbe.cs:313
var hand = handKeySeen ? "；HandTracking/UseMultiModal = 开" : string.Empty;
```

`handKeySeen` 只表示「键出现过」，值可能是 `false`。头显上手部追踪**明确关闭**时，
证据栏照样写 `= 开`——而判定分支（`:250`）用的却是那个布尔值。
证据说开着，判定按关的处理。

**修法**：`handKeySeen == false` → Unknown（`读到了设置文件，但里面没有 HandTracking/UseMultiModal 键，这一项判不了`）；
`:313` 改成按 `handTracking` 取值输出 `= 开/= 关`；`read < SettingsFiles.Length` 时在摘要里点名缺哪个文件。

### 2.5 `adb` 摘要：「客户端包在运行」用的是**已安装**的包数

```csharp
// HeadsetProbe.cs:141-156（实测）
var installed = PackageNames.Where(p => packages.Contains(p, …)).ToList();   // pm list packages
… var pids = …Where(x => int.TryParse(x, out _)).ToList();                     // :149
   if (pids.Count > 0) running.Add(…); else ev["进程 " + pkg] = "未在运行";
…
// HeadsetProbe.cs:181-183（摘要）
? $"{installed.Count} 个客户端包在运行，缺 {missing.Count} 项运行时权限"
: $"{installed.Count} 个客户端包在运行，{granted.Count} 项权限齐全"
```

存活判定写进 `running.Count`，摘要把 `installed.Count` 说成「包在运行」。
同时装了两个包（官方 `VirtualDesktop.Android` + 补丁 `com.dwgx1.vd.recovered` 是这个项目的常态）、
只跑起来一个时，用户会读到「2 个客户端包在运行」。
证据栏 `VD 进程存活数`（`:156`）倒是诚实的 —— 摘要和证据在同一个报告里互相矛盾。

**修法**：摘要改用 `running.Count`；若 `installed.Count != running.Count`，
摘要写成 `{running.Count}/{installed.Count} 个客户端包在运行`。

### 2.6 `link-type`：Pass 说「符合官方对电脑端的要求」，而要求里的 5GHz 路由器从没被测

```csharp
// WindowsStateChecks.cs:244-253
["官方要求"] = "Wired computer to 5 GHz AC or AX Wi-Fi router",      // :244
if (wired.Count > 0)
    → Pass  $"PC 走有线（{wired[0].Name}）", "符合官方对电脑端的要求。", …      // :247-248
else → Warn (wireless.Count > 0 ? "PC 只走无线" : "没有可用的链路"),          // :251-252
        "官方 Computer Requirements 要求电脑走网线接 5GHz 路由器；纯无线更容易掉帧与断链。"
```

- 判据只有 `wired.Count > 0`。它没读路由器型号、没读 5GHz/6GHz、没读链路协商速率
  （`link-rate` 读得到，也没在这里用）。Pass 的文案「符合官方对电脑端的要求」
  覆盖的是需求全文，而只验证了前半句。
- 同一个 detail 在 `wireless.Count == 0`（压根没有无线，只有别的路径断了）时也照抄
  「纯无线更容易掉帧」，`:251` 的摘要写的是「没有可用的链路」——文案说的是另一件事。
- 「更容易掉帧与断链」是本项目语料里的结论，不是这一项测出来的；它出现在一个「本项只测了
  走的是有线还是无线」的检测项里，没有 `[未验证]`。

**修法**：Pass 文案改成「PC 走有线（满足官方要求的前半句；路由器是否为 5GHz AC/AX 本项未测）」；
`:251` 的 detail 按 `wireless.Count` 分两句。

### 2.7 `nic-powersave`：问的是「空闲时会不会睡着」，测的是唤醒位；Pass 断言「休眠唤醒后网卡正常回来」

```csharp
// MachineStateChecks.cs:44-47（采集）
"…|WakeOnMagicPacket=$($_.WakeOnMagicPacket)|WakeOnPattern=$($_.WakeOnPattern)|"
+ "DeviceSleepOnDisconnect=$($_.DeviceSleepOnDisconnect)"
// :121-123（判据）
var risky = lines.Where(l => l.Contains("WakeOnMagicPacket=False") || l.Contains("WakeOnPattern=False"))
```

- 问题写的是「网卡会不会在空闲时睡着？」，判据里两个字段都是**唤醒**能力；
  唯一与「睡着」相关的 `DeviceSleepOnDisconnect` 被采集进证据却从未参与判定。
- `:126-128` 的 Pass 摘要「没有网卡关闭了网络唤醒」是对的，detail「休眠唤醒后网卡正常回来。」
  是一句关于**整机睡眠行为**的断言——这一项既没有让任何网卡睡过，也没有读过 `AllowComputerToTurnOffDevice`
  （D0 电源管理）这个真正管「空闲睡着」的字段。

**修法**：要么把 `DeviceSleepOnDisconnect=False` 加进 `risky`，要么把问题改成
「网卡休眠后能不能被唤醒」。Pass 的 detail 去掉「休眠唤醒后网卡正常回来」，
换成「本项只读唤醒能力，没有让任何网卡进入过睡眠」。

### 2.8 `svc-log`：阻断分支断言「被系统拒绝」「不会广播」，而只解析了 ERROR 这个级别

```csharp
// StreamerChecks.cs:150-152
var errors = lines.Select(l => ErrorLine.Match(l)).Where(m => m.Success).ToList();
var recent = errors.Where(m => m.Groups["level"].Value == "ERROR").ToList();
// :191-194
"含义：服务尝试拉起 Streamer 时被系统拒绝，网络层再正常也不会广播。这是「各项都正常但连不上」的典型原因。"
```

判据只有 `level == "ERROR"`。消息字段（`groups["msg"]`）被截断 160 字符塞进证据（`:154`），
**从未参与判定**。所以任意一条 ERROR——网络错误、配置错误、用户取消——都会被翻译成
「被系统拒绝」和「不会广播」这两个具体断言。`01-retrospective.md` §2.10 已经记过同一个日志的
`-2147024891 configured identity is incorrect`，那是这台机器的一条，不是这一类日志的通义。

**修法**：阻断文案改成「最近一次 ERROR（原文见证据）：{msg 前 80 字}」，
让原因由日志说话；只有当消息里确实含 `0x80070005` / `Access is denied` / `identity is incorrect` 时，
才追加「这通常是服务身份绑定问题」。

### 2.9 `gpu-encoder`：通过分支说「硬件编码器正在工作」，会话没有归属

```csharp
// GpuRuntimeChecks.cs:46-49
if (f.EncoderSessions > 0)
    return new CheckResult("gpu-encoder", CheckStatus.Pass,
        $"硬件编码器正在工作（{f.EncoderSessions} 个编码会话）",
        "说明此刻确实有硬件编码会话在跑。", …);
```

检测项的问题是「**串流的**编码会话现在在不在硬件上？」。`nvidia-smi` 的
`encoder.stats.sessionCount` 是**整机 NVENC 会话总数**，不区分是谁建的——
OBS、Chrome、ffmpeg、剪映都会占用它。在没开串流、只是剪视频的机器上，这一项会报
「硬件编码器正在工作（1 个编码会话）」，用户会读成「我的串流在走硬件编码」。
（同文件 `:52-56` 的空闲分支反而写得很克制：「**工具无法证明 VD 串流时用的是硬件编码还是软件编码**」——
两个分支对同一份证据给了宽严相反的结论。）

**修法**：两个分支统一成「本机此刻有 N 个 NVENC 编码会话；工具无法判断其中有没有 VD 的，
也无法证明 VD 串流走的是硬件还是软件编码」，并在无会话时保留现在那句好的说明。

### 2.10 `fw-defender`：问题问「是否被接管」，只读了 `Enabled`；detail 断言「入站与广播都可能被拦」

```csharp
// HealthChecks.cs:24（采集）
"Get-NetFirewallProfile | Select-Object Name,Enabled | …"
// :360-366
new("fw-defender", "防火墙总开关", "三个 profile 是否都被接管或关闭？", "防火墙", …),
e => !Lines(e).Any(l => l.Contains("False")),                                 // :362
_ => "任一 profile 被关闭或接管，VD 的入站与广播都可能被拦。",
```

`Enabled=False` 只说明 Defender 防火墙关了，**读不到「被第三方接管」**——
而「被接管」恰恰是这个检测项写在问题里的那一半，也是 detail 点名的原因。
「入站与广播都可能被拦」是一次拦截断言，而这一项没有读任何一条 Block 规则
（`fw-pair` 读了，只查针对 VD 的出站 Block）。本机基线 `docs/checks.md:17` 就是
`Domain True / Private False / Public False`，即 Private 档被第三方接管——这一项给出警告是对的，
但它给出的**理由**（「VD 被拦」）没有任何证据。

**修法**：问题改成「三个 profile 的 Defender 防火墙开关状态」，detail 改成
「Private 档 Defender 关闭，通常意味着第三方防火墙接管了这一档；本项没有读任何拦截规则，
要确认 VD 是否被拦看 fw-pair」，并在证据里加一行当前默认入站动作（`fw-profile-inbound` 已经在读）。

---

## 3. 低：数字、标签与措辞的细节

| # | 位置 | 现状 | 问题 | 修法 |
| --- | --- | --- | --- | --- |
| 3.1 | `HealthChecks.cs:342` + `:383-390` | `net-profile` 判据是「有 Private 行且无 Public 行」，摘要函数 `DescribeProfile` 却是 `Lines(e).FirstOrDefault(l => l.Contains("Ethernet"))` | 只走无线、或系统语言本地化导致接口别名不是 `Ethernet`（中文 Windows 是「以太网」）时，**状态是通过**、摘要写「主网卡网络类别：**未取到**」 | 摘要用与判据同一组行（取第一行判读 NetworkCategory），取不到就 Unknown |
| 3.2 | `HealthChecks.cs:352-354` | `fw-vd` 摘要 `?? "Virtual Desktop Streamer"` | 找不到任何 VD 行时用字面量补一个规则名显示给用户；且失败分支的摘要写「找到入站放行规则（…）」，而它匹配的行可能是 `Action=Block` | 失败分支统一写「未找到入站放行规则」并把匹配到的原文整行放进证据 |
| 3.3 | `ReachabilityCheck.cs:105-113` | Pass detail「如果头显里还是「连不上」，问题在 VD 应用侧或账号侧，**不在网络**。」 | 本项只做了一次 ICMP ping；同一函数里测到的 TCP 38810/38820 探测结果（`:105`）在 Pass 分支被整段丢弃 | detail 改成「ICMP 通（ping N ms）。UDP 广播与 TCP 会话路径本项未测，要排除网络因素看 link-rate / net-loss / 端口归属」 |
| 3.4 | `WindowsStateChecks.cs:222-225` | `route-metric` Pass detail「定向广播会从物理网卡发出。」 | 判据只有接口 metric 的大小关系，一个包都没发、一个广播都没看 | 改成「没有虚拟网卡的 metric 比有线低；本项没有发过广播包」 |
| 3.5 | `StreamerChecks.cs:127-135` | 阻断 detail「**本机 2026-09-07 起就持续记录启动失败**，见 svc-log 项。」；通过 detail「PC 侧会周期广播 UDP 38860，并监听 38850/38810-40。」 | 第一句把**这台机器的一次事故**写成所有机器上 Streamer 没跑时的通用说明（其它机器上这行是假的）；第二句断言了本项完全没测的行为（一个包都没抓） | 第一句删掉或改成「本机日志见 svc-log 项」；第二句改成「进程在跑。是否真的在广播要看 udp-discovery」 |
| 3.6 | `HealthChecks.cs:74-92` + `NetworkInventory.cs:105-106` | `net-primary` 通过时写「主用 {ups[0].Name}」 | `PrimaryCandidates()` 只是 `ReadAdapters().Where(IsUp && …)`，**没有排序**；`ups[0]` 是枚举顺序第一个，不是主用网卡。另外同一台机器上 `net-primary` 说「有网卡可用」而 `link-rate` 对同一份数据说「没有网卡拿到默认网关 → 未知」 | 「主用」改用「其中一块」；或与 `route-metric` 共用 metric 最小的那块 |
| 3.7 | `LossProbe.cs:137` | 通过分支 detail 写死「而且只有 8 次采样」 | 同一行的摘要用的是 `InPassSamples` 常量（`:29` = 8）。改常量后两处会漂 | 用 `{InPassSamples}` |
| 3.8 | `LossProbe.cs:42,79` | `Sample.Completed` 由 `completed >= count` 得出，而 `completed` 声明后**从未自增** | 该字段恒为 `false`；全仓库无人读它（grep 只命中声明与构造），所以现在无害——但它看起来像「采样是否完整」，谁接上去谁会踩雷 | 自增 `completed`，或删掉这个字段 |

---

## 4. 四条输出面：它们在哪里不一致

| 面 | 位置 | 差异 |
| --- | --- | --- |
| console | `App.xaml.cs:222-227` | 直接 `foreach ((k,v) in r.Evidence)` 原样打印，**不过 `Redact`**；markdown / HTML 都过 `ReportWriter.Redact`（`ReportWriter.cs:30,80`）。今天没有检测项把 DPAPI 密文放进证据（`FirewallPairChecks.cs:294` 的注释与实现都保证了），但这是**唯一一条没有兜底的面**——兜底一旦漏了，只有 CLI 会泄。 |
| console vs 报告 | `App.xaml.cs:217` 用 `r.Status.ToString()`（`Pass`/`Warn`/…），`ReportWriter.cs:37-43` 用中文徽章（`通过`/`警告`/…） | 同一个状态两种字面。用户把 `--selftest` 输出贴进 issue、别人看的是 HTML 报告时，对不上号。注释（`ReportWriter.cs:32-37`）说「两边一起改」，但它抄的是 WPF 转换器，不是 console。 |
| 报告 | `ReportWriter.cs:60-66` | `InternalKeys` 收录了 `_exit`/`_lines`/`_first`/`_script`/`_耗时`，**漏了 `_errLines`**。于是证据栏里会出现一行原始键名 `_errLines: ...`，与它上面那行 `命令退出码: 0` 风格不一致；这一行恰恰是 §1.1 那条守卫的输入。 |
| history | `HealthHistory.cs:59-66` | 只存 `Summary` 与 `Status`，不存 `Detail` / `Evidence`。「与上次的变化」因此只能比摘要——一条「摘要没变但 detail 里数字变了」的检测项（例如 `net-loss` 的采样结果变化不影响摘要）不会出现在变化列表里。这不是分歧，但它是 §1.2 那类问题的放大器。 |

---

## 5. 查过、判定干净的检测项

以下 26 项逐条读过：采集字段 → 判据 → 每个分支的文案 → 四个输出面。**没有发现结论跑在证据前面的地方**，
或发现的已在本会话修掉（`av`、`proc-tuner`、`gpu-pick` 的零适配器、`session-stale`、`lan-reach` 的邻居状态）。

`net-apipa`、`net-virtual`、`port-vd`、`cfg-streamer`、`link-rate`、`vpn-proc`、`rdp-session`、
`accounts-persisted`、`proc-tuner`、`display-inventory`、`cfg-version`、`svc-log`(历史分支)、
`av`、`gpu-pick`、`session-stale`、`lan-reach`(邻居状态判定)、`route-metric`(判据本身)、
`net-primary`(判据本身)、`net-profile`(判据本身)、`ics`/`fw-outbound`/`fw-profile-inbound`(判据本身)、
`fw-pair`(Judge 的四个分类)、`streamer-proc`(进程存在性)、`udp-discovery`(端口归属)、
`net-loss`(三分法)、`wifi-quality`(信号与 DFS 判定)、`gpu-throttle`(驱动降频原因判定)、
`HeadsetDeepProbe`(A6 MAC 位判定)。

几处**刻意写对了**、值得保持的，供下一个人不必重查：

- `net-apipa`（`HealthChecks.cs:112-127`）：一整段注释解释了为什么**故意不提供**禁用动作。
- `display-inventory`（`MachineStateChecks.cs:154-156`）：区分「没读到设备」与「设备都正常」，
  零设备走 Unknown。
- `link-rate`（`LinkRateChecks.cs:98-100`）：`-1` 当未知而不是慢，负值跳过不惩罚。
- `rdp-session`（`LinkRateChecks.cs:323-327`）：`listen`/`idle` 槽位不算占屏，并有 `exit 0` 归一。
- `fw-pair`（`FirewallPairChecks.cs:29-31`）：`Test-Path` 前先展开环境变量，注释记了实测。
- `accounts-persisted`（`FirewallPairChecks.cs:294-296`）：只报分组名与条目数，不碰密文。
- `nat-type`（`NatChecks.cs:15`）：全程只读，从不调 `CreatePortMapAsync`。

第三屏：`HeadsetDeepProbe` 的 A6（`HeadsetDeepProbe.cs:183-189`，U/L + 组播位，注释解释了为什么
不能只判 0x01）、降级点 1-5 的 Unknown 路径、以及 `HeadsetProbe.cs:50-55` 那个 TAB 切分的注释
（记录了它曾经造出一个假 Block 的全过程）。

---

## 6. 标记为 `[未验证]` 的部分

- **§1.2 的具体组合**（网关 6/8 + 头显 0/8）是**按代码推导的**，本轮没有制造这种网络状态。
  可判定它的命令：让头显关机（`adb devices` 为空亦可）同时在有线口上跑
  `powershell -c "ping -n 8 192.168.11.1"` 制造丢包，然后 `VdHelper.exe --selftest --report r.md`
  与 `VdHelper.exe --deep` 各跑一次，对照两处打印的丢包数字。
- **§1.1 的「查询失败」触发条件**需要一台缺 `NetSecurity` / `NetTCPIP` 模块或策略禁用的机器。
  本轮只证明了**静默后 stdout 与 stderr 同时为空**（上面那两条命令），
  没有在真机上把某个检测项打成零行。可判定它的命令：临时把 `PsService` 换成
  `Get-NonexistentCmdlet -ErrorAction SilentlyContinue | Format-Table | Out-String`，跑一次 `--selftest`，
  看 `svc-vd` 是否变成「服务未安装 / 警告」。
- **§1.3 / §1.4 / §1.5 全部需要头显。** 三者的判据都是从代码读出来的，触发条件分别是
  `dumpsys package` 失败、`netsh wlan show interfaces` 报 149-177 信道、`ps -A` 失败。
- **§2.4 的证据矛盾**（`HeadTracking=false` 却显示「= 开」）需要一份真的含该键且值为 false 的
  `UserSettings.json`，本机没有。
- **`docs/checks.md` 是已提交的生成物**，本轮没有重跑 `tools/export-checks.ps1`（那会写文件）。
  报告里引用的 `docs/checks.md` 行号来自当前 HEAD（工作区干净）。
