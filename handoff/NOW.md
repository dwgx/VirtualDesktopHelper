# 项目板 · VDHelper

## 刚发生
**v0.7.0 已发布并下载回来验过**（SHA256 `35806365…876c5` 一致、二进制能跑），补了 **MIT 授权**（之前公开仓库无许可证＝保留所有权利）。两份只读自审（`research/15-review/04`、`05`）共 13 处「说的比测的多」全部处置，参数校验器用**直接调用发布程序集的探针**对全部 111 行验过，19 用例 0 失败。新增 `usb-headset`（读 `Get-PnpDevice` 匹配 `VID_2833`，回答「头显到底有没有接在这台电脑」——此前全仓零命中）。**渲染 HTML 报告时发现今天最严重的一个缺陷**：标题写「此刻没有已建的 VD 通道」，而 `udp-discovery` 与 Windows 都显示 4 条通道已建立——原因是 `_livePorts` 只从 `fresh` 子集取；两份审计都漏了，因为都在读检测项而没人读被渲染成的文档。社区复扫两份报告落盘，其中 scout 声称「本仓库没点名 AP isolation」**是错的**，已在报告中更正为四处 `file:line`。**五道闸门**（症状反漂移 / 退出码契约 / 报告自洽 / 引证 / issue 模板），CI 连续绿色。

## 下一步
等 Owner 四件：① 开无线调试或插 USB-C（`adb devices` 仍空；**插上后 `usb-headset` 会立刻告诉你 Windows 看没看见它**）；② 管理员那一次点击跑 `tools/capture-discovery.ps1` 抓 60 秒；③ 补丁 APK（`F:/Project/VirtualDesktop/analysis/apk_patch/` 仍是 0 个）；④ **要不要接 STUN**——`NatTypeTester`（MIT，NuGet `Stun.Net`，`net10.0` 直接可消费）能补上 `NatChecks.cs:111-114` 自认的唯一缺口，但接了工具就开始向外发包。**头显侧代码至今一次都没在真机上跑过**，这是我不打算假装完成的那部分。