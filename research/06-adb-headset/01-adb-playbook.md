# 01 — ADB 头显侧 Playbook（获取、连接、能读到什么、权限、无线安全边界）

> 面向 VDH 的「头显检测」页面。本文件所有结论带证据来源（`file:line` 或本机真实命令输出）。
> **本机 2026-10-05 未连接任何头显**，因此凡需要真机的命令输出形态一律标 `[未验证]`，
> 并写清「验证条件」。所有 PC 侧命令输出是 2026-10-05 在本机实跑得到的原文，不是伪造。

---

## 0. 一句话结论

`adb.exe` 不在 PATH（实测），但本机 **VIVE Hub 自带的 adb 30.0.4 存在且可直接用**，
旧 VDH 的候选顺序里它排第 16 位、能命中；无线 ADB 走 `adb pair`（30.0.4 已支持，见 §1.4 实测 help 原文）。
包名真值是 **`VirtualDesktop.Android`**（不是 `com.vrdesktop.streamer`），启动 Activity 是混淆名
`md59102214312e19799944a61bf7bc2f23e.VrActivity`。

---

## 1. adb 获取

### 1.1 本机现状（2026-10-05 实测）

```
$ where adb
INFO: Could not find files for the given pattern(s).     ← exit=1，PATH 里没有

$ echo "ANDROID_HOME=[$ANDROID_HOME] ANDROID_SDK_ROOT=[$ANDROID_SDK_ROOT]"
ANDROID_HOME=[] ANDROID_SDK_ROOT=[]                      ← 两个 SDK 环境变量都空
```

结论：PATH 无 adb，`ANDROID_HOME` / `ANDROID_SDK_ROOT` 都未设置 → 任何依赖环境变量的发现逻辑在本机全部落空。

### 1.2 本机真实可用 adb（VIVE Hub 自带）

```
$ "D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe" version
Android Debug Bridge version 1.0.41
Version 30.0.4-6686687
Installed as D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe

$ ls -la "D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\"
-rwxrwxrwx  97792  AdbWinApi.dll
-rwxrwxrwx  62976  AdbWinUsbApi.dll
-rwxrwxrwx 5193216  adb.exe
-rwxrwxrwx 231594  libwinpthread-1.dll
```

注意这个目录**只有 `AdbWinApi.dll` / `AdbWinUsbApi.dll`，没有 `AdbWinUsbApi.dll` 之外的上层 `AdbWinApi` 依赖缺失问题**
——4 个文件自洽，可直接用。`libwinpthread-1.dll` 必须与 `adb.exe` 同目录，否则 USB 模式会失败（Windows 上 adb 的
MinGW 运行时依赖）。VDHelper 若采用此路径，不要只复制 `adb.exe` 单文件。

### 1.3 发现判定表（抄旧 VDH 的顺序，来源 `reference/legacy_vdh/VDH.Extra.cs:239-297`）

旧 VDH 的三段结构：
- `FindAdb(bool prompt)`（`:244`）：先看配置里记住的 `cfg.AdbPath`，文件还在就直接用（`:246-247`）；
  否则遍历候选表，**第一个存在的即命中并落盘到配置**（`:248-257`）；全空才提示用户（`:258-259`）。
- `EnumerateAdbCandidates()`（`:262`）：纯 `yield return` 的有序列表，**顺序即优先级**。
- `DownloadAdb()`（`:362`）：唯一允许的下载源。

判定表（按 `EnumerateAdbCandidates` 的 yield 顺序逐行抄，附本机 2026-10-05 实测命中情况）：

| # | 候选路径（`VDH.Extra.cs` 行号） | 来源类别 | 探测命令 | 命中判据 | 本机实测 2026-10-05 |
|---:|---|---|---|---|---|
| 0 | `cfg.AdbPath`（用户/上次自动记住） `:246` | 配置 | `File.Exists(cfg.AdbPath)` | 文件存在即用，**不校验版本** | 无配置（新装） |
| 1 | `<exeDir>\adb\adb.exe` `:265` | 自带 SDK（首选） | 同上 | 存在 | miss |
| 2 | `<exeDir>\platform-tools\adb.exe` `:266` | 自带 SDK | 同上 | 存在 | miss |
| 3 | `%AppData%\VirtualDesktopHelper\platform-tools\adb.exe` `:267` | 下载落地目录 | 同上 | 存在（= 曾经下载过） | miss |
| 4 | 逐级上溯最多 6 层的 `<ancestor>\analysis\apk_patch\quest_adb_tools\dist\adb.exe` `:268-273` | repo 内自带 | 同上 | 存在 | miss（`F:\Project\VirtualDesktop\analysis\apk_patch\**/adb.exe` glob 零命中） |
| 5 | `D:\Project\VirtualDesktop\analysis\apk_patch\quest_adb_tools\dist\adb.exe` `:274` | repo 内自带（写死） | 同上 | 存在 | miss |
| 6 | `D:\Project\VirtualDesktop\analysis\apk_patch\output\adb\adb.exe` `:275` | repo 内产物 | 同上 | 存在 | miss |
| 7 | `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` `:276-277` | 标准 Android SDK | 同上 | 存在 | miss |
| 8 | `%ANDROID_HOME%\platform-tools\adb.exe` `:278-283` | SDK 环境变量 | 同上 | 变量非空 **且** 文件存在 | miss（变量为空） |
| 9 | `%ANDROID_SDK_ROOT%\platform-tools\adb.exe` `:278-283` | SDK 环境变量 | 同上 | 同上 | miss（变量为空） |
| 10 | `D:\Software\Android\Sdk\platform-tools\adb.exe` `:284` | 手装 SDK（写死） | 同上 | 存在 | **miss** ← 旧 `.bat` 里写死的就是这条，本机已失效 |
| 11 | `C:\Android\platform-tools\adb.exe` `:285` | 手装 SDK | 同上 | 存在 | miss |
| 12 | `C:\platform-tools\adb.exe` `:286` | 手装 SDK | 同上 | 存在 | miss |
| 13 | `%ProgramFiles%\Android\android-sdk\platform-tools\adb.exe` `:287` | Android Studio SDK | 同上 | 存在 | miss |
| 14 | `%ProgramFiles(x86)%\Android\android-sdk\platform-tools\adb.exe` `:288` | Android Studio SDK | 同上 | 存在 | miss |
| 15 | `D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe` `:289` | VIVE Hub | 同上 | 存在 | **HIT** ← 本机唯一命中 |
| 16 | `D:\Software\VIVE Hub\VIVE Business Streaming\CommonTools\ADB\adb.exe` `:290` | VIVE Hub | 同上 | 存在 | miss（未装 VIVE Business Streaming） |
| 17 | `%PATH%` 每一段目录 + `adb.exe` `:291-296` | PATH 兜底 | 同上，且 `p.Length > 8` | 任一段命中 | miss（PATH 无 adb） |

判定逻辑要点（可直接复用）：
- **顺序即优先级，先命中先赢**（`:248-256`）。
- `PATH` 兜底排在 VIVE Hub **之后**（`:291`），不是最先——这一点旧实现是对的，保留。
- 命中后**立刻写回 `cfg.AdbPath` 并 Save**（`:252-254`），下次直接走 #0。

**建议 VDH 改进（本轮未做，属 NetDiagnosis/StreamerSettings 范围）**：旧实现只判 `File.Exists`，
不校验 adb 版本。实测本机 VIVE Hub 是 30.0.4，够用（§1.4），但更老的 1.0.39 缺 `adb pair`。
判定函数应追加一次 `adb version` 版本号门槛（`< 30` 走下一个候选），否则无线调试功能会在旧 adb 上静默失败。

### 1.4 下载：只允许 Google 官方

来源 `VDH.Extra.cs:362-398`，逐条抄：

| 约束 | 实现 | 行号 |
|---|---|---|
| 唯一 URL | `https://dl.google.com/android/repository/platform-tools-latest-windows.zip` | `:364` |
| 主机白名单 | `u.Host.ToLowerInvariant() != "dl.google.com"` → `return null` | `:366` |
| 协议白名单 | `u.Scheme != Uri.UriSchemeHttps` → `return null` | `:366` |
| TLS 版本 | `ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072`（= TLS 1.2） | `:373` |
| UA | `"VirtualDesktopHelper/" + AppCfg.Version` | `:376` |
| 落地 | `%AppData%\VirtualDesktopHelper\platform-tools\`，zip 解到 AppDir 后删 zip | `:368/380/381` |
| 解压后校验 | `File.Exists(destDir\adb.exe)` 否则弹窗报错 | `:382-387` |

**本轮明确不执行下载**（Non-Conflict 约束）。VDHelper 应保留同一白名单逻辑：一处常量、一次 `Uri` 解析、
两个 `if` 就够，不要引入通用下载器。

### 1.5 已实测的 adb 子命令能力（`adb help` 原文，30.0.4）

```
networking:
 connect HOST[:PORT]      connect to a device via TCP/IP [default port=5555]
 disconnect [HOST[:PORT]]
 pair HOST[:PORT] [PAIRING CODE]
     pair with a device for secure TCP/IP communication
```

→ `adb pair` 存在，这是 Android 11+ 无线调试的必需命令。`adb tcpip 5555` 是老式（非配对）无线调试，
Quest/HorizonOS 上仍可用但要求同子网，见 §4。

---

## 2. 连接头显

### 2.1 `adb devices -l` 输出形态

本机实跑（**无设备**，2026-10-05）：

```
$ adb devices -l
* daemon not running; starting now at tcp:5037
* daemon started successfully
List of devices attached

$ adb get-state
error: no devices/emulators found          ← exit=1
```

有设备时的形态 `[未验证]`：
验证条件 = 头显 USB 连接 PC、在头显里点「允许 USB 调试」。形态是
`<serial>\t<state> product:<p> model:<m> device:<d> transport_id:<n>`，
`state` ∈ `device|unauthorized|offline|no permissions|recovery|sideload|bootloader|host|connecting|detached`。

旧 VDH 的解析逻辑（`VDH.Extra.cs:442-460`）可直接复用：
- 已连接：`dev.IndexOf("\tdevice") >= 0 || dev.IndexOf(" device ") >= 0`（`:445-446`）
- 未授权：`IndexOf("unauthorized", OrdinalIgnoreCase) >= 0`（`:447`）
- 离线：`IndexOf("offline", OrdinalIgnoreCase) >= 0`（`:448`）
- 四态给用户四句中文提示（`:450-453`）

**注意旧实现的两个缺陷**（VDHelper 照抄前要修）：
1. `:446` 的 `" device "` 匹配是子串匹配，`unauthorized` 行里也可能命中 → 建议改成**按 `\t` 切列后精确比对第 2 列**。
2. `:447` 只查 `unauthorized` 不查 `no permissions`，而 `no permissions`（缺 `android.permission.ACCESS_*`
   的 udev 规则）在 Windows 上不出现，可忽略；但 `offline` 与 `unauthorized` 必须**优先于** `device` 判定。

`install_template.bat:38-43` 提供了另一种更粗但更稳的探测：
```
%ADBT% get-state 1>nul 2>nul
if errorlevel 1 ( echo [ERROR] No device. ... )
```
`get-state` 无设备时 exit=1（本机实测确认）。VDHelper 可以在 `devices -l` 解析失败时退化到 `get-state`。

### 2.2 USB 连接与授权流程

1. 插 USB-C。Quest 3 头显**必须有电量**（`install_template.bat:82-85` 明确提示：头显睡着/没戴头上时，
   VR app 不会启动前台进程，adb 里看不到进程是**预期行为**，不是故障）。
2. 头显里弹出「允许 USB 调试吗？」→ 点**允许**（勾选「始终允许」便于后续免弹）。
   弹窗文案/触发时机 `[未验证]`；验证条件同上。
3. 回到 PC 复跑 `adb devices -l`，状态应从 `unauthorized` 转 `device`。
4. 若卡在 `unauthorized`：拔插数据线；仍不行则头显 `设置 → 关于 → 开发者选项 → USB 调试` 确认打开。
5. 已知序列号样例（真机记录）：`2G0YC5ZHBD01XF`
   （`install_template.bat:8`、`capture_vd_delayed_window.bat:23-56` 全部命令都用 `-s 2G0YC5ZHBD01XF`）。
   多头显时靠序列号区分，命令一律带 `-s <serial>`。

### 2.3 多设备纪律

旧 VDH 的 `HeadsetStart/Stop/Grant/Logcat`（`:462-510`）**都没有 `-s`**，依赖「只有一台设备」。
VDHelper 的检测页必须：
- 从 `adb devices -l` 拿到串号列表；
- 多于一台时在 UI 上要求用户选，或全部串号都跑一遍并标注串号；
- **禁止**依赖 `-e` / `-d`（多设备时它们直接报错，见 `adb help` 的 `-d` / `-e` 说明）。

---

## 3. 能读到的头显状态（命令 / 期望形态 / 判读）

包名常量：**`VirtualDesktop.Android`**（成品版）
Activity：**`md59102214312e19799944a61bf7bc2f23e.VrActivity`**

证据：
- `F:/Project/VirtualDesktop/analysis/apk_patch/install.bat:12,25-31` 全部用 `VirtualDesktop.Android`
- `analysis/report_sections/01_architecture_packaging.md:19` 表头「包名 | `VirtualDesktop.Android`」，
  `:130` 明写「补丁版**保留原包名** `VirtualDesktop.Android`」
- `analysis/apk_patch/install_template.bat:15` `set PKG=VirtualDesktop.Android`
- Activity 名来自反编译源码属性标注
  `decompiled/vdandroid/VirtualDesktop.Android/VrActivity.cs:13`：
  `[Activity(Label = "Virtual Desktop", Name = "md59102214312e19799944a61bf7bc2f23e.VrActivity", ... MainLauncher = true, ...)]`
  并与 `analysis/apk_patch/capture_diagnostic.bat:28` 的 `am start -n %PKG%/<该名>` 一致。

⚠️ **不要写成 `com.vrdesktop.streamer`**。历史上出现过两个实验包名，检测页要能同时认出：
- `com.dwgx1.vd.recovered` —— 二进制补丁实验线的包名（`build_apk_renamed.py:34-35` 明确
  `OLD_PKG = 'VirtualDesktop.Android'` → `NEW_PKG = 'com.dwgx1.vd.recovered'`；
  `capture_diagnostic.bat:6` 用它）
- `com.dwgx1.virtualdesktop.recovered` —— 源码重建线的「canonical」包名
  （`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:22-23` 明确说 `com.dwgx1.vd.recovered` **不是**它）

### 3.1 包与版本

```bash
adb shell pm list packages | grep -i -E "VirtualDesktop|vrdesktop|recovered"
```
期望形态：至少一行 `package:<name>`。`[未验证]`（验证条件：头显 USB 连接授权，且装过补丁 APK）。

```bash
adb shell dumpsys package VirtualDesktop.Android
```
真机输出形态已有实例（实验包名，其余字段结构相同）
—— `analysis/apk_patch/V77_AOT_BUILD_INSTALL_REPORT_20260623.md:107-114`：
```
Package: com.dwgx1.vd.recovered
codePath: /data/app/~~5VV4KxzmKExoxKdTr9WoZQ==/com.dwgx1.vd.recovered-JKN8cdSjadm4SpFyPh1n4w==
versionCode: 10686
versionName: 1.34.18.0
lastUpdateTime: 2026-06-23 11:10:01
signatures: [9f4ebf64]
```
另一处更完整的形态（`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:139-142, 226-229`）：
```
Package [com.dwgx1.vd.recovered]
  versionName=1.34.18.0
  firstInstallTime=2026-06-22 07:28:48
  lastUpdateTime=2026-06-23 08:18:44
  ...
launchable activity: md59102214312e19799944a61bf7bc2f23e.VrActivity
```
判读：
- `versionName` 与本机 Streamer 版本（`1.34.22.0`，见 §6）不匹配 → 客户端/串流端版本不对，
  `NetworkManager` 有硬门：头显侧字符串 `"Streamer uses a newer version, update this app before connecting"`
  （`reference/vdapkpatcher/profiles/vd-1.34.22/managed-strings.csv:60`，来自 `<ConnectToComputerAsync>d__74::MoveNext` IL_0245）。
- `signatures:` 非官方 → 装的是补丁版（预期）；官方版与补丁版**签名不同，同包名无法覆盖安装**，
  必须先 `adb uninstall`（`install_template.bat:46-48`、`01_architecture_packaging.md:130`）。
- `launchable activity` 缺失 → APK 装残了。

一次性拿权限现状（**这是「30 秒失焦」那一项的判定命令**）：
```bash
adb shell dumpsys package VirtualDesktop.Android | grep -E "permission" 
```
期望形态：`runtime permissions:` 段下每项形如 `<perm>: granted=true` / `granted=false`。`[未验证]`
（验证条件：头显连接授权 + 已执行 `pm grant`）。

### 3.2 进程

```bash
adb shell pidof VirtualDesktop.Android
```
真机输出形态两例（`V77_..._REPORT:119` 与 `:150`）：
```
pidof com.dwgx1.vd.recovered: no process
```
`pidof` 的空结果在 `adb shell` 里会变成字面量 `no process`（旧 VDH 的 `HeadsetLogcat` 就是靠
`RunAdb("shell pidof " + Pkg)` 返回的字符串判空，`:464-470`）。有进程时输出**纯数字 PID**。

```bash
adb -s <serial> shell ps -A | grep VirtualDesktop
```
形态：`USER PID PPID VSZ RSS WCHAN ADDR S NAME`。`[未验证]`。

⚠️ **「no process」不一定是故障**：`install_template.bat:82-85` 写明头显没戴/睡着时 VR app 不起前台进程。
判定必须结合 §3.7 的唤醒状态一起看，不能只看 `pidof`。

启动 / 停止（抄 `VDH.Extra.cs:473-483`，加 `-s`）：
```bash
adb shell am start -n VirtualDesktop.Android/md59102214312e19799944a61bf7bc2f23e.VrActivity
# 老实现还追加一次 monkey 兜底（:476）：
adb shell monkey -p VirtualDesktop.Android -c android.intent.category.LAUNCHER 1
adb shell am force-stop VirtualDesktop.Android
```
VR 类别启动需要带 intent 类别（真机记录 `V85_..._REPORT:133-134`）：
```
am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR \
  -n <pkg>/md59102214312e19799944a61bf7bc2f23e.VrActivity
```
`capture_vd_delayed_window.bat:23-26` 的完整前置序列（唤醒 → 强停 → 清日志 → 启动）：
```
adb shell input keyevent KEYCODE_WAKEUP
adb shell am force-stop <pkg>
adb logcat -c
adb shell am start -n <pkg>/<activity>
```

### 3.3 网络接口与 IP

```bash
adb shell ip -4 addr show
```
形态为 `N: <ifname>:` + `inet <a.b.c.d>/<prefix>` 缩进块。`[未验证]`（验证条件：头显连接授权）。

判定（本工具最关心的两条事实，均有源码依据）：
- **`wlan0` 必须有 `inet` 行且不是 `127.0.0.1`**。VD 自己的界面就是这么判的：
  `PerfStatsHelper.OnWifiTimerTick`（`decompiled/.../PerfStatsHelper.cs:112-120`）读
  `WifiInfo.Frequency`，`frequency < 0` 就把整个 `WifiMetrics` 置为 `default`，
  即「未连接」；`ComputersTab.Update`（`decompiled/.../ComputersTab.cs:117-123`）在
  `!IsConnected` 时显示 `"Not connected to Wi-Fi"`（对应字符串在
  `profiles/vd-1.34.22/managed-strings.csv:66`，位于 `<GetComputersAsync>d__78::MoveNext` IL_028b）。
- **Wi-Fi 频段/信号/协商速率在 App 里是这么算的**（VDHelper 应复刻同一判据，否则两处结论会打架）：
  `PerfStatsHelper.cs:114-123` → `new WifiMetrics(CalculateSignalLevel(Rssi,4), null, Frequency,
   max(LinkSpeed, RxLinkSpeedMbps, TxLinkSpeedMbps), IpAddress)`；
  `WifiMetrics.cs:21-35` `FrequencyInGHz`：`>= 5925 → 6f`，`> 4000 → 5f`，否则 `2.4f`；
  `WifiMetrics.cs:38-41` **`IsSlow() = IsConnected && (Frequency < 4000 || LinkSpeed < 450)`**。
  → 频段 < 4000 MHz（2.4G）或协商速率 < 450 Mbps 就算「慢」，App 会把 WiFi 那一行染成警告色并加后缀
  `" - Streaming performance will be degraded"`（`ComputersTab.cs:107`）。
  这两个阈值就是 VDH「延迟高/掉帧」判定项的**来源阈值**，直接复用，不要另发明数字。

⚠️ **SSID 读不到（App 自己也没读到）**：`PerfStatsHelper.cs:123` 传给 `WifiMetrics` 构造器的第 2 个参数
（SSID）字面量是 `null`，`WifiMetrics.cs:9-17` 构造器把它原样赋值。所以 App 界面上 SSID 恒为空。
要在检测页显示 SSID/频段，只能走系统侧：
```bash
adb shell dumpsys wifi | grep -E "mWifiInfo|SSID|frequency|LinkSpeed|RSSI|Supplicant state"
```
形态 `[未验证]`；验证条件同上。注意 HorizonOS 上 `dumpsys wifi` 可能受限，届时降级为
`cmd wifi status`（**未在本机验证可用性**，`[未验证]`）。

### 3.4 路由

```bash
adb shell ip route show
adb shell ip -4 route show table all | grep -E "default|wlan0"
```
形态：`<dest>/<prefix> dev <if> [via <gw>] ...`。判定：存在 `default via <gw> dev wlan0`；
若只有 `unreachable default` 或没有 default → 头显出不了局域网，**PC 侧发现必然失败**。
`[未验证]`（验证条件：头显连接授权）。

### 3.5 DNS

```bash
adb shell getprop | grep -i dns
adb shell settings get global private_dns_mode
adb shell settings get global private_dns_specifier
```
`private_dns_mode` ∈ `off|opportunistic|hostname`；`opportunistic` 会尝试 DoT，失败回落但**可能拖慢首次解析**。
`[未验证]`（验证条件：头显连接授权）。离线基线下这一项只用于排除「DNS 卡住导致云查询 12s 超时」这一干扰项，
不是修复项。

### 3.6 HTTP 代理

```bash
adb shell settings get global http_proxy
```
`[未验证]` 期望形态。判定：非 `null` / 非 `:0` → **头显的出网流量（含 WCF 云查询）会走代理**，
离线 LAN 直连不受影响，但会让「云查询失败」的耗时与错误变得不可预测 → 检测页应给警告并给关闭方式：
```bash
adb shell settings put global http_proxy :0
```
⚠️ 上面这条是 `settings put`，属于**改头显设置**。本手册只做只读检测，`put` 仅作为「用户确认后的修复动作」列出，
VDHelper 不得默认执行。

### 3.7 系统版本与硬件

```bash
adb shell getprop ro.build.version.release      # Android 版本号
adb shell getprop ro.build.version.sdk          # API level
adb shell getprop ro.build.fingerprint
adb shell getprop ro.product.model              # 期望 "Quest 3" / "Quest 2" ...
adb shell getprop ro.hardware
```
`[未验证]` 形态（标准 `getprop` 单行 `key: value`）。

**有一条 App 自己读的属性值得注意**（VDHelper 可直接采信，不需重算）：
`decompiled/vdandroid/VirtualDesktop.Android/VrApp.cs:92-107` `DeterminePlatform()` 里，
Quest 分支取的是 **`ro.vros.build.version`**（HorizonOS 自己的版本），空则退回
`ro.build.branch` 并截最后一个 `v` 之后的数字。
→ 检测项「HorizonOS 版本」应读 `ro.vros.build.version`，读 `ro.build.version.release` 是读不到 VR 侧的。

`VrApp.cs:132-135` 还有硬门：`Build.Model` 以 `SM-` 开头（Samsung Gear VR）→ 直接
`throw new Exception("Unsupported Platform")`。

唤醒 / 佩戴状态（判定「进程为什么不在」）：
```bash
adb shell dumpsys power | grep -E "mWakefulness|mProximityPositive|virtual_proximity_state"
adb shell dumpsys activity activities | grep -E "VrActivity|mResumedActivity"
```
真机输出形态实例（`V77_..._REPORT:150-153`、`VD_VIEW_CAPTURE_NOTES_20260623.md:12-14`）：
```
pidof com.dwgx1.vd.recovered: no process
virtual_proximity_state: 3
mWakefulness: Asleep
mProximityPositive: false
```
→ **「Asleep + 进程不在」= 头显没戴，不是故障**。这条要写进检测页的首个提示，否则会误报。
`VrApp.OnPause/OnResume`（`VrApp.cs:168-181`）确实在暂停时 `Release()` WiFiLock、恢复时 `Acquire()`，
所以佩戴状态直接影响 WiFi 状态判定。

退出原因（判定「刚打开就退」）：
```bash
adb shell dumpsys activity exit-info VirtualDesktop.Android
```
真机输出形态实例（`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:213-220`）：
```
ApplicationExitInfo #0:
  ...
  pid=32139
  process=com.dwgx1.vd.recovered
  reason=1 (EXIT_SELF)
  status=1
  procState=2
```
`reason=1 EXIT_SELF` = **App 自己调 Kill/Exit**，不是崩溃。真机日志能对上
（`VD_V76_...:226-235`）：
```
06-23 08:19:04 ActivityTaskManager START ...VrActivity
06-23 08:19:05 VolumetricContentMonitor focused window ... state=ACTIVITY_STATE_VISIBLE
06-23 08:20:02 WindowManager WIN DEATH ...VrActivity
06-23 08:20:02 ActivityManager Process ... (pid 32139) has died: fg TOP
```
→ 用户报「连上立刻断开 / 打开就退」时，**先跑这一条**，比读 logcat 快一个数量级。

存储：
```bash
adb shell df -h /data
adb shell dumpsys package VirtualDesktop.Android | grep -E "codePath|dataDir|primaryCpuAbi|legacyNativeLibraryDir"
```
`[未验证]`。用途：APK 约 941–958 MiB（`01_architecture_packaging.md:22`、`03_patching_toolchain.md:214`），
`/data` 不足会导致 `adb install` 失败（形态：`adb install` 报 `INSTALL_FAILED_INSUFFICIENT_STORAGE`）。`[未验证]` 该错误串。

### 3.8 logcat

见 `02-logcat-triage.md`（独立文件）。此处只给入口形态：

```bash
adb -s <serial> logcat -c                                          # 清缓冲
adb -s <serial> logcat -d -t 5000                                  # 抓尾（真机实例：capture_vd_view.bat:43）
adb -s <serial> logcat -d -t 8000                                  # 抓更大尾（capture_vd_delayed_window.bat:55）
adb -s <serial> shell pidof <pkg>                                   # 拿 PID
adb -s <serial> logcat -d -t 80 --pid=<PID>                        # 旧 VDH 的做法（:468）
adb -s <serial> logcat -d -t 80 -s Unity:I AndroidRuntime:E ActivityManager:I   # 无 PID 时兜底（:470）
```
`-v threadtime` 是上一轮工程实际用的格式（`capture_diagnostic.bat:23`），带线程号，抓并发问题时更好用。

---

## 4. 无线 ADB

### 4.1 完整步骤（两条路径）

**路径 A — 老式 `tcpip`（兼容性最好，Quest 通用）**
```bash
# 1. 必须先 USB 连接并已授权
adb devices -l                       # 确认 <serial>\tdevice
# 2. 切到 TCP 模式（这一步之后 USB 可以拔）
adb -s <serial> tcpip 5555
# 3. 从头显里读 IP（Settings → Wi-Fi → 连接的 SSID → IP 地址）
adb -s <serial> shell ip -4 addr show wlan0
# 4. 连接
adb connect <headset-ip>:5555
```
- `adb tcpip 5555` 的期望输出：真机形态为 `restarting in TCP mode port: 5555`。`[未验证]`
  （验证条件：头显 USB 连接授权）。本机实跑无参数版本可确认参数校验：
  ```
  $ adb tcpip
  adb.exe: tcpip requires an argument
  ```
  即缺参数时 exit≠0 且打印这一行 —— VDH 可以据此确认 adb 可执行。
- `adb connect` 成功输出形态：`connected to <ip>:5555`；失败形态：
  `failed to connect to <ip>:5555`（超时）或 `cannot connect to <ip>:5555: Connection refused`。`[未验证]`
  （验证条件：头显已 `tcpip 5555` 且在同子网）。

**路径 B — 新式 `pair`（Android 11+，本机 adb 30.0.4 支持，`adb help` 原文见 §1.5）**
```bash
# 头显：Settings → 开发者选项 → 无线调试 → 使用配对码配对设备
# 屏幕上会显示 "IP 地址与端口" + "配对码"（形如 192.168.x.x:37000 与 6 位数字）
adb pair <ip>:<pairing-port>
# 成功后提示监听端口，再用它 connect
adb connect <ip>:<connect-port>
```
`adb pair` / `pairing-port` 形态 `[未验证]`；验证条件：头显 USB 首次授权 + HorizonOS 开发者选项里有「无线调试」。

### 4.2 常见失败原因

| 失败原因 | 判据 | 说明 / 证据 |
|---|---|---|
| **不是同一子网** | `adb shell ip -4 addr show wlan0` 的 `/prefix` 与 PC 不同段（如头显 `192.168.1.x/24`、PC `192.168.11.x/24`） | ADB over Wi-Fi 走 TCP 5555，**必须同一 IP 子网且路由可达**；`02_networking_streaming.md:179` 明确本工程只覆盖「同网段 LAN 直连」 |
| **头显 Wi-Fi 拿到的是访客网络 / AP 隔离** | 头显 SSID 与 PC SSID 不同但路由器相同；或路由器开了 AP/客户端隔离 | 隔离会让两台设备**互相不可见**。PC 侧排查归 NetDiagnosis |
| **PC 有多网卡（VPN/虚拟网卡）选错路由** | `route print <headset-ip>` 走了非物理网卡 | `tcpip` 后的 connect 走本机路由表；VPN 网段可能劫持 |
| **头显休眠** | `dumpsys power` → `mWakefulness=Asleep` | 睡着时无线调试 socket 可能掉；先 `input keyevent KEYCODE_WAKEUP` |
| **`adb tcpip 5555` 后又插回 USB/重启** | `adb devices -l` 出现 `offline` | 老式 tcpip 模式在设备重启后失效，需重新 USB + tcpip |
| **USB 线只有电源没有数据** | `adb devices -l` 列表为空但 Windows 设备管理器见到 MTP | 换数据线 |
| **防火墙挡了 adb server 的 5037** | `adb connect` 报 `cannot connect ... Connection refused` | adb server 默认只 listen localhost（`adb help`: `-L SOCKET listen on given socket for adb server [default=tcp:localhost:5037]`）；跨机访问才需要 `-a`，**不要为了单机检测开 `-a`** |

### 4.3 「adb over Wi-Fi 要求同一子网」的准确表述

上面的说法要分清两件事：
- **ADB 无线连接本身**：Android 官方要求 PC 与设备在同一网络、且设备 IP 可达；实践中同子网是可靠前提。
  跨子网需要路由放行 5555/TCP，安全上不推荐。
- **VD 自己的串流**：与 ADB 无关，走 **UDP 38850 广播发现 + TCP 38810/38820/38830/38840**
  （`PROJECT_HISTORY.md:138-145`）。这条链路**同样要求同网段**——`02_networking_streaming.md:177`
  明写远程/CGNAT/double-NAT 路径离线下必然失败，本研究只覆盖同网段 LAN 直连。

→ 检测页必须把「adb 能连上但 VD 搜不到电脑」判为**两类不同故障**：adb 通只证明控制面通，串流发现面另有一套端口与广播要求。

---

## 5. 权限与自启动

### 5.1 上一轮工程的 7 项 runtime 权限（逐项）

来源 `analysis/apk_patch/install.bat:25-31` 与 `analysis/apk_patch/HANDOFF.md:23-29`，两处逐字一致；
`analysis/report_sections/03_patching_toolchain.md:167-173` 也逐条复述。

| # | 权限全名 | 所属组 | `pm grant` 写法 | 不授的具体症状 |
|---:|---|---|---|---|
| 1 | `com.oculus.permission.USE_SCENE` | VR 场景（Scene） | 见下 | HorizonOS 不给 VR 焦点 → 进不了 VR 环境 |
| 2 | `horizonos.permission.USE_SCENE` | VR 场景（Scene），HorizonOS 新命名空间 | 见下 | 同上；老 Quest OS 只认 `com.oculus.*`，新系统只认 `horizonos.*`，**两条都要授** |
| 3 | `com.oculus.permission.FACE_TRACKING` | 面部追踪 | 见下 | 无面部追踪；foveated streaming 不可用 |
| 4 | `horizonos.permission.FACE_TRACKING` | 面部追踪（HorizonOS 命名空间） | 见下 | 同上 |
| 5 | `com.oculus.permission.EYE_TRACKING` | 眼动追踪 | 见下 | 无眼动 → 无 eye-tracked foveation |
| 6 | `horizonos.permission.EYE_TRACKING` | 眼动追踪（HorizonOS 命名空间） | 见下 | 同上 |
| 7 | `android.permission.POST_NOTIFICATIONS` | 通知 | 见下 | 无通知；用户看不到连接/断连提示，会误以为「没反应」 |

**所属组的准确出处**：`com.oculus.permission.*` 与 `horizonos.permission.*` 是 Meta 自定义的
VR 权限，其 permission-group 名未在本工作区任何文件中出现 → **组名 `[未验证]`**；
`android.permission.POST_NOTIFICATIONS` 属于 AOSP 标准组
`android.permission-group.POST_NOTIFICATIONS`（AOSP 事实，非本工作区证据）。
验证条件：头显上跑 `adb shell pm list permissions -g | grep -i -E "SCENE|TRACKING"`，
或 `adb shell dumpsys package permissions | grep -i scene`。

**为什么这 7 项是硬要求**（两条独立证据）：
- `analysis/apk_patch/HANDOFF.md:32`：「**重要**：每次安装后必须授权权限，否则 30 秒后 VR 焦点被回收。」
- `analysis/report_sections/03_patching_toolchain.md:175` + `HANDOFF.md:171`（教训 #7）：
  「7 个运行时权限必须预授 — 否则 HorizonOS 30 秒后收回 VR 焦点」。

注意：**Android 每次覆盖安装 / 重装都会重置 runtime 授权**，所以「每次安装后必须授权」是硬规则，
不是首次安装的一次性动作。

### 5.2 `pm grant` 的授法与判读

```bash
adb -s <serial> shell pm grant VirtualDesktop.Android com.oculus.permission.USE_SCENE
adb -s <serial> shell pm grant VirtualDesktop.Android horizonos.permission.USE_SCENE
adb -s <serial> shell pm grant VirtualDesktop.Android com.oculus.permission.FACE_TRACKING
adb -s <serial> shell pm grant VirtualDesktop.Android horizonos.permission.FACE_TRACKING
adb -s <serial> shell pm grant VirtualDesktop.Android com.oculus.permission.EYE_TRACKING
adb -s <serial> shell pm grant VirtualDesktop.Android horizonos.permission.EYE_TRACKING
adb -s <serial> shell pm grant VirtualDesktop.Android android.permission.POST_NOTIFICATIONS
```

`pm grant` 的返回判读，旧 VDH `HeadsetGrant()`（`VDH.Extra.cs:485-510`）已经写好了三种判据，可直接抄：

| `pm grant` 的 stderr/stdout 形态 | 判定 | 旧实现动作 | 行号 |
|---|---|---|---|
| 空输出 | 成功 | 打印 `OK <shortname>` | `:506-507` |
| 含 `not a changeable` | 该权限不是 runtime 类型（如 INTERNET/WIFI），跳过即可 | `skip <shortname>` | `:501-503` |
| 含 `has not requested` | APK 没声明这个权限（版本/平台不匹配） | `skip <shortname>` | `:501-503` |
| 含 `Exception` | 授权失败 | `fail <shortname>` | `:504-505` |

旧实现还打印一句总说明（`:495-496`）：
「只授运行时权限。Internet/Wi-Fi 等安装时权限不用 grant。」—— 这句要保留在 UI 上，
否则用户会以为漏授了 `INTERNET`。

复验命令（授完必须复验，不能只看 `pm grant` 的空输出）：
```bash
adb -s <serial> shell dumpsys package VirtualDesktop.Android | grep -E "granted="
```
期望：7 项全是 `granted=true`。`[未验证]` 该输出形态；验证条件：头显连接授权 + 已执行上述 7 条。

### 5.3 第 8–10 项（非 7 项内，但模板脚本会授）

`analysis/apk_patch/install_template.bat:59-75` 是更新版模板，授 **10 项**，在 7 项之外多授 3 项：

| 权限 | 为什么加 | 缺失症状 |
|---|---|---|
| `android.permission.RECORD_AUDIO` | 串流语音（麦克风透传） | 首启弹窗**阻塞 VR 窗口放置**（`HANDOFF.md:54-55`）；App 会自动把 `MicPassthrough` 关掉（`VrApp.cs:277-286`） |
| `android.permission.READ_EXTERNAL_STORAGE` | 截图/外部存储 | 旧 SDK 兼容项，Android 13+ 基本无效 |
| `android.permission.READ_MEDIA_IMAGES` | 截图（Android 13+ 新媒体权限） | 截图功能不可用 |

`install_template.bat:60` 的注释把 10 项的整体理由写清了：
「HorizonOS 会在 ~30s 后收回 VR 焦点,若以下权限未授予。」

VDHelper 的建议策略：**检测页展示 7 项（硬门）+ 3 项（可选项）两组，分开展示**，
避免用户以为 10 项缺一不可。`RECORD_AUDIO` 应单独提示，因为它对应的是「首启卡住」而非「30 秒失焦」。

### 5.4 自启动 / 保活

App 自己已经做了一件事，检测页要知道以免误判：
`VrApp.OnCreate` 里 `CreateWifiLock(4, "VRD")`（`VrApp.cs:80`，mode 4 = `WIFI_MODE_FULL_HIGH_PERF`），
`OnResume` 时 `Acquire()`、`OnPause` 时 `Release()`（`:172` / `:179`）。
→ Wi-Fi 锁 tag 就是 **`"VRD"`**，这是一个可查的、可用于确认「App 是否真的在前台持锁」的点：
```bash
adb shell dumpsys wifi | grep -i VRD
```
形态 `[未验证]`；验证条件：App 在头显里前台运行。

App 侧还有一个 5 分钟 inactivity 定时器：`Game.cs:80`
`new Timer(new TimerCallback(this.OnInactivityTimer), null, 300000, -1)`。
这不是「30 秒失焦」，别混淆——**30 秒**是 HorizonOS 因为缺权限收焦点，**300000ms**是 App 自己的闲置计时器。

---

## 6. PC 侧配对前提（供检测页交叉检查）

检测页若同时读到 PC 侧信息，可以交叉验证：

| 项 | 值 / 命令 | 证据 |
|---|---|---|
| 已装 Streamer 路径 | `C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe` | `VDH.cs:114` |
| Streamer 版本 | 实测 `FileVersion = 1.34.22.0`（`Get-Item ... .VersionInfo`，2026-10-05 本机） | 与 `VDH.cs:549-557` 的 `DetectStreamerVer()` 一致 |
| Streamer 设置 | `C:\ProgramData\Virtual Desktop\StreamerSettings.json` | `VDH.cs:115`；本机实读，见下 |
| 配对是**账号字符串匹配**，不是签名校验 | Streamer 把发现包里的 `AccountID` 字符串与自己 `Accounts` 里最多 5 个字符串比对 | `PROJECT_HISTORY.md:147-152`（实测 `account=recovered-local` → 1 个 Computer；`account=dwgx1337` → 0 个） |
| 发现的 UDP 端口 | **UDP 38850** 广播（`255.255.255.255`） | `PROJECT_HISTORY.md:138`；`docs/about.html:719-720` |
| 流通道 | TCP 38810 / 38820 / 38830 / 38840 | `02_networking_streaming.md:43-46`；本机运行时快照 `02_networking_streaming.md:53-58` |
| 云注册超时 | 在线 12s（`ComputerRegistryTimeout`），离线 fallback 3s（`ComputerRegistryOfflineTimeout`） | `decompiled/.../NetworkManager.cs:397,400` |
| 断线后重试延迟 | 不可达 6s / 需更新 50s / 普通 3s | `NetworkManager.cs:311,316,319`（常量 `:394` = 3s） |
| 本机 `StreamerSettings.json` 关键项 | `Accounts.OculusQuest` 1 条 + `Accounts.Oculus` 3 条；`ShowPairingRequests:false`；`PreferredCodec:11` / `CodecName:"AV1 10-bit"`；`MonitorCount:1`；`DeviceName:"Meta Quest 3"` | 本机 2026-10-05 实读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json:1-27` |

**`Accounts` 里存的是加密 blob（`AQAAANCMnd8B…`），不是明文账号名** ——
所以检测页**不能**从 `StreamerSettings.json` 直接读出「配对账号是什么」，只能报「有几个账号条目」。
旧 VDH 也正是因为这样才在 UI 上写「账户改成可读：平台 + 数量，不再摊开密文」（`VDH.cs:539`）。

---

## 7. 无线 ADB 的安全边界（必须原文进 UI）

> ### ⚠️ 无线调试是明文通道
> `adb tcpip` / `adb connect` / `adb pair` 建立的 ADB 通道**不加密**。任何能在你 Wi-Fi 上抓到包的人
> （同网段、恶意热点、ARP 欺骗、被入侵的 IoT 设备）都能看到并**完全控制**你的头显：装/卸应用、
> 读全部文件、截屏、录屏、开麦、读账号 token。这**不是「串流能不能连」的问题，是头显本身失守**。
>
> - 家庭/办公 Wi-Fi 上开启无线调试，等于把整个头显的 root 式控制面暴露给同网段。
>   HorizonOS 上 ADB 拿到的是 **shell（uid 2000）** 权限，足以 `pm grant` 提权、装任意 APK。
> - Quest 上这尤其敏感：能装 APK = 能装绕过平台校验的东西。
>
> **默认应该：关掉无线调试，用 USB。**
>
> **关闭方式**（用户在头显里操作，VDHelper 只提供指引与读状态，不代劳）：
> 1. 头显：`设置 → 开发者选项 → 无线调试` → **关闭**（`adb tcpip` 模式同时需要关闭 USB 调试里的
>    「默认 USB 调试」或直接拔线，系统会在拔线后回收 TCP 监听）。
> 2. 头显：`设置 → 开发者选项 → 撤销 USB 调试授权`（撤销所有已记住的 PC RSA 密钥）。
> 3. PC：不再需要时 `adb kill-server`（停止本机 adb server）。
>
> **必须同时说明的另一半**：关闭无线调试**不会**关掉 ADB 本身，也不影响 VD 串流 ——
> VD 串流用的是自己的 38810–38840 通道，与 5555 无关。关掉它你只是失去「用 PC 远程修头显」的能力，
> 不失去串流。
>
> VDH 侧纪律：**默认只走 USB**；无线仅在用户显式勾选并读过上面这段警告后可用；
> 检测页读到设备处于 `192.168.x.x:5555` 形式（而非 USB 序列号）时，页面顶部常驻这条警告。

---

## 8. `[未验证]` 清单（需要头显才能确证）

| 项 | 需要什么 |
|---|---|
| `adb devices -l` 有设备时的完整行形态 | 头显 USB 连接 PC + 在头显里点「允许 USB 调试」 |
| `adb tcpip 5555` 的成功输出 `restarting in TCP mode port: 5555` | 同上 |
| `adb connect <ip>:5555` 成功/失败的输出形态 | 同上 + 头显已切 tcpip |
| `adb pair` 的 pair-port 形态 | 同上 + HorizonOS「无线调试 → 使用配对码」 |
| `pm list packages \| grep` 的命中行 | 头显上装着补丁 APK |
| `dumpsys package` 中 `runtime permissions:` 的 `granted=true` 形态 | 头显连接授权 + 已跑 7 条 `pm grant` |
| `ps -A` 的完整行 | 头显上 App 正在运行 |
| `ip -4 addr show` / `ip route show` / `getprop \| grep dns` / `private_dns_mode` 的真实输出 | 头显连接授权 |
| `settings get global http_proxy` 的输出形态 | 头显连接授权 |
| `dumpsys wifi` 里 SSID/频段/LinkSpeed/RSSI 的可读性与字段名 | 头显连接授权（HorizonOS 可能限制） |
| `cmd wifi status` 是否可用 | 同上 |
| `dumpsys power` / `dumpsys activity activities` 在真机上的完整输出 | 头显连接授权（形态已有真机记录，但本轮未复跑） |
| `pm list permissions -g` 里 SCENE/TRACKING 的 permission-group 真名 | 头显连接授权 |
| `dumpsys wifi \| grep VRD` 能否看到 WiFiLock | 头显上 App 前台运行 |
| `df -h /data` 与 `INSTALL_FAILED_INSUFFICIENT_STORAGE` 串 | 头显连接授权 + 故意触发空间不足 |