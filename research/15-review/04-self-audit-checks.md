# 04 — 自查：`src/VdHelper/Core/Health/` 自 v0.6.0 起的改动

判据只有一条：**用户能读到的字，有没有超出同一文件里那段代码建立的东西。**
不看风格、不看命名、不看测试缺失。commit message 描述的是意图，不是行为 —— 下面有几处正是 commit 与代码不一致的地方。

读的是当前工作树（`7e160cd`），不是 diff 里的旧版本。

---

## 高：措辞断言了代码没有建立的东西

### H1 · `GpuRuntimeChecks.cs:104-105` — 温度分支在「一个降频原因都没有」时会写出「原因：。」

```csharp
: $"温度 {f.TemperatureC}°C 已越过 85°C，驱动报的降频原因："
  + string.Join("、", DescribeReasons(f.ThrottleReasons.Value)) + "。")
```

这个分支的条件只是 `f.TemperatureC >= 85`，与 `ThrottleReasons` 的**取值无关**。`DescribeReasons`（`:197-208`）是九个 `if ((r & Bit) != 0)`，所以 `r == 0` 时它 yield 零项，`string.Join` 得到空串——

> 温度 91°C 已越过 85°C，驱动报的降频原因：。功耗 88.3 W。温度高本身就会掉频……

代码建立的是「驱动报了 `0x0`」，写出去的是「驱动报了降频原因：」加一个空清单。这正是本次改动想消灭的那一类句子（把没测到的写成测到了），只是方向反了过来：从「编一个原因」变成「宣称有原因栏位却什么都没有」。

同一函数还有第二个、更隐蔽的：**这个分支只遮罩不分组**。`ThrottleReasons == 0x1`（GpuIdle，空闲——笔记本上极常见）会写出

> 驱动报的降频原因：GPU 空闲。

而同一个文件 `:175-177` 的注释明确把 GpuIdle 归为「normal states, not throttling」。用户读到「降频原因：GPU 空闲」只会更困惑。`DescribeReasons` 在 `:131` 被用于 `real != 0` 分支时同样不过滤（那里 `why` 来自完整的 `reasons`，不是 `real`），所以「降频原因：GPU 空闲」不止出现在温度分支。

**修法**：温度分支照抄 `real = reasons & RealThrottle` 的分法，`real != 0` 才列原因，否则照 `:112-122` 那句「驱动报的降频原因位域是 0x0，没有降频」如实写；并且让 `DescribeReasons` 收 `real` 而不是 `reasons`。顺手在 `real == 0` 时不必凑句子。

### H2 · `GpuRuntimeChecks.cs:180-182` — 注释里的两个数字，仓库里查不到出处

```csharp
// itself: the machine reached 89% load and 100.8 W on its own, and the published v0.6.0 binary
// reported 降频原因位域 0x4 — SwPowerCap, 1UL << 2 — and named it. So the bit table and the branch
// are confirmed against a real driver report, not only against the documentation.
```

全仓 `grep 100\.8` 只有这一处命中。`research/14-real-run/01-what-this-machine-found.md:88` 记的是 `2760/3090 MHz = 89%，56°C`，**89 在那份记录里是频率比，不是「负载」**；`docs/checks.md:44` 与 `docs/index.html:172` 记的那一次 Warn 是 `71%（2205/3090 MHz）（占用 53%，温度 69°C，当前功耗 90.1 W）`——**90.1 W，不是 100.8**。README:38 记的另一次是 `74%`、`0x0`、`72 W`。

所以这句话同时有两处超出：**「89% load」把频率比写成了占用**（research:88 的 89% 明确是 `MHz = 89%`），**「100.8 W」在仓库任何一次记录里都不存在**。这不是措辞洁癖——这段注释的用途是宣告「本分支已被真实驱动报告验证，不再是 `[未验证]`」，而它引用的证据对不上。

`0x4 = SwPowerCap` 这一半我核过，是对的（`SwPowerCap = 1UL << 2`，`:185`）。

**修法**：改成引用能查到的记录——`docs/checks.md:44` 那一次（71%、53%、69°C、90.1 W、SW Power Cap），并把「89% load」改成「频率比 89%」。如果 100.8 W 确实来自某次没写进仓库的运行，那它在注释里就是无源数字，删掉或补出处。

### H3 · `WifiQualityCheck.cs:141-143` 与 `:150-152` — 同一次运行会同时说「数值自相矛盾」和「这就是 2.4 GHz」

`channelNum` 落在 1–14 **且** `rx > 100` 时，`:141` 加一条 warning：

> 连的是 2.4 GHz 频段但协商速率却高于 100 Mbps，数值自相矛盾，请以实际频段为准。

新加的 `:150` 在**同一个 `channelNum` 落点**上又加一条：

> 连的是 2.4 GHz（信道 N）。这个频段在住宅环境里通常最拥挤……

两条会同时进 `string.Join("；", warnings)`（`:167`）。也就是说：netsh 报了「2.4 GHz 信道 + 300 Mbps 协商速率」——一个自相矛盾、字段本身不可信的输出——用户会同时被告知「数据自相矛盾，以实际频段为准」和「你确实连的是拥挤的 2.4 GHz」。后一条建立的是前者刚刚否认的那个结论。

这不是「多写了一句」，是两句互相否定的话并列在同一个 detail 里。

**修法**：`:150` 那条加 `!(rx 解析成功 && r > 100)` 的前置条件，或者把它挪到 `:141` 的 `else` 分支上——矛盾的读数只应该出矛盾那一条。

### H4 · `StreamerChecks.cs:307-315` — `IsLan` 对 IPv6 对端必然返回 false，于是被记成「到公网」

```csharp
bool IsLan(PortView p) =>
    System.Net.IPAddress.TryParse(p.Peer.Split(':')[0], out var ip)
    && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
    && localNets.Any(l => NetworkInventory.IsLanPeer(ip, l));
```

两个问题叠在一起：

1. `p.Peer` 来自 `NetworkInventory.cs:150` 的 `"$($_.RemoteAddress):$($_.RemotePort)"`。IPv6 对端形如 `fe80::1:38810`，`Split(':')[0]` 切出 `"fe80"` —— `TryParse` 失败（哪怕成功，`AddressFamily == InterNetwork` 也为 false）。所以**每一个 IPv6 的 Established 套接字都进 `cloud` 桶**。
2. 于是 `:316-317` 把它写成证据 `["到公网的已建立连接（非串流）"]`。`fe80::` 是链路本地地址——就在这根线上。它被断言为「到公网」。

用户在这个字段上读到的意思是「我有一条出公网的连接」。代码建立的是「这条 Established 套接字的对端不是同 /24 的 IPv4」。IPv6 对端、跨 /24 的同局域网对端、以及真正的公网对端，在这句话里是同一件事。

`IsLanPeer` 本身（`NetworkInventory.cs:124-131`）已经是 IPv4-only 且 `/24`，这个限制在它的注释里说得很清楚（`:117` 「Same /24 — enough to tell a LAN peer from a cloud relay, and no more than claimed」）。问题出在 `StreamerChecks` 用一个**更宽的失败模式**（解析失败）把它变成了一个**更窄的断言**（公网）。

**修法**：拆成三桶——`IsLan`（同 /24 IPv4）、`IsNotLanButParsed`（解析成功但不同网段，包括 IPv6 与 loopback）、`Unparsed`。证据键按桶写，第三桶写「对端无法解析，未知」，不要并进「到公网」。

### H5 · `NatChecks.cs:117` + `:126-129` — 双层 NAT 的否定句与 `RemoteGuidance` 的肯定句同时出现

```csharp
var doubleNat = ext is not null && IsPrivate(ext);
```

`doubleNat` **不受 `deviceIsOurSubnet` 约束**。它只用在两个地方：

- `:126-129` 的 summary——这一处**处理得对**，选了不在本网段的设备时会写「**但选中的 UPnP 设备不在本网段，这条双层 NAT 的结论不成立**」。
- `:134` 的 `RemoteGuidance(doubleNat)`——这一处**没有处理**。

于是选到网段外设备、且 `ext` 落私有段时，同一个 `CheckResult` 里：

> **Summary**：…；外网 IP 是 192.168.1.1（私有段）——**但选中的 UPnP 设备不在本网段，这条双层 NAT 的结论不成立**
> **Guidance**：…本机外网 IP 落在私有段，说明路由器后面还有一层 NAT（多半是运营商光猫）：只在路由器上转发端口是不够的，上级设备也要参与，或者改用 Streamer 的云中继。

Summary 刚说结论不成立，Guidance 下一句就让它成立，还给了操作建议。这条否定句（`:126-129`）是本次改动新加的、也是正确的那一半；另一半漏了。

**修法**：`RemoteGuidance(doubleNat && deviceIsYourSubnet)`。`doubleNat` 保留原样给 summary 用，或直接改成 `ext is not null && IsPrivate(ext) && deviceIsOurSubnet` 并在 summary 里改用原始条件——两种都行，要的是这两个出口共用同一个被约束过的值。

### H6 · `LossProbe.cs:126` — 一个 target 死了，另一个有 30% 丢包时，30% 那条被整条丢掉

`:126` 的 `if (silent.Count > 0)` **先于** `:135` 的 `if (worstPartial >= 5)` 返回。所以「网关 30% 丢包 + 头显不应答」这一组里，summary 只写：

> 头显 192.168.11.23 完全不应答（0 收到）

30% 那条**只存在于 evidence**（`:105-107` 的 `ev[label + " " + host]`），不进 summary、不进 detail、不进 guidance。guidance（`:131-134`）写「本机链路本身没问题：默认网关 192.168.1.1 通畅。」——**而这正是那句 30% 的来源**。

CLI 侧 `Verdict()` 在同一组数据上两个分支都执行（`:238-247` partial，`:248-259` dead），会同时打出丢包行和不应答行。所以注释 `:114-116` 说的「in the same order as Verdict() below — the CLI and the screen were answering differently about the same measurement」只修好了一半：**顺序对齐了，但 screen 在 silent 存在时会吞掉 partial，而 CLI 不会。**

这不是措辞问题，是丢测量：用户拿到的 summary 说他只有「一个目标不应答」，而实际测到了另一个目标的 30% 丢包。

**修法**：silent 分支的 summary 与 guidance 都把 `partial` 一并列出（`partial.Count > 0` 时加上「另外 {labels} 测到 {worstPartial:F0}% 丢包」），或者把 `silent.Count > 0` 与 `worstPartial >= 5` 合成一个返回四种组合的分支。

---

## 中：证据与它旁边那句话说的不是一件事

### M1 · `LossProbe.cs:148` — `InPassSamples` 是常量，正文里却写死了 `8`

```csharp
$"没有测到丢包（快速采样 {InPassSamples} 次）",
"注意这是单次快照，而且只有 8 次采样；随机丢包很容易刚好没赶上。"
```

`:147` 用 `{InPassSamples}`，`:148` 写死 `8`。`InPassSamples` 是 `public const int`（`:29`），改一次就有一处跟着说谎。这是本次 diff 亲手做的替换（`:135` 的 `worst` → `worstPartial`）附近的同一段文字，diff 里保留了它。

**修法**：`:148` 也用 `{InPassSamples}`。

### M2 · `MachineStateChecks.cs:129-133` — 「两者都读进证据了」中的一项没有对应字段

```csharp
"「它会不会在空闲时睡着」要看 DeviceSleepOnDisconnect 与电源管理里的"
+ "「允许计算机关闭此设备以节约电源」——两者都读进证据了，但没参与判定。"
```

脚本（`:44-47`）取的是 `WakeOnMagicPacket` / `WakeOnPattern` / `DeviceSleepOnDisconnect`。**「允许计算机关闭此设备以节约电源」不在 `Get-NetAdapterPowerManagement` 的输出里**，脚本从未查过它，它也不在 `ev["电源管理属性"]`（`:112`）中。

这句话作为「本项答什么、不答什么」的澄清写得很好——但它把两项都宣称成已采集，其中一项没有。读者会去找这两个字段，发现只有一个。

**修法**：删掉「两者都读进证据了」，或改成「只有 DeviceSleepOnDisconnect 在证据里；设备管理器的那个勾选项本项读不到」。

### M3 · `WifiQualityCheck.cs:148-150` — Pass 分支在 2.4 GHz 上再也进不去了，注释说的还是旧因果

`warnings.Count == 0` 才 Pass（`:161`），而 `:150` 让 1–14 无条件进 `warnings`。所以 2.4 GHz 现在**只会** Warn。这是对的（题目问的就是这个），但 `:148-149` 的注释给的理由是旧的：

> the old pass path said 无线链路正常 on a 2.4 GHz link at 90% signal.

结论正确、理由正确，**代码也对**。这一条不算缺陷——记在这里是因为它是我按题目逐条核对「Pass 分支是否在该出现时出现」的结果：2.4 GHz 不再 Pass、149–177 仍是 note 且能 Pass，符合预期。唯一可挑的是 `:147` 的 149–177 note 在 `:161` 的 Pass 里会以 `"。" + detail` 拼在「这几项没有明显的丢包来源。」之后，读起来是「没有明显的丢包来源。信道 149 在 5 GHz 高信道段，通常是较空闲的选择（这不是问题）。**但这一项没有测过丢包**」——语义通顺，不报。

### M4 · `HealthChecks.cs:19-25` — `-ErrorAction Stop` 的注释承诺了一件 `PowerShellCheck` 只做了一半的事

```csharp
// A query that genuinely finds nothing still returns zero rows and still reaches the judge, which
// is the point: "there is no rule" and "I could not ask" have to stay different answers.
```

这一半成立：`PowerShellCheck.cs:33-37` 在 `_exit != "0"` 时返回 Unknown，而 `Get-NetFirewallRule -ErrorAction Stop` 失败确实是非零退出码。

但「读不到」还有第二条路，`PowerShellCheck.cs:39-49` 也处理了（stderr 非空且 stdout 空 → Unknown）——**注释只提了退出码这一条**，而对 `fw-vd` 而言 stderr 那条才是常见的失败形态（cmdlet 存在但拒绝访问时 PowerShell 常常仍退 0）。两条都在代码里，注释只说了一条。这属于注释不完整，不是代码缺陷，**低优先级**，记此备查。

### M5 · `FirewallPairChecks.cs:34-45` — 注释说「exit 0 + 空 stdout」是旧行为，但旧脚本的 `exit 0` 会**吃掉**退出码

```csharp
// This script had both, so a Get-NetFirewallRule that failed outright looked identical to
// "there are no rules" — exit 0, empty stdout — and fw-pair answered Block with a repair
// attached on a machine it had failed to read.
```

旧脚本（`git show v0.6.0`）末尾确实是 `}; exit 0`，`$ErrorActionPreference='SilentlyContinue'` 会把 `Get-NetFirewallRule` 的失败吞成非终止错误，然后 `exit 0` 强制退出码 0。所以「exit 0, empty stdout」的描述**准确**，`fw-pair` 判 Block 也准确（`:117-119` 的 `if (!r.Ok)` 在旧代码里同样存在，只是 `Ok` 恒为真）。

新脚本去掉了 `exit 0`，加了 `-ErrorAction Stop`，失败时退出码非零，`:117` 的 `!r.Ok` 就能拦住。**这一处改对了，注释也对**。清白。

---

## 已核过并清白

| 文件 | 核了什么 | 结论 |
|---|---|---|
| `ReachabilityCheck.cs:109-127` | 三次采样循环；`attempts` 能否在 `replies > 0` 时为空 | **不能**。`for` 至少执行一次，`attempts.Add`（`:117`）无条件先于 `break`（`:118`），故 `attempts.Count >= 1` 恒成立。`ping = attempts.FirstOrDefault(a => a.Success)`（`:120`）在 `replies == 0` 时返回 `default`（`Ms = 0`），但那两个消费点（`:126` 的 `ev["ping"]`、`:141` 的 summary）都在 `replies > 0` 之下，`:129` 的 `attempts.Count` 也只在 `replies == 0` 时用于「全部未应答」——三处都安全。 |
| `ReachabilityCheck.cs:122-128` | `probeNote` 是否为 evidence 与 summary 的**同一**来源 | **是**。`probeNote`（`:123-125`）算一次，`:126` 用 `TrimStart('，')`、`:141` 用原串，两边同一个变量，不可能不一致。`:130` 的 `ev["采样"]` 与 `ev["ping"]` 也同源。 |
| `ReachabilityCheck.cs:85-89` | Reachable/Stale/其他 三态措辞 | `:85` 取 `neighbor[0]` 的原始状态名，`:164`/`:171-176` 按 `Reachable` / `Stale` / 其它三路分发，措辞与分支对得上。`:89` 的 `（状态陈旧：…）` 只在 `!reachable` 时出现，此时**任何**非 Reachable 状态都会吃到「陈旧」这个词——`Probe` 也会。这是一个措辞偏粗的点：`:79-84` 的注释明确说「wording that said 已过期 for everything non-Reachable would have named a state the tool never saw」，而 `:89` 正是那样写的。**见下条 L1**，算低。 |
| `GpuRuntimeChecks.cs:194-195` | `RealThrottle` 是否正确排除了 GpuIdle / ApplicationsClocks / DisplayClockSetting | **正确**。`RealThrottle = SwPowerCap \| HwSlowdown \| SwThermalSlowdown \| HwThermalSlowdown \| HwPowerBrake`（`:195`），逐位核对：`GpuIdle`(bit0)、`ApplicationsClocks`(bit1)、`DisplayClockSetting`(bit8) 均**不在**掩码内。`SyncBoost`(bit4) 也不在——题目只问了那三个，SyncBoost 被排除是作者的判断，NVIDIA 文档未把它列为降频，**不算错**。 |
| `NatChecks.cs:68-76` | 是否真的选到了同网段设备 | **是**。`:70-74` 三级回退：同 /24 IPv4 → 任意 IPv4 → `devices[0]`；`:75-76` 的 `deviceIsOurSubnet` 与第一级的条件**逐字相同**，两者不会分歧。`IsLanPeer` 是 `/24`（`NetworkInventory.cs:124-131`），与 `PrimaryCandidates()`（`:105-106`，已滤掉 down/loopback/无 IPv4）组合合理。**除 H5 的 doubleNat 出口外，这段是对的。** |
| `LossProbe.cs:120-124` vs `:229-260` | 四个形状下 screen 与 `Verdict()` 是否一致 | 逐个走了一遍：**both clean**（screen→Pass `:146`；CLI→`:231-233`「每个目标都通」）一致；**both dead**（screen→`:126`；CLI→`:234-237`）一致；**one partial**（screen→`:135`；CLI→`:238-247`）一致；**one silent + one clean**（screen→`:126`；CLI→`:248-259`）一致。**但 silent + partial 混合**不一致 —— 见 H6。 |
| `StreamerChecks.cs:78-83` + `:203-212` | svc-log 身份匹配器 | `LooksLikeIdentityFailure` 匹配 5 个串（`0x80070005` / `-2147024891` / `Access is denied` / `UnauthorizedAccessException` / `identity is incorrect`），命中时 detail 写「**日志原文指向服务身份绑定问题**」并列出三种文本；未命中时写「本项只按级别判定、不解释内容……也可能是网络、配置或用户取消」。判定对象是 `recent[^1].Groups["msg"].Value`（`:203`），即**最近一条** ERROR 的原文，而 summary 里的条数 `recent.Count` 是全部条数——两者不同源，但 summary 明写「最近一次 {last}」（`:200`），读者能对上。`Truncate(…, 160)`（`:165`）只裁 evidence，不裁判定输入（`:203` 取的是未截断的 `msg`）——这是对的。**无缺陷。** |
| `WifiQualityCheck.cs:161-170` | Pass 与 Warn 的 detail 拼接 | `detail`（`:159`）在两处都加了 `。` 分隔，Warn 分支（`:168`）用 `detail.Length > 0` 守卫，notes 为空时不产生悬空的 `。`。**拼接正确。** |
| `WindowsStateChecks.cs:236-268` | link-type | Pass 分支（`:254-258`）现在明说只覆盖官方要求的前半句、后半句没测（`:255-257`）；无链路分支（`:263`）不再复制无线措辞。`:259` 的三路分发（有线优先→无线→无链路）与 `wired`/`wireless` 的构造（`:238-239`）一致。**无缺陷**——这是本次 diff 里改得最干净的一处。 |

---

## 低

### L1 · `ReachabilityCheck.cs:89` — `（状态陈旧：这条记录已经过期…）` 覆盖了所有非 Reachable 状态

`:89` 的三元只判 `reachable`，所以 Windows 报 `Probe` 时 evidence 写「状态陈旧：这条记录已经过期，不能当作它还在」。`Probe` 的含义是「正在解析、还没成功」，把它叫「已过期」不准确。`:79-84` 的注释（本次新增）恰恰是在讲这件事，`:85`/`:164`/`:171-176` 的新措辞也正确处理了——只有 `:89` 这一行没跟上。改法：`reachable ? "（状态可达）" : $"（状态 {stateName}）"`。

### L2 · `HealthChecks.cs:22-25` — 注释只列了「读不到」的两条路中的一条

见 M4。代码两条都有，注释只说退出码。

---

## 未验证 / 未覆盖

- **`ReachabilityCheck.cs:157-179` 的 `notNetwork` 段与 `:161-165` 的三分支**，我只核了它与 `:77-89` 的 `reachable`/`stateName` 是否自洽（三路分发正确、`:171` 的 `Stale` 判断用的是 `:85` 取到的原始状态名）。这两段的**外部引用**（`NetworkManager.cs:184-186`、`:212-214`，`ComputerDiscoveryClient.cs:98-107`）指向 VD 反编译源码，**不在本次范围内，我没有核**。它们的正确性由前两轮审计负责。
- **`GpuRuntimeChecks.cs:213-231` 的 CSV 解析**：字段顺序依赖 `--query-gpu=` 的参数顺序与 `parts[]` 下标对应。看起来对（8 个字段、8 个下标），但**没有在真机上跑过**，标 `[未验证]`。
- **`StreamerChecks.cs:262-283` 的 `WhoOwnsUdpPortAsync`** 与 `:285-320` 的 Block/Warn 分支本次未改，只核了 `IsLan`/`cloud` 那一段（`:307-317`）以及它们与 `:323`/`:341` 的先后关系（`:323` 的 `UDP 38850` Pass 先于 `:341` 的 `live.Count > 0` 分支——即「串流中」那一支只在 38850 **没有**监听时才会出现，与 `:342` 的措辞「Streamer 已释放发现端口」一致）。
- **UI 层如何渲染 `ev`**：`HealthReport` 把 Warn 汇总成什么、evidence 面板是否折叠，本次未查。H6 的影响面（30% 丢包只在 evidence 里）按 UI 会展示 evidence 估计减轻，但**未验证**。

---

## 结论

- **H1–H6 是真缺陷**，都能在不删功能的前提下改掉，改动量都很小（多数是一行条件或一处常量替换）。H1、H6 是本次改动**新引入或未修完**的；H2、H3、H4、H5 是改动带进来的措辞。
- 题目点名的六处里，**四处清白**：`RealThrottle` 掩码正确、`NatChecks` 的设备选择正确（除 H5 的另一出口）、`attempts` 不可能为空且 evidence 与 summary 同源、`probeNote` 单点计算。
- **一处未按预期**：`StreamerChecks` 的 LAN/cloud 拆分在 IPv6 对端上必然失败（H4），且失败被写成了「到公网」。
- 没有发现「编造一个测量值」这一类最严重的问题——`power.draw` / `clocks_event_reasons.active` 是真的读了，Pass 分支的 `0x{reasons:X}` 证据是真的。

**建议的下一步，按性价比**：H1（一行条件）→ H6（一处拼接）→ H5（一个布尔）→ H4（三桶拆分）→ H3（一处 else）→ H2（改引用）→ M1/M2/L1（各一行）。
