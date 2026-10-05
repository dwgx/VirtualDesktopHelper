# 07-02 Patch Profile 版本 × 补丁项交叉表

数据源：`reference/vdapkpatcher/profiles/**`，全部用 `python json.load` 解析 `profile.json` 实测读出，非人工抄写。
**这一节是后续测 Quest 侧 IL 常量的依据**：它给出「哪些方法名/状态机编号在哪个版本存在」，以及**哪些编号会随版本漂移**。

## 0. 先说三条读表规则（否则会误用）

1. **`methodPatches` 里没有任何「offset」字段。** 定位键只有 5 个：程序集名、完整类型名、方法名、参数个数、operation（`Configuration/PatchProfile.cs:39-44` 的 `MethodPatchSpec`）。
   offset 只出现在**翻译 CSV** 里（`managed-strings.csv` 的 `BaseILOffset`），而 `AGENTS.md:239` 明说实现**不用**这些 offset，只当审计信息。
   → **想拿 IL 偏移必须自己 dump IL，不能从 profile 抄。**
2. **定位靠「类型全名 + 方法名 + 参数个数」三键，且要求唯一**（`TokenPreservingAssemblyPatcher.cs:73-78`：0 个抛、>1 个也抛）。
   所以同一个补丁项在不同版本能只改一个字段就迁移 —— 前提是那一个字段漂移了。
3. **写回必须保留 RID**：`MetadataFlags.PreserveRids`（`TokenPreservingAssemblyPatcher.cs:59`），因为 Xamarin native typemap 按 RID 引用 managed 类型（`AGENTS.md:165`）。
   任何「我读了但没改」的场景不受此约束 —— 这是我们只读方案能成立的前提。

## 1. APK profile 身份表（`profile.json` 顶层字段）

| profile 目录 | id | versionName | inputSha256（数量） | packageName | assemblyStoreRelativePath | bundleRelativePath | methodPatches | overlays/additions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `vd-1.34.18` | `virtual-desktop-1.34.18.0-zh` | `1.34.18.0` | 2 | `VirtualDesktop.Android` | `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so` | `assets/data/db/bundles/default.bundle` | 10 | 8 / 8 |
| `vd-1.34.19` | `virtual-desktop-1.34.19.0-zh` | `1.34.19.0` | 1 | 同上 | 同上 | 同上 | 10 | 8 / 8 |
| `vd-1.34.20` | `virtual-desktop-1.34.20.0-zh` | `1.34.20.0` | 1 | 同上 | 同上 | 同上 | 11 | 8 / 8 |
| `vd-1.34.21` | `virtual-desktop-1.34.21.0-zh` | `1.34.21.0` | 1 | 同上 | 同上 | 同上 | 11 | 8 / 8 |
| `vd-1.34.22-beta` | `virtual-desktop-1.34.22.0-beta-zh` | `1.34.22.0`（`AGENTS.md:26` 记 `versionCode 10704`） | 1 | 同上 | 同上 | 同上 | 11 | 8 / 8 |
| `vd-1.34.22` | `virtual-desktop-1.34.22.0-zh` | `1.34.22.0` | 1 | 同上 | 同上 | 同上 | 11 | 8 / 8 |

输入哈希（`inputSha256`，逐字节实测）：

| profile | SHA-256 |
| --- | --- |
| `vd-1.34.18` | `09647919c9057801adfedc90d5498f3a28828c21abc4bfbc9c31b4c52865168e` |
| `vd-1.34.18` | `8081401ca24e7d65cfc2ee41d84857f56c7294da6072f87811f2b76bbf24bfab` |
| `vd-1.34.19` | `d67149eca2e9fe3a969da138cfb6491eb14acb57b5fd3da67d6d4bf195f636ea` |
| `vd-1.34.20` | `5704438d006b7fee44ab25839d35c850a36f9f5609dc549479db6c7f142072f8` |
| `vd-1.34.21` | `69025b21913a33b1ef2853900f0b0f4281d3b2090b845d3ffeec30721312e204` |
| `vd-1.34.22-beta` | `a0f74fe190150108c3ac5459aa6018113562daf7f99dc148499ca77ea9592a9b` |
| `vd-1.34.22` | `f37cd70f74eb86fc3a6104aaf905649632023a5458301660556ad4b3f14ad79a` |

`vd-1.34.22-beta` 与 `vd-1.34.22` 逐字段 diff：**只有 `id` 和 `inputSha256` 不同**，其余 11 个字段（含全部 methodPatches、全部 ObjectId、三份 CSV 路径）完全一致。
→ 同版本号下 beta 与正式是两个不同 APK 构建，**profile 必须按 SHA-256 选，不能按 versionName 选**。

## 2. 补丁项 × 版本 交叉表（核心）

`✔` = 该 profile 含此项。类型名按 `profile.json` 的 `type` 字段逐字记录。

| # | operation | 目标程序集 | 目标类型 | 目标方法 | 参数数 | 1.34.18 | 1.34.19 | 1.34.20 | 1.34.21 | 1.34.22-beta | 1.34.22 | 迁移说明 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `BypassOnCreateSignature` | `VirtualDesktop.Android.dll` | `VirtualDesktop.VrApp` | `OnCreate` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改，唯一完全稳定的补丁项 |
| 2 | `ReturnOculusToken` | `VirtualDesktop.Android.dll` | `VirtualDesktop.VrApp` | `CreateAccessTokenAsync` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 同上 |
| 3 | `ReturnOculusTokenPair` | `VirtualDesktop.Android.dll` | `VirtualDesktop.VrApp` | `GetAccessTokenAsync` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 同上 |
| 4 | `ReturnVoid` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.InputSystem/<>c` | `<.ctor>b__1_0` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改 |
| 5 | `ReturnVoid` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.InputSystem/<>c` | `<.ctor>b__1_1` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改 |
| 6 | `ReturnVoid` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.Keyboard/<>c` | `<.ctor>b__61_1` | 1 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改（注意 `b__61_1` 这个编号在 6 个版本里都没漂） |
| 7 | `BypassKeyboardSignature` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.Keyboard` | `LoadContent` | 0 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改 |
| 8 | `ReturnTrue` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.UserSettings` | `get_HasValidIdentity` | 0 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | 六版一字未改 |
| 9 | `ReturnTrue` | `VirtualDesktop.Mobile.dll` | `VirtualDesktop.Mobile.UserSettings` | **`<GetHasValidIdentityAsync>b__459_0`** → **`<GetHasValidIdentityAsync>b__474_0`** | 0 | ✔ `459_0` | ✔ `474_0` | ✔ `474_0` | ✔ `474_0` | ✔ `474_0` | ✔ `474_0` | **1.34.18→19 迁移：lambda 序号 459→474（+15）** |
| 10 | `SkipCloudComputerResults` | `VirtualDesktop.Mobile.dll` | **`...NetworkManager/<GetComputersAsync>d__69`** → **`d__78`** → **`d__79`** | `MoveNext` | 0 | ✔ `d__69` | ✔ `d__78` | ✔ `d__79` | ✔ `d__79` | ✔ `d__79` | ✔ `d__79` | **1.34.18→19：69→78（+9）；1.34.19→20：78→79（+1）；1.34.20 起冻结** |
| 11 | `RaiseVrBitrateLimit` | `VirtualDesktop.Mobile.Shared.dll` | `Xenko.VR.HmdResolutionTypeExtensions` | `GetMaxVRBitrate` | 3 | — | — | ✔ | ✔ | ✔ | ✔ | **1.34.20 新增**，无删改；把 `200000000` → `960000000` |

计数：1.34.18 = 10 项，1.34.19 = 10 项（改 2 个名字），1.34.20 = 11 项（+1），1.34.21/22-beta/22 = 11 项。**与 `AGENTS.md:21-26` 的表完全吻合。**

### operation 语义（读 `TokenPreservingAssemblyPatcher.cs` 得到）

| operation | 实现 | 实际做什么 |
| --- | --- | --- |
| `BypassOnCreateSignature` | `:81-107` | 找到 `VirtualDesktop.Core.CurrentProcess.Kill` 调用，从该处 NOP 到方法末尾第一个 `ret` —— 即删掉「签名校验失败就杀进程」整块 |
| `ReturnOculusToken` | `:238-290`（`pair:false`） | 整个方法体重建为：`AsyncTaskMethodBuilder.Create` → `ldstr "vrzwk"` → `newobj VirtualDesktop.Interfaces.PlatformAccessToken(string,string)` → `SetResult` → `get_Task` → `ret` |
| `ReturnOculusTokenPair` | `:238-290`（`pair:true`） | 同上，但额外 `dup` + `newobj ValueTuple'2<string,string>::.ctor` 造元组 |
| `ReturnVoid` | `:200-211` | 方法体换成 `ret`（校验返回类型必须 `void`） |
| `ReturnTrue` | `:200-211` | 方法体换成 `ldc.i4.1; ret`（校验返回类型必须 `bool`） |
| `BypassKeyboardSignature` | `:109-143` | 定位 `CanScroll` 陷阱前的保护分支并 NOP 掉分支体，再 NOP 掉 `Finish` 调用前的 `ldsfld; castclass` 参数生成序列 |
| `SkipCloudComputerResults` | `:145-176` | 在 `MoveNext` 里以 `ReplaceWithBranch`(`:194-198`) 把 `americaRegistryTask` 与 `europeRegistryTask` 两个云端注册块**直接跳到 `get_HasValidIdentity` 之后**，即跳过全部云端结果 |
| `RaiseVrBitrateLimit` | `:213-236` | 要求方法返回 `int`；数 `ldc.i4 200000000` 命中数，**必须恰好 4 处**（`:217`）否则抛；全部改为 `960000000` |

`ReturnOculusToken`/`ReturnOculusTokenPair`/`ReturnTrue`(`get_HasValidIdentity`)/`BypassOnCreateSignature`/`BypassKeyboardSignature` 这五项是**付费/身份鉴权绕过**，与 VDHelper 硬规则 1 直接冲突，功能层面一律不取。

## 3. 状态机编号（`d__NN` / `b__NN_M`）迁移表 —— 测 IL 时的头号坑

从 `methodPatches` + `managed-strings.csv` 的 `Method` 列抽出的全部出现编号，按版本：

| 编译生成类型 | 1.34.18 | 1.34.19 | 1.34.20 | 1.34.21 | 1.34.22-beta | 1.34.22 |
| --- | --- | --- | --- | --- | --- | --- |
| `NetworkManager/<GetComputersAsync>d__NN`（补丁项 10 目标） | `d__69` | `d__78` | `d__79` | `d__79` | `d__79` | `d__79` |
| `<ConnectToComputerAsync>d__NN::MoveNext`（CSV） | `d__66` | `d__74` | `d__74` | `d__74` | `d__74` | `d__74` |
| `<RefreshComputersAsync>` / `<RefreshComputersInternalAsync>d__NN::MoveNext`（CSV） | `d__65` | `d__77` | `d__78` | `d__78` | `d__78` | `d__78` |
| `<OnVideoBufferingClick>d__NN`（CSV） | `d__128` | `d__128` | `d__128` | `d__128` | `d__131` | `d__131` |
| `<OnTrackControllersClick>d__NN`（CSV） | `d__130` | `d__130` | `d__130` | `d__130` | `d__133` | `d__133` |
| `<OnForwardTrackingDataClick>d__NN`（CSV） | `d__131` | `d__133` | `d__133` | `d__133` | `d__134` | `d__134` |
| `<OnDeleteClick>d__NN`（CSV） | `d__124` | `d__124` | `d__124` | `d__124` | `d__124` | `d__124` |
| `UserSettings.<GetHasValidIdentityAsync>b__NN_0`（补丁项 9 目标） | `b__459_0` | `b__474_0` | `b__474_0` | `b__474_0` | `b__474_0` | `b__474_0` |
| `InputSystem/<>c::<.ctor>b__1_0` / `b__1_1`（补丁项 4/5） | 1_0 / 1_1 | 同 | 同 | 同 | 同 | 同 |
| `Keyboard/<>c::<.ctor>b__61_1`（补丁项 6） | `61_1` | 同 | 同 | 同 | 同 | 同 |
| `<>c__DisplayClass83_0::<RefreshVideos>b__0`（CSV） | `83_0` | 同 | 同 | 同 | 同 | 同 |

**读法（关键）**：`d__NN` 编号是 Roslyn 按**方法在类型内的声明顺序**生成的，跟版本无关的代码增量无关，只跟「这个编译单元之前新增了多少个带 async/lambda 的成员」有关。所以：

- 1.34.19 一次性 +9（66→74、65→77、69→78、459→474），说明这一版在 `NetworkManager` 之前插入了约 9 个状态机生成成员。
- 1.34.20 只 +1（`<GetComputersAsync>` 78→79、`<RefreshComputers*>` 77→78），说明只加了一个状态机成员。
- **1.34.20 → 1.34.22-beta 全线只漂 `<OnVideoBufferingClick>`/`<OnTrackControllersClick>`/`<OnForwardTrackingDataClick>` 三个**（128→131、130→133、133→134），而 `d__79` / `d__78` 纹丝不动。
  → 「`GetComputersAsync` 在 1.34.20–1.34.22 稳定」是**该三个版本编译器顺序恰好未变**的结果，不是「这些编号不会变」的保证。`AGENTS.md:413` 专门警告过这点（「`d__78` 到 `d__79` 的漂移」）。

**对我们测量的直接影响**：任何「找 `NetworkManager/<GetComputersAsync>d__*` 然后取 IL」的脚本，**不要按名字匹配编号**，应按「类型名去掉 `d__NN` 后缀再前缀匹配」或干脆按方法名 `MoveNext` + 所属类型 `NetworkManager` 定位。

### 一处 profile 自身的不一致（不影响构建，但要知道）

`methodPatches[9]` 的目标类型从 1.34.20 起是 `.../<GetComputersAsync>d__79`，但同版本 `managed-strings.csv` 里对应 26 条文案仍标为 `'<GetComputersAsync>d__78'`：

| profile | patch 目标 | CSV 标签 | 是否一致 |
| --- | --- | --- | --- |
| `vd-1.34.18` | `d__69` | `'<GetComputersAsync>d__69'` | ✔ |
| `vd-1.34.19` | `d__78` | `'<GetComputersAsync>d__78'` | ✔ |
| `vd-1.34.20` | `d__79` | `'<GetComputersAsync>d__78'` | ✘ |
| `vd-1.34.21` | `d__79` | `'<GetComputersAsync>d__78'` | ✘ |
| `vd-1.34.22-beta` | `d__79` | `'<GetComputersAsync>d__78'` | ✘ |
| `vd-1.34.22` | `d__79` | `'<GetComputersAsync>d__78'` | ✘ |

原因是 CSV 的 `Method` 列不是定位键 —— `ApplyStringPatches`（`:292-343`）把 `Method` 按 `::` 切成类型名/方法名后，**只比 `pair.Type.Name`**（dnlib 的 `TypeDef.Name`，即不含命名空间与外层的短名），`d__78` 与 `d__79` 短名不同，本应匹配失败；失败后走 `:329-335` 的**全模块兜底**（按原文唯一匹配，>1 处即抛）。
这说明 1.34.20 起那 26 条 `GetComputersAsync` 文案是靠「全模块唯一匹配」兜底成功的，不是靠类型名。
**教训**：这套 CSV 的 `Method` 列在 4 个版本上是**过期的**；我们若要拿它当定位索引，必须先按当前版本重取，否则会误判。

## 4. CSV 规模与页面清单

| profile | `managed-strings.csv` 行数 | 不同方法数 | `bundle-strings.csv` 行数 | `android-strings.csv` |
| --- | --- | --- | --- | --- |
| `vd-1.34.18` | 138 | 28 | 221 | 2 行（`app_name` → `Virtual Desktop`） |
| `vd-1.34.19` | 137 | 28 | 216 | 2 行（`app_name` → `Virtual Desktop (VD串流中文版)`） |
| `vd-1.34.20` | 139 | 28 | 219 | 2 行（同 1.34.20） |
| `vd-1.34.21` | 143 | 28 | 219 | 2 行 |
| `vd-1.34.22-beta` | 143 | **29** | 219 | 2 行 |
| `vd-1.34.22` | 142 | 28 | 219 | 2 行 |

`vd-1.34.22-beta` 多出的第 29 个方法是 `VrApp::DeterminePlatform`，唯一一条 `ldstr "\x20Beta -\x20"`（`IL_0164`）—— beta 构建的版本后缀。

`bundle-strings.csv` 只覆盖 6 个 Xenko 页面（`vd-1.34.22` 实测）：

| Page | 条数 |
| --- | --- |
| `UI/ControlPanel/Page` | 172 |
| `UI/PerformanceOverlay/Page` | 24 |
| `UI/DesktopToolBar/Page` | 10 |
| `UI/ScreenToolBar/Page` | 9 |
| `UI/Keyboard/Page` | 3 |
| `UI/VideoToolBar/Page` | 1 |

→ **桌面画面（streamed desktop）不在 bundle 里**，`default.bundle` 只管 Quest 端 UI 覆盖层与我们关心的控制面板。控制面板里的发现/连接 UI（`UI/ControlPanel/Page`）才是这条主线的 UI 层。

CSV 表头（`AGENTS.md:234-236`，实测一致）：

```text
managed-strings.csv: Assembly,Method,BaseILOffset,ZhILOffset,Original,Localized
bundle-strings.csv:  Page,BaseOffset,ZhOffset,Original,Localized
android-strings.csv: Name,Original,Localized
```

**`BaseILOffset` / `ZhILOffset` / `BaseOffset` / `ZhOffset` 四列当前实现完全不用**（`AGENTS.md:239`），只是审计信息。但它们是官方 APK 的 IL 偏移快照，可以拿来**交叉校验我们 dump 出来的偏移**。抽样验证（1.34.22，`<GetComputersAsync>d__78`）：

| BaseILOffset | 英文原文（截断） |
| --- | --- |
| `IL_028b` | `Not connected to Wi-Fi` |
| `IL_02b0` | `Meta servers unreachable` |
| `IL_0604` | `Virtual Desktop servers partially unreachable, some computers might not appear` |
| `IL_060b` | `Virtual Desktop servers unreachable, only showing local computers` |

这批文案本身就是**发现失败诊断的权威分类表**：Wi-Fi 未连 → 各平台服务器不可达 / 不响应 / 出问题 → 全部不可达只显示本地电脑 → 「No computer found」/「Make sure your computer is running the Streamer app」。可直接变成我们的检测项文案映射。

## 5. Streamer profile（`streamer-1.34.21` / `1.34.22`）

| 字段 | `streamer-1.34.21` | `streamer-1.34.22` |
| --- | --- | --- |
| `id` | `streamer-1.34.21-zh-cn` | `streamer-1.34.22-zh-cn` |
| `version` | `1.34.21.0` | `1.34.22.0` |
| `inputSha256` | `CC1276B45C9DD5FC31C7C79406397D184622BA5B33D5200D9B416A1F9D535A9A` | `6BFEC9E4E62509F4FDB0EC21C144B4584F9450701DCF5BD756D6FD2AEC7CBB51` |
| `bindingActionProviderToken` | `0x06000575` | `0x06000575`（**两版相同**，`AGENTS.md:64` 确认） |
| `aboutCredit.label` / `.value` | `Vrzwk汉化组:` / `vrzwk.com` | 同 |
| `baml-strings.csv` | 95 行 / 4 个资源 | **逐字节相同**（`cmp` 实测 IDENTICAL） |
| `managed-strings.csv` | 114 行 / 78 个不同 token | **逐字节相同**（`cmp` 实测 IDENTICAL） |
| `binding-actions.csv` | 9 条 | **逐字节相同**（`cmp` 实测 IDENTICAL） |

BAML 资源分布（实测）：`mainwindow.baml` 85 条、`appschecker/appscheckerwindow.baml` 6 条、`addaccountwindow.baml` 3 条、`messageboxwindow.baml` 1 条。

`binding-actions.csv` 的 9 个枚举名（可直接用作我们读 Streamer 的字段名清单）：
`SwitchMonitor`、`ToggleVRMode`、`ToggleVRPassthrough`、`EnableVRPassthrough`、`DisableVRPassthrough`、`ToggleHandPassthrough`、`ToggleDeskPassthrough`、`TogglePerformanceOverlay`、`ToggleFoveatedStreaming`。

`StreamerPatchLayout`（`StreamerLocalDiscoveryPatcher.cs:250-259`）的版本 × token 表：

| token 字段 | 1.34.19 | 1.34.20 | 1.34.21 | 1.34.22 |
| --- | --- | --- | --- | --- |
| `ConnectionStateMachineToken` | `0x060001a2` | `0x060001a4` | `0x060001a5` | `0x060001a5` |
| `AppOnStartupToken` | `0x0600052b` | `0x0600052f` | `0x06000530` | `0x06000530` |
| `StartLocalDiscoveryToken` | `0x060000e7` | `0x060000e8` | `0x060000e8` | `0x060000e8` |
| `StopLocalDiscoveryToken` | `0x060000e8` | `0x060000e9` | `0x060000e9` | `0x060000e9` |
| `CheckForUpdateToken` | `0x06000509` | `0x0600050d` | `0x0600050e` | `0x0600050e` |
| `LocalDiscoveryReceiveLoopToken` | `0x060213d1` | `0x060213d6` | `0x060213d3` | `0x060213d3` |
| `GetIPv6InterfaceIndicesToken` | `0x060214bd` | `0x060214c5` | `0x060214c2` | `0x060214c2` |
| `VerifyExecutableSignatureToken` | `0x0602170a` | `0x06021712` | `0x0602170f` | `0x0602170f` |
| `ExpectedExternalStopCalls` | 6 | 6 | 2 | 2 |

**token 增量完全不规律**：`StartLocalDiscovery` 从 `e7`→`e8`（+1），而 `LocalDiscoveryReceiveLoop` 从 `d1`→`d6`（+5）再**倒退**回 `d3`（−3）。这印证 `AGENTS.md:423`「不要假设 token 使用固定增量」。**每次版本升级必须重新 dump 验证**，不能线性外推。

1.34.22 整列已在本机官方 EXE 上实测命中 8/8（见 `01-capability-inventory.md` 实测段）。

## 6. 给 `03-quest-parameters` 的移交结论

1. **不要从这些 profile 抄 IL offset**，它们不存 offset。要 offset 就自己 dump。
2. **不要相信 `managed-strings.csv` 的 `Method` 列**（4 个版本过期，见 §3 末）。
3. **唯一可直接复用的定位索引**是 §2 交叉表里那 6 个稳定补丁项的方法全名 —— 但它们全是鉴权绕过点，我们**不用它们**，只用它们证明「这 6 个方法名在 6 个版本都没变，可作为版本无关的存在性探针」。
4. **`d__NN` 一律不要硬编码。**