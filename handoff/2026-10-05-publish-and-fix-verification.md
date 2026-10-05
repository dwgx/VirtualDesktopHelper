# 2026-10-05 第二轮：发布、修复全量实跑、GitHub 重写

## 这轮做完了什么

1. **GitHub 发布**（Owner 授权）
   - 旧历史封存：分支 `legacy`（0.4.7 的 `59472b0`）+ 标签 `v0.4.0`–`v0.4.7` 原样保留，旧 Release 不动。
   - `main` 强制改写为 VDHelper 新项目。
   - 仓库 description + 11 个 topics（virtualdesktop / vr / quest / tool / helper / diagnostics / troubleshooting / adb / firewall / csharp / wpf）+ 8 个自定义 label。
   - 首个新 Release：`v0.1.0`（VdHelper.exe + SHA256SUMS.txt + VERSION.txt）。
   - `legacy-ARCHIVE.md` 说明旧历史去哪了。
2. **修复动作全量实跑**（Owner 授权）
   - `streamer-launch`：已验证 → `udp-discovery` 由 Warn 变 Pass，回滚后复原。
   - `svc-repair`：**真的跑了**——MsiInstaller 事件（09:38:00-01）证明 `VirtualDesktop.Service.msi` 完成 reconfigure。
   - 配置写入：真文件写入 `StartMinimizedInTray=true` → 读回 True → 用备份恢复 → `diff` 与原文件 **完全一致**。
   - 写入守卫：DPAPI 只读键、头显侧键、未知键，三种都拒绝并给出原因。
3. **两个由证据倒逼出来的修正**
   - `svc-log` 原先永远 Block：修好之后日志里的旧 ERROR 还在，会一直喊「狼来了」。改为 Streamer 在跑时降为 Warn 并说明是历史。
   - `cfg-streamer` 原先把 `ShowPairingRequests=false` 当阻断。按 Owner 的实际用法（**在 Windows 客户端填名字直接连，不走弹窗配对**），改为 Warn 并写清「靠弹窗配对才会中招」。
4. **健壮性**：PowerShell 执行加 60s 超时并杀进程树（管理员修复会弹 UAC，没人点就会挂死 UI 线程）。
5. **新 CLI**：`--apply <fixId>` / `--apply --list` / `--set-param <key> <json>`。

## 证据

- 修复前 `verdict=Blocked`（exit 4）→ 服务修好 + Streamer 起后 `verdict=AtRisk`（exit 3），6 条警告 0 条阻断。
- 本机当前真实状态：`docs/checks.md`（由 `--selftest` 生成）。
- 发布产物哈希：`a18a6b9e0d5b6c12fb65dd546cb00a79aa3fb857731f1c571ce3c97f95b43f47  VdHelper.exe`。
- 逐条修复验证记录：`notes/2026-10-05-fix-verification.md`（本文件补记了第二轮）。

## 下一刀

1. `research/09-failure-corpus`（社区症状词库）报告未落盘，需重派；接进来后把用户原话写进检测项文案。
2. 端到端归因报告（PC 侧 + 头显侧合并）。
3. 文档站三段式（index / checks / faq），骨架复用上一轮 `docs/about.html`。
4. 想看界面的话：`dist/v0.1.0/VdHelper.exe` 直接双击；**本机 Virtual Desktop Streamer 目前是我为验证而启动的，还开着**。

## 没验证的

- `fw-restore-vd` 与 `svc-start`：`Remove-NetFirewallRule` 需要 UAC 提权，本 shell 拿不到，规则删除失败 → 未能端到端验证防火墙修复（MSI 那条能提权成功，说明是命令本身需要管理员，不是环境不允许）。
- 头显侧：没有头显，所有 adb 读取分支都只跑过「无设备」路径。
- 广播收发没有抓包证据（只验证了端口绑定状态）。