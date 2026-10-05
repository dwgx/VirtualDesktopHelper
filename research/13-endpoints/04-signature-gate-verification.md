# 复核：签名门「阻断发现」这一结论不成立

## 结论先说

`research/13-endpoints/03-patched-baseline-deps.md` §3.3 断言：
「补丁后重签名 APK 过不了身份门 ⇒ `FindComputersAsync` 永不被调用 ⇒ 头显永不发 UDP 38850 ⇒
`No computer found`」。**这条结论是错的**，它建立在一个不存在的调用点��。

写这份复核是因为这条结论如果传下去，会让整个 VDHelper 把方向定错：
「头显找不到 PC 是因为 APK 签名」，而不是网络或防火墙。

## 三条反证，每条都可复核

### 1. `GetHasValidIdentityAsync` 在整棵反编译树里**没有任何调用方**

```
$ cd F:/Project/VirtualDesktop/analysis
$ grep -rn "GetHasValidIdentityAsync" --include=*.cs .
.\apk_patch\decompiled\xenko\VirtualDesktop.Mobile\UserSettings.cs:1738:  internal Task<bool> GetHasValidIdentityAsync()
```

只有定义那一行。没有调用方。一个没人调用的方法不可能是「局域网发现的总闸」。

### 2. 被引用的 `NetworkManager.cs:2395` 不存在——那个文件只有 453 行

```
$ wc -l analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/NetworkManager.cs
453 analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/NetworkManager.cs
```

报告里引用的 `NetworkManager.cs:2395 / :2467 / :2471 / :1885 / :1910` 全部超出文件长度。
`glob` 全树也只有这一个 `NetworkManager.cs`。那段「`discoveryTask = GetHasValidIdentityAsync()
.ContinueWith(...)`」的代码块在磁盘上找不到。

**教训**：worker 自述用 ilspycmd 重新反编译了一份，但那份产物没有落盘到本仓库，
所以它的行号无法被第三方复核。凡是行号对不上的结论，一律按未验证处理。

### 3. 真正的签名门在 `InputSystem.cs:21`，而它的行为是**杀掉进程**，不是跳过发现

```csharp
// analysis/apk_patch/decompiled/xenko/VirtualDesktop.Mobile/InputSystem.cs:21
if (PlatformAndroid.Context.PackageManager.GetPackageInfo(((Activity)PlatformAndroid.Context).PackageName, 64).Signatures[0].GetHashCode() != 1778352252)
{
    Task.Delay(10).ContinueWith(delegate(Task t) { CurrentProcess.Kill(); });
    Application.SynchronizationContext.Post(delegate(object s) { ((Activity)PlatformAndroid.Context).Finish(); }, null);
```

签名不符的后果是**应用当场退出**，不是「照常打开但发现不到电脑」。
用户看到的症状会是闪退 / 秒退，不是 `No computer found`。

同一份 Owner 自己的分析早就写过这件事，见
`analysis/report_sections/05_input_aot_native.md:30-32`：

> #### 身份门（identity gate）—— 不可绕过
> `InputSystem` 构造函数（`InputSystem.cs:21-33`）做签名校验……若 `!= 1778352252` 则延迟
> `Task.Delay(10)` 后 `CurrentProcess.Kill()`……这三处在退出面扫描中分类为 identity-gate，
> **仅诊断、不得补成功**。

### 4. 而且补丁**本来就处理了这个门**

`analysis/apk_patch/binary_patch.py:271-277`：

```python
# InputSystem/<>c.<.ctor>b__1_1 (Activity.Finish)
# NOP'd — gets triggered during init after signature check failure
total_patches += nop(raw52, il + 0x00, 5, "ldsfld Context")
total_patches += nop(raw52, il + 0x05, 5, "castclass Activity")
total_patches += nop(raw52, il + 0x0A, 5, "callvirt Finish")
```

`Keyboard.LoadContent` 里两个反篡改陷阱同样是为了重签名场景被 NOP 掉的，
脚本注释自己写得很清楚：`bool=false(重签名)→ 落入 13 个 NOP → fall-through`。

也就是说，**重签名 APK 走不通的不是「发现」，而是「启动时自杀」这条路径，而这条路径已被补丁覆盖**。

## 仍然没有定论的那一环

`CurrentProcess.Kill()` 那个 `Task.Delay(10)` 延续体**没有被 NOP**
（上面三处 NOP 只覆盖 `Finish`）。按 `InputSystem.cs:21` 的字面代码，重签名后它应当仍然触发。

但两件事与它矛盾：

1. Owner 自己的 `analysis/apk_patch/HANDOFF.md` 记录该补丁基线
   「自动发现 PC Streamer ✅ 自动连接 / 桌面 LAN 串流 ✅ 流畅」。应用能跑起来并自动连上，
   说明这条 kill 在实际成品上并没有生效。
2. 落盘的 `analysis/apk_patch/decompiled/xenko/` 与 ilspycmd 新反编译产物可能不是同一份。

**所以这一环标 `[未验证]`**，需要一次实证：头显在线时抓一次包，
看有没有发往 `255.255.255.255:38850` 的 UDP。
- 有包 ⇒ §3.3 整节作废（本文的结论成立）。
- 没包 ⇒ 真正原因是 APK 重签名而不是网络，**VDHelper 该提示的是「先确认补丁基线能启动」，而不是去查防火墙**。

## 这对 VDHelper 意味着什么

| 原结论（错的） | 修正后 |
| --- | --- |
| 「发现失败可能是签名门，与网络无关」 | 签名门若生效，表现是**启动即退**，不是发现失败。用户报「找不到电脑」时，应用是能打开的 |
| 「要测 `*.vrdesktop.net:443` 可达性」 | **不要测**。客户端的 registry 调用被 `if (americaProofValid)` 一类判断短路，无有效 UserProof 时一个包都不发，与「连不上」没有因果通路 |
| 「UDP 38850 上有广播包」当就绪判据 | **PC 从不广播 38850**，只监听并单播回包。用这个判据会永远失败 |

真正值得做的还是那句：**PC 侧监听 `0.0.0.0:38850`，判定「头显到底有没有发包过来」**。
这是唯一能把「网络/防火墙挡了」和「客户端根本没发」分开的观测点，而且它纯被动、不需要凭据。

## 报告本身哪些部分仍然可用

§3.3 之前的内容（registry 调用被 proof 守卫、发现包只含 RSA 公钥+AccountID、
补丁没改端口/协议/TLS/更新检查）行号同样大多对不上 453 行的文件，
**建议按未验证对待，直到有人把 ilspycmd 的产物落盘并重跑核对**。

唯一可以当硬事实用的是两条，来自 Owner 自己的既有文档与实测：

- `analysis/apk_patch/SIGNING.md`：成品统一用固定自签名密钥 `vdpatch`，
  证书 SHA-256 = `aad0b756d3ff2c9da73f0e3f681b29bc069b394e667d90f6cf03b62d83cc9c3f`。
- Owner 的 `HANDOFF.md`：该基线在同网段下发现与串流均正常。

**推论（重要）**：既然补丁基线在同网段实测能自动发现并连上，那么
「同网段连不上」就**不是**签名问题，而应当回到网络层排查——这正是 VDHelper 的主场。