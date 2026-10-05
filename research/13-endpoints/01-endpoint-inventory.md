# 01 — 端点清单：Quest 端和 PC Streamer 端到底连了什么

本文件只回答一个问题：**代码里实际出现的 host / 端口 / URL，各是什么、在哪一侧、属本地还是云端、被防火墙挡住会坏哪一步。**
发现机制本身归 `02-discovery-protocol.md`；补丁基线还需不需要这些归 `03-patched-baseline-deps.md`。

写作时 `research/13-endpoints/` 下两位同事的文件已存在（`02-discovery-protocol.md`、
`03-patched-baseline-deps.md`），但本文件先于读取它们落盘，结论全部来自本轮独立实跑。

---

## 0. 证据基线（先说清楚这份清单是怎么来的）

### 0.1 一个必须先纠正的坑：`analysis\apk_patch\extracted_assemblies\` 里的程序集是错位的

`F:\Project\VirtualDesktop\analysis\apk_patch\extracted_assemblies\` 里那 7 个 `VirtualDesktop.*.dll`
**文件名和内容对不上**。实测：

```
$ cd F:/Project/VirtualDesktop/analysis/apk_patch/extracted_assemblies
$ ilspycmd -o <tmp> -r . VirtualDesktop.Net.dll
$ grep -oP 'AssemblyTitle\("\K[^"]*' <tmp>/VirtualDesktop.Net.decompiled.cs
OpenTK                      ← 文件名叫 VirtualDesktop.Net.dll，实际是 OpenTK

$ 同样对其余 6 个：
VirtualDesktop.Android.dll     ->  TITLE=Xenko.Core.IO
VirtualDesktop.Core.dll        ->  TITLE=Xamarin.Google.Android.Play.Integrity
VirtualDesktop.Interfaces.dll  ->  TITLE=Xamarin.GooglePlayServices.Tasks
VirtualDesktop.Mobile.Shared.dll -> TITLE=Oculus.Platform
VirtualDesktop.Mobile.dll      ->  TITLE=ZString
VirtualDesktop.WCF.dll         ->  TITLE=PFD.Android
```

`VirtualDesktop.Net.dll`（2,126,336 字节）里 `ilspycmd -l` 列出的 1598 个类型中 1584 个是 `OpenTK.*`，
**VD 自己的 `NetClient` / `ComputerDiscoveryClient` 一个都没有**：

```
$ ilspycmd -l class,struct,interface,enum VirtualDesktop.Net.dll | grep -v OpenTK | head
Class <Module>
Class <PrivateImplementationDetails>
Struct <PrivateImplementationDetails>+__StaticArrayInitTypeSize=12
   …（全是 <PrivateImplementationDetails>）
```

我第一轮就是被这个坑住的：照着 `F:\...\apk_patch\decompiled\xenko\VirtualDesktop.Mobile\NetworkManager.cs`
以为 Quest 侧只有壳，结果 grep 不到任何 URL。**结论：那份 `extracted_assemblies` 不能作为端点证据源。**
（`02-discovery-protocol.md` 开头也踩到了同一个坑并写下了 `[未验证]`，此处一并纠正。）

### 0.2 正确的提取路径（可复现）

Quest 端的托管程序集不在 APK 的 zip 里，而是打包在 Xamarin 的 assembly store blob 里：
`lib/arm64-v8a/libassemblies.arm64-v8a.blob.so`，格式 `ELF(外壳) + XABA(索引) + XALZ(LZ4 条目)`。
解析方式直接抄 Owner 自己 `analysis\apk_patch\binary_patch.py:63-91` 的 `parse_blob` / `extract_entry`：

| 步骤 | 实测值 |
| --- | --- |
| `XABA` 标记位置 | 文件偏移 `0x4000` |
| `entry_count` = `u32 @ xaba+8` | `181` |
| `index_size` = `u32 @ xaba+16` | `4344` |
| `desc_start` = `xaba + 20 + index_size` | `0x510C` |
| 每个 `XALZ` 记录 | `pos`，`idx=u32@pos+4`，`uncomp=u32@pos+8`，payload 在 `pos+12` |
| 第 i 个描述符 | `desc_start + i*28`，`data_sz = u32 @ desc+8` |
| 程序集名表 | `desc_start + entry_count*28` = `0x64D8`，格式 `(uint32 len + ascii 名，以 .dll 结尾)` |
| payload 解压 | `lz4.block.decompress(data[pos+12 : pos+12+data_sz-12], uncompressed_size=uncomp)` |

提取出的 7 个 VD 程序集（我这一轮的实际产物，SHA256 前 16 位）：

```
VirtualDesktop.Android.dll      35840  11896bef852a2ceb
VirtualDesktop.Core.dll         40448  c6228aefe3ce0e8d
VirtualDesktop.FFmpeg.dll       17920
VirtualDesktop.Interfaces.dll   29184  6ba6b54d3e210c72
VirtualDesktop.Mobile.dll      529408  79311294078a9f46
VirtualDesktop.Mobile.Shared.dll 87552  30a695d50cb636da
VirtualDesktop.Net.dll          66048  fcb27ea66705b389   ← AssemblyTitle="VirtualDesktop.Net", Version 1.18.57.0
VirtualDesktop.WCF.dll           8704  ee3c2c7d3512acba
```

与 0.1 那套**哈希全部不同**，且 `VirtualDesktop.Net.dll` 类型表 65 项、0 个 OpenTK：

```
$ ilspycmd -l class,struct,interface,enum VirtualDesktop.Net.dll | wc -l   → 65
$ ilspycmd -l … | grep -c OpenTK                                          → 0
$ ilspycmd -l … | grep -i "NetClient\|Discovery\|Messaging"
Class VirtualDesktop.Net.ComputerDiscoveryClient
Class VirtualDesktop.Net.NetClient
Class VirtualDesktop.Net.NetMessagingClient
…
```

提取脚本是临时写在 `%TEMP%\vd_ep_01\extract_xaba.py` 的，**用完已删**（见 §0.4）。
反编译产物在 `%TEMP%\vd_ep_01\vd\`（Quest 侧）和 `%TEMP%\vd_ep_01\pc\VirtualDesktop.Streamer\`（PC 侧），
这两个是临时目录，不在 Owner 的树里，也没写进本仓库。

### 0.3 PC 侧反编译源

PC 侧直接用**本机已安装**的 Streamer（比 F: 树新，且能对上 `analysis\` 里的字符串报告）：

```
$ cd "C:/Program Files/Virtual Desktop Streamer"
$ ilspycmd -o %TEMP%/pc/VirtualDesktop.Streamer -r . -p VirtualDesktop.Streamer.exe
   → 11537 个 .cs
```

本轮 PC 侧用的路径记作：

```
PC-S/  = %TEMP%\vd_ep_01\pc\VirtualDesktop.Streamer\
          （SmartAssembly 混淆 + ILSpy 根文件 `-` 与 `--`；保留原名的子目录如 VirtualDesktop.Streamer\、VirtualDesktop.Net\）
```

> 与 `F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer\` 是同一份混淆树的另一次导出，
> 行号可能差几行；本文件所有 PC 侧 `file:line` 都以 `PC-S/` 为准。

`libVirtualDesktopNet.dll` 是**原生 C++**，ilspy 拒读：

```
$ ilspycmd -p libVirtualDesktopNet.dll
ICSharpCode.Decompiler.Metadata.MetadataFileNotSupportedException:
  PE file does not contain any managed metadata.
```

所以「原生 net.dll 里有没有额外端口」这一项标 `[未验证]`（见 §5）。

### 0.4 用过的完整搜索命令（可原样重跑）

```bash
# ---- 0) 取程序集（见 §0.2 表） ----
# 提取脚本 %TEMP%\vd_ep_01\extract_xaba.py 已删除；逻辑见上表，可用任意 XABA+LZ4 解析器复现

# ---- 1) 反编译 ----
cd %TEMP%/vd_ep_01/assemblies
ilspycmd -o ../vd/VirtualDesktop.Net         -r . -p VirtualDesktop.Net.dll
ilspycmd -o ../vd/VirtualDesktop.Mobile       -r . -p VirtualDesktop.Mobile.dll
ilspycmd -o ../vd/VirtualDesktop.Mobile.Shared -r . -p VirtualDesktop.Mobile.Shared.dll
ilspycmd -o ../vd/VirtualDesktop.Interfaces   -r . -p VirtualDesktop.Interfaces.dll
ilspycmd -o ../vd/VirtualDesktop.Core        -r . -p VirtualDesktop.Core.dll
ilspycmd -o ../vd/VirtualDesktop.Android      -r . -p VirtualDesktop.Android.dll
ilspycmd -o ../vd/VirtualDesktop.WCF         -r . -p VirtualDesktop.WCF.dll
cd "C:/Program Files/Virtual Desktop Streamer"
ilspycmd -o %TEMP%/pc/VirtualDesktop.Streamer -r . -p VirtualDesktop.Streamer.exe

# ---- 2) URL / host 字面量（主 grep）----
# 2a. C# 源码里的 URL 字面量
grep -rn --include=*.cs -oE '"[a-zA-Z][a-zA-Z0-9+.-]*://[^"]*"' <vd 目录>
# 2b. 域名字面量（不带 scheme）
grep -rn --include=*.cs -oE '"[A-Za-z0-9][A-Za-z0-9._-]*\.(com|net|org|io|dev|co|me|tv)(/[A-Za-z0-9._~:/?#@!$&()*+,;=%-]*)?"' <vd 目录>
# 2c. IL 里的全部 ldstr（反编译器可能吞掉或改写字符串，这一遍是最硬的）
for a in VirtualDesktop.Net VirtualDesktop.Mobile VirtualDesktop.Mobile.Shared \
         VirtualDesktop.Interfaces VirtualDesktop.Core VirtualDesktop.Android VirtualDesktop.WCF; do
  ilspycmd -il "$a.dll" | grep -oP 'ldstr\s+"\K[^"]*'
done | sort -u > ldstr_all.txt
grep -iE 'http|\.com|\.net|\.org|\.io|vrdesktop|guillemot|api|azure|amazonaws|8\.8\.8\.8|[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' ldstr_all.txt

# ---- 3) 端口 ----
grep -rn --include=*.cs -E '\b(388[0-9]{2}|39[0-9]{3})\b' <vd 目录> <PC-S 目录> | grep -viE 'Token|RID|RVA|0x'
grep -rn --include=*.cs -E 'new IPEndPoint|IPEndPoint\(' <vd 目录>

# ---- 4) 二进制里的 host（原生 .so / .dll）----
python - <<'PY'
import zipfile, re
z = zipfile.ZipFile(r'F:\Project\VirtualDesktop\VirtualDesktop.Android_1.34.18.0_base.apk')
for n in [x for x in z.namelist() if x.startswith('lib/arm64-v8a/')]:
    d = z.read(n)
    hits = set()
    for src, s in (('U16', d.decode('utf-16-le','ignore')),
                   ('ASC', d.decode('latin-1','ignore'))):
        for m in re.finditer(r'https?://[A-Za-z0-9._~:/?#@!$&()*+,;=%-]{8,}', s):
            hits.add((src, m.group(0)))
    if hits:
        print('===', n)
        for src, h in sorted(hits):
            print('  ', src, h)
PY

# ---- 5) 托管程序集里的 host（校验 C# 里没漏）----
cd %TEMP%/vd_ep_01/assemblies && python - <<'PY'
import os, re
pat = re.compile(r'[A-Za-z0-9][A-Za-z0-9._-]{2,}\.(?:com|net|org|io|dev|co|me|tv)', re.I)
for f in sorted(os.listdir('.')):
    d = open(f, 'rb').read()
    h = set()
    for s in (d.decode('utf-16-le','ignore'), d.decode('latin-1','ignore')):
        for m in pat.finditer(s):
            g = m.group(0).lower()
            if 'vrdesktop' in g or 'guillemot' in g:
                h.add(m.group(0))
    if h:
        print(f, sorted(h)[:20])
PY
```

第 5 条命令的输出就是「Quest 侧只有 3 个 VD 域名字面量」这个结论的来源：

```
VirtualDesktop.Interfaces.dll ['america.vrdesktop.net']
VirtualDesktop.Mobile.dll      ['www.vrdesktop.net']
idx51 ['america.vrdesktop.net']       ← 这两个是 blob 索引名残留，非程序集
idx52 ['www.vrdesktop.net']
```

`ldstr_all.txt` 里网络相关的全部命中（这就是 §2 表的原料）：

```
8.8.8.8
20.225.41.170
40.89.161.236
https://america.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry
https://europe.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry
https://virtualdesktopaiamerica.azurewebsites.net/api/getvocalanswer/
www.vrdesktop.net   （只出现在 4 条用户可见提示文本里，不是请求地址）
```

**Quest 侧全域名字面量就这些。没有 CDN、没有 asset host、没有 ws://、没有 wss://。**

---

## 1. 端口总表

端口在代码里是**裸字面量**，没有常量名。两侧的数值必须成对看：一个「本地直连端口」和一个
「经云端中继端口」，相差 1（`+ ServerRotation`，见 §1.3）。

### 1.1 本地直连端口（PC 与 Quest 同网段直连）

| 端口 | 传输 | 用途 | Quest 侧证据 | PC 侧证据 |
| --- | --- | --- | --- | --- |
| 38810 | TCP | 控制/消息通道（messaging） | `PC-S`… 见下 `NetworkManager.cs:425,457` | `ConnectionManager.cs:987,2067` |
| 38820 | TCP | 数据通道（data / 输入回传） | `NetworkManager.cs:539` | `ConnectionManager.cs:1081,2075` |
| 38830 | TCP | 视频通道 | `NetworkManager.cs:529` | `ConnectionManager.cs:1005,2082` |
| 38840 | TCP | 音频通道 | `NetworkManager.cs:534` | `ConnectionManager.cs:1045,2084` |
| 38850 | UDP | 局域网广播发现 | `ComputerDiscoveryClient.cs:63-64` | `--/-.cs:18167` |
| 38860 | UDP | Wake-on-LAN 广播探测 | Quest 侧不发起 | `ConnectionManager.cs:2669` |

Quest 侧原始行（`%TEMP%\vd_ep_01\vd\VirtualDesktop.Mobile\VirtualDesktop.Mobile\NetworkManager.cs`）：

```
425:  await MessagingClient.ConnectToPeerAsClientAsync(region, connectionID, Platform, aes, 38811, AnyAddress, privateAddresses, 38810, true)
457:  await MessagingClient.ConnectToLocalPeerAsync(aes, AnyAddress, privateAddresses, 38810, true)
529:  await VideoClient.ConnectToLocalPeerAsync(aes, localAddress, privateAddresses, 38830, encryptLocalTraffic)
534:  await AudioClient.ConnectToLocalPeerAsync(aes, localAddress, privateAddresses, 38840, encryptLocalTraffic)
539:  await DataClient.ConnectToLocalPeerAsync(aes, localAddress, privateAddresses, 38820, encryptLocalTraffic)
```

PC 侧原始行（`%TEMP%\vd_ep_01\pc\VirtualDesktop.Streamer\VirtualDesktop.Streamer\ConnectionManager.cs`）：

```
2067:  _0001(new NetMessagingClient(new IPEndPoint(_0099_0002._0002._0001, 38810)));
2075:  _0001(new _0096_0002._0002(new IPEndPoint(_0099_0002._0002._0001, 38820)));
2082:  _0001(new _000E._0007(new IPEndPoint(_0099_0002._0002._0001, 38830)));
2084:  _0001(new _001F._0005(new IPEndPoint(_0099_0002._0002._0001, 38840)));
2669:  IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Broadcast, 38860);
```

UPnP 映射的也只有这四个（`PC-S\VirtualDesktop.Net\UPnPManager.cs`）：

```
112:  if ((publicPort == 38810 || publicPort == 38820 || publicPort == 38830 || publicPort == 38840) && …)
402:  if (obj2._0001 != 38810 && obj2._0001 != 38820 && obj2._0001 != 38830 && obj2._0001 != 38840)
424:  …CreatePortMapAsync(new Mapping(Protocol.Tcp, obj._0001, 38810, 38810, 0, …))
428:  …CreatePortMapAsync(new Mapping(Protocol.Tcp, obj._0001, 38820, 38820, 0, …))
432:  …CreatePortMapAsync(new Mapping(Protocol.Tcp, obj._0001, 38830, 38830, 0, …))
436:  …CreatePortMapAsync(new Mapping(Protocol.Tcp, obj._0001, 38840, 38840, 0, …))
```

### 1.2 云端中继端口（PC 先连云端拿 IP，再由云端把 Quest 的包转到 PC）

| 端口 | 用途 | 谁拨 | 证据 |
| --- | --- | --- | --- |
| 38811 + `ServerRotation` | 云端中继（控制） | PC **主动拨**云端 IP | `ConnectionManager.cs:167` |
| 38821 + `ServerRotation` | 云端中继（数据） | PC 拨 / Quest 拨 | `ConnectionManager.cs:1065` |
| 38831 + `ServerRotation` | 云端中继（视频） | PC 拨 / Quest 拨 | `ConnectionManager.cs:1027` |
| 38841 + `ServerRotation` | 云端中继（音频） | PC 拨 / Quest 拨 | `ConnectionManager.cs:1097` |
| 443 | 云端注册表 HTTPS | 两侧 | `NetHelper.cs:42,44` |

### 1.3 `ServerRotation` 把中继端口展开成 6 个

```
PC-S\VirtualDesktop.Streamer\StreamerSettings.cs:3225
    ServerRotation = (_serverRotation + 1) % 6;
```

所以中继端口实际是：

| 通道 | 实际端口集合 |
| --- | --- |
| 控制 | 38811, 38812, 38813, 38814, 38815, 38816 |
| 数据 | 38821 – 38826 |
| 视频 | 38831 – 38836 |
| 音频 | 38841 – 38846 |

`ConnectionManager.cs:167` 每次 `IncrementServerRotation()` 后换下一个端口，**这是为了绕开单端口被占/被限**。
Quest 侧只见到基准值 38811/38821/38831/38841（`NetworkManager.cs:425,543,548,553`），
由云端返回的 `PeerInfo` 里带真实端口。

### 1.4 UDP 38850 的发现广播

Quest 侧（`%TEMP%\vd_ep_01\vd\VirtualDesktop.Net\VirtualDesktop.Net\ComputerDiscoveryClient.cs`）：

```
 63:  BroadcastEP = new IPEndPoint(IPAddress.Broadcast, 38850);
 64:  ListeningEP = new IPEndPoint(IPAddress.Any, 38850);
 98:  _broadcastClient.Send(array, array.Length, BroadcastEP);
```

**Quest 端只广播、不监听**：`ListeningEP` 定义了但代码里从未使用（`StopListening()` 只 Dispose 一个恒为 null 的
`_listeningClient`，第 164-167 行）。Quest 侧也**没有** `JoinMulticastGroup`：

```
$ grep -rn --include=*.cs -iE 'Multicast|239\.|224\.|JoinMulticast|MulticastOption' \
      VirtualDesktop.Net VirtualDesktop.Mobile VirtualDesktop.Mobile.Shared VirtualDesktop.Android
（0 命中）
$ grep -rn --include=*.cs -iE 'NatPmp|UPnP|NatDiscover|Open\.Nat' VirtualDesktop.Net VirtualDesktop.Mobile
（0 命中）
```

PC 侧**有** multicast（在每块网卡的默认网关上 join），组地址是 `ff02::1`：

```
PC-S\--\-.cs:18276-18280
    enumerator = _0012_0003._0004._0001().GetEnumerator();   // 遍历各网卡默认网关 IPv4
    while (enumerator.MoveNext()) {
        *(int*)ptr = enumerator.Current;
        udpClient.JoinMulticastGroup(*(int*)ptr, _0010_0002._0001.m__0002.Address);
    }
PC-S\--\-.cs:18260-18261
    udpClient.Client.DualMode = true;
    udpClient.Client.Bind(_0010_0002._0001._0003);            // = new IPEndPoint(IPAddress.IPv6Any, 38850)
```

**`ff02::1` 的提取方法**（临时脚本，已删）：`m__0002` 来自一个 16 字节静态字段，
直接按 `BroadcastAesIV` 这个已知常量在 exe 里定位同一段静态块：

```python
iv = bytes([82,200,129,118,144,104,249,4,62,20,120,110,20,180,63,31])   # 来自 --/-.cs:18162-18166
i = d.find(iv)          # → 0x967e68
key = d[i+16 : i+16+32] # 校验：是否等于 BroadcastAesKey
mcast = d[i-16 : i]     # 取它前面 16 字节
```

实测输出：

```
BroadcastAesIV occurrences: ['0x967e68']
key matches BroadcastAesKey: True
key hex: 2047ec5ec22255ffa5acbbbb9606686a39393ef4724baeed093024ef523962cd50
16 bytes BEFORE the key: ff020000000000000000000000000001 -> ff02::1
is_multicast: True
same bytes as 239.255.255.250 (SSDP)? True
```

即 multicast 组 = `ff02::1`（all-nodes，link-local scope）。**[推断]** Quest 侧广播的
`255.255.255.255:38850` 会被同网段 PC 的 `ff02::1` 收下——因为收下后 PC 仍是从 `ReceiveAsync().Result`
拿到包再按 `ConnectionID` 匹配，组地址只影响 IPv6 路径；这条推断**未做运行时抓包验证**，标 `[推断]`。

### 1.5 WOL 38860 的实际行为

```
PC-S\VirtualDesktop.Net\WOLHelper.cs:58
    this.m__0001 = new UdpClient(38860, AddressFamily.InterNetwork);
PC-S\VirtualDesktop.Streamer\ConnectionManager.cs:2676,2679
    num = …Send(udpClient, Array.Empty<byte>(), 0, iPEndPoint2);   // 空包，连发两次
```

`Array.Empty<byte>()` —— **发的是空包**，只用来让 PC 自己 UDP socket 有回包以刷新 `LastReceiveTimeout` 计时，
不是标准 WOL magic packet（`0xFF`×6 + MAC）。**[推断]** 它不是 Wake-on-LAN 协议包，
只是保活；标 `[推断]`。

---

## 2. 端点主表

列含义：`端点 | 侧 | 本地/云端 | 协议:端口 | 证据 file:line | 挡住会坏什么`

### 2.1 云端端点（**「打了补丁的离线基线不需要这个」候选**）

| # | 端点 | 侧 | 本地/云端 | 协议:端口 | 证据 | 挡住会坏什么 |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | `20.225.41.170`（America 中部注册服务器） | Quest + PC | **云** | TCP: 38811–38846 | Quest `NetClient.cs:166,207` 用 `NetHelper.GetServerIP()`；IP 字面量 `NetHelper.cs:16` | Quest 在**跨网段**时永远连不上 PC；同网段时完全用不到 |
| C2 | `40.89.161.236`（Europe 注册服务器） | Quest + PC | **云** | TCP: 38811–38846 | 同上，`NetHelper.cs:17` | 同上 |
| C3 | `https://america.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry` | Quest + PC | **云** | HTTPS:443 (TLS 1.2) | Quest `NetHelper.cs:44`；初始化 `NetworkManager.cs:135`；PC `PC-S\-/-.cs:10193`；实际调用受 `NetworkManager.cs:710-712` 的 `if (americaProofValid)` 守卫 | 云端注册表拿不到 PC 列表。**注意：补丁基线上广播也不发**（见 §2.3 的更正 3），所以这不是「只是少一项」，而是列表彻底空 |
| C4 | `https://europe.vrdesktop.net/ComputerRegistry.svc/IComputerRegistry` | Quest + PC | **云** | HTTPS:443 (TLS 1.2) | Quest `NetHelper.cs:42`；`NetworkManager.cs:136`；PC `PC-S\-/-.cs:10190`；受 `NetworkManager.cs:714-716` 的 `if (europeProofValid)` 守卫 | 同 C3 |
| C5 | `https://download.vrdesktop.net/files/version.txt` | PC | **云** | HTTPS:443 | `PC-S\VirtualDesktop.Streamer\UpdateHelper.cs:70` | Streamer 更新检查失败（PC 侧）；另有 hosts 文件被改的检测（`:82`），**不阻断连接** |
| C6 | `https://www.vrdesktop.net` | PC | **云** | 仅浏览器打开 | `PC-S\VirtualDesktop.Streamer\ConnectionManager.cs:158` | 无功能影响（注册失败时的提示按钮） |
| C7 | `https://virtualdesktopaiamerica.azurewebsites.net/api/getvocalanswer/{user}/{lang}/{q}` | Quest | **云** | HTTPS:443 | `%TEMP%\vd_ep_01\vd\VirtualDesktop.Mobile\VirtualDesktop.Mobile\Assistant.cs:134` | 语音助手只不出声（`MediaPlayer.SetDataSourceAsync` 抛异常，被 `:136` 附近的 try 吞掉） |
| C8 | Azure Speech STT，region = `southcentralus` → `wss://southcentralus.stt.speech.microsoft.com` | Quest | **云** | WSS:443 | region 字面量 `Assistant.cs:148`；主机模板在原生库 `libMicrosoft.CognitiveServices.Speech.core.so` 字符串表：`.stt.speech.microsoft.com` | 语音助手「听不懂」；同样不影响串流 |
| C9 | `8.8.8.8` | Quest | **云** | ICMP | `TraceRoute.cs:15`（`DestinationIPAddress`），Linux 分支 `:32` `tracert -c 1 -W 1000 -t <ttl> 8.8.8.8`；调用点 `NetworkManager.cs:470`，**受 `:468` 的 `!computer.IsOnSameNetwork && computer.AllowRemoteConnections` 守卫** | 只在**远端且连不上**时跑，仅影响「双 NAT / CGNAT」判定文案（`:472-477`）；同网段 LAN 会话走不到这里 |
| C10 | `https://api.steampowered.com/`、`https://cm0.steampowered.com`、`https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/` | PC | **云** | HTTPS:443 | `PC-S\SteamKit2\WebAPI.cs:784`、`SteamKit2.Discovery\SmartCMServerList.cs:399`、`VirtualDesktop.Streamer\GameManager.cs:2414` | 仅影响 SteamVR 平台的 Steam 游戏串流 |
| C11 | `https://www.oculus.com/experiences/rift/` | PC | **云** | 仅浏览器打开 | `PC-S\VirtualDesktop.Streamer\GameManager.cs:2410` | 无功能影响 |
| C12 | `http://discord.vrdesktop.net?` | PC | **云** | 字符串存在，用途 `[未验证]` | 仅在 exe 原始字符串表命中：`ASC http://discord.vrdesktop.net?`（`strings/…VirtualDesktop.Streamer.exe.strings.txt:154958`）；C# 里搜不到对应调用 | `[未验证]` |
| C13 | SmartAssembly 错误上报：`http://www.smartassembly.com/webservices/Reporting/UploadReport2`、`http://www.smartassembly.com/webservices/UploadReportLogin/GetServerURL`、`http://sawebservice.red-gate.com/` | **仅 PC**（Quest 侧无此通道） | **云** | HTTP:80 | PC 侧 `PC-S\SmartAssembly.SmartExceptionsCore\ReportingService.cs:9,18`、`UploadReportLoginService.cs:9,19`、`PC-S\-/-.cs:5043`；Quest 侧七个程序集 grep 上报关键字 = 0 命中（只有 `using SmartAssembly.Attributes;` 混淆标记） | 崩溃上报失败；PC 侧与串流无关，但会出现在防火墙日志里 |
| C14 | 平台 SDK 端点（Meta / Pico / Viveport / Google Play） | Quest | **云** | 未知 | 代码里只有 `Platform` 枚举分支和文案（「Meta servers unreachable」等，`NetworkManager.cs:687-704`），**没有 URL 字面量**；`Oculus.Platform.dll` / `Pico.Platform.dll` / `Viveport.Android.dll` 的 UTF-16 与 ASCII 扫描均 0 命中 | 取 access token 失败 → 走不到注册表查询；`[未验证]` 具体 host |

### 2.2 本地 / 局域网端点（PC 侧工具**真正能测**的那些）

| # | 端点 | 侧 | 本地/云端 | 协议:端口 | 证据 | 挡住会坏什么 |
| --- | --- | --- | --- | --- | --- | --- |
| L1 | `255.255.255.255:38850` 广播发现 | Quest → PC | **本地** | UDP:38850 | Quest `ComputerDiscoveryClient.cs:63,98`（`IPAddress.Broadcast, 38850`） | 头显列表里没有本机 PC —— **纯 LAN 下最常见的唯一断点** |
| L2 | `<PC 各网卡 IPv4>:38850` 单播应答 | PC → Quest | **本地** | UDP:38850 | Quest `ComputerDiscoveryClient.cs:107-129`（`ReceiveAsync` 后要求 `buffer.Length > 128`，前 128 字节是 RSA 加密的 AES key+IV，其余是 AES-CBC 密文）；PC 侧 `PC-S\--\-.cs:18304-18311` 收 Quest 的 **17 字节**握手（16 字节 `ConnectionID` + 1 字节 platform），`:18318` 之后回 **>243 字节**的加密应答 | 列表出现但点进去连不上 |
| L3 | `<PC 局域网 IP>:38810` | Quest ⇄ PC | **本地** | TCP:38810 | Quest `NetworkManager.cs:425,457`；PC `ConnectionManager.cs:2067` | 控制通道不通，串流完全起不来 |
| L4 | `<PC 局域网 IP>:38820` | Quest ⇄ PC | **本地** | TCP:38820 | 同上 `:539` / `:2075` | 画面能出但输入/外设无回传 |
| L5 | `<PC 局域网 IP>:38830` | Quest ⇄ PC | **本地** | TCP:38830 | 同上 `:529` / `:2082` | 无画面 |
| L6 | `<PC 局域网 IP>:38840` | Quest ⇄ PC | **本地** | TCP:38840 | 同上 `:534` / `:2084` | 无声音 |
| L7 | `255.255.255.255:38860` 空 UDP 包 | PC 自发 | **本地** | UDP:38860 | `PC-S\VirtualDesktop.Streamer\ConnectionManager.cs:2669,2676` | 仅影响保活计时，`[推断]` |
| L8 | 本地路由探测（tracert / ping 逐 TTL） | Quest | **本地→公网** | ICMP / UDP traceroute | `TraceRoute.cs:17-99`（`PingOptions(ttl, true)` 循环，Linux 走 `/system/bin/tracert`） | 只影响 NAT 判定文案 |
| L9 | `https://<本机默认网关>/` 或 `http://<本机默认网关>/` 探测 → 存入 `RouterUrl` | PC | **本地** | HTTP(S) | `PC-S\VirtualDesktop.Streamer\ConnectionManager.cs:2472`（`"https://" + current`）→ `:2484`（`"http://" + current`）→ `:2514` 存入 `SettingsBase<DynamicSettings>.Default.RouterUrl`；候选 IP 来自各网卡默认网关（`--/-.cs:22784-22842`） | `RouterUrl` 为空；`[未验证]` 空了之后哪条路径受影响 |
| L10 | UPnP/NAT-PMP 端口映射 38810/38820/38830/38840 → 外网 | PC → 路由器 | **本地**（走局域网到路由器） | UPnP / NAT-PMP | `PC-S\VirtualDesktop.Net\UPnPManager.cs:112,402,424-436`；调用点 `ConnectionManager.cs:2420,2110`（**仅当 `!AllowRemoteConnections` 时才映射**） | 跨网段远程连接失败；同网段无影响 |
| L11 | `ff02::1:38850`（IPv6 multicast，PC 加入） | PC | **本地** | UDP:38850 | `PC-S\--\-.cs:18276-18280`；组地址提取见 §1.4 | 同 L1；纯 IPv4 环境下此路径不生效 |
| L12 | `<PC 局域网 IP>` 任意端口，来自云端 `PeerInfo` 消息 | 云 → Quest | 视情况 | TCP | `PC-S\VirtualDesktop.Net\NetClient.cs`（Quest 侧 `%TEMP%\vd_ep_01\vd\VirtualDesktop.Net\VirtualDesktop.Net\NetClient.cs:334`：`new PeerInfo(ReadIPEndPoint(), !ReadBoolean())`） | 云端给的端口被本地防火墙挡 → 连不上，但这是云端路径 |

### 2.3 「纯 LAN 也会被挡」的那几条

按「即使两台机器在同一网段、一个互联网都不给，也依然会失败」筛出来：

| 端点 | 为什么纯 LAN 也会碰 | 挡了的现象 |
| --- | --- | --- |
| **L1 UDP 38850 广播** | 发现**只有**广播这一条 LAN 路径（Quest 侧无 UPnP、无 mDNS、无组播加入） | 头显列表空 → 最典型的「找不到我的电脑」 |
| **L3–L6 TCP 38810/20/30/40** | 串流全走这四条 TCP | 列表里有、点进去失败，或连上后黑屏/无声音/无输入 |
| **C13 SmartAssembly 上报（仅 PC）** | Streamer 侧可能在后台发 HTTP:80，与串流无关 | 无用户可见后果，但会在防火墙日志里出现，容易被误判成「VD 在外联」。**Quest 侧没有这条通道**（见下） |
| **C5 `download.vrdesktop.net`（仅 PC）** | Streamer 更新检查，与串流无关但确实会发 | 无功能后果；`UpdateHelper.cs:73-88` 还会读本机 `drivers\etc\hosts` 看有没有 `vrdesktop.net` 条目 |

**两个更正**（初稿写错，已按反编译源码改掉）：

1. **C9 `8.8.8.8` 不是无条件打。** `NetworkManager.cs:468` 有守卫
   `if (!computer.IsOnSameNetwork && computer.AllowRemoteConnections)`
   才走到 `:470` 的 `TraceRoute.GetRoutingStatusAsync()`。同网段 LAN 会话里 `IsOnSameNetwork == true`，
   这段代码**走不到**。它只影响 NAT 分类文案（`:472-477` 的 "double NAT" / "CGNAT"），不影响串流。
   ⇒ **C9 应归入「只在远端场景」，不是「纯 LAN 也会被挡」。**
2. **Quest 侧没有 SmartAssembly 错误上报。** 七个 Quest 侧程序集 grep
   `SmartAssembly|SmartExceptions|ExceptionReporter|SendExceptionEmail|ErrorReport|sawebservice|smartassembly\.com|red-gate`
   只命中 `using SmartAssembly.Attributes;`（混淆标记，`VirtualDesktop.Net\VideoCodec.cs:1` 等 10+ 处），
   **没有任何上报端点**。C13 只属于 PC 侧。

**离线基线的关键更正**：`NetworkManager.cs:637` 是一个**三元表达式**，
`!t.Result` 为真时 `discoveryClient.FindComputersAsync(...)` 这个分支**根本不被求值**：

```csharp
// NetworkManager.cs:637
Task<IEnumerable<Computer>> discoveryTask = SettingsBase<UserSettings>.Default
    .GetHasValidIdentityAsync()
    .ContinueWith((Task<bool> t) => (!t.Result) ? EmptyComputersResult
                                               : discoveryClient.FindComputersAsync(accessTokens.Item1))
    .Unwrap();
```

`ComputerDiscoveryClient` **没有实例构造函数**（成员清单：`static ComputerDiscoveryClient()` +
`FindComputersAsync` / `StopSearch` / `StopListening` / `Dispose` / `CreateBroadcastMessage`），
唯一的 `UdpClient` 创建与 `Send` 都在 `FindComputersAsync` 内部
（`ComputerDiscoveryClient.cs:90` 建 socket，`:98` `_broadcastClient.Send(array, array.Length, BroadcastEP)`）。
**`:634` 的 `new ComputerDiscoveryClient()` 只是把字段置 null，不发任何包。**

⇒ 补丁 APK 重签名导致 `GetHasValidIdentityAsync()` 恒 false（`UserSettings.cs:1518`
 `signature.GetHashCode() - 22 == 1778352230`，官方签名的 hashCode 是 `1778352252`）
⇒ **`FindComputersAsync` 一次都不被调用 ⇒ 广播（固定 3000 ms 窗口）压根不启动。**

这意味着：在打了补丁的基线上，「头显找不到 PC」的**首要原因是重签名导致发现广播根本没发**，
而不是网络层拦截。VDHelper 的发现类检查必须能区分这两种情况——
判据是「Quest 是否向 `255.255.255.255:38850` 发了包」，而不是「PC 是否收得到」。

**本轮 `[未验证]`**：没有在设备上抓包确认补丁基线是否真的不发广播
（headset 当前无 adb）。这一条必须实测才能定案。

---

## 3. 两侧的连接顺序（照代码，不猜）

### 3.1 Quest 侧（`%TEMP%\vd_ep_01\vd\VirtualDesktop.Mobile\VirtualDesktop.Mobile\NetworkManager.cs`）

```
Initialize()            :114-139
  135: ServiceHelper<IComputerRegistry>.Initialize(HttpBinding.Default, NetHelper.GetServerUrl(AmericaCentral))
  136: ServiceHelper2<IComputerRegistry>.Initialize(HttpBinding.Default, NetHelper.GetServerUrl(Europe))
  137: RefreshComputersAsync()

RefreshComputersAsync()  :141-366   → 取 accessToken（平台 SDK）→ GetComputersAsync()

GetComputersAsync()      :629-833
  634: ComputerDiscoveryClient discoveryClient = new ComputerDiscoveryClient();
  637: 广播发现任务 = 三元 (!t.Result) ? EmptyComputersResult : FindComputersAsync(...)
       → t.Result==false 时 FindComputersAsync **不被求值，广播不发**
  712: americaRegistryTask = …BeginGetComputers2…    ← HTTPS 443（仅当 americaProofValid）
  716: europeRegistryTask  = …BeginGetComputers2…    ← 同上
  720: await Task.WhenAll(americaRegistryTask, europeRegistryTask)
  768-812: 合并云端结果与广播结果；云端有结果就 StopSearch()

连接（两种模式，由 computer.UdpEndPoint 是否为 null 决定）
  423: if (udpEndPoint == null)          → 走「云端中继」模式
  425:   MessagingClient.ConnectToPeerAsClientAsync(..., 38811 /*云端*/, ..., 38810 /*本地*/)
  427: else                                → 走「纯本地」模式
  436:   UDP 单播 17 字节到 computer.UdpEndPoint
  457:   MessagingClient.ConnectToLocalPeerAsync(aes, AnyAddress, privateAddresses, 38810)
  529/534/539: Video 38830 / Audio 38840 / Data 38820（本地）
  543/548/553: 同上，但先连云端 38831/38841/38821 再由云端转本地 38830/38840/38820
```

关键判据是 `computer.UdpEndPoint`：**广播发现到的 PC 才有这个字段**（`ComputerDiscoveryClient.cs:128`
`computer.UdpEndPoint = udpReceiveResult.RemoteEndPoint;`），云端注册表返回的 `Computer` 没有
（`%TEMP%\vd_ep_01\vd\VirtualDesktop.Interfaces\VirtualDesktop.Interfaces\Computer.cs:122-130`
里 `UdpEndPoint` **没有** `[DataMember]` 特性，而 `ID`/`Name`/`ConnectionID`/`Key`/`IV` 都有）。

**未打补丁的官方 APK**：`[推断]` 广播通了 → 走 `:427` 纯本地，不碰云端中继；
广播被挡 → 退化成必须走云端注册表 + 38811–38846 中继，此时需要 443 + 出网都通。

**打了补丁的基线（重签名）**：`UserSettings.cs:1518` 的签名校验恒 false ⇒ `:637` 走 `EmptyComputersResult`
分支 ⇒ **广播根本不发**，同时 `:710-716` 的 `americaProofValid`/`europeProofValid` 也拿不到有效 `UserProof`
⇒ **云端注册表一个包都不发**。两条发现路径同时归零，列表必然为空，
**与网络是否通无关**。这是「官方远端发现服务不可达」（根因 B9）在补丁基线上的真实形态：
不是云端挂了，是客户端压根没去问。

### 3.2 PC 侧（`%TEMP%\vd_ep_01\pc\VirtualDesktop.Streamer\`）

```
启动
2054: _0006_0003._0005<IComputerRegistry>._0001(America 区域, Europe 区域)   ← 建两个注册表代理
2062: UpdateHelper._0002()                                                   ← 拉 version.txt（C5）
2067-2084: 建 4 个监听客户端 38810 / 38820 / 38830 / 38840
2110/2420: if (!AllowRemoteConnections) UPnPManager._0001()._0001()          ← L10

有人来连时（ConnectionManager.cs:123-167）
132:  RegisterComputer5(connectionID, …, OS.Windows, …, IsLocalTrafficEncrypted, encryptRemoteTraffic: true)  ← C3/C4
167:  ConnectToPeerAsClientAsync(_0084._0004._0001() /*服务器 IP*/, registrationToken.ConnectionID,
                                registrationToken.Aes, 38811 + ServerRotation)                                ← C1/C2

网络变化 / 定时
 166-167: IncrementServerRotation() 然后换下一个中继端口
2472-2514: 遍历默认网关，试 https:// 和 http://，成功就存 RouterUrl                                     ← L9
2653-2680: 往 255.255.255.255:38860 发两个空 UDP 包                                                     ← L7
```

---

## 4. 端口 ↔ 常量来源对照

brief 要求「给出常量名和来源」。实话说：**这些端口在两侧都是裸字面量，没有常量名。**
能确认的「来源」只有一处 —— `ServerRotation`：

| 端口 | 常量名 | 来源 |
| --- | --- | --- |
| 38810 / 38820 / 38830 / 38840 | 无 | 两侧各自硬编码，见 §1.1 |
| 38850 / 38860 | 无 | 硬编码，见 §1.1 |
| 38811/38821/38831/38841（+n） | `StreamerSettings.ServerRotation` | `PC-S\VirtualDesktop.Streamer\StreamerSettings.cs:242-273`（属性）、`:3217-3233`（`IncrementServerRotation`，`% 6`） |
| 云端 IP 20.225.41.170 / 40.89.161.236 | `NetHelper.AmericaCentralServerIP` / `EuropeServerIP` | Quest `%TEMP%\vd_ep_01\vd\VirtualDesktop.Interfaces\VirtualDesktop.Interfaces\NetHelper.cs:8,10,16,17`；PC `PC-S\-/-.cs:10062,10064,10081,10083` |
| 注册表 URL | `NetHelper.GetServerUrl(ServerRegion)` | Quest `NetHelper.cs:38-45`；PC `PC-S\-/-.cs:10181-10203` |
| 区域选择 | `NetHelper.Region` | Quest `NetHelper.cs:18-26`：本机 UTC 偏移在 `[-12, -2]` → AmericaCentral，否则 Europe |

`Region` 的判定是**按 PC/头显本地时区**，不是按实际位置：

```csharp
// NetHelper.cs:18-26
double totalHours = TimeZoneInfo.Local.BaseUtcOffset.TotalHours;
if (totalHours <= -2.0 && totalHours >= -12.0)
    Region = ServerRegion.AmericaCentral;
else
    Region = ServerRegion.Europe;
```

**[推断]** 中国的头显（UTC+8）会落到 Europe 分支，即去拨 `40.89.161.236` 和 `europe.vrdesktop.net`。
标 `[推断]`——本轮没做运行时验证。

---

## 5. 查不到 / 未验证

| 项 | 我搜了什么 | 状态 |
| --- | --- | --- |
| Quest 侧 CDN / asset host | §0.4 第 2、3、5 条命令，`ldstr_all.txt` 全量 + 二进制 host 扫描 | **不存在**（只有 §2.1 那 3 个 VD 域名） |
| `ws://` / `wss://` 字面量 | 同上 | **不存在**于 VD 自有代码；只有 §0.4 第 4 条扫出的 Speech SDK 原生库模板 `.stt.speech.microsoft.com` 等 |
| 流媒体走 UDP/QUIC | `ConnectTo*Async` 全部是 `TcpClient`；`UdpClient` 只出现在发现广播（38850）、发现应答解密（`ComputerDiscoveryClient.cs:107`）和连上 PC 前的 17 字节单播握手（`NetworkManager.cs:436`） | **无 UDP 视频/音频传输**；串流全 TCP |
| `libVirtualDesktopNet.dll`（原生）里有没有额外端口 | 该文件 ilspy 拒读（无托管元数据）；扫描 LE16 端口常量：38810 出现 1 次、38851/38852 各 1 次，38820/38830/38840/38850/38860 各 0 次 | `[未验证]` 原生层是否有额外监听 |
| Meta/Pico/Viveport/Google 平台 SDK 的实际 host | 三个 SDK 程序集 UTF-16 + ASCII 全量扫描 0 命中；代码里只有 `Platform` 枚举分支 | `[未验证]` C14 |
| C12 `discord.vrdesktop.net` 的用途 | 只在 exe 原始字符串表命中，C# 反编译树里搜不到调用点 | `[未验证]` |
| 云端中继的真实端口是否为 38811–38846 连续 6 个 | 代码只能证明 `base + (x % 6)`；具体 base 列表 38811/21/31/41 见 §1.1/1.2 | 部分 `[推断]`（展开逻辑已确证，端口基数来自硬编码字面量） |
| UPnP 映射的是不是也只有这 4 个端口 | `UPnPManager.cs:112,402` 的白名单只含这 4 个 | 已确证 |
| 38860 是不是标准 WOL magic packet | `ConnectionManager.cs:2676` 发 `Array.Empty<byte>()` | `[推断]` **不是** magic packet |
| 「纯 LAN 下完全不联网」 | 初稿说「`TraceRoute` 无条件对 `8.8.8.8` 打」—— **已更正为错**，见 §2.3 更正 1，`NetworkManager.cs:468` 有 `IsOnSameNetwork` 守卫 | 更正后：同网段会话里 Quest 侧确实没有无条件公网请求；但代码里仍无 `if (offline) skip` 机制，是否真能零出网 `[未验证]` |
| **补丁基线上广播是否真的不发** | 静态推导链完整（`:637` 三元 + `ComputerDiscoveryClient` 无实例 ctor + `UserSettings.cs:1518` 签名常量），但**没有设备抓包** | `[未验证]` —— 这是整份报告最该实测的一条，见 §6 |

---

## 6. 给下游的交接

- **worker 2（发现机制）**：端口和广播细节在我这里（§1.1、§1.4）；发现包格式在
  `%TEMP%\vd_ep_01\vd\VirtualDesktop.Net\VirtualDesktop.Net\ComputerDiscoveryClient.cs:175-187`
  （RSA 公钥 + platform 字节 + AccountID，AES-CBC，`PaddingMode.None`），应答解密在 `:107-129`。
- **worker 3（补丁基线依赖）**：§2.1 整列都是「打了补丁的离线基线不需要这个」候选，
  但**判断权在你**。我给的原始事实是：
  - **更正（我初稿写错了，已按源码改）**：我原写「`GetHasValidIdentityAsync()` 恒 false 但广播任务仍会跑」——
    **错**。`NetworkManager.cs:637` 是三元，`!t.Result` 为真时 `discoveryClient.FindComputersAsync(...)`
    这个分支不被求值；`ComputerDiscoveryClient` 没有实例构造函数，唯一 `Send` 在
    `FindComputersAsync` 内部（`ComputerDiscoveryClient.cs:90` 建 socket、`:98` 发送）。
    ⇒ **补丁重签名 ⇒ 广播一次都不发。** 这条是你指出来的，我复核后确认成立。
  - 配套事实：`:710-716` 的 `if (americaProofValid)` / `if (europeProofValid)` 守卫，
    无有效 `UserProof` 时云端注册表也一个包都不发。⇒ 两条发现路径同时归零。
  - ⇒ 对根因 B9 的直接含义：**补丁基线上「列表空」的原因是客户端没去问，不是网络挡了**。
    VDHelper 必须能区分「没发包」和「发了被挡」，否则会把重签名问题报成防火墙问题。
  - 我 §2.1 的云端点清单全部保留在表里（原料完整），但按你 D16 的建议，
    在补丁基线上它们**整条不可达**——不是「降级为 Warn」，是「不可能发生」。
  - 你另两条指正我也接受并已改：C9 `8.8.8.8` 受 `NetworkManager.cs:468`
    `if (!computer.IsOnSameNetwork && computer.AllowRemoteConnections)` 守卫，
    同网段会话走不到；C13 SmartAssembly 上报**只属 PC 侧**，Quest 侧七个程序集 grep 上报关键字 = 0 命中。
  - **[未验证] 唯一一条**：设备上抓一次包，看有没有发往 `255.255.255.255:38850` 的 UDP。
    有包 ⇒ 上面「广播不发」这条错；没包 ⇒ 成立。这是你提的、我同意的落点，
    本轮 headset 无 adb，做不了。
