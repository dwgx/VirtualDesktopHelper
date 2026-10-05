# 01 · PC 侧 Virtual Desktop Streamer 参数键全表

调研日期：2026-10-05
源码基准：`F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/`（`AssemblyInfo.cs:14` → **1.34.18.0**）
本机安装：**1.34.22.0**（键集比对见 `03-ui-to-key-map.md` §4）
配置文件路径取证见 `02-file-locations.md`

---

## 0. 读表前必须知道的 4 件事

1. **只有 3 个文件在 PC 落盘**：`%ProgramData%\Virtual Desktop\StreamerSettings.json`（主配置）、`%APPDATA%\Virtual Desktop\GameSettings.json`、`%ProgramData%\Virtual Desktop\BindingSettings.json`。
2. **`key 缺失` ≡ `[DefaultValue]` 声明值**。序列化用 `DefaultValueHandling.IgnoreAndPopulate`（`-/.83.cs:16`）。→ 读参数必须做默认值兜底。
3. **串流质量参数（码率/分辨率/刷新率/VR 画质）不在 PC 本地 JSON 里**。它们属于 `Shared*Settings`，PC 是 client，由头显端持有（`ConnectionManager.cs:203-205`）。`SharedStreamerSettings` 是 PC 侧 server 但**只在属性变化后才落盘**。
4. **配对/证书相关一律只读**：`ProtectedComputerID` 与 `Accounts.*` 是 DPAPI 密文（实测值以 `AQAAANCMnd8B` 开头），改写等于重置本机身份和全部配对。

---

## 1. `StreamerSettings.json` — 主配置（54 键）

文件：`C:\ProgramData\Virtual Desktop\StreamerSettings.json`
类：`VirtualDesktop/Streamer/StreamerSettings.cs:20`（`SerializedSettingsBase<StreamerSettings>`）

**图例**
- `默认` 列 = `[DefaultValue]` 声明值；`—` 表示无 `[DefaultValue]`，此时 C# 字段默认（`false`/`0`/`null`）生效，且**等于该值的键会被完全省略**
- `LAN` = 是否影响 LAN 发现/可连接性；`画质` = 是否影响画质/延迟
- `重启` = 改了是否需要重启 Streamer；`重连` = 是否需要断开重连才生效
- `安全` = 改动安全性：**可改** / **谨慎** / **只读不改**

| # | 键名（JSON 路径） | 类型 | 默认 | 合法取值 / 范围 | 作用 | LAN | 画质 | 重启 | 重连 | 安全 | 来源 file:line |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `SelectedTab` | int | — (0) | 任意 int；UI 实测 0–5 | 主窗口当前标签页索引（Options/Advanced/Media/About 等）。纯 UI 状态 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:1114`；消费点 `StreamerManager.cs:855` |
| 2 | `ServerRotation` | int | — (0) | 0–5 循环 | 流式服务器轮转计数，`IncrementServerRotation()` 做 `(x+1)%6` | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:1160`；轮转逻辑 `:3163-3182` |
| 3 | `ProtectedComputerID` | string | — (null) | DPAPI 密文，不可构造 | **本机身份/受保护 computer ID**。解密失败时 `Initialize()` 会重新生成并清空所有账号 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1206`；加解密 `:3394-3420` |
| 4 | `Accounts` | object | — (null) | `{ "OculusQuest": [密文…], "Oculus": [密文…] }` | 已配对账号集合，按 `Platform` 名分组。值全为 DPAPI 密文 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1244`；序列化形状 `Interfaces/AccountCollection.cs:44` |
| 5 | `ProtectedOculusID` | string | — (null) | DPAPI 密文 | 旧版单账号字段（已迁移到 `Accounts`，仍保留读写） | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1262` |
| 6 | `ProtectedOculusID2` | string | — (null) | DPAPI 密文 | 同上，第 2 个槽位 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1285` |
| 7 | `ProtectedOculusID3` | string | — (null) | DPAPI 密文 | 同上，第 3 个槽位 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1308` |
| 8 | `ProtectedOculusID4` | string | — (null) | DPAPI 密文 | 同上，第 4 个槽位 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1331` |
| 9 | `SavedPicoID` | string | — (null) | DPAPI 密文 | Pico 账号槽位 1（注意命名是 `Saved` 不是 `Protected`，但仍加密） | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1354` |
| 10 | `SavedPicoID2` | string | — (null) | DPAPI 密文 | Pico 账号槽位 2 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1377` |
| 11 | `SavedPicoID3` | string | — (null) | DPAPI 密文 | Pico 账号槽位 3 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1400` |
| 12 | `SavedPicoID4` | string | — (null) | DPAPI 密文 | Pico 账号槽位 4 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1423` |
| 13 | `ProtectedViveID` | string | — (null) | DPAPI 密文 | Viveport 账号槽位 1 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1446` |
| 14 | `ProtectedViveID2` | string | — (null) | DPAPI 密文 | Viveport 账号槽位 2 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1469` |
| 15 | `ProtectedGoogleID` | string | — (null) | DPAPI 密文 | Google Play 账号槽位 1 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1492` |
| 16 | `ProtectedGoogleID2` | string | — (null) | DPAPI 密文 | Google Play 账号槽位 2 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1515` |
| 17 | `ProtectedPFDID` | string | — (null) | DPAPI 密文 | Play for Dream 账号槽位 1 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1538` |
| 18 | `ProtectedPFDID2` | string | — (null) | DPAPI 密文 | Play for Dream 账号槽位 2 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1561` |
| 19 | `ProtectedAppleID` | string | — (null) | DPAPI 密文 | Apple Vision 账号槽位 1 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1584` |
| 20 | `ProtectedAppleID2` | string | — (null) | DPAPI 密文 | Apple Vision 账号槽位 2 | 是 | 否 | 是 | 是 | **只读不改** | `StreamerSettings.cs:1607` |
| 21 | `AllowRemoteConnections` | bool | **true** | true / false | **是否允许互联网远程连接**。`false` 时 `ConnectionManager.cs:416` 调 `UPnPManager` 撤销端口映射 | **是** | 否 | 否 | 否 | 谨慎 | `StreamerSettings.cs:1631`；UPnP 分支 `ConnectionManager.cs:414-441,466-469` |
| 22 | `EncryptLocalTraffic` | bool | — (false) | true / false | **局域网流量加密**。UI 明确提示「需断开重连才生效」 | **是** | 否 | 否 | **是** | 谨慎 | `StreamerSettings.cs:1682`；提示文案 `MainWindow.xaml.cs:1382-1393` |
| 23 | `AutoAdjustBitrate` | bool | **true** | true / false | **按可用带宽自动调码率**。UI tooltip 明确「requires re-connection」 | 否 | **是** | 否 | **是** | 可改 | `StreamerSettings.cs:1734`；同步到 shared `ConnectionManager.cs:199` |
| 24 | `StartWithWindows` | bool | **true** | true / false | 开机自启。**Streamer 自己不写注册表**；`Initialize()` 在距上次连接 >60 天时强制置 false | 否 | 否 | 否 | 否 | 谨慎 | `StreamerSettings.cs:1786`；60 天降级 `:3395-3400` |
| 25 | `StartMinimizedInTray` | bool | — (false) | true / false | 启动时最小化到托盘 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:1831` |
| 26 | `AutoSelectMicrophone` | bool | **true** | true / false | 麦克风直通开启时，自动在 Windows 里选中 VD 麦克风 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:1879` |
| 27 | `UseVirtualAudioDriver` | bool | **true** | true / false | 使用 VD 虚拟音频驱动。音频出问题的首选开关 | 否 | 否 | 否 | 否 | 谨慎 | `StreamerSettings.cs:1931`；UI 联动 `MainWindow.xaml:659-661` |
| 28 | `VoiceMeeterMode` | bool | — (false) | true / false | VoiceMeeter 兼容模式。UI 中仅在 `UseVirtualAudioDriver=true` 时可点 | 否 | 否 | 否 | 否 | 谨慎 | `StreamerSettings.cs:1982`；UI `IsEnabled` 绑定 `MainWindow.xaml:659` |
| 29 | `UseTouchInput` | bool | **true** | true / false | 允许触摸手势。UI tooltip「Disable if clicks don't work correctly」 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2034` |
| 30 | `LockComputer` | bool | **false** | true / false | 断开连接时锁屏 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2074` |
| 31 | `ShowPairingRequests` | bool | **true** | true / false | **新头显在局域网搜索电脑时弹配对确认框**。UI tooltip 原文：「Shows a prompt when a new headset is searching for computers in your local network」 | **是** | 否 | 否 | 否 | 谨慎 | `StreamerSettings.cs:2121` |
| 32 | `ShownH264PlusWarning` | bool | — (false) | true / false | H.264+ 警告是否已弹过（一次性标记）。本机实测为 `true` | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2176` |
| 33 | `UseFovStencil` | bool | **true** | true / false | 渲染时跳过画面四角（FOV stencil），降 GPU 开销。录屏时需关闭 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2223`；UI 多绑定 `MainWindow.xaml:884-903` |
| 34 | `HorizontalFovTangent` | float | **1.0** | UI 滑块 0.4–1.0（step 0.01） | 水平 FOV 正切缩放。UI 与 `UseFovStencil` 联动 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2263`；滑块 `MainWindow.xaml:770-779` |
| 35 | `VerticalFovTangent` | float | **1.0** | UI 滑块 0.4–1.0 | 垂直 FOV 正切缩放 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2323`；滑块 `MainWindow.xaml:789-798` |
| 36 | `RenderResolutionOverride` | float | **1.0** | UI 滑块 0.5–2.0 | VDXR 渲染分辨率倍率。UI 建议保持 100%，改用头显端 VR Graphics Quality | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2383`；滑块 `MainWindow.xaml:808-817` |
| 37 | `FoveaSize` | float | **0.25** | UI 滑块 0.15–0.35 | 注视点串流的注视区大小，仅对眼动头显 + foveated streaming 生效 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2431`；滑块 `MainWindow.xaml:833-842` |
| 38 | `UseSharpening` | bool | **false** | true / false | 编码前加 CAS（对比度自适应锐化） | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2474`；UI `MainWindow.xaml:847-851` |
| 39 | `Sharpening` | float | **0.7** | UI 滑块 0.05–1.0 | 锐化强度。常量 `DefaultSharpening = 0.7f`（`StreamerSettings.cs:3464`）。仅在 `UseSharpening=true` 时可用 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2514`；滑块 `MainWindow.xaml:854-863` |
| 40 | `PreferredCodec` | int (enum `VideoCodec`) | — (0 = `Automatic`) | 0 Automatic / 1 H.264 / 2 HEVC / 3 VP8 / 4 VP9 / 5 H.264+ / 6 HEVC 10-bit / 10 AV1 / **11 AV1 10-bit** | **首选视频编码器**。`Initialize()` 见到旧的 `AV1`(=10) 会自动升级为 `AV110bit`(11)（`:3442-3445`） | 否 | **是** | 否 | **是** | 可改 | `StreamerSettings.cs:2553`；枚举 `Net/VideoCodec.cs:6-18`；AV1 升级 `:3442-3445` |
| 41 | `EnableAQ` | bool | **true** | true / false | 自适应量化（AQ），降低暗部压缩伪影 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2605`；UI `MainWindow.xaml:581` |
| 42 | `EnableYuv444` | bool | — (false) | true / false | 4:4:4 色度采样。**UI 中 `Visibility="Collapsed"`**（仅 iPhone/iPad 暴露）；PC 侧被 `Features` 位掩码门控 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2645`；UI 折叠 `MainWindow.xaml:583-588`；门控 `DesktopStreamer.cs:1058` |
| 43 | `Enable2Pass` | bool | — (false) | true / false | 两遍编码，提升压缩质量但吃 GPU。UI 仅在 `DynamicSettings.Supports2Pass=true` 时显示 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:2684`；UI 可见性 `MainWindow.xaml:591-592` |
| 44 | `AudioStreaming` | int (enum `AudioStreaming`) | **`HeadsetOnly`** (1) | 0 Disabled(仅电脑) / 1 HeadsetOnly / 2 HeadsetAndComputer | **音频串流范围** | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2724`；枚举 `Streamer/AudioStreaming.cs:6-13` |
| 45 | `GamepadEmulation` | int (enum `GamepadEmulation`) | **`Automatic`** (0) | 0 Automatic / 1 Xbox / 2 Dualshock | 手柄模拟型号。映射到 `GameModel.Xbox360` / `DualShock4` | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2776`；枚举 `Streamer/GamepadEmulation.cs:5-10`；映射 `StreamerManager.cs:828-840` |
| 46 | `DeviceName` | string | — (null) | 任意字符串（显示名） | **上次连接的头显名**，本机实测 `"Meta Quest 3"`。由运行时写入，UI 只读展示 | 否 | 否 | 否 | 否 | 谨慎（会被覆盖） | `StreamerSettings.cs:2827`；写入 `OculusStreamer.cs:3111` |
| 47 | `CodecName` | string | — (null) | 任意字符串（显示名） | 上次实际协商到的编码器名，本机实测 `"AV1 10-bit"`。运行时写入 | 否 | 否 | 否 | 否 | 谨慎（会被覆盖） | `StreamerSettings.cs:2864`；写入 `OculusStreamer.cs:885`、`DesktopStreamer.cs:1142` |
| 48 | `VideosRootPath` | string | — (null) | 任意存在的目录路径；null/不存在时回退到 `Environment.SpecialFolder.MyVideos` | 视频目录，VR 里可浏览。实测 `"C:\\Users\\dwgx1\\Videos\\"`（自动补尾部分隔符） | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2901`；默认回退 `DynamicSettings.cs:1213-1234` |
| 49 | `ScreenshotsRootPath` | string | — (null) | 任意存在的目录路径；null 时回退到 `SpecialFolder` 索引 0 | VR 截图复制目标目录。UI 「Change location...」 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2938`；默认回退 `DynamicSettings.cs:1413-1451`；写 UI `MainWindow.xaml.cs:1108` |
| 50 | `LastConnectDate` | DateTime | — (`DateTime.MinValue`) | ISO-8601；**精度只到日**（`Initialize()` 归一化为「55 天前的今天」） | 上次连接日期。距今 >60 天时强制 `StartWithWindows=false` | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:2975`；归一化 `:3384-3388` |
| 51 | `BoostGamePriority` | bool | — (false) | true / false | 提升游戏 GPU 优先级（治游戏卡死）。UI tooltip 明确「requires restart of game/SteamVR」 | 否 | **是** | 否 | 否（需重启游戏） | 可改 | `StreamerSettings.cs:3016`；UI `MainWindow.xaml:905-906` |
| 52 | `OpenXRRuntime` | int (enum `OpenXRRuntime`) | — (0 = `Automatic`) | 0 Automatic / 1 SteamVR / 2 VDXR | **OpenXR 运行时选择**，决定 VR 里用哪套 runtime。本机实测 `1`（SteamVR） | 否 | **是** | 否 | **是** | 可改 | `StreamerSettings.cs:3062`；枚举 `Interfaces/OpenXRRuntime.cs:5-10` |
| 53 | `MonitorCount` | int | — (0) | 由硬件编码器与 HMD 类型自动钳制；VR HMD 走多显示器上限 | **串流显示器数量**。`StreamerManager.cs:654` 会按 `HasHardwareEncoder` 或 `--AllowMultiMonitor` 钳制，非 VR 时强制回写 1 | 否 | **是** | 否 | 否 | 可改 | `StreamerSettings.cs:3101`；钳制 `StreamerManager.cs:654-658`、`DynamicSettings.cs:402-404` |
| 54 | `DontWarnApps` | string[] (`HashSet<string>`) | — (空集) | 只能取 23 个合法 `AppError.ID` 之一 | 已被用户勾掉「不再警告」的干扰应用 | 否 | 否 | 否 | 否 | 可改 | `StreamerSettings.cs:3140`；读 `AppsCheckerManager.cs:99,124,166,195,236,280,325` |

**`DontWarnApps` 的 23 个合法值**（逐字取自 `-/.3.cs` … `-/.25.cs` 的 `base.ID`）：

```
Action, AMDDriverHEVC, AMDDriverFreeze, Avast, Bitdefender, CCleanerPro,
COMODO, ESET, ExpressVPN, LenovoNerveCenter, LenovoVantage, Malwarebytes,
NetLimiter, NetworkProfile, NordVPN, OpenXRToolkit, Portmaster,
PunkBuster, SteelSeriesSonar, SurfShark, TotalSecurity, Webroot,
WindowsVersion
```

> 写入非法 ID 不会报错（`HashSet<string>` 无校验），只是永远匹配不上任何检查项——**静默无效**。

---

## 2. `BindingSettings.json` — 热键（2 键）

文件：`%ProgramData%\Virtual Desktop\BindingSettings.json`（本机不存在，仅在热键改动后落盘）
类：`VirtualDesktop/Streamer/BindingSettings.cs:19`

| # | 键名（JSON 路径） | 类型 | 默认 | 合法取值 | 作用 | LAN | 画质 | 重启 | 安全 | 来源 file:line |
|---|---|---|---|---|---|---|---|---|---|---|
| 55 | `Bindings` | object (dictionary) | — (null) | 键为 `BindingAction` 枚举名，值为 `BindingShortcut` | 全局热键绑定表。默认键见 `BindingAction.cs:10-27` | 否 | 否 | 否 | 可改 | `BindingSettings.cs:21` |
| 56 | — (`[JsonIgnore] HotKeysEnabled`) | bool | — (false) | 不落盘 | 热键总开关。由连接状态驱动（`MainWindow.xaml.cs:647`） | 否 | 否 | 否 | 运行时 | `BindingSettings.cs:40`（`[JsonIgnore]`） |

**`BindingAction` 全部 10 个合法键名 + 默认快捷键**（`BindingAction.cs:8-28`）：

| 枚举名 | 默认键 | 汉化（profile csv） |
|---|---|---|
| `SwitchMonitor` | Shift+Win+A | 切换显示器 |
| `ToggleVRMode` | Shift+Win+D | 切换 VR 模式 |
| `ToggleVRPassthrough` | Shift+Win+C | 切换 VR 透视 |
| `EnableVRPassthrough` | Shift+Win+Z | 启用 VR 透视 |
| `DisableVRPassthrough` | Shift+Win+X | 禁用 VR 透视 |
| `ToggleHandPassthrough` | Shift+Win+H | 切换手部透视 |
| `ToggleDeskPassthrough` | Shift+Win+K | 切换桌面透视 |
| `TogglePerformanceOverlay` | Shift+Win+O | 切换性能叠加层 |
| `ToggleFoveatedStreaming` | Shift+Win+G | 切换注视点串流 |

> csv 里只有 9 行，`ToggleVRMode` 之后的枚举共 10 个（我数了 `BindingAction.cs` 的定义）。CSV 少一行是 profile 侧的事，不影响枚举本身。

---

## 3. `GameSettings.json` — 游戏库映射（3 键）

文件：`%APPDATA%\Virtual Desktop\GameSettings.json`（本机实测 258 B）
类：`VirtualDesktop/Streamer/GameSettings.cs:9`

| # | 键名（JSON 路径） | 类型 | 默认 | 合法取值 | 作用 | LAN | 画质 | 重启 | 安全 | 来源 file:line |
|---|---|---|---|---|---|---|---|---|---|---|
| 57 | `OculusExperienceNames` | object (`ulong` → string) | — (空) | key = experience ulong id | Oculus 体验名称缓存 | 否 | 否 | 否 | 可改 | `GameSettings.cs:11` |
| 58 | `SteamProductInfos` | object (`uint` appid → `SteamProductInfo`) | — (空) | 本机实测 `"438100"`（Half-Life 2） | Steam 游戏的 VR 支持标记（`OpenVRSupport`/`OpenXRSupport`/`Executable`/`LaunchType`） | 否 | 否 | 否 | 可改 | `GameSettings.cs:30`；消费 `GameManager.cs:2493,3090` |
| 59 | `ShippingExe` | object (`uint` appid → string/null) | — (空) | 可为 null | Steam shipping exe 路径缓存 | 否 | 否 | 否 | 可改 | `GameSettings.cs:49` |

**这三个键都与串流质量无关**，纯游戏启动映射。VDH 工具可以不解析。

---

## 4. `Shared*Settings` — 串流质量参数（**PC 上不是本地配置**）

三个类的共同基类 `NetworkSettingsBase<T>`（`Net/NetworkSettingsBase.cs:13`）。
**PC 侧角色**（`ConnectionManager.cs:203-205`）：`SharedUserSettings`/`SharedMobileSettings` 是 **client**（永不落盘），`SharedStreamerSettings` 是 **server**（变化后才落盘到 `%APPDATA%\Virtual Desktop\SharedStreamerSettings.json`）。

以下参数**在 VDH 里必须从头显端（或 Streamer 内存）取，不能只读 PC 的 JSON**。

### 4.1 `SharedUserSettings`（15 键，头显持有）

类：`VirtualDesktop/Mobile/SharedUserSettings.cs:12`

| # | 键名 | 类型 | 默认 | 合法取值 | 作用 | 画质 | 来源 file:line |
|---|---|---|---|---|---|---|---|
| 60 | `VRFramerate` | enum `Framerate` | **`Framerate.Default`** (0) | 0 Default / 24 / 30 / 60 / 72 / 80 / 90 / 96 / 100 / 120 | **VR 串流帧率上限** | **是** | `SharedUserSettings.cs:847`；枚举 `Mobile/Framerate.cs:6-18` |
| 61 | `VRGraphicsQuality` | enum `VRGraphicsQuality` | **`Medium`** (1) | -1 Potato / 0 Low / 1 Medium / 2 High / 3 Ultra / 4 Godlike / 5 Monster | **VR 图形质量档**，直接决定渲染分辨率 | **是** | `SharedUserSettings.cs:898`；枚举 `Mobile/VRGraphicsQuality.cs:6-15` |
| 62 | `DesktopBitrateLimit` | float | **0.17** | UI 语义为码率上限比例（0.17 ≈ 低档） | **桌面串流码率上限** | **是** | `SharedUserSettings.cs:949` |
| 63 | `VRBitrateLimit` | float | **0.36** | 同上 | **VR 串流码率上限** | **是** | `SharedUserSettings.cs:1000` |
| 64 | `Gamma` | float | **1.0** | 1.0 = 无校正 | 伽马校正 | 是 | `SharedUserSettings.cs:1051` |
| 65 | `UseOptimalResolution` | bool | **true** | true / false | 自动选最优桌面分辨率 | **是** | `SharedUserSettings.cs:1102` |
| 66 | `EmulateGamepad` | bool | **true** | true / false | 模拟手柄 | 否 | `SharedUserSettings.cs:1153` |
| 67 | `MicPassthrough` | bool | **true** | true / false | **麦克风直通** | 否 | `SharedUserSettings.cs:1204`；PC 侧消费 `Microphone.cs:469` |
| 68 | `MicVolume` | float | **0.85** | 0.0–1.0 | 麦克风音量 | 否 | `SharedUserSettings.cs:1255` |
| 69 | `IncreaseVideoNominalRange` | bool | — (false) | true / false | 提升视频 nominal range | **是** | `SharedUserSettings.cs:1305` |
| 70 | `ForwardTrackingData` | bool | — (false) | true / false | 转发追踪数据 | 否 | `SharedUserSettings.cs:1355` |
| 71 | `EmulateTrackers` | bool | — (false) | true / false | 模拟 tracker | 否 | `SharedUserSettings.cs:1405` |
| 72 | `EmulateIndexControllers` | bool | — (false) | true / false | 模拟 Index 手柄 | 否 | `SharedUserSettings.cs:1455` |
| 73 | `ExtraDisplay` | bool | — (false) | true / false | 附加显示器。断开时强制清零（`ConnectionManager.cs:404`） | **是** | `SharedUserSettings.cs:1505` |
| 74 | `UseMultiModal` | bool | — (false) | true / false | 多模态 | 否 | `SharedUserSettings.cs:1555` |

### 4.2 `SharedMobileSettings`（22 键，头显持有）

类：`VirtualDesktop/Mobile/SharedMobileSettings.cs:19`

| # | 键名 | 类型 | 默认 | 合法取值 | 作用 | 画质 | 来源 file:line |
|---|---|---|---|---|---|---|---|
| 75 | `HmdType` | enum `HmdType` | **`\u0001`（Oculus Quest = 259）** | -1 未知 / 10 iPhone / 20 iPad / 25 AppleTV / 30 AndroidPhone / 40 Tablet / 45 TV / 50 Desktop / 259 Quest / 320 Quest2 / 400 QuestPro / 600 Focus3 / 650 XR Elite / 700 PicoNeo3 / 710 PicoNeo3Link / 800 Pico4 / 1000 Quest3 / 1001 Quest3S / 1010 Pico4Ultra / 1020 ? / 1100 GalaxyXR / 1200 XREAL Aura / 1400 PFD MR / 1500 Steam Frame | **头显型号**，决定多显示器上限等分支 | 是 | `SharedMobileSettings.cs:698`；枚举 `Xenko/VR/HmdType.cs:6-50` |
| 76 | `Resolution` | `Size2` | — (null) | 宽高 | 桌面串流输出分辨率 | **是** | `SharedMobileSettings.cs:742` |
| 77 | `IsPortrait` | bool? | **null** | true / false / null | 竖屏（手机）。`null` = 未指定 | 是 | `SharedMobileSettings.cs:784` |
| 78 | `IPD` | float | **0.0635** | 米，约 0.05–0.08 | 瞳距 | 否 | `SharedMobileSettings.cs:832` |
| 79 | `FovLeft` | `FovPort` | — (null) | 结构体 | 左眼 FOV 覆盖 | 是 | `SharedMobileSettings.cs:871` |
| 80 | `FovRight` | `FovPort` | — (null) | 结构体 | 右眼 FOV 覆盖 | 是 | `SharedMobileSettings.cs:910` |
| 81 | `LeftHanded` | bool | — (false) | true / false | 左手模式 | 否 | `SharedMobileSettings.cs:949` |
| 82 | `MeasuredBandwidth` | int | — (0) | 整数 | **实测带宽**，`AutoAdjustBitrate` 的输入 | **是** | `SharedMobileSettings.cs:988` |
| 83 | `IsVideoPaused` | bool | — (false) | true / false | 视频暂停 | 否 | `SharedMobileSettings.cs:1075` |
| 84 | `UseSpacewarp` | bool | — (false) | true / false | SpaceWarp（异步重投影） | **是** | `SharedMobileSettings.cs:1114` |
| 85 | `DesktopFramerate` | int | **60** | 任意 int（24/30/60/90/120 等） | **桌面串流帧率** | **是** | `SharedMobileSettings.cs:1154`；PC 消费 `DesktopStreamer.cs:468,1053` |
| 86 | `VideosFolderPath` | string | **`""`** | 任意路径 | 头显端视频目录 | 否 | `SharedMobileSettings.cs:1194` |
| 87 | `DesktopBitrate` | int | — (0) | `[JsonIgnore]`，不落盘不传输 | 运行时桌面码率 | **是** | `SharedMobileSettings.cs:1251`（`[JsonIgnore]`） |
| 88 | `VRBitrate` | int | — (0) | `[JsonIgnore]` | 运行时 VR 码率 | **是** | `SharedMobileSettings.cs:1298`（`[JsonIgnore]`） |
| 89 | `H264PlusVRBitrate` | int | — (0) | `[JsonIgnore]` | 运行时 H.264+ VR 码率 | **是** | `SharedMobileSettings.cs:1331`（`[JsonIgnore]`） |
| 90 | `AV1VRBitrate` | int | — (0) | `[JsonIgnore]` | 运行时 AV1 VR 码率 | **是** | `SharedMobileSettings.cs:1364`（`[JsonIgnore]`） |
| 91 | `VRFramerate` | int | — (0) | `[JsonIgnore]`（注意：与 #60 的枚举版同名不同类） | 运行时 VR 帧率 | **是** | `SharedMobileSettings.cs:1397`（`[JsonIgnore]`） |
| 92 | `Features` | 标志枚举 | — (0) | 位掩码，PC 侧按位门控 YUV444 / 2-pass / 多显示器 | **头显能力位**，`Features & X > Features.Y` 形式判断 | **是** | `SharedMobileSettings.cs:1455`；门控 `OculusStreamer.cs:822`、`DesktopStreamer.cs:1058` |
| 93 | `VideoPlaybackSpeed` | float | **1.0** | 任意 float | 本地视频回放速度 | 否 | `SharedMobileSettings.cs:1506` |
| 94 | `UseAnnexB` | bool | **true** | true / false | NAL 格式 Annex-B | 是 | `SharedMobileSettings.cs:1565` |
| 95 | `FoveatedStreaming` | bool | **false** | true / false | **注视点串流**，配合 `FoveaSize` | **是** | `SharedMobileSettings.cs:1604` |
| 96 | `RefreshRate` | int | — (0) | 任意 int | **刷新率** | **是** | `SharedMobileSettings.cs:1654` |

> **#87–#91 这 5 个键带 `[JsonIgnore]`**：既不落盘、也不走 NetMessage 同步。它们是连接期间算出来的运行时值。**VDH 不能通过读参数拿到它们**，要靠实测或读 Streamer 内存。

### 4.3 `SharedStreamerSettings`（15 键，PC 侧 server）

类：`VirtualDesktop/Mobile/SharedStreamerSettings.cs:12`

| # | 键名 | 类型 | 默认 | 合法取值 | 作用 | 画质 | 来源 file:line |
|---|---|---|---|---|---|---|---|
| 97 | `HasVR` | bool | — (false) | true / false | 当前是否有 VR runtime | 是 | `SharedStreamerSettings.cs:672` |
| 98 | `StreamingSource` | enum `StreamingSource` (byte) | — (0 = `Desktop`) | 0 Desktop / 1 VR / 2 Video / 3 LocalVideo | **串流源**。VR 模式下强制回 Desktop（`StreamerManager.cs:672-674`） | 是 | `SharedStreamerSettings.cs:711`；枚举 `Net/StreamingSource.cs:6-11` |
| 99 | `ActiveCodec` | enum `VideoCodec` | — (0) | 同 #40 | 实际生效的编码器 | 是 | `SharedStreamerSettings.cs:752` |
| 100 | `AutoAdjustBitrate` | bool | — (false) | true / false | shared 侧的码率自适应开关。**由 PC 的 `StreamerSettings.AutoAdjustBitrate` 单向灌入**（`ConnectionManager.cs:199`） | **是** | `SharedStreamerSettings.cs:791` |
| 101 | `CanSwitchMonitor` | bool | — (false) | true / false | 能否切显示器 | 否 | `SharedStreamerSettings.cs:837` |
| 102 | `CanLaunchSteamVR` | bool | **true** | true / false | 能否启动 SteamVR | 否 | `SharedStreamerSettings.cs:877` |
| 103 | `ActiveRuntime` | enum `Runtime` | — (`\u0001`) | 0 未知 / 1 SteamVR / 2 VDXR / 3 VDXR+OC / 4 SteamXR | 生效的 runtime | 是 | `SharedStreamerSettings.cs:916`；枚举 `Mobile/Runtime.cs:6-17` |
| 104 | `AllowMotionExtrapolation` | bool | **true** | true / false | 允许运动外推（降低延迟） | **是** | `SharedStreamerSettings.cs:955` |
| 105 | `AdapterName` | string | — (null) | 显卡名 | 实际使用的 GPU 名 | 否 | `SharedStreamerSettings.cs:994`；写入 `OculusStreamer.cs:817` |
| 106 | `AdapterIsAMD` | bool | — (false) | true / false | GPU 是否 AMD（AMD 有 Instant Replay 干扰告警） | 否 | `SharedStreamerSettings.cs:1068`；`AppsCheckerManager.cs:151` |
| 107 | `DownloadState` | enum `DownloadState` | — (0 = `None`) | 0 None / 1 CanPaste / 2 RetrievingInfo / 3 Downloading | YouTube 下载状态 | 否 | `SharedStreamerSettings.cs:1106`；枚举 `Mobile/DownloadState.cs:6-13` |
| 108 | `DownloadProgress` | float | — (0) | 0.0–1.0 | 下载进度 | 否 | `SharedStreamerSettings.cs:1145` |
| 109 | `InputLanguage` | string | — (null) | 语言标记 | 输入法语言 | 否 | `SharedStreamerSettings.cs:1184` |
| 110 | `CanAddMonitor` | bool | — (false) | true / false | 能否加显示器 | 否 | `SharedStreamerSettings.cs:1258` |
| 111 | `CanRemoveMonitor` | bool | — (false) | true / false | 能否删显示器 | 否 | `SharedStreamerSettings.cs:1297` |

---

## 5. `DynamicSettings` — **纯运行时，不落盘**

类：`VirtualDesktop/Streamer/DynamicSettings.cs:26`，基类是 `SettingsBase`（**不是** `SerializedSettingsBase`）→ **没有任何 JSON 键**。
但这些是 VDH「显示参数」面板最有价值的诊断数据，全部来自 UI 绑定（`MainWindow.xaml`）。

| 属性 | 类型 | 来源 file:line | UI 位置 | 诊断意义 |
|---|---|---|---|---|
| `IsConnected` | bool | `DynamicSettings.cs:132` | 托盘菜单 | 是否已连接 |
| `IsEditingAccounts` | bool | `DynamicSettings.cs:171` | 账户页 | 是否在编辑账号 |
| `AdapterName` | string | `DynamicSettings.cs:217` | About 页「Graphics Card:」`MainWindow.xaml:1158` | 实际选中的 GPU |
| `IsLocalTrafficEncrypted` | bool | `DynamicSettings.cs:254` | 「Encrypt local traffic」勾选框回读 `MainWindow.xaml.cs:1389` | 加密是否**已生效**（≠ 配置值） |
| `Monitors` | `MonitorInfo[]` | `DynamicSettings.cs:300` | — | 枚举到的显示器 |
| `MonitorStartIndex` | int | `DynamicSettings.cs:420` | — | 当前起始显示器 |
| `Latency` | int (ms) | `DynamicSettings.cs:471` | About 页「Latency:」`MainWindow.xaml:1188` | **实时延迟，VDH 核心指标**。仅在 Options 页可见时刷新（`StreamerManager.cs:854-856`） |
| `IsAppCheckerWindowVisible` | bool | `DynamicSettings.cs:517` | About 页按钮可用性 `MainWindow.xaml:1335` | — |
| `VideosRootPath` | string | `DynamicSettings.cs:563` | Media 页 `MainWindow.xaml:1010` | 已回退后的有效值 |
| `VideosRootPathImage` | `BitmapSource` | `DynamicSettings.cs:602` | Media 页图标 | — |
| `VideoFolders` | string[] | `DynamicSettings.cs:636` | Media 页列表 `MainWindow.xaml:1038` | — |
| `ScreenshotsRootPath` | string | `DynamicSettings.cs:670` | Media 页 `MainWindow.xaml:971` | 已回退后的有效值 |
| `ScreenshotsRootPathImage` | `BitmapSource` | `DynamicSettings.cs:709` | Media 页图标 | — |
| `RouterUrl` | string | `DynamicSettings.cs:743` | About 页「Router Settings:」`MainWindow.xaml:1291-1293` | **路由器管理 URL**，LAN 诊断要点 |
| `ConnectionType` | enum `NetworkConnectionType` | `DynamicSettings.cs:780` | About 页「PC Ethernet:」`MainWindow.xaml:1252` | **PC 网卡类型**，LAN 诊断要点 |
| `RoutingStatus` | enum `RoutingStatus` | `DynamicSettings.cs:826` | About 页「Remote Routing:」`MainWindow.xaml:1276` | **PublicIP/NAT/DoubleNAT/CGNAT**，NAT 诊断要点 |
| `HasHardwareEncoder` | bool | `DynamicSettings.cs:872` | About 页「Hardware Encoder:」`MainWindow.xaml:1209` | 硬件编码器可用性 |
| `SupportsYuv444` | bool | `DynamicSettings.cs:918` | — | 头显是否支持 4:4:4 |
| `Supports2Pass` | bool | `DynamicSettings.cs:964` | 2-Pass 勾选框可见性 `MainWindow.xaml:591` | 头显是否支持 2-pass |
| `HasModifiedHostFile` | bool | `DynamicSettings.cs:1010` | About 页 `MainWindow.xaml:1359` | hosts 文件被改过 |
| `ForceKeyFrame` | bool | `DynamicSettings.cs:1056` | — | 强制关键帧（瞬时） |
| `SecureBoot` | bool | `DynamicSettings.cs:1088` | About 页「Secure Boot:」`MainWindow.xaml:1242` | 安全启动状态 |
| `IsWindowVisible` | bool | `DynamicSettings.cs:1120` | — | 窗口可见性 |
| `HasSwitchedMonitor` | bool | `DynamicSettings.cs:1152` | — | 是否切过显示器 |

> **重要**：`RoutingStatus` 的四个值 `PublicIP / NAT / DoubleNAT / CGNAT` 定义在 `Net/RoutingStatus.cs:6-11`——这是 VDH 做 NAT 诊断可直接复用的分类。
> `ConnectionType` 的 4 个成员在反编译里被混淆成 `\u0001`–`\u0004`（`Net/NetworkConnectionType.cs:6-13`），只有 UI 标签「PC Ethernet」可读。**枚举字面量值需连同上下文重新确认**。

---

## 6. 命令行参数（不进 JSON，优先级最高）

类：`VirtualDesktop/Streamer/Arguments.cs:7`。由 `-/-.102.cs:12-137` 的反射解析器填充，规则是 `^(-{1,2}|\/)([a-z].*)`（`IgnoreCase`）匹配属性名，支持首字母缩写。

| 参数 | 类型 | 作用 | 来源 file:line |
|---|---|---|---|
| `-AllowMultiMonitor` | bool | **无硬件编码器时也允许多显示器**。绕过 `MonitorCount` 的钳制（`StreamerManager.cs:654`） | `Arguments.cs:41`；消费 `DesktopStreamer.cs:895` |
| `-DisableRenderPose` | bool | 禁用渲染姿态 | `Arguments.cs:9` |
| `-Exit` | bool | 启动后立即退出（单实例互斥用）。`App.xaml.cs:58-64` 检测到就 `Environment.Exit(100)` | `Arguments.cs:73`；消费 `App.xaml.cs:58` |

bool 参数后可以跟 `true`/`false` 字符串（`-AllowMultiMonitor false`）；单独给 `-X` 等价 `true`（`-/.102.cs:42-45`）。
未识别的参数只记日志，不中断（`-/.102.cs:58`）。

---

## 7. 越界行为汇总

| 场景 | 实际行为 | 证据 |
|---|---|---|
| JSON 里写非法 enum 值（如 `PreferredCodec: 99`） | Newtonsoft 把 int 直接塞进 enum 字段，`switch` 落到无匹配分支。**不抛异常，静默失效** | `VideoCodec.cs:6-18` 是纯 enum，无 `Enum.IsDefined` 校验（全树 grep 无此调用） |
| 写超范围浮点（如 `Sharpening: 5.0`） | 字段接受，UI 滑块夹到 `Maximum` 显示，**实际编码用的是超出值** | setter 无 clamp（`StreamerSettings.cs:2514-2552`）；滑块 `Maximum="1.0"` `MainWindow.xaml:860` |
| `DontWarnApps` 写非法 ID | 静默无效，不报错 | `AppsCheckerManager.cs:99` 只做 `Contains` |
| `MonitorCount` 写大于显示器数 | 下次连接被 `StreamerManager.cs:654` 钳制；非 VR 强制回写 1 | `StreamerManager.cs:654-658` |
| `PreferredCodec: 10`（旧 `AV1`） | `Initialize()` 自动升级为 11 并立即保存 | `StreamerSettings.cs:3442-3445` |
| `ProtectedComputerID` 解密失败 | **重新生成 computer ID + 清空所有账号 + `Save()`** | `StreamerSettings.cs:3401-3420` |
| JSON 语法错误 | `Reload()` 吞异常 → 全部重置为默认值 | `JsonSettingsBase.cs:128-137` |
| `StartWithWindows: true` 但距上次连接 >60 天 | 启动时强制置 `false` | `StreamerSettings.cs:3395-3400` |
| Streamer 运行中改 JSON | 2 秒内任何 UI 改动都会覆盖写入 | `SerializedSettingsBase.cs:194` |
| `EnableYuv444: true` 但头显不支持 | `Features` 位掩码门控，实际不生效 | `DesktopStreamer.cs:1058`、`OculusStreamer.cs:822` |
| `Enable2Pass: true` 但不支持 | UI 上根本显示不出这个勾选框 | `MainWindow.xaml:591` 绑定 `Supports2Pass` |

---

## 8. VDH 工具实现建议（按优先级）

1. **读**：`%ProgramData%\Virtual Desktop\StreamerSettings.json` → 合并 `[DefaultValue]` 兜底 → 展示 **36 个非密文键**（54 键中 18 个是 DPAPI 密文，见 §1 的只读行）。
2. **显示延迟**：`DynamicSettings.Latency` 不落盘，只能从 UI 读或从 Streamer 日志抓。
3. **显示网络拓扑**：`RouterUrl` / `ConnectionType` / `RoutingStatus` 三件套是 LAN 诊断的直接数据源，但都不在 JSON 里。
4. **改**：只改「不等于默认值」的键，且**先退出 Streamer**。安全白名单：`AutoAdjustBitrate` `PreferredCodec` `OpenXRRuntime` `MonitorCount` `EnableAQ` `Enable2Pass` `EnableYuv444` `RenderResolutionOverride` `FoveaSize` `UseSharpening`/`Sharpening` `UseFovStencil` `*FovTangent` `AudioStreaming` `GamepadEmulation` `VideosRootPath` `ScreenshotsRootPath` `DontWarnApps` `BoostGamePriority`。
5. **灰名单（改前必须提示用户）**：`AllowRemoteConnections` `EncryptLocalTraffic` `ShowPairingRequests` `StartWithWindows` `UseVirtualAudioDriver` `VoiceMeeterMode`。
6. **黑名单（永不写）**：`ProtectedComputerID` `Accounts` 及全部 `Protected*ID` / `SavedPicoID*`。
7. **重连语义要明示 UI**：`AutoAdjustBitrate` `EncryptLocalTraffic` `PreferredCodec` `OpenXRRuntime` 改了都需要断开重连。