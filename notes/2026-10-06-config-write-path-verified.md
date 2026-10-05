# 配置写入路径：对真实配置的端到端验证（2026-10-06）

## 为什么做这件事

`--set-param` 与参数页的「切换」是本工具**唯一会改用户真实配置**的路径，风险最高。
但它从来没被真正执行过——本机的 Virtual Desktop Streamer 一直在跑（PID 会变），所以每次调用都停在
`exit 8`（"Streamer 正在运行"）。**一个只验证过拒绝路径的写入路径，等于没验证过写入路径。**

## 怎么绕开 Streamer 又不碰真实配置

`StreamerConfigWriter.Write(path, key, value)` 接受显式路径，所以直接把
`C:\ProgramData\Virtual Desktop\StreamerSettings.json` **复制一份**到临时目录，所有验证都在副本上做。
真实配置自始至终没有被写入。

## 验证结果（在真实配置的副本上执行）

| 检查 | 结果 |
|---|---|
| 写入成功并返回备份路径 | ok |
| 目标键真的变了 | ok |
| 备份里是旧值 | ok |
| 14 个原有键一个没丢 | ok |
| 其余 13 个键逐键未变 | ok |
| 4 个 DPAPI 密文 blob（OculusQuest / Oculus ×3）**逐字节相同** | ok |
| 坏 JSON 被拒绝，且文件一个字节没动 | ok |

## 过程中查出一个真问题

第一轮跑下来，`ProtectedComputerID` 和 `Accounts` 报"被改了"。追下去是：

```
BEFORE: "ProtectedComputerID": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAsj+LhLx…"
AFTER : "ProtectedComputerID": "AQAAANCMnd8BFdERjHoAwE/Cl\u002BsBAAAAsj\u002BLhLx…"
```

`System.Text.Json` 的默认 encoder 会把 `+` 转义成 `\u002B`。

**这不是损坏**：JSON 里 `\u002B` 就是 `+`，任何合规解析器解出来完全一样。逐条比对确认了
4 个 DPAPI blob 的**解码后字符串逐字节相同**（308/328/328/328 字符）。

但它仍然是问题：**写完之后那个文件不再和 Streamer 写出来的样子一样**，备份对比、diff、
任何按原始文本判断"配置有没有被动过"的工具都会误报。

修法是序列化时用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`。名字里的 Unsafe 只针对
**HTML/JS 注入语境**，这里写的是本地配置文件、读回它的是 JSON 解析器，与该风险无关。

修完复验：副本里再无 `\u002B`，且仍然只有目标键变化。

## 结论

配置写入路径**不会损坏配对数据**——这一条现在有执行证据，不再是推断。
它此前一直只有拒绝路径的证据。

要真正跑通 `--set-param` 的成功路径，仍然需要**退出 Streamer**。这一条等 Owner。