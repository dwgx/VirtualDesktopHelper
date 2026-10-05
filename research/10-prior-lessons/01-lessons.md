# 01 — 上一轮（F:\Project\VirtualDesktop）的工程教训表

> 目的：Owner 上一轮 IL 补丁工程踩过的坑，这一轮（VDHelper）**不能重踩**。
> 一手材料全部只读自 `F:\Project\VirtualDesktop`。本表所有「发生在哪」列都带 `文件:行`。
> VDHelper 侧的对应约束写到最后一列，可直接抄进 `AGENTS.md` / `WORKFLOW.md`。

## 0. 怎么读这张表

- 第 1 列是**教训编号**（`L01`…`L31`），带 ⚠ 的表示上一轮已经明确吃过、这一轮必须写进规则文件。
- 「当时怎么处理的」写的是**上一轮真实做过的事**，不是我的评价。
- 「为什么是错的/代价」区分两类代价：**机器/设备级事故**（BSOD、崩溃、产物失效）与**认知级事故**（结论错、文档错、计数错）。后者在 VDHelper 里更容易复发，因为它不报错。
- 最后一列是**可执行的约束**，不是感想。

## 1. 教训表

| 编号 | 教训 | 发生在哪(文件:行) | 当时怎么处理的 | 为什么是错的/代价是什么 | 这一轮在VDHelper里对应的具体约束 |
| --- | --- | --- | --- | --- | --- |
| L01 | ⚠ 并发开 4 个 Codex 后台 worker 导致真蓝屏 | `INCIDENT_REPORT_20260616_CODEX_PARALLEL_BSOD.md:185-191`；`CLAUDE_CODEX_WORKFLOW.md:70-76` | 事故后把默认并发改为 0，文档写死「不要再启动 4 个 Codex worker」 | `BugCheck 0x109 CRITICAL_STRUCTURE_CORRUPTION`，工作机当场崩；根因**至今未证明**（报告 `:172-179` 明确「只是时间相关，非因果证明」），所以无法靠「避开某个 driver」解除禁令 | VDHelper 的并发只受**路径独占**约束，不受「机器扛不扛得住」约束：同一时间只允许 build/落盘类操作一个，其余子代理必须只读。禁止 `--background` + 轮询等待组合 |
| L02 | ⚠ 边等后台任务边 `status --wait` 阻塞 | `INCIDENT_REPORT_20260616_CODEX_PARALLEL_BSOD.md:64-65`（13:32 status --all，13:33 起 status --wait，13:40:38 崩） | 事后列为禁用项 | 阻塞等待期间主脑无法感知系统异常，等于把唯一的看门狗也一起停掉 | VDHelper 禁止以轮询 job 当心跳；子代理产出必须**先落盘**再报状态 —— 历史教训是「报告只在最终答复里就等于没交」 |
| L03 | ⚠ debugger-router / frida 类调试 MCP 与并发同开 | `INCIDENT_REPORT_20260616_CODEX_PARALLEL_BSOD.md:77-86`（4 号 job 用了 debugger-router） | 写成「不同时开并发和调试后端」 | 与蓝屏时间窗重叠，是仅剩的少数可疑项之一；即使非因果，也不该继续叠风险 | VDHelper 的 ADB 探测走 `adb.exe` 只读命令（已有 `tools/probe-ports.ps1`），**不开任何调试器/内核级 MCP**；需要内核态信息时先问 Owner |
| L04 | ⚠ 版本号跳跃 + 实验脚本雪崩（v7.7 一路跳到 v12，盘上留下 v79..v101 二十多个 blob） | `PROJECT_HISTORY.md:298-319`；盘上 `analysis/apk_patch/libassemblies.arm64-v8a.blob.v7x..v101.so` 每个 12.7MB | 每版一份 `patch_vNN_*.py` + `repack_blob_vNN.py` + `build_vNN_*.py` 三件套，最后才收敛回 `binary_patch.py` | 20+ 个 12.7MB blob ≈ 260MB 废料；更贵的是每次实验都要重建 1GB 级 APK + 装机 + 人工目检 | VDHelper 只允许**一条流水线、一个产物名**。实验分支走 `tools/` 一次性脚本并当场删；任何「为了试一下」不许新建版本化产物 |
| L05 | ⚠ 补丁计数漂移：44 还是 40 | `HANDOFF.md:73`（v13 总补丁数 40）vs `AGENTS.md:10`、`HANDOFF.md:8`、`HANDOFF.md:88`（均写 44）、`HANDOFF.md:186` | 两处数字并存，谁也没去核 | 44 是 v12 的数，40 是 v13（动画版少打 4 处）的数。文档统一喊 44，等于**对外报了一个当前产物里不存在的数**；数字一漂，后面所有「覆盖率/已完成」都不可信 | VDHelper 每个对外数字（检测项数、支持型号数、修复项数）必须写清**口径 + 产物版本 + 生成日期**。禁止出现无出处的「N 个」 |
| L06 | ⚠ Entry #81 计数 14 vs 15，且表里列了三个根本没打的方法 | `HANDOFF.md:157-158`（自注：实际 15 个 nop，旧表列的 ValidateSignature/CheckSignature/OnResume 未打）；`VERIFICATION_REPORT_PHASE1_20260629.md:67-69`（F6） | 只在 HANDOFF 里加了一句自注，主表格未改 | 后续 agent 读表会以为这三个方法已处理，从而**漏掉真正的自毁路径**。文档 bug 比没文档更危险 | 检测项表里每个「已处理/已检测」都必须有对应代码行或 `file:line`；宁可表格少一行，不许多一行 |
| L07 | ⚠ 「强制绘制」整条弯路：v78→v101 十四个版本全在追「怎么把 ControlPanel 画出来」 | `PROJECT_HISTORY.md:298-318`；`V80_CONTROLPANEL_FORCE_DRAW_REPORT_20260623.md:104-112`；`V81_FORCE_PRIMARY_CONTROLPANEL_REPORT_20260623.md:132-142` | 每版改一个变量：强制 `ShowControlPanelAsync`、强制 `FadeInAsync`、强制 `DrawUI`、改 `DrawPrimaryScreen`/`Internal` 的 `Visible`/`Opacity` 门 | 每一步都在**症状层**。真根因是 `Scene.LoadEnvironmentAsync.MoveNext` 里 `RecreateCylinder` 的条件门（`streamingSource != 1 && (IsOnScreen() \|\| ...)`），离线模式 `streamingSource=0` 时 cylinder 几何零尺寸 → 面板根本没有可画的几何体 | VDHelper 每个修复项必须写「**根因在哪一层**」。改症状（强制开关、改显示属性、改渲染参数）在 review 时一律打回。判据：问「这一项为什么坏」能一路问到源头吗 |
| L08 | ⚠ 真修复在初始化，不在绘制 | `analysis/apk_patch/binary_patch.py:2-11`（docstring 直书 ROOT CAUSE）；`PROJECT_HISTORY.md:272-274` | v9 一次性 NOP 掉 49 字节条件门（IL_098D-09BD）让 `RecreateCylinder` 必跑，面板立刻出现 | 与 L07 互为因果。这是**唯一一条**值得抄进规则的经验：找根因的层级比改的次数重要 | 同上；并规定检测项要能区分「症状项」（面板不显示）与「根因项」（init 未执行），UI 只显示根因项的症状映射 |
| L09 | ⚠ 改错层：修 `Keyboard.BeginDraw` 而真因在 `LoadContent` 的反篡改 trap | `PROJECT_HISTORY.md:302-304`、`326-328`；`V85_REWRITE_KEYBOARD_BEGINDRAW_REPORT_20260623.md:21-42` | v8.4/v8.5 连续两版重写 `BeginDraw` 方法体（fat header flags 0x3003 / max_stack 1 / code_size 2） | 键盘崩溃真因是 `LoadContent` 的两个 anti-tamper trap（`_pnCapsLock.CanScroll` NullRef + `Activity.Finish()`）。改错层不但没修好，还把能跑的方法改成不能跑，直接引出 `InvalidProgramException` | 动某个模块之前先确认故障的**产生点与消费点**不是同一个；不确认就改 = 改错层 |
| L10 | ⚠ IL 有效性：只改首字节、不改 fat header → `InvalidProgramException` | `V85_..._REPORT:26-42`（v8.3 报 `IL_0000: ret`；v8.4 报 `IL_0002: bge.un IL_6f060009`）；`HANDOFF.md:165` | v8.5 才整体重写方法头 | 连续两版崩溃，且崩溃点位移说明**Mono 仍在验证残留方法体**，前一次的「改法」只是把失败推迟 | VDHelper 无 IL 补丁，但同构问题在配置改写里存在：`StreamerSettings.json` / 注册表 / Quest preferences 改写后必须重读校验，不接受「写成功」当成功 |
| L11 | ⚠ 栈失衡被运行时宽容掩盖（`brtrue.s`→`br.s` 不 pop） | `VERIFICATION_REPORT_PHASE1_20260629.md:24-33`（`binary_patch.py:181` / `:273`） | 保留，因为 Mono mini-JIT 非验证模式容忍，设备实测正常 | 按 ECMA-335 III.1.7.5 这是不合法 IL。当前环境成立所以「能跑」，换运行时/重新 AOT 就炸。**依赖运行时宽容 = 埋雷** | 检测项若依赖某个 API 的**未文档化宽容行为**，条目上标 `[依赖宽容]` 并给出替代检测路径 |
| L12 | ⚠ 解析走启发式 + 缺总量断言 | `VERIFICATION_REPORT_PHASE1_20260629.md:56-61`（`data.find(b'XALZ')` 线性扫描，缺 `assert len(xalz_entries)==entry_count`） | 判为「严重度低」，只列为建议 | 4 字节 magic 落进压缩数据中段就会伪命中，且**错了不报错**。这类 bug 只在换版本时爆炸 | 所有二进制/协议解析必须：① 有 magic 就必须断言总数；② 解析失败必须抛错，不许「尽力而为返回部分结果」 |
| L13 | ⚠ ELF 重建是 best-effort，却没标出来 | `VERIFICATION_REPORT_PHASE1_20260629.md:51-55`（`repack_blob_v102.py:132-145` 只更新 `e_shoff` 与一个 section 的 `sh_size`，不改 program headers、不改后继 section offset） | 依赖「消费者不当真 ELF 加载」这个前提侥幸成立 | 前提一旦不成立（改 manifest、换 loader）立刻静默损坏。上轮做法是「知道不完整但不写注释」 | 每个 best-effort 路径必须在代码里写明**依赖前提是什么、前提不成立时的症状**。未文档化的宽容不许留 |
| L14 | ⚠ 签名漂移：旧 keystore 被无意 `keytool` 重建 | `SIGNING.md:45-49` | 两个成品（`v13_anim` 旧指纹 `d693…`、`zh_min7` 新指纹 `c5a5…`）签名不一致无法互升，全部弃用改用固定 `vdpatch`（指纹 `aad0…`）重签 | 一次手滑让两个已验证可用的成品同时失效，还要重签 + 重验 | VDHelper 产物（EXE + `SHA256SUMS.txt`）入 git 的只有校验和；发布流水线固定一个构建标识。任何「重建」动作必须先确认不会改指纹 |
| L15 | ⚠ keystore 密码明文写进代码与文档，违反自家硬规则 | 规则在 `AGENTS.md:26`（「Keystore 密码：禁止写进任何文档/代码/输出」）；实现在 `build_v12_no_aot.py:118-119`、`build_zh.py:30,115-116`、`build_v13_anim.py:118-119`、`SIGNING.md:15,30,38` | 三个构建脚本 + 一份说明文档各写一遍 `vdpatch2026` | 自签调试证书的密码确实不是真凭据（`SIGNING.md:20` 也这么解释），但**规则被自己破掉时，规则就不再是规则**。下一把真凭据就会顺着这个口子进来 | VDHelper 不持有任何签名私钥；`.gitignore` 已有 keystore 兜底（`.gitignore:95-101`）。任何口令/token 走环境变量或用户机器上的凭据管理器，且**不许写进 `research/` 与 `notes/`** |
| L16 | ⚠ 安装脚本指向的产物名与文档不一致 | `install.bat:4` 指向 `output\signed_zh_noto.apk`；`HANDOFF.md:5` 与 `AGENTS.md:37` 都写 `signed_v12_no_aot.apk` | 无 | 一键安装脚本是**用户唯一入口**。入口指向不存在的文件，文档再准也没用。上一轮还专门写了 `install.bat` 第一步先卸载（`HANDOFF.md:32`），说明这个入口被认真用过 | VDHelper 的「一键修复」必须有单一入口文件，入口里的产物路径由脚本自己解析（不许硬编码会变的文件名）。发布前跑一次「产物存在性」自检 |
| L17 | ⚠ 文档与代码脱节：签名身份在 CLAUDE.md 里是旧的 | `CLAUDE.md:105-108`（`codex-debug.keystore` / alias `codexdebug` / pass `android`）；`SIGNING.md:45-49`（该密钥 2026-06-29 已废弃，全部改用 `vdpatch`）；`build_v12_no_aot.py:117-119`（实际用 `vdpatch`） | 三份文件三种说法。`PROJECT_HISTORY.md:381-383` 说「已于 2026-06-29 核对统一，以脚本为准」，但 `CLAUDE.md` 至今没改 | 下一轮 agent 读 `CLAUDE.md` 会拿旧密钥名去 rebuild，产出签名不同的包，走一遍刚踩过的签名漂移 | VDHelper 的权威事实**只认一处**（代码或配置），文档指向它并注明「本文件不复制该值」。任何指纹/路径变更，先改那一处，再全文搜旧值 |
| L18 | ⚠ 声明「COMPLETE / 全部通过测试」与未验证项并存 | `AGENTS.md:10`（「44 个补丁，全部核心功能已通过测试」）vs `CLAUDE.md:151`（⏳ 佩戴头显验证文案到位 + 缺字；Desktop 真机渲染待测） | 核心补丁标 COMPLETE，汉化标进行中，两个状态混在同一份入口文档，读者容易只看到前者 | 完成度是多维的（功能 / 设备 / 文案 / 分发各算一维），压成一个词就丢信息。VDHelper 首屏已跑 18 项检测，若也写「全部通过」就是同一个坑 | VDHelper 状态词必须**带维度**：`功能已实现 / 已在真机验证 / 未在真机验证` 三档分开写。禁止裸用 COMPLETE |
| L19 | ⚠ 远程截图拍到的是 Guardian 对话框，验证无效但差点被当证据 | `V81_REMOTE_CAPTURE_FINDING_20260623.md:28-56`；`VD_VIEW_CAPTURE_NOTES_20260623.md:25-37`（`screencap` 产出 0 字节） | 作者明确写了「本次捕获不能用于评估 ControlPanel 可见性」——这一步做对了 | 代价是**两轮设备窗口作废**。截图看起来有内容（Guardian 界面）就容易被当成「环境正常」 | ADB 截图/证据必须先过**有效性门**：目标进程在前台、显示未休眠、窗口 `isOnScreen=true`，三关过了才采信像素内容 |
| L20 | ⚠ 启动脚本改全局设备状态并 force-stop VRShell | `VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:242-266`（`launch_vd.bat:27-32,42-43,47,102-105` 读写 `virtual_proximity_state`、force-stop `com.oculus.vrshell`、clear logcat） | 判定该脚本「不应作为默认启动器」，另写只读诊断脚本替代 | 违反自家规则（`AGENTS.md` / workflow §8「不 force-stop VRShell/SystemUX/Guardian」） | 与 ADR-003 同源：**修复前先备份、显示将改什么、可回滚**。设备侧（ADB）动作默认只读，任何 `settings put` / `am force-stop` 类动作必须显式列出并逐条等 Owner 点头 |
| L21 | ⚠ 包名分裂导致验证不可靠 | `VD_V76_CURRENT_ISSUE_TRIAGE_20260623.md:111-157`（设备上同时装 `com.dwgx1.vd.recovered` 与 `com.dwgx1.virtualdesktop.recovered`，签名/权限/时间戳全不同）；`:466-470` 判定「包名分裂让验证不可靠」 | 记录下来，但实验线一直在短包名上跑 | 日志、启动脚本、权限、签名、库里的可见条目可能指向**两个不同的 app**；所有基于「logcat 里有/没有某行」的结论都不可信 | 检测前必须先确认环境身份：包名、签名摘要、路径、版本。**在结论里显式打印被检对象身份**，不要只打「通过/失败」 |
| L22 | ⚠ 汉化链路：bundle `write()` 后未重新 parse → 小索引偏移错位 → 真机崩溃 | `localization/PLAN.md:101`（126604 vs 126019）；`CLAUDE.md:154` | 修复为 write 后重新 parse 大 bundle 再 `sync_from_incremental`，并验证小/大索引偏移三字段全等 | 这是**唯一一条靠「结构自洽」而非「跑起来」抓到的 bug**：容器解析成功但索引不一致，真机才崩 | 改配置/索引类结构时，验收必须包含**结构自洽断言**（长度、偏移、计数三者互校），不只看「文件写成功了」 |
| L23 | ⚠ BAML 变长替换破坏 deferred 键→偏移表，顺序解析仍过、按键查表崩 | `localization/PLAN.md:46`（`DefAttributeKeyTypeRecord.ReadDefer` → `KeyNotFoundException '3011'`） | 改为结构化 round-trip（移植 dnSpy 读写器，重算所有 deferred 偏移），验收 = 反编译不再崩 + WPF reader 仍通过 | 「顺序遍历能过」给了假的安全感。真 bug 只在**按键访问**这条路径上 | 同上：诊断项的判读逻辑不能只验证「能读出来」，必须验证「读出来的值和真值一致」 |
| L24 | ⚠ 侦察报告结论被后续实测推翻（R1 误判 exe 性质） | `PLAN.md:21`（R1 判「native apphost + 内嵌 WPF」）vs `PLAN.md:42` 与 `localization/recon/R4_baml_repack.md:11,27-33`（实测：普通托管 .NET Framework WPF 程序集，ILONLY） | 在 PLAN.md 里显式写「✅ 纠正 R1 误判」——保留并标注纠正，做法是对的 | 侦察结论被下游当既定前提用，下游路线选择因此走偏 | `research/` 每份结论**必须标注证据等级**：`[实测]` / `[反编译推导]` / `[未验证]`。下游引用必须带等级，不得升级 |
| L25 | ⚠ 退出面普查后仍保留「诊断专用、不许改」的分类 | `EXIT_SURFACES_V76_20260623.md:9-13,37-39`（8 已覆盖 / 1 do-not-bypass / 7 framework / 2 identity-gate） | 对 2 个 `InputSystem` 身份门 kill 回调**只登记不改**，明说「不要盲目改成成功」 | 做对了：分类让「哪些能改、哪些不能」变成可查表而不是靠记性。但上轮后期仍被 tempt 去盲改 | 把「能自动修的」「只能提示的」「不能碰的（要问 Owner）」三类**显式成表**；UI 的「一键修复」只允许第一类 |
| L26 | ⚠ 误加的断言让补丁脚本根本跑不通 | `HANDOFF.md:75-77`（Jun 29 误加断言：`VrApp.OnDestroy` 是 static，块首应是 `ldfld 0x7E` 而非 `ldarg.0 0x02`） | 按反编译源码 + 已知 good 交付件逐字节 diff 改正 | 断言写错 = 脚本完全不可运行；不改断言 = 静默改错位置。两种都不能要 | 「修改前原值断言」必须与上游事实一致，且**要有一条测试证明这个断言会真的拦下来**（否则断言写错了也拦不住） |
| L27 | ⚠ 轻量 review 被当成不复核 | `CLAUDE_CODEX_WORKFLOW.md:196-205`（只抽查关键不变量 + 用 build 日志兜底，不逐文件读 diff） | 明确列为 2026-06-16 用户要求 | 这条在 Codex 场景成立（产物是构建日志）。VDHelper 的产物是**文档结论**，没有 build 日志兜底 —— 直接照搬会退化成「无人复核」 | 轻量 review 只用在「有机器可验证产物」的项；纯文档结论必须逐条抽查 `file:line`（成本低，因为不需要跑） |
| L28 | ⚠ 简报里同时写「只读」和「写文件」→ 沙箱变只读 → 报告写不出来 | `CLAUDE_CODEX_WORKFLOW.md:146-158`；`:157` 原文「不要既说『只读审计』又要求它『写到某文件』」 | 归纳成两条实践规则：要么不写「只读」，要么接受「结论在回信里」 | 派发出去的任务**没有产出物 = 这一轮白跑**，且失败原因极隐蔽（任务本身看起来成功） | brief 里的 `# Non-Conflict` 只写路径、不写「只读」；写「只写 <scope>，其他文件只读」，两者不冲突 |
| L29 | ✅ 清理脚本用 dry-run + 白名单 + 路径前缀断言（上轮做对了，应继承） | `vd_cleanup.ps1:1-14`（护栏四条）、`:40-53`（`Assert-Safe` 校验路径前缀 + KEEP 白名单）、`:113-116`（默认 dry-run） | 默认只列清单，加 `-Execute` 才真删 | 这是上一轮少数**结构正确**的危险操作实现 | VDHelper 若有任何删除能力，必须复制这个形状：默认 dry-run、路径前缀断言、显式白名单、逐条打印结果 |
| L30 | ✅ `.gitignore` 三层：默认忽略 + 白名单放行 + 兜底硬排除（上轮做对了，应继承） | `.gitignore:8-34`（默认 `/*` 与 `analysis/*`）、`:72-101`（兜底排除 `*.apk/*.so/*.keystore`）、`:1-4`（理由：工作区含 208GB 中间产物） | 三层结构 + 顶部写清理由 | 上轮工作区 208GB，不这么写必然误提交。VDHelper 的 `reference/` 全是外部快照，同样高危 | 继承三层结构。VDHelper 现有 `.gitignore`（221B），需确认是否覆盖到这层（尤其 `reference/` 与任何密钥） |
| L31 | ⚠ 「完成」靠人肉目检，没有可复跑的验收入口 | 全篇：所有 report 的 Verification 段落都是「build 成功 + apksigner Verifies + adb install Success」，没有一条一键复验命令 | 靠每篇报告重复贴 SHA256 与命令输出来替代 | 交接时无法一键确认「盘上这份还是不是当时那份」；新 agent 得重跑整个 1GB 管线才知道 | VDHelper 每个验收必须是一条**可复制粘贴、能出非零退出码**的命令（这正是本轮 brief 的 `DONE WHEN` 写法）。文档贴真实输出，不贴「应该可以」 |

## 2. 从这张表提炼的三条总纲

1. **症状层 vs 根因层**（L07、L08、L09）—— 上一轮 14 个版本花在症状层，唯一有效的 v9 一次性解决根因。VDHelper 的检测项 schema（ADR-002）里应当有一列强制回答「这一项的根因在哪一层」。
2. **不报错的那类错最贵**（L05、L06、L11、L12、L13、L15、L17、L22）—— 栈失衡、解析伪命中、best-effort ELF、计数漂移、文档脱节、索引错位。全都不会崩，全都会在下一轮变成「为什么和文档说的不一样」。VDHelper 的防线是：**任何数字带口径，任何宽容路径写前提，任何断言有测试**。
3. **完成度是多维的**（L18、L19、L31）—— `COMPLETE` 和「⏳ 待真机验证」写在同一份入口文档里。VDHelper 首屏已经跑 18 项检测，但「检测跑通」不等于「检测结论在真机成立」。状态词必须带维度。

## 3. 本表与其它两份的关系

- 哪些规矩要原样搬进 VDHelper → `02-discipline-inherited.md`
- `docs/` 站能不能直接当 VDHelper 文档站 → `03-doc-assets.md`

## 4. 上一轮没收尾的活（逐条判断 VDHelper 要不要接手）

> 判据：VDHelper 是**检测/诊断/修复工具**，不是补丁工程。判断标准是「这条未完项的未完成部分，是否是 VDHelper 用户会遇到的故障，且 VDHelper 有能力测它」。

| 编号 | 未收尾项 | 出处file:line | 未完成的部分 | VDHelper接手? | 理由 |
| --- | --- | --- | --- | --- | --- |
| U01 | F1 栈失衡（`brtrue.s`→`br.s` 不 pop bool） | `VERIFICATION_REPORT_PHASE1_20260629.md:24-39`、`:94-97` | 两处补丁的 ECMA 不合规未修；报告自己说「A 级改动需用户点头」「强烈建议先权衡是否值得」 | **不接手** | 这是 APK 补丁的字节级问题。VDHelper 不出 APK、不改 IL。但**继承其精神**：VDHelper 的检测项若依赖运行时宽容（L11），条目必须标 `[依赖宽容]` |
| U02 | F2 定点补丁缺 token 断言 | 同上 `:41-49` | `GameLoop restore_bytes` 无 assert、`VrApp.OnDestroy` 只验越界不验 opcode、call 断言只验 opcode 不验目标 token | **不接手（但继承做法）** | 同上。具体继承：VDHelper 的每个「原值断言」必须有一条测试证明它会真的拦下来（L26） |
| U03 | F4 XALZ 线性扫描缺总量断言 | 同上 `:56-61` | `data.find(b'XALZ')` 可能伪命中，未加 `assert len(xalz_entries)==entry_count` | **不接手（但继承做法）** | 同上。继承：VDHelper 所有二进制/协议解析「有 magic 就必须断言总数」（L12） |
| U04 | F3 ELF 修正 best-effort 未标范围 | 同上 `:51-55` | 只改了 `e_shoff` 与一个 section 的 `sh_size`，未加注释说明 | **不接手** | APK 内部格式。继承：任何 best-effort 路径要写明依赖前提（L13） |
| U05 | F6 HANDOFF 表与代码脱节 | 同上 `:67-69` | Entry#81 表据实更新（删 3 个未打方法、加 `VrActivity.OnDestroy`、计数改 15）**至今未做** | **不接手（但这是本表的活）** | 上一轮的文档债，且 F: 树只读。但它的教训已写进 L06/L17，并成为 VDHelper 的硬要求：检测项表多一行 = 漏一个真故障 |
| U06 | 动画恢复方案 C（自写不依赖引擎 Update 的简易动画） | `CONTROLPANEL_ANIMATION_RESTORE.md:78-80` | 方案 A 已在 v13 落地，方案 C 明确「仅在 A/B 都失败时考虑」，未做 | **不接手** | 上一轮已闭环（`HANDOFF.md:57-73` v13 产出）。VDHelper 无 UI 动画需求 |
| U07 | v7.7 AOT 实验的 native patch site 未重新验证 | `V77_AOT_BUILD_INSTALL_REPORT_20260623.md:163-171` | 三项 offline 检查未做（确认每个 native 地址映射到目标方法体、对比 AOT 与 JIT 行为、单独产物并保留 v7.6 作 rollback） | **不接手** | AOT 路线已被 `:124-129` 判定为回归失败并放弃。VDHelper 不做 APK 构建，无从接手 |
| U08 | APK 汉化：佩戴头显验证文案到位 + 缺字 | `CLAUDE.md:151`；`localization/PLAN.md:103` | 「应用 isSleeping（未佩戴头显），UI 页未渲染，故缺字 warning/方框未观察到」 | **不接手** | 汉化工程，与 VDHelper 的检测/诊断目标无关 |
| U09 | Desktop 汉化：真机 GUI 渲染测试 | `CLAUDE.md:144`、`:151`；`recon/R4_baml_repack.md:118` | 已用 WPF `Baml2006Reader` 离线验证到结构零差异，但「未在真实显示器上启动 exe 渲染窗口（需交互式桌面会话）」 | **不接手** | 同上。VDHelper 自己也是 WPF，自己的渲染由自己的验收管 |
| U10 | Desktop 汉化：code-behind 硬编码串（`#US` 堆）未处理 | `recon/R4_baml_repack.md:117`；`PLAN.md:48` | 运行时拼接的状态文字仍是英文 | **不接手** | 汉化范围问题 |
| U11 | Streamer 搜索时序（先开 PC Streamer 再开 Quest VD） | `HANDOFF.md:51`；`PROJECT_HISTORY.md:341`；`CLAUDE.md:165` | 「先开 Quest VD 后开 PC Streamer 可能搜不到」，无根治，只是记录了规避顺序 | **接手** | 这是**用户真实会遇到的故障**，且 VDHelper 完全有能力测：能否检测出「Quest 侧已启动但 PC Streamer 未就绪」这一时序错配。本机基线里 `ShowPairingRequests=false`（`DONT_WARN_APPS=NetworkProfile`）就是同一族的发现/配对时序问题 |
| U12 | 麦克风线程偶发 `pthread_mutex_lock on destroyed mutex` | `HANDOFF.md:52`；`PROJECT_HISTORY.md:345`；`CLAUDE.md:166` | 「不影响功能」，未定位根因 | **不接手（但可观察）** | 是 Quest 侧应用内警告，PC 端测不到。VDHelper 不该对无法观测的现象给检测项（否则就是 L11 那种「依赖宽容」的条目） |
| U13 | PC 侧防火墙被 Avast/AVG/McAfee/Norton 设成「公用网络」 | `docs/index.html:421-424`、`:497-500`（Wix 页文案）；`:464`（Avast/AVG/McAfee 需卸载或加例外） | 官方 FAQ 只给文字指引，没有可自动化的检测/修复 | **接手（最高优先级）** | 本机基线里 `DontWarnApps=NetworkProfile` + **Defender Private/Public profile 均 False** 正是这一族的具体表现。VDHelper 首屏的防火墙检测项应当覆盖「防火墙配置文件被第三方切成 Public」这一分支，而不只是「防火墙有没有开」 |
| U14 | 完全锥形 NAT / 开放 NAT 未开启 | `docs/index.html:430-431`、`:506-507` | 官方 FAQ 要求路由器开 Full cone NAT；路由器设置本机改不了 | **接手（只解释 + 给指引，不自动修）** | 与 ADR-003 一致：路由器/AP 隔离/VLAN 本机改不了，只解释 + 给指引 + 给检测方法（判断当前 NAT 类型）。这是 VDHelper 检测项的天然边界 |
| U15 | VPN 软件阻断到 PC 的连接 | `docs/index.html:427-428`、`:503-504` | 官方 FAQ 只说「确认电脑没有运行 VPN 软件」 | **接手** | 完全在本机可测：枚举活动网卡上的虚拟适配器（VPN/虚拟网卡）+ 默认路由表异常项。这是纯 PC 侧检测，不需要 ADB |
| U16 | 双重 NAT / ISP DS-Lite 导致连不上 | `docs/index.html:444-450`、`:519-526` | 官方 FAQ 建议联系 ISP 要公网 IPv4 | **接手（只检测 + 解释）** | 本机可测：拿不到公网 IPv4 + 处在运营商 CGNAT 段是可检测的。修复做不到（要找 ISP），正符合 ADR-003 的「只解释」类别 |
| U17 | 已连接但黑屏：HDR / 广色域(WCG) 开启 | `docs/index.html:457-459`、`:533-535` | 官方 FAQ 列出 HDR/WCG 为黑屏诱因 | **接手（需实测确认可检测性）** | HDR 开关状态可从注册表/显示 API 读。⚠ 但这一条**上一轮没有任何证据**表明它对 Virtual Desktop 的串流有实际影响 —— 它是从官方 FAQ 抄来的。接手时必须标 `[未验证]`，否则就是拿官方 FAQ 冒充本机实测（L24） |
| U18 | 已连接但黑屏：显示器未被 Windows 识别 | `docs/index.html:467-469`、`:543-545` | 官方 FAQ：部分显示器/电视需开机才能被检测 | **接手** | `Get-CimInstance` / 显示设备枚举可在检测项里给出「当前是否有无信号输出设备」。属于纯 PC 侧可测 |
| U19 | 首启 RECORD_AUDIO / 存储类权限未预授导致首启卡住 | `HANDOFF.md:54-55` | `install.bat` 只预授 7 个 VR 权限，`RECORD_AUDIO`/存储类「需额外 grant，否则首启卡住」 | **不接手（越界）** | 这是 APK 安装后的问题，VDHelper 不装 APK。但**相关的一条要接手**：Quest 侧权限（7 个 VR 权限）由 ADB 检测项覆盖，属 `research/06-adb-headset` 范畴，不属本表 |
| U20 | 「搜索时序」的更上游：发现/配对请求被静默 | 未在上轮文档留下条目 —— 本机基线 `ShowPairingRequests=false` | 上一轮没记录，但这是本机真实故障 | **接手** | `ShowPairingRequests=false` 直接导致「配对请求不弹窗」，用户体感就是「发现不了 PC」。与 U11 是同一族，应合并成一个检测项而不是两条 |