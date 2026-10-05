# 07-03 许可、作者与复用边界

## 0. 一句话结论

**这份代码没有可依据的开源许可 —— 只能「参考，不复制」。**
仓库里既没有 LICENSE 文件，也没有版权声明头，`apk签名.txt` 只说明 APK 怎么签名，与代码许可无关。
即便如此，我们仓库里**一行代码都不该从它搬**：功能重心（绕过付费鉴权、重打包官方 APK、改第三方 EXE）与 VDHelper 的边界冲突，且快照本身**缺 `lib/` 依赖目录、原样也编译不过**。

## 1. 事实清单（逐条实测）

### 1.1 树里有什么 / 没有什么

```text
$ cd reference/vdapkpatcher && ls -a
.gitignore  AGENTS.md  Apk  Bundles  Configuration  Infrastructure  Managed
Program.cs  README.md  VdApkPatcher.csproj  apk签名.txt  global.json  profiles

$ find . -iname "*licen*" -o -iname "*copying*" -o -iname "*notice*"
（无输出）

$ ls .git
ls: cannot access '.git': No such file or directory

$ ls lib
ls: cannot access 'lib': No such file or directory
```

| 检查项 | 结果 | 命令证据 |
| --- | --- | --- |
| LICENSE / COPYING / NOTICE 文件 | **无** | `find . -iname "*licen*" -o -iname "*copying*" -o -iname "*notice*"` → 无输出 |
| 源码头部的版权行（`// Copyright` / `/* (c) */`） | **无** | `grep -rniI "copyright" --include=*.cs .` → 0 处（`grep -c` 逐文件计数全是 0） |
| README 里的许可段落 | **无** | 读全文 `README.md:1-119`，无 License / Licence / 版权字样 |
| AGENTS.md 里的许可段落 | **无** | 读全文 `AGENTS.md:1-515`，无 |
| `.csproj` 里的 `<PackageLicenseExpression>` / `<Authors>` | **无** | `VdApkPatcher.csproj:1-39` 全文无这些属性 |
| git 历史 / 作者署名 | **无**（快照无 `.git`） | `ls .git` → No such file or directory |
| 上游仓库地址 / 项目主页 | **查不到** | 源码、README、AGENTS.md、`global.json` 均未给出 |

### 1.2 `apk签名.txt` 的实际内容

```text
$ cat apk签名.txt
sign --ks "D:\Tools\ApkTool\vrzwk.keystore" --ks-key-alias "vrzwk.com" --ks-pass pass:vrzwk.com
```

这是 **apksigner 命令行片段**，披露了签名 keystore 的**路径、别名和明文口令**。
它**不是**许可声明，也**不是**任何形式的使用授权。
它同时是一条**不该进我们仓库的东西**：`BuildOptions.cs:61-63` 把同样的 `vrzwk.com` / `vrzwk.com` 写成了默认 keystore 别名与口令。
（`AGENTS.md:279` 自己写了「不得在日志或交付文档中输出 keystore 密码」，但默认值就写在源码常量里。这条自相矛盾本身也说明这套代码没经过对外发布审查。）

### 1.3 能识别出的署名

代码里出现的唯一身份线索是字符串 `vrzwk` / `vrzwk.com`：

| 位置 | 内容 |
| --- | --- |
| `apk签名.txt:1` | keystore 别名与口令均为 `vrzwk.com` |
| `Configuration/BuildOptions.cs:61-63` | 默认 keystore `D:\Tools\ApkTool\vrzwk.keystore`，alias `vrzwk.com`，pass `vrzwk.com` |
| `Managed/TokenPreservingAssemblyPatcher.cs:277` | 注入 IL 的假 access token 字符串字面量 `"vrzwk"` |
| `profiles/streamer-1.34.2*/profile.json` | `aboutCredit.label = "Vrzwk汉化组:"`，`aboutCredit.value = "vrzwk.com"` |
| `AGENTS.md:248` | 同上 |

网络检索 `vrzwk.com` 命中 **VR中文库（vrzwk.com / vrzwk.cn）**，一个售卖 VR/Quest 中文资源与汉化的商业站点（页面含 VIP 会员、付费下载、客服 QQ）。
**这说明作者的公开身份是「vrzwk 汉化组」这一商业汉化团队，而不是 GitHub 开源项目作者。**
没有查到任何名为 `VdApkPatcher` 的公开 GitHub 仓库，也没有 questhelper 团队公开发布该源码的证据 —— `[未验证]`：本轮只做了关键词检索，未做 GitHub code search 的全量反查；验证它需要有人在 GitHub 上以 `VdApkPatcher` + `vrzwk` 精确搜索仓库名并确认归属。

### 1.4 第三方依赖的许可（这些是可以合法用的，与本仓库代码无关）

`VdApkPatcher.csproj:13-33` 声明的依赖，逐个查证：

| 依赖 | 版本 | 许可 | 证据 |
| --- | --- | --- | --- |
| `BamlParser.NetStandard` | 1.0.0 | **MIT** | nuget.org/packages/BamlParser.NetStandard → "License: MIT"，描述 "BAML Reader and Writer from the ConfuserEx project" |
| `K4os.Compression.LZ4` | 1.3.8 | **MIT** | nuget 页面 + `raw.githubusercontent.com/MiloszKrajewski/K4os.Compression.LZ4/master/LICENSE` → MIT, Copyright (c) 2017 Milosz Krajewski |
| `Mono.Cecil` | 0.11.5 | **MIT** | nuget.org/packages/Mono.Cecil → "License: MIT", Authors: Jb Evain |
| `dnlib` | 4.5.0 | **MIT** | `raw.githubusercontent.com/0xd4d/dnlib/.../src/dnlib.csproj` → `<PackageLicenseExpression>MIT</PackageLicenseExpression>`, `<Authors>0xd4d</Authors>` |
| `Xenko.Core` / `Xenko.Core.IO` / `Xenko.Core.Serialization` | 手工 `<Reference>` 指向 `lib/` | Xenko/Stride 引擎 MIT | github.com/migueldeicaza/xenko、github.com/stride3d/stride → MIT |
| `Confuser.Renamer.BAML`（代码里 `using` 的） | 无独立包，来自 ConfuserEx | **MIT** | `raw.githubusercontent.com/yck1509/ConfuserEx/master/LICENSE` → "ConfuserEx is licensed under MIT license. Copyright (c) 2014 yck1509" |

**这里有一处工程不自洽，值得记下来**：`.csproj:14` 声明的是 `BamlParser.NetStandard`，而 `Managed/BamlStringRewriter.cs:2` 写的是 `using Confuser.Renamer.BAML;`，`:47` 用 `BamlWriter.WriteDocument`、`:51` 用 `BamlDocument`。
`BamlParser.NetStandard` 正是 ConfuserEx 的 BAML 子集的独立打包（namespace 保留为 `Confuser.Renamer.BAML`），所以能编译 —— 但 `.csproj` 里**没有** `ConfuserEx` 这个包引用，读者会以为 `Confuser.Renamer.BAML` 来自 ConfuserEx 本体。
`AGENTS.md:210` 又把这段描述成「使用 `BamlParser.NetStandard` 结构化读取」，与源码的 using 不完全对应。属文档与代码脱节。

**结论**：依赖树全部是 MIT，我们可以自由引用这些库。但**这不构成对 VdApkPatcher 自身代码的任何授权**。

### 1.5 快照原样编译不过（第二条「不搬」的理由）

即使抛开许可，`reference/vdapkpatcher/` 这个快照**照原样 `dotnet build` 会失败**：

- `VdApkPatcher.csproj:10` 把 `XenkoLibDir` 默认设为 `$(MSBuildThisFileDirectory)lib`，`:21-33` 用 `<HintPath>` 引用 `lib\Xenko.Core.dll` 等三个 DLL —— **`lib/` 目录不在快照里**（`AGENTS.md:118` 的目录图里列了 `lib/`，实际树里没有）。
- `VdApkPatcher.csproj:37` 还要求 `lib\win-x64\libcore.dll`（Xenko 原生 LZ4）—— 同样缺失。
- 本机 `dotnet --list-sdks` 只有 `10.0.400`；`global.json:3` 要求 `8.0.406` + `rollForward: latestPatch`，**没有 8.0.x SDK 就直接拒绝**。

也就是说：**没有 Xenko 的三个 DLL + 原生 libcore + .NET 8 SDK，这份代码在本机一行都跑不起来。**
这不是挑刺，是说明「搬代码」在物理上就不成立 —— 我们要的能力（读 ELF/XABA、扫 IL 常量）实测只用 dnlib + K4os LZ4 两个包就完整跑通了（`01-capability-inventory.md` 实测段）。

## 2. 裁定

| 问题 | 答案 |
| --- | --- |
| 作者是谁？ | 公开身份线索只有 **vrzwk 汉化组 / vrzwk.com**（VR中文库，商业汉化团队）。无 git 历史、无源码内署名、无仓库主页可交叉确认 |
| 什么许可？ | **未声明**。无 LICENSE、无版权头、无 csproj 许可元数据 |
| 能不能复制进我们的仓库？ | **不能。** 无许可 = 默认保留全部权利，未经授权不得复制 |
| 我们该怎么做？ | **仅参考，不复制。** 学习公开的方法论与格式，重新实现我们需要的能力 |

补充：即便日后拿到许可，也**不该搬功能**。`AGENTS.md:2` 硬规则 1「不伪造鉴权」与 2「不分发官方二进制」直接排除以下四项：
`ReturnOculusToken` / `ReturnOculusTokenPair`（造 access token）、
`ReturnTrue` on `get_HasValidIdentity`（伪造身份有效）、
`BypassOnCreateSignature` / `BypassKeyboardSignature`（移除签名校验）、
以及整个 `ApkBuildPipeline`（重打包并签名官方 APK）。

## 3. 只做参考的话，能学到什么

这些是**公开的技术事实与工程方法**，学到后自己写，与复制代码无关：

1. **Xamarin assembly store 的格式**：`libassemblies.arm64-v8a.blob.so` 是 ELF64，`payload` section 里是 `XABA`（magic `0x41424158`）容器，条目可能是 `XALZ`（magic `0x5a4c4158`）+ LZ4。
   这是**逆向必须知道的格式**，任何分析 APK 的人都会独立得出同样结论。本机已实测：1.34.18 的 blob 有 11 个 section、`payload` offset 16384 size 13319390、store 里 181 个程序集、`VirtualDesktop.Mobile.dll` 压缩后 250371 字节解出 529408 字节。
2. **`MetadataFlags.PreserveRids` 的必要性**：Xamarin native typemap 用 RID 引用 managed 类型，dnlib/Cecil 写回时若重排 RID，运行时会崩。
   这是 Xamarin 生态的公共知识，但这里给出了**为什么**的准确理由，值得记进我们的 `notes/`。
3. **严格计数补丁纪律**：改 IL 前先数命中处数，不符即失败（`RaiseVrBitrateLimit:217` 要求恰好 4 处；`DisableExternalStopCalls:116` 要求恰好 6/2 处）。
   版本漂移时**宁可构建失败，也不猜着改**。这是我们做 IL 分析时最该借的工程态度 —— 也正好对应我们仓库的证据驱动硬规则 3。
4. **profile 组织格式**：一个版本 = 一个目录 = `profile.json`（身份 + 补丁表）+ 三份 CSV（托管 / bundle / Android）+ 资源目录。加哈希白名单、按 SHA 而非按版本名选 profile。
5. **`patch-streamer` 的失败链分析**（`AGENTS.md:194-204` + `StreamerLocalDiscoveryPatcher.cs`）：PC 侧局域网发现会在四个地方被掐断 —— 云端注册结果为空、注册令牌无效、启动更新检查返回、IPv6 组播网卡枚举。这些是**关于 Virtual Desktop 的事实**，不是代码，我们要用检测项的形式重新表达。
6. **「先脱敏再打日志」的参数处理**（`ProcessRunner.RunAsync` 的 `displaySanitizer` 回调，`ApkBuildPipeline.cs:127` 用它把 `pass:***` 替换真口令）。思路可直接用到我们的日志层。

## 4. 可瘦身清单：搬 / 不搬 / 改写后搬

### 4.1 不搬（与边界冲突）

| 条目 | 理由 |
| --- | --- |
| `Managed/TokenPreservingAssemblyPatcher.cs` 全部 8 个 operation | `ReturnOculusToken`/`ReturnOculusTokenPair` 造 access token；`ReturnTrue` 伪造 `HasValidIdentity`；`BypassOnCreateSignature`/`BypassKeyboardSignature` 移除签名校验。**违反硬规则 1** |
| `Apk/ApkBuildPipeline.cs` + `AndroidManifestPatcher.cs` + `AndroidResourceTranslator.cs` | 重打包并重新签名官方 APK。**违反硬规则 2**，且与「网络诊断」无关 |
| `Bundles/XenkoBundleService.cs` + `FileObjectBackend.cs` + `LinearSearchReadOnlyDictionary.cs` | 需要随包分发 Xenko 引擎 DLL 与原生 `libcore.dll`（都是别人的二进制）；解决的是「汉化 bundle 里的页面文案」，不是「发现不到 PC」 |
| `Managed/StreamerLocalizationPatcher.cs` + `BamlStringRewriter.cs` | WPF BAML 汉化，与检测/修复无关；且改第三方 EXE 不是我们职责 |
| `Managed/TranslationStringScanner.cs` | 汉化候选挖掘，与我们无关 |
| `Program.cs` + `Configuration/BuildOptions.cs` | 命令行分派 + 硬编码 `D:\Tools\ApkTool\...` 与**明文 keystore 口令**。绝不能进我们的仓库 |
| `apk签名.txt` | 含 keystore 路径、别名、明文口令 |
| `profiles/*/bundle-assets/`（~90 MB 字体与纹理 .bin） | 微软雅黑字体对象的 Xenko 序列化产物，我们不做字体替换 |
| `profiles/*/{managed,bundle,android}-strings.csv` | 翻译数据。**但**可当**只读交叉表**用（见 02 文档 §4），不进仓库 |

### 4.2 搬（可以原样采用，量极小）

| 条目 | 理由 | 搬进来后的形态 |
| --- | --- | --- |
| `Infrastructure/ProcessRunner.cs` 的**参数脱敏回调**这一个概念 | 与 VdApkPatcher 本身无关，是通用日志卫生 | 不搬文件。在 `src/VdHelper/Core/Checks/PowerShellRunner.cs` 加一个 `Func<string,string>? sanitizer` 可选参数，签名与 `ProcessRunner.RunAsync:7-11` 对齐（`fileName, arguments, workingDirectory, displaySanitizer`），口令类参数替换为 `***` 后再写日志 |
| `Infrastructure/FileSystemUtil.cs:7-11` 的 `Sha256` **这一行做法** | 判定「装的是官方版还是补丁版」是我们诊断的刚需 | 不搬文件。`System.Security.Cryptography.SHA256.HashData` + `Convert.ToHexString(...).ToLowerInvariant()` 直接内联到我们自己的 `FileHash` helper。注意取**小写十六进制**以便和 profile 里的哈希字符串直接比较 |
| `StreamerLocalDiscoveryPatcher.cs:9-52` 的 **token 表数据** | 是关于 Virtual Desktop 1.34.19–1.34.22 的**事实**，不是代码。已在 1.34.22 上实测 8/8 命中（`01-capability-inventory.md`） | 作为数据落进 `research/04-streamer-settings/`，并在我们自己的代码里以「token → 语义」映射表存在，供检测项引用。**不搬 `Apply` 方法** |
| `02-patch-profile-matrix.md` §2 的 **6 个稳定补丁项方法全名** | 证明这些方法名跨 6 个版本稳定，可作版本无关的**存在性探针**（我们用它验证「装的 APK 是不是这一代」） | 作为常量表。但**只用存在性，不实现任何绕过语义** |

### 4.3 改写后搬（核心能力，必须自己写）

| 条目 | 搬进来后的形态 |
| --- | --- |
| `Managed/ElfPayloadFile.cs` 的**读路径**（`Open` + `ReadPayload`，共 128 行里约一半有用） | **值得作为我们读 APK blob 的能力**，但只取读、不取写。新文件 `src/VdHelper/Core/Apk/ElfPayloadReader.cs`：只读、只暴露 `PayloadOffset`/`PayloadLength`/`ReadPayload()`。丢弃 `WriteWithPayload`（`:85-107`，我们要写回干什么）、私有构造与 `gapAfterPayload` 字段（`:35`，只服务于写回）。保留全部边界校验（`bytes.Length < 64`、ELF magic、class==2 data==1、section table 越界、payload 越界）—— 这些校验是安全读 1 GB APK 的前提 |
| `Managed/XamarinAssemblyStore.cs` 的**解析 + 单条解出**（`Parse:24-79`、`AssemblyEntry.GetAssemblyBytes:195-212`） | **值得搬**，这是读 Quest IL 的必经之路。新文件 `XamarinAssemblyStoreReader.cs`：保留 `XABA` magic、`version & 0x80000000` 决定的 index entry size（`:35`）、7 个 u32 的描述符布局、`XALZ` + LZ4 解压与长度校验。**丢弃** `Build:102-158`、`ReplaceAssembly:96`、`SetAssemblyBytes:214-242`、`ExtractAssemblies:85-94`（我们逐条按需解出，不落盘 181 个文件） |
| `Managed/AssemblyStoreService.cs` 的 `Load`（`:5-9`，8 行） | 只是两个调用的组合，**不值得单独成文件**；新文件 `QuestAssemblySource.cs` 把 ELF 读 + XABA 解析合成一个入口，返回「程序集名 → dnlib `ModuleDef`」的只读索引 |
| `Managed/AssemblyInspector.cs` 的 `DumpIntegerConstants:101-116`、`DumpMethodByToken:118-132`、`DumpMethodReferences:134-152`、`TryGetInt32:154-173`、`AllTypes:186-194` | **思路照搬，代码重写**。差别：它用 Mono.Cecil 且只 `Console.WriteLine`；我们用 dnlib（已在本地 nuget cache 实测 4.5.0 可用）并返回**强类型 record**，供 WPF 检测引擎消费。加两样它没有的：① 按类型名前缀模糊匹配（因为 `d__NN` 会漂，见 02 文档 §3）；② 按数值区间扫描（如一次性列出 38000–39999 的所有 `ldc.i4`，一次看全端口） |
| `Managed/ElfPayloadFile` + `XamarinAssemblyStore` + `AssemblyInspector` 的**组合使用顺序** | 这套顺序（zip → ELF → payload → XABA → LZ4 → dnlib）是我们的实现骨架，已在 `%TEMP%/vdprobe` 端到端跑通 |

### 4.4 一句话回答两个具体问题

- **`Managed/ElfPayloadFile.cs` 是否值得作为我们读 APK blob 的能力？**
  **值得，但只取读的一半。** ELF64 section header 解析 + `payload` 定位这段（约 40 行有效逻辑）我们自己写一份只读版即可，边界校验照抄思路（不是抄代码）。写回部分（`WriteWithPayload` 与它为重排 section table 引入的 `gapAfterPayload`）一律不要。
  额外价值：它证明了 `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so` 里的程序集**不落在 APK 的 zip entry 里**，必须自己解 ELF 再解 XABA —— 这是「为什么 ILSpy 直接拖 APK 找不到程序集」的答案。
- **`Bundles/XenkoBundleService.cs` 是否与我们无关？**
  **无关。** 三条理由：① 解决的是 bundle 里页面文案汉化，不是发现失败；② 依赖 Xenko 引擎三 DLL + 原生 `libcore.dll`，等于把别人的二进制拖进我们仓库，与「不分发官方二进制」的边界精神一致（虽 Xenko 是 MIT，但体积与职责都不该引入）；③ 它是本仓库里最复杂的一段（320 行 + 两个适配类），性价比最低。
  唯一沾边的事实：`bundle-strings.csv` 覆盖的 6 个页面里有 `UI/ControlPanel/Page`（172 条），而控制面板正是用户看到的发现/连接界面 —— 但这属于 `03-quest-parameters` 的文案枚举，不需要搬任何代码。

## 5. 它能帮 VDHelper 做什么

### 5.1 要不要用它来读 Quest 侧 IL 常量？—— **要这个能力，不要这份代码**

它证明了链路可行（并给了我们格式细节），但它的实现不适合我们：Cecil 而非 dnlib、只输出到 stdout、无法被 WPF 检测引擎消费、且整个工程在本机跑不起来。
**我们已经用 dnlib 独立跑通了同样的链路**，输出见 `01-capability-inventory.md` 实测段，并且顺带拿到一条它 profile 里没有的关键事实：**Quest 侧局域网发现端口是 UDP 38850**（`VirtualDesktop.Net.ComputerDiscoveryClient::.cctor`，全 store 扫 38860 = 0 处）。

它**已经**帮到我们的地方，是知识而非代码：
- `StreamerLocalDiscoveryPatcher` 揭示 PC 侧发现被云端状态机掐断的四个具体位置 → 直接变成检测项。
- `managed-strings.csv` 的发现失败文案分类（Wi-Fi 未连 / 各平台服务器不可达 / 不响应 / 有问题 / No computer found / 「确认你的电脑在运行 Streamer」）→ 直接变成 UI 提示文案表。

### 5.2 要不要用它做 APK 版本识别？—— **能，但它给的信息不够**

它现有的识别手段是 **SHA-256 白名单**（`profile.InputSha256`，`ApkBuildPipeline.cs:28-29`）和 **apktool.yml 里的 versionName**（`:135-141`）。
前者要求 apktool 先解包，对我们的只读诊断太重；后者依赖 apktool。

但我们在实测中发现了**更好的识别键**，它自己没用上：
`VirtualDesktop.Android.dll` 与 `VirtualDesktop.Mobile.Shared.dll` 的 **程序集版本 == APK 的 `versionName`**，直接写在 assembly metadata 里，不经 apktool：

```text
[module] name=VirtualDesktop.Android.dll       asmVersion=1.34.18.0  mvid=6517552f-b80c-42f5-92dc-8d075cd4b025
[module] name=VirtualDesktop.Mobile.Shared.dll  asmVersion=1.34.18.0  mvid=199f089a-41cb-43a1-9b49-5c4f563640ff
[module] name=VirtualDesktop.Mobile.dll         asmVersion=1.0.0.0    mvid=b0028b8c-b080-4bcf-9009-7303d0ae089f
[module] name=VirtualDesktop.Core.dll           asmVersion=1.18.57.0  mvid=4501c10d-46dc-41f8-993c-104fb670cd6d
```

即：**只解一个 ELF + XABA + LZ4，就能同时得到「APK 版本号」「APK 身份 MVID」「哪些程序集存在」，全程不需要 apktool、不需要 Java、不写盘。**
这正好补上我们 APK 侧识别的一环 —— 配合 ADB 从头显上 `pm path` 取 APK 路径，可以判断「头显上装的这个 APK 是不是我们分析的那一版」。

### 5.3 接口草案

以下签名均为 `[草案]`，未实现、未编译；命名遵循本仓库既有风格（`src/VdHelper/Core/**`）。

```csharp
namespace VdHelper.Core.Apk;

// ---------- 只读 ELF：定位 Xamarin payload section ----------
public sealed class ElfPayloadReader
{
    public long PayloadOffset { get; }
    public long PayloadLength { get; }

    // 从 APK 的 zip entry 流读取；必须是 little-endian ELF64 且含名为 "payload" 的 section。
    public static ElfPayloadReader Open(Stream apkStream, string entryPath);

    // 只读副本。VDHelper 不重写 APK，故不提供任何写入方法。
    public byte[] ReadPayload();
}

// ---------- 只读 XABA/XALZ：程序集索引 ----------
public sealed record QuestAssemblyEntry(string Name, int StoredSize, bool IsCompressed);

public sealed class XamarinAssemblyStoreReader
{
    public const uint StoreMagic     = 0x41424158; // "XABA"
    public const uint CompressedMagic= 0x5A4C4158; // "XALZ"

    public uint Version { get; }
    public IReadOnlyList<QuestAssemblyEntry> Entries { get; }

    public static XamarinAssemblyStoreReader Parse(ReadOnlySpan<byte> payload);

    public bool Contains(string assemblyName);

    // 解出单个程序集（XALZ 条目走 LZ4 解压并校验长度）。
    // 只在内存返回，不落盘 —— 我们不导出程序集文件。
    public byte[] GetAssemblyBytes(string assemblyName);
}

// ---------- 组合入口：把上面两层接起来，返回可查询的模块集合 ----------
public sealed record QuestModuleInfo(
    string AssemblyName,
    Version AssemblyVersion,
    Guid Mvid);

public sealed class QuestAssemblySource : IDisposable
{
    // apkStream 定位在 APK zip 内；入口路径固定为 lib/arm64-v8a/libassemblies.arm64-v8a.blob.so。
    public static QuestAssemblySource FromApk(Stream apkStream);
    public static QuestAssemblySource FromBlobFile(string blobPath); // 已解包的 blob，供离线复核

    public IReadOnlyList<QuestModuleInfo> Modules { get; }        // 181 条，含版本与 MVID

    // 版本识别键：Android / Mobile.Shared 的 assembly version == APK versionName
    public Version? ApkVersionName { get; }
    public Guid? ApkMvid { get; }

    // 按需加载单个模块的 dnlib ModuleDef（Lazy，重复调用返回同一实例）。
    public dnlib.DotNet.ModuleDefMD GetModule(string assemblyName);

    public void Dispose();                                        // 释放所有已加载 ModuleDef
}

// ---------- IL 常量查询：对应 AssemblyInspector 的三个 Dump* ----------
public sealed record IlIntConstantHit(
    string AssemblyName, string TypeFullName, string MethodName,
    int MetadataToken, int IlOffset, int Value);

public sealed record IlStringConstantHit(
    string AssemblyName, string TypeFullName, string MethodName,
    int MetadataToken, int IlOffset, string Value);

public sealed record MethodTokenHit(
    string AssemblyName, string CallerTypeFullName, string CallerMethodName,
    int CallerToken, int IlOffset);

public sealed class QuestIlInspector
{
    public QuestIlInspector(QuestAssemblySource source);

    // 1) 全模块扫某个整型常量。实测：value=38850 → VirtualDesktop.Net.dll
    //    ComputerDiscoveryClient::.cctor IL_0033/IL_0047；value=38860 → 0 处。
    public IReadOnlyList<IlIntConstantHit> FindIntConstants(
        int value, string? assemblyFilter = null);

    // 2) 区间扫描，一次看全端口。
    //    实测 value∈[38000,39999] 于 VirtualDesktop.Mobile.dll 得 12 处 38810/38811/38820/
    //    38821/38830/38831/38840/38841，全部位于 <ConnectToComputerAsync>d__66::MoveNext。
    public IReadOnlyList<IlIntConstantHit> FindIntConstantRange(
        int minInclusive, int maxInclusive, string? assemblyFilter = null);

    // 3) 按子串搜字符串常量。用于把「No computer found」这类用户可见文案定位回方法。
    public IReadOnlyList<IlStringConstantHit> FindStrings(
        string contains, string? assemblyFilter = null);

    // 4) 按 metadata token 定位单个方法（Streamer 侧主力手段）。
    public IlIntConstantHit DumpMethod(string assemblyName, int metadataToken);

    // 5) 反查：谁调用了这个 token？用于确认「发现被谁掐断」。
    public IReadOnlyList<MethodTokenHit> FindCallersOfToken(
        string assemblyName, int targetMetadataToken);

    // 6) [草案] 按类型名前缀定位，绕开 d__NN 漂移。
    //    typeFullName = "VirtualDesktop.Mobile.NetworkManager/<GetComputersAsync>d__69"（1.34.18）
    //    typeFullName = "...d__78"（1.34.19） "...d__79"（1.34.20+）
    //    传入 prefix = "VirtualDesktop.Mobile.NetworkManager/<GetComputersAsync>d__"，
    //    由实现方剥离末尾数字后前缀匹配，且要求唯一命中，否则抛歧义异常。
    public IReadOnlyList<IlIntConstantHit> FindByTypeNamePrefix(
        string assemblyName, string typeFullNamePrefix, int? ilIntValue = null);
}
```

配套的服务层（消费上面的结果，产出诊断结论）：

```csharp
public sealed record DiscoveryFacts(
    int? QuestDiscoveryUdpPort,          // 实测 1.34.18 = 38850
    bool QuestBroadcastsDiscovery,      // BroadcastEP != null
    bool QuestUsesPaddingNone,          // set_Padding(PaddingMode.None)
    string? QuestDiscoverySerializer,    // "DataContractSerializer(VirtualDesktop.Interfaces.Computer)"
    Version? ApkVersionName,
    Guid? ApkMvid);

public sealed class QuestApkProbe
{
    public QuestApkProbe(QuestAssemblySource source, QuestIlInspector inspector);

    // 汇总发现链路事实，供 02-network-diagnosis 生成检查项。
    [草案] public Task<DiscoveryFacts> ReadDiscoveryFactsAsync(CancellationToken ct = default);

    // ADB 侧：把头显上的 APK 拉回来（或流式读取）后判定「是不是我们分析的那一版」。
    [草案] public Task<ApkIdentity> IdentifyAsync(string adbApkPath, CancellationToken ct = default);
}

public sealed record ApkIdentity(
    Version? ApkVersionName,
    Guid? ApkMvid,
    bool MatchesAnalyzedBuild,          // 与本轮分析的 1.34.18 base.apk 同一 MVID？
    string? Note);
```

`DiscoveryFacts` 每一项都直接对应一条可执行的检测项：
`QuestDiscoveryUdpPort != 38850` → 端口异常；
`QuestBroadcastsDiscovery == false` → 头显不会广播发现（PC 端必然扫不到）；
`QuestUsesPaddingNone == false` → 发现载荷解密参数不符；
`ApkVersionName` 与 PC Streamer 版本跨代不兼容 → 明确报「客户端与串流端版本不匹配」。

### 5.4 验收口径（把这些结论钉成可复跑的检查）

```powershell
# 判定头显上装的 APK 是不是我们分析的那一版（无需 apktool / Java）
# -> 期望输出：ApkVersionName=1.34.18.0, ApkMvid=b0028b8c-b080-4bcf-9009-7303d0ae089f
#    （MVID 取自 VirtualDesktop.Mobile.dll；Android.dll 为 6517552f-...）

# 判定 Quest 侧局域网发现端口
# -> 期望输出：VirtualDesktop.Net.dll  VirtualDesktop.Net.ComputerDiscoveryClient::.cctor
#              IL_0033 = 38850 / IL_0047 = 38850   (Matches: 2)

# 判定 38860 是否出现在 Quest 侧（用于证伪旧报告）
# -> 期望输出：Matches: 0

# 判定 PC Streamer 是否为受支持的发现补丁版本
# -> 期望输出：asmVersion=1.34.22.0 且 8 个 layout token 全部命中，
#              StopCallSites=4 / 状态机内 2 / 剩余 2（与 ExpectedExternalStopCalls 一致）
```

## 6. 边界重申

本仓库（VDHelper）是**学习与互操作性工具**：不含伪造鉴权、不分发官方二进制（APK / keystore / 官方 EXE）。
参考 VdApkPatcher 的**格式知识**（XABA/XALZ、RID 保留、严格计数）与**关于 Virtual Desktop 的事实**（发现端口、失败链、文案分类），
不复制它的**代码**，不实现它的**鉴权绕过**，不搬运它的**汉化数据与字体二进制**。
与 Virtual Desktop, Inc. 无隶属关系。