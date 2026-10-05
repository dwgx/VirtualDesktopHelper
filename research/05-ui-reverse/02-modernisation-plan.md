# 02 — 界面现代化计划（对照官方 `VirtualDesktop.Streamer.exe`）

> 只读分析 / 未修改任何产品代码 / 2026-10-06
> 官方侧证据树（下称 `S/`）：`F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer\VirtualDesktop.Streamer\`
> 我方证据（下称 `R/`）：`D:/Project/VirtualDesktopHelper/`
> 前置阅读：`research/05-ui-reverse/01-ui-spec.md`（第一轮逆向规格）、`docs/product-spec.md`、`notes/decisions.md`
>
> **本文与 01 的关系**：01 给的是「官方长什么样」的静态规格。本文补三件 01 没做的事 ——
> (a) 01 只测了官方，**没有对照我们今天实际渲染出来的界面**；(b) 01 没有给语义色的可达性结论；
> (c) 01 没有「不许改」的清单。凡与 01 不一致处，本文逐条标注**同意 / 修正 / 不同意**。

---

## 1. 官方客户端实际长什么样（控件词汇表，不是形容词）

证据树根：`S/`。17 个 `.xaml`，主窗口 `S/VirtualDesktop/Streamer/MainWindow.xaml:1-1460`。

### 1.1 控件词汇表（按出现次数实测）

`MainWindow.xaml` 的元素计数（`grep -oE "<[A-Za-z][A-Za-z0-9_.:]*" | sort | uniq -c | sort -rn`）：

| 控件 | 次数 | 官方用它做什么 |
| --- | --- | --- |
| `TextBlock` | 63 | **主力**。几乎所有标签/值/说明都是 TextBlock，不是 Label |
| `StackPanel` | 25 | 纵向分组；横向时显式 `Orientation="Horizontal"` |
| `Button` | 18 | 全部走隐式 `ButtonStyle`（`S/Themes/Metrodark/Styles.Shared.xaml:1759-1899`）|
| `CheckBox` | 17 | 全部走 `Margin="-5 0 0 0"`（如 `S/VirtualDesktop/Streamer/MainWindow.xaml:577-581`）|
| `Grid` | 15 | 全部布局骨架，`RowDefinition` 36 个 / `ColumnDefinition` 20 个 |
| `Label` | 14 | 只用于「小节标题」，一律 `FontWeight="Bold" FontSize="14"`（`S/VirtualDesktop/Streamer/MainWindow.xaml:559-561`）|
| `Image` | 12 | 全部 `Stretch="None"`，尺寸来自资源文件本身（16×16 或 32×32）|
| `TabItem` | 6 | ACCOUNTS/BINDINGS/OPTIONS/ADVANCED/MEDIA/ABOUT |
| `ItemsControl` | 4 | 所有列表（账号、快捷键、文件夹、AppsChecker 行）—— **官方从不用 ListView/ListBox 做数据展示** |
| `Slider` | 5 | ADVANCED 页，恒定 `Width="160"`（`S/VirtualDesktop/Streamer/MainWindow.xaml:767-773`）|
| `ComboBox` | 4 | OPTIONS 页，`Width="180"` 或 `210` |
| `ScrollViewer` | 3 | ACCOUNTS、BINDINGS、MEDIA |
| `Hyperlink` | 3 | ABOUT 页链接，`Foreground="#2e79bd"` 直写（`S/VirtualDesktop/Streamer/MainWindow.xaml:1320-1322`）|
| `Expander` / `DataGrid` / `TreeView` | **0** | 官方主窗口零使用 |

**关键事实**：`grep -rn "ListView\|DataGrid\|TreeView" S/**/*.xaml` 只命中主题里的样式定义
（`S/Themes/Metrodark/Metrodark.Mscontrols.Core.Implicit.xaml:2259-2265`），**业务 XAML 里一次都没有**。
即官方展示数据一律是 `ScrollViewer > ItemsControl > DataTemplate`，不是虚拟化列表。
01 的说法与此一致，本文**同意**。

### 1.2 窗口结构（三行 Grid + 自绘标题栏）

| 事实 | 证据 |
| --- | --- |
| `WindowStyle="None"` `AllowsTransparency="True"` `Background="#00FFFFFF"` | `S/VirtualDesktop/Streamer/MainWindow.xaml:19-21` |
| `FontFamily="Verdana"` `Foreground="#F0F0F0"` | 同上 `:22, :24` |
| `Width/MinWidth="700"` `Height/MinHeight="516"` | 同上 `:27-30` |
| `UseLayoutRounding="True"` `SnapsToDevicePixels="True"` | 同上 `:17-18` |
| `WindowChrome CaptionHeight=0 CornerRadius=0 GlassFrameThickness=0 ResizeBorderThickness=8` | 同上 `:234-248` |
| 三行 `48 / * / 48` | 同上 `:261-268` |
| 标题栏 `Background="#1D1D1D" CornerRadius="8 8 0 0"` | 同上 `:270-273` |
| 状态栏 `Background="#1D1D1D" CornerRadius="0 0 8 8"` | 同上 `:1368-1371` |
| 内容区外层 `#151515`，`TabControl` 自身 `#101010` | 同上 `:306-315` |

`CaptionHeight=0` + `GlassFrameThickness=0` 意味着**完全不用系统标题栏**；圆角是内容里的两个 Border 画的。
`ResizeBorderThickness="8"` 是 8px 无边框拖拽区。

### 1.3 导航：左侧 TabControl，不是导航栏

- `TabControl TabStripPlacement="Left"`（`S/VirtualDesktop/Streamer/MainWindow.xaml:310-315`）。
- 隐式 `TabItem` 被**整段覆盖**（`:97-169`）：`Foreground="#808080"` `Background="#151515"`
  `FontSize="12"` `Width="160"` `Height="48"` `Padding="16"` `HorizontalContentAlignment="Stretch"`。
- 模板 = `Grid > Border > ContentPresenter(ContentSource="Header")`（`:126-166`）。
- 触发器只有两条（`:144-164`）：`IsMouseOver && !IsSelected → #FFFFFFFF`；`IsSelected → #FFFFFFFF`。
  **没有背景色变化，没有指示条，没有 Focusable 反馈。**
- Tab 文字另有 `TabTextBlock`：`FontWeight="Bold" FontSize="16"`（`:170-179`）。
- 首个 TabItem 单独 `Height="56"`、`Padding="16 16 0 0"`（`:316-319`）。

### 1.4 样式构造（真正的词汇表）

| 构造 | 证据 |
| --- | --- |
| **隐式样式**（无 `x:Key`，`TargetType="{x:Type X}"`）主导：`TabItem`/`TextBlock`/`Label`/`ToolTip`/`ListView`/`ListViewItem`/`ScrollBar`/`Expander` | `S/Themes/Metrodark/Metrodark.Mscontrols.Core.Implicit.xaml:133-164, 2259-2265, 2299-2301, 2303` |
| **命名样式**：`ButtonStyle` `ComboBoxStyle` `TextBoxStyle` `ScrollBarStyle` `ScrollViewerStyle` `ToolTipStyle` `ExpanderStyle` `ExpanderDownHeaderStyle` `ImageButtonStyle` | `Styles.Shared.xaml:1759, 1904`；`Styles.Wpf.xaml:1134, 1393, 312, 470`；`Metrodark.Mscontrols.Core.Implicit.xaml:1182, 1612, 1553` |
| **`ControlTemplate` 全量重写**：几乎每个控件都换掉了默认模板 | 同上行号 |
| **`VisualStateManager`**：`Button` 的 `CommonStates` + `FocusStates`（`Styles.Shared.xaml:1803-1873`）；`ScrollBar` 的 `CommonStates` + `GeneratedDuration="0:0:0.3"`（`Styles.Wpf.xaml:341-364`）|
| **`Storyboard` 驱动状态**：Hover/Pressed/Disabled 都用 `ObjectAnimationUsingKeyFrames` 改 Background/BorderBrush/Margin | `Styles.Shared.xaml:1810-1865` |
| **`Trigger` vs `DataTrigger` 分工**：视觉态用 `Trigger`，数据驱动（如 AppsChecker 的 Warn/Error 图标）用 `DataTrigger` | `S/VirtualDesktop/Streamer/AppsCheckerWindow.xaml:57-65` |
| **`MultiTrigger` / `MultiBinding`**：TabItem 悬停用 `MultiTrigger`（`MainWindow.xaml:145-157`）；ADVANCED 的 CheckBox 用 `MultiBinding` |
| **`FocusVisualStyle="{x:Null}"` 显式关掉焦点视觉**（TextBox / PasswordBox）| `Styles.Wpf.xaml:923-925`；`Metrodark.Mscontrols.Core.Implicit.xaml:1095-1097` |
| `FocusStates` 组**存在但两个 VisualState 都是空的** | `Styles.Shared.xaml:1867-1873` |
| **`AutomationProperties.AutomationId`** 只用在 ScrollBar 的 PART 上（10 处），**没有任何 `AutomationProperties.Name`** | `Styles.Wpf.xaml:525, 537, 592, 605, 646, 701, 715, 771, 783` |
| **`KeyboardNavigation.TabNavigation="None"`** 只在 PasswordBox 上 | `Metrodark.Mscontrols.Core.Implicit.xaml:1089-1091` |
| 唯一的键盘绑定：`Ctrl+V` → `PasteUrlCommand` | `MainWindow.xaml:209-210, 249-259` |
| 唯一的 `Tooltip` 样式：`ToolTipStyle`，`Padding="10,7"` + `SystemDropShadowChrome` | `Metrodark.Mscontrols.Core.Implicit.xaml:1181-1245` |

### 1.5 控件度量（全部实测行号）

| 控件 | MinHeight | 其他 | 证据 |
| --- | --- | --- | --- |
| Button | **30** | `MinWidth="30"` `Padding="10,0,10,2"` `CornerRadius="3"` `DropShadowEffect BlurRadius=2 ShadowDepth=1 Opacity=0.4` | `Styles.Shared.xaml:1772-1796` |
| TextBox | **30** | `Padding="6,3"` | `Styles.Wpf.xaml:1414-1419` |
| ComboBox | **30** | `Padding="6,1,6,3"` | `Styles.Wpf.xaml:1137-1150` |
| ScrollBar | — | **`Width="7"` `MinWidth="7"` `BorderThickness="0"`**，thumb `Width="7"` | `Styles.Wpf.xaml:321-332, 384-390` |
| Expander | — | `CornerRadius="3"` `BorderThickness="1"` `Padding="2"`；header 是 `ToggleButton` | `Metrodark.Mscontrols.Core.Implicit.xaml:1611-1679` |
| Slider thumb | — | `Ellipse 16×16`，聚焦时叠一个 `Ellipse StrokeThickness="2" Opacity=0` | 同上 `:212-223` |
| TabItem | 48（首项 56） | `Width="160"` `Padding="16"` | `MainWindow.xaml:110-121, 316-319` |
| 对话框按钮 | — | `Width="80"`，间距 `Margin="10 0 0 0"` | `S/VirtualDesktop/UI/MessageBoxWindow.xaml` 按钮行 |

### 1.6 排版层级（字号）

| 场景 | 字号 | 字重 | 证据 |
| --- | --- | --- | --- |
| 全局资源 `FontSize` | **14.667** | Normal | `Styles.Shared.xaml:13-14` |
| 全局资源 `FontFamily` | **Verdana** | — | 同上 `:11-12` |
| 隐式 `TextBlock` / `Label` | 13.333 | Normal | `Metrodark.Mscontrols.Core.Implicit.xaml:139-141, 151-154` |
| Tab 标题 | 16 | **Bold** | `MainWindow.xaml:170-179` |
| 小节 Label | 14 | **Bold** | `MainWindow.xaml:559-561, 593-596, 762-764` |
| ComboBox item | 12 | Normal | `MainWindow.xaml:570-573` |
| 对话框正文 | 12 | Normal | `MessageBoxWindow.xaml:15`、`AddAccountWindow.xaml:16` |
| ABOUT 按钮 | 12 | Normal | `MainWindow.xaml:1333, 1342, 1349` |
| `HeaderTextBlock`（主题内备选）| 17 | Normal + 阴影 | `S/Themes/Generic.xaml:83-96` |

**结论：官方只有 6 档字号**（12 / 13.333 / 14 / 14.667 / 16 / 17），且只有两档字重（Normal / Bold）。
这不是「风格」，是可核对的数字集合。

### 1.7 调色板：官方**没有语义色**

`S/Themes/Metrodark/Theme.Colors.xaml:1-149` 定义了 76 个色键，但**实际被引用的只有 12 个**
（`grep -rc "StaticResource Color_0NN}" --include=*.xaml` 逐键计数）：

| 色键 | 值 | 被引用次数 | 用途 |
| --- | --- | --- | --- |
| `Color_024` | `#FF2E79BD` | **29** | 唯一强调色：按下边框、CheckBox 勾选、链接 |
| `Color_009` | `#FF000000` | 23 | 所有控件背景 |
| `Color_006` | `#AA444444` | 18 | 全局边框 |
| `Color_004` | `#FF747474` | 10 | 悬停边框 |
| `Color_007` | `#FF444444` | 6 | 悬停背景 |
| `Color_003` | `#FF858585` | 5 | 灰 |
| `Color_005` | `#FF565656` | 4 | 中灰 |
| `Color_002` | `#FFBABABA` | 3 | 正文前景 |
| `Color_022` `Color_023` `Color_025` | | 1/2/1 | Caret / 链接辅助 |
| `Color_028` `Color_029` `Color_030` | `#FFD0284C` `#FFF55E7F` `#FFFFCAD5` | 4/3/1 | **只用于 CheckBox 的 Invalid 态**（`Styles.Shared.xaml:275-298`）|
| `Color_049` `#FF037A54`（绿）| — | **0** | 定义了但没人用 |
| `Color_046` `#FFFCA910`（琥珀）| — | **0** | 定义了但没人用 |
| `Color_043` `#FF81106B`（品红）| — | **0** | 定义了但没人用 |
| `Color_000` `#FF282828` | — | **0** | 定义了但没人用 |

**这条要单独强调，它推翻了一个很自然的假设**：
官方那套 MahApps 血统的色板里有绿、有琥珀、有红，但**业务 UI 一个都没用**。
官方的「问题列表」（AppsCheckerWindow）用的是**图标**区分等级，不是颜色：
`Warning.png` / `Error.png` 两张 16×16 位图 + `DataTrigger IsWarning` 切换
（`S/VirtualDesktop/Streamer/AppsCheckerWindow.xaml:23-28, 47-67`）；
ABOUT 页的「潜在问题」按钮用 `Piracy.png`（`MainWindow.xaml:1360-1362`）。

> **对 01 的修正**：01 §4.1 把 `Color_049`「绿」/ `Color_046`「琥珀」列进调色板全表并写「用途（据代码引用推定）」。
> 01 自己写了「据推定」，但没去数引用。实测引用数为 0，所以**不能把它们当作官方的语义色先例**。
> 我们要做 Pass/Warn/Block 三色，官方**没有**可抄的先例 —— 这是本文必须自造的部分。

### 1.8 「标签 + 副文本」范式

官方表达「一个开关带解释」有两种做法，都值得抄：

1. `ToolTip` 里用 `&#xA;`（换行）分段（`MainWindow.xaml:580, 585-586, 590-591`）——
   例：`Improves color accuracy for desktop and games (recommended)` + 换行 + `Only supported with H.264 and HEVC 10-bit...`。
2. 可见的灰字说明行，`Foreground="#808080"`（`MainWindow.xaml:748, 755, 822`）。

### 1.9 密度

官方主窗口 700×516，6 个 tab，每个 tab 的内容根 Grid 统一 `Margin="24 8 8 8"`（MEDIA 是 `24 8 0 8`），
列宽多为固定值（ABOUT 是 `Auto / *`，OPTIONS 是 `246 / *`，ADVANCED 是 `236 / *`，AppsChecker 是 `32 / 180 / * / 62`）。
**这是一个「一屏能看完」的密度**，不是「滚动信息流」的密度。

---

## 2. 我们今天用什么（对照渲染结果，不只看源码）

### 2.1 文件清单与规模

| 文件 | 行数 | 角色 |
| --- | --- | --- |
| `R/src/VdHelper/App.xaml` | 142 | **唯一的资源字典**：全部颜色/字体/三个 ControlTemplate 都在这里 |
| `R/src/VdHelper/Views/ShellWindow.xaml` | 63 | 三行骨架 + 左侧 TabControl + 标题/状态栏 |
| `R/src/VdHelper/Views/HealthView.xaml` | 192 | 接下来做什么 + 症状芯片 + 深度探测 + 34 行 ListView(Expander) |
| `R/src/VdHelper/Views/ParametersView.xaml` | 66 | 四列 ListView（Key / Current / Effect / 状态+切换）|
| `R/src/VdHelper/Views/HeadsetView.xaml` | 111 | adb 摘要 + IP 输入 + 检查卡 + Facts ListView |
| `R/src/VdHelper/StatusConverters.cs` | 52 | `StatusToBrushConverter` / `StatusToTextConverter` / `StringToVisibilityConverter` |
| `R/src/VdHelper/Views/ShellWindow.xaml.cs` | 116 | 拖拽 / 最小化 / 关闭 / 总判定 |

`R/src/VdHelper/Resources/` 下**只有 `parameters.json`**，没有 XAML、没有图片（`ls` 实测）。
**结论：我们没有任何图标资源。** 官方的 28 张图标（`01-ui-spec.md` §7）我们一张都没有。

### 2.2 我们用了哪些控件

各视图元素计数（`grep -oE "<[A-Za-z][A-Za-z0-9_.:]*"`，按文件）：

| 控件 | HealthView | ParametersView | HeadsetView | ShellWindow |
| --- | --- | --- | --- | --- |
| `TextBlock` | 20 | 8 | 12 | 3 |
| `ListView` | 1 | 1 | 1 | 0 |
| `ItemsControl` | 3 | 0 | 2 | 0 |
| `StackPanel` | 7 | 3 | 4 | 1 |
| `Button` | 2 | 1 | 2 | 2 |
| `Border` | 5 | 0 | 1 | 2 |
| `Expander` | 1（每个数据行一个）| 0 | 0 | 0 |
| `TextBox` | 0 | 0 | 1 | 0 |
| `CheckBox` / `ComboBox` / `Slider` / `RadioButton` | 0 | 0 | 0 | 0 |
| `ScrollBar` / `ContextMenu` / `ToolTip` / `ToggleButton` / `ProgressBar` | **0** | 0 | 0 | 0 |

`App.xaml` 里定义了 **7 个 Style、11 个 SolidColorBrush、1 个 FontFamily、1 个 Double**、**3 个 ControlTemplate**（Button / TabItem / TabControl）。
转换器只有 4 个（`App.xaml:12-15`，其中 `BooleanToVisibilityConverter` 是 WPF 自带的）。

### 2.3 样式做法

我们没有资源字典分层，全部塞在一个 `Application.Resources` 里（`R/src/VdHelper/App.xaml:11-142`）：

  `MutedBrush #9A9A9A` / `AccentBrush #2E79BD` / `HoverBrush #262626` / `LineBrush #2E2E2E` /
  `PassBrush #5BC85B` / `WarnBrush #E0A030` / `BlockBrush #E05B5B`（`App.xaml:17-27`）。

**三个必须点名的事实**：

1. **`PassBrush` / `WarnBrush` / `BlockBrush` 三个键定义在 `App.xaml:25-27`，但引用次数是 0**
   （`grep -rc "StaticResource PassBrush}" R/src/VdHelper` = 0）。真正生效的是
   `StatusConverters.cs:14-20` 里**另外一套**硬编码色值。两套并存、其中一套是死的。
2. **`LineBrush`（`#2E2E2E`）被引用 7 次，是全 App 引用最多的色键**，而官方的全局边框是
   `Color_006 = #AA444444`（`S/Themes/Generic.xaml:17-19`）。我们的边框比官方更暗。
3. 视图里还有 **7 个绕过 token 体系的裸十六进制值**，硬编码在 XAML 里
   （`grep -oE "#[0-9A-Fa-f]{6}" R/src/VdHelper/Views/*.xaml` 计数）：
   `#161616`×5、`#9AA0A6`×2、`#C9D0DE`×1、`#6B7385`×1、`#1E2A38`×1、`#1E1E1E`×1、`#141414`×1。
   其中 `#C9D0DE`（蓝灰）与 `#9AA0A6`（中性灰）**是两套不同的灰**，同一个 App 里混用。

### 2.4 密度（实测渲染，不是估算）

用 `R/tools/capture-window.ps1` 留下的实拍图（`R/_shot3.png` 本机体检、`R/_shot4.png` 参数页、`R/_shot7.png` 头显页）
逐像素测量（Pillow 12.3.0，`im.getpixel`）：

| 量 | 实测 | 官方对照 |
| --- | --- | --- |
| 窗口尺寸 | **980×680** | 700×516 |
| 标题栏高 | 48（y=0..47，背景 `#1D1D1D`）| 48 ✅ |
| 状态栏高 | 48（y=632..679，背景 `#1D1D1D`）| 48 ✅ |
| 左侧导航列宽 | **160**（x=0..159）| 160 ✅ |
| 导航项高 | **48**（y=56..103 选中态背景 `#262626`）| 48 ✅ |
| 状态徽标底 | `#1E1E1E`，`CornerRadius=2`，`Padding="6,1"`（`HealthView.xaml:131-132`）| 官方无对应物 |
| 徽标文字色（实测像素）| 通过 `(46,125,50)`、警告 `(178,106,0)`、阻断 `(198,40,40)` | 官方无对应物 |
| Expander 圆点 | **直径 16px 的纯白 `#FFFFFF` 圆 + 白色 v 形**（x=182..197, y=94..109）| 官方 Expander 是 `CornerRadius=3` 的方框，无圆点 |
| 滚动条 | **宽 17px、纯 `#F0F0F0`（240,240,240）**（x=944..960, y=300 全列同色）| 官方 `Width="7"` 暗色 |
| 窗口圆角 | **四角 `(0,0)` 实测 `#1D1D1D`，无圆角** | 官方 `CornerRadius="8 8 0 0"` / `"0 0 8 8"` |
| 每屏可见检测行 | 体检页约 **17 行**（34 行需滚动）| 官方 AppsChecker 一屏约 12 行 |

**这一节是全文最重要的一节。** 上面标 ✅ 的四条说明外壳抄对了；
但**渲染出来还有四个东西是 WPF 默认主题漏出来的**：

| 漏出的默认件 | 证据 | 后果 |
| --- | --- | --- |
| **Expander 圆点**（白色圆 + 白 v）| `_shot3.png` x=182..197 实测纯白圆；`grep -c Expander R/src/VdHelper/App.xaml` = 0（**我们没给 Expander 写任何样式**）| 深色主题上的**纯白圆点**，36 行里每行一个，最扎眼的不一致 |
| **滚动条 17px 纯白** | `_shot4.png` x=944..960 实测 `#F0F0F0`；`grep -c ScrollBar R/src/VdHelper/App.xaml` = 0 | 深色界面上的**一条白色竖条** |
| **TextBox 默认 26px** | `_shot10.png` 实测 border y=102..127 共 26px；`grep -c TextBox R/src/VdHelper/App.xaml` = 0 | 与官方 30px 不一致（差 4px，肉眼可见的行高不齐）|
| **窗口无圆角** | `_shot3.png` `(0,0)` = `#1D1D1D`；`ShellWindow.xaml:11` 有 `WindowStyle="None"` 但**没有 `AllowsTransparency="True"`** | 官方是 8px 圆角 |

前三条的共同根因：**`App.xaml` 只重写了 3 个 ControlTemplate（Button / TabItem / TabControl）**，
其余控件（Expander / ScrollBar / ScrollViewer / TextBox / ToolTip / ContextMenu）全部落回 WPF 默认主题。
这是「源码里看不出、渲染出来才看见」的那一类缺陷，本项目的 `notes/2026-10-05-ui-input-limits.md`
已经吃过一次亏（「`PrintWindow` 截图正常，说明窗口渲染没问题」—— 渲染正常不代表渲染**对**）。

### 2.5 与官方逐条对照：我们具体落后在哪

| # | 维度 | 官方 | 我们 | 差在哪（可核对） |
| --- | --- | --- | --- | --- |
| 1 | 字号档数 | 6 档（12/13.333/14/14.667/16/17）| **7 档**（10.5/11/11.5/12/12.5/13/14 + BodySize 14.667）| `grep -oE 'FontSize="[0-9.]+"' R/src/VdHelper/Views/*.xaml \| sort \| uniq -c` 实测 |
| 2 | 字号治理 | 常量 `FontSize=14.667` 在主题里（`Styles.Shared.xaml:13-14`），视图只写 12/14/16 | `BodySize` 只被用了 4 次，视图里 17 处 `FontSize` 全是字面量 | `App.xaml:30` 定义 `BodySize`，但 `HealthView.xaml` 8 处直接写字面量 |
| 3 | 控件高度 | 全部 30（Button/TextBox/ComboBox/ShortcutEditor）| **30 与 24 混用**：`App.xaml:56` 默认 30，但 4 个按钮显式 `Height="24"`（`HealthView.xaml:94,162`、`ParametersView.xaml:58`、`HeadsetView.xaml:68`）| 两套高度，且 View 里的覆盖让 `App.xaml` 的 30 失效 |
| 4 | 最小控件宽 | Button `MinWidth="30"`（`Styles.Shared.xaml:1777-1779`）| `MinWidth="110"`（`App.xaml:57`），View 里改成 180/72/64 | 「切换」按钮 64、「执行」72、「深度探测」180 —— 三种宽度无规则 |
| 5 | 字体回退 | 单一 `Verdana`（`Styles.Shared.xaml:11-12`）| `UiFont = Verdana`（`App.xaml:29`），**但中文靠系统回退** | 01 §4.3 已警告过；实测截图里中文与拉丁字母基线不齐（`_shot3.png` 中 `3 块离线网卡持有 APIPA 地址` 一行）|
| 6 | 语义色 | **不存在**（见 §1.7）| 两套并存：一套死键（`App.xaml:25-27`），一套硬编码在转换器里（`StatusConverters.cs:14-20`）| 官方没得抄，必须自造并**一次性定死** |
| 7 | 语义色的可达性 | 不适用 | **实测不达标**（见 §3.3 计算）| 通过 `#2E7D32` 在 `#1E1E1E` 徽标底上只有 **3.25:1**，阻断 `#C62828` 只有 **2.97:1** |
| 8 | 状态冗余编码 | 用**图标**（`AppsCheckerWindow.xaml:23-28, 47-67`）| **只有颜色 + 一个 2 字徽标**（`HealthView.xaml:131-136`）| 色觉障碍用户无法区分通过/警告/阻断 |
| 9 | 列表控件 | `ScrollViewer > ItemsControl`（`MainWindow.xaml:325-330`）| `ListView` + 全模板覆盖 `ListViewItem`（`HealthView.xaml:107-121`）| 我们多了一层虚拟化（36 行值得），但**也丢了选中态与焦点态**（模板里只有 `ContentPresenter`）|
| 10 | 焦点反馈 | **也没有**（`FocusVisualStyle="{x:Null}"`，`Styles.Wpf.xaml:923-925`；`FocusStates` 空组，`Styles.Shared.xaml:1867-1873`）| **也没有**（`App.xaml:75-85` Button 模板无 focus 触发器）| **这一项官方不能抄**（见 §4）|
| 11 | 键盘 | 只有 `Ctrl+V`（`MainWindow.xaml:254-259`）| **一个都没有**（`grep -rn "KeyBinding\|AccessKey" R/src/VdHelper` = 0）| 症状芯片是 `TextBlock` + `MouseBinding`（`HealthView.xaml:73-76`），**键盘完全不可达** |
| 12 | 自动化 | 10 个 `AutomationId`，0 个 `Name`（`Styles.Wpf.xaml:525, 537, 592, 605, 646, 701, 715, 771, 783`）| **0 个** | 屏幕阅读器读不出任何东西 |
| 13 | ToolTip 样式 | `ToolTipStyle` `Padding="10,7"` + 投影（`Metrodark.Mscontrols.Core.Implicit.xaml:1181-1245`）| 无 ToolTip 样式，且**一处 ToolTip 都没用**（`grep -c ToolTip R/src/VdHelper` = 0）| 官方用 ToolTip 承载副文本（`MainWindow.xaml:580, 585-586, 590-591`），我们把说明全写在正文里 |
| 14 | 窗口圆角 | 8px | 无（`ShellWindow.xaml:11` 缺 `AllowsTransparency`）| 实测 `(0,0)` 无圆角 |
| 15 | 标题栏按钮 | `ImageButtonStyle`，透明底 + `Opacity 0.35→0.75` + 按下 1px 位移（`Styles.Shared.xaml:1904-1955`）| 普通 `Button` + 文字 `—` / `✕`（`ShellWindow.xaml:39-40`）| 官方靠图标淡入淡出，我们靠字符 |
| 16 | 导航选中反馈 | 仅前景色 `#808080`→`#FFFFFF`（`MainWindow.xaml:144-164`）| **加了官方没有的东西**：背景 `#262626` + 3px 蓝色指示条（`App.xaml:104-114`）| **这一项我们比官方好，不要「改回去」**（见 §4）|


