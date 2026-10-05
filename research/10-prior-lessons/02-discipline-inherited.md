# 02 — 纪律继承清单

> 来源：`F:\Project\VirtualDesktop\AGENTS.md`、`CLAUDE.md`、`CLAUDE_CODEX_WORKFLOW.md`（上一轮定下的规矩）。
> 逐条判断在 VDHelper 是否继续沿用，理由必须落到 VDHelper 的具体处境上，不是「因为上一轮这么写」。
> 对应教训编号见 `01-lessons.md`。

## 0. 判断口径

- **沿用**：规矩的前提在 VDHelper 仍然成立，且 VDHelper 已经有或应该有对应机制。
- **改造后沿用**：前提成立但 VDHelper 的形态不同（上一轮是设备/构建产物，VDHelper 是文档与配置结论），照抄会走形。
- **不沿用**：前提在 VDHelper 不成立（VDHelper 不碰 APK、不持有密钥、不操作头显文件系统），或与 VDHelper 已定的 `AGENTS.md` / `WORKFLOW.md` / ADR 冲突。
- 凡「不沿用」的，必须说明**这条规矩在 VDHelper 里由哪条代替**，不留空。

## 1. 清单

| 编号 | 规矩 | 来源file:line | 沿用? | 理由 |
| --- | --- | --- | --- | --- |
| D01 | 不伪造 entitlement / UserProof / Integrity / 签名证明 | `AGENTS.md:22`；`CLAUDE.md:132-133`；`CLAUDE_CODEX_WORKFLOW.md:217` | 沿用（已写入 VDHelper） | VDHelper `AGENTS.md:34` 已逐字继承同一条。上一轮把「NOP 掉自毁」与「伪造证明」分开是本工程最核心的合法性判断；VDHelper 虽然不做 IL 补丁，但「不生成假证明、不模拟平台鉴权成功」这条线同样适用（例如不得在检测报告里伪造「串流可用」） |
| D02 | IL 补丁结论必须有 offset/hex/反编译证据 | `AGENTS.md:23` | 改造后沿用 | 前提（IL 补丁）在 VDHelper 不成立，但**证据要求**成立。VDHelper `AGENTS.md:36` 已写成「任何『Quest 侧参数 X 存在』的结论都要给 `file:line`」，这就是同一条规矩的形态。上一轮的执行方式是 offset + hex + 反编译三方互证，VDHelper 的等价形态是 `file:line` + 命令输出 |
| D03 | 不确定就标注「未验证」 | `AGENTS.md:17`；`AGENTS.md:36` 派生 | 沿用（已写入 VDHelper） | VDHelper 用两种标记且分工不同：`[未验证]`（结论存在但未确证，见 `AGENTS.md:36`）与 `[UNVERIFIED]`（东西没跑过，见 `AGENTS.md:41`）。上一轮只有一个「未验证」，本轮拆成两个是有意的改进 —— 「结论对但没确证」和「代码没跑过」的风险性质不同 |
| D04 | Keystore 密码禁止写进任何文档/代码/输出 | `AGENTS.md:26`；`PROJECT_HISTORY.md:47` | 沿用（但 VDHelper 无对象） | VDHelper 不持有任何签名私钥、不出 APK，这条自然落空。但**上轮自己破了自己的规矩**（`build_v12_no_aot.py:118-119` 等四处明文，见 L15），所以 VDHelper 继承的不是「别写 keystore 密码」这窄版本，而是宽版本：**任何口令/凭据不进仓库、不进 `research/`、不进 `notes/`** |
| D05 | 编排安全：默认不并发多个 Codex、不开后台 worker、不碰 debugger-router | `AGENTS.md:21`；`CLAUDE_CODEX_WORKFLOW.md:70-76`；`INCIDENT_REPORT:185-191` | 改造后沿用 | 「并发=0」这个具体数字来自上一轮那次 BSOD（L01-L03），**根因至今未证明**，所以不能原样继承数字，但**风险必须继承**。VDHelper 的形态是 `WORKFLOW.md` 的路径独占 + brief 非冲突；VDHelper 另有硬地板的 `board.py open --scope`，比上一轮的「口头各写各的」强。VDHelper 额外禁止任何调试器/内核级 MCP（L03） |
| D06 | build / 设备 / 写同一 tracker 一律串行 | `CLAUDE_CODEX_WORKFLOW.md:97-103` | 沿用 | 前提完全成立：VDHelper 有 build（`dotnet build/publish`）、有共享产物目录（`src/VdHelper`）、有唯一入口 `WORKFLOW.md`。且 L31 要求验收是**可复跑命令**，这意味着 build 结果会被反复引用，被并行覆盖的概率比上一轮更高 |
| D07 | 编辑共享文件要串行（被多处调用的公共符号、入口脚本） | `CLAUDE_CODEX_WORKFLOW.md:100` | 沿用（已由 board.py 承接） | VDHelper 的等价物是 `AGENTS.md:37` 的「一棵树一个写手」。VDHelper 的版本更强：占用发生在派发前（`WORKFLOW.md:12`），上一轮是派发后口头约定 |
| D08 | 权威状态文档只由主脑定稿，工人产草稿不直接覆盖 | `CLAUDE_CODEX_WORKFLOW.md:33`、`:189` | 沿用（已写入 VDHelper） | VDHelper `AGENTS.md:17-18` 已把 `notes/`、`handoff/` 划给主脑。上一轮这条的价值在 L17 体现得最清楚：文档脱节发生在多个层级各自改同一事实，VDHelper 必须防同一件事 |
| D09 | 结论默认「待验证」，写进权威文档前主脑抽查关键不变量 | `CLAUDE_CODEX_WORKFLOW.md:196-205` | 改造后沿用 | 见 L27：上一轮能轻量 review 是因为产物有 build 日志兜底；VDHelper 的产物是文档结论，**没有机器兜底**。所以 VDHelper 的版本是「纯文档结论逐条抽查 `file:line`」—— 成本其实更低（不需要跑） |
| D10 | 「恢复成功 / 100% / 等价于原版」这类措辞必须有证据，否则降级 | `CLAUDE_CODEX_WORKFLOW.md:201` | 沿用（见 L18） | VDHelper 首屏输出「阻断/警告/通过」三态，本身就是降级机制。追加一条 VDHelper 特有的：完成度必须分「功能已实现 / 已在真机验证 / 未在真机验证」三档，禁止裸用 COMPLETE |
| D11 | 交付格式固定：结论 / 做了什么 / 证据 / 验证 / 风险 / 下一步 | `AGENTS.md:77`；`CLAUDE_CODEX_WORKFLOW.md:174` | 改造后沿用 | VDHelper `AGENTS.md:52` 已改成 `STATUS: DONE|BLOCKED` + 做了什么/落盘路径/证据/未验证项。差异点：VDHelper 强制**报告先落盘再答复**（`AGENTS.md:53`），这是上一轮没有的 —— 因为上一轮出现过「派出去的活回来没有产物」（L28） |
| D12 | 高危动作先停下问用户，并说明「改什么/为什么/后果/怎么回滚」 | `CLAUDE_CODEX_WORKFLOW.md:209-219`；`PROJECT_COMPLETION_PLAN.md:49-50` | 沿用（已写入 VDHelper ADR-003） | VDHelper ADR-003 把这条升级成了数据模型的一部分：每个修复动作必须声明 `备份位置 / 回滚命令 / 风险级别`。这比上一轮的「停下来问」更强 —— 上一轮的口头询问在 `launch_vd.bat` 那种场景下失效了（L20：脚本里读写了 `virtual_proximity_state`） |
| D13 | 「改系统服务/驱动/启动项/Defender/防火墙/注册表/引导/分区」先停 | `CLAUDE_CODEX_WORKFLOW.md:216` | 沿用，但**要修正措辞** | 关键修正：VDHelper 的**产品功能就是改防火墙与网络配置**（`AGENTS.md:8`）。所以这条不能照抄成禁令，必须改成：**改之前备份 + 显示将改什么 + 可回滚 + 按风险级别分级**，高风险项仍逐条问 Owner。VDHelper 的 `AGENTS.md:39` 已经写成这个形态（「不装常驻服务 / 不改系统级设置除非 Owner 点名；工具内做修复一律先备份、后改、可回滚」） |
| D14 | 绕过签名/授权/许可/entitlement 的尝试直接拒绝，不是「先停」 | `CLAUDE_CODEX_WORKFLOW.md:217` | 沿用 | 与 D01 同源但强度不同：前面几条是「先停问」，这条是「直接拒绝」。VDHelper 继承这个区分，因为「修好串流」与「绕过鉴权」在用户诉求里经常混在一起 |
| D15 | 设备动作清单：不 uninstall / reboot / tcpip / usb / settings put-delete / pm clear / 系统包 disable-enable / force-stop Guardian | `CLAUDE_CODEX_WORKFLOW.md:214-215`；`AGENTS.md` §2 | 沿用 | VDHelper 有 ADB 头显侧检测（`research/06-adb-headset`），必然碰到这些命令。上一轮 `VD_V76_CURRENT_ISSUE_TRIAGE:242-266` 已经列过一份逐条的行号级黑名单，可直接作为 VDHelper 的默认禁止清单 |
| D16 | 「改状态先确认」，唤醒头显/改 `stay_on_while_plugged_in` 属于改状态 | `CONTROLPANEL_ANIMATION_RESTORE.md:87`、`:90`；`V85_..._REPORT:212-214` | 沿用 | 上一轮 `V85_..._REPORT:212-214` 明确记了「本次未写 Quest 设置、未改 virtual proximity 或 `stay_on_while_plugged_in`；`KEYCODE_WAKEUP` 只用了一次，`mStayOnWhilePluggedInSetting` 保持 0」—— 这种「改了也要说」的记账方式值得继承。VDHelper 的 ADB 侧应保留一份「本次会话改过的设备状态」清单 |
| D17 | 一个 Codex 沙箱的权限形态：写任务 vs 只读任务的措辞陷阱 | `CLAUDE_CODEX_WORKFLOW.md:136-160` | 改造后沿用 | 平台不同（omp 的 `task` 工具不是 Codex 插件），沙箱机制不可比。但**结论可移植**：见 L28 —— brief 措辞矛盾会导致产出物写不出来。VDHelper 的形态是 `# Non-Conflict` 只写路径、不写「只读」 |
| D18 | brief 必须写：目标 / 范围 / 不要碰 / 证据要求 / 输出 / 并发隔离 | `CLAUDE_CODEX_WORKFLOW.md:166-176` | 沿用（已升级并写入 VDHelper `WORKFLOW.md:23-31`） | VDHelper 的形状是 `# Target` / `# Non-Conflict` / `# Change` / `# Acceptance: DONE WHEN` / `# Report`，并要求**验收句就是可跑命令**。这是对上一轮 L31 的直接回应 |
| D19 | 多个 Codex 各写各的独立新文件，草稿先落 `*_DRAFT.md`，主脑复核后合并 | `CLAUDE_CODEX_WORKFLOW.md:78-79`、`:106-107` | 沿用（已写入 VDHelper） | VDHelper 的 `AGENTS.md:16` 就是这条：每个子代理只写自己那一个 `research/<主题>/` 目录。`research/README.md:3` 进一步固化为「一目录一写手」 |
| D20 | 关键路径表格 + 「不要动」清单放在入口文档里 | `AGENTS.md:29-43`；`CLAUDE.md:377-379` | 沿用（已写入 VDHelper） | VDHelper `AGENTS.md:13-30` 的目录地图 + 外部只读素材表就是同一形状，且明确标了「不要写，Owner's 树」。这条是防止下一轮 agent 误改 F: 树的关键 |
| D21 | 「不要把已放弃的目录当主工作目录」 | `AGENTS.md:42-43`；`CLAUDE.md:175-176`；`PROJECT_HISTORY.md:75-76` | 沿用 | VDHelper 的对应物是 `reference/`（全部 gitignore、外部快照）和 `F:\Project\VirtualDesktop\analysis\VirtualDesktop.Android_1.34.18.0\modified_repack\`（上一轮明写「不要动」）。VDHelper `AGENTS.md:21` 已把 `reference/` 标为不提交 |
| D22 | 补丁原理/技术约束写进入口文档，失败经验集中到 HANDOFF | `AGENTS.md:65-71`；`CLAUDE.md:168-174` | 沿用（但载体不同） | 上一轮是「入口给约束 + HANDOFF 给失败经验」两层。VDHelper 的对应载体是 `research/` 各主题目录 + `notes/`。要点继承的是**失败经验必须有一处集中地**，否则下一轮会重走 L07 那条十四版弯路 |
| D23 | 「已完成」要有产物指纹（SHA256 + 大小 + 包名 + 签名） | `HANDOFF.md:5-8`；`PROJECT_HISTORY.md:24-26` | 改造后沿用 | 上一轮给 APK 指纹。VDHelper 的对应物是发布物的 `SHA256SUMS.txt` + `VERSION.txt`（`AGENTS.md:48` 已定）。这条同时回应 L05（口径）与 L31（可复跑验收） |
| D24 | 清理脚本默认 dry-run + 白名单 + 路径前缀断言 | `vd_cleanup.ps1:1-14`、`:40-53` | 沿用（有条件） | VDHelper 目前无删除能力，`AGENTS.md:39` 也不允许未经点名的删除。**若将来引入**（比如清理 `reference/` 快照），必须复制这个形状。判定「沿用（有条件）」是因为它现在没有执行对象 |
| D25 | `.gitignore` 三层：默认忽略 + 白名单放行 + 兜底硬排除凭据与二进制 | `.gitignore:1-4`、`:8-34`、`:72-101` | 沿用 | 见 L30。VDHelper 的 `reference/` 全是外部仓库快照（`AGENTS.md:21` 标不提交），风险结构与上一轮 208GB 中间产物完全同构 |
| D26 | 报告写「已证实」与「未证实」两段，不把时间相关当因果 | `INCIDENT_REPORT:160-179` | 沿用 | 上一轮 BSOD 报告严格分「What Is Proven / What Is Not Proven Yet」，这是全篇最有价值的方法论之一。VDHelper 的检测结论同理：「检测到 X」不等于「X 是故障原因」。这一条 VDHelper 目前**没有**写进 `AGENTS.md`，建议补 |
| D27 | 侦察阶段的结论可以被后续实测推翻，推翻要留痕 | `localization/PLAN.md:42`（纠正 R1）；`recon/R4_baml_repack.md:11` | 沿用（建议写入 VDHelper） | 见 L24。VDHelper 的 `research/` 里已有不少「推导」结论（反编译、patch profile），一旦被实测推翻必须留痕而不是静默改写，否则下一轮会把旧结论当既定前提 |
| D28 | 每次交接写一份 handoff，写清「没验证什么」 | `CLAUDE_CODEX_WORKFLOW.md:190`（第 8 步）；`AGENTS.md:52` | 沿用（已写入 VDHelper） | VDHelper `WORKFLOW.md:44` 已有同形状的 `handoff/YYYY-MM-DD-*.md`。要点是「哪些没验证」这一节必须存在 |
| D29 | 上游事实只认一处，其余文档引用不复制 | 反向总结自 `PROJECT_HISTORY.md:381-383` 与 L17 | 建议新增（上一轮没做到） | 上一轮写了「以脚本为准」，但 `CLAUDE.md:105-108` 仍留着旧密钥名 —— 说明这条规矩**上一轮定过但没落实**。VDHelper 应把它写成可执行的：改事实源后必须全文搜旧值。这一条是本文档对上一轮的实质性修正 |
| D30 | 对外数字必须带口径与版本 | 反向总结自 L05、L06 | 建议新增（上一轮没做到） | 上一轮的 44/40/14/15 四个数字互相矛盾且无人发现。VDHelper 首屏有 18 项检测，更容易出现「18 项里几项真机验证过」这类分母问题。VDHelper `AGENTS.md:60` 已有「我要写的这个数字，分母是哪个」，需落成检测项表的字段 |

## 2. 汇总

| 判定 | 条数 | 编号 |
| --- | --- | --- |
| 沿用 | 20 | D01、D03、D04、D06、D07、D08、D11、D12、D14、D15、D16、D18、D19、D20、D21、D22、D23、D26、D27、D28 |
| 改造后沿用 | 6 | D02、D05、D09、D10、D17、D24 |
| 建议新增（上一轮定过或该定但没落实） | 3 | D29、D30，以及 D26 落到 `AGENTS.md` 的那条 |
| 不沿用 | 0 | —— |

**没有一条完全不沿用**，但有 6 条必须改造形态（照抄会走形），3 条要新增。

## 3. 三个需要主脑决策的点

1. **D05（并发）**：VDHelper 若不写死并发数，就等于放弃上一轮用 BSOD 换来的那条教训。建议在 `WORKFLOW.md` 补一句：允许并发的子代理必须全部只读（除自己的 `research/<主题>/`），且同一时间只允许一个执行 build。
2. **D13（改防火墙）**：这条与 VDHelper 的产品定义直接冲突，必须确认 `AGENTS.md:39` 的措辞（先备份/显示/可回滚 + 分级）是否就是 Owner 认可的形式，还是需要更严的分级表。
3. **D26、D29、D30 三条建议新增**：上一轮没落实，本轮要么写进 `AGENTS.md` 要么明确不写。建议写 —— 三条的共同点是「防止不报错的错」，成本极低。