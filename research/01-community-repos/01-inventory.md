# 01 · 社区 GitHub repo 与开源辅助软件盘点

调查日期：2026-10-05。范围：Virtual Desktop（vrdesktop.net）生态里**有源代码**、或虽无源代码但对「PC↔Quest 连不上」这个问题有直接参考价值的仓库与工具。

## 0 · 取数方法（每行的许可证/日期都来自这条命令，可复跑）

```bash
for r in <owner>/<repo>; do gh api "repos/$r" --jq '[.full_name,(.language//"-"),(.license.spdx_id//"NO-LICENSE-FILE"),(.archived|tostring),.pushed_at[0:10],.stargazers_count] | @tsv'; done
```

`license.spdx_id` 为 `null` 时本表写 `NO-LICENSE-FILE`（仓库根目录没有 GitHub 能识别的许可证文件），
为 `NOASSERTION` 时写 `NOASSERTION`（有 LICENSE 但 GitHub 无法映射到 SPDX）。两者都**不等于**可自由复用，判定见 `02-reuse-candidates.md`。

## 1 · 主清单

| 名称 | URL | 语言 | 许可证(spdx) | 最后活跃时间 | 它解决什么 | 与本项目的关系 | 证据URL |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Virtual Desktop（官方 issue 仓库） | https://github.com/guygodin/VirtualDesktop | - | NO-LICENSE-FILE | 2026-04-01 | 官方唯一 GitHub 落点，**只有 README.md、`languages` API 返回 `{}`（零代码）**，承担用户报障与官方公告 | **仅参考**：官方口径的故障表述与已知问题清单来源；零代码可搬 | https://api.github.com/repos/guygodin/VirtualDesktop |
| Virtual DesktopTimecodeServer | https://github.com/michael-mueller-git/VirtualDesktopTimecodeServer | C# | MIT | 2024-07-14 | 给 Virtual Desktop 的 Wired/Classic 版本提供 SMPTE timecode（Lens/Lightstorm 同步用） | **仅参考**：证明 VD Streamer 侧存在可被外部进程访问的本地 socket/服务接口，值得做「占用端口/冲突」检测项 | https://api.github.com/repos/michael-mueller-git/VirtualDesktopTimecodeServer |
| VirtualDesktop-OpenXR（VDXR） | https://github.com/mbucchia/VirtualDesktop-OpenXR | C++ | MIT | 2026-09-13 | Virtual Desktop 的完整 OpenXR 1.0/1.1 runtime 实现，367 star，VR Discord/Reddit 反复被点名 | **仅参考**（对我们无网络价值，但**MIT**，可作为「VD 侧技术栈全貌」的权威参考实现） | https://api.github.com/repos/mbucchia/VirtualDesktop-OpenXR |
| OculusXR-Compatibility | https://github.com/mbucchia/OculusXR-Compatibility | C++ | MIT | 2025-10-17 | 让 VD 串流下继续用 OculusXR API 的兼容层 | **无关**（网络排查无关）；证明 VD 的 SteamVR/OpenXR 兼容面是社区长期痛点 | https://api.github.com/repos/mbucchia/OculusXR-Compatibility |
| OpenXR-Toolkit | https://github.com/mbucchia/OpenXR-Toolkit | C++ | MIT | 2025-10-19 | OpenXR 应用诊断/增强工具集，453 star | **仅参考**：OpenXR 侧的问题定位工具集，可借鉴其「检测项→诊断报告」产品形态 | https://api.github.com/repos/mbucchia/OpenXR-Toolkit |
| tiefling（CombatWombat） | https://github.com/combatwombat/tiefling | JavaScript | MIT | 2026-07-22 | 2D-to-3D 转换器/viewer，但**实为 VD 生态里最活跃的安装/分发脚本仓库之一**，140 star，持续更新 | **仅参考**：其安装脚本里对 VD Streamer 安装路径、进程名、依赖的处理是可抄的常识来源 | https://api.github.com/repos/combatwombat/tiefling |
| VirtualDesktopQuest（SoumyaRanjanPatnaik） | https://github.com/SoumyaRanjanPatnaik/VirtualDesktopQuest | Rust | NO-LICENSE-FILE | 2023-08-28 | GSoC 2023（CCExtractor 组织）项目，目标是给 Meta Horizon Workrooms 做 Linux 后端。**实测只有音频/帧采集与 `virtual_desktop::Manager` 骨架，没有任何网络传输代码**（`src/virtual_desktop/mod.rs` 仅 `pub mod manager;`） | **仅参考**：确认「第三方面向 Quest 的 VD 端后端」至今没做完；**不能当协议参考**（没有协议实现） | https://api.github.com/repos/SoumyaRanjanPatnaik/VirtualDesktopQuest |
| remote-desktop-for-ubuntu | https://github.com/yveshughes/remote-desktop-for-ubuntu | Java | NO-LICENSE-FILE | 2026-07-05 | Meta Quest 原生客户端，把 Ubuntu 桌面变成 VR 虚拟显示器（Sunshine + Moonlight 方案） | **无关**（是绕开 VD 的替代串流栈），但反证「Quest 侧串流客户端」有第三方在写 | https://api.github.com/repos/yveshughes/remote-desktop-for-ubuntu |
| ALVR | https://github.com/alvr-org/ALVR | Rust | MIT | 2026-10-04 | 7972 star 的开源 PCVR 串流栈（含 PC 端 server + Quest 端 client）。**功能上等价于 VD 的串流部分** | **仅参考（强）**：MIT + 活跃 + 自带完整串流/网络/诊断实现，是判断「VD 网络协议怎么做」的最佳第三方参照 | https://api.github.com/repos/alvr-org/ALVR |
| ALXR-nightly（korejan） | https://github.com/korejan/ALXR-nightly | - | NO-LICENSE-FILE | 2026-10-04 | 81 star 的 ALXR（Quest 专版 ALVR）nightly 发布仓库，非 fork（`fork=false`） | **无关**（发布渠道），但证明 Quest 串流客户端有独立活跃分支 | https://api.github.com/repos/korejan/ALXR-nightly |
| WiVRn | https://github.com/WiVRn/WiVRn | C++ | GPL-3.0 | 2026-10-04 | 1749 star，Linux 下的 OpenXR 串流应用到 standalone 头显 | **仅参考**：协议/网络设计的 GPL 参考实现，**GPL-3.0 不可搬** | https://api.github.com/repos/WiVRn/WiVRn |
| SideQuest | https://github.com/SideQuestVR/SideQuest | TypeScript | MIT | 2026-10-01 | 409 star 的 Quest 桌面客户端，内含 ADB 修复/侧载/包管理的一整套成熟实现 | **可复用**：MIT + TypeScript/Electron；其 ADB 调用与「设备状态判定」逻辑可直接对照我们的 Quest 侧检测 | https://api.github.com/repos/SideQuestVR/SideQuest |
| QAdb | https://github.com/ludoven/QADB | Kotlin | MIT | 2026-09-24 | 162 star，Compose Multiplatform 的 ADB GUI，Windows/macOS/Linux | **仅参考**：多平台 ADB 客户端的成熟交互范式；Kotlin 与 .NET 不共通 | https://api.github.com/repos/ludoven/QADB |
| Open Quest Hub | https://github.com/Watash1no/open-quest-hub | Rust | MIT | 2026-10-01 | VR 场景设计的 ADB wrapper，Rust 编写、活跃 | **仅参考**：Rust 实现的 adb 命令封装，可参考其命令集合 | https://api.github.com/repos/Watash1no/open-quest-hub |
| ADBO | https://github.com/ovsky/ADBO | Batchfile | MIT | 2026-09-29 | 用 Meta 官方 ADB fork 的流式文件管理加速 ADB 操作 | **仅参考**：证明「ADB 操作慢」是社区反复抱怨的点 → 我们的检测项该有超时与耗时指标 | https://api.github.com/repos/ovsky/ADBO |
| QuestToolboxV2 | https://github.com/DigitalGenesisDev/QuestToolboxV2 | JavaScript | NO-LICENSE-FILE | 2022-04-21 | 14 star 的 Quest ADB 客户端，已停更 | **无关**（停更），仅作对照 | https://api.github.com/repos/DigitalGenesisDev/QuestToolboxV2 |
| ADBForwarder | https://github.com/AtlasTheProto/ADBForwarder | C# | MIT | 2022-02-22 | 23 star，**已 archived**。用 ADB 命令开启 Quest 的有线串流（ALVR wired） | **可复用（结构参考）**：C# + MIT + `archived=true` 意味着无维护风险可借鉴、无补丁可跟；其 ADB 命令集合是有线模式的现成参考 | https://api.github.com/repos/AtlasTheProto/ADBForwarder |
| quest-link-fixer | https://github.com/pattycoder01/quest-link-fixer | Batchfile | MIT | 2025-08-09 | 4 star。**唯一专门做「Quest 连不上」一键修复的独立工具**：管理员提权后批量结束 Oculus 运行时进程 | **仅参考（但价值高）**：它把我们的一半修复项做成了「杀残留进程」；这是社区真实痛点的直接证据 | https://api.github.com/repos/pattycoder01/quest-link-fixer |
| QuestHome | https://github.com/QuestMods/QuestHome | C# | NO-LICENSE-FILE | 2022-09-25 | 9 star，Quest 上的第三方 launcher（mod 社区常用） | **无关**（launcher 方向），但说明第三方 launcher 生态存在且无许可证 | https://api.github.com/repos/QuestMods/QuestHome |
| QuestHomeSwitcher | https://github.com/QuestMods/QuestHomeSwitcher | C# | MIT | 2023-10-02 | Quest 应用 launcher（Unity 实现） | **无关** | https://api.github.com/repos/QuestMods/QuestHomeSwitcher |
| WindowsFirewallHelper | https://github.com/falahati/WindowsFirewallHelper | C# | MIT | 2025-02-24 | 301 star，72 fork。**Windows 防火墙 COM interop 完整封装**：`FirewallManager.Instance.CreateApplicationRule/CreatePortRule/Rules.Add/Remove/Profiles`，还有 `Addresses.LocalSubnet/DefaultGateway/NetworkAddress` | **可复用（首选）**：MIT + 纯托管 + 直接覆盖我们「防火墙规则修复」和「本机网段/网关判定」两个检测/修复项 | https://api.github.com/repos/falahati/WindowsFirewallHelper |
| SharpOpenNat | https://github.com/JeremyAnsel/SharpOpenNat | C# | MIT | 2026-07-01 | 12 star。UPnP / NAT-PMP 端口映射 C# 库：`OpenNat.Discoverer.DiscoverDeviceAsync`、`device.CreatePortMapAsync(new Mapping(...))`、`GetExternalIPAsync()` | **可复用**：MIT + C#，直接支撑「NAT 类型/路由器是否支持 UPnP/外网 IP 是否变化」检测项与端口转发修复项 | https://api.github.com/repos/JeremyAnsel/SharpOpenNat |
| SharpPcap | https://github.com/dotpcap/sharppcap | C# | NO-LICENSE-FILE | 2026-09-28 | 1482 star，跨平台 .NET 抓包库（pcap） | **可复用（附条件）**：GitHub API 判不出许可证，但仓库用 REUSE 规范明确声明 MIT（见 02 号文件），代码可用；需要 Npcap/WinPcap 驱动 | https://api.github.com/repos/dotpcap/sharppcap |
| powershell-network-security-toolkit | https://github.com/EdwardOconnell/powershell-network-security-toolkit | PowerShell | MIT | 2026-09-26 | 0 star，PowerShell 7 审计模块：`Get-FirewallAudit` / `Set-FirewallBaseline` / `Get-NetAdapterHealth` / `Find-NetworkDevice` / `Test-RouterExposure` / `Test-NetworkSpeed` | **仅参考（高价值）**：它的 cmdlet 划分几乎就是我们检测项清单的现成模板；0 star 新仓，不能信其实现，只能借结构 | https://api.github.com/repos/EdwardOconnell/powershell-network-security-toolkit |
| IPBan | https://github.com/DigitalRuby/IPBan | C# | MIT | 2026-10-03 | 2204 star，Windows 防火墙规则管理的老牌实现（`IPBanCore/Windows/COM/INetFwMgr.cs`） | **仅参考**：「程序化管防火墙规则」的成熟踩坑清单（权限、profile 作用域、重复规则） | https://api.github.com/repos/DigitalRuby/IPBan |
| Wireshark | https://github.com/wireshark/wireshark | C | GPL-2.0 | 2026-10-04 | 9957 star，抓包分析。**GPL-2.0，不可搬代码** | **仅参考（外部工具）**：tshark 可作为我们「发现广播到底有没有到 PC」的人工取证手段，但 VDH 不能链接它 | https://api.github.com/repos/wireshark/wireshark |
| Nmap | https://github.com/nmap/nmap | C | NOASSERTION | 2026-10-03 | 13712 star，网络扫描器。许可证为 Nmap License（OSI 未批准） | **仅参考（外部工具）**：`-sU` 扫 UDP 端口可人工验证端口状态；**不可复用代码** | https://api.github.com/repos/nmap/nmap |
| VRChatSettingTool（zapabob） | https://github.com/zapabob/VRChatSettingTool | Python | MIT | 2025-07-27 | 含 `VirtualDesktop_VRChat_Optimizer_Clean.ps1` / `_NoAdmin.ps1`：检测 `VirtualDesktop.Streamer` 进程与 `VirtualDesktop.Service` 服务，然后跑 `netsh int tcp set global ...` | **仅参考（可直接借鉴的检测逻辑）**：它证明「VD Streamer 进程 + VirtualDesktop.Service 服务」是社区通用的存活判据 | https://api.github.com/repos/zapabob/VRChatSettingTool |
| Virtual_Desktop_Body_Tracking_Configurator | https://github.com/DenTechs/Virtual_Desktop_Body_Tracking_Configurator | Python | MIT | 2024-11-30 | 218 star，VR 环境下驱动 Virtual Desktop 身体追踪的配置工具 | **仅参考**：说明 VD 周边第三方工具的典型形态（GUI 改配置 + 写回） | https://api.github.com/repos/DenTechs/Virtual_Desktop_Body_Tracking_Configurator |
| desktop2stereo | https://github.com/lc700x/desktop2stereo | Python | MIT | 2026-07-31 | 164 star，桌面 2D→3D，README 明确以 VD 为主要使用场景 | **无关**（画质方向） | https://api.github.com/repos/lc700x/desktop2stereo |
| OpenVRDesktopDisplayPortal | https://github.com/Hotrian/OpenVRDesktopDisplayPortal | C# | NOASSERTION | 2017-03-30 | 451 star，把 Windows 桌面窗口塞进 OpenVR 游戏里（C#） | **无关**（2017 年老代码，无许可证，仅登记留档） | https://api.github.com/repos/Hotrian/OpenVRDesktopDisplayPortal |
| DorsalVR | https://github.com/MichaelJW/DorsalVR | C# | NO-LICENSE-FILE | 2021-11-05 | 82 star，PC 游戏的 VR 接口层，README 引用 vrdesktop.net | **无关**（无许可证 + 停更） | https://api.github.com/repos/MichaelJW/DorsalVR |
| VRDroid | https://github.com/babysource/VRDroid | Java | NO-LICENSE-FILE | 2016-11-18 | 202 star，2016 年的 Android RDP 客户端，与 vrdesktop.net 无关 | **无关**（关键词噪声） | https://api.github.com/repos/babysource/VRDroid |

### Owner 自己以前的作品（单独小节）

| 名称 | URL | 语言 | 许可证(spdx) | 最后活跃时间 | 它解决什么 | 与本项目的关系 | 证据URL |
| --- | --- | --- | --- | --- | --- | --- | --- |
| dwgx/VirtualDesktop | https://github.com/dwgx/VirtualDesktop | C# | NO-LICENSE-FILE | 2026-08-28 | Virtual Desktop 1.34.x 的**离线局域网串流补丁 + 简体中文汉化**，IL 级二进制补丁工程。2 个 release（v1.34.18.0 / v1.34.19.0）+ 1 个仅 tag（v1.34.22.0-lan-500），1 star | **本项目上游**：Quest 侧 patched APK 的来源 | https://api.github.com/repos/dwgx/VirtualDesktop |
| dwgx/VirtualDesktopHelper | https://github.com/dwgx/VirtualDesktopHelper | C# | NO-LICENSE-FILE | 2026-08-28 | C# WinForms 的 VD Streamer 辅助工具，**明确声明 No Quest APK**。7 个 release（v0.4.0 ~ v0.4.7），0 star | **本项目前身**：新工具是它的重做 | https://api.github.com/repos/dwgx/VirtualDesktopHelper |
| dwgx/Quest-ADB-Dashboard | https://github.com/dwgx/Quest-ADB-Dashboard | C# | MIT | 2026-09-30 | Meta Quest ADB 诊断面板 + 可分享 HTML 报告导出。6 个 release（v0.1.0 ~ v0.3.0），4 star | **可复用**：Quest 侧检测能力 + 「导出报告」产品形态都已实现且是 MIT | https://api.github.com/repos/dwgx/Quest-ADB-Dashboard |
| dwgx/VRCSM | https://github.com/dwgx/VRCSM | TypeScript | NOASSERTION | 2026-09-30 | VRChat Settings Manager：Windows 缓存清理、设置备份/迁移、诊断 | **仅参考**：同一套「Windows 端设置管理 + 诊断」工程能力，非 VD 领域 | https://api.github.com/repos/dwgx/VRCSM |

### Owner 仓库现状补充（release / issue 证据）

```text
$ gh api repos/dwgx/VirtualDesktop/releases --jq '.[]|[.tag_name,.name,.published_at[0:10]]|@tsv'
v1.34.19.0	VirtualDesktop 1.34.19.0 — 离线局域网串流补丁（960 Mbps）	2026-08-22
v1.34.18.0	VirtualDesktop 1.34.18.0 — 离线局域网串流补丁	2026-07-04
$ gh api repos/dwgx/VirtualDesktopHelper/releases --jq '.[].tag_name'
v0.4.7 v0.4.6 v0.4.5 v0.4.3 v0.4.2 v0.4.1 v0.4.0
$ gh api repos/dwgx/VirtualDesktop/issues?state=all --jq '.[]|[.number,.state,.title[0:100]]|@tsv'
1	closed	install bat 全分支 Windows 实测夹具 + 3 个真 bug 修复	2026-08-09
$ gh api repos/dwgx/Quest-ADB-Dashboard/issues?state=all --jq '.[]|.title'
#4 Update pillow requirement ... / #3 Update mcp requirement ... / #2 Bump actions/checkout / #1 Bump actions/setup-python
```

**结论（必须直说）：三个仓库合计 issue 数为 5，其中 4 条是 Dependabot 自动 PR，1 条是 Owner 自己写的。**
「有没有 issue 描述过用户痛点」→ **没有**。三个仓库 star 合计 5（1 + 0 + 4）。
所以本项目的用户痛点假设**不能**从自家 issue 里得到验证；痛点证据只能来自 §3 的外部社区。

### 3 · 社区/教程来源（非 GitHub，但被反复引用）

| 来源 | URL | 覆盖什么 | 与本项目的关系 | 证据URL |
| --- | --- | --- | --- | --- |
| r/OculusQuest Wiki：Virtual Desktop Guide | https://www.reddit.com/r/OculusQuest/wiki/faq/virtualdesktop | 「PC 必须有线接路由器 / Quest 走 5GHz WiFi」——被引用最多的单条排障结论 | **仅参考**：应做成网卡检测项（有線=以太网适配器 up 且有默认网关） | https://www.reddit.com/r/OculusQuest/wiki/faq/virtualdesktop |
| r/OculusQuest Wiki：PCVR Virtual Desktop（分册） | https://www.reddit.com/r/OculusQuest/wiki/faq/pcvr/virtualdesktop | 分篇设置教程 | **仅参考** | https://www.reddit.com/r/OculusQuest/wiki/faq/pcvr/virtualdesktop |
| r/OculusQuest 帖：「Virtual desktop not working」 | https://www.reddit.com/r/OculusQuest/comments/y2ebe8/virtual_desktop_not_working | 三条经典排查：防火墙、PC 有线、重启路由器/PC/Quest | **仅参考** → 直接对应三个检测项 | https://www.reddit.com/r/OculusQuest/comments/y2ebe8/virtual_desktop_not_working |
| r/OculusQuest 帖：「Port Forwarding Help」 | https://www.reddit.com/r/OculusQuest/comments/vugy2u/port_forwarding_help | 「别转发端口，开 uPnP 就行；Quest 不需要入站端口」 | **仅参考（重要）**：纠正了一个常见误解——VD 局域网串流**不需要**端口转发，我们的检测项不应默认要求转发 | https://www.reddit.com/r/OculusQuest/comments/vugy2u/port_forwarding_help |
| r/oculus 帖：「Virtual desktop not working :/」 | https://www.reddit.com/r/oculus/comments/1d2ldwv/virtual_desktop_not_working | `Win-R` → `FW.msc` 打开防火墙控制台；Avast/AVG 把网络 profile 设 Private 而非 Public | **仅参考** → 「网络 profile 是否 Private」必须是一个检测项 | https://www.reddit.com/r/oculus/comments/1d2ldwv/virtual_desktop_not_working |
| r/OculusQuest 帖：「PC is Unreachable」 | https://www.reddit.com/r/OculusQuest/comments/1kp2oya/virtual_desktop_says_pc_is_unreachable_after_not | 「PC is unreachable」这一具体报错的复现讨论 | **仅参考** → 对应我们最核心的检测项 | https://www.reddit.com/r/OculusQuest/comments/1kp2oya/virtual_desktop_says_pc_is_unreachable_after_not |
| r/OculusQuest 帖：「Better ALVR Alternative」 | https://www.reddit.com/r/OculusQuest/comments/d4s89y/better_alvr_alternative | 指向 ALVR release，证明 VD 的用户同时是 ALVR 的用户（竞争/参照人群重合） | **仅参考** | https://www.reddit.com/r/OculusQuest/comments/d4s89y/better_alvr_alternative |
| r/oculus 帖：「VDXR 自家 runtime」 | https://www.reddit.com/r/oculus/comments/17ebf7r/virtual_desktop_said_screw_it_and_did_their_own | 指认 VirtualDesktop-OpenXR「完全开源」的社区共识 | **仅参考** | https://www.reddit.com/r/oculus/comments/17ebf7r/virtual_desktop_said_screw_it_and_did_their_own |
| VR Discord Community：Virtual Desktop Guide | https://vrdiscord.com/guides/quest-wireless/virtualdesktop.html | 第三方社区完整指南（Getting Started / Desktop Streamer / **Troubleshooting**） | **仅参考** → 检测项清单的比对基线 | https://vrdiscord.com/guides/quest-wireless/virtualdesktop.html |
| QuestMods 组织 | https://github.com/QuestMods | Quest mod/launcher 社区的 4 个仓库（QuestHome / HomeBuilder / QuestHomeSwitcher / CustomHomes） | **无关**（launcher 方向） | https://api.github.com/orgs/QuestMods/repos |

### 4 · 关键词覆盖与「查不到」声明

已用 `gh search repos` / `gh api search/repositories` / `search/code` 跑过：
`vrdesktop`、`vrdesktop.net`、`virtual desktop vrdesktop`、`virtual desktop quest`、`quest streamer`、
`vrdesktop patch`、`quest streamer helper`、`quest vr streaming firewall`、`quest vr port forward`、`vrdroid`、
`VDHelper virtual desktop`、`quest lan streaming`、`virtual desktop offline patch`、`quest virtual desktop mod`、
`ALVR virtual desktop`、`VD APK patch`、`VDH virtual desktop helper quest`、`virtual desktop protocol quest`。

以下关键词**在 GitHub 上查不到任何「Virtual Desktop 专用」的仓库**（`total_count = 0` 或全部为噪声），如实记录：

- `quest vr streaming firewall` → `total_count = 0`。**没有任何开源的「Quest 串流防火墙脚本」仓库**。
- `quest vr port forward` → `total_count = 0`。**没有任何 VD 专用端口转发工具**。
- `quest lan streaming` / `virtual desktop offline patch` / `quest virtual desktop mod` / `ALVR virtual desktop` / `VD APK patch` → 全部无相关结果。
- `VDHelper virtual desktop` → 只命中 `dwgx/VirtualDesktopHelper` 自己。
- 代码检索 `"Virtual Desktop Streamer.exe"`、`"Virtual Desktop Streamer.txt"`、`"com.vrdesktop"` → 各 `total_count = 0`（`vdstreamer` 只有 135 条命中，且全部是 GTAV/VRChat 的无关代码）。
- `reference/vdapkpatcher/`（本地只读素材）**没有 `.git`、没有任何 `github.com/...` 出处**，`grep -rhoiE "github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+" reference/` 只命中 `dwgx/VirtualDesktopHelper` 两次。→ **该树的上游出处查不到，不做猜测**。

### 5 · 盘点结论

- Virtual Desktop 的 **PC 侧与 Quest 侧都没有开源实现**；官方 GitHub 只有一个空的 issue 仓库。
- 「Quest 连不上」这个问题的**开源供给几乎为零**：全网只有一个 4 star 的 `quest-link-fixer`（杀 Oculus 进程），其余全是文字教程。
- 生态里成熟的开源能力集中在**别的地方**：ALVR（串流栈）、SideQuest/QAdb/ADBO（ADB）、WindowsFirewallHelper/SharpOpenNat/SharpPcap（Windows 网络基建）。