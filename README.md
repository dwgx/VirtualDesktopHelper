# VDHelper

Windows 端 **Virtual Desktop 串流检测 / 诊断 / 修复工具**。

面向使用**去联网鉴权、去 Quest 账号鉴权**的 patched Virtual Desktop 客户端的玩家。
这类基线最常见的失败不是画面，而是「**头显找不到 PC / PC 发现不到头显**」——被防火墙、网络配置文件、
虚拟网卡、AP 隔离、服务身份错误挡住�。VDHelper 把这一整层做成可检测、可解释、可回滚的工具。

## 三个界面

| 屏 | 回答什么 | 需要什么 |
| --- | --- | --- |
| 本机体检 | PC 侧网卡 / 防火墙 / 服务 / 配置 / GPU 有没有断链（**36 项**） | 无 |
| 串流参数 | Streamer 111 个配置键的当前值与含义（DPAPI 密文不显示） | 无 |
| 头显诊断 | 通过 adb 读头显的包、权限、网络，**并判断应用进程是否还活着** | 头显 USB 连接并授权 |
| ↑ 第三屏也可以直接命令行跑：`VdHelper.exe --adb`，输出可直接贴进 issue，不必截图 |

顶部常驻总判定：**可串流 / 有隐患 / 阻断**。

体检第一屏最上面不是 36 行列表，而是 **「接下来做什么」**：失败项排最前，
可修的其次，只是解释的再次。判定本身永远由全部 36 项算出，筛选不改变结论。

旁边还有一个 **「深度探测丢包」** 按钮（等价于命令行 `--deep`）：
主体检只做 8 次快速采样，因为主体检的耗时由最慢那一项决定；
画面卡但各项都绿的时候，按它跑 20 次，同时测默认网关与头显 IP——
网关也丢是 PC 到路由器这一段，只有头显丢是 Wi-Fi 那一段。

## 真正查出来的东西（都是本机实测，不是推测）

- **Virtual Desktop Monitor 显示驱动是禁用的**：`ConfigManagerErrorCode=22`（ERROR_DISABLED）。
  两个独立来源（CIM 与 `Get-PnpDevice`）给出同一结论。这一条正好落在「连上但没画面」上。
  工具只报不改——改显示驱动可能让画面彻底出不来。
- **GPU 没跑在满频，原因是功耗墙不是温度**：频率在最高值的 76–89% 浮动，温度始终只有 52–56°C。
  两者的修法完全不同：过热要清灰垫高，功耗墙要查插电状态与驱动限功耗。
- **防火墙「关了」不等于「不拦」**：Private/Public profile 整个是关的，
  但三个 profile 的 `DefaultInboundAction` 都是 `NotConfigured`，语义上等于 Block。
- **发现端口被占，日志里查不到**：Streamer 用 `new UdpClient` 独占绑定 38850，绑不上时异常被静默吞掉。
  别的进程占了它，头显就会收到「找不到电脑」，而本机没有任何日志说明原因。工具会报出占用者与 PID。
- **配对请求被静默忽略**：`ShowPairingRequests=false` 时，PC 收到未知 token 的发现包会走一条
  一个字节都不回的分支（`-.112.cs:432-442`），弹窗又被这个开关关掉。
  工具现在能直接修这一项，并回读确认写成功了。
- **GPU 硬件编码器会话数**：`nvidia-smi` 能读到，但工具明说它**证明不了**串流走的是硬件还是软件编码。
- **无线链路本身**：头显在典型配置下就是走 Wi-Fi 连这台 PC 的，工具会报频段、信道、协商速率与信号，
  并单独标出 **DFS 信道（36–48）**——部分路由器会在雷达检测时短暂静默，表现成周期性卡顿，
  而有线指标全绿时根本看不出来。无线没连上时这一项报「未知」而不是「通过」：没测到不等于没问题。
- **服务在跑 ≠ Streamer 起得来**：本机 `ServiceLog.txt` 反复记录
  `HRESULT -2147024891 configured identity is incorrect`。此时所有网络项都显示正常，但 PC 永远不广播。
- **官方自己的告警被屏蔽**：`DontWarnApps` 含 `NetworkProfile`。
- **离线网卡持有 APIPA**：本机 3 块 Down 状态网卡各持一个 `169.254.x.x`，干扰发现与选路。
- **虚拟网卡排在物理网卡前面**：广播会走错出口（Hyper-V / WSL / VPN）。
- **本机在双层 NAT 后面**：路由器支持 UPnP，但外网 IP 落在 `172.16.80.42`（私有段），
  异地连接必须先有映射。同网段不受影响。

## 先选症状，再看检测

第一屏顶部 7 个症状芯片，用的是社区里真实用户的原话
（`no computer found` / `computer is unreachable` / `stuck on measuring bandwidth` …）。
「看不见电脑」和「看得见连不上」的根因几乎不重叠，选对能省掉一半排查。

筛选不改变结论：`verdict` 永远由全部 36 项算出——「筛一下就变绿」是一种骗人的修法。

无头用法：`VdHelper.exe --selftest --symptom S2 --out report.txt`，
用户可以把某一类症状的完整报告发出来当求助材料。

## 报告导出（可以直接发出去）

`--report` / `--report-html` 把一次体检写成**能贴进社区求助帖或 GitHub issue 的成品**，
而不是给自己看的控制台流水：

```powershell
VdHelper.exe --adb                             REM 头显侧，命令行版（第三屏的等价物）
VdHelper.exe --report out.md --symptom S2        REM Markdown，适合贴 issue
VdHelper.exe --report-html out.html --symptom S2  REM 单文件 HTML，原始输出折叠在 <details> 里
```

两种格式内容一致，都含：结论行（含「串流中 / 未串流」与通道端口、对端）、生成时间与机器标识、
按症状类分组的检测项（状态徽标 + 编号 + 一句结论 + 折叠的原始输出）、与上次的变化、免责声明。
不带 `--symptom` 时按 7 个症状类分组输出全部 36 项。

**脱敏口径**：不写任何 DPAPI 密文、令牌、账户条目内容——`Accounts` 只报分组名与条目数
（沿用 `FirewallPairChecks` 的口径，另有一道 `AQAA` 前缀兜底）。机器名、用户名与局域网地址**保留**：
没有它们，别人没法判断你的网络环境。贴到公开场合前请自己再看一眼。

报告还会点名「症状类列了、但本轮 PC 侧体检没有结果」的检测（如 `headset-deep`），
而不是悄悄略过——那一栏是 Unknown，不是通过。

## 用法

```bat
tools\publish.ps1            REM 构建自包含单文件 + SHA256SUMS + VERSION
dist\v0.3.0\VdHelper.exe     REM 直接双击用
```

`--set-param` 在 Streamer 运行时一律拒绝（它有 2 秒防抖保存，会覆盖外部写入）。要先改参数就跑 `--quit-streamer`——本机的 Streamer 以管理员权限运行，所以那条命令中途会弹一次 UAC。

所有命令行的用法。退出码写在下表里，脚本可以直接判：

| 命令 | 作用 | 退出码 |
|---|---|---|
| `VdHelper.exe` | 打开界面（三个标签页） | — |
| `--selftest [--out f] [--symptom S1..S7]` | 无头自检 | `0` 可串流 / `3` 有隐患 / `4` 阻断 / `5` 运行失败 |
| `--adb [--serial S]` | 头显侧，第三屏的命令行等价物 | `0` 正常 / `3` 有隐患 / `4` 未连上或不可用 |
| `--deep [--samples N]` | 丢包与抖动深度探测（默认 20 次采样，约 20 秒） | `0` 完成 |
| `--report f.md` / `--report-html f.html` | 写成能直接贴进 issue 的成品 | 同 `--selftest` |
| `--apply --list` | 列出本轮可自动修复的项 | `0` |
| `--apply <fixId>` | 执行一项修复（会先备份，可回滚） | `0` 成功 / `6` 失败 / `9` 没有匹配的修复项 |
| `--quit-streamer` | 结束 Streamer，好让参数能改 | `0` 已退出 / `6` 结束失败 |
| `--set-param <key> <json>` | 直接写一个配置键 | `0` 成功并已回读确认 / `2` 未知键 / `7` 只读或不在本机 / `8` Streamer 在运行 / `6` 写失败 |

`--report` / `--report-html` 排在 `--selftest` **之前**：两个一起给时，报告标志生效。
以前是 `--selftest` 先命中，于是 `--selftest --report issue.md` 只打到 stdout、**不写文件也不警告**——
在求助帖里就是一个空附件。指定文件是更明确的要求，所以它赢。两者跑的是同一个体检引擎，结论一致。

常用组合：

```powershell
VdHelper.exe --report-html issue.html --symptom S2   REM 最常被要的那一份
VdHelper.exe --apply --list                          REM 先看能修什么，再决定修不修
VdHelper.exe --set-param ShowPairingRequests true    REM 需要先退出 Streamer
```

开发：

```powershell
dotnet build src/VdHelper/VdHelper.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1        # 构建 + SHA256 + 自检
powershell -NoProfile -ExecutionPolicy Bypass -File tools\export-checks.ps1    # 重新生成 docs/checks.md
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-symptom-map.ps1   # 反漂移闸门，README 数字也会被它核对
```

## 先读这三页

检测项全表、症状分诊表、社区错解逐条纠偏——都由脚本从调研源文件生成，数字不会和代码脱节。

> **下面三个是 `.html`，在 GitHub 上看到的是源码，不是渲染后的页面**（本仓库还没开 Pages）。
> 想直接看内容，点 [`docs/checks.md`](docs/checks.md)（GitHub 会正常渲染），或本地跑一次
> `tools/export-docs-site.ps1` 再用浏览器打开。

- [常见问题 · 症状分诊 + 错解纠偏](docs/faq.html)
- [检测项全表 · 怎么查 / 怎么判 / 怎么退](docs/checks.html)
- [首页 · 本机实测结论](docs/index.html)

## 它不做什么

- 不做串流本身（那是官方 Streamer 的事）。
- **不伪造任何鉴权**（entitlement / token / UserProof / 签名证明一律不碰）。
- **不分发官方二进制**（APK / keystore / EXE 一律不入库）。
- 不自动关第三方杀软、不自动改路由器、不自动改接口 metric —— 这三类只解释、只给指引。
- 不提供「改了没用」的假开关：读不到的值会写明「由头显决定」。

## 仓库结构

```
AGENTS.md        主脑规则（自动加载）      WORKFLOW.md   派发与验收
src/VdHelper     工具本体（WPF, net10.0-windows）
research/        14 个主题的调研落盘区（结论带 file:line / 命令输出 / URL 证据）
notes/           长期笔记：本机基线、ADR
handoff/         跨会话交接
docs/            三段式文档站（index / checks / faq），由脚本生成，数字不会和代码脱节
tools/           探测 / 发布 / 文档生成脚本
```

与 Virtual Desktop, Inc. 无隶属关系。学习与互操作性工具。