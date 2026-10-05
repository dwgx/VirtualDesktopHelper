# VDHelper

Windows 端 **Virtual Desktop 串流检测 / 诊断 / 修复工具**。

面向使用**去联网鉴权、去 Quest 账号鉴权**的 patched Virtual Desktop 客户端的玩家。
这类基线最常见的失败不是画面，而是「**头显找不到 PC / PC 发现不到头显**」——被防火墙、网络配置文件、
虚拟网卡、AP 隔离、服务身份错误挡住�。VDHelper 把这一整层做成可检测、可解释、可回滚的工具。

## 三个界面

| 屏 | 回答什么 | 需要什么 |
| --- | --- | --- |
| 本机体检 | PC 侧网卡 / 防火墙 / 服务 / 配置有没有断链 | 无 |
| 串流参数 | Streamer 111 个配置键的当前值与含义 | 无 |
| 头显诊断 | 通过 adb 读头显的包、权限、网络 | 头显 USB 连接并授权 |

顶部常驻总判定：**可串流 / 有隐患 / 阻断**。

## 它解决的真问题（都是本机实测）

- **服务在跑 ≠ Streamer 起得来**：本机 `ServiceLog.txt` 反复记录
  `HRESULT -2147024891 configured identity is incorrect`。此时所有网络项都显示正常，但 PC 永远不广播。
- **配对请求被静默忽略**：`StreamerSettings.json` 里 `ShowPairingRequests=false`，
  新头显搜索电脑时不会有任何提示。
- **官方自己的告警被屏蔽**：`DontWarnApps` 含 `NetworkProfile`。
- **离线网卡持有 APIPA**：本机 3 块 Down 状态网卡各持一个 `169.254.x.x`，干扰发现与选路。
- **虚拟网卡排在物理网卡前面**：广播会走错出口（Hyper-V / WSL / VPN）。
- **Defender 防火墙 Private/Public profile 被关闭**：规则存在 ≠ 防火墙开着。

## 用法

```bat
tools\publish.ps1            REM 构建自包含单文件 + SHA256SUMS + VERSION
dist\v0.1.0\VdHelper.exe     REM 直接双击用
```

无头自检（CI 与脚本用）：

```powershell
VdHelper.exe --selftest --out report.txt   # 退出码 0=可串流 3=有隐患 4=阻断 5=运行失败
```

开发：

```powershell
dotnet build src/VdHelper/VdHelper.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools\export-checks.ps1   # 重新生成 docs/checks.md
```

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
research/        11 个主题的调研落盘区（结论带 file:line / 命令输出 / URL 证据）
notes/           长期笔记：本机基线、ADR
handoff/         跨会话交接
docs/checks.md   检测项清单（由真实运行结果自动生成）
tools/           探测 / 发布 / 文档生成脚本
```

与 Virtual Desktop, Inc. 无隶属关系。学习与互操作性工具。