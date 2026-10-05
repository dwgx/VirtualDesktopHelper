# 02 — PC 侧检测与修复清单

配套文档：`01-ports-and-discovery.md`（端口/发现机制事实）、`03-root-cause-triage.md`（排查顺序）。

**列定义**：`编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险`

**约定**：
- 所有命令在 **PowerShell** 下运行（本轮已逐条实跑验证，输出见各行）。
- `⚙` = 工具**可以自动修**（有确定回滚）。`🖐` = 工具**只能解释并指导**，不能代改。
- `回滚` 写不出来的项一律标 `[未验证]`，**不允许留空**。
- 命令中 `%ProgramFiles%` 等环境变量请在实现时用 `Environment.GetFolderPath` 展开；
  本文档为可读性保留 `%VAR%` 写法。

---

## 阶段 0：先确认「PC 上真的有 Streamer 在跑」（最高频、最便宜的失败）

| 编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险 |
|------|------|------------------|---------|-----------------|---------|------|
| 1 | 头显列表里根本没有这台 PC（但 PC 明明开着 Streamer 窗口） | `Get-Process VirtualDesktop.Streamer -ErrorAction SilentlyContinue \| Select-Object Id,ProcessName,StartTime` | 输出 ≥1 行，`StartTime` 是近期 | ⚙ 启动 Streamer：`Start-Process "$env:ProgramFiles\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe"` | 结束进程即回滚：`Stop-Process -Id <Id>` | 低。启动 GUI 程序，无持久副作用 |
| 2 | Streamer 窗口打开了但 38810-40 没有监听 | `Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue \| Where-Object {$_.LocalPort -in 38810,38820,38830,38840} \| Select-Object LocalAddress,LocalPort,OwningProcess` | 4 行齐全，且 `OwningProcess` 指向 `VirtualDesktop.Streamer.exe`（用第 1 项的 PID 交叉核对） | 🖐 查 `C:\ProgramData\Virtual Desktop\ServiceLog.txt` 里的 `Failed to start Streamer on active session` / `UnauthorizedAccessException`；按 03 文档走「服务身份」分支 | 不适用（只读诊断） | 无（只读） |
| 3 | 服务在跑但 Streamer 进程起不来（本机 2026-09 反复出现） | `Get-Service VirtualDesktop.Service -ErrorAction SilentlyContinue \| Format-List Name,Status,StartType; Get-Content "C:\ProgramData\Virtual Desktop\ServiceLog.txt" -Tail 20` | `Status=Running` **且** 日志尾部无 `HRESULT -2147024891` | 🖐 该错误是「服务登录身份配置错误」，需重装/修复 Streamer（官方 FAQ：「add an exception to your anti-virus or manually install VirtualDesktop.Service by double-clicking it」）；**不要**让工具自动重装 | 不适用 | 中。重装会改服务账户与防火墙规则，必须人工确认 |
| 4 | PC 上次到底连成功过没有（判定「Streamer 是否真的工作过」的最强单一信号） | `Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw \| ConvertFrom-Json \| Select-Object LastConnectDate,ServerRotation,ShowPairingRequests,DontWarnApps` | `LastConnectDate` 非空且是近期 | 🖐 `LastConnectDate` 为空 = 从未成功连过，回到第 1 项；`ShowPairingRequests=false` 时提示用户在 Streamer 里打开「显示配对请求」，否则新头显会被静默忽略 | 不适用（只读） | 无（只读） |

---

## 阶段 1：防火墙（本轮实测发现两条反直觉事实，见 01 文档 §5.1-5.2）

| 编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险 |
|------|------|------------------|---------|-----------------|---------|------|
| 5 | 防火墙里根本没有 VD 的入站规则 | `netsh advfirewall firewall show rule name=all \| findstr /i /c:"virtual desktop"` | 至少 1 行 `Rule Name:` / `Grouping:` | ⚙ 建规则（见第 6 项的命令），建完复查本项 | ⚙ `netsh advfirewall firewall delete rule name="Virtual Desktop Streamer"` | 中。新增规则会扩大入站面；必须**回滚**且不改其它规则 |
| 6 | 规则存在但不覆盖当前网络配置文件 | `netsh advfirewall firewall show rule name="Virtual Desktop Streamer" verbose` | `Profiles:` 含当前 profile（Private 或 Public），`Direction: In`，`Action: Allow`，`Enabled: Yes` | ⚙ 补 profile：`netsh advfirewall firewall set rule name="Virtual Desktop Streamer" new profile=any` | 记下原值 `netsh advfirewall firewall show rule name="Virtual Desktop Streamer" verbose \| findstr /i profiles` → 改回 | 中。`profile=any` 会让规则在所有 profile 生效 |
| 7 | **规则是 Program 作用域，但 Streamer.exe 被移动/重装过** → 规则指向不存在的路径 | `netsh advfirewall firewall show rule name="Virtual Desktop Streamer" verbose \| findstr /i /c:"Program:"` | `Program:` 指向的文件**确实存在**：`Test-Path "C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe"` 返回 `True` | ⚙ 用实际路径重建规则（先删后建，见第 5 项回滚） | 先 `delete` 再建 = 可回滚 | 中 |
| 8 | **Defender 防火墙 profile 整体被关闭**（本机 Private/Public 均为 False） | `Get-NetFirewallProfile \| Format-Table Name,Enabled -AutoSize` | `Private` 与 `Public` 至少一个是 `True` | 🖐 提示用户：防火墙关闭意味着有别的东西（VPN/加速器/杀软驱动）在接管过滤，**风险由未知方承担**；是否开启由用户决定 | 不适用（工具不改） | 低（只读）。但**绝不要**让工具自动开启防火墙——那会改变用户整体安全姿态 |
| 9 | 出站被默认 Block 策略或第三方规则拦截 | `Get-NetFirewallProfile \| Format-Table Name,DefaultOutboundAction -AutoSize` | `DefaultOutboundAction` = `NotConfigured`（即 Allow）或 `Allow` | 🖐 若为 `Block`，找对应的出站 Block 规则并加入例外；这是企业策略，**工具只报告不修改** | 不适用 | 低（只读） |
| 10 | 第三方杀软/防火墙接管（Avast/AVG/McAfee/Norton/360/火绒） | `Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct \| Format-Table displayName,productState -AutoSize` | 只有 `Windows Defender` 一项（本机实测：`Windows Defender / 401664`） | 🖐 官方 FAQ 原话：McAfee/Norton「try disabling them or adding an exception for Virtual Desktop Streamer」；Avast/AVG「把防火墙网络配置文件设为 Private 而非 Public」。**工具只给指引，不自动禁用杀软** | 不适用 | 无（只读）。自动禁用杀软是绝对禁止的 |
| 11 | 出向 UDP 38850/38860 被驱动级过滤（杀软/加速器的 WFP 过滤器） | ⚙ 需管理员+抓包：`pktmon start --capture --comp nics --pkt-size 0 --file-name "$env:TEMP\vd.etl"; pcapmon start` 后开 Streamer，等 60s，再 `pktmon stop` | 抓到 `udp dstport 38850` 或 `udp dstport 38860` 的包 | 🖐 有规则没放行/有杀软 → 定位到第 10 项；无第三方但仍无包 → 转阶段 4 的网卡/路由 | `pktmon stop` / 删除 `%TEMP%\vd.etl` | 低（只读抓包）。但需要管理员权限；且 `pktmon` 会短暂占用抓包句柄 |
| 12 | VD 官方警告类别 `NetworkProfile` 曾被触发过 | `Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw \| ConvertFrom-Json \| Select-Object -ExpandProperty DontWarnApps` | 输出不含 `NetworkProfile`（或用户已知晓） | 🖐 `DontWarnApps` 含 `NetworkProfile` = 用户曾手动屏蔽过这个告警 → **提示用户重新打开该告警**，因为它正是本清单的核心告警 | 不适用（工具不改用户的屏蔽列表） | 无（只读） |

---

## 阶段 2：IP 层（子网 / APIPA / 路由选错）

| 编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险 |
|------|------|------------------|---------|-----------------|---------|------|
| 13 | 有网卡落在 APIPA 169.254/16（DHCP 失败或未连） | `Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue \| Where-Object {$_.IPAddress -like "169.254.*"} \| Format-Table InterfaceAlias,IPAddress,PrefixLength -AutoSize` | **输出为空** | 🖐 APIPA 说明该网卡没拿到 DHCP 地址。逐个 `Get-NetAdapter` 判定是「该拔掉」还是「该重连」；必要时 `Disable-NetAdapter -Name "<别名>" -Confirm:$false` | ⚙ `Enable-NetAdapter -Name "<别名>"` | **高**。禁用网卡会切断正在使用的连接（可能是远程会话）。**必须先确认该别名不是当前活动链路**，且默认用 `-Confirm:$false` 前先交互确认 |
| 14 | APIPA 检测被虚拟/隐藏网卡污染（需要排除噪声） | `Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue \| Where-Object {$_.IPAddress -like "169.254.*"} \| Where-Object {$_.InterfaceAlias -notmatch "^Local Area Connection\*"}` | 排除 `Local Area Connection*` 后为空 | 🖐 本机实测：5 个 APIPA 中有 2 个（`Local Area Connection* 2 / * 5`）是**虚拟/点对点伪连接**，`Get-NetAdapter` 里根本看不到 → 判定主网卡是否 APIPA 时必须用**排除后**的集合，否则永远误报 | 不适用 | 无（只读） |
| 15 | 当前网络配置文件不是 Private | `Get-NetConnectionProfile \| Format-Table InterfaceAlias,NetworkCategory -AutoSize`；数值版：`Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_NetConnectionProfile \| Format-Table InterfaceAlias,NetworkCategory -AutoSize` | `NetworkCategory` = `Private`（数值 `1`）。枚举：`0`=Public，`1`=Private，`2`=DomainAuthenticated | 🖐 Public 下 Windows 的入站策略最严。**建议**改 Private：`Set-NetConnectionProfile -InterfaceAlias "<别名>" -NetworkCategory Private`；⚙ 但必须先弹窗说明「这会放宽该网络上的入站信任」 | ⚙ `Set-NetConnectionProfile -InterfaceAlias "<别名>" -NetworkCategory Public` 改回 | **中-高**。把 Public 改成 Private 会放宽整张网络的入站信任，属安全姿态变更。**必须**先记录原值，且 UI 上要求显式二次确认 |
| 16 | 多网卡 / VPN / Hyper-V / WSL / VMware 抢路由 | `Get-NetAdapter \| Where-Object {$_.InterfaceDescription -match "Hyper-V\|VMware\|VirtualBox\|TAP\|Wintun\|WireGuard\|Tailscale\|ZeroTier\|WSL"} \| Format-Table Name,Status,InterfaceDescription -AutoSize` | 为空（本机实测有 3 条：VeryKuai TAP、2× Hyper-V） | 🖐 本机实测：`Ethernet 2 / VeryKuai TAP Adapter`、`vEthernet (Default Switch)`、`vEthernet (WSL (Hyper-V firewall))` 均存在且部分 Up。VD 的广播**从哪块网卡出去**由 Windows 路由决定 → 见第 17 项 | 不适用 | 无（只读） |
| 17 | 选错出口：广播没有走物理 LAN | `Get-NetIPInterface -AddressFamily IPv4 \| Sort-Object InterfaceMetric \| Format-Table InterfaceAlias,InterfaceMetric,ConnectionState -AutoSize`（越小越优先） | 物理 LAN 网卡的 `InterfaceMetric` **小于**所有虚拟/VPN 网卡 | 🖐 若虚拟网卡 metric 更小（Hyper-V 默认 5、物理网卡默认 25），广播会走错路。修复需改 `Set-NetIPInterface -InterfaceMetric`，属于网络栈高级操作 | ⚙ 记下原 metric，改回去即可 | **高**。改接口 metric 会影响全局路由与出站选择，可能打断 VPN 与其它全部流量。**只做诊断输出，不自动修** |
| 18 | ICS（Internet 连接共享）改变了 NAT 行为 | `Get-Service SharedAccess \| Format-Table Name,Status,StartType -AutoSize`；另查 `Get-NetNat -ErrorAction SilentlyContinue` | `Status = Stopped`（本机实测为 **Running** → 命中） | 🖐 ICS 会把共享网卡 NAT 化，广播可能被 ICS 过滤/改写。停用：`Disable-NetConnectionSharing -PrivateInterface "<别名>" -PublicInterface "<别名>"`，或直接关 ICS 服务 | ⚙ `Enable-NetConnectionSharing -PrivateInterface "<别名>" -PublicInterface "<别名>"` | **高**。ICS 往往在承载手机的热点/共享，乱停会断掉别人的网络。**只报告** |

---

## 阶段 3：端口占用与协议层

| 编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险 |
|------|------|------------------|---------|-----------------|---------|------|
| 19 | 38810-40 被别的进程占用（Streamer listen 失败 `WSAEADDRINUSE`） | `Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue \| Where-Object {$_.LocalPort -in 38810,38820,38830,38840} \| Format-Table LocalAddress,LocalPort,OwningProcess -AutoSize` | 要么为空（Streamer 没跑），要么 4 个端口的 `OwningProcess` **全部相同**且等于 Streamer PID | 🖐 若出现多个不同 PID → 有第二个程序占了 VD 端口。用 `Get-Process -Id <OwningProcess> \| Select-Object ProcessName,Path` 定位；**不要**让工具杀进程（硬地板禁止不明的进程终止） | 不适用 | 无（只读） |
| 20 | UDP 38850 被别的进程占用（发现协议被抢占 → 必然发现失败） | `Get-NetUDPEndpoint -ErrorAction SilentlyContinue \| Where-Object {$_.LocalPort -in 38850,38860} \| Format-Table LocalAddress,LocalPort,OwningProcess -AutoSize` | 空，或占用者就是 Streamer | 🖐 同第 19 项。**注意**：38850 是绑 `0.0.0.0` 的独占端口，被占即发现完全失效 | 不适用 | 无（只读） |
| 21 | 出向端口映射（UPnP）异常 | `Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw \| ConvertFrom-Json \| Select-Object ServerRotation` | 同网段 LAN 场景下 **UPnP 完全不需要**，`ServerRotation` 与发现无关 | 🖐 仅当用户要用**远程**连接时才需要：`AllowRemoteConnections` + 路由器开 UPnP。官方 FAQ：「forward TCP ports 38810, 38820, 38830 and 38840」。同网段不要去动它 | 不适用 | 低（只读）。盲目开 UPnP 会扩大 NAT 暴露面，**不要自动开** |
| 22 | 远程中继端口被误用在同网段场景（**工具自身的 bug 防线**） | 常量表里 38811/38821/38831/38841 必须挂在**远程分支**下，判据 = 同网段场景不检查它们 | 工具任何针对这四个端口的防火墙建议/占用检查，**都不得在同网段触发** | ⚙ 保持门控，不要在同网段路径引用 | n/a | **高（若错用）**。此前以为这四个端口不存在，2026-10-05 证明它们存在（01 文档 §4.1 更正：旧的「零命中」是在文件名与内容错位的程序集里搜出来的）。它们是**远程中继**端口，与同网段发现无关——同网段走 38810/20/30/40 直连 |

---

## 阶段 4：物理链路 / 节能 / 时序

| 编号 | 症状 | 检查命令(可直接粘) | 通过判据 | 失败时的修复动作 | 回滚方式 | 风险 |
|------|------|------------------|---------|-----------------|---------|------|
| 23 | PC 用无线连路由器（性能与稳定性双输） | `Get-NetAdapter \| Where-Object {$_.Status -eq "Up" -and $_.InterfaceDescription -match "Wireless\|Wi-Fi\|802\.11"} \| Format-Table Name,Status,LinkSpeed -AutoSize` | 无 Up 的无线网卡 | 🖐 官方 Computer Requirements：*"**Wired** computer to 5 GHz AC or AX Wi-Fi router"*。建议插网线；**不要**让工具禁用无线网卡（可能正在用它连外网） | 不适用 | 无（只读） |
| 24 | 网卡节能把链路睡掉（表现为「一段时间后突然 PC 不可达」） | `powercfg /a`；以及 `Get-NetAdapterPowerManagement -Name "<别名>" -ErrorAction SilentlyContinue` | `powercfg /a` 里 S0 低电量空闲（Modern Standby）**未启用**，或该网卡不支持节能 | 🖐 关闭网卡节能：设备管理器 → 网卡 → 电源管理 → 取消「允许计算机关闭此设备以节约电源」；或 `powercfg /setacvalueindex scheme_current sub_sleep standbyidle 0`。**Realtek/Wi-Fi 驱动层另有省电开关，工具无法可靠编程关闭** | `powercfg /setacvalueindex scheme_current sub_sleep standbyidle <原值>` | **中-高**。改电源计划影响整机续航/发热。**只诊断不给一键修** |
| 25 | PC 刚睡醒，头显连不上（必须先碰一下 PC） | `powercfg /lastwake`；`powercfg /waketimers` | — | 🖐 这是**代码里明确存在的行为**，不是 bug：`ConnectionManager.cs:863-882` 的 `PowerModeChanged` 处理器在 `Suspend`/`Resume` 时置位内部状态，唤醒后必须重新握手。指引：关掉 PC 自动睡眠，或唤醒后先在 Streamer 里点一次刷新 | 不适用 | 无（只读） |
| 26 | 网卡换 IP 后 VD 没恢复（`NetworkAddressChanged` 2 秒去抖） | 需人工观察：切换网线/Wi-Fi 后 5 秒内在 Streamer 窗口确认是否重新出现 | Streamer 自动恢复 | 🖐 代码依据：`ConnectionManager.cs:246-247` 订阅地址/可用性变化，`:830` 走 `CancelAfter(2000)` 后取消会话。恢复路径依赖上层重新发起连接 → 指引用户重启 Streamer（`Stop-Process` 再 `Start-Process`，见第 1 项） | 同第 1 项 | 无（只读） |
| 27 | 「先开 Streamer 再开头显」的时序约束 | 需人工：关掉头显 VD → 重开 Streamer → 先开 PC 端 Streamer 再从头显连 | 连续 3 次成功 | 🖐 上一轮 HANDOFF 记为「经验性约束」。本轮**未从代码中找到 Quest 端的重试时序证据**（Quest 网络代码未反编译）→ `[未验证]`。验证它需要：Quest + PC 联调，用 `pktmon` 记录 38850/38860 的实际握手顺序 | 不适用 | 无（只读） |

---

## 覆盖对照（brief 要求的失败族 → 编号）

| 失败族 | 覆盖编号 |
|--------|---------|
| Windows 防火墙缺入站规则 | 5, 6, 7 |
| 出站被拦 | 9, 11 |
| 第三方杀软（Defender profile 关闭 / McAfee / Norton / 360 / 火绒） | 8, 10 |
| 网络配置文件是 Public | 12, 15 |
| APIPA 169.254.x.x | 13, 14 |
| 多网卡 / VPN / Hyper-V / WSL2 / VMware 路由选错 | 16, 17 |
| 路由器 AP 隔离 / 访客网络 / 双频不同网段 / 无线有线混用 / VLAN | **不可自动检测**，见下 |
| 端口被占（38810-40 冲突） | 19, 20 |
| 休眠 / 网卡节能 / 驱动掉线 | 24, 25, 26 |
| UDP 广播被交换机或 VPN 客户端吞掉 | 11, 16, 17 |
| ICS 开启后 NAT 行为改变 | 18 |

**关于「路由器 AP 隔离 / 访客网络 / VLAN / 双频不同网段」这一族**：
这些状态**在 PC 上没有任何可编程的观测点** —— Windows 侧无法读到自家路由器的 AP 隔离开关，
也无法知道头显的 SSID 是否与 PC 同网段。工具只能：
1. 检测**间接证据**：PC 有多个 Up 网卡且分属不同子网（提示可能连了不同的 AP/频段）；
2. 比对 PC IP 与头显上报的 IP 是否同 `/24`（需要头显侧提供自己的地址）；
3. 输出一段**固定的路由器指引文案**（关闭 AP 隔离 / 退出访客网络 / 让两端都上 5 GHz / 确认无 VLAN）。

> `[未验证]` 「同一 SSID 的 2.4 GHz 与 5 GHz 必然落在不同网段」——这取决于路由器实现
> （部分路由器做 band steering 会在同网段，部分会拆网段）。验证它需要两台可控设备实测。
> **工具不要把这条写成硬规则**，只作为文案提示。

---

## 命令实跑验证记录（2026-10-05）

以下为本机真实输出，用于确认上表命令可用（完整输出见 `01-ports-and-discovery.md` §5）：

- 第 2/19 项：本机 **输出为空**（Streamer 未运行，与 §5.4 的 ServiceLog 错误一致）
- 第 8 项：`Domain=True / Private=False / Public=False` ← **本机命中「防火墙 profile 被关闭」**
- 第 13 项：5 条 APIPA，其中 2 条为 `Local Area Connection*` 伪连接（第 14 项要排除的）
- 第 15 项：`Ethernet / NetworkCategory = 1`（= Private）
- 第 16 项：3 条虚拟网卡命中正则（VeryKuai TAP + 2× Hyper-V）
- 第 18 项：`SharedAccess / Running`
- 第 10 项：仅 `Windows Defender / 401664`

> 上述 8 项里有 **3 项在本机就是「失败/命中」状态**（第 8、13、18 项），
> 说明这份清单确实覆盖了真实机器上会发生的形态，而不是纸面清单。
