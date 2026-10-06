# 项目板 · VDHelper

## 刚发生
UI 计划收口（P0-1…P0-11、P1 各项完成；P1-13 用 `WindowChrome.CornerRadius="8"` 替代 `AllowsTransparency`，不把窗口踢下 GPU；P1-5 间距 token 明确不做）；新增第四道闸门 `tools/check-exit-codes.ps1`（只读清单硬编码，结构上到不了 `--quit-streamer` / `--apply <id>` / `--set-param`，已接进 CI）；退出码契约九条命令实测全对；**第一次真串流会话**，工具报的 4 个已建立通道与 Windows 逐条对上（方向是本地端口 38810-40 → 头显临时端口，警告写在 `IsLanPeer` 旁边）；`v0.6.0` 之后 **34 个提交未进任何 release**。上一版项目板说 Pages 返回 404、`v0.3.0`、156 处引证——三条都已证伪（Pages `built` 且 200、版本 0.6.0、157 处）。

## 下一步
两份只读审计在跑，等 `research/15-review/04-self-audit-checks.md` 与 `05-self-audit-adb.md` 落盘后逐条处置；Owner 侧四件未决：开无线调试或插 USB-C（串流通了但 `adb devices` 仍为空）、管理员那一次点击跑 `capture-discovery.ps1`、补丁 APK、是否切 v0.7.0。