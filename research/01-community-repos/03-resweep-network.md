# 03 · 复扫：PC 侧网络诊断（2026-10-06）

只覆盖一个角度：PC 侧**网络**诊断，以及能自动化 VDHelper 核心失败路径的东西。
不复述 `01-inventory.md` 已有的行，除非状态变了。

取数与 `01` 号同形状，可复跑：

```bash
gh api "repos/$r" --jq '[.full_name,(.language//"-"),(.license.spdx_id//"NO-LICENSE-FILE"),.pushed_at[0:10],.stargazers_count] | @tsv'
```

**每一条「它做什么」都读了源文件，不是读 README 自述。**

> 完整原始报告在 agent transcript `history://SweepNetwork`（该 scout 无写文件工具，主代理代存）。

---

## 1 · 先说清楚 VDHelper 已经做到了什么（决定后面所有排名的分量）

以下是**本仓库源码实读**的结果：

| 关注点 | VDHelper 现状 | 证据 |
|---|---|---|
| 防火墙规则审计 | **已有** `fw-vd`/`fw-pair`/`fw-outbound`/`fw-defender`/`fw-profile-inbound` | `HealthChecks.cs:19-34`、`PerformanceChecks.cs:20-21`、`WindowsStateChecks.cs:28-29` |
| 网络配置文件 Private/Public | **已有** `net-profile` | `HealthChecks.cs:19` |
| 出口网卡优先级 / 路由 metric | **已有** `route-metric`，读 `Get-NetIPInterface.InterfaceMetric` | `WindowsStateChecks.cs:24-26`、`:188-230` |
| 发现广播抓包 | **已有** `tools/capture-discovery.ps1`，用 Windows 自带 `pktmon`（非 Npcap） | `capture-discovery.ps1:1-30`、`:98-101` |
| UDP 38850 监听 + 占用者归属 | **已有** `udp-discovery` | `StreamerChecks.cs:234-298` |
| UPnP / NAT-PMP | **已有** `nat-type`，用 SharpOpenNat，只读 | `NatChecks.cs:63-95` |
| **NAT 行为类型（cone/restricted/symmetric）** | **没测，且代码自己写明没测** | `NatChecks.cs:111-114`：「Distinguishing them needs a mapping probe or **STUN**; saying so is more useful than a label the data does not carry」 |
| **ICE 连通性检查 / 打洞测量** | **没有**，全树无 STUN/ICE 代码 | 全仓检索无 `Stun`/`Ice`/`ChangeRequest` 命中 |
| 过滤器驱动绑定顺序 | 没有。`route-metric` 只读接口 metric | `WindowsStateChecks.cs:24-26` 是唯一数据源 |
| PCP（RFC 6887） | 没有。SharpOpenNat 只有 Upnp/Pmp | `NatChecks.cs:79` |

**结论：真正剩下的缺口只有三个——① UDP 上的 NAT/ICE 行为测量；② 不依赖 Npcap 的按进程 UDP 观测；③ PCP。**
其余重点（防火墙、profile、metric、发现抓包）社区侧不缺，VDHelper 也没比它们差。

## 2 · 唯一直接补上自认缺口的仓库

### HMBSbige/NatTypeTester

| 项 | 值 |
|---|---|
| 语言 | C# |
| `license.spdx_id` | **MIT** |
| `pushed_at` | **2026-10-04**（主代理独立复核，非转述） |
| `stargazers_count` | **4869** |
| NuGet | **`Stun.Net`**，已发布至 **10.0.9**（主代理查 `api.nuget.org/v3-flatcontainer/stun.net/index.json` 复核） |
| TFM | `Directory.Build.props` = **`net10.0`**（主代理读原文件复核），本仓库 `net10.0-windows`，**可直接消费** |

**它做什么**：用 STUN 把 NAT 的**映射行为**与**过滤行为**真正测出来。不是读路由器映射表，是发包、
比对换目的地后 `XOR-MAPPED-ADDRESS` 变不变。

**证据（读源码）**：
- `src/STUN/Client/Stun5389NatBehaviorDiscovery.cs` 类注释首句即
  「Implements the NAT behavior discovery algorithm as defined in **RFC 5780 Section 4.2**」。
  `HandleFilteringTest2/3` 用 `BuildChangeRequest(true, true)` / `(false, true)`，落到
  `EndpointIndependent / AddressDependent / AddressAndPortDependent`；
  `HandleMappingTest2/3` 依次打 `(otherIP, serverPort)` 与 `otherEndPoint`，比对两次 `XorMappedAddress`。
- `src/STUN/Enums/NatType.cs` 把 RFC 3489 §5 的标签一字不差列全：
  `UdpBlocked / OpenInternet / SymmetricUdpFirewall / FullCone / RestrictedCone / PortRestrictedCone / Symmetric`，
  每个成员都带该类型的定义原文。

**落地成本**：`src/STUN/STUN.csproj` 有 `PackageLicenseExpression` MIT，`PackageId` = `Stun.Net`，
**有 NuGet 包，不需要抄源码**。代价：同 csproj 内有两个 `PackageReference`（`DTLS`、`Socks5`），
是为 `src/STUN/Proxy/` 服务的——若只取 STUN 核心需确认这二者能否不引入。

**主代理补注（本仓库侧）**：`NatChecks.cs:117-119` 现在主动说「NAT 类型这一项没有测」，
并把结果判为**通过**（因为没测出假结论）。若接 STUN，这条要重新设计三态：
「测到了 Cone/Restricted/Symmetric」/「测了但路由器挡了 STUN」/「根本没测」。
**后者与前者都必须能表达**——这正是本项目一贯的纪律。

## 3 · 不可复用的（记录以免重复查）

| 仓库 | 许可证 | 处置 |
|---|---|---|
| `basil00/WinDivert` | `NOASSERTION` | **不可复用**。仅作为 ProxyBridge 背后的驱动来源记录；本项目应留在系统自带 `pktmon` |
| `samyk/pwnat` | **GPL-3.0** | **不可复用**（强传染）。登记意义：证明存在绕过 STUN 的第三条 UDP 打洞测量路径，若 STUN 被用户路由器挡掉可作参考方向 |