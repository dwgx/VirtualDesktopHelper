# 02 · 许可证判定 · 可搬代码候选 · 功能空白分析

配套文件：`01-inventory.md`（数据来源）。本文只回答三个问题：
**① 哪个许可证允许把代码搬进 .NET 项目；② 具体搬哪些文件、哪些类、依赖什么；③ 哪些功能已经有成熟开源方案（别重造），哪些是空白（我们的机会）。**

本项目当前工程底座：`src/VdHelper/VdHelper.csproj:4` → `<TargetFramework>net10.0-windows</TargetFramework>`，`<UseWPF>true</UseWPF>`，**目前零 `PackageReference`**。

---

## 1 · 许可证判定

判定口径：
- **可复用** = OSI 认可且允许再分发+商用+闭源衍生（MIT / Apache-2.0 / BSD / ISC 等）。
- **不可复用** = GPL 系（传染）、NOASSERTION 且非 OSI 许可证（Nmap License、Hotrian 自定义）、或**根本没有 LICENSE 文件**（默认「保留一切权利」，GitHub 的 Terms of Service 只给查看/派生 fork 权，**不构成再分发许可**）。

### 1.1 可复用（MIT / Apache-2.0）

| 仓库 | API 返回的 spdx_id | 判定 | 证据 |
| --- | --- | --- | --- |
| falahati/WindowsFirewallHelper | MIT | **可复用** | https://api.github.com/repos/falahati/WindowsFirewallHelper |
| JeremyAnsel/SharpOpenNat | MIT | **可复用** | https://api.github.com/repos/JeremyAnsel/SharpOpenNat |
| dotpcap/sharppcap | `null`（NO-LICENSE-FILE） | **可复用（附条件，见下）** | https://api.github.com/repos/dotpcap/sharppcap |
| SideQuestVR/SideQuest | MIT | 可复用 | https://api.github.com/repos/SideQuestVR/SideQuest |
| alvr-org/ALVR | MIT | 可复用（Rust，不进 .NET） | https://api.github.com/repos/alvr-org/ALVR |
| mbucchia/VirtualDesktop-OpenXR | MIT | 可复用（C++/native，不进 .NET） | https://api.github.com/repos/mbucchia/VirtualDesktop-OpenXR |
| mbucchia/OculusXR-Compatibility | MIT | 可复用（同上） | https://api.github.com/repos/mbucchia/OculusXR-Compatibility |
| mbucchia/OpenXR-Toolkit | MIT | 可复用（同上） | https://api.github.com/repos/mbucchia/OpenXR-Toolkit |
| michael-mueller-git/VirtualDesktopTimecodeServer | MIT | 可复用（C#，但无网络排查价值） | https://api.github.com/repos/michael-mueller-git/VirtualDesktopTimecodeServer |
| AtlasTheProto/ADBForwarder | MIT | 可复用（C#，但已 archived） | https://api.github.com/repos/AtlasTheProto/ADBForwarder |
| pattycoder01/quest-link-fixer | MIT | 可复用（单个 .bat，逻辑极简） | https://api.github.com/repos/pattycoder01/quest-link-fixer |
| ludoven/QADB | MIT | 可复用（Kotlin，不进 .NET） | https://api.github.com/repos/ludoven/QADB |
| Watash1no/open-quest-hub | MIT | 可复用（Rust，不进 .NET） | https://api.github.com/repos/Watash1no/open-quest-hub |
| ovsky/ADBO | MIT | 可复用（Batchfile） | https://api.github.com/repos/ovsky/ADBO |
| QuestMods/QuestHomeSwitcher | MIT | 可复用 | https://api.github.com/repos/QuestMods/QuestHomeSwitcher |
| EdwardOconnell/powershell-network-security-toolkit | MIT | 可复用（PowerShell 模块） | https://api.github.com/repos/EdwardOconnell/powershell-network-security-toolkit |
| DigitalRuby/IPBan | MIT | 可复用（只借思路，不整包引） | https://api.github.com/repos/DigitalRuby/IPBan |
| lc700x/desktop2stereo | MIT | 可复用（Python，不进 .NET） | https://api.github.com/repos/lc700x/desktop2stereo |
| DenTechs/Virtual_Desktop_Body_Tracking_Configurator | MIT | 可复用（Python，不进 .NET） | https://api.github.com/repos/DenTechs/Virtual_Desktop_Body_Tracking_Configurator |
| zapabob/VRChatSettingTool | MIT | 可复用（PowerShell 脚本片段） | https://api.github.com/repos/zapabob/VRChatSettingTool |
| combatwombat/tiefling | MIT | 可复用（JS，不进 .NET） | https://api.github.com/repos/combatwombat/tiefling |
| dwgx/Quest-ADB-Dashboard（Owner 自有） | MIT | 自有，随便用 | https://api.github.com/repos/dwgx/Quest-ADB-Dashboard |

### 1.2 SharpPcap 的许可证特殊情况（必须照实说）

GitHub API 对 `dotpcap/sharppcap` 返回 `license.spdx_id = null`，**这不是「无许可证」**。该仓库用 REUSE 规范声明许可证，`gh api repos/dotpcap/sharppcap/contents/` 的根目录列表里确实有 `LICENSES` 与 `REUSE.toml`：

```text
$ gh api repos/dotpcap/sharppcap/contents/ --jq '.[].name'
... .gitattributes  .github  ...  LICENSES  README.md  REUSE.toml  SharpPcap  SharpPcap.sln ...

$ gh api repos/dotpcap/sharppcap/contents/LICENSES --jq '.[].name'
MIT.txt

$ gh api repos/dotpcap/sharppcap/contents/REUSE.toml --jq '.content|@base64d'
# Copyright 2023-2024 Ayoub Kaanich <kayoub5@live.com>
# SPDX-License-Identifier: MIT
version = 1
SPDX-PackageName = "sharppcap"
...
SPDX-License-Identifier = "MIT"
```

**判定：MIT，可复用。** 依据：<https://github.com/dotpcap/sharppcap/blob/master/REUSE.toml> 与 `LICENSES/MIT.txt`。
附带义务：仓库采用「聚合声明」，`Examples/**`、`SharpPcap.sln`、`Test/capture_files/**` 由他人版权声明（Tamir Gal / Chris Morgan）聚合进来 —— 引用示例代码时要保留其声明。

### 1.3 不可复用（GPL / 非 OSI / 无许可证 / 仅二进制）

| 仓库 | spdx_id | 为什么不能搬 |
| --- | --- | --- |
| wireshark/wireshark | GPL-2.0 | 强传染；只能用外部 `tshark.exe` 不能链接进 VDH |
| WiVRn/WiVRn | GPL-3.0 | 强传染，且是 C++/native |
| nmap/nmap | NOASSERTION | Nmap License 非 OSI；只能外部调用 `nmap.exe` |
| Hotrian/OpenVRDesktopDisplayPortal | NOASSERTION | 自定义/无法映射许可证 |
| guygodin/VirtualDesktop | NO-LICENSE-FILE | 保留一切权利；**且仓库只有 README.md，`languages` API 返回 `{}`，根本没有代码** |
| SoumyaRanjanPatnaik/VirtualDesktopQuest | NO-LICENSE-FILE | 保留一切权利；**且只有采集后端骨架，没有网络代码可参考** |
| QuestMods/QuestHome | NO-LICENSE-FILE | 保留一切权利 |
| MichaelJW/DorsalVR | NO-LICENSE-FILE | 保留一切权利 |
| DigitalGenesisDev/QuestToolboxV2 | NO-LICENSE-FILE | 保留一切权利 |
| yveshughes/remote-desktop-for-ubuntu | NO-LICENSE-FILE | 保留一切权利 |
| korejan/ALXR-nightly | NO-LICENSE-FILE | 保留一切权利（发布仓库，本无源码） |
| **dwgx/VirtualDesktop**（Owner 自有） | NO-LICENSE-FILE | 保留一切权利 —— 自己的东西，自己决定；**但若对外分发需先补 LICENSE** |
| **dwgx/VirtualDesktopHelper**（Owner 自有） | NO-LICENSE-FILE | 同上 |
| dwgx/VRCSM（Owner 自有） | NOASSERTION | 无法映射，需查仓库内 LICENSE 文件后再定 |
| `reference/vdapkpatcher/`（本地素材） | 查不到 | 该目录没有 `.git`、没有任何 `github.com/...` 出处 → **上游许可证未知，不得搬运** |

### 1.4 一句话结论

**能进 .NET 项目的只有 3 个真实候选：`WindowsFirewallHelper`（MIT）、`SharpOpenNat`（MIT）、`SharpPcap`（MIT via REUSE）。**
其余 C#/C++ 的好东西（ALVR、SideQuest、VDXR、QAdb）都是别的语言或别的形态，只能读。

---

## 2 · 代码可直接搬进 .NET 项目的候选

> 下列文件路径与类名均为 `gh api repos/<owner>/<repo>/git/trees/<branch>?recursive=1` 的**实测输出**，不是推测。

### 2.1 WindowsFirewallHelper —— 防火墙规则读写 + 网段/网关判定

- 仓库：<https://github.com/falahati/WindowsFirewallHelper>（MIT，301★，最后 push 2025-02-24）
- NuGet：`WindowsFirewallHelper` 最新版 **2.2.0.86**
  （证据：<https://api.nuget.org/v3-flatcontainer/windowsfirewallhelper/index.json>）
- TFM：`<TargetFrameworks>netstandard2;net4;net5.0</TargetFrameworks>`（`WindowsFirewallHelper/WindowsFirewallHelper.csproj`）
- 依赖：仅 `System.ServiceProcess.ServiceController` 5.0.0（非 net4 分支）；`MSBump` 2.3.2 是 `PrivateAssets="all"` 的构建期工具。**无第三方运行时依赖。**
- 强签名：`SignAssembly=true` + `OpenSourceStrongNameSignKey.pfx`

关键文件 / 类名：

| 文件路径（在仓库内） | 关键类 | 我们用它做什么 |
| --- | --- | --- |
| `WindowsFirewallHelper/FirewallManager.cs` | `static class FirewallManager`（`Instance` / `Version` / `IsServiceRunning` / `RegisteredProducts`） | 统一入口，判断 `FirewallAPIVersion` 是 WAS 还是 Legacy |
| `WindowsFirewallHelper/FirewallWAS.cs` | `class FirewallWAS`（`Rules` / `Profiles`） | Windows 8+ 的 `INetFwPolicy2` 封装 —— 我们的主力路径 |
| `WindowsFirewallHelper/FirewallLegacy.cs` | `class FirewallLegacy` | XP/Win7 的 `INetFwPolicy` 路径，**我们大概率不需要，但可作为降级兜底** |
| `WindowsFirewallHelper/FirewallRules/FirewallWASRule.cs` / `FirewallWASRuleWin8.cs` / `FirewallWASRuleWin7.cs` | 规则对象 | 「检测项：VD Streamer 是否已被放行」的判定实体 |
| `WindowsFirewallHelper/Collections/FirewallWASRulesCollection.cs` | `FirewallWASRulesCollection`（`Add` / `Remove` / `ToArray` / LINQ） | 「修复项：补一条程序放行规则」 |
| `WindowsFirewallHelper/FirewallProfiles.cs` | `FirewallProfiles`（枚举遍历 profile） | 「检测项：Domain/Private/Public 三个 profile 分别是什么状态」 |
| `WindowsFirewallHelper/FirewallWASProfile.cs` | `FirewallWASProfile`（`Enabled` / `DefaultInboundAction` / `DefaultOutboundAction`） | 「检测项：出站被 block」直接可读 |
| `WindowsFirewallHelper/COMInterop/INetFwPolicy2.cs` / `INetFwRule3.cs` / `INetFwMgr.cs` / `COMTypeResolver.cs` | COM interop 层 | 我们自己手写 COM interop 时**可以直接对照**（但 MIT 已允许整包引用） |
| `WindowsFirewallHelper/Addresses/LocalSubnet.cs` | `LocalSubnet : SpecialAddress` | 「检测项：本机所在网段」 |
| `WindowsFirewallHelper/Addresses/DefaultGateway.cs` | `DefaultGateway : SpecialAddress` | 「检测项：默认网关」 |
| `WindowsFirewallHelper/Addresses/NetworkAddress.cs` | `NetworkAddress`（按子网表示地址范围） | 「修复项：只对本机网段放行，而不是全网」 |
| `WindowsFirewallHelper/Addresses/SingleIP.cs` / `IPRange.cs` | `SingleIP` / `IPRange : IAddress` | 单 IP / IP 段放行 |
| `WindowsFirewallHelper/FirewallDirection.cs` / `FirewallAction.cs` / `FirewallScope.cs` / `FirewallProtocol.cs` / `FirewallPortType.cs` | 枚举 | 组装规则参数 |

README 里已验证可用的调用形态（<https://github.com/falahati/WindowsFirewallHelper#readme>）：

```csharp
var rule = FirewallManager.Instance.CreateApplicationRule(...);
FirewallManager.Instance.Rules.Add(rule);
var allRules = FirewallManager.Instance.Rules.ToArray();
var myRule = FirewallManager.Instance.Rules.SingleOrDefault(r => r.Name == "My Rule");
FirewallManager.Instance.Rules.Remove(myRule);
foreach (var profile in FirewallManager.Instance.Profiles) { /* profile.* */ }
wasRule.Interfaces = NetworkInterface.GetAllNetworkInterfaces();   // ← 精确限定网卡，正是我们要的
```

⚠️ **[未验证]**：该包最新 TFM 只到 `net5.0`/`netstandard2.0`，能否在 `net10.0-windows` 上 restore + build 通过，**必须实跑一次 `dotnet add package WindowsFirewallHelper`** 才能下结论。若失败，退路是只抄 `COMInterop/` + `FirewallManager/FirewallWAS*` 那 20 来个文件进本项目（MIT 允许）。

### 2.2 SharpOpenNat —— NAT 类型 / UPnP 可用性 / 端口映射

- 仓库：<https://github.com/JeremyAnsel/SharpOpenNat>（MIT，12★，最后 push 2026-07-01）
- NuGet：`SharpOpenNat` 最新版 **4.0.19**（<https://api.nuget.org/v3-flatcontainer/sharpopennat/index.json>）
- TFM：`<TargetFrameworks>net8.0;net6.0;net48;netstandard2.0</TargetFrameworks>`（`SharpOpenNat/SharpOpenNat/SharpOpenNat.csproj`）→ **net8.0 资产可直接被 net10.0-windows 消费**
- 依赖：csproj 内无第三方 `PackageReference`

| 文件路径 | 关键类 | 我们用它做什么 |
| --- | --- | --- |
| `SharpOpenNat/SharpOpenNat/Discovery/Searcher.cs` + `ISearcher.cs` | `ISearcher` / `Searcher` | SSDP M-SEARCH 发现，支持 `PortMapper.Upnp` 与 `PortMapper.Pmp` 两种协议 |
| `SharpOpenNat/SharpOpenNat/INatDiscoverer.cs` | `INatDiscoverer` | `OpenNat.Discoverer.DiscoverDeviceAsync(PortMapper.Upnp, token)` |
| `SharpOpenNat/SharpOpenNat/INatDevice.cs` | `INatDevice` | `GetExternalIPAsync()` → **「检测项：外网 IP / 是否在 NAT 后」**；`GetSpecificMappingAsync(Protocol, int, ct)` → 「检测项：这个端口在 NAT 上有没有映射」（**修正 2026-10-05**：4.0.19 的 `INatDevice` 没有 `GetMappedPortAsync`；已按上游 `INatDevice.cs` 逐行核实，公开面只有 HostEndPoint / LocalAddress / CreatePortMapAsync / DeletePortMapAsync / GetAllMappingsAsync / GetExternalIPAsync / GetSpecificMappingAsync） |
| `SharpOpenNat/SharpOpenNat/Mapping.cs`（同目录可见 `Enums/ProtocolType.cs`） | `Mapping`、`ProtocolType` | `CreatePortMapAsync(new Mapping(ProtocolType.Tcp, 1600, 1700, "name"))` → **「修复项：给路由器加映射」** |
| `SharpOpenNat/SharpOpenNat/EventArgs/DeviceEventArgs.cs` | `DeviceEventArgs` | 路由器设备上下线事件 |
| `SharpOpenNat/SharpOpenNat/Exceptions/NatDeviceNotFoundException.cs`、`MappingException.cs` | 两个异常 | 「检测项：路由器不支持 UPnP」的明确失败信号 |

README 已验证调用（<https://github.com/JeremyAnsel/SharpOpenNat#readme>）：

```csharp
var device = await OpenNat.Discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts.Token);
await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, 1600, 1700, "The mapping name"));
var ip = await device.GetExternalIPAsync();
```

⚠️ **重要取舍**：r/OculusQuest 上关于 VD 的高赞答复是「**不要转发端口，开 uPnP 就行；Quest 不需要任何入站端口**」
（<https://www.reddit.com/r/OculusQuest/comments/vugy2u/port_forwarding_help>）。
所以 SharpOpenNat 的正确用法是**做检测与归因**（路由器支不支持 UPnP / 外网 IP 稳不稳 / 端口有没有被占），
**不要**做成「给 VD 加端口映射」的默认修复动作 —— 那会与社区经验冲突，甚至加剧冲突。

### 2.3 SharpPcap —— 抓包确认「发现广播到底有没有到 PC」

- 仓库：<https://github.com/dotpcap/sharppcap>（MIT via REUSE，1482★，最后 push 2026-09-28）
- NuGet：`SharpPcap` 最新版 **6.3.1**（<https://api.nuget.org/v3-flatcontainer/sharppcap/index.json>）
- **依赖：必须有 Npcap（或 WinPcap）驱动**。这是它最大的落地成本 —— 我们的检测项不能强制用户装驱动。

| 文件路径 | 关键类 | 我们用它做什么 |
| --- | --- | --- |
| `SharpPcap/CaptureDeviceList.cs` | `CaptureDeviceList.DeviceList`、`Instance` | 枚举抓包网卡（可与「WiFi 还是有线」联动） |
| `SharpPcap/ICaptureDevice.cs`、`ILiveDevice.cs`、`IPcapDevice.cs` | 接口 | 抽象层 |
| `SharpPcap/PacketCapture.cs` | `PacketCapture` | 同步抓包循环，`OnPacketArrival` 回调 |
| `SharpPcap/RawCapture.cs` | `RawCapture` | 异步抓包 |
| `SharpPcap/Packet.cs`、`Ethernet.cs`、`IPv4.cs`、`UDP.cs`、`RawCaptureStatistics` 等 | 数据包解析 | 过滤 UDP 广播帧 |
| `SharpPcap/CaptureDeviceExtensions.cs` | `Open()` / `BeginCapture()` / `StopCapture()` | 打开/停止 |
| `SharpPcap/GetPacketStatus.cs`、`PcapException.cs`、`DeviceNotReadyException.cs` | 异常 | **驱动未装 → 必须优雅降级，不能让 VDH 崩** |

### 2.4 明确**不引包**、用 .NET 内置就够的部分

- **网卡枚举 / up-down / 有线 vs 无线 / 网关** → `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()`、
  `NetworkChange.NetworkAddressChanged`、`NetworkInterface.OperationalStatus`、`NetworkInterfaceType`。
  这正好覆盖 r/OculusQuest 最高赞的「**PC 必须有线接路由器**」这条（<https://www.reddit.com/r/OculusQuest/wiki/faq/virtualdesktop>），无需第三方。
- **网络 profile 是 Private 还是 Public** → 直接跑 `netsh advfirewall show currentprofile` 或
  `Get-NetConnectionProfile`（PowerShell 内置 cmdlet）。**不要**为此引 `Microsoft.PowerShell.SDK`（几十 MB）。
- **端口探测** → `System.Net.Sockets.TcpClient.ConnectAsync(..., timeout)` + `UdpClient`，自行实现超时。
- **ping / 丢包** → `System.Net.NetworkInformation.Ping`（自带 RTT/丢包统计）。

---

## 3 · 重复实现 vs 独家实现

### 3.1 已经有成熟开源方案 —— **不要重造**

| 功能 | 现成方案 | 许可证 | 我们的处理 |
| --- | --- | --- | --- |
| Windows 防火墙规则读/写/删 | **falahati/WindowsFirewallHelper**（MIT）、DigitalRuby/IPBan（MIT，2204★） | MIT | **直接引包**，别自己 P/Invoke `INetFwPolicy2` |
| 路由器 UPnP/NAT-PMP 探测与端口映射 | **JeremyAnsel/SharpOpenNat**（MIT） | MIT | **直接引包** |
| 抓包 / 协议帧解析 | **SharpPcap**（MIT）；人工取证用 Wireshark/tshark | MIT / GPL-2.0(外部) | 引包 + 优雅降级 |
| ADB 客户端（安装/卸载/传输/日志） | **SideQuest**（MIT，409★）、**QAdb**（MIT，162★）、**ADBO**（MIT）、**Open Quest Hub**（MIT） | MIT | **不要重写 ADB 客户端**。我们要的是「检测 + 判定」，不是「侧载器」 |
| Quest 端串流栈 / 协议实现 | **ALVR**（MIT，7972★，Rust）、**WiVRn**（GPL-3.0） | MIT / GPL-3.0 | **只读不抄**。跨语言，且我们目标是「修 VD」不是「重写 VD」 |
| OpenXR runtime / 兼容层 | **mbucchia/VirtualDesktop-OpenXR**（MIT）、**OculusXR-Compatibility**（MIT） | MIT | 只读 |
| 端口扫描 | **Nmap**（Nmap License，外部二进制） | 非 OSI | 外部调用，不内嵌 |
| ADB over TCP 前置（有线模式开关） | AtlasTheProto/ADBForwarder（MIT，已 archived） | MIT | 抄它的 adb 命令集合即可 |

### 3.2 空白区 —— 我们的机会

| 空白 | 现状证据 | 我们的检测项 / 修复项 |
| --- | --- | --- |
| **Virtual Desktop 专用的 PC↔Quest 发现失败诊断** | 全网检索 `quest vr streaming firewall` / `quest vr port forward` / `virtual desktop offline patch` 全部 `total_count = 0`；官方 GitHub 仓库零代码 | 检测项：VD Streamer 进程是否 alive、`VirtualDesktop.Service` 服务状态（判据来自 `zapabob/VRChatSettingTool` 的 `Test-VirtualDesktop`）、监听端口是否被占（`VirtualDesktopTimecodeServer` 证明 VD 侧确有可被外部占用的端口） |
| **第三方安全软件拦截识别** | `quest-link-fixer` 全文只做「结束 Oculus 进程」，README 自己写「**cannot explain exactly why or guarantee it will work**」—— 说明社区在这一步是盲的 | 检测项：Windows Defender 防火墙 / Avast / AVG / Norton 等是否注册了 `FirewallProducts`（`FirewallManager.RegisteredProducts` 正好能读）；修复项：给出针对性的放行指引 |
| **网络 profile Private/Public 自动判定与一键修正** | r/oculus 高赞答复要求用户手动开 `FW.msc` 并把 profile 改 Private（<https://www.reddit.com/r/oculus/comments/1d2ldwv/virtual_desktop_not_working>） | 检测项：`Get-NetConnectionProfile` 的 NetworkCategory；修复项：切 Private |
| **PC 有线/WiFi 拓扑 + 5GHz 频段判定** | r/OculusQuest Wiki 把「PC 必须有线」列为第一前提 | 检测项：`NetworkInterfaceType` + `OperationalStatus`；WiFi 频段需 WlanAPI（`WlanQueryInterface`）—— **[未验证]**，需实跑确认可拿 |
| **一键清理残留 Oculus / VD 进程** | 全网唯一实现是 4 star 的 `pattycoder01/quest-link-fixer`（MIT，单 .bat，13 行 kill 逻辑） | 修复项：可读它的 `files/QuestLinkFixer_win.bat`，但要**加确认框 + 列出将被杀的进程**，不能盲杀 |
| **「PC is unreachable」这句具体报错的归因** | 该报错在 r/OculusQuest 有独立讨论帖，但没有任何工具做归因 | 检测项：把上面所有子项聚合成一份「归因报告」，这是 VDH 的核心卖点 |
| **中文 / 本地化排障体验** | 现有教程全是英文（VR Discord、Reddit、官方 FAQ） | 报告导出（`dwgx/Quest-ADB-Dashboard` 已有 MIT 的 HTML 报告导出实现，可直接复用思路） |

### 3.3 明确的反模式（有成熟方案就别写）

- ❌ 自己写 ADB 客户端 / APK 安装器 → SideQuest、QAdb 已经是 MIT 成熟品。
- ❌ 自己 P/Invoke `INetFwPolicy2` → WindowsFirewallHelper 已封装且是 MIT。
- ❌ 自己实现 UPnP SSDP → SharpOpenNat 已封装且是 MIT。
- ❌ 把「端口转发」当默认修复动作 → 社区明确说 VD 局域网串流不需要入站端口转发。
- ❌ 参考 `SoumyaRanjanPatnaik/VirtualDesktopQuest` 的「协议实现」→ **它没有协议代码**，只有采集后端骨架，且无许可证。
- ❌ 假设 `reference/vdapkpatcher/` 有上游可抄 → 出处查不到，许可证未知。

---

## 4 · 未验证清单（需要实跑才能下结论）

| 待验项 | 验证条件 |
| --- | --- |
| `WindowsFirewallHelper` 2.2.0.86 能否在 `net10.0-windows` restore/build | `dotnet add package WindowsFirewallHelper` 后 `dotnet build` |
| `SharpOpenNat` 4.0.19 能否在 `net10.0-windows` 编译 | 同上（net8.0 资产，理论上兼容） |
| `SharpPcap` 6.3.1 在无 Npcap 的机器上的降级路径 | 干净 Windows 机器上抓包 + 捕获 `DllNotFoundException` |
| Quest WiFi 是否 5GHz 能否在 PC 侧读到 | 需要 WlanAPI 调用实测（`WlanQueryInterface`/`WlanEnumInterfaces`） |
| SharpPcap MIT 判定 | 已验证（REUSE.toml + LICENSES/MIT.txt），无需再验 |
| `dwgx/VRCSM` 的真实许可证 | 读仓库内 LICENSE 文件（API 只能给 `NOASSERTION`） |