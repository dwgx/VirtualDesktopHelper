# 报告导出（`--report` / `--report-html`）

体检跑完之后，把结果导成一份**能直接发出去的文本 / HTML**：贴进社区求助帖，或贴进 GitHub issue。
这是 `--symptom S2` 的可交付形态——症状类负责「只挑该看的检测」，本功能负责「挑完之后怎么讲给别人听」。

## 用法

```powershell
VdHelper.exe --report out.md --symptom S2        REM Markdown，适合贴 issue
VdHelper.exe --report-html out.html --symptom S2  REM 单文件 HTML，原始输出折叠在 <details> 里
VdHelper.exe --report out.md                      REM 不带 --symptom：按 7 个症状类分组输出全部 25 项
```

退出码与 `--selftest` 一致：`0` 可串流 / `3` 有隐患 / `4` 阻断 / `5` 运行失败 / `2` 参数写错。
`--report-html` 的目标目录不存在会自动创建。输出固定写 **UTF-8 无 BOM**
（HTML 头部已声明 `<meta charset="utf-8">`，BOM 会在部分论坛的 markdown 渲染器里变成一个多余字符）。

## 两种格式内容一致

| 段落 | 内容 |
| --- | --- |
| 1. 结论 | `verdict` + `VerdictText`；「串流中：N 个通道已建立（端口 …），对端 …」或「此刻没有已建立的 VD 通道」 |
| 1. 机器标识 | 生成时间、机器名、用户名、系统版本、逐状态统计（通过 / 警告 / 阻断 / 未知 / 共 N 项） |
| 2. 检测项 | 按症状类分组；每项 = 状态徽标（通过/警告/阻断/未知）+ 编号 + 一句结论 + 为什么 + 修复（id/风险/备份/回滚）+ 只能指引的事 + **折叠的原始输出** |
| 3. 与上次的变化 | 逐条列 `旧结论 → 新结论`；无变化时写「与上次相比无变化。」 |
| 4. 免责声明 | 不做鉴权判定 / 不分发官方二进制 / 不自动改路由器与杀软 / 观测时点与公开性提醒 |

两处折叠的差异：HTML 用 `<details>`（点击展开），Markdown 没有折叠控件，原始输出走围栏代码块。
围栏长度按内容里最长的连续反引号自动加长，机器输出里出现 ``` 也关不住代码块。

## 分组口径

- **给了 `--symptom Sx`**：只出该类，按 `SymptomClass.RelevantChecks` 的顺序走——那本来就是该类文档里写的排查顺序。
  报告里写明用了哪一类、用户原话（`UserPhrases` 逐字来自社区语料）、以及「已隐藏 N 项无关检测」。
- **不给**：7 个症状类各一组，一项检测会出现在**所有引用它的类**下面。
  「PC unreachable」和「每 5–7 分钟卡一次」都需要 `session-stale`，只放一次会让另一个类看起来不完整。
- 没有任何类引用的检测进「未归入任何症状类」组（当前 25 项全部被引用，该组为空）。

编号 = 该项在整轮体检里的固定位置（与 `docs/checks.md` 表格顺序一致），不是分组内序号——
同一个检测项在多个类下重复出现时编号不变，读者可以拿它去对 `checks.md`。

## 脱敏口径

**不写**：任何 DPAPI 密文、令牌、账户条目内容。
两道防线：

1. 上游本来就只给形状——`FirewallPairChecks` 的 Accounts 只报**分组名与条目数**
   （`OculusQuest = 1 条 ;; Oculus = 3 条`），条目值是 DPAPI 密文，读取与展示都没有意义。
2. `ReportWriter.Redact()` 兜底：DPAPI blob 的 base64 一定以 `AQAA` 开头
   （provider version `0x01` 是头三个字节），命中即替换为「〔已脱敏：DPAPI 密文〕」。

**保留**：机器名、用户名、本机与头显的局域网地址、外网 IP。
没有它们，别人没法判断你的网络环境，报告也就失去意义。免责声明第 4 条明确提醒
「贴到公开场合前请自行确认这些可以公开」。

## 已知缺口（报告自己会说出来）

症状类点名的检测若本轮 PC 侧体检没跑出结果，报告会**点名列出**：

> 这一类点名了 1 项本轮 PC 侧体检没有结果的检测：headset-deep。
> 本报告只跑 PC 侧那一遍（如实列出，不静默略过）；头显侧检测在第三屏，需要 adb 连上头显。

当前命中的是 `headset-deep`（A6 头显 MAC 随机化 / F1 头显端设置 / A5 头显侧 VPN）。
它在 `Core/Adb/HeadsetDeepProbe.cs` 里、由第三屏 `HeadsetViewModel` 触发，不在 `HealthChecks.Create()` 内，
所以本报告永远不会有它——但 S1「头显里看不见电脑」确实靠它覆盖三条根因。
静默略过会让那一栏读起来像「通过」，这是报告最不能犯的错。

## 边界

报告是**读**出来的：它跑一遍体检、写一份历史快照（`HealthHistory.Save`，这样「与上次的变化」才有依据），
然后渲染。导出本身不改任何系统状态、不执行任何修复动作。

## 实现

| 文件 | 作用 |
| --- | --- |
| `src/VdHelper/Reports/ReportWriter.cs` | `Markdown()` / `Html()`：一个共享骨架 + `Md` / `HtmlDialect` 两套方言 |
| `src/VdHelper/Reports/ReportExport.cs` | 无头入口，参数解析、落盘、退出码 |
| `src/VdHelper/App.xaml.cs` | `--report` / `--report-html` 分支（`--report-html` 先判，否则会落进 Markdown 写入器） |

分派逻辑（分组、编号、脱敏、免责声明文案）只写一遍，两种格式不可能互相漂移；
HTML 侧所有来自检测项的字符串走 `WebUtility.HtmlEncode`，`<details>` 里的机器输出不会逃出标签。
