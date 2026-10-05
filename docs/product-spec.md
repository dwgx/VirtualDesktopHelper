# VDHelper 产品规格

> **状态：历史文档。** 这份草案写于 v0.1，当时只有 18 项检测、三个界面里有两个是空的。
> 现在的事实以 `README.md` 与 `docs/checks.md` 为准（后者由 `tools/export-checks.ps1` 从一次
> 真实运行生成）。留在这里是为了记录当初是怎么定范围的，不是当前规格。

一个 Windows 桌面工具，面向**使用 patched Virtual Desktop 基线的玩家**，回答一个问题：

> **「头显连不上我的电脑，到底卡在哪一环？能不能一键修？」**

## 1. 三个界面（ADR-005）

| 屏 | 名字 | 回答什么 | 需要的条件 |
| --- | --- | --- | --- |
| 1 | **本机体检** | PC 侧网络/防火墙/网卡/路由有没有断链 | 无（只读本机） |
| 2 | **串流参数** | Streamer 配置 + Quest 侧限制，逐项显示当前值与含义 | 无 |
| 3 | **头显诊断** | 通过 ADB 读头显状态、包、权限、日志 | 头显 USB 连接并授权 |

顶部常驻一条**总判定**：「可串流 / 有隐患 / 阻断」，来自屏 1 与屏 3 的结果合并。

## 2. 屏 1：本机体检

### 2.1 检测项（数据驱动，schema 见 ADR-002）

每一项展示：状态徽标（通过 / 警告 / 阻断 / 未知）、一句人话解释、展开看命令与原始输出。

预期覆盖族（详见 `research/02-network-diagnosis/02-pc-checklist.md`）：

1. 主网卡是否正常拿到局域网地址（不是 APIPA `169.254.*`）
2. 是否存在**多块持有 APIPA 的离线网卡**（本机中招：3 块）
3. Hyper-V / WSL / VPN 虚拟网卡是否介入
4. 网络配置文件是 Private 还是 Public
5. 官方防火墙入站规则是否存在、启用、profile 覆盖
6. Defender 防火墙三个 profile 是否都被第三方接管
7. VD 端口 38810/20/30/40 占用与监听状态
8. VD 服务是否运行
9. 路由器/AP 隔离（只能解释，给指引）
10. mDNS/广播是否被虚拟交换机吞掉

### 2.2 修复动作

| 动作 | 可自动 | 说明 |
| --- | --- | --- |
| 启用/重置失效网卡 | 是 | 先记录原状态，可回滚 |
| 清理 APIPA 地址（DHCP 续租/重新获取） | 是 | 失败则不强制改静态地址 |
| 新增缺失的防火墙入站规则 | 是 | 严格按官方规则模板，先导出备份 |
| 网络配置文件 Private ⇄ Public | 是 | 改前提示对其他应用的影响 |
| 关闭网卡节能 | 是 | 可回滚 |
| 关闭第三方杀软防火墙 | 否 | 只给指引与官方下载链接 |
| 路由器 AP 隔离 / VLAN / 双频分离 | 否 | **只解释**，给出用户该问路由器的三个问题 |

## 3. 屏 2：串流参数

- 分三组：**画质与码率 / 输入与音频 / 网络与高级**。
- 每行：名称、当前值（读到的真实值）、合法范围、来源（Streamer 配置键 / Quest preferences / Quest IL 常量）、一个「解释」链接。
- **不提供假开关**（ADR-004）。读不到的值显示来源说明，不给无效控件。
- 需要重启 Streamer 才生效的行，明确标 `需重启`。

## 4. 屏 3：头显诊断

- 顶部：adb 状态（未找到 / 未连接 / 已连接 `序列号 型号 Android版本`）。
- 分区：包与版本、进程、网络（IP/路由/DNS/proxy）、权限（7 项 runtime 权限逐项 ✓/✗）、日志判定。
- 一键动作：授全部 runtime 权限、拉取并过滤 VD 日志、把头显 IP 带入屏 1 的直连检测。

## 5. 技术骨架

```
src/VDHelper/
  VdHelper.csproj            net10.0-windows, WPF
  App.xaml(.cs)              资源字典（视觉语言取自官方 Streamer）
  Views/
    ShellWindow.xaml         三屏导航
    HealthPage.xaml          屏 1
    SettingsPage.xaml        屏 2
    HeadsetPage.xaml         屏 3
  Core/
    Checks/                  检测项执行器：PowerShell / NetApi / TcpProbe / Adb / ConfigRead
    Model/                   CheckResult, FixAction, HealthVerdict
    Fix/                     修复执行 + 备份 + 回滚
    Config/                  Streamer 配置读写、Quest preferences 读取
```

## 6. 不做的事

- 不做串流本身（那是官方 Streamer 的事）。
- 不做云账号、entitlement、证书（ADR-003 只读）。
- 不做 APK 分发；APK 相关只保留「检测头显上装的是不是基线版本」。

## 7. 验收标准（自我冒烟）

在本机（已知有 3 块 APIPA 网卡 + Hyper-V/WSL）运行：
屏 1 必须把 APIPA 与虚拟网卡报为**警告或阻断**，并给出可回滚的修复；修复后重测状态改变。
连上头显时屏 3 能读出包名/版本/IP 与 7 项权限状态。