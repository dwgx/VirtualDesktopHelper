# VDHelper

Windows 端 **Virtual Desktop 串流检测 / 诊断 / 修复工具**。

面向使用**去联网鉴权、去 Quest 账号鉴权**的 patched Virtual Desktop 客户端的玩家。
这类基线最常见的失败不是画面，而是「**头显找不到 PC / PC 发现不到头显**」——被防火墙、网络配置文件、
虚拟网卡、AP 隔离、服务身份错误挡住�。VDHelper 把这一整层做成可检测、可解释、可回滚的工具。

## 三个界面

| 屏 | 回答什么 | 需要什么 |
| --- | --- | --- |
| 本机体检 | PC 侧网卡 / 防火墙 / 服务 / 配置 / GPU 有没有断链（判定项 **36 项**） | 无 |
| 串流参数 | Streamer 111 个配置键的当前值与含义（DPAPI 密文不显示） | 无 |
| 头显诊断 | 通过 adb 读头显的包、权限、网络，**并判断应用进程是否还活着** | 头显 USB 连接并授权 |
| ↑ 第三屏也可以直接命令行跑：`VdHelper.exe --adb`，输出可直接贴进 issue，不必截图 |

顶部常驻总判定：**可串流 / 有隐患 / 阻断**。

体检第一屏最上面不是 36 行列表，而是 **「接下来做什么」**：失败项排最前，
可修的其次，只是解释的再次。判定本身永远由 PC 侧那 36 项算出，筛选不改变结论。

旁边还有一个 **「深度探测丢包」** 按钮（等价于命令行 `--deep`）：
主体检只做 8 次快速采样，因为主体检的耗时由最慢那一项决定；
画面卡但各项都绿的时候，按它跑 20 次，同时测默认网关与头显 IP。

**耗时取决于有没有目标不应答**：两个目标都秒回时约 **4–5 秒**——采样之间固定停 100 ms
（`LossProbe.cs:60` 无条件 `Task.Delay(100)`），20 次采样 × 2 个目标，光停顿就是 4.0 秒下界；
本机实测 4398 ms。有目标不应答时，每目标上界约 **18 秒**（20 × (800 ms 超时 + 100 ms 停顿)）。
网关也丢是 PC 到路由器这一段，只有头显丢是 Wi-Fi 那一段。

## 真正查出来的东西（都是本机实测，不是推测）

- **Virtual Desktop Monitor 显示驱动是禁用的**：`ConfigManagerErrorCode=22`（ERROR_DISABLED）。
  两个独立来源（CIM 与 `Get-PnpDevice`）给出同一结论。这一条正好落在「连上但没画面」上。
  工具只报不改——改显示驱动可能让画面彻底出不来。
- **GPU 没跑在满频，但这不是问题**：频率在最高值的 74% 附近，占用只有 47–52%，
  驱动报的降频原因位域是 `0x0`（一条降频都没有），功耗 72 W。这一条以前写成「原因是功耗墙不是温度」——
  一个瓦都没量。现在 GPU 只要负载高、频率却上不去，而驱动说它没被限制，这一项会明写「原因不明」。
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
- **服务在跑 ≠ Streamer 起得来（已过去的一次故障）**：`ServiceLog.txt` 里留有 5 条历史
  `HRESULT -2147024891 configured identity is incorrect`，最近一条 2026-09-18。当时所有网络项都正常，PC 却不广播——
  **现在不是这个状态**：`svc-log` 报 Warn 并说明身份绑定已恢复，`udp-discovery` 报 Pass（38850 正在监听）。
- **官方自己的告警被屏蔽**：`DontWarnApps` 含 `NetworkProfile`。
- **离线网卡持有 APIPA**：本机 3 块 Down 状态网卡各持一个 `169.254.x.x`，干扰发现与选路。
- **虚拟网卡排在物理网卡前面**：广播会走错出口（Hyper-V / WSL / VPN）。
- **本机在双层 NAT 后面**：路由器支持 UPnP，但外网 IP 落在 `172.16.80.42`（私有段），
  异地连接必须先有映射。同网段不受影响。

## 先选症状，再看检测

第一屏顶部 7 个症状芯片，用的是社区里真实用户的原话
（`no computer found` / `computer is unreachable` / `stuck on measuring bandwidth` …）。
「看不见电脑」和「看得见连不上」的根因几乎不重叠，选对能省掉一半排查。

筛选不改变结论：`verdict` 永远由 PC 侧的 36 项算出——「筛一下就变绿」是一种骗人的修法。

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
连同第三屏的两项——`adb`（头显连上了、包/权限/进程活不活）与 `headset-deep`
（MAC 随机化、头显侧设置、头显侧 VPN）——**共注册 38 项**；前两项不参与判定，它们只在第三屏跑。

**脱敏口径**：不写任何 DPAPI 密文、令牌、账户条目内容——`Accounts` 只报分组名与条目数
（沿用 `FirewallPairChecks` 的口径，另有一道 `AQAA` 前缀兜底）。机器名、用户名与局域网地址**保留**：
没有它们，别人没法判断你的网络环境。贴到公开场合前请自己再看一眼。

报告还会点名「症状类列了、但本轮 PC 侧体检没有结果」的检测（如 `headset-deep`），
而不是悄悄略过——那一栏是 Unknown，不是通过。

## 下载直接跑（没有签名，会被拦一次）

release 里的 `VdHelper.exe` **没有代码签名**（`Get-AuthenticodeSignature` 报 `NotSigned`）。
SmartScreen 对没有签名的下载会拦一次，弹「Windows 已保护你的电脑」。**这是预期行为，不是文件坏了。**

**先核对哈希再放行**——包里的 `SHA256SUMS.txt` 就是给你做这一步的：

```bat
certutil -hashfile VdHelper.exe SHA256
```

对得上再点「更多信息」→「仍要运行」。**对不上就别运行**，把值贴进 issue。

自包含单文件，约 134 MB，不需要装 .NET。
## 用法

```bat
tools\publish.ps1            REM 构建自包含单文件 + SHA256SUMS + VERSION
dist\v0.8.0\VdHelper.exe     REM 直接双击用
```

`--set-param` 在 Streamer 运行时一律拒绝（它有 2 秒防抖保存，会覆盖外部写入）。要先改参数就跑 `--quit-streamer`——本机的 Streamer 以管理员权限运行，所以那条命令中途会弹一次 UAC。

所有命令行的用法。退出码写在下表里，脚本可以直接判：

| 命令 | 作用 | 退出码 |
|---|---|---|
| `VdHelper.exe` | 打开界面（三个标签页） | — |
| `--selftest [--out f] [--symptom S1..S7]` | 无头自检 | `0` 可串流 / `3` 有隐患 / `4` 阻断 / `5` 运行失败 |
| `--adb [--serial S]` | 头显侧，第三屏的命令行等价物 | `0` 正常 / `3` 有隐患 / `4` 未连上或不可用 |
| `--deep [--samples N]` | 丢包与抖动深度探测（默认每目标 20 次采样；实测 12 秒） | `0` 完成 |
| `--report f.md` / `--report-html f.html` | 写成能直接贴进 issue 的成品 | 同 `--selftest` |
| `--apply --list` | 列出本轮可自动修复的项 | `0` |
| `--apply <fixId>` | 执行一项修复（会先备份，可回滚） | `0` 成功 / `6` 失败 / `7` 需要管理员权限，未执行 / `9` 没有匹配的修复项 |
| `--report f.md --symptom ZZ` | 症状类写错了 | `2` 未知症状类（会列出可选的 S1–S7） |
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
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-exit-codes.ps1   # 退出码契约，只跑只读命令
python tools\check-citations.py                                             # 引用的 File.cs:NNN 必须能解析
python tools\check-issue-form.py
python tools\check-report-consistency.py                               # 报告内部不能自相矛盾
python tools\check-docs-coverage.py                                  # docs/checks.md 不能漏项
> **六个闸门都在 CI 里跑**（`.github/workflows/build.yml`）。后两道是今天加的：前一条修的是报告标题说「没有通道」而检测项说「有」——**那不是笔误，是 `_livePorts` 只从 fresh 子集取**，而两份审计都在读检测项、没有一个在读渲染成的文档。
> `check-exit-codes.ps1` 的只读清单是硬编码的，结构上到不了 `--quit-streamer` / `--apply <id>` /
> `--set-param <key> <value>`——原因见 `WORKFLOW.md` §10。
```

## 先读这三页

检测项全表、症状分诊表、社区错解逐条纠偏——都由脚本从调研源文件生成，数字不会和代码脱节。

> **下面三个是 `.html`。文档站已开在 https://dwgx.github.io/VirtualDesktopHelper/ ，内容与这三页同源**
> （`gh-pages` 分支，由 `tools/export-docs-site.ps1` 生成）。想直接看源码，点 [`docs/checks.md`](docs/checks.md)。
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
research/        15 个主题的调研落盘区（结论带 file:line / 命令输出 / URL 证据）
notes/           长期笔记：本机基线、ADR
handoff/         跨会话交接
docs/            三段式文档站（index / checks / faq），由脚本生成，数字不会和代码脱节
tools/           探测 / 发布 / 文档生成脚本
```

与 Virtual Desktop, Inc. 无隶属关系。学习与互操作性工具。