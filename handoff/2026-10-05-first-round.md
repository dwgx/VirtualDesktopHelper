# 2026-10-05 第一轮：项目大脑 + 三屏工具 + 八路调研

## 这轮做完了什么

1. **项目大脑**：`AGENTS.md`、`WORKFLOW.md`、`research/`（11 个主题目录）、`notes/`（含本机基线与 5 条 ADR）、`handoff/`。
2. **三屏可用工具**（`src/VdHelper`，`net10.0-windows` WPF）：
   - 本机体检：18 项检测，数据驱动，修复动作都带备份与回滚。
   - 串流参数：111 个调研来的键，标注 PC 侧/头显侧、只读、需重启；可写项先备份再写。
   - 头显诊断：adb 查找（本机命中 `D:\Software\VIVE Hub\...\adb.exe`）、设备探测、包名与 7 项权限判定。
3. **无头自检**：`VdHelper.exe --selftest`，退出码 0/3/4/5，CI 与发布脚本都用它。
4. **发布链路**：`.github/workflows/build.yml` + `tools/publish.ps1`（自包含单文件 + SHA256SUMS + VERSION），实跑产出 `dist/v0.1.0/`。
5. **文档不脱节**：`tools/export-checks.ps1` 从真实运行结果生成 `docs/checks.md`。
6. **11 份调研报告**落盘 `research/01…11`。

## 证据在哪

- 编译：`dotnet build src/VdHelper/VdHelper.csproj -c Release` → Build succeeded，0 error 0 warning。
- 自检：`./src/VdHelper/bin/Release/net10.0-windows/VdHelper.exe --selftest --out _selftest.txt` → exit 4，17 项结果。
- UI：`_shot3.png`（体检+参数）、`_shot7.png`（头显），由 `tools/capture-window.ps1` 实拍。
- 本机事实：`notes/2026-10-05-local-baseline.md`。
- 参数目录：`tools/extract-parameters.py` → `src/VdHelper/Resources/parameters.json`（111 键，18 只读）。

## 下一刀切哪

1. **验证修复动作**：现在 18 项里只有「禁用网卡 / 重建防火墙规则 / 启服务 / 重装服务 / 启动 Streamer / 授权限」几条修复，**尚未在真机上执行过任何一条**（怕动 Owner 的机器）。需要 Owner 点名后实跑一条并复验。
2. **端到端归因**：把第一屏与第三屏合成一份报告（A1-A15 规则见 `research/11-quest-dashboard/02-screen3-design.md`），并把 `research/09-failure-corpus` 的用户原话接进文案。
3. **发布到 GitHub**：需要 Owner 点头才能 push；仓库 topics/labels（`virtualdesktop` `vr` `quest` `tool` `helper` `diagnostics` `troubleshooting`）也等点头。
4. **文档站**：可复用上一轮 `docs/about.html` 的骨架，按 `research/10-prior-lessons/03-doc-assets.md` 的改造清单做三段式（index / checks / faq）。

## 没验证的

- **没有任何修复动作在真机执行过**（只验证了「会生成并显示备份/回滚」）。
- **没有头显连过**：`headset` 屏只在「无设备」路径上跑通；包名/权限读取、IP、proxy 都是草案形态。
- **广播发现未实测**：38850/38860 的收发没有抓包证据（工具只检查端口是否存在）。
- **参数写入未实测**：`StreamerConfigWriter` 没有在真配置上跑过一次写入 + 回滚。
- `research/09-failure-corpus` 第一份报告在写完后交回，文案接入尚未做。