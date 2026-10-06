# 04 · 复扫：生态工具与替代实现（2026-10-06）

角度：Virtual Desktop 生态里的工具、替代实现、配置管理器、诊断浮层与分支。
不复述 `01-inventory.md` 已有行，除非状态变了。

**每一条都读了源文件并给了 `file:line`。** 完整原始报告在 `history://SweepStreamer`。

---

## 0 · 一句话结论

**六个新项目，四个可行动，两个有硬性许可阻断。**
最有价值的两个不是代码，是一个**设计输入**和一个**缺失的检测项**。

## 1 · 真正可复用的（MIT）

### alvr-org/settings-schema-rs — 设计输入，不是代码输入

| 项 | 值 |
|---|---|
| 语言 / 许可 | Rust / **MIT** |
| `pushed_at` | 2023-09-11 |
| `stargazers_count` | 11 |

**它做什么**：从 Rust 结构体/枚举**派生**出一棵可序列化的 JSON schema（`SchemaNode`），
字段元数据用 `#[schema(gui(slider(min=, max=, step=)), strings(display_name=, help=))]` 就地标注。
**schema 和 UI 从同一声明生成，所以不可能漂移。**

**证据**：`alvr/session/src/settings.rs:1-14` 导入
`settings_schema::{ArrayDefault, DictionaryDefault, OptionalDefault, SettingsSchema, Switch, VectorDefault}`，
每个设置结构体都挂 `#[derive(SettingsSchema, Serialize, Deserialize, Clone)]`
（例：`:26-38` 的 `FrameSize` 带 `#[schema(gui(slider(min = 0.25, max = 2.0, step = 0.01)))]`）；
`wiki/How-ALVR-works.md` 的 "Procedural generation of code and UI" 一节记载同一条流水线。

**为什么本仓库做不到**：我们 `ParameterValues.cs` 是**从中文散文列里反向推边界**的——
`NumericRange()` 不得不为五种分隔符加特判，再加一个 `TrailingNumber()` 补丁；
`EnumAllowed()` 得靠「含 `– — ~ ≤ ≥` 就当不是列表」来猜。
**这是散文当 schema 的代价，而且无法靠更用力地解析来根治——那个文本在源头就是有损的。**

**移植的含义是采纳形状**：把边界作为**机器可读数据**与键并存，人类可读的那句留作单独 help 字段。

### DoktorAssering/OculusQuestLink-Fix

| 项 | 值 |
|---|---|
| 语言 / 许可 | PowerShell / **MIT** |
| `pushed_at` | 2026-08-10 |

**三个本仓库完全没有的检测项**：

1. **USB 层 Meta 在场检测**——`Get-QuestLinkDiagnostics.ps1:99-121` 查 `Get-PnpDevice`，
   用 `-match "^USB\\VID_2833&"` 匹配 Meta 设备。本仓库 `grep VID_2833` **零命中**，
   所以**无法区分「头显根本没接上这台电脑」和「接上了但没在串流」**。
2. **`VID_0000&PID_0002` 描述符读取失败检测**——设备枚举上了但身份读不出来，
   脚本明确警告「VID_0000&PID_0002 means Windows failed before it could read the device identity」。
   **这是一个和「没插线」不同的故障，本仓库没有表达它的手段。**
   **← 与 Owner 卡住的「插 USB-C」直接相关。**
3. **`docs/TROUBLESHOOTING.md:26-30` 给出五个可直接 grep 的 Meta 日志标记**：
   `incompatible_packet_version`、`incompatible version type`、`WinUSB`、`DiscoHighwind`、`XrsTransport`。

另：`QuestLinkSafeStart.ps1:96-121` 的 `Get-ConfiguredNetworkAdapter` 先按 `InterfaceGuid` 解析、
再按显示名——**按 GUID 而非名字定位网卡**是一个我们没有的好习惯。

## 2 · 有硬性阻断的（记录以免重复查）

| 仓库 | 许可 | 处置 |
|---|---|---|
| `farmerarmor/oculus-debug-mcp` | **NO-LICENSE-FILE**（`license=null`） | **不可复用**，仅参考 |
| `Eliminater74/MetaQuestTrayTool` | **NOASSERTION**——`LICENSE.txt` 是 MIT 正文加四段附加条款，GitHub 无法映射 SPDX | **不可复用**，仅参考 |
| `SUPTECGourry/quest-battery-osc` | **NO-LICENSE-FILE** | **不可复用** |

`quest-battery-osc` 声称 VD Streamer 开 `ws://localhost:19999`——
**主代理已在本机复核：19999 无监听，不成立。** 该说法只有单一来源，不作依据。

`MetaQuestTrayTool` 读 Meta 自己的 `%LocalAppData%\Oculus\DeviceCache.json`，
**主代理已复核：本机该文件不存在**（该目录下只有 `OculusSetup.log`，Meta 运行时并未安装），
所以那条「不经 ADB 判断头显是否醒着」的路径在本机走不通。

## 3 · 对已有行的更新（ALVR）

- **决定规则**：`alvr/client_core/src/sockets.rs:21-38` 的 `AnnouncerSocket::announce()` 以
  `if local_ip.is_unspecified() { bail!("IP is unspecified"); }` 开头，
  `alvr/system_info/src/lib.rs` 的 `local_ip()` 失败时回退到 `V4(UNSPECIFIED)`。
  **即 ALVR 把「PC 不可被发现」写成一个显式命名状态**，而不是当成缺失。
- **排障树**：`wiki/Troubleshooting.md` 是本轮找到的唯一一份有序的发现失败分诊。
  关键句：「ALVR on the headset sends broadcast packets which the PC application listens for.
  These can be blocked by your firewall or possibly your router, if both headset and PC are connected
  wirelessly, **having AP isolation enabled on the router will cause this**.」
  四步分诊：ping 头显 → 关防火墙试 → 开 9943/9944 → 关路由器 PMF。
  **主代理复核：这一条的「本仓库没点名」是不成立的。** `ReachabilityCheck.cs` 有四处：
  `:95` 证据行「两端不在同一网段：头显可能在访客网络，或**路由器开了 AP 隔离**」；
  `:169` 把「邻居表 Reachable 但 ping 不通」判为「更像 AP 隔离或来宾网络」；
  `:172` 反过来在状态非 Reachable 时**明确排除**这个解释（「而不是被 AP 隔离挡住」）；
  `:178` 指引「同网段还不通：查 AP 隔离 / 访客网络 / 无线与有线隔离 / …」。
  `LossProbe.cs:271` 也点名。**该 sweep 的这条更新是错的，已在此更正。**
- **HTTP 存活端点**：`wiki/How-ALVR-works.md` 记载 `http://localhost:8082/api/ping` 返回 200 即驱动存活，
  且 dashboard 在驱动未启动时仍然可用——**「客户端发现不了」被写成一个独立的、可命名的状态**。

## 4 · 本仓库相对 Owner 旧作丢失的能力（grep 实读确认）

对 `src/VdHelper` grep `CapPatch|InstallFromFolder|KnownApk|\.apk|install -r -g|SettingHelp|Sha256Hex|SemVerLite|HostAllowed` —— **零命中**。

旧 WinForms 版曾有：① `HeadsetInstall()` 的 `install -r -g` APK 安装，
及 `InstallFromFolder()` 的 SHA-256 白名单流水线（**拒绝安装任何它认不出的 APK**）；
② `CapPatch`（XABA/XALZ → LZ4 → 三段 int32 立即数，改写 APK 内的 `VirtualDesktop.Mobile.dll`）。
**这些属于 patched APK 路线，与本仓库"不分发官方二进制"规则冲突，需 Owner 裁决，不自行恢复。**

## 5 · 负面结果（连同产生它的检索）

- `search/repositories q="vr streaming diagnostic report tool windows"` → **total_count 0**，精确为空。
- `search/repositories q="Virtual Desktop" quest streamer` → **total_count 2**。
- **没有任何开源项目读取 VD 自身日志（`ServiceLog.txt`）并下结论。**
  **局限**：GitHub code search 返回 **HTTP 401 未认证**，`"ServiceLog.txt" "VirtualDesktop"` 未能执行，
  故该负面结论仅基于 repo 检索与网页检索，**不是穷尽证明**。

## 6 · 对已有行的更新（Owner 旧作）

`dwgx/Quest-ADB-Dashboard`（MIT，已在清单内）以只读方式 vendored 在 `reference/quest-adb-dashboard/`，
**在广度上仍领先当前仓库**：`QuestAdbWebUi.cs:1742-1754` 经 ADB 采集
`dumpsys input`、`pm list packages -f -i`、`pm list features`、`cmd package list libraries`、
`df -h`、`ip route`、`dumpsys package VirtualDesktop.Android`、`dumpsys package com.oculus`
与 3000 行 logcat 尾部，每条都有显式超时（5000–12000 ms）；
`:1806-1860` 派生出约 50 个字段，含 thermal / usb / bluetooth / camera 与工厂校准元数据。
其 `docs/METHODS.md` 有一节 **"What ADB Cannot Reliably Prove"**——
**这份「我们证明不了什么」的清单比它的功能清单更值得继承。**