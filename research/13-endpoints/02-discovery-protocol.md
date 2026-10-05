# 02 — 发现机制：头显到底怎么找到 PC，在哪儿断

本文件只回答一个问题：**PC↔Quest 的「发现」（discovery）这一段，代码里实际发生了什么，断点在哪。**
端口清单本身归 `01-endpoint-inventory.md`；补丁基线是否还需要这些端点归 `03-patched-baseline-deps.md`。
本文件与两位同事并行产出。

> **修订记录（重要）**：本文第一版把 Quest 侧判成「方法体被 AOT 剥掉 → `查不到`」，
> **这个结论是错的**，已在本版全面推翻并用双侧实测证据替换。错因与证据见 §0.2。

---

## 0. 证据基线与路径约定

### 0.1 两棵树

```
VD-R/  = F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/-/
         PC 侧，混淆重命名根，304 个 `-NNN.cs` + 一个 `--NNN.cs` 家族
VD-S/  = .../VirtualDesktop.Streamer/VirtualDesktop/
         PC 侧，保留原名的子目录（Streamer/ Net/ Interfaces/ …）

VD-Q/  = %TEMP%\vd_ep_01\vd\VirtualDesktop.Net\VirtualDesktop.Net\      ← Quest 侧网络栈
         %TEMP%\vd_ep_01\vd\VirtualDesktop.Mobile\VirtualDesktop.Mobile\  ← Quest 侧编排
         %TEMP%\vd_ep_01\vd\VirtualDesktop.Interfaces\VirtualDesktop.Interfaces\
         （由同批 worker 从 APK 的 XABA 汇编 blob 提取，本轮已逐文件复核关键行）
```

`%TEMP%` = `C:\Users\dwgx1\AppData\Local\Temp`。该目录是**临时目录**，
若后续接手的人读不到，请按 §0.2 的方法自行重建，或只依赖本文件已引用的 `file:line`。

### 0.2 ⚠️ 第一版的错误，和它的反证

**第一版写的话**：「APK 里的 `VirtualDesktop.Net.dll` 我用 `ilspycmd` 实测反编译过，140,576 行输出里只有
16 个 namespace，全部是 `OpenTK*`，VD 自己的 `NetClient` / `NetMessagingClient` / 任何 UDP 代码方法体已被 AOT 剥掉。」

**反证**：`F:/Project/VirtualDesktop/analysis/apk_patch/extracted_assemblies/` 下那 7 个
`VirtualDesktop.*.dll` **文件名与内容完全不对应** —— 它们不是 VD 的程序集：

| 文件名 | 实际 `AssemblyTitle` |
|--------|---------------------|
| `VirtualDesktop.Net.dll` | **OpenTK** |
| `VirtualDesktop.Android.dll` | Xenko.Core.IO |
| `VirtualDesktop.Core.dll` | Xamarin.Google.Android.Play.Integrity |
| `VirtualDesktop.Interfaces.dll` | Xamarin.GooglePlayServices.Tasks |
| `VirtualDesktop.Mobile.Shared.dll` | Oculus.Platform |
| `VirtualDesktop.Mobile.dll` | ZString |
| `VirtualDesktop.WCF.dll` | PFD.Android |

（同一条错误也污染了 `analysis/apk_patch/patched_assemblies/VirtualDesktop.Net.dll` —— 2,126,336 字节，
与上面那个 OpenTK 文件同大小。）

**⚠️ 附带的文件名错位（已定位到机制，2026-10-05 补）**：XABA 容器里程序集**名表在 0x64D8 的偏移是错的，
整体偏移了 +11 个条目**。以 md5 为准的实测映射：

| 条目索引 | 大小 | 真实身份（md5 实证） |
|---------|------|---------------------|
| **idx43** | 2,126,336 | OpenTK（不是 VirtualDesktop.Net！） |
| **idx49** | 40,448 | **`VirtualDesktop.Core.dll`**（md5 `27bf89c9e588afe47f82094e2afab477`），含 `VirtualDesktop.Core.CurrentProcess` |
| **idx52** | 529,408 | `Xenko.dll` |
| **idx54** | 66,048 | **真正的 `VirtualDesktop.Net.dll`**（含 `VirtualDesktop.Net.ComputerDiscoveryClient`） |
| **idx60** | 161,792 | 真正的 `Xenko.OpenXR.dll`（md5 `c1c7afa880cb97bc1e66a257648b8a3d`） |

按此映射去读名表会得到「#43 = VirtualDesktop.Net.dll」和「#49 = Xenko.OpenXR.dll」——两条都错，
**正好各差 +11**。因此：

1. `analysis/apk_patch/extracted_assemblies/` 那 7 个 `VirtualDesktop.*.dll` 不可信（`VirtualDesktop.Net.dll` 实为 idx43 的 OpenTK）；
2. `binary_patch.py:256` 把 **idx49（`VirtualDesktop.Core`）** 的补丁结果写成
   `patched_assemblies\Xenko.OpenXR.dll`（实测 40,448 字节，与 `extracted` 的 idx49 仅差 14 字节）；
   而真正的 `Xenko.OpenXR.dll` 是 **idx60**，**根本不在 `patched_assemblies/` 里**。
   ⇒ **补丁程序集被贴错了文件名**，任何按文件名消费 `patched_assemblies/` 的下游都会拿到错的东西。

**正确来源**：托管程序集在 APK 的 `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so`
（ELF + XABA 容器 + XALZ/LZ4 压缩条目）。实测容器参数：

```
XABA        @ 0x4000
entry_count = u32@xaba+8   = 181
index_size  = u32@xaba+16  = 4344
desc_start  = xaba + 20 + index_size = 0x510C
描述符 28 字节：desc_start + i*28，data_sz = u32@desc+8
条目头 12 字节：idx=u32@pos+4 / uncompressed_size=u32@pos+8
程序集名表 @ desc_start + entry_count*28 = 0x64D8 （uint32 len + ascii 名）
解压：lz4.block.decompress(data[pos+12 : pos+12+data_sz-12], uncompressed_size=uncomp)
```

（XABA 解析逻辑可复用 `F:/Project/VirtualDesktop/analysis/apk_patch/binary_patch.py:63-91`。）

从 blob 解出的 `VirtualDesktop.Net.dll` 是 66,048 字节、65 个类型、**0 个 OpenTK**，
`ComputerDiscoveryClient` / `NetClient` / `NetMessagingClient` 全在，**方法体完整**。

**结论**：Quest 侧**不是** `查不到`，而是之前找错了文件。下面 §2–§5 的协议部分因此从「PC 侧反推」
升级为「双侧实测」，§7 的「无外网能否发现」也因此有了确定答案。

### 0.3 其它路径说明

- brief 里提到的 `F:/Project/VirtualDesktop/analysis/VirtualDesktop.Android_1.34.18.0/` 实读为**空目录**
  （只有 `modified_repack/` 和两个 keystore），不含反编译源码。
- PC 侧反编译树规模（用于说明「grep 零命中」的分量）：**12,704 个 `.cs` / 2,454,457 行**，
  其中 `VirtualDesktop.Streamer/` 子目录 773 个、`-` 混淆根 304 个。

---

## 1. 机制定性：不是 mDNS、不是组播、不是 SSDP、不是子网扫描、不是云中继

| 候选机制 | 结论 | 证据 |
|---------|------|------|
| mDNS / DNS-SD（`.local`、DNS-SD ServiceType） | **否** | 两棵树全树 `DnsServiceDiscovery` = 0 命中；`.local` 的 22 处命中全是 `this.locals`（ProtoBuf/Compiler）。VD 自有的 `IPAddress.Broadcast` 只有 4 处，全是 38850/38860 |
| 组播 multicast | **否** | 两棵树全树 `MulticastOption` / `JoinMulticastGroup` / `AddMembership` **0 命中**；`MulticastDelegate` 只是 C# 委托基类 |
| SSDP / UPnP **发现** | **否**（UPnP 只做端口映射） | `VD-S/Net/UPnPManager.cs` 只被 `ConnectionManager.cs:242/421/442/469` 调用于建删 TCP 38810-40 映射 |
| 云中继做「同网段」发现 | **否** | 云只登记 PC；发现请求由 Quest 自己广播（`VD-Q/…/ComputerDiscoveryClient.cs:98`） |
| LAN 子网扫描 / 全网段端口扫 | **否** | 两棵树无 `UdpPort`、无扫描器 |
| **定向广播 + 单播回包** | **是，且仅此一种** | 广播方 = **Quest**：`ComputerDiscoveryClient.cs:63`（`Broadcast:38850`）+ `:92`（`EnableBroadcast=true`）+ `:98`（`Send`）。回包方 = **PC**，且**单播**：`VD-R/-.112.cs:451` |
| **PC 侧另有一条零长广播**（不是发现协议本体） | **是** | `ConnectionManager.cs:674/681/684`：UDP 38860，0 字节负载 |

**一句话**：**头显广播、PC 单播应答**。PC 从不在 38850 上发任何包，只回单播；
PC 侧唯一的广播是一条不含任何信息的 38860「我在线」脉冲。

---

## 2. PC 侧：UDP 38850 发现/配对服务

### 2.1 类与静态材料

| 项 | 内容 | 证据 |
|----|------|------|
| 类 | `namespace \u008B` → `internal sealed class \u0002 : IDisposable`（混淆名，本文称 **LocalDiscoveryServer**） | `VD-R/-.112.cs:21`、`:23` |
| AES Key（32B） | `32,71,236,94,194,34,85,255,165,172,187,150,6,104,106,57,57,62,244,114,75,174,237,9,48,36,239,82,57,98,205,80` | `VD-R/-.112.cs:197-203`；**Quest 侧逐字节相同**：`ComputerDiscoveryClient.cs:51-57` |
| AES IV（16B） | `82,200,129,118,144,104,249,4,62,20,120,110,20,180,63,31` | `VD-R/-.112.cs:204-208`；Quest 侧相同：`:58-62` |
| **两端硬编码同一对 Key/IV** | → 任何第三方都能解密/伪造发现请求 | 上面两行的并置 |
| 广播端点 `Broadcast:38850` | **声明了但全文件从未被读**（见 §8.1 纠正） | 赋值 `VD-R/-.112.cs:209`；字段 `:594`；文件内 0 处读取 |
| 监听端点 `Any:38850` | PC 侧唯一实际使用 | 赋值 `:212`；使用 `:330` |
| `DataContractSerializer(typeof(Computer))` | 两端一致 | `VD-R/-.112.cs:213`；`ComputerDiscoveryClient.cs:65` |
| `HashSet<string>` | PC = 已见过的 RSA 公钥（去重）；Quest = `_pairingRequests` | `VD-R/-.112.cs:216`、`:432`；`ComputerDiscoveryClient.cs:30/66` |
| 共享 `Aes` | 两端都 `PaddingMode.None` | `VD-R/-.112.cs:217-231`；`ComputerDiscoveryClient.cs:67-70` |

### 2.2 生命周期

| 动作 | 证据 |
|------|------|
| 启动 `\u0001(HashSet<PlatformAccessToken>, Computer)`：先停旧实例 → `Task.Factory.StartNew(..., LongRunning)` 起收包循环 | `VD-R/-.112.cs:264-289`（`:280` 停、`:281` 起） |
| 停止 `\u0002()` / 释放 `\u0003()` | `:291-320`、`:563-575` |
| 清空「已见 RSA 公钥」`public static \u0001()` | `:577-588`（由 `StreamerSettings.cs:2154` 在 `ShowPairingRequests` 置 true 时调用） |

### 2.3 收包循环（`VD-R/-.112.cs:322-502`）

```
\u0002(tokens, computer)                                   :322
 ├─ :328  byte[] payload = this.\u0001(computer, out byte[48] keyMaterial)
 ├─ :330  this.\u0002 = new UdpClient(IPEndPoint(Any, 38850))
 └─ :331  while (this.\u0002 != null) { ReceiveAsync().Result }  ← :335

    分支 A：len == 17                                    :339
      :343  payload[0..16) == computer.ConnectionID ?
      :347  Platform = (Platform)payload[16]
      :352  触发 (platform, computer.EncryptLocalTraffic) 事件
      :354  this.\u0002(); break;          ← 收到即关闭 38850 监听

    分支 B：len > 243                                    :365-368（<=243 直接 continue）
      :372  CreateDecryptor()  ← 用硬编码 Key/IV 解密请求
      :377-382 明文前 243 字节 = ASCII 的 RSA 公钥 XML（RSAParameters）
      :383-391 余下 = [0x00 标记 + Platform] + UTF8 token
      B1  token ∈ tokens                                :401
        :408-409  RSACryptoServiceProvider.FromXmlString → Encrypt(48B 会话密钥)
        分支 C：回包 :445-447 array5 = [128B RSA 密文] ++ [AES(DataContract(Computer))]
                :451  u4.Send(array5, len, result.RemoteEndPoint)   ← 单播
      B2  token ∉ tokens                                :432
        :432  RSA 公钥已见过 → continue（去重，不重复弹窗）
        :436-441 触发「配对请求」事件 → continue（**不回包**）
    catch { }                                            :454-456   ← 空 catch
```

### 2.4 PC 侧出向记录构造（`VD-R/-.112.cs:504-561`）

```
:509  Aes.Create()        ← 每次回包一把新的随机密钥
:513  PaddingMode.None
:514  out byte[48]         ← 要 RSA 包给头显的会话材料
:515  aes.Key (32B) → [0..32)
:516  aes.IV  (16B) → [32..48)
:523  DataContractSerializer 序列化 Computer 进 CryptoStream
:524  ToArray() → 密文
```

### 2.5 公告载荷 `Computer` 的字段

| 字段 | 上线？ | 证据 |
|------|-------|------|
| `ID` / `Name` / `Description` / `OS` / `StreamerVersion` | ✅ `[DataMember]` | `VD-S/Interfaces/Computer.cs:47/80/113/146/179` |
| `PrivateAdapters` (`NetworkAdapter[]`) | ✅ | `Computer.cs:212` |
| `AllowRemoteConnections` / `EncryptLocalTraffic` / `EncryptRemoteTraffic` | ✅ | `Computer.cs:405/438/471` |
| `ConnectionID`(16B) / `IV` / `Key` | ✅ | `Computer.cs:523/556/589` |
| `Region` | ✅（由 Quest 侧在合表时打上） | `Computer.cs:705`；赋值 `VD-Q/…/NetworkManager.cs:785/808` |
| `PrivateAddresses` | ❌ 无 `[DataMember]` | `Computer.cs:244` |
| `IsOnSameNetwork` | ❌ 无 `[DataMember]` | `Computer.cs:276` |
| **`UdpEndPoint`** | ✅ 但**只在本地发现路径上被赋值** | 赋值 `ComputerDiscoveryClient.cs:128`；消费 `VD-Q/…/NetworkManager.cs:418/423` |

`NetworkAdapter` 的字段：`IPAddresString` / `MACAddresString` / `IsWireless` / `IsGigabit`
（全部 `[DataMember]`，`VD-S/Interfaces/NetworkAdapter.cs:100-110`）。

> ⚠️ **注意 `UdpEndPoint` 压倒了 `PrivateAdapters`**：Quest 侧把 `Computer.PrivateAddresses`
> 和 `NetClient.AnyAddress` 一起交给 `ConnectToLocalPeerAsync`（`NetworkManager.cs:457/529-539`），
> 但**判定「用不用远程」的真正依据是 `UdpEndPoint != null`**（`:423`）。
> `UdpEndPoint` 只在「这条记录来自 UDP 38850 广播」时被赋值 → **云注册表里回来的 PC 一律没有 `UdpEndPoint`**。

---

## 3. Quest 侧：`ComputerDiscoveryClient` —— 真正的发现发起方

### 3.1 端点与初始化（`VD-Q/…/ComputerDiscoveryClient.cs`）

```
:24  static readonly IPEndPoint BroadcastEP;
:26  static readonly IPEndPoint ListeningEP;
:63  BroadcastEP  = new IPEndPoint(IPAddress.Broadcast, 38850);   ← 真正在用
:64  ListeningEP  = new IPEndPoint(IPAddress.Any, 38850);         ← 死代码，见 §3.4
```

### 3.2 `FindComputersAsync(PlatformAccessToken)`（`:73-150`）

```
:75   _searchCancelled = false
:76-80 _token = P_0；_broadcastMessageTask = Task.Run(CreateBroadcastMessage)   （同 token 不重建）
:90   _broadcastClient = new UdpClient(AddressFamily.InterNetwork)
:92   _broadcastClient.EnableBroadcast = true
:98   _broadcastClient.Send(array, array.Length, BroadcastEP)   ← ★ 整个发现只有这一次发送
:99   Stopwatch.StartNew()
:100  while (!_searchCancelled)
:102    num = 3000 - (int)stopwatch.ElapsedMilliseconds
:103-105 if (num <= 0) break;                                  ← ★ 收包窗口只有 3 秒
:107    await _broadcastClient.ReceiveAsync().TimeoutAfter(num)
:110-130   解回包（见下）
:138-143 catch (SocketException ex) { if (ex.SocketErrorCode == HostUnreachable) LocalNetworkFailure?.Invoke(...) }
:145-147 catch { }                                             ← 其余全部静默
```

**三个必须记住的量化事实（此前全部未知）**：

1. **每次刷新只广播 1 个包**（`:98`），不是周期广播。
2. **回包窗口 = 3000 ms 硬上限**（`:102-105` + `:107` 的 `TimeoutAfter(num)`）。
   ⇒ PC 必须在 3 秒内应答；PC 慢一点（磁盘/AV 扫描），头显这一轮就看不到它。
3. **`LocalNetworkFailure` 是死事件**：全树只有声明 `:47` 与 raise `:142`，**零订阅者**。
   ⇒ 头显检测到 `HostUnreachable` 后**什么都不做**，用户看不到任何提示。

### 3.3 请求包构造 `CreateBroadcastMessage()`（`:175-187`）—— 与 PC 侧逐字段对应

```
:177  _rsa = new RSACryptoServiceProvider();                 ← 每次搜索一把新密钥对（只导公钥）
:179  AesStream aesStream = new AesStream(memoryStream, _broadcastAes)   ← 硬编码共享 AES
:180-181  aesStream.Write(ASCII(_rsa.ToXmlString(false)), …)   ← ★ 公钥 XML，可 >243B
:182  aesStream.WriteByte(0);                                 ← ★ 0x00 标记
:183  aesStream.WriteByte((byte)_token.Platform);             ← ★ Platform
:184-185  aesStream.Write(UTF8(_token.AccountID), …)          ← ★ token = AccountID
:186  return memoryStream.ToArray();
```

**与 PC 侧 `-.112.cs:365-391` 逐条对上**：243 字节 ASCII 公钥 XML → `:382`；
`array3[0] == 0` → `:385-390` 吃掉标记与 Platform；
`Encoding.UTF8.GetString(...)` 的 token → `:391`，再与 `A_1.Contains(new PlatformAccessToken(platform2, string2))` 比较 → `:401`。

### 3.4 回包解析（`:107-130`）—— 与 PC 侧 `-.112.cs:445-447` 对称

```
:111  if (buffer.Length > 128)
:113-115  array2 = buffer[0..128); sourceArray = _rsa.Decrypt(array2, true)
:116-119  array3 = sourceArray[0..32) → aes.Key
          array4 = sourceArray[32..48) → aes.IV                    ← 与 PC -.112.cs:514-516 对称
:120-124  Aes.Create(); Padding=None; Key/IV; CreateDecryptor()
:125-127  AesStream(buffer, 128, …) → XmlReader → ComputerSerializer.ReadObject
:128      computer.UdpEndPoint = udpReceiveResult.RemoteEndPoint   ← ★ 后续连接地址来自这里
:129      computers.Add(computer)
:132-134 catch { }                                                  ← 单个坏包静默跳过
```

**Quest 只广播、不监听**：`ListeningEP`（`:64`）声明后从未使用；
`_listeningClient` 字段（`:42`）只在 `StopListening()`（`:166`）里被
`Interlocked.Exchange(ref _listeningClient, null)` 置空，而 `FindComputersAsync` **从不创建它**
→ 恒为 null，Quest 端 38850 上没有监听套接字。

### 3.5 分支 A（17 字节包）的真实语义 —— 连接前的「预告包」

```
VD-Q/…/NetworkManager.cs:418  IPEndPoint udpEndPoint = computer.UdpEndPoint;
:423  if (udpEndPoint == null)  →  ConnectToPeerAsClientAsync(region, connectionID, platform,
                                    aes, 38811, AnyAddress, privateAddresses, 38810, true)     ← 远程
:427  else                       →  新建一个临时 UdpClient
:431-436   data = ArrayPool.Rent(17); connectionID.CopyTo(data,0);
           data[16] = Platform; await client.SendAsync(data, 17, udpEndPoint)   ← ★ 单播给 PC
:440-449   catch → 等 1 秒，换一个新 UdpClient 重发一次（同样吞异常）
:456  await Task.Delay(50)
:457  await MessagingClient.ConnectToLocalPeerAsync(aes, AnyAddress, privateAddresses, 38810, true)
```

⇒ **17 字节包 = 「我要连你了」的预告**，**单播**发给上一次发现时记下的 PC 源端点，
紧接着才建 TCP 38810。它**不是 NAT 打洞**（打洞需要往未知端口发，而这里端口和地址都是已知的）。
PC 侧 `-.112.cs:354` 收到后关闭 38850 监听与此自洽：应答完就不再需要发现通道了。

---

## 4. PC 侧第二个发现动作：UDP 38860 零长广播

```
private static void \u0007()                                        VD-S/Streamer/ConnectionManager.cs:657
 ├─ :665  new UdpClient(AddressFamily.InterNetwork)
 ├─ :673  EnableBroadcast = true
 ├─ :674  new IPEndPoint(IPAddress.Broadcast, 38860)
 ├─ :681  udpClient.Send(Array.Empty<byte>(), 0, ep)     ← 0 字节负载
 ├─ :684  udpClient.Send(Array.Empty<byte>(), 0, ep)     ← 连发两次
 └─ :697-699  catch { }                                  ← 空 catch，发不出去也不报
```

- **负载 0 字节**，不含主机名/IP/标识，只表达「这台机器上有 VD Streamer 在跑」。
- 触发者：`:908-927` 的 `Timer` 回调 → 异步状态机 `ConnectionManager.\u0008`；
  `Timer` 在静态构造里创建（`:1011`）。
- **`[未验证]` 周期**：状态机 `MoveNext` 不在树里；实测全树对 `ConnectionManager.\u0007()` 只有 6 处命中
  （`:208/:212/:214` 同名事件处理器、`:933/:944/:955` 同名状态机），**无任何一处真正调用它** → 调用点 `查不到`。
- 非发现、但同族的局域网广播：`VD-S/Net/WOLHelper.cs:43` `Broadcast:7`、`:47` `Broadcast:9`。
  **该类全树无调用点** → `查不到`。

---

## 5. 完整时序（双侧对齐）

```
[Quest] RefreshComputersAsync()                                VD-Q/…/NetworkManager.cs:141
 ├─ :155 timeout = IsComputerRegistryOffline ? 3s : 12s
 ├─ :156 accessTokens = await _accessTokenGetter(timeout)      ← ★ 出网，拿 token
 ├─ :160-187 accountID == null   → "Failed entitlement check" 弹窗 → 8s 后 CurrentProcess.Kill()
 ├─ :188-215 accountID == ""     → "Unable to retrieve identity" 弹窗 → 10s 后 Kill()
 └─ :220 GetComputersAsync(accessTokens, timeout)              ← 见下

[Quest] GetComputersAsync()                                   :629
 ├─ :634 discoveryClient = new ComputerDiscoveryClient()
 ├─ :637 discoveryTask = GetHasValidIdentityAsync()
 │        .ContinueWith(t => !t.Result ? EmptyComputersResult
 │                                    : discoveryClient.FindComputersAsync(accessTokens.Item1))
 │        ★★ 身份闸门：false ⇒ 连广播包都不发
 ├─ :646-676 按本机时区把两个 token 分配给 america/europe
 ├─ :677-706 两个 proof 都无效 ⇒ IsComputerRegistryOffline = true，写 ComputerRegistryWarning
 ├─ :707-766 否则并行 GetComputers2(两区)，TimeoutAfter(timeout)
 │        :726-765 fault 分支写另一组 ComputerRegistryWarning
 ├─ :768-813 把云结果并进 HashSet；任一区非空即 discoveryClient.StopSearch()（:788/:811）
 └─ :814-823 if (HasValidIdentity) 把 discoveryTask 结果并进 HashSet（:818 还要 hadValidIdentity）

[Quest] FindComputersAsync(token)                             ComputerDiscoveryClient.cs:73
 ├─ :98  广播 1 个包到 255.255.255.255:38850
 └─ :107 最多收 3000 ms 的回包

[PC]  LocalDiscoveryServer 收包                                VD-R/-.112.cs:322
 ├─ 分支 A（17B）: :352 触发事件 → :354 关闭监听              ← 只在「点连接」时发生（§3.5）
 ├─ 分支 B1（token 已知）: :451 单播回 C
 └─ 分支 B2（token 未知）: :436-441 触发配对请求事件 → continue，不回包
      └─ LocalDiscoveryManager: :117 if (ShowPairingRequests) → :190 MessageBox
                        "Allow {Platform} user '{name}' to access this computer?"
         :202 Accounts.Add → :206 Save → :229 ShowPairingRequests = !flag

[Quest] 用户点「允许」后再刷新一次 ⇒ 这次落进 B1 ⇒ 拿到 Computer + UdpEndPoint

[Quest] 点某台电脑 → ConnectToComputerAsync                  NetworkManager.cs:423
 ├─ UdpEndPoint == null → 远程 38811（云注册表来的 PC）
 └─ UdpEndPoint != null → 单播 17B 预告包 → ConnectToLocalPeerAsync(..., 38810)
```

---

## 6. 断点：失败模式逐条挂证据

| # | 失败模式 | 机制 | 证据 |
|---|---------|------|------|
| **F1** | **访客网络 / AP 隔离** | 头显那条 `255.255.255.255:38850` 广播到不了 PC。PC 回包是**单播**（`-.112.cs:451`），所以**回程不受影响，失效的只有去程** | 机制：`ComputerDiscoveryClient.cs:63/98`。语料：`research/09-failure-corpus/02-symptom-to-rootcause.md:67-68`（C1/C2 引 R10 "disable Guest networks and any AP isolation options"、R11 "isolate WiFi traffic from wired/ethernet traffic"）、`research/09-failure-corpus/01-symptom-corpus.md:25-26`（R08 官方 FAQ / R09 开发者 ggodin）。**根因在头显所在的 AP，PC 侧工具只能做辅助推断** |
| **F2** | **组播被过滤** | **不适用** —— VD 根本不用组播 | 两棵树全树 `MulticastOption`/`JoinMulticastGroup`/`AddMembership` = 0 命中（§1）。**不要为组播做检测项** |
| **F3** | **广播被过滤 / 出站被拦** | 38850 广播（出）与 38860 脉冲（出）都出不去；头显 3 秒内收不到任何回包 → 列表为空且**无任何提示**（`ComputerDiscoveryClient.cs:102-105` 静默 break，`:145-147` 空 catch） | 发送点 `ComputerDiscoveryClient.cs:98`、`ConnectionManager.cs:674/681/684`；静默点 `:145-147` 与 `ConnectionManager.cs:697-699` |
| **F4** | **PC 3 秒内没回包** | 窗口硬编码 3000 ms（`ComputerDiscoveryClient.cs:102`），超时即 `break`，本轮发现直接结束 | `ComputerDiscoveryClient.cs:99-107`。**没有任何重试** → 头显侧刷新一次就定生死 |
| **F5** | **Streamer 没跑 / 服务拉不起进程** | PC 上没有 `UdpClient(Any:38850)`，没有 38860 脉冲 | 监听建立 `ConnectionManager.cs:200-213` + `-/-.28.cs:72/76`。本机实测 `ServiceLog.txt` 反复 `Failed to start Streamer on active session (HRESULT -2147024891)` / `UnauthorizedAccessException`，`Get-NetTCPConnection` / `Get-NetUDPEndpoint` 对 38810-40/38850/38860 全空 —— `research/02-network-diagnosis/01-ports-and-discovery.md:336-346`、`:400-407`。清单项 `research/02-network-diagnosis/02-pc-checklist.md:20-23` |
| **F6** | **`ShowPairingRequests = false`** | 分支 B2 只在 true 时弹窗（`-/-.28.cs:117`），否则 `continue` **一个字节都不回** → 头显永远等不到这台 PC | `-/-.28.cs:117` + `-.112.cs:432-442`；`StreamerSettings.cs:2121/2154`。**纯配置故障，所有网络检测项全绿也查不出** |
| **F7** | **`Accounts` 为空 / token 不匹配** | `-/-.28.cs:76` 把 `PlatformAccessToken[]` 传进发现器；`-.112.cs:401` 用它做 B1/B2 分流。空 ⇒ 全部落 B2 | 本机实测 `Accounts` = `OculusQuest`/`Oculus`（`research/02-network-diagnosis/01-ports-and-discovery.md:326`） |
| **F8** | **子网不匹配 / 跨 VLAN** | PC 从不广播 38850（§8.1），完全依赖头显那一侧的广播能否跨过来；`255.255.255.255` 类广播路由器默认不转发 | 语料 `research/09-failure-corpus/01-symptom-corpus.md:25-26`（R08/R09）、`:84`（R67 把「网络发现」误当 UPnP）。清单项 `02-pc-checklist.md:46-51` |
| **F9** | **端口 38850 被占** | `new UdpClient(Any:38850)` 抛 `SocketException`，发生在 `Task.Factory.StartNew(..., LongRunning)` 内（`-.112.cs:281`）而**返回的 Task 被丢弃** → unobserved task exception → **进程不崩、日志没有、发现彻底死掉** | `VD-R/-.112.cs:281/330`。清单项 `02-pc-checklist.md:60` |
| **F10** | **防火墙规则专门挡发现** | 本机实测只有 **1 条**规则：Program 作用域 + `Protocol: Any` + `Direction: In` + `Profiles: Domain,Private,Public`。它覆盖**入站 38850**；但 38850 广播与 38860 脉冲是**出向**，只受 `DefaultOutboundAction` 管 | 实测输出 `research/02-network-diagnosis/01-ports-and-discovery.md:253-271`。**全树搜不到任何创建规则的代码**（`advfirewall`/`netsh`/`INetFw*`/`INetFwPolicy2`/`HNetCfg.FwMgr` 全 0 命中）→ 规则是安装器或用户手工建的，不能假设存在 |
| **F11** | **杀软 / 加速器的 WFP 驱动级过滤** | UDP 广播与单播回包被驱动层拦掉，`-.112.cs:454-456`、`ConnectionManager.cs:697-699`、`ComputerDiscoveryClient.cs:132-134/145-147` 四处空 catch 让它**完全静默** | 二进制自带文案（`F:/Project/VirtualDesktop/analysis/VirtualDesktop.Streamer/strings/C__Program_Files_Virtual_Desktop_Streamer_VirtualDesktop.Streamer.exe.strings.txt`）：`:140139` "If you experience issues connecting to your computer or launching games, try disabling or uninstalling any anti-virus, internet security, firewall or VPN software…"；`:140218-140219` "Error establishing connection" / "Anti-virus or VPN software is preventing connections to your PC"（代码 `VD-S/Streamer/ConnectionManager.cs:765-776`）；`:140110` 360 Total Security；`:140118-140119` ZoneAlarm Firewall |
| **F12** | **虚拟网卡 / VPN 抢路由** | 38860 脉冲与 38850 广播都不指定源地址/接口，出口由 Windows 路由决定 | 机制：`ConnectionManager.cs:665-684`、`ComputerDiscoveryClient.cs:90-98`。本机实测 VeryKui TAP + 2× Hyper-V + APIPA —— `research/02-network-diagnosis/01-ports-and-discovery.md:348-385`；清单项 `02-pc-checklist.md:49-50` |
| **F13** | **APIPA 169.254 段** | PC 侧 bind `Any` 仍会绑上；但头显在 169.254 段不可能路由到它 | 机制：`-.112.cs:330`。本机实测 5 个 APIPA 网卡 —— `research/02-network-diagnosis/01-ports-and-discovery.md:372-385`；清单项 `02-pc-checklist.md:46-47` |
| **F14** | **休眠 / 网卡掉线** | `PowerModeChanged` 在 Suspend/Resume 置位内部状态（`ConnectionManager.cs:876-898`），Resume 后必须重新握手（`:882-888`）；`NetworkAddressChanged`/`NetworkAvailabilityChanged` 走 **2 秒去抖后 `cts.Cancel()`**（`:246-247` → `:830` → `:844`）。断连时**显式停掉本地发现**（`:399`） | 清单项 `02-pc-checklist.md:72-73`。⚠️ 停掉之后谁负责重启：`LocalDiscoveryManager.\u0001(5 参)` 在全树**没有调用点**（实测 `\u0004\u0002.\u0002.` 全树 26 处命中，24 处在 `-/-.28.cs` 自身，`ConnectionManager.cs` 只有 `:399` Stop 与 `:436` IsRunning）→ **重启路径 `查不到`** |
| **F15** | **重放的 17 字节包杀死 PC 监听** | 分支 A 命中即 `this.\u0002(); break;`（`-.112.cs:354`），本运行期内的 38850 监听就此结束，无日志无 UI | 机制 `-.112.cs:343-355`。可达性比第一版判断的更低：该包只能**单播**发给 `computer.UdpEndPoint`（`NetworkManager.cs:418/436`），而 `UdpEndPoint` 只有完成过一次发现才拿得到 → 需要受害者先成功发现过一次。`[未验证]` 现实里是否发生过 |
| **F16** | **身份闸门导致连广播都不发** | `NetworkManager.cs:637`：`GetHasValidIdentityAsync()` 返回 false ⇒ 用 `EmptyComputersResult`，**一个广播包都不发**。而 `:814` 还有第二道 `HasValidIdentity` 闸门 | 见 §7.3。**这一条对补丁基线是致命的，见 §7.4** |
| **F17** | **云挂了把用户挡在门外（但仍显示本地 PC）** | 云故障时 VD 会退化成「只显示本地电脑」，而不是完全不工作 | 文案来源 `NetworkManager.cs:677-706`（"…servers unreachable, only showing local computers"）、`:726-765`（"…not responding, only showing local computers" / "…partially unreachable, some computers might not appear"）、`:683`（"Not connected to Wi-Fi"，来自 `PerfStatsHelper.GetWifiMetrics()`）。**R08 的原句就是 `:762` 这个字符串** |

---

## 7. 核心问题：**完全没有外网时，发现能不能成功？**

第一版的答案是「PC 端可以、头显端 `查不到`」。**现在两侧都有代码，答案要重写。**

### 7.1 协议层：发现本身是纯局域网的 ✅ 已证

| 论据 | 证据 |
|------|------|
| 广播的**载荷**只含 RSA 公钥 + Platform + AccountID，**不含任何服务器地址** | `ComputerDiscoveryClient.cs:175-187` |
| PC 侧的应答只回单播到 `result.RemoteEndPoint` | `VD-R/-.112.cs:451` |
| PC 的 AES Key/IV 是硬编码常量，**Quest 侧逐字节相同** —— 两端不交换密钥材料 | `-.112.cs:197-208` ↔ `ComputerDiscoveryClient.cs:51-62` |
| 云地址（两个 registry URL、两个 relay IP）只出现在 `NetHelper` 与远程连接分支，**不在发现路径上** | `VD-Q/…/NetHelper.cs:16-17/42/44`；`NetworkManager.cs:425` |
| `FindComputersAsync` 全程只碰一个 socket，无 DNS、无 HTTP | `ComputerDiscoveryClient.cs:90-135` |

### 7.2 「能不能走到协议层」有**四道**闸门，全部在 Quest 侧，全部在 `NetworkManager.cs` / `UserSettings.cs`

> 本节的门 3b、门 4 是同批 worker 复核时补上的，我第一版漏了 —— 漏掉的那道**即使前面全部被补丁放行也照样生效**。

```
闸门 1  :156  accessTokens = await _accessTokenGetter(timeout)
               timeout = IsComputerRegistryOffline ? 3s : 12s                       (:155/:31/:33)
               ↑ 这一步出网。失败/超时 ⇒ 后面全走不下去。

闸门 2  :160-187  accountID == null  → ShowWarning("Failed entitlement check", …)
                    → await Task.Delay(8000) → CurrentProcess.Kill()
               :188-215  accountID == ""  → ShowWarning("Unable to retrieve identity", …)
                    → await Task.Delay(10000) → CurrentProcess.Kill()

闸门 3  :637  GetHasValidIdentityAsync() 返回 false ⇒ EmptyComputersResult
               ⇒ FindComputersAsync 根本不被调用 ⇒ 广播一个包都不发
               （GetHasValidIdentityAsync = 官方签名 && _hasValidIdentity，见 §7.3）

闸门 3b :814  if (SettingsBase<UserSettings>.Default.HasValidIdentity)
               ⇒ 当前值为 false 时，广播回来的条目**一条都不遍历**

闸门 4  :818  if (hadValidIdentity) hashSet.Add(item)
               ⇒ hadValidIdentity 是 :633 在本轮开始时的快照
               ⇒ 即使 :814 因为本轮拿到了有效结果而为真，:633 的旧值仍是 false 时照样丢弃
```

**门 3b / 门 4 为什么在补丁基线上必然失败**：`_hasValidIdentity` 的唯一写入点是

```
:778  if (hasQueriedRegistry && !IsComputerRegistryOffline)
          SettingsBase<UserSettings>.Default.HasValidIdentity = result != null;
:801  （europe 区同样一句）
```

而 `hasQueriedRegistry` 只在 `:722` 的 `Task.WhenAll(america, europe)` 成功后才置 true，
`result == null` 时 `:774`/`:796` 直接 `return null`。

⇒ **`_hasValidIdentity` 只能由「一次成功的云注册表查询」点亮。** 云不可达 ⇒ 它永远是 false
⇒ 门 3 挡住广播、门 3b 挡住遍历、门 4 挡住入库，**三处各自独立**。

> `[未验证]`：「`_hasValidIdentity` 只在 `:778/:801` 被写」是从唯一赋值点推出的；
> `SettingsBase<T>` 的序列化/迁移是否在别处写这个字段，本轮看不到（该基类不在 Quest 侧程序集里）。
> **但门 3b/门 4 的读取是无条件的** —— 不管该字段怎么变，只要它是 false，结果就被丢掉。

### 7.3 闸门 3 判的是**本地 APK 签名**，不联网

```csharp
// VD-Q/…/UserSettings.cs:1509-1520
internal Task<bool> GetHasValidIdentityAsync()
{
    return Task.Run(() =>
    {
        Signature signature = PlatformAndroid.Context.PackageManager
            .GetPackageInfo(PlatformAndroid.Context.PackageName, PackageInfoFlags.ResolvedFilter)
            .Signatures.FirstOrDefault();
        if (signature == null) return false;
        return signature.GetHashCode() - 22 == 1778352230 && _hasValidIdentity;
    });
}
```

- `signature.GetHashCode()` 是**当前 APK 签名证书**的哈希；常量 `1778352230` 对应 Meta 官方签名。
- `&& _hasValidIdentity` 是**本地缓存的历史结论**（`UserSettings.cs:1126-1140`），
  由上一次云校验成功后写入（`NetworkManager.cs:778/801`）。
- ⇒ **这个闸门本身完全离线**，它判的是「这个 APK 是不是官方签的」+「这台头显历史上验过没有」。

### 7.4 所以答案分四档（补丁基线单列，因为它不是「崩溃」而是「静默」）

| 场景 | 能否发现 | 证据 |
|------|---------|------|
| ① **原版 APK，首次使用、从未联网验过、无外网** | ❌ **不能，且是闪退**。闸门 1 取不到 token；即使取到，`accountID == null` 会弹 "Failed entitlement check" 并在 8 秒后 **`CurrentProcess.Kill()` 自杀** | `NetworkManager.cs:156/160-187`；`_hasValidIdentity` 初值 false → 闸门 3/3b/4 也全过不去 |
| ② **原版 APK，已联网验过并缓存 `_hasValidIdentity`、官方签名、之后断网** | ✅ **能**。闸门 3 是纯本地判定必然通过；随后纯 LAN 广播 + 3 秒窗口 + 单播回包，全程不碰服务器 | `:637` / `UserSettings.cs:1509-1520` / `ComputerDiscoveryClient.cs:90-135` |
| ③ **原版 APK，无外网 + 云调用失败但身份仍在** | ✅ **能，且只显示本地 PC**。云失败降级为一句提示 | `:677-706`、`:726-765`、`:762` |
| ④ **补丁基线，无外网** | ❌ **不能，但**不闪退**，而是无声地列出零台电脑**。`CurrentProcess.Kill()` 的方法体已被改成 `ret`，所以闸门 2 只是空操作，流程继续往下走，直接撞死在闸门 3/3b/4 | 闸门 2/3/3b/4 见 §7.2；`Kill()` 的 NOP 证据属 `03-patched-baseline-deps.md`（见 §7.5） |

**这正好对上语料**：R29「PC 断开互联网时 Streamer 图标变灰」（`research/09-failure-corpus/01-symptom-corpus.md:46`）
= 场景 ①；R30 的 entitlement check（`:47`）= 场景 ① 的 `:160-187`；
R73「in order to find your PC VD has to talk to a server on the Internet from your Quest」（`:90`）
= 闸门 1，**代码层面成立但只在首次/换设备时成立**，不是每次都成立。

> **对 VDHelper 的直接设计约束（01 也已同步）**：场景 ① 和场景 ④ 的用户可见症状完全不同
> —— 一个是「App 闪退」，一个是「App 活着但列表空」。
> **判据必须是「进程是否还活着」，不能把「列表空」一律归进网络检查。**

### 7.5 对补丁基线（代码事实；结论归 `03-patched-baseline-deps.md`）

补丁掉云认证之后能否离线发现，取决于**五个开关**：

| # | 开关 | 位置 | 不补丁的后果 |
|---|------|------|------------|
| 1 | `_accessTokenGetter` | `NetworkManager.cs:156` | 取不到 token → 整条链路挂 |
| 2 | `AccountID` 返回值（null / 空串 / 有值） | `NetworkManager.cs:160/188` | 原版：null → 8 秒后 Kill；空串 → 10 秒后 Kill。**补丁基线**：`CurrentProcess.Kill()` 的方法体已被 NOP 成 `ret`，所以只弹警告不退出，继续往下走 |
| 3 | **APK 签名常量 `1778352230`** | `UserSettings.cs:1518` | 补丁 APK 签名不同 → `GetHasValidIdentityAsync()` 恒 false → **连广播都不发**（闸门 3） |
| 4 | `HasValidIdentity` 的读取（`:814`） | `NetworkManager.cs:814` | false ⇒ 广播回来的条目一条都不遍历（闸门 3b） |
| 5 | `hadValidIdentity` 快照（`:633` → `:818`） | `NetworkManager.cs:633/818` | false ⇒ 即使 4 放行，入库仍被丢弃（闸门 4） |

**已核实的三点（我自己复跑过，不是转述）**：

1. **开关 3（APK 签名常量）经字节级验证：补丁没有改它。**
   `UserSettings` 被合并进 XABA 条目 **idx52（`Xenko.dll`，529,408 B）**，
   常量以 IL `ldc.i4` 形式落在文件偏移 `0x318cc`：

   ```
   $ python -c "blob=open('idx52','rb').read(); pat=open('.../patched_assemblies/Xenko.dll','rb').read(); ..."
   len 529408 529408
   diff bytes: 130      ranges: 38
   0x318cc inside-diff-range= False blob= 20 66 80 ff 69 patched= 20 66 80 ff 69
   0x13126 inside-diff-range= False blob= 20 7c 80 ff 69 patched= 20 7c 80 ff 69
   ```

   `20 66 80 ff 69` = `ldc.i4 0x69FF8066` = **`ldc.i4 1778352230`**；
   `20 7c 80 ff 69` = `ldc.i4 0x69FF807C` = `ldc.i4 1778352252`（同族的另一个常量）。
   两个常量都**落在 130 字节改动区间之外且逐字节相同**。
   ⇒ **签名检查原封未动**。补丁 APK 是重签名的，所以这个检查在补丁基线上**必然返回 false**
   ⇒ **闸门 3 未被放行 ⇒ `FindComputersAsync` 根本不被调用 ⇒ 连广播都不发。**
   （在此之前我只能写「grep 0 命中 = grep 不到 ≠ 没处理」；现在是正面结论。）
2. `CurrentProcess.Kill()` 定义在 **`VirtualDesktop.Core`**，XABA 条目 **idx49** 就是
   `VirtualDesktop.Core.dll`（40,448 B，md5 `27bf89c9e588afe47f82094e2afab477`）；
   `binary_patch.py:226-254` 在该条目上打 RVA `0x3ADC`，
   `patched_assemblies/Xenko.OpenXR.dll`（40,448 B）与 `extracted` 的 idx49 **实测只差 14 字节**。
   ⇒ **被 NOP 的确实是 `CurrentProcess.Kill()`**，但**输出文件名是错的**（见 §0.2）。
3. 在 `F:/Project/VirtualDesktop/analysis/apk_patch/` 全树（`*.py` / `*.md` / `*.json`）
   grep `1778352230` / `GetHasValidIdentityAsync` / `HasValidIdentity` → **0 命中**，
   与第 1 条的字节级结果一致：**源码层与二进制层都未处理**。
   开关 4/5（`NetworkManager.cs:814` / `:633→:818`）在源码层根本不可见，只能靠实机验证。

> **仍需实机确认的一件事**（现在只剩验证，不剩判定）：
> 在设备上抓一次包，看有没有发往 `255.255.255.255:38850` 的 UDP。
> 按第 1 条，**预期结果是抓不到**；抓到了就说明还有本轮没定位到的改写路径，那才是意外。
> 本轮无 adb，做不了，已标 `[未验证]` 并报给 Main。

---

## 8. 对既有报告的纠正（逐条给了反证）

### 8.1 `research/02-network-diagnosis/01-ports-and-discovery.md:31` 把 `-.112.cs:209` 称作「广播端点」——**该端点从未被使用**

`-.112.cs:209` 确实构造了 `new IPEndPoint(IPAddress.Broadcast, 38850)`，字段声明在 `:594`，
但**整个 639 行文件里没有任何一处读取它**（逐行枚举 `\u008B.\u0002` 的所有出现位置，
唯一使用端点字段的是 `:330` 的 `new UdpClient(\u008B.\u0002.\u0002)`，即 `Any:38850`）。
Quest 侧同样不监听 38850（`ComputerDiscoveryClient.cs:64` 声明、`:42/:166` 恒为 null）。

**纠正**：**PC 从不向 `255.255.255.255:38850` 发包，Quest 也从不监听 38850。**
38850 上的 38850 是「Quest 单次广播 → PC 单播应答」的单向对。工具的探测方向必须是**被动**。

### 8.2 ⚠️ `research/02-network-diagnosis/01-ports-and-discovery.md:234` 「38811/38821/38831/38841 全树零命中，不要使用」——**四个端口全部存在**

第一版也照抄了这条禁令。**它是错的**，根因同 §0.2：搜索对象是错的程序集。
在正确的 Quest 程序集里：

```
$ grep -rhoE "\b388[0-9][0-9]\b" VD-Q/ | sort | uniq -c
      2 38810        1 38811
      2 38820        1 38821
      2 38830        1 38831
      2 38840        1 38841
      2 38850
```

| 端口 | 角色 | 证据 |
|------|------|------|
| 38810 / 38820 / 38830 / 38840 | **LAN 本地连接**：控制 / 数据 / 视频 / 音频 | `NetworkManager.cs:457/539/529/534`（均为 `ConnectToLocalPeerAsync`） |
| **38811** / **38821** / **38831** / **38841** | **远程中继连接**：控制 / 数据 / 视频 / 音频 | `NetworkManager.cs:425/553/543/548`（均为 `ConnectToPeerAsClientAsync`，且**只在 `computer.UdpEndPoint == null` 时走**，即该 PC 来自云注册表而非 LAN） |

**工具影响**：38811/21/31/41 **可以进常量表**，但必须标明「仅远程中继用，同网段 LAN 场景永远用不到」，
且**不要**把它们和 38810-40 混在一张「同网段必需端口」表里 —— 那正是原报告担心的假告警。

### 8.3 `VIRTUAL_DESKTOP_APK_STREAMER_LINK_ANALYSIS.md:30` 把 ConnectionID/key/IV 说成全部来自云

- 云路径成立：`VD-S/Interfaces/RegistrationToken.cs:12` 构造函数 `(byte[] connectionID, byte[] iv, byte[] key, bool isValid)`，
  `:83-113` 的 `Aes` 属性用 `_key`/`_iv` 重建 AES；`IComputerRegistry.cs:15/18/21` 三个 `RegisterComputer3/4/5` 返回 `RegistrationToken`。
- **但那是远程路径**。本地发现在 `-/-.28.cs:65-71` **自己**生成 AES Key/IV 并自己构造 `Computer`，
  `ConnectionID` 来自本地 Guid（`Computer.cs:23`）。**两条路径的身份材料来源不同**，
  Quest 侧也据此分流（`NetworkManager.cs:423`）。

### 8.4 本文第一版的两条错误（自我纠正）

| 第一版 | 更正 |
|--------|------|
| 「Quest 侧方法体被 AOT 剥掉 → `查不到`」 | 错。是 `extracted_assemblies/` 那批 dll 名实不符（§0.2）。真实程序集在 APK 的 XABA blob 里，方法体完整 |
| 「38950 分支 = NAT 打洞」 | 错。该包是**连接前的单播预告包**，目标地址来自上一次发现时记下的 `computer.UdpEndPoint`（`NetworkManager.cs:418/436`），打洞不需要已知地址 |

---

## 9. 本轮仍然「查不到」的清单

| 缺口 | 为什么查不到 |
|------|------------|
| UDP 38860 广播的**周期**与调用点 | `ConnectionManager.\u0008` 状态机未反编译；`\u0007()` 全树无调用点（§4） |
| `WOLHelper`（UDP 7/9）的调用点 | 全树只有定义 `Net/WOLHelper.cs:9-64`，无调用 |
| `LocalDiscoveryManager.\u0001(5 参)`（谁决定何时开始发现）的调用点 | 全树 26 处 `\u0004\u0002.\u0002.` 命中里没有它 |
| `IComputerRegistry` 的具体实现类（云注册 WCF client） | `RegisterComputer3/4/5`、`BeginGetComputers2` 只在接口里出现 |
| 断连（`ConnectionManager.cs:399`）之后谁重启本地发现 | 同上第三条 |
| `_accessTokenGetter` 的实现（token 是否有本地缓存） | 定义在 Quest 的启动层，不在 `VirtualDesktop.Mobile` 里。§7.4 场景 ② 依赖它，**必须实机验证** |
| AES 的 `CipherMode` | 两端都只 `Aes.Create()` + `PaddingMode.None`，模式取默认值，基类初始化未反编译 |
| 「提权导致 PC 端配对弹窗不可见」 | 代码里无任何提权/会话分支；`-/-.28.cs:119` 只做 `Dispatcher.BeginInvoke` |
| `HasValidIdentity` 在补丁 APK 上的实际取值 | 需要在真机上 `adb` 读 `UserSettings`，头显当前不可达 |
| **补丁基线上是否真的发过 `255.255.255.255:38850`** | 这是区分「闸门 3 未处理」与「闸门 3b/4 丢结果」的唯一判据，必须设备抓包。本轮无 adb |
| `_hasValidIdentity` 是否只在 `:778/:801` 被写 | 从唯一赋值点推出；`SettingsBase<T>` 的序列化/迁移不在 Quest 侧程序集里，看不到（§7.2）。**但 `:814`/`:818` 的读取是无条件的，不依赖这条** |
| 38811/21/31/41 在 **PC 侧**是否对称存在 | PC 侧 `VirtualDesktop.Streamer` 反编译树里只见 38810-40/38850/38860；PC 侧远程客户端实现不在本树 → `查不到`（但这不影响端口清单，01 已双侧记录） |
| D3 探测包在 Windows 上是否产生 ICMP 噪音 | 未实跑 |

---

## 10. 自检：本轮实跑过的命令

```
$ ls "F:/Project/VirtualDesktop/analysis/VirtualDesktop.Android_1.34.18.0/"
→ 只有 modified_repack/（空目录，brief 里的路径不含反编译源码）

$ ls "F:/Project/VirtualDesktop/analysis/apk_patch/extracted_assemblies/" | grep -i virtual
→ 7 个 VirtualDesktop.*.dll

$ "C:/Users/dwgx1/.dotnet/tools/ilspycmd.exe" -o D:/tmp/vdnet …/extracted_assemblies/VirtualDesktop.Net.dll
$ grep -c "^namespace " …                                          → 16（全部 OpenTK*）
$ grep -n "UdpClient\|IPAddress.Broadcast\|EnableBroadcast" …       → 0
   ← 这两条正是第一版误判的依据

$ cd %TEMP%/vd_ep_01/vd && grep -rn "FindComputersAsync\|ComputerDiscoveryClient\|StopListening" --include=*.cs .
→ VirtualDesktop.Mobile/…/NetworkManager.cs:634 / :637
→ VirtualDesktop.Net/…/ComputerDiscoveryClient.cs:73 / :164

$ cd %TEMP%/vd_ep_01/vd && grep -rhoE "\b388[0-9][0-9]\b" --include=*.cs .
→ 38810×2 38811×1 38820×2 38821×1 38830×2 38831×1 38840×2 38841×1 38850×2

$ cd %TEMP%/vd_ep_01/vd && grep -rn "LocalNetworkFailure" --include=*.cs .
→ 只有 :47 声明 与 :142 raise，零订阅者

$ python …rglob("*.cs") 于 VD-R 全树                                    → 12704 文件 / 2454457 行
$ grep -rln "38850" 于 VD-R                                           → 仅 ./-.112.cs
$ grep -c 'MulticastOption|JoinMulticastGroup|AddMembership|DnsServiceDiscovery' → 全 0（两棵树）
$ grep -c 'advfirewall|netsh|INetFw|HNetCfg.FwMgr|INetFwPolicy2'      → 全 0
$ grep -n "F5=u" 于 VD-R                                              → 仅 -/-.38.cs:14

$ cd "F:/Project/VirtualDesktop/analysis/apk_patch" && grep -rn "1778352230|GetHasValidIdentityAsync|HasValidIdentity" --include=*.py --include=*.md --include=*.json .
→ 0 命中
```

未跑（本轮无法跑）：`pktmon` 抓包实跑、任何需要 Quest 在场的验证、`adb` 读 `UserSettings.HasValidIdentity`。
符合 brief 的静态切片约定。

---

## 可自动化的检测项

判据栏写的是**实测可得**的事实。分成 **PC 侧**（工具能自己做）与 **头显侧**（需 adb）两组。

### A. PC 侧（不需要 Quest 配合）

| # | id | 检查什么 | 具体命令 / 读什么 | 判据 | 证据（为什么可判定） |
|---|----|---------|------------------|------|-------------------|
| **D0** | `disc-inbound-38850` | **60 秒内是否收到过一个「头显的发现请求」** —— 抓 `udp.dstport==38850 && len>243` 的**入站**包 | `pktmon start --capture --comp nics --pkt-size 0 --file-name "$env:TEMP\vd.etl"`，等 60s，`pktmon stop` + `pktmon etl2txt` | 见到 ≥1 个即「头显的发现请求真的到了」 | 广播载荷结构 `ComputerDiscoveryClient.cs:175-187`（固定特征：前 243B 解密后是 RSA 公钥 XML、明文里第 0 字节必为 `0x00`）。**这是唯一能把「包没到」（F1/F3/F8/F12/F13）与「包到了但 PC 不回」（F6/F7）彻底分开的信号，且无需任何凭据、不解密**。**优先级高于 D4** |
| D1 | `disc-udp-listen` | UDP 38850 是否被 Streamer 绑在 0.0.0.0 | `Get-NetUDPEndpoint \| Where-Object {$_.LocalPort -eq 38850} \| Select-Object LocalAddress,LocalPort,OwningProcess` | 1 条、`LocalAddress = 0.0.0.0`、`OwningProcess` = `VirtualDesktop.Streamer.exe` 的 PID | 绑定代码 `-.112.cs:212/330`。**发现是否可能工作的第一必要条件** |
| D2 | `disc-38850-owner` | 38850 被谁占了 | 同上取 `OwningProcess` → `Get-Process -Id <id> \| Select-Object ProcessName,Path` | PID == D1 的 Streamer PID | `new UdpClient(Any:38850)` 独占且异常被静默吞（`-.112.cs:281/330` + `:454-456`），**只能靠端口表判定** |
| D3 | `disc-38850-loopback` | 本机自测：向 `127.0.0.1:38850` 发一个 17 字节全 0 哑包 | 工具内发 UDP（`127.0.0.1:38850`，17 字节 0） | **预期「无回包、无异常」= 正常** | 17B 分支只在 ConnectionID 匹配时才回（`-.112.cs:343-355`）。作用是把「socket 存在」和「协议活着」分开。**注意**：现在已知真实 17B 包是单播到 `computer.UdpEndPoint`（`NetworkManager.cs:436`），所以**不要**期待本地回环能有任何反应 |
| D4 | `beacon-38860` | PC 是否真的在发 38860 零长广播 | 复用 D0 的 etl，过滤 `udp.dstport == 38860` | 60 秒内 ≥1 个 `len=0` 包 | `ConnectionManager.cs:665-684`（`EnableBroadcast=true`、`Array.Empty<byte>()`、连发两次）。周期 `查不到` → 按「60s 内 ≥1 包」判 |
| D5 | `beacon-src-iface` | 38860 / 38850 广播从哪块网卡出去 | D0 的 etl 里看包的源 IP，与 `Get-NetIPInterface -AddressFamily IPv4 \| Sort-Object InterfaceMetric` 对照 | 源 IP 属物理 LAN 且该网卡 metric 最小 | `ConnectionManager.cs:665-684`、`ComputerDiscoveryClient.cs:90-98` 均不指定源/接口 |
| D6 | `disc-tcp-listen` | 38810-40 是否在 Listen | `Get-NetTCPConnection -State Listen \| Where-Object {$_.LocalPort -in 38810,38820,38830,38840}` | 4 条齐全且 PID 相同 | `ConnectionManager.cs:200/206/211/213`；Quest 侧对应 `NetworkManager.cs:457/529/534/539` |
| D7 | `cfg-showpairing` | `ShowPairingRequests` 是否为 false | `Get-Content "C:\ProgramData\Virtual Desktop\StreamerSettings.json" -Raw \| ConvertFrom-Json \| Select-Object ShowPairingRequests` | `= true` | `-/-.28.cs:117` + `-.112.cs:432-442` + `StreamerSettings.cs:2121/2154`。**「包到了但 PC 不理」的第一嫌疑** |
| D8 | `cfg-accounts` | `Accounts` 是否为空 | 同 D7 加 `-ExpandProperty Accounts` | 至少 1 个 | `-/-.28.cs:76` + `-.112.cs:401`。为空 ⇒ 全部落 B2 ⇒ 零回包 |
| D9 | `cfg-lastconnect` | 上次真正连成功是什么时候 | 同 D7，`Select-Object LastConnectDate` | 非空且近期 | `research/02-network-diagnosis/01-ports-and-discovery.md:325`；机制支撑 = 证明走通过 B1（token 已知 → 单播回 C） |
| D10 | `svc-fail-to-launch` | Streamer 进程是否真的在跑 | `Get-Process VirtualDesktop.Streamer`；`Get-Content "C:\ProgramData\Virtual Desktop\ServiceLog.txt" -Tail 20` | 进程在 **且** 无 `Failed to start Streamer on active session` | 没有进程 ⇒ `-/-.28.cs:72` 的 `new \u008B.\u0002()` 从未执行。本机实测反复出现（`01-ports-and-discovery.md:336-346`） |
| D11 | `fw-inbound-any` | 有没有那条 Program 作用域 / `Protocol: Any` / `Direction: In` 的 VD 规则 | `netsh advfirewall firewall show rule name=all \| findstr /i /c:"virtual desktop"`；再 `… name="Virtual Desktop Streamer" verbose` | ≥1 条且 In/Any/Allow | 本机实测 `01-ports-and-discovery.md:253-271`。**必须说明**：In 规则不覆盖 38850/38860 的**出向发送** |
| D12 | `fw-outbound-policy` | 默认出站是否 Allow | `Get-NetFirewallProfile \| Format-Table Name,Enabled,DefaultOutboundAction` | ∈ {NotConfigured, Allow} | 38850 广播（`ComputerDiscoveryClient.cs:98`）与 38850 回包（`-.112.cs:451`）、38860 脉冲（`ConnectionManager.cs:681`）全是出向 |
| D13 | `fw-profile-state` | 三个 profile 分别开没开 | 同 D12 | 至少 Private 或 Public = True | 本机实测 Private/Public 全 False（`01-ports-and-discovery.md:286-293`） |
| D14 | `route-metric` | 物理 LAN 网卡 metric 是否最小 | `Get-NetIPInterface -AddressFamily IPv4 \| Sort-Object InterfaceMetric` | 物理 LAN < 所有虚拟/VPN 网卡 | 决定 D0/D4/D5 的出口。清单项 `02-pc-checklist.md:50` |
| D15 | `apipa-nics` | 有多少网卡在 169.254/16 | `Get-NetIPAddress -AddressFamily IPv4 \| Where-Object {$_.IPAddress -like "169.254.*"}` | 排除 `Local Area Connection*` 后为空 | 头显不可能路由到 169.254 段。⚠️ 必须排除 —— 本机那 2 条在 `Get-NetAdapter` 里根本看不到（`01-ports-and-discovery.md:372-385`） |
| D16 | `vd-cloud-endpoints` | 四个云端点（2 主机名 + 2 IP）的 DNS + TCP 443 | `Resolve-DnsName america.vrdesktop.net, europe.vrdesktop.net`；`Test-NetConnection <name> -Port 443`；再测 `20.225.41.170` / `40.89.161.236` | 解析成功 + `TcpTestSucceeded=True` → Pass；失败 **Warn 不 Block** | 端点来源 `VD-S/…/-.92.cs:21/23/127/133`，Quest 侧独立同证 `VD-Q/…/NetHelper.cs:16-17/42/44`。**文案必须写「只影响需要出网的环节（首次 entitlement / 远程连接 / 换设备），不代表同网段局域网发现坏了」** —— 依据 §7.1 vs §7.4 |
| D17 | `streamer-alive` | Streamer 启动时刻 vs 现在 | `Get-Process VirtualDesktop.Streamer \| Select-Object Id,StartTime` | 启动后 ≥ 数秒 | 关联 F15：17B 分支命中会永久关掉本运行期内的 38850 监听（`-.112.cs:354`）而重启路径 `查不到`。「Streamer 久跑 + D1 显示 38850 不在」是独立可判的异常态 → 提示重启 Streamer |
| D18 | `wol-7-9` | 是否出现 UDP 7/9 广播 | 复用 D0 的 etl，过滤 `udp.dstport in (7,9)` | **默认不检查**（非发现必需） | `WOLHelper.cs:43/47`，但全树无调用点 → 只作观测项 |
| D19 | `remote-ports-not-needed` | 提醒：本机**不需要**放行 38811/21/31/41 | 从常量表/防火墙建议里排除这四个 | 永远排除 | `NetworkManager.cs:425/543/548/553` 证明它们只在 `computer.UdpEndPoint == null`（云注册表的 PC）时使用。**这纠正了 `01-ports-and-discovery.md:234` 的「来源不明、不要使用」** |

### B. 头显侧（需 adb，PC 侧工具做不到，但值得单列）

| # | id | 检查什么 | 判据 | 证据 |
|---|----|---------|------|------|
| H1 | `hs-registry-warning` | 读 `DynamicSettings.ComputerRegistryWarning` | 非空即**直接给出根因标签** | `NetworkManager.cs:683`（"Not connected to Wi-Fi"）、`:733-749`（"…servers not responding, only showing local computers"）、`:757`（"Meta servers having issues, some computers might not appear"）、`:762`（"Virtual Desktop servers unreachable…" / "…partially unreachable, some computers might not appear" —— **R08 原句**） |
| H2 | `hs-has-valid-identity` | 读 `UserSettings.HasValidIdentity` | false ⇒ 连广播都不发（F16） | `NetworkManager.cs:637/814`；判定式 `UserSettings.cs:1509-1520` |
| H3 | `hs-discovery-window` | 若 D0 抓到入站 38850 但头显仍不显示 | 3 秒窗口已过（PC 慢） | `ComputerDiscoveryClient.cs:102-107` |
| H4 | `hs-wifi-metrics` | 头显是否真的连上 Wi-Fi | `PerfStatsHelper.GetWifiMetrics()` 的 `IsConnected`/`IPAddress` | `NetworkManager.cs:680-684`；对应提示 "Not connected to Wi-Fi" |

### C. 明确**不要**做的检测项

| 别做 | 原因 |
|------|------|
| mDNS / 组播相关任何检查 | 两棵树全树零命中（§1 / F2） |
| 「UDP 38850 有广播包」当就绪判据 | **PC 从不广播 38850，Quest 也从不监听**（§8.1）—— 这个判据会永远失败 |
| 用 38811/21/31/41 当「同网段必需端口」 | 它们只在远程中继路径出现（§8.2 / D19）。正确做法是**排除**它们而不是放行它们 |
| 主动向 38850 发「探测包」期待拿到 PC 记录 | 广播 Key/IV 虽已公开（`-.112.cs:197-208` ↔ `ComputerDiscoveryClient.cs:51-62`），但分支 B1 需要**合法 token**、B2 一律不回包（`-.112.cs:401/432-442`）。第三方拿不到回包，**这不是 bug**。要用就抓入站包（D0），不要造包 |
| 把 38860 周期写死 | 周期 `查不到`（§4）；按「60s 内 ≥1 包」判 |
| 让工具自动改防火墙 profile / 自动关防火墙 | 安全姿态变更；清单项 `02-pc-checklist.md:34/48` 已标风险 |
| 让工具杀占用 38850 的进程 | 硬地板禁止不明进程终止；`02-pc-checklist.md:59` |