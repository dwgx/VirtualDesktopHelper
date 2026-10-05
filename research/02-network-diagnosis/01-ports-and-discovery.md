# 01 — PC↔Quest 局域网串流：端口矩阵与发现机制

范围：Virtual Desktop（vrdesktop.net）PC Streamer ↔ Quest 头显，**同网段 LAN 直连**路径。
所有代码事实来自 Owner 的反编译树，**本轮重新核实过**，不复用上一轮报告的行号（上一轮引用的
`NetworkManager.cs:452-496 / 582-592` 与 `ConnectionManager.cs:697` 在当前树里无法复现，见 §4）。

证据路径前缀（下文简写为 `VD-Net/`）：

```
VD-Net/ = F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/VirtualDesktop/
VD-Mob/ = F:/Project/VirtualDesktop/analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/
```

---

## 1. 端口 / 协议 / 方向 / 用途 / 是否必须

**方向列以 PC 为视角**（PC 是什么角色）。这是本轮最重要的一个事实纠正：

> **PC 在全部四个 TCP 通道上都是被动方（bind + accept）。Quest 是 TCP 客户端。**
> PC 侧 bind 地址是 `IPAddress.Any`（0.0.0.0），即监听所有网卡 —— 证据 `VD-Net/../../-\-.132.cs:1014`
> （`\u0096\u0002.\u0002.\u0001 = (... ? IPAddress.IPv6Any : IPAddress.Any)`），被
> `VD-Net/Streamer/ConnectionManager.cs:200/206/211/213` 用来构造四个 `IPEndPoint`。

| 端口 | 协议 | PC 方向 | 用途 | 必须？ | 证据 `file:line` |
|------|------|---------|------|--------|-----------------|
| 38810 | TCP | **listen** (0.0.0.0) | 控制/消息通道 `NetMessagingClient` | **必须**（LAN 与远程都用它） | `VD-Net/Streamer/ConnectionManager.cs:200` |
| 38820 | TCP | **listen** (0.0.0.0) | 数据通道（输入/控制回传） | **必须** | `VD-Net/Streamer/ConnectionManager.cs:206` |
| 38830 | TCP | **listen** (0.0.0.0) | 视频通道 | **必须** | `VD-Net/Streamer/ConnectionManager.cs:211` |
| 38840 | TCP | **listen** (0.0.0.0) | 音频通道 | **必须** | `VD-Net/Streamer/ConnectionManager.cs:213` |
| **38850** | **UDP** | **listen (0.0.0.0) + 单播回包**；另持有 `Broadcast:38850` 端点 | **真正的发现/配对协议**（上一轮报告完全漏掉） | **必须**（发现成败取决于它） | `VD-Net/-\-.112.cs:209`（广播端点）、`:212`（监听端点）、`:330`（`new UdpClient(Any:38850)`）、`:451`（单播回包） |
| **38860** | **UDP** | **broadcast send，负载 0 字节** | 宣告/唤醒脉冲（空包） | 必须（发现时序的一部分） | `VD-Net/Streamer/ConnectionManager.cs:674`（`new IPEndPoint(IPAddress.Broadcast, 38860)`）、`:681`/`:684`（`Send(Array.Empty<byte>(), 0, ep)`） |
| 7 / 9 | UDP | broadcast send | Wake-on-LAN magic packet（唤醒睡眠的 PC） | 非必须（仅唤醒用） | `VD-Net/Net/WOLHelper.cs:43`（`:7`）、`:47`（`:9`） |
| 38810-40 | TCP | UPnP 出向映射 | 仅 `AllowRemoteConnections` 的远程/NAT 穿透用 | 同网段 LAN **不需要** | `VD-Net/Net/UPnPManager.cs:220`（端口过滤）、`:242-254`（`CreatePortMapAsync(Protocol.Tcp, …, 38810, 38810, …)`）；调用点 `VD-Net/Streamer/ConnectionManager.cs:240-243` |

**端口是硬编码常量，不是 DHCP 分配的** —— 38810/20/30/40/50/60 全部以字面量出现在反编译源码里，
无任何配置项可改（`StreamerSettings.json` 里也没有端口键，见 §5）。

### 官方文档对照（`https://www.vrdesktop.net/`，本轮实读）

- FAQ「Can I connect to my computer over the Internet?」原文：
  > "the Streamer App will forward the required ports automatically. If you want to manually
  > configure your router, **forward TCP ports 38810, 38820, 38830 and 38840.**"
- 官方**只写了 TCP 38810-40**。**UDP 38850 / 38860 官方文档从未提及**，它们只存在于反编译源码里
  → 工具必须把这两个 UDP 端口当作「代码事实」而非「文档事实」，这一点要在 UI 上区分清楚。
- 官方 Computer Requirements：*"**Wired** computer to **5 GHz** AC or AX Wi-Fi router"*。

---

## 2. 发现机制的完整时序

### 2.1 机制定性（先否掉几种猜测）

| 候选机制 | 是否使用 | 依据 |
|----------|---------|------|
| mDNS / DNS-SD | **否** | 全树无 `DnsServiceDiscovery`/`ServiceType`/`.local` 相关引用；发现走裸 UDP 广播 |
| 组播 multicast | **否** | 无 `MulticastOption`/`JoinMulticastGroup`/`AddMembership`；发现端点是 `IPAddress.Broadcast` |
| **定向广播 broadcast** | **是** | `IPAddress.Broadcast`（255.255.255.255），`ConnectionManager.cs:674`、`-\-.112.cs:209` |
| **单播 unicast 回包** | **是** | `-\-.112.cs:451` `u4.Send(array5, …, remoteEndPoint)` |
| 云注册发现 | 是，但**离线时被绕过** | `VD-Mob/NetworkManager.cs:400` `ComputerRegistryOfflineTimeout = 3s`；云不可达即 fallback 到本地发现 |

> **广播是发现的关键**：这意味着任何丢广播的网络设施（AP 隔离、访客网络、VLAN、
> VPN 客户端的虚拟网卡）都会**直接**让发现失败，而单播 TCP 其实完全没问题。

### 2.2 PC 侧启动时序（`VD-Net/Streamer/ConnectionManager.cs:178-256`）

```
ConnectionManager.\u0001()  启动
 ├─ 191  <IComputerRegistry> 初始化（云注册通道）
 ├─ 200  listen TCP 38810  (NetMessagingClient)   ← 控制
 ├─ 206  listen TCP 38820  (数据)
 ├─ 211  listen TCP 38830  (视频)
 ├─ 213  listen TCP 38840  (音频)
 ├─ 238-243  if (!AllowRemoteConnections) → UPnPManager.\u0001().\u0001()
 ├─ 244  Task.Run(...)  周期任务（Timer，见 2.4）
 ├─ 246  NetworkAddressChanged      += \u0006   ← 网卡地址变化
 ├─ 247  NetworkAvailabilityChanged += \u0006   ← 网卡可用性变化
 └─ 248  PowerModeChanged           += \u0001   ← 睡眠/唤醒
```

**网卡变化处理**（`:817-861`）：变化事件 → `CancellationTokenSource.CancelAfter(2000)`，
即 **2 秒去抖**后取消当前连接任务（`:830` `~\u0001(cts, 2000, -1)`；`:844` `cts.Cancel()`）。
含义：网卡掉线/换 IP 后，VD 自己会撤销旧会话，但**要求上层重新发起连接**——
这正是「网卡节能掉线 → 表现为 PC 突然不可达」的机制根源。

**电源事件处理**（`:863-906`）：读 `PowerModes`（`:874`），`Suspend` / `Resume` 分别置位
`ConnectionManager.\u0002`（`:882` 对 Resume 置 `false`）→ **唤醒后需要重新握手**。
所以「PC 睡眠后头显连不上、必须先碰一下 PC」是可解释的、有代码依据的行为。

### 2.3 宣告脉冲：UDP 38860 空广播（`VD-Net/Streamer/ConnectionManager.cs:657-708`）

```csharp
private static void \u0007() {
    UdpClient udpClient = new UdpClient(AddressFamily.InterNetwork);   // :665
    udpClient.EnableBroadcast = true;                                   // :673
    IPEndPoint ipendPoint = new IPEndPoint(IPAddress.Broadcast, 38860); // :674
    udpClient.Send(Array.Empty<byte>(), 0, ipendPoint);                 // :681  (重复 :684)
}
```

- **负载长度 = 0**。它不是「握手包」，不是「ConnectionID + Platform」。
- 它只表达「这台 PC 上有 VD Streamer 在跑」。
- 整段包在 `try/catch{}` 里**吞掉所有异常**（`:697-699` 空 catch）→ 广播发不出去时
  **Streamer 不会报错、日志里也看不到**。这解释了为什么「PC 明明开着但头显列表里没有它」
  完全没有本地线索。**这是工具最有价值的一个检测点。**

### 2.4 广播周期

- 周期任务由 `Timer` 驱动：`ConnectionManager.cs:1011`
  `ConnectionManager.\u0001 = new Timer(new TimerCallback(ConnectionManager.\u0001));`
- 回调体 `:908-927` 只是启动一个 async 状态机 `ConnectionManager.\u0008`。
- **该状态机的实现文件不在当前反编译树里**，`Change(...)` 的周期参数也搜不到。

> `[未验证]` **38860 广播的真实周期**。验证它需要：在 PC 上开 Wireshark 抓
> `udp.port == 38860`，同时让 VD Streamer 运行 ≥60 秒，统计包间隔。
> 工具实现时**不要写死周期**——按「60 秒内 ≥1 个包」判通过即可。

### 2.5 真正的发现/配对协议：UDP 38850（`VD-Net/-\-.112.cs`）

这是上一轮报告完全缺失、但**决定「头显能不能发现 PC」的核心**。
类：`internal sealed class \u0002 : IDisposable`，namespace `\u008B`（`-\-.112.cs:23`）。

**静态材料（`-\-.112.cs:193-231` 静态构造）**

| 字段 | 值 | 行 |
|------|-----|-----|
| 固定 AES **Key** (32B) | `32,71,236,94,194,34,85,255,165,172,187,150,6,104,106,57,57,62,244,114,75,174,237,9,48,36,239,82,57,98,205,80` | `:197-203` |
| 固定 AES **IV** (16B) | `82,200,129,118,144,104,249,4,62,20,120,110,20,180,63,31` | `:204-208` |
| 广播端点 | `IPAddress.Broadcast : 38850` | `:209` |
| 监听端点 | `IPAddress.Any : 38850` | `:212` |
| 序列化器 | `new DataContractSerializer(typeof(Computer))` | `:213` |
| 加密 | `PaddingMode.None` | `:231` |

**绑定与收包循环（`-\-.112.cs:322-457`）**

```
\u0002(HashSet<PlatformAccessToken> tokens, Computer self)      // :322
 ├─ :330  this.\u0002 = new UdpClient(IPEndPoint(0.0.0.0, 38850))   ← PC 监听 38850
 └─ :335  while (udp != null) { ReceiveAsync() … }

   分支 A：len == 17                                            // :339
     └─ :343  前 16 字节 == self.ConnectionID ?
        :347  Platform = (Platform)buffer[16]
        :352  触发 (platform, self.EncryptLocalTraffic) 事件
        :354  关闭监听（break）        ← 「NAT 打洞/唤醒」，收到即收工

   分支 B：len > 243                                           // :365-368
     └─ :372  CreateDecryptor()  ← 用固定 Key/IV 解密
        :377-382  前 243 字节 ASCII = RSA 公钥 XML (RSAParameters)
        :383-391  余下 = platform 字节(可选) + UTF8 token/UserProof
        分支 B1：token 已在已知集合                               // :401
          :409  RSACryptoServiceProvider.Encrypt(48B 会话密钥材料)
          → 走分支 C 回包
        分支 B2：token 未知                                       // :432
          :436-441  触发「配对请求」事件 → Streamer 的 ShowPairingRequests UI

   分支 C：回包                                                 // :445-452
     [128 字节 RSA 密文] + [明文 Computer 载荷]
     u4.Send(array5, len, remoteEndPoint)     ← 单播回给请求方
```

**出向记录构造 `private byte[] \u0001(Computer, out byte[])`（`-\-.112.cs:504-561`）**

```
:509  Aes aes = Aes.Create()                     ← 每次新随机密钥
:513  PaddingMode.None
:514  out byte[48]
:515  拷入 aes.Key  → [0..32)
:516  拷入 aes.IV   → [32..48)
:520-524  DataContractSerializer 把 Computer 序列化后过加密流
```

→ 出向广播/响应格式为 **`[32B Key][16B IV][AES(DataContract(Computer))]`**。

**与 `ShowPairingRequests` 的耦合**：`VD-Net/Streamer/StreamerSettings.cs:2154`
在 `_showPairingRequests` 置 true 时调用 `\u008B\u0002.\u0001()`（`-\-.112.cs:577-588`，清空
「已见过的 RSA 公钥」`HashSet<string>`），让老头显能重新发起配对。

### 2.6 Quest 侧（已核实到的部分）

`VD-Mob/NetworkManager.cs`（**全文仅 453 行，且不含任何端口字面量**）：

| 事实 | 行 |
|------|-----|
| `ComputerRegistryTimeout = 12s` | `:397` |
| `ComputerRegistryOfflineTimeout = 3s` | `:400` |
| `DisconnectRefreshDelay = 3s` | `:394` |
| `GetComputersAsync(tokens, timeout)` | `:220` |
| `ConnectToComputerAsync(computer)` | `:164` |
| `_computerUnreachable` / `_computerUnableToConnect` / `_computerNeedsUpdate` 三个失败态 | `:293-303` |
| 判定 unreachable 后 6 秒自动重刷列表 | `:311` |
| 普通断线 50ms / needsUpdate 50 秒后重刷 | `:316-319` |

> **Quest 侧的 38850/38860 发送代码不在可读树里。**
> `F:/Project/VirtualDesktop/analysis/apk_patch/decompiled/VirtualDesktop.Net/` 目录下
> **只有 `OpenTK-1.1/`**，没有 `VirtualDesktop.Net.dll` 的网络实现（真正的 Net 类在 APK 的
> IL blob entry #43，未反编译）。所以「Quest 端到底发什么到 38850」只能从 PC 端的
> 收包分支反推，**不能声称已直接读到 Quest 的发送代码**。

---

## 3. 什么条件下发现必然失败

按「必然失败」的程度排序（越靠前越是确定性阻断）：

| # | 条件 | 为什么必然失败 | 证据 |
|---|------|--------------|------|
| F1 | PC 与头显**不在同一 IP 子网 / 跨 VLAN** | 全部发现都靠 `255.255.255.255` 定向广播；路由器默认不转发广播 | `ConnectionManager.cs:674`、`-\-.112.cs:209` |
| F2 | 路由器 **AP 隔离 / Client Isolation / 访客网络**开启 | 同上，且常见于「访客 Wi-Fi」——官方 FAQ 明确点名 Guest network | `https://www.vrdesktop.net/` FAQ |
| F3 | PC 或头显拿到 **APIPA 169.254.x.x** | 169.254/16 是无 DHCP 的自我地址，PC 上 `ConnectionManager` 监听在 0.0.0.0 仍会绑上，但**路由/回程选错**，且头显在 169.254 段找不到任何真实 PC 地址 | 本机实测见 §5 |
| F4 | **Public 网络配置文件** + 防火墙规则只建在 Private/Public 之一 | 见 §5 实测：本机规则覆盖三 profile，但**若被第三方工具重建成只覆盖 Public，则 Private 侧全断** | `netsh` 输出见 §5 |
| F5 | 出站被拦（默认出站 Block 策略 / 第三方杀软） | 38850 的单播回包（`-\-.112.cs:451`）和 38860 广播都属出向 | 同上 |
| F6 | **第三方防火墙/杀软**过滤 UDP 38850/38860（Avast/AVG/McAfee/Norton/360/火绒） | UDP 广播被驱动层过滤时，Streamer **静默失败**（空 catch，`ConnectionManager.cs:697`） | 官方 FAQ 明确点名 McAfee/Norton/Avast/AVG |
| F7 | VPN / TAP / Hyper-V / WSL 虚拟网卡**路由 metric 更小** | Windows 选源地址/出接口错，广播从 VPN 网卡出去到不了物理 LAN | 本机实测见 §5（VeryKuai TAP + 2× Hyper-V） |
| F8 | ICS（Internet 连接共享）开启 | 共享网卡被 NAT，PC 侧广播被 ICS 过滤/改写 | 本机 `SharedAccess` = Running，见 §5 |
| F9 | 网卡节能/驱动掉线（Realtek/Wi-Fi 省电） | 地址变化触发 2 秒去抖取消会话（`ConnectionManager.cs:830`），上层不重连就表现为不可达 | `ConnectionManager.cs:246-247, 830` |
| F10 | PC 休眠后未唤醒 | `PowerModeChanged` 置位后需重新握手 | `ConnectionManager.cs:863-882` |
| F11 | Streamer 进程/服务没跑 | 无监听、无广播。**这是「找不到 PC」最高频的平凡原因** | 见 §5 的 ServiceLog 实测 |
| F12 | TCP 38810-40 被别的进程占用 | `new IPEndPoint(Any, 38810)` 后 listen 会 `WSAEADDRINUSE`，异常向上抛（`ConnectionManager.cs:250-254` rethrow） | `ConnectionManager.cs:200/206/211/213` |

---

## 4. 对上一轮报告的纠正（重要）

上一轮 `02_networking_streaming.md` 声称的行号在**当前树里无法复现**，本轮不沿用：

| 上一轮声称 | 本轮核实结果 |
|-----------|-------------|
| `NetworkManager.cs:452-496` / `:464/568/573/578/582/587/592` 含端口 | **文件只有 453 行，且零端口字面量**。Quest 侧端口常量在未反编译的 `VirtualDesktop.Net.dll`（APK blob entry #43）里 |
| `ConnectionManager.cs:697` 是 `new IPEndPoint(IPAddress.Broadcast, 38860)` | 文件确实存在（`VD-Net/Streamer/ConnectionManager.cs`），但 38860 在 **`:674`**；`:697` 是空 `catch` |
| 「17 字节 UDP 包 = ConnectionID(16B)+Platform(1B)」发到 **38860** | 方向对（是 Quest→PC），但**端口是 38850 不是 38860**，且方向是 **PC 收**（`-\-.112.cs:330/339/343/347`） |
| 「`UPnPManager` 创建/清理 38810-40 TCP 映射」 | ✅ 成立（`Net/UPnPManager.cs:220-254`） |
| 「`ComputerRegistryOfflineTimeout = 3s`」 | ✅ 成立（`VD-Mob/NetworkManager.cs:400`） |
| 「端口矩阵：38811/38821/38831/38841 远程端口」 | ⚠️ **本条已于 2026-10-05 推翻，见下方更正。**当时的「全树零命中」是在 `extracted_assemblies/` 里搜出来的，那套 dll 的文件名与内容错位，真正的 `VirtualDesktop.Net.dll` 根本没被搜到。**正确结论：这四个端口确实存在**，用于远程中继，与同网段发现无关 |
| 「UDP 38860 是局域网广播（唤醒/通知）」 | ✅ 成立，但**负载是 0 字节**，不是带信息的包 |

---

## 5. 本机实测取证（2026-10-05，只读查询）

### 5.1 防火墙规则（验收要求项，原样粘贴）

```
> netsh advfirewall firewall show rule name=all | findstr /i /c:"virtual desktop"

Rule Name:                            Virtual Desktop Streamer
Grouping:                             Virtual Desktop Streamer
```

带详情：

```
> netsh advfirewall firewall show rule name="Virtual Desktop Streamer" verbose

Rule Name:                            Virtual Desktop Streamer
----------------------------------------------------------------------
Enabled:                              Yes
Direction:                            In
Profiles:                             Domain,Private,Public
Grouping:                             Virtual Desktop Streamer
LocalIP:                              Any
RemoteIP:                             Any
Protocol:                             Any
Edge traversal:                       Yes
Program:                              C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe
InterfaceTypes:                       Any
Security:                             NotRequired
Rule source:                          Local Setting
Action:                               Allow
Ok.
```

**读出来的三条结论：**

1. **只有 1 条规则**，不是 4 条。它是 **Program 作用域 + `Protocol: Any` + In 方向**，
   一次性放行 38810-40 与 UDP 38850/38860（Any 协议覆盖 UDP）。
2. **`Profiles: Domain,Private,Public` —— 三个 profile 全覆盖**，所以「Public 配置文件导致不通」
   在**本机**不成立。但注意 `Edge traversal: Yes`：这条规则允许 teredo/ISATAP 边缘穿越，
   在多网卡 + VPN 场景下可能让 Windows 把流量判给虚拟接口。
3. **`Protocol: Any` 是个隐患**：它不区分 38850/38860，工具**无法**通过这条规则判断
   「UDP 389xx 是不是也被放行了」——判定必须靠实际抓包或端口探测，不能靠规则表。

### 5.2 防火墙 profile 实测（异常）

```
> powershell -NoProfile -Command 'Get-NetFirewallProfile | Format-Table Name,Enabled -AutoSize'

Name    Enabled
----    -------
Domain     True
Private    False
Public     False
```

> **本机 Defender 防火墙的 Private 与 Public profile 全部处于关闭状态。**
> 这意味着：在这台机器上，「防火墙缺入站规则」**不是**当前故障原因，
> 而真正的风险转移到了「谁在替防火墙干活」（很可能就是 VeryKuai 之类VPN/加速器的驱动级过滤）。
> 工具必须把 profile 开关作为**独立检测项**，不能假设「规则存在 == 防火墙开着」。

### 5.3 配置与日志的真实位置

```
C:\ProgramData\Virtual Desktop\StreamerSettings.json     2196 B   ← 主设置
C:\ProgramData\Virtual Desktop\StreamerLog.txt            242 B
C:\ProgramData\Virtual Desktop\ServiceLog.txt            1920 B
C:\ProgramData\Virtual Desktop\updates.aiu                544 B
C:\Users\<user>\AppData\Roaming\Virtual Desktop\GameSettings.json   258 B
```

注册表：**三个候选路径全部不存在**（工具不要去读注册表）：

```
ABSENT: HKCU:\Software\Virtual Desktop
ABSENT: HKLM:\SOFTWARE\Virtual Desktop
ABSENT: HKLM:\SOFTWARE\WOW6432Node\Virtual Desktop
```

`StreamerSettings.json` 里与网络/发现直接相关的键（**这就是工具要读的真实参数键**）：

| 键 | 本机值 | 意义 |
|----|--------|------|
| `"ServerRotation"` | `2` | 远程连接的服务器区域轮换序号；**没有端口键** |
| `"ShowPairingRequests"` | `false` | 为 false 时不显示配对请求 UI（新头显可能因而被静默忽略） |
| `"DontWarnApps"` | `["NetworkProfile"]` | **VD 自己就有 `NetworkProfile` 这个告警类别** → 证实「网络配置文件」是 VD 官方承认的故障面 |
| `"LastConnectDate"` | `2026-10-04T00:00:00Z` | 上次成功连接时间；**判定「Streamer 实际工作过」的最强单一信号** |
| `"Accounts"` | `OculusQuest` / `Oculus` | 已配对头显的平台条目指纹 |

### 5.4 服务与日志里的真实故障（非网络，但排第一）

```
Name                            DisplayName              Status  StartType
----                            -----------              ------  ---------
VirtualDesktop.Service.exe      Virtual Desktop Service  Running Automatic
```

`ServiceLog.txt` 尾部反复出现（2026-09-07 / 09-14 / 09-18）：

```
ERROR|Service|Failed to start Streamer on active session (HRESULT -2147024891)|
System.UnauthorizedAccessException: The server process could not be started because
the configured identity is incorrect. Check the username and password.
```

> 服务在跑（Running），但**在活动会话里拉起 Streamer 进程反复失败**。
> 这种状态下 PC 上**没有** 38810-40 监听、**没有** 38860 广播 → 头必然「找不到 PC」，
> 而所有网络检测项都会显示正常。**所以「进程/服务存活」必须排在整个清单的第一位。**

### 5.5 本机网络概况（工具未来要判定的字段，实测长什么样）

```
Name                               Status         LinkSpeed InterfaceDescription
----                               ------         ---------- -------------------
Ethernet                           Up             1 Gbps      Realtek PCIe 2.5GbE Family Controller   ← ifIndex 13, 192.168.11.2/24, Private
Wi-Fi                               Disconnected   0 bps       Intel(R) Wi-Fi 7 BE200                  ← ifIndex 16, 169.254.51.4   ← APIPA
Wi-Fi 3 / Wi-Fi 4                   Not Present    0 bps       Intel(R) Wi-Fi 7 BE200
Ethernet 2                         Disconnected   100 Mbps    VeryKuai TAP Adapter                     ← ifIndex 18, 169.254.64.247 ← VPN TAP + APIPA
Bluetooth Network Connection       Disconnected   3 Mbps      Bluetooth Device (PAN)                  ← ifIndex 17, 169.254.189.235 ← APIPA
vEthernet (Default Switch)         Up             10 Gbps     Hyper-V Virtual Ethernet Adapter        ← ifIndex 22, 172.19.160.1/20
vEthernet (WSL (Hyper-V firewall)) Up             10 Gbps     Hyper-V Virtual Ethernet Adapter #2     ← ifIndex 66, 172.23.48.1/20
```

`Get-NetConnectionProfile` 只有一条，且是 **Private**：

```
InterfaceAlias InterfaceIndex NetworkCategory IPv4Connectivity
Ethernet                   13         Private         Internet
```

`MSFT_NetConnectionProfile.NetworkCategory` 数值枚举：**`0`=Public，`1`=Private，`2`=DomainAuthenticated**。
本机 Ethernet = `1` = Private。

**APIPA 网卡实测（5 个！）**：

```
InterfaceAlias               IPAddress       PrefixLength
Ethernet 2                   169.254.64.247  16
Local Area Connection* 5     169.254.5.122   16
Local Area Connection* 2     169.254.224.66  16
Bluetooth Network Connection 169.254.189.235 16
Wi-Fi                        169.254.51.4    16
```

> ⚠️ 注意 `Local Area Connection* 2 / * 5` 这两条**在 `Get-NetAdapter` 里根本不显示**
> （那里只有 7 条），只有 `Get-NetIPAddress` 才看得到。
> **工具必须用 `Get-NetIPAddress` 扫 APIPA，不能只扫 `Get-NetAdapter`，否则会漏掉这两块。**

**ICS**：`SharedAccess` 服务 = **Running**（Manual 启动类型）→ ICS 在本机是活跃的。

**第三方杀软**：只有 `Windows Defender`（`productState 401664`）。

`401664` 的字节拆解（实算）：`401664 = 0x062100`，即 高字节 `0x06` / 中字节 `0x21` / 低字节 `0x00`。
高字节 `0x06` 是 Microsoft 提供商标识；低字节 `0x00` 表示病毒库为最新。
中字节 `0x21` 的精确语义在公开文档里没有权威对照表，本轮**未验证**其「实时保护是否开启」的确切编码，
因此**工具不得依赖解码这个字段来判断防火墙/防病毒状态** —— 判断防火墙是否工作请直接用
`Get-NetFirewallProfile`（清单 #8，见 02 文档）。

这个字段可靠的用途只有一个：**`displayName` 的数量与名称**，用来区分
「只有 Defender」与「有第三方杀软接管」（第三方接管时 `displayName` 会出现其产品名）。

### 5.6 当前端口占用

```
Get-NetTCPConnection -State Listen | Where-Object LocalPort -in 38810,38820,38830,38840
→ （空）   当前 Streamer 进程没在跑，与 §5.4 的日志一致
Get-NetUDPEndpoint | Where-Object LocalPort -in 38850,38860
→ （空）
```

---

## 6. 给工具的三条落地结论

1. **判定「PC 侧发现就绪」的最强判据是抓包，不是读配置**：
   绑定 UDP 38850 + 周期收到 `udp.dstport==38860` 的零长广播 + TCP 38810-40 处于 Listen
   且 `OwningProcess` 是 `VirtualDesktop.Streamer.exe`。三条同时成立才算就绪。
2. **UDP 38850 是上一轮完全漏掉的核心端口**，任何只做 38810-40 的工具都会漏掉整个
   「头显发现不到 PC」失败族（因为那 4 个 TCP 端口只在**已经发现之后**才用得上）。
3. **38811/38821/38831/38841 不要用于同网段场景** —— 原因已更正：它们不是不存在，而是**远程中继专用**（见下方更正，§4.1）。


## 4.1 更正（2026-10-05）：这四个端口存在，此前是搜错了地方

**原结论**：38811/38821/38831/38841 在反编译源码里零命中，来源不明，工具不得使用。

**为什么错**：那次搜索是在 `analysis/apk_patch/extracted_assemblies/` 做的。
那 7 个 `VirtualDesktop.*.dll` **文件名与内容错位**——`VirtualDesktop.Net.dll` 的 AssemblyTitle
其实是 OpenTK。用错文件去搜，零命中是必然的，与端口存不存在无关。

（机制补充：XABA 名表整体偏移 **+11**，所以按名表命名会拿到错位的程序集。必须按 md5 定位。
正确的映射：`idx43`=OpenTK、`idx49`=`VirtualDesktop.Core.dll`、
**`idx54`=真正的 `VirtualDesktop.Net.dll`**、`idx60`=真正的 `Xenko.OpenXR.dll`。）

**正确结论**：这四个端口在真正的 `VirtualDesktop.Net.dll` 里，用作**远程中继**端口，
与同网段的局域网发现**无关**。判定依据是 Quest 侧选路的条件：
只有当 `computer.UdpEndPoint == null`（这台 PC 是从云注册表学来的，不是局域网发现扫到的）
才会走远程路径。

**对工具的实际影响**：

| | 旧口径 | 新口径 |
|---|---|---|
| 这四个端口存不存在 | 不存在，禁止使用 | 存在，但只服务远程 |
| 同网段要不要开 | —— | **不要开**，同网段走 38810/20/30/40 直连 |
| 工具常量表能不能出现 | 不得出现 | 可以出现，但**必须**只挂在远程分支上 |
| 清单第 22 项 | 「存在性校验，零命中即删」 | 改为：**按 `UdpEndPoint == null` 门控**，同网段场景不检查 |

**仍然没验证的**：那组中继端口在 `StreamerSettings.cs:3225` 的 `% 6` 展开成 38811–16 /
38821–26 / 38831–36 / 38841–46，其中 4 个基数来自硬编码字面量而非常量名。
且 PC 侧静态分析没有运行时 netstat 佐证。

**教训**：**「搜不到」在搜索对象本身可疑时，不构成结论。**
文件名对不上内容的时候，零命中只能证明搜错了文件。
