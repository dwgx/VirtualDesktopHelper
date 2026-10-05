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
| 通过 | **port-vd** | 4 个端口由 Virtual Desktop 持有，没有被别的程序抢占 |
| 阻断 | **session-stale** | 1 个通道是残留套接字（最早建立于 14 分钟前） |
| 通过 | **streamer-proc** | Streamer 进程运行中（1 个） |
| 警告 | **svc-log** | 服务日志有 5 条历史 ERROR（最近一次 2026-09-18 15:37:35.6962），但 Streamer 正在运行 |
| 通过 | **udp-discovery** | UDP 38850 正在监听（发现/配对协议就绪） |
| 警告 | **cfg-streamer** | ShowPairingRequests=false：靠弹窗配对新头显会被静默忽略（靠名字在客户端选则不受影响）；DontWarnApps 含 NetworkProfile：官方自己的网络告警被屏蔽了 |
| 警告 | **ics** | SharedAccess(ICS)：Running |
| 通过 | **fw-outbound** | 出站策略：Domain          NotConfigured / Private         NotConfigured / Public          NotConfigured |
| 通过 | **av** | 已注册杀软：Windows Defender :: 397568 |
| 通过 | **route-metric** | 有线网卡优先级 10，没有虚拟网卡排在它前面 |
| 通过 | **link-type** | PC 走有线（Ethernet） |
| 阻断 | **lan-reach** | 头显 192.168.11.14 ping 不通 |
| 通过 | **link-rate** | Ethernet 协商速率 1 Gbps |
| 通过 | **vpn-proc** | 没有发现 VPN/代理客户端进程 |
| 通过 | **rdp-session** | 只有本机 console 会话（1 条），没有远程桌面在跑 |
| 通过 | **nat-type** | 路由器支持 UPnP（UPnP / SSDP），NAT 类型 Open；外网 IP 是 172.16.80.42（私有段），上级还有一层 NAT——这只影响异地连接，不影响同网段 |
| 通过 | **fw-pair** | 入站放行有效（Virtual Desktop Streamer），没有针对 VD 的出站拦截 |
| 通过 | **accounts-persisted** | 配对信息已落盘（OculusQuest 1 条、Oculus 3 条），设备名 Meta Quest 3 |

## 检查项定义与来源

检测项的完整定义（症状 / 检查命令 / 判据 / 修复动作 / 回滚 / 风险）见
`research/02-network-diagnosis/02-pc-checklist.md`，实现见 `src/VdHelper/Core/Health/`。

参数项（111 个，含 18 个只读）见 `research/04-streamer-settings/01-config-keys.md`，
由 `tools/extract-parameters.py` 生成到 `src/VdHelper/Resources/parameters.json`。

头显侧判定规则见 `research/06-adb-headset/03-symptom-decision-table.md`。
