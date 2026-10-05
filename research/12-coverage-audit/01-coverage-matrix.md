# 01 — 43 个根因 × 现有 25 项检测 覆盖度矩阵

> 生成日期：2026-10-05
> 范围：`research/09-failure-corpus/02-symptom-to-rootcause.md` §2 的 43 个工程根因
> （S1 A1–A7 = 7、S2 B1–B9 = 9、S3 C1–C4 = 4、S4 D1–D5 = 5、S5 E1–E6 = 6、S6 F1–F6 = 6、S7 G1–G6 = 6；7+9+4+5+6+6+6 = **43**）
> 对照对象：本机实测 `--selftest` 输出的 **25 项** PC 侧检测 + 2 项条件触发的 adb 检测。
> 纯只读核查，未改任何代码。

---

## 0. 「已有什么」—— 本机实测的 25 个 check id

`./src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --selftest --out /dev/null`
（退出码 4 = `verdict=Blocked`），逐行抄下的 `[状态] id`：

| # | check id | Summary（实测原文要点） | 本次状态 |
|---|---|---|---|
| 1 | `net-primary` | 1 块网卡可用，主用 Ethernet (192.168.11.2) | Pass |
| 2 | `net-apipa` | 3 块离线网卡持有 APIPA 地址 | Warn |
| 3 | `net-virtual` | 2 块虚拟网卡启用中（Default Switch / WSL） | Warn |
| 4 | `net-profile` | 主网卡网络类别：专用网络 (Private) | Pass |
| 5 | `fw-vd` | 找到入站放行规则（Virtual Desktop Streamer Inbound Allow） | Pass |
| 6 | `fw-defender` | Defender 防火墙：Domain True / Private False / Public False | Warn |
| 7 | `svc-vd` | VirtualDesktop.Service.exe Running Automatic | Pass |
| 8 | `port-vd` | 4 个端口由 Virtual Desktop 持有，没有被别的程序抢占 | Pass |
| 9 | `session-stale` | 1 个通道是残留套接字（最早建立于 57 分钟前） | **Block** |
| 10 | `streamer-proc` | Streamer 进程运行中（1 个） | Pass |
| 11 | `svc-log` | 服务日志有 5 条历史 ERROR，但 Streamer 正在运行 | Warn |
| 12 | `udp-discovery` | UDP 38850 正在监听（发现/配对协议就绪） | Pass |
| 13 | `cfg-streamer` | ShowPairingRequests=false；DontWarnApps 含 NetworkProfile | Warn |
| 14 | `ics` | SharedAccess(ICS)：Running | Warn |
| 15 | `fw-outbound` | 出站策略：三个 profile 全 NotConfigured | Pass |
| 16 | `av` | 已注册杀软：Windows Defender :: 397568 | Pass |
| 17 | `route-metric` | 有线网卡优先级 10，没有虚拟网卡排在它前面 | Pass |
| 18 | `link-type` | PC 走有线（Ethernet） | Pass |
| 19 | `lan-reach` | 头显 192.168.11.14 ping 不通 | **Block** |
| 20 | `link-rate` | Ethernet 协商速率 1 Gbps | Pass |
| 21 | `vpn-proc` | 没有发现 VPN/代理客户端进程 | Pass |
| 22 | `rdp-session` | 只有本机 console 会话（1 条），没有远程桌面在跑 | Pass |
| 23 | `nat-type` | 路由器支持 UPnP，NAT 类型 Open；外网 IP 172.16.80.42（私有段，双层 NAT） | Pass |
| 24 | `fw-pair` | 入站放行有效，没有针对 VD 的出站拦截 | Pass |
| 25 | `accounts-persisted` | 配对信息已落盘（OculusQuest 1 条、Oculus 3 条），设备名 Meta Quest 3 | Pass |

**另有 2 项条件性 adb 检测**（`src/VdHelper/Core/Adb/`，因为本机 `adb devices -l` 设备列表为空，
所以**没有出现在上面 25 项里**；下文矩阵把它们计入覆盖，但每处都标注「条件触发」）：

| id | 位置 | 覆盖的根因 | 无头显时的行为 |
|---|---|---|---|
| `headset` | `HeadsetProbe.cs` | 包名识别 + 7 项运行时权限 | Unknown |
| `headset-deep` | `HeadsetDeepProbe.cs:22` | A6 头显 MAC 随机化 / F1 头显端设置 / A5 头显侧 VPN | Unknown |

> ⚠️ 这是一个容易漏掉的口径问题：**`headset-deep` 是覆盖 A6/F1/A5 的唯一检测，但它是条件性的。**
> 本机 `adb devices -l` 返回空列表（已实测），所以对「没插数据线」的用户，这三条覆盖等于零。
> 矩阵里我把它们算作「完全覆盖」，但在「可自动化程度」列一律标 `需头显`。

---

## 1. 判定口径

**覆盖强度**（针对该根因的判据是否真正命中）：

- **完全** = 现有某项检测的判据直接读出这个根因的事实，命中即定性，不需要用户再做任何事。
- **部分** = 能发现相关现象或读出相关数据，但不足以定性，或只能覆盖根因的一部分形态。
- **无** = 没有任何检测读这个事实。注意 `Symptom.cs` 里把某 check 列进
  `RelevantChecks` **不等于**覆盖——那只是「症状页建议先看哪几项」的顺序，不是判据。

**可自动化程度**（针对「无」与「部分」的条目，回答「这个根因能不能、靠什么查」）：

- `PC 侧可查` —— 只用 Windows 命令 / .NET API / 读文件即可判定，不需要头显在线、不需要问路由器。
- `需头显` —— 必须 adb 读到头显侧事实（版本、网关、RSSI、解码统计、端内设置）。
- `只能问路由器或用户` —— 事实只存在于路由器后台、BIOS 或用户主观描述里。
- `原理上不可自动检测` —— 属于认知/文案问题（例如把码率数字当画质），不是设备状态。

---

## 2. 覆盖度矩阵（43 行，一行一个根因）

| 编号 | 症状类 | 根因一句话 | 语料证据 (R) | 现有检测覆盖 | 覆盖强度 | 为什么 | 可自动化程度 |
|---|---|---|---|---|---|---|---|
| A1 | S1 看不见电脑 | Streamer 配置里的账号名与头显端显示名不匹配 | R04 | `accounts-persisted` | 部分 | 它只读 PC 侧 `StreamerSettings.json` 的 Accounts 分组名与条目数（实测读到 `OculusQuest = 1 条 ;; Oculus = 3 条`、`DeviceName = Meta Quest 3`），**从不与头显端的显示名做比对**。R04 的判据是「两边字符串相等」，本机这半边拿不到 | 需头显 |
| A2 | S1 看不见电脑 | 账户页填了用户名但没点 Save，配置没落盘 | R05 | `accounts-persisted` | 完全 | 判据直击：读文件真实值，`Accounts` 缺失/null/非对象/条目数 0、`DeviceName` 空 → Warn 并点名「填了没点 Save」。这正是 R05 | PC 侧可查 |
| A3 | S1 看不见电脑 | 两端版本不匹配（头显商店版 vs PC 侧 beta） | R06, R07 | 无 | 无 | 25 项里**没有任何一项读版本号**。`streamer-proc` 只判进程在不在；`cfg-streamer` 取了 7 个键（`ShowPairingRequests`/`DontWarnApps`/`LastConnectDate`/`DeviceName`/`CodecName`/`PreferredCodec`/顶层键名），**没有版本**。本机 Streamer=1.34.22.0、Service=1.18.60 可读，但没人读 | 需头显 |
| A4 | S1 看不见电脑 | Streamer 未真正就绪（灰图标）—— 出网 entitlement check 失败 | R15, R16 | `svc-log`, `cfg-streamer` | 部分 | `svc-log` 抓的是服务拉起进程失败（本机实测 `Failed to start Streamer on active session (HRESULT -2147024891)`），`cfg-streamer` 抓 `LastConnectDate` 为空（「从未成功连过」）——两者都是**entitlement 失败的下游症状**，不是它本身。没有任何一项去测出站到 VD 服务器的连通性 | PC 侧可查 |
| A5 | S1 看不见电脑 | 头显侧挂着 VPN（PC 全绿） | R03 | `headset-deep`（条件） | 完全 | `HeadsetDeepProbe.ProbeQuestVpnAsync` 逐个比对 Quest 上 26 个 VPN/代理进程名（ssd/v2ray/sing-box/mihomo/tailscaled…）。R03 的实证正是「Quest 上关掉 VPN 就找到 PC 了」。**但本机 `adb devices -l` 为空，该项不运行** | 需头显 |
| A6 | S1 看不见电脑 | 头显 MAC 随机化破坏绑定 | R17 | `headset-deep`（条件） | 完全 | `ProbeMacAsync` 读 `/sys/class/net/wlan0/address`，失败退 `ip addr show wlan0`，按首字节组播位(0x01)/本地管理位(0x02)判随机 MAC。R17「MAC isn't the one the application expected」逐字对应。**条件触发** | 需头显 |
| A7 | S1 看不见电脑 | 杀软/EDR 禁用或卸载了 VD 服务 | R64 | `svc-vd`, `svc-log`, `av` | 完全 | 三项合力闭合：`svc-vd` 读服务 Status+StartType（本机实测 `Running / Auto`，`StartName=LocalSystem`），`svc-log` 读出启动失败码（`-2147024891 configured identity is incorrect`，正是 R64 同款弹窗的真因），`av` 读 SecurityCenter2 已注册杀软（实测只有 Windows Defender）。R64 的建议项「实际运行状态」逐条落地 | PC 侧可查 |
| B1 | S2 连不上 | Windows 网络配置文件是 Public | R60, R13 | `net-profile` | 完全 | 判据 `Private` 命中即 Pass，`Public` 即 Warn 并给「设置 → 网络和 Internet → 属性」的改法。R60 Streamer 面板原话的中文版 | PC 侧可查 |
| B2 | S2 连不上 | 防火墙只有入站放行、缺出站规则 | R18 | `fw-pair` | 完全 | `FirewallPairChecks.Judge` 显式枚举出站方向：Windows 出站默认放行，所以它精确判「显式 Outbound + Block」才致命，`Program=Any` 与「指向已删 exe」两种失效也分开报。R18「我手工加了一条出站规则就好了」被逐字写进 Detour 文案 | PC 侧可查 |
| B3 | S2 连不上 | 防火墙 profile 级默认入站被整体改成 Block | R17, R31 | 无 | 无 | **`fw-defender` 只判 `Enabled`（开/关），从不读 `DefaultInboundAction`；`fw-outbound` 只读 `DefaultOutboundAction`。** 入站 profile 默认动作这一栏**没有任何一项在看**。R31「Domain 和 Private 两个 profile 的 Inbound Connections 改成 ALLOW 就好了」正是这一栏。本机实测三 profile 的 `DefaultInboundAction` 全是 `NotConfigured` | PC 侧可查 |
| B4 | S2 连不上 | VPN 客户端后台仍在跑（退出 ≠ 未运行） | R19 | `vpn-proc` | 完全 | `.NET Process.GetProcessesByName` + CIM `Win32_Process` **两路独立枚举**，27 个进程名整体锚定（`^(openvpn\|...)\.exe$`，注释里记录了误伤 `AggregatorHost`/`WUDFCompanionHost` 的教训），并区分「已连上（多一张隧道网卡）」与「只是进程在跑」。R19 原话「我以为从托盘退出就够了，但它后台肯定还在跑」被写进 Detour | PC 侧可查 |
| B5 | S2 连不上 | 第三方杀软（任意常驻后台进程） | R21, R43, R60 | `av` | 部分 | `av` 读 `root/SecurityCenter2:AntiVirusProduct`，只覆盖**已注册**的产品。R43 点名的 `NLSSRV32.EXE`（Nalpeiron Licensing Service）是个没注册的常驻进程，这一条**漏**。Corpus 自己的判语就是「FAQ 漏了任意常驻后台进程」 | PC 侧可查 |
| B6 | S2 连不上 | VirtualDesktop.Service 未运行 / 服务身份错 | R64, 本机实测 | `svc-vd`, `svc-log` | 完全 | `svc-vd` 判 `Running`；`svc-log` 把 `HRESULT -2147024891` 原样读出并翻译成「服务登录身份配置错误」，还给出「重装服务无效就查服务账户口令」的下一步。这正是 corpus §3.7 指出的「开发者与官方文案矛盾」的正解 | PC 侧可查 |
| B7 | S2 连不上 | 两端版本不匹配（回归型，升级后出现） | R63, R52 | 无 | 无 | 与 A3 同一条，同样**零覆盖**。R63「VD 更新之后开始出这个问题，重装 Streamer 好了」——回归型版本漂移是语料里最常见的求助框架之一 | 需头显 |
| B8 | S2 连不上 | 远端场景 DMZ 配置错误 / 端口未转发 | R21, R22, R23 | `nat-type` | 部分 | `nat-type` 只读路由器 UPnP 控制面（`GetExternalIPAsync`/`GetSpecificMappingAsync`/`GetAllMappingsAsync`，全程只读，**从不调 `CreatePortMapAsync`**）：实测拿到「外网 IP 172.16.80.42 是私有段 → 双层 NAT」「TCP 38810 已有 UPnP 映射」。它**无法**读出 DMZ 开关状态，也**无法**验证用户转发的 38810-40 是否真的生效——但它把 R22/R23 的矛盾结论写成了确定的说法（不要开 DMZ、要精确转发） | 只能问路由器或用户 |
| B9 | S2 连不上 | 官方远端发现服务不可达 | R08, R09 | 无 | 无 | 25 项里**没有任何一项连 VD 的云端服务器**，所以「servers partially unreachable」这类官方侧故障会被报成用户机器的问题。R09 开发者原话「It's back up now for remote connections」——本机实测 `virtualdesktop.net:443` 可达（`TcpTestSucceeded=True`，解析到 192.64.151.235），说明这条判定在 PC 侧完全可做 | PC 侧可查 |
| C1 | S3 说不在同一网络 | 头显连的是 Guest 网络 | R10, R11 | `lan-reach` | 部分 | `lan-reach` 实测输出「同网段: 是 / ping: 不通」，Detour 文案把 Guest 网络与 AP 隔离并列列出——它**知道该往这边查**，但两者在同一现象下不可区分，工具给不出唯一判定 | 需头显 |
| C2 | S3 说不在同一网络 | AP / 客户端隔离（无线↔有线不通） | R11 | `lan-reach` | 完全 | 主动探测本身就是判据：PC → 头显 IP 发包，收不到就 Block。Corpus §4.2 说这是「社区唯一共识诊断法，社区目前靠手敲 ping」——`lan-reach` 把它自动化了。本机实测该机因此直接 Block | PC 侧可查 |
| C3 | S3 说不在同一网络 | 真不同子网 | R12 | `lan-reach` | 完全 | `SameSubnet()` 拿 PC 主地址与头显 IP 判同段，实测输出「同网段: 是」。R12「Quest 的 IP 前 3 段不同」被直接程序化 | PC 侧可查 |
| C4 | S3 说不在同一网络 | 第二台路由器/AP 模式配错，PC 与头显网关不同 | R02 | 无 | 无 | `lan-reach` 只比**子网**，从不读**头显的网关**。R02 的判据是「PC 与头显网关不同 → 二级路由没配好」。PC 侧只能读到自己的网关（实测 192.168.11.1），另一半必须 adb `getprop dhcp.gateway` | 需头显 |
| D1 | S4 卡在测速 | 笔记本厂商网络加速（Lenovo Vantage network boost） | R40, R41, R42 | 无 | 无 | **零覆盖**，且这是全语料里性价比最高的一条：开发者唯一回复就是它（R36 整帖 ping 9ms、速率 200+、全程有线回程、所有安全软件已配好，仍然卡住），R41/R42 多人同答案。本机实测扫 `Win32_Process` + `Win32_Service` 找 Lenovo/Legion/LZ/L-Config 系列 → 空，可干净地报「未发现」 | PC 侧可查 |
| D2 | S4 卡在测速 | 自动调码率在探测阶段失败 | R43, R30 | `cfg-streamer` | 无 | 严格判**无**：它连 `AutoAdjustBitrate` 的**值**都没取（只在「顶层键」里列了键名）。本机实测 `AutoAdjustBitrate=False` —— 用户 R43 描述的正是这个开关 | PC 侧可查 |
| D3 | S4 卡在测速 | 云 PC 场景码率设得过高（Shadow 98Mbps） | R43 | 无 | 无 | 无任何检测知道「当前目标是不是云 PC」，也不知道当前码率设置值。`cfg-streamer` 取了 `PreferredCodec=11`/`CodecName=AV1 10-bit`，但**不取码率**、不做云场景判词 | PC 侧可查 |
| D4 | S4 卡在测速 | 网卡协商速率只有 100Mbps | R46 | `link-rate` | 完全 | 读 `NetworkInterface.Speed`，阈值 1 Gbps（`OneGbps = 1_000_000_000L`），并特意把 `Speed == -1`（未知）判 Unknown 而非失败（避免未连上的无线网卡造假警报）。实测本机 1 Gbps 通过；Detour 里逐段列了网线/交换机/路由器 LAN 口/网卡属性四段排查，正是 R46 的最终根因 | PC 侧可查 |
| D5 | S4 卡在测速 | 路由器固件随机丢包 | R45 | 无 | 无 | 无任何丢包率探测。本机实测 `ping -n 20 192.168.11.1` → `Lost = 0 (0% loss)`，0ms 平均、4ms 最大 —— 判据可干净落地（但要注意耗时：本机实测 **19.43 秒**，而一轮完整体检实测 4.84 秒；`HealthEngine` 只有 8 路并发上限、无全局超时，必须走异步/后台观测） | PC 侧可查 |
| E1 | S5 有画面但黑 | 没有已启用且有信号的物理显示器（拔过线） | R51 | 无 | 无 | 25 项里**没有任何一项枚举显示器**。本机实测 `WmiMonitorID` 读到 2 块（`BOE0CE4` + `AOCB470`，均 `Active=True`），而 `[Screen]::AllScreens` 只返回 1 个 `DISPLAY1` —— 两边不一致，R51「拔了显示器再插回去就黑屏」正需要这个对照 | PC 侧可查 |
| E2 | S5 有画面但黑 | 远程桌面会话独占显示器 | R50 | `rdp-session` | 完全 | `qwinsta` 解析出全部会话，排除 `console`/`services` 与 `listen`/`idle`/`init`/`down` 保留槽位（注释记录了官方示例里那行 `rdp-tcp … 2 listen` 的坑），命中即 Block 并给出 `logoff <id>`。R50 原话与「第二次连接就黑屏」的经典真因被写进 Detour | PC 侧可查 |
| E3 | S5 有画面但黑 | GPU 硬件编码器不可用 / 被禁用 | R49 | 无 | 无 | 无任何检测读编码器状态。本机实测 `nvidia-smi --query-gpu=...,encoder.stats.sessionCount --format=csv` 正常返回（`sessionCount = 0`），`Win32_VideoController` 也可列 4 个适配器 —— 数据可得，只是没人读。R49 Steam Deck「Valve 禁用了 VD 需要的多屏硬件编码器」 | PC 侧可查 |
| E4 | S5 有画面但黑 | GPU 性能不足（渲染能力，非编码问题） | R47 | 无 | 无 | 无检测读 GPU 型号或对照 VR Ready 门槛。本机实测 `Win32_VideoController` 报出 `NVIDIA GeForce RTX 5070 Ti Laptop GPU` / `Intel(R) Graphics` —— 判据可做，但「换硬件 vs 改设置」这个区分需要一份对照表 | PC 侧可查 |
| E5 | S5 有画面但黑 | 多显卡选错（游戏跑在核显上） | R48 | 无 | 无 | **零覆盖**，而本机就是这个根因的现场：实测 `Win32_VideoController` 同时列出 `Intel(R) Graphics` 与 `NVIDIA GeForce RTX 5070 Ti`，且 `HKCU\…\DirectX\UserGpuPreferences` 里 `SteamVR\bin\win64\vrdashboard.exe : GpuPreference=0`（**0 = 省电 = 核显**），`VRChat.exe = 2`、`llama-server.exe = 2`。R48「disable the other one it worked 100%」就是这个 | PC 侧可查 |
| E6 | S5 有画面但黑 | 驱动版本与 VD 冲突（最新驱动反而是风险项） | R54, R55 | 无 | 无 | 无任何检测比对 GPU 驱动版本与 VD 推荐矩阵。本机实测 `Win32_VideoController.DriverVersion = 32.0.15.9649`、注册表 `DisplayVersion = 596.49`、Streamer `FileVersion = 1.34.22.0` —— R54 开发者说的「591 及以后的 Nvidia 驱动有 bug，1.34.14 修好了」完全可以自动比对 | PC 侧可查 |
| F1 | S6 连上就掉 | 头显端手部/身体追踪设置 | R57 | `headset-deep`（条件） | 完全 | `ProbeHeadsetSettingsAsync` 读头显 `$HOME/.config/Virtual Desktop/{UserSettings,SharedUserSettings}.json`，命中手部追踪键即 Warn 并写明「PC 侧全绿时它就是每 60 秒卡一下的那种根因」。R57 原话「PC 侧 Game/Encode/Network/Decode 全部稳定」。**但读文件需 patched APK 可调试或已 root，否则降级 Unknown** | 需头显 |
| F2 | S6 连上就掉 | 头显解码器卡死（Decoding > 400ms） | R58 | 无 | 无 | 无任何检测读串流统计。R58 的判据是**头显端**的 Decoding 段耗时（Networking 0ms 但 Decoding >400ms，换新头显仍复现），必须在串流进行中由头显回传 —— 25 项里没有任何一项接触这条链路 | 需头显 |
| F3 | S6 连上就掉 | Windows 周期性 WiFi 扫描打断 | R62 | 无 | 无 | 无任何检测看 WLAN 扫描行为。**本机实测就是它的前置条件**：`Get-Service WlanSvc` → `Running / Automatic`，而主链路是有线 Ethernet —— Wi-Fi 开着周期性扫描会打断串流，R62 原话如此 | PC 侧可查 |
| F4 | S6 连上就掉 | ICS（网络共享）参与转发 | R65 | `ics` | 完全 | `Get-Service SharedAccess` 判 Running。本机实测 `SharedAccess: Running / Manual`，且 `Hns` 也在 Running —— 判据命中，且该项刻意**只报告不修改**（停 ICS 会断掉别人的热点），符合安全要求 | PC 侧可查 |
| F5 | S6 连上就掉 | 信号覆盖不足（墙体/距离） | R56 | 无 | 无 | 无检测读信号强度。**注意：RSSI 在头显的无线网卡上，PC 侧读不到自己没连的链路质量**，也不该把 PC 的 Wi-Fi 强度当成头显的强度 | 需头显 |
| F6 | S6 连上就掉 | 网卡节能（省电设置） | R65 | 无 | 无 | 无检测读网卡节能开关。本机实测 `Get-NetAdapterAdvancedProperty -Name Ethernet` 读到 5 个相关键且**全为「关闭」**：`节能乙太网路(*EEE)=关闭`、`Advanced EEE=关闭`、`环保节能(EnableGreenEthernet)=关闭`、`Power Saving Mode=关闭`、`中断调整(*InterruptModeration)=Enabled`。数据完全可得，只是没人读 | PC 侧可查 |
| G1 | S7 画质差/不跟手 | PC 与头显挂在不同网关（跨路由） | R44 | 无 | 无 | 与 C4 同一条：需要头显的网关值。`net-primary`/`route-metric` 只知道 PC 自己的网关（实测 192.168.11.1）。R44「PC 接第一台路由、Quest 接第二台，第二台把信号劣化了」——社区给出的答案互相矛盾，只有工具读出两边网关才能判 | 需头显 |
| G2 | S7 画质差/不跟手 | 5GHz 信道拥塞 / 信道宽不是 80MHz | R72, R71 | 无 | 无 | 无信道扫描。实测 `netsh wlan show networks mode=bssid` 在本机**直接失败**（退出码 1，输出「The wireless local area network interface is powered down and doesn't support the requested operation.」）—— 本机走有线，命令不可用。所以它虽属 PC 侧命令，但**有前置条件**：需要一块处于 Up 的无线网卡 | PC 侧可查 |
| G3 | S7 画质差/不跟手 | 内存 RGB 控制软件等常驻工具占用 | R59 | 无 | 无 | **零覆盖，而本机现场命中**：`Win32_Process` 扫出 4 个常驻进程 `ArmouryCrate.exe`(18576)、`ArmouryCrate.Service.exe`(5772)、`ArmouryCrateControlInterface.exe`(5740)、`ArmouryCrate.UserSessionHelper.exe`(15100)。R59 那位用户为它耗了 18 个月 | PC 侧可查 |
| G4 | S7 画质差/不跟手 | 过热降频 | R59 | 无 | 无 | 无温度/降频检测。本机实测：`nvidia-smi --query-gpu=temperature.gpu,clocks_event_reasons.active,power.draw` → `54, 0x0000000000000000, 35.71 W`（54°C、无降频）；而 `MSAcpi_ThermalZoneTemperature` **返回空** —— 说明 Windows WMI 这条路在笔记本上常常拿不到，必须以 `nvidia-smi` 为主、ACPI 为辅 | PC 侧可查 |
| G5 | S7 画质差/不跟手 | BIOS Resizable BAR | R60' | 无 | 无 | 无覆盖，且**不建议加检测项**——语料自己在 ⚠️ 里写明「社区流传、无证据、高风险」，首条高赞回复直接反驳「significant chance that your change is a placebo fix」。列进检测列表会把盲试合法化 | 只能问路由器或用户 |
| G6 | S7 画质差/不跟手 | 用户拿码率数字当画质 | R30 | 无 | 无 | 无覆盖，且它**不是设备状态问题**——R30「youtube 上看到人家 100+，我只有 60-70」需要的是把 Mbps 翻译成「够用」的判词与纠偏文案，属于报告呈现层而非检测层 | 原理上不可自动检测 |

---

## 3. 统计

| 覆盖强度 | 条数 | 编号 |
|---|---|---|
| **完全** | **14** | A2、A5※、A6※、A7、B1、B2、B4、B6、C2、C3、D4、E2、F1※、F4 |
| **部分** | **5** | A1、A4、B5、B8、C1 |
| **无** | **24** | A3、B3、B7、B9、C4、D1、D2、D3、D5、E1、E3、E4、E5、E6、F2、F3、F5、F6、G1、G2、G3、G4、G5、G6 |
| 合计 | **43** | 14 + 5 + 24 = 43 ✅ |

※ A5、A6、F1 靠 `headset-deep` 达成完全覆盖，但**该检测条件触发**（需 adb + 头显在线）。
本机 `adb devices -l` 实测为空列表 → 对「没插数据线」的用户，这 3 条的**实际覆盖为 0**。
按最坏口径（用户没插头显线）重算：**完全 11 / 部分 5 / 无 27**。

### 3.1 24 条「无」按可自动化程度分四类

| 类别 | 条数 | 编号 | 理由 |
|---|---|---|---|
| **PC 侧可查** | 16 | B3、B9、D1、D2、D3、D5、E1、E3、E4、E5、E6、F3、F6、G2、G3、G4 | 全部只用 Windows cmdlet、WMI/CIM、注册表、`nvidia-smi`、`netsh` 或读 Streamer 配置文件即可判定。本机 16 类命令**已逐条实跑**，其中 15 条返回干净可解析的输出；G2（`netsh wlan show networks`）在本机因无线网卡 powered down 返回退出码 1，需前置条件 |
| **需头显在线（adb）** | 6 | A3、B7、C4、F2、F5、G1 | 事实全在头显上：头显 app 版本（A3/B7）、头显网关（C4/G1）、串流统计里的 Decoding 段（F2）、头显 Wi-Fi RSSI（F5）。PC 侧无从取得——这不是实现偷懒，是物理上拿不到 |
| **只能问路由器或用户** | 1 | G5 | Resizable BAR 是 BIOS 开关，正常路径要重启进 BIOS 才看得到；社区证据本身也被判为安慰剂效应 |
| **原理上不可自动检测** | 1 | G6 | 「把码率数字当画质」是用户的认知映射问题，设备上没有可读的事实。只能靠报告文案纠偏（corpus §3.10 已给出 R72 的四段判词） |

> B8（DMZ / 端口转发）虽然已有 `nat-type` 部分覆盖，但它归在「部分」里，其残余缺口（DMZ 开关状态、转发是否真生效）
> 属「只能问路由器」——路由器后台没有可被 PC 侧 API 读取的 DMZ 状态接口。
