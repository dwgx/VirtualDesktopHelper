# 03 — 症状判障决策表（症状 → 检查命令 → 期望 → 判读 → 修复 → 复验）

> 表头：`症状(用户原话)` | `检查命令` | `期望` | `不符说明什么` | `修复动作` | `复验命令`
>
> **前提约定**
> - `adb` = 检测页找到的 adb 路径（见 `01-adb-playbook.md` §1.3）；本文用 `<S>` 占位**头显串号**、
>   `<PKG>` 占位**包名**（成品 = `VirtualDesktop.Android`，实验包 = `com.dwgx1.vd.recovered`，
>   两者都要能自动识别，见 `01-adb-playbook.md` §3）。
> - 所有 `adb` 命令**默认只读**。标 🔧 的行是**修复动作**，VDHelper 必须让用户逐条确认后才执行；
>   标 🔴 的行会改头显设置或杀进程，默认折叠。
> - **本机 2026-10-05 未连接头显**：所有需要真机输出的「期望」列写的是**形态**而非本机实测输出，
>   形态来源逐条标在证据列。凡形态本身也无真机记录的，写 `[未验证]` 并在 §末尾汇总。
> - **分工提醒**：串流的**成功/失败最终判定在 PC 侧**（连接表 / 防火墙），头显侧只能给条件。
>   理由：`02-logcat-triage.md` §0-② 已确证 VD 的网络层不打 logcat。头显侧能做的只是排除本侧原因。
> - 覆盖的九个必答症状落在哪一节：头显搜不到电脑 / 电脑搜不到头显 → **A**；连上立刻断开 / 打开就退 → **B**；
>   30 秒自动失焦（关联 7 权限）→ **B + C**；连上黑屏 → **D**；只有声音没画面 → **D**；
>   手柄不工作 → **D2**；延迟高 / 掉帧 → **D**；码率被限制在 500 → **D**；串流是否真的成功 → **E**。

---

## A. 头显搜不到电脑 / 电脑搜不到头显

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 头显里「电脑」列表是空的 / 显示 "No computer found" | `adb -s <S> shell ip -4 addr show wlan0` | 有 `inet <a.b.c.d>/<prefix>`，且与 PC 的 IPv4 同一子网 | **头显没连 Wi-Fi**，或连了但 PC 不在同一子网 → UDP 38850 广播到不了 PC | 头显里连 Wi-Fi；PC 与头显接同一路由器（`02_networking_streaming.md:177`：本研究只覆盖同网段 LAN 直连） | `adb -s <S> shell ip -4 addr show wlan0` 与 PC 的 IPv4 前三段相同 |
| 同上 | `adb -s <S> shell settings get global http_proxy` | `null` 或 `:0` | 设了代理 → 云查询与部分出网被劫持，离线发现时序变得不可预测 | 🔧 `adb -s <S> shell settings put global http_proxy :0` | `adb -s <S> shell settings get global http_proxy` → `null` |
| 同上 | `adb -s <S> shell ip route show` | 有 `default via <gw> dev wlan0` | 无 default 或 `unreachable default` → 头显出不了局域网 | 头显里重连 Wi-Fi；DHCP 异常时头显重启 | `adb -s <S> shell ip route show \| grep default` |
| 同上（头显在 PC 的电脑列表里也看不到） | PC 侧：`Get-Process VirtualDesktop.Streamer` （复本机实测：`C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe` FileVersion = 1.34.22.0） | 进程存在 | Streamer 没开 → 没人回应 UDP 38850 的 Discover | 先开 Streamer 再开头显 App（`02_networking_streaming.md:179` 记「先开 Streamer 再开 Quest VD」是经验性约束） | 进程存在 + 头显列表刷新后出现条目 |
| 同上 | PC 侧读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json`，数 `Accounts.OculusQuest` + `Accounts.Oculus` 数组长度 | 本机实测：`OculusQuest` 1 条 + `Oculus` 3 条 | **0 条** → 没人能通过账号字符串匹配（`PROJECT_HISTORY.md:147-152`：准入门就是字符串比对，实测不匹配的账号返回 0 个 Computer） | 在 Streamer UI 里 Accounts → Add 添加账号（上限 5 个 owner 控制的字符串） | 重读 JSON，数组非空 |
| 同上 | 头显 UI 上找这两行字样：`Virtual Desktop servers unreachable, only showing local computers`（`managed-strings.csv:91`）或 `Meta servers unreachable, only showing local computers`（`:68`） | 离线基线下**出现是正常的**（云不通 → 已 fallback 到本地发现） | 若**没出现**且列表仍空 → 可能 App 根本没走到 fallback（补丁未生效） | 查 logcat：`adb -s <S> logcat -d -t 200 \| grep -E "VRD\|MonoDroid"`（见 `02` §3） | 列表出现条目 |
| 同上 | PC 侧：Streamer 进程的连接表，看有没有 `:38850` 的 UDP 或 38810 的 TCP | 头显点电脑时有 38810 尝试 | 完全没有 → 头显的 Discover 广播没到 PC（**同子网/防火墙/AP 隔离问题**） | 🔧 检查 Windows 防火墙是否挡 UDP 38850 / TCP 38810-38840（归 NetDiagnosis）；确认路由器没开 AP 隔离 | 重试后连接表出现 38810 条目 |
| 同上（提示「更新 Quest OS 以使用 USB」，`managed-strings.csv:5`） | `adb -s <S> shell getprop ro.vros.build.version` | 非空 | 为空 → Quest OS 太老，App 提示升级 | 头显系统里升级 HorizonOS | `adb -s <S> shell getprop ro.vros.build.version` 非空 |
| 同上（提示 "Failed entitlement check"，`managed-strings.csv:98`） | `adb -s <S> logcat -d -t 200 \| grep "AppManagerInternal: Entitlement"` | 有 `not found` 行 —— **离线基线下这是预期**（`02-logcat-triage.md` §3.5 已实测） | 若同时伴随 `E VRD` 崩溃行 → 不是鉴权问题，是崩溃 | 无需修（补丁已 NOP 自毁，App 继续 fallback）。若确有崩溃按 `02` §3 走 | `adb -s <S> shell pidof <PKG>` 有 PID |
| 电脑端能看到头显但点「断开」后再也回不来 | `adb -s <S> shell pidof <PKG>` + `adb -s <S> shell dumpsys power \| grep mWakefulness` | `dumpsys power` 显示 `mWakefulness=Awake` 时进程应在 | `Asleep` + 无进程 = **头显没戴**，不是故障（`install_template.bat:82-85` 明写：睡着/没戴时不报崩溃） | 戴上头显唤醒 | 复跑两条，进程出现 |
| 同上 | `adb -s <S> shell dumpsys activity exit-info <PKG>` | 最近一次退出 `reason=` 不是 `1 (EXIT_SELF)` | `reason=1 (EXIT_SELF) status=1` = **App 自杀**（`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:218-219` 真机记录），不是崩溃 → 鉴权自毁路径未 NOP 掉 | 重装带补丁的 APK；重授 7 项权限 | 同命令，`reason` 变化且 App 能留住 |

---

## B. 连上立刻断开 / 打开就退

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 点一下电脑就掉回头显主页，提示 "Failed to connect, please try again."（`managed-strings.csv:143`，源码 `NetworkManager.cs:297`） | PC 侧：`Get-NetTCPConnection -OwningProcess <StreamerPid>` | 有到头显 IP 的 38810 `Established`，或至少有 `SynSent` | **完全无连接** → 38810 被防火墙挡或路由不通 | 🔧 放行 TCP 38810-38840 + UDP 38850/38860（归 NetDiagnosis） | 重连后出现 `Established` |
| 同上 | 头显 UI：电脑列表项上是否有 "Streamer uses a newer version, update this app before connecting"（`managed-strings.csv:60`） | **不应出现** | 出现 → 版本门拦下（头显 App 比 Streamer 旧） | 升级头显 App，或把 Streamer 降到匹配版本 | 该行消失 |
| 同上 | `adb -s <S> shell dumpsys package <PKG> \| grep -E "versionName\|versionCode"` | 与本机 Streamer `1.34.22.0` 同代（App 侧 `1.34.18.0`/`1.34.22.0`） | 明显更旧（如 1.22.x） | 同上 | 复跑 |
| 打开 App 不到 30 秒就被踢回主页，VR 画面没了 | `adb -s <S> shell dumpsys package <PKG> \| grep -A40 "runtime permissions"` | 7 项全 `granted=true` | 任一 `granted=false` → **HorizonOS 在 ~30s 收回 VR 焦点**（`HANDOFF.md:32`、`:171` 教训 #7；`install_template.bat:60` 注释「~30s 后收回 VR 焦点」） | 🔧 见 §C 的 7 条 `pm grant` | 同命令，7 项全 true |
| 同上（且是「没画面」而非「掉回主页」） | `adb -s <S> logcat -d -t 400 \| grep "MemoryBroker"` | 无 `does not have access` 行 | 有 `client <pkg> ... does not have access (app ops or VR focus)` → **VR 焦点被收回**，不是网络问题（真机记录 `VD_V76_...:205-206`） | 🔧 先补 7 项权限；再看是否被 Guardian 弹窗挡住 | 同命令无该行 |
| 同上 | `adb -s <S> logcat -d -t 400 \| grep -E "GrantPermissionsActivity\|Launch is blocked"` | 无 `Launch is blocked because: a ... OS dialog is currently showing.` | 有 → 系统弹窗（Guardian / 权限申请）盖住了 App，真机实例 `logcat_exit6.txt:56` | 在头显里把那个弹窗关掉；权限弹窗用 `pm grant` 预先授掉就不弹 | 同命令无该行 |
| 同上 | `adb -s <S> shell input keyevent KEYCODE_WAKEUP` 然后 `adb -s <S> shell pidof <PKG>` | 有 PID | 无 PID → App 没起来，看 logcat 的 `FATAL EXCEPTION` / `E VRD`（`02` §3） | 按 `02-logcat-triage.md` §7 抓日志定位 | 复跑 `pidof` |
| App 刚启动就崩 | `adb -s <S> logcat -d -t 400 \| grep -E "FATAL EXCEPTION\|MonoDroid: UNHANDLED"` | 无输出 | 有 `AndroidRuntime: FATAL EXCEPTION: main` → Java 侧崩溃；`android.runtime.JavaProxyThrowable: [System.XxxException]` 表明是**托管异常**，看 `at VirtualDesktop.VrApp.<Method>` 定位生命周期 | 按栈里指出的方法定位（本轮已确证样例：`OnResume` 崩溃见 `logcat_exit3.txt:1488`、`OnCreate` 崩溃见 `logcat_exit4.txt:196`） | 修复后重跑，无 FATAL |
| 同上 | `adb -s <S> logcat -d -t 400 \| grep -E "\[VDIWEF] VRD"` | 无输出 | 有 → 全 App 唯一的托管日志点（源码 `VrApp.cs:304`）。按 `--- End of managed ... stack trace ---` 截断取托管栈 | 同上 | 同命令无输出 |

---

## C. 权限与 7 项 runtime 权限

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 「授权」按钮点了没用 / 7 项权限状态不明 | `adb -s <S> shell pm grant <PKG> com.oculus.permission.USE_SCENE`（其余 6 条见下） | 空输出 = 成功 | 返回含 `not a changeable` → 不是 runtime 权限，跳过；含 `has not requested` → APK 没声明；含 `Exception` → 失败。判据抄旧 VDH `VDH.Extra.cs:501-505` | 🔧 逐条执行 7 条（见下方代码块） | `adb -s <S> shell dumpsys package <PKG> \| grep granted=` |
| 30 秒自动失焦（用户可能描述成「刚进去就黑」「过一会儿断」） | `adb -s <S> shell dumpsys package <PKG> \| grep -A40 "runtime permissions"` | 7 项 `granted=true` | 缺任一 → HorizonOS 收回 VR 焦点。**这是「30 秒」现象的头号原因** | 🔧 7 条 `pm grant` 全跑一遍 | 同命令 |
| 首启卡在权限弹窗出不来（用户描述「打开就卡住」「没反应」） | `adb -s <S> shell pm grant <PKG> android.permission.RECORD_AUDIO` | 空输出 | 未授 → `RECORD_AUDIO` 弹窗**阻塞 VR 窗口放置**（`HANDOFF.md:54-55`） | 🔧 授 `RECORD_AUDIO`（`install_template.bat:69` 在 7 项之外加的第 8 项） | `adb -s <S> logcat -d -t 200 \| grep GrantPermissionsActivity` 无行 |
| 同上（截图功能不可用） | `adb -s <S> shell pm grant <PKG> android.permission.READ_MEDIA_IMAGES` | 空输出 | 未授 → Android 13+ 截图无权限（`install_template.bat:71` 第 10 项） | 🔧 授该权限 | 同上 |
| 麦克风没声音 / 提示静音 | `adb -s <S> shell dumpsys audio \| grep -i mic`（形态 `[未验证]`） | 麦克风未被静音 | UI 会显示 "Muted in headset settings"（`managed-strings.csv:20`） | 头显系统设置里取消静音 | 同命令 |
| 授完权限还是不生效 | `adb -s <S> shell am force-stop <PKG>` 🔴 然后重启 App | 重启后生效 | Android 的 runtime 授权变更可能需要进程重启才被完全采纳 | 🔴 强停并重启（**会杀掉正在跑的 App**，需用户确认） | `adb -s <S> shell pidof <PKG>` 有新 PID |

<details>
<summary>7 条 `pm grant` 全文（逐字抄 <code>analysis/apk_patch/install.bat:25-31</code>）</summary>

```bash
adb -s <S> shell pm grant <PKG> com.oculus.permission.USE_SCENE
adb -s <S> shell pm grant <PKG> horizonos.permission.USE_SCENE
adb -s <S> shell pm grant <PKG> com.oculus.permission.FACE_TRACKING
adb -s <S> shell pm grant <PKG> horizonos.permission.FACE_TRACKING
adb -s <S> shell pm grant <PKG> com.oculus.permission.EYE_TRACKING
adb -s <S> shell pm grant <PKG> horizonos.permission.EYE_TRACKING
adb -s <S> shell pm grant <PKG> android.permission.POST_NOTIFICATIONS
```
`com.oculus.*` 与 `horizonos.*` **成对出现**：老 Quest OS 只认 `com.oculus.*`，新系统只认 `horizonos.*`，
两条都要授（`install.bat` 与 `install_template.bat:61-68` 都是成对列出）。
</details>

---

## D. 黑屏 / 只有声音 / 延迟 / 掉帧 / 码率

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 连上了但一片黑，没画面 | `adb -s <S> logcat -d -t 400 \| grep "MemoryBroker"` | 无 `does not have access` | 有 → VR 焦点被收回（黑屏头号原因） | 先补 7 项权限；关 Guardian 弹窗 | 同命令无行 |
| 同上 | `adb -s <S> logcat -d -t 400 \| grep "xrWaitFrame"` | 无 `returning early due to activity pause` | 有 → 没进 VR 渲染循环（真机记录 `V85_..._REPORT:192`） | 同上（焦点问题） | 同命令无行 |
| 同上 | `adb -s <S> shell dumpsys power \| grep -E "mWakefulness\|mProximityPositive"` | `Awake` + `mProximityPositive=true`（戴着） | `Asleep` / `false` → 头显睡着或没戴，App 不渲染（App 在 `OnPause` 里会 `Release()` WiFiLock，源码 `VrApp.cs:172`） | 戴上头显 | 同命令 |
| 同上 | PC 侧 `Get-NetTCPConnection -OwningProcess <StreamerPid> \| Where-Object RemotePort -eq 38830` | 有 `Established`（视频通道） | 无 38830 → 视频通道没建起来，问题在握手不是渲染 | 🔧 放行 TCP 38830 | 重连后有 `Established` |
| 「有声音没画面」 | PC 侧同上，但查 `RemotePort -eq 38840`（音频）与 `38830`（视频）两条 | 38840 有、38830 也应有 | **38840 有而 38830 无** → 只建了音频通道 → 视频流被防火墙或驱动挡 | 🔧 检查防火墙 / GPU 编码器 | 两条都在 |
| 「有画面没声音」 | PC 侧查 `38840` | 有 `Established` | 无 → 音频通道没建；或 `settings get global http_proxy` 非空干扰 | 先放行 38840 | 同上 |
| 延迟高 / 掉帧（用户描述「卡」「拖影」「晕」） | 头显 UI 上 WiFi 那一行读 `GHz \| Mbps` 两数；或 `adb -s <S> shell dumpsys wifi`（形态 `[未验证]`） | **频段 ≥ 4000 MHz 且协商速率 ≥ 450 Mbps** | 低于任一阈值即 App 自己判为「慢」：`WifiMetrics.IsSlow() = Frequency < 4000 \|\| LinkSpeed < 450`（源码 `WifiMetrics.cs:38-41`）；界面会显示 "... - Streaming performance will be degraded"（`ComputersTab.cs:107`，字面量 `managed-strings.csv:7`） | 切 5GHz/6GHz 信道；拉近路由器 | 复读两数，均达标 |
| 同上（PC 侧） | 头显 UI 上电脑条目的警告行 | 无 "Computer not wired to router with Ethernet cable, performance will suffer"（`managed-strings.csv:10`）/ "Computer wired but not with Gigabit Ethernet..."（`:11`） | 出现即 PC 是无线或百兆 | PC 接网线 + 千兆口 | 该行消失 |
| 同上（USB 链路限速） | 头显 UI 上是否显示 "USB 2 \| {N} Mbps - Bitrate will be limited"（`managed-strings.csv:9`）或 "Limited to USB 2 speeds. Try a better USB cable or different port on your computer"（`:8`） | **不应显示** | 显示 → Streamer 到头显的 USB 链路是 USB 2 速率 | 换 USB 3 数据线 / 换 PC 上的口 | 该行消失 |
| 码率被限制在 500（用户描述「画质上不去」「码率选项到 500 就没了」） | 头显 App 的 Streaming 页码率滑条；源码侧上限是 `HmdResolutionTypeExtensions.GetMaxVRBitrate(HmdType, ActiveCodec)`（`StreamingTab.cs:991`） | 原版上限 **500 Mbps** | 这是 **APK 里的 IL 立即数**，不是设置项 —— 旧 VDH 明确记载「Streamer JSON 没有 MaxBitrate；滑条上限是 Quest APK 里 Mobile.dll 三处 IL 立即数，原版 500」（`VDH.Extra.cs:184`），三个偏移按版本不同：1.34.19.0 = `{0x1396C,0x14CA5,0x2BC97}`、1.34.22.0 = `{0x13B0C,0x14E45,0x2BFE3}`（`VDH.cs:33-34`） | **改 Streamer 无效**（旧 VDH 结论：「重启串流端无效」）。要改必须重打包 APK 改 IL 后重装 | `adb -s <S> shell dumpsys package <PKG> \| grep versionName` 确认装的是哪个构建 |
| 同上（想确认当前 APK 是不是我们登记过的） | `adb -s <S> shell dumpsys package <PKG> \| grep -E "versionName\|signatures"` | 与登记表匹配 | SHA 登记在旧 VDH `VDH.cs:60-63`：英文冻结包 SHA256 `B0604A84…`（标注「IL 500 Mbps」）、`8DEEF4FF…`（标注「960-cap, superseded」） | 装已登记的包，别装来路不明的 | 同上 |
| 体感延迟高但网速够 | `adb -s <S> shell dumpsys package <PKG> \| grep -E "versionName"` + PC 侧 Streamer 编码设置 | Streamer `AutoAdjustBitrate` 关闭时需手动定档；本机实测 `StreamerSettings.json` 当前为 `"AutoAdjustBitrate": false`、`"PreferredCodec": 11` / `"CodecName": "AV1 10-bit"` | AV1 10-bit 需要 GPU 硬编；老引擎可能走软编 → 延迟高。旧 VDH 记载 H.264+ / HEVC 10-bit 各自的风险（`VDH.Extra.cs:136-148`） | 把编码降到 H.264（`PreferredCodec: 0`）试 | 主观复测延迟 |

---

## D2. 手柄 / 控制器不工作

⚠️ 本节的关键事实：**头显侧没有一条 adb 命令能直接判断「手柄有没有生效」**。
`InputSystem.Update` 是每帧跑的逻辑（`InputSystem.cs:46-78`），**全程无 Log、无状态文件**。
所以判据分两层：先查「哪些设置会让 Update 直接 return」（源码确证），再查「控制器有没有被系统识别」（系统侧）。

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 手柄在 VR 里不出现 / 游戏里没输入 | `adb -s <S> shell dumpsys input \| grep -iE "controller\|oculus_touch"`（形态 `[未验证]`） | 系统能枚举到两个 Touch 控制器 | 系统层就没认到控制器 → 与 VD 无关，是头显硬件/配对问题 | 头显里检查控制器电量与配对状态 | 复跑枚举 |
| 手柄能亮灯但游戏里没反应 | 头显 App 的 Input 页，看 **"Use motion controllers as gamepad"**（`managed-strings.csv:15`，`InputTab::.ctor` IL_0317）是否开启 | 已开启 | 未开启 → 手柄不会被当游戏手柄 | 在 Input 页开启该选项 | 复看该开关 |
| 同上（提示 "Your controllers won't appear in your VR games.\nAre you sure you want to do this?"，`managed-strings.csv:23`） | 该提示对应的是**关闭**「追踪控制器」后的确认框 | 提示只在用户主动关闭时出现 | 出现说明用户刚把它关了 | 重新开启对应开关 | 复看 |
| 菜单键按了没反应 | 头显 App 设置 + 源码条件 | 菜单键链路是 `Game.OnHmdBackPressed`（`Game.cs:1770`），但有 **Quest 3 gamepad-emulation guard**：`HmdType >= 259 && IsControllerEmulatingGamepad` 时直接 `return`（`05_input_aot_native.md:28`、`Game.cs:1772`） | 若正在用「手柄模拟 Xbox 手柄」跑游戏，菜单键**按设计**被吞掉 —— 这是保护不是 bug | 退出该游戏/关掉手柄模拟后再按菜单键 | 同上 |
| 在 Input 系统里按钮完全无反应（切显示器 / 呼出键盘都没反应） | `adb -s <S> shell dumpsys power \| grep mWakefulness` + `adb -s <S> shell pidof <PKG>` | 头显 `Awake` 且 App 有 PID | `Asleep` 或无进程 → 见 B 表 | 先唤醒 + 启动 App | 复跑 |
| 同上（App 活着但 `InputSystem.Update` 被整帧跳过） | 用设备型号交叉判断（`adb -s <S> shell getprop ro.product.model`，形态 `[未验证]`） | 型号为 `Quest 3` 一类，`HmdType >= 259` | `InputSystem.Update:49` 的守卫里有 `hmd.Type < 259` 一条 → **非 Quest 3 级别设备整个按钮处理整帧跳过**（`05_input_aot_native.md:16`） | 该守卫**不可绕过**（`05_input_aot_native.md:30` 标为 identity gate，禁补）→ 只能换头显或接受该功能不可用 | 无（设计限制） |
| 同上（正在串流中，Y/X 键无反应） | 源码条件 | `StreamingSource != null`（已在串流中）时 `Update` 直接 return（`InputSystem.cs:49`） | 已连上串流时菜单键被设计为不生效 | 断开串流后再试 | 同上 |
| 同上（ScreenAdjustment 设在 ThumbstickMotion） | 头显 App 设置 | `ScreenAdjustment != ThumbstickMotion` | 等于 `ThumbstickMotion` → 同样整帧 return（`InputSystem.cs:49`） | 改这个设置 | 复看 |
| 手柄输入延迟/漂移 | `adb -s <S> shell dumpsys input \| grep -i drift`（形态 `[未验证]`） | 无异常漂移值 | 追踪问题，属头显原生层 | 头显系统里重新校准追踪 | 复跑 |

---

## E. 串流成功判定（PC 侧，非头显侧）

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 「到底连上了没有？」 | PC 侧 `Get-NetTCPConnection -OwningProcess <StreamerPid> \| Where-Object State -eq Established \| Select LocalPort,RemotePort` | **4 条** `LocalPort` 为 38810/38820/38830/38840 的 `Established`。本机实测快照（`02_networking_streaming.md:53-58`）：`192.168.11.23:38810 → 192.168.11.15:34405`、`38820 → :42021`、`38830 → :39637`、`38840 → :43807` | 不足 4 条 → 对应通道没建起来（0 条 = 没连上；1 条 = 只握手成功） | 按缺哪条放行哪个端口 | 重连后 4 条齐 |
| 「RemotePort 是乱的，是不是错了？」 | 同上 | **正常**：`RemotePort` 是对端 ephemeral，不是固定协议端口（`02_networking_streaming.md:60` 原文结论） | 不是错误 | 无需处理 | — |
| 「能不能看到具体字节/丢包」 | 头显 UI 上 PerformanceOverlay（本机可读项：`tbBitrate` / `tbMaxBitrate` / `tbCodec`，`PerformanceOverlay.cs:44-46`） | 有实时数值 | 无 → 头显侧 Overlay 没开 | 在头显设置里开性能浮层 | 复看 |
| 「这次串流到底用什么编码？」 | PC 侧读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json` 的 `PreferredCodec` / `CodecName` | 本机实测 `11` / `"AV1 10-bit"` | 与头显选的对不上 → 画质/延迟异常 | 在 Streamer 下拉里改（两键是同一项，`VDH.Extra.cs:574`） | 重读 JSON |

---

## F. adb 本身连不上（检测页自身的前置故障）

| 症状(用户原话) | 检查命令 | 期望 | 不符说明什么 | 修复动作 | 复验命令 |
|---|---|---|---|---|---|
| 工具说「找不到 adb」 | 检测页跑候选表（见 `01-adb-playbook.md` §1.3） | 至少一个候选 `File.Exists` | 全 miss。**本机实测（2026-10-05）**：18 个候选里只有 `D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe` 命中，版本 `1.0.41 / 30.0.4-6686687`；`where adb` exit=1、`ANDROID_HOME`/`ANDROID_SDK_ROOT` 均为空 | 手动指定路径，或下载官方 platform-tools（**只允许 `dl.google.com`**，白名单抄 `VDH.Extra.cs:364-367`） | 复跑候选表 |
| 「adb 一直报 tcpip requires an argument」 | 直接跑不带参数的 `adb tcpip` | 输出 `adb.exe: tcpip requires an argument`（本机实测原文） | 这是**正常**的参数校验，不是 adb 坏了 | 补上端口号：`adb -s <S> tcpip 5555` | 带参数重跑 |
| 「adb 列表里没有头显」 | `adb devices -l` | 有 `<serial>\tdevice ...` 行 | 空列表 / `unauthorized` / `offline` | `unauthorized` → 头显里点允许；`offline` → 拔插数据线；空 → 换数据线或唤醒头显 | 复跑 |
| 「无线 adb 连不上」 | `adb -s <S> shell ip -4 addr show wlan0` 的 `/prefix` 与 PC 比对 | 同一子网 | 不同子网 → **adb over Wi-Fi 不通**；同时 VD 的 38850 广播也不会通（一箭双雕） | 头显与 PC 接同一子网 | 复比前缀 |
| 「开了无线调试之后不安全」 | 无需检测命令 —— **检测页读到串号形如 `<ip>:5555`（而非 USB 序列号）时必须主动弹警告** | — | 无线 ADB 是**明文**通道：同网段/恶意热点/ARP 欺骗者能完全控制头显（装卸应用、截屏录屏、开麦、读 token）。HorizonOS 上 ADB 拿的是 shell(uid 2000)，足以 `pm grant` 提权与装任意 APK | 用户在头显里关闭无线调试（`设置 → 开发者选项 → 无线调试 → 关闭`）；必要时「撤销 USB 调试授权」；PC 侧 `adb kill-server` | 关闭后 USB 仍可连；**VD 串流不受影响**（38810-38840 与 5555 无关） |

---

## G. 证据索引（每条判据的来源）

| 表 | 主要证据 |
|---|---|
| A | `PROJECT_HISTORY.md:138-152`（UDP 38850 广播、账号字符串匹配、实测返回 0 个）；`02_networking_streaming.md:41-60`（端口矩阵 + 本机快照）；`:177-179`（只覆盖同网段、先开 Streamer）；`managed-strings.csv:5,8,9,10,11,60,68,91,98,143`（UI 字面量）；`install_template.bat:82-85`（睡着不是故障）；`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:218-219`（EXIT_SELF 真机） |
| B | `HANDOFF.md:32,171` + `install_template.bat:60`（30s 收焦点）；`NetworkManager.cs:297`（Failed to connect 字面量）；`02_networking_streaming.md:41-48`（端口）；`logcat_exit6.txt:56`（Launch is blocked，真机）；`VD_V76_...:205-206`（MemoryBroker，真机）；`V85_..._REPORT:192`（xrWaitFrame，真机）；`logcat_exit3.txt:1488` / `logcat_exit4.txt:196`（托管崩溃栈定位） |
| C | `install.bat:25-31`（7 条原文）；`HANDOFF.md:23-29,32,54-55,171`；`install_template.bat:59-75`（10 条 + `~30s` 注释）；`VDH.Extra.cs:485-510`（`pm grant` 三种返回判据）；`VrApp.cs:172`（OnPause 释放 WiFiLock） |
| D | `WifiMetrics.cs:21-41`（频段映射 + `IsSlow()` 双阈值）；`PerfStatsHelper.cs:112-123`（App 读 WiFi 的方式）；`ComputersTab.cs:97-123,169-184`（警告文案与颜色）；`managed-strings.csv:7,8,9,10,11,20`（字面量）；`StreamingTab.cs:989-992`（滑条上限来源）；`VDH.cs:33-34,60-63`（IL 偏移与 SHA 登记表）；`VDH.Extra.cs:136-148,181-184,574`（编码风险、500 上限、两键同项）；`02_networking_streaming.md:53-60`（四条 Established 真机快照） |
| D2 | `InputSystem.cs:46-78`（`Update` 的 5 条整帧 return 守卫：`hmd == null`、`hmd.Type < 259`、`StreamingSource != null`、`EmulateGamepad && ControllerGamepad`、`ScreenAdjustment == ThumbstickMotion`）；`05_input_aot_native.md:13-30`（守卫清单 + Quest 3 的 `HmdType >= 259` 门槛 + 「identity gate 不可绕过」）；`managed-strings.csv:15,23`（两条 Input 页文案）；`Game.cs:1770-1772`（菜单键的 gamepad-emulation guard） |
| E | `02_networking_streaming.md:53-60`；`PerformanceOverlay.cs:44-46,389-391`；本机实测 `C:\ProgramData\Virtual Desktop\StreamerSettings.json`（`AutoAdjustBitrate:false`、`PreferredCodec:11`、`CodecName:"AV1 10-bit"`、`MonitorCount:1`）；本机实测 Streamer FileVersion `1.34.22.0` |
| F | `VDH.Extra.cs:239-297`（候选表）+ `:364-367`（下载白名单）；本机实测 `where adb` exit=1、`ANDROID_HOME`/`ANDROID_SDK_ROOT` 空、18 候选仅 VIVE Hub 命中（`adb version` → `1.0.41 / 30.0.4-6686687`）、`adb tcpip` 无参报 `tcpip requires an argument`、`adb get-state` 无设备时 `error: no devices/emulators found` exit=1、`adb devices -l` 空列表输出、`adb help` 含 `pair HOST[:PORT] [PAIRING CODE]` |

---

## H. `[未验证]` 汇总（需真机）

| 表 | 行 | 需要什么 |
|---|---|---|
| A | `ip route show` / `getprop ro.vros.build.version` / `settings get global http_proxy` 的输出形态 | 头显 USB 连接授权 |
| D | `dumpsys wifi` 里 SSID / 频段 / LinkSpeed / RSSI 的可读性与字段名 | 头显连接授权（HorizonOS 可能限制；`01-adb-playbook.md` §3.3 已注明 App 自己读不到 SSID，`PerfStatsHelper.cs:123` 传的是 `null`） |
| D | `dumpsys audio \| grep -i mic` 的输出形态 | 头显连接授权 |
| C | `pm grant` 7 条的返回输出形态（成功=空、`not a changeable`、`has not requested`、`Exception` 四种判据来自旧 VDH 代码 `VDH.Extra.cs:501-505`，**本轮未在真机复跑**） | 头显连接授权 |
| C | `dumpsys package` 里 `runtime permissions:` 段的 `granted=true` 形态 | 头显连接授权 + 已执行 7 条 `pm grant` |
| A/D | 头显 UI 上「码率滑条实际值」能否从 adb 读到 | 需要 App 设置文件路径 —— **本工作区无证据**；读 `/data/data` 需 root 或 `run-as`（实验线注入过 `android:debuggable="true"`，`build_apk_debuggable.py:6`；成品线未确认） |
| A | 「连上立刻断开」时 `Get-NetTCPConnection` 的实际条数（本表按 `02_networking_streaming.md` 的端口矩阵推断，未在真机失败态下抓过快照） | 头显 + PC 联动复现一次断连 |
| E | 「只建了音频没建视频」时的实际连接表（**整行均为推断**，依据是四通道独立：`NetworkManager.cs:88-89` 里 `IsConnected` setter 只在控制通道就绪时 `StartReceiving()` 视频与音频） | 头显 + PC 联动复现 |
| D2 | `dumpsys input` 里控制器枚举字段名（`controller` / `oculus_touch` / drift 字段）—— 全部形态 `[未验证]` | 头显连接授权 + 控制器已配对开机 |
| D2 | 「手柄生效」本身**无 adb 判据**：`InputSystem.Update` 每帧跑、无 Log、无状态文件（`InputSystem.cs:46-78` 全文无 `Log` 调用）。检测页只能报「哪些设置会让 Update 整帧跳过」，不能报「手柄通不通」 | 无解，需用户主观确认 |

---

## I. 给实现者的一条纪律

**本表里任何一行，只要「期望」列写的是形态而不是本机实测输出，UI 上就必须标 `[未验证]`。**
本轮未连头显，绝大多数行属于这一类。把它们和 E 栏（第 G 节）、D 栏（第 H 节）里的实测证据混在一起不加区分，
等于把推断包装成事实 —— 这是这份手册最大的使用风险。

优先做成硬判据的三类（都有本轮或上一轮的实测支撑）：

1. **本机实测**：`where adb` 无、18 候选只有 VIVE Hub 命中、`adb tcpip` 无参报错文案、`adb get-state` 无设备 exit=1、Streamer FileVersion `1.34.22.0`、`StreamerSettings.json` 各键值、11 份真机 logcat 的 tag 统计。
2. **上一轮真机记录**：`dumpsys package` 形态、`pidof` 的 `no process`、`exit-info` 的 `EXIT_SELF`、`MemoryBroker does not have access`、`Launch is blocked`、`xrWaitFrame returning early`、三条崩溃栈、`VRD` 栈格式、38810-38840 四条 Established。
3. **源码/二进制字面量**：7 项权限名与 Activity 名、UI 上 20+ 条状态文案、`IsSlow()` 的 `Frequency < 4000 || LinkSpeed < 450` 双阈值、四通道端口、`PreferredCodec`/`CodecName` 同项。