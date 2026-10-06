# 项目板 · VDHelper

## 刚发生
**v0.8.0 已发布并下载回来验过**（SHA256 `351fb942…9677c` 一致、退出码 3、含 `usb-headset`）。这一版修掉 **0.7.0 自己带出来的一个严重缺陷**：报告标题写「此刻没有已建立的 VD 通道」，而同一份报告的 `udp-discovery` 与 Windows 都显示 4 条已建立——原因是 `_livePorts` 只从 `fresh` 子集取，**「有没有」和「新不新」被从同一个集合里读**；**两份审计都漏了，因为它们都在读检测项，没有一个在读渲染成的那份文档**。同一提交里还有两份自审的 13 处（两处本来能让 `--set-param PreferredCodec 999` 写进配置）、脱敏补上三条控制台路径、IPv6 链路本地不再被写成"到公网连接"、新增 `usb-headset`。**六道闸门全部在 CI 里跑并通过**（今天其中两道抓到的是我，不是代码）。社区复扫两份报告落盘，其中 scout 声称的「本仓库没点名 AP isolation」**已被本地 grep 推翻并更正**。两次自建闸门被真实数据推翻后**主动撤掉**（`check-emphasis.py`、栅栏奇偶计数），记在 `notes/`。

## 下一步
等 Owner 四件：① 开无线调试或插 USB-C（`adb devices` 仍空；**插上后 `usb-headset` 会立刻告诉你 Windows 看没看见它**，这是本轮为此新增的）；② 管理员那一次点击跑 `tools/capture-discovery.ps1` 抓 60 秒；③ 补丁 APK（`F:/Project/VirtualDesktop/analysis/apk_patch/` 仍是 0 个）；④ **要不要接 STUN**——`HMBSbige/NatTypeTester`（MIT，NuGet `Stun.Net`，`net10.0` 直接可消费）能补上 `NatChecks.cs:111-114` 自认的唯一缺口，**但接了工具就开始向外发包，所以没接**。**头显侧代码至今一次都没在真机上跑过**——这是唯一一件不打算假装完成的事。