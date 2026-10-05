# 2026-10-05 修复动作实跑验证（Round trip）

目的：证明「检测项说谎」不是问题，**修复真的能执行、真的能回滚**。全程在本机做，做完恢复原状。

## 被验证的修复

`streamer-launch`（`StreamerChecks.StreamerProcessCheck` 的修复项，风险 Low）

```
命令：Start-Process 'C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe'
备份：只启动进程，不改任何配置。
回滚：Stop-Process -Name 'VirtualDesktop.Streamer' -Force
```

## 实跑

```powershell
# 1) 修复前
VdHelper.exe --selftest --out before.txt
#   [Block] streamer-proc  Streamer 进程没有运行——PC 侧不会广播，也不会监听串流端口
#   [Warn ] udp-discovery  UDP 38850/38860 都没有活动

# 2) 执行修复（无头路径）
VdHelper.exe --apply streamer-launch
#   执行 streamer-launch — 启动 Virtual Desktop Streamer
#     命令：Start-Process 'C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe'
#     结果：成功

# 3) 修复后复测
VdHelper.exe --selftest --out after.txt
#   [Pass ] streamer-proc  Streamer 进程运行中（1 个）
#   [Pass ] udp-discovery  UDP 38850 已监听（发现/配对协议）

# 4) 回滚（文档里写的那条命令）
Stop-Process -Name 'VirtualDesktop.Streamer' -Force
VdHelper.exe --selftest --out rollback.txt
#   [Block] streamer-proc  Streamer 进程没有运行
#   [Warn ] udp-discovery  UDP 38850/38860 都没有活动
```

**结论**：诊断 → 修复 → 复测 → 回滚，四个环节都成立，且状态可观察地翻转。

## 顺带证实了一件调研结论（一手）

`Get-NetUDPEndpoint` 在 Streamer 运行期间显示：

```
LocalAddress LocalPort OwningProcess
::               38850         48904   ← Get-Process 48904 = VirtualDesktop.Streamer.exe
```

- **UDP 38850 确实是 Streamer 空闲时就绑定的发现/配对监听端口**，
  这条来自 `research/02-network-diagnosis/01-ports-and-discovery.md`（反编译结论），本轮得到运行时证实。
- **修正一处**：调研写「PC 在 TCP 38810/20/30/40 上全部 bind 0.0.0.0 并 accept」。
  实测 Streamer **空闲时这四个 TCP 端口并没有监听**，它们在会话建立时才绑。
  因此 `port-vd` 检测项在空闲状态下报「空闲」是正确的，不是漏报。

## 还没验证的修复

| 修复 | 风险 | 为什么不跑 |
| --- | --- | --- |
| `disable-adapter:*` | Low | 会断 Owner 的网络（含可能的远程会话） |
| `fw-restore-vd` | Medium | 本机规则已存在，重建会先删后建 |
| `svc-repair` | Medium | 重装服务会改服务账户绑定，需 Owner 同意 |
| `svc-start` | Low | 本机服务已在运行，是空操作 |
| `headset-grant` | Low | 没有头显 |

要验证它们，需要 Owner 点名允许在真机上执行。