## VDHelper 0.1.0 — Virtual Desktop 串流体检

面向使用 patched Virtual Desktop 基线（去联网鉴权、去 Quest 账号鉴权）的玩家。
这一版解决的不是画面问题，而是**「头显找不到 PC / PC 发现不到头显」**这一层。

### 三个界面

- **本机体检**：18 项检测。网卡 / APIPA / 虚拟网卡 / 网络配置文件 / 防火墙规则 / 防火墙总开关 / 端口 / 服务 / Streamer 进程 / 服务日志 / UDP 发现端口 / Streamer 配置 / ICS / 出站策略 / 第三方杀软 / 路由优先级 / 接入方式。
- **串流参数**：111 个配置键，逐项显示当前值、默认值、合法范围、来源 file:line；18 个 DPAPI 键标为只读。
- **头显诊断**：自动查找 adb（本机实测 PATH 里没有），读包名、版本、Wi-Fi、proxy、7 项 HorizonOS 运行时权限。

### 修复都能回滚

每个修复动作都显示**备份**与**回滚命令**，路由器 / 杀软 / 接口 metric 这类本机改不了的只给指引，不假装能自动修。
无头用法：`VdHelper.exe --selftest --out report.txt`（退出码 0/3/4）、`VdHelper.exe --apply <fixId>`、`--apply --list`。
报告导出：`VdHelper.exe --report out.md [--symptom S2]` / `--report-html out.html`，
把一次体检写成能直接发出去的成品（结论行、按症状类分组、折叠的原始输出、与上次的变化、免责声明），
Markdown 与单文件 HTML 两种。不写任何 DPAPI 密文 / 令牌 / 账户条目内容，机器名与局域网地址保留。

### 本机实测抓到的真实故障

| 现象 | 根因 |
| --- | --- |
| 所有网络项正常但连不上 | `ServiceLog.txt` 反复 `HRESULT -2147024891 configured identity is incorrect`，服务在跑但拉不起 Streamer |
| 新头显搜不到电脑 | `StreamerSettings.json` 里 `ShowPairingRequests=false`，配对请求被静默忽略 |
| 官方网络告警不弹 | `DontWarnApps` 含 `NetworkProfile` |
| 发现不稳 | 3 块离线网卡各持一个 `169.254.x.x`，外加 Hyper-V / WSL 虚拟网卡 |

### 边界

不含伪造鉴权，不分发官方二进制，不自动关杀软 / 不改路由器 / 不改路由 metric。
与 Virtual Desktop, Inc. 无隶属关系。

旧版 0.2–0.4.7 的历史封存在分支 `legacy` 与标签 `v0.4.*`。
