# 01 — 旧版 VDH 0.4.7 资产地图与复用决策

盘点对象：`D:/Project/VirtualDesktopHelper/reference/legacy_vdh/`（Owner 自有代码，可放心复用）
目标工程：`D:/Project/VirtualDesktopHelper/src/`（C# / .NET 10 WPF，基线 = patched Virtual Desktop 客户端）

## 0. 规模与构成（实测）

```
$ wc -l reference/legacy_vdh/*.cs reference/legacy_vdh/*.csproj reference/legacy_vdh/*.manifest
   277 BitrateApk.cs
   874 VDH.Extra.cs
  1009 VDH.cs
    38 VirtualDesktopHelper.csproj
    26 app.manifest
  2325 total
```

- `VDH.cs` — `L` i18n shim、`Catalog` 静态目录、`Codec`、`Paths`、`AppCfg`、`partial MainForm : Form`（WinForms 全代码 UI）、`Program`。
- `VDH.Extra.cs` — `partial MainForm` 的另一半：头显页、Wiki 页、adb 全部逻辑、OTA、反馈。
- `BitrateApk.cs` — `CapPatch`：Quest APK 里 `libassemblies.arm64-v8a.blob.so` 的 XABA/XALZ 段定位 → LZ4 解压 `VirtualDesktop.Mobile.dll` → 改三处 int32 立即数 → 重新 LZ4 压缩 → 重建 blob → 重打包 APK → zipalign + apksigner。

原仓库：`https://github.com/dwgx/VirtualDesktopHelper.git`（`F:/Project/VirtualDesktop/_upstream/vdh/`，shallow clone，HEAD = `59472b0 docs: 0.4.7 ships Write IL; APKs stay at 500`）。远端 release 实测：

```
$ gh api repos/dwgx/VirtualDesktopHelper/releases --jq '.[] | {tag_name, published_at, assets: [.assets[].name]}'
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T10:08:01Z","tag_name":"v0.4.7"}
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T09:30:11Z","tag_name":"v0.4.6"}
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T09:12:26Z","tag_name":"v0.4.5"}
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T08:41:36Z","tag_name":"v0.4.3"}
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T08:26:12Z","tag_name":"v0.4.2"}
{"assets":["SHA256SUMS.txt","VDH.exe","VERSION.txt"],"published_at":"2026-08-28T08:24:58Z","tag_name":"v0.4.1"}
{"assets":["SHA256SUMS.txt","VDH.exe"],            "published_at":"2026-08-28T08:20:35Z","tag_name":"v0.4.0"}
```

## 1. 功能 → 实现 → 复用决策 主表

复用决策三档：**直接搬**（逐字或近逐字进新工程）/ **改写后搬**（逻辑保留，实现层换掉）/ **弃用**（不进新工程）。

| 功能 | 实现位置（文件:行-行） | 关键类 / 方法 | 依赖 | 质量评价 | 复用决策 | 理由 |
| --- | --- | --- | --- | --- | --- | --- |
| i18n 一行开关 `L.T(en, zh)` | VDH.cs:16-20 | `static class L` | 无 | 极简、无依赖，但 `L.Zh` 是全局可变静态，测试之间会互相污染 | 改写后搬 | 新工程改成 `Loc.T(en, zh)` + `IOptionsMonitor`/`LanguageOption`，用 `ILoc` 抽象注入，避免静态可变状态；但调用形态 `T("Save","保存")` 一模一样，可逐字沿用 |
| Streamer 版本 → Quest 版本目录 | VDH.cs:31-35, 83-93 | `Catalog.Lines` / `ByVersion` / `ByMobileSize` | 无 | 数据与算法分离得干净；但表只覆盖 1.34.18/19/22，且只用于一行显示文本 | 直接搬 | 表结构（`Version` / `MobileSize` / `BitrateImm` 三元组）与算法都可直接搬；**扩展点**：把 `static readonly Line[]` 改成从内嵌 JSON 资源加载，新增 patched 版本行只改数据不改代码。搬到新工程后为 `static class QuestLineCatalog`（纯数据，无 UI 依赖） |
| Codec 表（id ↔ JSON 名 ↔ 显示名） | VDH.cs:38-43, 103-110 | `Catalog.Codecs` / `sealed class Codec` | 无 | 好。四项 `0 H.264 / 5 H.264+ / 1 HEVC 10-bit / 2 AV1 10-bit` 与官方 Streamer 下拉一致；注释已说明 JSON 里还可能有 6/11 | 直接搬 | `Codec` 是不可变值类型（只读字段 + `ToString()` 取当前语言），与 WinForms 无关。新工程形态：`record Codec(int Id, string JsonName, string En, string Zh)`，`ToString()` 改走 `Loc`；WPF 侧 `<ComboBox ItemTemplate>` 绑 `En`/`Zh` 两个值而不是靠 `ToString()`，数据本身逐字搬 |
| 平台名映射（`OculusQuest` → `Meta Quest` 等 8 项） | VDH.cs:45-55, 95-100 | `Catalog.Platforms` / `PlatformName` | `L` | 纯查表，边界清晰（未知 key 原样返回） | 直接搬 | 逐字搬成 `static class PlatformNames`；新工程里 `PlatformName(key)` 去掉 `L.T` 依赖，改接收已定好的语言或返回 `(En, Zh)` 记录，由 ViewModel 选。查表本体不动 |
| `SecretKeys` / `SettingOrder` 两张表 | VDH.cs:57, 78-81 | `Catalog.SecretKeys` / `Catalog.SettingOrder` | 无 | **死代码**：`grep -n "SecretKeys\|SettingOrder"` 只命中定义，无任何读取点 | 弃用 | 真正生效的白名单散落在 `FillAccounts()` 的 `cfg.AllowSecrets` 分支里。不要把死表搬过去；新工程把「哪些键算敏感」集中成一处 `StreamerSettingsSchema.SensitiveKeys`，让 `Pretty()` 与账户页共用 |
| 已知自建 APK SHA 白名单 + 码率推断 | VDH.cs:60-66, 68-76 | `Catalog.KnownApk` / `CapForSha` | 无 | 逻辑好（不认识的 SHA 一律拒装，superseded 会二次确认）。**但数据已过期**：四个 SHA 全是旧 1.34.22.0 EN/ZH LAN 包；当前基线是去联网鉴权 + 44 处 IL 补丁 + 包名改 `com.dwgx1.vd.recovered`，SHA 完全不同 | 弃用（表）/ 直接搬（机制） | 机制（`Dictionary<sha,描述>` + 拒装未知 + superseded 警告）搬成 `ApkAllowList`，但**表数据必须重新生成**：对当前基线 APK 重新算 SHA-256 再登记。旧表一个字节都不能沿用 |
| 路径常量集 | VDH.cs:112-136 | `static class Paths` | `Environment.SpecialFolder` / `Application.ExecutablePath` | 硬编码 `C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe` 与 `C:\ProgramData\Virtual Desktop\StreamerSettings.json`（`:113,:115`）；`RepoMobile`（`:127-135`）是 `..\analysis\...` 相对上跳，发布后必然失效 | 改写后搬 | 结构保留、值重算。新工程形态：`StreamerPaths` 用 `Environment.GetFolderPath(SpecialFolder.CommonApplicationData)` 拼 `Virtual Desktop\StreamerSettings.json`，Streamer exe 先查 `ProgramFiles\Virtual Desktop Streamer` 再查 uninstall 注册表；`RepoMobile` 这种「往上一级找 analysis」的路子删掉，改为显式参数或 settings 里可配路径 |
| 自身设置持久化 `%AppData%\VirtualDesktopHelper\config.json` | VDH.cs:138-160 | `class AppCfg` / `Load()` / `Save()` | `System.Web.Script.Serialization`（net48 only） | **.NET 10 不可用**。实测：`JavaScriptSerializer` 在 `net10.0-windows` 下 `error CS0234: The type or namespace name 'Script' does not exist in the namespace 'System.Web'`；只有 `HttpWebRequest` 会以 `warning SYSLIB0014` 通过编译。另外 `Load()` 的 `catch { }` 吞掉所有解析异常后返回默认对象，配置坏了用户完全无感 | 改写后搬 | 搬到 `ConfigStore`：字段集合（`AllowSecrets` / `Language` / `CheckUpdates` / `LastBitrate` / `LastApkSha` / `AdbPath`）保留，序列化换 `System.Text.Json`（`JsonSerializerOptions { WriteIndented = true }`）；原子写（写 `.tmp` 再 `File.Move(overwrite)`），解析失败要**报错并保留坏文件**而不是静默回默认 |
| StreamerSettings.json 读取 + 类型宽容取值 | VDH.cs:572-577, 593-613 | `LoadSettingsFile()` / `GetBool` / `GetInt` / `GetStr` / `GetObj` | `JavaScriptSerializer` | 取值层设计正确：`Convert.ToInt32` + `CultureInfo.InvariantCulture`，能吃 JSON 里 `true` / `"True"` / `1` 三种写法。整体落盘路径没做 Schema 校验 | 改写后搬 | JSON 换 `System.Text.Json` 的 `JsonDocument`（保留「未知键原样带回写」的语义，这是本工程的核心需求）。`GetBool/GetInt/GetStr/GetObj` 四个 helper 直接搬成 `JsonDocEx.GetBool(doc, key)` 等扩展方法，函数体逐字保留 |
| StreamerSettings.json 回写（手写 pretty printer） | VDH.cs:763-770, 772-789, 791-835 | `SaveNow()` / `Pretty()` / `FormatJsonValue()` | `StringBuilder` + `ser.Serialize` | **值得留的核心设计**：手工缩进 2 空格、保留全部未识别键与顺序，Streamer 不会因为不认识新键而丢配置。缺点是 120 行字符串拼装，`FormatJsonValue` 对 `float`/`DateTime`/`byte[]` 会走 `Convert.ToString` 兜底，不保真 | 改写后搬 | 保留「未知键不丢」这个语义，实现换成 `System.Text.Json` 的 `Utf8JsonWriter` + `WriteRawValue`（`JsonDocument` 读 → 递归回写 `doc.RootElement.GetRawText()`），得到同样的人可读缩进但类型保真；写前必备份（见下一行） |
| 设置页表单 ↔ JSON 双向绑定 | VDH.cs:273-373（UI）, 579-591（读）, 740-761（写） | `BuildSettingsTab()` / `FillFromData()` / `PushIntoData()` | WinForms 控件 | 键名硬编码在两处（`:581-588` 与 `:742-754`），改一个键名要同时改两行；`numMon`/`numRot` 用 `Math.Max/Min` 静默夹到 1..8 / 0..99 | 改写后搬 | `FillFromData`/`PushIntoData` 的映射语义保留，但键名收进 `StreamerSettingsSchema` 的单一声明（键名 + 类型 + 范围 + 中英文说明各一列），UI 由它生成。WPF 侧走 `IEditableObject` + `IDataErrorInfo` 的 `SettingsViewModel`，越界在 UI 上报错而不是静默夹 |
| Codec 下拉与 JSON 同步 | VDH.cs:615-630 | `SyncCodecCombo()` | `Catalog.Codecs` | 逻辑正确：先按 `CodecName` 匹配、再按 `PreferredCodec` 数字匹配、都没中默认索引 1（即 H.264+）。默认索引 1 这个隐式约定只有注释没有断言 | 直接搬 | 算法逐字搬成 `CodecSelector.ResolveIndex(JsonDocument)`，**外加一条自检**：`ResolveIndex` 返回的索引写回后 `CodecName` 必须等于所选项的 `JsonName`，不成立就报出来。WPF 侧 `SelectedValuePath="Id"` + `SelectedValue="{Binding PreferredCodec}"` |
| 逗号列表 ⇄ JSON 数组格式化 | VDH.cs:632-643 | `FormatList(object)` | 无 | 小而正确：`v is string` 显式排除，避免把字符串当 `IList` 逐字符拆 | 直接搬 | 逐字搬成 `JsonListFormat.ToCommaSeparated(JsonElement?)` 与 `FromCommaSeparated(string)`，纯函数无 UI，可直接进单元测试（这是旧工程 0 测试里最值得补测试的一块） |
| 账户页展示（只显示平台 + 数量，密文按需展开） | VDH.cs:645-700 | `FillAccounts()` | WinForms `ListView` | 安全姿态正确：默认不摊开 DPAPI 密文，只显示条数与长度；`ProtectedComputerID` 只显示「已绑定 / 未设置」不显示值。UX 代价是用 `Tag` 字符串 `"acc:xxx"` / `"slot:xxx:i"` 编码层级 | 改写后搬 | 展示语义（只出平台名 + 条数 + 密文字符数）保留。WPF 侧用 `HierarchicalDataTemplate` + `ObservableCollection<AccountRow>`，`Tag` 字符串改 `AccountRow` 上的 `RowKind` 枚举 + `PlatformKey` + `SlotIndex` 属性，去掉字符串解析 |
| 删除选中账户 / 清除受保护电脑 ID | VDH.cs:702-738 | `RemoveSelectedAccount()` | WinForms `ListView` | **有真 bug**：改完 `data` 只调 `FillAccounts()`，从不 `SaveNow()`，所以删除**根本不落盘**，重启后又回来；`ProtectedComputerID` 清空同理。另外删的是 Streamer 自己的账号槽，需要重启 Streamer 才生效，UI 也没提示 | 改写后搬 | 逻辑保留但必须补落盘：删除 → `Snapshot()` 备份 → 序列化写回 → 提示「重启 Streamer 后生效」。新工程再补一条：这个操作是**破坏性修复**，按 AGENTS.md 第 6 条必须先备份、后改、可回滚，并在 UI 上写清将要改什么 |
| 出厂备份 / 历史快照 / 载入 / 清空 | VDH.cs:837-895 | `EnsureFactory()` / `Snapshot()` / `LoadHistoryList()` / `ClearHistory()` / `LoadHistory()` / `RestoreFactory()` | 无 | 备份策略是对的：首次见到 StreamerSettings 就存 `factory.json`，每次保存前 `Snapshot()` 到 `history\yyyyMMdd-HHmmss.json`，文件名排序即时间序。缺陷：`ClearHistory()` 里 `try{File.Delete}catch{}` 逐个吞异常；`LoadHistory()` 直接覆盖正式配置，没有二次确认 | 直接搬 | 机制整体搬：搬到 `SettingsBackup`（`EnsureFactory` / `Snapshot` / `List` / `Clear` / `Restore`），路径常量仍走 `%AppData%\VirtualDesktopHelper\history`。**两处补强**：清空改成返回失败清单而不是静默吞；`Restore` 覆盖前必须 `Snapshot()` 一次（`RestoreFactory` 已经是这样，`LoadHistory` 漏了） |
| 重启 Streamer | VDH.cs:897-907 | `RestartStreamer()` | `Process.GetProcessesByName` / `Process.Start` | 有顺序风险：先 `Kill()` 再 `Start()`，不等待旧进程真正退出。Streamer 被强杀时可能把内存里的旧设置回写 `StreamerSettings.json`，把用户刚保存的配置覆盖掉。`catch { }` 吞掉全部 kill 失败 | 改写后搬 | 搬成 `StreamerProcess.RestartAsync()`：先 `CloseMainWindow()` 温和关闭 → `WaitForExit(5000)` → 超时才 `Kill(entireProcessTree: true)` → 轮询确认文件句柄释放 → 再启动。WPF 侧走 `async Task`，按钮期间禁用并显示进度 |
| Streamer 版本探测 | VDH.cs:549-557, 559-570 | `DetectStreamerVer()` / `RefreshDetect()` | `FileVersionInfo` | 好：`FileVersionInfo.GetVersionInfo` 取 FileVersion，取不到就返回空串不抛。版本匹配用 `StartsWith` 前缀匹配（`1.34.22.0` 会命中 `1.34.22.0.1234`），方向正确 | 直接搬 | 两方法逐字搬成 `StreamerVersion.Detect()` 与 `QuestLineCatalog.MatchVersion(FileVersion?)`。**这是新工程「检测项」的第一条**：Streamer 装没装、什么版本要独立成一个可单测的纯函数，不塞在 UI 事件里 |
| 每项设置的中英文说明文案 | VDH.Extra.cs:131-188 | `SettingHelp(string key)` | `L` | 文案质量高：讲清「这一项是什么、动了会发生什么、什么时候不该动」。例如 H.264+ 明确写「可能卡顿、黑屏、多延迟。只建议独立路由」，`LastConnectDate` 明确写「这里只读，避免把日期改乱」 | 直接搬 | **整段文案逐字搬**（这是 Owner 自己写的解释资产，重写只会变差）。搬到新工程后 `SettingHelp` 变成 `SettingsHelp.For(key)`，文案改成 `Loc` 资源而不是 `if/else` 双语分支；同时补上旧版**没有**的 `AutoAdjustBitrate`、`OpenXRRuntime` 两条说明 |
| Wiki / 指南 HTML 页 | VDH.Extra.cs:549-553, 555-632 | `LoadWiki()` / `WikiHtml()` | WinForms `WebBrowser`（IE 内核） | 内容扎实（三份产物、配对步骤、编码表、给后续 agent 的 5 条冻结约定），CSS 内联。但渲染载体是 `WebBrowser`，WPF 没有对应控件 | 改写后搬 | **HTML 内容逐字保留**（CSS + 中英双份都留着）。渲染载体换：WPF 用 `WebView2`（Chromium）走 `NavigateToString(html)`，或干脆用 `FlowDocument` + XAML 模板重排。另一条路是把这份内容当作 `docs/` 下的 Markdown，UI 只做「打开文档」按钮 |
| `GuideBody()` 纯文本长文 | VDH.Extra.cs:190-231 | `GuideBody()` | 无 | **死代码**：`grep -n GuideBody` 只命中定义，0 个调用点（0.4.0 起被 `WikiHtml()` 取代） | 弃用 | 内容已被 `WikiHtml()` 覆盖，不要搬。新工程的等价物是 `docs/` 下的用户文档 |
| 更新日志内嵌兜底 | VDH.cs:528-547 | `ChangelogText()` | `Paths.Changelog` | **有 bug**：优先读 exe 同目录的 `CHANGELOG.md`，但 Release 产物只有 `VDH.exe` + `SHA256SUMS.txt` + `VERSION.txt`（实测 assets 见 §0），**`CHANGELOG.md` 从不随包发布**。所以线上所有 0.4.x 用户看到的都是硬编码的 **0.2** 更新日志 | 改写后搬 | 逻辑保留但**删掉硬编码 0.2 兜底**：改成读内嵌资源（`CHANGELOG.md` 设为 `EmbeddedResource`），读不到就显示「见发布页」。这修掉一个发布约定与代码不一致的真问题 |
| adb 查找：候选路径枚举 | VDH.Extra.cs:262-297 | `EnumerateAdbCandidates()` | `Environment.GetEnvironmentVariable` / `SpecialFolder` | 覆盖全面且顺序合理：exe 同目录 → 上一级 `analysis\apk_patch\quest_adb_tools`（最多回溯 6 层）→ 本机 Android SDK → `ANDROID_HOME`/`ANDROID_SDK_ROOT` → VIVE Hub → `PATH` 逐项。注释「never fall back to a bare adb on PATH」是踩过坑的记录（CHANGELOG 0.4.4） | 改写后搬 | 候选顺序**逐条保留**，删掉 `analysis\apk_patch\quest_adb_tools` 与两条 `D:\Project\...` 私货（`:271-275`）。WPF 侧：`AdbLocator.Enumerate()` 保持 `IEnumerable<string>` 惰性枚举，落地为 `AdbLocator.LocateAsync() -> AdbLocation?`（含来源说明，供 UI 显示「在哪找到的」） |
| adb 查找：结果缓存 | VDH.Extra.cs:239-242, 244-260 | `FindAdb()` / `FindAdb(bool prompt)` | `AppCfg` / `AppCfg.Save()` | 缓存命中就返回并回写 `cfg.AdbPath`。副作用：每次 `FindAdb` 都会触发 `cfg.Save()` + `ShowAdbPath()`，即一次「查找」会改磁盘。`:239` 的无参重载是死代码 | 改写后搬 | 缓存语义保留。拆成 `AdbLocator`（纯查找，无副作用）+ `AdbPreference`（读写 `config.json` 的 `AdbPath`）；删掉无参重载。**新工程形态**：`AdbLocator.LocateAsync` 是 `Task<AdbLocation?>`，`AdbLocation` 记录 `Path` + `Source`（`Configured`/`SdkEnv`/`PathEnv`/`Bundled`/`Downloaded`），诊断项直接把 `Source` 显示出来 |
| adb 交互：询问 / 浏览 | VDH.Extra.cs:299-304, 306-341, 343-360 | `ShowAdbPath()` / `AskAdb(bool)` / `BrowseAdb()` | `MessageBox` / `OpenFileDialog` | `AskAdb` 用三态 YesNoCancel 表达三选项（用第一个 / 自己选 / 下载官方），设计好；但 `:329` 两个分支返回同一个 `MessageBoxButtons.YesNoCancel`，是冗余条件；`:310` 用 `found.Exists(x => …)` 做去重，O(n²) | 改写后搬 | 三选项交互模型**保留**，载体换 WPF：WinForms `MessageBox` → `Microsoft.Win32` 没有多选项框，用一个小 `AdbChoiceDialog`（或在主窗口内联一个「选择 adb 来源」区）；`FolderBrowserDialog` → WPF 没有内置，用 `Microsoft.WindowsAPICodePack` 或让用户在只读 TextBox 里粘贴路径 + 校验文件存在。`OpenFileDialog` → `Microsoft.Win32.OpenFileDialog`（同一命名空间，几乎逐字） |
| adb 下载（Google platform-tools） | VDH.Extra.cs:362-398 | `DownloadAdb()` | `WebClient` / `ZipFile` / `ServicePointManager` | 来源校验做对了一半：`:366` 校验 host 必须是 `dl.google.com` 且 scheme 为 HTTPS。三个问题：① `Directory.Delete(destDir, true)` 直接删掉用户已有的 platform-tools 再解压，没有备份；② 下载的 zip **不做任何 SHA-256 校验**（`dl.google.com` 的 `platform-tools-latest-windows.zip` 有官方 SHA256 可取）；③ `WebClient` + `ServicePointManager.SecurityProtocol = 3072` 都是过期 API | 改写后搬 | 保留「只允许 dl.google.com + HTTPS」这条安全姿态。改为 `HttpClient` + `HttpCompletionOption.ResponseHeadersRead` 流式落盘 + 进度回调；解压前校验 zip SHA-256；解压到 `%AppData%\VirtualDesktopHelper\platform-tools` 前先备份同名目录（`platform-tools.bak`），失败自动回滚。**WPF 侧**：整个过程是 `IAsyncOperation`，进度绑到进度条；**这条要出现在「修复项」里并明确写「将新增目录 X、删除目录 Y」** |
| adb 命令执行 | VDH.Extra.cs:400-440 | `RunAdb(string, int)` / `RunAdb(string, int, bool)` | `ProcessStartInfo(adb, args)` | **两个真 bug**：① `RunAdb` 整个是同步阻塞，且在 UI 线程调用，最长 600 秒（`HeadsetInstall`），期间窗体假死，`WriteBitrate`/`HeadsetInstall` 只能靠 `Application.DoEvents()` 硬撑；② `:431` 的 `WaitForExit(timeoutMs)` **是死代码**——`:429-430` 已经 `ReadToEnd()` 读完两个流，读完意味着进程已退出，所以超时分支永远不会走。另外先 `ReadToEnd(stdout)` 再 `ReadToEnd(stderr)`，大输出时 stderr 管道写满会死锁。参数是**单字符串拼接**，APK 路径用 `\"` 包但没有转义内部引号 | 改写后搬 | `RunAdb` 是新工程所有头显检测项的地基，必须重写：改 `Task<AdbResult> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)`；`ProcessStartInfo.ArgumentList` 逐参数加，**不再拼字符串**；输出用 `BeginOutputReadLine`/`BeginErrorReadLine` 异步泵，`ct` 取消时 `Kill(entireProcessTree: true)`；超时真实生效。`AdbResult` 记录 `ExitCode`/`Stdout`/`Stderr`/`Elapsed`/`TimedOut`，供诊断项展示 |
| 头显连接状态判定 | VDH.Extra.cs:442-460 | `RefreshHeadset(bool verbose)` | `RunAdb` | 四态判定（device / unauthorized / offline / 无设备）是对的，每态给了用户可执行的下一步（插线、唤醒、允许调试）。判定用 `IndexOf("\tdevice")` 字符串匹配，脆 | 改写后搬 | 四态语义与提示文案**保留**（文案质量高）。判定改用 `adb devices -l` 的行解析：按 `\r\n` 切行、`\t+` 切列，state 取第 2 列，避免 `\tdevice` 在别的上下文误命中。WPF 侧 `Task` + `IProgress<string>`，输出进只读 `TextBox` 的 `AppendText`（走 Dispatcher） |
| 头显 logcat 拉取 | VDH.Extra.cs:462-471 | `HeadsetLogcat()` | `RunAdb` | 好：`pidof` 拿到 PID 后用 `logcat --pid=` 精确过滤，失败则退回 tag 过滤 `-s Unity:I AndroidRuntime:E ActivityManager:I`。CHANGELOG 0.4.5 记了「不再 dump kernel `audit: rate limit`」——踩坑改过的 | 改写后搬 | 逐字保留这套两级过滤。新工程加第三层：`--pid` 为空时先跑 `pidof` 失败就要在诊断项里显示「应用没在跑」，而不是静默退回全量 logcat |
| 启动 / 停止头显应用 | VDH.Extra.cs:473-483 | `HeadsetStart()` / `HeadsetStop()` | `RunAdb` | 启动用了 `am start -n <pkg>/md5910…VrActivity`，失败再 `monkey -p` 兜底，兜底链设计好。**但包名是错的**：`const string Pkg = "VirtualDesktop.Android"`（`:18`），当前基线包名是 `com.dwgx1.vd.recovered`（证据：`F:/Project/VirtualDesktop/analysis/apk_patch/build_apk_2d_launch.py:38-39` `OLD_PKG='VirtualDesktop.Android'` → `NEW_PKG='com.dwgx1.vd.recovered'`；`capture_diagnostic.bat:6` `set PKG=com.dwgx1.vd.recovered`）。Activity 名 `md59102214312e19799944a61bf7bc2f23e.VrActivity` 是对的（`VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:52`） | 改写后搬 | `monkey` 兜底链**保留**；包名改掉并做成**可配置 + 自动发现**：先用 `pm list packages | findstr virtualdesktop` + `pm list packages | findstr dwgx` 列出候选，再 `dumpsys package <pkg>` 读 `versionName` 与 launchable activity，取匹配基线的那一个。这样工具不写死包名，换一次基线不用改代码 |
| 运行时权限授予 | VDH.Extra.cs:485-510 | `HeadsetGrant()` | `RunAdb` | 好：只授运行时权限（`RECORD_AUDIO` / `READ_EXTERNAL_STORAGE` / `WRITE_EXTERNAL_STORAGE` / `BLUETOOTH_CONNECT`），并按 adb 回显区分 OK / skip（非运行时权限）/ fail（Exception）。文案说明「Internet/Wi-Fi 等安装时权限不用 grant」 | 直接搬 | 四个权限清单与三分支判定逻辑**逐字搬**成 `QuestPermissions.RuntimeOnly`。WPF 侧输出成一张三列结果表（权限 / 结果 / 原始回显），比纯文本行可读；结果表本身就是一条「检测项」的证据 |
| APK 安装（已知文件名路径） | VDH.Extra.cs:512-534 | `HeadsetInstall()` | `RunAdb` | 三个候选文件名（`VirtualDesktop_1.34.22.0_patched.apk` / `signed_v22.apk` / `..\analysis\apk_patch\output\signed_v22.apk`）全是旧基线产物名，当前仓库里已不存在；`install -r -g` 的 `-g`（grant all runtime perms）好用；`:532` 用 `Application.DoEvents()` 撑 UI | 改写后搬 | `install -r -g "<path>"` 的**命令形态保留**，`-g` 的语义在新工程里明确写成「一次性授予所有运行时权限」。路径发现改为「让用户选文件」而非猜死文件名；参数走 `ArgumentList`。`-r -g` 装错包会静默替换现有应用，所以必须**先显示 APK 的 SHA-256 + 版本号让用户确认**再装 |
| APK 安装（SHA 白名单 + 从文件夹批量识别） | VDH.Extra.cs:683-737 | `InstallFromFolder()` / `Sha256File()` | `FolderBrowserDialog` / `ZipFile` | 逻辑设计很好：逐个 APK 算 SHA → 命中 `KnownApk` 标 `[ok]`、未命中标 `[?]` → **认不出的一个都不装** → 命中 superseded 的要二次确认 → 命中 superseded 里的 960 包自动把 `cfg.LastBitrate` 设为 960 | 改写后搬 | 整条流水线（算 SHA → 白名单判定 → 未识别拒装 → superseded 二次确认 → `install`）**保留**，这是旧工程里最负责任的一段。表数据重生成（见上）。`Sha256File` 单独 `直接搬`（见下）；`FolderBrowserDialog` 换 WPF 文件选择或让用户粘贴目录 |
| SHA-256 文件哈希 | VDH.Extra.cs:676-681 | `static string Sha256File(string path)` | `System.Security.Cryptography` | 标准正确写法：`using` + 流式 `ComputeHash` + `BitConverter…Replace("-","")` 出大写 hex。旧工程另有一处重复实现（VDH.cs:933 `SHA256.Create().ComputeHash(File.ReadAllBytes(dest))`，会把整个 APK 读进内存） | 直接搬 | **逐字搬**成 `Hashing.Sha256Hex(string path)`。**唯一改动**：另加一个 `SHA256.hex` 风格的比较辅助，让 OTA 校验用 `==` 而不是子串 `IndexOf`。VDH.cs:933 那处重复实现删掉，统一调本函数（`install -r` 前算一次 1 GB APK 的哈希正是要流式的理由） |
| 诊断转储 + 反馈邮件 | VDH.Extra.cs:536-547, 858-872 | `DiagnosticDump()` / `SendFeedback()` | `Clipboard` / `Uri.EscapeDataString` | 姿态好：dump 落临时文件 + 复制到剪贴板 + `mailto:` 带正文摘要（截断 1600 字符），用户能自己看完整文件。字段只有 6 行（版本 / streamer / adb / apk sha / bitrate / devices），信息量偏少 | 改写后搬 | 「三路交付（文件 + 剪贴板 + mailto）」**保留**。字段扩成新工程的全量诊断：网卡 / 路由 / 防火墙规则命中 / NAT 类别 / Streamer 设置 diff / 头显状态（这是本项目的主价值）。`mailto:` 在 WPF 里仍用 `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})`（`System.Diagnostics.Process` 与 WinForms 无关，逐字可用） |
| OTA：版本号清洗与比较 | VDH.Extra.cs:743-752, 810-826 | `SanitizeVersion()` / `CheckOta()` 头部 | 无 | 清洗逻辑对：只留数字与 `.`、去 `v` 前缀、去首尾 `.`，所以 `VERSION.txt` 里混进杂字符也不会被当版本。比较用 `Version.TryParse` 优先，失败退回字符串比较 | 直接搬 | `SanitizeVersion` **逐字搬**成 `SemVerLite.Sanitize(string)`，纯函数、可单测。比较逻辑搬成 `SemVerLite.IsNewer(remote, local)`：成功解析走 `Version` 比较，**解析失败时不要静默退回字符串比较**——应该报「远端版本号无法解析」让用户知道（见技术债 §2） |
| OTA：下载主机白名单 | VDH.Extra.cs:754-765 | `HostAllowed(string url)` | 无 | 好：必须 HTTPS，且 host ∈ `{api.github.com, github.com, objects.githubusercontent.com, release-assets.githubusercontent.com, *.githubusercontent.com}`；重定向后的 `Location` 也要重新过白名单（`:779`、`:800`），防跳转绕过 | 直接搬 | **逐字搬**成 `DownloadPolicy.HostAllowed(Uri)`。**唯一改动**：`api.github.com` 可以从白名单删掉——代码注释（`:739-740`）明说 0.4.1 起已不用 API，留着只是让可下载源变宽。搬到新工程后由 `OtaPolicy` 持有，**不要**做成「用户可填自定义源」 |
| OTA：HTTP GET / 下载 | VDH.Extra.cs:767-808 | `HttpGet()` / `HttpDownload()` | `HttpWebRequest`（net48；net10 下仅 `warning SYSLIB0014`） | 手工关掉自动重定向、自己跟 `Location`，是为了让每跳都过白名单——这个意图正确，实现方式落后。递归跟重定向**无深度上限**（自指 Location 会栈溢出）。无 User-Agent 之外的重试/退避 | 改写后搬 | 保留「每跳过白名单」的语义，换 `HttpClient` + `HttpClientHandler{ AllowAutoRedirect = false }` 手写跳转循环，**加最大跳数 5**；`HttpGetAsync` / `DownloadFileAsync`（流式 + `IProgress`）；统一 `HttpClient` 单例注入（不要每次 new） |
| OTA：自更新与替换自身 | VDH.Extra.cs:810-856 | `CheckOta(bool interactive)` | `Process.Start` / `File.WriteAllText` | 流程对：从 tag 固定的 URL 取 `SHA256SUMS.txt` 与 `VDH.exe` → 校验 SHA-256（`:837` 子串包含）→ 写一个 `vdh-swap.bat` 做 `copy /y` + `start` → `Application.Exit()`。`:832` 的注释「`/latest/download/` 是 CDN 缓存」是踩过坑的记录（0.4.1→0.4.5）。三个问题：① `:837` 用 `sums.IndexOf(got) < 0` 子串判存在，不是逐行解析；② bat 覆盖正在运行的 exe，若 copy 失败（文件锁）用户就得到一个既没更新也没退出的界面；③ 失败无回滚 | 改写后搬 | 整体保留：**tag 固定 URL**（不用 `latest`）+ **SHA-256 必须出现在 `SHA256SUMS.txt`** + **不自签、不用 API** 三条是本工程的安全底线。改法：SHA 校验逐行解析 `SHA256SUMS.txt` 的 `<hex>  <name>` 格式并比对**文件名**；替换自身改成「下载到 `%TEMP%` → 校验 → 启动一个 updater（自己的 EXE 带 `--apply-update <tmp>` 参数）做 `MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)` 或退到后台再替换 → 替换失败保留原文件并报错」，比 bat 可靠且可测试 |
| LZ4 DLL 自解压 + `AssemblyResolve` 兜底 | VDH.cs:969-1007 | `Program.EnsureLz4()` | `AppDomain.AssemblyResolve` / 内嵌资源 | 0.4.7 的补丁（CHANGELOG）：0.4.6 单文件 VDH.exe 因 `Could not load K4os.Compression.LZ4` 失败，于是把 5 个 dll 作为 `EmbeddedResource` 打包、运行时释放到 `%AppData%\...\lib` 再 `AssemblyResolve` 加载。**在 .NET 10 上整套机制都不需要** | 弃用 | .NET 10 自包含发布本来就带全部依赖 dll，且不再支持 `AppDomain.AssemblyResolve` 兜底。用标准做法：`PackageReference K4os.Compression.LZ4` + `dotnet publish --self-contained`，依赖随产物走。0.4.7 这次的 bug 在新工程里不存在 |
| Program 入口 | VDH.cs:956-1008 | `Program.Main()` | WinForms `Application` | `Application.EnableVisualStyles` / `SetCompatibleTextRenderingDefault(false)` 是 WinForms 专属。`Main` 里 `Directory.CreateDirectory` 两次是重复（`AppCfg.Load` 里又建一次） | 弃用（方法体）/ 直接搬（启动顺序意图） | WPF 入口是 `App.OnStartup` 或 `StartupUri`。**保留**的意图只有一条：启动即确保配置目录存在。把重复的 `CreateDirectory` 收敛成一处 |
| WinForms 全代码 UI（6 个 Tab） | VDH.cs:183-441（Settings / Accounts / About） + VDH.Extra.cs:20-114（Guide / Headset / App） | `MainForm` ctor + 6 个 `BuildXxxTab()` | `System.Windows.Forms` / `System.Drawing` | 布局全靠 `TableLayoutPanel` + `FlowLayoutPanel` 手算行高像素（`RowStyle(SizeType.Absolute, 32)` 之类），字体写死 `Segoe UI 9.5f`；`BuildSettingsTab` 里出现 `grid.Controls.Remove(grid.GetControlFromPosition(0,0)); grid.Controls.Add(lbCodec, 0, 0);` 这类「先放再取再删再放」的绕法（`:281-282`、`:308-309`、`:323-324`…共 6 处）；语言切换靠 `RebuildTexts()` 逐控件重设文案（77 行），维护成本高 | 弃用 | WPF 重新设计，对齐官方 `VirtualDesktop.Streamer.exe` 的控件语言与排版层级（AGENTS.md §3）。**不要**把 WinForms 布局翻译成 XAML——那是纯搬运工作量。唯一值得继承的是 6 个 Tab 的**信息架构**：Home/Settings、Accounts、Guide、Headset、App、About，以及底部固定条（历史下拉 + 载入 + 清空 + 保存 + 出厂 + 重启 + 反馈） |
| 高 DPI / manifest 声明 | app.manifest:1-26 | `app.manifest` | — | 内容对：`asInvoker`（不提权）、5 个 `supportedOS` GUID（Win10/8.1/8/7/Vista）、`dpiAware=true/pm` + `PerMonitorV2` | 直接搬 | **逐字搬**到新 WPF 工程（`<ApplicationManifest>app.manifest</ApplicationManifest>`）。唯一改动：`assemblyIdentity version="0.4.7.0"` 改成由构建注入（MSBuild 属性），别再手写死 |
| 工程与打包约定 | VirtualDesktopHelper.csproj:1-38 | csproj | — | 关键约定：`OutputType=WinExe`、`AssemblyName=VDH`、`AppendTargetFrameworkToOutputPath=false`（产物平铺在 `bin\Release\VDH.exe`，不是 `bin\Release\net48\…`，OTA 才能按固定名下载）；`K4os.Compression.LZ4 1.3.8`；`EmbeddedResource lib\*.dll`；`LangVersion=7.3`、`Nullable=disable`（即放弃新语言特性） | 改写后搬 | `AppendTargetFrameworkToOutputPath=false` 这条**必须保留**（`bin\Release\` 平铺），它是 OTA 按名下载的前提。`LangVersion` 换现代默认（C# 14）、`Nullable` 建议 `enable`；`net48` → `net10.0-windows` + `UseWPF=true`；`EmbeddedResource lib\*.dll` 与 `EnsureLz4` 一起删掉 |
| 手工发布脚本 | `F:/Project/VirtualDesktop/_upstream/vdh/VDH.bat` | `VDH.bat` | — | `if not exist EXE` 才 build，然后 `start` —— 只是本地「双击即用」便捷脚本，与发布无关 | 弃用 | 新工程不需要。发布走 CI（见 `03-release-conventions.md`） |

## 2. 「搬」之后在新 WPF 工程里的形态（逐条）

| 旧版形态（WinForms / net48） | 新工程形态（.NET 10 WPF） | 说明 |
| --- | --- | --- |
| `MainForm : Form` + 6 个 `BuildXxxTab()` 手搭控件树 | `MainWindow.xaml` + 6 个 `View`（`UserControl`）+ MVVM | 布局重新设计，对齐官方 Streamer 观感 |
| `TabControl` / `TabPage`（代码创建） | XAML `<TabControl>` / `<TabItem>` + `DataTemplate` | 同样是 `TabControl`，但从代码搬到声明式 XAML |
| `TableLayoutPanel(ColumnCount=3)` + `RowStyle(SizeType.Absolute, 32)` | `Grid` + `Auto`/`*` 行高，或 `ItemsControl` + `UniformGrid` | 不再手算像素行高 |
| 控件字段 `cmbCodec` / `tbBitrate` / `numMon` | `SettingsViewModel` 上的 `Codec` / `BitrateText` / `MonitorCount` 属性 | `INotifyPropertyChanged` + `IEditableObject` |
| `cmbLang.SelectedIndexChanged += …` 逐控件改文案 | `Loc` 服务 + XAML `Binding` / `DynamicResource` | 消灭 `RebuildTexts()` 那 77 行 |
| `RebuildTexts()` 里 `tabs.TabPages[0].Text = L.T("Home","主页")` | `<TabItem Header="{Binding T[Home]}">` 或资源字典切换 | 索引写死（`[0]`..`[5]`）是旧版最脆的写法之一 |
| `ToolTip.SetToolTip(cmbCodec, SettingHelp("codec"))` | `ToolTip="{Binding Help}"`，Help 来自 `SettingsHelp.For(key)` | 文案本体逐字保留 |
| `System.Windows.Forms.WebBrowser`（IE 内核） | `WebView2`（Chromium）`NavigateToString(html)`，或 `FlowDocument` / Markdown 文档页 | WPF 无 `WebBrowser` |
| `FolderBrowserDialog`（选文件夹） | WPF 无内置：`Microsoft.WindowsAPICodePack.Shell` 或「粘贴路径 + 存在性校验」 | 不要为了一个对话框引第三方包 |
| `OpenFileDialog`（选 APK） | `Microsoft.Win32.OpenFileDialog` | 同一命名空间，代码几乎逐字可用 |
| `MessageBox.Show(this, text, "VDH", MessageBoxButtons.YesNo)` | WPF `MessageBox` 在 `System.Windows`（同名但不同程序集），三态要用自建对话框 | 别直接 `using System.Windows.Forms` |
| `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})` | **完全相同**（`System.Diagnostics.Process`，与 UI 框架无关） | `OpenUrl` 逐字搬 |
| `new ProcessStartInfo(adb, "shell pm grant " + pkg + " " + perm)` 字符串拼接 | `new ProcessStartInfo(adb)` + `psi.ArgumentList.Add("shell")` / `.Add("pm")` / `.Add("grant")` / … | `ArgumentList` 免注入、免转义；带空格/引号的路径与 APK 名直接作为**一个**参数加进去 |
| `Application.DoEvents()` 撑长任务 | `async` / `await` + `IProgress<T>` + `CancellationToken` | `DoEvents` 会让用户在补丁进行中点第二个按钮 |
| `RunAdb(args, timeoutMs)` 同步 | `Task<AdbResult> AdbClient.RunAsync(IReadOnlyList<string> args, TimeSpan, CancellationToken)` | 真超时、真取消、流式读 stdout/stderr |
| `AppCfg.Load()/Save()` + `JavaScriptSerializer` | `ConfigStore` + `System.Text.Json`（原子写 + 解析失败显式报错） | `JavaScriptSerializer` 在 net10 不存在（实测 `error CS0234`） |
| `JavaScriptSerializer.Deserialize<Dictionary<string,object>>` 读 StreamerSettings | `JsonDocument.Parse` + `GetBool/GetInt/GetStr/GetObj` 扩展方法 | 保留「未知键原样带回写」语义 |
| `Pretty()` / `FormatJsonValue()` 手写缩进 | `Utf8JsonWriter` + `WriteRawValue` 递归 | 类型保真，人可读缩进，不丢键 |
| `System.Web.Script.Serialization` | `System.Text.Json` | net48 专属，net10 移除 |
| `HttpWebRequest` + 手工跟 `Location` | `HttpClient` + `AllowAutoRedirect=false` + 自写跳转循环（上限 5 跳） | `HttpWebRequest` 在 net10 只是 `warning SYSLIB0014`，能编但已废弃 |
| `WebClient.DownloadFile` | `HttpClient.GetAsync(..., ResponseHeadersRead)` + 流式复制 + 进度 | 顺带能做真正的进度条与取消 |
| `AppDomain.CurrentDomain.AssemblyResolve` + 内嵌 LZ4 dll | 无需：标准 `PackageReference` + 自包含发布 | .NET 10 不走这条路 |
| `Application.EnableVisualStyles()` / `SetCompatibleTextRenderingDefault(false)` | WPF 不需要 | — |
| `doubleBuffered = true` / `Application.DoEvents` | WPF 渲染线程模型不同，无需 | — |
| `Program.Main` + `[STAThread]` | `App.xaml` 的 `Startup` 事件 | — |
| 硬编码 `C:\Program Files\...` / `C:\ProgramData\...` | `Environment.GetFolderPath` 拼装 + 注册表 fallback | 见主表「路径常量集」 |
| `const string Pkg = "VirtualDesktop.Android"` | `HeadsetLocator.DiscoverAsync()`：`pm list packages` + `dumpsys package` 自动发现 | 当前基线包名是 `com.dwgx1.vd.recovered` |
| `Catalog.KnownApk` 四条旧 SHA | 按当前基线 APK 重新生成的 `ApkAllowList` | 旧 SHA 全部作废 |
| `Catalog.Lines` 三行静态数组 | 从内嵌 JSON 加载的 `QuestLineCatalog` | 加版本行只改数据 |

## 3. 旧版**没有做**的事（缺口清单 = 新项目的价值）

旧 VDH 是一个「Streamer 设置编辑器 + APK 码率补丁器 + 头显遥控器」。它**没有**：

| 缺口 | 旧版现状（证据） | 新工程该补什么 |
| --- | --- | --- |
| **PC 侧网络诊断** | 全文无一处 `netsh` / `Get-NetFirewallRule` / `Test-NetConnection` / `IPGlobalProperties`。`grep -nE "netsh|Firewall|Ping|RouteTable|Socket" *.cs` 0 命中 | 网卡枚举与状态（虚拟网卡 vs 物理网卡）、路由表、AP 隔离线索、`advfirewall` 出入站规则命中检查、mDNS/broadcast 发现端口是否被挡 |
| **头显可达性判定** | 只在 `RefreshHeadset()`（VDH.Extra.cs:442-460）判「adb 上有没有设备」。**无任何 PC↔头显网络层探测** | 从 PC 侧对头显 IP 的 ping / 端口可达 / UDP 发现响应检测 |
| **Streamer 参数面** | 只覆盖 8 个键 + `Accounts` + `ProtectedComputerID` + `PreferredCodec`/`CodecName`（VDH.cs:78-81、`:742-759`）。本机实测 `C:\ProgramData\Virtual Desktop\StreamerSettings.json` 有 **12 个顶层键**：`ServerRotation` `ProtectedComputerID` `Accounts` `AutoAdjustBitrate` `ShowPairingRequests` `ShownH264PlusWarning` `PreferredCodec` `DeviceName` `CodecName` `VideosRootPath` `LastConnectDate` `OpenXRRuntime` `MonitorCount` `DontWarnApps` —— **`AutoAdjustBitrate` 与 `OpenXRRuntime` 旧版完全没碰** | 参数面补全 + 每个键的默认值/合法范围/改动后果/可回滚 |
| **配对故障诊断** | 只在文案里提「账户名对得上时局域网发现仍能连」（VDH.Extra.cs:151-152），无检测 | 「头显找不到 PC / PC 发现不到头显」的分因诊断：账号是否已加、账号名是否与 APK 烤名一致、AP 隔离、防火墙、Streamer 是否在跑 |
| **码率上限的实际验证** | `ReadRepoBitrate()`（VDH.Extra.cs:664-674）读的是 `..\analysis\apk_patch\patched_assemblies\VirtualDesktop.Mobile.dll`，而该路径在当前 `F:` 树里**不存在**（实测 `ls` 报 No such file，`patched_assemblies/` 里只有 `Xenko.dll` / `VirtualDesktop.Net.dll` / `Xenko.Native.dll` / `Xenko.OpenXR.dll` / `System.ComponentModel.TypeConverter.dll`）。所以「当前 IL 上限」这一栏在发布版里恒为空 | 从头显侧实读：装好的 APK 算 SHA → 匹配 profile → 显示实际码率上限；或 `adb pull` 出 APK 现场解析 |
| **patched 基线适配** | 全部围绕 1.34.22.0 EN/ZH LAN 包；包名 `VirtualDesktop.Android` 对当前基线无效 | 以「去联网鉴权 + 44 处 IL 补丁 + JIT」基线为目标，包名/版本/activity 自动发现 |
| **检测项 / 修复项框架** | 没有「检测项」抽象，只有分散的按钮动作；没有统一结果模型 | 每条检测 = 一条命令 + 一个可读参数键 + 通过/失败/未验证三态 + 一句话解释 + 一键修复（先备份后改可回滚） |
| **测试** | 全仓库无测试工程（`wc -l` 只有 3 个 .cs + 1 个 .csproj，无 `*Test*`），CI 也不跑测试（`ci.yml` 只有 `dotnet build`） | `Sha256Hex` / `SanitizeVersion` / `HostAllowed` / `FormatList` / `CodecSelector` / `AdbLocator` 这些纯函数全部可单测 |
| **结构化日志** | 只有往 TextBox 追加的纯文本 dump（`DiagnosticDump()` VDH.Extra.cs:536-547） | 结构化诊断条目（项名 / 命令 / 原始输出 / 判定 / 建议），可导出 |
| **CI 产物与自动发布** | `ci.yml` 只 `dotnet build`；7 个 release 全是手工上传 | 见 `03-release-conventions.md` |
| **无障碍 / 键盘 / 深色** | 无 | 与官方 Streamer 观感对齐的一部分 |

## 4. 复用决策统计

按 §1 主表的 45 条数据行逐条数出（脚本核对：`python` 按 `|` 切列，取第 6 列）：

| 决策 | 条目数 | 代表 |
| --- | --- | --- |
| 直接搬 | 13 | `Codec` 表、`PlatformNames`、`SyncCodecCombo`、`FormatList`、`Sha256File`、`SanitizeVersion`、`HostAllowed`、`SettingHelp` 文案、`app.manifest`、历史/出厂备份机制、Quest 版本目录表、`DetectStreamerVer` |
| 改写后搬 | 25 | `RunAdb`（async + ArgumentList）、`CheckOta`（HttpClient + updater）、`DownloadAdb`（校验 + 回滚）、`StreamerSettings` 读写（System.Text.Json + 原子写）、`CapPatch`（去掉签名链）、`RemoveSelectedAccount`（补落盘）、`RestartStreamer`（等待退出） |
| 弃用 | 4 | `GuideBody`、`EnsureLz4`、`Program.Main` 方法体、`VDH.bat` |
| 拆分（一半弃用 / 一半搬） | 2 | `KnownApk`（表弃用 / 机制搬）、`Program` 入口（方法体弃用 / 启动意图搬），明细见下 |

拆分项明细：

- `KnownApk` 白名单 → **表弃用**（四个 SHA 全是旧 1.34.22.0 LAN 包，当前基线 SHA 完全不同）+ **机制搬**（`Dictionary<sha,描述>` + 拒装未知 + superseded 二次确认）。
- `Program` 入口 → **`Main` 方法体弃用**（WinForms `Application.*`）+ **启动顺序意图搬**（启动即确保配置目录存在，且收敛到一处）。
- 另有两行的决策列写在「质量评价」里而不在决策列（`SecretKeys`/`SettingOrder` 死表 = 弃用、硬编码包名/绝对路径 = 归入各所属功能的改写后搬），不重复计入上表。

代码量估计：可复用逻辑约 **700 行**（其中直接搬约 250 行），需重写约 1400 行（WPF UI + 新的网络/头显诊断层），可弃用约 900 行（WinForms UI + 死代码 + 自解压机制）。

## 5. 未验证项

- `[未验证]` `Catalog.Codecs` 的 4 项是否覆盖本机 Streamer 1.34.x 全部可选项（JSON 里出现 `PreferredCodec=11`，本机 `CodecName="AV1 10-bit"`，旧版注释提到「6/11 if JSON already has them」）——需要 `04-streamer-settings/` 用反编译源码给出完整枚举。验证条件：`F:/Project/VirtualDesktop/localization/desktop/decompiled_streamer/` 里 `PreferredCodec` 的取值定义。
- `[未验证]` `CapPatch` 的三处 IL 偏移 `0x13B0C` / `0x14E45` / `0x2BFE3`（1.34.22.0，Mobile.dll size 544256）在当前 patched 基线上是否仍然有效——旧版只在 README 与 CHANGELOG 里自述，`F:/Project/VirtualDesktop/analysis/` 全树 grep `0x13B0C` 无命中（只命中无关的 `System.Private.CoreLib/String.cs:1356` 注释）。验证条件：对当前基线 APK 做一次 XALZ 解析 + 反汇编确认。
- `[未验证]` keystore 别名 `vdpatch` / 口令 `vdpatch2026`（由 `BitrateApk.cs:43-44` 的 XOR 0x5A 字节数组解出，本地已解码确认）是否指向一个仍在使用、且**允许分发**的签名密钥。本工程 AGENTS.md 第 2 条禁止仓库含 keystore，故按「不搬」处理。
- `[未验证]` `HttpGet` 的重定向深度在 GitHub 实际链路上是多少（实测无网络路径可跑）；判断「无上限递归是否真会栈溢出」需要构造自指 `Location` 的服务器。
