# 03 — 文档资产可复用评估（`docs/` 站 → VDHelper 文档站）

> 评估对象（全部只读）：
> - `F:\Project\VirtualDesktop\docs\index.html`（989.8 KB，727 行）
> - `F:\Project\VirtualDesktop\docs\about.html`（78.4 KB，1111 行）
> - `F:\Project\VirtualDesktop\templates\about_template.html`（78.4 KB，1111 行）
> - `F:\Project\VirtualDesktop\docs\background-video.mp4`（27.1 MB）、`docs\.nojekyll`（0 B）
>
> **结论一句话**：`about.html` 的**骨架和视觉语言可以直接复用**（复制 + 改文案），`index.html` **不能复用**（Wix 导出物，改不动也依赖 Owner 的 Wix 站点）。导航只需重排一层。

## 1. 三个文件的实测性质

| 文件 | 大小 | 行数 | 实测性质 | 证据 |
| --- | --- | --- | --- | --- |
| `docs/index.html` | 989.8 KB | 727 | **Wix.com Website Builder 导出物**，不是手写 HTML | `:6` `<meta name="generator" content="Wix.com Website Builder">`；`:26` `wix-essential-viewer-model`；`:97-98` `X-Wix-Meta-Site-Id` / `X-Wix-Application-Instance-Id`；`:100` `X-Wix-Published-Version: 829`；`:564` `wix-fedops` 含 `metaSiteId` / `siteRevision`；`:614` `wix-viewer-model`；`:691` `wix-warmup-data` |
| `docs/about.html` | 78.4 KB | 1111 | **手写单文件**，零外部依赖（除 Google Fonts），内容静态 | `:1-18` 手写 `<head>` + `<style>`；`:429-783` 静态 section；`:784-1110` 内联 `<script>`。无任何 `wix` 标识 |
| `templates/about_template.html` | 78.4 KB | 1111 | **与 `docs/about.html` 逐字节相同** | `md5sum` 两者均为 `794a1ec44ac48b753a464c6c6af5faa5`（已在 F: 侧实跑） |
| `docs/.nojekyll` | 0 B | — | GitHub Pages 关闭 Jekyll 处理 | 文件存在 |
| `docs/background-video.mp4` | 27.1 MB | — | 首页背景视频，被 `index.html` 以 `static.parastorage` CDN 相对路径引用 | `index.html` 内 `background-video.mp4` 出现 3 次（grep 计数） |

### 1.1 一个直接结论：`templates/about_template.html` 是**冗余副本**，不是模板

它和 `docs/about.html` 的 md5 完全一样，即「模板」里**没有任何占位符**（没有 `{{ }}`、没有 `<!--SLOT-->`）。实测 grep：模板文件里找不到任何变量标记结构。所以：

- 它不能当「模板」用（套模板的人会得到一模一样的页）；
- 它是上一轮留的一份**手工同步副本**，内容已经和 `docs/` 分叉过至少一次的可能（`.gitignore:48-50` 单独放行了 `/templates/`，说明 Owner 当时确实打算维护两份）；
- `.gitignore:23-25` 放行 `/docs/**`，其中含 27.1 MB 视频 —— 上一轮的 `.gitignore` 第 3-4 行自述「工作区含 208GB 中间产物」，视频是被特意放进版本控制的。

**对 VDHelper 的影响**：`reference/` 里也已经有 `streamer_ui/` 快照。VDHelper 若要建文档站，**不要复制这种「模板 = 产物副本」的双份结构** —— 那是 L17（文档与代码脱节）的物理来源。

## 2. 能不能直接当 VDHelper 的文档站？

### 2.1 `index.html` — **不能复用**（结论：不改造，直接弃用）

理由（逐条带证据）：

1. **它是 Wix 导出物，不是源码**。页面结构在 `:302-563` 以 Wix 的 mesh/component 协议序列化（`data-mesh-id`、`comp-xxx` ID、`wixui-rich-text__text` 类名）。要改一个字，得改 Wix 后台再重新导出，不是改 HTML。VDHelper 的文档站不能依赖一个外部 SaaS 的编辑器。
2. **它绑定的是 Owner 的 Wix 站点身份**：`:97` metaSiteId `8ce8fd3b-ed81-4ba5-a140-f57b1bb17ca6`、`:98` instanceId `b50a796e-919a-44f8-a5a0-185ca09a66c2`、`:564` `externalBaseUrl: https://www.vrdesktop.net`、`:126` `og:url` 同样指向 `vrdesktop.net`。**这是 vrdesktop.net 官方站点的镜像**，不是 VDHelper 的资产。VDHelper 的文档站挂在 Owner 名下再声明与 vrdesktop.net 无关，会造成身份混淆（`about.html:441` 恰恰花了整段解释「非 Virtual Desktop, Inc. 官方网站」）。
3. **运行时依赖外部 CDN**：`:26` / `:614` 从 `https://static.parastorage.com` 拉 `wix-thunderbolt`、`editor-elements` 等；字体从 `//static.parastorage.com/tag-bundler/` 拉 barlow / poppins / din / proxima。离线打不开。
4. **内容与 VDHelper 主题不符**：`index.html` 的正文是**虚拟桌面支持设备列表 + 串流故障 FAQ**（`:316-332` 头显型号、`:360-371` 电脑配置、`:410-435` 连不上时检查同路由器/防火墙/NAT、`:466-481` 黑屏排查、`:482-527` 串流端装不上）。这些内容 VDHelper 确实用得上，但**形态是 Wix 的 accordion/rich-text，不是可维护的源**。真正的价值在**内容**（这份 FAQ 清单本身就是 VDHelper 检测项的对照表），不在**站点**。
5. **体积**：989.8 KB 单文件 + 27.1 MB 视频。VDHelper 的文档站应该能进仓库、能 review diff。

**处理方式**：把 `index.html` 当**内容来源**读（提取 FAQ 条目 → 映射到 VDHelper 检测项），不复制文件。

### 2.2 `about.html` — **可复用，需改造**（结论：复制 + 改文案 + 减法）

这是三个文件里唯一真正有价值的资产。理由：

1. **零构建、零依赖、单文件**。`:7-9` 只 preconnect Google Fonts（`Inter` / `JetBrains Mono`），其余全部内联。手写 CSS（`:12-427`）+ 内联 JS（`:784-1110`）。拷进任何静态托管都能跑。
2. **视觉语言已成型**：暗色（`--bg:#05060a`）、等宽字体点缀、`.idawin` 仿 IDA 窗口、`.graph` 手绘 CFG 图、`.codewin` 仿反汇编窗口（`:48-63`、`:647-665`）。这套「逆向工程档案」的调性**正是 VDHelper 该用的**（VDHelper 本身是检测/诊断工具，用户要的是「这机器到底怎么了」的工程师视角）。
3. **交互组件已经写好且自带降级**：导航条（`:517-539`，滚动驱动反汇编）、Hex 视图（`:742-756`，可点字符串看改写前后）、pcap 帧列表（`:715-727`）、计数动画（`:544-547`）。这些是**重写成本最高的部分**，直接复用省掉几天。
4. **可访问性已考虑**：`:428` 起有 `@media (prefers-reduced-motion:reduce)` 三处降级（`:24` 滚动条、`:427` reveal 动画）；SVG 有 `role="img"` + `aria-label`（`:453`）。
5. **有页脚免责声明**（`:779-782`），且**已经写好了「非官方网站」的措辞**，VDHelper 可以改主体但保留这个句式。

**必须改造的地方**（见 §4）。

### 2.3 `background-video.mp4` — **不复用**

27.1 MB，视频内容是 Virtual Desktop 的品牌宣传片（从 `index.html:717` 的 `lan_handshake.pcapng` 假包、`VIDEO` 相关 aria-label、`:347` 的 Play Store 原版按钮推断）。VDHelper 没有任何视频素材，引入它等于盗用上一轮的品牌资产。删。

### 2.4 `docs/.nojekyll` — **沿用**

0 字节，作用是让 GitHub Pages 跳过 Jekyll。VDHelper 文档站如果放 `docs/`，这个文件照抄即可，无成本。

## 3. 结论汇总

| 资产 | 结论 | 一句话理由 |
| --- | --- | --- |
| `docs/about.html` | **复用**（复制 + 改造） | 手写单文件、零构建、视觉语言对口、交互组件已写好 |
| `templates/about_template.html` | **不复用**（md5 与产物相同，无占位符，是冗余副本） | 复制它 = 复制上一轮的「模板≠产物」分裂源 |
| `docs/index.html` | **不复用文件，复用内容** | Wix 导出物，绑定 vrdesktop.net 站点身份，依赖外部 CDN |
| `docs/background-video.mp4` | **不复用** | 27.1 MB 他人品牌素材 |
| `docs/.nojekyll` | **沿用** | 0 字节，无成本 |

## 4. 改造清单（若采纳）

### 4.1 页面复用

| 源页面区块 | 源位置 | 处理 | 说明 |
| --- | --- | --- | --- |
| `<head>` + `<style>`（`:1-428`） | 全部 | **直接复用** | 只需改 `:6` 的 `<title>`、`:780` 的页脚主体名 |
| Hero（`:435-509`） | 全部 | **改造后复用** | `:439` 的 `patch offline_lan(image *apk, locale zh_CN)` 是 IL 补丁签名，VDHelper 不是补丁工程 → 换成诊断语义（如 `diagnose(image *host, adapter *nic)`）。`:440` 主标题、`:441-444` lead 三句都要重写。`:451-452` 的 `Keyboard.LoadContent — Graph View` / `IDA View-A` 假 tab → 换成真实的检测项流程图 |
| 计数条（`:544-547`） | 全部 | **复用结构，换数据** | 现在是 `44 / 168 / 13336408 / 126`，四个 `data-to` 分别是 IL 补丁数、AOT 数、blob 字节、报告数。VDHelper 换成：`18` 检测项、`N` 项已真机验证、`N` 项需人工介入等。**计数必须带口径**（见 L05） |
| Core Strategy（`:550-583`） | 全部 | **删除** | 讲的是「删 AOT 强制 JIT」，是上一轮 APK 工程的核心，VDHelper 完全无关 |
| Our Approach（`:585-602`） | 全部 | **改造** | `:589-591` 拿「hosts 劫持 vs IL 字节补丁」做对照。VDHelper 的对照应该是「乱改网络设置 vs 先备份/可回滚」，正好接 ADR-003 |
| Build Pipeline（`:604-638`） | 全部 | **删除** | 三段式 APK 管线，与 VDHelper 无关 |
| Surgical IL Patches（`:641-706`） | 全部 | **改造为「检测项 × 根因 × 修复 × 回滚」** | 这张表（`:667-676`，5 个程序集分布）和 4 张 xref 卡（`:678-705`）的**信息结构完全可复用**，只是内容换掉。`:699-705` 那张「ECMA-335 不变量」卡换成「回滚不变量」卡 |
| Feasibility Proof（`:709-732`） | 部分 | **改造** | pcap 帧组件（`:715-727` + `:978-1055` 的 `FR` 帧数据）可复用，但**帧数据全是编的** —— `:978-1055` 里 `RSA-pub:` / `OFFER` / `AES Key 32 bytes` 等是示意文本。VDHelper 必须换成**真实抓包/真实探测结果**，否则就是「把示意数据做成视觉效果」的复刻。⚠ 这是本节唯一一条**不可照抄**的地方 |
| Localization（`:734-757`） | 全部 | **删除** | 汉化工程，与 VDHelper 无关 |
| Get Involved（`:759-777`） | 全部 | **改造** | `:765-768` 指向 `github.com/dwgx/VirtualDesktop`（上一轮仓库）→ 换成 VDHelper 仓库。`:769-772` 的 QQ 群保留（同一个 Owner） |
| 页脚（`:779-783`） | 全部 | **复用句式，换主体** | `:780` 免责声明保留（改「汉化研究项目」→「检测/诊断工具」） |

### 4.2 文案里已经过时 / 不可用的部分

| 文案 | 位置 | 问题 | 处理 |
| --- | --- | --- | --- |
| 「44 处 IL 字节级补丁」 | `:530`、`:537`、`:544`、`:903` | **数字已漂移**（L05：v13 实为 40）。且这是 APK 工程的数，与 VDHelper 无关 | 整块删除或换数 |
| 「patch offline_lan(image *apk, locale zh_CN)」 | `:439` | 语义是 IL 补丁，不是 VDHelper 的诊断 | 换签名 |
| `Keyboard.LoadContent — Graph View`、`IDA View-A / Hex View-1 / Pseudocode-A / Structures` | `:451-452` | 假 UI（纯装饰 tab，点击无行为） | 换成真实检测流程，或删 |
| pcap 帧数据全部为示意 | `:978-1055` | `ConnectionID 0x7F3A21C8`、`AES Key 32 bytes`、`AES-256-GCM` 等均无抓包出处 | **必须换成真实数据或删掉这块** |
| 「126 份实验报告复盘」 | `:547`、`:763` | 是上一轮的报告数，与 VDHelper 无关 | 换 |
| 「com.dwgx1/VirtualDesktop 仓库 / binary_patch / repack_blob / build_v12」 | `:765-768` | 指向上一轮仓库 | 换 VDHelper |
| 「本站为个人离线汉化研究项目」 | `:780` | VDHelper 不是汉化项目 | 改主体名，保留「非官方网站」句式 |
| `OpenXRHMD::MakeCurrent @0x0A3F0` | `:686` | 这个 RVA 在本轮核对的 `HANDOFF.md` 表里查不到（表里 #61 是 `ReleaseVR @0xAF8C`），`:699-705` 的 `RecreateCylinder @0x1C2D4` 同理（表里是 `Scene.LoadEnvironmentAsync.MoveNext`） | **这是文档与事实脱节的实例**（L17）：展示页上的 offset 与权威表不一致。VDHelper 的文档站**禁止手工写 offset**，必须从权威表生成 |
| `templates/about_template.html` 与 `docs/about.html` 双份 | 两文件 | md5 相同 → 无占位符的冗余副本 | 不复制这个结构 |

### 4.3 导航怎么排

现状（`:433` 只有一个「← 返回首页」）：`index.html` ⇄ `about.html`，两个页面，靠页脚一个链接进出。`index.html:269` 页脚有唯一的 `about.html` 链接；`about.html:781` 页脚有指向自己的签名链接。

VDHelper 建议改成**三段式**，与产品的三屏结构对齐（ADR-005：首屏 PC 网络 / 次屏 Streamer 参数 / 第三屏 ADB 头显）：

```
index.html        首屏 = VDHelper 是什么 + 18 项检测总览 + 一键体检入口
  ├─ checks.html    检测项全表（编号/症状/根因/检查方式/期望/判读/修复动作/回滚/风险级别）
  ├─ about.html     工程档案（原 about.html 的骨架，见 §4.1 改造表）
  └─ faq.html       从 index.html 提取的串流故障 FAQ（对照表，见下）
```

- **导航条放页头**，不要只靠页脚。当前 `about.html` 顶部只有一个返回链接（`:433`），是 Wix 站换皮后的残留。
- **`checks.html` 是 VDHelper 独有的一页**，也必须是唯一真相源：检测项表（ADR-002 的 schema）。`about.html` 里若要展示检测项，只允许**链接过去**，不允许复制表格 —— 复制就是下一次计数漂移的起点（L05）。
- **`faq.html` 的内容来源就是 `index.html`**：把 `:410-435`（连不上）、`:466-481`（黑屏）、`:482-527`（串流端装不上）、`:368-371`（电脑配置）、`:332`（5GHz 有线）这些条目抽成 Markdown，**每条挂一个到 `checks.html` 对应检测项编号的锚点链接**。这样 FAQ 与检测项是一对一映射，不会各自漂移。
- **不要引入构建工具**。三页都是手写单文件 + `.nojekyll`。`about.html` 已经证明这条路可行。

## 5. 一句话总结

**复制 `docs/about.html`（它和它的「模板」），删掉 Core Strategy / Build Pipeline / Localization 三个 section，把 pcap 示意数据换成真实或删掉，把 `index.html` 当 FAQ 内容源而不是站点源，导航加一层 `checks.html` 作为检测项的唯一真相源。** `templates/about_template.html` 不要复制 —— 它的存在本身就是「文档与产物分叉」的证据。