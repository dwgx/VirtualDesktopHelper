# 01 — Quest 侧可调参数全集（主表）

> 研究对象：Virtual Desktop Android 客户端 **1.34.18.0**（Owner patched 版基线），分析对象是工作区内反编译源码。
> 覆盖范围：用户在头显 VD 应用 SETTINGS / STREAMING / INPUT 三个标签页里能改的**每一个**参数，
> 以及 PC Streamer 通过消息通道下发过来的参数，以及头显本地保存的运行态参数。
>
> **证据分层**：
> - `file:line` 均为本工作区反编译源码的真实行号，可直接复核。
> - 本机 adb 不可用（`adb` 不在 PATH，`D:/Software/Android/Sdk/platform-tools/adb.exe` 不存在），
>   因此所有 adb 读取形态都是**依据代码推导的草案**，标注 `[未验证]`。验证条件见 §6。
> - 码率上限 IL 常量已在本机**实际反汇编验证**（见 `03-il-constants.md`），非推导。
>
> **反编译树命名警告（重要）**：`analysis/apk_patch/extracted_assemblies/` 下的文件名**与程序集真名不对应**。
> 实测（`ilspycmd -l class` + 类型计数）：
> - `Xenko.dll`（529408 字节）才是 `VirtualDesktop.Mobile.dll`（含 132 个 `VirtualDesktop.Mobile.*` 类型）。
> - `Xenko.Rendering.dll`（87552 字节）才是 `VirtualDesktop.Mobile.Shared.dll`（含 `SharedStreamerSettings` /
>   `SharedUserSettings` / `SharedMobileSettings` / `Xenko.VR.HmdResolutionTypeExtensions`）。
> - `VirtualDesktop.Mobile.dll` / `VirtualDesktop.Mobile.Shared.dll` 这两个"看起来对"的文件实际内容是
>   `ZString` 和 `Oculus.Platform`。**工具实现时不要按文件名取程序集，必须按类型定位。**

---

## 0. 设置类总览：谁存、谁读、谁下发

VD Android 端一共有 **7 个设置类**，分三层：

| 层 | 类 | 基类 | 持久化 | 归属 | 权威方向 |
|---|---|---|---|---|---|
| L1 头显本地 | `VirtualDesktop.Mobile.UserSettings` | `SerializedSettingsBase<UserSettings>` | ✅ `UserSettings.json` | 仅头显 | 头显本地读写 |
| L1 头显本地 | `VirtualDesktop.Mobile.ScreenSettings` | `SerializedSettingsBase<ScreenSettings>` | ✅ `ScreenSettings.json` + `SecondaryScreenSettings.json` + `TertiaryScreenSettings.json` | 按显示器(最多3) | 头显本地读写 |
| L1 头显本地 | `VirtualDesktop.Mobile.VideoSettings` | `SerializedSettingsBase<VideoSettings>` | ✅ `VideoSettings.json` | 视频库列表 | 头显本地读写 |
| L2 运行时 | `VirtualDesktop.Mobile.DynamicSettings` | `SettingsBase<DynamicSettings>` | ❌ **不落盘** | 进程生命周期 | 头显运行时计算 |
| L3 联动 | `VirtualDesktop.Mobile.SharedUserSettings` | `NetworkSettingsBase<SharedUserSettings>` | ✅ 仅 IsServer 端落盘 | PC↔Quest | **头显是 server**，改了就推给 PC |
| L3 联动 | `VirtualDesktop.Mobile.SharedMobileSettings` | `NetworkSettingsBase<SharedMobileSettings>` | ✅ 仅 IsServer 端落盘 | 头显硬件能力 | **头显是 server**，描述头显自己（IPD/HMD类型/带宽测量） |
| L3 联动 | `VirtualDesktop.Mobile.SharedStreamerSettings` | `NetworkSettingsBase<SharedStreamerSettings>` | ❌ | PC 能力 | **头显是 client**，PC 推给头显 |

**方向证据**：`NetworkManager.cs:134-136`

```csharp
SettingsBase<SharedUserSettings>.Default.InitializeAsServerAsync(NetworkManager.MessagingClient);   // 头显持有主副本
SettingsBase<SharedMobileSettings>.Default.InitializeAsServer(NetworkManager.MessagingClient);     // 头显持有主副本
SettingsBase<SharedStreamerSettings>.Default.InitializeAsClient(NetworkManager.MessagingClient);   // PC 持有主副本
```

`NetworkSettingsBase.cs:276-290`（`StartSaveTimer` 只在 IsServer 时落盘）、`NetworkSettingsBase.cs:292-310`（`Reload` 非 server 时直接 return，不读本地文件）。
**推论**：Quest 上的 `SharedStreamerSettings.json` 即使存在也是死文件，工具不应读它。

### 消息通道映射

`Net/MessageType.cs:12-17`：

| MessageType | 值 | 用于 |
|---|---|---|
| `UserSettingsReload` | 11 | SharedUserSettings 全量重载 |
| `UserSettingsPropertyChanged` | 12 | SharedUserSettings 单键变更 |
| `MobileSettingsReload` | 13 | SharedMobileSettings 全量重载 |
| `MobileSettingsPropertyChanged` | 14 | SharedMobileSettings 单键变更 |
| `StreamerSettingsReload` | 15 | SharedStreamerSettings 全量重载 |
| `StreamerSettingsPropertyChanged` | 16 | SharedStreamerSettings 单键变更 |

单键变更的载体是 `propertyName` 字符串（`NetworkSettingsBase.cs:185-235`，`OnPropertyChanged<TValue>(value, propertyName)`）。
**这意味着 JSON 的字段名 = 协议里的属性名 = 工具可用的 key**，见 `02-storage-and-io.md`。

---

## 1. 主表 A — `UserSettings`（L1 头显本地，SETTINGS/STREAMING/INPUT 三页共用）

文件：`F:/Project/VirtualDesktop/analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/UserSettings.cs`
类：`VirtualDesktop.Mobile.UserSettings`（`UserSettings.cs:17`）
存储：`UserSettings.json`
「默认值」列 = 源码字段初始化器 + `[DefaultValue]` 特性（后者决定 JSON 是否写出该字段）。

| 参数名 | 中文含义 | 类型 | 默认值 | 存储位置 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|---|
| Environment | 虚拟环境（背景） | `Environment` 枚举 | `3` (HomeCinema) | JSON `Environment` | `UserSettings.cs:313` / 字段 `UserSettings.cs:2443` | 画质 | ✅ | ✅ |
| EnvironmentQuality | 环境画质档 | `EnvironmentQuality` (Low=-1/Medium=0/High=1) | `0` (Medium) | JSON `EnvironmentQuality` | `UserSettings.cs:337` / `UserSettings.cs:2446` | 画质/延迟 | ✅ | ✅ |
| ScreenBrightness | 虚拟屏亮度（0~1，平方存储） | `float` | `1.0` | JSON `ScreenBrightness` | `UserSettings.cs:358` / `UserSettings.cs:2449` | 画质 | ✅ | ✅ |
| DynamicLightingBehavior | 动态光照策略 | `DynamicLightingBehavior` (0=Disabled/1=WhenControllerInactive/2=Always) | `1` | JSON `DynamicLightingBehavior` | `UserSettings.cs:380` / `UserSettings.cs:2452` | 延迟/性能 | ✅ | ✅ |
| LastComputerID | 上次连接的电脑 ID（空=禁用自动连接） | `string` | `null` | JSON `LastComputerID` | `UserSettings.cs:400` / `UserSettings.cs:2455` | **连通性** | ✅ | ✅ |
| ComputerOS | 已连电脑的 OS | `OS` (Windows/MacOS) | `0` (Windows) | JSON `ComputerOS` | `UserSettings.cs:419` / `UserSettings.cs:2458` | 连通性（选驱动） | ✅ | ✅ |
| AutoConnect | 启动后自动连上次电脑 | `bool` | `true` | JSON `AutoConnect` | `UserSettings.cs:440` / `UserSettings.cs:2461` | **连通性** | ✅ | ✅ |
| ShowGamesTab | 是否显示 GAMES 标签 | `bool` | `false` | JSON `ShowGamesTab` | `UserSettings.cs:460` / `UserSettings.cs:2464` | UI | ✅ | ✅ |
| ArrangeMonitorsOnRecenter | Recenter 时自动排布多显示器 | `bool` | `false` | JSON `ArrangeMonitorsOnRecenter` | `UserSettings.cs:480` / `UserSettings.cs:2467` | 显示器布局 | ✅ | ✅ |
| DesktopFramerate | 桌面模式目标帧率 | `Framerate` 枚举 (Default=0/24/30/60/72/80/90/96/100/120) | `0` (Default) | JSON `DesktopFramerate` | `UserSettings.cs:501` / `UserSettings.cs:2470` | **延迟/画质** | ✅ | ✅ |
| HiddenItems | 隐藏控制面板条目 | `bool` | `false` | JSON `HiddenItems` | `UserSettings.cs:521` / `UserSettings.cs:2473` | UI | ✅ | ✅ |
| BackgroundMusic | 环境背景音乐 | `bool` | `true` | JSON `BackgroundMusic` | `UserSettings.cs:542` / `UserSettings.cs:2476` | 音频 | ✅ | ✅ |
| NoiseCancellation | 麦克风降噪 | `bool` | `true` | JSON `NoiseCancellation` | `UserSettings.cs:563` / `UserSettings.cs:2479` | 音频 | ✅ | ✅ |
| ControllerDesktop | Touch 手柄作为桌面指针 | `bool` | `true` | JSON `ControllerDesktop` | `UserSettings.cs:584` / `UserSettings.cs:2482` | 输入 | ✅ | ✅ |
| HandDesktop | 手部追踪作为桌面指针 | `bool` | `true` | JSON `HandSettings`→`HandDesktop` | `UserSettings.cs:605` / `UserSettings.cs:2485` | 输入 | ✅ | ✅ |
| AutoHideController | 静止时隐藏手柄 | `bool` | `true` | JSON `AutoHideController` | `UserSettings.cs:626` / `UserSettings.cs:2488` | 稳定性 | ✅ | ✅ |
| AutoHideHands | 静止时隐藏手部模型 | `bool` | `true` | JSON `AutoHideHands` | `UserSettings.cs:647` / `UserSettings.cs:2491` | 稳定性 | ✅ | ✅ |
| PointerStabilization | 指针稳定（抑制抖动） | `bool` | `true` | JSON `PointerStabilization` | `UserSettings.cs:668` / `UserSettings.cs:2494` | **延迟** | ✅ | ✅ |
| TrackpadVerticalScrolling | 触摸板垂直滚动 | `bool` | `true` | JSON `TrackpadVerticalScrolling` | `UserSettings.cs:689` / `UserSettings.cs:2497` | 输入 | ✅ | ✅ |
| TrackpadHorizontalScrolling | 触摸板水平滚动 | `bool` | `false` | JSON `TrackpadHorizontalScrolling` | `UserSettings.cs:709` / `UserSettings.cs:2500` | 输入 | ✅ | ✅ |
| HandTracking | 启用手部追踪 | `bool` | `true` | JSON `HandTracking` | `UserSettings.cs:730` / `UserSettings.cs:2503` | 输入/稳定性 | ✅ | ✅ |
| GripScreen | 握持键吸附屏幕 | `bool` | `true` | JSON `GripScreen` | `UserSettings.cs:751` / `UserSettings.cs:2506` | 显示器布局 | ✅ | ✅ |
| HoldMenu | 长按 B 键呼出菜单 | `bool` | `true` | JSON `HoldMenu` | `UserSettings.cs:772` / `UserSettings.cs:2509` | 交互 | ✅ | ✅ |
| ControllerGamepad | 手柄模拟 XInput 手柄 | `bool` | `false` | **`[JsonIgnore]` 不落盘** `UserSettings.cs:792` / `UserSettings.cs:2512` | 输入 | ✅(运行时) | ❌ |
| DpadStartEmulationMode | 十字键/Start 模拟方式 | `DpadStartEmulationMode` (None/RGripPressed/RThumbPressed/RThumbRestActive) | `0` (None) | JSON `DpadStartEmulationMode` | `UserSettings.cs:812` / `UserSettings.cs:2515` | 输入 | ✅ | ✅ |
| Sharpening | 画面锐化强度（VR 页滑条） | `float` | `0.75` | JSON `Sharpening` | `UserSettings.cs:833` / `UserSettings.cs:2518` | 画质 | ✅ | ✅ |
| VRPassthrough | VR 透视背景 | `bool` | `false` | JSON `VRPassthrough` | `UserSettings.cs:853` / `UserSettings.cs:2521` | 画质/性能 | ✅ | ✅ |
| VRPassthroughSimilarity | 透视相似度阈值 | `float` | `0.2` | JSON `VRPassthroughSimilarity` | `UserSettings.cs:874` / `UserSettings.cs:2524` | 画质 | ✅ | ✅ |
| VRPassthroughSmoothness | 透视平滑度 | `float` | `0.1` | JSON `VRPassthroughSmoothness` | `UserSettings.cs:895` / `UserSettings.cs:2527` | 延迟 | ✅ | ✅ |
| VRPassthroughOpacity | 透视不透明度 | `float` | `1.0` | JSON `VRPassthroughOpacity` | `UserSettings.cs:936` / `UserSettings.cs:2530` | 画质 | ✅ | ✅ |
| VRPassthroughColor | 透视抠像颜色（RGBA） | `Color` | `Color.Black` | JSON `VRPassthroughColor` | `UserSettings.cs:915` / `UserSettings.cs:2533` | 画质 | ✅ | ✅ |
| SuperResolution | 超分辨率（foveated 上采样） | `bool` | `false` | JSON `SuperResolution` | `UserSettings.cs:956` / `UserSettings.cs:2548` | **画质/延迟** | ✅ | ✅ |
| VideoBuffering | 视频缓冲（低延迟 vs 抗卡顿） | `bool` | `true` | JSON `VideoBuffering` | `UserSettings.cs:977` / `UserSettings.cs:2551` | **延迟** | ✅ | ✅ |
| StageTracking | Room-scale 舞台追踪 | `bool` | `false` | JSON `StageTracking` | `UserSettings.cs:997` / `UserSettings.cs:2554` | 输入/稳定性 | ✅ | ✅ |
| TrackControllers | 追踪手柄 | `bool` | `true` | JSON `TrackControllers` | `UserSettings.cs:1018` / `UserSettings.cs:2557` | 输入 | ✅ | ✅ |
| FoveatedStreaming | 眼动注视点编码（foveated streaming） | `bool` | `false` | JSON `FoveatedStreaming` | `UserSettings.cs:1038` / `UserSettings.cs:2560` | **画质/延迟** | ✅ | ✅ |
| AllowCustomOrientation | 允许自定义朝向（不强制竖屏） | `bool` | `false` | JSON `AllowCustomOrientation` | `UserSettings.cs:1058` / `UserSettings.cs:2563` | 显示器布局 | ✅ | ✅ |
| HeadLockNoDelay | 头部锁定不引入延迟 | `bool` | `false` | JSON `HeadLockNoDelay` | `UserSettings.cs:1078` / `UserSettings.cs:2566` | **延迟** | ✅ | ✅ |
| BoostClockRates | 提升 CPU/GPU 时钟（性能模式） | `bool` | `false` | JSON `BoostClockRates` | `UserSettings.cs:1098` / `UserSettings.cs:2569` | 稳定性/温度 | ✅ | ✅ |
| CopyScreenshots | 截图复制到 PC 桌面 | `bool` | `true` | JSON `CopyScreenshots` | `UserSettings.cs:1119` / `UserSettings.cs:2572` | 权限相关 | ✅ | ✅ |
| LocalDimming | 本地调光（桌面流） | `bool` | `true` | JSON `LocalDimming` | `UserSettings.cs:1140` / `UserSettings.cs:2575` | 画质/性能 | ✅ | ✅ |
| LocalDimmingVR | 本地调光（VR 流） | `bool` | `true` | JSON `LocalDimmingVR` | `UserSettings.cs:1161` / `UserSettings.cs:2578` | 画质/性能 | ✅ | ✅ |
| IncreaseColorVibrance | 提高色彩鲜艳度（桌面流） | `bool` | `true` | JSON `IncreaseColorVibrance` | `UserSettings.cs:1182` / `UserSettings.cs:2581` | 画质 | ✅ | ✅ |
| IncreaseVRColorVibrance | 提高色彩鲜艳度（VR 流） | `bool` | `true` | JSON `IncreaseVRColorVibrance` | `UserSettings.cs:1203` / `UserSettings.cs:2584` | 画质 | ✅ | ✅ |
| ShowPerformanceOverlay | 显示性能浮层 | `bool` | `false` | JSON `ShowPerformanceOverlay` | `UserSettings.cs:1280` / `UserSettings.cs:2596` | 诊断 | ✅ | ✅ |
| SpacewarpBehavior | Spacewarp（异步重投影）策略 | `SpacewarpBehavior` (0=Disabled/1=Automatic/2=Always) | `1` (Automatic) | JSON `SpacewarpBehavior` | `UserSettings.cs:1305` / `UserSettings.cs:2545` | **延迟/稳定性** | ✅ | ✅ |
| MuteVideo | 视频/环境静音 | `bool` | `false` | JSON `MuteVideo` | `UserSettings.cs:1325` / `UserSettings.cs:2599` | 音频 | ✅ | ✅ |
| InstalledLanguage | 已装 UI 语言（由系统 locale 反写） | `string` | `null` | JSON `InstalledLanguage` | `UserSettings.cs:1383` / `UserSettings.cs:2608` | UI | ✅ | ⚠️ 只读 |
| DesktopTrackedKeyboard | 桌面流追踪键盘 | `bool` | `true` | JSON `DesktopTrackedKeyboard` | `UserSettings.cs:1450` / `UserSettings.cs:2611` | 输入 | ✅ | ✅ |
| VRTrackedKeyboard | VR 流追踪键盘 | `bool` | `false` | JSON `VRTrackedKeyboard` | `UserSettings.cs:1470` / `UserSettings.cs:2614` | 输入 | ✅ | ✅ |
| PassthroughHands | 桌面模式透视手部 | `bool` | `false` | JSON `PassthroughHands` | `UserSettings.cs:1511` / `UserSettings.cs:2617` | 输入/画质 | ✅ | ✅ |
| VRPassthroughHands | VR 模式透视手部 | `bool` | `false` | JSON `VRPassthroughHands` | `UserSettings.cs:1531` / `UserSettings.cs:2620` | 输入/画质 | ✅ | ✅ |
| VRShowBoundary | 显示房间边界 | `bool` | `true` | JSON `VRShowBoundary` | `UserSettings.cs:1491` / `UserSettings.cs:2623` | 稳定性 | ✅ | ✅ |
| DeskPortal | 桌面传送门锚点 | `DeskPortal` struct (Dimensions+AnchorUuid) | `DeskPortal.Zero` | JSON `DeskPortal` | `UserSettings.cs:1551` / `UserSettings.cs:2626` | 显示器布局 | ✅ | ✅ |
| VRDeskPortal | VR 模式启用桌面传送门 | `bool` | `false` | JSON `VRDeskPortal` | `UserSettings.cs:1570` / `UserSettings.cs:2629` | 显示器布局 | ✅ | ✅ |
| ScreenTransparencyColor | 屏幕透视抠像颜色（RGBA） | `Color` | `Color.Black` | JSON `ScreenTransparencyColor` | `UserSettings.cs:1632` / `UserSettings.cs:2536` | 画质 | ✅ | ✅ |
| ScreenTransparencySimilarity | 屏幕透视相似度阈值 | `float` | `0.01` | JSON `ScreenTransparencySimilarity` | `UserSettings.cs:1591` / `UserSettings.cs:2539` | 画质 | ✅ | ✅ |
| ScreenTransparencySmoothness | 屏幕透视平滑度 | `float` | `0.2` | JSON `ScreenTransparencySmoothness` | `UserSettings.cs:1612` / `UserSettings.cs:2542` | 延迟 | ✅ | ✅ |
| PreferEyeGazeInput | 优先眼动输入 | `bool` | `false` | JSON `PreferEyeGazeInput` | `UserSettings.cs:1652` / `UserSettings.cs:2632` | 输入 | ✅ | ✅ |
| ActiveSeats | 每个环境记住的座位号 | `Dictionary<string,int>` | `{}` | JSON `ActiveSeats` | `UserSettings.cs:1401` / `UserSettings.cs:2635` | 显示器布局 | ✅ | ⚠️ 慎改 |
| QuestionsAsked | 提问频率限制队列（16h 内 10 次） | `Queue<DateTime>` | `{}` | JSON `QuestionsAsked` | `UserSettings.cs:1412` / 字段 `UserSettings.cs:2440` | 遥测 | ✅ | ❌ |
| HasValidIdentity | 身份有效性（被 IL 补丁强制 true） | `bool` | `false` | JSON `HasValidIdentity` | `UserSettings.cs:1430` / `UserSettings.cs:2641` | **鉴权门控** | ✅ | ❌ |

`UserSettings` 上的 UI 复位按钮（"重置 VR 设置"）一次性写回的值见 `StreamingTab.cs:1134-1154`，工具可用它生成完整默认 JSON。

---

## 2. 主表 B — `ScreenSettings`（L1 头显本地，**按显示器**分 3 份）

文件：`.../VirtualDesktop.Mobile/ScreenSettings.cs`
类：`VirtualDesktop.Mobile.ScreenSettings`（`ScreenSettings.cs:11`）
存储：`ScreenSettings.json`（主屏）/ `SecondaryScreenSettings.json` / `TertiaryScreenSettings.json`
分派规则：`ScreenSettings.cs:225-236` — `Get(streamIndex)`，`1→Secondary`、`2→Tertiary`、其余→`Default`。
文件名注入：`ScreenSettings.cs:26-30`（静态构造）。

| 参数名 | 中文含义 | 类型 | 默认值 | 存储位置 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|---|
| ScreenSize | 虚拟屏尺寸 | `float` | `0.75` | JSON `ScreenSize` | `ScreenSettings.cs:45` / `ScreenSettings.cs:297` | 显示器布局 | ✅ | ✅ |
| ScreenHeight | 虚拟屏垂直偏移 | `float` | `-0.3` | JSON `ScreenHeight` | `ScreenSettings.cs:61` / `ScreenSettings.cs:300` | 显示器布局 | ✅ | ✅ |
| ScreenDistance | 虚拟屏距离 | `float` | `3.0` | JSON `ScreenDistance` | `ScreenSettings.cs:77` / `ScreenSettings.cs:303` | 显示器布局 | ✅ | ✅ |
| ScreenCurve | 虚拟屏曲率 | `float` | `1.0` | JSON `ScreenCurve` | `ScreenSettings.cs:93` / `ScreenSettings.cs:306` | 显示器布局 | ✅ | ✅ |
| HeadLock | 头部锁定该屏 | `bool` | `false` | **`[JsonIgnore]`** `ScreenSettings.cs:108` / `ScreenSettings.cs:309` | 显示器布局 | ✅(运行时) | ❌ |
| ScreenTransparency | 该屏开背景透视 | `bool` | `false` | JSON `ScreenTransparency` | `ScreenSettings.cs:128` / `ScreenSettings.cs:312` | 画质/性能 | ✅ | ✅ |
| Orientation | 自定义朝向矩阵 | `Matrix` | `Matrix.Identity` | JSON `Orientation` | `ScreenSettings.cs:148` / `ScreenSettings.cs:315` | 显示器布局 | ✅ | ✅ |
| PositionOffset | 位置偏移 | `Vector3` | `Vector3.Zero` | JSON `PositionOffset` | `ScreenSettings.cs:167` / `ScreenSettings.cs:318` | 显示器布局 | ✅ | ✅ |
| IsSnapped | 该屏已摆位吸附 | `bool` | `false` | JSON `IsSnapped` | `ScreenSettings.cs:186` / `ScreenSettings.cs:321` | 显示器布局 | ✅ | ✅ |

滑条范围常量（工具做边界校验用）：`ScreenSettings.cs:258-291`
`MinScreenSize=0.2 / MaxScreenSize=1.6 / ScreenSizeRange=1.4`、
`MinScreenHeight=-1 / MaxScreenHeight=0.2 / ScreenHeightRange=1.2`、
`MinScreenDistance=0.5 / MaxScreenDistance=15 / ScreenDistanceRange=14.5`、
`MinScreenCurve=0.1 / MaxScreenCurve=1 / ScreenCurveRange=0.9`

> ⚠️ **同名但不同常量陷阱**：`UserSettings.cs:2404-2434` 也有一套**范围不同**的常量
> （`MinScreenSize=0.4`、`ScreenSizeRange=1.2`、`MinScreenDistance=0.5`、`MaxScreenDistance=15`、
> `MinScreenCurve=0.1`）。两组同时存在，工具**必须区分是给 ScreenSettings 还是 UserSettings 用**。

---

## 3. 主表 C — `SharedUserSettings`（L3，**头显是 server**，头显改 → 推给 PC）

文件（权威副本，PC 侧反编译树）：`F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/VirtualDesktop/Mobile/SharedUserSettings.cs`
类：`VirtualDesktop.Mobile.SharedUserSettings`
存储：头显为 server → `SharedUserSettings.json` 落盘在头显（`NetworkSettingsBase.cs:276-290`）
意义：**这些是「头显本地保存且 PC 侧读得到」的参数**，改它们需要断连重连才生效。

| 参数名 | 中文含义 | 类型 | 默认值 | 存储位置 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|---|
| VRFramerate | VR 流目标帧率 | `Framerate` 枚举 | `Default`(=0) | JSON `VRFramerate` | `SharedUserSettings.cs:847` | **延迟/画质** | ✅ | ✅ |
| VRGraphicsQuality | VR 图形质量档 | `VRGraphicsQuality` (Potato=-1..Monster=5) | `Medium`(=1) | JSON `VRGraphicsQuality` | `SharedUserSettings.cs:898` | 画质/性能 | ✅ | ✅ |
| DesktopBitrateLimit | 桌面码率滑条位置（**归一化 0~1 系数，不是 Mbps**） | `float` | `0.17` | JSON `DesktopBitrateLimit` | `SharedUserSettings.cs:949` | **画质/码率** | ✅ | ✅ |
| VRBitrateLimit | VR 码率滑条位置（归一化 0~1） | `float` | `0.36` | JSON `VRBitrateLimit` | `SharedUserSettings.cs:1000` | **画质/码率** | ✅ | ✅ |
| Gamma | 伽马校正 | `float` | `1.0` | JSON `Gamma` | `SharedUserSettings.cs:1051` | 画质 | ✅ | ✅ |
| UseOptimalResolution | 使用"最优"分辨率（否则手动定分辨率） | `bool` | `true` | JSON `UseOptimalResolution` | `SharedUserSettings.cs:1102` | 画质 | ✅ | ✅ |
| EmulateGamepad | 手柄模拟 XInput | `bool` | `true` | JSON `EmulateGamepad` | `SharedUserSettings.cs:1153` | 输入 | ✅ | ✅ |
| MicPassthrough | 麦克风直通（PC 收头显麦） | `bool` | `true` | JSON `MicPassthrough` | `SharedUserSettings.cs:1204` | **音频 + 权限** | ✅ | ✅ |
| MicVolume | 麦克风音量（0~1） | `float` | `0.85` | JSON `MicVolume` | `SharedUserSettings.cs:1255` | 音频 | ✅ | ✅ |
| IncreaseVideoNominalRange | 提亮视频 nominal range（10→full range） | `bool` | `false` | JSON `IncreaseVideoNominalRange` | `SharedUserSettings.cs:1305` | 画质 | ✅ | ⚠️ 仅 PC 设 true |
| ForwardTrackingData | 向 PC 转发追踪数据 | `bool` | `false` | JSON `ForwardTrackingData` | `SharedUserSettings.cs:1355` | 输入/延迟 | ✅ | ✅ |
| EmulateTrackers | 模拟 Index 追踪器 | `bool` | `false` | JSON `EmulateTrackers` | `SharedUserSettings.cs:1405` | 输入 | ✅ | ✅ |
| EmulateIndexControllers | 模拟 Index 手柄 | `bool` | `false` | JSON `EmulateIndexControllers` | `SharedUserSettings.cs:1455` | 输入 | ✅ | ✅ |
| ExtraDisplay | 额外显示器 | `bool` | `false` | JSON `ExtraDisplay` | `SharedUserSettings.cs:1505` | 显示器布局 | ✅ | ✅ |
| UseMultiModal | 多模态手部输入 | `bool` | `false` | JSON `UseMultiModal` | `SharedUserSettings.cs:1555` | 输入 | ✅ | ✅ |

**码率换算链（工具算「用户设的 0.17 到底是多少 Mbps」时必须走这条）**：

```
DesktopBitrateLimit (0~1)
  → 阈值量化: Math.Round(limit, 3, MidpointRounding.AwayFromZero)     [GetDesktopBitrate, hmdres.cs:681-687]
  → Lerp(GetMinDesktopBitrate(HmdType), GetMaxDesktopBitrate(HmdType), 阈值) → int bps
```
- `GetMinDesktopBitrate`：移动端设备 2 Mbps，其余 4 Mbps（`hmdres.cs:627-668`）
- `GetMaxDesktopBitrate`：**Quest 1 (259) = 40 Mbps**，其余 **120 Mbps**（`hmdres.cs:670-679`）
- 实际下发的 `DesktopBitrate` 还要再乘 `MeasuredBandwidth`（PC 侧测得的带宽）
  与 `AutoAdjustBitrate`（`SharedMobileSettings.cs:1833-1835`，`SharedStreamerSettings.cs:791`）

```
VRBitrateLimit (0~1)
  → Lerp(GetMinVRBitrate=10Mbps, GetMaxVRBitrate(HmdType, ActiveCodec), 阈值) → int bps
```
- `GetMaxVRBitrate` 见 `hmdres.cs:701-807` 与 `03-il-constants.md`（这里有 4 处 `200000000` 硬上限）

---

## 4. 主表 D — `SharedMobileSettings`（L3，**头显是 server**，描述头显自己的硬件）

文件：`.../VirtualDesktop/Mobile/SharedMobileSettings.cs`
存储：`SharedMobileSettings.json`（头显落盘）

| 参数名 | 中文含义 | 类型 | 默认值 | 存储位置 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|---|
| HmdType | 头显型号（决定所有码率/分辨率上限） | `HmdType` 枚举 | `Unknown(-1)` | JSON `HmdType` | `SharedMobileSettings.cs:698` / `SharedMobileSettings.cs:1981` | **全链路** | ✅ | ❌ |
| Resolution | PC 侧上报的桌面分辨率 | `Size2` | `2560x1440` | JSON `Resolution` | `SharedMobileSettings.cs:742` / `SharedMobileSettings.cs:1983` | 画质 | ✅ | ❌ |
| IsPortrait | 显示器竖屏标记 | `bool?` | `null` | JSON `IsPortrait` | `SharedMobileSettings.cs:175`（ILSpy） | 显示器布局 | ✅ | ✅ |
| IPD | 瞳距（米） | `float` | `0.0635` | JSON `IPD` | `SharedMobileSettings.cs:832` / `SharedMobileSettings.cs:1987` | 画质/眩晕 | ✅ | ❌ |
| FovLeft / FovRight | 左右眼 FOV 端口 | `FovPort` | 零值 | JSON `FovLeft`/`FovRight` | `SharedMobileSettings.cs:871` / `:910` / `:1989`/`:1991` | 画质 | ✅ | ❌ |
| LeftHanded | 左手模式 | `bool` | `false` | JSON `LeftHanded` | `SharedMobileSettings.cs:949` / `SharedMobileSettings.cs:1993` | 画质 | ✅ | ✅ |
| MeasuredBandwidth | 测得带宽（bps） | `int` | `0` | JSON `MeasuredBandwidth` | `SharedMobileSettings.cs:988` / `SharedMobileSettings.cs:1995` | **连通性** | ✅ | ❌ |
| IsVideoPaused | 视频已暂停 | `bool` | `false` | JSON `IsVideoPaused` | `SharedMobileSettings.cs:1075` / `SharedMobileSettings.cs:1997` | 稳定性 | ✅ | ✅ |
| UseSpacewarp | 启用 Spacewarp | `bool` | `false` | JSON `UseSpacewarp` | `SharedMobileSettings.cs:1114` / `SharedMobileSettings.cs:1999` | **延迟** | ✅ | ✅ |
| DesktopFramerate | 生效的桌面帧率（PC 侧确认后的值） | `int` | `60` | JSON `DesktopFramerate` | `SharedMobileSettings.cs:1154` / `SharedMobileSettings.cs:2001` | **延迟** | ✅ | ✅ |
| VideosFolderPath | 视频库目录 | `string` | `""` | JSON `VideosFolderPath` | `SharedMobileSettings.cs:1194` / `SharedMobileSettings.cs:2005` | 存储 | ✅ | ✅ |
| Features | 能力位图 | `Features` [Flags] | `0` | JSON `Features` | `SharedMobileSettings.cs:1455` / `SharedMobileSettings.cs:2007` | 兼容性 | ✅ | ❌ |
| VideoPlaybackSpeed | 视频播放倍速 | `float` | `1.0` | JSON `VideoPlaybackSpeed` | `SharedMobileSettings.cs:1506` / `SharedMobileSettings.cs:2009` | 播放 | ✅ | ✅ |
| UseAnnexB | 码流用 Annex-B（而非 AVCC）格式 | `bool` | `true` | JSON `UseAnnexB` | `SharedMobileSettings.cs:1565` / `SharedMobileSettings.cs:2011` | **解码稳定性** | ✅ | ✅ |
| FoveatedStreaming | 眼动编码生效（头显侧回执） | `bool` | `false` | JSON `FoveatedStreaming` | `SharedMobileSettings.cs:1604` / `SharedMobileSettings.cs:2013` | 画质 | ✅ | ✅ |
| RefreshRate | 头显当前刷新率（Hz） | `int` | `0` | JSON `RefreshRate` | `SharedMobileSettings.cs:1654` / `SharedMobileSettings.cs:2015` | **延迟** | ✅ | ❌ |
| DesktopBitrate | 实际生效桌面码率 | `int` | `0` | **`[JsonIgnore]` 不落盘** `SharedMobileSettings.cs:1250` / `:2003` | 码率 | ✅(运行时) | ❌ |
| VRBitrate | 实际生效 VR 码率 | `int` | `0` | `[JsonIgnore]` `SharedMobileSettings.cs:1297` | 码率 | ✅(运行时) | ❌ |
| H264PlusVRBitrate | H.264+ 下的 VR 码率 | `int` | `0` | `[JsonIgnore]` `SharedMobileSettings.cs:1331` | 码率 | ✅(运行时) | ❌ |
| AV1VRBitrate | AV1 下的 VR 码率 | `int` | `0` | `[JsonIgnore]` `SharedMobileSettings.cs:1364` | 码率 | ✅(运行时) | ❌ |
| VRFramerate | 实际生效 VR 帧率（HmdType 相关，默认 72/90） | `int`（计算值） | 计算 | `[JsonIgnore]` `SharedMobileSettings.cs:1397` | **延迟** | ✅(运行时) | ❌ |

> **关键推论**：`UseAnnexB` 默认 `true`（`SharedMobileSettings.cs:2011` 字段初始化器 `_useAnnexB = true`）。
> 这是**解码稳定性开关**——头显侧解码器期望 Annex-B；PC 侧若发 AVCC 会黑屏/花屏。
> 属于「工具应检测但不要盲改」的项。

---

## 5. 主表 E — `SharedStreamerSettings`（L3，**PC 下发给头显**，头显只读）

文件：`.../VirtualDesktop/Mobile/SharedStreamerSettings.cs`
存储：**头显不落盘**（`InitializeAsClient`），全部来自 PC

| 参数名 | 中文含义 | 类型 | 默认值 | 存储位置 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|---|
| HasVR | 当前是否在 VR 流 | `bool` | `false` | 网络消息（不落盘） | `SharedStreamerSettings.cs:672` / `:1388` | 画质路径 | ✅(运行时) | ❌ |
| StreamingSource | 流来源 | `StreamingSource` (Desktop=0/VR=1/Video=2/LocalVideo=3) | `Desktop`(=0) | 网络消息 | `SharedStreamerSettings.cs:711` / `:1390` | 流模式 | ✅ | ⚠️ 只能经协议 |
| ActiveCodec | 生效编码器 | `VideoCodec` (Automatic=0/H264=1/HEVC=2/VP8=3/VP9=4/H264Plus=5/HEVC10bit=6/AV1=10/AV110bit=11) | `Automatic`(=0) | 网络消息 | `SharedStreamerSettings.cs:752` / `:1392` | **画质/兼容性** | ✅ | ⚠️ 只能经协议 |
| AutoAdjustBitrate | 自动调码率 | `bool` | `false` | 网络消息 | `SharedStreamerSettings.cs:791` / `:1394` | **画质/稳定性** | ✅ | ⚠️ |
| CanSwitchMonitor | 允许切换显示器 | `bool` | `false` | 网络消息 | `SharedStreamerSettings.cs:837` / `:1396` | 显示器布局 | ✅ | ❌ |
| CanLaunchSteamVR | 允许启动 SteamVR | `bool` | `true` | 网络消息 | `SharedStreamerSettings.cs:877` / `:1398` | 兼容性 | ✅ | ❌ |
| ActiveRuntime | PC 运行时 | `Runtime` (枚举) | `0` | 网络消息 | `SharedStreamerSettings.cs:916` / `:1400` | 兼容性 | ✅ | ❌ |
| AllowMotionExtrapolation | 允许运动外插 | `bool` | `true` | 网络消息 | `SharedStreamerSettings.cs:955` / `:1402` | **延迟** | ✅ | ⚠️ |
| AdapterName | PC 显卡名 | `string` | `null` | 网络消息 | `SharedStreamerSettings.cs:994` / `:1404` | 诊断 | ✅ | ❌ |
| AdapterIsAMD | PC 显卡是 AMD | `bool` | `false` | 网络消息 | `SharedStreamerSettings.cs:1068` / `:1406` | 兼容性 | ✅ | ❌ |
| DownloadState | 资产下载状态 | `DownloadState` | `0` | 网络消息 | `SharedStreamerSettings.cs:1106` / `:1408` | 稳定性 | ✅ | ❌ |
| DownloadProgress | 下载进度 | `float` | `0` | 网络消息 | `SharedStreamerSettings.cs:1145` / `:1410` | 稳定性 | ✅ | ❌ |
| InputLanguage | 键盘布局语言（如 `"US"`） | `string` | `null` | 网络消息 | `SharedStreamerSettings.cs:1184` / `:1412` | 输入 | ✅ | ⚠️ |
| CanAddMonitor | 允许加显示器 | `bool` | `false` | 网络消息 | `SharedStreamerSettings.cs:1258` / `:1414` | 显示器布局 | ✅ | ❌ |
| CanRemoveMonitor | 允许删显示器 | `bool` | `false` | 网络消息 | `SharedStreamerSettings.cs:1297` / `:1416` | 显示器布局 | ✅ | ❌ |

`ActiveCodec` 与码率上限强耦合：`VideoCodec.cs` 里 `H264Plus = 5`，而 `GetMaxVRBitrate` 对 `codec==5`
走更高的分支（`hmdres.cs:796-806`，非 5 是 150/200 Mbps，5 是 400/600 Mbps）。
**所以「选 H.264+」本身就是抬码率上限的动作**，这是工具做诊断时容易漏的一条。

---

## 6. 主表 F — `DynamicSettings`（L2，**不落盘**，纯运行时状态）

文件：`.../VirtualDesktop.Mobile/DynamicSettings.cs`
存储：**无 JSON 文件**（基类 `SettingsBase<T>` 无序列化，`DynamicSettings.cs:15`）。
工具只能通过 **adb shell 读进程内状态**（无文件）或 **IL 探针** 拿值。

| 参数名 | 中文含义 | 类型 | 默认值 | 来源 file:line | 影响 | 工具可读 | 工具可写 |
|---|---|---|---|---|---|---|---|
| Platform | 平台枚举（1=Quest/2=Vive/3=通用Android/5=Pico/6=PFD/8=Steam） | `Platform` | `0` | `DynamicSettings.cs:877`（`VrApp.cs:97/110/119/128/137` 赋值） | 全链路 | ⚠️ 仅运行时 | ❌ |
| OSVersion | 头显 OS 版本 | `Version` | `1.0` | `DynamicSettings.cs:882`（`VrApp.cs:143/148`） | 兼容性 | ⚠️ | ❌ |
| VersionString | 版本串 | `string` | — | `DynamicSettings.cs:887` | 诊断 | ⚠️ | ❌ |
| AccountID | 平台账号 ID | `string` | `null` | `DynamicSettings.cs:892` | 鉴权 | ⚠️ | ❌ |
| EyeTrackingPermission | 眼动权限名（按平台变） | `string` | 见 `VrApp.cs:154/160/164` | `DynamicSettings.cs:897` | **权限** | ⚠️ | ❌ |
| FaceTrackingPermission | 面追权限名 | `string` | 见 `VrApp.cs:155/161/165` | `DynamicSettings.cs:902` | **权限** | ⚠️ | ❌ |
| IsInactive | 进程是否空闲 | `bool` | `true` | `DynamicSettings.cs:912` | 省电 | ⚠️ | ❌ |
| MeasuringBandwidth | 正在测带宽 | `bool` | `false` | `DynamicSettings.cs:481` | **连通性诊断** | ⚠️ | ❌ |
| AutomaticBitrate | 自动调码率结果 | `bool` | `false` | `DynamicSettings.cs:957`（`NetworkManager.cs:245` 赋值） | 画质 | ⚠️ | ❌ |
| ActiveQuality | VR 当前生效画质 | `VRGraphicsQuality` | `0` | `DynamicSettings.cs:917`（`StreamingTab.cs:678` 赋值） | 画质 | ⚠️ | ❌ |
| ActiveForwardTrakingData | 追踪数据转发生效态 | `bool` | `false` | `DynamicSettings.cs:922` | 输入 | ⚠️ | ❌ |
| ActiveEmulateTrackers | 模拟追踪器生效态 | `bool` | `false` | `DynamicSettings.cs:927` | 输入 | ⚠️ | ❌ |
| ActiveEmulateIndexControllers | 模拟 Index 手柄生效态 | `bool` | `false` | `DynamicSettings.cs:932` | 输入 | ⚠️ | ❌ |
| CanUse120Hz | 允许 120Hz | `bool` | `false` | `DynamicSettings.cs:818` | **延迟** | ⚠️ | ❌ |
| CanUseVRPassthrough | 允许 VR 透视 | `bool` | `false` | `DynamicSettings.cs:977` | 画质 | ⚠️ | ❌ |
| PerformanceOverlayHidden | 性能浮层已隐藏 | `bool` | `false` | `DynamicSettings.cs:972` | 诊断 | ⚠️ | ❌ |
| SkipScreenRendering | 跳过屏幕渲染（省电） | `bool` | `false` | `DynamicSettings.cs:952` | 省电 | ⚠️ | ❌ |
| SkipScreenRendering 之外的省电：`IsInactive` | 同上 | `bool` | `true` | `DynamicSettings.cs:912` | 省电 | ⚠️ | ❌ |
| SpacewarpStartFrameIndex | Spacewarp 起始帧 | `int` | `0` | `DynamicSettings.cs:962` | 延迟 | ⚠️ | ❌ |
| UseAlternateEyeSpacewarp | 备用眼动 Spacewarp | `bool` | `false` | `DynamicSettings.cs:967` | 延迟 | ⚠️ | ❌ |
| VideoWaitForSyncFrame | 等同步帧 | `bool` | `false` | `DynamicSettings.cs:947` | 延迟 | ⚠️ | ❌ |
| DesktopFormat / VRFormat / VideoFormat / StreamingFormat | 各模式当前视频格式 | `VideoFormat` | `Default` | `DynamicSettings.cs:322/354/378/286` | 画质 | ⚠️ | ❌ |
| SelectedStreamingSource | UI 选中的流源 | `StreamingSource` | — | `DynamicSettings.cs:444` | 流模式 | ⚠️ | ❌ |
| ComputerRegistryWarning | 云注册告警文案 | `string` | `null` | `DynamicSettings.cs:529` | **连通性诊断** | ⚠️ | ❌ |

---

## 7. 主表 G — Android 系统侧参数与权限（影响「能不能跑起来」）

### 7.1 7 个必须预授的 runtime 权限（不授 = 30 秒后失焦）

来源：`F:/Project/VirtualDesktop/analysis/apk_patch/install.bat:25-31`、`HANDOFF.md:23-32`

| # | 权限 | 对应平台后缀 | 缺失症状 | 证据 |
|---|---|---|---|---|
| 1 | `USE_SCENE` | `com.oculus.permission.` | 无场景权限 → 拿不到 IMMERSIVE 焦点 → **30s 后焦点被 HorizonOS 收回** | `HANDOFF.md:32`、`HANDOFF.md:171` |
| 2 | `USE_SCENE` | `horizonos.permission.` | 同上（新 HorizonOS 命名空间） | `install.bat:26` |
| 3 | `FACE_TRACKING` | `com.oculus.permission.` | 面追不可用；`HMD.Supports(64)` 分支关闭 | `StreamingTab.cs:310` |
| 4 | `FACE_TRACKING` | `horizonos.permission.` | 同上 | `install.bat:28` |
| 5 | `EYE_TRACKING` | `com.oculus.permission.` | foveated streaming 被强制关闭（`VrApp.cs:267-275` 拒绝即置 false） | `VrApp.cs:269-274` |
| 6 | `EYE_TRACKING` | `horizonos.permission.` | 同上 | `install.bat:30` |
| 7 | `POST_NOTIFICATIONS` | `android.permission.` | 无通知；Android 13+ 上部分 HUD 提示不可见 | `install.bat:31` |

补充 3 个（`install_template.bat:61-74` 用同一个循环授 **10** 个）：
`android.permission.RECORD_AUDIO`（**MicPassthrough 直接依赖**，见 `VrApp.cs:277-286`；缺失时首启弹窗阻塞 VR 窗口放置，见 `HANDOFF.md:54-55`）、
`android.permission.READ_EXTERNAL_STORAGE`、`android.permission.READ_MEDIA_IMAGES`（视频库截图/封面）。
外部全文件访问走 `Environment.IsExternalStorageManager`（`ExternalStorageManager.cs:27`，API 34+）。

**工具检测项形态**：
```bash
adb shell dumpsys package VirtualDesktop.Android | grep -A40 "runtime permissions"
adb shell cmd appops get VirtualDesktop.Android MANAGE_EXTERNAL_STORAGE
```
以上形态 `[未验证]`（本机无 adb）。

### 7.2 代码读取的 Android 系统属性

`VrApp.cs:98-145` 通过 `AndroidSystemProperties.Get(...)` 读：
| 属性 key | 平台 | 用途 | file:line |
|---|---|---|---|
| `ro.vros.build.version` | Quest | OS 版本，回退到 `ro.build.branch` | `VrApp.cs:98` |
| `ro.build.branch` | Quest | 同上（取最后一个 `v` 之后） | `VrApp.cs:101` |
| `ro.product.version` | Vive/Focus | OS 版本 | `VrApp.cs:111` |
| `ro.build.display.id` | PFD/Steam/其他 | OS 版本 | `VrApp.cs:120/129/138` |
| `Build.Model` | 全部 | 判平台（`StartsWith("Quest")` / `"Vive"` / `"Focus"` / `"PFD"` / `"Steam"` / `"SM-"`） | `VrApp.cs:93-139` |

工具形态：
```bash
adb shell getprop ro.vros.build.version
adb shell getprop ro.product.model
```
`[未验证]`

### 7.3 Wi-Fi 锁（省电策略的直接开关）

`VrApp.cs:80`：`activity.GetSystemService("wifi").CreateWifiLock(4, "VRD")`
`4` = `WifiLockMode.WIFI_MODE_FULL_HIGH_PERF`。
`VrApp.cs:179`（`OnResume`）acquire，`VrApp.cs:172`（`OnPause`）release。
**不授 Wi-Fi 相关权限 / 被系统省电打断时，串流会被降速**。这是「网络模式」这一类参数的真正实现，
**代码里没有「强制 LAN」开关**——LAN/远程由 PC 侧的 `Computer.IsOnSameNetwork` 决定
（`Interfaces/Computer.cs:276`），头显只在 `ComputersTab.cs:169` 显示告警。

### 7.4 头显侧 Wi-Fi 判慢阈值（UI 上"网络慢"的判定）

`WifiMetrics.cs:37-41` `IsSlow()`：`Frequency < 4000 || LinkSpeed < 450`
→ 即 **非 5GHz 就算慢** 或 **链路速率 < 450 Mbps**。
`WifiMetrics.cs:21-35` `FrequencyInGHz`：≥5925→6G，>4000→5G，否则 2.4G。
工具可用 `adb shell dumpsys wifi` 取 frequency / linkSpeed 复现同一判定。

---

## 8. 工具可读/可写的整体结论

- **可读且可写（headset 本地 JSON，10 个键）**：`UserSettings.json`（61 键）、`ScreenSettings.json` +
  `Secondary/TertiaryScreenSettings.json`（各 8 键）、`VideoSettings.json`、`SharedUserSettings.json`（15 键）、
  `SharedMobileSettings.json`（18 键）。
- **可读不可写**：`[JsonIgnore]` 键（`ControllerGamepad`、`HeadLock`、`DesktopBitrate`、`VRBitrate`、
  `H264PlusVRBitrate`、`AV1VRBitrate`、`VRFramerate`）、`HasValidIdentity`（被 IL 补丁强制 true）。
- **只在协议里，无文件**：`SharedStreamerSettings` 全 15 键 + `DynamicSettings` 全键。
- **在 IL 里，无 key**：`GetMaxVRBitrate` 的 4 处 `200000000`（见 `03-il-constants.md`）。

**写后生效条件**：所有 L1/L3 落盘键由 `SerializedSettingsBase.StartSaveTimer()` 起 **2000ms 单次定时器**落盘
（`SerializedSettingsBase.cs:47`），且应用启动时 `JsonSettingsBase.Initialize()` 会 `Reload()` 覆盖内存
（`JsonSettingsBase.cs:66-69`）。所以**外部改 JSON 必须先停应用再改，否则被内存值覆盖**。