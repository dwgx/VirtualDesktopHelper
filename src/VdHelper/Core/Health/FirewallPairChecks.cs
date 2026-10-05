using System.Text.Json;
using VdHelper.Core.Checks;
using VdHelper.Core.Config;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Two failure modes that no existing check can see, because neither one is a "value" — they are
/// a firewall rule whose target file no longer exists, and a settings key that was typed but never
/// saved. Covers B2 (inbound/outbound rule pairing) and A2 (pairing info actually on disk) of
/// <c>research/09-failure-corpus/02-symptom-to-rootcause.md</c>.
/// </summary>
public static class FirewallPairChecks
{
    public static IReadOnlyList<ICheck> Create() =>
    [
        FirewallRulePairCheck(),
        AccountsPersistedCheck(),
    ];

    // ================================================================ B2 防火墙规则成对性

    /// <summary>
    /// 一行一条规则，管道分隔：DisplayName|Direction|Action|Enabled|Profile|Program|Test-Path 结果。
    /// 管道符在 Windows 文件名里是非法字符，所以拿它当分隔符不会切错路径。
    /// <c>Test-Path</c> 在 PowerShell 侧直接做掉：exe 被移动/重装过之后规则还在，但已经指向空气。
    /// <para>
    /// <c>%SystemRoot%</c> 这类环境变量必须先 <c>ExpandEnvironmentVariables</c> 再测：
    /// 实测 <c>Test-Path -LiteralPath '%SystemRoot%\system32\spoolsv.exe'</c> 返回 False，
    /// 而该文件确实存在。不展开就会把系统自带规则一律误判成「指向空气」。
    /// </para>
    /// </summary>
    private const string PsFwRules =
        // -ErrorAction Stop on the outer query, and no blanket $ErrorActionPreference / exit 0.
        // This script had both, so a Get-NetFirewallRule that failed outright looked identical to
        // "there are no rules" — exit 0, empty stdout — and fw-pair answered Block with a repair
        // attached on a machine it had failed to read. The per-rule noise is real and is silenced
        // locally instead, on just the two calls that generate it.
        "Get-NetFirewallRule -DisplayName 'Virtual Desktop*' -ErrorAction Stop | ForEach-Object { $r=$_; "
        + "$p=($r | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue).Program; "
        + "$ex = if ([string]::IsNullOrWhiteSpace($p) -or $p -eq 'Any') { 'NoProgram' } "
        + "else { [string](Test-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($p))) }; "
        + "Write-Output ('{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $r.DisplayName,$r.Direction,$r.Action,"
        + "$r.Enabled,$r.Profile,$p,$ex) }";

    /// <summary>
    /// 解析脚本输出。独立成方法是为了能对真实规则行做单测，而不必去创建/改动防火墙规则。
    /// 少于 7 段的行一律丢弃（表头、空行、被截断的行）。
    /// </summary>
    internal static List<FwRule> ParseRules(string? stdout)
    {
        var rules = new List<FwRule>();
        foreach (var line in (stdout ?? string.Empty)
                     .Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var f = line.Split('|');
            if (f.Length < 7) continue;
            rules.Add(new FwRule(f[0], f[1], f[2], f[3], f[4], f[5], f[6]));
        }
        return rules;
    }

    /// <summary>一条 Virtual Desktop 防火墙规则，已带路径有效性判定。</summary>
    internal sealed record FwRule(
        string Name, string Direction, string Action, string Enabled,
        string Profile, string Program, string PathState)
    {
        public bool IsEnabled => Enabled.Equals("True", StringComparison.OrdinalIgnoreCase);

        public bool Incoming => Direction.Equals("Inbound", StringComparison.OrdinalIgnoreCase);

        public bool Outgoing => Direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase)
                             || Direction.Equals("Outbound", StringComparison.OrdinalIgnoreCase);

        public bool Allows => Action.Equals("Allow", StringComparison.OrdinalIgnoreCase);

        public bool Blocks => Action.Equals("Block", StringComparison.OrdinalIgnoreCase);

        /// <summary>规则指向一个确实存在的文件；Program 为 Any / 空路径不算数。</summary>
        public bool TargetAlive => PathState.Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>Program 是 Any 或空路径 —— 规则没有绑定到具体程序上（与「指向失效文件」是两种故障）。</summary>
        public bool HasNoProgram =>
            Program.Equals("Any", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Program);

        public string AsEvidence() =>
            $"{Name} | {Direction} | {Action} | Enabled={Enabled} | Profile={Profile} | "
            + $"Program={Program} | Test-Path={PathState}";
    }

    /// <summary>
    /// 语料 R19 的 OP 自己给出的解法不是关防火墙，而是补规则：「我注意到 VD 没有出站规则，
    /// 手工加了一条，现在即使防火墙开着也能连上」。所以本项查的是**成对性 + 指向有效性**，
    /// 不是「防火墙开没开」。
    /// </summary>
    public static ICheck FirewallRulePairCheck() =>
        CheckFactory.Delegate(
            new("fw-pair", "防火墙规则成对性", "入站放行在不在、指向的文件还在不在、有没有出站拦截？", "防火墙"),
            async ct =>
            {
                var r = await PowerShellRunner.RunAsync(PsFwRules, ct: ct).ConfigureAwait(false);
                var rules = ParseRules(r.StdOut);

                var ev = new Dictionary<string, string>
                {
                    ["查询命令"] = "Get-NetFirewallRule -DisplayName 'Virtual Desktop*' + Get-NetFirewallApplicationFilter + Test-Path",
                    ["规则条数"] = rules.Count.ToString(),
                    ["规则明细（完整字段）"] = rules.Count == 0
                        ? "(没有任何 DisplayName 以 'Virtual Desktop' 开头的规则)"
                        : string.Join(" ;; ", rules.Select(x => x.AsEvidence())),
                    ["判定口径"] =
                        "入站 Pass = 有 Inbound + Allow + Enabled 且 Program 指向存在的文件；"
                        + "出站 Block = 存在针对 Virtual Desktop 的显式 Outbound + Block 规则（Windows 出站默认放行，"
                        + "所以要查的是显式拦截，不是「没有出站规则」）",
                    ["PowerShell 退出码"] = r.ExitCode.ToString(),
                };
                if (r.StdErr is { Length: > 0 } && !string.IsNullOrWhiteSpace(r.StdErr))
                    ev["stderr"] = r.StdErr.Trim();

                if (!r.Ok)
                    return new CheckResult("fw-pair", CheckStatus.Unknown, "防火墙规则没能读完",
                        "Get-NetFirewallRule 查询未正常返回，无法判断入站放行是否有效。", ev,
                        Array.Empty<FixAction>(), "以管理员身份重试；PowerShell 策略若禁用了 NetSecurity 模块，这里会一直读不到。");

                var v = Judge(rules);
                if (v.Outcome == FwOutcome.NoRules)
                    return new CheckResult("fw-pair", CheckStatus.Block, "一条 Virtual Desktop 防火墙规则都没有",
                        "官方安装器会建一条入站放行规则；完全没有规则通常意味着装的是 Android/手机端、"
                        + "或装完又被清理工具删干净了。此时 PC 侧不回应入站，头显就是「找不到电脑」。",
                        ev, Fixes.RestoreVdRule(),
                        "不要靠关防火墙解决（见 §3.3）：关掉只是让诊断失真，还顺手干掉 ICS 与 VPN 的接口隔离，"
                        + "制造出新的干扰因素。正确动作是保持防火墙开启、把缺的规则补上。"
                        + "修完用这条命令自查：Get-NetFirewallRule -DisplayName 'Virtual Desktop*' | "
                        + "Select-Object DisplayName,Direction,Action,Enabled,Profile");

                var names = string.Join("、", v.Offenders.Select(x => x.Name));

                if (v.Outcome == FwOutcome.OutboundBlocked)
                    return new CheckResult("fw-pair", CheckStatus.Block,
                        $"{v.Offenders.Count} 条针对 Virtual Desktop 的出站拦截规则：{names}",
                        "Windows 出站默认是放行，所以只有**显式 Block 规则**才会真的把 PC 侧的应答掐掉。"
                        + "这类规则多半来自安全软件、企业策略，或用户自己为了排查临时加的。"
                        + "命中它时，头显能发现电脑却建立不了串流连接，现象是「一直转圈 / 连接超时」。"
                        + "R19 的 OP 走的是反方向——他缺的是**出站放行**，手工补了一条才在防火墙开着的情况下连上。",
                        ev, Array.Empty<FixAction>(),
                        "先确认这条 Block 是谁加的，再决定怎么处理："
                        + "① 用户自己加的排查用规则 → 删掉即可；"
                        + "② 杀软/安全套件加的 → 在该软件里把 Virtual Desktop Streamer 与 VirtualDesktop.Service 加进白名单；"
                        + "③ 企业 GPO 下发的 → 工具不改，也不该改，找 IT 把 Virtual Desktop 加入出站例外。"
                        + "删除前先导出备份：`netsh advfirewall firewall export D:\\fw-backup.wfw`（导出整个策略，"
                        + "删除单条规则无法回滚）。");

                // Program=Any（规则打得太宽）与「指向失效文件」（重装后路径变了）是两种不同故障。
                if (v.Outcome == FwOutcome.InboundNotBound)
                    return new CheckResult("fw-pair", CheckStatus.Block,
                        $"{v.Offenders.Count} 条入站放行规则没有指向具体程序（Program=Any）：{names}",
                        "规则是放行的，但没有绑定到 Virtual Desktop 的可执行文件上。"
                        + "这种规则要么来自安装器降级，要么被手工改宽了：它对任何程序都生效，"
                        + "既不能证明 VD 真的被放行（策略顺序仍可能被更靠前的规则拦掉），"
                        + "也会让防火墙高级设置里的界面看起来「已经配好了」。",
                        ev, Fixes.RestoreVdRule(),
                        "删掉这条 Program=Any 的宽规则，再执行本项提供的「重建 Virtual Desktop 入站放行规则」，"
                        + "让规则明确绑定到 VirtualDesktop.Streamer.exe。删之前先导出备份："
                        + "`netsh advfirewall firewall export D:\\fw-backup.wfw`。");

                if (v.Outcome == FwOutcome.InboundTargetGone)
                    return new CheckResult("fw-pair", CheckStatus.Block,
                        $"{v.Offenders.Count} 条入站放行规则指向已不存在的程序：{names}",
                        "规则本身完好（Enabled=True、Action=Allow），但它的 Program 路径已经不存在了——"
                        + "通常是重装或升级后 exe 换了目录、或装到了另一块盘。"
                        + "Windows 防火墙对一条指向空气的规则等于没有：用户在「防火墙高级设置」里看到它绿着，"
                        + "于是永远不会怀疑它，但入站连接照样被默认丢弃。这是**静默失效**，只看规则列表查不出来。",
                        ev, Fixes.RestoreVdRule(),
                        "重建之前先删掉失效的旧规则，否则会出现两条同名规则互相盖："
                        + "`netsh advfirewall firewall delete rule name=\"<上表中的规则名>\"`；"
                        + "然后执行本项提供的「重建 Virtual Desktop 入站放行规则」。"
                        + "删之前先导出备份：`netsh advfirewall firewall export D:\\fw-backup.wfw`。");

                if (v.Outcome == FwOutcome.NoUsableInbound)
                    return new CheckResult("fw-pair", CheckStatus.Block,
                        "没有可用的 Virtual Desktop 入站放行规则（" + v.Note + "）",
                        "PC 侧要主动回应头显的串流请求，就必须有入站放行。"
                        + "注意 Windows 的出站默认放行，所以缺出站规则通常不影响连上；"
                        + "真正致命的是入站这一侧缺失、被禁用、或被第三方防火墙接管。",
                        ev, Fixes.RestoreVdRule(),
                        "按 R19 的思路补规则、保持防火墙开启，而不是关防火墙。"
                        + "若装过第三方杀软（McAfee/Norton/Avast/AVG），还要在该软件里放行 Virtual Desktop Streamer 与 VirtualDesktop.Service；"
                        + "只补 Windows 规则而不动第三方防火墙往往还是连不上。");

                return new CheckResult("fw-pair", CheckStatus.Pass,
                    $"入站放行有效（{string.Join("、", v.Good.Select(x => x.Name))}），没有针对 VD 的出站拦截",
                    "防火墙保持开启就能连——这正是 R19 里那位 OP 补完规则后的状态。"
                    + "本项通过不代表防火墙配置没问题，只代表 Virtual Desktop 这一对规则是成对且有效的。",
                    ev, Array.Empty<FixAction>(),
                    v.Notes.Count == 0
                        ? "如果现在仍然连不上，故障不在规则上：下一步查虚拟网卡/APIPA、路由 metric、VPN 过滤驱动与头显侧设置。"
                        : string.Join("；", v.Notes) + "。若现在仍连不上，故障不在规则上，请继续查虚拟网卡、路由 metric 与 VPN 过滤驱动。");
            });

    /// <summary>fw-pair 的判定结果类别。顺序即判定优先级（见 <see cref="Judge"/>）。</summary>
    internal enum FwOutcome
    {
        /// <summary>入站放行有效且没有出站拦截。</summary>
        Ok,
        NoRules,
        OutboundBlocked,
        InboundNotBound,
        InboundTargetGone,
        NoUsableInbound,
    }

    /// <summary>判定结果：类别 + 触发它的规则 + 供文案使用的补充信息。</summary>
    internal sealed record FwVerdict(
        FwOutcome Outcome,
        IReadOnlyList<FwRule> Offenders,
        IReadOnlyList<FwRule> Good,
        string Note,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// 纯判定，无副作用，因此可以对真实规则行做单测而不必创建或改动防火墙规则。
    /// 优先级是有讲究的：出站拦截最致命，其次是入站指向失效，再退到「入站不可用」。
    /// </summary>
    internal static FwVerdict Judge(IReadOnlyList<FwRule> rules)
    {
        if (rules.Count == 0)
            return new FwVerdict(FwOutcome.NoRules, Array.Empty<FwRule>(), Array.Empty<FwRule>(), "", Array.Empty<string>());

        // 出站显式 Block —— Windows 出站默认放行，只有显式拦截才会掐掉应答。
        var outBlock = rules.Where(x => x.Outgoing && x.Blocks && x.IsEnabled).ToList();
        if (outBlock.Count > 0)
            return new FwVerdict(FwOutcome.OutboundBlocked, outBlock, Array.Empty<FwRule>(), "", Array.Empty<string>());

        // 入站放行但没绑定具体程序：规则范围太宽，证明不了 VD 真的被放行。
        var unbound = rules.Where(x => x.Incoming && x.Allows && x.IsEnabled && x.HasNoProgram).ToList();
        if (unbound.Count > 0)
            return new FwVerdict(FwOutcome.InboundNotBound, unbound, Array.Empty<FwRule>(), "", Array.Empty<string>());

        // 静默失效：规则完好，但 exe 被移动/重装过，路径指向空气。
        var gone = rules
            .Where(x => x.Incoming && x.Allows && x.IsEnabled && !x.TargetAlive && !x.HasNoProgram).ToList();
        if (gone.Count > 0)
            return new FwVerdict(FwOutcome.InboundTargetGone, gone, Array.Empty<FwRule>(), "", Array.Empty<string>());

        // 有效入站 = Inbound + Allow + Enabled + 指向确实存在的文件。
        var good = rules
            .Where(x => x.Incoming && x.Allows && x.IsEnabled && x.TargetAlive && !x.HasNoProgram).ToList();
        if (good.Count == 0)
        {
            var near = rules.Where(x => x.Incoming).ToList();
            var why = near.Count == 0
                ? "没有任何入站规则"
                : "入站规则存在但不合格：" + string.Join("；", near.Select(x =>
                    $"{x.Name}(Action={x.Action}, Enabled={x.Enabled}, "
                    + $"Program={(x.HasNoProgram ? "Any/空" : x.Program)}, Test-Path={x.PathState})"));
            return new FwVerdict(FwOutcome.NoUsableInbound, near, Array.Empty<FwRule>(), why, Array.Empty<string>());
        }

        var notes = new List<string>();
        var disabled = rules.Where(x => !x.IsEnabled).ToList();
        if (disabled.Count > 0)
            notes.Add($"{disabled.Count} 条 Virtual Desktop 规则当前是关闭的：{string.Join("、", disabled.Select(x => x.Name))}（不影响本次判定）");
        var noProg = rules.Where(x => x.HasNoProgram).ToList();
        if (noProg.Count > 0)
            notes.Add($"{noProg.Count} 条规则的 Program 是 Any 或空路径（范围比预期宽，不影响本次判定）：{string.Join("、", noProg.Select(x => x.Name))}");

        return new FwVerdict(FwOutcome.Ok, Array.Empty<FwRule>(), good, "", notes);
    }

    // ================================================================ A2 配对信息是否落盘

    /// <summary>
    /// 语料 R05：用户在 Streamer 里填了 Oculus 用户名，**没点 Save**，配置没落盘，
    /// 表现成「配对不上」。官方与社区都没有给过自查办法，所以这一项只能靠读文件本身。
    /// </summary>
    public static ICheck AccountsPersistedCheck() =>
        CheckFactory.Delegate(
            new("accounts-persisted", "配对信息落盘", "账户和设备名真的写进配置文件了吗？", "配置"),
            ct =>
            {
                var s = StreamerSettings.Load();
                var ev = new Dictionary<string, string>
                {
                    ["配置文件"] = s.Path,
                    ["文件是否存在"] = s.Exists ? "是" : "否",
                    ["最后修改时间"] = s.LastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                    ["读取口径"] = "本项读的是 StreamerSettings.json 里的真实值，不是界面上显示的值",
                };
                if (!s.Exists)
                    return Task.FromResult(new CheckResult("accounts-persisted", CheckStatus.Unknown,
                        "读不到 Streamer 配置", s.ParseError ?? "文件不存在", ev, Array.Empty<FixAction>(),
                        "官方安装会写这个文件；缺失说明 Streamer 从未正常运行过，先把 PC 端装好、配一次对。"));

                ev["顶层键"] = s.TopLevelKeys.Count == 0 ? "(空对象)" : string.Join(", ", s.TopLevelKeys);

                // Accounts：只报分组名与条目数。条目本身是 DPAPI 密文，任何情况下都不进界面、不进证据。
                var accounts = ReadAccounts(s);
                ev["Accounts 分组（只报名称与条目数）"] = accounts.Shape switch
                {
                    AccountsShape.Missing => s.TopLevelKeys.Contains("Accounts")
                        ? "(Accounts 键在，但值是 null)"
                        : "(顶层没有 Accounts 键)",
                    AccountsShape.NotObject => $"(Accounts 的 JSON 类型是 {accounts.RawKind}，不是预期的对象)",
                    _ => accounts.Groups.Count == 0
                        ? "(Accounts 是空对象 {})"
                        : accounts.Groups.All(g => g.Count == 0)
                            ? "分组都在但每组都是空数组：" + string.Join(" ;; ", accounts.Groups.Select(g => $"{g.Name} = 0 条"))
                            : string.Join(" ;; ", accounts.Groups.Select(g => $"{g.Name} = {g.Count} 条")),
                };
                ev["Accounts 条目总数"] = accounts.Groups.Sum(g => g.Count).ToString();
                ev["密文处理"] = "Accounts 每个条目都是 DPAPI 密文，本项只统计条目数，不读取也不显示密文原文";

                var device = s.GetString("DeviceName");
                ev["DeviceName"] = device ?? "(空 / null)";

                var total = accounts.Groups.Sum(g => g.Count);
                var problems = new List<string>();
                if (total == 0)
                    problems.Add(accounts.Shape switch
                    {
                        AccountsShape.Missing => s.TopLevelKeys.Contains("Accounts")
                            ? "Accounts 的值是 null：配对信息没有真正写进配置文件"
                            : "配置里没有 Accounts 键：配对信息没有真正写进配置文件",
                        AccountsShape.NotObject =>
                            $"Accounts 的 JSON 类型是 {accounts.RawKind} 而不是对象：读不到任何账户条目，配对信息没有真正写进配置文件",
                        _ => "Accounts 里没有任何账户条目：配对信息没有真正写进配置文件",
                    });
                if (device is null)
                    problems.Add("DeviceName 为空：头显客户端里这台电脑会显示不出名字，只能靠 IP 选");

                if (problems.Count == 0)
                    return Task.FromResult(new CheckResult("accounts-persisted", CheckStatus.Pass,
                        $"配对信息已落盘（{string.Join("、", accounts.Groups.Select(g => $"{g.Name} {g.Count} 条"))}），设备名 {device}",
                        "配置里有真实的账户条目，头显侧应该能在账户列表里看到这台电脑。", ev, Array.Empty<FixAction>()));

                var detail = problems[0].Contains("没有真正写进配置文件")
                    ? "配对信息没有真正写进配置文件；最常见的原因是**在 Streamer 的账户页填了用户名但没点 Save**——"
                    + "输入框里看得见字符不代表已经持久化，关掉窗口就丢了。文件读不到任何账户条目时，"
                    + "头显侧表现是「找不到这台电脑」或「配对一直失败」，但界面上看不出任何异常，官方与社区都没给过自查办法。"
                    + (problems.Count > 1 ? " 另外 DeviceName 也是空的：头显端列表里这台电脑会没有可辨认的名字。" : "")
                    : problems[0];

                return Task.FromResult(new CheckResult("accounts-persisted", CheckStatus.Warn,
                    string.Join("；", problems), detail, ev, Array.Empty<FixAction>(),
                    "请打开 PC 端的 Virtual Desktop Streamer，在账户（Accounts）页面把 Oculus 账号登录/填好，"
                    + "**点 Save 保存**，再点右上角显示设备名的地方确认 DeviceName 不是空的，然后完全退出 Streamer 再重开。"
                    + "本项读的是文件里的真实值，不是界面显示值：所以「界面上明明填了」与「这项报空」可以同时成立，"
                    + "以本项为准。退出后再跑一次体检，若 Accounts 仍为空，说明保存没成功"
                    + "（常见于配置目录权限不足或被安全软件拦下写入），把该目录加入白名单后重试。"));
            });

    private enum AccountsShape { Missing, NotObject, Object }

    /// <summary>
    /// Accounts 的分组名与条目数。**只取名称和长度，绝不取条目值**——
    /// 那些值是 DPAPI 密文，即使本机可解，展示出来也没有意义，只会让日志和截图泄密。
    /// </summary>
    private readonly record struct AccountGroups(AccountsShape Shape, IReadOnlyList<(string Name, int Count)> Groups, JsonValueKind RawKind)
    {
        public static readonly AccountGroups Empty =
            new(AccountsShape.Missing, Array.Empty<(string, int)>(), default);
    }

    private static AccountGroups ReadAccounts(StreamerSettings s)
    {
        if (s.Root is not { ValueKind: JsonValueKind.Object } root) return AccountGroups.Empty;
        if (!root.TryGetProperty("Accounts", out var acc)) return AccountGroups.Empty;

        switch (acc.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var groups = new List<(string, int)>();
                foreach (var g in acc.EnumerateObject())
                {
                    var count = g.Value.ValueKind switch
                    {
                        JsonValueKind.Array => g.Value.GetArrayLength(),
                        JsonValueKind.String => string.IsNullOrEmpty(g.Value.GetString()) ? 0 : 1,
                        JsonValueKind.Null or JsonValueKind.Undefined => 0,
                        _ => 1, // 数字/布尔/嵌套对象：算一条，具体形态放进证据顶层键里已可见
                    };
                    groups.Add((g.Name, count));
                }
                return new AccountGroups(AccountsShape.Object, groups, acc.ValueKind);
            }
            case JsonValueKind.Array:
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new AccountGroups(AccountsShape.NotObject, Array.Empty<(string, int)>(), acc.ValueKind);
            default: // Null / Undefined：键在但没有内容
                return new AccountGroups(AccountsShape.Missing, Array.Empty<(string, int)>(), acc.ValueKind);
        }
    }
}