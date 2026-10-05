# 2026-10-05 环境限制：合成鼠标输入到不了本窗口

## 现象

用 `SetCursorPos` + `mouse_event` 合成点击，按钮和症状芯片都**没有任何反应**：
- 点「深度探测丢包」按钮 → 什么都不发生（连等 28 秒后的界面与点击前逐字节相同）
- 点症状芯片「头显里看不见电脑」→ 列表不过滤，芯片不高亮
- 点已知的可点区域多次，`_deep2.png` 与 `_deep3.png` 的 SHA256 **完全相同**

`PrintWindow` 截图正常，说明窗口渲染没问题；所以不是坐标算错，是**输入事件根本没送达**。

## UIA 也看不到内容控件

`AutomationElement` 从 `RootElement` 按 ProcessId 找到窗口后，`Descendants` 里只有 **2 个按钮**
（标题栏的最小化与关闭）。WPF 内容区里的「深度探测丢包」按钮完全不在自动化树里。
所以这不是 UIA 用法问题——本环境下 WPF 的内容控件没有暴露给 UIA。

## 结论：这条验证路径在本机不可用

**症状芯片与按钮的「鼠标点击 → Command」这一段，本机无法直接验证。** 不要反复重试合成输入。

## 已经拿到的证据（绕开输入层）

| 环节 | 证据 |
| --- | --- |
| 按钮渲染 | `_shot*.png` 截图里按钮可见、位置正确、样式与官方一致 |
| 数据绑定 | `DeepProbeText` 绑定在真实窗口里构建，构建 0 error |
| Command 本体 | `--deep-ui` 开关构造 `HealthViewModel`，执行**按钮绑定的同一个 `AsyncRelayCommand` 实例**，输出正确 |
| 探测逻辑 | `--deep` CLI 实跑：网关 20/20 收到 0% 丢包，头显 0/20 |

`--deep-ui` 这个开关是为此专门加的，保留在源码里作为回归入口。

## 顺带踩出来的真 bug：先写管道再 AttachConsole 会死锁

`--deep-ui` 第一版把 `AttachConsole(-1)` 放在 `await` **之后**，而 `Failed` 事件处理器里有一句
`Console.Error.WriteLine`。在控制台还没附着、stdout/stderr 又被重定向到管道时，
这次写入**阻塞**，整个进程挂死（连 `timeout` 都杀不掉，跑了 75 秒以上）。

规则：**这个 CLI 里任何输出都必须排在 `AttachConsole` 之后。**

## 另一处设计改进

`AsyncRelayCommand.Execute` 原来是 `async void`——一旦 `execute()` 抛异常，
整个应用会在用户正看证据的时候崩掉，连同报告一起丢。改成：

- 暴露 `ExecutionTask`，调用方可以 await 而不是猜
- 增加 `Failed` 事件，异常被上报而不是撕进程