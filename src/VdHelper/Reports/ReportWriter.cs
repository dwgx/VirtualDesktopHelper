using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VdHelper.Core.Health;
using VdHelper.Core.Model;

namespace VdHelper.Reports;

/// <summary>
/// Renders one health pass into something a user can paste into a forum post or a GitHub issue.
/// <para>
/// Why a second renderer instead of reusing the <c>--selftest</c> text: selftest output is a
/// console transcript for the person who ran it, this is an artefact for <em>somebody else</em>.
/// It has to survive being read by a stranger who does not own the machine — so it leads with the
/// verdict, groups the evidence by the symptom the user actually typed, collapses the raw output,
/// and ends with what the tool does not do.
/// </para>
/// <para>
/// Redaction: the primary defence is upstream — <c>FirewallPairChecks</c> never puts an Accounts
/// entry into evidence, only group names and counts, because those entries are DPAPI ciphertext.
/// <see cref="Redact"/> is the backstop for anything that reaches a report another way: a DPAPI
/// blob always base64-encodes starting with "AQAA" (provider version 0x01 as the first three
/// bytes), so that prefix is what we look for. Machine name, user name and LAN addresses are kept
/// on purpose — a report is close to useless without knowing which machine and which subnet.
/// </para>
/// </summary>
public static class ReportWriter
{
    /// <summary>Base64 of any DPAPI blob begins "AQAA"; one of those must never reach a report.</summary>
    private static readonly Regex Ciphertext = new(@"AQAA[A-Za-z0-9+/=]{16,}", RegexOptions.Compiled);

    /// <summary>
    /// Status → the same four words <c>StatusConverters.StatusToTextConverter</c> puts on screen.
    /// Duplicated rather than called because that one is a WPF <c>IValueConverter</c> and a report
    /// must be renderable without a dispatcher. Change both together if the vocabulary moves.
    /// </summary>
    private static string Badge(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "通过",
        CheckStatus.Warn => "警告",
        CheckStatus.Block => "阻断",
        _ => "未知",
    };

    private static string VerdictWord(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Streamable => "可串流",
        HealthVerdict.AtRisk => "有隐患",
        HealthVerdict.Blocked => "阻断",
        _ => "未知",
    };

    // ------------------------------------------------------------------ evidence labels

    /// <summary>
    /// Evidence keys starting with "_" are plumbing between a check and the verdict logic. They
    /// are still the raw output a helper asks for, so they get readable names instead of being
    /// dropped — except the two live-session keys, which the headline already states in full.
    /// </summary>
    private static readonly Dictionary<string, string> InternalKeys = new(StringComparer.Ordinal)
    {
        ["_exit"] = "命令退出码",
        ["_lines"] = "原始输出",
        ["_first"] = "输出首行",
        ["_script"] = "查询命令",
        ["_耗时"] = "本项耗时",
    };

    private const string LivePortsKey = "_livePorts";
    private const string LivePeerKey = "_livePeer";

    private static List<KeyValuePair<string, string>> Reportable(CheckResult r) =>
        r.Evidence
            .Where(e => e.Key != LivePortsKey && e.Key != LivePeerKey)
            .Select(e => new KeyValuePair<string, string>(
                InternalKeys.TryGetValue(e.Key, out var label) ? label : e.Key,
                Redact(e.Value)))
            .ToList();

    private static string Redact(string value) => Ciphertext.Replace(value, "〔已脱敏：DPAPI 密文〕");

    // ------------------------------------------------------------------ public API

    /// <summary>Markdown: raw output goes in fenced code blocks.</summary>
    public static string Markdown(HealthReport report, IReadOnlyList<HealthHistory.Change>? changes, SymptomClass? focus) =>
        Render(report, changes, focus, Md.Head, Md.Checks, Md.Group, Md.Item, Md.Tail, Md.Disclaimer);

    /// <summary>One self-contained HTML file: inline CSS, UTF-8, no external requests.</summary>
    public static string Html(HealthReport report, IReadOnlyList<HealthHistory.Change>? changes, SymptomClass? focus) =>
        Render(report, changes, focus, HtmlDialect.Head, HtmlDialect.Checks, HtmlDialect.Group, HtmlDialect.Item, HtmlDialect.Tail, HtmlDialect.Disclaimer);

    // ------------------------------------------------------------------ shared model

    private sealed record Meta(
        HealthVerdict Verdict, string Word, string Text,
        IReadOnlyList<int> LivePorts, string LivePeer,
        int Pass, int Warn, int Block, int Unknown, int Total,
        DateTime At, string Machine, string User, string Os,
        string FocusTag);

    private sealed record Group(
        string ClassId, string Title, string? PhraseLine, string? FirstLook,
        IReadOnlyList<Item> Results, IReadOnlyList<string> NotRun)
    {
        public bool IsOverflow => ClassId.Length == 0;
    }

    private sealed record Item(CheckResult Result, CheckDefinition? Definition, int No)
    {
        /// <summary>Position in the pass, 1-based; matches the order in <c>docs/checks.md</c>.</summary>
        public int Number => No;
    }

    private sealed record Changes(IReadOnlyList<HealthHistory.Change> List);

    private sealed record Intro(string Lead, string PhraseLine, string FirstLook, int Shown, int Hidden, bool Grouped);

    // ------------------------------------------------------------------ one body, two dialects

    /// <summary>
    /// Markdown and HTML share the section order, the numbers and every sentence; only the
    /// wrapper differs. Every string taken from a check goes through <see cref="Redact"/> and the
    /// escaping its dialect needs, so neither a <c>&lt;script&gt;</c> in a summary nor a leaked
    /// blob can escape into the output.
    /// </summary>
    private static string Render(
        HealthReport report,
        IReadOnlyList<HealthHistory.Change>? changes,
        SymptomClass? focus,
        Func<Meta, string> head,
        Func<Intro, string> checks,
        Func<Group, string> group,
        Func<Item, string> item,
        Func<Changes, string> tail,
        Func<string[], string> disclaimer)
    {
        var meta = BuildMeta(report, DateTime.Now, focus);
        var groups = Group_By(report, focus);
        var sb = new StringBuilder();

        sb.Append(head(meta));
        sb.Append(checks(BuildIntro(report, groups, focus)));
        foreach (var g in groups)
        {
            sb.Append(group(g));
            foreach (var r in g.Results) sb.Append(item(r));
        }
        sb.Append(tail(new Changes(changes ?? Array.Empty<HealthHistory.Change>())));
        sb.Append(disclaimer(DisclaimerLines()));
        return sb.ToString();
    }

    /// <summary>
    /// The flags that produced this report. When a symptom class was chosen, name it — printing
    /// "[--symptom Sx]" on a report that was in fact generated with S1 tells the reader nothing about
    /// the artifact in their hands.
    /// </summary>
    private static string UsageSuffix(Meta m) =>
        string.IsNullOrEmpty(m.FocusTag) ? "" : " " + m.FocusTag;

    private static Meta BuildMeta(HealthReport report, DateTime at, SymptomClass? focus) => new(
        report.Verdict, VerdictWord(report.Verdict), Redact(report.VerdictText),
        report.LiveSessionPorts, Redact(report.LiveSessionPeer),
        report.Results.Count(r => r.Status == CheckStatus.Pass),
        report.Results.Count(r => r.Status == CheckStatus.Warn),
        report.Results.Count(r => r.Status == CheckStatus.Block),
        report.Results.Count(r => r.Status == CheckStatus.Unknown),
        report.Results.Count,
        at, Environment.MachineName, Environment.UserName, Environment.OSVersion.VersionString,
        // Print the class that was actually used, not the Sx placeholder.
        focus is null ? "" : "--symptom " + focus.Id);

    /// <summary>
    /// With a focus, one group in that class's own walk order — the order is the documented
    /// triage order. Without one, every class gets a group and a check appears under each class
    /// that references it: "PC unreachable" and "freezes every 5 minutes" both need
    /// <c>session-stale</c>, and hiding the second one would make the class look incomplete.
    /// Anything no class claims lands in the overflow group.
    /// </summary>
    private static List<Group> Group_By(HealthReport report, SymptomClass? focus)
    {
        var classes = focus is null ? SymptomCatalog.All : [focus];
        var byId = report.Results.ToDictionary(r => r.Id);
        var number = report.Results
            .Select((r, i) => (r.Id, No: i + 1))
            .ToDictionary(x => x.Id, x => x.No, StringComparer.Ordinal);
        var groups = new List<Group>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cls in classes)
        {
            var items = new List<Item>();
            foreach (var id in cls.RelevantChecks)
            {
                if (!byId.TryGetValue(id, out var r)) continue;
                claimed.Add(id);
                items.Add(new Item(r, Definition_Of(report, id), number[r.Id]));
            }
            // The class names checks this run did not produce. Name them: a report that silently
            // drops one of them reads as "this area is fine", which is the opposite of the truth.
            var notRun = cls.RelevantChecks.Where(id => !byId.ContainsKey(id)).ToList();
            groups.Add(new Group(cls.Id, cls.Title, cls.PhraseLine, cls.FirstLook, items, notRun));
        }

        var rest = report.Results
            .Where(r => !claimed.Contains(r.Id))
            .Select(r => new Item(r, Definition_Of(report, r.Id), number[r.Id]))
            .ToList();
        if (focus is null && rest.Count > 0)
            groups.Add(new Group("", "未归入任何症状类", null, null, rest, Array.Empty<string>()));
        return groups;
    }

    private static CheckDefinition? Definition_Of(HealthReport report, string id) =>
        report.Definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// Hidden is "what this run produced that the focus class does not name" — the same count
    /// <c>--selftest</c> prints, so the two outputs can never disagree about how much was left out.
    /// </summary>
    private static Intro BuildIntro(HealthReport report, List<Group> groups, SymptomClass? focus)
    {
        var shown = groups.Sum(g => g.Results.Count);
        if (focus is null)
            return new Intro("未指定症状类：以下是整轮体检结果，按症状类分组。", "", "", shown, 0, true);
        // The phrase and first-look lines are not repeated here: the one group below prints both
        // already, and a report people paste into issue threads should not say everything twice.
        return new Intro(
            $"本报告只针对症状类 {focus.Id}「{focus.Title}」——这一类对应你描述的那句人话。",
            "", "", shown, report.Results.Count - shown, false);
    }

    /// <summary>
    /// The three limits the project already holds itself to (AGENTS.md §2): no auth verdicts, no
    /// official binaries, no silent changes to the router or the AV. A shareable report that
    /// omits these reads as a stronger claim than the tool is entitled to make.
    /// </summary>
    private static string[] DisclaimerLines() =>
    [
        "本工具**不做鉴权相关判定**：不检查 entitlement / token / 账号登录状态，也不生成任何凭据。"
            + "报告里的「串流会话」只说明 TCP 38810 那一组通道此刻是 Established，"
            + "不代表账号侧被验证过——鉴权失败的头显在本报告里和能连上的头显长得一样。",
        "本仓库与本报告**不分发任何官方二进制**（APK / keystore / 官方 EXE）。"
            + "报告里出现的安装路径只用于让你在自己机器上核对，不要据此去别处下载。",
        "本工具**不会自动修改路由器设置或第三方杀软**：这两类改动只给指引，由你在对应软件里自己完成。"
            + "工具能自动改的项都带备份与回滚命令，且需要你显式确认（本报告的「原始输出」里能查到它用的是哪条命令）。",
        "报告里的每一项都是**运行那一刻**的观测，状态会变。原始输出含本机与头显的局域网地址，"
            + "机器名与用户名也一并写出——贴到公开场合前请自行确认这些可以公开。",
    ];

    // ------------------------------------------------------------------ shared fragments

    private static string Live(Meta m) => m.LivePorts.Count == 0
        ? "此刻没有已建立的 VD 通道（不是「串流中」）。"
        : $"串流中：{m.LivePorts.Count} 个通道已建立（端口 {string.Join("、", m.LivePorts)}）"
            + (m.LivePeer.Length == 0 ? "" : $"，对端 {m.LivePeer}");

    private static string IsNew(HealthHistory.Change c) => c.IsNew ? "（新增）" : "";

    private static string Stats(Meta m) =>
        $"通过 {m.Pass} / 警告 {m.Warn} / 阻断 {m.Block} / 未知 {m.Unknown}，共 {m.Total} 项";

    private static string BadgeClass(CheckStatus s) => s switch
    {
        CheckStatus.Pass => "pass",
        CheckStatus.Warn => "warn",
        CheckStatus.Block => "block",
        _ => "unknown",
    };

    private static string VerdictClass(HealthVerdict v) => v switch
    {
        HealthVerdict.Streamable => "pass",
        HealthVerdict.AtRisk => "warn",
        HealthVerdict.Blocked => "block",
        _ => "unknown",
    };

    /// <summary>Escape for Markdown inline code: a backtick would end the span early.</summary>
    private static string Esc(string s) => s.Replace("`", "'");

    /// <summary>
    /// A code fence long enough that its content cannot close it. Evidence is machine output and
    /// is the one field nobody vets before it lands in a public report.
    /// </summary>
    private static string Fence(string body)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in body)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}text\n{body}\n{fence}";
    }

    private static string EvidenceBody(IReadOnlyList<KeyValuePair<string, string>> evidence) =>
        string.Join("\n", evidence.Select(e => $"{e.Key}: {e.Value}"));

    // ------------------------------------------------------------------ Markdown dialect

    private static class Md
    {
        public static string Head(Meta m) => $"""
            # VDHelper 体检报告

            > 生成方式：`VdHelper.exe --report <file>{UsageSuffix(m)}`。可直接贴到社区求助或 GitHub issue。
            > 脱敏口径：不写任何 DPAPI 密文、令牌、账户条目内容；机器名 / 用户名 / 局域网地址保留。

            ## 1. 结论

            **{m.Word}** — {m.Text}

            - 生成时间：{m.At:yyyy-MM-dd HH:mm:ss}
            - 机器名：`{Esc(m.Machine)}`　用户名：`{Esc(m.User)}`　系统：{Esc(m.Os)}
            - 串流会话：{Live(m)}
            - 统计：{Stats(m)}


            """;

        public static string Checks(Intro intro)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 2. 检测项");
            sb.AppendLine();
            sb.AppendLine(intro.Lead);
            sb.AppendLine();
            if (intro.PhraseLine.Length > 0)
            {
                sb.AppendLine($"- 用户原话：{intro.PhraseLine}");
                sb.AppendLine($"- 先看这里：{intro.FirstLook}");
                sb.AppendLine();
            }
            sb.AppendLine(intro.Grouped
                ? "> 同一检测项会在多个症状类下重复出现，因为多个症状共用它；"
                    + "编号是它在整轮体检里的固定位置，与 `docs/checks.md` 的顺序一致。"
                : $"> 本症状类列出 {intro.Shown} 项相关检测，已隐藏 {intro.Hidden} 项无关检测。");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string Group(Group g)
        {
            var sb = new StringBuilder();
            sb.AppendLine(g.IsOverflow
                ? $"### {g.Title}　（{g.Results.Count} 项）"
                : $"### {g.ClassId}「{g.Title}」　（{g.Results.Count} 项）");
            sb.AppendLine();
            if (g.PhraseLine is not null)
            {
                sb.AppendLine($"用户原话：{g.PhraseLine}");
                sb.AppendLine();
                sb.AppendLine($"先看这里：{g.FirstLook}");
                sb.AppendLine();
            }
            if (g.NotRun.Count > 0)
            {
                sb.AppendLine($"> 这一类点名了 {g.NotRun.Count} 项本轮 PC 侧体检没有结果的检测："
                    + string.Join("、", g.NotRun.Select(Esc))
                    + "。本报告只跑 PC 侧那一遍（如实列出，不静默略过）；头显侧检测在第三屏，需要 adb 连上头显。");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Item(Item item)
        {
            var r = item.Result;
            var sb = new StringBuilder();
            var title = item.Definition is null ? "" : $" — {Esc(item.Definition.Title)}";
            sb.AppendLine($"#### {item.Number}. 【{Badge(r.Status)}】`{Esc(r.Id)}`{title}");
            sb.AppendLine();
            sb.AppendLine(Redact(r.Summary));
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(r.Detail))
                sb.AppendLine($"**为什么**：{Redact(r.Detail)}");
            if (r.Fixes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**可以做的修复**：");
                foreach (var f in r.Fixes)
                    sb.AppendLine($"- `{Esc(f.Id)}`（风险 {f.Risk}）{Esc(f.Title)} — {Redact(f.What)}"
                        + $"　备份：{Redact(f.Backup)}　回滚：{Redact(f.Rollback)}"
                        + (f.NeedsElevation ? "　（会弹 UAC，需要你点确认）" : ""));
            }
            if (!string.IsNullOrWhiteSpace(r.Guidance))
            {
                sb.AppendLine();
                sb.AppendLine($"**只能指引、不能自动做的事**：{Redact(r.Guidance)}");
            }
            var evidence = Reportable(r);
            if (evidence.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**原始输出**：");
                sb.AppendLine();
                sb.AppendLine(Fence(EvidenceBody(evidence)));
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static string Tail(Changes c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 3. 与上次的变化");
            sb.AppendLine();
            if (c.List.Count == 0)
            {
                sb.AppendLine("与上次相比无变化。");
                sb.AppendLine();
                return sb.ToString();
            }
            sb.AppendLine($"共 {c.List.Count} 处：");
            sb.AppendLine();
            foreach (var x in c.List)
                sb.AppendLine($"- `{Esc(x.Id)}`{IsNew(x)}：{Redact(x.Before)} → **{Redact(x.After)}**");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string Disclaimer(string[] lines) =>
            "## 4. 免责声明\n\n"
            + string.Join("\n", lines.Select(l => "- " + l))
            + "\n\n本报告由 VDHelper 自动生成，检测项定义见 `docs/checks.md`，"
            + "症状类划分与用户原话见 `research/09-failure-corpus/01-symptom-corpus.md`。\n";
    }

    // ------------------------------------------------------------------ HTML dialect

    private static class HtmlDialect
    {
        private const string Css = """
            :root{--pass:#2E7D32;--warn:#B26A00;--block:#C62828;--unknown:#6A737C;--fg:#1b1f23;--muted:#5b646d;--line:#d8dee4;--card:#f6f8fa}
            *{box-sizing:border-box}
            body{margin:0;padding:32px 20px;background:#fff;color:var(--fg);font:15px/1.75 "Segoe UI","Microsoft YaHei",system-ui,sans-serif}
            main{max-width:980px;margin:0 auto}
            h1{font-size:26px;margin:0 0 4px}
            h2{font-size:20px;margin:32px 0 10px;padding-bottom:6px;border-bottom:2px solid var(--line)}
            h3{font-size:17px;margin:26px 0 8px}
            h4{font-size:15px;margin:20px 0 6px}
            .lead{color:var(--muted);margin:0 0 18px}
            .verdict{border-left:5px solid var(--unknown);background:var(--card);padding:14px 18px;border-radius:0 6px 6px 0}
            .verdict .say{display:block;margin-top:6px}
            dl.meta{display:grid;grid-template-columns:auto 1fr;gap:2px 14px;margin:14px 0 0;font-size:14px}
            dl.meta dt{color:var(--muted)}dl.meta dd{margin:0}
            code{background:var(--card);padding:1px 5px;border-radius:4px;font:13px/1.5 Consolas,monospace}
            .badge{display:inline-block;padding:1px 10px;border-radius:11px;color:#fff;font-size:13px;font-weight:600}
            .b-pass{background:var(--pass)}.b-warn{background:var(--warn)}.b-block{background:var(--block)}.b-unknown{background:var(--unknown)}
            .sum{margin:4px 0 8px}
            .count{color:var(--muted);font-weight:400;font-size:14px}
            details{margin:8px 0 12px;border:1px solid var(--line);border-radius:6px;background:var(--card)}
            details>summary{cursor:pointer;padding:7px 12px;font-size:14px;color:var(--muted);user-select:none}
            details[open]>summary{border-bottom:1px solid var(--line)}
            pre{margin:0;padding:12px;overflow-x:auto;white-space:pre-wrap;word-break:break-word;font:13px/1.65 Consolas,"Microsoft YaHei",monospace}
            .note{color:var(--muted);font-size:13.5px;border-left:3px solid var(--line);padding-left:10px;margin:8px 0}
            .why{background:var(--card);border-radius:6px;padding:10px 14px;margin:8px 0}
            .why>b{color:var(--muted);font-weight:600;margin-right:6px}
            ul{margin:6px 0 10px;padding-left:22px}
            .dis li{margin-bottom:8px}
            footer{margin-top:34px;padding-top:14px;border-top:1px solid var(--line);color:var(--muted);font-size:13px}

            """;

        private static string E(string s) => WebUtility.HtmlEncode(s);

        /// <summary>
        /// Escapes, then renders the small Markdown subset that check text actually uses:
        /// **bold**, `code`, and blank-line paragraph breaks.
        /// <para>
        /// The HTML report previously escaped only, so every "**为什么**" line in a shareable
        /// report shipped to a GitHub issue showed raw asterisks — while the CSS right next to it
        /// styled a &lt;b&gt; that was never emitted. Order matters: escape first, then substitute,
        /// so a literal &lt; in the text can never become markup.
        /// </para>
        /// </summary>
        private static string Em(string s)
        {
            var h = WebUtility.HtmlEncode(s ?? "");
            h = System.Text.RegularExpressions.Regex.Replace(h, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            h = System.Text.RegularExpressions.Regex.Replace(h, @"`([^`]+)`", "<code>$1</code>");
            return h.Replace("\r\n", "\n").Replace("\n\n", "</p><p>").Replace("\n", "<br>");
        }

        public static string Head(Meta m) => $"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>VDHelper 体检报告</title>
            <style>{Css}</style>
            </head>
            <body><main>
            <h1>VDHelper 体检报告</h1>
            <p class="lead">由 <code>VdHelper.exe --report-html &lt;file&gt;{UsageSuffix(m)}</code> 生成：单文件、内联样式、UTF-8，可直接贴到社区求助或 GitHub issue。原始输出默认折叠。脱敏口径同 Markdown 版——不写任何 DPAPI 密文、令牌、账户条目内容，机器名 / 用户名 / 局域网地址保留。</p>
            <h2 id="verdict">1. 结论</h2>
            <div class="verdict" style="border-left-color:var(--{VerdictClass(m.Verdict)})"><span class="badge b-{VerdictClass(m.Verdict)}">{E(m.Word)}</span><span class="say">{E(m.Text)}</span></div>
            <dl class="meta">
            <dt>生成时间</dt><dd>{m.At:yyyy-MM-dd HH:mm:ss}</dd>
            <dt>机器名</dt><dd><code>{E(m.Machine)}</code></dd>
            <dt>用户名</dt><dd><code>{E(m.User)}</code></dd>
            <dt>系统</dt><dd>{E(m.Os)}</dd>
            <dt>串流会话</dt><dd>{E(Live(m))}</dd>
            <dt>统计</dt><dd>{E(Stats(m))}</dd>
            </dl>

            """;

        public static string Checks(Intro intro)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<h2 id=\"checks\">2. 检测项</h2>");
            sb.AppendLine($"<p>{E(intro.Lead)}</p>");
            if (intro.PhraseLine.Length > 0)
                sb.AppendLine("<p class=\"note\"><b>用户原话</b>：" + E(intro.PhraseLine)
                    + "<br><b>先看这里</b>：" + E(intro.FirstLook) + "</p>");
            sb.AppendLine(intro.Grouped
                ? "<p class=\"note\">未指定症状类，因此按症状类分组。同一检测项会在多个症状类下重复出现，因为多个症状共用它；"
                    + "编号是它在整轮体检里的固定位置，与 <code>docs/checks.md</code> 的顺序一致。</p>"
                : $"<p class=\"note\">本症状类列出 {intro.Shown} 项相关检测，已隐藏 {intro.Hidden} 项无关检测。</p>");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string Group(Group g)
        {
            var sb = new StringBuilder();
            var head = g.IsOverflow ? g.Title : $"{g.ClassId}「{g.Title}」";
            sb.AppendLine($"<h3>{E(head)} <span class=\"count\">（{g.Results.Count} 项）</span></h3>");
            if (g.PhraseLine is not null)
                sb.AppendLine("<p class=\"note\"><b>用户原话</b>：" + E(g.PhraseLine)
                    + "<br><b>先看这里</b>：" + E(g.FirstLook ?? "") + "</p>");
            if (g.NotRun.Count > 0)
                sb.AppendLine($"<p class=\"note\">这一类点名了 {g.NotRun.Count} 项本轮 PC 侧体检没有结果的检测："
                    + string.Join("、", g.NotRun.Select(E)) + "。本报告只跑 PC 侧那一遍（如实列出，不静默略过）；"
                    + "头显侧检测在第三屏，需要 adb 连上头显。</p>");
            return sb.ToString();
        }

        public static string Item(Item item)
        {
            var r = item.Result;
            var cls = BadgeClass(r.Status);
            var sb = new StringBuilder();
            var title = item.Definition is null ? "" : $" — {E(item.Definition.Title)}";
            sb.AppendLine($"<h4>{item.Number}. <span class=\"badge b-{cls}\">{Badge(r.Status)}</span> <code>{E(r.Id)}</code>{title}</h4>");
            sb.AppendLine($"<p class=\"sum\">{Em(Redact(r.Summary))}</p>");
            if (!string.IsNullOrWhiteSpace(r.Detail))
                sb.AppendLine($"<div class=\"why\"><b>为什么</b><p>{Em(Redact(r.Detail))}</p></div>");
            if (r.Fixes.Count > 0)
            {
                sb.AppendLine("<div class=\"why\"><b>可以做的修复</b><ul>");
                foreach (var f in r.Fixes)
                    sb.AppendLine($"<li><code>{E(f.Id)}</code>（风险 {f.Risk}）{Em(f.Title)} — {Em(Redact(f.What))}"
                        + $"<br>备份：{Em(Redact(f.Backup))}　回滚：{Em(Redact(f.Rollback))}"
                        + (f.NeedsElevation ? "<br>会弹 UAC，需要你点确认。" : "") + "</li>");
                sb.AppendLine("</ul></div>");
            }
            if (!string.IsNullOrWhiteSpace(r.Guidance))
                sb.AppendLine($"<div class=\"why\"><b>只能指引、不能自动做的事</b><p>{Em(Redact(r.Guidance))}</p></div>");
            var evidence = Reportable(r);
            if (evidence.Count > 0)
                sb.AppendLine("<details><summary>原始输出（点击展开）</summary><pre>"
                    + string.Join("\n", evidence.Select(e => $"{E(e.Key)}: {E(e.Value)}"))
                    + "</pre></details>");
            return sb.ToString();
        }

        public static string Tail(Changes c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<h2 id=\"changes\">3. 与上次的变化</h2>");
            if (c.List.Count == 0)
            {
                sb.AppendLine("<p>与上次相比无变化。</p>");
                return sb.ToString();
            }
            sb.AppendLine($"<p>共 {c.List.Count} 处：</p><ul>");
            foreach (var x in c.List)
                sb.AppendLine($"<li><code>{E(x.Id)}</code>{E(IsNew(x))}：{E(Redact(x.Before))} → <b>{E(Redact(x.After))}</b></li>");
            sb.AppendLine("</ul>");
            return sb.ToString();
        }

        public static string Disclaimer(string[] lines) =>
            "<h2 id=\"disclaimer\">4. 免责声明</h2>\n<ul class=\"dis\">\n"
            + string.Join("\n", lines.Select(l => "<li>" + Inline(l) + "</li>"))
            + "\n</ul>\n<footer>本报告由 VDHelper 自动生成。检测项定义见 <code>docs/checks.md</code>，"
            + "症状类划分与用户原话见 <code>research/09-failure-corpus/01-symptom-corpus.md</code>，"
            + "每类判障顺序见 <code>research/06-adb-headset/03-symptom-decision-table.md</code>。</footer>\n"
            + "</main></body>\n</html>\n";

        /// <summary>Escape, then re-mark the <c>**bold**</c> spans the disclaimer lines use.</summary>
        private static string Inline(string s)
        {
            var sb = new StringBuilder();
            var parts = s.Split("**");
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (i % 2 == 1) sb.Append("<b>").Append(E(parts[i])).Append("</b>");
                else sb.Append(E(parts[i]));
            }
            return sb.ToString();
        }
    }
}
