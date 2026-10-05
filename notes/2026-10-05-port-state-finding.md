# 端口状态判读：一个我自己写错、被真实使用抓出来的 bug

**发现时间**：2026-10-05，Owner 电脑重启后跑体检时暴露。
**性质**：检测项在**串流正在进行**时报「端口空闲」。这是最坏的一类错——它让人不信任工具。

## 现象

重启后第一次跑 `--selftest`：

```
[Pass] port-vd  四个 VD 端口此刻都空闲
```

同一时刻用 PowerShell 查：

```
Get-NetTCPConnection | Where-Object { $_.LocalPort -ge 38810 -and $_.LocalPort -le 38840 }

LocalAddress  LocalPort  RemoteAddress      RemotePort  State        OwningProcess
::            38840       ::                 0           Bound        11120
::            38830       ::                 0           Bound        11120
::            38820       ::                 0           Bound        11120
::            38810       ::                 0           Bound        11120
192.168.11.2  38810       40.89.161.236      38813       Established  11120
```

四个端口全被 `VirtualDesktop.Streamer`（PID 11120）占着，其中 38810 上还有一条**活动会话**。
工具说「空闲」，是错的。

## 两个叠加的错误

1. **只看 `-State Listen`**。Streamer 的四个端口常态是 **Bound**（已绑定未监听），
   不是 Listen；`Get-NetTCPConnection -State Listen` 因此什么都看不到。
2. **用「试着绑一下」来判断端口是否被占**。这个方法有两个致命问题：
   - 端口被别人持有时，绑定失败抛的是 `WSAEACCES`（访问权限被拒），不是「已被占用」；
   - 更要紧的是，它**扰动了自己要测的东西**——一次绑定尝试本身就是一次状态改变。
   本机上实测：向 38810 绑 `IPAddress.Any` 直接被拒（`netsh` 排除端口范围里并没有 38810，
   说明拒绝来自持有者，不是保留段）。

附带发现：owner 名单那次「四个端口被监听、占用进程未知」也是这个 bug 的表现——
第二次查询（问占用进程）时端口状态已经变了，于是全部落成「未知」。

## 正确做法

**问 Windows 一次，看所有状态，把状态本身当结论**，不要推断：

| 状态 | 含义 | 该怎么说 |
| --- | --- | --- |
| `Established` | 有活动会话 | 「PC 侧通道是通的」——这时候还说连不上，问题在头显侧或账号侧 |
| `Listen` | 在等连接 | 正常 |
| `Bound` | 端口被拿下但没在听 | Streamer 待机态，**正常**，不是冲突 |
| 什么都没有 | 空 | 多半 Streamer 没跑，看 `streamer-proc` |
| `Bound`/`Listen` 但占用者不是 VD | 真冲突 | 唯一的警告分支，提示先确认是什么程序 |

实现见 `src/VdHelper/Core/Diagnosis/NetworkInventory.cs` 的
`ObserveVdPortsAsync` 与 `src/VdHelper/Core/Health/HealthChecks.cs` 的 `port-vd`。
一次查询同时拿到端口、状态、占用进程、对端地址，证据里附查询原文。

## 修正后的实测（本机）

```
[Pass] port-vd  4 个端口上有活动会话：38810、38820、38830、38840
        38810: 已建立会话 → 192.168.11.14:36081（占用 VirtualDesktop.Streamer）
        38820: 已建立会话 → 192.168.11.14:42507（占用 VirtualDesktop.Streamer）
        38830: 已建立会话 → 192.168.11.14:41517（占用 VirtualDesktop.Streamer）
        38840: 已建立会话 → 192.168.11.14:37769（占用 VirtualDesktop.Streamer）
```

`192.168.11.14` 正是本项目开工第一天在 ARP 表里记下的那台随机 MAC 设备——
也就是此刻正在串流的头显。**这条检测项是这次修正之后才真正开始说人话的。**

## 教训

工具的检测项不能「看起来对」。一个在串流时报警空闲的检测项，
比没有这个检测项更糟：用户会照着它去改路由器。
所有涉及「有没有占用」的判读，都要用**一次查询拿到的事实**，
不要用「试一下会不会失败」这种间接推断。