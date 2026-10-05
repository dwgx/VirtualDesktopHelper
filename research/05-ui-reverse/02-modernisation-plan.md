# 02 — 界面现代化计划（对照官方 `VirtualDesktop.Streamer.exe`）

> 只读分析 / 未修改任何产品代码 / 2026-10-06
> 官方侧证据树（下称 `S/`）：`F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer\VirtualDesktop.Streamer\`
> 我方证据（下称 `R/`）：`D:/Project/VirtualDesktopHelper/`
> 前置阅读：`research/05-ui-reverse/01-ui-spec.md`（第一轮逆向规格）、`docs/product-spec.md`、`notes/decisions.md`
>
> **本文与 01 的关系**：01 给的是「官方长什么样」的静态规格。本文补三件 01 没做的事 ——
> (a) 01 只测了官方，**没有对照我们今天实际渲染出来的界面**；(b) 01 没有给语义色的可达性结论；
> (c) 01 没有「不许改」的清单。凡与 01 不一致处，本文逐条标注**同意 / 修正 / 不同意**。
