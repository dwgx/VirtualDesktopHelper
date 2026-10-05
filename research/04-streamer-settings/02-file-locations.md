# 02 · PC 侧 Virtual Desktop Streamer 配置文件真实路径取证

调研日期：2026-10-05
被调研机器：`win32 10.0.26300 x64`，用户 `dwgx1`
本机已安装 Streamer 版本（实测）：**1.34.22.0**

> 全部为**只读**命令。没有启动/关闭 Streamer 进程，没有改任何配置文件。

---

## 0. 结论摘要（先看这个）

| 配置文件 | 真实路径 | 本机存在 | 谁写它 |
|---|---|---|---|
| `StreamerSettings.json` | `C:\ProgramData\Virtual Desktop\StreamerSettings.json` | ✅ 2196 B | `VirtualDesktop.Streamer.exe`（PC 主配置） |
| `GameSettings.json` | `C:\Users\<user>\AppData\Roaming\Virtual Desktop\GameSettings.json` | ✅ 258 B | Streamer（Steam/Oculus 游戏库映射） |
| `BindingSettings.json` | `C:\ProgramData\Virtual Desktop\BindingSettings.json` | ❌ 不存在 | Streamer（仅在热键改动后才落盘） |
| `SharedStreamerSettings.json` | `C:\Users\<user>\AppData\Roaming\Virtual Desktop\SharedStreamerSettings.json` | ❌ 不存在 | Streamer（PC 作为 server 才会写，见 §5） |
| `SharedUserSettings.json` | 同上目录 | ❌ 不存在 | 头显端为主，PC 只是 client |
| `SharedMobileSettings.json` | 同上目录 | ❌ 不存在 | 头显端为主，PC 只是 client |

- **`%LOCALAPPDATA%` 下没有 Virtual Desktop 相关目录**（只有无关的 `VirtualStore`）。
- **没有 `HKCU\Software\Virtual Desktop`**。只有 `HKLM\Software\Virtual Desktop, Inc.`（安装器/服务用，非配置）。
- 注册表 `Run` 键里**没有** Virtual Desktop 自启动项——`StartWithWindows` 是由安装器/服务侧落地的，不是 Streamer 自己写注册表。

---

## 1. 源码里配置读写的定位（一手证据链）

### 1.1 三层设置基类

反编译树：`F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/`
（该树 `Properties/AssemblyInfo.cs:14` 声明 `[assembly: AssemblyVersion("1.34.18.0")]`，即反编译自 **1.34.18**；本机装的是 1.34.22，键集比对见 §6）

| 层 | 文件 | 职责 |
|---|---|---|
| `SettingsBase<T>` | `VirtualDesktop/Core/SettingsBase.cs:8` | 单例 + `INotifyPropertyChanged`。`Default` 属性在**静态构造**里 `Activator.CreateInstance<T>()` 然后 `Initialize()`（`SettingsBase.cs:14-15`） |
| `JsonSettingsBase<T>` | `VirtualDesktop/Core/JsonSettingsBase.cs:10` | 加 `Reload()` / `Reset()`，从 `GetFileName()` 读文件，`JsonConvert.PopulateObject` 填充 |
| `SerializedSettingsBase<T>` | `VirtualDesktop/Core/SerializedSettingsBase.cs:9` | 加 `Save()` + 2 秒防抖保存定时器 |

关键实现细节：

- **防抖保存**：`SerializedSettingsBase.cs:194` `private const int SaveDelay = 2000;`，`StartSaveTimer()` 起 `Timer`（`SerializedSettingsBase.cs:79-110`），每次 `OnPropertyChanged` 都会重置该定时器（`SerializedSettingsBase.cs:163-179`）。
  → **对工具的含义**：改完 JSON 后若 Streamer 还在跑，2 秒内的任何 UI 改动都会把你的写入覆盖掉。**改配置必须先退出 Streamer**。
- **写盘用 `File.WriteAllText`**（`SerializedSettingsBase.cs:35` 与 `:44`，两次 try 是重试）。
- **`Reload()` 对坏 JSON 是静默兜底**：`JsonSettingsBase.cs:126-137`，解析失败 → `PopulateObject("{}", ...)`，不抛。也就是说**文件里写坏一个键，整个文件会被重置成默认值**。

### 1.2 序列化行为（决定了「哪些键会出现在文件里」）

`-\u001E.\u0006`（`-/.83.cs`）是全局 serializer 设置：

```csharp
// decompiled_streamer/VirtualDesktop.Streamer/-/.83.cs:14-23
\u0006.\u0001 = new JsonSerializerSettings {
    DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,   // ← 关键
    Formatting = Formatting.Indented
};
\u0006.\u0002 = new JsonSerializerSettings {
    DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
    Formatting = Formatting.None
};
```

`StreamerSettings` / `GameSettings` / `BindingSettings` 走 `GetSerializerSettings()` 的 `Indented` 版本（`JsonSettingsBase.cs:186`）。
`NetworkSettingsBase` 子类覆盖成 `Formatting.None`（`Net/NetworkSettingsBase.cs:312-325`）。

**`DefaultValueHandling.IgnoreAndPopulate` 的直接后果**（对写工具极其重要）：

- **等于 `[DefaultValue]` 声明的默认值的键，不会被写进 JSON**。
- 所以 JSON 里「键不存在」≡「该键 = 默认值」。**读参数时必须做默认值兜底，不能把「键缺失」当成「键非法」。**
- 这解释了为什么实测的 `StreamerSettings.json` 只有 13 个顶层键，而类里有 45 个属性。

### 1.3 路径拼接

```csharp
// JsonSettingsBase.cs:166（默认实现）
_fileName = Path.Combine(\u0007.\u0002.\u0007(),            // %APPDATA%\Virtual Desktop
                         GetType().Name + ".json");
```

```csharp
// Streamer/StreamerSettings.cs:3367（覆盖）
text = Path.Combine(\u0007.\u0002.\u0006(), "StreamerSettings.json");   // %ProgramData%\Virtual Desktop
// Streamer/BindingSettings.cs:280（覆盖）
text2 = Path.Combine(\u0007.\u0002.\u0006(), "BindingSettings.json");   // %ProgramData%\Virtual Desktop
```

两个路径函数的定义在 `-\u0007.\u0002`（`-/.80.cs`）：

```csharp
// decompiled_streamer/VirtualDesktop.Streamer/-/.80.cs:325
\u0007.\u0002.\u0006 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                   \u0007.\u0002.\u0001());     // ProductName = "Virtual Desktop"

// decompiled_streamer/VirtualDesktop.Streamer/-/.80.cs:383
\u0007.\u0002.\u0007 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   \u0007.\u0002.\u0001());     // ProductName = "Virtual Desktop"
```

`\u0007.\u0002.\u0001()`（`-/.80.cs:7-70`）从 `AssemblyProductAttribute` 读取产品名；本机实测 `ProductName = Virtual Desktop`（见 §2.4），与目录名 `Virtual Desktop` 完全对上。

**注意 `-/.80.cs:317/373` 还有 macOS 分支**（`/Users/Shared`），Windows 上不触发。

---

## 2. 本机实测命令与输出（逐条可复现）

### 2.1 候选目录扫描

```powershell
PS> Get-ChildItem "$env:APPDATA" -Filter "*Virtual*" -Force | Select-Object FullName
PS> Get-ChildItem "$env:LOCALAPPDATA" -Filter "*Virtual*" -Force | Select-Object FullName
PS> Get-ChildItem "$env:PROGRAMDATA" -Filter "*Virtual*" -Force | Select-Object FullName
```

实际输出：

```
FullName
-------
C:\Users\dwgx1\AppData\Roaming\Virtual Desktop
C:\Users\dwgx1\AppData\Roaming\VirtualDesktopHelper
C:\Users\dwgx1\AppData\Roaming\VirtualTracker
---LOCALAPPDATA---
C:\Users\dwgx1\AppData\Local\VirtualStore
---PROGRAMDATA---
C:\ProgramData\Virtual Desktop
```

判读：
- `VirtualDesktopHelper` / `VirtualTracker` 是本仓与 Tracker 的目录，**不是 Streamer 的**。
- `VirtualStore` 是 Windows 应用商店缓存，与 VD 无关。
- 命中两个：`%APPDATA%\Virtual Desktop` 和 `%ProgramData%\Virtual Desktop`。

### 2.2 两个候选目录的完整内容

```powershell
PS> Get-ChildItem "C:\Users\dwgx1\AppData\Roaming\Virtual Desktop" -Recurse -Force |
      Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize -Wrap
```

```
FullName                                                          Length LastWriteTime
--------                                                          ------ -------------
C:\Users\dwgx1\AppData\Roaming\Virtual Desktop\GameSettings.json    258 9/29/2026 10:14:30 PM
```

```powershell
PS> Get-ChildItem "C:\ProgramData\Virtual Desktop" -Recurse -Force |
      Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize -Wrap
```

```
FullName                                            Length LastWriteTime
--------                                            ------ -------------
C:\ProgramData\Virtual Desktop\ServiceLog.txt         1920 9/18/2026  3:37:35 PM
C:\ProgramData\Virtual Desktop\StreamerLog.txt          242 8/27/2026 12:53:56 PM
C:\ProgramData\Virtual Desktop\StreamerSettings.json   2196 10/5/2026  5:32:59 AM
C:\ProgramData\Virtual Desktop\updates.aiu              544 8/29/2026  1:22:41 PM
```

**两个 `.json` 的位置与 §1.3 的推导完全吻合**：
- `StreamerSettings.json` → 覆盖成 `CommonApplicationData` → `%ProgramData%` ✅
- `GameSettings.json` → 用默认 `GetFileName()` → `ApplicationData` → `%APPDATA%` ✅

### 2.3 `StreamerSettings.json` 内容（全文 2196 B，安全可读）

```json
{
  "ServerRotation": 2,
  "ProtectedComputerID": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAsj+LhLxtwkedfUqMLuJKlgQAAAACAAAAA...(DPAPI 密文，已截断)...WUZLNGYr99nb8eHjHtOMj9F+3bNf0z2rl93vfgAWo2daY=",
  "Accounts": {
    "OculusQuest": [
      "AQAAANCMnd8B...(密文)"
    ],
    "Oculus": [
      "AQAAANCMnd8B...(密文)",
      "AQAAANCMnd8B...(密文)",
      "AQAAANCMnd8B...(密文)"
    ]
  },
  "AutoAdjustBitrate": false,
  "ShowPairingRequests": false,
  "ShownH264PlusWarning": true,
  "PreferredCodec": 11,
  "DeviceName": "Meta Quest 3",
  "CodecName": "AV1 10-bit",
  "VideosRootPath": "C:\\Users\\dwgx1\\Videos\\",
  "LastConnectDate": "2026-10-04T00:00:00Z",
  "OpenXRRuntime": 1,
  "MonitorCount": 1,
  "DontWarnApps": [
    "NetworkProfile"
  ]
}
```

顶层键实测清单（13 个）：

```
ServerRotation, ProtectedComputerID, Accounts, AutoAdjustBitrate,
ShowPairingRequests, ShownH264PlusWarning, PreferredCodec, DeviceName,
CodecName, VideosRootPath, LastConnectDate, OpenXRRuntime, MonitorCount,
DontWarnApps
```

关键判读：

- **`ProtectedComputerID` 和 `Accounts.*` 是 DPAPI 密文**（`AQAAANCMnd8B…` 开头，标准 DPAPI blob 头）。`StreamerSettings.cs:3394` 用 `global::\u0091\u0002.\u0002.\u0001()` 读写，即 `ProtectedData`。
  → **绝对不能手改这两类键，写坏等于重置本机身份 + 全部配对关系。**
- **`PreferredCodec: 11`** = `VideoCodec.AV110bit`（`Net/VideoCodec.cs`，`AV110bit` 显式赋值 `= 11`），与 `CodecName: "AV1 10-bit"` 一致，自洽。
- **`OpenXRRuntime: 1`** = `Interfaces/OpenXRRuntime.cs` 的 `SteamVR`。
- **`MonitorCount: 1`** — 说明本机曾用过（值 != 默认 0 才落盘）。
- **`LastConnectDate: 2026-10-04T00:00:00Z`** — 注意**被截断到日**：`StreamerSettings.cs:3384-3388` 的 `Initialize()` 里有 `LastConnectDate = DateTime.Today - TimeSpan.FromDays(55.0)` 的归一化逻辑，所以精度只到天。
- **`DontWarnApps`** 是 `HashSet<string>`（`StreamerSettings.cs:3140`），存的是 `AppError.ID`，本机被勾掉的是 `NetworkProfile`。
- **`AutoAdjustBitrate: false` 落盘了**，因为它的 `[DefaultValue(true)]`（`StreamerSettings.cs:1733`）与当前值不同 → 反证 `IgnoreAndPopulate` 生效。
- `EnableAQ: true` 没出现，因为它等于 `[DefaultValue(true)]` → 被忽略。**这是「键缺失 = 默认值」的第一手实证。**

### 2.4 安装版本与 ProductName

```powershell
PS> (Get-Item "C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe").VersionInfo |
      Select-Object FileVersion,ProductVersion,ProductName,CompanyName | Format-List
```

```
FileVersion    : 1.34.22.0
ProductVersion : 1.34.22.0
ProductName    : Virtual Desktop
CompanyName    : Virtual Desktop, Inc.
```

`ProductName = Virtual Desktop` → 反推目录名正是 `%APPDATA%\Virtual Desktop` / `%ProgramData%\Virtual Desktop`。这条闭环了 §1.3 里从 `AssemblyProductAttribute` 取名的推导。

### 2.5 注册表（排除项）

```powershell
PS> Get-ChildItem "HKCU:\Software" | Where-Object {$_.PSChildName -like "*Virtual*"}
PS> Get-ChildItem "HKLM:\Software" | Where-Object {$_.PSChildName -like "*Virtual*"}

Name
----
HKEY_LOCAL_MACHINE\Software\Virtual Desktop, Inc.
```

```powershell
PS> Get-ChildItem "HKLM:\Software\Virtual Desktop, Inc." -Recurse
Name
----
HKEY_LOCAL_MACHINE\Software\Virtual Desktop, Inc.\Virtual Desktop Service
HKEY_LOCAL_MACHINE\Software\Virtual Desktop, Inc.\Virtual Desktop Streamer
```

```powershell
PS> Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" | Select-Object *Virtual*
PS> Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" | Select-Object *Virtual*
PS> Get-ScheduledTask | Where-Object {$_.TaskName -like "*Virtual*"}

（以上三条均无输出）
```

判读：
- **`HKCU\Software\Virtual Desktop` 不存在** → 配置不在注册表。
- `HKLM\...\Virtual Desktop Service` / `...\Virtual Desktop Streamer` 是安装器给服务/驱动用的注册表项（MSI 服务注册 + 驱动），**不是用户配置**。
- `Run` 键和计划任务里都没有 VD → `StartWithWindows` 这个 JSON 键由别处落地（安装器写入的启动项，本次查询时本机未开该功能，与 JSON 里也没有 `StartWithWindows` 键自洽）。

---

## 3. `GameSettings.json` 内容

```json
{
  "OculusExperienceNames": {},
  "SteamProductInfos": {
    "438100": {
      "OpenVRSupport": true,
      "OpenXRSupport": true,
      "Executable": "launch.exe",
      "LaunchType": "vr"
    }
  },
  "ShippingExe": {
    "438100": null
  }
}
```

`438100` = Half-Life 2。这是 Streamer 的游戏启动映射缓存（`Streamer/GameSettings.cs`，三个 `ConcurrentDictionary`），**与串流质量无关**。

---

## 4. 对 VDH 工具的直接可用结论

1. **只读参数**：读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json`，**只解析非密文键**。判定密文的规则很简单——值以 `AQAAANCMnd8B` 开头的是 DPAPI blob，跳过。
2. **写参数的安全流程**：
   - 先确认 `VirtualDesktop.Streamer.exe` **没有运行**（否则 2 秒防抖保存会覆盖你的写入，见 §1.1）；
   - 备份原文件；
   - **绝不触碰** `ProtectedComputerID` 和 `Accounts`（DPAPI + 身份/配对）；
   - 只改「不等于 `[DefaultValue]` 的值」——改了等于默认值的话，Streamer 下次保存时会把该键**删掉**（这本身不是错误，但会让「改完没生效」看起来像 bug）。
3. **「键缺失」的语义**：等价于 `[DefaultValue]` 声明值，不是「无效」。UI 显示时必须先做默认值合并。
4. **坏 JSON 的后果**：`Reload()` 静默吞异常并重置全部默认值（`JsonSettingsBase.cs:128-137`）→ 写文件必须先做 JSON 语法校验，否则用户会遇到「我改的设置全没了」。

---

## 5. `Shared*.json` 为什么不存在（不是 bug）

三个 `NetworkSettingsBase` 子类的角色在 `Streamer/ConnectionManager.cs:203-205` 里定死：

```csharp
SettingsBase<SharedUserSettings>.Default.InitializeAsClient(...);      // PC 是 client
SettingsBase<SharedMobileSettings>.Default.InitializeAsClient(...);    // PC 是 client
SettingsBase<SharedStreamerSettings>.Default.InitializeAsServer(...);  // PC 是 server
```

而 `NetworkSettingsBase.Reload()`（`Net/NetworkSettingsBase.cs:292-310`）第一行就是 `if (!IsServer) return;`，
`StartSaveTimer()`（`NetworkSettingsBase.cs:276-290`）也是 `if (IsServer) base.StartSaveTimer();`。

所以：
- `SharedUserSettings` / `SharedMobileSettings` 在 PC 上**永不落盘**——它们由头显端持有，PC 通过 NetMessage 收发（`Net/NetworkSettingsBase.cs:414-418` 走 `PropertyChangeMessageType` 增量同步）。
- `SharedStreamerSettings` 是 PC 侧 server，会落到 `%APPDATA%\Virtual Desktop\SharedStreamerSettings.json`，但只有**发生过属性变化**（触发 2 秒防抖保存）才会有文件。本机没有该文件 → 从未有过变化。

**对工具的含义**：码率/分辨率/刷新率/VR 画质这些**串流质量参数在 PC 上不是本地配置，而是头显端配置**。VDH 想「显示参数」必须连上头显或从 Streamer 内存里读，不能只读 PC 的 JSON。

---

## 6. 反编译源（1.34.18）vs 本机安装（1.34.22）的键集比对

在 1.34.22 的 `VirtualDesktop.Streamer.exe` 原始字节里，按 UTF-16LE（.NET #US 堆）和 UTF-8（#Strings 堆）两种编码搜索 1.34.18 源码里提取的 38 个 `StreamerSettings` 键名：

```
$ python -c "..."
checked 38 missing []
```

**38/38 全部命中，无缺失。** 详见 `03-ui-to-key-map.md` §4 的版本差异比对。

---

## 7. 引用的源码位置清单

| 事实 | 位置 |
|---|---|
| `SettingsBase<T>.Default` 单例 + 静态构造初始化 | `VirtualDesktop/Core/SettingsBase.cs:8,14-15,24` |
| `Reload()` 读文件 + 坏 JSON 静默兜底 | `VirtualDesktop/Core/JsonSettingsBase.cs:103-156`（兜底在 126-137） |
| 默认 `GetFileName()` = `%APPDATA%\<ProductName>\<TypeName>.json` | `VirtualDesktop/Core/JsonSettingsBase.cs:158-179`（拼接语句在 166） |
| `Save()` = `File.WriteAllText` | `VirtualDesktop/Core/SerializedSettingsBase.cs:32-36` |
| 2 秒防抖保存 | `VirtualDesktop/Core/SerializedSettingsBase.cs:79-110,163-179,194` |
| `DefaultValueHandling.IgnoreAndPopulate` | `-/.83.cs:14-23` |
| `CommonApplicationData` = `%ProgramData%` | `-/.80.cs:325` |
| `ApplicationData` = `%APPDATA%` | `-/.80.cs:383` |
| ProductName 从 `AssemblyProductAttribute` 读 | `-/.80.cs:7-70` |
| `StreamerSettings.json` 路径覆盖 | `VirtualDesktop/Streamer/StreamerSettings.cs:3357-3368`（拼接在 3367） |
| `BindingSettings.json` 路径覆盖 | `VirtualDesktop/Streamer/BindingSettings.cs:263-283`（拼接在 280） |
| DPAPI 保护 computer ID / 账号 | `VirtualDesktop/Streamer/StreamerSettings.cs:3394`（`Initialize()` 里的加解密分支） |
| `LastConnectDate` 归一化到「55 天前的今天」 | `VirtualDesktop/Streamer/StreamerSettings.cs:3384-3388` |
| 三个 Shared 设置的 client/server 角色 | `VirtualDesktop/Streamer/ConnectionManager.cs:203-205` |
| `NetworkSettingsBase` 只在 IsServer 时读写盘 | `VirtualDesktop/Net/NetworkSettingsBase.cs:276-310` |
| 枚举 `VideoCodec`（`AV110bit = 11`） | `VirtualDesktop/Net/VideoCodec.cs:6-18` |
| 枚举 `OpenXRRuntime` | `VirtualDesktop/Interfaces/OpenXRRuntime.cs:5-10` |
| 本机版本 1.34.22.0 / ProductName | `C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe` FileVersionInfo |
| 全部 23 个可勾选的 `AppError.ID` | `-/.3.cs` … `-/.25.cs`（逐文件 `base.ID = "…"`） |