# 2026-10-05 本机网络基线实测（研究阶段，一手证据）

> 目的：VDHelper 的第一个客户就是 Owner 自己。这台机器当前的网卡状态**本身就是**产品的典型故障样本。
> 所有数字来自本机只读命令，命令原文附后。实测时间 2026-10-05 上午。

## 1. 网卡与地址

```
Name                                  Status          Link
Wi-Fi (Intel BE200)                   Not Present     0 bps
Wi-Fi 3 / Wi-Fi 4 (Intel BE200)      Not Present     0 bps
Ethernet (Realtek 2.5GbE)             Up              1 Gbps
Ethernet 2 (VeryKuai TAP)             Disconnected    100 Mbps
Bluetooth Network Connection          Disconnected    3 Mbps
vEthernet (Default Switch)            Up              10 Gbps
vEthernet (WSL (Hyper-V firewall))    Up              10 Gbps
```

| 接口 | IPv4 | 角色 |
| --- | --- | --- |
| Ethernet | **192.168.11.2** | 主用，有默认网关 `192.168.11.1` |
| vEthernet (Default Switch) | 172.19.160.1 | Hyper-V |
| vEthernet (WSL) | 172.23.48.1 | WSL2 |
| Wi-Fi | **169.254.51.4** | 未连接，APIPA |
| Ethernet 2 (VeryKuai TAP) | **169.254.64.247** | 未连接，APIPA |
| Bluetooth PAN | **169.254.189.235** | 未连接，APIPA |

**判读**：三块处于 Down 状态的网卡各自持有 `169.254.x.x`（APIPA / 自指地址）。
这类地址对「UDP 广播发现 + 网卡绑定枚举」是**已知的破坏源**：发现包可能被发到错误接口，
或者应用枚举出多个候选网卡的错误组合而自检失败。**这就是 VDHelper 第一条检测项。**

## 2. 虚拟化叠加

Hyper-V 与 WSL2 同时启用，两块 vNIC 处于 Up。虚拟交换机过滤与 WSL 的 NAT 网络
都可能改变广播/组播的转发行为。**这是第二条检测项。**

## 3. 防火墙

```
DisplayName                  Enabled  Direction  Action  Profile
Virtual Desktop Streamer     True     Inbound    Allow   Domain, Private, Public
```
官方安装器建的入站放行规则存在且启用，profile 覆盖三种，**这一项本机是通过的**
（工具仍然要查，因为很多人是装了别的防火墙/规则被删/只装了 Android 端）。

网络配置文件：`Ethernet = Private`（不是 Public），这一项也通过。

## 4. 服务与安装

```
VirtualDesktop.Service.exe  Running  Automatic
C:\Program Files\Virtual Desktop Streamer\   存在
%APPDATA%\Virtual Desktop\                  存在
```

## 5. 端口与邻居（`tools/probe-ports.ps1` 实跑）

```
LOCAL_IP=192.168.11.2   SUBNET=192.168.11.0/24
## listening-on-host        ← 38810/20/30/40 本机无监听（Streamer 未运行）
## neighbors
  192.168.11.1   4c-85-8a-2a-89-08  open=[]
  192.168.11.14  c2-90-b8-76-94-1e  open=[] locally-administered/randomized-MAC
  192.168.11.4   22-07-4d-10-52-45  open=[] locally-administered/randomized-MAC
```

邻居里除网关外有两台使用**随机化 MAC** 的设备（.4 / .14），当前无任何 VD 端口开放。
本次测量时头显不在场或未开 VD，**无法据此判定哪一侧 listen**——见 `research/02-network-diagnosis/`。

## 6. 本机自带的结论

1. APIPA × 3 + 虚拟网卡 × 2 = 本机具备「发现失败」的完整土壤，即使防火墙正常。
2. 因此工具的**第一屏**应该是「网卡体检」，而不是「设置页」。
3. 检查必须**只读、瞬时、可解释**：告诉用户「哪一块网卡、哪个地址、为什么碍事」，
   而不是甩一个 `Enable-NetAdapter` 就完事。

## 7. 复现这些数字的命令

```powershell
Get-NetAdapter | Select-Object Name,InterfaceDescription,Status,LinkSpeed
Get-NetIPConfiguration
Get-NetConnectionProfile
Get-NetFirewallRule | Where-Object { $_.DisplayName -like "*Virtual*" }
Get-Service | Where-Object { $_.DisplayName -like "*Virtual*" }
arp -a
powershell -NoProfile -ExecutionPolicy Bypass -File D:/Project/VirtualDesktopHelper/tools/probe-ports.ps1
```