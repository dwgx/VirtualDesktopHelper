# 01 — 官方 Virtual Desktop Streamer WPF UI 逆向规格

> 只读逆向 / 未修改 `C:\Program Files\Virtual Desktop Streamer\` / 未启动 Streamer 进程 / 2026-10-05
> 目标：产出可直接照着实现的 UI 规格，让 VDHelper 的界面「像官方的一个模块」。

---

## 0. 结论速览（TL;DR）

- 官方 Streamer 是**托管 .NET Framework WPF 程序集**，SmartAssembly 混淆。UI 视觉语言 = **MetroDark 主题**（MahApps.MetroDark 血统），由 `Themes/Generic.xaml` 合并 `Themes/Metrodark/*` 提供。
- **反编译源码里已有完整 XAML**（17 个 `.xaml`，10328 行），无需再解 BAML —— 这是本轮最大收获，`R1` 里「需要 dnSpy 反解 BAML」的路线**不必要**。
- **主窗口 = 700×516 固定起步尺寸**，`WindowStyle=None` + `AllowsTransparency=True` + `WindowChrome` 自绘标题栏，三行 Grid：`48px 标题栏 / * 内容 / 48px 状态栏`。
- 导航**不是**左侧导航栏 + 右侧内容，而是 `TabControl TabStripPlacement="Left"`（左侧竖排 tab，160×48/项），内容区 `#101010`。
- **6 个 tab，顺序**：ACCOUNTS → BINDINGS → OPTIONS → ADVANCED → MEDIA → ABOUT。
- 字体 **Verdana**，正文 `14.667`（WPF 设备无关像素，≈11pt），控件隐式样式 13.333（≈10pt）。
- **Xceed.Wpf.Toolkit 只用于主题样式的 `WatermarkTextBox` / `ColorPicker` / `ColorCanvas` / `ColorSpectrumSlider`**，业务视图**一个都没用**。brief 里假设的 `NumericUpDown`、`CheckListBox` 在整个程序集里**不存在**（grep 全仓 0 命中）。
- **托盘不是 WPF**，是 WinForms `NotifyIcon` + `ContextMenuStrip`（`System.Windows.Forms`），菜单项在 code-behind 里用 `new ToolStripMenuItem(...)` 硬编码。

### 与旧 VDH 0.4.7 的页面对照（重要修正）

brief 说旧 VDH 是 `Home/VDH/Guide/Headset` **四页**。实测是 **六页**（`reference/legacy_vdh/VDH.cs:227-232, 454-459`）：

| # | 旧 VDH（WinForms TabControl） | 官方（TabStripPlacement=Left） |
|---|---|---|
| 0 | Home（主页） | ACCOUNTS |
| 1 | Accounts（账户） | BINDINGS |
| 2 | Wiki（指南） | OPTIONS |
| 3 | Headset（头显） | ADVANCED |
| 4 | VDH（本软件） | MEDIA |
| 5 | About（关于） | ABOUT |

旧 VDH 是 WinForms（`VDH.cs` 纯代码建控件，**无任何 `.xaml`**），字号 `Segoe UI 14 Bold`（`VDH.cs:425`）。要对齐观感，只需对齐**官方**，不必迁就旧版的 WinForms 观感。

---

## 1. 视图类清单（Window / UserControl）

源码树：`F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer\VirtualDesktop.Streamer\`

| 视图 | 文件 | 行数 | 类型 | 尺寸 |
|---|---|---|---|---|
| `VirtualDesktop.Streamer.MainWindow` | `VirtualDesktop/Streamer/MainWindow.xaml` | 1460 | Window（主） | 700×516 |
| `VirtualDesktop.Streamer.AddAccountWindow` | `VirtualDesktop/Streamer/AddAccountWindow.xaml` | 133 | Window（模态） | 490×190 |
| `VirtualDesktop.Streamer.AppsCheckerWindow` | `VirtualDesktop/Streamer/AppsCheckerWindow.xaml` | 154 | Window（模态） | 760×460, Min 600×300 |
| `VirtualDesktop.UI.MessageBoxWindow` | `VirtualDesktop/UI/MessageBoxWindow.xaml` | 158 | Window（自制对话框） | SizeToContent, 200–500 |
| `VirtualDesktop.UI.InvisibleWindow` | `VirtualDesktop/UI/InvisibleWindow.xaml` | 23 | Window（0×0 隐形） | 0×0 |
| `VirtualDesktop.Streamer.ShortcutEditor` | `Themes/Shortcuteditor.xaml` | 168 | **UserControl**（非 Window） | MinHeight 30 |

**没有独立的 Page / UserControl 页面**：6 个 tab 都是 `TabItem` 里直接塞布局，没有导航框架、没有 `Frame`、没有 MVVM `Page`。唯一的自定义控件是 `ShortcutEditor`（一个按键捕获框）。

`InvisibleWindow` 是隐藏消息窗口（`AllowsTransparency` + `Topmost` + `ShowActivated=False`，`InvisibleWindow.xaml:11-23`），用于承接 Windows 消息，界面上不可见。

### 1.1 主窗口窗口属性（`MainWindow.xaml:2-30`）

```
WindowStyle="None"          自绘标题栏
AllowsTransparency="True"   透明背景，配合圆角
Background="#00FFFFFF"      完全透明
FontFamily="Verdana"
Foreground="#F0F0F0"
ResizeMode="CanResize"
WindowStartupLocation="CenterScreen"
Title="Virtual Desktop Streamer"
Width="700"  MinWidth="700"
Height="516" MinHeight="516"
UseLayoutRounding="True"  SnapsToDevicePixels="True"
```

`WindowChrome`（`MainWindow.xaml:234-248`，挂在 Window 级 Style 上）：

```xml
<WindowChrome CaptionHeight="0" CornerRadius="0"
              GlassFrameThickness="0" ResizeBorderThickness="8" />
```

`CaptionHeight=0` + `GlassFrameThickness=0` = 完全不用系统标题栏，**圆角靠内容里的 Border 画**。`ResizeBorderThickness="8"` = 8px 无边框拖拽区。

---

## 2. 主窗口排版层级（逐层，带 file:line）

### 2.1 三行骨架（`MainWindow.xaml:260-268`）

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="48" />   <!-- 标题栏 -->
    <RowDefinition Height="*"  />   <!-- 内容 -->
    <RowDefinition Height="48" />   <!-- 状态栏 -->
  </Grid.RowDefinitions>
```

### 2.2 行 0 — 标题栏（`MainWindow.xaml:270-305`）

```xml
<Border Grid.Row="0" Background="#1D1D1D" CornerRadius="8 8 0 0">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="320" />   <!-- logo 区 -->
      <ColumnDefinition Width="*"  />    <!-- 按钮区 -->
    </Grid.ColumnDefinitions>
    <Image Source="Resources/LogoHeader.png" Stretch="None" Margin="8 0 0 0"
           HorizontalAlignment="Left" VerticalAlignment="Center" />
    <StackPanel Grid.Column="1" Orientation="Horizontal"
                HorizontalAlignment="Right" Margin="0 4 4 0">
      <Button Style="{StaticResource ImageButtonStyle}"
              Content=".../Resources/Minimize32.png" />
      <Button Style="{StaticResource ImageButtonStyle}"
              Content=".../Resources/Close32.png" />
    </StackPanel>
  </Grid>
</Border>
```

- **只有最小化 + 关闭**，没有最大化按钮（`ResizeMode=CanResize` 但仍不给最大化入口）。
- 标题栏 48px 高，logo 300×48（`resources/logoheader.png` 实测 300×48），放在 320px 宽的列里，`Margin="8 0 0 0"` 左缩进 8。
- 按钮 `Margin="0 4 4 0"`：上 4、右 4。

### 2.3 行 1 — 内容区（`MainWindow.xaml:306-315`）

```xml
<Grid Row="1" Background="#151515">
  <TabControl TabStripPlacement="Left"
              BorderThickness="0" Padding="0" Background="#101010"
              SelectedIndex="{Binding ... SelectedTab, Mode=TwoWay}">
```

**关键**：TabControl 自己的背景 `#101010`（tab 导航条那一竖条），外面 Grid 是 `#151515`，而每个 TabItem 内容区又是 `#101010`。**两级灰度 `#151515` / `#101010` 是官方最显眼的层次手法。**

TabItem 本地覆盖（`MainWindow.xaml:97-169`，覆盖隐式 `TabItem` 样式）：

| 属性 | 值 |
|---|---|
| `Foreground` | `#808080`（未选中）|
| `Background` | `#151515` |
| `FontSize` | 12 |
| `Width` | 160 |
| `Height` | 48 |
| `Padding` | 16 |
| `HorizontalContentAlignment` | Stretch |

模板 = `Grid > Border(#151515, BorderThickness=0, Padding=绑定 Padding) > ContentPresenter(ContentSource="Header", HorizontalAlignment=Left)`。

触发器（`MainWindow.xaml:144-165`）：
- `IsMouseOver=True` **且** `IsSelected=False` → `Foreground=#FFFFFFFF`
- `IsSelected=True` → `Foreground=#FFFFFFFF`

即**未选中灰色 `#808080`，悬停或选中变纯白**，无背景色变化、无指示条 —— 极简。

Tab 内文字样式 `TabTextBlock`（`MainWindow.xaml:170-179`）：`FontWeight=Bold` + `FontSize=16`。

**注意**：首 tab `ACCOUNTS`（`MainWindow.xaml:316-319`）额外覆盖 `Padding="16 16 0 0"` + `Height="56"`（比默认 48 高 8，因为内容是卡片列表）。

### 2.4 行 2 — 状态栏（`MainWindow.xaml:1368-1458`）

```xml
<Border Grid.Row="2" Background="#1D1D1D" CornerRadius="0 0 8 8">
  <Grid VerticalAlignment="Center" Margin="12 0 12 0">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="Auto" MinWidth="60" />  <!-- "Version:" -->
      <ColumnDefinition Width="*" />                    <!-- 版本号 -->
      <ColumnDefinition Width="Auto" />                 <!-- 右侧按钮区 -->
    </Grid.ColumnDefinitions>
```

- 标题栏与状态栏**同色 `#1D1D1D`**，与内容区 `#151515` 形成三明治。
- `"Version:"` `Foreground="#808080"`；版本号 `Foreground="#A0A0A0"`；两者 `Margin="4"`。
- 右侧按钮 `Padding="10 0"`，图标 `16×16 Stretch="None"` + `TextBlock Margin="8 0 0 0"`（图标与文字间距 8）。
- YouTube 下载进度条：`ProgressBar Height="6" Margin="0 3"`，上方 `TextBlock FontSize="12" Margin="0 2 0 0"`；进度条容器 `Width="260"`。

### 2.5 圆角总表

| 位置 | CornerRadius | 证据 |
|---|---|---|
| 标题栏（上） | `8 8 0 0` | `MainWindow.xaml:273` |
| 状态栏（下） | `0 0 8 8` | `MainWindow.xaml:1371` |
| WindowChrome | `0`（不参与） | `MainWindow.xaml:242` |

窗口四角 = 内容区靠 `AllowsTransparency` + 两个 Border 的 8px 圆角拼出来。

---

## 3. 六个 tab 的控件清单与顺序（**照抄顺序**）

所有 tab 内容根 Grid 统一：`Background="#101010" Margin="24 8 8 8"`（MEDIA 是 `24 8 0 8`）。

### Tab 0 — ACCOUNTS（`MainWindow.xaml:316-454`）

根：`ScrollViewer(HorizontalScrollBarVisibility=Disabled, VerticalScrollBarVisibility=Auto) > StackPanel(Vertical)`

1. `ItemsControl`（平台账号卡片，`ItemsPanel` = `WrapPanel Background="#101010" Margin="24 8 8 8"`）
2. 卡片内 `Grid > StackPanel Margin="0,0,0,8"`：
   - `StackPanel Horizontal`：平台图标 `Image 16×16 Margin="4,0,0,0"` + 平台名 `Label Bold FontSize=14`
   - `ItemsControl` 账号列表
   - 账号行 `TextBlock Foreground="#FF808080" FontSize="14" Padding="4,0,6,6" TextWrapping=Wrap`
   - 删除按钮：`Button Width="20" Height="20" Margin="4 -5 0 0"` + `ImageButtonStyle` + `Clear20.png`，`ToolTip="Remove"`
   - `ToggleButton Padding="24 10 8 8" Margin="8 8 14 0"`：内容 `TextBlock "Add account"` + `TextBlock "▾" FontSize="18"`，`ContextMenu` = `ctxAddAccount`（`MenuItem` 带 `Icon` = 平台图标）
   - 行尾按钮：`Button Content="Change" Margin="-52 10 0 10" FontSize="14"`、`Button Content="Save" ... Visibility` 由 `MultiBinding(IsEditingAccounts, HasValidAccountID)` 决定

### Tab 1 — BINDINGS（`MainWindow.xaml:454-534`）

根 `Grid Margin="24 8 8 8"`，行：`Auto / * / Auto`

1. `Label "Keyboard shortcuts"` **Bold FontSize=14**（`MainWindow.xaml:472`）
2. `ScrollViewer Grid.Row="1" HorizontalScrollBarVisibility=False` > `ItemsControl`（`x:Name="BindingsItemsControl"`）
   - 行内 `Grid` 列：`* / Auto`
   - `Label FontSize=14 VerticalAlignment=Center`（快捷键名）
   - `local:ShortcutEditor`（`:515`，`Grid.ColumnSpan=2`，Height 170 系列）
   - `DataTrigger` → `Setter Background="#1D1D1D"`（编辑态高亮）
3. 底部 `Button Content="Reset to Defaults" HorizontalAlignment=Right Margin="... 14"`（`:526`）

### Tab 2 — OPTIONS（`MainWindow.xaml:535-713`）

根 `Grid Margin="24 8 8 8"`，行 `* / Auto`，列 **`246 / *`** —— 左列固定 246px 放参数，右列放开关。

**左列（StackPanel Grid.Column=0）：**
1. `Label "Preferred Codec"` Bold 14（`:559`）→ `ComboBox Width="180"`（`:562`，`HorizontalContentAlignment=Left`），item `TextBlock FontSize=12 Margin="4,0,0,0"`
2. `CheckBox "Adaptive quantization"` — 副文本「Reduces compression artifacts in dark scenes (recommended)」，`Margin="-5 0 0 0"`（`:577`）
3. `CheckBox "4:4:4 Chroma subsampling"` — 副文本两行（`:582`）
4. `CheckBox "2-Pass encoding"` — 副文本两行（`:587`）
5. `Label "OpenXR Runtime"` Bold 14 Margin="0 8 0 0" → `ComboBox Width="180"`（`:593,597`）
6. `Label "Gamepad Emulation"` Bold 14 Margin="0 8 0 0" → `ComboBox Width="180"`（`:612,616`）
7. `Label "Audio Streaming"` Bold 14 Margin="0 8 0 0" → `ComboBox Width="210"`（`:631,635`）
8. `CheckBox "Use virtual audio driver"` 副文本（`:650`）
9. `CheckBox "VoiceMeeter mode"` 副文本（`:655`）

**右列（StackPanel Grid.Column=1，VerticalAlignment=Top）：** 一列纯 CheckBox（`:666-698`）
1. `Allow remote connections`
2. `chkEncryptLocalTraffic` — `Encrypt local traffic`
3. `chkAutoAdjustBitrate` — `Automatically adjust bitrate`（副文本含 `(requires re-connection)`）
4. `Start with Windows`
5. `Start minimized in tray`
6. `Use touch input`（副文本 `Enable to allow touch gestures...`）
7. `Lock computer on disconnect`
8. `Auto-select microphone`（长副文本）
9. `Ask for computer access`（副文本 `Shows a prompt when a new headset is searching for computers in your local network`）

底部 `Button "Reset to Defaults"` Right（`:704`）。

> **CheckBox 两段式排版**：主标题 + 换行 + 灰色副文本，用 `&#xA;`（`\n`）分隔。副文本走 `ToolTip` 或 `TextBlock`。这是官方「一个开关带解释」的标准范式，VDHelper 的每个检测项应照此。

### Tab 3 — ADVANCED（`MainWindow.xaml:714-918`）

根 `Grid Margin="24 8 8 8"`，行 `Auto / Auto / * / Auto`，列 **`236 / *`**

1. 行 0 警告条（`StackPanel Grid.ColumnSpan=2 Orientation=Horizontal`）：`Image Info.png 16×16` + `TextBlock Foreground="#808080" TextWrapping=Stretch`「Do NOT change the settings below unless you know what you are doing」
2. 行 1：`TextBlock Foreground="#808080" Margin="20 0 0 0"`「Requires restart of game / SteamVR to take effect」
3. 行 2 主区（`StackPanel Margin="0 8 0 0"`）— **「Label + 横向 Slider + 数值」三件套**，每个 Slider `Width="160"`：

| Label | Slider 范围 | 行 |
|---|---|---|
| Horizontal FOV Tangent | `0.4–1.0`, TickFrequency 0.01/0.05 | `:767` |
| Vertical FOV Tangent | `0.4–1.0` | `:786` |
| VDXR Render Resolution | `0.5–2.0` | `:805` |
| Fovea Size | `0.15–0.35`（附「Only applies to eye-tracked headsets…」说明行）| `:830` |
| （Additional sharpening 勾选后出现）| `0.05–1.0` | `:851` |

- Label 样式：`Bold FontSize=14`，相邻项 `Margin="0 10 0 0"`（垂直间距 10）
- Slider 右侧数值 `TextBlock Style={StaticResource NormalTextBlock} FontSize=12 Margin="5 0 0 0" HorizontalAlignment=Right`
- 「Best to use 100%…」提示 `TextBlock Foreground="#808080" TextWrapping=Wrap Margin="4 4 0 0"`（`:818`）
- `CheckBox "Use Fov Stencil"`（`:871`）— `IsEnabled` 与 `IsChecked` 都用 **MultiBinding**（`HorizontalFovTangent` / `VerticalFovTangent` + `UseFovStencil`）
- `CheckBox chkBoostGamePriority "Boost game priority"`（`:902`）
- 底部 `Button "Reset to Defaults"` Right（`:909`）

### Tab 4 — MEDIA（`MainWindow.xaml:919-1096`）

根 `Grid Background="#101010" Margin="24 8 0 8"`，行 **`Auto / Auto / 16 / Auto / Auto / * / Auto`**，列 **`Auto / * / Auto`**

**Screenshots 区块（行 0–1）：**
1. 行 0 `StackPanel Horizontal`：图标（绑定） + `Label "Screenshots" Foreground="#BABABA" FontSize=14`
2. 路径 `TextBlock Foreground="#808080" FontSize=14 TextTrimming=CharacterEllipsis`
3. `Button "Change location..." Margin="8 0 16 0" FontSize=14`（列 2）
4. 行 3 说明：`Image Info.png` + `TextBlock Foreground="#808080" TextWrapping=Wrap`「Screenshots taken in VR will be copied to this folder」

**Videos 区块（行 3–4）**：同样结构，`Label "Videos"` + 路径 + `Button "Change location..."` + 说明「Videos from this location will be accessible in VR」

**视频文件夹列表（行 5）：** `ScrollViewer` > `ItemsControl`
- 行 `Grid` 列：`Auto / Auto(MinWidth=90) / * / Auto`
- `Image Folder.png 16×16` + `Label Foreground="#BABABA" FontSize=14` + 路径 `TextBlock Foreground="#808080" FontSize=14 TextTrimming=CharacterEllipsis Margin="4"` + 删除 `Button`（`ImageButtonStyle` + `Clear20.png`，`Padding="8 0 16 0"`，内容 `TextBlock "Remove"`）
- 底部 `Button "Add video folder..." Margin="0 10 0 10"`（`:1087`）

> **`#BABABA` 是官方次级标签色**（`Theme.Colors.xaml:26` `Color_002`），`#808080` 是三级/说明色。这两档灰度在 MainWindow 内联样式里被反复直写。

### Tab 5 — ABOUT（`MainWindow.xaml:1097-1365`）

根 `Grid Margin="24 8 8 8"`，**行 0–14 `Auto` + 行 15 `*` + 行 16 `Auto`**，列 `Auto / *`

**这是一个「标签-值」两列信息表**，顺序即诊断信息顺序：

| 行 | 标签 | 值控件 |
|---|---|---|
| 0 | Graphics Card: | TextBlock `Margin=3` |
| 1 | Headset: | |
| 2 | Codec: | |
| 3 | Latency: | |
| 4 | Processor: | |
| 5 | Hardware Encoder: | 带长说明「A hardware video encoder is required for multi-monitor or VR game streaming」 |
| 6 | Memory: | |
| 7 | Operating System: | |
| 8 | Secure Boot: | 说明「When Secure Boot is disabled in BIOS, it can solve compatibility with some games」 |
| 9 | PC Ethernet: | `MultiBinding`（3 路） |
| 10 | Auto Adjust Bitrate: | `MultiBinding` |
| 11 | Remote Routing: | `MultiBinding` |
| 12 | Router Settings: | `Hyperlink`（`Foreground="#2e79bd"`） |
| 13 | Website: | `Hyperlink "https://www.vrdesktop.net" → www.vrdesktop.net` |
| 14 | Discord: | `Hyperlink "http://discord.vrdesktop.net" → discord.vrdesktop.net` |
| 16 | 按钮行 | `Check for interfering apps...` / `Check for updates...` / `Show logs` |

标签列 `TextBlock Style="{StaticResource ...}"`（`AboutTextBlock`：`Foreground="#808080" Margin="3" HorizontalAlignment=Right`），值列 `Margin=3`。

**ABOUT 页对 VDHelper 最有参考价值**：`PC Ethernet` / `Remote Routing` / `Router Settings` / `Secure Boot` / `Latency` / `Hardware Encoder` 全是网络与串流链路状态 —— **这就是 VDHelper「检测项」的天然列表，且官方已经用同一套视觉语言表达过了。**

按钮行（`:1329,1337,1344`）：`Check for interfering apps...` / `Check for updates...` / `Show logs`，`FontSize=14`，`VerticalAlignment=Top`。

---

## 4. 视觉语言规格（复刻用）

### 4.1 调色板全表（`Themes/Metrodark/Theme.Colors.xaml`）

| Key | 值 | 用途（据代码引用推定） |
|---|---|---|
| `Color_000` | `#FF282828` | 深底 |
| `Color_001` | `#FFFFFFFF` | 白 |
| `Color_002` | `#FFBABABA` | **次级标签色**（MEDIA/ACCOUNTS 的 Label） |
| `Color_003` | `#FF858585` | 灰 |
| `Color_004` | `#FF747474` | CheckBox hover 边框 / indeterminate |
| `Color_005` | `#FF565656` | 中灰 |
| `Color_006` | `#AA444444` | **全局 BorderBrush**（Generic.xaml:19） |
| `Color_007` | `#FF444444` | ComboBox item hover |
| `Color_008` | `#FF292929` | |
| `Color_009` | `#FF000000` | CheckBox 背景 |
| `Color_010`–`016` | `#E5FFFFFF` … `#00FFFFFF` | 白色 8 档透明度 |
| `Color_017`–`021` | `#99000000` … `#00000000` | 黑色 8 档透明度 |
| `Color_022` | `#BFBABABA` | |
| `Color_023` | `#FF0086AF` | |
| `Color_024` | `#FF2E79BD` | **强调蓝**：LinkBrush / CheckBox 勾选 / 按下边框 |
| `Color_025`–`027` | `#FF80D5EF` / `#FFB2E1EF` / `#352E79BD` | 蓝系辅助 |
| `Color_028`–`030` | `#FFD0284C` / `#FFF55E7F` / `#FFFFCAD5` | 错误红系 |
| `Color_035` | `#FF006481` | |
| `Color_037`–`039` | `#FF8A9B0F` / `#FF3E4700` | 橄榄 |
| `Color_040`–`042` | `#FFF14D0F` / `#FF8D2E00` | 橙红 |
| `Color_043`–`045` | `#FF81106B` / `#FF410135` | 品红 |
| `Color_046`–`048` | `#FFFCA910` / `#FF8D4902` | 琥珀 |
| `Color_049`–`051` | `#FF037A54` / `#FF003F2A` | 绿 |
| `Color_052`–`054` | `#FF154D85` / `#FF02284D` | 深蓝 |
| `Color_055`–`057` | `#FF543511` / `#FF211303` | 棕 |
| `Color_058`–`060` | `#FF89806D` / `#FF393225` | 卡其 |
| `Color_061`–`063` | `#FF58458B` / `#FF211347` / `#7FB9B9B9` | 紫 |
| `Color_064`–`068` | `#33565656` / `#7F3F3F3F` / `#FF686868` / `#8000AADE` / `#CC3F3F3F` | 半透明中性 |
| `Color_071`–`075` | `#FF0092BE` / `#FF00AADE` / `#FF2BB9E5` / `#FF55C8EB` / `#FF80D7F2` | **主强调青蓝**（`Brush01=#FF00AADE`） |

另有 `Brush01`–`Brush05`（`Theme.Colors.xaml:6-20`）：`#FF00AADE` / White / `#FFC6C6C6` / `#FF565656` / `#FF333333`。

### 4.2 三层背景（**最重要的一条视觉规则**）

| 层 | 色值 | 证据 |
|---|---|---|
| 标题栏 / 状态栏 | `#1D1D1D` | `MainWindow.xaml:272,1370` |
| 外层 Grid（内容底） | `#151515` | `MainWindow.xaml:308` |
| TabControl 导航条 / tab 内容 | `#101010` | `MainWindow.xaml:314` + 各 tab 内容根 |

`#1D1D1D`（29）> `#151515`（21）> `#101010`（16）。**官方靠这三档相差 5–8 的近黑灰做层次，不靠分割线。** VDHelper 直接照抄这三档。

### 4.3 字体规格（`Themes/Metrodark/Styles.Shared.xaml:12-17`）

```xml
<x:Key="FontFamily">Verdana</x:Key>
<x:Key="FontSize">14.667</x:Key>          <!-- 系统 DPI 下 ≈ 11pt -->
<x:Key="ForegroundBrush" Color="{StaticResource Color_002}" />   <!-- #BABABA -->
```

| 场景 | 字号 | 字重 | 证据 |
|---|---|---|---|
| 全局正文 | 14.667 | Normal | `Styles.Shared.xaml:14` |
| 隐式 Label / TextBlock | 13.333 | Normal | `Metrodark.Mscontrols.Core.Implicit.xaml:134-163` |
| Tab 标题 | **16** | **Bold** | `MainWindow.xaml:170-179` |
| 小节 Label（OPTIONS/ADVANCED/MEDIA） | **14** | **Bold** | `MainWindow.xaml:559` 等 |
| ComboBox item | 12 | Normal | `MainWindow.xaml:570` |
| 状态栏进度文字 | 12 | Normal | `MainWindow.xaml:1442` |
| 对话框正文 | **12** | Normal | `MessageBoxWindow.xaml:15` |
| Header 文本块 | 17 | Normal | `Generic.xaml:88` |

> Verdana 是**宽体无衬线**，中文字符会回退到系统默认（Microsoft YaHei UI）。VDHelper 若用中文，需显式设 `FontFamily="Verdana,Microsoft YaHei UI"` 或走 `Language="zh-CN"` 回退，否则中英混排基线会跳。这是从官方实测参数推出的**建议**，非官方既有配置。

### 4.4 控件高度 / 内边距总表

| 控件 | MinHeight | Padding | 证据 |
|---|---|---|---|
| Button | **30** | `10,0,10,2` | `Styles.Shared.xaml` ButtonStyle |
| ComboBox | **30** | `6,1,6,3` | `Styles.Wpf.xaml:1137-1150` |
| TextBox | 30 | — | `Styles.Wpf.xaml:1394` |
| ShortcutEditor | **30** | — | `Shortcuteditor.xaml:45-46` |
| TabItem | 48（首项 56） | 16 | `MainWindow.xaml:111-118` |
| ProgressBar | 6（状态栏用） | — | `MainWindow.xaml:1447` |
| 状态栏进度容器 | `Width=260` | `Margin="12 0"` | `MainWindow.xaml:1435,1438` |
| 消息框按钮 | `Width=80` | 间距 `Margin="10 0 0 0"` | `MessageBoxWindow.xaml:141-153` |

**30px 是官方的标准控件高度**（Button/ComboBox/TextBox/自定义框全部统一）。VDHelper 应固定用 30。

### 4.5 Button 视觉（`Styles.Shared.xaml:145-194`）

| 状态 | 背景 | 边框 |
|---|---|---|
| Normal | 垂直渐变 `#FF202020` → `#FF000000` | 渐变 `#AA444444`（上下同色，实为纯色感） |
| Hover | 渐变 `#FF202020` → `#FF000000`（同 Normal） | 渐变 `#FF747474` → `#FF575757` |
| Pressed | 纯色 `#FF151515` | 纯色 `#FF2E79BD`（**强调蓝**） |

**按下调蓝色边框**是官方唯一的强交互反馈，VDHelper 复刻时优先做这一条。

### 4.6 ImageButtonStyle（标题栏按钮，`Styles.Shared.xaml:1904-1955`）

- `Padding=0`、`Background="#00FFFFFF"`（透明）
- 模板：`Grid > Image`，`Source` 绑定 `Button.Content`，`Stretch="None"`
- **`Opacity="0.35"`（默认）→ `0.75`（悬停）** —— 图标平时很淡，悬停才亮
- 按下：`RenderTransform = TranslateTransform(X=1, Y=1)`（**1px 位移**，无变色）
- 附 `DropShadowEffect BlurRadius="2" ShadowDepth="0" Color="Black"`

复刻要点：**图标按钮 = 透明底 + 35% 不透明度 + 悬停提亮 + 按下位移 1px**，不要加背景色。

### 4.7 CheckBox 视觉（`Styles.Shared.xaml:264-305`）

| 状态 | 边框 | 勾选标记 |
|---|---|---|
| Normal | `Color_006` = `#AA444444` | `CheckBoxCheckBackgroundBrush` = `Color_024` = `#2E79BD` |
| Hover | `Color_004` = `#747474` | |
| Pressed | `Color_024` = `#2E79BD` | |
| Invalid | `Color_028/029` = `#D0284C` / `#F55E7F` | 红系 |

背景恒为 `Color_009` = `#000000`（全状态）。

ADVANCED 的 CheckBox 统一 `Margin="-5 0 0 0"`（`:843,871,902`），OPTIONS 的也是（`:577,582,587,650,655`）—— **负左边距把勾选框拉到文字基线对齐**，是个值得抄的细节。

### 4.8 ContextMenu（`Metrodark.Mscontrols.Core.Implicit.xaml:2028-2050`）

`Background=ContextMenuBackgroundBrush`、`BorderThickness=1`、`BorderBrush=ContextMenuBorderBrush`、`Padding=2`、`HasDropShadow=DynamicResource SystemParameters.DropShadowKey`。

ACCOUNTS 的「Add account」右键菜单（`MainWindow.xaml:400-425`）每项：`Header` 绑定 + `MenuItem.Icon` = 平台图标，`ItemContainerStyle` 覆盖。

### 4.9 链接色

`LinkBrush = Color_024 = #2E79BD`（`Generic.xaml:16`），ABOUT 页 `Hyperlink Foreground="#2e79bd"` 直写（`MainWindow.xaml:1288,1306,1320`）。

---

## 5. 资源字典加载链

```
App.xaml  (Application.Resources)
  ├─ pack://application:,,,/VirtualDesktop.Streamer;component/Themes/Generic.xaml   ← App.xaml:12
  │    └─ MergedDictionaries → MetroDark/MetroDark.MSControls.Core.Implicit.xaml   ← Generic.xaml:6
  │         ├─ Theme.Colors.xaml      (149 行，色板)
  │         ├─ Styles.Shared.xaml     (1962 行，brush + Button/CheckBox/ImageButton)
  │         ├─ Styles.Wpf.xaml        (1860 行，ScrollBar/ComboBox/TextBox/ListBox)
  │         └─ Styles.Xceed.xaml      (1453 行，Xceed 控件皮肤)
  ├─ /Themes/Settings.xaml   (14 行，4 个单例 SettingsBase)   ← App.xaml:14
  ├─ /Themes/Controls.xaml   (8 行，→ ShortcutEditor.xaml)    ← App.xaml:16
  └─ ObjectDataProvider x:Key="Resources" → CultureResources.GetResourceInstance  ← App.xaml:18-21
```

`Themes/Settings.xaml` 注册 4 个应用级单例（`:5-16`）：`StreamerSettings`、`DynamicSettings`、`UISettings`、`SharedStreamerSettings` —— XAML 里到处 `{StaticResource StreamerSettings}` 就是它们。**VDHelper 若要「像官方」，也应把 settings 做成 ResourceDictionary 里的单例**，而不是每个 View 各自 new。

`Generic.xaml` 额外定义（`:8-109`）：`NormalBrush`(White)、`SecondaryBrush`(Gray)、`LinkBrush`、`BorderBrush`、`LightBackgroundBrush`(#33777777)、`DropShadowText`(ShadowDepth=2)、`NormalLabel`、`ShadowLabel`、`LinkLabel`、`NormalTextBlock`、`ShadowTextBlock`、`HeaderTextBlock`(17px)、`HeaderBorder`(#50444444)。

---

## 6. 转换器清单（23 个）

**`VirtualDesktop.Streamer.Converters`（16 个，`VirtualDesktop/Streamer/Converters/`）：**

| 转换器 | 用途（据命名+用法） |
|---|---|
| `PlatformToImageSourceConverter` | 平台枚举 → 16×16 图标 |
| `PlatformAccountsConverter` | 平台 → 账号集合 |
| `LatencyConverter` | 延迟数值 → 文本 |
| `BooleanToEnabledConverter` / `BooleanToYesConverter` | 布尔 → 启用态 / Yes-No |
| `SymbolicLinkNameConverter` / `SymbolicLinkFinalPathConverter` | 符号链接名 / 终路径 |
| `DownloadStateToStringConverter` | 下载状态 → 进度文字 |
| `IPAddressToUriConverter` / `IPAddressToSecureUriConverter` | IP → http(s) URI |
| `PCEthernetConverter` | **PC 有线网状态** |
| `RoutingStatusConverter` | **远端路由状态** |
| `EnumToBindingActionConverter` | 枚举 → 绑定动作 |
| `FovStencilIsEnabledConverter` / `FovStencilIsCheckedConverter` | FOV 裁剪联动 |
| `XamlToTextBlockConverter` | XAML 片段 → TextBlock（AppsChecker 详情列） |

**`VirtualDesktop.UI.Converters`（8 个，`VirtualDesktop/UI/Converters/`）：**

| 转换器 | 用途 |
|---|---|
| `ReferenceToVisibilityConverter` / `InverseReferenceToVisibilityConverter` | 空引用 → 可见性 |
| `EnumToVisibilityConverter`（带 `ConverterParameter` 指定枚举值） | **官方显示/隐藏的主力机制** |
| `EnumToBooleanConverter` | 枚举 → bool（ProgressBar IsIndeterminate） |
| `InverseBooleanToVisibilityConverter` | 反转 |
| `EnumToDisplayNameConverter` | 枚举 → 显示名 |
| `MultiBooleanToVisibilityConverter` | 多 bool |
| `StringNotEmptyConverter` | 空串校验（AddAccount 输入框） |

MainWindow 本地资源还注册 4 个 **ObjectProvider**（枚举 → 下拉项，`:47-64`）：`VideoCodecProvider`、`AudioStreamingProvider`、`OpenXRRuntimeProvider`、`GamepadEmulationProvider`、`BindingActionProvider`、`PlatformProvider`。**这是「枚举下拉框」的标准做法**：`controls:EnumDataProvider EnumType="{x:Type ...}"` + `ComboBox ItemsSource="{StaticResource XxxProvider}"` + `ItemTemplate` 绑定 `Name`。

---

## 7. 图片 / 图标资源清单（28 个，实测尺寸）

来源：`VirtualDesktop.Streamer.g.resources`（397,067 字节，44 条目），已 dump 到 `reference/streamer_ui/res/`。

| 资源键 | 实测尺寸 | 用途 | 证据 |
|---|---|---|---|
| `resources/logoheader.png` | **300×48** | 标题栏 logo（`Stretch="None"`，故按原始尺寸显示） | `MainWindow.xaml:286` |
| `resources/minimize32.png` | 32×32 | 最小化按钮 | `MainWindow.xaml:296` |
| `resources/close32.png` | 32×32 | 关闭按钮 | `MainWindow.xaml:301` |
| `resources/clear20.png` | 16×16 | 行内「移除」按钮（配 `ImageButtonStyle`，按钮本身 20×20） | `MainWindow.xaml:227,1074` |
| `resources/info.png` | 16×16 | 黄色说明图标（ADVANCED / MEDIA 提示行） | `MainWindow.xaml:742,984` |
| `resources/error.png` | 16×16 | AppsChecker 错误态 | `AppsCheckerWindow.xaml:26` |
| `resources/warning.png` | 16×16 | AppsChecker 警告态 | `AppsCheckerWindow.xaml:23` |
| `resources/folder.png` | 16×16 | 视频文件夹行图标 | `MainWindow.xaml:1053` |
| `resources/youtube.png` | 16×16 | 状态栏「Paste URL」 | `MainWindow.xaml:1411` |
| `resources/cancel.png` | 16×16 | 状态栏「Cancel」 | `MainWindow.xaml:1426` |
| `resources/settings.png` | 16×16 | 设置入口图标 | g.resources |
| `resources/apple.png` | 16×16 | Apple Vision 平台图标 | g.resources |
| `resources/google.png` | 16×16 | Google Play 平台图标 | g.resources |
| `resources/steam.png` | 16×16 | Steam 平台图标 | g.resources |
| `resources/vive.png` | 16×16 | Vive 平台图标 | g.resources |
| `resources/oculusquest.png` | 16×16 | Quest 平台图标 | g.resources |
| `resources/pico.png` | 16×16 | PICO 平台图标 | g.resources |
| `resources/playfordream.png` | 16×16 | Play for Dream 平台图标 | g.resources |
| `resources/piracy.png` | 20×20 | 盗版警示（**唯一非 16/32 尺寸**） | g.resources |
| `resources/icons/information.png` | 32×32 | MessageBox 信息图标 | `MessageBoxWindow.xaml:27` |
| `resources/icons/question.png` | 32×32 | MessageBox 问号图标 | `MessageBoxWindow.xaml:31` |
| `resources/icons/warning.png` | 32×32 | MessageBox 警告图标 | `MessageBoxWindow.xaml:34` |
| `resources/icons/error.png` | 32×32 | MessageBox 错误图标 | `MessageBoxWindow.xaml:25` |
| `resources/setup.ico` | 48×48 | 应用图标 | g.resources |
| `resources/connectedtoserver.ico` | 64×64 | 托盘：已连服务器 | g.resources |
| `resources/connectedtopeer.ico` | 64×64 | 托盘：已连对端 | g.resources |
| `resources/disconnected.ico` | 64×64 | 托盘：未连接 | g.resources |

**尺寸规约**：UI 内联图标一律 **16×16**；窗口按钮 **32×32**；对话框图标 **32×32**；托盘 **64×64**；品牌 logo **300×48**。VDHelper 按这套尺寸做图标即可对齐。

---

## 8. 对话框清单

### 8.1 MessageBoxWindow（自制，非系统 MessageBox）

`MessageBoxWindow.xaml`：窗口 `SizeToContent="WidthAndHeight"`、`ResizeMode="NoResize"`、`ShowInTaskbar="False"`、`FontSize="12"`、`Background="#151515"`、`WindowStyle="None"`。

结构（`:37-157`）：

```
Border MinWidth=200 MaxWidth=500 Margin=1 BorderThickness=1 BorderBrush={BorderBrush}
 └ Grid (4 行: Auto / * / Auto / Auto)
    ├ 行0 标题栏 Grid Background="#00FFFFFF"
    │       TextBlock Margin="8 7" Text={Binding Title}
    │       Button PART_CloseButton Style=ImageButtonStyle Content=Close32.png Right
    ├ 行1 正文 Grid Margin="24 16 24 20" (列: Auto / *)
    │       Image 32×32（DataTrigger 按 MessageBoxImage 切 Source）
    │       TextBlock Grid.Column=1 Margin="12 0 0 0" TextWrapping=Wrap
    ├ 行2 Grid Margin="60 -8 24 0" → CheckBox "Don't ask me again" FontSize=12
    └ 行3 Border Background="#1D1D1D"
            StackPanel Orientation=Horizontal HorizontalAlignment=Right Margin=12
              Button PART_Button1 Width=80 (Collapsed)
              Button PART_Button2 Width=80 Margin="10 0 0 0" (Collapsed)
              Button PART_Button3 Width=80 Margin="10 0 0 0"
```

**按钮统一 `Width=80`、间距 10、右对齐、底部条同标题栏色 `#1D1D1D`。** 这是官方对话框的标准收尾，VDHelper 应照抄。

### 8.2 AddAccountWindow

`AddAccountWindow.xaml:11-25`：`Width=490 Height=190`、`WindowStartupLocation="CenterOwner"`、`ResizeMode="NoResize"`、`FontSize="12"`、`Background="#151515"`、`WindowStyle=None`、`AllowsTransparency=True`。

结构：标题 `TextBlock "Add Account" Margin="8 7"` + `btnClose`（Close32.png）；正文 `StackPanel Margin="24 10 24 16"`：平台图标 16×16 `Margin="4,0,0,0"` + `Label Bold 14` + `TextBox txtAccountID Height=254?? `（`Grid.Row=1`）；底部 `Border Background="#1D1D1D"` + `StackPanel Horizontal Right Margin=12` + `Button btnAdd "Add" Width=80`。

含 `AutoPopupErrorTemplate`（`:29-44`）：`Popup Bottom=True` → `Border Background="#FF4444" BorderBrush="#FFFFFFFF" BorderThickness=1 CornerRadius=6` + `TextBlock Foreground="#FFFFFFFF" FontWeight=SemiBold` —— **红色圆角 6 的输入错误气泡**。

### 8.3 AppsCheckerWindow（**与 VDHelper 功能最接近的官方窗口**）

`AppsCheckerWindow.xaml:12-21`：`Width=760 Height=460 MinWidth=600 MinHeight=300`、`FontFamily="Verdana"`、**注意此窗口没有 `WindowStyle="None"`，用系统原生标题栏**（与主窗口不同）。

结构：
- 列定义 **`32 / 180 / * / 62`**（`:33-39`）—— 图标 / 应用名 / 详情 / 勾选框
- 表头行：`Image`(Warning/Error 16×16) + `TextBlock "Application"`(宽 32 换行) + `ContentControl`(详情，宽 180 起) + `CheckBox`(62 列)
- 正文 `Border Margin="4 0 20 0 0 1"` + `ScrollViewer`
- 列标题：`"Application"` / `"Details"` / `"Don't warn me on launch"`（后者宽 62，`TextWrapping=Wrap`、`HorizontalAlignment=Right`）

**这是「列出问题项 + 逐条勾选忽略 + 可展开详情」的官方范式，VDHelper 的「检测结果列表」页应当照抄它的列结构。**

---

## 9. 交互：菜单 / 托盘 / 快捷键 / 模态

### 9.1 托盘（**WinForms，不是 WPF**）

`MainWindow.xaml.cs:341-395` `UpdateContextMenu()`：

```csharp
ContextMenuStrip strip = new ContextMenuStrip {
    Renderer  = new ToolStripProfessionalRenderer(new NotifyIconContextMenuColorTable()),
    BackColor = <theme>, ForeColor = <theme>
};
strip.Items.Add(new ToolStripMenuItem("Settings...",   Images.<Settings>, OnSettingsClick));
strip.Items.Add(new ToolStripSeparator());
strip.Items.Add(new ToolStripMenuItem("Launch Game...", null, OnLaunchGameClick) { Enabled = IsConnected });
strip.Items.Add(new ToolStripSeparator());
strip.Items.Add(new ToolStripMenuItem("Disconnect", null, OnDisconnectClick)      { Enabled = IsConnected });
strip.Items.Add(new ToolStripSeparator());
strip.Items.Add(new ToolStripMenuItem("Exit", null, OnExitClick));
```

托盘图标三态：`Disconnected.ico` / `ConnectedToServer.ico` / `ConnectedToPeer.ico`（`Resources` 内 64×64）。
双击托盘 → 切换 `WindowState.Normal` / `Minimized`（`:208,274,473-481`）。
`NotifyIconContextMenuColorTable : ProfessionalColorTable`（`:1700+`）覆盖 `ImageMarginGradientBegin/Middle/End` 以套主题色。

### 9.2 快捷键

- **`Ctrl+V` → `PasteUrlCommand`**（`MainWindow.xaml:254-259` `KeyBinding`，`:209-210` `RoutedUICommand x:Key="PasteUrlCommand"`，`:251` `CommandBinding`）。语义 = 从剪贴板 URL 下载 YouTube 视频。
- 其余全局热键走 `SettingsBase<BindingSettings>.Default.HotKeysEnabled`（`MainWindow.xaml.cs:647`），随连接状态启用/禁用。**具体键位表未在 XAML 中**，`[未验证]` —— 需要在运行时打开 BINDINGS 页读取。
- 「Start with Windows」/「Start minimized in tray」= 托盘常驻的两个开关（OPTIONS 页 `:681,684`）。

### 9.3 模态对话框

| 对话框 | 触发 | 形态 |
|---|---|---|
| AddAccountWindow | ACCOUNTS → `ToggleButton`「Add account」→ ContextMenu 选平台 | `CenterOwner`, 490×190 |
| AppsCheckerWindow | ABOUT → 「Check for interfering apps...」 | `CenterScreen`, 760×460 |
| MessageBoxWindow | 全局错误/确认/警告（含「Don't ask me again」） | `CenterScreen`, SizeToContent |

---

## 10. 给 VDHelper 的落地建议

1. **视觉三件套照抄**：`#1D1D1D` / `#151515` / `#101010` 三层底、Verdana、控制高 30、Button 按下蓝边 `#2E79BD`、图标按钮 35%→75% 不透明度。
2. **窗口自绘**：`WindowStyle=None` + `AllowsTransparency=True` + `WindowChrome(CaptionHeight=0, CornerRadius=0, GlassFrameThickness=0, ResizeBorderThickness=8)`，上下 Border 画 8px 圆角。
3. **导航**：`TabControl TabStripPlacement="Left"` + 160×48 TabItem + Bold 16 标题 + 灰/白两态。这比传统「左侧导航栏」更省横向空间，且与官方完全一致。
4. **ABOUT 页就是检测页**：把官方 15 行「标签-值」表当模板，替换成 VDHelper 的检测项。
5. **检测结果列表**照抄 AppsCheckerWindow 的 `32/180/*/62` 四列 + 逐条忽略勾选。
6. **对话框**照抄 MessageBoxWindow：`MinWidth=200 MaxWidth=500`、80px 按钮、右对齐、`#1D1D1D` 底条。
7. **中文排版**：Verdana 不含中文字形，需 `FontFamily="Verdana,Microsoft YaHei UI"` 或依赖 `zh-CN` 回退，并实测控件宽度（官方 CheckBox 副文本有固定宽度假设）。
8. **Xceed 按需引入**：官方只用 `WatermarkTextBox`（输入框水印）和 `ColorPicker`（取色）。VDHelper 若需要数字微调，官方**没有**先例，`NumericUpDown` 需自行配主题。

---

## 11. 证据与产物清单

**只读分析副本**（均在 `D:/Project/VirtualDesktopHelper/reference/streamer_ui/`）：

| 产物 | 说明 |
|---|---|
| `streamer.g.resources` | 从 exe dump 的 397,067 字节 `.resources` 容器 |
| `res/` | 44 个条目逐一 dump（`#` 已替换路径分隔符）+ `_index.tsv` |
| `res/_index.tsv` | 键名 / 类型 / 文件名 索引 |

**关键源码位置**（Owner 的树，只读）：

| 内容 | 路径:行 |
|---|---|
| 主窗口全部布局 | `VirtualDesktop/Streamer/MainWindow.xaml:1-1460` |
| 窗口属性 | `MainWindow.xaml:2-30` |
| WindowChrome | `MainWindow.xaml:234-248` |
| 三行骨架 | `MainWindow.xaml:260-268` |
| 标题栏 | `MainWindow.xaml:270-305` |
| TabControl + TabItem 样式 | `MainWindow.xaml:97-169, 306-315` |
| 状态栏 | `MainWindow.xaml:1368-1458` |
| 色板 | `Themes/Metrodark/Theme.Colors.xaml:1-149` |
| 字体常量 | `Themes/Metrodark/Styles.Shared.xaml:12-17` |
| Button 渐变 | `Themes/Metrodark/Styles.Shared.xaml:145-194` |
| CheckBox 色 | `Themes/Metrodark/Styles.Shared.xaml:264-305` |
| ImageButtonStyle | `Themes/Metrodark/Styles.Shared.xaml:1904-1955` |
| ComboBox 样式 | `Themes/Metrodark/Styles.Wpf.xaml:1134-1200` |
| 隐式 Label/TextBlock | `Themes/Metrodark/Metrodark.Mscontrols.Core.Implicit.xaml:134-163` |
| 资源字典链 | `VirtualDesktop/Streamer/App.xaml:8-23` |
| Generic.xaml 基样式 | `Themes/Generic.xaml:1-110` |
| 单例注册 | `Themes/Settings.xaml:5-16` |
| ShortcutEditor | `Themes/Shortcuteditor.xaml:1-168` |
| 消息框 | `VirtualDesktop/UI/MessageBoxWindow.xaml:1-158` |
| 加账号 | `VirtualDesktop/Streamer/AddAccountWindow.xaml:1-133` |
| 应用检测 | `VirtualDesktop/Streamer/AppsCheckerWindow.xaml:1-154` |
| 托盘菜单 | `VirtualDesktop/Streamer/MainWindow.xaml.cs:341-395` |
| 托盘配色 | `MainWindow.xaml.cs:1700+` |
| 旧 VDH 六页 | `reference/legacy_vdh/VDH.cs:227-232, 454-459` |

**未验证项**：

- `[未验证]` **BINDINGS 页的具体键位表**（哪些动作绑哪个组合键）—— 在 `SettingsBase<BindingSettings>` 里，非 XAML，需运行时或读 `BindingShortcut.cs` 才能确证。
- `[未验证]` **各控件模板的像素级细节**（如 CheckBox 方框边长、Slider 轨道粗细）—— 反编译 XAML 丢掉了 `Thickness` 结构体的内部记号展开，需 BAML 或运行时确认；本规格只覆盖 Setter 层可见数值。
- `[未验证]` **MetroDark 各隐式样式的 BasedOn 继承链末端** —— `Styles.Shared/Wpf/Xceed` 之间的交叉引用在反编译 XAML 中保留，但部分 `BasedOn="{StaticResource {x:Type ...}}"` 会成环（反编译已知问题，见 `MainWindow.xaml:1` 注释），实际运行以 BAML 为准。
- `[未验证]` **未在真实显示器上启动 exe 目检** —— 本轮全程只读静态分析，未启动任何 Streamer 进程（遵守 Non-Conflict）。

**方法说明**：BAML 逆向在本任务中**不需要**。反编译源码树已含全部 17 个 `.xaml`（10328 行）的可读 XAML，直接读取即可。`R1_desktop_strings.md:16,137` 提出的「dnSpyEx 反解 BAML → 改字符串 → 回写」路线，对**读规格**而言是多余的；它只在**改二进制**时才必要。