# 03 — Quest/ADB 工具面盘点与首连清单

> 写作时间：2026-10-06。机器：`D:/Project/VirtualDesktopHelper`，分支 `main`，`VERSION.txt` = `0.5.0`。
>
> **本轮读的是 2026-10-06 收工时的代码。** `src/VdHelper/Core/Adb/` 在本文写作期间被并行改过一轮
> （`HeadsetProbe.cs` 174→295 行、`HeadsetDeepProbe.cs` 469→495 行、`App.xaml.cs` 489→505 行），
> 本文所有 `file:line` 都是**改完之后**重新对齐的。若日后行号再漂，以符号名为准。
>
> 本机 adb：`D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe`，**不在 PATH**（`which adb` 退出码 1）。
> `adb version` = `1.0.41` / `30.0.4-6686687`（本轮实跑）。
> **仍然没有连过任何头显**：`adb devices -l` 的 stdout 只有 `List of devices attached`，exit=0。
> 凡需真机的输出形态一律标 `[未验证]`，并在 §5 给出确证它的那一条命令。

## ⚠️ 一个必须先说的实测发现

`%AppData%\VirtualDesktopHelper\config.json` 里存着 `headsetIp = 192.168.11.14`。
本轮实跑发现：**这个地址现在有活的东西**——

```
$ ping -n 3 192.168.11.14   →  3 发 3 收，0% 丢包，1–3 ms，TTL=64
$ arp -a 192.168.11.14      →  c2-90-b8-76-94-1e   dynamic
$ 本机以太网                 →  192.168.11.2 / B0-82-E2-6C-F7-EE
$ 5555 / 38810 / 38820 / 38830 / 38840 / 5037   →  全部 closed/filtered
```

也就是说：**有一个第三方设备在同网段上开着 IP 层应答，但 adb 的 5555 和 VD 串流的 38810–38840 全关。**
`c2-90-b8` 这个 OUI 在 `api.macvendors.com` 查不到（返回 `Not Found`）——**不是已登记的厂商前缀，
也不在本机任何网卡上**（本机是 `94-B6-09` / `B0-82-E2`）。

**它是不是头显，本轮无法判定**，三种可能都活着：(a) 是头显但 adb 没开；(b) 是别的手机/IoT；
(c) 是路由器的另一个网段设备被记错了。**工具的判断是对的**——它没有把这个 IP 当成头显，
只报「ping 通但 adb 连不上」（`HeadsetProbe.cs:74-76`、`HeadsetDeepProbe.cs:105-107`），
没有替用户认设备。

**首连第一件事因此变成：确认 `192.168.11.14` 是不是那台头显。** 见 §5.1 step 0.0。

---

## 0. 一页结论

| 问题 | 结论 | 依据 |
|---|---|---|
| 本仓 adb 能力有多大？ | 5 条快照命令 + 7 项权限授予状态 + 3 条根因子判定，全部 `ArgumentList` 拼参数 | `HeadsetProbe.cs:107-114`、`AdbClient.cs:141-173` |
| 阈值有出处吗？ | 大部分有（反编译源码 / RFC 4300 / 旧 VDH）。**仍有一处是猜的且已自认**（25 个 VPN 进程名）；**另有两处文案与事实不符**（§1.4） | — |
| adb 发现顺序可靠吗？ | 顺序可靠，但**第 8 条候选（Unity）永远不可能命中**（`EnumerateFiles` 用在了目录上），且**没有版本门槛** | `AdbClient.cs:65`、`AdbClient.cs:19-28` |
| 会挂死吗？ | 单条命令都有超时；**整轮无总时限**，最坏 ~4 分 46 秒 | `HeadsetDeepProbe.cs:400-412`、§2.4 |
| 要不要引外面的工具？ | **不引任何一个做检测判定的**。可采纳的只有 scrcpy 这类「看画面」的旁观工具，且只作文档引用 | §3、§4.1 |
| 首连第一件事？ | **先确认 `192.168.11.14` 是不是头显**，再 USB 授权；无线是兜底且**代码里的 connect 端口是错的** | §5.1、§4.3 |

---

## 1. 本仓已经在 adb 上做了什么

### 1.0 三个文件的分工

```
src/VdHelper/Core/Adb/
├── AdbClient.cs        173 行  AdbLocator（找 adb）+ ConfigFile + AdbClient（跑 adb）
├── HeadsetProbe.cs     295 行  第三屏的 `headset` 检查（包名 / 权限授予 / 进程存活）
└── HeadsetDeepProbe.cs 495 行  `headset-deep` 检查（A6 MAC / F1 头显设置 / B4 头显侧 VPN）
```

只有 `headset-deep` 注册进 36 项检测表（`src/VdHelper/Core/Model/Symptom.cs:28`）；
`headset` 不在其中，**但 `--adb` 现在两个都跑**（`App.xaml.cs:471-476`）。

> `headset-deep` 是 A5/A6/F1 的唯一覆盖，而它**条件触发**。本机设备列表为空 →
> 这三条的实际覆盖为 0（`research/12-coverage-audit/01-coverage-matrix.md:135-137` 已记）。

### 1.1 `AdbClient` —— 唯一真正跑 adb 的地方

`RunAsync(args, timeoutMs = 8000, ct)`（`AdbClient.cs:141-172`）：

| 环节 | 做法 | 行号 |
|---|---|---|
| 参数传递 | `psi.ArgumentList.Add(a)`，**不走 shell 字符串** | `AdbClient.cs:153` |
| 编码 | stdout/stderr 强制 UTF-8 | `:146-151` |
| 读取 | 两个流 `ReadToEndAsync` **先并行发起**，再 `WaitForExitAsync` | `:159-171` |
| 超时 | `CancelAfter(timeoutMs)` → `Kill(entireProcessTree: true)` → `TimedOut = true` | `:162-169` |
| 成功判据 | `Ok => ExitCode == 0 && !TimedOut` | `AdbClient.cs:136` |

`Lines` 只切 **stdout**（`:137-138`）。下面 1.2 的一个正确性结论依赖这一点。

> **已实测（好消息 1）**：`kill-server` 之后 `adb devices -l` 的
> `* daemon not running; starting now at tcp:5037` 走 **stderr**，
> stdout 仍只有 `List of devices attached`。
> 所以 `HeadsetProbe.cs:51` 的 `Skip(1)`（跳表头）是安全的。

> **已实测（好消息 2）**：超时 `Kill(entireProcessTree: true)` **不会杀掉 adb server**。
> `kill-server` → 起一个会阻塞的 `adb connect` → 6 秒后 SIGKILL 客户端 →
> `Get-Process adb` 仍有 PID 26584，`adb devices` 照常。
> 即 VdHelper 超时不会把 Android Studio / SideQuest 的 adb server 一起带走。

### 1.2 `HeadsetProbe`（`headset`）逐条拆解

入口 `RunAsync(string? serial = null, ct)`（`HeadsetProbe.cs:44-183`）。

**步骤 1：列设备**（`:48-54`）

```csharp
var serials = devices.Lines
    .Skip(1)
    .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
```

**读法**：切的是**空格**，而 `adb devices -l` 的行是 `<serial>\t<state> product:…`（**制表符分隔**，
`01-adb-playbook.md:141`）。

> **缺陷（本轮用 .NET 语义实测，不是猜测）**：
> `"2G0YC5ZHBD01XF\tdevice product:quest3"` 按 `' '` 切，第一个 token 是
> `"2G0YC5ZHBD01XF\tdevice"`——**序列号后面粘着状态**。
> 拿这个串去 `-s`，adb 直接拒绝。实跑：
> ```
> $ adb -s "2G0YC5ZHBD01XF<TAB>device" shell getprop ro.build.version.release
> adb.exe: device '2G0YC5ZHBD01XF<TAB>device' not found      exit=1
> ```
> 对照 `HeadsetDeepProbe.ParseDevices`（`:414-424`）用的是 `Split((char[]?)null, …)`（按空白符切），
> **是对的**。同一个仓库里两个解析器，一个对一个错，错的那个在第三屏上。
>
> **后果链**（这一条最要紧）：序列号一旦被污染 → 五条 `shell` 命令全失败 →
> `pidof` 必然失败 → `running.Count == 0` → 命中 `HeadsetProbe.cs:162-172` 的
> **`Block`「客户端装了，但没有进程在运行」**。
> **一个解析 bug 会伪装成一条 Block 级根因**，而它引用的
> `NetworkManager.cs` 两条 Kill 路径全是真货——用户会被指向一个完全错误的方向。
> **首连当天第一件要修的就是这一处。**

**步骤 2：早退与分流**

| 条件 | 状态 | 语义 | 行号 |
|---|---|---|---|
| `devices.TimedOut` | `Unknown` | 「adb 无响应」 | `:56-58` |
| 设备列表空 **且 ping 得到保存的 `headsetIp`** | `Unknown` | **「头显在网络上，但 adb 连不上它」** | `:77-83` |
| 设备列表空 **且 ping 不通 / 没填 IP** | `Warn` | 「没有连着的头显」 | `:77-79` |
| `--serial` 指定的序列号不在列表 | `Unknown` | 点名现在连着的是谁 | `:92-96` |
| 同时 >1 台设备且没给 `--serial` | `Unknown` | 「不知道该看哪一台」 | `:97-102` |

> **这一步做得对。** 用「ping 通不通」把「网络层断了」和「adb 控制面没建立」分开——
> 这正是 `research/09-failure-corpus/02-symptom-to-rootcause.md` 里 R10/R02 的分诊口径。
> **本轮实测正好命中它**：`192.168.11.14` ping 通、adb 列表空 → 两个探针都报
> 「头显在网络上，但 adb 连不上它」（§0 的实测记录）。
> 注意它**没有**断言「那就是头显」——文案只说 `{configured} ping 得到应答`，用的是配置里的地址。

**步骤 3：五条快照命令**（`:107-114`）

| id | 命令 | 用途 |
|---|---|---|
| `props` | `shell getprop ro.build.version.release` | 只展示 |
| `model` | `shell getprop ro.product.model` | 只展示 |
| `wlan` | `shell ip -4 addr show wlan0` | 只展示 |
| `proxy` | `shell settings get global http_proxy` | 只展示，**不判定**（playbook `:348` 有规则，代码未实现） |
| `packages` | `shell pm list packages` | **判定用**：找包名 |

**阈值来源**：`PackageNames` 四个候选（`:14-20`）= `VirtualDesktop.Android` /
`com.dwgx1.vd.recovered` / `com.dwgx1.virtualdesktop.recovered` / `com.vrdesktop.streamer`。
文件头 `:6-11` 声明来源是 `01-adb-playbook.md` 与 `03-il-constants.md`。**有出处。**

**步骤 4：权限（已修好）**（`:33-42`、`:199-233`）

```csharp
var r = await adb.RunAsync(["-s", serial, "shell", "dumpsys", "package", pkg], 15000, ct);
…
var m = Regex.Match(line.Trim(), @"^(?<name>[A-Za-z0-9_.]+):\s*granted=(?<g>true|false)");
```

**三个曾经很严重的缺陷这一轮都修掉了**，代码注释 `:24-32` 与 `:185-198` 自己写了原因：

1. 权限字面量从捏造的 `com.oculus.horizonos.permission.*` 改回
   **`com.oculus.permission.*` 与 `horizonos.permission.*` 两组各三条** + `POST_NOTIFICATIONS`
   ——与反编译字面量 `com.oculus.permission.FACE_TRACKING`
   （`%TEMP%\vd_ep_01\vd\VirtualDesktop.Android\VirtualDesktop\VrApp.cs:56`）和
   `01-adb-playbook.md:510-516` 一致。
2. 判定源从 `pm list permissions` 换成 **`dumpsys package <pkg>` + 解析 `granted=`**——
   前者只列「系统里有哪些权限名」，**没有 per-app 状态**（AOSP 的
   `PackageManagerShellCommand.doListPermissions` 对每个组调 `queryPermissionsByGroup`
   然后逐条打印 `permission:<name>`，
   https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android11-dev/services/core/java/com/android/server/pm/PackageManagerShellCommand.java ），
   原来的 `Contains` 是**恒真**的。
3. 7 项从「7 行 4 个不同串」变成 **7 个真串**；`same-上（第二个授权位）` 的假注释没了。
   并且加了 `Absent` 集合（`:224-228`、`:236`）：某系统上根本不存在的权限名
   报「该系统不认识这条（非故障）」而不是记成缺失——**这是对的**，因为同一个 Quest OS 上
   `com.oculus.*` / `horizonos.*` 通常只有一个存在。

**阈值来源**：7 项的清单来自 `install.bat:25-31` / `install_template.bat:61-68`
（注释 `:26-27` 指了后者）。**有出处。**

> **注意阈值本身仍 `[未验证]`**：`dumpsys package` 里 `runtime permissions:` 段的
> `granted=true` 行形态至今无真机记录（`01-adb-playbook.md:247`、`:562`），
> 正则 `^(?<name>[A-Za-z0-9_.]+):\s*granted=(?<g>true|false)` **锚定行首**——
> 真实 dumpsys 缩进多少、是否带别的修饰词，**首连当天必须实测**（§5.6 A7）。
> 若真实行是 `    com.oculus.permission.FACE_TRACKING: granted=true, flags=…`，
> `line.Trim()` 之后能匹配；但若权限名与 `granted=` 之间还有别的字段，**这一项会全部落进 `Absent`
> 而报「非故障」**——**假通过的风险在这里，不在「报缺失」那边。**

**步骤 5：进程存活**（`:134-152`、`:162-172`）

```csharp
var pids = (pid.Ok ? pid.StdOut : "")
    .Split(' ', …).Where(x => int.TryParse(x, out _)).ToList();
```

`pidof` 的输出被逐 token 校验为数字才当 PID（`:139-144`）——这修掉了一个真问题：
`pidof` 失败时可能回一句 shell 报错，当成 PID 就会把死进程报成活的。

**这一步的判定本身是本仓最有价值的部分**，且**有出处**：
`installed.Count > 0 && running.Count == 0` → `Block`，文案点名
`NetworkManager.cs:184-186` / `:212-214` 两条 Kill 路径和 `binary_patch.py:226-254`
把 Kill NOP 成 ret（`:166-169`）。**「列表空」与「网络不通」的分水岭。**

> 但如上所述，**它现在被步骤 1 的序列号 bug 直接污染**——bug 未修，Block 判定就是一颗地雷。

**修复动作也修好了**（`:239-296`）：`GrantPermissions(perms, serial, packages)` 现在**真执行**
`pm grant`（`:277-279`）并逐项回报（`:285-292`）。原版返回 `Task.FromResult(true)` 却不跑任何命令——
`--apply headset-grant` 会打印「成功」而设备上什么都没变。
这一轮的注释 `:244-247` 写得很准：「在一条全部价值都建立在结论可信的工具上，
这等于制造了本项目一直在消灭的那种假绿灯」。**完全同意。**

### 1.3 `HeadsetDeepProbe`（`headset-deep`）逐条拆解

入口 `ProbeAsync(serial, ct)`（`:73-151`）。

**降级链**

| # | 条件 | 状态 | 行号 |
|---|---|---|---|
| 0 | `adb.exe` 不存在 | `Unknown` + 写出本机已知路径 | `:78-82` |
| 1 | `adb devices -l` 超时 | `Unknown` | `:86-88` |
| 2 | 设备列表为空（带 ping 分流） | `Unknown` | `:92-115` |
| 2b | >1 台设备且无 `--serial` | `Unknown` | `:116-118` |
| 3 | serial 不在列表 / 状态不是 `device` | `Unknown` + `DescribeState` | `:121-128` |

`DescribeState` / `DescribeStateFix`（`:444-460`）覆盖 `unauthorized` / `offline` /
`no permissions` / 兜底四态，每态一句中文 + 一句改法。**本仓写得最好的部分。**

> **两处文案与事实不符（仍在）**：
> - `:457` 把修复写成 `` `adb kill-server && adb start-server` `` —— 这是给用户复制到终端的字符串，
>   `&&` 在 cmd 里可用但 `adb start-server` 更稳。
> - `:439` 的 `DescribeFailure` 硬编码「超时（**8 秒**无回包）」，而 B4 的 pidof 兜底用 **6000ms**
>   （`:336`）、F1 的设置读用 **8000ms**（`:265`/`:270`）、`headset` 的 `dumpsys package` 用
>   **15000ms**（`HeadsetProbe.cs:207`）。**文案会撒谎**，且这是给人看的第一手证据。

**子判定 A6 — 头显 MAC 随机化**（`:157-195`）

| 环节 | 内容 | 行号 |
|---|---|---|
| 命令 1（首选） | `shell cat /sys/class/net/wlan0/address` | `:166` |
| 命令 2（兜底） | `shell ip addr show wlan0` | `:173` |
| 都空 | → `Unknown`，**不猜** | `:179-180` |
| 解析 | 正则 `([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}`，两侧禁止再跟十六进制 | `:25-27`、`:428-436` |
| 判定 | 首字节 `& 0x01`（组播位）**或** `& 0x02`（本地管理位）为 1 → `Warn` | `:182-190` |

> **阈值来源：RFC 4300**（`:184` 注释明写）。**这是对的**，且 `:187-188` 专门解释了
> 为什么不能只判 `0x01`——会漏掉 `02/06/0A/…` 整类真实形态。**本仓阈值里质量最高的一处。**
>
> ⚠️ **首连当天顺手验一件事**：本机 ARP 表里那个 `c2-90-b8-76-94-1e` 就是
> **首字节 `0xC2` = 组播位 0 + 本地管理位 1** 的形态。这正是 A6 会报的形态
> （**若**它是头显）。留着做对照。

**子判定 F1 — 头显本地设置**（`:199-278`）

| 环节 | 内容 | 行号 |
|---|---|---|
| 前置 | `pm list packages` 找包；找不到 → `Unknown` | `:202-208` |
| 候选目录 ×3 | `/data/user/0/<pkg>/files/.config/Virtual Desktop`、`/data/data/<pkg>/…`、`/storage/emulated/0/Android/data/<pkg>/…` | `:212-217` |
| 候选文件 ×2 | `UserSettings.json`、`SharedUserSettings.json`（**故意不含** `SharedStreamerSettings.json`，理由见 `:64-67`：该文件不落盘） | `:67` |
| 读法 A | `exec-out run-as <pkg> cat <path>`（需 debuggable APK） | `:265` |
| 读法 B | `exec-out su -c "cat <path>"`（需 root） | `:270` |
| 都失败 | → `Unknown`，点明「需要 patched APK 可调试或已 root」 | `:238-247` |
| 判定 | 顶层键 `HandTracking` / `UseMultiModal` 为 `true` → `Warn` | `:295-299`、`:250-252` |
| 脱敏 | 只报字节数 + 顶层键名（上限 40），**不报值** | `:310-313` |

> **阈值来源**：目录形态 `:210-211` 指向 `03-quest-parameters/02-storage-and-io.md §1.2/§2`；
> 判定键指向同文件 `§2.1/§2.3`（`:279`）。**有出处。**
> 读不到时如实报 `Unknown`，`:483-485` 还专门解释「官方 APK 两者都没有，
> 所以这一项在官方版本上永远是 Unknown——这是前置条件缺失，不是故障」。**态度是对的。**

> **首连当天必看**：官方 APK + 未 root 上这一项**必然 Unknown**，
> 于是整轮被 `:140-142` 合成成 `Unknown`。**拿到 Unknown 是正常状态，不是工具坏了。**
> 说清楚这件事，否则会有人误报。

> **两处未验证的引用**：
> - `:260` 注释称「路径里的空格由 adb 自己的参数转义处理（**adb ≥ 1.0.31 的 escape_arg**）」。
>   这个版本号我没能在 AOSP 找到对应提交，**标 `[未验证]`**；
>   而且它说的是 `adb shell` 的 `escape_arg`（AOSP `commandline.cpp` 里有
>   https://android.googlesource.com/platform/platform_system_core/+/a49d024/adb/client/adb_install.cpp ），
>   与 `exec-out run-as … cat <带空格路径>` 是不是同一条路径，**没有设备无法验**。
> - `su -c "cat <带空格路径>"`（`:270`）：`su` 各家实现对 `-c` 参数的引号处理不同，
>   被按空格切开就会失败。**同样只能实测**（§5.6 D1/D2）。

**子判定 B4 — Quest 侧 VPN**（`:320-357`）

| 环节 | 内容 | 行号 |
|---|---|---|
| 命令 1 | `shell ps -A` 一次拿全量 | `:325` |
| 解析 | 逐行取**最后一列**为进程名（不按列数硬解析，兼容内核截断到 15 字符） | `:361-375` |
| 比对 | 全等，或「候选是前缀且多出部分全是数字且 ≤3 字符」（覆盖 `nordvpn3`/`openvpn3`） | `:382-391` |
| 命令 2（兜底） | `ps -A` 读不到时**逐个** `pidof`，6000ms/个 | `:334-336` |
| 判定 | 命中 → `Warn`；进程列表都读不到 → `Unknown` | `:340-348` |

> **阈值来源：`VpnProcesses` 这 25 个名字（`:34-61`）是启发式名单，不是权威注册表。**
> 注释 `:32-33` 自己写了：「这是一份启发式名单，不是权威注册表：命中即提示，不命中不代表干净。」
> **这是诚实的 guesses。** 本轮数过，正好 **25** 个
> （`ssd, shadowsocks, ss-local, ssr, v2ray, v2rayNG, xray, sing-box, clash, clash.meta, mihomo,
> nekobox, hiddify, outline, openvpn, nordvpn, expressvpn, protonvpn, windscribe, mullvad, surfshark,
> psiphon, wireguard, tailscaled, zerotier`）。
> `research/12-coverage-audit/01-coverage-matrix.md:84` 写的是「26 个」——**又一处数字漂移**。
> `IsVpnProcess` 刻意不用子串包含（`:379-381` 解释了 `ssd`/`v2ray` 会误命中）——**设计是对的**。

**合成规则**（`:138-142`）：任一 `Warn` → `Warn`；无 `Warn` 但有 `Unknown` → `Unknown`；全 `Pass` → `Pass`。
**永不 `Block`**（`:138-139`：随机 MAC 与 VPN 进程本身合法）。**这个克制是对的。**

### 1.4 阈值来源总表（含「猜的」与「错的」）

| 判定 | 阈值 / 字面量 | 出处 | 是不是猜的 |
|---|---|---|---|
| 包名识别 | 4 个 `PackageNames` | `01-adb-playbook.md` + `03-il-constants.md`（`:6-11`） | 否 |
| 进程存活 | `pidof` 回数字 token | 反编译 `NetworkManager.cs` 两条 Kill 路径（`:166-169`） | 否 |
| 权限清单 | 7 个真串（两命名空间各三 + 通知） | `install.bat:25-31` / `install_template.bat:61-68`（`:26-27`） | 否 |
| 权限授予态 | `dumpsys package` 的 `granted=` | AOSP `dumpsys package` 语义 | 否，但**行形态 `[未验证]`** |
| A6 随机 MAC | 首字节 `0x01` / `0x02` | **RFC 4300**（`:184`） | 否 |
| F1 设置文件 | 3 目录 × 2 文件 | `02-storage-and-io.md §1.2/§2` | 否 |
| F1 判定键 | `HandTracking` / `UseMultiModal` | 同上 `§2.1/§2.3`（`:279`） | 否 |
| B4 VPN 名单 | **25 个进程名** | 无权威表，注释自认启发式（`:32-33`） | **是（已如实标注）** |
| `ps -A` 列位 | 最后一列是 NAME | `01-adb-playbook.md §3.2`（`:356-357`） | 否，形态本身 `[未验证]` |
| `http_proxy` 判定 | **未实现**（只展示） | playbook `:348` 有规则 | **缺失** |
| adb 版本门槛 | **无** | `11-quest-dashboard/02-screen3-design.md:481` 规划过未实现 | **缺失** |
| 「`pair` 后连 5555」 | `HeadsetDeepProbe.cs:465`、`HeadsetProbe.cs:86` | **与 AOSP 文档矛盾** | **错（见 §4.3）** |

---

## 2. adb 发现顺序与失败模式

### 2.1 `AdbLocator.Probe()` 的确切顺序

`Probe()`（`AdbClient.cs:42-85`）返回**有序**列表，`Find()` 取第一个（`:87`）：

| 序 | 来源标签 | 候选 | 行号 |
|---|---|---|---|
| 1 | `上次选择` | `%AppData%\VirtualDesktopHelper\config.json` 的 `adbPath` | `:55-56` |
| 2 | `Android SDK / VIVE Hub` | `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` | `:21` |
| 3 | 同上 | `%ANDROID_HOME%\platform-tools\adb.exe` | `:22` |
| 4 | 同上 | `%ANDROID_SDK_ROOT%\platform-tools\adb.exe` | `:23` |
| 5 | 同上 | `D:\Software\Android\Sdk\platform-tools\adb.exe` | `:24` |
| 6 | 同上 | **`D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe`** ← 本机唯一命中 | `:25` |
| 7 | 同上 | `%PROGRAMFILES%\VIVE Hub\Live\ADB\adb.exe` | `:26` |
| 8 | `Android SDK 目录` | `%LOCALAPPDATA%\Unity\Hub\Editor\*\…\SDK\platform-tools\adb.exe`（通配） | `:27`、`:58-72` |
| 9 | `仓库自带` | `D:\Project\VirtualDesktop\_upstream\quest_adb_tools\adb.exe` | `:30-34`、`:74` |
| 10 | `仓库自带` | `D:\Project\VirtualDesktopHelper\tools\platform-tools\adb.exe` | `:33`、`:74` |
| 11 | `PATH` | 逐个 PATH 目录拼 `adb.exe` | `:76-82` |

去重靠 `seen` + `File.Exists`（`Try`，`:47-53`）。
**PATH 排最后是刻意的**（`:76` 注释：an adb on PATH may be a stale wrapper, so it never wins over a real SDK copy）。

**本机逐条 `Test-Path` 实测**：1–5、7 全部 ❌；**6 ✅**；9、10 的目录本身都不存在。
`ANDROID_HOME` / `ANDROID_SDK_ROOT` 均未设置，展开成 `\platform-tools\adb.exe`，`File.Exists` 为 false（无害）。
所以本机唯一命中第 6 条。且 `config.json` 已写着 `adbPath = <VIVE Hub 路径>`
——是 `HeadsetViewModel.cs:86` **每次加载都写回去**的，所以真正常跑时命中第 1 条，值还是第 6 条。

> **副作用**：**「查找 adb」这个只读动作会改磁盘**——`HeadsetViewModel.cs:86` 无条件
> `AdbLocator.RememberedPath = adbPath`。这正是旧 VDH 被批评过的行为
> （`research/08-legacy-vdh/01-asset-map.md:64`：「一次「查找」会改磁盘」）。**旧坑原样搬回。**
> `ConfigFile.Write` 失败被吞（`:122-125`），所以不会炸，但会静默写盘。

### 2.2 通配候选（第 8 条）永远不可能命中

```csharp
foreach (var file in Directory.EnumerateFiles(parent, Path.GetFileName(dir)))
    Try("Android SDK 目录", Path.Combine(file, "adb.exe"));
```
（`AdbClient.cs:65-66`）

`*` 位是一个**版本目录**（`Editor\6000.0.30f1\`），所以要「在 `SDK` 下**按名字枚举目录**」。
代码用的是 `EnumerateFiles`——**枚举文件**；而且下一步把结果当目录用。

**本轮用假树实测**（`%TEMP%` 下造 `Editor\6000.0.30f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`）：

```
EnumerateFiles(parent, "platform-tools")        → count=0
EnumerateDirectories(parent, "platform-tools")  → …\SDK\platform-tools
```

**结论：Unity Hub 那条是死代码。** 本机没装 Unity 所以现在无害，
但「装了 Unity Hub 仍找不到 adb」这类 issue 会查无实据。正确写法是 `EnumerateDirectories`。

### 2.3 三种失败模式的确定行为

#### (a) adb 缺失

| 场景 | 行为 | 行号 |
|---|---|---|
| CLI `--adb` | `Find()` 返回 null → 打印两行提示 → **exit 3** | `App.xaml.cs:458-466` |
| 第三屏 | `Probe()` 空 → `Status = Warn` + 官方下载 URL → **提前 return** | `HeadsetViewModel.cs:77-83` |
| deep | 路径为空或文件不存在 → `Unknown` + 本机已知路径 | `HeadsetDeepProbe.cs:78-82` |

#### (b) adb 存在但没有设备

**本轮实跑的 `--adb` 完整输出**（首连前的基线，**注意它不是「没填 IP」那个分支**——
本机填了 `192.168.11.14` 且 ping 得通，所以走的是 Unknown 分支）：

```
[Unknown] adb  头显在网络上，但 adb 连不上它
  **网络这一段是通的**——192.168.11.14 ping 得到应答。所以这不是网络问题，是 adb 这一段没建立。
  adb: D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe
  adb devices: List of devices attached
  头显是否在网络上可达: 是：192.168.11.14 ping 通
  指引：无线方式：头显里 开发者选项 → 打开无线调试，PC 上 `adb pair <头显IP:配对端口> <配对码>`，再 `adb connect <头显IP>:5555`；头显 IP 可在「设置 → Wi-Fi → 连接详情」看到。

[Unknown] headset-deep  头显在网络上，但 adb 连不上它
  **网络这一段是通的**——192.168.11.14 ping 得到应答。所以这不是网络问题，是 adb 这一段没建立：要么没插 USB，要么头显里的「无线调试」没开。三项子判定一个都跑不了。
  adb: D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe
  adb devices: List of devices attached
  头显是否在网络上可达: 是：192.168.11.14 ping 通
  指引：① USB-C 接 PC，② 头显里点「允许 USB 调试」（勾「始终允许」），③ 回到本屏重跑。无线方式：先在头显 开发者选项 打开无线调试，PC 上 `adb pair <头显IP:配对端口> <配对码>`，再 `adb connect <头显IP>:5555`；头显 IP 在 设置 → Wi-Fi → 连接详情 里看。

exit=4
```

两个探针都跑、都打印，退出码取两者更差的那个
（`App.xaml.cs:494-504`：`Pass→0`、`Warn→3`、其余 `→4`，取 `Math.Max`）。

若 `headsetIp` 没填或 ping 不通，`headset` 给 **`Warn`「没有连着的头显」**、
deep 给 **`Unknown`**（`HeadsetProbe.cs:77-79`、`HeadsetDeepProbe.cs:113`）。

#### (c) 有设备但 `unauthorized` / `offline`

**deep 屏处理正确**（`HeadsetDeepProbe.cs:127-128` + `:444-460`）：

| 状态 | 文案 | 修复指引 |
|---|---|---|
| `unauthorized` | 「头显上还没点「允许 USB 调试」，adb 被拒绝。」 | 拔插 + 点允许并勾「始终允许」；没弹窗就去 设置 → 关于 → 开发者选项 |
| `offline` | 「adb 连得上但拿不到响应，通常是头显睡着或线刚插上还没握手。」 | 戴上头显唤醒；或重启 adb server 后重连 |
| `no permissions` | 「这台机器缺 adb 的 USB 驱动授权规则。」 | 换线/换口；仍不行去设备管理器装 OEM 驱动 |

**`headset` 屏不读状态**：`:50-54` 只取第一列当序列号，**不看第二列**，
然后拿一个 `unauthorized` 的设备去跑五条 `shell`——全失败，最后落到
「没找到 Virtual Desktop 客户端」这个**误导性的 Warn**（`:154-157`）。
`deep` 的现成文案表（`:444-460`）可以直接复用。

### 2.4 会挂死 / 会无界等待的地方

| 位置 | 会不会挂 | 说明 |
|---|---|---|
| 单条 adb 命令 | **不会** | 每条都有超时（6000 / 8000 / 10000 / 15000ms）+ `Kill(entireProcessTree)`（`AdbClient.cs:162-169`） |
| `TryAsync` 兜底 | **不会** | 把 `Win32Exception` / `InvalidOperationException` / `IOException` / `UnauthorizedAccessException` / `RegexMatchTimeoutException` 全折叠成 `ExitCode = -1`（`HeadsetDeepProbe.cs:400-412`）。**这层很关键**——`AdbClient.RunAsync` 自己不 catch（`:158` 的 `p.Start()` 会抛） |
| **整轮** | **会等很久** | **没有总时限** |
| `adb connect` | 会 | 实跑连不可达 IP 耗时 **21126 ms** 且 **exit=0**。代码现在不调它，但说明 8s 超时对 connect 类不够 |
| `adb pair` 不给配对码 | 会读 stdin | 实跑 `adb pair 192.168.99.99:37000`（stdin 接 `/dev/null`）→ `Enter pairing code: adb.exe: No pairing code provided`，0 秒退出。**stdin 是交互终端时会一直等**直到被超时杀。**配对码必须当 argv 传** |
| `adb mdns services` | 边缘 | 冷启动实测 11853 ms，第二次 96 ms，接近默认 8s 超时 |

**整轮最坏耗时**（全部命令都超时才算）：

| 阶段 | 调用数 × 超时 | 小计 |
|---|---|---|
| `devices -l` | 1 × 8s | 8s |
| `headset`：5 快照 + `dumpsys package` ×包数 + `pidof` ×包数 | ≈ 5×8 + 1×15 + 1×8 | 63s |
| A6 | 2 × 8s | 16s |
| F1 | `pm list packages` 1×8 + 2 文件 × 3 目录 × 2 读法 = 12×8 | 104s |
| B4 | `ps -A` 1×8 + 25 × `pidof` 6s | 158s |
| **合计** | | **≈ 349 秒 ≈ 5 分 49 秒** |

`--adb` 传的 `CancellationToken` 是 `CancellationToken.None`（`App.xaml.cs:471-475`），
**没有外部取消点**；WPF 第三屏也没有停止按钮（`HeadsetViewModel.cs:28` 只有一个 `RefreshCommand`）。
**这是首连前必须补的第二件事**：总时限 + 取消。

---

## 3. 外面的 Quest/ADB 工具世界

> 只收开源或官方可下载的。许可证与维护状态用 GitHub API / 官方页面**本轮实查**（2026-10-06）。
> 无线前置三档：**A** = USB 授权即可；**B** = 必须先 `tcpip`/`pair`；**C** = 需 root。

| 工具 | URL | 做什么（我们没有的） | 许可证 | 维护状态（实查） | 无线前置 |
|---|---|---|---|---|---|
| **scrcpy** | https://github.com/Genymobile/scrcpy | 头显屏幕/摄像头镜像到 PC，键鼠注入 | Apache-2.0 | ★151093，push 2026-10-05，**活跃** | A |
| **SideQuest**（开源部分） | https://github.com/SideQuestVR/SideQuest | 应用安装/管理、库、无线 ADB 配对 UI | MIT | ★409，push 2026-10-01，**活跃** | A（自带配对 UI） |
| **Meta Quest Developer Hub** | https://developers.meta.com/horizon/downloads/package/oculus-developer-hub-win/ | 官方设备管理 + **Meta 定制 ADB fork（streamed file ops）** | **专有闭源**，需 Meta 账号 | 页面版本 6.4.0 / 6.4.1，**活跃** | A |
| **Meta Oculus ADB 驱动** | https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/ | Windows 侧 USB 驱动 | 专有 | 官方 | A |
| **open-quest-hub** | https://github.com/Watash1no/open-quest-hub | scrcpy 集成、APK/OBB 一键推、logcat 导出、文件浏览、TUI | MIT | ★7，push 2026-10-01，活跃但很新 | A |
| **QAdb** | https://github.com/ludoven/QADB | Compose Multiplatform 图形 ADB 壳 | MIT | ★164，push 2026-09-24，活跃 | A |
| **ADBO** | https://github.com/ovsky/ADBO | 包一层 **MQDH 的 Meta ADB fork** | MIT | ★7，push 2026-09-29，很新 | A（**但要求先装 MQDH**） |
| **visor** | https://github.com/chisomobanzi/visor ・ https://pypi.org/project/visor-dev | Quest 专用 wrapper + **MCP server（29 个 `quest_*` 工具）+ Web 仪表盘 + VR profiler（吃 Quest 每秒 VrApi 指标）** | MIT | ★2，创建 2026-07-12，**仅 1 个 release v0.1.0**，push 2026-07-29，PyPI 周下载 **2** | A |
| **Android Toolkit** | https://github.com/TeamNocturnal/AndroidToolkit | ADB/backup/debloat/系统工具，自带 adb 二进制 | **无 LICENSE**（API 报 `None`） | ★19，push 2026-10-05，活跃 | A |
| **beautycat** | https://github.com/jeziellago/beautycat | 浏览器里的 logcat 视图 | MIT | ★25，push 2026-09-28，活跃 | A |
| **Quest-Update-Tool** | https://github.com/SasukeSagara/Quest-Update-Tool | 自动检测头显型号 + 更新 | **无 LICENSE** | ★0，push 2026-03-16 | A |
| **MetaADBdocs** | https://github.com/Estati/MetaADBdocs | Quest adb 命令速查 | **无 LICENSE** | ★1，push 2026-07-28 | 文档 |
| ~~oculus-manager~~ | https://github.com/AwA-VR/oculus-manager | 老 GUI | GPL-3.0 | ★4，**最后 push 2023-05-01** | **已弃** |
| ~~quest-vd-wired~~ | https://github.com/kkoemets/quest-vd-wired | USB-C 网卡模式跑 VD | Apache-2.0 | ★19，**仓库已 archived** | **已弃** |
| ~~SideQuestAppLauncher~~ | https://github.com/SideQuestVR/SideQuestAppLauncher | — | 无 | push 2021-02-13 | **死** |

**关键观察**

1. **没有一个在做我们做的事。** 上表全是「操作 / 看画面 / 看日志 / 看性能」。
   没有一个去判「MAC 是不是随机」「头显里有没有挂 VPN」「客户端进程死没死」。
   **我们的三个子判定在这个方向上是唯一的，不是重复造轮子。**
2. **visor 功能最接近**（VR profiler + MCP + 无线一键配对），但 **2 星 / 1 个 v0.1.0 / 周下载 2 次**。
   **成熟度不足以进一条「先做什么」的建议。**
3. **需要 root 的一个都没进推荐**，因为假设不了。
   root 类工具（Quest Tools 一类 https://dabean24.itch.io/quest-tools ）我们**不采用**：
   官方 APK + 未 root 上 `su` 不存在，`HeadsetDeepProbe.cs:270` 的兜底直接失效。
4. **MQDH 的 Meta ADB fork** 值得单记：用 streamed file management，大文件 push/pull/install 明显快。
   **但它是 MQDH 的闭源产物**，ADBO 只是包了个 `.cmd`。
   我们不可能 vendor 闭源二进制（`.gitignore` 也禁 `*.exe`/`*.dll`）。
5. **Quest 官方文档仍然只教 `tcpip`，不教 `pair`**：
   https://developers.meta.com/horizon/documentation/native/android/ts-adb/
   给的流程是 `adb devices` → `adb shell ip route` 取 `src` 后的 IP → `adb tcpip <port>` →
   `adb connect <ip>:<port>`，示例端口 5555，输出形态
   `restarting in TCP mode port: 5555` / `connected to 10.0.32.101:5555`（`[未验证]`）。

---

## 4. 采纳 / 自研 / 拒绝

### 4.1 采纳（ADOPT）

| 项 | 怎么采纳 | 为什么 |
|---|---|---|
| **scrcpy** | **文档化外部前置**，不 vendor、不打包 | 唯一能补上「头显里到底显示了什么」的手段，而这正是我们**原理上读不到**的（`03-symptom-decision-table.md:173` 已论证「手柄通不通」无 adb 判据）。Apache-2.0、★151k、持续维护。写进 `01-adb-playbook.md` 作为「需要人眼看画面时的旁路工具」 |
| **MQDH 的存在** | **文档化外部前置** | 若首连发现标准 adb 在 Quest 上有兼容问题，MQDH 是官方退路，且自带 Meta ADB fork。写进 playbook 的「adb 找不到 / 不兼容」分支 |
| **Meta 官方 ADB 文档** | **文档化引用** | `ts-adb` 是 `tcpip` 之外唯一第一方来源，playbook 的 `tcpip` 片段应标出处 |
| **visor** | **只登记为「以后再评估」** | 功能最接近但成熟度不够。**本轮不采纳、不排除**。重评触发条件写死：star ≥ 300 或 release ≥ 1.0，且它开始读 Virtual Desktop 相关的头显端状态 |
| **`adb mdns services`** | **采纳为「将来的端口发现手段」，本轮不实现** | 实测可用（本机 `mdns daemon unavailable`，但命令 exit 0、96 ms 返回表头）。它是 §4.3 那个 `5555` 硬编码的正解 |

### 4.2 自研（WRITE OURSELVES）

| 项 | 为什么现有的不够 |
|---|---|
| **修 `HeadsetProbe` 的序列号解析**（最高优先级） | `HeadsetProbe.cs:52` 按空格切，`adb devices -l` 是制表符分隔。已实测 adb 会拒绝被污染的 `-s`。`HeadsetDeepProbe.cs:414-424` 有正确写法可直接照抄。**它现在会伪装成一条 Block 级根因** |
| **`headset` 屏读设备状态** | `HeadsetProbe.cs:50-54` 完全不看 `unauthorized`/`offline`。`HeadsetDeepProbe.cs:444-460` 有现成文案表 |
| **`DescribeFailure` 的超时文案** | `HeadsetDeepProbe.cs:439` 硬编码「8 秒」，实际有 6000/8000/15000ms 四种。**给人看的第一手证据在说谎** |
| **`kill-server &&` 文案** | `HeadsetDeepProbe.cs:457`。给用户复制的字符串应跨 shell 稳 |
| **整轮超时 + 取消** | 最坏 5 分 49 秒（§2.4），CLI 传 `CancellationToken.None`，WPF 无停止按钮 |
| **无线调试的端口发现** | 见 §4.3 |
| **版本门槛** | `AdbLocator` 只有 `File.Exists`。本机 adb 1.0.41 有 `pair`（`adb help` 实查）与 `mdns`，但更老的 adb 没有。`11-quest-dashboard/02-screen3-design.md:481` 规划过未实现 |
| **修 Unity 通配候选** | `AdbClient.cs:65` 的 `EnumerateFiles` 应为 `EnumerateDirectories`（已用假树实测） |
| **「查找 adb」不要写盘** | `HeadsetViewModel.cs:86` 每次加载都写 `config.json`。旧 VDH 的已知坑被原样搬回 |
| **`http_proxy` 判定** | playbook `:348` 有规则，代码只展示不判 |
| **「连接端口 ≠ 5555」的知识** | 两处文案仍在说 5555，见 §4.3 |

### 4.3 无线调试：现有文案有一处**事实错误**（必须改）

`HeadsetDeepProbe.cs:465` 与 `HeadsetProbe.cs:86` 都写着：

> …PC 上 `adb pair <头显IP:配对端口> <配对码>`，再 `adb connect <头显IP>:5555`

**第二句是错的。** AOSP 的 ADB Wifi 设计文档明写：

> After pairing, and if the user has enabled "Wireless debugging", adbd listens on a
> **TCP server socket (port picked at random). This is not the same as the legacy `tcpip` socket.**

—— https://android.googlesource.com/platform/packages/modules/adb/+/HEAD/docs/dev/adb_wifi.md

配对成功后，**连接端口是另一个随机端口**，不是 5555；5555 只属于「USB + `adb tcpip 5555`」那条老路。
头显屏幕上会同时显示**配对端口**和**连接端口**两个数字。
`playbook §4.1`（`01-adb-playbook.md:466-474`）写对了（`adb connect <ip>:<connect-port>`），
**代码的文案没照抄 playbook**——和权限那次是同一个病（这一轮已修）。

正解：`adb mdns services` 会把 `_adb-tls-connect` / `_adb-tls-pairing` 连同端口一起列出来
（本机实测命令可用，96 ms），用它替掉用户手抄端口这一环。

### 4.4 拒绝（REFUSE）

| 拒绝项 | 原因 |
|---|---|
| **任何伪造 entitlement / token / 签名材料** | 头显客户端 `VrApp.cs:262-274` 在拿不到 viewer entitlement 时 `Environment.Exit(1)` + `CurrentProcess.Kill()`，拿不到 user 则返回 `PlatformAccessToken(…, string.Empty)`。**这条路只有真登录能过。** 任何绕开它的方案（本地 mock 一个 Oculus platform token、伪造 `viewerEntitledTask`）都是伪造第三方授权，本项目不做 |
| **重签名 / 重新打包官方 APK** | `VrApp.cs:49` 嵌的是**公钥证书**不是私钥。用它签出的包只能证明「同一签名者」，不能凭空获得 entitlement。`01-adb-playbook.md:239-241` 已记：官方版与补丁版签名不同、同包名无法覆盖安装。**重签名不产生新权限** |
| **依赖 root 的工具**（含把 `su` 兜底当**主**路径） | 官方 APK + 未 root 上不存在 `su`。`HeadsetDeepProbe.cs:270` 的 `su` 可保留为「有 root 就用」，但不得作唯一路径，也不得在 UI 上暗示可以要求用户 root |
| **vendor 任何外部二进制**（MQDH 的 Meta ADB、ADBO、Android Toolkit 自带 adb） | 闭源 / 无 LICENSE（Android Toolkit、Quest-Update-Tool、MetaADBdocs 的 API license 字段都是 `None`）；`.gitignore` 禁 `*.exe`/`*.dll`；本仓 `dist/` 每版只有自签 `VdHelper.exe` + `SHA256SUMS.txt` + `VERSION.txt` |
| **`adb -a` / `-L` 放开 server 监听** | `01-adb-playbook.md:486` 已写：adb server 默认只 listen localhost，**单机检测不需要 `-a`**，跨机不在本项目范围 |
| **用 VPN / 代理「绕过」发现问题** | B4 子判定就是查这个（`:320-357`）。给用户「挂个代理试试」等于把 §1.4 的根因反过来当解法 |
| **`kkoemets/quest-vd-wired`** | 仓库 **archived**。它会改变头显的网络拓扑——而 A6（随机 MAC）与 B4（VPN/路由）判定的前提正是「头显网络是用户自己的路由器」。用它做实验会污染实验 |

---

## 5. 头显接上那天：第一轮清单

> 顺序有意：**先 USB 后无线、先授权后命令、先看到 `device` 再跑任何判定。**
> 全部 adb 命令用绝对路径（本机不在 PATH）：
>
> ```powershell
> $adb = 'D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe'
> ```
>
> 若 5.1 就通过，5.2 之后所有 `[未验证]` 项都可先跳过——§5.6 那张表就是为此准备的。

### 5.0 阶段 0：接线**之前**（现在就能做完）

| # | 动作 | 判据 |
|---|---|---|
| **0.0** | **确认 `192.168.11.14` 是不是那台头显** | 见下方专门小节。**本轮实测该地址 ping 通（MAC `c2-90-b8-76-94-1e`），但 adb/VD 端口全关。**先确认它是谁 |
| 0.1 | 确认 adb 路径没变 | `& $adb version` → `1.0.41` / `30.0.4-6686687`。变了就改 `config.json` 的 `adbPath` |
| 0.2 | 留 `--adb` 基线 | `VdHelper.exe --adb; echo $LASTEXITCODE` → 存下 §2.3(b) 那段，`exit=4` |
| 0.3 | 干净起点 | `& $adb kill-server` |
| 0.4 | 留 PC 侧基线 | `VdHelper.exe --selftest --out pre.txt`，记下 `lan-reach` 状态（当前本机唯一 blocker） |
| 0.5 | 头显充满电 | `install_template.bat:82-85`：头显睡着/没戴时 VR app 不起前台进程。**没电的 Quest 3 插线也读不到东西** |

#### 0.0 展开：`192.168.11.14` 是谁

本轮实测的三条事实：

```
ping 192.168.11.14   → 3 发 3 收，0% 丢包，1–3 ms，TTL=64
arp  -a              → c2-90-b8-76-94-1e   dynamic
本机以太网           → 192.168.11.2 / B0-82-E2-6C-F7-EE
5555 / 38810 / 38820 / 38830 / 38840 / 5037 → 全部 closed/filtered
```

**判定步骤：**

1. 在头显里 **设置 → Wi-Fi → 当前网络 → IP 地址**，读出真实 IP。
2. 若**就是 `192.168.11.14`** → 那它在线、只是 adb 没开，进 5.1。
3. 若**不是** → `config.json` 的 `headsetIp` 是**陈旧/错误**的：
   拿真实 IP 覆盖它，然后**顺手查一下 `192.168.11.14` 是什么**（路由器后台的 DHCP 客户端列表最快）。
   MAC `c2-90-b8` 在 `api.macvendors.com` 查不到，且 `0xC2` 的本地管理位为 1 → **它是私有/随机 MAC**，
   手机、IoT、部分路由器客户端都长这样。**不要假设它是头显。**
4. 无论哪种，都**不要动 `lan-reach` 的判定逻辑**——它已经正确地分开了
   「网络通、adb 不通」和「网络也不通」（`HeadsetProbe.cs:74-83`）。

> **这一条同时是 A6 子判定的一次预演**：`c2` 的低两位 = `0b10`，
> 即组播位 0 / **本地管理位 1**——`HeadsetDeepProbe.cs:189` 会报的形态。
> 如果它真是头显且走随机 MAC，那 A6 命中且**这是合法的**，只是路由器若按 MAC 绑定/过滤才会打断连接（`:473-475`）。

### 5.1 阶段 1：USB 连接与授权（**默认路径**）

| # | 动作 | 成功长什么样 | 不像就停在哪 |
|---|---|---|---|
| 1.1 | USB-C 接 PC，**原装数据线**（充电线没有数据线芯，`01-adb-playbook.md:485`） | Windows 枚举到设备 | 设备管理器只有 MTP → 换线 |
| 1.2 | 戴上头显等它完全醒来 | 弹出「允许 USB 调试吗？」 | 没弹窗 → 头显 设置 → 关于 → **开发者选项** 确认「USB 调试」已开（官方文档同样这么说） |
| 1.3 | 点「允许」，**勾「始终允许」** | 弹窗消失 | — |
| 1.4 | `& $adb devices -l` | `<serial>\tdevice product:… model:… device:… transport_id:N` | 见下方三分支 |

> 这一步同时确证 `01-adb-playbook.md:141` 的形态断言（`\t` 分隔 + `state` 取值集合）。
> **请把真实行原样贴回来**——那是 §1.2 序列号 bug 的判定依据。

| 看到什么 | 含义 | 下一步 |
|---|---|---|
| `<serial>\tdevice` | **成了** | 进 5.2 |
| `<serial>\tunauthorized` | 头显没授权 PC | 拔插一次；仍不行重开开发者选项 |
| `<serial>\toffline` | 线通了、握手没完成 | 戴头显唤醒；`& $adb kill-server` 后重插 |
| **列表为空** | 线 / 驱动 / 开发者模式 三者之一 | 换线 → 查开发者选项 → 装 https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/ |

### 5.2 阶段 2：确证「工具看得见设备」

| # | 动作 | 成功长什么样 |
|---|---|---|
| 2.1 | `& $adb -s <serial> shell getprop ro.product.model` | `Quest 3` / `Quest 3S` |
| 2.2 | `& $adb -s <serial> shell getprop ro.build.version.release` | Android 版本号 |
| 2.3 | `& $adb -s <serial> shell pm list packages \| Select-String -Pattern "VirtualDesktop\|dwgx1\|vrdesktop"` | 至少一行 `package:<name>` |
| 2.4 | 头显里**手动打开** Virtual Desktop，再跑 `& $adb -s <serial> shell pidof <pkg>` | 回一个纯数字 PID |
| 2.5 | `VdHelper.exe --adb --serial <serial>; echo $LASTEXITCODE` | 见 5.3 |

> 2.4 **必做**：没有前台进程时 `pidof` 回空是**预期行为**，不是故障
> （`01-adb-playbook.md:267-268`）。不先做这步，5.3 的输出会把「没开 App」误读成「客户端自己退了」。
> `--serial` 是 `--adb` 的可选参数（`App.xaml.cs:455-456`；README:105）。

### 5.3 阶段 3：`--adb` 的预期输出

`--adb` 现在**两个探针都跑**（`App.xaml.cs:471-476`），逐个打印
`[状态] <id>  摘要` → 缩进 `Detail` → 每个证据项 `key: value` → `指引：`（`App.xaml.cs:477-490`）。
退出码取两者更差者（`:494-504`）。

| 落点 | 长什么样 | 含义 |
|---|---|---|
| **最好** | `[Pass] adb  1 个客户端包在运行，7 项权限齐全` + `[Pass] headset-deep  头显 Wi-Fi MAC 是厂商分配地址（…）；头显设置读到 2/2 个文件；Quest 上没查到 VPN/代理进程（逐个查了 25 个名字）` | 三项全过。**注意 F1 要读 `/data/user/0/…`，官方 APK + 未 root 下必然 Unknown，所以首连拿到全 `Pass` 是意外之喜，不是常态** |
| **正常** | `[Unknown] headset-deep  读不到头显 Wi-Fi MAC（两条命令都没拿到地址）；头显本地设置读不到（需要 patched APK 可调试或已 root）；进程列表读不到，VPN 未能判定` | 控制面通了、深度项前置条件没有。**不是故障**（`:147-148` 的 Detour 原文就这么写） |
| **权限那栏要特别看** | `权限 com.oculus.permission.USE_SCENE: 该系统不认识这条（非故障）` | **正常**——同一 Quest OS 上 `com.oculus.*` / `horizonos.*` 通常只有一个存在（`HeadsetProbe.cs:224-228`）。**但若 7 项全部落进这一栏，就说明 `dumpsys package` 的行形态与正则不匹配 → 假通过，必须回 §5.6 A7 核对** |
| **要修** | `[Unknown] headset-deep  头显 <S> 状态是 unauthorized` | 回 5.1.4 |
| **不该出现** | `[Block] headset-deep` | deep **永不返回 Block**（`:138-142`）。出现即代码有 bug |
| **不该出现** | `[Warn] … 读不到头显 Wi-Fi MAC` | Warn 只来自「读到了且判定有问题」。读不到一律 Unknown |

第三屏 `headset` 的预期：**修掉序列号 bug 之前，首连当天预期 `headset` 会全线报「读取失败」，
并可能落到 `Block`「客户端装了，但没有进程在运行」**（`:162-172`）。
**先记下这个已知偏差、把原始输出留档当验收基线，别在当天改产品代码。**

### 5.4 阶段 4：无线调试（**兜底，不是默认**）

> **纪律**（`01-adb-playbook.md:619-643`）：默认只走 USB。无线是**明文/弱加密通道**
> （老 `tcpip` 完全不加密，见 adb_wifi.md 原文）。**关掉无线调试不会关掉 ADB 本身，也不影响 VD 串流**
> ——VD 串流走自己的 38810–38840（UDP 38850 广播发现），与 5555 无关。

**路径 A（老式 `tcpip`，兼容性最好，官方文档只教这条）**

```powershell
# 前提：已 USB 授权，`adb devices -l` 显示 `<serial>\tdevice`
& $adb -s <serial> shell ip -4 addr show wlan0      # 取头显 IP
& $adb -s <serial> tcpip 5555                       # 期望：restarting in TCP mode port: 5555
& $adb connect <头显IP>:5555                        # 期望：connected to <头显IP>:5555
```

- 这两条的输出形态 `01-adb-playbook.md:455`、`:462-463` 标着 `[未验证]`——**本步就是来确证它们的**。
- **`tcpip` 模式在设备重启后失效**（`:484`），插回 USB 可能让状态变 `offline`。
- **本机实测：连不可达地址时 `adb connect` exit 仍是 0**（21126 ms）。
  **不要用退出码判断 connect 成功，只看 stdout 有没有 `connected to`。**

**路径 B（`pair`；本机 adb 1.0.41 的 `adb help` 确有 `pair HOST[:PORT] [PAIRING CODE]`）**

```powershell
# 头显：设置 → 开发者选项 → 无线调试 → 使用配对码配对设备
# 屏幕显示【IP 地址与端口】+【配对码】——端口随机，形如 37xxx  [未验证]
& $adb pair <头显IP>:<配对端口> <配对码>     # 配对码必须当 argv 传，不要让它读 stdin
# 成功后头显显示【IP 地址和端口】——另一个随机端口，不是配对端口，也不是 5555
& $adb mdns services                        # 应列出 _adb-tls-connect / _adb-tls-pairing 及端口
& $adb connect <头显IP>:<连接端口>
```

- **端口不是固定值**：adb_wifi.md 明写「port picked at random. This is not the same as the legacy `tcpip` socket」。
- `01-adb-playbook.md:474` 里的 `37000` 只是**形态举例**。
- **代码里那句 `adb connect <头显IP>:5555`（`HeadsetDeepProbe.cs:465`）是错的**（§4.3），先绕开。

**两条都失败时的排查**（全部来自 `01-adb-playbook.md:476-486`）：

| 症状 | 检查 |
|---|---|
| PC 与头显 `/prefix` 不同段 | 同子网是可靠前提 |
| 头显在访客网络 / 路由器开了 AP 隔离 | 两台设备互相不可见 |
| PC 有 VPN/虚拟网卡 | `route print <头显IP>` 走了非物理网卡 |
| 头显睡着 | 先唤醒 |
| `adb connect` 报 `Connection refused` | **不要开 `-a`**，先查是不是 5037 被挡 |

### 5.5 三个最可能发生的失败，以及它们各自说明什么

| # | 失败 | 最可能的原因 | 它说明的是 | 立刻做什么 |
|---|---|---|---|---|
| **F1** | `adb devices -l` **列表为空** | ① 线只有电源没数据 ② 没开开发者模式/USB 调试 ③ 缺 OEM USB 驱动 | **控制面根本没通。** 这时任何「头显里看不见电脑」的抱怨都**还不能归因到 VD**——USB 授权没做，就没资格讨论串流 | 换线 → 查开发者选项 → 装官方驱动。三条排掉还空就是硬件 |
| **F2** | 状态停在 **`unauthorized`** | 头显没点「允许 USB 调试」，或点过「拒绝」被记住 | **控制面半通。** USB 通道建立了但 RSA 授权没给。注意 `headset` 屏此时会给**误导性的**「没找到 Virtual Desktop 客户端」Warn（`:154-157`），**别信它**；deep 屏的文案才是对的 | 拔插 → 重开开发者选项 → 若曾授权过别的机器，先在头显里撤销 |
| **F3** | 状态是 **`offline`** | 头显睡着；刚插上还没握手；`tcpip` 后又插回 USB/重启 | **控制面建立了但设备不响应。** 常见于「adb tcpip 后重启」这条老路的已知失效（`:484`） | 戴头显唤醒 → `& $adb kill-server` → 重插 |

> 三个的共同点：**都在「头显能不能被 PC 触达」这一层，全都不是 VD 自己的故障。**
> 这正是 `HeadsetDeepProbe` 降级点 1/2/3（`:78`、`:92`、`:121-128`）的意义——**读不到就报 Unknown，不猜。**
> **首连当天要顶住这条纪律。**

> **本轮已经先撞上了第四种**：`192.168.11.14` ping 通但 adb 列表空
> → 两个探针都报「头显在网络上，但 adb 连不上它」。
> **这是本机当前的真实状态**，也是 5.0 step 0.0 存在的原因。

### 5.6 首连当天应当被消掉的全部 `[未验证]`

#### A. 来自 `01-adb-playbook.md`

| # | 条目（出处） | 确证命令 | 期望形态 |
|---|---|---|---|
| A1 | `adb devices -l` 有设备时的完整行形态（`:139`、`:651`） | `& $adb devices -l` | `<serial>\tdevice product:… model:… device:… transport_id:N`。**原样贴回** |
| A2 | `tcpip 5555` 成功输出（`:455`、`:652`） | `& $adb -s <S> tcpip 5555` | `restarting in TCP mode port: 5555` |
| A3 | `connect` 成功/失败形态（`:462-463`、`:653`） | `& $adb connect <IP>:5555` | `connected to <IP>:5555` / `failed to connect` / `cannot connect … Connection refused` |
| A4 | `pair` / pairing-port 形态（`:474`、`:654`） | `& $adb pair <IP>:<随机端口> <6位码>` | 成功提示 + 头显显示**另一个**连接端口 |
| A5 | 「允许 USB 调试」弹窗文案/触发时机（`:167`） | 人工观察 | 记录原文 |
| A6 | `pm list packages` 命中行（`:211`、`:655`） | `& $adb -s <S> shell pm list packages \| Select-String VirtualDesktop` | 至少一行 `package:<name>` |
| **A7** | **`dumpsys package` 的 `granted=true` 行形态（`:247`、`:562`、`:656`）** | `& $adb -s <S> shell dumpsys package <pkg> \| Select-String granted=` | **7 项对应行。必须逐字核对是否与 `HeadsetProbe.cs:212-214` 的正则 `^(?<name>[A-Za-z0-9_.]+):\s*granted=(?<g>true\|false)` 匹配——不匹配就会全部落进 `Absent` 而报「非故障」，即假通过。** 顺带跑 `pm list permissions -g \| Select-String "SCENE\|TRACKING"` 补上 `:519` 标着未验证的 permission-group 真名 |
| A8 | `ps -A` 完整行（`:265`、`:657`） | `& $adb -s <S> shell ps -A` | `USER PID PPID VSZ RSS WCHAN ADDR S NAME`，最后一列是进程名 |
| A9 | `ip -4 addr show wlan0` 形态（`:295`、`:658`） | `& $adb -s <S> shell ip -4 addr show wlan0` | `N: <ifname>:` + `inet <a.b.c.d>/<prefix>` 缩进块 |
| A10 | `ip route show` / dns / `private_dns_mode`（`:330`、`:340`、`:658`） | `& $adb -s <S> shell ip route show`；`… shell getprop \| Select-String dns`；`… shell settings get global private_dns_mode` | 记录原样 |
| A11 | `settings get global http_proxy` 形态（`:348`、`:659`） | `& $adb -s <S> shell settings get global http_proxy` | `null`/`:0` → 出网不走代理 |
| A12 | `getprop` 单行形态（`:365`） | `& $adb -s <S> shell getprop ro.build.version.release` | `key: value` |
| A13 | `dumpsys wifi` 里 SSID/频段/LinkSpeed/RSSI 可读性与字段名（`:319`、`:660`） | `& $adb -s <S> shell dumpsys wifi` | 能读到就读；读不到试 `& $adb -s <S> shell cmd wifi status`（`:320`、`:661` 同标未验证） |
| A14 | `pm list permissions -g` 里 SCENE/TRACKING 的组名（`:519`、`:663`） | 见 A7 第二条 | 补上组名 |
| A15 | `dumpsys wifi \| grep VRD` 能否看到 WiFiLock（`:589`、`:664`） | App 前台时 `& $adb -s <S> shell dumpsys wifi \| Select-String VRD` | 出现 `VRD`（`VrApp.cs:80` 的 `CreateWifiLock(4,"VRD")`） |
| A16 | `dumpsys power` / `dumpsys activity activities`（`:662`） | `& $adb -s <S> shell dumpsys power`；`… shell dumpsys activity activities` | 记录原样；顺带看 `mWakefulness` |
| A17 | `df -h /data`（`:421-422`、`:665`） | `& $adb -s <S> shell df -h /data` | 剩余空间。`INSTALL_FAILED_INSUFFICIENT_STORAGE` 需**故意触发**才拿得到，不必制造 |

#### B. 来自 `02-logcat-triage.md`

| # | 条目（出处） | 确证命令 | 备注 |
|---|---|---|---|
| B1 | `--pid=` 与 `-s TAG:LEVEL` 在 HorizonOS 上的可用性与组合（`:143`、`:537`） | `& $adb logcat --help`（先读 `-s` 的解释）；拿 PID 后 `& $adb -s <S> logcat --pid=$PID -d` | 输出发回 |
| B2 | `cmd wifi status` 是否可用（`:538`） | `& $adb -s <S> shell cmd wifi status` | 与 A13 一起 |
| B3 | `dumpsys netstats` 能否证明 UDP 38850 发出（`:539`） | 头显里触发一次刷新后 `& $adb -s <S> shell dumpsys netstats \| Select-String 38850` | 需头显侧主动触发 |
| B4 | Streamer 日志文件名（`:371`） | **不需要头显**：`Get-ChildItem "$env:ProgramData\VirtualDesktop" -Recurse -Filter *.log` | 级别 `LogLevel.Error` 起 → 成功流程不落盘 |
| B5 | 是否存在被 release 裁掉的 `Log.Debug` 路径（`:38`） | **本工作区无证据** | 属设计限制，标 `[无法验证]`，不要写成待办 |
| B6 | 成功串流时的 logcat 长什么样（`:536`） | 头显里**真连一次**，PC 侧 `& $adb -s <S> logcat -d -s VRD:V` | 本工作区 11 份 logcat 无一份是成功串流的 |
| B7 | §6 全部 7 条假设（`:540`） | 逐个开关功能各抓一次 | 成本高，先做 B6 |

#### C. 来自 `03-symptom-decision-table.md`

| # | 条目（出处） | 确证命令 |
|---|---|---|
| C1 | `ip route show` / `ro.vros.build.version` / `http_proxy` 形态（`:164`） | `& $adb -s <S> shell ip route show`；`… getprop ro.vros.build.version`；`… settings get global http_proxy` |
| C2 | `dumpsys wifi` 的 SSID/频段/LinkSpeed/RSSI 字段名（`:165`） | 同 A13。**关键读数**：频段 ≥ 4000 MHz 且协商速率 ≥ 450 Mbps（App 自己判慢的双阈值，`WifiMetrics.IsSlow()`） |
| C3 | `dumpsys audio \| grep -i mic` 形态（`:63`、`:166`） | `& $adb -s <S> shell dumpsys audio \| Select-String -Pattern mic -CaseSensitive:$false` |
| **C4** | **`pm grant` 7 条的返回形态（`:167`）** | 逐条 `& $adb -s <S> shell pm grant <pkg> <perm>`。判据四种：空输出=成功、`not a changeable`=非 runtime、`has not requested`=APK 没声明、`Exception`=失败。**顺手确证 `com.oculus.permission.*` 与 `horizonos.permission.*` 在目标 OS 上各自成不成立**——这直接验证 `HeadsetProbe.cs:35-40` 那 7 个串 |
| C5 | `dumpsys package` 的 `granted=true` 形态（`:168`） | 同 A7 |
| C6 | 头显 UI 码率滑条实际值能否从 adb 读到（`:169`） | 读 `/data/data` 需 root 或 `run-as`。**除非已 root，否则 `[无法验证]`**，别写成待办 |
| C7 | 「连上立刻断开」时 `Get-NetTCPConnection` 的实际条数（`:170`） | PC 侧 `Get-NetTCPConnection -RemotePort 38810,38820,38830,38840`，与头显联动复现 |
| C8 | 「只建了音频没建视频」时的连接表（`:171`） | 同 C7，四通道分开看 |
| C9 | `dumpsys input` 里控制器枚举字段名（`:111`、`:116`、`:119`、`:172`） | 控制器**已配对开机**时 `& $adb -s <S> shell dumpsys input \| Select-String -Pattern "controller\|oculus_touch\|drift" -CaseSensitive:$false` |
| C10 | `getprop ro.product.model` 形态（`:116`） | 同 2.1 |
| C11 | 「手柄生效」本身（`:173`） | **无解**，需用户主观确认。`InputSystem.Update` 每帧跑、无 Log、无状态文件 |

#### D. 本文新引入的待验证项

| # | 条目 | 确证命令 |
|---|---|---|
| **D1** | `adb ≥ 1.0.31` 的 `escape_arg` 是否真的处理 `run-as` 路径里的空格（`HeadsetDeepProbe.cs:260` 的注释） | 头显 root 或 debuggable APK 就绪后 `& $adb -s <S> exec-out run-as <pkg> cat "/data/user/0/<pkg>/files/.config/Virtual Desktop/UserSettings.json"`。**返回空 → 那句注释是错的，F1 在带空格路径上不可靠** |
| **D2** | `exec-out su -c "cat <带空格路径>"` 的实际行为（`:270`） | 同上。`su` 对 `-c` 参数的引号处理各家不同，被按空格切开就会失败 |
| D3 | `dumpsys thermalservice` 在 Quest OS v14+ 能否读到 45 个传感器 | `& $adb -s <S> shell dumpsys thermalservice`（visor 的 README 说 v14+ 上 `/sys/class/thermal` 被拒，改走这条） |
| D4 | 头显上「无线调试」入口在当前 HorizonOS 上**是否存在** | 人工看 设置 → 开发者选项。社区 2025-02 有「Wireless ADB seems to be gone」的报告，但**本轮未能取到原帖正文**（Reddit 反爬），**标 `[未验证]`，不作为结论** |
| **D5** | `HeadsetDeepProbe.cs:212-214` 的正则与真实 `dumpsys package` 行是否匹配 | 见 A7。**这是首连当天优先级最高的 D 项**——不匹配就是假通过 |

### 5.7 首连当天**不要**做的事

| 别做 | 为什么 |
|---|---|
| 在没留基线前改产品代码 | `--selftest`（step 0.4）与 `--adb`（step 0.2）基线是验收的唯一参照 |
| 改 `AdbLocator` 的候选顺序 | 本机只有 VIVE Hub 一条命中，改顺序等于凭想象改 |
| 动 `config.json` 里的 `headsetIp` **而不先确认那是谁** | 见 §5.0 step 0.0。`192.168.11.14` 现在 ping 得通但端口全关，身份未确认 |
| 用 VPN/代理试「能不能找到电脑」 | B4 子判定的前提就是「头显网络是用户自己的路由器」，改了没法归因 |
| `adb -a` / 放开 5037 | 单机诊断不需要，跨机不在范围（`01-adb-playbook.md:486`） |
| 顺手 `adb root` / `adb remount` | 官方 APK 未 root 时必然失败；且会改变 F1 的读法，让「读不到」与「读到了」不可比 |
| 因 F1 是 Unknown 就判定工具坏了 | F1 需 patched APK 可调试或 root（`:483-485` 自己就这么写），官方 APK 上永远 Unknown 是**设计** |
| 把权限栏「该系统不认识这条」当成通过 | 7 项全落进这一栏说明正则没匹配上 → **假通过**（D5） |
| 相信 `headset` 屏的 `Block` 而不看 `headset-deep` | 序列号 bug 未修时 `Block` 可能是假的（§1.2 步骤 1） |

---

## 附录 A：外部来源

| 用途 | URL |
|---|---|
| Quest 官方 ADB 指南（`tcpip` 流程、驱动、`kill-server`、多设备 `-s`） | https://developers.meta.com/horizon/documentation/native/android/ts-adb/ |
| AOSP ADB Wifi 设计（**随机端口、非 `tcpip` socket、六位配对码**） | https://android.googlesource.com/platform/packages/modules/adb/+/HEAD/docs/dev/adb_wifi.md |
| adb(1) 手册页 | https://android.googlesource.com/platform/packages/modules/adb/+/show/refs/heads/main/docs/user/adb.1.md |
| `pm list permissions` 实现（证明输出只有 `permission:<name>`） | https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android11-dev/services/core/java/com/android/server/pm/PackageManagerShellCommand.java |
| adb `escape_arg` 用例（`adb_install.cpp`） | https://android.googlesource.com/platform/system/core/+/a49d024/adb/client/adb_install.cpp |
| Meta Quest Developer Hub（6.4.x，含 Meta ADB fork） | https://developers.meta.com/horizon/downloads/package/oculus-developer-hub-win/ |
| Meta Oculus ADB 驱动 | https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/ |
| scrcpy | https://github.com/Genymobile/scrcpy |
| SideQuest（开源部分） | https://github.com/SideQuestVR/SideQuest |
| open-quest-hub | https://github.com/Watash1no/open-quest-hub |
| QAdb | https://github.com/ludoven/QADB |
| ADBO | https://github.com/ovsky/ADBO |
| visor（GitHub / PyPI） | https://github.com/chisomobanzi/visor ・ https://pypi.org/project/visor-dev |
| Android Toolkit（**无 LICENSE**） | https://github.com/TeamNocturnal/AndroidToolkit |
| beautycat | https://github.com/jeziellago/beautycat |
| Quest-Update-Tool（**无 LICENSE**） | https://github.com/SasukeSagara/Quest-Update-Tool |
| MetaADBdocs（**无 LICENSE**） | https://github.com/Estati/MetaADBdocs |
| archived：quest-vd-wired | https://github.com/kkoemets/quest-vd-wired |
| 已弃（2023）：oculus-manager | https://github.com/AwA-VR/oculus-manager |
| root 类工具（**明确不采用**） | https://dabean24.itch.io/quest-tools |

## 附录 B：本文实跑过的命令（可复现）

```powershell
$adb = 'D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe'

& $adb version                      # 1.0.41 / 30.0.4-6686687
& $adb devices -l                   # stdout 只有 'List of devices attached'，exit 0
& $adb get-state                    # error: no devices/emulators found，exit 1
& $adb help | Select-String pair    # pair HOST[:PORT] [PAIRING CODE]
& $adb mdns check                   # ERROR: mdns daemon unavailable，exit 0
& $adb mdns services                # 表头行；冷启 11853 ms / 热 96 ms
& $adb pair 192.168.99.99:37000 < $null   # 'Enter pairing code: … No pairing code provided'，0 s 退出
& $adb connect 192.168.99.99:5555   # 'cannot connect … (10060)'，**21126 ms，exit 0**
& $adb -s "2G0YC5ZHBD01XF<TAB>device" shell getprop ro.build.version.release
                                    # adb.exe: device '…<TAB>device' not found，exit 1
& $adb kill-server
& $adb devices -l 2>&1 >$null       # '* daemon not running; …' 走 stderr

ping -n 3 192.168.11.14             # 3 发 3 收，0% 丢包，1–3 ms，TTL=64
arp  -a 192.168.11.14               # c2-90-b8-76-94-1e  dynamic
5555/38810/38820/38830/38840/5037   # 全部 closed/filtered
getmac /v /fo csv                   # 本机 94-B6-09-8D-27-28 / B0-82-E2-6C-F7-EE（均非 c2-90-b8）
https://api.macvendors.com/c290b876941e   # {"errors":{"detail":"Not Found"}}

dotnet build src/VdHelper/VdHelper.csproj -c Release   # 0 error, 4 warning
src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --adb   # 见 §2.3(b)，exit=4
```

以及两条假树/语义实验：`EnumerateFiles` vs `EnumerateDirectories`（§2.2）、
.NET `Split(' ')` vs `Split((char[]?)null)` 在制表符行上的差别（§1.2）。

## 附录 C：本文**没有**验证的

- 任何真机输出形态 —— 全在 §5.6，逐条给了确证命令。
- `192.168.11.14` 那台设备的真实身份（§5.0 step 0.0）。
- HorizonOS 当前版本上「无线调试」入口是否存在（D4；社区报告未取到原帖）。
- `pm list permissions` 在 **HorizonOS 具体版本**上的实际输出（AOSP 语义已确认）。
- `com.oculus.permission.*` 与 `horizonos.permission.*` 在**目标 OS 版本**上哪些存在、哪些被 `pm grant` 接受（C4）。
- `HeadsetDeepProbe.cs:212-214` 正则与真实 dumpsys 行的匹配情况（D5，**优先级最高**）。
- scrcpy / visor / open-quest-hub 在 Quest 上的**实际可用性**——本文只读了 README 与仓库元数据，**没有装过任何一个**。