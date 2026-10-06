# 检测项清单（自动生成）

> 由 `tools/export-checks.ps1` 从 `VdHelper.exe --selftest` 的真实运行结果生成，**不要手工编辑**。
> 生成时间以 git 提交为准；本机实测退出码 3（0=可串流 3=有隐患 4=阻断）。

本机最近一次结论：VDHelper selftest  verdict=AtRisk  串流中：4 个通道的会话已建立（对端 192.168.11.14:37455）；下面 11 项隐患不影响当前这一局，但下次连接前值得看一眼

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
| 警告 | **session-stale** | 4 个到头显的通道仍是已建立状态（最早建立于 2 小时 56 分前） |
| 通过 | **streamer-proc** | Streamer 进程运行中（1 个） |
| 警告 | **svc-log** | 服务日志有 5 条历史 ERROR（最近一次 2026-09-18 15:37:35.6962），但 Streamer 正在运行 |
| 通过 | **udp-discovery** | 串流中：到头显的通道已建立，Streamer 已释放发现端口（正常） |
| 警告 | **cfg-streamer** | ShowPairingRequests=false：靠弹窗配对新头显会被静默忽略（靠名字在客户端选则不受影响）；DontWarnApps 含 NetworkProfile：官方自己的网络告警被屏蔽了；AutoAdjustBitrate=false：自动调码率已关，卡在「measuring bandwidth」时社区的首选解法就是把它打开 |
| 警告 | **ics** | SharedAccess(ICS)：Running |
| 通过 | **fw-outbound** | 出站策略：Domain          NotConfigured / Private         NotConfigured / Public          NotConfigured |
| 通过 | **av** | 已注册杀软：Windows Defender（productState 原值见下方原始输出，未解码：那是各版本含义不一的位掩码） |
| 通过 | **route-metric** | 有线网卡优先级 10，没有虚拟网卡排在它前面 |
| 未知 | **usb-headset** | 这台电脑上没有枚举到 USB 方式的头显 |
| 通过 | **link-type** | PC 走有线（Ethernet） |
| 通过 | **lan-reach** | 头显 192.168.11.14 可达（ping 1 ms，首次即应答） |
| 通过 | **link-rate** | Ethernet 协商速率 1 Gbps |
| 通过 | **vpn-proc** | 没有发现 VPN/代理客户端进程 |
| 通过 | **rdp-session** | 只有本机 console 会话（1 条），没有远程桌面在跑 |
| 通过 | **nat-type** | 路由器响应 UPnP 控制面（UPnP / SSDP），且当前有 38810 的入站映射；**NAT 类型这一项没有测**（需要映射行为探测或 STUN，本检查只读了两项）；外网 IP 是 172.16.80.42（私有段），上级还有一层 NAT——这只影响异地连接，不影响同网段 |
| 通过 | **fw-pair** | 入站放行有效（Virtual Desktop Streamer），没有针对 VD 的出站拦截 |
| 通过 | **accounts-persisted** | 配对信息已落盘（OculusQuest 1 条、Oculus 3 条），设备名 Meta Quest 3 |
| 警告 | **fw-profile-inbound** | profile 默认入站：Domain     True        NotConfigured / Private   False        NotConfigured / Public    False        NotConfigured |
| 警告 | **gpu-pick** | Virtual Desktop Monitor |
| 警告 | **proc-tuner** | 按进程名命中硬件调校类 7 个（未测 CPU 占用或线程优先级，只按名字匹配） |
| 通过 | **nic-powersave** | 没有网卡关闭了网络唤醒 |
| 警告 | **display-inventory** | Virtual Desktop Monitor(Error) |
| 通过 | **cfg-version** | Streamer 版本 1.34.22.0 |
| 通过 | **gpu-encoder** | 本机此刻有 1 个 NVENC 硬件编码会话 |
| 通过 | **gpu-throttle** | GPU 跑在最高频率的 80%，占用 77%、温度 75°C。驱动没有报任何降频原因——不是被压着。 |
| 未知 | **wifi-quality** | 无线未连接（disconnected），这一项没有测到任何链路数据 |
| 通过 | **net-loss** | 没有测到丢包（快速采样 8 次） |

## 检查项定义与来源

检测项的完整定义（症状 / 检查命令 / 判据 / 修复动作 / 回滚 / 风险）见
`research/02-network-diagnosis/02-pc-checklist.md`，实现见 `src/VdHelper/Core/Health/`。

参数项（111 个，含 19 个只读，其中 18 个是配对密文）见 `research/04-streamer-settings/01-config-keys.md`，
由 `tools/extract-parameters.py` 生成到 `src/VdHelper/Resources/parameters.json`。

头显侧判定规则见 `research/06-adb-headset/03-symptom-decision-table.md`。
