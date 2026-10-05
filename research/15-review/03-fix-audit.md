# 03 — 修复面审计：工具说「修好了」，它核对过吗

> 范围：`HeadsetProbe.cs` / `ParameterCatalog.cs` + 配置写入路径 / `Core/Health/Fixes.cs` + `FixRow` /
> `parameters.json`（111 条）/ `App.xaml.cs` 的 `--apply` `--set-param` `--quit-streamer` / `docs/` 与 `README.md`。
>
> 判据只有一条：**这个修复执行的命令、它回读的东西、它打印的话、它承诺的回滚，四者是否互相吻合。**
>
> 已在本会话修掉的十类（`gpu-throttle` 功耗墙、`av` 假通过、`lan-reach` 陈旧邻居、`--deep` 耗时、
> `docs/checks.md` 18/19、`session-stale`、`net-loss`、`proc-tuner`、`productState` 位掩码、
> `HeadsetProbe` 串号切分、对比度）**不复述**。`01-retrospective.md` 的 11 处存活实例也不重复，
> 只在它直接波及修复面时才引用（§7 末尾列了哪些）。
>
> **本文只写报告，没有改动任何 `src/` 文件。**

## 0. 先说结论

七个注册修复里，**回读纪律分三档**，而且这个分档在 UI 上完全看不出来——三档在 `HealthView.xaml` 里
长得一模一样（一个「执行」按钮、一行 `State`）。

| 档 | 修复 | 读回什么 |
| --- | --- | --- |
| 真回读（2） | `enable-pairing-requests`、`headset-grant` | 重新打开文件 / 逐条看退出码 |
| 半回读（1） | `svc-start` | 查了状态，但**查的是另一个名字的服务**，且这段是死代码（F2） |
| 只看退出码（3） | `fw-restore-vd`、`svc-repair`、`streamer-launch` | `netsh`/`msiexec`/`Start-Process` 的退出码 |

**外加两个更硬的问题**：`--set-param` 对**每一个 bool 键**都会报「写入成功但回读不一致」并返回 6
（F1，已实跑复现）；`fw-restore-vd` 在本机**永远失败**，因为它不给自己要的那个权限（F3，已实跑）。

---

## 1. 高危

### F1 `--set-param` 对所有 bool 键必然误报失败，退出码 6 —— 「回读确认」这段代码本身是坏的

**位置**：`src/VdHelper/App.xaml.cs:434-445`

```csharp
var after = Core.Config.StreamerSettings.Load();
var expected = value.ToString();                 // JsonElement.ToString()
var actual = after.GetRaw(key);                  // == GetRawText()
var matches = string.Equals(actual?.Trim('"'), expected.Trim('"'), StringComparison.Ordinal);
```

**实测**（本轮在临时目录用一个独立 .NET 10 小程序逐字复刻 `:438-446` 这三行）：

```
--set-param ShowPairingRequests true
   on disk      : true
   expected str : True
   matches      : False   => exit 6
   msg if fail  : 写入返回成功，但回读确认 ShowPairingRequests 是 true，不是 True。备份已保留，未回滚。

--set-param AllowRemoteConnections false
   on disk      : false
   expected str : False
   matches      : False   => exit 6

--set-param MonitorCount 3          ==> match: True   => exit 0
--set-param DeviceName "Rig"        ==> match: True   => exit 0
```

**根因**：`JsonElement.ToString()` 对 `JsonValueKind.True` 返回 .NET 的 `Boolean.ToString()`，
即 **`"True"` / `"False"`**（首字母大写）；而 `GetRawText()` 返回 JSON 字面量 **`true` / `false`**。
`StringComparison.Ordinal` 又大小写敏感，于是**每一个布尔键的每一次写入都被判为不一致**。

**为什么这是最该修的一条**：

1. 写入**其实成功了**——文件里确实写进了 `true`。但用户看到的是「写入返回成功，但回读确认
   ShowPairingRequests 是 true，不是 True。备份已保留，未回滚。」
2. 这条消息在**字面上就是错的**：磁盘上的值是对的。工具正在告诉用户「你要写的值没写上」，
   而它自己打印的 `actual` 恰恰证明了写上了。
3. 退出码 6 = 写失败。`README.md:117` 明确把 6 定义为「写失败」。**按文档分流脚本会把一次
   成功的写入读成失败**，用户很可能因此去用 `.vdhelper.bak` 回滚——把一次正确的改动撤掉。
4. 布尔键是这套配置里**最常被想改的一批**。`parameters.json` 里 36 个 CLI 可写键中 18 个是 bool，
   而且 `ShowPairingRequests`、`AllowRemoteConnections`、`StartWithWindows` 三个都在
   `caution: true` 名单里——也就是最需要「写成功」的那几个。

**影响面**：README:128 给的示例命令 `VdHelper.exe --set-param ShowPairingRequests true`
**今天一定是 exit 6**。而这条示例在 `notes/2026-10-06-config-write-path-verified.md` 里
被列为「配置写入路径已验证」的成果——**那份验证只覆盖了 `StreamerConfigWriter.Write` 本身，
没有覆盖 `--set-param` 的回读比较**。两份笔记都诚实，但它们验证的不是同一条代码。

**修法**（三行）：比较前把两边按 JSON 语义归一，不要拿 .NET 的 `ToString()` 当 JSON 序列化：

```csharp
var expected = value.ValueKind is JsonValueKind.True or JsonValueKind.False
    ? (value.GetBoolean() ? "true" : "false")
    : value.ToString();
```

更稳的做法是别比字符串：比较 `GetRawText()` 与 `value.GetRawText()`。

**顺带**：`App.xaml.cs:437-440` 在不匹配时只打印、不回滚，然后 `return 6`。既然写已经成功，
应当明确告诉用户「写成功了，但工具的核对逻辑有 bug，值可用」，否则用户会以为改动没生效。

### F2 `svc-start` 启动并回读的是**两个不同的东西**，而本机那个名字不存在

**位置**：`src/VdHelper/Core/Health/Fixes.cs:67,69,73,77-82`

```csharp
// :73  执行的
RunPsAsync("Start-Service -Name 'VirtualDesktop.Service'", ...)
// :77-78  回读的
PowerShellRunner.RunAsync("(Get-Service | Where-Object { $_.Name -like 'VirtualDesktop*' } | Select-Object -First 1).Status", ...)
// :80  判定
state.StdOut.Contains("Running", StringComparison.OrdinalIgnoreCase)
```

**实测**（本轮只读命令）：

```
Get-Service | Where-Object { $_.Name -like 'VirtualDesktop*' }
Name                        Status  StartType
----                        ------  ---------
VirtualDesktop.Service.exe  Running Automatic

Get-Service -Name 'VirtualDesktop.Service'
Get-Service : Cannot find any service with service name 'VirtualDesktop.Service'.
(匹配结果 -eq $null) : True
```

**本机的服务真名是 `VirtualDesktop.Service.exe`，带 `.exe`。** 而 `:73` 的 `Start-Service` 用的
是不带 `.exe` 的名字——**PowerShell 的 `-Name` 是精确匹配，不是通配**（上面 `Get-Service -Name`
实测报错证实）。所以 `:73` 必然抛 `NoServiceFoundForGivenName`，退出码 1，
`RunPsAsync` 返回 `Success=false`，`:76` 直接短路返回失败。

**这里有两层问题，第二层更严重：**

- **F2a 表里写着一个本机不存在的服务名。** `--apply svc-start` 在这台机器上永远失败。
- **F2b 回读那段是死代码，且它一旦运行也是错的。** 因为 `:73` 先失败，`:77-82`
  这段被专门写来防止「谎报成功」的代码**从加入至今一次都没执行过**。
  而它的判据本身也不对：`-like 'VirtualDesktop*'` + `Select -First 1`
  取的是**任意一个** VirtualDesktop 前缀的服务，`Contains("Running")` 只要**那一个**在跑就报成功——
  它验证的不是 `:73` 刚启动的那个服务。

`Fixes.cs:67` 的 `What` 还写着「（**并按需** Set-Service -StartupType Automatic）」，
`:68` 的 Backup 写着「未改动启动类型」。**文案说了一件代码没做的事**——`:73` 只调 `Start-Service`，
`Set-Service` 从未出现在任何执行路径里。

**修法**：名字从 `HealthChecks.PsService`（`:27-28`，已用 `-like 'VirtualDesktop*'` 这个正确写法）
取得或直接用 `VirtualDesktop.Service.exe`，不要在 `Fixes.cs` 里再手写一份字面量；
回读要**锁定同一个名字**（`Get-Service -Name <刚启动的那个> | Select -ExpandProperty Status`），
不要用通配 + First 1；顺手删掉 `:67` 里那句「并按需 Set-Service」，或真的实现它。

### F3 `fw-restore-vd` 在本机永远失败：它不给自己要的那个权限

**位置**：`src/VdHelper/Core/Health/Fixes.cs:57`

```csharp
ct => RunPsAsync(add, "Virtual Desktop Streamer", ct),   // add = netsh advfirewall firewall add rule ...
NeedsElevation: true),
```

**实测**（本轮实跑两次，结果相同）：

```
netsh advfirewall firewall add rule name='ZZProbeDupTest' dir=in action=allow ...
The requested operation requires elevation (Run as administrator).
exit=1
```

**`RunPsAsync`（`:326-332`）走 `PowerShellRunner.RunAsync`，而这个 runner 结构上不提升权限**——
`PowerShellRunner.cs:54-58` 自己写着：「This runner never elevates, and there is deliberately no flag
that claims to」。`:58` 的 `NeedsElevation: true` **纯粹是给界面看的标签**，
它不会让 `:57` 的执行体获得管理员权限——UI 里 `HealthViewModel.cs:20-22` 据此显示
「会弹 UAC，请点「是」——本机实测这一等可能要 1 分半」。

**所以 `fw-restore-vd` 的实际行为是**：显示一句「会弹 UAC」→ 不弹 → netsh 拒绝 → 报「可能需要管理员权限」。
**同一条路径在同文件里已经有正确写法**：`ElevatedAsync`（`:215`），`streamer-restart` 用它，
带 UAC 超时与 marker 文件回读。`fw-restore-vd` 没接上去。

**同一类问题的还有 `svc-repair`（`:317`）**，只是它凑巧能用：`Start-Process msiexec -Verb RunAs -Wait`
自己带了 `RunAs`。但**它的成功判据是坏的**，见 F4。

**「备份」与「回滚」两栏也不匹配它实际做的事**（`:54`/`:55`）：

- `Backup` 写「添加前先导出：`netsh advfirewall firewall export <备份文件>`；同名规则若已存在需先删除。」
  —— **代码两件事都没做**。没有 export，没有先删。而 `Backup` 栏在 UI 上是当
  「工具已经备份了什么」显示的（`HealthView.xaml:177` 的前缀字面就是「备份：」），
  不是「你应该先做什么」。
  尤其「同名规则需先删除」——`FirewallPairChecks.cs:180-182` 的指引也要求用户先删，
  而这个 fix 自己不删。用户按工具的指引做一遍，**可能得到两条同名规则**
  （`netsh add rule` 不去重：实测第二次调用同样返回「要求提权」，即去重与否由 netsh 决定，
  不是由工具保证的 `[未验证]`）。
- `Rollback` 是 `netsh ... delete rule name="Virtual Desktop Streamer"` —— 这会删掉**所有**同名规则，
  包括用户原本就有的、自己建的。

**修法**：`:57` 改走 `ElevatedAsync(add, ct)`（和 `:126` 一样），并在 apply 之后加回读
（`Get-NetFirewallRule -DisplayName 'Virtual Desktop Streamer'` 确认存在且 Program 指向真实 exe）。
`Backup` 栏改成工具**实际**做过的事。`Rollback` 改成只删自己刚建的那一条；
netsh 做不到就如实说「会删掉全部同名规则」。

---

## 2. 中危

### F4 `svc-repair` 的成功判据是外层 PowerShell 的退出码，而 `Start-Process` 不传递子进程退出码

**位置**：`src/VdHelper/Core/Health/Fixes.cs:317-321`

```csharp
var script = "Start-Process msiexec -ArgumentList '/i \"" + msi + "\" /qn' -Verb RunAs -Wait";
var r = await PowerShellRunner.RunAsync(script, ct: ct);
return r.Ok ? new FixResult(true, "Virtual Desktop 服务已处理", r.Combined) : ...
```

**实测**（本轮）：

```
Start-Process cmd -ArgumentList '/c exit 5' -Wait -NoNewWindow; Write-Host ('LASTEXITCODE=' + …)
LASTEXITCODE=          <-- 空
outer_exit=0           <-- 子进程 exit 5，外层照样 0
```

**`Start-Process -Wait` 不传播子进程退出码**（这是 PowerShell 自身行为，与 msiexec 无关），
所以 `r.Ok`（`ExitCode == 0`）**只说明「外层 powershell 自己跑完了」，与 msiexec 的成败无关**。
msiexec 返回 1603（致命错误）或 1618（另一个安装正在进行）时，这里照样 `FixResult(true, ...)`。

这正是同文件 `:104-108` 注释里已经学到的那个坑：

> that inner call does NOT propagate the child's exit code, so the inner script writes its own
> result to a temp file and we read that instead

**`ElevatedAsync` 为此专门实现了 marker 文件机制，而 `svc-repair` 绕过了它**，
自己拼一个 `Start-Process -Verb RunAs` 就宣布成功。同一份知识，同一个文件，隔了 200 行丢掉。

**还有一句**：`FixResult(true, "Virtual Desktop 服务已处理")` 里的「已处理」是含糊词，
但 `HealthViewModel.cs:40` 会把它渲染成状态 `已执行`，`--apply` 会打印「结果：成功」。
**一个 1603 失败的重装，在 UI 上是「已执行」。**

**修法**：`svc-repair` 改走 `ElevatedAsync`（它已能提权、能回读、能处理 UAC 超时），
在 marker 里记 msiexec 的真实退出码；成功后再回读一次
`Get-Service -Name 'VirtualDesktop.Service.exe'` 的 `Status`，确认服务真起来了。

### F5 `--apply <id>` 会执行**所有**同名修复，而不是用户点名的那一个

**位置**：`src/VdHelper/App.xaml.cs:305-334`

```csharp
foreach (var result in report.Results)
foreach (var fix in result.Fixes)
{
    if (!wantedList && !fix.Id.Equals(wanted, ...)) continue;
    matched++;
    ...
    var outcome = await fix.Apply(CancellationToken.None);   // 没有 break
    ...
}
```

**循环体里没有 `break`，也没有「同 id 只取第一个」的判断。** 而 `fw-restore-vd` 这个 id
**被 5 处产出**（`FirewallPairChecks.cs:128,159,171,183` + `HealthChecks.cs:356`）。
前四处是同一个检查 `FirewallRulePairCheck` 的互斥分支（每个分支都 `return`），
但第 5 处 `fw-vd`（`HealthChecks.cs:349-357`）是**另一个独立检查**，判据是
「有含 Virtual Desktop 且 Allow 的规则」，**与 `fw-pair` 可以同时失败**。
当规则存在但 Program 指向失效路径时，两个都 Block，
`--apply fw-restore-vd` 就会把 netsh 跑两遍。

`matched` 会计成 2，循环跑到底，**用户点名一个 id，工具改了两处系统状态**，
而屏幕上只打印两遍几乎一样的「执行 fw-restore-vd」。

**`--apply --list` 也有同一个毛病**：`fw-restore-vd` 会被列多次（`:311-322`），
每次都告诉用户「执行：VdHelper.exe --apply fw-restore-vd」，
看不出它们其实来自不同检查、不同触发条件。

**修法**：加 `if (matched > 1) break;`，并在 `--list` 里按 id 去重、
把「出自」那一行改成「出自：a、b、c（同一修复，多个检查命中）」。

### F6 `--apply` 的退出码不区分「执行了」和「执行了并核对过」

**位置**：`src/VdHelper/App.xaml.cs:336`（`return 0`）、`README.md:114`

`README.md:114` 写的是：`0` 成功 / `6` 失败 / `9` 没有匹配的修复项。

但 `FixResult` 只有 `bool Success`（`Core/Model/Health.cs:31`），**没有「是否核对过」这一维**。
于是这三种情况在退出码上是同一个 `0`：

| 情况 | 实际依据 | 退出码 |
| --- | --- | --- |
| `enable-pairing-requests` | 重新读了文件，确认值是 `True` | 0 |
| `fw-restore-vd` | 只看 netsh 退出码，且本机必然失败 | 0 / 6 |
| `streamer-launch` | 只看 `Start-Process` 的退出码 | 0 |

**`streamer-launch` 尤其值得单说**（`Fixes.cs:270-280`）：`RunPsAsync` 只看外层退出码，
**没有像 `RestartStreamerVerifiedAsync`（`:111-140`）那样去比对 PID**。
`Start-Process` 返回 0 只说明「进程被创建了」——而这个工具的整个存在理由就是
「服务在跑 ≠ Streamer 起得来」（`README.md:49-51`、本仓库最著名的那个故障）。
**一个专门用来启动 Streamer 的修复，不检查 Streamer 起来没有。**
它的兄弟 `streamer-restart` 做了 PID 比对（`:132-138`），同一个文件、同一个类、两种纪律。

**修法**：`FixResult` 加一个 `Verified` 字段；`--apply` 对「执行了但没核对」的修复
返回一个新的码（例如 10）并在 README 表里写清楚；或者最低限度，让 `streamer-launch`
也做一次「起来了吗」的回读（等 1-2 秒查 `Process.GetProcessesByName`），与 `streamer-restart` 对齐。

### F7 `headset-grant` 的回滚承诺覆盖了它没做过的范围

**位置**：`src/VdHelper/Core/Adb/HeadsetProbe.cs:266-274`

```csharp
"仅授予权限，不改其它设置；回滚用 pm revoke 逐项撤销：" + string.Join(" ;; ", perms),
```

这条 `Backup` 的前半句是**准确的**（这个 fix 真的只跑 `pm grant`，逐行看过 `:290-305`），
和本会话之前那个空执行版本相比已经是**实质改进**——现在它逐条跑、逐条看退出码、
成功失败分别计数（`:296-303`），失败时还会提醒 `com.oculus.*` / `horizonos.*`
只有一个存在是正常的。

剩下一处**范围大于实际**：`:288` 的 `perms` 来自 `missing`，而 `missing` 是
`HeadsetProbe.cs:139` 的 `granted.Where(g => !g.Granted)`——**跨所有已装包去重后的并集**。
但 `:294-295` 的执行体是 `foreach (var pkg in pkgs) foreach (var perm in perms)`，
即**笛卡尔积**。当用户装了 2 个客户端包、缺的是不同的两条权限时，
实际执行了 4 次 `pm grant`（其中 2 次是「本来就已授予」的 no-op），
而 `Rollback` 里的 `pm revoke` 列表只列 2 条权限名、**没有列是哪几个包**——
用户照着回滚，会 revoke 掉一个本来就被正确授予的权限。

另外 `:288` 的 `AdbLocator.Find()` 是**重新找一次 adb**，而不是复用 `HeadsetProbe`
构造时拿到的那个 `AdbClient`（`:12` 的 `adb.AdbPath`）。两次查找之间如果 `adbPath` 配置被改，
执行的会是另一份 adb。

**修法**：`Rollback` 用和执行体同构的展开（`pkgs × perms` 全列出），
并且优先复用 `ProbeAsync` 传进来的那个 adb 实例（把它一起捕获进闭包）。

---

## 3. 低危 / 记账

### F8 `parameters.json` 有 2 个重复 key，UI 会把它们显示成两行无法区分的条目

**实测**（`python` 直接读 `src/VdHelper/Resources/parameters.json`）：

```
total 111 / distinct 109
keys dup: ['AutoAdjustBitrate', 'VRFramerate']

#22  AutoAdjustBitrate  type bool              table 0   readOnly false
#99  AutoAdjustBitrate  type bool              table 5   readOnly false
#59  VRFramerate        type "enum Framerate"  table 3   readOnly false
#90  VRFramerate        type int               table 4   readOnly false
```

这不是数据重复，是**故意收录了两个不同表里的同名键**（#90 的 `range` 栏自己写着
「[JsonIgnore]（注意：与 #60 的枚举版同名不同类）」）。但代价是：

- `ParametersViewModel.Load()`（`:150-159`）按条目建行，**用户看到两行都叫 `AutoAdjustBitrate`**，
  一行显示「true」（PC 侧真值），一行显示「本机不落盘（由头显决定）」。除了那行小字没法区分。
- `ParameterCatalog.All.Count` 是 **111**，而 `App.xaml.cs:380` 的错误文案
  「不是调研表里的 **111** 个键之一」是**手写常量**（`grep` 确认 `App.xaml.cs:380` 是唯一一处），
  `docs/checks.md:53` 的「111 个」则是 `export-checks.ps1:60` **现算**的。
  两处口径一致纯属巧合，任何一次加键都会让 `:380` 悄悄过期——
  **这正是本项目反复修的那类漂移**。
- 同一个 key 在 `--set-param` 里走 `FirstOrDefault`（`App.xaml.cs:374-375`）拿**第一条**。
  `#22 AutoAdjustBitrate` 是 table 0（可写），`#99` 是 table 5（不可写）。
  现在数组顺序恰好让可写的在前、CLI 拒绝对的那条走 `:387` 的 `!info.LivesOnPc` 分支——
  **这是靠数组顺序维持的正确性，不是靠逻辑**。

**修法**：`key` 加表前缀，或在 `ParameterInfo` 上加 `TableLabel` 并让 `ParameterRow` 显示它；
`App.xaml.cs:380` 改成 `$"{ParameterCatalog.All.Count} 个键"`。

### F9 CLI 能写的键比参数页多 18 个，且**不做任何类型/范围校验**

实测对比两个写入面的准入条件：

| 写入面 | 准入 | 实际可写键数 |
| --- | --- | --- |
| 参数页「切换」`ParametersViewModel.cs:24` | `LivesOnPc && !ReadOnly && IsBool` | **18**（只有 bool） |
| `--set-param` `App.xaml.cs:374-392` | 在目录里 && `!ReadOnly` | **36** |

多出来的 18 个里有几个是危险的：

- `VideosRootPath` / `ScreenshotsRootPath`（目录路径）、`DeviceName` / `CodecName`（任意字符串）——
  `--set-param VideosRootPath "C:\Windows\System32"` 会被照写。
- `MonitorCount`、`SelectedTab`、`PreferredCodec`、`GamepadEmulation`、`OpenXRRuntime`、
  `AudioStreaming` —— 这些是 **enum**，目录里写了合法值列表（`range` 栏），
  但 `--set-param` **一个都不校验**，写 `999` 也照写。
- `HorizontalFovTangent` 等 5 个 float，目录里写着「UI 滑块 0.4–1.0」，CLI 同样不查。
- `LastConnectDate` 类型是 `DateTime`，写进去 Streamer 解析不了就会用默认值。

`ParameterInfo` 带着 `Type`、`Range`、`Default`、`Caution` 四个字段，
**`--set-param` 一个都没用**（`App.xaml.cs:362-456` 全文只用 `Key` / `ReadOnly` / `Secret` / `LivesOnPc`）。
参数页也没用——它只在 `Badges` 里显示 `Caution`（`ParametersViewModel.cs:31`），
**8 个 `caution: true` 的键在写入前不给任何警告**（`AllowRemoteConnections`、`EncryptLocalTraffic`、
`StartWithWindows`、`UseVirtualAudioDriver`、`VoiceMeeterMode`、`ShowPairingRequests`、
`DeviceName`、`CodecName`）。

这不属于「说谎」，属于「守门人缺席」。但它让 `--set-param` 成为一把
「目录说有约束、代码不执行」的空手套——**目录的 `range` 栏成了没有读者的文档**。

**修法**：`--set-param` 在写入前按 `Type`/`Range` 校验并拒绝越界值；
`caution: true` 的键在两个写入面都先打一行警告。

### F10 只读键的拒绝理由现在是准的 —— 记一笔，确认没退化

`App.xaml.cs:385-389`：

```csharp
Console.WriteLine(info.Secret
    ? $"{key} 是 DPAPI 密文键，改了会清空配对，拒绝写入。"
    : $"{key} 是只读键：Streamer 自己不持久化它（源码上带 [JsonIgnore]），本机改不了。");
```

实测目录：19 个 `readOnly`，其中 18 个 `secret`，**非密文只读键只有 `HotKeysEnabled` 一个**
（`table: 1`，`effect` 栏自己写着「Streamer 不持久化它（源码上带 [JsonIgnore]）」）。
**代码这句话对这个唯一的例子是准确的。** 这条不用改。

`App.xaml.cs:387` 的 `!info.LivesOnPc` 分支（「不在 PC 落盘，由头显决定」）也是对的——
表 1-5 共 57 个键，table 0 只有 54 个。

### F11 文档侧：一处已经过期的总结

- `README.md:114` / `:116` / `:117` 三行的退出码表与代码一致（`0/6/9`、`0/6`、`0/2/7/8/6`），
  **已核对无误**。但如 F6 所述，这张表**没有体现「是否核对过」**，
  属于**表本身正确、表达的信息不够**。
- `docs/RELEASE-0.2.0.md:31` 有一行**已过期**的总结：
  「写入 | 四条写入路径都只看函数返回 | 全部回读确认后才说成功」。
  今天不是四条路径都回读了：`fw-restore-vd`（`Fixes.cs:57`）与 `svc-repair`（`:317`）
  只看退出码，`streamer-launch`（`:279`）也是。**这条总结在 F4/F6 修好之前是假的。**
- `docs/RELEASE-0.3.0.md:11-16` 把 `fw-restore-vd` / `svc-start` / `disable-adapter`
  列为「静默失败」并宣布已修。**「已修」的部分只是加了 `NeedsElevation` 标签**（F3），
  标签不改变执行体拿不到权限的事实。这三行现在读起来像是权限问题解决了。
- `docs/checks.md:53`「111 个，含 19 个只读，其中 18 个是配对密文」——
  **与实测完全一致**（我数过 `parameters.json`），且已改成由 `export-checks.ps1:60` 现算。
  **这条是好的。**

---

## 4. 七个注册的修复：逐条可追溯性

| # | fix id | 跑什么 | 回读什么 | 打印什么 | 回滚字符串说了什么 | 核对过吗 |
|---|---|---|---|---|---|---|
| 1 | `streamer-launch`<br>`Fixes.cs:270-280` | `Start-Process '<ResolveStreamerExe()>'` | **无** | 「Virtual Desktop Streamer **已处理**」 | 「Stop-Process -Name 'VirtualDesktop.Streamer' -Force」 | ❌ 只看退出码（F6） |
| 2 | `enable-pairing-requests`<br>`WindowsStateChecks.cs:102-136` | `StreamerConfigWriter.Write(ShowPairingRequests=true)`，先拒绝 Streamer 在跑 | **有**：重开文件读 `ShowPairingRequests` 是否 `True`（`:126-131`） | 「已确认磁盘上 ShowPairingRequests=true，备份在 …」；不一致时说「请用备份还原」 | 「把备份文件复制回 StreamerSettings.json」 | ✅ **真回读** |
| 3 | `svc-start`<br>`Fixes.cs:62-85` | `Start-Service -Name 'VirtualDesktop.Service'`（**本机无此名**，F2） | 声称重查状态，**实际是死代码**（`:77-82` 永不执行） | 「启动命令未成功：…」 | 「Stop-Service -Name 'VirtualDesktop.Service'」（**同样错名**） | ⚠️ 有回读代码，跑不到 + 判据错（F2） |
| 4 | `fw-restore-vd`<br>`Fixes.cs:41-60` | `netsh advfirewall firewall add rule …`（**不提权**，本机必然失败，F3） | **无** | 「Virtual Desktop Streamer 已处理」 | 「netsh … delete rule name="Virtual Desktop Streamer"」——**删掉全部同名规则**；Backup 栏承诺的 export 与先删，**代码都没做** | ❌ 只看退出码（F3） |
| 5 | `svc-repair`<br>`Fixes.cs:286-299`, `:313-322` | `Start-Process msiexec /i … /qn -Verb RunAs -Wait` | **无**；且退出码不传播子进程（F4） | 「Virtual Desktop 服务**已处理**」→ UI 显示「已执行」 | 「msiexec /i … /qn（重装即恢复默认账户绑定）」——**这是重装，不是撤销** | ❌ 只看外层退出码（F4） |
| 6 | `headset-grant`<br>`HeadsetProbe.cs:266-306` | 逐条 `adb -s <serial> shell pm grant <pkg> <perm>` | **有**：逐条看退出码，成功/失败分别计数（`:290-303`） | 「已授予 N 项」/「成功 N 项，失败 M 项：…」 | 「仅授予权限，不改其它设置；回滚用 pm revoke 逐项撤销」——**列的是并集权限名，没列包**（F7） | ✅ **真回读**（本会话已修好） |
| 7 | `disable-adapter:<name>`<br>`Fixes.cs:15-32` | `Disable-NetAdapter -Name … -Confirm:$false` | **无** | 「<网卡名> 已处理」 | 「Enable-NetAdapter -Name …」 | ⚠️ **不可达**（下） |

**关于第 7 个**：`Fixes.DisableUnusableAdapters()`（`Fixes.cs:37-39`）恒返回
`Array.Empty<FixAction>()`，注释明写「Kept deliberately unused」。全仓库唯一调用点是
`HealthChecks.cs:87`（即 `net-primary`）。**所以 `disable-adapter:*` 今天不可能被任何检查产出**，
`--apply --list` 永远不会列出它，`docs/RELEASE-0.5.0.md:25-26` 仍在把它列为工具会给的动作。
（`01-retrospective.md` §6.1 第 4/5 条已记录不可达，本条只补一个此前没写的点：
**它的 `Backup` 栏「修复前已记录该网卡当前 Enabled 状态」也是假的——代码没有记录任何东西**，
`Fixes.cs:24` 那句是一个常量字符串。若将来复活，这句必须先改成真去读一次 `Get-NetAdapter`。）

**「哪些栏位从源里无法核实」—— 明说**：

- **第 1、4、5、7 行的「回读」栏，我只能核实「代码里没有回读语句」，不能核实「实际运行时会不会
  恰好成功」。** `fw-restore-vd` 我实测了 netsh 的非提权拒绝行为（确定失败）；
  `svc-repair` 我实测了 `Start-Process` 不传递退出码（确定判据无效），
  但**没有真的跑 msiexec**（要 UAC，且会改服务安装）。
- **第 1 行「streamer-launch 起没起来」我无法核实**——需要 Streamer 当前不运行，
  而它在本机是运行中的（`docs/checks.md:21`）。这一格是**从代码判定**（无回读语句），
  不是从运行判定。
- **`headset-grant` 的真机行为完全无法核实**：头显不在 adb 上
  （`01-retrospective.md` §0.2 已实测 `adb devices` 无设备）。我只核实了它的**代码形状**
  （逐条执行、逐条判成败、失败时如实报数量），**没有核实 `pm grant` 的真实返回形态**。
  它是否会因为 HorizonOS 上「权限不存在」的返回码形态而把正常权限误报为失败，仍是未验证的。

---

## 5. `--set-param` 与配置写入路径：其余部分核对结果（无问题）

- **回滚机制是真的**：`StreamerConfigWriter.Write`（`ParameterCatalog.cs:84-108`）先
  `File.Copy(path, backup, overwrite: true)`（`:96`），再写；备份路径是 `path + ".vdhelper.bak"`。
  `App.xaml.cs:432-433` 把备份路径和回滚命令都打印了。**这一段没有说谎。**
- **坏 JSON 被拒且文件不动**：`:97` 的 `JsonNodeShim.Parse(text)` 在
  `File.Copy` 与 `File.WriteAllText` **之前**抛异常。`:104` 写完又 `JsonNode.Parse(updated)`
  复验一次。**顺序正确。**
- **`+` 不被转义成 `\u002B`**：`:105` 用 `UnsafeRelaxedJsonEscaping`，
  注释（`:98-103`）解释了为什么这个 "Unsafe" 在本地配置文件上无害。
  **与实测副本验证一致。**
- **`enable-pairing-requests` 拒绝 Streamer 在跑**（`WindowsStateChecks.cs:113-114`），
  理由写清了 2 秒防抖保存。**`--set-param` 的同一道闸在 `App.xaml.cs:393-407`**，
  且给了三种退出方式 + 真实 PID。**两处一致。**
- 参数页「切换」的回读（`ParametersViewModel.cs:106-116`）用的是 `GetBool` 与 `next` 比，
  **不是字符串比较，所以没有 F1 那个 bool 缺陷**。这是两条写入路里写得更好的一条。

---

## 6. 建议的修复顺序

1. **F1**（`--set-param` bool 全错）—— 一处三行的比较逻辑，影响 README 的示例命令，
   且会让用户回滚一次正确的改动。**先修这个。**
2. **F2 + F3**（`svc-start` 错服务名 / `fw-restore-vd` 不提权）—— 两个在本机 100% 不可用的修复。
   `fw-restore-vd` 改成走 `ElevatedAsync`（同文件 `:215` 现成的）。
3. **F4**（`svc-repair` 退出码不传播）—— 同样接 `ElevatedAsync`，一次解决 F3 的同类问题。
4. **F5 + F6**（`--apply` 重复执行 / 退出码不区分是否核对）—— 属于 CLI 语义，一次设计改动。
5. **F8 + F9**（重复 key / CLI 不校验类型范围）—— 记账类，可与下一次 `extract-parameters.py`
   重跑一起做。
6. **F11**（`RELEASE-0.2.0.md:31` 那行总结）—— 等 2、3 修完再改，否则改完还是假的。

---

## 7. 与 `01-retrospective.md` 的关系

以下几条**在回溯里已经记过、本文不重复计分**，只补了修复面的一个新角度：

| 回溯条目 | 本文补充 |
| --- | --- |
| §3.2 #10 `headset-grant` 空执行 | 已在本会话修好（现在真逐条执行）。本文只补 F7 的回滚范围问题 |
| §6.1 #4/#5 `disable-adapter` 不可达 | 补：它的 `Backup` 栏「已记录当前 Enabled 状态」是假的（`Fixes.cs:24`） |
| §2.8 `docs/checks.md` 数字 | 已修好并改为现算，本文实测一致，记为正面（F11） |
| §2.1 `--adb` 不等于第三屏 | 已修好（`App.xaml.cs:476-479` 现在两个探针都跑）。**但 `--apply` 仍跑不到 `headset-grant`**：`HeadsetProbe` 不在 `HealthChecks.Create()` 里（`grep -c` 结果 0），所以 `--apply --list` 永远不会列出它。**这是回溯没提到的一点** |

**最后一条需要展开**：CLI 有两条互不相通的修复通道。
`--apply` 走 `HealthEngine`（PC 侧 35 项），`--adb` 走 `HeadsetProbe`。
`headset-grant` 是**唯一由头显侧检查产出的修复**，因此：
`VdHelper.exe --apply headset-grant` **永远返回 9**（`App.xaml.cs:341-347`），
并在屏幕上打印「本轮可用的修复项：…」。用户会以为这个修复不存在。
它只能在第三屏点。这算不算缺陷取决于设计意图，但**README 与 `--apply --list`
都没有一句话说明「有些修复只在第三屏可用」**——这是本审计范围内**唯一一处
「文档没有覆盖到的能力边界」**。

---

## 附：本文实跑过的命令（全部只读或只写临时目录）

```powershell
# 服务名（F2、F4）
Get-Service | Where-Object { $_.Name -like 'VirtualDesktop*' }        # -> VirtualDesktop.Service.exe
Get-Service -Name 'VirtualDesktop.Service'                             # -> NoServiceFoundForGivenName
Get-Service -Name 'VirtualDesktop.Service' -ErrorAction SilentlyContinue -eq $null   # -> True

# 退出码传播（F4）——子进程自己 exit 5
powershell -Command "Start-Process cmd -ArgumentList '/c exit 5' -Wait -NoNewWindow; …"
#   -> LASTEXITCODE 为空，outer_exit=0

# netsh 非提权行为（F3），跑两次，结果相同
powershell -Command "netsh advfirewall firewall add rule name='ZZProbeDupTest' ..."
#   -> The requested operation requires elevation (Run as administrator). exit=1

# PowerShell 错误退出码（确认 RunPsAsync 的失败分支可达）
powershell -Command "Start-Service -Name 'NoSuchServiceXYZ'"           # -> exit=1
powershell -Command "Get-NoSuchCmdlet -Foo"                            # -> exit=1

# 目录（只读 json，F8、F9、F10）
python 读 src/VdHelper/Resources/parameters.json
#   -> total 111 / distinct 109 / dup ['AutoAdjustBitrate','VRFramerate']
#   -> readOnly 19 / secret 18 / ro&notsecret ['HotKeysEnabled']
#   -> table0 可写 36，其中 bool 18

# F1 复现：临时目录里一个独立 .NET 10 小程序，逐字复刻 App.xaml.cs:438-446
#   ShowPairingRequests true      -> match False (exit 6)，actual "true" vs expected "True"
#   AllowRemoteConnections false  -> match False (exit 6)
#   MonitorCount 3 / DeviceName "Rig" -> match True (exit 0)
```

**没有跑**：`--apply`（任何 id）、`--set-param`、`--quit-streamer`、`--adb`、
任何会改系统状态的修复。原因见 `WORKFLOW.md:74-91`。
F3/F4 的 netsh 与 msiexec 行为是**在非提权、无副作用的条件下**测的通用机制
（`Start-Process` 不传递退出码这一点与 msiexec 无关，是 PowerShell 自身行为），
不是对这两个修复的实跑。
