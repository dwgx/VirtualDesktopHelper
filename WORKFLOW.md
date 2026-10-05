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
## 9. 发布：标签与资产不可原地覆盖

`gh release upload <tag> --clobber` 用成了默认动作，结果 `v0.2.0` 的资产在发布页写完"让「通过」这两个字先被证明"
之后又被反复覆盖。现在下载到的不是发布当时那一份——发布页从此与资产对不上。

规则：

1. **标签一旦发布就冻结。** 改版就改 `VERSION.txt`、改 `dist/v<版本>`、发新标签。
2. **不写 `--clobber`。** 旧标签上的东西即使知道是错的，也不覆盖；写进新版发布说明里说明。
3. `publish.ps1` 产出的 `dist/v<版本>` 目录名由 `VERSION.txt` 推导，不是手打的。
4. README 里的 `dist\vX.Y.Z\` 必须与 `VERSION.txt` 一致 —— `tools/check-symptom-map.ps1` 会比，不一致就 exit 1。
5. 发布后把 `VERSION.txt` 下载回来核一遍，别信本地那份。

`v0.1.0` 与 `v0.2.0` 的资产都已被覆盖过，页面上有说明；`v0.3.0` 起按上面的规则走。

## 10. 清扫不等于执行：会改机器状态的命令不进清扫

**发生过一次。** 为核对 README 的退出码契约，把每个命令都跑了一遍，其中包含 `--quit-streamer`，
它把 Owner 正在运行的 Virtual Desktop Streamer 杀掉了。当时我在加这个功能时就写过"跑到底会断掉
串流、弹 UAC，先问再做"——然后在例行清扫里就做了。完整证据链见
`notes/2026-10-06-do-not-sweep-state-changing-commands.md`。

规矩：

1. **验证一个命令不等于执行它。** 先读代码判断它会不会改系统状态。
2. 以下命令**不得**进入任何自动清扫或批量核对：
   `--quit-streamer`、`--apply <id>`、`--set-param <key> <value>`（写路径）、
   以及任何会向网络发包的探测。
3. 它们的退出码靠**读代码**确认，或在**副本 / 临时路径**上验证（配置写入路径此前就是这样验的）。
4. 确实要跑，**先说、拿到同意**，并在跑前记录前置状态、跑后对比。
5. 清扫开始前，先把命令清单按「只读 / 会改状态」分一遍，只跑前一类。
