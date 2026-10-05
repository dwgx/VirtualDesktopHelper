# 2026-10-05 一次真实运行抓到了什么

## 这份文档存在的理由

工具写了 34 项检测，但**检测项列表本身不能证明工具有用**。有用要看它在一台真实机器上
到底抓到了什么、漏了什么、以及有没有把没事说成有事。

本文记录一次完整真实运行的原始输出与复核过程。所有结论都能用文中命令重跑。

机器：Owner 的开发本机，`Virtual Desktop Streamer 1.34.22.0` 运行中。

<!-- checks-total: 34 -->
```
$ ./src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --selftest

VDHelper selftest  verdict=Blocked  阻断：有检查项失败，串流很可能起不来
34 checks, exit=4
```

## 一、两个 Block，都不是配置错误

这是这份文档里最重要的一条：**阻断级结论不等于机器坏了。**

| 检测项 | 结论 | 复核 |
| --- | --- | --- |
| `session-stale` | 1 个通道是残留套接字，最早建立于 2 小时 55 分前 | 同一个 socket 已不在活跃会话里，但 Windows 仍留着 Established |
| `lan-reach` | 头显 192.168.11.14 ping 不通，ARP 缓存里也没有它 | 同一次运行里 ARP 条目状态是 `Stale` |

两个都是**状态残留**而不是设置错误：

- 残留套接字 → 需要一次提权重启 Streamer 才清得掉（工具给 `RestartStreamer` 修复项）。
- 头显 → Quest 会休眠。当时它没在应答。**几分钟后重跑，同一项变成 `Pass（ping 78 ms）`**，
  ARP 条目从 `Stale` 变 `Reachable`。

一个把「设备睡着了」报成「网络不通」的工具，用三次就没人信了。这一条是工具按**证据形态**
分级（`Stale` / 无记录 / `Reachable`）才做到的，见 `src/VdHelper/Core/Health/ReachabilityCheck.cs`。

## 二、真正需要人处理的三条（都是 Warn）

### 1. `Virtual Desktop Monitor` 显示驱动被禁用

```
[Warn] gpu-pick          Virtual Desktop Monitor
[Warn] display-inventory Virtual Desktop Monitor(Error)
```

`ConfigManagerErrorCode = 22 = ERROR_DISABLED`，意思是设备被禁用，不是驱动缺失也不是故障。

**两个互相独立的来源给出同一结论：**

```
Get-CimInstance Win32_VideoController | ? Name -like '*Virtual Desktop*' | % { "$($_.Name)|$($_.Status)|$($_.ConfigManagerErrorCode)" }
Virtual Desktop Monitor|Error|22

Get-PnpDevice -Class Display | ? FriendlyName -like '*Virtual Desktop*' | % { "$($_.FriendlyName)|$($_.Status)" }
Virtual Desktop Monitor|Error
```

VD 自己那个虚拟显示器驱动处于禁用状态，而这一条正好落在**「连上但没画面 / 黑屏」**那一类症状上。

**工具只报不改**：禁用或启用一个显示驱动可能导致画面完全出不来，这个决定该由人在
设备管理器里看着屏幕做。

顺带记录一个自己踩的坑：`display-inventory` 第一版读 `Win32_DesktopMonitor`，在现代 Windows 上
拿到 3 条 `Default Monitor|OK|x` 的占位行，于是报告「4 个显示器全部正常」——近乎废话。
改成 `Get-PnpDevice -Class Display` 之后，它和 `gpu-pick` 才 independently 对上。

### 2. 防火墙：两个 profile 整个关着，另外三个 profile 默认入站都是 Block

```
[Warn] fw-profile-inbound profile 默认入站：Domain True NotConfigured / Private False NotConfigured / Public False NotConfigured
[Warn] fw-defender       Defender 防火墙：Domain True / Private False / Public False
```

两个独立事实，方向相反，都得说：

- **Private 与 Public profile 整个是关的**（`Enabled=False`）。
- **三个 profile 的 `DefaultInboundAction` 都是 `NotConfigured`**，语义上等于 Block。

所以「防火墙关了」并不等于「没有拦截规则」——profile 内的默认入站动作仍然是拦。
原先的检测只看 `Enabled`，所以漏掉了这一半；补上 `DefaultInboundAction` 之后才看全。

工具两条都只读不写：改防火墙要么被 UAC 挡住，要么把机器真的暴露出去。

### 3. GPU 没跑在满频，原因是功耗墙不是温度

```
[Pass] gpu-throttle  GPU 频率正常（2760/3090 MHz = 89%，56°C）
[Pass] gpu-encoder   硬件编码器空闲（当前 0 个编码会话）
```

多次采样之间这个比值在 76%–89% 之间浮动，温度始终只有 52–56°C。
**温度不高却上不了满频，是功耗墙而不是过热**，两者修法完全不同：过热要去清灰和垫高，
功耗墙要查插电状态、独显直连与驱动限功耗。

`gpu-encoder` 那条同时写明了它**证明不了**什么：编码会话数为 0 只说明此刻没人用，
不能推出 VD 串流时走的是硬件编码还是软件编码——那要看 Streamer 自己的日志。

## 三、抓到了但当前不必处理

| 检测项 | 结论 | 为什么先不动 |
| --- | --- | --- |
| `proc-tuner` | 硬件调校类命中 6 个（Armoury Crate 系列） | 工具**故意不自动停它**：停 Armoury Crate 会连带关掉风扇控制。列出来是因为它在社区里是「常规指标全绿但串流仍然卡」的常见真凶 |
| `cfg-streamer` | `AutoAdjustBitrate=false` | 语料里「卡在 measuring bandwidth」的首选解法就是打开它；但它是**配置意图**，不该由工具替你改 |
| `net-apipa` | 3 块离线网卡各持一个 `169.254.x.x` | 离线网卡拿不到 DHCP 就自己占一个链路本地地址。真实设备**全部可达**时这一条不该报——见下方「一处已修的缺陷」 |
| `net-virtual` | 2 块虚拟网卡启用中 | Hyper-V / WSL 在用。有线网卡 metric=10，没有虚拟网卡排在它前面，所以不影响路由 |
| `ics` | SharedAccess (ICS) Running | 关掉可能影响手机/热点共享，是否需要由用户判断 |
| `nat-type` | 外网 IP 是 `172.16.80.42`，落在私有段 → 双层 NAT | 纯 LAN 串流不受影响，只影响从外网连回来。改路由器要 Owner 自己做 |
| `svc-log` | 5 条历史 ERROR（最近一次 2026-09-18） | Streamer 正在运行，所以降级为 Warn；历史遗留不该按现状报警 |
| `nic-powersave` | **Unknown**：读不到网卡电源管理属性 | 本机驱动不通过 WMI 暴露这组属性。如实报 Unknown 而不是 Pass |

## 四、一处已修的缺陷：`net-apipa` 曾经误报

第一次实跑时 `net-apipa` 报的是「3 块**离线**网卡持有 APIPA 地址」。

问题在于：APIPA 地址**只有网卡在线时才会被指派**。一块 `Status=Disconnected` 的网卡
持有 `169.254.x.x`，通常意味着它刚拿到地址就掉线了——这确实值得看一眼，但不该和
「在线设备拿不到地址」混成一条同等严重的结论。

现在文案分开写了，并且证据里带上 `Up=True/False`：

```
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
  ForEach-Object { "$($_.InterfaceAlias)|$($_.IPAddress)|$($_.PrefixOrigin)" }
```

配合 `Get-NetAdapter` 的 `Status` 判断在线与否。

## 五、这次运行还暴露了工具自己的两个 bug

1. **`--selftest` 重定向输出时中文变乱码**。原因：WinExe 用 `AttachConsole` 借控制台，
   但**从未设置 `Console.OutputEncoding`**，于是控制台按 OEM 代码页输出。
   现象很有迷惑性——前半段正常，后半段变成 `å‡ºç«™ç­›`，还有一部分停在单层乱码
   `ï¼š`。现在所有 CLI 分支统一走 `ClaimConsole()`，一次做「先 AttachConsole 再置 UTF-8」。
   顺序也不能反：**AttachConsole 之前往被重定向的管道写东西会让整个进程死锁**
   （`--deep-ui` 第一版就是这么挂的，连 `timeout` 都杀不掉）。

2. **`AsyncRelayCommand.Execute` 是 `async void`**。处理器里抛异常会在用户正看证据的时候
   把整个应用撕掉，连同报告一起丢。现在暴露 `ExecutionTask` 并加了 `Failed` 事件。

## 六、没测到的（必须说清楚）

| 项 | 为什么没测到 |
| --- | --- |
| 头显侧 adb 三分支 | 头显没插 USB，也没开无线调试（5555/5554/5556/5557/5558/8080 全部关闭）。**插一下 USB 就能验。** |
| 广播实发抓包 | Npcap 已安装且能枚举 12 个设备，但抓包需要管理员权限，这次会话没有人在键盘前确认 UAC |
| `fw-restore-vd` | `Remove-NetFirewallRule` 需要提权 |
| 提权重启 Streamer | 上一轮成功过一次（PID 11120 → 36784），这一次因为无人确认 UAC 而如实报了失败 |

## 七、结论

这台机器上跑了 34 项检测，抓到 **3 条需要人处理**、**8 条如实记录但不建议动**、
**2 条 Unknown 且说清了为什么**、**2 条 Block 且都不是配置错误**。

最有价值的一条不是任何单个检测项，而是：**工具没有把「头显睡着了」说成「网络不通」。**
一个会这么做的工具，用户第三次就不看了。

复现命令：

```bash
cd D:/Project/VirtualDesktopHelper
./src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --selftest --out run.txt
```