# 2026-10-05 第四轮：把语料里没覆盖的根因补上 + 症状入口 + 文档站

## 这轮做完了什么

1. **检测项 18 → 24**，新增 6 项全部在本机实跑过：
   - `link-rate` 网卡协商速率（D4，社区花整帖才定位的隐蔽坑）
   - `vpn-proc` VPN/代理进程存活（B4，官方 FAQ 的措辞漏洞）
   - `rdp-session` 活动 RDP 会话占显示器（E2，「第二次就黑屏」的经典真因）
   - `nat-type` NAT 类型，走可复用的 MIT 开源内核 **SharpOpenNat 4.0.19**（Owner 点名的 NAT）
   - `fw-pair` 防火墙入站/出站成对性 + 规则 Program 路径是否失效（B2）
   - `accounts-persisted` 配对信息是否真的落盘（A2，「填了没点 Save」）
   - `headset-deep` 头显侧三项（A6 MAC 随机化 / F1 头显设置 / B4-Quest VPN），接进第三屏
2. **症状入口**（ADR-006）：7 个症状类，用语料里的用户原话，选中后只显示相关检测项；
   `--symptom S2` 可直接产出可分享的分诊报告。
3. **三段式文档站**：`docs/index.html` / `checks.html` / `faq.html`，由
   `tools/export-docs-site.ps1` 生成。faq.html 含 10 条社区错误解法逐条纠偏。
4. **纠正一处调研口径错误**：语料 §2 实际列出 **43** 个根因（7+9+4+5+6+6+6），
   早期版本把覆盖度评估的分母「23」当成了根因总数。已改源文件并留下口径说明。

## 证据

```
dotnet build src/VdHelper/VdHelper.csproj -c Release   → 0 error, 0 warning
VdHelper.exe --selftest                                → exit 3，24 项（通过 17 警告 6 阻断 0 未知 1）
--symptom S1                                           → 只显示 11 项相关检测
nat-type 实测：路由器支持 UPnP，外网 IP 172.16.80.42（私有段）→ 判定双层 NAT
fw-pair 实测：入站规则 1 条，Program 路径 Test-Path=True，无出站拦截 → Pass
accounts-persisted 实测：OculusQuest 1 条 / Oculus 3 条，DeviceName=Meta Quest 3 → Pass
文档站：checks.html 39 行表格、faq.html 10 条错解卡、过时文案（44 处/126 份/1000+）grep 计数 0
```

## 这一轮里，三个工人推翻了我给的规格

1. **随机 MAC 的判定位**：我写的是「首字节最低位（组播位）为 1」。实测 `02/06/0A` 开头的
   真实随机 MAC **bit0 全是 0**，按我的写法会漏掉全部真实形态。正确的是本地管理位 bit1（RFC 4300）。
2. **`SharpOpenNat` 的 API**：我按调研文档写了 `GetMappedPortAsync()`，4.0.19 **没有这个方法**；
   正确是 `GetSpecificMappingAsync(Protocol, int, ct)`。已按上游 `INatDevice.cs` 核实并改了调研文档。
3. **PowerShell 的两个陷阱**：`query session` 成功时退出码是 1（`qwinsta` 才是 0），
   不归一化会让检测项永远报「检查未能完成」；`Test-Path -LiteralPath '%SystemRoot%\...'` 不展开
   环境变量，不处理的话 Windows 自带规则会被一律误判成「指向空气」。

三条我都独立复现验证过，不是照抄工人的结论。

## 下一刀

1. **还有 20 个根因从未逐条评估**（语料只评估了 23 个里的覆盖度）。
2. **有设备时的验证**：头显侧三分支（MAC 解析、设置 JSON、VPN 进程）本机无头显，全部标 `[未验证]`。
3. `fw-restore-vd` 修复仍未端到端验证（删防火墙规则要 UAC，本 shell 拿不到）。
4. 「昨天还好好的」已经有 diff 了，但还没做成**按时间点的曲线**，只能看上一轮。

## 没验证的

- 头显侧全部读取分支（无设备）。
- NAT 探测只在本机跑过一次（双层 NAT 已确认），没有第二台机器对照。
- 症状筛选的 UI 只截了图，没有逐个芯片点过——筛选逻辑走 `--symptom S1/S2/S7` 在命令行验过，
  UI 走的是同一条 `Selected` 路径，但按钮点击未逐个实测。