# 02 — 症状 → 工程根因归并

> 输入：`01-symptom-corpus.md` 的 96 条真实用户描述（79 个完整 Reddit 帖 + vddesktop.net 官方 FAQ + 开发者 u/ggodin 公开回复）。
> 目的：把用户在屏幕/口头上说的话，收敛成有限的工程根因集合，**从而决定 VDHelper 下一批检测项**。
> 引用格式：`R01`–`R96` 指 01 号文档主表行号。

---

## 0. 一句话结论
**用户说的「连不上」在工程上被逐条列出 **43 个**互不相同的根因（§2 的 S1–S7：7+9+4+5+6+6+6，各节标题与表格自洽）；其中 **23 个**被单独做过覆盖度评估（见下文「覆盖度小结」），当时的 18 项检测最多覆盖 9 个。** 更要命的是：社区最常给的三个答案（端口转发、开 UPnP、关防火墙全 profile）**对本地局域网场景全部无效或有害**。

> **口径修正 2026-10-05**：本文早期版本的 §0 与覆盖度小结把「23」写成了根因总数，那是覆盖度评估的分母（9 已覆盖 + 4 部分 + 10 未覆盖 = 23），与 §2 实际列出的 43 条不符。根因总数以 §2 为准；`tools/export-docs-site.ps1` 检测到该不一致时会打印 `SOURCE-MISMATCH` 警告。
---

## 1. 症状词典：把用户的话映射到有限症状类

用户会说的词远多于症状，但可以归并为 **7 个症状类 + 12 个子症状**。VDHelper 的第一屏应该直接用这些词。

| 症状类 | 用户会怎么说（真实措辞） | 判定信息（VDHelper 需要拿到的） |
|---|---|---|
| **S1 看不见电脑** | "no computer found" / "no computer detected" / "big yellow sign" / "doesn't see the PC" / "servers unreachable, only showing local computers" | 头显端收到零个广播应答；PC 侧 UDP 38850 是否在发 |
| **S2 连不上** | "computer is unreachable" / "PC is unreachable" / "failed to see any computer at all" / "can't connect to a computer contact support" | 收到了应答但后续 TCP 握手失败 |
| **S3 说不在同一网络** | "not on same network" / "not on same local network" | 两端子网 / AP 隔离 |
| **S4 卡在测速** | "stuck on measuring bandwidth" / "won't get past \"measuring bandwidth\"" / "keeps measuring bandwidth" | 带宽探测阶段超时 |
| **S5 有画面但黑 / 无画面** | "black screen" / "blank screen" / "I can hear audio, but no visuals" / "shows no display" | 音视频分离 → 解码/显示器/编码器 |
| **S6 连上就掉 / 定时卡** | "keeps disconnecting" / "disconnects after 30 seconds" / "freezes exactly at 5-7 mins" / "60-Second Stutter" / "The first time I tried it, it worked perfectly for hours, hasn't worked since" | 周期性 → 时序特征 |
| **S7 画面质量差 / 不跟手** | "choppy" / "stutter" / "bitrate only 60-70" / "black bars when turning my head" / "blurry" | 抖动/丢包/解码耗时 |

**关键洞察：S6 的三个帖子（1tj7glb「每 60 秒」、1s4ln9d「每 5-7 分钟」、jvweq6「第一次之后每次」）周期完全不同，但用户用同一句话描述：「卡」/「掉」。** 单点检测无法区分，VDHelper 必须有**时序/周期性观测**能力。

---

## 2. 症状 → 根因归并表（这一节直接决定检测项）

### S1 看不见电脑（no computer found）—— 对应 7 种根因

| # | 工程根因 | 语料证据 | 现有 FAQ 是否覆盖 | 建议检测项 |
|---|---|---|---|---|
| A1 | **用户名不匹配** | R04（开发者原话 "No computer found means that the entered Oculus username in the Streamer window doesn't match"） | ❌ FAQ 完全不提 | 读 Streamer 配置文件中的账号名，与头显显示名程序化比对 |
| A2 | **填了但没点 Save** | R05 "I forgot to click Save at the bottom of the accounts window" | ❌ 零覆盖 | 读**生效配置值**，并与 UI 显示值交叉验证；配置为空 → 阻断级告警 |
| A3 | **两端版本不匹配**（头显商店版 vs PC 侧 beta） | R06（俄语用户：「версии чуток не совпали，我 потратил 5ч」）；R07 开发者要求两端都是 1.21.0 | ❌ FAQ 不要求比对版本 | 比对 Streamer 版本 ↔ 头显 app 版本，不一致直接报 |
| A4 | **Streamer 未真正就绪**（灰图标）—— 出网做 entitlement check 失败 | R15（"when I'm not connected to the internet the icon is grey"）；R16 "When you first start up Virtual Desktop, it will do an entitlement check on VD's servers" | ❌ FAQ 只在远程场景提 UPnP | 读 Streamer 就绪状态码；测出站连通性 |
| A5 | **头显侧 VPN** | R03 "i was using a VPN in my quest 2 ... when I disconnected the vpn the VD in the quest found my pc" | ❌ FAQ 只说 "Make sure your PC isn't running VPN software" | 明确区分 PC 侧 VPN / 头显侧 VPN 两个检测项 |
| A6 | **头显 MAC 随机化破坏绑定** | R17 多人复现："This was causing issues with binding because the MAC isn't the one the application expected to receive" | ❌ 零覆盖 | 读取头显当前 MAC 与被记录 MAC 是否一致；提示关闭隐私/MAC 随机化 |
| A7 | **杀软/EDR 卸载或禁用了 VD 服务** | R64（官方弹窗 "disable anti virus and reinstall desktop streamer"，开发者明确说 "Windows Defender won't affect it so it has to be something else"） | ⚠️ 官方错误文案直接误导用户 | 检测 `VirtualDesktop.Service` 的**实际运行状态 + 安装身份 + 最后修改时间** |

> **注意 A1–A3 都是「配置类」，全部可程序化判定，A4–A6 需要 PC 以外的信息源（Streamer 状态 / 头显设置）。**

### S2 连不上（computer unreachable）—— 对应 9 种根因

| # | 工程根因 | 语料证据 | 现有 FAQ 是否覆盖 | 建议检测项 |
|---|---|---|---|---|
| B1 | **Windows 网络配置文件是 Public / NetworkProfile** | R60 Streamer 面板原文 "your network profile in windows isn't set to private which can prevent connecting to your comptuter"；R13 社区建议"改成 Public"（**方向相反**） | ⚠️ 面板提了但用工程语言，用户读不懂（R61 "I'm not amazing at computers"） | 读 `NLA` 策略/接口 profile，输出人话："Windows 把这个 WiFi 当公用网络" |
| B2 | **防火墙缺出站规则（只有入站）** | R18 "I noticed that VD didn't have an outbound rule in windows firewall, only an inbound one, so I added one manually." | ❌ FAQ 只会说"别挡防火墙" | 枚举入站/出站规则，检测成对性，指出缺哪条 |
| B3 | **防火墙 profile 级别被整体改动** | R17 解决方式：高级安全里把 Domain/Private 的 Inbound Connections 改 ALLOW；R34 "Event Viewer doesn't record any blocked connections"（即拦截根本没发生，profile 层面才是问题） | ❌ 零覆盖 | 检测 profile 级入站策略 + 事件日志无拦截记录的矛盾态 |
| B4 | **VPN 客户端后台仍在运行**（退出 ≠ 未运行） | R19 "I thought that exiting NordVPN in taskbar and not connecting to it was the same but it was probably running some of that in the background"；点名关闭 "CyberSec" 和 "Invisibility on LAN" | ❌ FAQ 措辞让人以为退出即可 | 检测**进程存活**而非连接状态；点名具体功能开关 |
| B5 | **第三方杀软（非 Defender）** | R21 Avast 排除规则；R60 开发者原话；R43 自述 "there was something hidden in the details tab in the Task Manager named NLSSRV32.EXE (Nalpeiron Licensing Service)" | ⚠️ FAQ 提了 Avast/AVG/McAfee/Norton，但漏了"任意常驻后台进程" | 用「同网线下笔记本正常/台式不行」的对比思路，扫可疑常驻进程 |
| B6 | **VirtualDesktop.Service 未运行 / 身份错** | 本机实测基线：`ServiceLog.txt` 反复 `HRESULT -2147024891 configured identity is incorrect`；R64 官方弹窗同一现象 | ❌ FAQ 归因杀软，方向错误 | 直接读服务状态 + 启动失败码，把 -2147024891 翻译成"服务登录账号口令/权限不对" |
| B7 | **两端版本不匹配（回归型）** | R63 "Happened to me after a VD update. Reinstall the streamer app on the PC"；R52 "It only recently started doing this too" | ❌ | 与 A3 共用检测项，但需按"回归型故障"提级 |
| B8 | **远端场景下 DMZ 配置错误 / 端口未转发** | R21/R22/R23 三条互相矛盾的说法 | ⚠️ FAQ 给了端口号但没给场景边界 | 分诊：同网段 → 不需要端口转发；跨网 → 需要，且必须提示 DMZ 风险 |
| B9 | **官方远端发现服务不可达** | R08 "Virtual Desktop servers partially unreachable, some computers may not appear"；R09 开发者 "It's back up now for remote connections" | ❌ | 内置官方状态源，命中时直接告知"非本机问题" |

### S3 说不在同一网络（not on same network）—— 对应 4 种根因

| # | 工程根因 | 语料证据 | 建议检测项 |
|---|---|---|---|
| C1 | **头显在 Guest 网络** | R10 开发者："disable Guest networks and any AP isolation options"；R11 "you have some other settings on your router to isolate WiFi traffic from wired/ethernet traffic" | 读头显 BSSID/网关 → 与 PC 网关比对；不等网关直接判不同网段 |
| C2 | **AP / 客户端隔离（无线↔有线不通）** | 同 R11 | **主动探测**：PC → 头显 IP 发包，收不到即判隔离。这是社区唯一共识诊断法 |
| C3 | **真不同子网** | R12 "If your Quest has a different first 3 octets of the IP address..." | 比对 IP/掩码/网关三元组 |
| C4 | **第二台路由器/AP 模式配错导致与 PC 不同段** | R02「tplink 路由器当 access point」+ 回复 "They should have same subnet eg. 192.168.1.1xx ... your access point may not be setup right" | 检测是否存在二级路由（PC 与头显网关不同） |

> R13 里 OP 自称 "both the pc and vr are on the same subnet"，开发者仍归因子网不同 —— **用户对"同网"的判断不可信，只有工具读出来才算数**。这是 VDHelper 相对人工排查最硬的价值点。

### S4 卡在测速（measuring bandwidth）—— 对应 5 种根因

| # | 工程根因 | 语料证据 | 建议检测项 |
|---|---|---|---|
| D1 | **笔记本厂商网络加速（Lenovo Vantage network boost）** | R40 开发者唯一回复即此；R41 "Literally just did that 10 minutes ago and was about to come post here that it's working again"；R42 同答案 | 扫 Lenovo Vantage / 厂商网络增强进程 |
| D2 | **自动调码率在探测阶段失败** | R43 "I also have Automatically adjust bandwidth off because if that's enabled it will just get stuck on measuring bandwidth"；R30 同样成因 | 读 Streamer `AutomaticallyAdjustBitrate` 开关 |
| D3 | **云 PC 场景码率设得过高**（Shadow 98Mbps） | R43 "You have Virtual Desktop set to 98Mbps when using Shadow? Unless you have fiber internet that's probably way too high." | 识别目标为云服务时改用云场景判词与默认码率建议 |
| D4 | **网卡协商速率只有 100Mbps** | R46 "in vd stated \"ethernet wired but not with gogabit ethernet\" ... actually 100mbps"，最终发现是路由器 LAN 口只支持 10/100M | **读网卡协商速率**，<1Gbps 报阻断 |
| D5 | **路由器固件随机丢包** | R45 "turns out it was my router firmware, which seems to just drop packets randomly"（NETGEAR → OpenWRT） | 多次主动探测取丢包率，>0 即报，并点名固件方向 |

> **D1 是最反直觉的一条**：ping 9ms、测速 200+、全有线回程、所有安全软件都已正确配置 —— 所有常规指标全绿，仍然卡在测速。VDHelper 若只做"指标检查"会全绿放行，**必须做主动探测**。

### S5 有画面但黑 / 无画面 —— 对应 6 种根因

| # | 工程根因 | 语料证据 | 建议检测项 |
|---|---|---|---|
| E1 | **无已启用且有信号的物理显示器**（拔过线） | R51 "I unplugged the monitor ... I plugged the monitor back in and now my virtual desktop shows a blank black screen. I can see the cursor moving" | 检测当前显示器枚举状态与信号状态 |
| E2 | **远程桌面会话独占显示器** | R50 "if you've got a connection established to your PC from another machine via Microsoft Remote Desktop, disconnect it before connecting with Virtual Desktop. I've found that a remote desktop connection will cause that monitor to appear black" | **检测是否存在活动 RDP 会话** —— 这是社区孤例，但与 VD 第二显示器逻辑完全吻合 |
| E3 | **GPU 硬件编码器不可用 / 被禁用** | R49（Steam Deck："Valve have disabled the hardware video encoder which Virtual Desktop needs for multiple screens"）；R47 社区判 "Sounds to me like a rendering issue" | 检测编码器存在性与是否被切换到软件编码 |
| E4 | **GPU 性能不足（非编码问题，是渲染能力）** | R47 Dell 笔记本，桌面模式正常、进 VR 就黑 | 检测 GPU 型号 + VR Ready 门槛，把"换硬件"与"改设置"分开说 |
| E5 | **多显卡选错（游戏跑在核显上）** | R48 "I found the problem, it had loaded on the wrong graphics card. After disableing the other one it worked 100%" | 检测 SteamVR/游戏进程的 GPU 选择 |
| E6 | **驱动版本与 VD 冲突** | R54 开发者："Make sure you have the latest Streamer installed (1.34.14), that fixes a bug with latest Nvidia drivers (591 and later)"；R55 "solved by installing drivers that 2 months earlier" | 检测 GPU 驱动版本 vs VD 推荐矩阵，**"最新驱动"对 VD 是风险项** |

### S6 连上就掉 / 定时卡 —— 对应 6 种根因

| # | 工程根因 | 语料证据 | 建议检测项 |
|---|---|---|---|
| F1 | **头显端手部/身体追踪** | R57 "The issue is related to hand tracking in the quest 3 settings ... I disabled it and the stutter is completely gone."（PC 侧 Game/Encode/Network/Decode 全部稳定） | ⚠️ **根因在头显设置里，PC 侧 18 项检测结构上无法发现** |
| F2 | **头显解码器卡死**（Decoding >400ms） | R58， Networking 0ms 但 Decoding >400ms；换新头显仍复现 | 读四段指标分布，直接输出"Decoding 红了 → 找 Meta 售后" |
| F3 | **Windows 周期性 WiFi 扫描打断** | R62 "Windows does periodic WiFi searches even with the hotspot on, and it causes this issue" | 检测 WLAN AutoConfig 服务 + 周期性扫描 |
| F4 | **ICS（网络共享）参与转发** | R65 "Using ICS to share my modem internet ... I disabled ALL power saving settings such a Gigabit Lite" | 检测 ICS 状态 |
| F5 | **信号覆盖不足（墙体/距离）** | R56 "my house being built into a hill is partially underground so the wifi strength through the walls is not very strong" | 测 RSSI/信号质量，非单纯速率 |
| F6 | **网卡节能（省电设置）** | R65 同上 | 检测网卡节能开关 |

> **F1 是整个语料里最重要的发现。** 一个 PC 侧全部指标正常、ping 正常、码率正常、路由器同房间、有线的用户，卡顿的根因是**头显设置里的一个手部追踪开关**。VDHelper 当前架构（PC 端检测 + 2.5 秒跑完）在结构上覆盖不了这一类。

### S7 画质差 / 卡顿 —— 对应 6 种根因

| # | 工程根因 | 语料证据 | 建议检测项 |
|---|---|---|---|
| G1 | **PC 与头显挂在不同网关（跨路由）** | R44 "my computer is wired to the first router ... I think the second router deteriotes the signal back to my comp too much" | 检测两端网关是否相同 |
| G2 | **5GHz 信道拥塞 / 信道宽不是 80MHz** | R72 "5Ghz congestion is actually a real issue"; R71 "If your PC shows 866mbps in the VD app on the quest then its 80mhz channel width" | 扫 5GHz 各信道占用，输出人话判词 |
| G3 | **内存 RGB 控制软件等常驻工具占用** | R59 "Disabling the RGB software that controls my RAM ... That's it."（18 个月苦战后解决） | 扫常驻硬件厂商工具 |
| G4 | **过热降频** | R59 追评："I just needed to clean out my fans and replace the thermal paste it was throttling bad" | 检测温度与降频状态 |
| G5 | **BIOS Resizable BAR（社区盲试，收益无证据）** | R60' "I disabled 'resizeable bar' ... and it eliminated the last of the very few stutters"；**首条高赞回复直接反驳** "There's a significant chance that your change is a placebo fix" | ⚠️ 不建议列为检测项，但需在文档中标注为"社区流传、无证据、高风险" |
| G6 | **用户拿码率数字当画质** | R30 "I saw on youtube people are getting 100+ and i seem to only get around 60-70" | 把 60-70 Mbps 翻译成"够用"，纠偏而非报警 |

### 覆盖度小结：被评估的 23 个根因 vs 现在的 36 项检测（2026-10-06 复核）

> 范围：§2 共列出 **43** 条根因；本小结只对其中 **23** 条做过逐条判定（其余 20 条未逐条评估，不代表已覆盖）。
> **2026-10-06 复核**：原判定是对着当时的 18 项检测做的。此后新增了 18 项，下表已逐条重新核对，结论只在这 36 项（PC 侧体检跑 35 项，`headset-deep` 在第三屏）成立。

| 被评估的根因 | 已覆盖 | 部分覆盖 | **完全未覆盖** |
|---|---|---|---|
| 23 | 15 | 6 | **2** |

**完全未覆盖的 2 项**，以及它们为什么不覆盖：

1. **F1 头显端配置（外部注入/篡改）** —— 需要连上头显。`HeadsetDeepProbe` 已经读得到包、
   权限与网络，但**没有覆盖「配置被外部改过」这一类**。要判定它，得知道配置文件的基线值，
   而基线只能由一次干净安装产生。见 `research/06-adb-headset/`。

2. **C2 主动探测（PC 主动发包 / UDP 38850）** —— **明确决定不做**，并且写下了理由。
   试过向 `127.0.0.1:38850` 发 17 字节探测包（见 `notes/2026-10-05-rejected-loopback-probe.md`）：
   17 字节包在协议里是连接前的单播预告包，PC 侧命中该分支会**关闭本次运行的 38850 监听**
   （`-.112.cs:354`）。用一个可能关掉用户发现通道的探测，去换一个「端口活着还是处理器活着」
   的区别，不划算，而且它把整轮体检卡死了 100 秒。
   正确的做法是被动观察（pktmon，需要管理员），不是主动发包。

## 复核结论：原判 10 项未覆盖，现在剩 2 项

| 原未覆盖项 | 现在的状态 | 由谁覆盖 |
|---|---|---|
| A6 头显 MAC 随机化 | **已覆盖** | `headset-deep` 的「A6 头显 Wi-Fi MAC」子判定（第三屏，需 adb） |
| A3/B7 两端版本一致性 | **部分覆盖** | `cfg-version` 读 PC 侧 Streamer 版本；头显侧版本仍需 adb |
| A2 配置是否真正 Save 落盘 | **已覆盖** | `accounts-persisted`（逐种 Accounts 形态）+ `cfg-streamer` |
| B2 防火墙入站/出站规则成对性 | **已覆盖** | `fw-pair`、`fw-outbound`、`fw-profile-inbound` |
| D4 硬编码阈值 >1Gbps | **已覆盖** | `link-rate` |
| B4 VPN 进程存在 | **已覆盖** | `vpn-proc` |
| E2 活动 RDP 会话 | **已覆盖** | `rdp-session` |
| G1 两端编码参数一致 | **部分覆盖** | `cfg-streamer` 读 PC 侧编码字段；头显侧仍需 adb |
| F1 头显端配置 | 未覆盖 | 需要 adb + 干净安装基线 |
| C2 主动探测 | 未覆盖（**有意不做**） | 理由见上 |

---

## 3. 社区常用但错误的解法（VDHelper 必须纠偏）

### 3.1 端口转发 38810/38820/38830/38840 —— **本地场景完全无关**

**社区原话**：
- R14 "I remoted into my home PC ... I was able to set the port forwarding for TCP 38810, 38820, 38830, and 38840 ... It still says \"computer unreachable\"."
- R14 用户自己也问 "anyone got a list of the ports that need forwarding?"

**为什么错**：VD 的**本地**发现与连接不依赖这四个 TCP 端口。已知基线：发现走 **UDP 38850**（配对/发现）与周期广播 **255.255.255.255:38860**；TCP 38810/20/30/40 属于远程连接路径。把本地故障归因于端口转发，会让用户去改路由器，却碰不到真正的问题。
**证据**：R21 用户转发端口后**仍失败**；R44 用户把 "Has any tried forwarding ports for the Quest?" 当作求助方向，帖内无人认为这对本地有效。
**正确说法**：**先判定场景**。PC 与头显同网段 → 不需要任何端口转发；异地 → 需要转发 TCP 38810/20/30/40，且 Streamer 需勾选 "Allow remote connections"。开发者本人在 R29 明确说过 "You don't need to forward ports to connect to Shadow."

### 3.2 开 UPnP —— 方向搞反了，且被用户误解成"开网络发现"

**社区原话**：
- R53 "I have heard about enabling Upnp but I tried that by going into network settings and **enabling network discovery** and that didn't work."
- R22 社区建议 "If possible I'd **disable** UPnP, security reasons, this is why I just suggest direct port forward instead."

**为什么错**：UPnP 与 Windows 的"网络发现"是完全不同的两件事，用户把它们混为一谈（见 R53）。本地场景根本不需要 UPnP；而开启 UPnP 意味着任何程序都能在你不知情时开端口。
**正确说法**：本地场景不开 UPnP；远程场景优先静态 IP + 精确端口转发。R22 那条"禁用 UPnP 改手动转发"的建议是对的，且与开发者口径一致。

### 3.3 关防火墙全 profile —— 掩盖问题，且往往无效

**社区原话**：
- R01 "Full send and turn off your firewall. That's what I did because I was **getting tired of struggling with it**"
- R19 OP 的最终解法其实是补规则，不是关防火墙："I noticed that VD didn't have an outbound rule ... so I added one manually. Now I can connect to my PC **even with the firewall enabled**."

**为什么错**：R01 那位用户明说是因为**放弃了**才关防火墙——这不是解法，是退出排查。而且大量帖子里关防火墙完全无效（R01 "Already did all that a few times."、R34 关了也连不上且事件日志无任何拦截记录）。关掉防火墙还会顺手干掉 ICS、VPN 的接口隔离，制造出新的干扰因素。
**正确说法**：保持防火墙开启，**改成检测哪条规则缺失**。R19 是语料中唯一一个"关防火墙只是诊断手段、正确解法是补规则"的完整案例——这就是 VDHelper 应该给用户的答案。

### 3.4 开 DMZ —— 同一社区内互相矛盾，且方向通常是危险的

**社区原话**（同一帖内）：
- R21（转述开发者）"I had the same problem and the dev ggodin told me to check router DMZ. ... That fixed it"
- R22（开发者本人）"**You have to disable any DMZ configuration** for Virtual Desktop to work."
- R23（社区用户自发警告）"You have to be really careful with DMZ. **You are exposing your PC to full access from the internet** if you use it."

**为什么错**：同一位开发者在同一问题下被转述出"开 DMZ"和"关 DMZ"两个相反建议。其中"开 DMZ"来自二手转述、无原始出处；而社区用户自己的安全警告（把整台 PC 暴露给全互联网）是正确的。DMZ 是安全反模式，不应作为任何 VDHelper 建议的出口。
**正确说法**：精确端口转发 TCP 38810/20/30/40 到 PC 静态 IP；**明确说明 DMZ 是错误做法及其风险**。VDHelper 在这里必须比社区更确定。

### 3.5 手改头显 IP 匹配 PC 网段 —— 会造成 IP 冲突

**社区原话**：R12 "If needed you can **manually assign an IP address on the Quest** to match the PC network"，并让用户"pick any number from 1 to 254... as long as nothing else is on the number you choose"。

**为什么错**：让用户手动在头显上填 IP，等于把 IP 冲突的风险交给用户，而 Quest OS 的 DHCP 行为不受用户控制。这是把网络问题的责任转嫁给最没有能力处理它的一方。
**正确说法**：改路由器配置（关 Guest 网络 / AP 隔离 / 统一网段），不要动头显的 IP。

### 3.6 "重启一切" —— 兜底而非解法，且常被当作正式建议

**社区原话**：R01 "Make sure your firewall does not block Virtual Desktop connections. Make sure your PC is WIRED to your router. **Restart your router/PC/Quest.**"；R52 OP 明确回复 "**Already did all that a few times.**"

**为什么错**：它在社区里的出现频率极高，但被提问者当场否认有效。它之所以流行是因为它零成本、不需要理解问题。
**正确说法**：VDHelper 应把"重启"**降级为最后一招**，并在前面给出真正有信息量的检查。

### 3.7 归因"杀软"—— 官方错误文案带偏了整个社区

**社区原话**：官方错误弹窗 R64 "it says \"virtual desktop service not running, **disable anti virus** and reinstall desktop streamer"；而开发者本人在同帖 R64 明确说 "**Windows Defender won't affect it so it has to be something else.**"

**为什么错**：官方文案把用户直接导向"关杀软"这个无效动作。开发者与自己的文案矛盾。
**正确说法**：查 `VirtualDesktop.Service` 的真实状态与失败码。本机基线已经拿到具体证据：`ServiceLog.txt` 反复 `HRESULT -2147024891 configured identity is incorrect` —— 这是**服务登录身份配置错误**，和杀软毫无关系。

### 3.8 把"No computer found"一律归因用户名 —— 开发者自己的过度归因

**社区原话**：R04 开发者 "No computer found means that the entered Oculus username in the Streamer window doesn't match. Check the spelling."；同帖提问者 R04b 自述已排除 8 项仍失败，同帖另一回复直接回 "**bullshit**"。

**为什么错**：提问者逐项排除了（切私有网络、核对用户名、卸载 Norton、关防火墙、同 5GHz、重装、重启、对版本），用户自己的第一反应就是"这说法不对"。
**正确说法**：用户名不匹配应当**被程序化判定并单独报出**（连两边的实际值一起给），而不是用它解释所有"No computer found"。开发者是对的结论、错的适用范围。

### 3.9 改 BIOS 试运气（Resizable BAR / 各类 BIOS 开关）

**社区原话**：R60' "I disabled 'resizeable bar' today in the bios and was surprised that it eliminated the last of the very few stutters I was getting."；**同帖最高赞回复反驳**："Resizable bar provides your CPU with direct access to video memory and has significant performance increases for a lot of non-vr games. There's a significant chance that your change is a **placebo fix**"；OP 被追问是否验证过复现时未给出数据。

**为什么错**：改 BIOS 是高风险、收益无证据的动作，却在语料中被当作推荐解法传播。
**正确说法**：VDHelper 不跟风推荐，但**可以**提示"你可能已经试过这类改动，目前没有证据表明它对 VD 有效"。

### 3.10 买更贵的路由器作为万能解

**社区原话**：R56 回复 "buy another one and use it dedicated to VR"；R59 OP 甚至 "drove over an hour to pickup an Eero 6E for a bargain price"，问题最终出在内存 RGB 软件上。

**为什么错**：语料里至少三例（R44 换第二台路由器、R59 买 Eero 6E、R62 换专用路由）**都不是最终解因**。设备升级是最贵的盲试。
**正确说法**：先用工具判定瓶颈在哪一段（Game/Encode/Network/Decode）。R72 社区已有的判据可以直接用："if game in red, lower your in-game setting... if encoding in red, consider upgrading your GPU... if networking in red, upgrade/get a dedicated router... **if decoding in red, contact meta support for RMA**" —— 很多时候答案是降画质或换显卡，不是换路由器。

---

## 4. 现有工具与社区帖都没覆盖的空白 —— VDHelper 可独占的价值点

以下 6 条在**官方 FAQ、79 个社区帖、现有 18 项检测**中均无对应物，且都能被 VDHelper 以低成本自动化实现。

### 4.1 头显侧状态的读取（最大结构空白）

现有 18 项检测全部在 PC 侧。语料里至少三个根因在头显上，且**PC 侧指标全绿**：
- F1 手部/身体追踪导致每 60 秒卡顿（R57）—— PC 侧 Game/Encode/Network/Decode 全部稳定
- A6 MAC 随机化（R17，至少 4 人复现，2025-05 至 2026-04 持续有人解决）
- R52 "Meta 固件 v69 后桌面相关全坏"（头显固件版本无人查）

**独占点**：VDHelper 是 PC 端工具，天然拿不到头显设置；但 **VD Streamer 本身与头显有 IPC**（Streamer 界面里的"potential problems"区已经在读头显信息）。把这一层做成结构化读取，是社区完全没有的能力。

### 4.2 主动探测取代人工二分

社区唯一的共识诊断法是 R11 的 "ping your Quest from your PC" + R08 的 "Do VD alternatives like Airlink or SteamLink work? That should tell you if it's a VD or networking problem"。
两者都是**动作**，不是**状态**，必须人去执行。
**独占点**：VDHelper 可把
- 「PC → 头显 IP 可达性」
- 「UDP 38850 主动探测 + 应答计数」
- 「255.255.255.255:38860 广播是否在发」
做成内置的、带成功率的探测，直接产出"隔离 / 不同段 / 服务未起 / 被拦"四选一的判定。

### 4.3 对照产品自动二分（Airlink / ALVR / SteamLink）

R08 有人提出但无人工具化。更强的证据在语料里：
- R67：同一服务 **Mac 正常、Windows 异常** → Windows 侧问题
- R44：**ALVR 稳定、VD 不行** → 两者同用 TCP 38810/20/30/40，问题必在 VD 侧而非网络侧
- R65：**笔记本正常、台式不行**（同网络）→ 本机侧问题
- R46：路由器 LAN 口只支持 10/100M

**独占点**：这四条线索每一条都出现在帖子里，且**每一条都能把排查范围瞬间砍半**。没有任何社区工具会替你做这个二分。

### 4.4 "回归型故障"差分

语料里最常见的求助框架是 R52 "It only recently started doing this too"、R51 "worked fine an hour ago"、R63 "Happened to me after a VD update"、R54 "Didn't use my q3 for month or so"、R45 "The first time I tried it, it worked perfectly for hours, hasn't worked since"。
**现有 FAQ 是纯静态清单，逐条打勾在这种场景下 100% 失效** —— 所有项目都会显示通过，而故障确实存在。
**独占点**：VDHelper 记录 Streamer 配置与检测结果的**历史快照**，故障时输出"自上次正常以来，以下发生了变化"。这正好落在 VDHelper 已有的 `StreamerSettings.json` 上——配置文件的 diff 成本极低。

### 4.5 周期性故障的时序识别

语料里有三个明确的周期性：
- 每 **60 秒**卡 0.5 秒（R57）
- 每 **5-7 分钟**冻结（R58）
- **首次正常，之后每次必掉**（R45）

用户把这三种用同一句话描述。**任何单点检测都无法区分它们**，因为单点全绿。
**独占点**：VDHelper 在 2.5 秒内跑完 18 项的结构下，可以增加一个**低频后台观测**（例如每 30 秒探测一次并记录时间戳），把周期性变成可读结论。这是产品形态上的扩展，不是加一项检测。

### 4.6 用人话重述 VD 自己的错误文案

VD 自己的界面已经在报正确的事，但用的是工程语言：
- R60 面板原文 "your network profile In windows isn't set to private which can prevent connecting to your comptuter" → 用户回应 "I'm not amazing at computers so I'm not exactly sure what that is?"
- R64 面板原文 "disable anti virus" → 开发者本人否认

**独占点**：VDHelper 的定位不该是"更多检测项"，而是**把 VD 已经知道的事翻译成人话**。"你的 Windows 把这个 WiFi 当公用网络看待，关掉后端显才能主动连上你的电脑" 是一句用户能行动的话；"网络配置文件 ≠ Private" 不是。

---

## 5. 给 VDHelper 的具体动作建议（按可落地性排序）

| 优先级 | 动作 | 依据 |
|---|---|---|
| P0 | 检测项改名成人话，并在括号里保留工程原词 | 4.6；R60/R61 |
| P0 | 新增：两端版本一致性（Streamer ↔ 头显 app） | A3/B7，R06/R07/R63 |
| P0 | 新增：网卡协商速率 ≥1Gbps 告警 | D4，R46 整帖定位 |
| P0 | 修正「杀软」相关文案（开发者已否认） | B6，R64 + 本机 -2147024891 |
| P1 | 新增：PC 与头显网关一致性检测 | G1/C4，R44/R02 |
| P1 | 新增：主动探测 PC→头显 + UDP 38850 应答率 | C2/4.2，R11 社区共识 |
| P1 | 新增：防火墙入站/出站规则成对性 | B2，R18 有成功案例 |
| P1 | 新增：VPN **进程存活**检测（区分 PC 侧/头显侧） | A5/B4，R03/R19 |
| P2 | 端口转发按场景分诊；明确写"本地不需要" | 3.1，R21/R29 |
| P2 | 写死纠偏清单：UPnP / DMZ / 关防火墙 / 手改头显 IP / 改 BIOS | 第 3 节全部 |
| P2 | 对照产品二分提示（Airlink/ALVR/Mac/笔记本） | 4.3，R08/R67/R44/R65 |
| P3 | 配置历史快照 + diff | 4.4，R52/R51/R63 |
| P3 | 低频后台时序观测 | 4.5，R57/R58/R45 |
| P3 | 头显侧状态读取 | 4.1，R17/R57/F1 |
