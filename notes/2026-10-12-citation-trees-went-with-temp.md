# 引证闸门的证据树随 %TEMP% 一起没了

2026-10-12（0.9.0 发布当天）。**当天重建，闸门回绿：`OK all 157 citations resolve`。**

## 现象

`python tools/check-citations.py` 本地红：4 条引用解析不到任何反编译树。

```
  ComputerDiscoveryClient.cs:98-107  x2  cited in src/VdHelper/Core/Health/ReachabilityCheck.cs
  DownloadState.cs:6-13  x1  cited in src/VdHelper/Resources/parameters.json
  NetHelper.cs:17  x1  cited in src/VdHelper/Core/Health/HealthChecks.cs
  NetworkManager.cs:184-186  x2  cited in src/VdHelper/Core/Adb/HeadsetProbe.cs
```

## 原因

闸门配了两棵树：

| 树 | 路径 | 当时 |
|---|---|---|
| streamer | `F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer` | 在（12704 个 .cs） |
| vd | `%TEMP%\vd_ep_01\vd` | **没了** |

- 第二棵树放在 `%TEMP%` 下，**而 %TEMP% 会被清**。10-06 那批引证核对用的就是它
  （树的来历见 `research/13-endpoints/01-endpoint-inventory.md` §0.2/§0.3，当时就写明是临时目录）。
- 前三条要的正是 vd 树里的 Quest 侧文件。现存 F: 上的反编译都不是那棵树：
  `analysis/apk_patch/decompiled/xenko/` 那份 Mobile 反编译是另一个东西（其 `NetworkManager.cs` 里
  根本没有 Kill 路径）；`EuropeServerIP` 这个字面名在 F: 的 `.cs` 里一处都没有。
- **引文本身没有被证伪**——是证据树没了，所以无法核对。

## 处理（当天完成）

- `DownloadState.cs:6-13` → `Mobile/DownloadState.cs:5-11`。枚举在现存树里就是 5-11 行
  （`decompiled_streamer/.../Mobile/DownloadState.cs` 共 12 行；三份现存副本全是 12 行），陈述不变。
- **重建 Quest 侧证据树**（确定性重建）：
  - 源：`F:\Project\VirtualDesktop\analysis\apk_patch\libassemblies.arm64-v8a.blob.so`（ELF + XABA + XALZ/LZ4）。
  - 解析格式照抄 `research/13-endpoints/01-endpoint-inventory.md` §0.2 与
    `analysis/apk_patch/binary_patch.py:63-91`（`parse_blob` / `extract_entry`）。
  - 提取出的 8 个程序集 SHA256 与 §0.2 表**逐条一致**（Mobile.dll `79311294078a9f46…`、
    Net.dll `fcb27ea66705b389…` 等），即与当时核对引文用的那份逐字节相同。
  - 反编译：`ilspycmd 11.1.0.9782`，命令照 `research/13-endpoints/01-endpoint-inventory.md` §0.4。
  - 落盘：`reference/decompiled/vd/`（仓库内、`reference/` 已 gitignore；DLL 原件在 `_assemblies/`）。
- 3 条悬空引用**逐条对重建树核实，行号精确吻合**：
  - `NetHelper.cs:16` = `AmericaCentralServerIP`、`:17` = `EuropeServerIP`（VirtualDesktop.Interfaces）；
  - `ComputerDiscoveryClient.cs:98-107` = 3000 ms 回包窗口 `TimeoutAfter(num)`（VirtualDesktop.Net）；
  - `NetworkManager.cs:184-186` 与 `:212-214` = 两条 `CurrentProcess.Kill()` 路径（VirtualDesktop.Mobile）。
- `tools/check-citations.py`：
  - `DEFAULT_TREES` 第二项 `%TEMP%\vd_ep_01\vd` → `reference\decompiled\vd`（相对路径按 `--root` 解析）；
  - 逐棵打印 `present` / `MISSING`——之前只打印存在的树，**一棵树没了看不出来**，这次恰恰栽在这；
  - 修掉「只剩第二棵树时标签错位成 streamer」的 zip 拼法。

## 教训

**闸门的证据树不能放在 `%TEMP%`。** 证据树是闸门的真值来源；
把真值来源放在会被清的地方，等于闸门随时可能悄悄失去一半判据——而旧输出连「一棵树没了」都不说。

重建是确定性的（同一 blob + 同一反编译器版本 → 同树），所以 157 条引用全部原位复现；
下次需要重建时，按上面三行源路径照做即可。
