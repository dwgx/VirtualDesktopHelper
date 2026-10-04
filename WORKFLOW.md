# WORKFLOW — VDHelper 编排细则

## 0. 角色

- **omp = 项目主脑**：定架构、拆任务、复核证据、对 Owner 负责。
- **subagent（task 工具）= 工人**：一次一个 bounded slice，产物落盘到**自己那一个目录**。
- 子代理不再开子代理（`task.maxRecursionDepth: 1`）。跨 harness（Claude / Codex）派发走
  `C:\Users\dwgx1\.dwgx\AgentRule\派发.md` 的 dispatch.ps1 流程。

## 1. 派发前三步

```bash
# 1) 占坑：声明独占写路径，撞路径直接拒
python C:/Users/dwgx1/.omp/extra-hands/CACHE/board.py open --scope "research/03-quest-parameters/**" --brief "<id>"

# 2) brief 必须含两节，否则不派：
#    # Non-Conflict  —— 工人只许写 scope 里那一个目录
#    # Acceptance: DONE WHEN: —— 验收句就是验证命令本身

# 3) 一次一批 tasks[]，扇出真切片；不排队、不造假活
```

## 2. brief 必备形状

```
# Target        精确文件/符号；显式写 non-goals
# Non-Conflict  只写 <scope>；其他文件只读
# Change        步骤、API、要跟随的既有约定
# Acceptance: DONE WHEN:  一条能跑的命令，其输出就是证据
# Report        落盘路径（research/<topic>/NN-*.md），答复里只给摘要
```

## 3. 收工

```bash
python C:/Users/dwgx1/.omp/extra-hands/CACHE/board.py close --verdict ok --verify "<真跑过的那条命令>"
python C:/Users/dwgx1/.omp/extra-hands/CACHE/board.py list        # 看谁没收口
```

`--verify` 里必须是**真跑过的命令**，不是「应该可以」。

## 4. 交接

每轮结束写 `handoff/YYYY-MM-DD-*.md`：**这轮做完了什么 / 证据在哪 / 下一刀切哪 / 哪些没验证**。
主脑先读 `handoff/` 最新一篇再派活，避免重复劳动。

## 5. 调研主题与目录（`research/`）

| 目录 | 主题 |
| --- | --- |
| `01-community-repos` | Virtual Desktop 社区 GitHub repo 与开源辅助软件盘点 |
| `02-network-diagnosis` | PC 侧网络检测/修复：防火墙、NAT、网卡、路由、广播 |
| `03-quest-parameters` | Quest 侧可调参数全集（反编译 + 补丁 profile 证据） |
| `04-streamer-settings` | PC Streamer 侧参数全集（配置键、UI 控件、默认值） |
| `05-ui-reverse` | VirtualDesktop.Streamer.exe 的 UI 构造（WPF 视觉树/资源/排版） |
| `06-adb-headset` | ADB 头显检测：能读什么、怎么判故障 |
| `07-vdapkpatcher` | questhelper 的 VdApkPatcher 能力盘点与可瘦身部分 |
| `08-legacy-vdh` | 旧 VDH（0.4.7）资产盘点：哪些代码直接复用 |