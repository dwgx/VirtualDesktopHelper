# 02 — VDHelper 第三屏「头显诊断」落地方案

> 依赖：[`01-reuse-map.md`](01-reuse-map.md)（`dwgx/Quest-ADB-Dashboard` 复用审计）
> 阈值真值来源：`research/06-adb-headset/01-adb-playbook.md`
> 视觉语言来源：`research/05-ui-reverse/01-ui-spec.md`
> 现状参照：`src/VdHelper/Core/Model/Health.cs`、`Core/Health/HealthEngine.cs`、`Views/ShellWindow.xaml`、`App.xaml`
>
> **本文件只是方案，没有改任何 `src/` 代码** —— Non-Conflict 限定本轮只写 `research/11-quest-dashboard/**`。

---

## 0. TL;DR

- 第三屏 = **头显侧证据采集 + 跨端归因**，不是「再来一份头显体检表」。
- 数据来源：**17 条 adb 命令**（从该 repo 的 29 条里砍到 17 条，每条都喂一个判定）。
- 判定层：10 个 `ICheck`，全部沿用第一屏的 `CheckStatus` 四态，**不新增概念**。
- 增量核心：**一个 `Attribution` 引擎**，把第一屏 17 项 + 第三屏 10 项合成一句带证据的结论。
- UI：沿用 `App.xaml` 已有的资源键（`ContentBrush`/`UiFont`/`BodySize`/`NavTab`/`Muted`/`Mono`/`AccentBrush`），**零新增主题**。
- 报告：一份端到端 HTML，三列「字段 / 值 / 证据来源」，抄 `QuestAdbWebUi.cs:1895-1909` 的 DSL。

---

## 1. 第三屏的定位与边界

### 1.1 一句话

> 第一屏回答「**PC 这台机器**能不能起串流」，第三屏回答「**连不上的话，是头显那边的事吗**」，
> 并且给出**证据链**而不是猜测。

### 1.2 三条硬边界

| 边界 | 规则 | 依据 |
|---|---|---|
| **默认只读** | 第三屏不写任何 `settings put`。修复一律走 `FixAction`，用户点确认才执行，且带 `Backup`/`Rollback` | `01-reuse-map.md` §4 危险动作行；`Model/Health.cs:26-36` |
| **默认只走 USB** | 检测到设备是 `ip:port` 形式（非 USB 序列号）时，页面顶部常驻 playbook §7 的明文通道告警 | playbook §7 |
| **只认一个包名族** | `VirtualDesktop.Android` / `com.dwgx1.vd.recovered` / `com.dwgx1.virtualdesktop.recovered` 三个都要认，报告里标明认到哪个 | playbook §3「⚠️ 不要写成 `com.vrdesktop.streamer`」 |

### 1.3 与该 repo 的分工

| | Quest-ADB-Dashboard | VDHelper 第三屏 |
|---|---|---|
| adb 发现 | 36 条候选（BAT + Python 两套） | **合并**：旧 VDH 18 条 + 它的 Meta Quest Developer Hub / `oculus-diagnostics` 路径，**加版本门槛**（playbook §1.3） |
| 进程执行 | `ProcessStartInfo.Arguments` 手拼 | `ArgumentList`（移植函数体，不移植实现） |
| 采集 | 29 条命令，串行，全量 | **17 条**，并行，全量必跑 + 按需补 |
| 判定 | **无** | 10 个 `ICheck` + 归因引擎 |
| 报告 | 单端双档 | **单份端到端**，双档（share-safe / private-full） |

---

## 2. 架构落点（新增/修改文件清单）

```
src/VdHelper/Core/Adb/AdbLocator.cs        [新]  adb 发现 + 版本门槛
src/VdHelper/Core/Adb/AdbRunner.cs         [新]  移植 RunResult → ArgumentList
src/VdHelper/Core/Adb/HeadsetSnapshot.cs   [新]  移植 Capture/Snapshot
src/VdHelper/Core/Adb/HeadsetProbe.cs      [新]  移植 CollectSnapshot → 17 条命令
src/VdHelper/Core/Adb/Redactor.cs          [新]  移植 Redact/RedactLoose/SerialMask
src/VdHelper/Core/Health/HeadsetChecks.cs  [新]  10 个 ICheck
src/VdHelper/Core/Diagnosis/Attribution.cs [新]  跨端归因引擎
src/VdHelper/Core/Report/EndToEndReport.cs [新]  合并 HTML 报告
src/VdHelper/Views/HeadsetView.xaml(.cs)   [新]  第三屏
src/VdHelper/Views/HeadsetViewModel.cs     [新]
src/VdHelper/Core/Model/Health.cs          [改]  CheckResult 加 Scope 字段
src/VdHelper/Core/Health/HealthEngine.cs   [改]  允许按 scope 跑
src/VdHelper/Views/ShellWindow.xaml        [改]  加第二个 TabItem
```

---

## 3. 数据来源：adb 命令清单

### 3.1 选取原则

1. **每条命令必须喂至少一个判定**。只为了「放进报告好看」的命令不跑。
2. **超时预算**：全部 17 条并行，总预算 **4.5 秒**（串行相加是 ~85 秒，不可接受）。
3. **一条都不写**。全部只读。
4. 与该 repo 的取舍写在「取舍」列里，写清**为什么**。

### 3.2 清单

| # | 命令 | 超时(ms) | 喂给哪个判定 | 取舍 |
|---|---|---:|---|---|
| **G1** | `adb devices -l` | 4000 | `hs-link` 设备态 | 照抄 repo `:1726` |
| **G2** | `adb version` | 2500 | `hs-adb` 版本门槛 | **repo 没有**。playbook §1.3 要求 ≥30 才有 `pair` |
| **G3** | `shell id` | 2500 | `hs-adb` shell uid | 照抄 repo `:1727` |
| **N1** | `shell ip -4 addr show wlan0` | 3000 | `hs-net-ip` 头显 IP+前缀 | 改写 repo 的 `ip addr`（`:1750` 全量）→ 只取 wlan0 |
| **N2** | `shell ip route` | 3000 | `hs-net-route` default 路由 | **repo 采了但从不用**（`01-reuse-map.md` §5.2-B 实测）。我们把它变成判定 |
| **N3** | `shell dumpsys wifi` | 8000 | `hs-wifi-perf` 频段/速率/RSSI | 照抄 repo `:1736` |
| **N4** | `shell settings get global http_proxy` | 2500 | `hs-proxy` | playbook §3.6；repo 只在 `settings list global` 里隐含 |
| **N5** | `shell settings get global private_dns_mode` | 2500 | `hs-dns` | playbook §3.5 |
| **V1** | `shell dumpsys package VirtualDesktop.Android` | 6000 | `hs-vd-pkg` / `hs-vd-perm` | repo `:1752` **只取 versionName**。我们要取签名、`granted=`、`launchable activity` |
| **V2** | `shell pidof VirtualDesktop.Android` | 3000 | `hs-vd-proc` | playbook §3.2。repo 没有 |
| **V3** | `shell dumpsys power` | 6000 | `hs-pwr` 唤醒/接近 | playbook §3.7。repo `:1733` 采了但只用来做电池摘要 |
| **V4** | `shell dumpsys activity exit-info VirtualDesktop.Android` | 6000 | `hs-vd-exit` | playbook §3.7。**repo 没有**，而这是「刚打开就退」的第一诊断命令 |
| **V5** | `shell dumpsys activity activities` | 6000 | `hs-vd-foreground` | playbook §3.7 |
| **V6** | `shell dumpsys connectivity` | 6000 | `hs-net-eval` | repo `:1737` 采了但从不用。同 N2，我们把它变成判定 |
| **S1** | `shell getprop ro.vros.build.version` | 2500 | `hs-os` HorizonOS 版本 | playbook §3.7：VD App 自己读这个（`VrApp.cs:92-107`），读 `ro.build.version.release` **读不到 VR 侧** |
| **S2** | `shell df -h /data` | 4000 | `hs-storage` | playbook §3.7（APK ~941–958 MiB） |
| **L1** | `logcat -d -t 3000` | 10000 | 仅 `hs-vd-exit` 判为崩溃时才拉 | repo `:1754` 无条件跑 3000 行。我们**按需**——这是它最贵的一条 |

**被砍掉的 12 条**（repo 有、我们不要）：`settings list global/system/secure`（我们要的是 2 个具体键，不是 200 行）、
`dumpsys battery`、`dumpsys display`、`dumpsys usb`、`dumpsys bluetooth_manager`、`dumpsys media.camera`、
`dumpsys sensorservice`、`dumpsys input`、`pm list packages`、`pm list features`、`cmd package list libraries`、
`dumpsys package com.oculus`。
理由：这些是**通用设备盘点**，不是**串流诊断**。它们进报告只会稀释注意力。

### 3.3 执行器（移植 `RunResult`）

```csharp
// 移植自 dwgx/Quest-ADB-Dashboard src/QuestAdbWebUi.cs:1943-1967（MIT）
// 改动：psi.Arguments 手拼 → ArgumentList；去掉 Clean() 的 "-" 哨兵，改用 null。
internal static async Task<AdbResult> RunAsync(
    string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
{
    var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true,
                                          RedirectStandardError = true, CreateNoWindow = true };
    foreach (var a in args) psi.ArgumentList.Add(a);   // ← 原实现在这里手拼字符串

    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync(ct);
    var stderr = p.StandardError.ReadToEndAsync(ct);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    try { await p.WaitForExitAsync(cts.Token); }
    catch (OperationCanceledException) { TryKill(p); return new(false, -1, "", "超时", true); }
    return new(true, p.ExitCode, await stdout, await stderr, false);
}
```

> 原实现用两个裸 `Thread` + `ReadToEnd()`，且 `outThread.Join(1000)` 是**固定 1 秒**——
> 慢机器上会读到不完整的输出。换成 `ReadToEndAsync` 消除了这个竞态。

### 3.4 证据留存

每条命令留存 `Name / Command / ExitCode / TimedOut / DurationMs / Output`（移植 repo 的 `Capture`，`:34-44`），
并**原样**进 `CheckResult.Evidence`——这与第一屏 `Health.cs:47` 的 `IReadOnlyDictionary<string,string> Evidence`
是同一件事，UI 不用为第三屏新开一条渲染路径（`HealthView.xaml:44` 已经会渲染它）。

---

## 4. 第三屏检查项（10 个 `ICheck`）

全部 `CheckFactory.Delegate(new(CheckDefinition(Id, Title, Question, Category)), runAsync)`，
与第一屏 `HealthChecks.cs` 完全同构。

| Id | 标题 | 问题 | 类别 | 判定规则 | 阈值来源 |
|---|---|---|---|---|---|
| `hs-link` | 头显连接 | adb 认到设备了吗？ | 连接 | `devices -l` 按 `\t` 切列，第 2 列 ∈ `device` → Pass；`unauthorized`/`offline` → Block 并给对应中文提示 | playbook §2.1。**注意按列精确比对**，不抄旧 VDH 的子串匹配 |
| `hs-adb` | adb 可用性 | 找到的 adb 版本够吗？ | 连接 | `version` 主版本 ≥ 30 → Pass；< 30 → Warn（「无线配对不可用」）；未找到 → Block | playbook §1.3 |
| `hs-net-ip` | 头显 Wi-Fi 地址 | 头显在 wlan0 上有地址吗？ | 网络 | `ip -4 addr show wlan0` 有 `inet` 且 ≠ `127.0.0.1`/`0.0.0.0` → Pass；无 → **Block** | playbook §3.3 |
| `hs-net-route` | 头显路由 | 头显出得了局域网吗？ | 网络 | `ip route` 有 `default via <gw> dev wlan0` → Pass；只有 `unreachable default` 或无 default → **Block** | playbook §3.4 |
| `hs-wifi-perf` | 头显 Wi-Fi 性能 | 频段/协商速率够串流吗？ | 网络 | **直接用 VD 自己的阈值**：`IsSlow() = IsConnected && (Frequency < 4000 \|\| LinkSpeed < 450)` → Warn | `PerfStatsHelper.cs:114-123` + `WifiMetrics.cs:38-41`，playbook §3.3 已抄出。**不要另发明数字** |
| `hs-net-eval` | 头显网络评估 | 系统认为网络可用吗？ | 网络 | `dumpsys connectivity` 无 `VALIDATED`/`CAPABILITY` 降级 → Warn | repo 采了不用（`01-reuse-map.md` §5.2-B） |
| `hs-vd-pkg` | VD 客户端 | 头显上装了 VD 吗？版本对吗？ | 客户端 | 三包名任一 `Package [...]` 命中 → Pass；无 → **Block**（给安装指引）；有但 `versionName` 与 PC Streamer `1.34.22.0` 不匹配 → **Block**（VD 有硬门："Streamer uses a newer version, update this app before connecting"） | playbook §3.1；包名 `VirtualDesktop.Android` 在 repo `:1665` 与 playbook 一致 |
| `hs-vd-perm` | 运行时权限 | 7 项 VR 权限授全了吗？ | 客户端 | 7 项任一 `granted=false` → **Block**（「30 秒后 VR 焦点被回收」）。`RECORD_AUDIO` 单列 Warn（「首启弹窗会卡住」）；另 2 项 legacy 只提示 | playbook §5.1/§5.3 |
| `hs-vd-proc` | VD 进程 | 客户端在跑吗？ | 客户端 | `pidof` 有 PID → Pass；无 PID **且** `mWakefulness=Asleep` → **Pass + 说明「头显睡着，不是故障」**；无 PID **且** 已唤醒 → Warn | playbook §3.2 + §3.7。**这条必须与 `hs-pwr` 联判** |
| `hs-vd-exit` | 最近退出原因 | 「刚打开就退」是什么？ | 客户端 | `dumpsys activity exit-info` 首条 `reason=1 (EXIT_SELF)` → Warn（App 自退，非崩溃）；其他 reason → **Block** + 拉 L1 logcat | playbook §3.7。**repo 没有这一项，而它是这个症状的第一诊断命令** |

> 额外 4 项**按需**采集（不默认跑）：`hs-proxy`(N4)、`hs-dns`(N5)、`hs-pwr`(V3，被 `hs-vd-proc` 拉)、
> `hs-os`(S1，被 `hs-vd-pkg` 拉)、`hs-storage`(S2，被 `hs-vd-pkg` 拉)。
> 依据 repo 的做法：**采集层与判定层分离**（repo 的 `FillSnapshotFields` 就是纯函数，不含判定）。

---

## 5. 归因引擎 —— 这是第三屏真正的产品

### 5.1 为什么必须有

第一屏 17 项 + 第三屏 10 项 = 27 项。如果只是并排显示，用户看到的仍是「一堆红黄绿」，
和该 repo 的「一堆字段」没区别。**归因是唯一让用户知道下一步做什么的东西。**

### 5.2 归因规则表（跨端）

规则按顺序求值，**第一条命中即产出结论**（`Attribution.Evaluate(pcReport, headsetSnapshot)`）。

| # | PC 侧条件（第一屏 Id） | 头显侧条件（第三屏） | 归因结论 | 用户该做什么 |
|---|---|---|---|---|
| **A1** | `hs-link=Block` | — | **头显没连上**：无 USB 连接或未授权 | 按 `hs-link` 的提示接好线、点「允许」 |
| **A2** | — | `hs-vd-pkg=Block` | **头显没装 VD 客户端** | 装 APK（复用 playbook 的安装序列） |
| **A3** | `net-primary=Block` | 任意 | **纯 PC 侧**：PC 没有可用局域网地址 | 先修 PC 网卡，**不用看头显** |
| **A4** | `fw-vd=Block` | 任意 | **纯 PC 侧**：入站放行规则缺失 | 用 `Fixes.RestoreVdRule()` |
| **A5** | `svc-vd=Block` 或 `svc-log` 有 `-2147024891` | 任意 | **纯 PC 侧**：Streamer 起不来（身份/权限） | `Fixes.StartVdService()` + 看 ServiceLog |
| **A6** | `net-primary=Pass` | `hs-net-ip=Block` | **头显没连 Wi-Fi** | 头显里连 Wi-Fi |
| **A7** | `net-primary=Pass` | `hs-net-route=Block` | **头显无路由**：`unreachable default` | 头显重连 Wi-Fi / 检查 DHCP |
| **A8** | `net-primary=Pass` | `hs-net-ip=Pass` **且** 与 PC 主网卡不同 `/prefix` | **不在同一网段**：定向广播 `255.255.255.255` 出不了这个段，**发现必然失败** | 两边改到同一子网 |
| **A9** | `net-profile=Warn`（Public） | `hs-net-ip=Pass` | **纯 PC 侧**：网络类别不对 | 把网络设为「专用网络」 |
| **A10** | 全部 Pass | `hs-vd-perm=Block` | **头显侧**：权限未授，30 秒失焦 | 走 7 条 `pm grant`（`FixAction`，带确认门） |
| **A11** | 全部 Pass | `hs-vd-proc=Pass` 且 `hs-pwr=Asleep` | **不是故障**：头显睡着 | 戴上头显 |
| **A12** | 全部 Pass | `hs-vd-exit=Block` | **头显侧**：客户端崩溃 | 看 logcat 尾 |
| **A13** | 全部 Pass | `hs-wifi-perf=Warn` | **瓶颈在头显 Wi-Fi**：频段 < 4000MHz 或协商速率 < 450Mbps | 切 5GHz/6GHz 或靠近 AP |
| **A14** | 全部 Pass | `hs-vd-pkg=Block`（版本不匹配） | **版本硬门**：Streamer 比头显新 | 先升头显客户端 |
| **A15** | 全部 Pass | 头显侧全部 Pass | **归因落到发现面**：网络与客户端都正常，问题在 PC 侧的**发现/配对**逻辑——`ShowPairingRequests=false`、账号密文不匹配、或 38860 周期广播没发出去 | 看第一屏 `streamer-settings` 与 `net-discovery` |

**A15 是最有价值的一条**：它是「所有检测都绿但就是不连」的唯一出口。
没有 A15，用户拿到的就是「全绿，然后呢？」。

### 5.3 结论的数据结构

复用 `Model/Health.cs` 的形状，不新造：

```csharp
public sealed record AttributionResult(
    string RuleId,            // "A8"
    CheckStatus Severity,     // Block / Warn / Pass
    string Verdict,           // "不在同一网段"
    string Why,               // 一句话，带具体数字
    string NextStep,          // 用户下一步
    IReadOnlyList<string> EvidenceKeys);   // 指回 pcReport / snapshot 的 key
```

### 5.4 `CheckResult` 的最小扩展

`Health.cs` 的 `CheckResult` 加一个字段，让 UI 能分栏：

```csharp
public sealed record CheckResult(
    string Id, CheckStatus Status, string Summary, string Detail,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<FixAction> Fixes, string? Guidance = null,
    CheckScope Scope = CheckScope.Pc)      // ← 新增：Pc | Headset | Cross
{
}
```

一个枚举字段，不是新类型 —— 第一屏的渲染路径一行不改。

---

## 6. UI 布局（沿用官方视觉语言）

### 6.1 复用现成资源，零新增主题

`App.xaml` 已经全部到位（对照 `01-ui-spec.md` §4）：

| 官方规格（`01-ui-spec.md`） | `App.xaml` 现成键 |
|---|---|
| §4.2 三层近黑底 `#1D1D1D`/`#151515`/`#101010` | `TitleBrush` / `BodyBrush` / `ContentBrush` |
| §4.3 Verdana 14.667 | `UiFont` / `BodySize` |
| §4.3 小节标题 17 Bold | `H1` |
| §4.3 次级说明 | `Muted` |
| §4.4 控件高 30 | 隐式 `Button` Style `Height=30` |
| §4.5 按下蓝边 `#2E79BD` | `AccentBrush`（隐式 Button 的 `IsPressed` 触发器已用） |
| §2.3 左侧 160×48 导航 | `NavTab` |
| 三态色 | `PassBrush` / `WarnBrush` / `BlockBrush` + `StatusBrush` 转换器 |
| §4 原始输出等宽 | `Mono`（Consolas 11.5）—— **第三屏的 adb 原文用它** |

**唯一需要新增**：一个 `Card` Style（`Border` + `Background=ContentBrush` + `BorderBrush=LineBrush` + `CornerRadius=3`），
用来放归因结论卡。官方 ABOUT 页的标签-值两列表（`01-ui-spec.md` §3 Tab 5）是内联 Grid，没有现成 Style 可抄，
但配色全部命中现有键。

### 6.2 XAML 草案

```xml
<!-- Views/HeadsetView.xaml -->
<UserControl x:Class="VdHelper.Views.HeadsetView" ...>
  <!-- 官方约定：tab 内容根 Margin="24 8 8 8"，背景 #101010（01-ui-spec.md §3） -->
  <Grid Background="{StaticResource ContentBrush}" Margin="24,8,8,8">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />   <!-- 0 连接条 -->
      <RowDefinition Height="Auto" />   <!-- 1 归因卡 -->
      <RowDefinition Height="Auto" />   <!-- 2 无线告警（条件） -->
      <RowDefinition Height="*" />      <!-- 3 检查列表 -->
      <RowDefinition Height="Auto" />   <!-- 4 按钮行 -->
    </Grid.RowDefinitions>

    <!-- 行 0：连接状态条。官方 CheckBox 两段式排版（主标题 + 灰色副文本），
         这里照搬到状态条：主行讲状态，副行讲 adb 来源与串号掩码。 -->
    <Grid Grid.Row="0" Margin="0,0,0,10">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
      </Grid.ColumnDefinitions>
      <Border Padding="8,2" CornerRadius="3" Background="White"
              BorderBrush="{StaticResource LineBrush}" BorderThickness="1">
        <TextBlock FontSize="12" FontWeight="SemiBold"
                   Text="{Binding LinkText}" Foreground="{Binding LinkStatus, Converter={StaticResource StatusBrush}}" />
      </Border>
      <TextBlock Grid.Column="1" Margin="10,0,0,0" Style="{StaticResource Muted}"
                 Text="{Binding LinkDetail}" VerticalAlignment="Center" />
      <TextBlock Grid.Column="2" Style="{StaticResource Mono}" Text="{Binding AdbSourceLabel}" />
    </Grid>

    <!-- 行 1：归因卡。官方 ABOUT 页（01-ui-spec.md §3 Tab 5）的标签-值两列范式，
         首行改为大字结论。左侧 3px AccentBrush 竖条呼应 NavTab 的选中指示。 -->
    <Border Grid.Row="1" Background="{StaticResource HoverBrush}" CornerRadius="3"
            BorderBrush="{StaticResource LineBrush}" BorderThickness="1"
            Border.LeftThickness="3" Margin="0,0,0,10">
      <Grid Margin="14,10">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="180" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <StackPanel>
          <TextBlock Style="{StaticResource H1}" Text="{Binding Verdict}" />
          <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,0"
                     Text="{Binding RuleTag}" />
        </StackPanel>
        <StackPanel Grid.Column="1" Margin="16,0,0,0">
          <TextBlock Style="{StaticResource Muted}" Text="{Binding Why}" />
          <TextBlock Style="{StaticResource Muted}" Margin="0,6,0,0" FontWeight="SemiBold"
                     Foreground="{StaticResource InkBrush}" Text="{Binding NextStep}" />
        </StackPanel>
      </Grid>
    </Border>

    <!-- 行 2：无线 ADB 明文通道告警（playbook §7 原文进 UI）。
         仅当设备是 ip:port 形态时可见 → BoolToVisible。 -->
    <Border Grid.Row="2" Visibility="{Binding IsWireless, Converter={StaticResource BoolToVisible}}"
            Background="#2A1F12" BorderBrush="{StaticResource WarnBrush}" BorderThickness="1"
            CornerRadius="3" Margin="0,0,0,10">
      <TextBlock Style="{StaticResource Muted}" Margin="12,8" Text="{Binding WirelessWarning}" />
    </Border>

    <!-- 行 3：检查列表。列结构照 AppsCheckerWindow 的 32/180/*/62
         （01-ui-spec.md §8.3），但第三屏不需要「忽略」列，
         改成 72（状态徽章）/ *（摘要）/ Auto（类别）。 -->
    <ListView Grid.Row="3" ItemsSource="{Binding Rows}" Background="Transparent" BorderThickness="0"
              ScrollViewer.HorizontalScrollBarVisibility="Disabled">
      <ListView.ItemTemplate>
        <DataTemplate>
          <Expander Margin="0,0,0,8" Background="Transparent" BorderThickness="0">
            <Expander.Header>
              <Grid Background="Transparent">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="72" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <!-- 徽章：与第一屏 HealthView.xaml:26-31 完全同一段 XAML -->
                <Border Grid.Column="0" VerticalAlignment="Center" Padding="8,2" CornerRadius="3"
                        Background="White" BorderBrush="{StaticResource LineBrush}" BorderThickness="1">
                  <TextBlock FontSize="12" FontWeight="SemiBold"
                             Text="{Binding Status, Converter={StaticResource StatusText}}"
                             Foreground="{Binding Status, Converter={StaticResource StatusBrush}}" />
                </Border>
                <TextBlock Grid.Column="1" VerticalAlignment="Center" TextWrapping="Wrap"
                           Margin="10,0,0,0" FontWeight="SemiBold" Text="{Binding Summary}" />
                <TextBlock Grid.Column="2" VerticalAlignment="Center" Style="{StaticResource Muted}"
                           Text="{Binding Category}" />
              </Grid>
            </Expander.Header>
            <!-- 展开内容与第一屏同构；唯一新增：Evidence 用 Mono 等宽（adb 原文） -->
            <StackPanel Margin="24,6,0,0">
              <TextBlock Style="{StaticResource Muted}" Text="{Binding Detail}" />
              <TextBlock Style="{StaticResource Mono}" Margin="0,8,0,0" Text="{Binding Evidence}" />
              <TextBlock Style="{StaticResource Muted}" Margin="0,8,0,0"
                         Text="{Binding GuidanceText, StringFormat=指引：{0}}"
                         Visibility="{Binding HasGuidance, Converter={StaticResource BoolToVisible}}" />
            </StackPanel>
          </Expander>
        </DataTemplate>
      </ListView.ItemTemplate>
    </ListView>

    <!-- 行 4：按钮行。官方状态栏按钮 Padding="10 0"、图标文字间距 8
         （01-ui-spec.md §2.4），这里简化为文字按钮 + 8 间距。 -->
    <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
      <Button Content="重新采集" Click="OnRefresh" />
      <Button Content="导出端到端报告" Margin="8,0,0,0" Click="OnExport" />
    </StackPanel>
  </Grid>
</UserControl>
```

### 6.3 接入 ShellWindow

`ShellWindow.xaml:48-52` 现在只有一个 `TabItem`。加第二个：

```xml
<TabItem Header="头显诊断" Style="{StaticResource NavTab}">
    <views:HeadsetView />
</TabItem>
```

`TabControl` 隐式 Style 已把导航条和内容区都画好了（`App.xaml` 里的 `TabControl` 模板），
**不需要改任何模板代码**。

### 6.4 视觉自检清单（照 `01-ui-spec.md` §10）

- [ ] tab 内容根 `Background=#101010` `Margin="24 8 8 8"`
- [ ] 所有控件 `Height=30`（Button 隐式已保证）
- [ ] 小节标题 14 Bold（这里用 17 的 `H1` 放归因结论，属刻意强调）
- [ ] 次级说明一律 `MutedBrush`（`#9A9A9A`，官方是 `#808080`，我们略亮以适配深底）
- [ ] 中文字体回退：`Verdana` 不含中文字形 → `App.xaml:38` 已是 `<FontFamily x:Key="UiFont">Verdana</FontFamily>`，
      **中英混排基线会跳**，`01-ui-spec.md` §4.3 建议改 `Verdana,Microsoft YaHei UI`（本轮不改，另记）

---

## 7. 端到端报告（第三屏的出口）

### 7.1 一份，不两份

用户要的是**一份**报告同时包含 PC 侧与头显侧。repo 的双档（share-safe / private-full）思路保留，
但档位作用在**一份报告**上。

### 7.2 结构（照 `QuestAdbWebUi.cs:1863-1892` 的骨架，换掉内容）

```text
<title>          Virtual Desktop 串流体检报告 — {SHARE-SAFE | PRIVATE FULL}
<style>          内联，零外部依赖（repo:1873-1874 的做法）
<header>         报告编号 VD-yyyyMMdd-HHmmss / 生成时间 / 隐私印章 / adb 来源（AdbSourceLabel）
<section>        ★ 端到端归因结论   ← 新增。归因表 + 每条规则的证据指针
<section>        PC 侧检查（第一屏 17 项）  三列表：字段 | 值 | 证据来源
<section>        头显侧检查（第三屏 10 项）三列表
<section>        推断边界（repo:1887 的做法）
                 「本报告不能证明：Streamer 未广播的原因、头显能否成功串流、账号归属」
<section>        原始输出附录 <details>（repo:1911-1923）
                 PC 侧 PowerShell 原文 + 头显侧 adb 原文，safe 版跳过 logcat
<footer>         库来源 + 许可声明
```

### 7.3 三列 DSL 直接抄

```csharp
// 移植自 dwgx/Quest-ADB-Dashboard src/QuestAdbWebUi.cs:1895-1909（MIT, Copyright (c) 2026 dwgx1337）
// defs 格式 "key|标签|证据来源"
static void AddFacts(StringBuilder sb, string title,
                     IReadOnlyList<CheckResult> results, bool safe, Redactor redact)
{
    sb.Append("<section><h2>").Append(H(title)).Append("</h2>"
        + "<table><thead><tr><th>字段</th><th>值</th><th>证据来源</th></tr></thead><tbody>");
    foreach (var r in results)
        foreach (var (k, v) in r.Evidence.Where(kv => !kv.Key.StartsWith('_')))
        {
            var val = safe ? redact(v) : v;   // ← safe 档逐值过脱敏，不是整篇一刀切
            sb.Append("<tr><td>").Append(H(k)).Append("</td><td>").Append(H(val))
              .Append("</td><td>").Append(H(r.Id)).Append(" " + H(r.Status.ToString()))
              .Append("</td></tr>");
        }
    sb.Append("</tbody></table></section>");
}
```

**与原实现的一处行为差异**（有意为之）：原实现 `safe` 档在 `AddInvoiceFacts` 里对**每个字段值**调 `Privacy(val, true)`（`:1905`），
在 `AddInvoiceRaw` 里对**整段输出**调 `Redact(text, snap)`（`:1918`）。我们保持同一策略 —— 逐值脱敏，
不整篇替换，避免把证据链一起抹掉。

### 7.4 脱敏（移植 `Redact`）

`QuestAdbWebUi.cs:2210-2228` 的 9 条正则全收，**再补 3 条我们特有的**：

| 新增 | 理由 |
|---|---|
| PC 侧 IPv4 / 网关 | 我们的报告新增了 PC 网卡地址，repo 没有 |
| `Accounts` 密文片段（`AQAAANCMnd8B…`） | playbook §6：`StreamerSettings.json` 里的账号是加密 blob，share-safe 不该带 |
| 完整 `ServiceLog.txt` 尾部 | 本机日志里含 Windows 路径与 SID 形态串 |

**兜底**：safe 档报告页脚固定印一行「Redaction is best-effort. 发布前请人工复核」
（`docs/EXPORT_REPORTS.md` 原文这句话是对的，照抄）。

### 7.5 许可声明（必须印）

报告 `<footer>` 固定一行：

```
头显采集部分改编自 dwgx/Quest-ADB-Dashboard（MIT License, Copyright (c) 2026 dwgx1337）
```

MIT 不要求在运行时输出里署名，但成本为零，且这行让报告本身成为合法的「拷贝或实质性部分」。

---

## 8. 未验证清单

| 项 | 需要什么才能确证 |
|---|---|
| `[未验证]` N1–N2/V1–V6 的**真机输出形态** | 接真头显。playbook §8 已列全；本轮无头显 |
| `[未验证]` `dumpsys wifi` 在 HorizonOS 上能否读到 `frequency`/`LinkSpeed` | 同上。playbook §3.3 明确标注 HorizonOS 可能受限，届时降级 `cmd wifi status`（该命令可用性本身也未验证） |
| `[未验证]` `ro.vros.build.version` 在 Quest 2/3 各固件上是否存在 | 接真机。当前依据是 decompiled `VrApp.cs:92-107` 的读取代码，**不是实测输出** |
| `[未验证]` `dumpsys activity exit-info <pkg>` 的 HorizonOS 输出形态 | Android 10+ 标准命令，Quest 上形态未实测 |
| `[未验证]` 17 条并行在真机上的实际耗时 | 预算设 4.5s 是从 repo 的逐条超时（`:1726-1754`，串行相加约 200s）除出来的**估计值**，未实测 |
| `[未验证]` 无线 ADB 下 `dumpsys` 的可用性 | playbook §7 说无线通道是明文的，但受限程度未知 |

---

## 9. 实施顺序（建议）

| 步 | 交付 | 依赖 |
|---|---|---|
| 1 | `AdbLocator` + `AdbRunner` + `AdbLocator` 版本门槛 | 无 |
| 2 | `HeadsetProbe` 17 条 + `HeadsetSnapshot` | 步 1 |
| 3 | `HeadsetChecks` 前 6 项（`hs-link`/`hs-adb`/`hs-net-*`/`hs-wifi-perf`） | 步 2 |
| 4 | `Attribution` 规则 A1–A9（只依赖 PC 侧 + 网络层头显检查） | 步 3 |
| 5 | `HeadsetView` + 接入 `ShellWindow` | 步 3 |
| 6 | `HeadsetChecks` 后 4 项（VD 包/权限/进程/退出） | 步 2 |
| 7 | 归因规则 A10–A15 | 步 6 |
| 8 | `Redactor` + `EndToEndReport` | 步 4 |
| 9 | `Fixes.cs` 补 `DeniedSetting` 同款黑名单（移植 `:2245-2252`） | 步 8 |

**步 1–3 就能独立交付价值**（头显网络层 + 「是不是 PC 的问题」判据），不必等归因引擎全量。