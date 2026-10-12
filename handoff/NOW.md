# 项目板 · VDHelper

## 刚发生
**v0.9.0 已发布并下载回来验过**（SHA256 `9b54d9c0…18fc81` 一致、`VERSION.txt`=0.9.0、下载的 exe 实跑 rc=4；tag `de68a23`）。这一版的主题是**闸门与作者**：第七道闸门（ADB 只读白名单，13 条命令）写完下一轮就抓到作者自己新加的两条（确认是读之后才入列）；另有两道自建闸门被真实数据推翻、撤掉——**会误报的闸门比没有闸门更糟**。第三屏新增 `A7 电量与发热`（`dumpsys battery` / `thermalservice`，四种结论，「没测成」不冒充「正常」）；两种报告格式现在点名它们带头显 adb 序列号（保留、但说清楚）。发布当天还修掉：文档站把 `未知` 行整行丢掉的老缺陷（说 34 项、实际 36，`usb-headset` 上线以来没露过面）；引证闸门证据树随 %TEMP% 消失 → **确定性重建到 `reference/decompiled/vd/`**（8 个 DLL 的 SHA256 与 §0.2 表逐条一致），157/157 条引用全绿。七道闸门本地全过。

## 下一步
等 Owner 四件：① 开无线调试或插 USB-C（`adb devices` 仍空；**插上后 `usb-headset` 会立刻告诉你 Windows 看没看见它**）；② 管理员那一次点击跑 `tools/capture-discovery.ps1` 抓 60 秒；③ 补丁 APK（`F:/Project/VirtualDesktop/analysis/apk_patch/` 顶层 `.apk` 仍是 0 个；**F: 盘今天可见**，已复核）；④ **要不要接 STUN**——`HMBSbige/NatTypeTester`（MIT，NuGet `Stun.Net`，`net10.0` 直接可消费）能补上 `NatChecks.cs:111-114` 自认的唯一缺口，**但接了工具就开始向外发包，所以没接**。**头显侧代码至今一次都没在真机上跑过**——这是唯一一件不打算假装完成的事。
