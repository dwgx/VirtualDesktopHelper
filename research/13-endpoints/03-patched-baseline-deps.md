# 13-03 补丁后基线还剩哪些网络依赖

> 目标：回答「一个绕开云端鉴权与 Quest 账号鉴权的客户端，还剩什么网络依赖」，并据此判定
> VDHelper 该查什么、不该查什么。
> 纯静态分析。未在头显上做任何验证（本次无 adb / 无头显在线）。
> ⚠️ **本文件 §3.3 的核心结论已被复核推翻，行号大面积不可复核。**
> 它引用的 `NetworkManager.cs:2395/2467/2471` 超出该文件长度（实际 453 行），
> `GetHasValidIdentityAsync` 在整棵反编译树里没有调用方；真正的签名门 `InputSystem.cs:21`
> 的行为是**杀掉进程**而非跳过发现，且该路径已被 `binary_patch.py:271-277` NOP 覆盖。
> **引用前先读 [`04-signature-gate-verification.md`](04-signature-gate-verification.md)。**
> 根因：撰写者用 ilspycmd 重新反编译了一份未落盘的产物，行号无法被第三方核对。

## 0. 本文件的证据基础（先说清楚）

- **`01-endpoint-inventory.md` 与 `02-discovery-protocol.md` 在我开工时不存在**
  （`D:\Project\VirtualDesktopHelper\research\13-endpoints\` 目录当时为空）。本文件全部结论由我从
  源码自行推导。若后续那两个文件给出不同端点，以它们为准，但 §2/§3 的判断链条不依赖它们。
- 主要证据源（全部只读，未写入 `F:\Project\VirtualDesktop`）：
  1. `F:\Project\VirtualDesktop\analysis\apk_patch\binary_patch.py` —— 补丁的唯一权威来源，逐行读过。
  2. `F:\Project\VirtualDesktop\analysis\apk_patch\decompiled\vdandroid\VirtualDesktop.Android\VrApp.cs`
     与 `...\decompiled\xenko\VirtualDesktop.Mobile\{NetworkManager,InputSystem,UserSettings}.cs`
     —— 工作区内的既有 ILSpy 反编译。
  3. 我自己用 `ilspycmd` 对 `analysis\apk_patch\extracted_assemblies\*.dll` 做的**新鲜反编译**
     （命令见 §7）。凡是只存在于这份新鲜反编译的证据，位置都写明来源文件与行号。
- **行号口径**：`apk_patch\decompiled\xenko\VirtualDesktop.Mobile\NetworkManager.cs` 那份反编译**没有
  编译出 async 状态机**（它停在 453 行，`GetComputersAsync` / `RefreshComputersAsync` /
  `CheckForStreamerUpdateAsync` 都只剩外壳）。因此本文凡引用这三个方法的行号，
  一律来自我用 `ilspycmd -p` 的新鲜反编译（`NetworkManager.cs` 共 2767 行），与
  `decompiled\xenko\...` 的行号**不对应**。同理，`Assistant.cs` / `ComputerDiscoveryClient.cs` /
  `NetHelper.cs` / `HmdResolutionTypeExtensions` 的行号也只存在于新鲜反编译里。
  可以直接对照工作区既有反编译的只有：`VrApp.cs`（entry #81，但那份同样**不含状态机**，
  `<CreateAccessTokenAsync>d__30` / `<GetAccessTokenAsync>d__31` 的方法体只在我这份新鲜反编译里）、
  `InputSystem.cs:21`、`UserSettings.cs:1743`、以及 `NetworkManager.cs:119/131-147/391-409`
  这些非状态机部分。
- **`extracted_assemblies/` 的文件名是 blob 标签，不是程序集真名。** 这不是猜测，是实测：
  - `System.ComponentModel.TypeConverter.dll` 里装的是 `VirtualDesktop.VrApp` / `VrActivity`
    （真名 `VirtualDesktop.Android`，即 blob entry #81）
  - `Xenko.dll` 里装的是 `namespace VirtualDesktop.Mobile`（真名 `VirtualDesktop.Mobile`，entry #52）
  - `SmartAssembly.Attributes.dll` 里装的是 `namespace VirtualDesktop.Interfaces`（`NetHelper` / `Computer`）
  - `VirtualDesktop.Mobile.Shared.dll` 里装的是 `namespace Oculus.Platform`
  - `VirtualDesktop.Net.dll` 与 `entry43.dll` 字节相同，装的是 OpenTK（entry #43）
  引用程序集时务必用真名，用文件名的结论会错。

---

## 1. 客户端会拨的出口（穷举）

| 出口 | 何时拨 | 证据 |
|---|---|---|
| `https://america.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry` | **仅当** `accessTokens.Item1.UserProof` 非空 | `NetHelper.GetServerUrl`（新鲜反编译 `sa/VirtualDesktop.Interfaces\NetHelper.cs:47-53`）；调用条件 `NetworkManager.GetComputersAsync` 里 `if (americaProofValid)`（新鲜反编译 `NetworkManager.cs:2406/2411/2417/2467-2470`） |
| `https://europe.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry` | 同上，取第二个 token | 同上（`NetHelper.cs:51`；`NetworkManager.cs:2471-2474`） |
| 硬编码 IP `20.225.41.170`（America）/ `40.89.161.236`（Europe） | `NetHelper.GetServerIP()` 的返回值；**调用点我查不到** | `NetHelper.cs:24-25, 38-45` |
| `https://virtualdesktopaiamerica.azurewebsites.net/api/getvocalanswer/{user}/{lang}/{q}` | 只有用户点 Assistant 才拨 | `Assistant.cs:340`（新鲜反编译）；`il_ldstr_raw.json` 归到 `VirtualDesktop.Mobile.Assistant/<SpeakAnswerAsync>d__26.MoveNext` |
| Azure Speech `southcentralus` + 客户端内嵌订阅 key `35ca2995766a4b76a16991d5b090fe7f` | 只有 Assistant 语音输入 | `Assistant.cs:388-389`（新鲜反编译） |
| Meta 平台 SDK（`OVRPlatform.InitializeAndroidAsync` / `IsViewerEntitledAsync` / `GetLoggedInUserAsync`） | 启动必跑，但走**头显本机 JNI**，不是网络 | `VrApp.<CreateAccessTokenAsync>d__30.MoveNext:291/312/313`（新鲜反编译 entry #81）；AppId 常量 `VrApp.cs:331` `QuestAppId = "2017050365004772"` |
| `OVRPlatform.GetUserProofAsync()` ×2 | 启动必跑，**离线必失败** | `VrApp.<GetAccessTokenAsync>d__31.MoveNext:779-780`（新鲜反编译 entry #81） |

`www.vrdesktop.net` 只出现在提示文案里（"Download and re-install the Streamer app from the
website: www.vrdesktop.net"），**不是被拨的端点**（`il_ldstr_raw.json` 归到
`NetworkManager/<ConnectToComputerAsync>d__66.MoveNext`）。
两个 registry 端点**不在** `ldstr` 表里（`NetHelper.GetServerUrl` 是方法体内的字面量，
本地化抽取器没抓），是我对 `extracted_assemblies\` 全量 dll 做原始字节扫描得到的：
全 store 里带 `vrdesktop.net` 的 http(s) 字面量**只有这两个**；客户端 IL 中唯一的
`ldstr` 型 http(s) URL 只有 AI 端点那一条。

---

## 2. 依赖表

> 「补丁后仍需要」= 对「补丁后基线在同网段起一次桌面串流」而言。

| # | 依赖 | 补丁后仍需要？ | 证据（file:line / patch-profile 行） | 若被移除，是哪个补丁 |
|---|---|---|---|---|
| D1 | UDP `255.255.255.255:38850` 局域网广播发现 | **需要，且离线时是电脑列表的唯一来源** | `ComputerDiscoveryClient..cctor`：`BroadcastEP = new IPEndPoint(IPAddress.Broadcast, 38850)`（新鲜反编译 `xc\VirtualDesktop.Net\ComputerDiscoveryClient.cs:317-318`）；单轮搜索窗 3000 ms：同文件 `:362-367`；唯一调用点 `NetworkManager.cs:2395` | 未被任何补丁触碰（`ComputerDiscoveryClient` 所在 blob 成员不在 `binary_patch.py` 的 5 个输出文件里） |
| D2 | TCP `38810/20/30/40` 四通道（控制/数据/视频/音频） | **需要** | `NetworkManager.Initialize` 建 4 个 `NetClient(NetClient.AnyEndPoint)`：`NetworkManager.cs:131-144` | 未被触碰 |
| D3 | 云 registry `BeginGetComputers2`（两个 region） | **不需要**。`UserProof` 为空时**根本不拨号**，直接置 `IsComputerRegistryOffline=true` 并只走本地 | `NetworkManager.cs:2435-2463`（`!americaProofValid && !europeProofValid` 分支）；`NetworkManager.cs:2467/2471` 的 `if (americaProofValid)` 守卫 | 不是被移除，是**被条件跳过** |
| D4 | Meta 平台 entitlement（`IsViewerEntitledAsync`） | 执行，但**不是网络依赖**（头显本机 JNI 服务），且失败不阻断 | `VrApp.cs` d__30:312；失败即 `CurrentProcess.Kill()`（d__30:310），该调用被 entry #49 置空 | entry #49：`CurrentProcess.Kill()` body → `ret`（`binary_patch.py:226-254`，RVA `0x3ADC`） |
| D5 | `OVRPlatform.GetUserProofAsync()` | 执行，离线必失败 → `UserProof` 保持 null，从而触发 D3 的跳过 | `VrApp.cs` d__31:779-780，`TimeoutAfter(..., timeout, null)`（timeout = 3 s 或 12 s） | 未触碰（也不需要碰：失败即跳过云） |
| D6 | Pico / Viveport / Steam / PlayForDream / Google 平台 SDK 分支 | Quest 上**不执行** | `VrApp.cs` d__30:252-275 按 `DynamicSettings.Platform` 分派，Quest 走 `case 0/1` | — |
| D7 | Google Play Integrity（`GooglePlayManager.GetIntegrityTokenAsync`） | Quest 上不执行 | `VrApp.cs` d__31:817-827（`platform == 5` 分支） | — |
| D8 | Play 商店许可 `Google.Android.Vending.Licensing` | 存在于 store，但客户端六棵反编译树里**零引用** | 对 §2 D13 那六棵树 grep `Google.Android.Vending\|LicenseChecker\|ServerManagedPolicy\|APKExpansionPolicy`，**6/6 命中 0** | `[未验证]`：只有 AOT 编译路径可能引用，但 168 个 AOT `.so` 已被全删，运行时不存在 |
| D9 | Assistant 语音问答主端点 | 只影响 Assistant 面板，**与串流无关** | `Assistant.cs:340` | 未触碰 |
| D10 | Azure Speech STT | 同上 | `Assistant.cs:388-389` | 未触碰 |
| D11 | Streamer 版本协商 / "需要更新"检查 | **纯 LAN 消息，不出网** | `CheckForStreamerUpdateAsync`：`NetMessage.CreateOutgoing(MessageType.UpdateRequired).SendTo(MessagingClient)` + 10 s 超时（新鲜反编译 `NetworkManager.cs:2610-2613`；常量 `NetworkManager.cs:391`） | 未触碰 |
| D12 | Streamer 版本门槛（`1.20.3` Windows / `1.34.0` mac / `1.20.17` 加密） | 纯本地比较 `computer.StreamerVersion` | `NetworkManager.cs:403-409`；比较点 `NetworkManager.cs:768/782` | 未触碰 |
| D13 | TLS 证书校验 | **未被禁用**，客户端里不存在任何 `ServerCertificateValidationCallback` / `ServicePointManager` / `DangerousAcceptAny*` | 对 `VirtualDesktop.Mobile` / `Mobile.Shared` / `Interfaces` / `Android` / `Net` / `Core` 六棵新鲜反编译树全量 grep（`ServerCertificateValidation\|ServicePointManager\|SecurityProtocolType\|DangerousAcceptAny\|RemoteCertificateValidation`），**6/6 命中 0** | — |
| D14 | 同网段/无线/千兆判定 | 纯本地计算（`NetworkInterfaceHelper.GetPrivateAddresses`） | `NetworkManager.cs:1885` | 未触碰 |
| D15 | 头显 Wi-Fi 连通性（`PerfStatsHelper.GetWifiMetrics`） | 本地；仅用于把警告文案从「不可达」细化成「Not connected to Wi-Fi」 | `NetworkManager.cs:2438-2441` | 未触碰 |
| D16 | Wi-Fi 锁 `CreateWifiLock(4, "VRD")` | 设备侧，PC 不可查；抑制 Wi-Fi 休眠，与能否连上无关 | `VrApp.cs:80` | 未触碰 |
| D17 | **`GetHasValidIdentityAsync()` 里的 APK 签名哈希门** | **需要 —— 但补丁后的重签名 APK 过不了（见 §3.3）** | `UserSettings.GetHasValidIdentityAsync`：`signature.GetHashCode() - 22 == 1778352230 && this._hasValidIdentity`。工作区 `apk_patch\decompiled\xenko\VirtualDesktop.Mobile\UserSettings.cs:1743`；`01-endpoint-inventory.md` 用另一版反编译记为 `:1509-1520`（代码字面一致，行号口径不同） | **无补丁覆盖** |
| D18 | ICMP ping `8.8.8.8`（`TraceRoute`，NAT 分类） | **同网段 LAN 会话里不执行** | 调用点被门控：`NetworkManager.cs:678-682` `if (!computer.IsOnSameNetwork && computer.AllowRemoteConnections) TraceRoute.GetRoutingStatusAsync()`；目标地址常量 `TraceRoute.cs:274` `IPAddress.Parse("8.8.8.8")`，实参 `TraceRoute.cs:133/291` `-c 1 -W 1000 -t {ttl} 8.8.8.8`。只在**远端且不可达**时跑，只决定 "behind a double NAT / CGNAT" 这类**文案** | 未触碰 |
| D19 | 崩溃 / 错误上报通道 | **客户端侧不存在** | 对 §2 D13 那六棵树 grep `ExceptionReporter\|SendExceptionEmail\|SmartAssemblyException\|ErrorReport` = **0 命中**；`SmartAssembly` 只出现在 `sa\SmartAssembly.Attributes.csproj` 这个**文件名**里。HTTP:80 错误上报是 Streamer 侧（`Xenko.Net` 混淆器）的，不在 Quest 侧 | — |

**D18 补一句**：`8.8.8.8` 在 `01` 的清单里被描述为「无条件打」。按客户端代码它**不是**无条件的 ——
同网段（`IsOnSameNetwork == true`）时那段 `if` 进不去。`01` 观察到的「无条件」应是在 Streamer 侧
或未反编译的状态机里。LAN 诊断不应因此报「客户端在打外网」。

---

## 3. 直接回答：补丁后的客户端，起局域网会话需要外网吗？

分三层，别混。

### 3.1 原版设计层面：不需要

这是产品自己写进代码的行为，不是补丁造出来的：

- 云查询有显式的离线分支，且写死了两档超时
  `ComputerRegistryTimeout = 12s` / `ComputerRegistryOfflineTimeout = 3s`
  （`NetworkManager.cs:397/400`），并且按上一轮的 `IsComputerRegistryOffline` 选档
  （`NetworkManager.cs:1623-1624`）。
- 云查询**只在 `UserProof` 非空时才发起**（`NetworkManager.cs:2467/2471`）。离线时 `UserProof`
  必然为空，于是**一个 registry 包都不发**，直接走本地。
- 客户端文案自己承认这件事，且这些字符串在**未打补丁的** `VirtualDesktop.Mobile.dll` 里
  （`il_ldstr_raw.json` → `NetworkManager/<GetComputersAsync>d__69.MoveNext`）：
  `"Meta servers unreachable, only showing local computers"`、
  `"… not responding, only showing local computers"`、`"Virtual Desktop servers partially unreachable"`。
- 局域网发现包**不含任何平台证明**：`CreateBroadcastMessage()` 只写
  RSA 公钥 XML + `0x00` + 1 字节 `Platform` + UTF-8 `AccountID`
  （新鲜反编译 `ComputerDiscoveryClient.cs:472-502`）。没有 `UserProof`、没有 entitlement。
- 云路径失败后没有任何"必须联网才能继续"的断言，只有 `ComputerRegistryWarning` 这类提示。

**所以：缺外网不会让补丁基线「连不上」。** 联网只会让电脑列表多几个条目。

### 3.2 补丁没有引入任何新的网络依赖

逐项核对 `binary_patch.py` 的 5 个程序集：

| entry | 程序集（真名） | 补丁内容 | 与网络有关？ |
|---|---|---|---|
| #43 | `VirtualDesktop.Net` | `EglContext.MakeCurrent` 改 surfaceless（`binary_patch.py:166-211`） | 否，OpenGL |
| #49 | `VirtualDesktop.Core` | `CurrentProcess.Kill()` body → `ret`（`binary_patch.py:226-254`） | 否，但**关掉了鉴权失败后的自毁** |
| #52 | `VirtualDesktop.Mobile` | 输入/键盘/淡入淡出/cylinder 共 27 处（`binary_patch.py:264-570`） | 否 |
| #61 | `Xenko.VR` | `OpenXRHMD.ReleaseVR` → `ret`（`binary_patch.py:581-612`） | 否 |
| #81 | `VirtualDesktop.Android` | 4 处 `Environment.Exit` + 8 处 `CreateAccessTokenAsync` 的 `Kill/Exit`（`binary_patch.py:626-688`） | 否，**只删自毁** |

**端口、发现协议、UDP 38850 包格式、TCP 38810-40 通道、TLS 校验、更新检查——一个字节都没改。**
（`ComputerDiscoveryClient` 所在 blob 成员在盘上是 `Xenko.Core.Serialization.dll`；
`binary_patch.py` 只提取 entry `43/49/52/61/81` 五个（`binary_patch.py:158/223/265/582/623`），
写出的 5 个 patched 文件里也没有它，所以它整份未动。§7 ① 的 diff 也证明 `VirtualDesktop.Mobile`
（唯一含网络状态机的那一份）总共只改了 130 字节、12 个区间，全在 UI/输入/渲染路径上。）

### 3.3 但是：静态证据显示，补丁后基线有一个**与网络无关**的阻断

这条必须写进报告，因为它决定 VDHelper 该不该把「发现失败」归因到网络上。

`UserSettings.GetHasValidIdentityAsync()`（`UserSettings.cs:1743`）：

```csharp
Signature signature = FirstOrDefault(PackageManager.GetPackageInfo(PackageName, 64).Signatures);
if (signature == null) return false;
return signature.GetHashCode() - 22 == 1778352230 && this._hasValidIdentity;
```

它是局域网发现的**总闸**（新鲜反编译 `NetworkManager.cs:2395`）：

```csharp
discoveryTask = GetHasValidIdentityAsync().ContinueWith(t =>
    (!t.Result) ? EmptyComputersResult : discoveryClient.FindComputersAsync(accessTokens.Item1));
```

**这里有一个必须点破的细节，否则容易读反**（`01-endpoint-inventory.md` 的初稿就读反了）：
`discoveryClient` 是在 `GetHasValidIdentityAsync()` **之前**就 `new` 出来的，但这**不代表广播已发出**。
`ComputerDiscoveryClient` **没有实例构造函数** —— 我把它反编译后列全了成员，只有
`static ComputerDiscoveryClient()`（建 `BroadcastEP` / `ListeningEP` / `ComputerSerializer` /
`_broadcastAes`，**不建 socket、不 Send**）与 `FindComputersAsync` / `StopSearch` / `StopListening` /
`Dispose` / `CreateBroadcastMessage`。**唯一创建 `UdpClient` 并 `Send` 的代码在 `FindComputersAsync`
内部**（`ComputerDiscoveryClient.cs:346` `new UdpClient((AddressFamily)2)` → `:358`
`_broadcastClient.Send(array, array.Length, BroadcastEP)`）。
而全 store grep `FindComputersAsync` 只有 4 处命中、**唯一 call site 是
`NetworkManager.cs:100`**，即上面那个 lambda 的 then 分支。`t.Result == false` 时该 lambda
**根本不被求值** ⇒ 广播窗口（固定 3000 ms）不会启动。
**构造 ≠ 发起搜索。** 这是本节成立的支点。

`GetHasValidIdentityAsync()` 返回 false ⇒ `FindComputersAsync` 永不被调用 ⇒ 头显永不发
UDP 38850 ⇒ `_computers.Count == 0` ⇒ UI 报 `"No computer found"`。

`1778352252`（= `1778352230 + 22`，`InputSystem.cs:21` 直接用前一个数）是**官方 Google Play
签名证书的 Android `Signature.hashCode()`**。我把 `VrApp.Signature` 常量里的证书 DER 取出，
按 `android.content.pm.Signature.hashCode()` 的公式（`31*h + Arrays.hashCode(der)`，从 1 起）
算了一遍：

```
der len 939
hashCode = 1778352252
diff to 1778352252 = 0
```

补丁 APK 用的是自签名 `vdpatch` 密钥，证书 SHA-256 `aad0b756…`（`SIGNING.md:18`），
与官方证书不同 ⇒ `Signatures[0].GetHashCode() != 1778352252` ⇒ 该方法恒为 false。

这段 IL **没有被任何补丁改过**：`extracted_assemblies\Xenko.dll` 与
`patched_assemblies\Xenko.dll` 逐字节 diff 只有 **130 字节、12 个区间**
（`0xdea0`、`0xec43…0xec86`、`0xf2cc…0xf2fd`、`0x10ae9`、`0x13c4b…0x13c57`、
`0x13eae…0x13ebc`、`0x36649…0x36688`、`0x37415…0x37445`），而两个常量分别落在
**文件偏移 `0x318cc`（`UserSettings`）与 `0x13126`（`InputSystem..ctor`）**，都不在这些区间里。

这解释了为什么补丁基线**不会自杀**却可能**发现不到电脑**：

- `InputSystem..ctor`（`InputSystem.cs:21-79`）里的签名门会 `Task.Delay(10).ContinueWith(CurrentProcess.Kill())`
  + `Post(Activity.Finish())`。`Finish()` 被 `binary_patch.py:274-277`（RVA `0x38448`）NOP 掉；
  `CurrentProcess.Kill()` 被 **entry #49 整体置空**，所以这两条都不致命。
  （`InputSystem.Update` 里那条 `Process.KillProcess` 是死代码：`_checkedSignature` 在 ctor
  末尾无条件置 true，`InputSystem.cs:80 / 124-127`。）
- 但 `GetHasValidIdentityAsync` 返回的是**布尔值**，entry #49 管不到它。

**这一条与 `HANDOFF.md:38-44`「自动发现 PC Streamer ✅ 自动连接」冲突。** 项目自己的文档也确认
签名门是刻意不碰的（`BINARY_EXPERIMENT_NEXT_STEPS_20260623.md:177-181` 把
`InputSystem identity/signature gates` 列入 do-not-patch；`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:324-337`
的 IL 字节证明也只覆盖了 `Finish()` 那个 lambda）。

**唯一没跑的一环**：我没有在设备上验证 `Android.Content.PM.Signature.GetHashCode()` 的运行时返回值
等于 Java 侧 `Signature.hashCode()`（APK 里 `Mono.Android.dll` / `Mono.Android.Runtime.dll` 都只是
5 KB facade，实体实现不在 blob 里，无法反编译核对）。

**一次实验即可了结**：头显在线时抓一次包，看有没有发往 `255.255.255.255:38850` 的 UDP。

- 有包 → 我这条结论错了，§3.3 整节作废，其余各节不受影响；
- 没包 → 「发现失败」的主因是 APK 重签名而非网络，VDHelper 的发现类检查必须能区分这两种情况。

### 3.4 一句话结论

**不需要外网。** 补丁后的基线起局域网会话，对外网的依赖只有「有则更好」：有外网时
`GetUserProofAsync` 成功、云 registry 返回条目，电脑列表更全；没有外网时这些条目直接消失，
**不影响 UDP 38850 发现和 TCP 38810-40 串流**。真正可能让「发现」失败的是 §3.3 的签名门，
而那**不是网络问题**。

---

## 4. 对 PC 侧工具意味着什么

判定标准只有一条：**这个检查在补丁基线上会不会常亮、亮了有没有信息量。**

### 4.1 该查（都是补丁基线上真正会咬人的东西）

| 检查 | 判据 | 为什么值得 |
|---|---|---|
| **UDP 38850 方向性观测** | 在 PC 上监听 38850，看头显是否真的发来发现包（每轮刷新一次，搜索窗 3 s：`ComputerDiscoveryClient.cs:362-367`） | **唯一能把「网络/防火墙问题」和「客户端根本没发包」分开的判据**。§3.3 那种情况下这一项会是红的，但原因不是网 |
| Streamer 进程在跑 | 进程存在 | `"No computer found"` 的另一半文案就是 "Make sure your computer is running the Streamer app"（`NetworkManager.cs:1910`） |
| Streamer 版本 | `computer.StreamerVersion` 门槛 1.20.3 / 1.34.0 / 1.20.17（`NetworkManager.cs:403-409`） | 版本不够时客户端会拒连，文案是 `"Streamer on your PC needs to be updated"` |
| TCP 38810/20/30/40 监听 + 防火墙 | 四端口全 LISTEN 且允许入站 | 四通道缺一即连不上（`NetworkManager.cs:131-144`） |
| 同网段 / 同 SSID / 无 AP 隔离 | PC 主地址与头显 IP 同段 + 双向可达 | 客户端自己也这么判（`NetworkManager.cs:1885` `IsOnSameNetwork`） |

### 4.2 不该查（噪声，在补丁基线上必然常亮且没有信息量）

| 不该做的检查 | 为什么是噪声 |
|---|---|
| `*.vrdesktop.net:443` 可达性（覆盖矩阵 B9 那一项） | 补丁基线**没有 UserProof 时一个 registry 包都不发**（`NetworkManager.cs:2467/2471` 守卫）。这项要么永远绿、要么红了也跟连不上无关。B9 应改判为「**不适用于补丁基线**」，而不是「PC 侧可查」 |
| `virtualdesktop.net` / `*.vrdesktop.net` DNS 解析 | 同上；且客户端里另有硬编码 IP（`NetHelper.cs:24-25`），解析与拨号路径不完全等价 |
| Meta / Oculus / Steam / Pico 账号登录态、entitlement、Play Integrity | 全在头显本地或平台 SDK 里，PC 侧读不到；且失败只影响云条目数（§3.1） |
| 客户端提示 `"servers partially unreachable"` 类文案的复现 | 那是官方 UI 文案，与本机健康无关 |
| Assistant 端点 / Azure Speech | 只影响语音问答面板（`Assistant.cs:340,388-389`） |
| TLS 证书链 / 信任库 | 客户端没做任何校验绕过（§2 D13），出问题会表现为「连不上」，不会表现为「证书错误」 |
| Streamer 自身的在线更新检查 | Streamer 版本协商走 LAN 消息，不出网（`NetworkManager.cs:2610`） |

### 4.3 给 B9 的处置建议（决定性）

把 `research\12-coverage-audit\01-coverage-matrix.md:95` 的 B9 行从
「PC 侧可查 / 无对应检测」改成：**不适用于补丁基线，属噪声，不得进入健康报告**。
理由是上表，不是推测：registry 调用在离线/无 UserProof 时被 `if (americaProofValid)` 短路，
不存在「官方远端发现服务可达性」影响串流的通路。

如果 Owner 想保留一条远端提示，唯一诚实的形式是**不做成布尔检查**，只在报告尾部作为
「已跳过（补丁基线不依赖）」的一行说明。

---

## 5. 补丁本身对网络行为的改变（端口/发现/证书/更新）

| 维度 | 改了吗 | 证据 |
|---|---|---|
| 端口 | **没改** | `binary_patch.py` 全文没有任何 socket/端口/host 相关补丁；5 个 entry 的补丁点逐条列在 §3.2 表 |
| 发现协议 | **没改** | `ComputerDiscoveryClient` 所在 blob 成员不在 patched 输出列表；`extracted` vs `patched` 的 `Xenko.dll` diff 12 个区间也都不在网络代码区 |
| 证书校验（TLS） | **没改，也没有任何自签名放行** | 六棵反编译树（同 §2 D13 的 grep）6/6 命中 0；两个 registry 端点都是 `https://` |
| APK 签名（发布签名） | **改了，而且这是唯一影响网络行为的改动** | `build_v13_anim.py:64-93` / `build_v12_no_aot.py:64-93` 删除 `META-INF` 旧签名后用 `vdpatch` 重签（`SIGNING.md:34-43`）；`SIGNING.md:18` 证书 SHA-256 `aad0b756…` |
| 更新检查 | **没改，且本来就不出网** | `CheckForStreamerUpdateAsync` 用 LAN 消息（`NetworkManager.cs:2610`） |
| 行为性改动：自毁 | **改了，且是全局的** | entry #49 把 `CurrentProcess.Kill()` 整体置空（`binary_patch.py:226-254`）。这一个补丁就让 `InputSystem..ctor`、`CreateAccessTokenAsync`（8 处）、`RefreshComputersAsync`（3 处 `Kill`）的 `Kill()` 全部变成空操作——比 entry #81 逐个 NOP 覆盖面大得多 |
| 行为性改动：AOT | **删了 168 个 `libaot-*.dll.so`，走 JIT** | `build_v13_anim.py:91` `Removed {N} AOT .so files`；`HANDOFF.md:110`。这不是网络改动，但**所有 IL 补丁只有在它成立时才生效** |
| 行为性改动：证书门失败后的 UI | 部分保留 | `Activity.Finish()` 在 3 处被 NOP（`binary_patch.py:274-277` / `:315` / `:327-329`）；但 `"Failed entitlement check"`、`"Unable to retrieve identity"`、`"You need to purchase the app in the Meta Quest store"` 这些**警告文案一个都没删**（`NetworkManager.cs:1747-1769`）。补丁基线用户会看到「你在商店没买这个」这类误导性提示，但会继续往下走 |

---

## 6. 交叉核对与未验证项

1. **bitrate 上限的程序集归属**（brief 点名要核的那条）
   `Xenko.VR.HmdResolutionTypeExtensions.GetMaxVRBitrate(HmdType, codec)` 定义在盘上文件
   `extracted_assemblies\Xenko.Rendering.dll` 里（我用 `ilspycmd -t` 拿到了该类）。该文件同时含
   `SharedUserSettings` / `SharedMobileSettings` / `SharedStreamerSettings` / `PermissionManager` /
   `GooglePlayManager` / `VideoFormatExtensions`，正是 "Mobile.Shared" 这一层的东西 →
   **与「不在 `Mobile.dll`」一致**。
   调用方在 `VirtualDesktop.Mobile` 里：`StreamingTab.cs:1726`、`PerformanceOverlay.cs:514`。
   `[未验证]`：我没能确认它的**真实程序集名**（store 用的是 blob 标签，标签不可信；我写的
   metadata 解析器在这些文件上没跑通）。要坐实"真名就是 `VirtualDesktop.Mobile.Shared.dll`"，
   需要从 blob 的 XABA 索引里读 entry→真名映射。**注意这条与补丁无关**——entry #81/#52 之外的
   程序集根本没被补丁碰过，上限行为不受补丁影响。
2. **38860 vs 38850**：`analysis\report_sections\02_networking_streaming.md:48` 写的是
   `ConnectionManager.cs:697` 的 `IPAddress.Broadcast, 38860`。我在 Quest 客户端侧找不到 38860；
   `ComputerDiscoveryClient..cctor` 用的两个端点都是 **38850**（`ComputerDiscoveryClient.cs:317-318`），
   本仓库 `research\07-vdapkpatcher\01-capability-inventory.md:54-57` 的 dnlib 全 store 扫描也是
   38850 = 2 处命中、38860 = 0 处。38860 大概率是 **PC 侧 Streamer** 的端口，不是头显的。
   归 `01-endpoint-inventory.md` 定夺。
3. `[未验证]` `NetHelper.GetServerIP()` 返回的两个硬编码 IP（`20.225.41.170` / `40.89.161.236`）
   在客户端的实际调用点——我没找到调用方，所以**不能**说客户端会直连 IP。
4. `[未验证]` `Google.Android.Vending.Licensing` 是否在补丁基线上还被引用（§2 D8）。
5. `[未验证]` §3.3 的设备侧确认。见 §3.3 末尾的一次抓包实验。
6. 覆盖矩阵 `:95` 里 `virtualdesktop.net:443` 解析到 `192.64.151.235`，与客户端硬编码的
   `20.225.41.170` / `40.89.161.236` 不一致。这两个数不冲突（一个是 DNS，一个是硬编码），
   但说明**不能用 DNS 解析结果代表 registry 可达性**。

---

## 7. 复现命令

```bash
# 客户端六个主要程序集（盘上文件名是 blob 标签，真名见行尾注释，见 §0）
ilspycmd -p -o nm  "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\Xenko.dll"                     # VirtualDesktop.Mobile
ilspycmd -p -o sa  "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\SmartAssembly.Attributes.dll"   # VirtualDesktop.Interfaces
ilspycmd -p -o xc  "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\Xenko.Core.Serialization.dll"  # ComputerDiscoveryClient
ilspycmd -p -o e81b "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\System.ComponentModel.TypeConverter.dll"  # VirtualDesktop.Android
ilspycmd -p -o msh "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\Xenko.Rendering.dll"               # VirtualDesktop.Mobile.Shared
ilspycmd -p -o e49 "F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\Xenko.OpenXR.dll"                 # VirtualDesktop.Core (CurrentProcess.Kill)

# ① 补丁前后逐字节 diff（entry #52 = VirtualDesktop.Mobile）  —— 实跑输出见下
cat > v1.py <<'PY'
a = open(r'F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\Xenko.dll','rb').read()
b = open(r'F:\Project\VirtualDesktop\analysis\apk_patch\patched_assemblies\Xenko.dll','rb').read()
d = [i for i in range(len(a)) if a[i] != b[i]]
rs = []
for i in d:
    if rs and i == rs[-1][1] + 1: rs[-1][1] = i
    else: rs.append([i, i])
print('diff bytes:', len(d))
print([(hex(s), hex(e)) for s, e in rs])
PY
python v1.py

# ② 官方证书的 Android Signature.hashCode() == 代码里的常量
cat > v2.py <<'PY'
import re
src = open(r'F:\Project\VirtualDesktop\analysis\apk_patch\decompiled\vdandroid\VirtualDesktop.Android\VrApp.cs', encoding='utf-8-sig').read()
der = bytes.fromhex(re.search(r'"([0-9a-fA-F]{500,})"', src).group(1))
h = 1
for x in der:
    h = (31 * h + (x if x < 128 else x - 256)) & 0xFFFFFFFF
s = h - 0x100000000 if h >= 0x80000000 else h
print('der len:', len(der), 'hashCode:', s, 'delta to 1778352252:', s - 1778352252)
PY
python v2.py
```

①② 的实跑输出（本机跑过，未改 `F:\` 任何文件）：

```
diff bytes: 130
[('0xdea0','0xdea0'), ('0xec43','0xec43'), ('0xec6f','0xec71'), ('0xec80','0xec83'), ('0xec85','0xec86'),
 ('0xf2cc','0xf2cf'), ('0xf2d1','0xf2d7'), ('0xf2d9','0xf2db'), ('0xf2e4','0xf2e7'), ('0xf2e9','0xf2ec'),
 ('0xf2ee','0xf2f1'), ('0xf2f3','0xf2f9'), ('0xf2fb','0xf2fd'), ('0x10ae9','0x10ae9'), ('0x13c4b','0x13c4e'),
 ('0x13c50','0x13c54'), ('0x13c56','0x13c57'), ('0x13eae','0x13eb0'), ('0x13eb2','0x13eb4'), ('0x13eb7','0x13eba'),
 ('0x13ebc','0x13ebc'), ('0x36649','0x3664b'), ('0x3664d','0x3664f'), ('0x36652','0x36655'), ('0x36657','0x36657'),
 ('0x3667a','0x3667c'), ('0x3667e','0x36680'), ('0x36683','0x36686'), ('0x36688','0x36688'), ('0x37415','0x37416'),
 ('0x37419','0x3741c'), ('0x3741e','0x37421'), ('0x37423','0x37429'), ('0x3742c','0x37432'), ('0x37434','0x37437'),
 ('0x37439','0x3743c'), ('0x3743e','0x37440'), ('0x37443','0x37445')]

der len: 939 hashCode: 1778352252 delta to 1778352252: 0
```

---

## 8. 与 `01-endpoint-inventory.md` / `02-discovery-protocol.md` 的对账（两份都在本文写作中途落地）

开工时 `01` / `02` 都不存在（§0）。收工时两份都已由其他 worker 写出。以下是我对它们的**修正与补充**：
PC 侧结论以 `02` 为准，端点清单以 `01` 为准，我只对「头显侧」和「补丁后还需不需要」负责。

**8.1 `02` 里 Quest 侧的几行 `查不到` 是找错了文件，不是真的查不到。**
`02` §6 第 359-361 行说「Quest 侧发给 38850 的请求包构造代码 —— `VirtualDesktop.Net.dll` 的 VD
自有类型在 AOT 后方法体已剥掉」。原因不是 AOT：**`extracted_assemblies\VirtualDesktop.Net.dll`
根本不是含 `ComputerDiscoveryClient` 的程序集**（它是 entry #43，里面全是 OpenTK；见 §0 的文件名
警告）。正确文件是 `extracted_assemblies\Xenko.Core.Serialization.dll`。反编译它即可拿到：

| `02` 标 `查不到` 的项 | 本文件的实测 |
|---|---|
| 头显侧发给 38850 的请求包构造 | `CreateBroadcastMessage()`：RSA 公钥 XML + `WriteByte(0)` + `WriteByte(Platform)` + UTF-8 `AccountID`，整体用静态 AES 密钥加密，`PaddingMode.None`。PC 侧看到的 17 字节 / >243 字节两分支对应的是 16 字节 ConnectionID 的 NAT 打洞包与这条加密包 —— **加密部分与 `02` §2 读到的 PC 端 AES Key/IV 是同一套静态常量**，两边对得上 |
| 头显侧是广播还是单播 | **广播**：`BroadcastEP` 在客户端**确实被读**（`FindComputersAsync:358` `_broadcastClient.Send(array, array.Length, BroadcastEP)`），与 `02` §7.1「PC 从不广播 38850」互补而非冲突：头显广播、PC 单播应答 |
| 头显是否也监听 38850 | **不监听**。客户端用 `new UdpClient((AddressFamily)2)`（未绑定端口）发+收，`ListeningEP` 在客户端文件里定义了但未被 `FindComputersAsync` 使用。所以 38850 端口**只有 PC 侧 bind**，`02` 的 D1/D2 检测项成立 |
| 请求周期 / 重试次数 | 每次 `FindComputersAsync` 只广播**一次**，随后在同一个 socket 上 `ReceiveAsync` 循环，**固定 3000 ms** 搜索窗（`num = 3000 - elapsed`）。周期由 `NetworkManager` 的刷新节奏决定（断连 3 s / 不可达 6 s / 需更新 50 s / 普通 50 ms 延迟，见 `NetworkManager.cs:311/316/319`） |
| Quest 端云注册失败后是否 fallback 到本地发现 | **是**，且 fallback 就在 `GetComputersAsync` 里（`NetworkManager.cs:2435-2463`）。⚠️ 但 fallback 路径的第一步被 §3.3 的签名门挡着 |

**8.2 我们对 `02` 的 D16 检测项（云端点可达性）有分歧，我的结论更强。**
`02` D16 建议查 4 个云端点（2 主机名 + 2 IP），并注明「只 Warn 不 Block」。本文件认为
**在补丁基线上它连 Warn 都不该出**：客户端侧 `if (americaProofValid)` 守卫（`NetworkManager.cs:2467/2471`）
意味着无有效 `UserProof` 时**一个 registry 包都不发**，这项检查与「连不上」之间没有因果通路。
两个文件独立读出的端点常量完全一致（客户端 `NetHelper.cs:24-25/51/53` ↔ `02` §-/.92.cs:21/23/127/133），
所以这不是「端点读错」，纯粹是「该不该查」的判断分歧。我的建议见 §4.3。

**8.3 §6 第 2 条（38860 vs 38850）已由 `02` 确认。**
`02` 查明 38860 是 **PC 侧** `ConnectionManager` 的 0 字节广播，且调用点 `查不到`；
头显侧代码里根本没有 38860。`report_sections\02_networking_streaming.md:48` 那张表把它
当成头显发现端口是错的。§6 第 2 条按此结论。

**8.4 与 `01-endpoint-inventory.md` 的对账**（它在我收工前落地，我读了它的 §2.1/§2.3/§6 摘要）。

- **采纳它的 §2.1 端点清单**（云端 IP 20.225.41.170 / 40.89.161.236、两个 registry HTTPS、
  `download.vrdesktop.net/files/version.txt` 仅 PC、AI 端点、Azure Speech、8.8.8.8），
  与我 §1 独立读出的常量**完全一致**。它额外指出远程中继端口段
  （38811–38816 / 38821–38826 / 38831–38836 / 38841–38846，由 `% 6` 展开）。
- **采纳它的关键机制**：`ConnectToComputerAsync` 按 `computer.UdpEndPoint` 是否为 null 分流 ——
  只有广播发现来的 PC 才有 `UdpEndPoint`（`ComputerDiscoveryClient.cs:128`
  `computer.UdpEndPoint = udpReceiveResult.RemoteEndPoint`），而 `UdpEndPoint` 没有 `[DataMember]`，
  云注册表不带它。所以同网段 + 广播通 ⇒ 云端一次都不碰。这**加强**而非削弱 §3.4 的结论。
- **它的第 2 条我判定为错，已回复纠正**（详见 §3.3 加粗段）：`discoveryClient` 提前 `new` 出来
  **不等于**广播已发出，因为 `ComputerDiscoveryClient` 没有实例构造函数，唯一建 socket 并
  `Send` 的代码在 `FindComputersAsync` 内部，而它只有一个 call site 且在 `t.Result == true`
  的分支里。
- **它说的「8.8.8.8 无条件打」「SmartAssembly 错误上报无条件发 HTTP:80」，客户端侧均不成立**：
  前者被 `NetworkManager.cs:678-682` 的 `!IsOnSameNetwork && AllowRemoteConnections` 门控（§2 D18），
  后者在 Quest 侧六棵反编译树里零命中（§2 D19）—— 那套上报在 Streamer 侧。
- **分歧仍在 D16/云端点检查**：`01` 建议查 4 个云端点并「只 Warn 不 Block」，我主张
  **整条删掉**（§4.3）。理由是客户端侧 `if (americaProofValid)` 守卫，与「连不上」无因果通路。
  这条需要 Main 拍板。
