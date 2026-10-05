# 02 — 发现机制：头显到底怎么找到 PC，在哪儿断

本文件只回答一个问题：**PC↔Quest 的「发现」（discovery）这一段，代码里实际发生了什么，断点在哪。**
端口清单本身归 `01-endpoint-inventory.md`；补丁基线是否还需要这些端点归 `03-patched-baseline-deps.md`。
本文件与两位同事并行产出，**未读他们的文件**（写作时 `research/13-endpoints/` 下尚不存在其它文件）。

---

## 0. 证据基线与路径约定

反编译树（PC 侧，Owner 的只读树）：

```
VD-R/  = F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/-/
         混淆重命名根，304 个 `-NNN.cs` + 一个 `--NNN.cs` 家族
VD-S/  = F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/VirtualDesktop/
         保留原名的子目录（Streamer/ Net/ Interfaces/ …）
```

本轮实测该树规模（用于说明「grep 零命中」的分量，不是猜测）：

```
$ python -c "…rglob('*.cs')…"
12704 files / 2454457 lines
Counter({... 'VirtualDesktop.Streamer': 773, '-': 304 ...})
```

Quest 侧（APK）：

```
VD-Mob/ = F:/Project/VirtualDesktop/analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/
```

> ⚠️ **`F:/Project/VirtualDesktop/analysis/VirtualDesktop.Android_1.34.18.0/` 是空目录**（brief 里说
> 「verify, don't assume」——实读结果：只有一个 `modified_repack/` 子目录和两个 keystore 文件）。
> APK 里的 `VirtualDesktop.Net.dll` 我用 `ilspycmd` 实测反编译过，
> **140,576 行输出里只有 16 个 namespace，全部是 `OpenTK*`**，VD 自己的 `NetClient` / `NetMessagingClient`
> / 任何 UDP 代码**方法体已被 AOT 剥掉**：
>
> ```
> $ "C:/Users/dwgx1/.dotnet/tools/ilspycmd.exe" -o D:/tmp/vdnet \
>     F:/Project/VirtualDesktop/analysis/apk_patch/extracted_assemblies/VirtualDesktop.Net.dll
> $ grep -c "^namespace " D:/tmp/vdnet/VirtualDesktop.Net.decompiled.cs   → 16
> $ grep -n "UdpClient\|IPAddress.Broadcast\|EnableBroadcast\|Multicast" … → 0
> $ grep -n "38850\|38860\|38810" …                                        → 0（只有 8388608 浮点噪声）
> ```
>
> **因此：Quest 侧「谁向 38850 发什么」在本轮是 `查不到`，下面所有关于请求包结构的描述都是从 PC 侧收包分支反推的。**
> 这一点必须在 UI/文案上区分（代码事实 vs 推断）。

---

## 1. 机制定性：不是 mDNS、不是组播、不是子网扫描、不是云中继

| 候选机制 | 结论 | 证据 |
|---------|------|------|
| mDNS / DNS-SD（`.local`、DNS-SD ServiceType） | **否** | 全树 `DnsServiceDiscovery` = 0 命中；`.local` 的 22 处命中全是 `this.locals`（ProtoBuf/Compiler）。VD 自有的 `IPAddress.Broadcast` 只有两处：`VD-R/-.112.cs:209`、`VD-S/Streamer/ConnectionManager.cs:674` |
| 组播 multicast | **否** | `MulticastOption` / `JoinMulticastGroup` / `AddMembership` 全树 **0 命中**；`MulticastDelegate` 只是 C# 委托基类 |
| SSDP / UPnP **发现** | **否**（UPnP 只做端口映射，不做主机发现） | `VD-S/Net/UPnPManager.cs` 只被 `ConnectionManager.cs:242` / `:421` / `:442` / `:469` 调用于建删 TCP 38810-40 映射 |
| 云中继做「同网段」发现 | **否**（云只做远程与账号，详见 §6） | 云的入口是 `IComputerRegistry`（`VD-S/Interfaces/IComputerRegistry.cs:7`），实现类不在本反编译树里（`RegisterComputer3/4/5`、`BeginGetComputers2` 在全树只有**接口那一处**命中）；本地发现器 `VD-R/-.112.cs` 全文无任何出网调用 |
| LAN 子网扫描 / 端口扫全网段 | **否** | 全树无 `UdpPort`、无扫描器；`VD-R/-.112.cs` 只 `ReceiveAsync` 一个已绑定的 socket |
| **定向广播 + 单播回包** | **是，且仅此一种** | PC 侧唯一出向广播：`VD-S/Streamer/ConnectionManager.cs:674/681/684`（UDP 38860，0 字节）。PC 侧唯一与「发现」相关的收发：`VD-R/-.112.cs:330`（`UdpClient(Any:38850)`）与 `:451`（`Send(..., remoteEndPoint)` 单播） |

**一句话**：VD 的发现是 **「头显发起 → PC 被动应答」的单向请求/应答 + 一条无内容的局域网广播心跳**，
PC 从不主动宣告自己的地址，任何情况下都要头显先开口。

---

## 2. 发现协议本体：UDP 38850

### 2.1 类与生命周期

| 项 | 内容 | 证据 |
|----|------|------|
| 类 | `namespace \u008B` → `internal sealed class \u0002 : IDisposable`（混淆名，本文称 **LocalDiscoveryServer**） | `VD-R/-.112.cs:21`、`:23` |
| 静态初始化 | AES **Key** 32B、**IV** 16B（**硬编码在二进制里**）、两个 `IPEndPoint`、`DataContractSerializer(typeof(Computer))`、`HashSet<string>`（已见过的 RSA 公钥）、一个共享 `Aes`（`PaddingMode.None`） | `VD-R/-.112.cs:193-238` |
| Key | `32,71,236,94,194,34,85,255,165,172,187,150,6,104,106,57,57,62,244,114,75,174,237,9,48,36,239,82,57,98,205,80` | `VD-R/-.112.cs:197-203` |
| IV | `82,200,129,118,144,104,249,4,62,20,120,110,20,180,63,31` | `VD-R/-.112.cs:204-208` |
| 广播端点 `Broadcast:38850` | **声明了但全文件从未被读** —— 见 §7.1 的纠正 | 赋值 `VD-R/-.112.cs:209`；字段声明 `:594`；文件内 0 处读取 |
| 监听端点 `Any:38850` | 唯一实际使用 | 赋值 `:212`；使用 `:330` |
| 启动 | `\u0001(HashSet<PlatformAccessToken>, Computer)`：先停旧实例 → `Task.Factory.StartNew(..., LongRunning)` 起收包循环 | `VD-R/-.112.cs:264-289`（`:280` 停、`:281` 起） |
| 停止 / 释放 | `\u0002()` 置 null 后 Dispose；`\u0003()` = 停 + 释放 | `:291-320`、`:563-575` |
| 清空「已见 RSA 公钥」 | `public static \u0001()` | `:577-588` |

### 2.2 绑定与收包循环（`VD-R/-.112.cs:322-502`）

```
\u0002(tokens, computer)                                   :322
 ├─ :328  byte[] payload = this.\u0001(computer, out byte[48] keyMaterial)
 ├─ :330  this.\u0002 = new UdpClient(IPEndPoint(Any, 38850))   ← PC 唯一发现监听
 └─ :331  while (this.\u0002 != null) { ReceiveAsync().Result }  ← :335

    分支 A：len == 17                                    :339
      :343  payload[0..16) == computer.ConnectionID ?
      :347  Platform = (Platform)payload[16]
      :352  触发 (platform, computer.EncryptLocalTraffic) 事件
      :354  this.\u0002(); break;          ← 收到即关闭监听（NAT 打洞/唤醒路径）

    分支 B：len > 243                                    :365-368（<=243 直接 continue）
      :372  CreateDecryptor()  ← 用硬编码 Key/IV 解密请求
      :377-382 明文前 243 字节 = ASCII 的 RSA 公钥 XML（RSAParameters）
      :383-391 余下 = [可选 0x00 标记 + Platform 字节] + UTF8 token
      B1  token ∈ tokens                                :401
        :408-409  RSACryptoServiceProvider.FromXmlString → Encrypt(48B 会话密钥)
        :424-428 num3=128
        B2  token ∉ tokens                                :432
        :432  若该 RSA 公钥已见过 → continue（去重，不重复弹窗）
        :436-441 触发「配对请求」事件 → continue（**不发任何回包**）
      分支 C：回包                                      :445-452
        :445-447 array5 = [128B RSA 密文] ++ [AES(DataContract(Computer))]
        :451  u4.Send(array5, len, result.RemoteEndPoint)   ← 单播回请求方
    catch { }                                            :454-456   ← 空 catch，吞掉一切
```

### 2.3 回包载荷构造（`VD-R/-.112.cs:504-561`）

```
:509  Aes aes = Aes.Create()          ← 每次回包一把新的随机密钥
:513  PaddingMode.None
:514  out byte[48]                     ← 这 48 字节就是要 RSA 包给头显的会话材料
:515  aes.Key (32B) → [0..32)
:516  aes.IV  (16B) → [32..48)
:523  DataContractSerializer.WriteObject(Computer) 进 CryptoStream
:524  ToArray() → 密文
```

### 2.4 线上包形状（全部由上述代码推出，标注了出处）

| # | 方向 | 长度 | 布局 | 证据 |
|---|------|------|------|------|
| **A** | Quest → PC | **恰好 17** | `[0..16) ConnectionID`（16B，`Guid.ToByteArray()`）`+ [16] Platform`（1B） | `VD-R/-.112.cs:339`、`:343`、`:347` |
| **B** | Quest → PC | **> 243** | `AES-CBC(硬编码 Key/IV, PaddingMode.None)( [0..243) RSA 公钥 XML(ASCII)] + [0x00?][Platform?] + UTF8(token) )` | `VD-R/-.112.cs:197-208`、`:213`、`:231`、`:365-391` |
| **C** | PC → Quest（**单播**） | 128 + N | `[0..128) RSA(48B = AES Key‖IV)` `+ [128..) AES(新随机 Key/IV)(DataContract(Computer))` | `VD-R/-.112.cs:409`、`:424`、`:445-447`、`:504-524` |

**B 的 0x00 标记**：`if (array3[0] == 0) { platform = array3[1]; offset = 2; }`（`:385-390`）——
即「不带 Platform 字段」用 `0x00` 起头。

> AES **模式**在代码里从未显式设置：`-.112.cs:217`（共享 Aes）与 `:509`（每包新 Aes）都只调 `Aes.Create()`
> 然后设 `PaddingMode.None`（`:231` / `:513`），**模式取 `Aes.Create()` 的默认值**。本轮没有反编译到
> 基类初始化，`Mode` 是否被别处改成 `ECB`/`CFB` 属于 `查不到` —— 抓包复现时若 CBC 解不出，
> 先怀疑这一项，不要直接判「流量被篡改」。

**`Computer` 里到底带了什么（这就是「PC 告诉头显去哪连」）**：

| 字段 | 是否上线 | 证据 |
|------|---------|------|
| `ID`(string)、`Name`、`Description`、`OS`、`StreamerVersion` | ✅ `[DataMember]` | `VD-S/Interfaces/Computer.cs:47/80/113/146/179` |
| `PrivateAdapters` (`NetworkAdapter[]`) | ✅ | `Computer.cs:212` |
| `AllowRemoteConnections` / `EncryptLocalTraffic` / `EncryptRemoteTraffic` | ✅ | `Computer.cs:405/438/471` |
| `ConnectionID`(16B) / `IV` / `Key` | ✅ | `Computer.cs:523/556/589` |
| `Region` | ✅ | `Computer.cs:705` |
| `PrivateAddresses` | ❌ **无 `[DataMember]`，不上线** | `Computer.cs:244` |
| `IsOnSameNetwork` | ❌ **无 `[DataMember]`，不上线** | `Computer.cs:276` |

`NetworkAdapter` 的字段是 `IPAddresString` / `MACAddresString` / `IsWireless` / `IsGigabit`（全部 `[DataMember]`，
`VD-S/Interfaces/NetworkAdapter.cs:100-110`）——**所以头显是靠 PC 报上来的每块网卡的 IP + MAC 来算「同网」的**。

### 2.5 谁启动它：`LocalDiscoveryManager`（`VD-R/-/-.28.cs`，namespace `\u0004\u0002`）

```
\u0001(name, desc, os, NetworkAdapter[] adapters, PlatformAccessToken[] tokens)   :55
 ├─ :59-64  停并释放上一个实例
 ├─ :65-70  Aes.Create() + PaddingMode.None          ← 本地会话密钥
 ├─ :71     new Computer(id, name, desc, os, envVersion, Guid,
 │                      adapters,
 │                      false,                       ← allowRemoteConnections 硬编码 false
 │                      DynamicSettings.Default.IsLocalTrafficEncrypted,
 │                      true,                        ← encryptRemoteTraffic 硬编码 true
 │                      aes.IV, aes.Key)
 ├─ :72     new \u008B.\u0002()                       ← 建发现服务
 ├─ :73     += 配对请求事件  → :111 弹窗
 ├─ :75     += 17 字节事件   → :130
 └─ :76     \u0001(new HashSet<PlatformAccessToken>(tokens), computer)   ← 起收包循环
```

三个必须记住的细节：

1. **公告里的 `allowRemoteConnections` 恒为 `false`**（`VD-R/-/-.28.cs:71` 第 8 个实参是字面量 `false`）
   → 走 LAN 发现拿到记录的头显**不会**拿这条记录去试远程。
2. **`Computer.id` 来自机器身份，不是随机**：`\u0091\u0002.\u0002.\u0001()` 的 string 重载，
   其静态构造从注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProductId` 取值，
   读不到就退化成一个新的 `Guid().ToString()`，再经过 `\u001E\u0004.~\u009D\u000F(…, "F5=uNé01i52TZ;HPNrI%M")`
   处理（`VD-R/-/-.38.cs:14-45`、`:61-73`）。
   **`ConnectionID` 则相反**：由第 6 个实参 `\u008A\u0002.\u008F\u0007()` 的 Guid 转换而来
   （`VD-S/Interfaces/Computer.cs:23` `ConnectionID = connectionGuid.ToByteArray()`），
   即**每次构造 `Computer` 都会换一个**（`-/-.28.cs:65-71` 每次 Start 都重建 Aes 与 Computer）。
   该 Guid 的来源函数在树里 `查不到`（`\u008A\u0002` 命名空间未反编译），但 `Computer.cs:23` 保证它必为 16 字节。
3. **本地发现的身份凭据是 `PlatformAccessToken[]`**，来源是 `A_4` 实参。这一条决定了 §6 的答案。

---

## 3. 发现链路上的第二个动作：UDP 38860 零长广播

```
private static void \u0007()                                        VD-S/Streamer/ConnectionManager.cs:657
 ├─ :665  new UdpClient(AddressFamily.InterNetwork)
 ├─ :673  EnableBroadcast = true
 ├─ :674  new IPEndPoint(IPAddress.Broadcast, 38860)
 ├─ :681  udpClient.Send(Array.Empty<byte>(), 0, ep)     ← 0 字节负载
 ├─ :684  udpClient.Send(Array.Empty<byte>(), 0, ep)     ← 同上，连发两次
 └─ :697-699  catch { }                                  ← 空 catch，发不出去也不报
```

- **负载长度 = 0**。它只表达「这台机器上有 VD Streamer 在跑」，不含任何主机名/IP/标识。
- 触发者：`private static void \u0001(object A_0)`（`:908-927`）作为 `Timer` 回调，
  `Timer` 在静态构造里创建（`:1011` `new Timer(new TimerCallback(ConnectionManager.\u0001))`）。
- **`[未验证]` 广播周期**：回调体只启动异步状态机 `ConnectionManager.\u0008`，该状态机的 `MoveNext`
  **不在反编译树里**；`Change(...)` 的周期参数全树搜不到。
  **实测全树对 `ConnectionManager.\u0007()` 的调用只有 6 处命中**（`:208/:212/:214` 是同名事件处理器、
  `:933/:944/:955` 是同名状态机），**没有任何一处真正调用这个 38860 广播方法** → 调用点 `查不到`。
- 非发现、但同族的局域网广播：`VD-S/Net/WOLHelper.cs:43` `Broadcast:7`、`:47` `Broadcast:9`
  （Wake-on-LAN magic packet）。该类在全树**没有调用点**，只可能在未反编译的状态机里被调起 → `查不到`。

---

## 4. 「找到 PC」的完整时序（含配对闸门）

```
[PC]  Streamer 启动 → ConnectionManager.\u0001()                       ConnectionManager.cs:178
       ├─ :191  \u0086\u0002.\u0004<IComputerRegistry> 初始化（**云注册通道**，独立于本地发现）
       ├─ :200/206/211/213  listen TCP 38810 / 38820 / 38830 / 38840
       ├─ :242  !AllowRemoteConnections → UPnPManager 建映射
       ├─ :244  Task.Run(<>c.<>9.\u0001)          ← 状态机未反编译（含 :657 的 38860 广播，未证实）
       └─ :246/247/248  订阅 NetworkAddressChanged / NetworkAvailabilityChanged / PowerModeChanged

[PC]  LocalDiscoveryManager.\u0001(...)                              -/-.28.cs:55
       → new UdpClient(0.0.0.0:38850) 开始收                          -/.112.cs:330

[Quest] 发出 A(17B) 或 B(>243B)  —— 头显侧代码 查不到

[PC] 分支 A：ConnectionID 对上
       → 触发事件 → LocalDiscoveryManager:\u0002.\u0001(...)          -/-.28.cs:147
          ConnectionManager.\u0001(registry, null, platform, null, computer)
       → this.\u0002(); break;  **关闭 38850 监听**                    -/.112.cs:354

[PC] 分支 B1：token 已知
       → 单播回 C  → 头显拿到 IP/Key/IV → 走 TCP 38810 …

[PC] 分支 B2：token 未知
       → RSA 公钥去重（-.112.cs:432）
       → 触发配对请求事件                                            -/.112.cs:441
       → LocalDiscoveryManager:\u0001(object, \u0005\u0003.\u0003)      -/-.28.cs:111
          :117  if (StreamerSettings.Default.ShowPairingRequests)
          :119      Dispatcher → :190 MessageBox
                    "Allow {Platform} user '{name}' to access this computer?"
          :202      Accounts.\u0002(...)  加入 ;  :206 Save()
          :229      ShowPairingRequests = !flag
       → **continue，不回包**                                          -/.112.cs:442
```

**这条时序里最重要的一句话**：分支 B2 在用户点「Allow」之前**一个字节都不回**。
所以「头显列表里根本没有这台 PC」有两种完全不同的成因，必须分开判：
(a) 包根本没到 PC（网络问题）；(b) 包到了但 token 不认识（配对/账号问题）。

---

## 5. 断点：失败模式逐条挂证据

| # | 失败模式 | 机制 | 证据 |
|---|---------|------|------|
| **F1** | **访客网络 / AP 隔离** | 头显的请求（广播）到不了 PC。PC 侧**回包是单播**（`-.112.cs:451`），所以「回程」不是问题，**去程**才是 | 机制：`VD-R/-.112.cs:330/451`。语料：`research/09-failure-corpus/02-symptom-to-rootcause.md:67-68`（C1/C2 引 R10 开发者 "disable Guest networks and any AP isolation options"、R11 "settings on your router to isolate WiFi traffic from wired/ethernet traffic"）；`research/09-failure-corpus/01-symptom-corpus.md:25-26`（R08 官方 FAQ / R09 开发者 ggodin）。**本工具的检测点在头显侧（adb）**，PC 侧只能做辅助推断 |
| **F2** | **组播被过滤** | **不适用** —— VD 根本不用组播 | 全树 `MulticastOption`/`JoinMulticastGroup`/`AddMembership` = 0 命中（§1）。**不要为组播做任何检测项** |
| **F3** | **广播被过滤** | 38860 广播出不去 → 头显的「列表刷新」看不到这台 PC | 发送点 `ConnectionManager.cs:674/681/684`；失败静默 `:697-699` 空 catch。本机实测无第三方杀软（`research/02-network-diagnosis/01-ports-and-discovery.md:389`），但该机 Private/Public profile 防火墙全关（`:286-293`），风险由 VeryKuai TAP 驱动层接管（同文件 `:356`） |
| **F4** | **Streamer 根本没跑 / 服务拉不起进程** | 没有 `UdpClient(Any:38850)`，没有 38860 广播 | 监听建立 `ConnectionManager.cs:200-213` + `-/-.28.cs:72/76`。本机实测 `ServiceLog.txt` 反复 `Failed to start Streamer on active session (HRESULT -2147024891)` / `UnauthorizedAccessException`，`Get-NetTCPConnection`/`Get-NetUDPEndpoint` 对 38810-40/38850/38860 全部返回空 —— `research/02-network-diagnosis/01-ports-and-discovery.md:336-346`、`:400-407`。对应清单项 `research/02-network-diagnosis/02-pc-checklist.md:20-23` |
| **F5** | **Streamer 提权运行 → 配对弹窗用户看不见** | 分支 B2 的确认是 PC 端 **GUI MessageBox**（`-/-.28.cs:190-197`，`MessageBoxButton.YesNo`） | 机制事实 = 弹窗在 PC 上；「提权导致弹窗不可见」在本轮代码里 `查不到`（`[未验证]`）。可判定的是另一条：`ShowPairingRequests=false` 时连弹窗都不弹（`-/-.28.cs:117`）→ **静默不回包** |
| **F6** | **子网不匹配 / 跨 VLAN** | PC 在 38850 上**从不广播**（见 §7.1 纠正），所以完全依赖头显那一侧的发现请求能不能跨过来；而 `255.255.255.255` 类广播路由器默认不转发 | 语料 `research/09-failure-corpus/01-symptom-corpus.md:25-26`（R08/R09）、`:84`（R67 用户把「网络发现」误当 UPnP）。清单项 `02-pc-checklist.md:46-51` |
| **F7** | **端口被占（38850）** | `new UdpClient(Any:38850)` 抛 `SocketException`；它在 `Task.Factory.StartNew(..., LongRunning)` 里执行（`-.112.cs:281`），**返回的 Task 被丢弃**，异常变成 unobserved task exception → **进程不崩、日志没有、发现彻底死掉** | `VD-R/-.112.cs:281/330`。清单项 `02-pc-checklist.md:60` |
| **F8** | **防火墙规则专门挡发现** | 本机实测只有 **1 条** 规则：Program 作用域 + `Protocol: Any` + `Direction: In` + `Profiles: Domain,Private,Public` → 它同时覆盖入站 38850 与出站 38860 的**入站部分**；但 38860 是**出向**包，只受 `DefaultOutboundAction` 管，不受这条 In 规则管 | 实测输出见 `research/02-network-diagnosis/01-ports-and-discovery.md:253-271`；`DefaultOutboundAction` 检测项见 `02-pc-checklist.md:35`。另：**全树搜不到任何创建防火墙规则的代码**（`advfirewall`/`netsh`/`INetFw*`/`INetFwPolicy2`/`HNetCfg.FwMgr` 全部 0 命中）→ 规则是安装器或用户手工建的，工具不能假设它一定存在 |
| **F9** | **杀软 / 加速器的 WFP 驱动级过滤** | UDP 广播与单播回包被驱动层拦掉，`-.112.cs:454-456` 与 `ConnectionManager.cs:697-699` 两个空 catch 让它**完全静默** | 二进制自带文案实证（`F:/Project/VirtualDesktop/analysis/VirtualDesktop.Streamer/strings/C__Program_Files_Virtual_Desktop_Streamer_VirtualDesktop.Streamer.exe.strings.txt`）：`:140139` "If you experience issues connecting to your computer or launching games, try disabling or uninstalling any anti-virus, internet security, firewall or VPN software and restart your computer."；`:140218-140219` "Error establishing connection" / "Anti-virus or VPN software is preventing connections to your PC"（对应 `VD-S/Streamer/ConnectionManager.cs:765-776`）；`:140110` 360 Total Security "Will prevent connecting to your computer"；`:140118-140119` ZoneAlarm Firewall "Can prevent connecting to your computer. Allow 'Virtual Desktop Streamer'…" |
| **F10** | **虚拟网卡 / VPN 抢路由** | 38860 广播从哪块网卡出去由 Windows 路由决定；选错出口 = 广播到不了 LAN | 机制：`ConnectionManager.cs:665-684`（未指定源地址/接口）。本机实测命中：VeryKuai TAP + 2× Hyper-V + APIPA —— `research/02-network-diagnosis/01-ports-and-discovery.md:348-385`；清单项 `02-pc-checklist.md:49-50` |
| **F11** | **APIPA 169.254 段** | PC 侧 bind 的是 `Any`，所以仍会绑上；但头显在 169.254 段不可能路由到它 | 机制：`-.112.cs:330`。本机实测 5 个 APIPA 网卡 —— `research/02-network-diagnosis/01-ports-and-discovery.md:372-385`；清单项 `02-pc-checklist.md:46-47` |
| **F12** | **休眠 / 网卡掉线** | `PowerModeChanged` 在 Suspend/Resume 置位内部状态（`ConnectionManager.cs:876-898`），Resume 后必须重新握手（`:882-888`）；`NetworkAddressChanged`/`NetworkAvailabilityChanged` 走 **2 秒去抖后 `cts.Cancel()`**（`:246-247` → `:830` → `:844`）。断连时会**停掉本地发现**（`:399`） | 清单项 `02-pc-checklist.md:72-73`。⚠️ 停掉之后**谁负责重启**：`LocalDiscoveryManager.\u0001(5 参)` 在全树**没有调用点**（实测：`\u0004\u0002.\u0002.` 全树 26 处命中，其中 24 处在 `-/-.28.cs` 自身，`ConnectionManager.cs` 只有 `:399` 的 Stop 与 `:436` 的 IsRunning）→ **重启路径 `查不到`** |
| **F13** | **被伪造/重放的 17 字节包杀死监听** | 分支 A 一旦命中就 `this.\u0002(); break;`（`-.112.cs:354`），**本次 Streamer 运行期内的 38850 监听就此结束**，且无日志、无 UI | 机制：`VD-R/-.112.cs:343-355`。可达性：需要知道 16 字节 `ConnectionID`；它在每次 `LocalDiscoveryManager` 启动时重建（`-/-.28.cs:65-71` + `Computer.cs:23`），因此**盲猜不可行，但历史抓包里出现过的值可重放**。`[未验证]`：现实里是否发生过 |

---

## 6. 核心问题：**完全没有外网时，发现能不能成功？**

分两端回答，因为证据强度差别很大。

### 6.1 PC 端：**可以，纯局域网，与外网零耦合** ✅ 已证

| 论据 | 证据 |
|------|------|
| 发现服务器 `-.112.cs` 全文没有任何 HTTP/WCF/DNS/出网调用；唯一的 socket 操作是 `new UdpClient(Any:38850)` 与 `Send(..., remoteEndPoint)` | `VD-R/-.112.cs:330`、`:451`（全文 639 行，逐行读过） |
| 本地会话材料（AES Key/IV、`Computer` 记录）是**本地现生成**的，不来自服务器 | `-/-.28.cs:65-71`（`Aes.Create()` + `new Computer(...)`） |
| 云注册通道是**另一条独立通道**，它的失败不影响本地发现器 | `ConnectionManager.cs:191` `\u0086\u0002.\u0004<IComputerRegistry>.\u0001(...)`（云注册 client 在 Streamer 启动时初始化）；本地发现器是 `-/-.28.cs:72` 独立 `new` 出来的对象，两者没有任何字段/调用交叉；断连时本地发现被显式停掉（`:399`），云注册的重注册在 `:352-382`、`:457-470` 各自独立 |
| 云端地址只用于**远程** | `VD-R/-.92.cs:127` `https://america.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry`、`:133` `https://europe.vrdesktop.net/…`；`:21` `20.225.41.170`、`:23` `40.89.161.236`（区域由本机时钟偏移选出，`:25-51`）。这些常量**不出现在发现路径上** |

### 6.2 头显端：`查不到` —— 但语料里明确说它要出网 ⚠️

- 本轮**无法**从代码回答：`VirtualDesktop.Net.dll` 的 VD 自有类型在 AOT 后方法体已剥掉（§0 的 ilspycmd 实测输出）。
  Quest 侧唯一读到的网络文件是 `VD-Mob/NetworkManager.cs`，它有 `IsComputerRegistryOffline`
  （`:119`）、`ComputerRegistryTimeout = 12s`（`:397`）、`ComputerRegistryOfflineTimeout = 3s`（`:400`）、
  以及 `ServiceHelper<IComputerRegistry>.Initialize(HttpBinding.Default, NetHelper.GetServerUrl(1/2))`
  （`:145-146`）—— 说明**云注册表确实是 Quest 侧的主列表来源**，但「云失败后是否 fallback 到 UDP 广播」
  在 `RefreshComputersAsync` 的状态机里，**未反编译**。
- 语料侧的反证（**本仓库已有**）：
  - `research/09-failure-corpus/01-symptom-corpus.md:90` R73："**in order to find your PC VD has to talk to a
    server on the Internet from your Quest.** You could have port issues....counterintuitive."
  - 同文件 `:47` R30："When you first start up Virtual Desktop, it will do an **entitlement check** on VD's servers."
  - 同文件 `:46` R29："PC 断开互联网时 Streamer 图标变灰、doesn't say it's ready"。
- **结论（对 B9 检测项的直接建议）**：
  - B9「官方远端发现服务不可达」若要落地，**端点清单现在有了**，且只需测这三个：
    `america.vrdesktop.net:443`、`europe.vrdesktop.net:443`、`20.225.41.170` / `40.89.161.236`（TCP 443）。
    这修正了 `research/12-coverage-audit/02-next-additions.md:335-339` 的「端点清单查不到」结论。
  - 但**判定文案必须写成「影响出网环节」，不能写成「你的局域网发现坏了」**——因为 §6.1 已经证明 PC 侧的
    发现链路不需要外网。B9 真正能解释的是「首次配对 / 远程连接 / 换设备」失败，不是「同一网段找不到 PC」。
  - Owner 的补丁基线（无云认证）恰恰是把 6.2 的那一环拿掉了：**此时发现是否完全离线可用，
    取决于补丁后 Quest 拿什么 token 去喂分支 B1**。这属于 `03-patched-baseline-deps.md` 的范围，
    本文件只能给出一条 PC 侧的硬事实：**token 集合为空 ⇒ 所有头显都落进分支 B2 ⇒ 一个字节都不回**
    （`-.112.cs:401` / `:432-442`）。

---

## 7. 对既有报告的纠正（逐条给了反证）

### 7.1 `research/02-network-diagnosis/01-ports-and-discovery.md:31` 把 `-.112.cs:209` 称作「广播端点」——**该端点从未被使用**

`-.112.cs:209` 确实构造了 `new IPEndPoint(IPAddress.Broadcast, 38850)`，字段声明在 `:594`，
但**整个 639 行文件里没有任何一处读取它**（实测逐行枚举 `\u008B.\u0002` 的所有出现位置，
唯一使用端点字段的地方是 `:330` 的 `new UdpClient(\u008B.\u0002.\u0002)`，即 `Any:38850`）。

**纠正**：**PC 从不向 `255.255.255.255:38850` 发任何包**。PC 在发现上的唯一出向广播是 38860 的 0 字节能量脉冲
（`ConnectionManager.cs:674`）。因此「PC 不广播所以头显必须主动问」这条才是真实拓扑，
而「PC 广播自己的记录」是错的 —— 这直接影响工具的探测方向（必须**被动**抓 38860，不能主动等 38850 广播）。

### 7.2 `VIRTUAL_DESKTOP_APK_STREAMER_LINK_ANALYSIS.md:30` 把 ConnectionID/key/IV 说成全部来自云

原文：「Streamer 用平台账号证明向 `vrdesktop.net` 的 ComputerRegistry.svc/IComputerRegistry 注册电脑，
服务端返回 `RegistrationToken`，其中包含 `ConnectionID` 和 AES key/IV」。

- 云路径本身**成立**：`VD-S/Interfaces/RegistrationToken.cs:12` 构造函数 `(byte[] connectionID, byte[] iv,
  byte[] key, bool isValid)`，`:83-113` 的 `Aes` 属性用 `_key`/`_iv` 重建 AES（`PaddingMode.None`）；
  `IComputerRegistry.cs:15/18/21` 三个 `RegisterComputer3/4/5` 返回 `RegistrationToken`。
- **但那是远程路径**。本地发现路径在 `-/-.28.cs:65-71` **自己**生成 AES Key/IV 并自己构造 `Computer`，
  `ConnectionID` 来自本地 Guid（`Computer.cs:23`）。**两条路径的身份材料来源不同。**
- 结论不变但要写准：**不能说「发现依赖云注册」，也不能说「发现完全不经过云」** —— 准确说法是
  「本机回包用本地密钥；头显端是否还要用云换 token，代码里查不到」。

### 7.3 `research/02-network-diagnosis/01-ports-and-discovery.md:112`「38860 周期未验证」——本轮把范围缩小了

本轮确认了触发路径（`Timer` → 状态机 `\u0008`，`ConnectionManager.cs:1011/908-927`）并确认
**该广播方法在全树没有任何调用点**，所以周期**不只是「未抓到包」，而是「调用链在未反编译的状态机里」**。
工具实现仍按「60 秒内 ≥1 个包」判通过（不要写死周期）。

### 7.4 报告未提及、本轮新增的两条硬事实

1. **公告里 `AllowRemoteConnections` 恒为 `false`**（`-/-.28.cs:71`）→ 同网段发现到的记录不会被拿去试远程。
2. **收包循环整体被空 catch 包住**（`-.112.cs:454-456`）**且 Task 被丢弃**（`:281`）→ 任何发现期异常
   都是**零日志零 UI** 的静默死亡。这是「PC 明明开着但头显找不到、且所有网络检测项全绿」的机制根源。

---

## 8. 本轮明确「查不到」的清单（供后续接手的人直接省下时间）

| 缺口 | 为什么查不到 |
|------|------------|
| Quest 侧发给 38850 的请求包构造代码 | `VirtualDesktop.Net.dll` 的 VD 自有类型在 AOT 后方法体已剥掉（§0 ilspycmd 实测） |
| Quest 侧是广播还是单播请求、请求周期、重试次数 | 同上 |
| Quest 端云注册失败后是否 fallback 到本地发现 | `VD-Mob/NetworkManager.cs` 的 `RefreshComputersAsync` / `GetComputersAsync` 只是状态机 stub（`:151-161`、`:220-229`），`MoveNext` 未反编译 |
| UDP 38860 广播的周期与调用点 | `ConnectionManager.\u0008` 状态机未反编译；`\u0007()` 在全树无调用点（§7.3） |
| `WOLHelper` 的调用点 | 全树只有定义，无调用 |
| `LocalDiscoveryManager.\u0001(5 参)` 的调用点（即「谁决定什么时候开始发现」） | 全树 26 处 `\u0004\u0002.\u0002.` 命中里没有它（§5 F12） |
| `IComputerRegistry` 的具体实现类（云注册客户端） | `RegisterComputer3/4/5`、`BeginGetComputers2` 全树只在接口里出现一次 |
| `ConnectionID` 的 Guid 来源（是否每次随机） | `\u008A\u0002` 命名空间未反编译；但 `Computer.cs:23` 保证了它一定是 16 字节 |
| 断连后本地发现是否会被自动重启 | 同 F12 |
| 「提权导致配对弹窗不可见」 | 代码里没有任何提权/会话相关分支；`-/-.28.cs:119` 只做 `Dispatcher.BeginInvoke` |

---

## 9. 自检：本轮实跑过的命令

```
$ ls "F:/Project/VirtualDesktop/analysis/VirtualDesktop.Android_1.34.18.0/"
→ 只有 modified_repack/（空目录，brief 里的路径不含反编译源码）

$ "C:/Users/dwgx1/.dotnet/tools/ilspycmd.exe" -o D:/tmp/vdnet …/VirtualDesktop.Net.dll
$ grep -c "^namespace " D:/tmp/vdnet/VirtualDesktop.Net.decompiled.cs      → 16（全部 OpenTK*）
$ grep -n "UdpClient\|IPAddress.Broadcast\|EnableBroadcast" …             → 0
$ grep -n "38850\|38860\|38810" …                                          → 0

$ python …rglob("*.cs") 于 VD-R 全树                                        → 12704 文件 / 2454457 行
$ grep -rln "38850" 于 VD-R                                                 → 仅 ./-.112.cs
$ grep -rlnF '\u008B' 于 VD-R                                               → 调用方集中在 -/-.28.cs
$ grep -c 'MulticastOption|JoinMulticastGroup|AddMembership|DnsServiceDiscovery' → 全 0
$ grep -c 'advfirewall|netsh|INetFw|HNetCfg.FwMgr|INetFwPolicy2'          → 全 0
$ grep -n "F5=u" 于 VD-R                                                    → 仅 -/-.38.cs:14
```

未跑（本轮无法跑）：任何需要 Quest 在场的抓包验证、需要管理员的 `pktmon` 实跑、
以及任何对 APK 的运行时验证（头显当前 adb 不可达，符合 brief 的静态切片约定）。

## 可自动化的检测项

判据栏写的是**实测可得**的事实，不是「跑一下看看」。每条都注明让它可判定的那份证据。

| # | id | 检查什么 | 具体命令 / 读什么 | 判据 | 证据（为什么可判定） |
|---|----|---------|------------------|------|-------------------|
| D1 | `disc-udp-listen` | **UDP 38850 是否被 Streamer 绑在 0.0.0.0** | `Get-NetUDPEndpoint -ErrorAction SilentlyContinue \| Where-Object {$_.LocalPort -eq 38850} \| Select-Object LocalAddress,LocalPort,OwningProcess` | 有 1 条、`LocalAddress = 0.0.0.0`、`OwningProcess` = `VirtualDesktop.Streamer.exe` 的 PID | 绑定代码 `VD-R/-.112.cs:212/330`。**这是发现是否可能工作的第一必要条件**；`LocalAddress` 不是 `0.0.0.0` 或 PID 不是 Streamer 即判失败 |
| D2 | `disc-38850-owner` | 38850 被谁占了 | 同上命令取 `OwningProcess` → `Get-Process -Id <id> \| Select-Object ProcessName,Path` | PID == D1 的 Streamer PID；否则 = 被抢占 | `new UdpClient(Any:38850)` 独占且异常被静默吞掉（`-.112.cs:281/330` + `:454-456`），所以「占用」不会有任何日志 —— **只能靠端口表判定** |
| D3 | `disc-38850-loopback` | 本机自测：向 `127.0.0.1:38850` 发一个 17 字节哑包，看有没有异常反应 | 工具内发 UDP（`127.0.0.1:38850`，17 字节全 0） | **预期「无回包、无异常」= 正常**；收到任何单播回包 = 异常 | 17 字节分支只在 ConnectionID 匹配时才回（`-.112.cs:343-355`），全 0 必然不匹配 → 空转。作用是**把「socket 存在」和「协议活着」分开**：`[未验证]` 需实跑确认 Windows 上不会因该包产生 ICMP 噪音 |
| D4 | `beacon-38860` | **PC 是否真的在发 38860 零长广播**（需要管理员 + 抓包） | `pktmon start --capture --comp nics --pkt-size 0 --file-name "$env:TEMP\vd.etl"`，等 60s，`pktmon stop` 后 `pktmon etl2txt` 过滤 `udp.dstport == 38860` | 60 秒内 **≥1 个** `len=0` 的 `udp dstport 38860` | 发送代码 `ConnectionManager.cs:665-684`（`EnableBroadcast=true`、`Array.Empty<byte>()`、连续两次 Send）。**这是唯一一个「不需要任何凭据、纯被动」就能确认 PC 侧发现心跳活着的信号**，也是本文件认为最值得做成主检测项的一条 |
| D5 | `beacon-src-iface` | 38860 广播是从哪块网卡出去的 | `pktmon etl2txt` 后看 38860 包的源 IP；与 `Get-NetIPInterface -AddressFamily IPv4 \| Sort-Object InterfaceMetric` 对照 | 源 IP 属于物理 LAN 网卡，且该网卡 `InterfaceMetric` 最小 | `ConnectionManager.cs:665-684` 不指定源地址/接口 → 出口由 Windows 路由决定。本机实测有 VeryKui TAP + 2× Hyper-V（`research/02-network-diagnosis/01-ports-and-discovery.md:348-360`） |
| D6 | `disc-tcp-listen` | 38810-40 是否在 Listen（发现成功后才会用到，但缺席说明 Streamer 初始化就断了） | `Get-NetTCPConnection -State Listen \| Where-Object {$_.LocalPort -in 38810,38820,38830,38840}` | 4 条齐全且 PID 相同 | `ConnectionManager.cs:200/206/211/213` |
| D7 | `cfg-showpairing` | **`ShowPairingRequests` 是否为 false**（纯配置导致的发现失败，网络检测全绿也查不出） | `Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw \| ConvertFrom-Json \| Select-Object ShowPairingRequests` | `= true`（或用户明确知道后果） | `-/-.28.cs:117` 只有 true 才弹窗；`-/.112.cs:432-442` 弹窗路径**不回包**；`StreamerSettings.cs:2121`（属性）/`:2154`（置 true 时清空已见 RSA 公钥）。**这是「包到了但 PC 不理」和「包没到」的分水岭** |
| D8 | `cfg-accounts` | **`Accounts` 数组是否为空** | 同 D7 的命令加 `-ExpandProperty Accounts` | 至少 1 个已配对账号 | `-/-.28.cs:76` 把 `PlatformAccessToken[]` 传进发现器；`-/.112.cs:401` 用它做判定。**Accounts 为空 ⇒ 任何头显都落进 B2 ⇒ 一个字节都不回**。本机实测值 `OculusQuest`/`Oculus`（`research/02-network-diagnosis/01-ports-and-discovery.md:326`） |
| D9 | `cfg-lastconnect` | 这台 PC 上次真正连成功是什么时候 | 同 D7，`Select-Object LastConnectDate` | 非空且近期 | `research/02-network-diagnosis/01-ports-and-discovery.md:325` 已把它定为「Streamer 实际工作过」的最强单一信号；本轮补一条机制支撑：它证明走通过 **B1（token 已知 → 单播回 C）** 那条路 |
| D10 | `svc-fail-to-launch` | Streamer 进程是否真的在跑（不是「服务在跑」） | `Get-Process VirtualDesktop.Streamer`；`Get-Content "C:\ProgramData\Virtual Desktop\ServiceLog.txt" -Tail 20` | 进程在 **且** 日志无 `Failed to start Streamer on active session` | 没有进程 ⇒ `-/-.28.cs:72` 的 `new \u008B.\u0002()` 从未执行、`:330` 的 socket 从未创建。本机实测该错误反复出现（`research/02-network-diagnosis/01-ports-and-discovery.md:336-346`） |
| D11 | `fw-inbound-any` | 防火墙里有没有那条 Program 作用域、`Protocol: Any`、`Direction: In` 的 VD 规则 | `netsh advfirewall firewall show rule name=all \| findstr /i /c:"virtual desktop"`；再 `... name="Virtual Desktop Streamer" verbose` | ≥1 条，且 `Direction=In` / `Protocol=Any` / `Action=Allow` | 本机实测形态见 `research/02-network-diagnosis/01-ports-and-discovery.md:253-271`。**必须说明**：这条规则是 In 方向，**不覆盖 38860 的出向发送**（出向靠 `DefaultOutboundAction`，见 D12）；且全树无任何创建规则的代码（`advfirewall`/`INetFw*` 0 命中），所以规则缺失是常态而非异常 |
| D12 | `fw-outbound-policy` | 默认出站是否 Allow | `Get-NetFirewallProfile \| Format-Table Name,Enabled,DefaultOutboundAction` | `DefaultOutboundAction` ∈ {NotConfigured, Allow} | 38860 广播与 38850 单播回包都是**出向**（`ConnectionManager.cs:681`、`-.112.cs:451`），不受 D11 的 In 规则保护。清单项 `research/02-network-diagnosis/02-pc-checklist.md:35` |
| D13 | `fw-profile-state` | 三个 profile 分别开没开 | 同 D12 | 至少 Private 或 Public = True | 本机实测 Private/Public 全 False（`research/02-network-diagnosis/01-ports-and-discovery.md:286-293`）—— 意味着「规则存在」不等于「有人在按规则过滤」，风险转移到了未知方 |
| D14 | `route-metric` | 物理 LAN 网卡的 `InterfaceMetric` 是否最小 | `Get-NetIPInterface -AddressFamily IPv4 \| Sort-Object InterfaceMetric` | 物理 LAN < 所有虚拟/VPN 网卡 | 直接决定 D4 的广播出口。清单项 `02-pc-checklist.md:50` |
| D15 | `apipa-nics` | 有多少网卡落在 169.254/16 | `Get-NetIPAddress -AddressFamily IPv4 \| Where-Object {$_.IPAddress -like "169.254.*"}` | 排除 `Local Area Connection*` 后为空 | 头显不可能路由到 169.254 段的 PC。⚠️ 必须排除 `Local Area Connection*` —— 本机实测这 2 条在 `Get-NetAdapter` 里根本看不到（`research/02-network-diagnosis/01-ports-and-discovery.md:372-385`） |
| D16 | `vd-cloud-endpoints` | 四个云端点（2 主机名 + 2 IP）的 DNS + TCP 443 | `Resolve-DnsName america.vrdesktop.net, europe.vrdesktop.net`；`Test-NetConnection <name> -Port 443`；再测 `20.225.41.170` / `40.89.161.236` | 解析成功 + `TcpTestSucceeded=True` → Pass；否则 Warn（**不 Block**） | 端点来源：`VD-R/-.92.cs:21/23/127/133`。**文案必须写「只影响需要出网的环节（首次配对 / 远程连接 / 换设备），不代表同网段局域网发现坏了」** —— 依据是本文 §6.1（PC 侧发现无出网调用）与 §6.2（头显侧 `查不到`）。R08/R84 见 `research/09-failure-corpus/01-symptom-corpus.md:25/101`；「首次启动要出网做 entitlement check」见同文件 `:47`（R30）与 `:90`（R73） |
| D17 | `streamer-alive` | Streamer 进程启动时刻 vs 现在 | `Get-Process VirtualDesktop.Streamer \| Select-Object Id,StartTime` | 启动后 ≥ 数秒（避免刚拉起就查） | 关联 F13：17 字节分支命中会**永久关掉本运行期内的 38850 监听**（`-.112.cs:354`），而重启路径 `查不到`（§5 F12）。因此「Streamer 已连续运行很久 + D1 显示 38850 不在」是一个**独立可判的异常态**，文案应提示重启 Streamer |
| D18 | `wol-7-9` | 是否出现 UDP 7/9 广播 | 复用 D4 的 etl，过滤 `udp.dstport in (7,9)` | **默认不检查**（非发现必需） | `VD-S/Net/WOLHelper.cs:43/47`；但该类**全树无调用点** → 是否真的会发 `查不到`。**只作观测项，不做告警** |

### 明确**不要**做的检测项（避免假通过/假告警）

| 别做 | 原因 |
|------|------|
| mDNS / 组播相关任何检查 | 全树零命中（§1 F2） |
| 「UDP 38850 有广播包」当就绪判据 | PC 从不广播 38850（§7.1）—— 这个判据会永远失败 |
| 用 `38811/38821/38831/38841` | 全树零命中，来源不明（`research/02-network-diagnosis/01-ports-and-discovery.md:234`） |
| 主动向 38850 发「探测包」期待拿到 PC 记录 | 分支 B1 需要合法 `PlatformAccessToken`；分支 B2 一律不回包（`-.112.cs:401/432-442`）。**第三方工具拿不到回包，这不是 bug** |
| 把 38860 周期写死 | 周期 `查不到`（§7.3）；按「60 秒内 ≥1 包」判 |
| 让工具自动把防火墙 profile 改成 Private / 自动关防火墙 | 属安全姿态变更；清单项 `02-pc-checklist.md:48/34` 已标注风险等级 |
| 让工具杀占用 38850 的进程 | 硬地板禁止不明进程终止；`02-pc-checklist.md:59` 已明确 |
