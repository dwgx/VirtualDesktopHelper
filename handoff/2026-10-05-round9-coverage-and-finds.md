# 2026-10-05 第九轮：覆盖度重算 + 补 6 项检测 + 报告导出

## 这轮做了什么

1. **派两个工人并行**：覆盖度重算（43 根因 × 25 检测）、可分享报告导出。
2. **覆盖度重算的结论**（`research/12-coverage-audit/`）：
   - 43 个根因 → **完全覆盖 14 / 部分 5 / 完全没覆盖 24**
   - 24 条未覆盖里：**PC 侧可查 16 / 需头显 6 / 只能问路由器或用户 1 / 原理上不可自动检测 1**
   - 按最坏口径（headset-deep 需要 adb，本机没插线不算覆盖）→ 完全 11 / 部分 5 / 无 27
3. **补了 6 项检测**（25 → 31）：`fw-profile-inbound`、`gpu-pick`、`proc-tuner`、`nic-powersave`、
   `display-inventory`、`cfg-version`；外加把 `cfg-streamer` 补成**真的读取值**。
4. **报告导出**：`--report out.md` / `--report-html out.html`，可配 `--symptom S1..S7`，
   DPAPI 脱敏（实测 `grep -ci AQAA` = 0），原始输出折叠。

## 在这台机器上抓到的真问题（都经过独立复核）

| 检测项 | 结论 | 独立复核方式 |
| --- | --- | --- |
| `gpu-pick` + `display-inventory` | **Virtual Desktop Monitor 状态 Error，ConfigManagerErrorCode=22 = ERROR_DISABLED** | CIM 与 `Get-PnpDevice` 两个来源一致 |
| `fw-profile-inbound` | 三个 profile 的 `DefaultInboundAction` 全是 `NotConfigured`（= Block） | 与 `Get-NetFirewallProfile` 原样一致 |
| `fw-defender` | Private / Public profile **整个是关的** | 同上 |
| `proc-tuner` | 6 个 Armoury Crate 进程常驻 | 进程名 + PID 都在证据里 |
| `cfg-streamer` | `AutoAdjustBitrate=false` | 直接读 `C:\ProgramData\Virtual Desktop\StreamerSettings.json` |
| `net-apipa` | 3 块离线网卡各持一个 `169.254.x.x` | 与 `Get-NetIPConfiguration` 一致 |
| `ics` | SharedAccess 在跑 | 服务状态 |
| `route-metric` | 有线网卡 10，虚拟网卡没排在前面 | `Get-NetIPInterface` |
| `nat-type` | 路由器支持 UPnP，但外网 IP 是 `172.16.80.42`（私有段）→ 双层 NAT | SharpOpenNat 4.0.19 实跑 |

**其中最值得你看一眼的是第一条**：Virtual Desktop 自己那个虚拟显示器驱动是**被禁用**状态
（错误码 22 = ERROR_DISABLED，不是驱动缺失）。这一条正好落在「连上但没画面」那一类症状上。
工具只报不改——改驱动可能连画面都出不来，该由你在设备管理器里决定。

## 两个我自己写错、被真实数据抓出来的 bug

1. `display-inventory` 第一版用 `Win32_DesktopMonitor`，在现代 Windows 上返回 3 条
   「Default Monitor|OK|x」的占位行，于是它报告「4 个显示器全部正常」——近乎无意义。
   改成读 `Get-PnpDevice -Class Display` 后，它和 `gpu-pick` 对上了：同一个设备、同一个错误。
2. `gpu-pick` 第一版用 `Format-Table` 的文本去匹配 `"Error"`，结果匹配上了表头那一格的
   `ConfigManagerErrorCode`，凭空多出一个「异常适配器」。改成分隔符输出后解析。

## 两个环境坑（已写进 notes）

- 本机 PowerShell：续行以空格 + `+` 开头会报 `Missing closing ')'`（5.1 与 7 都复现）。
  **所有脚本一律单行语句。**
- 提权修复不传播子进程退出码，且 `ProcessStartInfo.Verb` 在 `UseShellExecute=false` 时是空操作。
  修复动作现在必须**自查后置条件**，否则就是谎报成功。

## 仍然没验证的（全部需要物理动作）

- 头显侧 adb 三分支：头显在 LAN（192.168.11.14，ping 2ms），但 5555/5554/5556/5557/8080/5558 全关
  → 没开无线调试；不插 USB，adb 看不到它。
- 广播实发抓包：Npcap 在装且能枚举 12 个设备，仍需管理员。
- `fw-restore-vd`：`Remove-NetFirewallRule` 要提权。
- 提权重启 Streamer 这轮跑了一次成功（PID 11120 → 36784），下一次因无人确认 UAC 而如实报失败。

## 下一刀

1. 覆盖度里剩下的 PC 侧空白还有约 10 项（P7 云端端点可达性需要先做前置调研，P8 丢包探测实测 19 秒、
   不能放进主体检，应做成按需的深度项）。
2. `docs/` 的 FAQ 里补一节「这台机器上实际抓到了什么」，让读者看到真实输出长什么样。
3. 症状芯片的 UI 点击仍未逐个实测（筛选逻辑已在命令行验过）。