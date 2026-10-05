# 02 — 持久化落点全图与读取/写入方案

> 本文回答两个问题：**每个参数在头显上落在哪、写的是什么**；**工具怎么用 adb 把它读出来 / 改回去**。
> 所有路径推导都给出了源码行号；adb 命令形态标注 `[未验证]`（本机 `adb` 不在 PATH，
> `D:/Software/Android/Sdk/platform-tools/adb.exe` 实测不存在，见 §6 验证条件）。

---

## 1. 落点总图

### 1.1 存储介质：**没有 SharedPreferences，没有 SQLite，没有 MMKV**

这是本项目最容易搞错的一点，grep 已确认：

```
grep -rn "SharedPreferences|SQLite|MMKV|DataStore" analysis/apk_patch/decompiled/  → 无业务命中
```
（`decompiled/assembly_177/.../LocalDataStoreSlot.cs` 的命中是 .NET 内部 `Thread.LocalDataSlot`，
与 Android 持久化无关。）

VD Android 端**全部设置都是 Newtonsoft.Json 序列化的纯文本 JSON 文件**，
由 `VirtualDesktop.Core` 的三层基类统一管理：

```
SettingsBase<T>                     decompiled/vdcore/VirtualDesktop.Core/SettingsBase.cs:10
  └─ JsonSettingsBase<T>            JsonSettingsBase.cs:11
       ├─ Reload()   :23-46         读文件 → JsonConvert.PopulateObject
       ├─ GetFileName() :49-57      Path.Combine(AssemblyHelper.UserAppDataPath, typeof(T).Name + ".json")
       └─ Initialize()  :66-69      ← 静态构造时调用 Reload()
     └─ SerializedSettingsBase<T>   SerializedSettingsBase.cs:13
          ├─ Save()          :16-40 File.WriteAllText(GetFileName(), Serialize(this))
          ├─ StartSaveTimer():43-49 this._saveTimer.Change(2000, -1)   ← 2 秒防抖
          └─ OnPropertyChanged():59-63 每次属性变更都 StartSaveTimer()
```

### 1.2 根目录推导（三跳，每跳有源码）

**第 1 跳：`AssemblyHelper.UserAppDataPath`** — `decompiled/vdcore/VirtualDesktop.Core/AssemblyHelper.cs:127-148`

```csharp
public static string UserAppDataPath
{
    get {
        if (_userAppDataPath == null) {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                _userAppDataPath = Path.Combine("/Users/Shared/", ProductName);
            else
                _userAppDataPath = Path.Combine(Environment.GetFolderPath(26), ProductName);
            if (!Directory.Exists(_userAppDataPath))
                Directory.CreateDirectory(_userAppDataPath);
        }
        return _userAppDataPath;
    }
}
```

- `ProductName`：Quest 上来自 `VirtualDesktop.Android.dll` 的 `[assembly: AssemblyProduct("Virtual Desktop")]`
  （`decompiled/vdandroid/VirtualDesktop.Android/Properties/AssemblyInfo.cs:10`）。
  即目录名**带空格**：`Virtual Desktop`。

**第 2 跳：`Environment.GetFolderPath(26)`** — 26 是 `SpecialFolder.ApplicationData`
（`decompiled/assembly_177/System.Private.CoreLib/System/Environment.cs:680` `ApplicationData = 26`）。
该枚举值在 `Environment.cs:291-295` 的映射是：

```csharp
default:
    if (A_0 != Environment.SpecialFolder.ApplicationData) goto IL_00FF;
    return Path.Combine(text, ".config");      // ← Environment.cs:295
```

**第 3 跳：`text` = HOME** — `Environment.cs:249` `text = PersistedFiles.GetHomeDirectory();`，
失败回退 `"/"`（`Environment.cs:254-257`）。
`PersistedFiles.cs:10-35`：先读 `$HOME` 环境变量，空则 `getpwuid_r()`。

**结论**（Quest/Android 上的实际绝对路径 `[未验证]`，见 §6）：

| 形态 | 路径 |
|---|---|
| 若 `$HOME=/data/user/0/VirtualDesktop.Android/files` | `/data/user/0/VirtualDesktop.Android/files/.config/Virtual Desktop/` |
| 若 `$HOME=/storage/emulated/0/Android/data/VirtualDesktop.Android/files` | `.../files/.config/Virtual Desktop/` |
| 兜底（HOME 空且 getpwuid 失败） | `/.config/Virtual Desktop/`（几乎必然创建失败 → 所有设置回落默认值） |

**工具不要猜路径，直接 find**：
```bash
adb shell "run-as VirtualDesktop.Android find /data -name UserSettings.json 2>/dev/null"
adb shell "run-as VirtualDesktop.Android find /storage -name UserSettings.json 2>/dev/null"
```
`[未验证]`

### 1.3 文件清单（7 个 JSON，其中 1 个是死文件）

| 文件 | 类型 | 谁写 | 谁读 | 备注 |
|---|---|---|---|---|
| `UserSettings.json` | `UserSettings` | Quest 本地（2s 防抖） | Quest 启动时 | **61 个键** |
| `ScreenSettings.json` | `ScreenSettings`（主屏） | 同上 | 同上 | `GetFileName()` 无 override → 默认名 |
| `SecondaryScreenSettings.json` | `ScreenSettings` 实例 | 同上 | 同上 | 文件名在静态构造注入，`ScreenSettings.cs:26` |
| `TertiaryScreenSettings.json` | `ScreenSettings` 实例 | 同上 | 同上 | `ScreenSettings.cs:29` |
| `VideoSettings.json` | `VideoSettings` | 同上 | 同上 | 视频库排序 + 每文件设置字典 |
| `SharedUserSettings.json` | `SharedUserSettings` | 头显是 server → 落盘 | 头显 | 15 键 |
| `SharedMobileSettings.json` | `SharedMobileSettings` | 头显是 server → 落盘 | 头显 | 18 键 |
| ~~`SharedStreamerSettings.json`~~ | — | **不落盘** | — | `InitializeAsClient`，`StartSaveTimer` 非 server 直接 return（`NetworkSettingsBase.cs:276-290`）。**若文件存在是残留，工具不要读** |

另有 `SecondaryScreenSettings` / `TertiaryScreenSettings` 通过 `ScreenSettings.GetFileName()` override
返回 `_fileName ?? base.GetFileName()`（`ScreenSettings.cs:245-248`）。

### 1.4 序列化语义（决定工具怎么写 JSON）

`decompiled/vdcore/VirtualDesktop.Core/Json.cs:11-15`：

```csharp
public static JsonSerializerSettings SerializerSettings { get; } = new JsonSerializerSettings
{
    DefaultValueHandling = 3,     // ← IgnoreAndPopulate
    Formatting = 1                // Indented
};
```

`DefaultValueHandling = 3` = `IgnoreAndPopulate`：
- **序列化时**：值等于 `[DefaultValue]` 的字段**不写出**。
- **反序列化时**：JSON 里缺失的键**自动填成 `[DefaultValue]`**。

**对工具的三条硬约束**：
1. **不能靠"键缺失"判断"用户没设过"** —— 缺失 = 默认值，两种情况无法区分。
2. **改一个键不需要写全文件**，只写目标键即可，其余走默认值。
3. **删一个键 = 把它重置成默认值**（这是最干净的"恢复默认"实现方式）。

`VideoSettings` 有额外的 `MemberSerializer`（`DefaultValueHandling = 0` = Include），
但那只用于内部成员序列化（`Json.cs:31-35`），落盘仍走 `SerializerSettings`。

### 1.5 键 = 属性名（无重命名）

全类没有 `[JsonProperty("...")]` 或 `[JsonContractResolver]` 重命名，
`JsonSettingsBase.GetSerializerSettings()` 直接返回 `Json.SerializerSettings`（`JsonSettingsBase.cs:60-63`）。
**所以 JSON 的键名 = C# 属性名**，本目录 01 的"参数名"列可直接当 key 用。

`[JsonIgnore]` 的键（可读但不会出现在文件里，也写不进去）：
`ControllerGamepad`（`UserSettings.cs:792`）、`HeadLock`（`ScreenSettings.cs:108`）、
`SortDirection` / `FileIDComparer`（`VideoSettings.cs:57/73`）、
`DesktopBitrate`（`SharedMobileSettings.cs:1250`）、`VRBitrate`（`:1297`）、
`H264PlusVRBitrate`（`:1331`）、`AV1VRBitrate`（`:1364`）、`VRFramerate`（`:1397`）、
`IsInitialized` / `IsServer`（`NetworkSettingsBase.cs:15/62`）。

---

## 2. key 级台账：谁读谁写（类:行号）

### 2.1 `UserSettings.json` —— 61 键

**写方（Setter 调用点，全部在 UI 层）**：

| 键 | 写入点 | 触发控件 |
|---|---|---|
| `AutoConnect` | `SettingsTab.cs:481` | `chkAutoConnect` |
| `ShowGamesTab` | `SettingsTab.cs:488` | `chkShowGamesTab` |
| `ArrangeMonitorsOnRecenter` | `SettingsTab.cs:502` | `chkArrangeOnRecenter` |
| `BackgroundMusic` | `SettingsTab.cs:538` | `chkBackgroundMusic` |
| `NoiseCancellation` | `SettingsTab.cs:558` | `chkNoiseCancellation` |
| `AllowCustomOrientation` | `SettingsTab.cs:582` / `821` | `chkAllowCustomOrientation` |
| `BoostClockRates` | `SettingsTab.cs:588` / `828` | `chkBoostClockRates` |
| `CopyScreenshots` | `SettingsTab.cs:594` | `chkCopyScreenshots` |
| `LocalDimming` | `SettingsTab.cs:600` / `845` | `chkLocalDimming` |
| `IncreaseColorVibrance` | `SettingsTab.cs:606` / `853` | `chkColorVibrance` |
| `HeadLockNoDelay` | `SettingsTab.cs:612` / `860` | `chkHeadLockNoDelay` |
| `ScreenBrightness` | `SettingsTab.cs:727` | `sldBrightness`（**平方存储**：`value*value`） |
| `EnvironmentQuality` | `SettingsTab.cs:628/633/638` | 三档 CheckBox |
| `DesktopFramerate` | `SettingsTab.cs:683-713` | 7 个 CheckBox（60/72/80/90/96/100/120） |
| `ScreenTransparencyColor` | `SettingsTab.cs:892/901/910/919` | RGB 三个滑条 |
| `ScreenTransparencySimilarity` | `SettingsTab.cs:925` | `sldSimilarity` |
| `ScreenTransparencySmoothness` | `SettingsTab.cs:944` | `sldSmoothness` |
| `DynamicLightingBehavior` | `SettingsTab.cs:789/794/799` | 三档 CheckBox |
| `Sharpening` | `StreamingTab.cs:782/789` | `sldSharpening` |
| `Gamma` | `StreamingTab.cs:921` | `sldGamma` |
| `VRFramerate` → 落 SharedUserSettings | `StreamingTab.cs:640-664` | 7 个 CheckBox |
| `VRGraphicsQuality` | `StreamingTab.cs:712-740` | 7 档 CheckBox |
| `VRBitrateLimit` → 落 SharedUserSettings | `StreamingTab.cs:769` | `sldBitrate` |
| `DesktopBitrateLimit` → 落 SharedUserSettings | `SettingsTab.cs:734` | `sldBitrate` |
| `VRPassthrough` | `StreamingTab.cs:854` | `chkPassthrough` |
| `VRTrackedKeyboard` | `StreamingTab.cs:860` | `chkTrackedKeyboard` |
| `VRPassthroughHands` | `StreamingTab.cs:866` | `chkVRHands` |
| `VRDeskPortal` | `StreamingTab.cs:872` | `chkDeskPortal` |
| `VRShowBoundary` | `StreamingTab.cs:1314` | `chkShowBoundary` |
| `SuperResolution` | `StreamingTab.cs:1012` | `chkSuperResolution` |
| `VideoBuffering` | `StreamingTab.cs:950` | `chkVideoBuffering` |
| `StageTracking` | `StreamingTab.cs:1030` | `chkStageTracking` |
| `TrackControllers` | `StreamingTab.cs:962` | `chkTrackControllers` |
| `ShowPerformanceOverlay` | `StreamingTab.cs:968/1128` | `chkPerformanceOverlay` |
| `LocalDimmingVR` | `StreamingTab.cs:1102` | `chkLocalDimming` |
| `IncreaseVRColorVibrance` | `StreamingTab.cs:1115` | `chkColorVibrance` |
| `SpacewarpBehavior` | `StreamingTab.cs:898/903/908` | 三档 CheckBox |
| `VRPassthroughColor` | `StreamingTab.cs:1204/1213/1222/1231` | RGB 三滑条 |
| `VRPassthroughSimilarity` | `StreamingTab.cs:1237` | `sldSimilarity` |
| `VRPassthroughSmoothness` | `StreamingTab.cs:1256` | `sldSmoothness` |
| `VRPassthroughOpacity` | `StreamingTab.cs:1262` | `sldOpacity` |
| `ControllerDesktop` | `InputTab.cs:297` | `chkControllerDesktop` |
| `HandDesktop` | `InputTab.cs:304` | `chkHandDesktop` |
| `AutoHideController` | `InputTab.cs:323` | `chkAutoHideController` |
| `AutoHideHands` | `InputTab.cs:336` | `chkAutoHideHands` |
| `PointerStabilization` | `InputTab.cs:349` | `chkPointerStabilization` |
| `TrackpadVerticalScrolling` | `InputTab.cs:362` | `chkTrackpadVScrolling` |
| `TrackpadHorizontalScrolling` | `InputTab.cs:375` | `chkTrackpadHScrolling` |
| `HandTracking` | `InputTab.cs:382` | `chkHandTracking`（**同时写** `SharedUserSettings.UseMultiModal`，`InputTab.cs:385`） |
| `GripScreen` | `InputTab.cs:405` | `chkGripScreen` |
| `HoldMenu` | `InputTab.cs:418` | `chkHoldMenu` |
| `ControllerGamepad` | `InputTab.cs:451` | `chkControllerGamepad`（**`[JsonIgnore]` 不落盘**） |
| `DpadStartEmulationMode` | `InputTab.cs:487/492/497/503` | 4 档 CheckBox |
| `DesktopTrackedKeyboard` | `InputTab.cs`（`chkTrackedKeyboard`，写入点在 `OnTrackedKeyboardClick`） | `chkTrackedKeyboard` |
| `PassthroughHands` | `InputTab.cs:531` | 桌面透视手 |
| `PreferEyeGazeInput` | `InputTab.cs`（`chkEyeGaze`） | `chkEyeGaze` |
| `LastComputerID` | `NetworkManager.cs`（连接成功时写入）+ `ComputersTab.cs:269/303`（断开时清空） | 自动 |
| `InstalledLanguage` | `UserSettings.cs:2398`（`Reload()` 里按系统 locale 反写） | 自动 |
| `FoveatedStreaming` | `StreamingTab.cs:1005` | `chkFoveated` |
| `Environment` | `EnvironmentsTab.cs:167` | 环境按钮 |
| `FoveatedStreaming` → 同步 `SharedMobileSettings` | `UserSettings.cs:2389`（`Reload()` 里 `SharedMobileSettings.FoveatedStreaming = this._foveatedStreaming`） | 自动 |
| `EnvironmentQuality` 等"重置"批量写 | `StreamingTab.cs:1134-1154` | "重置 VR 设置"按钮 |

**读方（主要消费点）**：

| 键 | 消费点 | 作用 |
|---|---|---|
| `DesktopFramerate` | `SettingsTab.cs:666`（读 `SharedMobileSettings.DesktopFramerate` 回显）、`BottomPanel.cs:60-61`（算帧率与码率显示） | UI + HUD |
| `ScreenBrightness` | `VideoPlayer.cs:805/831`（乘进 shader Color 参数） | 画质 |
| `Sharpening` | `StreamingTab.cs:191` | 锐化 shader |
| `StageTracking` | `Game.cs:1034`（`hmd.StageTracking = ...`）、`Game.cs:1270-1271`（FloorHeight） | 输入 |
| `PointerStabilization` | `Game.cs:1560` 订阅事件 | 延迟 |
| `AutoHideController/AutoHideHands` | `Game.cs:1561-1562` | 渲染 |
| `HeadLock`（运行时） | `Screen.cs` / `Game.cs` / `ScreenManipulator.cs` | 显示器布局 |
| `LocalDimming` / `LocalDimmingVR` | `Game.cs:1035`、`SettingsTab.cs:846`（`base.Game.Hmd.LocalDimming = isChecked`） | 画质 |
| `SuperResolution` | `StreamingTab.cs:589`、渲染层 | 画质 |
| `BoostClockRates` | `Scene.cs:112`（`boostClockRates` 进性能设置） | 性能 |
| `FoveatedStreaming` | `Game.cs:1053/1090`（必须 `computer.StreamerVersion >= Game.EyeDataMinVersion` 才发眼动数据） | 画质 |
| `LastComputerID` + `AutoConnect` | `NetworkManager.cs:308`（`allowAutoConnect = !manuallyDisconnecting && !string.IsNullOrEmpty(LastComputerID)`） | **自动重连** |
| `HasValidIdentity` | `Game.cs:352`（与 `EmulateGamepad`/`ControllerDesktop` 组合判定桌面菜单可见） | 鉴权门控 |
| `DpadStartEmulationMode` | `InputTab.cs:543-546` | 输入 |
| `Environment` | `Scene.cs:115`（`EnvironmentExtensions.GetPerformanceSettings`）、`EnvironmentsTab.cs:164` | 画质/性能 |

### 2.2 `ScreenSettings.json` ×3 —— 8 键（+1 个非持久化）

| 键 | 写入点 | 读方 |
|---|---|---|
| `ScreenSize/Height/Distance/Curve` | `ScreenManipulator.cs`（拖拽手柄） | `Screen.cs:RecreateCylinder`、`Screen.ResetPose` |
| `ScreenTransparency` | `ScreenToolBar.cs:530`（`this._activeScreenSettings.ScreenTransparency = isChecked`） | `VideoPlayer.cs:357-359` 订阅三屏事件、`Scene.cs:283` |
| `Orientation` | `ScreenManipulator.cs` | `Screen.cs` 渲染矩阵 |
| `PositionOffset` | `ScreenManipulator.cs` | 同上 |
| `IsSnapped` | 摆位完成时 | `Game.cs:674/680`（Recenter 时判断是否重置姿态） |
| `HeadLock`（`[JsonIgnore]`） | `DynamicSettings.cs:217`（`ScreenAdjustment == ResetView` 时置 false） | `ScreenSettings.cs:241` `AnyHeadLock()` |

### 2.3 `SharedUserSettings.json` —— 15 键（跨设备）

**写方**：全部是头显 UI（见 §2.1 中标注"落 SharedUserSettings"的行）。
**读方**：头显渲染层 + PC 侧编码器。
**同步机制**：`NetworkSettingsBase.OnPropertyChanged<TValue>`（`NetworkSettingsBase.cs:185-235`）
→ 组 `MessageType.UserSettingsPropertyChanged`（=12）+ `propertyName` + value → 走 38810/tcp 发给 PC。

| 键 | 头显读方 | 语义 |
|---|---|---|
| `VRFramerate` | `SharedMobileSettings.VRFramerate` getter（`SharedMobileSettings.cs:1404`）换算成 int | 目标帧率 |
| `VRGraphicsQuality` | `StreamingTab.cs:600/748`、`Scene.cs:115`（转 `DynamicSettings.ActiveQuality`） | 画质档 |
| `DesktopBitrateLimit` | `SettingsTab.cs:164/734/740`、`SharedMobileSettings.cs:1834`（算 `DesktopBitrate`） | 归一化系数 |
| `VRBitrateLimit` | `StreamingTab.cs:181/769/775`、`SharedMobileSettings.cs:1861` | 归一化系数 |
| `Gamma` | `StreamingTab.cs:274/921` | 伽马 |
| `UseOptimalResolution` | `SettingsTab.cs:303/495/618` | 分辨率策略 |
| `EmulateGamepad` | `InputTab.cs:137/431/437`、`Game.cs:352` | 手柄模拟 |
| `MicPassthrough` | `SettingsTab.cs:309/437/551/564`、`VrApp.cs:277-286`（**同时触发 RECORD_AUDIO 权限申请**） | 麦克风直通 |
| `MicVolume` | `SettingsTab.cs:311/806/813` | 麦克风音量 |
| `IncreaseVideoNominalRange` | PC 侧解码器 | full/nominal range |
| `ForwardTrackingData` | `StreamingTab.cs:312/324/1058/1069`、`Game.cs:1559/1588` | 追踪转发 |
| `EmulateTrackers` | `StreamingTab.cs:313/325/1070/1077` | 模拟追踪器 |
| `EmulateIndexControllers` | `StreamingTab.cs:314/326/1088/1095` | 模拟 Index 手柄 |
| `ExtraDisplay` | PC 侧显示器枚举 | 显示器布局 |
| `UseMultiModal` | `InputTab.cs:385`、`StreamingTab.cs:1080` | 多模态 |

**「恢复默认」权威值**：`StreamingTab.cs:1134-1140` 一次性写回
`VRGraphicsQuality=1 / VRFramerate=0 / VRBitrateLimit=0.36 / Gamma=1.0 / ForwardTrackingData=false /
EmulateTrackers=false / EmulateIndexControllers=false`。

### 2.4 `SharedMobileSettings.json` —— 18 键（头显硬件事实）

| 键 | 写入方 | 读方 |
|---|---|---|
| `HmdType` | `GetHmdTypeAsync()`（`SharedMobileSettings.cs:1815`） | **所有码率/分辨率上限计算**：`SettingsTab.cs:33/761/989`、`StreamingTab.cs:26/989`、`InputTab.cs:202`、`hmdres.cs` 全部 |
| `Resolution` | PC 上报 | `SharedMobileSettings.GetOptimalDesktopResolution`（`:1728`） |
| `IsPortrait` | PC 上报（`MessageType.SetMonitorPortrait`） | 渲染朝向 |
| `IPD` | 头显硬件 | 渲染投影矩阵 |
| `FovLeft/FovRight` | 头显硬件 | 渲染 |
| `LeftHanded` | 头显设置 | 渲染 |
| `MeasuredBandwidth` | `GetMeasuredBandwidthAsync()`（`:1800`），由 `NetworkManager.cs:242-249 MeasureBandwidth()` 触发 | **滑条上限**：`SettingsTab.cs:758-764`、`StreamingTab.cs:986-992` |
| `IsVideoPaused` | `Game.cs:296` | 播放状态 |
| `UseSpacewarp` | 头显 | Spacewarp 层 |
| `DesktopFramerate` | 头显 `UserSettings.DesktopFramerate` 转换后回写 | `SettingsTab.cs:666`、`BottomPanel.cs:60` |
| `VideosFolderPath` | 视频库设置 | `VideosTab` |
| `Features` | PC 上报能力位图 | 特性开关 |
| `VideoPlaybackSpeed` | 播放 UI | 播放器 |
| `UseAnnexB` | PC 上报（默认 true，`SharedMobileSettings.cs:2011`） | **头显解码器** |
| `FoveatedStreaming` | `UserSettings.Reload()` 同步（`UserSettings.cs:2389`） | PC 编码器 |
| `RefreshRate` | 头显硬件（XR 运行时） | 帧率决策 |
| `DesktopBitrate/VRBitrate/H264PlusVRBitrate/AV1VRBitrate/VRFramerate` | **计算得出**，`[JsonIgnore]` 不落盘 | `BottomPanel.cs:61/65/68`、`PerformanceOverlay.cs:390-391` |

### 2.5 `SharedStreamerSettings` —— 15 键，**不落盘**

`SharedStreamerSettings.cs:1372-1386` 三个 override 全是空实现：
```csharp
public override void Reload() { }
public override void Save() { }
public override void StartSaveTimer() { }
```
**工具在头显上读不到这些值的持久化形态。** 只能：
1. 从 PC 端读 Streamer 的配置（另一条线，见 `research/04-streamer-settings/`）；或
2. 抓 38810/tcp 的 `MessageType.StreamerSettingsPropertyChanged`（=16）消息体。

### 2.6 `VideoSettings.json` / `VideoFileSettings`

`VideoSettings.cs:21-25`：`Items`（`ConcurrentDictionary<string, VideoFileSettings>`，key=文件路径，
OrdinalIgnoreCase）+ `SortDirections` + `SortByType`。
`VideoFileSettings.cs` 每个视频 9 键：
`Projection`(`:15`) / `LastPlayTimeUtc`(`:34`) / `AudioStreamIndex`(`:54`,默认 -1) /
`SubtitleTrackID`(`:73`) / `SubtitleStreamIndex`(`:93`,默认 -1) / `LastPresentationTime`(`:113`) /
`Fov`(`:133`,默认 180) / `Orientation`(`:152`) / `VideoTransparency`(`:171`) / `Zoom`(`:191`,默认 1)。
与串流体验/连通性无关，工具可只做只读展示。

---

## 3. adb 读取方案（形态推导，全部 `[未验证]`）

### 3.1 前置：定位设置目录

```bash
# 1) 先杀进程，保证读的是磁盘上的最终值（内存值优先，见 §4）
adb shell am force-stop VirtualDesktop.Android

# 2) 定位目录（不要硬编码路径）
adb shell "run-as VirtualDesktop.Android sh -c 'ls -la \$HOME/.config/Virtual\\ Desktop/'"
# 备选（HOME 未设时的兜底）
adb shell "run-as VirtualDesktop.Android sh -c 'ls -la /.config/Virtual\\ Desktop/'"
adb shell "ls -la /storage/emulated/0/Android/data/VirtualDesktop.Android/files/.config/Virtual Desktop/"

# 3) 一次性定位全部设置文件
adb shell "run-as VirtualDesktop.Android find / -name '*Settings.json' -path '*Virtual*' 2>/dev/null"
```

`run-as` 只在 **debuggable=true** 的 APK 上可用。Owner 的 patched APK 由
`build_v12_no_aot.py` 签名（`HANDOFF.md:88-91`），是否带 `android:debuggable` `[未验证]`。
若不可用，**必须 root**：

```bash
adb shell su -c "ls -la /data/user/0/VirtualDesktop.Android/files/.config/Virtual Desktop/"
```

### 3.2 读单个文件

```bash
PKG=VirtualDesktop.Android
DIR='.config/Virtual Desktop'

# 原样拉回（推荐工具用这个，避免 shell 转义地狱）
adb exec-out run-as $PKG cat "\$HOME/$DIR/UserSettings.json" > UserSettings.json

# 只看一个键（jq 在 Quest 上没有，用 PC 侧解析）
adb exec-out run-as $PKG cat "\$HOME/$DIR/UserSettings.json" | jq '.DesktopFramerate'
adb exec-out run-as $PKG cat "\$HOME/$DIR/UserSettings.json" | jq -c '{DesktopFramerate, AutoConnect, LastComputerID, BoostClockRates, VideoBuffering, Sharpening}'

# 三块屏幕设置
for f in ScreenSettings SecondaryScreenSettings TertiaryScreenSettings; do
  adb exec-out run-as $PKG cat "\$HOME/$DIR/$f.json"
done

# 跨设备设置
adb exec-out run-as $PKG cat "\$HOME/$DIR/SharedUserSettings.json"
adb exec-out run-as $PKG cat "\$HOME/$DIR/SharedMobileSettings.json"
```

### 3.3 写单个键

```bash
PKG=VirtualDesktop.Android
DIR='.config/Virtual Desktop'

# 流程：force-stop → pull 到 PC → jq 改 → push 回 → 冷启动
adb shell am force-stop $PKG
adb exec-out run-as $PKG cat "\$HOME/$DIR/UserSettings.json" > us.json
jq '.BoostClockRates = true | .VideoBuffering = false' us.json > us.new.json

# push 到 run-as 可写的临时位置，再原子替换
adb push us.new.json /data/local/tmp/us.json
adb shell "run-as $PKG cp /data/local/tmp/us.json \"\$HOME/$DIR/UserSettings.json\""
adb shell am start -n $PKG/com.xenocontroller...   # 或从头显主页手动启动
```

> **原子替换注意**：应用侧 `Save()` 是 `File.WriteAllText`（`SerializedSettingsBase.cs:25`），
> 不是写临时文件再 rename。所以工具 push 之前**必须 force-stop**，
> 否则应用退出/2 秒防抖触发时会把内存值写回去，静默吃掉工具的修改。

### 3.4 "恢复默认"的正确实现

因为 `DefaultValueHandling = IgnoreAndPopulate`（`Json.cs:13`），
**删除一个键 = 重置为默认值**：

```bash
jq 'del(.BoostClockRates) | del(.VideoBuffering)' us.json > us.new.json
```
比"写成 false"更准确——因为它们的 `[DefaultValue]` 不一定是 `false`
（`VideoBuffering` 默认 `true`，`BoostClockRates` 默认 `false`）。

### 3.5 读不到的东西：DynamicSettings 与 SharedStreamerSettings

| 想要 | 文件里有吗 | 替代读法 |
|---|---|---|
| `DynamicSettings.Platform / HmdType 推导结果 / CanUse120Hz / AutomaticBitrate / MeasuringBandwidth` | ❌ 内存态 | 见 §5 的 IL 探针 / 或读 `SharedMobileSettings.json` 的镜像字段 |
| `SharedStreamerSettings.ActiveCodec / AutoAdjustBitrate / AllowMotionExtrapolation / AdapterName` | ❌ 头显不落盘 | 抓 38810/tcp 消息，或读 PC 端 Streamer 配置 |
| `GetMaxVRBitrate` 的 200000000 上限 | ❌ 是 IL 常量 | 见 `03-il-constants.md` |

`DynamicSettings.CanUse120Hz` 等**派生量**还有一个间接读法：
`SettingsTab.cs:387` 用它决定 96/100/120Hz CheckBox 是否可用，
所以截图里 checkbox 的 `IsEnabled` 状态就是该值。

---

## 4. 生效时序（工具改值必须遵守）

```
App 启动
  └─ SettingsBase<T> 静态构造 → SettingsBase.cs:13-16  Default.Initialize()
       └─ JsonSettingsBase.Initialize() JsonSettingsBase.cs:66-69  → Reload() 读文件
            └─ UserSettings.Reload() UserSettings.cs:2386 覆写（还会同步 FoveatedStreaming 到 SharedMobileSettings）

App 运行中
  └─ 任意属性 set → OnPropertyChanged SerializedSettingsBase.cs:59-63
       └─ StartSaveTimer() :43-49  _saveTimer.Change(2000, -1)   ← 每次变更重置 2 秒
            └─ 2 秒后 OnSaveTimerElapsed :66-69 → Save() :16-40 File.WriteAllText

断连
  └─ 断连时把 LastComputerID 清空 ComputersTab.cs:303
```

**给工具的三条规则**：
1. **写盘前 force-stop**，否则 2 秒防抖会把内存值覆盖回去。
2. **改跨设备设置（SharedUserSettings/SharedMobileSettings）后必须重连**，
   因为它们在 `OnPropertyChanged` 里同步发给 PC（`NetworkSettingsBase.cs:212-226`），
   只在属性 set 的那一刻发一次；外部改文件不触发这个事件。
   → 工具应提示用户"改完重启应用或断开重连"。
3. **`IsServer` 判据**：`SharedUserSettings`/`SharedMobileSettings` 在头显是 server，
   所以**它们会落盘**；`SharedStreamerSettings` 在头显是 client，**改了文件也没用**。

---

## 5. 无文件可读时的替代：IL 探针

对 `DynamicSettings` 这类不落盘的对象，可行的替代路径是**在 Mono 运行时读字段偏移**。
Owner 上一轮已用同一条路读过码率常量（`03-il-constants.md`），
机制一致：拿 `Xenko.dll`（真 `VirtualDesktop.Mobile.dll`）的字段 RVA，
用 `adb` 附加或 dump 进程内存读 `SettingsBase<T>.Default` 单例。

已知可用的 RVA（来自反编译源码注释里的 `File Offset`，1.34.18.0 / 529408 字节的 `Xenko.dll`）：

| 目标 | File Offset | 来源 |
|---|---|---|
| `DynamicSettings.ActiveQuality` 相关字段区 | `0x2E627`~`0x2E784` | `DynamicSettings.cs:877-978` |
| `DynamicSettings.CanUse120Hz` 字段 | `0x2E5A5` | `DynamicSettings.cs:1116` |
| `NetworkManager.IsConnected` 字段 | `0x2A4B7` | `NetworkManager.cs:75` |
| `UserSettings.Sharpening` 字段 | `0x32961` | `UserSettings.cs:2518` |

`[未验证]` —— 这些 offset 是**文件内偏移**，进程内 ASLR 基址需另取。工具实现时要用
`/proc/<pid>/maps` 找 `libassemblies.arm64-v8a.blob.so` 的加载基址再加偏移；
且 patched APK 删了全部 AOT 走 JIT，托管堆是 GC 移动的，**单例引用位置需要先定位
`SettingsBase<T>.Default` 的静态字段槽**，不能直接套文件偏移。

**结论：优先走 §3 的 JSON 读法，IL 探针只作为最后手段。**

---

## 6. 验证条件（把这些 `[未验证]` 变成已验证需要什么）

本机**没有 adb**（实测：`which adb` → `command not found`；
`ls D:/Software/Android/Sdk/platform-tools/adb.exe` → `No such file or directory`）。
完成验证需要：

1. **硬件**：一台已开启开发者模式的 Quest / Quest 2 / Quest 3（Owner patched 基线所支持的机型）。
2. **工具**：Android SDK platform-tools（提供 `adb.exe`），路径加入 PATH 或用全路径调用。
3. **可执行步骤**：
   ```bat
   D:\Software\Android\Sdk\platform-tools\adb.exe devices
   :: 确认包名
   D:\Software\Android\Sdk\platform-tools\adb.exe shell pm list packages | findstr VirtualDesktop
   :: 确认 run-as 可用（debuggable）
   D:\Software\Android\Sdk\platform-tools\adb.exe shell run-as VirtualDesktop.Android id
   :: 确认目录
   D:\Software\Android\Sdk\platform-tools\adb.exe shell run-as VirtualDesktop.Android sh -c "ls -la \$HOME/.config/"
   ```
4. **需确证的 6 项**：
   - ① `UserAppDataPath` 在 Quest 上的真实绝对路径（§1.2 三种候选形态哪个成立）。
   - ② patched APK 是否 `debuggable`，决定 `run-as` 还是必须 root。
   - ③ 7 个权限是否全部处于 granted 状态（对照 `dumpsys package` 的 runtime permissions 段）。
   - ④ `MeasuredBandwidth` 在真机上的量级（决定 `GetMaxVRBitrate` 的插值落点）。
   - ⑤ `SettingsTab.cs:720` 显示的 Mbps 字符串与 `DesktopBitrateLimit` 的换算是否与 §2.3 公式一致。
   - ⑥ `SettingsTab.cs:2404` 与 `ScreenSettings.cs:258` 两套范围常量在真机滑条上各对应哪个。

**验证前不要把本文任何 adb 命令写进工具的必需路径** —— 目录形态 (§1.2) 未定，
工具应当先做一次「find + 回退」，而不是硬编码。