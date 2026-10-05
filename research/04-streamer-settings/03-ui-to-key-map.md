# 03 · 官方 UI 控件 ↔ 配置键映射 + 版本差异比对

调研日期：2026-10-05
UI 来源：反编译 XAML `VirtualDesktop/Streamer/MainWindow.xaml`（1.34.18）
UI 文案来源：`reference/vdapkpatcher/profiles/streamer-1.34.22/baml-strings.csv`（**1.34.22 的实际 BAML 文案**，含汉化）
键默认值来源：`01-config-keys.md`

> **这是 VDH UI 应该照抄的地方**：控件结构、绑定路径、分组顺序、可见性门控。

---

## 1. 主窗口标签页结构

`MainWindow.xaml` 共 6 个 `TabItem`，`SelectedTab` 索引对应（`StreamerSettings.cs:1114`；消费点 `StreamerManager.cs:855` 只判断 `== 5`）：

| 索引 | 标签（BAML 文案） | 行号 | 内容 |
|---|---|---|---|
| 0 | `ACCOUNTS` | `MainWindow.xaml:~300` | 账号管理（配对，只读） |
| 1 | `BINDINGS` | `MainWindow.xaml:~450` | 键盘快捷键 |
| 2 | `OPTIONS` | `MainWindow.xaml:~530` | 编解码/音频/连接开关 |
| 3 | `MEDIA` | `MainWindow.xaml:~920` | 视频/截图目录 |
| 4 | `ADVANCED` | `MainWindow.xaml:~720` | FOV/分辨率/锐化 |
| 5 | `ABOUT` | `MainWindow.xaml:~1140` | 诊断信息（**Latency 只在此页刷新**） |

三个 `Reset to Defaults` 按钮分别在 BAML 里的 3 个 Tab（BAML 串计数 `"Reset to Defaults","恢复默认设置","3"`），
对应代码 `OnOptionsResetDefaultsClick`（`MainWindow.xaml.cs:778-817`）与 `OnAdvancedOptionsResetDefaultsClick`（`MainWindow.xaml.cs:819-837`）。

---

## 2. OPTIONS 页（索引 2）— 逐控件映射

XAML 行号来自 `MainWindow.xaml`。

### 2.1 左列：编解码与音频

| 控件类型 | 标签（BAML 原文） | 绑定路径 | `Mode` | XAML 行 | JSON 键 | 默认 |
|---|---|---|---|---|---|---|
| `Label` | **Preferred Codec** | — | — | 561 | — | — |
| `ComboBox` | （ItemsSource = `VideoCodecProvider`） | `StreamerSettings.Default.PreferredCodec` | **TwoWay** | 566 | `PreferredCodec` | 0 Automatic |
| `CheckBox` | **Adaptive quantization**<br>Tip: "Reduces compression artifacts in dark scenes (recommended)" | `StreamerSettings.Default.EnableAQ` | 默认 OneWay→实际双向 | 580 | `EnableAQ` | `true` |
| `CheckBox` | **4:4:4 Chroma subsampling**<br>Tip: "Improves color accuracy…Only supported with H.264 and HEVC 10-bit on recent iPhones and iPads" | **无绑定**，`Visibility="Collapsed"` | — | 585 | `EnableYuv444` | `false` |
| `CheckBox` | **2-Pass encoding**<br>Tip: "Improves compression quality but uses some GPU horsepower…Only use when you have performance headroom to spare" | `StreamerSettings.Default.Enable2Pass`<br>`Visibility` ← `DynamicSettings.Default.Supports2Pass` | 默认 | 591-592 | `Enable2Pass` | `false` |
| `Label` | **OpenXR Runtime** | — | — | 595 | — | — |
| `ComboBox` | （ItemsSource = `OpenXRRuntimeProvider`） | `StreamerSettings.Default.OpenXRRuntime` | **TwoWay** | 601 | `OpenXRRuntime` | 0 Automatic |
| `Label` | **Gamepad Emulation** | — | — | 610 | — | — |
| `ComboBox` | （ItemsSource = `GamepadEmulationProvider`） | `StreamerSettings.Default.GamepadEmulation` | **TwoWay** | 616 | `GamepadEmulation` | 0 Automatic |
| `Label` | **Audio Streaming** | — | — | 625 | — | — |
| `ComboBox` | （ItemsSource = `AudioStreamingProvider`） | `StreamerSettings.Default.AudioStreaming` | **TwoWay** | 631 | `AudioStreaming` | 1 HeadsetOnly |
| `CheckBox` | **Use virtual audio driver**<br>Tip: "Enable this option if you experience issues streaming audio" | `StreamerSettings.Default.UseVirtualAudioDriver` | 默认 | 653 | `UseVirtualAudioDriver` | `true` |
| `CheckBox` | **VoiceMeeter mode**<br>Tip: "Enable this option to allow Virtual Desktop to work with VoiceMeeter" | `StreamerSettings.Default.VoiceMeeterMode`<br>`IsEnabled` ← `StreamerSettings.Default.UseVirtualAudioDriver` | 默认 | 659-660 | `VoiceMeeterMode` | `false` |

**三个 `ComboBox` 的统一模式**（照抄要点）：

```xml
<ComboBox Width="180" HorizontalAlignment="Left"
          SelectedValuePath="Key"
          ItemsSource="{Binding Source='{StaticResource VideoCodecProvider}'}"
          SelectedValue="{Binding Source='{StaticResource StreamerSettings}', Path=Default.PreferredCodec, Mode=TwoWay}">
  <ItemsControl.ItemTemplate>
    <DataTemplate><TextBlock FontSize="12" Padding="4 0 0 0" Text="{Binding Value}" /></DataTemplate>
  </ItemsControl.ItemTemplate>
</ComboBox>
```

`Key` = enum 整数值，`Value` = `EnumDisplayName` 显示名（`Core/EnumDisplayNameAttribute.cs`）。
**VDH 应该用整数下拉框 + 显示名，不要用字符串下拉框**——JSON 里存的是整数。

### 2.2 右列：连接与系统开关

| 控件类型 | 标签（BAML 原文） | 绑定路径 | XAML 行 | JSON 键 | 默认 |
|---|---|---|---|---|---|
| `CheckBox` | **Allow remote connections** | `StreamerSettings.Default.AllowRemoteConnections` | 667 | `AllowRemoteConnections` | `true` |
| `CheckBox` (x:Name=`chkEncryptLocalTraffic`) | **Encrypt local traffic** | `StreamerSettings.Default.EncryptLocalTraffic` | 673 | `EncryptLocalTraffic` | `false` |
| `CheckBox` (x:Name=`chkAutoAdjustBitrate`) | **Automatically adjust bitrate**<br>Tip: "Adjusts the video bitrate based on the available bandwidth (requires re-connection)" | `StreamerSettings.Default.AutoAdjustBitrate` | 679 | `AutoAdjustBitrate` | `true` |
| `CheckBox` | **Start with Windows** | `StreamerSettings.Default.StartWithWindows` | 682 | `StartWithWindows` | `true` |
| `CheckBox` | **Start minimized in tray** | `StreamerSettings.Default.StartMinimizedInTray` | 685 | `StartMinimizedInTray` | `false` |
| `CheckBox` | **Use touch input**<br>Tip: "Enable to allow touch gestures. Disable if clicks don't work correctly" | `StreamerSettings.Default.UseTouchInput` | 689 | `UseTouchInput` | `true` |
| `CheckBox` | **Lock computer on disconnect** | `StreamerSettings.Default.LockComputer` | 692 | `LockComputer` | `false` |
| `CheckBox` | **Auto-select microphone**<br>Tip: "Automatically selects the Virtual Desktop Microphone in Windows when Microphone passthrough is enabled in VR" | `StreamerSettings.Default.AutoSelectMicrophone` | 696 | `AutoSelectMicrophone` | `true` |
| `CheckBox` | **Ask for computer access**<br>Tip: "Shows a prompt when a new headset is searching for computers in your local network" | `StreamerSettings.Default.ShowPairingRequests` | 700 | `ShowPairingRequests` | `true` |

**`chkEncryptLocalTraffic` 的额外事件钩子**（`MainWindow.xaml.cs:1382-1393`）：

```csharp
private void OnEncryptLocalTrafficCheckedChanged(object sender, RoutedEventArgs e) {
    if (SettingsBase<DynamicSettings>.Default.IsConnected) {
        // 比较 chkEncryptLocalTraffic 与已生效的 IsLocalTrafficEncrypted
        if (changed) ShowMessage("You will need to disconnect and re-connect to your computer for this change to take effect.");
    }
}
```

**照抄要点**：只有「已连接时改需要重连的键」才弹提示，且比较的是**运行时的实际状态**而非配置值。

### 2.3 OPTIONS 页「Reset to Defaults」实际写了什么

`MainWindow.xaml.cs:780-815`（**不是**调用 `Reset()`，是逐键赋值）：

| 键 | 重置为 | 与 `[DefaultValue]` 是否一致 |
|---|---|---|
| `PreferredCodec` | `VideoCodec.Automatic` | 一致 |
| `EnableAQ` | `true` | 一致 |
| `Enable2Pass` | `false` | 一致 |
| `GamepadEmulation` | `GamepadEmulation.Automatic` | 一致 |
| `AudioStreaming` | `AudioStreaming.HeadsetOnly` | 一致 |
| `UseVirtualAudioDriver` | `true` | 一致 |
| `VoiceMeeterMode` | `false` | 一致 |
| `AllowRemoteConnections` | `true` | 一致 |
| `EncryptLocalTraffic` | `false` | 一致 |
| `AutoAdjustBitrate` | `true` | 一致 |
| `StartWithWindows` | `true` | 一致 |
| `StartMinimizedInTray` | `false` | 一致 |
| `UseTouchInput` | `true` | 一致 |
| `LockComputer` | `false` | 一致 |
| `AutoSelectMicrophone` | `true` | 一致 |
| `ShowPairingRequests` | `true` | 一致 |
| `OpenXRRuntime` | `OpenXRRuntime.Automatic` | 一致 |

**17/17 全部与 `[DefaultValue]` 一致** → VDH 的「恢复默认」按钮可以直接用同一张默认值表，无需再硬编码一份。

**注意重置范围**：不碰 `Accounts`/`Protected*`/`VideosRootPath`/`DeviceName`/`CodecName`/`LastConnectDate`/`MonitorCount`/`DontWarnApps`。

---

## 3. ADVANCED 页（索引 4）— 逐控件映射

页首两行提示（XAML `:749`、`:756`）：
- "Do NOT change the settings below unless you know what you are doing"
- "Requires restart of game / SteamVR to take effect"

### 3.1 左列：滑块组（全部是 `[DefaultValue]` + XAML `Minimum`/`Maximum` 双约束）

| 控件 | 标签 | `Minimum` | `Maximum` | `TickFrequency` | `LargeChange` | 绑定 | XAML(Label/Slider) | JSON 键 | 默认 | 一致性 |
|---|---|---|---|---|---|---|---|---|---|---|
| `Slider` | **Horizontal FOV Tangent** | 0.4 | 1.0 | 0.01 | 0.05 | `StreamerSettings.Default.HorizontalFovTangent` | 766 / 775 | `HorizontalFovTangent` | `1.0` | 在范围内（=Maximum） |
| `Slider` | **Vertical FOV Tangent** | 0.4 | 1.0 | 0.01 | 0.05 | `StreamerSettings.Default.VerticalFovTangent` | 785 / 796 | `VerticalFovTangent` | `1.0` | 同上 |
| `Slider` | **VDXR Render Resolution** | 0.5 | 2.0 | 0.01 | 0.05 | `StreamerSettings.Default.RenderResolutionOverride` | 804 / 813 | `RenderResolutionOverride` | `1.0` | 范围内 |
| `Slider` | **Fovea Size**<br>Tip: "Only applies to eye-tracked headsets with Foveated streaming enabled" | 0.15 | 0.35 | 0.01 | 0.05 | `StreamerSettings.Default.FoveaSize` | 831 / 840 | `FoveaSize` | `0.25` | 范围内 |
| `CheckBox` | **Additional sharpening**<br>Tip: "Adds Contrast Adaptive Sharpening (CAS) before video encoding.\nDoesn't require a restart of game or SteamVR to take effect" | — | — | — | — | `StreamerSettings.Default.UseSharpening` | 847 | `UseSharpening` | `false` | — |
| `Slider` | （无标签，嵌在上一 CheckBox 的 StackPanel 里，`IsEnabled` ← `UseSharpening`） | 0.05 | 1.0 | 0.01 | 0.05 | `StreamerSettings.Default.Sharpening` | 856 | `Sharpening` | `0.7` | 范围内 |

**照抄要点**：

1. **所有滑块的百分比显示都用 `StringFormat=0%`**，例如：
   ```xml
   <TextBlock … Text="{Binding Source='{StaticResource StreamerSettings}', Path=Default.HorizontalFovTangent, StringFormat=0%}" />
   ```
   → `0.5` 显示成 `50%`。VDH 必须照抄这个换算，否则用户会看到 `0.5` 以为是 0.5%。
2. **`Sharpening` 滑块的 `IsEnabled` 绑定 `UseSharpening`**（XAML `:855`）——条件启用的标准写法。
3. **`RenderResolutionOverride` 下方有明确警告**（XAML `:821`）："Best to use 100% and control the resolution via the VR Graphics Quality in the Streaming tab in VR"
   → **这条是跨端提示**：分辨率档位在头显端，不在 PC。VDH 应该在同一个地方提示。

### 3.2 右列：两个需要条件计算的控件

**`Use Fov Stencil`**（XAML `:884-903`）— **三路 `MultiBinding`，最值得抄的一个**：

```xml
<CheckBox Content="Use Fov Stencil"
          ToolTip="Optimizes game rendering by not rendering the corners. Only uncheck when recording gameplay (requires restart of game/SteamVR)">
  <UIElement.IsEnabled>
    <MultiBinding Converter="{StaticResource FovStencilIsEnabledConverter}">
      <Binding Source="{StaticResource StreamerSettings}" Path="Default.HorizontalFovTangent" />
      <Binding Source="{StaticResource StreamerSettings}" Path="Default.VerticalFovTangent" />
    </MultiBinding>
  </UIElement.IsEnabled>
  <ToggleButton.IsChecked>
    <MultiBinding Converter="{StaticResource FovStencilIsCheckedConverter}">
      <Binding Source="{StaticResource StreamerSettings}" Path="Default.UseFovStencil" />
      <Binding Source="{StaticResource StreamerSettings}" Path="Default.HorizontalFovTangent" Mode="OneWay" />
      <Binding Source="{StaticResource StreamerSettings}" Path="Default.VerticalFovTangent" Mode="OneWay" />
    </MultiBinding>
  </ToggleButton.IsChecked>
</CheckBox>
```

语义：**只有当两个 FOV tangent 都是 100% 时，FOV stencil 才有意义且可勾选**（切掉四角会切掉你正在看的内容）。
VDH 应该在两个 FOV tangent ≠ 1.0 时禁用并解释原因。

**`Boost game priority`**（XAML `:904-906`）：

| 控件 | 标签 | 绑定 | JSON 键 | 默认 |
|---|---|---|---|---|
| `CheckBox` (x:Name=`chkBoostGamePriority`) | **Boost game priority**<br>Tip: "Only enable if experiencing game freezes. Increases the GPU priority of games (requires restart of game/SteamVR)" | `StreamerSettings.Default.BoostGamePriority` | `BoostGamePriority` | `false` |

### 3.3 ADVANCED 页「Reset to Defaults」

`MainWindow.xaml.cs:821-835`：

| 键 | 重置为 | 与 `[DefaultValue]` |
|---|---|---|
| `HorizontalFovTangent` | `1f` | 一致 |
| `VerticalFovTangent` | `1f` | 一致 |
| `RenderResolutionOverride` | `1f` | 一致 |
| `FoveaSize` | `0.25f` | 一致 |
| `UseFovStencil` | `true` | 一致 |
| `BoostGamePriority` | `false` | 一致 |
| `UseSharpening` | `false` | 一致 |
| `Sharpening` | `0.7f` | 一致（= 常量 `DefaultSharpening`） |

**8/8 一致**。

---

## 4. MEDIA / ACCOUNTS / ABOUT 页映射

### 4.1 MEDIA 页（索引 3）

| 控件 | 标签（BAML） | 绑定源 | 实际 JSON 键 | 备注 |
|---|---|---|---|---|
| `TextBlock` | （路径显示） | `DynamicSettings.Default.ScreenshotsRootPath` | 写 `StreamerSettings.ScreenshotsRootPath` | XAML `:971`；`DynamicSettings.ScreenshotsRootPath` **无绑定**（运行时属性） |
| `Image` | （文件夹图标） | `DynamicSettings.Default.ScreenshotsRootPathImage` | — | XAML `:957` |
| `Button` | **Change location...** | Click → `MainWindow.xaml.cs:1108` 写 `StreamerSettings.ScreenshotsRootPath` | `ScreenshotsRootPath` | XAML `:973` |
| `TextBlock` | （路径显示） | `DynamicSettings.Default.VideosRootPath` | 写 `StreamerSettings.VideosRootPath` | XAML `:1010` |
| `Image` | （文件夹图标） | `DynamicSettings.Default.VideosRootPathImage` | — | XAML `:996` |
| `Button` | **Change location...** | Click → `MainWindow.xaml.cs:1168` 写 `StreamerSettings.VideosRootPath` | `VideosRootPath` | XAML `:1013` |
| `ItemsControl` | （子文件夹列表） | `DynamicSettings.Default.VideoFolders` | — | XAML `:1038`；无独立 JSON 键 |
| `Button` | **Add video folder...** | `MainWindow.xaml.cs:1273-1315` 写 `VideosRootPath` 下的子目录 | `VideosRootPath` | — |
| `Button` | **Remove** | 同上 | `VideosRootPath` | — |
| `Button` | **Paste URL** | `YouTubeHelper`，Tip: "Download a YouTube video from the URL in the clipboard (Ctrl+V)" | — | 不涉及配置键 |

> **照抄要点**：`VideosRootPath` / `ScreenshotsRootPath` 的**默认值不在 JSON 里**，而是运行时回退：
> `VideosRootPath` → `Environment.SpecialFolder.MyVideos`（`DynamicSettings.cs:1216-1221`）
> `ScreenshotsRootPath` → `GetFolderPath((SpecialFolder)0)`（`DynamicSettings.cs:1418-1430`）
> 且 `VideosRootPath` 会**自动补尾部分隔符**（`DynamicSettings.cs:1223-1227`）。本机实测值为 `"C:\\Users\\dwgx1\\Videos\\"`（带尾 `\`）。

### 4.2 ACCOUNTS 页（索引 0）

| 控件 | 绑定 | JSON 键 | 说明 |
|---|---|---|---|
| `ItemsControl` (DataTemplate) | `IsEditingAccounts` 切 `Visibility` | — | `MainWindow.xaml:368,375,389,432` |
| `TextBlock` | `AccountIDsString` | 由 `Accounts` 派生 | XAML `:374` |
| `Button` | **Save** | 写 `Accounts` | XAML `:435` |
| `Button` | **Add account** / **Change** / **Cancel** | — | BAML 有这三条文案 |

**全页只操作 `Accounts`（DPAPI 密文）+ `DynamicSettings.IsEditingAccounts`**。VDH 对这一页应当**只读展示**。

### 4.3 ABOUT 页（索引 5）— VDH 诊断面板的直接对应物

| BAML 标签 | 绑定 | 类型 | VDH 诊断价值 |
|---|---|---|---|
| **Graphics Card:** | `DynamicSettings.Default.AdapterName` | string | GPU 选择是否正确 |
| **Hardware Encoder:** | `DynamicSettings.Default.HasHardwareEncoder` | bool | 多显示器/VR 串流前提（Tip: "A hardware video encoder is required for multi-monitor or VR game streaming"） |
| **Auto Adjust Bitrate:** | `DynamicSettings.Default.AutoAdjustBitrate` | bool | 码率自适应开关 |
| **Codec:** | `DynamicSettings.Default.CodecName` | string | 实际协商到的编码器 |
| **Headset:** | `DynamicSettings.Default.DeviceName` | string | 上次连接的头显 |
| **Latency:** | `DynamicSettings.Default.Latency` + `LatencyConverter` | int (ms) | **实时延迟** |
| **Operating System:** | `UISettings.Default.OSVersion` | string | — |
| **Processor:** | `UISettings.Default.ProcessorName` | string | 读 `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` 的 `ProcessorNameString`（`UISettings.cs:75-78`） |
| **Memory:** | `UISettings.Default.Memory` | string | GlobalMemoryStatusEx（`UISettings.cs:114-127`） |
| **Router Settings:** | `DynamicSettings.Default.RouterUrl` | string | **路由器管理页 URL** |
| **PC Ethernet:** | `DynamicSettings.Default.ConnectionType` + `PCEthernetConverter` | enum | **PC 网卡是否有线** |
| **Remote Routing:** | `DynamicSettings.Default.RoutingStatus` + `RoutingStatusConverter` | enum | **PublicIP/NAT/DoubleNAT/CGNAT** |
| **Secure Boot:** | `DynamicSettings.Default.SecureBoot` + `BooleanToEnabledConverter` | bool | 安全启动（影响驱动加载） |
| **Version:** | `UISettings.Default.ProductVersion` | string | 1.34.22.0 |

> **这是本次调研对 VDH 最有价值的一节**：VDH 的「诊断」页几乎就是 ABOUT 页 + LAN 探测。
> **`PC Ethernet:` 和 `Remote Routing:` 两行是 Streamer 自己算的**，比 VDH 重新实现 STUN/NAT 检测更可信——可以直接读。
> `Latency` 只在 `SelectedTab == 5` 且 `IsWindowVisible` 时刷新（`StreamerManager.cs:854-856`），**ABOUT 页不打开就没有新数据**。

---

## 5. 版本差异比对（1.34.18 → 1.34.21 → 1.34.22）

### 5.1 profile 目录逐文件 diff

```bash
$ cd "D:/Project/VirtualDesktopHelper/reference/vdapkpatcher/profiles"
$ for f in baml-strings.csv binding-actions.csv managed-strings.csv profile.json; do
    echo "=== $f ==="; diff "streamer-1.34.21/$f" "streamer-1.34.22/$f" && echo "IDENTICAL"; done
```

实际输出：

```
=== baml-strings.csv ===
IDENTICAL
=== binding-actions.csv ===
IDENTICAL
=== managed-strings.csv ===
IDENTICAL
=== profile.json ===
2,3c2,3
<   "id": "streamer-1.34.21-zh-cn",
<   "version": "1.34.21.0",
---
>   "id": "streamer-1.34.22-zh-cn",
>   "version": "1.34.22.0",
5c5
<     "CC1276B45C9DD5FC31C7C79406397D184622BA5B33D5200D9B416A1F9D535A9A"
---
>     "6BFEC9E4E62509F4FDB0EC21C144B4584F9450701DCF5BD756D6FD2AEC7CBB51"
```

**比对了 4 个文件：**

| 文件 | 1.34.21 vs 1.34.22 | 结论 |
|---|---|---|
| `baml-strings.csv`（96 行 UI 文案） | 字节相同 | **UI 文案零变化 → 无新增/删除的设置项标签** |
| `managed-strings.csv`（115 行托管字符串） | 字节相同 | 提示文案零变化 → 无新增的诊断提示 |
| `binding-actions.csv`（10 行热键枚举） | 字节相同 | `BindingAction` 枚举零变化 → 热键键集未增删 |
| `profile.json` | 仅 `id` / `version` / `inputSha256` 三处不同 | 只有版本号与二进制哈希变化 |

**判定：1.34.21 → 1.34.22 配置键无增删。**

### 5.2 1.34.18（反编译源）→ 1.34.22（本机二进制）键名实测比对

profile CSV 只能证明**文案**没变，不能证明**属性名**没变。所以直接在本机 1.34.22 的 `VirtualDesktop.Streamer.exe` 原始字节里搜 1.34.18 源码提取的 38 个 `StreamerSettings` 键名（.NET 元数据 #US 堆是 UTF-16LE，#Strings 堆是 UTF-8，两种都查）：

```bash
$ python -c "
keys='SelectedTab ServerRotation ProtectedComputerID Accounts AllowRemoteConnections EncryptLocalTraffic AutoAdjustBitrate StartWithWindows StartMinimizedInTray AutoSelectMicrophone UseVirtualAudioDriver VoiceMeeterMode UseTouchInput LockComputer ShowPairingRequests ShownH264PlusWarning UseFovStencil HorizontalFovTangent VerticalFovTangent RenderResolutionOverride FoveaSize UseSharpening Sharpening PreferredCodec EnableAQ EnableYuv444 Enable2Pass AudioStreaming GamepadEmulation DeviceName CodecName VideosRootPath ScreenshotsRootPath LastConnectDate BoostGamePriority OpenXRRuntime MonitorCount DontWarnApps'.split()
data=open(r'C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe','rb').read()
miss=[k for k in keys if k.encode('utf-16-le') not in data and k.encode('utf-8') not in data]
print('checked',len(keys),'missing',miss)"
```

实际输出：

```
checked 38 missing []
```

**38/38 全部命中。判定：1.34.18 → 1.34.22 无键名删除。**

顺带验证文件名字符串（三种编码的探测结果）：

```
StreamerSettings  u16 1  u8 10
BindingSettings   u16 1  u8 1
DontWarn          u16 0  u8 8
GameSettings      u16 0  u8 1
```

→ 三个配置文件名的字符串在 1.34.22 里都在，路径逻辑（`02-file-locations.md` §1.3）未变。

### 5.3 双向差异的诚实边界

| 断言 | 状态 | 说明 |
|---|---|---|
| 1.34.18 的 38 个 `StreamerSettings` 键在 1.34.22 中都存在 | ✅ 已验证（二进制字节搜索） | 上面的命令输出 |
| 1.34.21 与 1.34.22 的 UI/提示/热键文案与枚举无差异 | ✅ 已验证（4 文件 diff） | 上面的 diff 输出 |
| **1.34.22 是否新增了 1.34.18 没有的键** | ❓ **未验证** | 见下 |

**为什么「未验证」**：我手上只有 1.34.18 的反编译源码树，没有 1.34.22 的反编译源码。仅靠二进制字符串搜索**无法排除**新增键（新增键只会让字符串更多，不会让旧字符串消失）。要确证需要：

- 反编译 `C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe`，或
- 拿到 1.34.22 的反编译源码树。

**给 VDH 的影响**：低。`DefaultValueHandling.IgnoreAndPopulate` 意味着未知键即使存在也会被**静默忽略**（`JsonSettingsBase` 用 `PopulateObject`，未匹配的属性直接丢弃）。所以即使 1.34.22 有新键，VDH 读旧表也不会崩，只是显示不出那几个新参数。

---

## 6. 给 VDH UI 的映射速查表

| VDH 面板分组 | 控件 | 绑定键 | 数据源 |
|---|---|---|---|
| **串流** | 下拉框（整数项） | `PreferredCodec` | StreamerSettings.json |
| | 下拉框（整数项） | `OpenXRRuntime` | StreamerSettings.json |
| | 下拉框（整数项） | `GamepadEmulation` | StreamerSettings.json |
| | 下拉框（整数项） | `AudioStreaming` | StreamerSettings.json |
| | 滑块 0.4–1.0 (`0%` 格式) | `HorizontalFovTangent` | StreamerSettings.json |
| | 滑块 0.4–1.0 (`0%` 格式) | `VerticalFovTangent` | StreamerSettings.json |
| | 滑块 0.5–2.0 (`0%` 格式) | `RenderResolutionOverride` | StreamerSettings.json |
| | 滑块 0.15–0.35 (`0%` 格式) | `FoveaSize` | StreamerSettings.json |
| | 勾选 + 联动滑块 0.05–1.0 | `UseSharpening` / `Sharpening` | StreamerSettings.json |
| | 勾选（条件启用） | `UseFovStencil` | StreamerSettings.json |
| | 勾选 | `EnableAQ` / `Enable2Pass` / `EnableYuv444` | StreamerSettings.json |
| | 勾选 | `BoostGamePriority` | StreamerSettings.json |
| | 数字框（只读） | `MonitorCount` | StreamerSettings.json |
| **音频** | 勾选（级联） | `UseVirtualAudioDriver` → `VoiceMeeterMode` | StreamerSettings.json |
| | 勾选 | `AutoSelectMicrophone` | StreamerSettings.json |
| **连接** | 勾选 ⚠️重连 | `AutoAdjustBitrate` | StreamerSettings.json |
| | 勾选 ⚠️重连 | `EncryptLocalTraffic` | StreamerSettings.json |
| | 勾选 ⚠️ 影响公网可达 | `AllowRemoteConnections` | StreamerSettings.json |
| | 勾选 ⚠️ 影响配对 | `ShowPairingRequests` | StreamerSettings.json |
| **系统** | 勾选 | `StartWithWindows` / `StartMinimizedInTray` / `LockComputer` / `UseTouchInput` | StreamerSettings.json |
| **媒体** | 目录选择器 | `VideosRootPath` / `ScreenshotsRootPath` | StreamerSettings.json |
| **诊断（只读）** | 文本 | `Latency` | ❌ 不落盘，只能读 UI |
| | 文本 | `RouterUrl` / `ConnectionType` / `RoutingStatus` | ❌ 不落盘 |
| | 文本 | `HasHardwareEncoder` / `Supports2Pass` / `SupportsYuv444` / `SecureBoot` / `HasModifiedHostFile` | ❌ 不落盘 |
| | 文本 | `DeviceName` / `CodecName` / `AdapterName` | 前两个落盘，`AdapterName` 不落盘 |
| | 文本 | `ProductVersion` / `OSVersion` / `ProcessorName` / `Memory` | ❌ 不落盘（`UISettings`） |
| **配对（只读，永不写）** | — | `Accounts` / `ProtectedComputerID` / `Protected*ID` / `SavedPicoID*` | StreamerSettings.json（DPAPI 密文） |
| **头显端（另一端）** | — | `VRFramerate` / `VRGraphicsQuality` / `DesktopBitrateLimit` / `VRBitrateLimit` / `DesktopFramerate` / `RefreshRate` / `FoveatedStreaming` / `MicPassthrough` / `MicVolume` / `Spacewarp` | ❌ PC 本地没有 |

---

## 7. 引用位置索引

| 事实 | file:line |
|---|---|
| 主窗口 XAML（6 个 Tab） | `VirtualDesktop/Streamer/MainWindow.xaml` |
| OPTIONS 左列编解码区 | `MainWindow.xaml:561-661` |
| OPTIONS 右列连接区 | `MainWindow.xaml:667-701` |
| ADVANCED 页 FOV/分辨率/锐化 | `MainWindow.xaml:749-880` |
| `UseFovStencil` 三路 MultiBinding | `MainWindow.xaml:884-903` |
| `BoostGamePriority` | `MainWindow.xaml:904-906` |
| MEDIA 页 | `MainWindow.xaml:920-1100` |
| ABOUT 页绑定 | `MainWindow.xaml:1158-1359` |
| OPTIONS 重置实现 | `MainWindow.xaml.cs:778-817` |
| ADVANCED 重置实现 | `MainWindow.xaml.cs:819-837` |
| 加密重连提示 | `MainWindow.xaml.cs:1382-1393` |
| 截图目录写入 | `MainWindow.xaml.cs:1102-1110` |
| 视频目录写入 | `MainWindow.xaml.cs:1160-1170` |
| 视频子目录增删 | `MainWindow.xaml.cs:1244-1315` |
| 1.34.22 UI 文案（含汉化） | `reference/vdapkpatcher/profiles/streamer-1.34.22/baml-strings.csv` |
| 1.34.22 托管字符串 | `reference/vdapkpatcher/profiles/streamer-1.34.22/managed-strings.csv` |
| `BindingAction` 枚举与默认键 | `VirtualDesktop/Streamer/BindingAction.cs:8-28` |
| `EnumDisplayNameAttribute`（下拉框显示名） | `VirtualDesktop/Core/EnumDisplayNameAttribute.cs` |
| Latency 只在 ABOUT 页刷新 | `VirtualDesktop/Streamer/StreamerManager.cs:854-856` |