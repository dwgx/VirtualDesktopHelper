# 02 — 旧版 VDH 0.4.7 技术债与新工程禁忌

对象：`D:/Project/VirtualDesktopHelper/reference/legacy_vdh/`（`VDH.cs` 1009 行、`VDH.Extra.cs` 874 行、`BitrateApk.cs` 277 行）
每条给出 `file:line`、为什么是坑、新工程必须怎么做。

---

## 1. UI 线程阻塞 / 假死 / 重入

### 1.1 所有 adb 调用同步阻塞在 UI 线程

- 位置：`VDH.Extra.cs:405-440` `RunAdb`，15 处调用点在按钮事件里同步执行。
- 最长阻塞：`VDH.Extra.cs:533` `install -r -g "<apk>"`，`timeoutMs = 600000`（**10 分钟**）。`VDH.Extra.cs:468` logcat 20 s、`VDH.Extra.cs:444` `devices -l` 15 s。
- 窗体在这段时间无响应，用户看到的是「点了没反应」。
- 靠 `Application.DoEvents()` 硬撑两处：`VDH.cs:928`（Write IL 期间）、`VDH.Extra.cs:532`（安装 APK 期间）。`DoEvents` 会把消息队列交还用户 → 用户可以在补丁/安装进行中**再点第二个按钮**，触发并发 `Process.Start` 与对同一文件的写。

**新工程禁止**：任何 `adb` / `zipalign` / `apksigner` / 网络请求一律 `async Task` + `CancellationToken`；进度用 `IProgress<T>` 推给 UI；长任务期间禁用触发按钮（`IsEnabled=false`）并在取消时 `Kill(entireProcessTree: true)`。**不要用 `DoEvents`，WPF 也没有它。**

### 1.2 `RunAdb` 的超时是死代码，且有管道死锁风险

```csharp
// VDH.Extra.cs:429-435
var o = p.StandardOutput.ReadToEnd();      // 阻塞直到进程关闭 stdout
var e = p.StandardError.ReadToEnd();      // 阻塞直到进程关闭 stderr
if (!p.WaitForExit(timeoutMs))            // 走到这里进程必然已退出
{
    try { p.Kill(); } catch { }
    return o + e + "\r\n[timeout]";
}
```

- `ReadToEnd()` 返回意味着两个流都已 EOF，进程已经结束 → `WaitForExit(timeoutMs)` **永远返回 true**，`[timeout]` 分支不可达。所有 timeout 参数都是摆设。
- 顺序读两个流：先同步读完 stdout。若子进程 stdout 数据量大而 stderr 同时在写，stderr 的 OS 管道缓冲（通常 4 KB）写满后子进程阻塞等 stderr 读，而主线程在等 stdout EOF → **经典死锁**。`adb install` 的输出恰好可能触发。

**新工程禁止**：输出必须异步泵（`OutputDataReceived` + `BeginOutputReadLine`，或 `ReadToEndAsync` 配 `Task.WhenAll`）。超时要真超时：`ct.CancelAfter(timeout)` + 显式判断 `AdbResult.TimedOut`。

### 1.3 长任务无取消、无进度、无法区分「慢」与「死了」

`VDH.Extra.cs:426` `using (var p = Process.Start(psi))` 一把梭，用户看到光标变不变化不知道。

**新工程禁止**：所有长任务必须可取消；必须显示「正在做什么 / 第几步 / 已耗时」；必须有超时与超时后的明确提示（不是 `[timeout]` 塞进输出流）。

---

## 2. 命令行字符串拼接

### 2.1 `ProcessStartInfo(file, args)` 单字符串

- `VDH.Extra.cs:415` `new ProcessStartInfo(adb, args)`，args 由调用方拼好：
  - `VDH.Extra.cs:456` `"shell dumpsys package " + Pkg`
  - `VDH.Extra.cs:468` `"logcat -d -t 80 --pid=" + n`
  - `VDH.Extra.cs:475` `"shell am start -n " + Pkg + "/md5910…VrActivity"`
  - `VDH.Extra.cs:500` `"shell pm grant " + Pkg + " " + p`
  - `VDH.Extra.cs:533` `install -r -g \"" + apk + "\"`
- `BitrateApk.cs:77` `-f -p 4 \"" + unsigned + "\" \"" + aligned + "\"`
- `BitrateApk.cs:82-86` apksigner 一长串含 `--ks-pass pass:<口令>` 明文。

引号包裹不等于转义：路径里出现 `"`（Windows 文件名允许吗？允许的是反斜杠，但 cmd 层仍会被 `&`、`^`、`%`、`!` 干扰）就会被截断或注入。`--ks-pass pass:vdpatch2026` 还会出现在进程命令行里，本机任何进程都能读到。

**新工程禁止**：`ProcessStartInfo.ArgumentList` 逐个加参数（`psi.ArgumentList.Add("shell")`、`Add(apkPath)` …），它自动做 Windows 命令行转义。口令类参数改用环境变量或 stdin 传入，不进命令行。

### 2.2 `Run` 里 stdout/stderr 又同步读

`BitrateApk.cs:259-268` 同样是 `ReadToEnd()` × 2 + `WaitForExit()`，同样的死锁形状，且 `zipalign`/`apksigner` 的输出量不小。

**新工程禁止**：同 §1.2。

---

## 3. `catch` 吞异常

全文 `catch { }` / `catch (Exception) { return … }` 出现在：

| 位置 | 吞掉了什么 | 后果 |
| --- | --- | --- |
| `VDH.cs:263` `MainForm` 构造里 `try { CheckOta(false); } catch { }` | 整个 OTA 检查的任何异常 | 启动期静默失败，用户以为已检查 |
| `VDH.cs:153` `AppCfg.Load` | 配置 JSON 解析失败 | **配置损坏被静默替换为默认值**，用户设置无声丢失 |
| `VDH.cs:534` `ChangelogText` | 读文件失败 | 显示 0.2 旧日志（见 §6.3） |
| `VDH.cs:556` `DetectStreamerVer` | `FileVersionInfo` 失败 | 误判「未安装」 |
| `VDH.cs:603` `GetInt` | 类型转换 | 用默认值，用户不知道自己填的值被丢了 |
| `VDH.cs:870` `ClearHistory` 逐文件 `try{File.Delete}catch{}` | 每个文件的删除失败 | 部分删除，用户以为清空了 |
| `VDH.cs:904` `RestartStreamer` | kill 失败 | Streamer 没重启，新配置不生效，无提示 |
| `VDH.cs:1004` `EnsureLz4` 的 `AssemblyResolve` | 加载失败 | `CapPatch.Apply` 抛 `FileNotFoundException`，靠 `:941` 特判才给友好提示 |
| `VDH.Extra.cs:236` `OpenUrl` | ShellExecute 失败 | 点链接无反应 |
| `VDH.Extra.cs:381` 下载后删 zip | 文件占用 | 临时 zip 残留 |
| `VDH.Extra.cs:433` `RunAdb` kill | kill 失败 | 声称超时但进程还在跑 |
| `VDH.Extra.cs:702` `InstallFromFolder` 算 SHA | 单文件读失败 | 该文件在报告里只显示异常消息，格式与其它行不一致 |
| `VDH.Extra.cs:839` OTA SHA 不符后删 tmp | 删除失败 | 校验失败的 exe 留在 `%TEMP%` |
| `VDH.Extra.cs:863` `Clipboard.SetText` | 剪贴板失败 | 用户以为复制成功了 |
| `VDH.Extra.cs:852` `CheckOta` | 所有异常 | 非交互模式下完全静默 |

**新工程禁止**：
- 禁止空 `catch`。至少 `catch (Exception ex) { Log.Warn(ex); … }`。
- **解析类**错误（配置 JSON、StreamerSettings JSON）必须**向上抛到 UI 并显示**「文件解析失败，已保留原文件在 X，是否要备份后重置？」——绝不能静默回默认。
- **破坏性操作**（删文件、改配置、装 APK）失败必须列出「哪些成功、哪些失败、失败原因」。
- 取消令牌引发的 `OperationCanceledException` 是正常路径，不入错误日志。

---

## 4. 硬编码路径与硬编码版本号

### 4.1 绝对路径

| 位置 | 值 | 问题 |
| --- | --- | --- |
| `VDH.cs:113` | `C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe` | 装到别处 / 装 D 盘 / 便携版即失效；x86 与 ARM64 路径不同；用户级安装目录不覆盖 |
| `VDH.cs:115` | `C:\ProgramData\Virtual Desktop\StreamerSettings.json` | 同上 |
| `VDH.Extra.cs:274-275` | `D:\Project\VirtualDesktop\analysis\apk_patch\quest_adb_tools\dist\adb.exe`、`…\output\adb\adb.exe` | 只在 Owner 机器上有效 |
| `VDH.Extra.cs:284` | `D:\Software\Android\Sdk\platform-tools\adb.exe` | 同上 |
| `VDH.Extra.cs:289-290` | `D:\Software\VIVE Hub\…\ADB\adb.exe` ×2 | 同上 |
| `BitrateApk.cs:239` | `D:\Software\Android\Sdk\build-tools\35.0.0\` + name | 同上，且 build-tools 版本写死 35.0.0 |
| `BitrateApk.cs:248` | `D:\Project\VirtualDesktop\analysis\apk_patch\vd-patch-release.keystore` | 同上 |

### 4.2 相对上跳路径

- `VDH.cs:127-135` `RepoMobile` = `<exe目录>\..\analysis\apk_patch\patched_assemblies\VirtualDesktop.Mobile.dll`。发布版 exe 在用户下载目录，上跳一级是下载目录的父目录 → 必然不存在。实测该文件在 `F:` 树里也已不存在（`patched_assemblies/` 仅有 `Xenko.dll`、`VirtualDesktop.Net.dll`、`Xenko.Native.dll`、`Xenko.OpenXR.dll`、`System.ComponentModel.TypeConverter.dll`）→ 这一栏在发布版里恒为空。
- `VDH.Extra.cs:271` `analysis\apk_patch\quest_adb_tools\dist\adb.exe`，配合 `:268-273` 最多回溯 6 层父目录 → 会在文件系统里做无意义的广度搜索。
- `VDH.Extra.cs:520` `..\analysis\apk_patch\output\signed_v22.apk`

### 4.3 硬编码版本号（四处不一致）

| 位置 | 值 |
| --- | --- |
| `VDH.cs:146` | `public const string Version = "0.4.7";` |
| `VirtualDesktopHelper.csproj:11` | `<Version>0.4.7</Version>` |
| `app.manifest:3` | `<assemblyIdentity version="0.4.7.0" …>` |
| `VERSION.txt` | `0.4.7`（仓库根） |

四处手工同步。加 `VERSION.txt` 时最容易漏其中一处（0.4.5 就是因为「strip junk from VERSION.txt」出的问题，见 CHANGELOG）。

### 4.4 硬编码的领域常量

| 位置 | 值 | 问题 |
| --- | --- | --- |
| `VDH.Extra.cs:18` | `const string Pkg = "VirtualDesktop.Android"` | **对当前基线无效**。当前基线包名 `com.dwgx1.vd.recovered`（`F:/Project/VirtualDesktop/analysis/apk_patch/build_apk_2d_launch.py:38-39`；`capture_diagnostic.bat:6`）。→ 头显页的 logcat / 启动 / 停止 / 授权 全部作用于一个不存在的包 |
| `VDH.Extra.cs:475` | `md59102214312e19799944a61bf7bc2f23e.VrActivity` | 正确（`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:52`），但同样写死 |
| `VDH.Extra.cs:364` | `https://dl.google.com/android/repository/platform-tools-latest-windows.zip` | `-latest` 每次内容可能变；无 SHA 校验 |
| `VDH.Extra.cs:741` | `raw.githubusercontent.com/dwgx/VirtualDesktopHelper/main/VERSION.txt` | 指向**旧仓库**，新工程要换成自己的 |
| `VDH.Extra.cs:833` | `github.com/dwgx/VirtualDesktopHelper/releases/download/v…` | 同上 |
| `BitrateApk.cs:19` | `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so` | 只处理 arm64；arm32/armeabi 变体不支持 |
| `BitrateApk.cs:28` | `size == 544256 ? … : size == 540672 ? … : null` | Mobile.dll 尺寸写死；尺寸变了直接 `throw InvalidOperationException("Mobile.dll not in blob")` |
| `VDH.cs:31-34` | 三行版本表 | 新版本要改代码 |
| `BitrateApk.cs:43-44` | XOR 0x5A 编码的 keystore 别名 `vdpatch` / 口令 `vdpatch2026` | **凭据在源码里**（本地解码确认）。且 `BitrateApk.cs:82-86` 把口令拼进 apksigner 命令行 → 出现在进程列表里 |

**新工程禁止**：
- 路径一律 `Environment.GetFolderPath` + 逐级候选 + 明确报「没找到，候选是 A/B/C」。
- 版本号**单一来源**：`<Version>` in csproj → MSBuild 写入 `AssemblyInformationalVersion` → 构建脚本用同一个值写 `VERSION.txt`。禁用手改。
- 领域常量（包名、activity、blob 路径、DLL 尺寸、IL 偏移）必须**可配置 + 自动发现**。
- **不写任何 keystore 与口令**。AGENTS.md 第 2 条。签名能力留给外部工具链，本工具只读不签。

---

## 5. 数据丢失 / 不一致风险

### 5.1 删除账户根本不落盘（真 bug）

```csharp
// VDH.cs:702-738 RemoveSelectedAccount()
// 改完 data 后只调：
FillAccounts();      // :737
```

从不调 `SaveNow()`（`VDH.cs:763`）或 `File.WriteAllText(Paths.Settings, …)`。用户点「删除选中」，列表刷新了看起来删掉了，关掉工具再开——账号还在。而且删的是 Streamer 自己的账号槽，即便写了也要重启 Streamer 才生效，UI 也没说。

### 5.2 `RestartStreamer` 的顺序风险

```csharp
// VDH.cs:897-907
foreach (var p in Process.GetProcessesByName("VirtualDesktop.Streamer")) p.Kill();
if (File.Exists(Paths.StreamerExe)) Process.Start(…);
```

`Kill()` 不等待退出。Streamer 被强杀时可能把内存中的旧设置回写 `StreamerSettings.json`，**覆盖掉用户刚保存的配置**。`catch { }` 还会吞掉 kill 失败 → 用户点了「重启串流端」，实际没重启，新设置不生效，零提示。

### 5.3 `LoadHistory()` 不备份就覆盖

```csharp
// VDH.cs:875-882
File.Copy(p, Paths.Settings, true);   // 直接盖正式配置，无二次确认、无 Snapshot
```
对比 `RestoreFactory()`（`:884-895`）是先 `Snapshot()` 再覆盖。同一功能的两个入口行为不一致。

### 5.4 `SaveNow` 的备份在写之前（对）但顺序易错

```csharp
// VDH.cs:763-770
PushIntoData(); Snapshot(); File.WriteAllText(Paths.Settings, Pretty(data), Encoding.UTF8);
```
`Snapshot()` 拷的是**改动前**的文件 —— 这是对的。但 `Snapshot()`（`:843-849`）自身不检查 `Paths.Settings` 是否存在就 `Directory.CreateDirectory`，且 `File.Copy` 失败会抛到 UI（未捕获）→ 保存操作整体失败，用户填的设置全丢。

### 5.5 `DownloadAdb` 先删后解，无回滚

```csharp
// VDH.Extra.cs:379-380
if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
ZipFile.ExtractToDirectory(zip, Paths.AppDir);
```
已有的 `platform-tools` 先删掉。zip 下载不完整 / 解压失败 → 用户原有的可用 adb 没了。**新工程禁止**：先解到临时目录 → 校验 → 备份旧目录 → 原子切换。

### 5.6 OTA 覆盖自身失败无回滚

```csharp
// VDH.Extra.cs:843-849
File.WriteAllText(bat, "@echo off\r\nping 127.0.0.1 -n 2 >nul\r\ncopy /y \"" + tmp + "\" \"" + self + "\"\r\nstart \"\" \"" + self + "\"\r\n");
Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true });
Application.Exit();
```
`ping -n 2` 是猜的等待（约 1 秒）。exe 被锁 → `copy` 失败 → bat 窗口一闪而过 → 用户得到「没更新也没退出」的界面。`%TEMP%\vdh-swap.bat` 也没有清理。bat 里的路径没转义 `%` 与 `&`。

### 5.7 全量读 APK 算 SHA

```csharp
// VDH.cs:933
cfg.LastApkSha = BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(dest))).Replace("-", "");
```
补丁后的 APK 上百 MB（`VDH.Extra.cs:529` 自己写着「~1 GB」），`ReadAllBytes` 一次性进内存。而 `VDH.Extra.cs:676-681` 的 `Sha256File` 是正确的流式版本 —— 同一工程里两个实现，调用点挑了错的那个。

### 5.8 `AppCfg.Save()` 无原子性

`VDH.cs:155-159`：`File.WriteAllText(Paths.Config, …)`。写到一半断电/被杀 → `config.json` 截断 → `Load()` 的 `catch { }`（`:153`）静默回默认 → 语言、adb 路径、OTA 开关全丢。

**新工程禁止**：任何配置写入 = 写 `.tmp` → `File.Move(tmp, target, overwrite: true)`。任何 StreamerSettings 修改 = 先 `Snapshot()` → 写 tmp → 原子替换 → 显示「已改：X / Y；回滚：点这里」。

---

## 6. 逻辑 bug

### 6.1 `CheckOta` 的 SHA 校验是子串包含

```csharp
// VDH.Extra.cs:837
if (sums.IndexOf(got, StringComparison.OrdinalIgnoreCase) < 0)
```
`SHA256SUMS.txt` 的**任意位置**出现该 hex 就算通过。没解析 `<hex>  <文件名>` 结构，没比对**文件名**。理论上 64 个 hex 出现在别的行的部分位置不会误判（长度固定），但语义是错的，且新版要支持多个产物时必然出错。

### 6.2 版本解析失败时静默退回字符串比较

```csharp
// VDH.Extra.cs:818-820
var remoteOk = Version.TryParse(remoteVer, out rv);
var localOk  = Version.TryParse(local, out lv);
bool newer = remoteOk && localOk ? rv > lv : string.Compare(remoteVer, local, StringComparison.OrdinalIgnoreCase) > 0;
```
远端 `VERSION.txt` 被写成 `"version 1.2"`（`SanitizeVersion` 会洗成 `1.2` 还行），但写成 `"release-2026"` 会洗成 `2026`，然后字符串比较 `2026 > 0.4.7` → true → **提示升级到一个不存在的版本**，下载 `v2026/VDH.exe` 404，报「检查更新失败」，用户永远看到这个。

**新工程禁止**：解析失败 → 明确报「远端版本号无法解析：<原文>」，不猜。

### 6.3 `ChangelogText` 永远显示 0.2

```csharp
// VDH.cs:529-535
var p = Paths.Changelog;              // <exe目录>\CHANGELOG.md
if (File.Exists(p)) { … }
return L.T("2026-08-28  VDH 0.2\r\n …");   // :536-546 硬编码 0.2 文本
```
实测 Release assets 只有 `SHA256SUMS.txt` / `VDH.exe` / `VERSION.txt`（`gh api` 结果见 `01-asset-map.md` §0），**`CHANGELOG.md` 从不随包发布**。所以线上 0.4.0–0.4.7 全部用户看到的「更新日志」都是 0.2 那段。

### 6.4 `HttpGet` / `HttpDownload` 重定向递归无深度上限

```csharp
// VDH.Extra.cs:776-781
if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400) {
    var loc = resp.Headers["Location"];
    if (!HostAllowed(loc)) throw …;
    return HttpGet(loc, timeoutMs);      // 递归，无跳数上限
}
```
`loc` 为 null 时 `HostAllowed(null)` → `Uri.TryCreate(null)` 返回 false → throw，尚可；但服务器返回自指 / 两点循环的 `Location` 会**栈溢出**（`StackOverflowException` 不可捕获，进程直接死）。

### 6.5 `HostAllowed` 保留了不再使用的 `api.github.com`

`:760` 白名单含 `api.github.com`，而 `:739-740` 的注释明说「Do not use api.github.com — unauthenticated calls 403 when the rate limit is hit」，0.4.1 起已不用。留着只是扩大了允许下载的源面。

### 6.6 `AskAdb` 的冗余分支

```csharp
// VDH.Extra.cs:328-329
found.Count > 0 ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.YesNoCancel);
```
两个分支返回同一个值。以及 `:310` `found.Exists(x => string.Equals(x, p, …))` 线性去重（O(n²)），列表可能几十项。

### 6.7 死代码

| 位置 | 内容 |
| --- | --- |
| `VDH.cs:57` | `Catalog.SecretKeys` —— 无任何读取点 |
| `VDH.cs:78-81` | `Catalog.SettingOrder` —— 无任何读取点 |
| `VDH.Extra.cs:190-231` | `GuideBody()` —— 无任何调用点（0.4.0 起被 `WikiHtml()` 取代） |
| `VDH.Extra.cs:239-242` | `FindAdb()` 无参重载 —— 无人调用 |
| `VDH.cs:403-404` | `cmbAddPlat` / `tbAddName` 创建后 `Visible=false`，从未加进任何容器；`VDH.cs:485` 还有一句 `tbAddName.Text = tbAddName.Text;`（自赋值 no-op） |
| `VDH.cs:180` | `line` 字段赋值（`:566`）后只用于 `:568` 拼一句文本 |

### 6.8 `BuildSettingsTab` 的「先放再取再删再放」

```csharp
// VDH.cs:281-282（以及 :308-309, :323-324, :329-330, :335-336, :341-342, :347-348, :355-356 共 8 处）
grid.Controls.Remove(grid.GetControlFromPosition(0, 0));
grid.Controls.Add(lbCodec, 0, 0);
```
先往格子里放匿名 Label 用来占位建行，记住行号后再删掉换成具名 Label。八次重复，纯绕法。**新工程禁止**：WPF 用 `ItemsControl` + `ItemTemplate` 由数据生成行，从根上不存在这个问题。

---

## 7. 写盘格式 / 编码

### 7.1 手写 JSON printer 类型不保真

`VDH.cs:791-835` `FormatJsonValue`：
- 支持 `null` / `bool` / 整型 / `Dictionary<string,object>` / `IList` / 字符串。
- 其余一律 `VDH.cs:834` `return ser.Serialize(Convert.ToString(v, CultureInfo.InvariantCulture));`
- 即：`float` / `double` 会变成**字符串**；`DateTime` 变成 `"01/01/2026 00:00:00"` 格式字符串；`byte[]` 变成 `"System.Byte[]"`。任何 Streamer 以后引入的新类型，写回时被污染。

### 7.2 写盘无 BOM / 无原子性 / 无校验

- `VDH.cs:767` `File.WriteAllText(Paths.Settings, Pretty(data), Encoding.UTF8)` —— `Encoding.UTF8` 在 .NET Framework 下**写 BOM**。Streamer 自己读时大概率容忍（`File.ReadAllText(path, Encoding.UTF8)` 会去 BOM，`VDH.cs:575` 自己也这么读），但这是一次不必要的风险。
- `VDH.cs:848` `File.Copy(…, true)` 快照到 `%AppData%\VirtualDesktopHelper\history\`，**无数量上限**。天天点保存就天天长一个文件，无清理、无上限。
- `AppCfg` 的字段用**公共字段**（`VDH.cs:140-145`）而非属性，`JavaScriptSerializer` 直接序列化字段 → 任何新增字段自动落盘，无法标注「不序列化」。

### 7.3 `Pretty` 的 key 顺序不保证

`VDH.cs:776` `new List<string>(d.Keys)` 直接取 `Dictionary` 的枚举顺序（.NET Framework 的插入序实现细节），不是显式顺序。`Catalog.SettingOrder`（`:78-81`）本来是想定义顺序的，但**从未被使用**。

**新工程禁止**：写盘前校验（读回 → 结构比对 → 不一致则回滚 + 报错）；未知键一律 `WriteRawValue` 原样保留；历史快照加数量上限（如保留最近 50 份）。

---

## 8. 命名 / 可维护性

| 问题 | 位置 |
| --- | --- |
| 无命名空间分层：`MainForm` 里有 UI、业务、HTTP、加密、进程管理 | 全部 |
| 888 行的 `partial class MainForm`（`VDH.cs:162-954` + `VDH.Extra.cs:15-873`）承载 40+ 方法 | — |
| 无接口、无依赖注入，静态全局 `L.Zh`、`data`、`cfg` | `VDH.cs:18`、`:166`、`:164` |
| 中英双语以 `L.T(en, zh)` 双参数内联在业务代码里 | 全文 ~120 处 |
| 语言切换 = 逐控件重设文案（77 行） | `VDH.cs:450-526` `RebuildTexts()` |
| Tab 索引写死 `tabs.TabPages[0..5]` | `VDH.cs:454-459` |
| 变量名 `o` / `e` / `p` / `d` / `r` / `x` | 到处 |
| `LangVersion=7.3`、`Nullable=disable`（`csproj:9-10`） | 放弃新语言特性与可空检查 |

---

## 9. 安全 / 边界

| 问题 | 位置 | 说明 |
| --- | --- | --- |
| keystore 别名 + 口令以 XOR 编码形式存在于源码 | `BitrateApk.cs:43-44` | 解码后为 `vdpatch` / `vdpatch2026`（本地解码确认）。**XOR 不是加密** |
| 口令进命令行 | `BitrateApk.cs:82-86` `--ks-pass pass:… --key-pass pass:…` | 本机任何进程可读进程命令行 |
| keystore 文件从三个位置自动发现（含 `D:\Project\...`） | `BitrateApk.cs:245-256` | 新工程禁止任何 keystore 逻辑 |
| 下载 adb **不校验 SHA-256** | `VDH.Extra.cs:374-378` | 只校验域名与 scheme |
| 下载 adb 无大小上限 | `VDH.Extra.cs:377` `wc.DownloadFile(url, zip)` | 可能写满磁盘 |
| 破坏性操作（杀 Streamer / 删账号 / 删历史 / 删 platform-tools）无统一「先备份后改可回滚」约定 | `VDH.cs:702-738`、`:862-873`、`:897-907`、`VDH.Extra.cs:379` | 与 AGENTS.md 第 6 条冲突 |
| 应用不申请提权（`app.manifest:5` `asInvoker`） | — | 这是**对的**，保留。但也意味着写 `C:\ProgramData` 需要管理员时必然失败 → 新工程要么检测并明确提示「需要管理员」，要么安装一个 service/计划任务（需 Owner 点名） |

---

## 10. 架构 / 流程

- **无测试**：无 `*Test*` 任何文件；`ci.yml` 只有 `dotnet build`。而 `FormatList` / `SanitizeVersion` / `HostAllowed` / `Sha256File` / `CodecSelector` 全是纯函数，白白浪费。
- **CI 只编译不产物**：`F:/Project/VirtualDesktop/_upstream/vdh/.github/workflows/ci.yml` 仅 `dotnet build VirtualDesktopHelper.sln -c Release`，不上传 artifact、不跑测试、不做 Release。
- **发布全手工**：7 个 release（v0.4.0–v0.4.7）都是手工上传 `VDH.exe` / `SHA256SUMS.txt` / `VERSION.txt`，且 v0.4.0 漏了 `VERSION.txt`、v0.4.2/0.4.3 跳号（CHANGELOG 里 0.4.3 存在但没有 0.4.2 之后的 0.4.4 说明错位）。
- **OTA 与发布约定不一致**：代码期望 `SHA256SUMS.txt` + `VERSION.txt`，但只发了 exe + sums + version，且 `VERSION.txt` 从 `main` 分支 raw 读 —— **release 的 tag 内容和 main 分支内容可以不一致**。0.4.5 的 CHANGELOG「strip junk from VERSION.txt」就是为此打的补丁。
- **`AppendTargetFrameworkToOutputPath=false`**（`csproj:14`）是**唯一让 OTA 能按固定名下载**的约定，必须保留。
- **构建依赖手工**：`VDH.bat` 里「装 VS2022 .NET desktop 开发工作负载」，CI 里 `microsoft/setup-msbuild@v2`。新工程 `dotnet publish` 自包含即可，不需要 MSBuild 工作负载。

---

## 11. 新工程禁忌速查（贴在 `docs/` 里）

| # | 禁止 | 旧版反例 |
| --- | --- | --- |
| 1 | UI 线程上跑任何进程 / 网络 | `VDH.Extra.cs:405` `RunAdb` 同步 + `timeoutMs=600000` |
| 2 | 用 `Application.DoEvents()` 撑长任务 | `VDH.cs:928`、`VDH.Extra.cs:532` |
| 3 | 拼字符串命令行 | `VDH.Extra.cs:415`、`:533`、`BitrateApk.cs:77`、`:82` |
| 4 | 先同步读 stdout 再读 stderr | `VDH.Extra.cs:429-430`、`BitrateApk.cs:264-265` |
| 5 | 空 `catch { }` | 15 处，见 §3 表 |
| 6 | 解析失败静默回默认 | `VDH.cs:153` `AppCfg.Load` |
| 7 | 非原子写配置 | `VDH.cs:767`、`VDH.cs:158` |
| 8 | 破坏性操作不备份、不给回滚 | `VDH.Extra.cs:379`、`VDH.cs:702-738`、`:897-907` |
| 9 | 硬编码绝对路径 / 盘符 | `VDH.cs:113`、`:115`、`VDH.Extra.cs:274-275,284,289-290`、`BitrateApk.cs:239,248` |
| 10 | 相对上跳猜仓库路径 | `VDH.cs:127-135`、`VDH.Extra.cs:271,520` |
| 11 | 版本号多处手写 | `VDH.cs:146`、`csproj:11`、`app.manifest:3`、`VERSION.txt` |
| 12 | 硬编码包名 / activity | `VDH.Extra.cs:18`（对当前基线已失效）、`:475` |
| 13 | 源码里放任何凭据（keystore / 口令） | `BitrateApk.cs:43-44,82-86` |
| 14 | 下载产物不校验 SHA-256 | `VDH.Extra.cs:374-378` 下载 adb 无校验 |
| 15 | 先删后解、失败不回滚 | `VDH.Extra.cs:379-380` |
| 16 | 子串包含当 SHA 校验 | `VDH.Extra.cs:837` |
| 17 | 解析失败退化成字符串比较 | `VDH.Extra.cs:818-820` |
| 18 | 递归跟重定向无跳数上限 | `VDH.Extra.cs:776-781`、`:797-803` |
| 19 | 用 bat 脚本覆盖正在运行的自己 | `VDH.Extra.cs:843-849` |
| 20 | 全量 `ReadAllBytes` 算大文件哈希 | `VDH.cs:933` |
| 21 | 手写 JSON printer 丢类型 | `VDH.cs:791-835` |
| 22 | 索引写死访问 UI 集合 | `VDH.cs:454-459` |
| 23 | 先占位再删再放的布局绕法 | `VDH.cs:281-282` 等 8 处 |
| 24 | 死代码留着当「以后用」 | `VDH.cs:57`、`:78-81`、`VDH.Extra.cs:190-231`、`:239-242` |
| 25 | CI 只 build，无测试无 artifact | `_upstream/vdh/.github/workflows/ci.yml` |
| 26 | 发布手工上传 | 7 个 release 全手工 |

---

## 12. 旧工程欠下的「发布 vs 代码」债

| 不一致 | 代码期望 | 实际发布 | 后果 |
| --- | --- | --- | --- |
| `CHANGELOG.md` | `VDH.cs:530` 读 exe 同目录 | assets 只有 3 个文件，无 `CHANGELOG.md` | 所有用户看 0.2 日志 |
| `VERSION.txt` 内容 | `VDH.Extra.cs:815` 从 `main` 分支 raw 读 | 从 tag 的 release 下载 sums 与 exe | tag 内容与 main 内容可不一致 |
| 版本比较 | `Version` 解析优先 | — | 解析失败静默字符串比较（§6.2） |
| `SHA256SUMS.txt` 格式 | 只做子串包含 | `SHA256SUMS.txt:1` = `59FA441B…A0  VDH.exe`（标准双空格格式） | 恰好能用，但语义没实现 |

---

## 13. 未验证项

- `[未验证]` §1.2 的 stdout/stderr 死锁在 `adb install` 上的**实际触发条件**（需要一次真实的大输出 adb 调用 + 观察）。判定成立条件：本机插着头显、装一个会产生大量输出的 APK、用同步双 `ReadToEnd` 跑并观察是否挂住。
- `[未验证]` §6.4 `HttpGet` 自指 `Location` 是否真能让 `StackOverflowException` 打崩进程（.NET 里 `StackOverflowException` 在 .NET Core 上行为可能不同）。验证条件：本地起一个返回 `Location: <自身 URL>` 的 HTTP 服务。
- `[未验证]` `BitrateApk.cs` 的三处 IL 偏移 `0x13B0C` / `0x14E45` / `0x2BFE3`（1.34.22.0，Mobile.dll size 544256）在当前 patched 基线上是否仍有效。`F:/Project/VirtualDesktop/analysis/` 全树 grep `0x13B0C` 无相关命中。验证条件：对当前基线 APK 做 XALZ 解析 + 反汇编。
- `[未验证]` `Catalog.Codecs` 是否覆盖 Streamer 1.34.x 的完整 `PreferredCodec` 枚举（本机 `PreferredCodec=11` / `CodecName="AV1 10-bit"`，旧注释提到「6/11 if JSON already has them」，但 11 不在表里 → `SyncCodecCombo` 会落回索引 1 = H.264+，UI 显示与实际 JSON 不符）。验证条件：反编译 Streamer 找 `PreferredCodec` 定义。
