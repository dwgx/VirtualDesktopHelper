# 03 — IL 硬上限常量表 + 读取方案 + 现有 patch 脚本位置

> 本文把「码率上限写在 IL 里、不是 JSON key」这件事查到底：
> 常量的确切值、确切字节偏移、读出方案，以及上一轮已经存在的 patch 脚本在哪。
>
> **本文所有偏移均为本机实测**（用 `ilspycmd` 反编译 + Python 直接读 `Xenko.Rendering.dll`
> 的原始字节校验），不是从别处抄的。

---

## 0. 首要更正：硬上限**不在 `Mobile.dll` 里**

上一轮（`_upstream/vdh`）把码率上限记在 Quest `Mobile.dll` 的 offset `0x13B0C` `0x14E45` `0x2BFE3`。
**本轮实测推翻了这一条**：

```
实测：在真正的 Mobile.dll（工作区里叫 Xenko.dll，529408 字节）中
  ldc.i4 200000000 出现次数 = 0
  960000000 / 600000000 / 500000000 / 400000000 / 150000000 / 120000000 / 100000000 / 40000000 全部 = 0
```

即：**Mobile.dll 里一个码率上限常量都没有**。那个三个偏移指向的是别的东西
（在 1.34.18.0 的 Xenko.dll 上按 int32 读出来分别是 1879061320 / 135074058 / 269703194，
opcode 字节分别是 `0x48` / `0x0A` / `0x1A` —— **都不是 `ldc.i4` 的 `0x20`**，
所以它们连"码率常量"都不是）。

码率上限的真实位置是 **`Xenko.VR.HmdResolutionTypeExtensions.GetMaxVRBitrate`**，
它属于 **`VirtualDesktop.Mobile.Shared.dll`**。三方独立证据一致：

| 证据来源 | 内容 |
|---|---|
| 本轮实测反编译 | `ilspycmd -l class Xenko.Rendering.dll` → `Class Xenko.VR.HmdResolutionTypeExtensions`、`Class VirtualDesktop.Mobile.SharedStreamerSettings` 等同在此程序集 |
| `reference/vdapkpatcher/profiles/vd-1.34.22/profile.json:85-89` | `"assembly": "VirtualDesktop.Mobile.Shared.dll"`, `"type": "Xenko.VR.HmdResolutionTypeExtensions"`, `"method": "GetMaxVRBitrate"`, `"operation": "RaiseVrBitrateLimit"` |
| `reference/vdapkpatcher/AGENTS.md:28` | 「将 `GetMaxVRBitrate` 中恰好四处 `200000000` 改为 `960000000`。桌面码率限制不得被修改。」 |

> **注意 1.34.18.0 与 1.34.20+ 的差异**：本轮手上是 **1.34.18.0**，
> 实测 `ldc.i4 200000000` 在该程序集里只有 **2 处**（文件偏移 `0xA4A`、`0xA5A`）。
> 而 `vdapkpatcher` 的 profile 覆盖 1.34.20/21/22，patcher 硬性要求 **恰好 4 处**
> （`TokenPreservingAssemblyPatcher.cs:217` `expectedReplacements = 4`），
> 否则抛 `InvalidDataException` 失败（`:228-232`）。
> → **1.34.18.0 上不能直接套用 vdapkpatcher 的 `RaiseVrBitrateLimit` profile，会因数量不符而失败。**
> 这是工具必须知道的版本边界。

> **`extracted_assemblies/` 文件名对照表**（本轮实测，`ilspycmd -l class` + 类型计数得出）：
>
> | 磁盘文件名 | 真实程序集名 | 大小 | 判据 |
> |---|---|---|---|
> | `Xenko.dll` | `VirtualDesktop.Mobile.dll` | 529408 | 含 132 个 `VirtualDesktop.Mobile.*` 类型 |
> | `Xenko.Rendering.dll` | `VirtualDesktop.Mobile.Shared.dll` | 87552 | 含 `SharedStreamerSettings`/`SharedUserSettings`/`SharedMobileSettings`/`HmdResolutionTypeExtensions` |
> | `VirtualDesktop.Mobile.dll` | 实际是 `ZString` | 192512 | 只有 `Cysharp.Text.*` |
> | `VirtualDesktop.Mobile.Shared.dll` | 实际是 `Oculus.Platform` | 17920 | 只有 `Oculus.Platform.*` |
>
> **工具实现时必须按类型定位，不能按文件名取程序集。**

---

## 1. 硬上限常量表（1.34.18.0 实测）

程序集：`Xenko.Rendering.dll`（= `VirtualDesktop.Mobile.Shared.dll`），87552 字节，SHA 未记录。

### 1.1 `GetMinVRBitrate(this HmdType)` —— VR 码率下限

| 项 | 值 |
|---|---|
| 方法 RVA | `0x2778` |
| 文件偏移 | `0x978` |
| Header size | 1（tiny header） |
| Code size | 6 |
| 返回 | **无条件 `10000000`（10 Mbps）** |

IL（实测原文）：
```
IL_0000: ldc.i4 10000000
IL_0005: ret
```

### 1.2 `GetMaxVRBitrate(this HmdType, VideoCodec)` —— VR 码率上限（**本表是工具要读的东西**）

| 项 | 值 |
|---|---|
| 方法 RVA | `0x2780` |
| 文件偏移 | `0x980` |
| Header size | 12 |
| Code size | 224（`0xE0`） |
| 返回类型 | `int32`（bps） |

**完整分支表（每行的"文件偏移"是该常量在 DLL 里的实际字节位置，已用 Python 读原始字节校验）**：

| HmdType | 判据（枚举值） | 设备 | codec ≠ 5 | codec == 5 (H.264+) | 非 H264+ 常量文件偏移 | H264+ 常量文件偏移 |
|---|---|---|---|---|---|---|
| `IL_00a4` | 259 | Oculus Quest 1 | **100 Mbps** | **100 Mbps**（不分 codec） | `0xA30` | — |
| `IL_00aa` | 320 / 400 | Quest 2 / Quest Pro | **150 Mbps** | **400 Mbps** | `0xA3A` | `0xA40` |
| `IL_00aa` | 600 / 650 / 700 / 701 | Vive Focus3 / XR Elite / Pico Neo3 / Neo3 Link | **150 Mbps** | **400 Mbps** | `0xA3A` | `0xA40` |
| `IL_00ba` | 1000 / 1001 | **Meta Quest 3 / 3S** | **200 Mbps** ⬅ | **500 Mbps** | `0xA4A` | `0xA50` |
| `IL_00ca` | 1010 / 1011 | Pico 4 / Pico 4 Ultra | **200 Mbps** | **600 Mbps** | `0xA5A` | `0xA60` |
| `IL_00ca` | 1100 / 1200 | Samsung Galaxy XR / XREAL Aura | **200 Mbps** | **600 Mbps** | `0xA5A` | `0xA60` |
| `IL_00ca` | 1400 / 1500 | Play for Dream MR / Steam Frame | **200 Mbps** | **600 Mbps** | `0xA5A` | `0xA60` |
| `IL_00da` | 其他（含 `-1` Unknown） | — | **40 Mbps** | **40 Mbps** | `0xA66` | — |

**本表最重要的一行：Quest 3 / 3S（1000/1001）的非 H264+ 上限是 `200000000`，位于文件偏移 `0xA4A`。**
这正是 `vdapkpatcher` 要改成 `960000000` 的那个值。

> ⚠️ **陷阱**：`HmdType == 259`（Quest 1）走 `IL_00a4`，**直接返回 100 Mbps，不看 codec**。
> 所以「选 H.264+ 抬码率上限」这条**对 Quest 1 无效**，对 Quest 2/3 才有效。
> 工具的诊断文案不能一刀切。

### 1.3 桌面码率上限（**不允许改**，见下）

`GetMinDesktopBitrate` / `GetMaxDesktopBitrate`（同程序集）：

| 函数 | 返回 | 文件偏移 |
|---|---|---|
| `GetMinDesktopBitrate`（移动端设备 iPhone/iPad/AppleTV/AndroidPhone/…） | 2 Mbps | `0x8F8` |
| `GetMinDesktopBitrate`（其余） | 4 Mbps | `0x8FE` |
| `GetMaxDesktopBitrate`（`HmdType == 259`，Quest 1） | 40 Mbps | `0x90D` |
| `GetMaxDesktopBitrate`（其余，含 Quest 2/3） | **120 Mbps** | `0x913` |

**`vdapkpatcher/AGENTS.md:28` 明确：「桌面码率限制不得被修改。」**
桌面 120 Mbps 上限的两个分支判定都在 `GetMaxDesktopResolution` 之外的独立函数里，
改 VR 的 `GetMaxVRBitrate` 不会碰桌面。

### 1.4 码率换算公式（工具算「用户实际拿到多少 Mbps」用）

```
桌面：
  DesktopBitrateLimit (0~1, 默认 0.17)
    → Math.Round(limit, 3, MidpointRounding.AwayFromZero)      [GetDesktopBitrate]
    → Lerp(GetMinDesktopBitrate(HmdType), GetMaxDesktopBitrate(HmdType), 阈值)
    → int bps，再被 MeasuredBandwidth 与 AutoAdjustBitrate 二次收敛

VR：
  VRBitrateLimit (0~1, 默认 0.36)
    → Math.Round(limit, 3, MidpointRounding.AwayFromZero)      [GetVRBitrate]
    → Lerp(10000000, GetMaxVRBitrate(HmdType, ActiveCodec), 阈值)
    → int bps
```

参数来源：`SettingsTab.cs:734`（桌面滑条）、`StreamingTab.cs:769`（VR 滑条）；
计算在 `SharedMobileSettings.cs:1834/1863-1864`。
`Lerp` 的两端点分别取自 `GetMinDesktopBitrate`/`GetMaxDesktopBitrate`（`hmdres.cs:681-687`）
与 `GetMinVRBitrate`/`GetMaxVRBitrate`（`hmdres.cs:809-816`）。

**举例（Quest 3，H.264，VRBitrateLimit = 0.36）**：
`round(0.36, 3) = 0.36` → `Lerp(10, 200, 0.36) = 10 + 190*0.36 = 78.4` → **78 Mbps**。
若 `MeasuredBandwidth` 测得只有 50 Mbps，实际会被拉回更低（`SharedMobileSettings.cs:1834`）。

---

## 2. 工具怎么读出这个值

### 2.1 方案 A（推荐）：静态读 APK 里的 `VirtualDesktop.Mobile.Shared.dll`

**步骤**：
1. 从 APK 取 `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so`
   （这个 `.so` 是 ELF，里面是 XABA payload，程序集以 XALZ+LZ4 压缩条目存放）。
2. 按 blob 格式解出各条目：`XABA` 头 → 描述符数组（每条 28 字节）→ 逐个 `XALZ` 条目 → LZ4 解压。
   参考实现（**本轮已读，确认了格式细节**）：
   `F:/Project/VirtualDesktop/_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:91-195`
   - `BlobPath = "lib/arm64-v8a/libassemblies.arm64-v8a.blob.so"`（`:19`）
   - `DescEntry = 28`（`:18`）
   - `entryCount = BitConverter.ToInt32(blob, payload + 8)`（`:95`）
   - `indexSize = BitConverter.ToInt32(blob, payload + 16)`（`:96`）
   - `desc = payload + 20 + indexSize`（`:97`）
   - 每条 `dataSize[i] = ToInt32(blob, desc + i*28 + 8)`（`:113`）
   - `idx[i] = ToInt32(blob, xalz[i] + 4)`、`uncomp[i] = ToInt32(blob, xalz[i] + 8)`（`:114-115`）
   - 按 `uncomp[i]` 体积匹配定位目标程序集（`:119-124`）
   - LZ4 解压：`LZ4Codec.Decode(comp, 0, comp.Length, raw, 0, raw.Length)`（`:130`）
   - 重压：`LZ4Codec.Encode(..., LZ4Level.L12_MAX)`（`:141`）+ 重排描述符（`:153-166`）
3. 在解出的 dll 里定位 `Xenko.VR.HmdResolutionTypeExtensions.GetMaxVRBitrate`：
   **不要按文件偏移硬编码**（版本会变），用 Mono.Cecil 遍历方法体数 `ldc.i4` 操作数。
4. 读出所有 `200000000`（Quest 3/3S 分支的实际值）。

**方案 A 的正确实现形态**（抄 `vdapkpatcher` 的纪律，不要硬编码）：
```
遍历 GetMaxVRBitrate 的 IL 指令
  → 收集 OpCode == Ldc_I4 && Operand is int && value == 200000000 的全部指令
  → 断言数量 == 期望值（1.34.18.0 是 2；1.34.20+ 是 4）
  → 数量不符就报错，不要猜
```
依据：`TokenPreservingAssemblyPatcher.cs:213-236`。

**为什么必须用 Cecil 而不是搜字节**：`ldc.i4 200000000` 的编码是 `20 00 C2 EB 0B`，
`0x200000000` 也可能出现在别的数据结构里（本轮用字节搜索在 `Xenko.Rendering.dll` 里
找到 2 处、且 opcode 字节确实是 `0x20`，但这是运气；换版本就可能撞上巧合）。

**本轮已实测的字节形态**（供工具做快速预筛）：
```
ldc.i4 200000000  →  20 00 C2 EB 0B
ldc.i4 960000000  →  20 80 84 38 00
ldc.i4 600000000  →  20 80 84 C3 23
ldc.i4 500000000  →  20 00 65 CD 1D
ldc.i4 400000000  →  20 00 84 D7 17
ldc.i4 150000000  →  20 80 D1 F0 08
```
（Python 实测输出，见 §1.2 的偏移表对应的十六进制。）

### 2.2 方案 B：`dumpsys` 读不到，只能靠 IL 探针

**`dumpsys` 对这些常量无效** —— 它们是 IL 立即数，不是 Android 系统状态。
可行的替代：

```bash
# 1) 从设备上把 blob 拉回来做静态分析（无需 root）
adb shell "ls -la /data/app/*/VirtualDesktop.Android*/lib/arm64/*/libassemblies.arm64-v8a.blob.so"
adb pull <上面那个路径> blob.so
# 或整包拉回（更稳，不受路径猜测影响）
adb shell pm path VirtualDesktop.Android     # → package:/data/app/.../base.apk
adb pull <base.apk> vd.apk
```

> `pm path` + `adb pull base.apk` 是**最稳的取程序集路径**（apk 一定在 `/data/app/` 下，adb 可读）。

### 2.3 方案 C：运行时读（不推荐，仅备选）

patched APK 删了全部 168 个 AOT `.so` 走 JIT（`HANDOFF.md:110`），
托管堆是 GC 移动的，`SettingsBase<T>.Default` 单例引用位置需要先定位静态字段槽，
**不能直接套文件偏移**。且这依赖 `[未验证]` 的运行时附加手段。
**结论：工具应当只实现方案 A。**

---

## 3. 现有 patch 脚本位置（不重写，只引用）

### 3.1 `reference/vdapkpatcher/` —— Owner 当前在用的补丁器（推荐参照）

| 路径 | 内容 |
|---|---|
| `reference/vdapkpatcher/Managed/TokenPreservingAssemblyPatcher.cs:213-236` | **`RaiseVrBitrateLimit` 实现**：`originalLimit = 200_000_000` → `patchedLimit = 960_000_000`，`expectedReplacements = 4`，用 Mono.Cecil 改 `instruction.Operand`，**保持 metadata token 不变** |
| `reference/vdapkpatcher/Managed/TokenPreservingAssemblyPatcher.cs:44-45` | 操作名分发：`case "RaiseVrBitrateLimit": RaiseVrBitrateLimit(method);` |
| `reference/vdapkpatcher/profiles/vd-1.34.20/profile.json:86-90` | profile 声明点（同结构也在 `vd-1.34.21` / `vd-1.34.22` / `vd-1.34.22-beta`） |
| `reference/vdapkpatcher/AGENTS.md:28` | 「1.34.20、1.34.21 和 1.34.22 比前两版多一个 `RaiseVrBitrateLimit` 补丁……桌面码率限制不得被修改。」 |
| `reference/vdapkpatcher/AGENTS.md:175-178` | 「版本漂移时应该失败，而不是猜测性修改。」← **工具应遵守的纪律** |
| `reference/vdapkpatcher/Managed/XamarinAssemblyStore.cs` | blob 存取（XABA/XALZ/LZ4） |

profile 声明原文（`profiles/vd-1.34.22/profile.json:83-90`）：
```json
{ "assembly": "VirtualDesktop.Mobile.Shared.dll",
  "type": "Xenko.VR.HmdResolutionTypeExtensions",
  "method": "GetMaxVRBitrate",
  "parameterCount": 3,
  "operation": "RaiseVrBitrateLimit" }
```
> `parameterCount: 3` = `this HmdType` + `VideoCodec` + 编译器生成的闭包/扩展标记（`hmdres.cs:701` 签名只有 2 个显式参数）。
> 匹配时按类型名 + 方法名 + `parameterCount` 三者校验（`AGENTS.md:178` 要求）。

### 3.2 `_upstream/vdh/` —— 旧 VDH 0.4.7 的字节级补丁器（**偏移表已过时，不建议沿用**）

| 路径 | 内容 | 状态 |
|---|---|---|
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:17` | `const uint Mask = 0xC2ACED01u`（偏移 XOR 掩码） | 有效 |
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:22` | `Enc22 = { 0x13B0C ^ Mask, 0x14E45 ^ Mask, 0x2BFE3 ^ Mask }`（1.34.22.0，Mobile.dll size 544256） | ⚠️ **本轮已证伪**：这些偏移不在 Mobile.dll 也不指向 `ldc.i4` |
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:24` | `Enc19 = { 0x1396C ^ Mask, 0x14CA5 ^ Mask, 0x2BC97 ^ Mask }`（1.34.19.0，size 540672） | ⚠️ 同上 |
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:26-33` | `Offs(int size)` — 按程序集体积选偏移表 | 机制有效，值需重算 |
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:91-195` | **`PatchBlob` — 本轮确认其 blob 格式解析细节正确**（见 §2.1） | ✅ 可复用 |
| `_upstream/vdh/VirtualDesktopHelper/BitrateApk.cs:132-138` | 改 4 字节立即数 + 记日志 `"IL 0x" + o.ToString("X") + " " + old + " -> " + cap` | ✅ 可复用 |
| `_upstream/vdh/VirtualDesktopHelper/VDH.cs:31-35` | 版本→体积→偏移三张表 | ⚠️ 表值需重算 |
| `_upstream/vdh/README.md:39-40` | 「Bitrate lives in Quest `Mobile.dll` IL (`0x13B0C` `0x14E45` `0x2BFE3` on 22), not Streamer JSON.」 | ⚠️ **与本轮实测冲突，应以本文为准** |

**给工具的处置建议**：
- 沿用 `_upstream/vdh/BitrateApk.cs` 的 **blob 解包/重打包**（已验证正确）。
- **丢弃**它的硬编码偏移表，改用 `vdapkpatcher` 的 **Cecil 遍历 + 数量断言** 模式。
- 交付 APK 时桌面码率上限**不得修改**（`AGENTS.md:28`）。

### 3.3 本轮实测的权威基线（1.34.18.0）

供工具做首版自检：

| 项 | 值 |
|---|---|
| 真实程序集 | `VirtualDesktop.Mobile.Shared.dll`（工作区文件名 `Xenko.Rendering.dll`） |
| 大小 | 87552 字节 |
| `GetMaxVRBitrate` RVA / 文件偏移 | `0x2780` / `0x980` |
| `GetMaxVRBitrate` Header / Code size | 12 / 224（`0xE0`） |
| `ldc.i4 200000000` 数量 | **2**（文件偏移 `0xA4A`、`0xA5A`） |
| Quest 3 非 H264+ 上限 | 200000000 @ `0xA4A` |
| 补丁后目标值 | 960000000 |
| 桌面 `GetMaxDesktopBitrate` | Quest 1=40Mbps @ `0x90D`；其余=120Mbps @ `0x913`（**不改**） |

---

## 4. Android 侧：7 个必须预授的 runtime 权限

### 4.1 权威来源（`pm grant` 命令原文）

`F:/Project/VirtualDesktop/analysis/apk_patch/install.bat:24-31`
与 `F:/Project/VirtualDesktop/analysis/apk_patch/HANDOFF.md:20-30` **逐字一致**：

```bat
adb shell pm grant VirtualDesktop.Android com.oculus.permission.USE_SCENE
adb shell pm grant VirtualDesktop.Android horizonos.permission.USE_SCENE
adb shell pm grant VirtualDesktop.Android com.oculus.permission.FACE_TRACKING
adb shell pm grant VirtualDesktop.Android horizonos.permission.FACE_TRACKING
adb shell pm grant VirtualDesktop.Android com.oculus.permission.EYE_TRACKING
adb shell pm grant VirtualDesktop.Android horizonos.permission.EYE_TRACKING
adb shell pm grant VirtualDesktop.Android android.permission.POST_NOTIFICATIONS
```

**「30 秒失焦」的确切表述**（`HANDOFF.md:32`）：
> **重要**：每次安装后必须授权权限，否则 30 秒后 VR 焦点被回收。

`HANDOFF.md:171`（Key Technical Lessons #7）复述：
> 7 个运行时权限必须预授 — 否则 HorizonOS 30 秒后收回 VR 焦点

`install_template.bat:60` 的注释（同一个循环授 **10** 个）：
> HorizonOS 会在 ~30s 后收回 VR 焦点,若以下权限未授予。10 个一并授权。

### 4.2 每个权限不授的具体症状

| # | 权限 | 缺失症状 | 证据 |
|---|---|---|---|
| 1 | `com.oculus.permission.USE_SCENE` | **无场景（Scene）权限 → 拿不到 IMMERSIVE XR 焦点 → HorizonOS 约 30 秒后收回焦点**，表现为「进不去 / 自动退出到桌面」 | `HANDOFF.md:32`、`HANDOFF.md:171` |
| 2 | `horizonos.permission.USE_SCENE` | 同上（新 HorizonOS 命名空间的老包用 `com.oculus.*`；新系统认 `horizonos.*`，两个都要授） | `install.bat:26` |
| 3 | `com.oculus.permission.FACE_TRACKING` | 面追不可用；`HMD.Supports(64)` 分支关闭；`FoveatedStreaming`/`ForwardTrackingData` 的可用性判定受影响 | `StreamingTab.cs:310` 检查 `PermissionManager.IsPermissionGranted(DynamicSettings.Default.FaceTrackingPermission)` |
| 4 | `horizonos.permission.FACE_TRACKING` | 同上 | `install.bat:28` |
| 5 | `com.oculus.permission.EYE_TRACKING` | **foveated streaming 被应用自己强制关闭**：请求失败则 `UserSettings.FoveatedStreaming = false` | `VrApp.cs:267-275`（`RequestPermissions()` 里 `ContinueWith(t => { if (!t.Result) FoveatedStreaming = false; })`） |
| 6 | `horizonos.permission.EYE_TRACKING` | 同上 | `install.bat:30` |
| 7 | `android.permission.POST_NOTIFICATIONS` | Android 13+ 上无通知；VD 的提示/HUD 类通知不可见（不影响串流本身，但影响「为什么没提示」的排查） | `install.bat:31` |

### 4.3 补充 3 个（`install_template.bat:61-74` 的同一个 for 循环，共 10 个）

| 权限 | 缺失症状 | 证据 |
|---|---|---|
| `android.permission.RECORD_AUDIO` | **麦克风直通（MicPassthrough）不可用**，且**首启弹窗会阻塞 VR 窗口放置**（`HANDOFF.md:54-55` 明写：「首启会请求 RECORD_AUDIO(串流语音),弹窗会阻塞 VR 窗口放置。install.bat 已预授 7 个 VR 权限,但 RECORD_AUDIO/存储类需额外 grant」）。应用侧逻辑：`VrApp.cs:277-286`，若 `SharedUserSettings.MicPassthrough` 为 true 就请求 `android.permission.RECORD_AUDIO`，拒绝则把 `MicPassthrough` 置 false | `VrApp.cs:279-285`、`HANDOFF.md:54-55` |
| `android.permission.READ_EXTERNAL_STORAGE` | 读不到外部存储的视频库文件 | `install_template.bat:70` |
| `android.permission.READ_MEDIA_IMAGES` | Android 13+ 读不到图片（视频封面/截图） | `install_template.bat:71` |

外部全文件访问另走 `Environment.IsExternalStorageManager`（`ExternalStorageManager.cs:27`，API 34+），
走 `android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION` Intent（`ExternalStorageManager.cs:45`）。

### 4.4 工具检测形态 `[未验证]`（本机无 adb）

```bash
PKG=VirtualDesktop.Android

# 列出所有已授 runtime 权限
adb shell dumpsys package $PKG | sed -n '/runtime permissions/,/^  [a-z]/p'

# 逐项检查（返回 granted=true/false）
for P in com.oculus.permission.USE_SCENE horizonos.permission.USE_SCENE \
         com.oculus.permission.FACE_TRACKING horizonos.permission.FACE_TRACKING \
         com.oculus.permission.EYE_TRACKING horizonos.permission.EYE_TRACKING \
         android.permission.POST_NOTIFICATIONS android.permission.RECORD_AUDIO \
         android.permission.READ_EXTERNAL_STORAGE android.permission.READ_MEDIA_IMAGES; do
  echo -n "$P : "
  adb shell dumpsys package $PKG | grep -c "granted=true.*$P" || echo "check-failed"
done

# 修复：对缺失的补授（这就是 pm grant 的批量形态）
for P in com.oculus.permission.USE_SCENE horizonos.permission.USE_SCENE \
         com.oculus.permission.FACE_TRACKING horizonos.permission.FACE_TRACKING \
         com.oculus.permission.EYE_TRACKING horizonos.permission.EYE_TRACKING \
         android.permission.POST_NOTIFICATIONS android.permission.RECORD_AUDIO; do
  adb shell pm grant $PKG $P
done
```

**工具设计建议**：
- 检测项：「7 个核心权限是否全 granted」→ 缺失即报「**会 30 秒失焦**」，这是**最高优先级**的检测项，
  因为它导致的是「完全连不上」而非画质问题。
- 修复项：直接 `pm grant`，7 条命令逐条执行，失败的不吞（`pm grant` 对未在 manifest 声明的权限会报错）。
- **不要**把权限状态当缓存：每次安装 / 每次重签 APK 后状态会重置（`HANDOFF.md:32` 明写「每次安装后必须授权」）。

### 4.5 与「连通性」的关系（工具文案要点）

Owner 记录的失败模式是「**头显端找不到 PC / PC 端发现不到头显**」。
本节 7 个权限与那条失败链**不是同一层**：

- 权限缺失 → 进程**起不来/被踢出 VR**，用户根本没进到「找电脑」那一步。
- 发现失败 → 进程起来了，38860/udp 广播或 38810/tcp 连不上，那是网络层（见 `research/02-network-diagnosis/`）。

工具应把两者做成**两个独立的检测项**，不要合并成「连不上」一个笼统提示。
参考：`report_sections/02_networking_streaming.md:149` 记录了 3 秒云超时后 fallback 到本地发现；
`HANDOFF.md:51` 记录「先开 Quest VD 后开 PC Streamer 可能搜不到，建议先开 Streamer」——
这是**发现时序**问题，与权限无关。

---

## 5. 未验证项清单

| 项 | 为什么没验 | 验证条件 |
|---|---|---|
| 1.34.22.0 的 `Mobile.dll` 偏移 `0x13B0C`/`0x14E45`/`0x2BFE3` 究竟指向什么 | 手上的 APK 是 1.34.18.0，无 1.34.22.0 blob | 拿到 1.34.22.0 APK，按 `BitrateApk.cs:91-195` 解包后 dump 这三个偏移前后 32 字节 |
| 1.34.20+ 的 `GetMaxVRBitrate` 里 4 处 `200000000` 的具体分布 | 本手上只有 1.34.18.0（2 处） | 拿到 1.34.20+ APK，同法解包 + Cecil 遍历 |
| Quest 真机上 `$HOME/.config/Virtual Desktop/` 的真实绝对路径 | 本机无 adb、无设备 | 见 `02-storage-and-io.md` §6 |
| patched APK 是否 `debuggable`（决定 `run-as` 可用性） | 同上 | 同上 |
| `pm grant` 命令在 Quest 当前 OS 版本上的实际返回 | 同上 | 同上 |
| 各权限缺失时的**真机**症状是否与 §4.2 描述一致 | §4.2 症状来自源码逻辑 + `HANDOFF.md` 文字，缺真机逐项复现 | 逐个 revoke 再观察（`adb shell pm revoke`），需 Owner 头显 |
| 「30 秒失焦」的 30 秒是否随 OS 版本变化 | 来自 `HANDOFF.md:32` 与 `install_template.bat:60` 的经验记录 | 同上 |

**未验证项一律不得写进工具的阻断性逻辑**；应作为「检测提示」并在真机验证后再升级为「自动修复」。