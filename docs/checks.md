# 检测项清单（自动生成）

> 由 `tools/export-checks.ps1` 从 `VdHelper.exe --selftest` 的真实运行结果生成，**不要手工编辑**。
> 生成时间以 git 提交为准；本机实测退出码 4（0=可串流 3=有隐患 4=阻断）。

本机最近一次结论：VDHelper selftest  verdict=Blocked  阻断：有检查项失败，串流很可能起不来

## 本机实测结果

| 状态 | 编号 | 结论 |
| --- | --- | --- |
| 通过 | **net-primary** | 1 块网卡可用，主用 Ethernet (192.168.11.2) |
| 警告 | **net-apipa** | 3 块离线网卡持有 APIPA 地址（Ethernet 2、Wi-Fi、Bluetooth Network Connection） |
| 警告 | **net-virtual** | 2 块虚拟网卡启用中：vEthernet (Default Switch)、vEthernet (WSL (Hyper-V firewall)) |
| 通过 | **net-profile** | 主网卡网络类别：专用网络 (Private) |
| 通过 | **fw-vd** | 找到入站放行规则（Virtual Desktop Streamer    True   Inbound  Allow） |
| 警告 | **fw-defender** | Defender 防火墙：Domain     True / Private   False / Public    False |
| 通过 | **svc-vd** | VirtualDesktop.Service.exe Running Automatic |
| 通过 | **port-vd** | 四个 VD 端口都空闲 |
| 阻断 | **streamer-proc** | Streamer 进程没有运行——PC 侧不会广播，也不会监听串流端口 |
| 阻断 | **svc-log** | 服务日志有 5 条 ERROR，最近一次 2026-09-18 15:37:35.6962 |
| 警告 | **udp-discovery** | UDP 38850/38860 都没有活动 |
| 阻断 | **cfg-streamer** | ShowPairingRequests=false：新头显的配对请求会被静默忽略；DontWarnApps 含 NetworkProfile：官方自己的网络告警被屏蔽了 |
| 警告 | **ics** | SharedAccess(ICS)：Running |
| 通过 | **fw-outbound** | 出站策略：Domain          NotConfigured / Private         NotConfigured / Public          NotConfigured |
| 通过 | **av** | 已注册杀软：Windows Defender :: 401664 |
| 通过 | **route-metric** | 有线网卡优先级 10，没有虚拟网卡排在它前面 |
| 通过 | **link-type** | PC 走有线（Ethernet） |

## 检查项定义与来源

检测项的完整定义（症状 / 检查命令 / 判据 / 修复动作 / 回滚 / 风险）见
`research/02-network-diagnosis/02-pc-checklist.md`，实现见 `src/VdHelper/Core/Health/`。

参数项（111 个，含 18 个只读）见 `research/04-streamer-settings/01-config-keys.md`，
由 `tools/extract-parameters.py` 生成到 `src/VdHelper/Resources/parameters.json`。

头显侧判定规则见 `research/06-adb-headset/03-symptom-decision-table.md`。
