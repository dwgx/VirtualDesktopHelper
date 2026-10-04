# VDHelper

Windows 端 **Virtual Desktop 串流检测 / 诊断 / 修复工具**。

目标用户：使用**去联网鉴权、去 Quest 账号鉴权**的 patched Virtual Desktop 客户端（基线运行环境）的玩家。
这类基线最大的失败模式不是渲染问题，而是「**头显找不到 Quest / 找不到 PC**」——路由器、防火墙、NAT、
网卡配置、广播发现被挡。VDHelper 把这一整层做成可检测、可解释、可修复的工具。

## 现在做到哪一步

见 `handoff/` 最新一篇。本仓库骨架：

```
AGENTS.md      主脑规则（自动加载）
WORKFLOW.md    派发 / 验收 / 交接流程
src/           工具本体（C# / .NET 10 WPF，对齐 VirtualDesktop.Streamer.exe 观感）
research/      调研落盘区，每主题一目录
notes/         长期笔记
handoff/       跨会话交接
```

## 技术栈

C# / .NET 10 WPF。UI 目标是对齐官方 `VirtualDesktop.Streamer.exe` 的控件语言与排版层级。

## 边界

学习与互操作性工具：**不含**伪造鉴权、**不分发**官方二进制（APK / keystore / EXE）。
只做网络与配置层的检测、解释与修复。

与 Virtual Desktop, Inc. 无隶属关系。