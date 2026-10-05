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

- 11 个色键：`TitleBrush #1D1D1D` / `BodyBrush #151515` / `ContentBrush #101010` / `InkBrush #E8E8E8` /
  `MutedBrush #9A9A9A` / `AccentBrush #2E79BD` / `HoverBrush #262626` / `LineBrush #2E2E2E` /
  `PassBrush #5BC85B` / `WarnBrush #E0A030` / `BlockBrush #E05B5B`（`App.xaml:17-27`）。
- 4 个文字样式：`ChromeText` / `H1` / `Muted` / `Mono`（`App.xaml:32-53`）。
- 3 个 ControlTemplate：Button（`:66-88`）、NavTab/TabItem（`:96-118`）、TabControl（`:123-139`）。

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
| 11 | 键盘 | 只有 `Ctrl+V`（`MainWindow.xaml:254-259`）| **一个都没有**（`grep -rn --include=*.xaml --include=*.cs "KeyBinding\|AccessKey" R/src/VdHelper` = 0 命中）| 症状芯片是 `TextBlock` + `MouseBinding`（`HealthView.xaml:73-76`），**键盘完全不可达** |
| 12 | 自动化 | 10 个 `AutomationId`，0 个 `Name`（`Styles.Wpf.xaml:525, 537, 592, 605, 646, 701, 715, 771, 783`）| **0 个** | 屏幕阅读器读不出任何东西 |
| 13 | ToolTip 样式 | `ToolTipStyle` `Padding="10,7"` + 投影（`Metrodark.Mscontrols.Core.Implicit.xaml:1181-1245`）| 无 ToolTip 样式，且**一处 ToolTip 都没用**（`grep -rn --include=*.xaml --include=*.cs ToolTip R/src/VdHelper` = 0 命中）| 官方用 ToolTip 承载副文本（`MainWindow.xaml:580, 585-586, 590-591`），我们把说明全写在正文里 |
| 14 | 窗口圆角 | 8px | 无（`ShellWindow.xaml:11` 缺 `AllowsTransparency`）| 实测 `(0,0)` 无圆角 |
| 15 | 标题栏按钮 | `ImageButtonStyle`，透明底 + `Opacity 0.35→0.75` + 按下 1px 位移（`Styles.Shared.xaml:1904-1955`）| 普通 `Button` + 文字 `—` / `✕`（`ShellWindow.xaml:39-40`）| 官方靠图标淡入淡出，我们靠字符 |
| 16 | 导航选中反馈 | 仅前景色 `#808080`→`#FFFFFF`（`MainWindow.xaml:144-164`）| **加了官方没有的东西**：背景 `#262626` + 3px 蓝色指示条（`App.xaml:104-114`）| **这一项我们比官方好，不要「改回去」**（见 §4）|


---

## 3. 现代化计划（按顺序，每项点名文件与可见变化）

**排序原则**：先做「渲染出来就扎眼」的（P0），再做「读起来累」的（P1），最后做可选的（P2）。
理由见 §2.4 —— 默认主题漏出来的东西比设计缺陷更影响第一印象。

### 3.1 P0 — 把 WPF 默认主题堵上（Expander / ScrollBar / ScrollViewer / TextBox）

**为什么排第一**：`_shot3.png` 上每一行一个纯白圆点，`_shot4.png` 上一条 17px 纯白竖条。
这不是审美问题，是「`App.xaml` 只写了 3 个 ControlTemplate」的遗漏（§2.4）。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P0-1 | 新建 `R/src/VdHelper/Resources/Theme.Controls.xaml` | 收编 4 个 ControlTemplate：`Expander`（`CornerRadius=3` `BorderThickness=1` `Padding=2`，照 `S/Themes/Metrodark/Metrodark.Mscontrols.Core.Implicit.xaml:1611-1679`）、`ScrollBar`（`Width=7 MinWidth=7 BorderThickness=0`，照 `S/Themes/Metrodark/Styles.Wpf.xaml:321-332, 384-390`）、`ScrollViewer`（复用官方 `PART_VerticalScrollBar` 模板，`S/Themes/Metrodark/Styles.Wpf.xaml:509-547`）、`TextBox`（`MinHeight=30 Padding="6,3"`，照 `S/Themes/Metrodark/Styles.Wpf.xaml:1414-1419`）|
| P0-2 | `R/src/VdHelper/App.xaml` | 在 `<Application.Resources>`（`:11`）里加 `<ResourceDictionary.MergedDictionaries>` 引入上面那个字典；现有 3 个 ControlTemplate（`:66-88, 96-118, 123-139`）一并搬进去 |
| P0-3 | `R/src/VdHelper/App.xaml:25-27` | 删掉 `PassBrush` / `WarnBrush` / `BlockBrush` 三个**引用数为 0 的死键**，值统一由 P0-4 提供 |

### 3.2 P0 — 一套语义色 token（Pass / Warn / Block / Unknown），在**实际发布的暗色**上可达

**为什么必须自造**：§1.7 已证明官方色板里的绿/琥珀**引用数为 0**，没有先例可抄。

**为什么现在的值不行**：现状是 `StatusConverters.cs:14-20` 的硬编码色，
在 `_shot3.png` 实测到的徽标底 `#1E1E1E` 上的对比度（WCAG 2.x 相对亮度公式逐项计算）：

| 状态 | 现值 | 在 `#1E1E1E` 徽标底 | 在 `#101010` 列表底 | WCAG AA 正文（4.5:1）|
| --- | --- | --- | --- | --- |
| Pass | `#2E7D32` | **3.25:1** | 3.71:1 | ❌ |
| Warn | `#B26A00` | **3.93:1** | 4.49:1 | ❌ |
| Block | `#C62828` | **2.97:1** | 3.38:1 | ❌ |
| Unknown | `#6A737C` | **3.46:1** | 3.95:1 | ❌ |
| 对照 `InkBrush #E8E8E8` | | 13.61:1 | 15.53:1 | ✅（说明计算本身没问题）|

**四个全部不达标**，Block 最差（2.97:1）。
另注：`App.xaml:25-27` 那套死键（`#5BC85B` / `#E0A030` / `#E05B5B`）实测在 `#101010` 上是
8.93 / 8.37 / 5.27，**都达标** —— 但它没被任何地方引用（§2.3）。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P0-4 | `R/src/VdHelper/App.xaml:17-27` | 定义 8 个语义 token：`PassInk` / `WarnInk` / `BlockInk` / `UnknownInk`（徽标文字色）+ `PassSurface` / `WarnSurface` / `BlockSurface` / `UnknownSurface`（徽标底色）。Ink 直接采纳现在那套已达标但没人用的 `#5BC85B` / `#E0A030` / `#E05B5B`，Unknown 取 `#98A2B3`（在 `#101010` 上 7.39:1）；Surface 取比 `#1E1E1E` 略深的一档，保证 1px 字形也看得见 |
| P0-5 | `R/src/VdHelper/StatusConverters.cs:12-21, 26` | `StatusToBrushConverter` 改为查 `{StaticResource}`，**不再用 `ColorConverter.ConvertFromString`**；删掉 `:26` 的 `Brush(string hex)` 私有方法 |
| P0-6 | `R/src/VdHelper/App.xaml:12-15` | 配套加 `StatusToSurfaceConverter`（返回徽标底色），供 P0-9 使用 |
| P0-7 | `R/src/VdHelper/Views/HealthViewModel.cs:285-287` | `NextActionRow.Accent` 的三个裸色 `#FF5B6E` / `#FFCC66` / `#8B93A8` 改为 token。三者在 `#141414` 上实测 6.12 / 12.35 / 6.00，**本身达标**；问题是绕过 token 体系，且 `#FF5B6E` 是纯红、与 Block 语义撞车 |

### 3.3 P0 — 状态不只用颜色（无障碍）

**现状**：`HealthView.xaml:131-136` 里状态 = 2 字中文（通过/警告/阻断）+ 颜色。
颜色是唯一的**快速**通道，而四个状态色对比度都在 3–4:1（§3.2），低视力用户读不出。
官方靠**图标**（`S/VirtualDesktop/Streamer/AppsCheckerWindow.xaml:23-28, 47-67`：Warning.png / Error.png 16×16 + `DataTrigger`），
**但官方只有两档图标，我们有四档状态**，字形得自造。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P0-8 | 新建 `R/src/VdHelper/Resources/Theme.xaml` | 加 4 个 16×16 单色字形（用 `Geometry` / `Path`，不引 PNG，`R/src/VdHelper/Resources/` 目前无任何图片）：通过=实心圆、警告=实心三角、阻断=实心方块、未知=空心圆。**形状在灰度与色觉障碍下仍可区分** |
| P0-9 | `R/src/VdHelper/Views/HealthView.xaml:131-136` | 徽标从「纯文字」改成「字形 + 文字」`StackPanel`：16×16 字形 + 2 字标签，底色用 P0-6 的 `StatusToSurfaceConverter`。**行的整体高度不变** |
| P0-10 | `R/src/VdHelper/Views/HealthView.xaml:126-140`、`R/src/VdHelper/Views/ShellWindow.xaml:35-38` | 给每个检测行与标题栏总判定加 `AutomationProperties.Name`。官方全树 0 个 `Name`（§2.5 第 12 行），**这一项我们必须比官方做得好** |
| P0-11 | `R/src/VdHelper/Views/HeadsetView.xaml:21-23`、`R/src/VdHelper/Views/ParametersView.xaml` 各状态 `TextBlock` | 同一个 `StatusToTextConverter` 的输出已有文字，改为与徽标同套字形，避免「列表有、参数页没有」的不一致 |

### 3.4 P1 — 排版与间距：7 档字号收敛成 5 档，控件高度统一 30

官方只有 6 档字号（§1.6），我们有 7 档（`grep -oE 'FontSize="[0-9.]+"' R/src/VdHelper/Views/*.xaml | sort | uniq -c` 实测：
10.5×1 / 11×1 / 11.5×11 / 12×3 / 12.5×5 / 13×2 / 14×1），且 `App.xaml:30` 的 `BodySize` 只被用了 4 次。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P1-1 | `R/src/VdHelper/App.xaml:32-53` | 把 `ChromeText`/`H1`/`Muted`/`Mono` 四个样式改为 5 个字号 token：`FsDisplay=16 Bold`（= 官方 Tab 标题档，`S/…/MainWindow.xaml:170-179`）、`FsSection=14 Bold`（= 官方小节 Label，`S/…/MainWindow.xaml:559-561`）、`FsBody=12.5`（行摘要）、`FsMeta=11.5`（副文本/来源/风险）、`FsMono=11 Consolas`（原始输出）|
| P1-2 | `R/src/VdHelper/App.xaml:56-57` | Button 的 `Height="30"` 改 `MinHeight="30"`；`MinWidth="110"` 降为 `72`（官方是 30，`S/…/Styles.Shared.xaml:1777-1779`；我们文案是中文，比 30 宽一点合理）|
| P1-3 | `R/src/VdHelper/Views/HealthView.xaml:94, 162`；`R/src/VdHelper/Views/ParametersView.xaml:58`；`R/src/VdHelper/Views/HeadsetView.xaml:68` | 删掉 4 处 `Height="24"` 与 `MinWidth` 字面量，改由 P1-2 的隐式样式给。**可见变化：四个「执行/切换/深度探测」按钮从 24px 变 30px，行高随之变化** |
| P1-4 | `R/src/VdHelper/App.xaml:1-4, 29` | `Application` 加 `Language="zh-CN"`；`UiFont` 改成 `"Verdana, Microsoft YaHei UI"`（采纳 01 §4.3 的建议）。**可见变化：中文不再靠系统随机回退，与拉丁字符基线对齐** |
| P1-5 | `R/src/VdHelper/App.xaml` + 三个 `Views/*.xaml` | 新增 4 个间距 token `GapTight=6` / `GapRow=10` / `GapBlock=18` / `PadCard=10`。现在视图里 `Margin`/`Padding` 是二十多个不同字面量（`18,14,18,10`、`0,0,0,10`、`0,0,0,8`、`10,7`、`10,4`、`74,4,0,10` …）|

### 3.5 P1 — 密集证据列表的视觉层级

**现状问题**（`_shot3.png` 可见）：34 行等权重、无分隔线、无选中态；展开后是
`Detail`（灰字）→ `Fixes` 卡片 → `Guidance` → `Evidence`（等宽）四段，**四段视觉权重几乎一样**。
更要紧的是：`CheckRow.Title`（= 检测项 id，如 `lan-reach`）**在列表里根本没显示** ——
`HealthView.xaml` 里 `Title` 只出现在 `:39`（NextAction）与 `:157`（FixRow），列表行只绑了 `Summary`（`:139`），
用户无法把界面上某一行和 `--report` 输出、issue 里的 `lan-reach` 对上号。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P1-6 | `R/src/VdHelper/Views/HealthView.xaml:126-140` | 行头 Grid 由 2 列（`64 / *`）改 3 列（`72 / 96 / *`）：徽标 / **检测项 id（`{Binding Title}`，`FsMono FsMeta`）** / 摘要。**可见变化：每行多了可与报告对号的 id** |
| P1-7 | `R/src/VdHelper/Views/HealthView.xaml:110` | 行间分隔从纯间距（`Margin="0,0,0,6"`）改成 1px 线（`Border BorderThickness="0,1,0,0"` + `LineBrush`）。官方 AppsChecker 就是这么做的：`S/VirtualDesktop/Streamer/AppsCheckerWindow.xaml:42-46`（`BorderThickness="0 1 0 0" Margin="0 -1 0 0"`）|
| P1-8 | `R/src/VdHelper/Views/HealthView.xaml:142-187` | 展开区四段分级：`Detail`（`:143`）改 `FsBody InkBrush`（现为 `Muted`）；`Guidance`（`:181-183`）保持 `FsMeta MutedBrush`；**`Evidence`（`:185-186`）改为默认折叠**，标题写「原始输出（N 行）· 点击展开」。**可见变化：一屏可见行数从 ~17 增加到 ~28** |
| P1-9 | `R/src/VdHelper/Views/HealthView.xaml:113-119` | `ListViewItem` 模板（现在只有一个 `ContentPresenter`，没有选中态也没有焦点态）补 `IsSelected` 与 `IsKeyboardFocusWithin` 视觉：`#1F2E79BD` 底 + `#A82E79BD` 边（官方 ListViewItem 选中色，`S/Themes/Metrodark/Styles.Shared.xaml:255-262, 1199-1204`）|

> **P1-8 的取舍要写明白**：现在原始输出默认展开，这是 ADR-002（`notes/decisions.md:14-24`）
> 「检测项是数据记录，展开看命令与原始输出」的直接实现。折叠**不删内容**，只改默认态；
 `--report-html` 里也已经是 `<details>` 折叠（`R/src/VdHelper/Reports/ReportWriter.cs`），
 所以这一步是让界面与已发布的报告格式一致，不是新发明。
>
> **P1-6 要注意一个真实风险**：徽标列 `64` + id 列 `96` 会吃掉 160px，
> 窗口 `MinWidth="840"`（`ShellWindow.xaml:6`）减去导航列 160 后内容区只剩 680。
> 这一项**必须**配合截图确认窄窗口下摘要不出现难看的截断（P1-6 的验证项见 §5.2）。

### 3.6 P1 — 键盘与焦点

**现状实测**：`grep -rn --include=*.xaml --include=*.cs "KeyBinding\|AccessKey" R/src/VdHelper` = **0 命中**
（不加 `--include` 会命中 `bin/` 下 WPF 程序集里的字符串，那是噪声）。
`App.xaml:75-85` 的 Button 模板触发器只有 `IsMouseOver` / `IsPressed` / `IsEnabled`，**没有 focus**。
症状芯片是 `Border > TextBlock` + `MouseBinding`（`HealthView.xaml:58-79`），**不是控件，键盘到不了**。

| 项 | 改哪个文件 | 可见变化 |
| --- | --- | --- |
| P1-10 | `R/src/VdHelper/App.xaml:75-85` | Button 模板加 `Trigger IsKeyboardFocusWithin=True` → `BorderBrush = AccentBrush`。**官方没有这个**（`FocusVisualStyle="{x:Null}"`，`S/…/Styles.Wpf.xaml:923-925`；Button 的 `FocusStates` 两个 VisualState 都空，`S/…/Styles.Shared.xaml:1867-1873`）—— **我们必须补上，见 §4.3** |
| P1-11 | `R/src/VdHelper/Views/HealthView.xaml:58-79` | 症状芯片从 `Border + MouseBinding` 改成 `ToggleButton`（或 `RadioButton` + `GroupName="Symptom"`）。**可见变化：芯片获得焦点环、可 Tab 到、可 Space 切换** |
| P1-12 | `R/src/VdHelper/Views/ShellWindow.xaml:44-55` | `TabControl` 加 `KeyboardNavigation.TabNavigation="Cycle"`，三个 `TabItem` 加 `AccessKey`（`本机体检`/`串流参数`/`头显诊断`）|
| P1-13 | `R/src/VdHelper/Views/ShellWindow.xaml:11-15, 24, 57` | 窗口加 `AllowsTransparency="True"`，标题栏/状态栏 Border 补 `CornerRadius="8 8 0 0"` / `"0 0 8 8"`（对齐官方 `S/…/MainWindow.xaml:273, 1371`）。**注意：这会关掉 DWM 硬件加速，拖拽与滚动性能需实测**（见 §5.2）|
| P1-14 | `R/src/VdHelper/Views/ShellWindow.xaml:39-40` + `R/src/VdHelper/App.xaml` | 标题栏两个按钮改 `ImageButtonStyle` 式样：透明底 + 常态 `Opacity=0.45` → 悬停 `0.9`（照 `S/Themes/Metrodark/Styles.Shared.xaml:1904-1955`）。**可见变化：字符 `—`/`✕` 由实心变淡，悬停才亮** |

### 3.7 P2 — 可选，且我明确不建议做：把说明挪进 ToolTip

官方把长解释放 `ToolTip`（`S/…/MainWindow.xaml:580, 585-586, 590-591`），
我们的对应物是 `ParametersView.xaml:15` 那一大段说明文字。
**标为 P2 且不建议做**：ADR-004（`notes/decisions.md:37-46`）要求「读不到的值要显示来源说明」，
把来源藏进 ToolTip 是**反 ADR** 的方向。若要做，只能对纯解释性的补充文案做，
正文里的来源/风险必须留在正文（`ParametersView.xaml:50-53`）。

---

## 4. 不要改的东西（写下来给下一轮）

### 4.1 ADR 与代码注释里写了理由的

| 不可改 | 证据 | 为什么不能「顺手现代化」|
| --- | --- | --- |
| **首屏必须是「接下来做什么」且不设条数上限** | `R/src/VdHelper/Views/HealthView.xaml:15-17` 注释：「A verdict alone is not actionable, and the first thing people do with a long list is close the window. **No cap**: an earlier version showed the top six and that silently hid the most informative finding.」| 注释记着一次**已经犯过并回滚**的错误。「只显示前 N 条」看着像现代化，实际是信息损失 |
| **症状筛选不得改变 verdict** | `R/notes/2026-10-05-symptom-entry.md:22-25`：「筛选不允许改变判定结果。它只决定『先看哪几行』，不决定 verdict」| 任何「筛选后重算结论」的 UI 改动，都是把一种骗人的修法做成产品功能 |
| **不做假开关** | `R/notes/decisions.md:37-46`（ADR-004）| 参数页「切换」按钮必须继续对只读项隐藏（`ParametersView.xaml:60` 的 `Visibility` 绑定）。改成永远可点、点了给提示是倒退 |
| **修复必须先备份 + 可回滚** | `R/notes/decisions.md:26-35`（ADR-003）| 「执行」旁的三行「命令 / 备份 / 回滚」（`HealthView.xaml:166-171`）不能折叠、不能收进 ToolTip。改版式可以，改信息层级不行 |
| **路由 / AP 隔离类只解释不改** | `R/notes/decisions.md:34`；`R/docs/product-spec.md:50` | UI 上不要给这些项任何「可修」的视觉暗示（不要用主色按钮）|
| **技术栈 WPF，不回退 WinForms** | `R/notes/decisions.md:3-12`（ADR-001）| 「用 WinForms 重写控件更快」直接否 |
| **PC 侧优先，ADB 是第二屏** | `R/notes/decisions.md:48-55`（ADR-005）| 不要因为「头显诊断更直观」而调换 tab 顺序 |
| **历史保留 40 次 + 报告里说变化** | `R/notes/decisions.md:56-69`（ADR-007）| 状态栏的「与上次相比」文案不要为了简洁删掉 |
| **App.xaml 已定下的三层底色** | `R/src/VdHelper/App.xaml:17-19` + 实测 `_shot3.png`（标题栏 `#1D1D1D`、列表区 `#101010`、导航列 `#151515`）| 与官方 `S/…/MainWindow.xaml:272, 308, 314` 一致且**实测渲染正确**，不用动 |
| **48px 标题栏 / 48px 状态栏 / 160×48 导航项 / 三行骨架** | 实测 `_shot3.png`：标题栏 y=0..47、导航项 y=56..103、状态栏 y=632..679、导航列 x=0..159 | 与官方 `S/…/MainWindow.xaml:261-268, 276-277, 112-115` 完全一致 |

### 4.2 我们比官方做对、不要改回去的

| 不可「改回官方」| 证据 | 理由 |
| --- | --- | --- |
| **导航选中态的蓝色指示条 + 背景** | `R/src/VdHelper/App.xaml:104-114`（`Border x:Name="Bar" Width="3"` + `#262626` 底）vs 官方 `S/…/MainWindow.xaml:144-164`（只有前景色变化、无指示条）| 官方纯灰→白在深色底上偏弱（`#808080` on `#101010` = 4.82:1，够正文但不够指示当前页）。我们的做法更好 |
| **总判定常驻标题栏** | `R/src/VdHelper/Views/ShellWindow.xaml:35-38`；官方状态栏只有 `Version:` + 版本号（`S/…/MainWindow.xaml:1385-1396`）| 判定是本工具的主信息，不该藏在角落 |
| **Expander 展开原始输出** | `R/src/VdHelper/Views/HealthView.xaml:185-186`；官方 AppsChecker 每行只有一句 `Description`（`S/…/AppsCheckerWindow.xaml:74-77`）| 我们卖的是证据链，不是摘要 |
| **`--tab N` 的无头入口** | `R/src/VdHelper/App.xaml.cs:125-127`；`R/tools/capture-tab.ps1:29-33` | 它同时是「第三屏可命令行跑」的兑现（`README.md:16`）与截图工具的基础，别为了 UI 统一而删 |

### 4.3 「官方也没有，所以不算落后」的三项 —— 但仍要做对

| 项 | 官方现状 | 我们的现状 | 结论 |
| --- | --- | --- | --- |
| 焦点视觉 | `FocusVisualStyle="{x:Null}"`（`S/…/Styles.Wpf.xaml:923-925`）；Button 的 `FocusStates` 两个 VisualState 都空（`S/…/Styles.Shared.xaml:1867-1873`）| 同样没有 | **仍要做 P1-10**。官方没做对不构成我们不做对的理由 |
| `AutomationProperties.Name` | 全树 0 个，只有 10 个 `AutomationId`（`S/…/Styles.Wpf.xaml:525, 537, 592, 605, 646, 701, 715, 771, 783`）| 0 个 | 做 P0-10 是净增益，不是「对齐官方」|
| 语义色 Pass/Warn/Block | 色板里有绿/琥珀，**业务引用 0 次**（§1.7）| 有，但在 2.97–3.93:1（§3.2）| 官方没先例，P0-4 必须**自造并补一条 ADR** |

---

## 5. 每一条怎么验证（不靠猜）

### 5.1 本项目已经踩过的坑 —— 它们决定了下面这套流程

`R/notes/2026-10-05-ui-input-limits.md` 实测记录：
- 合成鼠标输入（`SetCursorPos` + `mouse_event`）**到不了本窗口**（按钮无反应、症状芯片不高亮，`_deep2.png`/`_deep3.png` 的 SHA256 完全相同）；
- `AutomationElement` 的 `Descendants` 里**只有标题栏 2 个按钮**，WPF 内容控件不进自动化树；
- 但 **`PrintWindow` 截图正常** —— 「渲染没问题」不等于「渲染得**对**」。

`R/tools/capture-window.ps1:1-6` 的注释记着另一条：用 `CopyFromScreen` 会被别的窗口遮挡而截到遮挡物，
所以**截图一律走 `PrintWindow(PW_RENDERFULLCONTENT)`**（`capture-window.ps1:42`）。

`R/notes/2026-10-06-do-not-sweep-state-changing-commands.md:38-44` 记着第三条：
**验证一个命令不等于执行它**。UI 验证不得顺手触发会改机器状态的命令
（`--quit-streamer` / `--apply <id>` / `--set-param` 写路径一律不进本清单）。

### 5.2 逐项验证方法

| 项 | 验证方式 | 具体命令 / 判据 |
| --- | --- | --- |
| P0-1 ~ P0-3 | **渲染像素采样**（不是看源码）| `dotnet build src/VdHelper/VdHelper.csproj -c Release` → `powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-tab.ps1 -Tab 0` → 用 Pillow 采样：(a) 展开箭头区域**不应出现 16px 的 `(255,255,255)` 圆**（现在 `_shot3.png` x=182..197 是纯白）；(b) 右侧滚动条**不应出现 `(240,240,240)`** 且宽度 ≤ 8px（现在 `_shot4.png` x=944..960 共 17px）|
| P0-4 ~ P0-7 | **对比度计算 + 截图取色双向确认** | 先按 WCAG 相对亮度公式确认四色在 `#1E1E1E` 与 `#101010` 上均 ≥ 4.5:1；再截图采样徽标区域，**采样到的 RGB 必须等于 token 写入的值**。现有链路可作对照：现在采样得 `(46,125,50)`，与 `StatusConverters.cs:14` 的 `#2E7D32` 一致，说明「取色链路通」|
| P0-8 ~ P0-11 | **灰度截图**（色觉 / 打印模拟）| 同一张 `_tab0.png` 转灰度（`PIL.Image.convert("L")`）后，四种状态仍能靠**形状**区分：实心圆 / 实心三角 / 实心方块 / 空心圆。`AutomationProperties.Name` 用 `grep -c 'AutomationProperties.Name' src/VdHelper/Views/*.xaml` ≥ 3 静态核对 |
| P1-1、P1-5 | **静态计数闸门 + 截图** | `grep -oE 'FontSize="[0-9.]+"' src/VdHelper/Views/*.xaml \| sort -u` 的去重条目数应 **≤ 5**（现在 7 档）。建议把这条写成 `tools/` 下的检查脚本，否则会重新漂移 |
| P1-2、P1-3 | **量像素高度** | 截图中量「深度探测丢包」按钮上下沿之差 = **30**（现在 `HealthView.xaml:94` 是 24）|
| P1-4 | **截图目视 + 构建输出** | 同一行中英混排（如 `3 块离线网卡持有 APIPA 地址`）基线对齐；构建输出无字体回退警告 |
| P1-6 | **截图 + 与报告对号** | 截图中每行可见 id；再用 `VdHelper.exe --selftest --out x.txt` 核对 `lan-reach` 等 id 能在界面上一一找到。**并额外截一张 `MinWidth=840` 的窄窗口图**，确认摘要列没有难看的截断 |
| P1-7、P1-8 | **截图对比可见行数** | 折叠后一屏可见行数应从 ~17 增加到 ~28（按 `(680-48-48)/行高` 估，实测为准）。逐行**不含**任何原始输出文本 |
| P1-9 | **截图 + 键盘** | 手动 `Tab` 到列表某行后截图，行底应出现 `#1F2E79BD` 调的行。**不能**用 `AutomationElement` 断言（§5.1 实测只有 2 个按钮进树）|
| P1-10 | **键盘实测**（唯一可靠路径）| 手动 `Tab` 遍历并 `PrintWindow` 截图，应看到焦点环。合成键鼠输入在本机不可用（§5.1）|
| P1-11 | **静态核对 + 键盘** | `grep -c MouseBinding src/VdHelper/Views/HealthView.xaml` 应为 **0**（现在 1，`HealthView.xaml:74`）；Tab 序能走到症状芯片 |
| P1-12 | **键盘实测** | `Alt+1/2/3` 切 tab 并截图确认 |
| P1-13 | **像素采样 + 两项功能回归** | 截图 `(0,0)` 应**不是** `#1D1D1D`（圆角处应透出下层）。回归：`AllowsTransparency=True` 会关掉 DWM 硬件加速，**必须实测标题栏拖拽是否跟手**；以及 `VdHelper.exe --selftest --out x.txt` 退出码不变（UI 改动不得动 CLI 契约）|
| P1-14 | **像素采样** | 标题栏按钮区域常态最亮像素 < 150（半透明字符），悬停时 > 200。悬停态**无法自动触发**（合成输入不可用），需手动截图 |

### 5.3 每轮必跑的固定项（与 `R/.github/workflows/build.yml` 对齐）

```powershell
dotnet build src/VdHelper/VdHelper.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-tab.ps1 -Tab 0
powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-tab.ps1 -Tab 1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-tab.ps1 -Tab 2
./tools/check-symptom-map.ps1
python tools/check-citations.py
python tools/check-issue-form.py
```

**CI 里没有截图步骤**（`build.yml` 只做 build + `--selftest` + 三道闸门 + publish）。
也就是说「UI 改完必须看渲染结果」这条纪律目前**只靠人记**。
建议把 §5.2 里 P0-1 那两条像素判据写成 `tools/check-render.py` 挂进 CI，
否则 §2.4 列的四个默认主题漏出物会再次以「源码里看不出」的形式回归。

### 5.4 `[未验证]` 清单（本文自己没能确证的）

- **官方从未在真实显示器上目检过**（沿用 01 §11 的 `[未验证]`）。本文所有官方结论都是反编译 XAML 的**静态事实**。
- **官方控件模板的像素级细节**（CheckBox 方框边长、Slider 轨道粗细）**未验证**：反编译 XAML 丢了 `Thickness` 结构的记号展开（01 §11 已记）。
- **`AllowsTransparency=True` 对本工具的性能影响未验证**：P1-13 的风险是**推断**，依据是官方在用该属性（`S/…/MainWindow.xaml:20`），不是本机实测。
- **`_shot3/4/7/10.png` 与当前源码是否逐行对应，未逐一核对**：这四张图时间戳为 09:13–09:45（`ls --time-style=+%H:%M`），
  而 §2.5 的行数结论来自当前源码的静态计数。若有出入，**以源码计数为准**。
- **本轮未重跑渲染**：只读分析，未构建、未启动 GUI（遵守 Non-Conflict）。§2.4 的全部像素值来自上述既有截图。

---

## 6. 一页纸结论

1. **外壳抄对了，控件皮肤没抄全。** 48/48/48 三行、160×48 导航、三档底色实测与官方一致；
   但 `App.xaml` 只重写了 3 个 ControlTemplate，Expander / ScrollBar / ScrollViewer / TextBox 全落回 WPF 默认主题，
   实拍图上是**每行一个纯白圆点**和**一条 17px 纯白滚动条**（§2.4）。这是第一优先（P0-1 ~ P0-3）。
2. **语义色必须自造并定死。** 官方色板里的绿/琥珀**引用数为 0**，没有先例；
   我们现有四个状态色在徽标底上实测 2.97–3.93:1，全部不达 WCAG AA；
   而 `App.xaml:25-27` 有一套已达标但**引用数为 0** 的死键。合并成一套即可（P0-4 ~ P0-7）。
3. **状态要有第二个通道。** 现在状态 = 颜色 + 两字中文，唯一的快速通道恰好是不达标的那个。
   官方靠 16×16 图标（`AppsCheckerWindow.xaml:23-28`），我们有四档状态需要四枚字形（P0-8 ~ P0-11）。
4. **别抄官方的两个坏样。** `FocusVisualStyle="{x:Null}"`、空的 `FocusStates`、零个 `AutomationProperties.Name` ——
   这三样我们要**比官方做对**，不是对齐（P0-10、P1-10）。
5. **有些东西是对的，别动。** 首屏「接下来做什么」不设上限、症状筛选不改 verdict、不做假开关、
   修复的备份/回滚三行不折叠、导航指示条不回退官方 —— 都有 ADR 与代码注释背书，
   且首条记着一次已回滚的错误（§4.1、§4.2）。
6. **验证靠渲染，不靠源码。** 本项目已被「合成输入到不了窗口」和「PrintWindow 正常 ≠ 渲染正确」各咬过一次；
   本文每条改动都给了像素判据或具体命令，并承认 CI 目前不查渲染（§5.3）。

