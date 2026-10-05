# 13 号调研的综合结论：这些发现哪些变成了检测项

三份调研 + 一份复核，共 1754 行。写这份文件是为了回答一个问题：
**哪些结论变成了工具里的东西，哪些没有，以及为什么。**

## 一、可以直接用的（已经进了代码）

| 事实 | 证据 | 落到哪 |
| --- | --- | --- |
| 局域网发现靠 **UDP 38850**，PC 绑 `0.0.0.0`、只收包+单播回包，从不广播 | `-.112.cs:212/330/451` | `udp-discovery` 原有判据 |
| Streamer 用 `new UdpClient` 独占绑定，**绑不上时异常被静默吞掉**，日志不留一行 | `-.112.cs:281/330/454-456` | `udp-discovery` 新增归属判定 → Block |
| 绑在单个地址而不是 `0.0.0.0` 时，别的网卡进来的发现包收不到 | 同上 | `udp-discovery` 新增绑定地址告警 |
| 头显 token 未知时，PC 走三条 `continue`，**一个字节都不回** | `-.112.cs:432-442`（回复在 `:451`） | `cfg-streamer` 新增可修复项 |
| 配对弹窗由 `ShowPairingRequests` 决定 | `-/-.28.cs:117` | `enable-pairing-requests` 修复动作 |
| 同网段场景完全不需要 UPnP / 端口转发 | `ConnectionManager.cs:242/421/442/469` 的调用都在远程分支 | 既有 myth 纠偏的第 1、2 条 |

## 二、明确**不要**做的（避免制造噪音）

这几条如果做成检测项，会在每一个补丁基线用户身上亮红灯而毫无意义：

| 不做 | 理由 |
| --- | --- |
| `*.vrdesktop.net:443` 可达性 | 客户端的 registry 调用被 `if (americaProofValid)` 一类判断短路，无有效 UserProof 时一个包都不发，与「连不上」没有因果通路 |
| DNS 解析检查 | 同上；且客户端另有硬编码 IP，解析路径与拨号路径不等价 |
| mDNS / 组播检查 | 客户端与 Streamer 两侧全树零命中 |
| 「UDP 38850 上有广播包」当就绪判据 | **PC 从不广播 38850**，只监听并单播回包。用它当判据会永远失败 |
| Meta/Oculus/Steam/Pico 登录态 | 全在头显本地，PC 侧读不到 |
| Assistant / Azure Speech 端点 | 只影响语音问答面板 |
| TLS 证书链检查 | 客户端没做任何证书校验绕过；出问题表现为「连不上」而非「证书错误」 |
| 4 个云端中继端口 38811-16 等 | 只在远程连接时用 |

## 三、还没有定论的（**这一节最重要**）

### 争点：补丁基线到底还会不会广播 UDP 38850

三个 worker 从三条不同路径得到**同一个怀疑**，但都没有实测：

- **02**（发现协议）：Quest 侧反编译失败，标 `[未验证]`。
- **03**（补丁依赖）：说签名门 `GetHasValidIdentityAsync` 是发现总闸 —— **这条已被我推翻**，
  见 `04-signature-gate-verification.md`（该方法全树没有调用方，引用的行号超出文件长度）。
- **01**（端点清单）：换了一条路，指出 `NetworkManager.cs:637` 是三元、
  `!t.Result` 为真时 `FindComputersAsync` 根本不被求值，而 `ComputerDiscoveryClient` 没有实例构造函数，
  于是重签名版本一次广播都不发。

02 与 01 还互相纠了错，这比结论本身更有价值：

- 01 推翻 02 的「Quest 侧方法体被 AOT 剥掉」：`extracted_assemblies/` 里那 7 个
  `VirtualDesktop.*.dll` **文件名和内容全部错位**（`VirtualDesktop.Net.dll` 的 AssemblyTitle 其实是 OpenTK）。
  真正的托管程序集在 `lib/arm64-v8a/libassemblies.arm64-v8a.blob.so`（ELF + XABA + XALZ/LZ4），
  解析可以直接复用 Owner 自己的 `binary_patch.py:63-91`。
- 02 指出 01 初稿里「TraceRoute 无条件对 8.8.8.8 发」是错的（`:468` 有 `!IsOnSameNetwork` 守卫），
  01 已按源码更正。

**但两边共同的行号仍然无法被第三方复核**，因为重新反编译的产物没有落到本仓库。
凡行号对不上的，按 `[未验证]` 对待。

### 与一手经验冲突

Owner 的 `analysis/apk_patch/HANDOFF.md` 记录该补丁基线「自动发现 PC Streamer ✅ 自动连接 /
桌面 LAN 串流 ✅ 流畅」。**应用能跑起来并自动连上，说明广播确实发出去了。**

所以现状是：三条静态推导指向「不广播」，一条一手经验指向「广播正常」。
静态推导的链条每一环都带 `[未验证]`，而一手经验是实际跑出来的。**暂时以一手经验为准。**

### 决定性的实验（需要物理动作）

头显在线时，在 PC 上被动抓 60 秒，看有没有发往 `255.255.255.255:38850` 的 UDP：

- **有包** → 广播正常，上面整条怀疑链作废。VDHelper 继续按网络层排查。
- **没包** → 那「头显找不到电脑」就不是网络问题，VDHelper 该提示的是「先确认补丁基线能正常启动」，
  而不是让人去改路由器。

工具侧已经具备判定能力：`udp-discovery` 现在会告诉你 PC 是否在听、由谁在听。
缺的只是抓包那一半，而这需要管理员权限。

## 四、对 VDHelper 方向的影响

调研之前，这个工具默认「连不上 = 网络问题」。调研之后能说得更准：

1. **同网段连不上，先排除本机，再排除路由器，最后才怀疑头显。** PC 侧的 34 项检测覆盖了前两段。
2. **本机最容易被忽略的三件事**现在都有检测项了：38850 被别的进程占了（日志里查不到）、
   38850 绑错网卡、配对开关关着导致新头显被静默忽略。
3. **不要**把公网可达性塞进体检报告。对补丁基线用户来说，那是一条永远绿或永远无关的红灯。

## 五、还没查清的（留档）

- `libVirtualDesktopNet.dll` 是原生 C++，ilspy 拒读；原生层是否另有监听未确认。
- Meta/Pico/Viveport/Play 四个平台 SDK 的实际 host：程序集里扫不到域名。
- `discord.vrdesktop.net` 只在 exe 字符串表命中，C# 树里没有调用点。
- PC 侧组播组 `ff02::1` 与 Quest 侧 `255.255.255.255` 的互通性只做了静态推导。
- `NetHelper.Region` 按本地 UTC 偏移选区，中国会落到 Europe。
---

## 六、补充：发现链上有**四道闸门**，而且症状分两种

三个 worker 最后收敛到同一张图。这里记下来，因为它直接改变工具该怎么判。

| 闸门 | 位置 | 判据 | 不通过时的症状 |
|---|---|---|---|
| 1 取 token | `NetworkManager.cs:156`（超时 3s/12s） | 出网 | 整链挂 |
| 2 账号身份 | `:160-187` / `:188-215` | `accountID` 非 null 非空 | **`CurrentProcess.Kill()` 自杀** |
| 3 签名比对 | `:637` → `UserSettings.cs:1509-1520` | `signature.GetHashCode()-22 == 1778352230 && _hasValidIdentity` | `EmptyComputersResult`，**连广播都不发** |
| 3b/4 结果入库 | `:814` / `:633`→`:818` | `HasValidIdentity` | 广播回来的条目一条都不遍历 |

**补丁改掉了闸门 2**：`binary_patch.py:226-254` 把 `CurrentProcess.Kill()` 的方法体 NOP 成 `ret`
（宿主程序集是 `VirtualDesktop.Core`，XABA 条目 idx49，RVA 0x3ADC）。

于是补丁基线的失败形态变了：

| 场景 | 结果 |
|---|---|
| 原版 + 首次 + 无外网 | **闪退**（闸门 2 杀进程） |
| 原版 + 身份已缓存 + 断网 | 能用（闸门 3 是纯本地判定，全程不碰服务器） |
| 原版 + 无外网但身份还在 | 能用，列表只剩本地电脑，文案出自 `:762` |
| **补丁基线 + 无外网** | **不闪退，安静地列出零台电脑** |

### 这条对 VDHelper 的硬约束

**「列表空」和「闪退」是两种完全不同的故障，不能都归进网络检查。**
判断依据必须是**头显里那个进程是否还活着**，而不是「列表里没有电脑」。

前者去查 APK 与补丁；后者才轮到本工具的 34 项网络检测。
一个把它们混在一起的工具，会把用户送去改路由器，而真正的原因在头显上。

## 七、两条方法论教训（比结论本身更值钱）

### 1. 从 blob 里取程序集，**先核对 md5/size 再命名**

XABA 名表整体偏移了 **+11**，按名表命名会拿到：

| 条目 | 名表说的 | 实际是 |
|---|---|---|
| idx43 | VirtualDesktop.Net.dll | **OpenTK**（2,126,336 B） |
| idx49 | Xenko.OpenXR.dll | **VirtualDesktop.Core.dll**（40,448 B） |
| idx54 | — | **真正的 VirtualDesktop.Net.dll**（66,048 B，含 `ComputerDiscoveryClient`） |
| idx60 | — | 真正的 `Xenko.OpenXR.dll`（161,792 B） |

这正是仓库里 `research/02-network-diagnosis/01-ports-and-discovery.md:234`
「38811-41 全树零命中」那条错误的根源——那一族端口在真正的 `VirtualDesktop.Net.dll` 里。

可靠键是 `%TEMP%\vd_ep_01\assemblies\idxNN`（以 md5 为准）：
`idx49` = `27bf89c9e588afe47f82094e2afab477`，`idx60` = `c1c7afa880cb97bc1e66a257648b8a3d`。

### 2. **`grep 不到` ≠ `没处理`**

`apk_patch/` 全树（py / md / json）grep `1778352252|GetHasValidIdentityAsync|HasValidIdentity` → **0 命中**。
但补丁是**二进制级**的 IL 改写，源码层的 grep 覆盖不到。

所以「补丁没碰闸门 3」这句话**不能从 grep 推出**。真正的判据只有一个：
把**补丁后的 APK** 解出来，比对 `UserSettings.cs:1518` 那几字节。

**而这台机器上现在没有补丁后的 APK**（`analysis/apk_patch/*.apk` 为 0 个，
只剩 1 GB 的 `VirtualDesktop.Android_1.34.18.0_base.apk`）。这一项因此挂起。

## 八、当前最重要的未决项

`analysis/apk_patch/HANDOFF.md:40,43` 明确写着补丁基线
「桌面 LAN 串流 ✅ 流畅」「自动发现 PC Streamer ✅ 自动连接」。

若这条一手经验成立，则闸门 3 在实际成品上是通过的，
上面的静态推导链在某个环节与成品对不上（最可能是二进制级改动，grep 看不见）。

**判定方法只有一个**：抓包。头显在线时在 PC 上被动抓 60 秒，
看有没有发往 `255.255.255.255:38850` 的 UDP。

- 有包 → 静态链作废，一切按网络层排查，VDHelper 方向不变。
- 没包 → 「找不到电脑」不是网络问题，工具该先提示检查补丁基线能否正常启动。
