# 05 — 自审：ParameterValues 校验器实测 + adb / 控制台文案核对

只读审计。唯一写入是本文件。审的是**当前工作树**（`main` @ `fb53faa`，`git status` 干净）。
问题只有一个：**用户能看到的文字，是否断言了这段代码没有建立的东西。**

结论：**NEEDS_CHANGES**。校验器 `IsAcceptable` 有一处文档与代码直接矛盾（文档说"解释不了的类型一律放行"，
代码在 enum + 散文 Range 上**全部拒绝**），另有 4 个 PC 侧 `--set-param` 键的浮点/枚举边界**从未生效**，
而 `App.xaml.cs` 的注释正是拿这些当动机写的。adb 侧两处已修好。

> **审计期间工作树被切走过一次**：中途 `git checkout gh-pages` 使 `src/` 整树消失（gh-pages 只有文档站）。
> 已切回 `main` 并**逐个文件重读校验**了下面所有引用的行。`resources/parameters.json` 磁盘版 111 行，
> 与 HEAD 一致。下文所有 `file:line` 均在当前树上复核过。

---

## 一、`ParameterValues.IsAcceptable` —— 实测（本次唯一做了真跑的验证）

方法：把 `src/VdHelper/Core/Config/ParameterValues.cs:24-152` 逐行忠实移植为 Python
（`EnumAllowed` 的 `/ | ,` 切分、`Split(' ',2,RemoveEmptyEntries)`、去引号、`Distinct(OrdinalIgnoreCase)`、
`long.TryParse`/`double.TryParse` 的 `InvariantCulture` 语义全部对齐），对
`src/VdHelper/Resources/parameters.json` **全部 111 行**逐行跑，不是抽样。
Python 模型见下表"复核方式"列。

### 1.1 高危：`ActiveCodec` 被**全量拒绝**，而文档承诺放行

`src/VdHelper/Core/Config/ParameterValues.cs:8-21`（类注释）：

> Deliberately conservative: a key whose type or range this cannot interpret is **allowed**,
> with the reason saying so.

实测反例 —— `ActiveCodec`（type `enum VideoCodec`，range 散文 `同 #40`）：

| 值 | 结果 |
|---|---|
| 数字 `0` | **REFUSE** 「不在目录列出的合法取值里」 |
| 数字 `2` | **REFUSE** 同上 |
| 数字 `999` | REFUSE 同上 |
| 字符串 `"HEVC"` | REFUSE 「类型是 enum 或 int，但没有给出可校验的取值列表，且这个值不是整数」 |

链路：`EnumAllowed` 在 `:117-118` 因 range 是散文（无 `/|` 切分点、也无 `– — ~ ≤ ≥`）产出**空表** →
`:60` 的 `type.StartsWith("enum") && !EnumAllows(...)` 对空表求值 `!false` → **一律拒绝**。
文档说的"解释不了就放行"在这里是**拒绝**，且拒绝理由「不在目录列出的合法取值里」断言了一个
**目录里根本没有取值列表**的事实。同一个错句在 `:62` 和 `:74`。

`:77-81` 是唯一一处真正兑现文档承诺的分支（`allowed.Count == 0` 且值不是整数 → 拒绝），
但它只覆盖字符串，不覆盖数字。

**注意**：`ActiveCodec` 是 table 5，`SetParam` 在 `:427` 先被 `!info.LivesOnPc` 拦下，
所以这条**今天走不到用户**。但 `ParameterValues` 是 `public static`，`:39` 的类型分支就是这个逻辑，
任何新增调用方都会踩到。文档与代码的矛盾本身是缺陷。

**修法**（二选一）：`:60` 改为 `EnumAllowed(info).Count > 0 && !EnumAllows(info, ...)`；
或者承认"enum + 无列表 = 拒绝"是有意为之，把类注释 `:18-20` 改成实话。**不要留着注释说放行、代码拒绝。**

### 1.2 高危：4 个 PC 侧 `int (enum …)` 键的枚举边界**从未被检查**

`PreferredCodec` / `AudioStreaming` / `GamepadEmulation` / `OpenXRRuntime` 的 type 字符串是
`int (enum VideoCodec)` 这种形态，于是 `:50` 的 `StartsWith("int")` 命中、`:60` 的
`StartsWith("enum")` **不命中** → `EnumAllows` 根本不被调用。

实测（`PreferredCodec`，range 是完整的配对表 `0 Automatic / 1 H.264 / 2 HEVC / … / 11 AV1 10-bit`）：

| 输入 | 结果 |
|---|---|
| 数字 `999` | **ACCEPT** |
| 数字 `-1` / `0` / `2` / `11` | ACCEPT |
| 字符串 `"999"` | REFUSE（附 16 项列表） |
| 字符串 `"HEVC"` / `"Automatic"` / `"H.264"` / `"VP9"` | ACCEPT |

即：**任务书点名要测的 `999`，不拒绝。** 而且同一个键上，字符串 `"999"` 拒绝、数字 `999` 放行 ——
自相矛盾。`AudioStreaming` / `GamepadEmulation` / `OpenXRRuntime` 数字 `999` 同样 ACCEPT。
这四个都是 `table 0` 且 `readOnly=false`，**是 `--set-param` 今天就能走到的键**。

`:69-70` 的注释「An enum is often written as a string in this file」把字符串当主路径，
但 `StreamerSettings.json` 里枚举就是数字，所以这条注释恰好避开了真正会失败的那条路。

**修法**：`:50` 的类型判定改成"含 `enum` 字样"而不是"以 `enum` 开头"，例如
`type.Contains("enum")`，并让 `:60` 用同一个判定。

### 1.3 中危：12 个 float 键里 10 个的数值边界**取不出来**

`NumericRange`（`:142-153`）要求分隔符**两侧都能 `double.TryParse`**。而 PC 侧 5 个滑块键的 range 写成
`UI 滑块 0.4–1.0`、`UI 滑块 0.05–1.0（step 0.01）` —— 分隔符左边是 `UI 滑块 0.4`，解析失败 → `(null, null)` →
`:95` 的边界判断整段跳过。

实测全部 float 行：

| key | table | range | 取出的边界 | `999.0` |
|---|---|---|---|---|
| HorizontalFovTangent | 0 | `UI 滑块 0.4–1.0（step 0.01）` | 无 | **ACCEPT** |
| VerticalFovTangent | 0 | `UI 滑块 0.4–1.0` | 无 | **ACCEPT** |
| RenderResolutionOverride | 0 | `UI 滑块 0.5–2.0` | 无 | **ACCEPT** |
| FoveaSize | 0 | `UI 滑块 0.15–0.35` | 无 | **ACCEPT** |
| Sharpening | 0 | `UI 滑块 0.05–1.0` | 无 | **ACCEPT** |
| MicVolume | 3 | `0.0–1.0` | 0.0–1.0 | REFUSE |
| DownloadProgress | 5 | `0.0–1.0` | 0.0–1.0 | REFUSE |
| DesktopBitrateLimit / VRBitrateLimit / Gamma / IPD / VideoPlaybackSpeed | 3/4 | 散文 | 无 | ACCEPT |

所以 `--set-param FoveaSize 999` 今天能写进去。`App.xaml.cs:443` 的注释
「a float ignored its stated slider range」举的正是这个例子 —— 它**仍然被忽略**。

**修法**：`NumericRange` 改为在分隔符两侧各取**尾部**的浮点 token（正则 `[0-9.]+$` / `^[0-9.]+`），
或对 `UI 滑块 ` 这类前缀先剥掉。

### 1.4 低危：`[JsonIgnore]` 被当成取值列表的一部分

`DesktopBitrate` / `VRBitrate` / `H264PlusVRBitrate` / `AV1VRBitrate` 的 range 是
`[JsonIgnore]` / `[JsonIgnore]，不落盘不传输`。`EnumAllowed` 的 `Split(' ')` 把它当成一个字面取值，
于是字符串写入会被拒，理由是「不在目录列出的合法取值里：[JsonIgnore]」——
这句话断言了一份**不存在的取值列表**。`MeasuredBandwidth` 的 range 是 `整数`，同理得到
「不在目录列出的合法取值里：整数」。这四个 + MeasuredBandwidth 都是 `table 4`，
`SetParam` 在 `:427` 先拦下，**今天走不到用户**；同 1.1，属"注释/文案说有约束、代码没有"。

### 1.5 低危：`bool?` 的 `null` 被拒

`IsPortrait`（table 4）type `bool?`，range 明写 `true / false / null`。
`:40` 的分支对 `JsonValueKind.Null` 取 `raw="null"`，`:43-46` 只认 `true`/`false` → **REFUSE**。
拒绝理由「这是布尔键，只接受 true / false」与目录自己写的 `true / false / null` 冲突。
table 4，同样走不到 `--set-param`。

### 1.6 `MonitorCount`（散文 range）—— 通过

任务书点名要测的散文键。range = `由硬件编码器与 HMD 类型自动钳制；VR HMD 走多显示器上限`，
type `int`。`EnumAllowed` → 空表（正确识别为散文）。

| 输入 | 结果 |
|---|---|
| 数字 `3` / `0` / `-1` / `999` | ACCEPT |
| 字符串 `"3"` | ACCEPT |
| 字符串 `"abc"` | REFUSE「类型是 enum 或 int，但没有给出可校验的取值列表，且这个值不是整数」 |

**既没有"拒绝一切"，也没有"无检查地放行一切"**：数字放行（目录确实没给边界，这是对的），
非整数字符串被拒且理由诚实。这是本次唯一一个散文键处理得完全正确的行。

### 1.7 `2` 与 `HEVC` —— 通过

任务书点名要测的合法值。`PreferredCodec` 数字 `2` ACCEPT、字符串 `"HEVC"` ACCEPT，
`EnumAllowed` 正确产出 16 项（`0 / Automatic / 1 / H.264 / 2 / HEVC / 3 / VP8 / 4 / VP9 / 5 / H.264+ / 6 / 10 / AV1 / 11`）。
非整数确实被拒：数字 `1.5` → 「不是整数」，字符串 `"abc"` → 附列表拒绝。**这一条过了。**

注意列表里没有 `HEVC 10-bit`：`"6 HEVC 10-bit"` 被 `Split(' ',2)` 切成 `["6","HEVC 10-bit"]`，
`parts[1]` 是整个 `HEVC 10-bit`，所以它在 —— 上面 16 项是去重后的，标签齐全，只是 `6` 与
`HEVC 10-bit` 之间没有 `AV1` 之前的 `10`… 实际列表顺序正确。无缺陷。

### 1.8 全部 int/enum 行实测总表（21 行，非抽样）

`list` = `EnumAllowed` 产出项数；`num999` / `num0` / `str'0'` / `str'abc'` = 结果。

| key | table | type | list | num999 | num0 | str'0' | str'abc' |
|---|---|---|---|---|---|---|---|
| SelectedTab | 0 | int | 0 | OK | OK | OK | NO |
| ServerRotation | 0 | int | 0 | OK | OK | OK | NO |
| **PreferredCodec** | **0** | int (enum VideoCodec) | 16 | **OK** | OK | OK | NO |
| **AudioStreaming** | **0** | int (enum AudioStreaming) | 6 | **OK** | OK | OK | NO |
| **GamepadEmulation** | **0** | int (enum GamepadEmulation) | 6 | **OK** | OK | OK | NO |
| **OpenXRRuntime** | **0** | int (enum OpenXRRuntime) | 6 | **OK** | OK | OK | NO |
| MonitorCount | 0 | int | 0 | OK | OK | OK | NO |
| VRFramerate | 3 | enum Framerate | 11 | NO | OK | OK | NO |
| VRGraphicsQuality | 3 | enum VRGraphicsQuality | 14 | NO | OK | OK | NO |
| HmdType | 4 | enum HmdType | 48 | NO | **NO** | **NO** | NO |
| MeasuredBandwidth | 4 | int | 1 | OK | OK | **NO** | NO |
| DesktopFramerate | 4 | int | 5 | OK | OK | **NO** | NO |
| DesktopBitrate | 4 | int | 1 | OK | OK | **NO** | NO |
| VRBitrate | 4 | int | 1 | OK | OK | **NO** | NO |
| H264PlusVRBitrate | 4 | int | 1 | OK | OK | **NO** | NO |
| AV1VRBitrate | 4 | int | 1 | OK | OK | **NO** | NO |
| VRFramerate | 4 | int | 0 | OK | OK | OK | NO |
| RefreshRate | 4 | int | 0 | OK | OK | OK | NO |
| StreamingSource | 5 | enum StreamingSource (byte) | 8 | NO | OK | OK | NO |
| **ActiveCodec** | 5 | enum VideoCodec | 0 | **NO** | **NO** | OK | NO |
| ActiveRuntime | 5 | enum Runtime | 10 | NO | OK | OK | NO |
| DownloadState | 5 | enum DownloadState | 8 | NO | OK | OK | NO |

加粗 = 缺陷行。注意 `VRFramerate` 在目录里**出现两次**（table 3 `enum Framerate` 与 table 4 `int`，
后者 range 写「[JsonIgnore]（注意：与 #60 的枚举版同名不同类）」）。`ParameterCatalog.Load` 不去重
（`ParameterCatalog.cs:59-83`），`SetParam` 的 `FirstOrDefault`（`App.xaml.cs:405`）取到的是**文件里第一条**，
即 table 3 的 enum 版。这是有意的还是巧合，代码没说 —— `[未验证]`。

---

## 二、`HeadsetProbe` 权限 / 进程汇总 —— 通过

任务书问：read count 为 0 时，汇总是否还和自己的证据一致。

- `:143` `if (packagesRead == 0 && installed.Count > 0)` 提前返回 `Unknown`，
  Detail 明写「这一项**没测成**，不是「权限齐全」」。**零读数不会被报成齐全。**
- `:202-204` 的 `summary` 读的是 `running.Count`（`:168-169` 由 `pidof` 解析出来的），
  不是 `installed.Count`。注释 `:198-201` 记录的正是旧 bug（面板说 3 个在运行、证据行说 1/3），
  现在 `:173` 的 `VD 进程存活数: {running.Count} / {installed.Count}` 与 summary 同源。**一致。**
- `:185` `notRunning = installed.Count > 0 && running.Count == 0` 在 `:177` 的 `installed.Count == 0`
  早退之后，所以不会两个分支同时可达。**正确。**
- `:171` `ev["进程 " + pkg] = "未在运行"` 只在 `pids.Count == 0` 时写；`:164-167` 用
  `int.TryParse` 过滤 stdout，`no process` / shell 报错都不会被当成 pid。**与 `:162-163` 的注释相符。**

一处**不是**缺陷但值得记：`Absent`（`:280`）是实例字段且**从不清理**，`ReadPermissionsAsync` 只 `Add`
（`:271`）。同一个 `HeadsetProbe` 实例跑第二次会带上第一次的结论。当前代码每次 `new HeadsetProbe`
（`HeadsetViewModel.cs:99`、`App.xaml.cs:519`），所以今天不成立 —— `[未验证]` 是否有意。

---

## 三、`HeadsetDeepProbe` F1 三向分支 —— 通过

`ProbeHeadsetSettingsAsync`：三个出口分别由 `read == 0`（`:246`）、`!handKeySeen`（`:252`）、
`handTracking`（`:257`）驱动，措辞各自跟随条件：

- `read == 0` → `Unknown`「头显本地设置读不到（需要 patched APK 可调试或已 root）」—— 未读就说未读。
- `!handKeySeen` → `Unknown`「…**没测成**，不是「正常」」（`:250-254`）—— 键不存在不等于键是关的，注释
  `:249` 把这条道理写清楚了，代码照做。
- `handTracking` → `Warn` / 否则 `Pass`，summary 前缀是实读的 `头显设置读到 {read}/{2} 个文件`。

`B4` 的 `pidof` 兜底（`:341-349`）同样过滤非数字，并注释了 `no process` 是非空输出的坑（`:335-340`）。
**这两处已修好，清掉。**

---

## 四、`App.xaml.cs` —— 4 项核对

### 4.1 修复分组：id 命中 0 个 / 命中 2 个 —— 通过

`:315` `GroupBy(x => x.Fix.Id, StringComparer.OrdinalIgnoreCase)`。

- **命中 0 个**：`matched` 保持 0 → `:361-368` 打印「没有匹配的修复项」+ 本轮可用清单，`return 9`。
  注释 `:357-360` 说旧的实现会"落到循环外、打印判定、退出 0"。代码确实在 `:361` 拦住了。**正确。**
- **命中 2 个**：`group.First().Fix` 取一次，`offered.Count > 1` → `:324-326` 打印
  「被 N 个检查同时提供…这是**同一个修复**，只执行一次」，`:332` 只调一次 `fix.Apply`。**正确。**
- 核对：当前树里 `FixAction` 的 id 只有 7 个（`enable-pairing-requests` / `fw-restore-vd` /
  `headset-grant` / `streamer-launch` / `streamer-restart` / `svc-repair` / `svc-start`），
  其中 `Fixes.RestoreVdRule()` 被 `FirewallPairChecks.cs:132/163/175/187` 与 `HealthChecks.cs:355`
  **5 处**返回 —— 这就是"多检查命中"的真实路径，分组逻辑对它成立。**清掉。**

### 4.2 `Redact` 是否覆盖所有控制台路径 —— **否，两条路径未覆盖**

`--selftest` 侧确实全覆盖：`:223` Summary、`:227` Detail、`:229` evidence 值、`:231` fix `What`/`Rollback`、
`:233` Guidance，全部过 `Redact`。`:217-219` 的注释（"This was the only surface printing evidence verbatim"）
就本路径而言成立。

**漏掉的两条：**

1. **`--adb` / `--deep`**：`App.xaml.cs:526-535`。`:526` `r.Summary`、`:528` `r.Detail`、
   `:530` `{k}: {v}`、`:535` `指引：` —— **一个都没过 `Redact`**。而这两个探针的 evidence
   正是最可能带 `adb shell` 原始回包的地方。`:521` 的注释说"the third screen runs TWO probes"，
   承认了内容相同，却没提这条路径的脱敏。
2. **`--apply` 列举与执行**：`:329` `fix.What`、`:330` `fix.Backup`、`:331` `fix.Rollback`、
   `:349` `Check.Summary`、`:351-353` 同上，**全部裸打**。`:231` 证明作者知道 fix 字符串要过 `Redact`，
   `--apply` 这条路径漏了。

`ReportWriter.Redact` 已是 `public`（`ReportWriter.cs:94`），`Badge` 已是 `public`（`:40`），
`InternalKeys` 保持 `private`（`:63`）—— 三者的可见性与用途相符，**这一项本身通过**；
缺的是调用点，不是可见性。

**修法**：`:526-535` 与 `:329-353` 全部套 `Reports.ReportWriter.Redact(...)`。

### 4.3 症状校验顺序 —— 通过

`:154-163`：`--symptom` 在 `HealthEngine.RunAsync`（`:165`）**之前**解析并校验，
未知 id → 打印可选清单、`return 2`。`:148-152` 的注释说旧顺序要花一整轮体检，
`:157-158` 的 `si >= 0 && si + 1 < args.Length && focus is null` 三重条件保证
`--selftest`（不带 `--symptom`）与缺参数的情况都不误报。**与注释相符。**

### 4.4 新取值守卫的位置 —— 通过

`App.xaml.cs:444` `IsAcceptable` 在 `try` 内、`StreamerConfigWriter.Write`（`:456`）之前，
`:449` 打印「没有写入任何东西。」后 `return 7`。`:452` 的 `info.Caution` 提示在守卫**通过之后**才打，
所以被拒的写入不会先吐一条"注意"。**顺序正确。**
唯一遗留：`:445` 打的 `取值范围：{info.Range}` 是**目录原文**，即使 1.1/1.3 表明这份 range
对 `int (enum …)` 和 `UI 滑块 …` 根本没被解析，输出仍暗示它参与了判定。

---

## 五、`ReportWriter` / `NetworkInventory` / `Fixes` —— 通过

- **`ReportWriter.cs:40` `Badge` / `:94` `Redact` public，`:63` `InternalKeys` private** —— 与
  `:33-38` 的注释一致（"Public because the console has to use it too"）。`:82-84` 的
  `Reportable` 对 key 做 `InternalKeys` 映射、对 value 做 `Redact`。**通过。**
- **`NetworkInventory.cs:121-130` `IsLanPeer`** —— 摘要注释写「Same /24 — enough to tell a LAN peer
  from a cloud relay, **and no more than claimed**」，代码就是比对前三字节、IPv6 直接 false。
  **注释没有超出代码。** 调用点 `HealthChecks.cs:268`、`NatChecks.cs:72/76`、
  `StreamerChecks.cs:313` 三处一致。新加的 `:112-116` 现场验证注释（local 38810/20/30/40 →
  192.168.11.14 ephemeral port）标了 notes 文件出处，`[未验证]`（我没有 Windows 会话可复现），
  但它没有声称代码做不到的事。**通过。**
- **`Fixes.cs:287-324` `streamer-launch` 的回读** —— `:296` `Before()` 先取进程快照，
  `:304-317` 轮询 12×500ms，`:306` 一旦 `Process.GetProcessesByName` 非空即成功，
  `:318-322` 6 秒未见进程则失败并写明「**进程被创建不等于它活着**」。
  成功文案 `:309-311` 按 `before is null` 分两支，如实区分"新起的"与"启动前就在跑"。
  **与代码相符。**
  一处小瑕：`:295` `Before()` 用 `.FirstOrDefault() is var id && id > 0 ? id : null`，
  `FirstOrDefault()` 对空序列给 0 → 走 null 分支，逻辑正确但写法绕。**不是缺陷。**

---

## 六、检查过并清掉的清单

| 项 | 位置 | 结论 |
|---|---|---|
| 合法值 `2` 被接受 | `ParameterValues.cs:53-66` | 通过 |
| 标签 `HEVC` 被接受 | `ParameterValues.cs:71-76` | 通过 |
| 非整数被拒（数字 `1.5`） | `ParameterValues.cs:55-59` | 通过 |
| 散文 range `MonitorCount` 不误拒也不漏检 | `ParameterValues.cs:114-137` + 目录 table0 | 通过 |
| 零读数不报"权限齐全" | `HeadsetProbe.cs:143-152` | 通过 |
| summary 与 `VD 进程存活数` 同源 | `HeadsetProbe.cs:173` / `:202-204` | 通过 |
| `notRunning` 与 `installed.Count == 0` 不重叠 | `HeadsetProbe.cs:177` / `:185` | 通过 |
| `pidof` 不把 `no process` 当 pid | `HeadsetProbe.cs:164-167` | 通过 |
| F1 三出口措辞跟随条件 | `HeadsetDeepProbe.cs:246/252/257` | 通过 |
| B4 `pidof` 兜底过滤 | `HeadsetDeepProbe.cs:341-349` | 通过 |
| 修复 id 命中 0 个 → 退出码 9 | `App.xaml.cs:361-368` | 通过 |
| 修复 id 命中 2 个 → 只执行一次 | `App.xaml.cs:318-326` | 通过 |
| `--symptom` 在体检前校验 | `App.xaml.cs:154-163` | 通过 |
| 取值守卫在写入之前、caution 在守卫之后 | `App.xaml.cs:444-455` | 通过 |
| `--selftest` 全路径过 `Redact` | `App.xaml.cs:223-233` | 通过 |
| `Badge`/`Redact` public、`InternalKeys` private | `ReportWriter.cs:40/63/94` | 通过 |
| `IsLanPeer` 注释不超出实现 | `NetworkInventory.cs:112-130` | 通过 |
| `streamer-launch` 回读与文案相符 | `Fixes.cs:287-324` | 通过 |

## 七、`[未验证]` 清单（未跑完，不掩饰）

- `IsLanPeer` 新注释里的 live-session 端口方向（`NetworkInventory.cs:112-116`）—— 本机无串流会话可复现。
- 目录里 `VRFramerate` 重复出现（table 3 enum / table 4 int）是否为有意 —— 代码无说明。
- `HeadsetProbe.Absent`（`:280`）跨次调用累积 —— 当前每次 new，不成立，但无测试锁定。
- 报告类文件（`ReportWriter.cs` 300-639 行、`ParametersViewModel.cs`、`Views/*`）未逐行审，
  超出本次点名范围。
- 兄弟 agent 的 `04-self-audit-checks.md` 未读，两份报告如有重叠以合并后的为准。

## 八、按严重度排序的缺陷清单

| # | 严重度 | 位置 | 缺陷 | 修法 |
|---|---|---|---|---|
| 1 | 高 | `ParameterValues.cs:8-21` vs `:60` | 类注释称"解释不了的类型放行"，`ActiveCodec`（enum + 散文 Range）**全量拒绝**，理由还断言了一份不存在的取值列表 | `:60` 加 `EnumAllowed(info).Count > 0 &&` 前置；或改注释说实话 |
| 2 | 高 | `ParameterValues.cs:50` / `:60` | 4 个 PC 侧 `int (enum …)` 键（PreferredCodec / AudioStreaming / GamepadEmulation / OpenXRRuntime）**数字 `999` 放行**，字符串 `"999"` 却拒 —— `--set-param` 今天就能写到 | 类型判定从 `StartsWith("enum")` 改为 `Contains("enum")`，`:50` 与 `:60` 用同一判定 |
| 3 | 中 | `ParameterValues.cs:142-153` | `NumericRange` 要求分隔符两侧整体可解析，PC 侧 5 个 `UI 滑块 X–Y` 浮点键取不到边界，`999.0` 放行；`App.xaml.cs:443` 正是拿这个当修复动机 | 两侧改用尾部浮点 token 正则，或先剥 `UI 滑块 ` 前缀 |
| 4 | 中 | `App.xaml.cs:526-535` | `--adb` / `--deep` 控制台输出**全部绕过 `Redact`**（Summary / Detail / evidence / 指引） | 全部套 `Reports.ReportWriter.Redact(...)` |
| 5 | 中 | `App.xaml.cs:329-331, 349-353` | `--apply` 打印 fix `What`/`Backup`/`Rollback` 与 `Check.Summary` 未脱敏 | 同上 |
| 6 | 低 | `ParameterValues.cs:114-137` | `[JsonIgnore]` / `整数` / `不落盘不传输` 被当成取值列表，拒绝理由断言了不存在的列表（table 4，暂不可达） | `EnumAllowed` 跳过非取值形态的 range，或按 `[`/`（` 前缀早退 |
| 7 | 低 | `ParameterValues.cs:40-46` | `bool?` 的 `null` 被拒，与目录 range `true / false / null` 冲突（`IsPortrait`，table 4，暂不可达） | `type.EndsWith("?")` 时放行 `JsonValueKind.Null` |

**下一步唯一一件事**：先改第 2 项（`int (enum …)` 的枚举边界）—— 它是唯一一个 `--set-param`
今天就能写坏配置、且注释明确声称已修的缺陷。
