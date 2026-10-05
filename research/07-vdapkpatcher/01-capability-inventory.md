# 07-01 VdApkPatcher 能力矩阵

盘点对象：`reference/vdapkpatcher/`（questhelper 团队 `VdApkPatcher.zip` 解压结果，只读快照）。
快照内**没有 `.git`**，因此没有 commit 历史、作者署名或上游仓库地址可查（见 `03-license-and-reuse.md`）。

规模：C# 源码 21 个文件 / 3553 行（`wc -l` 实测），加 8 个 profile 目录。
两个功能面：① Android APK 汉化与补丁构建；② Windows `VirtualDesktop.Streamer.exe` 局域网发现补丁与 WPF 汉化。
声明的功能面见 `AGENTS.md:7-11`。

## 能力矩阵

| 模块 | 干什么 | 关键类/方法(file:line) | 依赖 | 对VDHelper的价值 | 理由 |
| --- | --- | --- | --- | --- | --- |
| `Program.cs` | CLI 分派，19 个子命令（build / inspect-store / inspect-method / inspect-int / patch-streamer-local 等） | `Program.Main:10`；`InspectStore:71`；`InspectMethod:144`；`PatchStreamer:382` | 无（仅 BCL） | 无 | VDHelper 是 WPF 工具，不做命令行分派；且它的 arg 解析是手写 `Dictionary` 逐对取值（`Program.cs:146-148`），质量低于 `System.CommandLine` |
| `Apk/ApkBuildPipeline.cs` | APK 全流程：校验哈希 → apktool d → 补丁程序集 → 改 bundle → 删 AOT → 回编译 → zipalign → apksigner → 验签 | `RunAsync:21`；`ValidateDecodedVersion:135`；`PatchManagedAssemblies:143`；`PatchBundles:170`；`ConfigureStoredBundles:196`；`RemoveAotImages:255` | apktool.jar / zipalign.exe / apksigner.jar / keystore、Java | 无 | 我们的边界是「检测/诊断/修复网络与配置」，不做 APK 重打包（`AGENTS.md:2` 硬规则 2：不分发官方二进制，工具本体不产出 APK） |
| `Apk/AndroidManifestPatcher.cs` | 校验包名 `VirtualDesktop.Android`，写 `android:debuggable="true"` | `EnableDebuggable:9-20` | System.Xml.Linq | 无 | 与网络诊断无关 |
| `Apk/AndroidResourceTranslator.cs` | 按资源名 + 英文原文严格替换 `res/values/strings.xml`，原文不符即抛 | `Apply:8-31`；`ReadMappings:33` | `Microsoft.VisualBasic.FileIO.TextFieldParser` | 无 | 翻译管线；我们不汉化 |
| `Bundles/XenkoBundleService.cs` | 解 Xenko root/incremental bundle（LZ4）→ 按 ObjectId 提取对象 → 改序列化字符串 → 重建 bundle | `ReadAssetIndex:16`；`TranslateBundle:23-76`；`TranslateChunkStrings:78-128`；`Extract:185-241`；`Pack:243-268`；`CanonicalizeChunk:270-294` | `Xenko.Core` / `Xenko.Core.IO` / `Xenko.Core.Serialization` / native `libcore.dll`（`lib/` 目录**不在快照里**） | 无 | 需要随包分发 Xenko 二进制；我们要判的失败模式是「找不到 PC」，不是「bundle 里某个页面文案错」 |
| `Bundles/FileObjectBackend.cs` | 用本地文件实现 Xenko `IOdbBackend` 只读后端，供 `BundleOdbBackend.CreateBundle` 消费 | `FileObjectBackend:8-40` | `Xenko.Core*` | 无 | Xenko 专用适配层 |
| `Bundles/LinearSearchReadOnlyDictionary.cs` | 44 行 `IDictionary` 只读实现（线性查找），只为满足 Xenko API 形状 | `LinearSearchReadOnlyDictionary:6-44` | 无 | 无 | 纯粹是为了喂第三方接口的垫片；我们自己写代码不该用 |
| `Configuration/BuildOptions.cs` | build 参数解析 + 硬编码默认工具路径 | `Parse:19-67`（默认 `D:\Tools\ApkTool\...`、keystore 密码 `vrzwk.com`）；`PrintUsage:69` | 无 | 无 | 与我们无关，且**绝不能照抄**（含明文口令，见 03 文档） |
| `Configuration/PatchProfile.cs` | APK profile JSON 模型 + 相对路径解析 | `Load:22-33`；`ResolveProfileFile:35`；`MethodPatchSpec:39` | System.Text.Json | 中（仅作为**数据格式样本**） | JSON 结构本身对我们读 IL 常量没帮助；但「一个 profile 目录 = 一个 APK 版本 = 一份补丁点位表」这个组织方式值得学 |
| `Configuration/StreamerLocalizationProfile.cs` | Streamer 汉化 profile 模型 + `bindingActionProviderToken` 十六进制解析 | `Load:18`；`GetBindingActionProviderToken:33-41` | System.Text.Json | 中（仅作为**token 表样本**） | 它证明「PC Streamer 侧靠 metadata token 定位方法」这条路可行——我们 `04-streamer-settings` 可以照此法复核 |
| `Infrastructure/FileSystemUtil.cs` | SHA-256 文件哈希 / 递归重置目录 / 复制文件 | `Sha256:7-11`；`ResetDirectory:13-18` | BCL | 高（概念） | 「以 SHA-256 作为二进制身份锚点」是 VDHelper 判定「装的是官方版还是补丁版」的直接依据。但实现只有 3 行，BCL 一行可替代，不值得搬代码 |
| `Infrastructure/ProcessRunner.cs` | 外部进程执行 + stdout/stderr 转发 + 退出码抛错 + 敏感参数脱敏 | `RunAsync:7-50`；脱敏回调 `ApkBuildPipeline.cs:127` | BCL | 中 | 我们要跑 adb/powershell，`PowerShellRunner.cs` 已有同类实现。它的**参数脱敏**思路（`displaySanitizer`）值得抄进我们的日志层 |
| `Managed/ElfPayloadFile.cs` | 解析 little-endian ELF64，按 section 名 `payload` 定位并读出 Xamarin assembly store；支持写回 | `Open:40-76`（magic/长度/越界校验见 43-52、70-71）；`ReadPayload:78-83`；`WriteWithPayload:85-107` | 仅 BCL（`BinaryPrimitives`） | **高** | 我们要读 APK 里的 `libassemblies.arm64-v8a.blob.so`。本机实测：官方 1.34.18 APK 的该 ELF 有 11 个 section，`payload` 在 index 10，offset=16384 size=13319390，代码路径可跑通（见下方实测） |
| `Managed/XamarinAssemblyStore.cs` | 解析 XABA store（181 条程序集）、处理 XALZ/LZ4 压缩条目、解出/替换单条程序集、重建 store | `Parse:24-79`（magic `0x41424158` 见 8 行、index 尺寸校验见 35-37）；`Get:81`；`ExtractAssemblies:85-94`；`ReplaceAssembly:96`；`Build:102-158`；`AssemblyEntry.GetAssemblyBytes:195-212`；`SetAssemblyBytes:214-242` | `K4os.Compression.LZ4` 1.3.8 | **高** | 读 Quest 侧 IL 的必经之路：程序集不在 APK 里直接躺在 zip 中，全在这个 XABA/XALZ blob 里。已实测跑通（见下方实测） |
| `Managed/AssemblyStoreService.cs` | ELF + store 的组合入口（Load/Save，原子替换） | `Load:5-9`；`Save:11-16` | 上述两个 | **高** | 语义清楚：给「拿到 store + 拿回 ELF 句柄」用。我们只要读，写回对我们无意义 |
| `Managed/AssemblyInspector.cs` | Cecil 只读检查器：按类型+方法名+参数数 dump 全部 IL / 按子串搜 `ldstr` / 搜字段引用 / 列类型+token / **搜 int 常量** / 按 token dump 方法 / 反查 token 调用点 | `DumpMethod:8-41`（唯一性检查 29-34）；`DumpStrings:43-61`；`DumpFieldReferences:63-81`；`DumpTypes:83-99`；`DumpIntegerConstants:101-116`；`DumpMethodByToken:118-132`；`DumpMethodReferences:134-152`；`TryGetInt32:154-173`；`AllTypes:186-194` | `Mono.Cecil` 0.11.5 | **高（概念），低（代码）** | `DumpIntegerConstants` / `DumpMethodReferences` / `DumpMethodByToken` 三件事正是我们要做的「读 Quest 侧 IL 常量」。但它只有 Console 输出、没有返回值结构，且依赖 Cecil；我们已经用 dnlib 跑通了同样功能（实测见下）。**思路照抄，代码不搬** |
| `Managed/TokenPreservingAssemblyPatcher.cs` | dnlib 改 Android 侧 IL：8 种 operation + 按原文改 `ldstr`；写回必须 `MetadataFlags.PreserveRids` | `Apply:12-63`（dispatch 23-48）；`BypassOnCreateSignature:81-107`；`BypassKeyboardSignature:109-143`；`SkipCloudComputerResults:145-176`；`ReplaceWithReturn:200-211`；`RaiseVrBitrateLimit:213-236`；`ReplaceAsyncResult:238-290`；`ApplyStringPatches:292-343` | `dnlib` 4.5.0 | **低（功能）/ 高（方法论）** | 功能是**绕过付费鉴权**（`ReturnOculusToken` 造 token、`get_HasValidIdentity` 恒真），与 VDHelper 硬规则 1「不伪造鉴权」直接冲突，**功能一律不搬**。值得学的是它的**严格计数**纪律：每处补丁都先数命中次数（`RaiseVrBitrateLimit:217` 期望 4 处、`ApplyStringPatches:319-338` 三级回退 + 歧义即抛），版本漂移就构建失败而不是猜 |
| `Managed/StreamerLocalDiscoveryPatcher.cs` | dnlib 改 PC Streamer：按 metadata token 定位，屏蔽「停止局域网发现」的调用、屏蔽云端注册有效性检查、把 IPv6 组播网卡枚举替换为空 `List<int>`、破 EXE 自签与启动更新退出 | `SupportedLayouts:9-52`（4 个版本 × 8 个 token）；`Apply:54-101`；`DisableExternalStopCalls:103-119`（116 行强制计数）；`SkipIPv6MulticastJoins:121-134`；`PatchExecutableSignatureCheck:136-152`；`PatchStartupUpdateExit:154-177`；`FindMethod:179-182`；`StreamerPatchLayout:250-259` | `dnlib` 4.5.0 | **中（作为知识，不作为代码）** | 这是**最贴近 VDHelper 主题**的一段：它精确指出「PC 侧发现被云端状态机掐断」这条失败链，以及 **IPv6 组播枚举会吃掉网卡列表**。这些应进我们的检测项。但改 EXE 本身不是我们的职责（我们不改第三方程序） |
| `Managed/StreamerLocalizationPatcher.cs` | dnlib 改 Streamer：重建 `.g.resources`（BAML）、按 token 改托管 `ldstr`、在 `EnumDataProvider.BeginQuery` 前注入 BindingAction 标签覆盖 | `Apply:16-47`；`Inspect:49-80`；`RewriteGeneratedResources:82-127`；`RewriteManagedStrings:129-146`；`RewriteBindingActionProvider:148-253`（180 行严格校验 token 归属类型/方法名/签名） | dnlib、`BamlParser.NetStandard` | 低 | 汉化；`RewriteBindingActionProvider` 展示了「在方法体中插入指令前缀并重连所有 jump target + ExceptionHandler」的完整技巧，难度高但我们不需要 |
| `Managed/BamlStringRewriter.cs` | BAML record 级字符串读写；可在 About 页插入署名行 | `ReadStrings:8-19`；`Rewrite:21-49`；`InsertAboutCredit:51-149`；`ReadDocument:151-162` | **`Confuser.Renamer.BAML`**（快照 `.csproj:14` 声明的却是 `BamlParser.NetStandard`，二者 namespace 一致，见 03 文档） | 无 | WPF BAML 我们走 XAML 源/资源字典路线 |
| `Managed/TranslationStringScanner.cs` | 新版本适配的候选文本扫描器：遍历程序集 IL 字符串、bundle `UI/*/Page` 页面、Android `strings.xml`，排除 profile 已收录项，输出三份 CSV | `Scan:18-75`；`ScanManagedAssemblies:77-105`；`ScanBundle:107-135`；`ExtractSerializedStrings:137-172`；`LooksLikeEnglishText:207-222` | Mono.Cecil + Xenko.Core.Serialization | 低 | 本质是汉化候选挖掘。唯一可借鉴的：`LooksLikeEnglishText` 那种「先廉价过滤再深挖」的启发式 |
| `profiles/vd-1.34.18 … 1.34.22-beta` | 每版一份：SHA-256 白名单、程序集/方法补丁表、Xenko 字体对象覆盖表、三份翻译 CSV | 见 `02-patch-profile-matrix.md` | — | **高（作为 IL 常量交叉表数据源）** | 它的 `managed-strings.csv` 里 `BaseILOffset` 列虽然**当前实现不用**（`AGENTS.md:239` 明说只作审计信息），但那份表本身是官方 APK 的 IL 偏移快照，可用来校验我们自己的偏移 |
| `profiles/streamer-1.34.21 / 1.34.22` | PC Streamer 汉化 profile：官方 EXE 哈希、BAML/托管/BindingAction 三份 CSV、数据源 token | `profiles/streamer-1.34.21/profile.json`（`bindingActionProviderToken: "0x06000575"`） | — | 中（作为 token 表数据源） | 78 个托管字符串 token + 4 个 BAML 资源名，可作 `04-streamer-settings` 的定位索引 |

## 实测验证（本轮真跑）

我在 `%TEMP%/vdprobe` 用 dnlib 4.5.0 + K4os LZ4 1.3.8 复刻了 `ElfPayloadFile.Open` + `XamarinAssemblyStore.Parse` + `AssemblyInspector.DumpIntegerConstants` 的读取链，对**官方未修改**的 `F:\Project\VirtualDesktop\VirtualDesktop.Android_1.34.18.0_base.apk` 跑通：

```text
[zip] lib/arm64-v8a/libassemblies.arm64-v8a.blob.so bytes=13336480
[elf] payload off=16384 size=13319390
[xaba] magic=0x41424158 version=0x80010002 entries=181 indexEntries=362 indexSize=4344
[store] VirtualDesktop.Mobile.dll index=52 storedBytes=250371
[store] extracted 529408 bytes -> ...\Temp\VirtualDesktop.Mobile.dll
[module] name=VirtualDesktop.Mobile.dll asmVersion=1.0.0.0 mvid=b0028b8c-b080-4bcf-9009-7303d0ae089f
```

结论：**读 Quest 侧 IL 常量的技术路径是通的、且不依赖这个仓库的任何代码**。同一工具扫出的可直接用于检测项的常量：

```text
# 全 store（181 个程序集）扫 38850
  HIT VirtualDesktop.Net.dll VirtualDesktop.Net.ComputerDiscoveryClient::.cctor IL_0033 = 38850
  HIT VirtualDesktop.Net.dll VirtualDesktop.Net.ComputerDiscoveryClient::.cctor IL_0047 = 38850
Matches(all): 2

# 全 store 扫 38860
Matches(all): 0        <-- 38860 在 Quest 侧程序集里不存在
```

`.cctor` 完整 IL 确认语义（这是 VdApkPatcher 的 profile 里**没有**、只有读 IL 才能拿到的关键事实）：

```text
=== VirtualDesktop.Net.dll System.Void VirtualDesktop.Net.ComputerDiscoveryClient::.cctor() token=0x0600001c
  IL_002e: ldsfld     System.Net.IPAddress System.Net.IPAddress::Broadcast
  IL_0033: ldc.i4     38850
  IL_0038: newobj     System.Void System.Net.IPEndPoint::.ctor(System.Net.IPAddress,System.Int32)
  IL_003d: stsfld     System.Net.IPEndPoint VirtualDesktop.Net.ComputerDiscoveryClient::BroadcastEP
  IL_0042: ldsfld     System.Net.IPAddress System.Net.IPAddress::Any
  IL_0047: ldc.i4     38850
  IL_004c: newobj     System.Void System.Net.IPEndPoint::.ctor(System.Net.IPAddress,System.Int32)
  IL_0051: stsfld     System.Net.IPEndPoint VirtualDesktop.Net.ComputerDiscoveryClient::ListeningEP
  IL_0065: stsfld     System.Runtime.Serialization.DataContractSerializer VirtualDesktop.Net.ComputerDiscoveryClient::ComputerSerializer
  IL_00a1: ldc.i4.1
  IL_00a2: callvirt   System.Void System.Security.Cryptography.SymmetricAlgorithm::set_Padding(System.Security.Cryptography.PaddingMode)
```

即：**Quest 侧局域网发现 = UDP 单播端口 38850，向 `255.255.255.255:38850` 广播发现报文，载荷是 AES-`PaddingMode.None` 加密的 `VirtualDesktop.Interfaces.Computer`（`DataContractSerializer`）。**
同一结论在 PC 侧反编译源码独立得到（`decompiled_streamer/VirtualDesktop.Streamer/-.112.cs:209` 与 `:212`，两行分别是 `IPAddress.Broadcast, 38850` 和 `IPAddress.Any, 38850`），且 32/16 字节的 `BroadcastAesKey` / `BroadcastAesIV` 字面量在 `- .112.cs:197-207` 可见。

> 这条修正了上一轮报告的 `02_networking_streaming.md:48` 与 `:149`：那里把 `38860/udp` 记为局域网广播端口并标注「已验证存在于反编译」。实测 38860 **只存在于 PC Streamer**（`VirtualDesktop.Streamer.ConnectionManager` 与 `VirtualDesktop.Net.WOLHelper`，即 WOL 唤醒），Quest 侧全 store 扫 38860 = 0 处。Quest↔PC 局域网发现的端口是 **38850**。

另外扫出 `RaiseVrBitrateLimit` 补丁点（`profiles/vd-1.34.18` 之后才引入的那个 operation）在 1.34.18 的实际形态：

```text
# VirtualDesktop.Mobile.Shared.dll 扫 200000000
  HIT Xenko.VR.HmdResolutionTypeExtensions::GetMaxVRBitrate IL_00be = 200000000
  HIT Xenko.VR.HmdResolutionTypeExtensions::GetMaxVRBitrate IL_00ce = 200000000
Matches: 2
```

1.34.18 只有 **2 处**，而 `TokenPreservingAssemblyPatcher.cs:217` 硬编码 `expectedReplacements = 4`。这解释了为什么该 operation 从 1.34.20 才加进 profile —— **1.34.20 起 `GetMaxVRBitrate` 里 200000000 变成 4 处**。[未验证] 1.34.20+ 的 APK 我手上没有（`F:\Project\VirtualDesktop` 只有 1.34.18 base.apk），未逐字节数过 4 处。

端口全量扫描（供 `02-network-diagnosis` 复用，1.34.18 Quest 侧 `VirtualDesktop.Mobile.dll` 38000–39999 区间）：

```text
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0681 = 38810
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0710 = 38811
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0720 = 38810
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0a9d = 38830
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0b35 = 38840
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0bcd = 38820
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0c58 = 38831
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0c69 = 38830
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0cfc = 38841
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0d0d = 38840
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0da0 = 38821
HIT ... NetworkManager/<ConnectToComputerAsync>d__66::MoveNext IL_0db1 = 38820
Matches: 12
```

这条同时给出**版本迁移的一个具体证据**：1.34.18 里 `<ConnectToComputerAsync>d__66`，而 `profiles/vd-1.34.19` 起 CSV 标签已改成 `d__74`（见 02 文档）。即编译器生成的状态机编号每次小版本都会漂移，**任何按 `d__NN` 名字定位的清单都必须按版本重取**。

## Streamer 侧 token 表实测

`StreamerLocalDiscoveryPatcher.SupportedLayouts`（`StreamerLocalDiscoveryPatcher.cs:42-51`，1.34.22 布局）在本机已安装的官方 Streamer 上逐条核对：

```text
[module] name=VirtualDesktop.Streamer.exe asmVersion=1.34.22.0 mvid=e7d5de80-709a-4866-9597-dc8aa4807734
  OK ConnectionStateMachine 0x060001a5 => VirtualDesktop.Streamer.ConnectionManager/<>c::
  OK AppOnStartup 0x06000530 => VirtualDesktop.Streamer.App::OnStartup
  OK StartLocalDiscovery 0x060000e8 => sig=System.Void (String,String,OS,NetworkAdapter[],PlatformAccessToken[])
  OK StopLocalDiscovery 0x060000e9 => sig=System.Void ()
  OK CheckForUpdate 0x0600050e => VirtualDesktop.Streamer.UpdateHelper::
  OK LocalDiscoveryReceiveLoop 0x060213d3 => sig=System.Void (HashSet`1<PlatformAccessToken>,Computer)
  OK GetIPv6InterfaceIndices 0x060214c2 => sig=IReadOnlyCollection`1<Int32> ()
  OK VerifyExecutableSignature 0x0602170f => sig=System.Boolean (String)
StopCallSites(excl. Start)=4; stateMachineNoped=2; remainingForDisable=2 (patcher expects 2)
   0x060001a5 VirtualDesktop.Streamer.ConnectionManager/<>c/:: IL_02a4
   0x060001a5 VirtualDesktop.Streamer.ConnectionManager/<>c/:: IL_02c7
   0x060001b1 VirtualDesktop.Streamer.ConnectionManager/:: IL_0683
   0x060001b5 VirtualDesktop.Streamer.ConnectionManager/:: IL_01d4
  IPv6EnumerateCall 0x060213d3 .:: IL_0091
```

本机文件：`C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe`，FileVersion `1.34.22.0`，SHA-256 `6BFEC9E4...7CBB51`（与 `AGENTS.md:61` 和 `profiles/streamer-1.34.22/profile.json` 白名单一致）。

结论：**该 token 表对本机这一版全部命中，8/8 准确**，其 `ExpectedExternalStopCalls: 2` 的计数推演也对得上（4 个调用点中 2 个在状态机内由 `PatchStartupUpdateExit` 之前的分支 NOP，另 2 个才归 `DisableExternalStopCalls`）。
`GetIPv6InterfaceIndices` 的签名 `IReadOnlyCollection<int>` 与 `SkipIPv6MulticastJoins`（`:126-130`）找 `List<int>::.ctor()` 替换成空列表的思路吻合。

> 注意 `0x060213d1`（`PORT 38850 ... .::.cctor`，3 处）与 `0x060213d3`（发现接收循环）是**相邻但不同**的 token —— 与 `AGENTS.md:423`「不要假设 token 使用固定增量」一致。

## 结论摘要

- **能直接搬的：一个都没有。** 整个仓库的重心（APK 重打包、汉化、绕过付费鉴权、改第三方 EXE）与 VDHelper 边界冲突。
- **必须重建的：Quest 侧 IL 常量读取链**（`ElfPayloadFile` + `XamarinAssemblyStore` + 一个常量扫描器）。技术上已独立跑通（见上），用 dnlib 即可，不搬它的代码。
- **必须吸收的知识**：`StreamerLocalDiscoveryPatcher` 的失败链分析（云端状态机掐断发现 / IPv6 组播枚举）和 `SupportedLayouts` token 表，直接进我们的检测项与 `04-streamer-settings`。
- **必须吸收的纪律**：`RaiseVrBitrateLimit` / `ApplyStringPatches` 那种「先数命中次数，不符即失败」的严格计数法。