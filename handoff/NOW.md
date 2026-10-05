# 项目板 · VDHelper

## 刚发生
连着多轮在做同一件事：**把「生成对了、但呈现/措辞错了」的错误挖干净**，并且每一处都改成闸门或可执行验证，而不是靠记性。这一串的形状已经稳定复现三次：措辞比实测多走一步（`net-loss` 把睡着���头显报成 100% 丢包并指向路由器；`proc-tuner` 只匹配了进程名却声称测了 CPU 与线程；`session-stale` 在「恰好存在残留套接字」的分支上说「也没有残留套接字」）。同源清理还包括：issue 模板是旧格式（缺 `body:`，整套指引从未生效）、`--report` 被 `--selftest` 吞掉、三个需要管理员的修复不说会弹 UAC、`asAdministrator` 是个被丢弃的假参数、写配置会把 DPAPI blob 的 `+` 改写成 `\u002B`、聚焦报告印着 `Sx` 占位符并把自己说两遍、CLI 的体检历史跨判定规则画了界面拒绝画的跳变。新增三道闸门：`check-citations.py`（156 处 `file:line` 全部可解析）、`check-issue-form.py`、`capture-discovery.ps1`（把散在提示文本里的 pktmon 四步收成一条命令）。覆盖度从 9/23 重核到 **15/23**，`v0.3.0` 已切出并且标签从此冻结。

## 下一步（仍卡在 Owner，三件事任一即可解锁）
① 头显在线时以管理员跑 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-discovery.ps1`（`-Analyze <文件>` 可单独判读已有抓包，不需要管理员）——这决定「同网段连不上」该不该归到网络上；② 或把补丁后的 APK 放回 `F:/Project/VirtualDesktop/analysis/apk_patch/`（刚复核，仍是 0 个）；③ 或退出 Streamer，让 `--set-param` 的成功路径与 `--quit-streamer` 的提权分支第一次被真正跑到（普通权限 `Stop-Process` 被拒已实测，UAC 弹窗不宜对着 Owner 的机器自顾自触发）。另有一件待点头：`gh api -repos/dwgx/VirtualDesktopHelper/pages` 返回 404，开 Pages 后文档站的相对链接才成立。