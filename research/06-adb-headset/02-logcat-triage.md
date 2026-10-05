# 02 — logcat 判障（过滤器、成功/失败对照、特征串）

> 本文件的证据分三类，逐条标注：
> - **【源码确证】**：反编译源码里有字面量或调用点，附 `file:line`。
> - **【真机日志确证】**：上一轮工程在 Quest 3（序列号 `2G0YC5ZHBD01XF`）上抓的 logcat 原文，附文件名与行号。
> - **【假设】**：无日志点可查，按行为推断。**VDHelper 必须标成「假设」，不能当判据。**
>
> **本机 2026-10-05 未连接头显**，本文件所有命令均未在真机上复跑。真机日志证据来自
> `F:/Project/VirtualDesktop/analysis/apk_patch/logcat_*.txt`（11 份，共约 13,000 行）与
> `run_recoveredpkg_20260620-070842/logcat.txt`。

---

## 0. 先说最重要的三件事

**① App 自己只打一个 tag：`VRD`。**
全工作区搜索 `Android.Util.Log` / `Log.Error` 等调用点，`VirtualDesktop.Mobile.dll` 与
`VirtualDesktop.Android.dll` 两个程序集里**只有一处**：

```csharp
// F:/Project/VirtualDesktop/analysis/apk_patch/decompiled/vdandroid/VirtualDesktop.Android/VrApp.cs:300-304
private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
{
    Exception ex = (Exception)e.ExceptionObject;
    ex = ex.GetBaseException();
    Log.Error("VRD", ex.ToString());      // ← 全 App 唯一的 logcat 输出点
```

且它只在 **未捕获异常** 时触发。**所以「logcat 里没有 VD 的日志」在正常运行时是预期现象，不是故障。**
（`[推断]`，依据：11 份真机 logcat 中 `E VRD` 行只出现在两份崩溃抓取里 —
`logcat_exit5.txt` 与 `logcat_exit6.txt` 各 53 行；其余 9 份 0 行。见 §5.1 计数。）

**② 真正的网络失败**在头显里**不打 logcat**。VD 的发现/连接失败全部走 **UI 字符串 + WCF 异常对象**，
没有 `Log.*` 调用。因此：
- `logcat` **不能**用来判断「头显搜不到电脑」；
- 要判断搜索结果，只能读 **UI 上的字符串**（这些字面量在二进制里，见 §4），
  或用 **PC 侧** 的连接表 / 防火墙规则来交叉验证（归 NetDiagnosis）。
- `[未验证]` 是否存在某条被 `Log.Debug` 但被 release 构建裁掉的日志路径 —— 工作区内**无证据**。

**③ 头显侧唯一能靠 logcat 判的 VD 故障是「崩溃 / 自毁」**，而且有非常清晰的三层信号
（`AndroidRuntime` FATAL → `MonoDroid` UNHANDLED → `E VRD` 托管栈），见 §3。

---

## 1. 采集流程（VDHelper 检测页应照此编排）

上一轮工程的两个脚本已经把顺序固定下来了，直接抄：

`analysis/apk_patch/capture_diagnostic.bat:1-31`：
```bat
set ADB=D:\Software\Android\Sdk\platform-tools\adb.exe
set PKG=com.dwgx1.vd.recovered
%ADB% logcat -c                                        REM 清缓冲
%ADB% shell am force-stop %PKG%                        REM 先强停，保证干净起点
start /B %ADB% logcat -v threadtime > "%OUTFILE%" 2>&1 REM 全量后台抓，threadtime 带线程号
timeout /t 2 /nobreak > nul
%ADB% shell am start -n %PKG%/md59102214312e19799944a61bf7bc2f23e.VrActivity
timeout /t 30 /nobreak > nul
taskkill /F /IM adb.exe > nul 2>&1
```

`analysis/apk_patch/capture_vd_delayed_window.bat:22-31` 加了唤醒与多时点采样：
```bat
adb shell input keyevent KEYCODE_WAKEUP
adb shell am force-stop %PKG%
adb logcat -c
adb shell am start -n %PKG%/%ACT%
REM 等 10s（XR 会话预热），再每 3s 连拍到 25s
```

`analysis/apk_patch/capture_vd_view.bat:19-24` 是纯只读快照（无清缓冲、无启动）：
```bat
adb get-state
adb shell pidof %PKG%
adb shell dumpsys power
adb shell dumpsys window
adb shell dumpsys SurfaceFlinger --display-id
```

**VDHelper 的编排建议**（三步，全部只读或仅启动 App）：
1. `adb -s <serial> logcat -c`
2. `adb -s <serial> shell am force-stop <pkg>` → `am start -n <pkg>/<activity>`（用户点「开始诊断」时执行）
3. 用户复现症状后，点「抓日志」→ `adb -s <serial> logcat -d -v threadtime > 文件`
   并把 §5 的过滤器逐条跑一遍。

⚠️ `logcat -c` 会**清空整个环形缓冲**（不只是本 App 的）。VDHelper 不得在用户未确认时执行。
`am force-stop` 会杀正在跑的 App —— 同理需确认。

---

## 2. 过滤器（可直接粘贴）

### 2.1 分层过滤器（按用途）

```bash
PID=$(adb -s <serial> shell pidof VirtualDesktop.Android | tr -d '\r')
```

| 用途 | 命令 | 说明 |
|---|---|---|
| **只看 App 自己** | `adb -s <serial> logcat -d -v threadtime --pid=$PID` | Android 8+ 支持 `--pid`。旧 VDH 用 `logcat -d -t 80 --pid=<n>`（`VDH.Extra.cs:468`） |
| **App 无进程时的兜底** | `adb -s <serial> logcat -d -t 200 -s AndroidRuntime:E MonoDroid:I ActivityManager:I ActivityTaskManager:I` | 旧 VDH 兜底式（`VDH.Extra.cs:470`），把 `Unity:I` 换成 `MonoDroid:I`（Unity 不存在，见 §5.3） |
| **崩溃三件套** | `adb -s <serial> logcat -d -v threadtime \| grep -E "FATAL EXCEPTION\|MonoDroid:\|[VDIWEF] VRD"` | §3 的核心过滤器 |
| **VR 焦点 / 渲染** | `adb -s <serial> logcat -d \| grep -E "XR_SESSION_STATE_\|renderingEnabled:\|VrFocus\|IMMERSIVE"` | 见 §3.3 |
| **系统弹窗 / 抢焦点** | `adb -s <serial> logcat -d \| grep -E "GrantPermissionsActivity\|GuardianDialogActivity\|SystemUXController\|Launch is blocked"` | 见 §3.4 |
| **entitlement / 云账号** | `adb -s <serial> logcat -d \| grep -E "AppManagerInternal: Entitlement\|FBNSHandler\|Invalid OAuth"` | 见 §3.5 |
| **网络栈（系统侧）** | `adb -s <serial> logcat -d \| grep -E "ExternalPlatformNetwork\|dhcp\|wlan0"` | 见 §3.6 |

### 2.2 上一轮工程用过的过滤器（逐条抄，保留原措辞）

`analysis/apk_patch/capture_diagnostic.bat:42-65` 有 7 段 findstr，这是上一轮**实际跑过**的：

```bat
findstr /I "VRD" "%OUTFILE%"                                     REM .NET 异常（tag=VRD）
findstr /I "openxr\|xrCreate\|xrEnd\|XR_ERROR\|session_state" "%OUTFILE%" | findstr /V "DEBUG"
findstr /I "EGL_BAD\|eglMakeCurrent\|eglCreateContext\|EGL Error" "%OUTFILE%"
findstr /I "GL_ERROR\|GL error\|glError" "%OUTFILE%"
findstr /I "FATAL\|CRASH\|signal\|tombstone\|abort\|backtrace" "%OUTFILE%"
findstr /I "mono\|System.Exception\|NullReference\|InvalidCast\|InvalidOperation\|UnhandledException" "%OUTFILE%"
```

⚠️ **其中三条在本机的实测是 0 命中**（统计了 11 份真机 logcat，`grep -c`）：
- `EGL_BAD|eglMakeCurrent|eglCreateContext|EGL Error` → **0**（无 EGL 错误行）
- `GL_ERROR|GL error|glError` → **0**
- `openxr|XR_ERROR|session_state` → 只有 `XR_ERROR_CALL_ORDER_INVALID`
  （`logcat_exit3.txt:1030,1032`，属 vrshell 的系统应用，不是 VD）

→ **VDHelper 的过滤器应砍掉 EGL / GL 两段**：它们在这批真机日志里一次都没响过，
留着只会让用户以为「没输出 = 没检查过」。保留 `VRD` + `MonoDroid` + `AndroidRuntime` +
`FATAL` 四条即可覆盖已证实的故障面。

### 2.3 反向过滤器（`-v threadtime` 下按 tag 精确取）

```bash
adb -s <serial> logcat -d -v threadtime -s VRD:E MonoDroid:I AndroidRuntime:E ActivityTaskManager:I
```
`-s TAG:LEVEL` 语法在本机 adb 30.0.4 的 `adb help` 里列为 `-s TAG[:PRIORITY]` 形式（本轮未在设备上验证可组合使用）。`[未验证]`

---

## 3. 失败时的日志（三层信号，逐层升级）

### 3.1 第一层：Java 层 FATAL

【真机日志确证】`logcat_exit5.txt:735-739`：
```
--------- beginning of crash
06-20 09:20:51.960 23378 23378 E AndroidRuntime: FATAL EXCEPTION: main
06-20 09:20:51.960 23378 23378 E AndroidRuntime: Process: com.dwgx1.vd.recovered, PID: 23378
06-20 09:20:51.960 23378 23378 E AndroidRuntime: java.lang.NoSuchMethodError: no non-static method "Lcom/yvr/thirdsdk/Account;.<init>(Landroid/content/Context;)V"
06-20 09:20:51.960 23378 23378 E AndroidRuntime: 	at md59102214312e19799944a61bf7bc2f23e.VrActivity.n_onCreate(Native Method)
```

形态固定三行：`FATAL EXCEPTION: main` / `Process: <pkg>, PID: <n>` / 异常类名 + 消息。

`logcat_exit3.txt:1485-1488` 是托管异常映射成 Java 异常的形态：
```
06-20 09:00:13.959 21472 21472 E AndroidRuntime: FATAL EXCEPTION: main
06-20 09:00:13.959 21472 21472 E AndroidRuntime: Process: com.dwgx1.vd.recovered, PID: 21472
06-20 09:00:13.959 21472 21472 E AndroidRuntime: android.runtime.JavaProxyThrowable: [System.NullReferenceException]: Object reference not set to an instance of an object
06-20 09:00:13.959 21472 21472 E AndroidRuntime: 	at VirtualDesktop.VrApp.OnResume + 0x6(Unknown Source)
```
→ `android.runtime.JavaProxyThrowable: [System.<X>Exception]` 是**托管异常的信号**，
后面的 `at VirtualDesktop.VrApp.<Method>` 直接指出是哪个托管方法炸的。
`VrApp.OnResume + 0x6` 对应源码 `VrApp.cs:169-173`（`OnPause`/`OnResume` 里碰 `_game`）。

`logcat_exit4.txt:193-196` 是类型加载失败（对应「IL/AOT 配平」类问题）：
```
E AndroidRuntime: FATAL EXCEPTION: main
E AndroidRuntime: Process: com.dwgx1.vd.recovered, PID: 22080
E AndroidRuntime: android.runtime.JavaProxyThrowable: [System.TypeLoadException]: Could not load type of field 'VirtualDesktop.VrApp+<CreateAccessTokenAsync>d__30:<userTask>5__4' (5) due to: Could not resolve type with token 0100005d from typeref (expected class 'Oculus.Platform.LoggedInUser' in assembly 'Oculus.Platform, Version=1.18.57.0, ...)
E AndroidRuntime: 	at VirtualDesktop.VrApp.OnCreate + 0x91(Unknown Source)
```

### 3.2 第二层：Mono 运行时 UNHANDLED

【真机日志确证】`logcat_exit5.txt:763-763, 881-882`（紧接 FATAL 之后 ~9ms）：
```
06-20 09:20:51.961 23378 23407 W monodroid-assembly: Shared library 'liblog' not loaded, p/invoke '__android_log_print' may fail
06-20 09:20:51.969 23378 23378 I MonoDroid: UNHANDLED EXCEPTION:
...
06-20 09:20:51.969 23378 23378 E VRD     : Java.Lang.LinkageError: no non-static method "Lcom/yvr/thirdsdk/Account;.<init>(Landroid/content/Context;)V"
```

`logcat_exit3.txt:1510-1516` 同形态：
```
W monodroid-assembly: Shared library 'liblog' not loaded, p/invoke '__android_log_print' may fail
I MonoDroid: UNHANDLED EXCEPTION:
I MonoDroid: Android.Runtime.JavaProxyThrowable: Exception_WasThrown, Android.Runtime.JavaProxyThrowable
I MonoDroid:   --- End of managed Android.Runtime.JavaProxyThrowable stack trace ---
I MonoDroid: android.runtime.JavaProxyThrowable: [System.NullReferenceException]: Object reference not set to an instance of an object
I MonoDroid: 	at VirtualDesktop.VrApp.OnResume + 0x6(Unknown Source)
```

⚠️ **`W monodroid-assembly: Shared library 'liblog' not loaded` 是噪声，不是故障。**
它在 5 份崩溃 logcat 里都出现，但 App 在无崩溃时也会打同族的
`Shared library 'android' not loaded, p/invoke 'ANativeWindow_release' may fail`
（`logcat_v2.txt:10`、`logcat_v2d.txt:1140,1147`、`logcat_binary_patch.txt:1092,1111`）。
→ VDH 过滤时应把 `monodroid-assembly` 一律丢弃，否则每次都报「缺库」。

`MonoDroid: UNHANDLED EXCEPTION:` 是**关键锚点**：它一出现就说明有托管异常，
往后的 `MonoDroid:` 行是托管栈，再往后的 `E VRD` 行是 VrApp 处理器补打的完整栈。

### 3.3 第三层：`E VRD` —— 唯一带完整托管栈的 tag

【真机日志确证】`logcat_exit5.txt:882-894` 原文（这是本文件最重要的日志样例）：
```
06-20 09:20:51.969 23378 23378 E VRD     : Java.Lang.LinkageError: no non-static method "Lcom/yvr/thirdsdk/Account;.<init>(Landroid/content/Context;)V"
06-20 09:20:51.969 23378 23378 E VRD     :    at Java.Interop.JniEnvironment.InstanceMethods.GetMethodID(JniObjectReference , String , String )
06-20 09:20:51.969 23378 23378 E VRD     :    at Java.Interop.JniType.GetConstructor(String )
06-20 09:20:51.969 23378 23378 E VRD     :    at Java.Interop.JniPeerMembers.JniInstanceMethods.GetConstructor(String )
06-20 09:20:51.969 23378 23378 E VRD     :    at Java.Interop.JniPeerMembers.JniInstanceMethods.FinishCreateInstance(String , IJavaPeerable , JniArgumentValue* )
06-20 09:20:51.969 23378 23378 E VRD     :    at Android.Views.SurfaceView..ctor(Context )
06-20 09:20:51.969 23378 23378 E VRD     :    at OpenTK.GameViewBase..ctor(Context context)
06-20 09:20:51.969 23378 23378 E VRD     :    at VirtualDesktop.VrGameView..ctor(Activity activity, IGraphicsContext graphicsContext)
06-20 09:20:51.969 23378 23378 E VRD     :    at VirtualDesktop.VrApp.OnCreate(Activity activity)
06-20 09:20:51.969 23378 23378 E VRD     :    at VirtualDesktop.VrActivity.OnCreate(Bundle savedInstanceState)
06-20 09:20:51.969 23378 23378 E VRD     :    at Android.App.Activity.n_OnCreate_Landroid_os_Bundle_(IntPtr jnienv, IntPtr native__this, IntPtr native_savedInstanceState)
06-20 09:20:51.969 23378 23378 E VRD     :    at Android.Runtime.JNINativeWrapper.Wrap_JniMarshal_PPL_V(_JniMarshal_PPL_V callback, IntPtr jnienv, IntPtr klazz, IntPtr p0)
06-20 09:20:51.969 23378 23378 E VRD     :   --- End of managed Java.Lang.LinkageError stack trace ---
06-20 09:20:51.969 23378 23378 E VRD     : java.lang.NoSuchMethodError: no non-static method "Lcom/yvr/thirdsdk/Account;.<init>(Landroid/content/Context;)V"
06-20 09:20:51.969 23378 23378 E VRD     : 	at md59102214312e19799944a61bf7bc2f23e.VrActivity.n_onCreate(Native Method)
06-20 09:20:51.969 23378 23378 E VRD     : 	at md59102214312e19799944a61bf7bc2f23e.VrActivity.onCreate(VrActivity.java:37)
06-20 09:20:51.969 23378 23378 E VRD     : 	at android.app.Activity.performCreate(Activity.java:8636)
06-20 09:20:51.969 23378 23378 E VRD     : 	at android.app.Activity.performCreate(Activity.java:8614)
```

形态要点（VDHelper 解析器按这个写）：
- tag 是 `VRD`，级别 `E`，**tag 后有 5 个空格**（logcat 对齐 tag 宽度），消息以 `: ` 分隔。
- 第一行是异常 `ToString()` 的首行 = `异常类型: 消息`。
- 中间是托管栈，每行前缀 `    at `（4 空格）。
- 有结束标记行 `   --- End of managed <类型> stack trace ---`（3 空格）。
- 标记行之后是**同一异常的 Java 侧重复输出**（`:895` 起），VDHelper 应在结束标记处停止解析。

可从栈里直接读出的启动链（`VrApp.cs:41-87` + `VrActivity.cs:17-21` 已核对源码）：
`VrActivity.OnCreate` → `VrApp.OnCreate` → `VrGameView..ctor` → `SurfaceView..ctor`。
**栈里出现 `VirtualDesktop.VrApp.OnCreate` = 崩在启动期**；出现在 `OnResume` = 崩在恢复期。

### 3.4 VR 焦点 / 被系统弹窗盖住（**不是 VD 的 bug，判据要能区分**）

【真机日志确证】`V85_..._REPORT_20260623.md:168-171`：
```
com.oculus.guardian/...GuardianDialogActivity is visible.
com.dwgx1.vd.recovered/...VrActivity task is visible=false.
com.dwgx1.vd.recovered window is isOnScreen=false / isVisible=false.
```
`V85_..._REPORT:192-194`：
```
OpenXR_Frame: xrWaitFrame: returning early due to activity pause
MemoryBroker: client com.dwgx1.vd.recovered does not have access
  (app ops or VR focus)
```
`V81_REMOTE_CAPTURE_FINDING_20260623.md:43-44`：
```
Launch is blocked because: a Guardian dialog is currently showing.
Caching launch for component com.dwgx1.vd.recovered/.../VrActivity
```

【真机日志确证】`logcat_exit6.txt:56`（同形态的原始行）：
```
06-20 09:22:21.286  2985  3419 I [SEO] SystemUXController: Launch is blocked because: a Reprojected OS dialog is currently showing. Caching launch  for component com.dwgx1.vd.recovered/md59102214312e19799944a61bf7bc2f23e.VrActivity
```

`MemoryBroker: client <pkg> ... does not have access (app ops or VR focus)`
【真机日志确证】`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:205-206`：
```
06-23 08:33:49 MemoryBroker INPUT_TYPE_MAP: client com.dwgx1.vd.recovered (3195) does not have access (app ops or VR focus)
06-23 08:33:49 MemoryBroker HAND_TRACKER: client com.dwgx1.vd.recovered (3195) does not have access (app ops or VR focus)
```

→ **判定规则**：`MemoryBroker ... does not have access (app ops or VR focus)`
= 「**VR 焦点被系统收回**」。先查权限（7 项）与是否有 Guardian 弹窗，**不要**当成 VD 网络问题。
这是「黑屏 / 没画面」最常见的真因之一。

### 3.5 entitlement / 云账号（离线基线下的**预期噪声**）

【真机日志确证】`logcat_v2.txt:141`：
```
06-20 09:44:48.013  3403 13132 W AppManagerInternal: Entitlement for packageName=com.dwgx1.vd.recovered not found, channels=[Store, Q4B, PcStore]
```
（`channels=[Store, Q4B, PcStore]` 表示查遍了三个渠道都没有 entitlement）

【真机日志确证】`logcat_exit5.txt:1547` 与 `:1616`（同一现象在 `logcat_binary_patch.txt:1199` / `:1350` 重复出现，`fbtrace_id` 不同）：
```
06-20 09:20:52.458 23378 23441 I OVRPlatform: [FBNSHandler] Push token received
06-20 09:20:52.688 23378 23418 E OVRPlatform: [FBNSHandler] Failed to register a push token: {"error":{"code":190,"error_data":{},"fbtrace_id":"ASeWB6MfdjcFfOl_05u3W_c","message":"Invalid OAuth 2.0 Access Token","type":"OCApiException"}}
```

【源码确证】这两类失败的**界面文案**（`reference/vdapkpatcher/profiles/vd-1.34.22/managed-strings.csv`）：
- `:98` `"Failed entitlement check"`（`RefreshComputersInternalAsync` IL_01ba）
- `:105` `"Unable to retrieve identity"`（IL_02bf）
- `:112` `"Unable to validate identity"`（IL_05a4）
- `:92` `"You need to purchase the app in the Meta Quest store.\nIf you already own it, try restarting your Quest."`（IL_0159）

【源码确证】成品把这些失败后的**自毁调用 NOP 掉了**，所以离线时 App 不会自杀，会继续往下走
（`analysis/report_sections/02_networking_streaming.md:126-150`，`CreateAccessTokenAsync` RVA 0x2A6C 处 8 处 NOP）。

→ **判定规则**：`AppManagerInternal: Entitlement ... not found` 与
`FBNSHandler ... Invalid OAuth 2.0 Access Token` 在**离线 / 去鉴权基线下是预期出现**的，
只要用户没同时报「连不上」就不该报警。VDHelper 应把这两条标为**黄色提示**而不是红色错误。

### 3.6 网络栈（系统侧）

**重要：VD 自己的 socket 层在 logcat 里没有日志点。**【源码确证】
`NetworkManager.cs`（453 行）全文无任何 `Log.*` 调用；`decompiled/xenko/VirtualDesktop.Mobile/`
下的 grep `Android\.Util\.Log|Log\.(d|i|w|e|v)\(|Console\.WriteLine|Trace\.WriteLine` **零命中**。

系统侧能看到的相关 tag：
- `E ExternalPlatformNetwork:` —— Oculus 平台的 P2P 网络（VD 不走这条，VD 走自己的 38850/38810）
  【真机日志确证】`logcat_v2d.txt` 中有该 tag 出现。
- `I OVRPlatform: [Context] Didn't find config(DisableP2pNetworking), returning default(0)`
  （`logcat_v2d.txt:2` 区域）—— OVR 默认启用 P2P，VD 不用它。

→ **VDHelper 若想在头显侧证明「UDP 38850 广播是否发出去了」，logcat 做不到。**
只能靠：① UI 上的发现结果字符串；② PC 侧抓包/连接表；③ `adb shell dumpsys netstats`（**未验证**）。

---

## 4. 「串流成功」与「串流失败」的对照

### 4.1 失败侧的**可判定**信号（全部有证据）

| # | 信号 | 来源 | 含义 |
|---|---|---|---|
| F1 | `E VRD` 有行 | 真机 `logcat_exit5/6` | 托管未捕获异常 |
| F2 | `AndroidRuntime: FATAL EXCEPTION: main` | 真机 `logcat_exit3/4/5/6` | 进程级崩溃 |
| F3 | `MonoDroid: UNHANDLED EXCEPTION:` | 真机 `logcat_exit3/5/6` | Mono 捕获到托管异常 |
| F4 | `WindowManager WIN DEATH <pkg>/...VrActivity` | 真机 `VD_V76:230` | 窗口消失 |
| F5 | `ActivityManager Process <pkg> (pid N) has died: fg TOP` | 真机 `VD_V76:231` | 前台进程死 |
| F6 | `dumpsys activity exit-info` → `reason=1 (EXIT_SELF) status=1` | 真机 `VD_V76:218-219` | **App 自杀**（不是崩溃） |
| F7 | `MemoryBroker: client <pkg> ... does not have access (app ops or VR focus)` | 真机 `VD_V76:205-206` | VR 焦点被收回 |
| F8 | `SystemUXController: Launch is blocked because: a ... OS dialog is currently showing.` | 真机 `logcat_exit6:56` | 系统弹窗挡住 |
| F9 | `OpenXR_Frame: xrWaitFrame: returning early due to activity pause` | 真机 `V85:192` | 没进 VR 渲染循环 |

⚠️ **F6 vs F2 的区分很重要**：`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:29` 明确说 v7.6 那次
「ended as app death / `EXIT_SELF` status `1`, **not** as a captured Java `FATAL EXCEPTION`」，
且 `:237-240` 明确「No current `AndroidRuntime FATAL EXCEPTION`, `NullReferenceException`,
`NoSuchMethodError`, or native signal line was found」。
→ 用户说「打开就退」时，**先跑 `dumpsys activity exit-info`**：如果是 `EXIT_SELF`，
问题是鉴权自毁路径（补丁没生效 / 权限缺失），不是崩溃；VDHelper 不该报「崩溃」。

### 4.2 成功侧的可判定信号（**均为系统侧，不是 VD 自己打的**）

| # | 信号 | 来源 | 含义 |
|---|---|---|---|
| S1 | `ActivityTaskManager: Displayed <pkg>/<activity> for user 0: +<ms>` | 真机 `logcat_v2.txt:11`（+763ms）、`logcat_v2d.txt:1148`（+785ms）、`logcat_binary_patch.txt:1112`（+1s108ms） | Activity 已上屏。**VDHelper 最可靠的「App 起来了」判据** |
| S2 | `UiModeController: Notify UI mode change: UiModeConfiguration { uiModeFlags = 8 (IMMERSIVE), ... immersiveAppPackageName = <pkg> }` | 真机 `logcat_exit3.txt:142` | 已进入沉浸 VR 模式 |
| S3 | `InterstitialController: onImmersiveActivityAppearing/Appeared` | 真机 `logcat_exit6:54` | 沉浸态确认 |
| S4 | `[SEO] ShellSpatialWindowManagerService: Placing immersive activity into new volumetric window` | 真机 `logcat_exit3:45` | VR 窗口已放置 |
| S5 | `xrBeginSession [start]` / `[end]` + `XR_SESSION_STATE_FOCUSED` | 真机 `logcat_exit6:59,110,250,252` | OpenXR 会话聚焦 |
| S6 | `InterstitialManager: Foreground app change: immersiveApp <pkg>, pid: <n>, renderingEnabled: 1` | 真机 `logcat_exit6:100`（注意这条的 pkg 是 `com.oculus.vrshell`、pid 2985 —— **要匹配 pid，不能只匹配 pkg**） | 渲染已开启 |
| S7 | `VrFocus: onForegroundActivitiesChanged: ... fg: 1` | 【假设】形态未在真机日志中捕获到 `fg: 1` 的实例；真机只见 `fg: 0`（`logcat_binary_patch.txt:118`） | VR 焦点在 App 上 |

⚠️ **S1–S7 全部只是「App 起来了、进 VR 了」，没有任何一条能证明「串流成功」。**
原因已在 §0-② 说明：VD 的串流状态不打日志。

### 4.3 串流是否成功的**替代判据**（推荐 VDH 采用）

既然头显侧无日志，**串流成功必须从 PC 侧判定**。可用的三个数据源：

| 判据 | 命令 / 位置 | 成功形态 | 证据 |
|---|---|---|---|
| **PC 四条 Established 连接** | `Get-NetTCPConnection -OwningProcess <StreamerPid>` | 4 条 `192.168.x.x:38810/38820/38830/38840 → 192.168.y.y:<ephemeral>` 的 `Established` | `02_networking_streaming.md:53-58` 有本机实测快照：`Local 192.168.11.23:38810 -> Remote 192.168.11.15:34405 Established` 等 4 条，并说明「远程端口为对端 ephemeral，不是固定协议端口」（`:60`） |
| **Streamer 日志** | NLog FileTarget，`LogLevel.Error` 起 | `[未验证]` 文件名。配置来源 `decompiled_streamer/.../-/-.99.cs:36-39`：`AddRule(LogLevel.Error, new FileTarget("logfile"){ FileName = <某个路径拼接>(<参数>) }, "*")`，`:74` 默认级别 `LogLevel.Error` | 级别 = Error 意味着**只有 Error 及以上落盘**，所以成功/常规流程**不会**产生日志行 |
| **头显 UI 状态字符串** | 让用户在头显上看电脑列表项的 `tbStatus` | 见 §4.4 | 见下 |

→ **VDHelper 检测页的串流判定应以 PC 侧连接表为主，头显 logcat 为辅**。
这是分工，不是权宜：头显侧确实没有这个信息。

### 4.4 头显 UI 上会出现的状态字符串（源码/二进制确证，可直接做文案对照）

这些字面量全部来自 `reference/vdapkpatcher/profiles/vd-1.34.22/managed-strings.csv`
（每条都带所属方法与 IL 偏移，是 VD 自己写进 `Computer.Status` 或消息框的文本）：

| UI 会看到的英文原文 | 中文 | 归属方法 / IL | 判定含义 |
|---|---|---|---|
| `Not connected to Wi-Fi` | 未连接到WiFi | `ComputersTab::Update` IL_0169 / `GetComputersAsync` IL_028b | 头显没连 Wi-Fi |
| `{0:0.#} GHz \| {1:n0} Mbps \| {2} - Streaming performance will be degraded` | — 串流性能将会降低 | `ComputersTab::Update` IL_00cf | **频段或速率触发 `IsSlow()`**（`Frequency < 4000 \|\| LinkSpeed < 450`，`WifiMetrics.cs:38-41`） |
| `USB 2 \| {0:n0} Mbps - Bitrate will be limited` | — 码率将受到限制 | `ComputersTab::Update` IL_0204 | **PC 侧 USB 2 链路限速** |
| `Limited to USB 2 speeds. Try a better USB cable or different port on your computer` | 速度受 USB 2 限制… | `ComputersTab::RefreshComputers` IL_0253 | 同上 |
| `Computer not wired to router with Ethernet cable, performance will suffer` | 电脑未通过以太网线连接到路由器… | `RefreshComputers` IL_018c | PC 走无线 |
| `Computer wired but not with Gigabit Ethernet, performance will suffer` | 电脑已有线连接但非千兆以太网… | `RefreshComputers` IL_01d1 | PC 是百兆 |
| `Update Quest OS to use USB` | 更新 Quest 系统以使用 USB | `ComputersTab::.ctor` IL_0284 | Quest OS 太老 |
| `No computer found` | 未找到电脑 | `RefreshComputersInternalAsync` IL_0752 | **发现列表为空** |
| `Make sure your computer is running the Streamer app` | 请确认电脑正在运行串流端应用 | 同上 IL_0757 | 同上 |
| `Unable to retrieve your computers` | 无法获取你的电脑列表 | 同上 IL_0871 等 12 处 | 云查询失败 |
| `Meta servers unreachable, only showing local computers` | 无法连接到 Meta 服务器,仅显示本地电脑 | `GetComputersAsync` IL_02b7 | 云不可达 → **已 fallback 到本地发现** |
| `Virtual Desktop servers unreachable, only showing local computers` | 无法连接到 Virtual Desktop 服务器,仅显示本地电脑 | `GetComputersAsync` IL_060b | 同上（VD 自家注册服务器） |
| `Virtual Desktop servers partially unreachable, some computers might not appear` | …部分电脑可能无法显示 | IL_0604 | 部分平台失败 |
| `Meta servers not responding, only showing local computers` | Meta 服务器无响应,仅显示本地电脑 | IL_04b7 | 超时（在线 12s） |
| `Connecting...` | 正在连接... | `ConnectToComputerAsync` IL_00a0 / IL_01ec | 连接中（两个位置） |
| `Establishing connection...` | 正在建立连接... | 同上 IL_0a16 | 握手阶段 |
| `Measuring bandwidth...` | 正在测量带宽... | 同上 IL_0e43 | 带宽测量（`NetworkManager.MeasureBandwidth` `NetworkManager.cs:242-249`） |
| `Unable to connect, please try again.` | 无法连接,请重试。 | 同上 IL_07b9 | **连接失败终态** |
| `Failed to connect, please try again.` | 连接失败,请重试。 | `NetworkManager::OnStatusChanged` IL_009f | 断线终态（源码同 `NetworkManager.cs:297`） |
| `Streamer uses a newer version, update this app before connecting` | 串流端使用了更新的版本,请先更新本应用再连接 | `ConnectToComputerAsync` IL_0245 | 版本门 |
| `Checking for updates...` / `Updating Streamer, this may take a few minutes...` | 正在检查更新... / 正在更新串流端... | 同上 IL_090d / IL_098f | Streamer 侧自更新 |
| `Failed entitlement check` | 授权检查失败 | `RefreshComputersInternalAsync` IL_01ba | 鉴权失败（离线基线下 App 不自杀，见 §3.5） |
| `Unable to retrieve identity` / `Unable to validate identity` | 无法获取/验证身份信息 | 同上 IL_02bf / IL_05a4 | 同上 |
| `Muted in headset settings` | 已在头显设置中静音 | `SettingsTab::UpdateMicVolumeState` IL_0053 | 麦克风被系统静音 |

**这一张表是「头显搜不到电脑 / 码率被限 / 连上就断」等症状最可靠的判据来源** ——
因为 VD 把这些原因都写成了明确的 UI 文案，而 VDH 的检测页可以把「用户描述」映射到「让他看 UI 上的哪一行」。

⚠️ 混淆版 CSV 里 `managed-strings.csv:68` 的中文列被误填成 `Vrzwk汉化组(vrzwk.com)` ——
那是打包工具的广告串，不是译文。VDHelper 若要内置中文对照表，**必须手工修这一格**，
不能直接导入 CSV 的 Localized 列。

---

## 5. 日志统计（真机 11 份，用于验证过滤器不会误报）

统计方式：`grep -c` 逐文件计数，2026-10-05 在本机对 `F:/Project/VirtualDesktop/analysis/apk_patch/logcat*.txt`
与 `run_recoveredpkg_20260620-070842/logcat.txt` 执行。

### 5.1 App 自己打了多少

| 文件 | 总行数 | `E VRD` | `MonoDroid` |
|---|---:|---:|---:|
| `logcat_binary_patch.txt` | 1910 | 0 | 0 |
| `logcat_exit2.txt` | 540 | 0 | 0 |
| `logcat_exit3.txt` | 3642 | 0 | 53 |
| `logcat_exit4.txt` | 648 | 0 | 53 |
| `logcat_exit5.txt` | 1989 | **53** | 55 |
| `logcat_exit6.txt` | 2005 | **53** | 55 |
| `logcat_v2.txt` | 608 | 0 | 0 |
| `logcat_v2b.txt` | 839 | 0 | 0 |
| `logcat_v2c.txt` | 580 | 0 | 0 |
| `logcat_v2d.txt` | 1350 | 0 | 0 |
| `run_recoveredpkg_.../logcat.txt` | 1042 | 0 | 0 |

结论：
- `E VRD` **只在两份崩溃抓取里出现**（各 53 行）→ 与 §0-① 一致。
- `MonoDroid` 在 4 份里出现（53/53/55/55），其中两份**没有** `E VRD` ——
  说明有托管异常被 Mono 处理掉但 VrApp 的处理器没打出来（`logcat_exit3/4`：
  异常在 `VrApp.OnResume` / `OnCreate`，此时 `NetworkManager.MessagingClient == null` 或
  `ex.GetBaseException()` 已在上游被处理）。
  → **VDHelper 的过滤器必须同时包含 `MonoDroid` 和 `VRD`，不能只要 `VRD`。**

### 5.2 旧过滤器里零命中的两条

```
EGL_BAD|eglMakeCurrent|eglCreateContext|EGL Error   → 0（全部 11 份）
GL_ERROR|GL error|glError                          → 0（全部 11 份）
openxr|XR_ERROR|session_state                     → 仅 logcat_exit3 的 2 行 XR_ERROR_CALL_ORDER_INVALID（vrshell，非 VD）
```
理由与裁剪建议见 §2.2。

### 5.3 出现频次最高的 tag（`logcat_v2d.txt`，共 1350 行）

```
100 OVRService            79 libjingle        74 [CT]          42 [SEO]
39 MSF.C.MSFCore         30 nativeloader     29 RipcServerMgr 29 RipcClientConnection
27 OpenXR_Properties     26 TREX             24 CompatibilityChangeReporter
24 OVRLibrary            22 RuntimeIpcBroker 21 org.webrtc.Logging
21 OpenXR-Loader         19 TelemetryService 19 OpenXR_ExtensionAccess
19 AppManagerInternal    18 libloaderimpl    18 OVRPlatform
```

**这些全是 Oculus 系统栈与 vrshell 的噪声**（`[CT]`=Compositor Tunnel、`libjingle`=WebRTC、
`OVRService`/`OVRLibrary`=Oculus 服务、`TREX`=Oculus 手部追踪、`MSF`=Presence）。
→ VDH 若把整份 logcat 直接甩给用户，他们只会看到这些。**必须按 §2.1 先过滤再展示。**

`Unity` / `Xenko` tag：**11 份全部 0 命中**（`grep -cE '\bUnity\b'` / `\bXenko'`）。
旧 VDH 的兜底过滤器 `logcat -d -t 80 -s Unity:I AndroidRuntime:E ActivityManager:I`
（`VDH.Extra.cs:470`）里的 `Unity` 是**抄的模板残留，VD 不是 Unity 应用**，应删。
本 App 的运行时 tag 是 `MonoDroid` / `monodroid-assembly`，不是 `mono`（`mono` 也 0 命中）。

### 5.4 「渲染已开启」那行的坑

`logcat_exit6.txt:100`：
```
06-20 09:22:21.338  2626  4014 I InterstitialManager: Foreground app change: immersiveApp com.oculus.vrshell, pid: 2985,  renderingEnabled: 1. submittedFrames: 219834
```
`logcat_exit3.txt:1593` 同形态（`pid: 2985`, `submittedFrames: 202995`）。
**两条里的 `immersiveApp` 是 `com.oculus.vrshell`、pid 2985 = 系统 shell，不是 VD。**
→ VDH 判「渲染已开」必须 **同时匹配 pkg 与 PID**，只 grep `renderingEnabled: 1` 会误报。

---

## 6. 假设清单（「打开 X 功能后会打什么」）

工作区内**无任何证据**表明 VD 在这些场景打日志。以下按「如果真打，会长什么样」列出，
**全部标 [假设]，VDHelper 不得作为判据**，仅供用户自行 grep 时参考。

| 假设的开关 | 假设的日志点 | 假设的形态 | 为什么假设 |
|---|---|---|---|
| 打开 `FoveatedStreaming`（注视点串流） | 眼动追踪权限被拒 | `E VRD : … EYE_TRACKING …` 或权限拒绝回调 | 源码里失败路径是 `FoveatedStreaming = false` 静默回落（`VrApp.cs:267-276`），**没有 Log 调用** |
| 打开 `MicPassthrough` | `RECORD_AUDIO` 被拒 | 同上，`MicPassthrough = false` 静默回落 | `VrApp.cs:277-286`，同样无 Log |
| 连接时带宽测量 | 测量结果 | 假设会有类似 `MeasuredBandwidth=NNNN` 的行 | `NetworkManager.MeasureBandwidth`（`:242-249`）只写 `SettingsBase<...>.Default`，**无 Log**。数据只在 PerformanceOverlay 显示（`PerformanceOverlay.cs:389-391`） |
| 发现电脑成功 | 发现到 N 台 | 假设类似 `Found N computers` | `RefreshComputersAsync` 全程无 Log |
| 云注册成功 | 注册 token 下发 | 假设类似 `Registered <region>` | `ServiceHelper<IComputerRegistry>` 走 WCF，无 Log |
| AOT/IL 配平破坏 | Mono 校验失败 | **假设** `E MonoDroid : InvalidProgramException: IL_xxxx: ...` | 这类异常**确实存在过**（`05_input_aot_native.md:49-51` 记录 v8.3/v8.4 分别抛
  `InvalidProgramException: IL_0000: ret` 与 `IL_0002: bge.un IL_6f060009`），
  且 `MonoDroid: UNHANDLED EXCEPTION:` 的格式已真机确证（§3.2）→ **这一条可信度较高，但仍标假设**，
  因为那两条具体 IL 报错行本身未出现在本工作区的 logcat 抓取里 |
| 串流黑屏 | 视频帧未到 | 假设类似 `FirstVideoSample` 相关 | `VideoPlayer.FirstVideoSample` 事件（`Game.cs:1569`）**无 Log** |

**若要把这些假设变成事实，唯一的办法是连上头显实测。** 本轮不做（Non-Conflict 禁止真机操作）。

---

## 7. VDH 检测页的 logcat 实现建议

1. **默认不抓全量**。先跑 `adb shell pidof <pkg>`；有 PID 就 `logcat -d --pid=$PID`，
   没有 PID 再退回 tag 过滤。理由：§5.3 说明全量 88% 是 Oculus 系统噪声。
2. **抓之前先展示过滤规则**，让用户知道会看到什么、并确认 `logcat -c` 的副作用。
3. **输出三段式**：`① 已证实的故障信号`（§4.1 的 F1–F9）→ `② 启动/焦点链`（§4.2 的 S1–S6）
   → `③ 原始尾部 200 行`。第 ③ 段折叠默认收起。
4. **硬编码丢弃**：`monodroid-assembly`、`Unity`、`Xenko`、`EGL_*`、`GL_ERROR`、
   `OculusMirror`/`OVRService`/`[CT]`/`libjingle`（§5.3 的高频噪声）。
5. **黄色 vs 红色**：离线基线下 `AppManagerInternal: Entitlement ... not found` 与
   `FBNSHandler ... Invalid OAuth` **必须黄不红**（§3.5）。
6. **`E VRD` 栈解析**：按 §3.3 的「结束标记行」截断，只显示托管栈那一段；
   从中提取 `VirtualDesktop.VrApp.<Method>` 作为「崩在哪个生命周期」。

---

## 8. `[未验证]` 汇总

| 项 | 需要什么 |
|---|---|
| 「串流成功时 logcat 长什么样」 | 头显连接授权 + 真机成功串流一次 + 抓 logcat。**本工作区 11 份 logcat 无一份是成功串流的**（全是崩溃/启动失败抓取，见 §5.1 计数：0 份出现 VD 侧串流相关行） |
| `--pid=` 与 `-s TAG:LEVEL` 在 HorizonOS 上的可用性与组合 | 头显连接授权 |
| `cmd wifi status` 是否可用（用于替代受限的 `dumpsys wifi`） | 头显连接授权 |
| `dumpsys netstats` 能否证明 UDP 38850 发出 | 头显连接授权 + 头显侧触发一次刷新 |
| §6 全部 7 条假设 | 头显连接授权 + 逐个开关功能并抓日志 |
| `StreamingTab` 码率滑条实际写入的值（`SharedUserSettings.VRBitrateLimit`，默认 `0.36f`，`StreamingTab.cs:1136`）如何从 adb 侧读到 | 需知道 App 设置文件在 `/data/data/<pkg>/` 下的具体路径 —— **本工作区无该路径的证据**，读 `/data/data` 还需要 root 或 `run-as`（debuggable 构建才行；`build_apk_debuggable.py:6` 说明实验线注入了 `android:debuggable="true"`，成品线未确认） |