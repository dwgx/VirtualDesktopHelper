# CI 挂住 = 它后面所有闸门都没在跑

2026-10-06。差点一直以为 CI 是绿的。

## 怎么发现的

顺手查了一下 `gh run list --workflow=build.yml`。**最近八个 run 全部 `in_progress`，没有一个有结论。**

```
Build            ✅ success
Self test        ✅ success
Symptom map gate ✅ success
Exit code gate   （无结论）
Citation gate    （无结论）
Issue form gate  （无结论）
Publish          （无结论）
```

最早那个 run 是**一个多小时前**创建的。就这么挂着。

## 原因

卡住的是**当天新加的退出码闸门**。它跑九条命令，**每条都完整跑一遍体检引擎**，而体检里的每个可达性探测都会
等满超时。GitHub 托管的 `windows-latest` **上没有 `192.168.11.14` 这台头显**，于是每个探测都走满超时，
而 `Start-Process -Wait` **没有上限**。

**一个会永久挂住的闸门，等于把同一个 job 里它之后的所有闸门一起废掉。** 引证、issue 模板、发布三步
都被吃掉了——而它们看起来只是"还没轮到"。

## 修法

- 每条命令一个截止时间（默认 90 秒，`-TimeoutSeconds`）。超时即 kill，**计为失败**，并打印 `TIMEOUT` 和命令名。
- CI 那一步再加 `timeout-minutes: 15` 作为外层兜底——**防的是整个步骤卡住，而不是单条命令卡住**。

## 一个坑

去掉 `-Wait` 之后**每一条 rc 都印成空的**。

`Start-Process -PassThru` 返回的 Process，**进程消失之后 `ExitCode` 是 null**，除非句柄在它还活着的时候
被缓存过。必须在 `WaitForExit` 之前 `$null = $proc.Handle`。

这也是原来那版要用 `-Wait` 的原因——**而它恰好就是挂住的原因**。两个问题挤在同一个参数上。

## 结论

**闸门必须能在有限时间内失败。** 一个永不返回的检查不只是自己没用——它会让后面所有检查**看起来在排队**，
于是"CI 是绿的"这个判断本身失效。

**判断 CI 状态要看结论，不要看"最近有没有 run"。** 八个 `in_progress` 摆在那里，不检查就会读成"在跑"。

## 同类

和 `tools/check-emphasis.py` 那次是同一种形状：**我造了一个自己信不过的检查，然后差点当成保护。**
区别是那次我自己发现并删掉了；这次它已经进了 CI 并且真的挂住了——**发出去的东西必须先验证它能失败。**