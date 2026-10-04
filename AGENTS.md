# AGENTS.md — VDHelper 项目主脑规则

> 本文件对 `omp` 与本仓库内所有 subagent 自动生效。Owner: dwgx。对 Owner 中文；代码、注释标识符、commit message 英文。

## 0. 一句话

**VDHelper** = Windows 端工具：对 [Virtual Desktop](https://www.vrdesktop.net) 的 PC↔Quest 串流做
**检测 / 诊断 / 修复**，覆盖本机网络、路由器、防火墙、NAT、ADB 头显侧状态、Streamer 参数与 Quest 侧参数，
基线运行环境是「去联网鉴权、去 Quest 账号鉴权」的 patched Virtual Desktop 客户端（见 `F:\Project\VirtualDesktop`）。

## 1. 目录地图

| 路径 | 作用 | 写权限 |
| --- | --- | --- |
| `src/` | 工具本体（C# / .NET 10 WPF） | 单一写手，默认 omp |
| `research/<主题>/` | 调研结论落盘区，每主题一目录 | 每个子代理**只写自己那一个目录** |
| `notes/` | 长期笔记：术语、坑、决策 | omp |
| `handoff/` | 跨会话交接，每轮一个文件 | omp |
| `docs/` | 用户文档 / wiki / 发布说明 | 按派发 |
| `tools/` | 一次性脚本（探测、抓取、生成） | 按派发 |
| `reference/` | 外部仓库与二进制的**本地快照**（大部分 gitignore） | 不提交 |

外部只读素材（**不要写**，Owner 的树）：

| 路径 | 内容 |
| --- | --- |
| `F:\Project\VirtualDesktop` | 上一轮补丁工程：APK 补丁流水线、反编译源码、5 节知识网络报告 |
| `F:\Project\VirtualDesktop\_upstream\vd` | `github.com/dwgx/VirtualDesktop` 克隆 |
| `F:\Project\VirtualDesktop\_upstream\vdh` | `github.com/dwgx/VirtualDesktopHelper` 克隆（旧版 VDH，本项目的前身） |
| `F:\Project\VirtualDesktop\VdApkPatcher.zip` | questhelper 团队对补丁工程的二次开发（profiles 1.34.18–1.34.22、BAML、ELF/XALZ） |

## 2. 硬规则

1. **不伪造鉴权**：不生成 entitlement / token / UserProof / 签名证明。工具只做网络与配置层的检测修复。
2. **不分发官方二进制**：仓库不含 APK / keystore / 官方 EXE，只含脚本、文档与自建代码。
3. **证据驱动**：任何「Quest 侧参数 X 存在」的结论都要给 `file:line`（反编译源码或 patch profile）；无法确证标 `[未验证]`。
4. **一棵树一个写手**：改同一个文件前先 `board.py open --scope` 占坑。派发流程见 `WORKFLOW.md`。
5. **不改 F: 树**（除 Owner 点名）。
6. **不装常驻服务 / 不改系统级设置**除非 Owner 点名；工具内做修复一律**先备份、后改、可回滚**，并显示将要改什么。
7. **commit 不写 AI 署名；push 要 Owner 点头。**
8. 只做点名的最小改动；没跑过的东西标 `[UNVERIFIED]`。

## 3. 技术栈决定（已定）

- 语言/框架：**C# / .NET 10 WPF**。本机 `dotnet` SDK `10.0.400` + `Microsoft.WindowsDesktop.App 10.0.11` 已装（实测 2026-10-05）。
- UI 目标：**对齐 `VirtualDesktop.Streamer.exe`**（WPF + `Xceed.Wpf.Toolkit`），同类控件语言、类似排版层级。
- ADB：`adb.exe` 不在 PATH。查找顺序与下载来源见旧 VDH 实现（`reference/legacy_vdh/VDH.Extra.cs`），只允许 Google `dl.google.com`。
- 打包：`dotnet publish` 自包含，产物单目录；发布物 = EXE + `SHA256SUMS.txt` + `VERSION.txt`。

## 4. 子代理最终答复格式

首行只允许 `STATUS: DONE` 或 `STATUS: BLOCKED`，随后：**做了什么 / 落盘路径 / 证据（路径+行号或命令输出） / 未验证项**。
报告必须**先落盘到自己那一个 `research/<主题>/` 文件**，再在答复里给摘要。

## 5. 每轮开工五问

1. 这一轮能交出一条能跑的命令或一个能看的文件吗？
2. 要问 Owner 什么？（不在「毁数据 / 花钱 / 停服务 / 两种读法差很远」里就自己定）
3. 这轮我留下的，有哪个是上一轮就该删的？
4. 派出去的活回来了吗？
5. 我要写的这个数字，分母是哪个，分子分母同一次统计吗？