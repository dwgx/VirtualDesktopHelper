using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VirtualDesktopHelper
{
    partial class MainForm
    {
        const string FeedbackMail = "csgowiki@qq.com";
        const string Pkg = "VirtualDesktop.Android";

        TabPage BuildGuideTab()
        {
            var page = new TabPage();
            webWiki = new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = true, ScriptErrorsSuppressed = true };
            page.Controls.Add(webWiki);
            return page;
        }

        TabPage BuildAppTab()
        {
            var page = new TabPage();
            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int r = 0;
            Action<string, Control> row = (lab, ctl) =>
            {
                extra["app." + r] = new Label { Text = lab, AutoSize = true, Anchor = AnchorStyles.Left, Tag = lab };
                g.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                g.Controls.Add((Control)extra["app." + r], 0, r);
                ctl.Dock = DockStyle.Fill;
                g.Controls.Add(ctl, 1, r);
                r++;
            };
            var ver = new Label { AutoSize = true, Text = "VDH " + AppCfg.Version, Anchor = AnchorStyles.Left };
            extra["app.ver"] = ver;
            row("Version", ver);
            chkOta = new CheckBox { AutoSize = true, Checked = cfg.CheckUpdates };
            chkOta.CheckedChanged += (s, e) => { cfg.CheckUpdates = chkOta.Checked; cfg.Save(); };
            row("OTA", chkOta);
            btnOta = new Button { AutoSize = true };
            btnOta.Click += (s, e) => CheckOta(true);
            row("Update", btnOta);
            btnOpenCfg = new Button { AutoSize = true };
            btnOpenCfg.Click += (s, e) =>
            {
                Directory.CreateDirectory(Paths.AppDir);
                Process.Start("explorer.exe", Paths.AppDir);
            };
            row("Config", btnOpenCfg);
            var cfgPath = new TextBox { ReadOnly = true, Text = Paths.AppDir };
            row("Path", cfgPath);
            extra["app.cfgpath"] = cfgPath;
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(g);
            return page;
        }

        TabPage BuildHeadsetTab()
        {
            var page = new TabPage();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lbHeadset = new Label { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            btnHsRefresh = new Button { AutoSize = true };
            btnHsLog = new Button { AutoSize = true };
            btnHsStart = new Button { AutoSize = true };
            btnHsStop = new Button { AutoSize = true };
            btnHsGrant = new Button { AutoSize = true };
            btnHsInstall = new Button { AutoSize = true };
            btnHsFolder = new Button { AutoSize = true };
            btnHsRefresh.Click += (s, e) => RefreshHeadset(true);
            btnHsLog.Click += (s, e) => HeadsetLogcat();
            btnHsStart.Click += (s, e) => HeadsetStart();
            btnHsStop.Click += (s, e) => HeadsetStop();
            btnHsGrant.Click += (s, e) => HeadsetGrant();
            btnHsInstall.Click += (s, e) => HeadsetInstall();
            btnHsFolder.Click += (s, e) => InstallFromFolder();
            bar.Controls.Add(btnHsRefresh);
            bar.Controls.Add(btnHsStart);
            bar.Controls.Add(btnHsStop);
            bar.Controls.Add(btnHsGrant);
            bar.Controls.Add(btnHsLog);
            bar.Controls.Add(btnHsInstall);
            bar.Controls.Add(btnHsFolder);
            txtHeadset = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9f) };
            root.Controls.Add(lbHeadset, 0, 0);
            root.Controls.Add(bar, 0, 1);
            root.Controls.Add(txtHeadset, 0, 2);
            page.Controls.Add(root);
            return page;
        }

        void ApplyTooltips()
        {
            if (tips == null) return;
            tips.SetToolTip(cmbCodec, SettingHelp("codec"));
            tips.SetToolTip(chkPair, SettingHelp("ShowPairingRequests"));
            tips.SetToolTip(chkH264Warn, SettingHelp("ShownH264PlusWarning"));
            tips.SetToolTip(tbDevice, SettingHelp("DeviceName"));
            tips.SetToolTip(tbVideos, SettingHelp("VideosRootPath"));
            tips.SetToolTip(tbLast, SettingHelp("LastConnectDate"));
            tips.SetToolTip(numMon, SettingHelp("MonitorCount"));
            tips.SetToolTip(tbWarn, SettingHelp("DontWarnApps"));
            tips.SetToolTip(numRot, SettingHelp("ServerRotation"));
            tips.SetToolTip(tbBitrate, SettingHelp("bitrate"));
        }

        string SettingHelp(string key)
        {
            switch (key)
            {
                case "codec":
                    return L.T(
                        "Codec (PreferredCodec + CodecName, one setting)\r\n\r\n" +
                        "H.264 (0) — widest compatibility, higher bitrate for the same look.\r\n" +
                        "H.264+ (5) — raises the usable bitrate ceiling. Can add lag, black frames, extra latency. Only with a dedicated router.\r\n" +
                        "HEVC 10-bit (1) — better quality per megabit. Needs a GPU that encodes HEVC 10-bit. AMD 24.1–24.2 desktop freeze warning.\r\n" +
                        "AV1 10-bit (2) — newest, best compression if the GPU encodes AV1.\r\n\r\n" +
                        "The headset still picks the stream. This JSON value is what Streamer offers. Restart Streamer after save.",
                        "编码（PreferredCodec 和 CodecName 是同一项）\r\n\r\n" +
                        "H.264（0）— 兼容最好，同样画质更吃码率。\r\n" +
                        "H.264+（5）— 能把可用码率顶上去。可能卡顿、黑屏、多延迟。只建议独立路由。\r\n" +
                        "HEVC 10-bit（1）— 同码率更清晰。显卡要能编 HEVC 10-bit。AMD 24.1–24.2 桌面串流会冻。\r\n" +
                        "AV1 10-bit（2）— 最新，压缩最好，显卡要能编 AV1。\r\n\r\n" +
                        "头显仍会按自己的滑条选码率。这里是 Streamer 提供的编码。保存后请重启串流端。");
                case "ShowPairingRequests":
                    return L.T(
                        "Show pairing requests\r\n\r\nWhen on, Streamer pops a prompt if a headset asks to pair. When off, pairing UI is suppressed. LAN discovery with a matching account name still works without this popup.",
                        "显示配对请求\r\n\r\n打开后，头显请求配对时 Streamer 会弹窗。关掉则不弹。账户名对得上时，局域网发现仍然能连，不依赖这个弹窗。");
                case "ShownH264PlusWarning":
                    return L.T(
                        "H.264+ warning shown\r\n\r\nStreamer remembers that you already dismissed the H.264+ caution. Set to off to see the warning again the next time you pick H.264+.",
                        "已显示 H.264+ 警告\r\n\r\nStreamer 记下你已经看过 H.264+ 风险提示。改成关，下次选 H.264+ 会再弹一次。");
                case "DeviceName":
                    return L.T(
                        "Device name\r\n\r\nLabel shown for this PC in the headset computer list (for example Meta Quest 3). Cosmetic. Does not change pairing.",
                        "设备名称\r\n\r\n头显电脑列表里这一台 PC 的显示名（例如 Meta Quest 3）。只影响显示，不改配对。");
                case "VideosRootPath":
                    return L.T(
                        "Videos folder\r\n\r\nWhere Streamer writes captured videos. Must be a writable folder on this PC.",
                        "视频目录\r\n\r\nStreamer 保存录像的文件夹。必须是本机可写路径。");
                case "LastConnectDate":
                    return L.T(
                        "Last connect date\r\n\r\nWritten by Streamer when a session connects. Read-only here so we do not fake a calendar.",
                        "上次连接日期\r\n\r\nStreamer 在连上时自己写。这里只读，避免把日期改乱。");
                case "MonitorCount":
                    return L.T(
                        "Monitor count\r\n\r\nHow many virtual / captured displays Streamer exposes. 1 is the usual desktop. Raising it does not create extra physical monitors.",
                        "显示器数量\r\n\r\nStreamer 向外提供几个桌面。一般是 1。加大不会变出新的物理显示器。");
                case "DontWarnApps":
                    return L.T(
                        "Apps not to warn\r\n\r\nComma-separated executable names that Streamer should not nag about (anti-cheat / capture overlays). Empty is fine.",
                        "不再警告的应用\r\n\r\n逗号分隔的进程名，Streamer 不再为它们弹捕获/反作弊提示。可以留空。");
                case "ServerRotation":
                    return L.T(
                        "Server rotation\r\n\r\nInternal counter Streamer bumps when talking to cloud registry endpoints. Safe to leave alone. The LAN patch does not need cloud.",
                        "服务器轮换\r\n\r\nStreamer 访问云端注册时用的计数。一般不用动。离线 LAN 补丁不依赖云。");
                case "bitrate":
                    return L.T(
                        "Headset bitrate (optional)\r\n\r\nStock Quest slider max is 500 Mbps. Our frozen 1.34.22.0 APK already streams without raising it. Write IL only if you are rebuilding the APK from this repo.",
                        "头显码率（可选）\r\n\r\n原版滑条上限 500 Mbps。冻结的 1.34.22.0 能用，不必改这个。只有你要从本仓库重打包 APK 时才写 IL。");
                default:
                    return "";
            }
        }

        string GuideBody()
        {
            return L.T(
                "Virtual Desktop — what this pack actually is\r\n\r\n" +
                "Quest APK 1.34.22.0 (C2acked by dwgx) streams the Windows desktop over LAN. " +
                "Pairing key is the account name baked into the APK (dwgx). PC Streamer → Accounts → Add → Meta → dwgx.\r\n" +
                "install_zh.bat is a Chinese installer script. It does NOT inject CJK fonts into the APK. " +
                "Headset UI stays English until a separate ZH APK (font + strings) is built.\r\n" +
                "HorizonOS dialog: Open app. Never Restore.\r\n\r\n" +
                "Codecs (Streamer dropdown = PreferredCodec + CodecName)\r\n" +
                "  H.264 / H.264+ / HEVC 10-bit / AV1 10-bit — see the help pane on Settings.\r\n\r\n" +
                "Settings on the first tab\r\n" +
                "  Show pairing requests — Streamer popup when a headset wants to pair.\r\n" +
                "  H.264+ warning shown — whether the caution was already dismissed.\r\n" +
                "  Device name — label in the headset PC list.\r\n" +
                "  Videos folder — capture output path.\r\n" +
                "  Last connect date — written by Streamer, read-only.\r\n" +
                "  Monitor count — virtual displays Streamer exposes.\r\n" +
                "  Apps not to warn — skip nag list.\r\n" +
                "  Server rotation — cloud counter; leave it.\r\n\r\n" +
                "Headset tab talks to adb (adb\\adb.exe next to VDH, or the repo quest_adb_tools). " +
                "Detect / start / stop / grant / logcat / install. Feedback mails a diagnostic dump to csgowiki@qq.com.\r\n",
                "Virtual Desktop — 这份包实际做什么\r\n\r\n" +
                "Quest 补丁 APK 1.34.22.0（C2acked by dwgx）走局域网串流 Windows 桌面。" +
                "配对钥匙是烤进 APK 的账户名 dwgx。电脑 Streamer → 账户 → 添加 → Meta → dwgx。\r\n" +
                "install_zh.bat 是中文安装脚本，不会把头显界面汉化进去。" +
                "头显 UI 仍是英文，直到另打带 CJK 字体的汉化 APK。\r\n" +
                "HorizonOS 对话框：打开应用。不要点恢复。\r\n\r\n" +
                "编码（Streamer 下拉 = PreferredCodec + CodecName）\r\n" +
                "  H.264 / H.264+ / HEVC 10-bit / AV1 10-bit — 点设置页右侧说明。\r\n\r\n" +
                "设置页每一项\r\n" +
                "  显示配对请求 — 头显要配对时 Streamer 是否弹窗。\r\n" +
                "  已显示 H.264+ 警告 — 是否已经看过风险提示。\r\n" +
                "  设备名称 — 头显电脑列表上的名字。\r\n" +
                "  视频目录 — 录像保存路径。\r\n" +
                "  上次连接日期 — Streamer 自己写，只读。\r\n" +
                "  显示器数量 — Streamer 提供几个桌面。\r\n" +
                "  不再警告的应用 — 不再弹提示的进程名。\r\n" +
                "  服务器轮换 — 云端计数，一般不用动。\r\n\r\n" +
                "头显页用 adb（VDH 旁边的 adb\\adb.exe，或仓库 quest_adb_tools）。" +
                "可检测、启动、停止、授权、日志、安装。反馈会把诊断寄到 csgowiki@qq.com。\r\n");
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        string FindAdb()
        {
            var dir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            var cands = new[]
            {
                Path.Combine(dir, "adb", "adb.exe"),
                Path.GetFullPath(Path.Combine(dir, @"..\analysis\apk_patch\quest_adb_tools\dist\adb.exe")),
                Path.GetFullPath(Path.Combine(dir, @"..\..\analysis\apk_patch\quest_adb_tools\dist\adb.exe")),
            };
            foreach (var p in cands) if (File.Exists(p)) return p;
            return "adb";
        }

        string RunAdb(string args, int timeoutMs)
        {
            var adb = FindAdb();
            var psi = new ProcessStartInfo(adb, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "failed to start adb";
                    var o = p.StandardOutput.ReadToEnd();
                    var e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return o + e + "\r\n[timeout]";
                    }
                    return (o + e).Trim();
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        void RefreshHeadset(bool verbose)
        {
            var dev = RunAdb("devices -l", 15000);
            var connected = dev.IndexOf("\tdevice", StringComparison.Ordinal) >= 0
                         || dev.IndexOf(" device ", StringComparison.Ordinal) >= 0;
            var unauth = dev.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0;
            var offline = dev.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0;
            string status;
            if (connected) status = L.T("Headset connected (adb device).", "头显已连接（adb device）。");
            else if (unauth) status = L.T("Headset unauthorized — allow USB debugging in the headset.", "头显未授权 — 在头显里允许 USB 调试。");
            else if (offline) status = L.T("Headset offline — replug USB.", "头显离线 — 拔插数据线。");
            else status = L.T("No headset on adb. Plug USB, wake the Quest, allow debugging.", "adb 没有头显。插线、唤醒 Quest、允许调试。");
            if (lbHeadset != null) lbHeadset.Text = status;
            if (!verbose && txtHeadset == null) return;
            var pkg = connected ? RunAdb("shell dumpsys package " + Pkg, 20000) : "";
            if (pkg.Length > 2500) pkg = pkg.Substring(0, 2500) + "\r\n…";
            if (txtHeadset != null)
                txtHeadset.Text = "adb: " + FindAdb() + "\r\n" + dev + "\r\n\r\n" + pkg;
        }

        void HeadsetLogcat()
        {
            txtHeadset.Text = RunAdb("logcat -d -t 120 -s Unity:I *:E", 20000);
        }

        void HeadsetStart()
        {
            txtHeadset.Text = RunAdb("shell am start -n " + Pkg + "/md59102214312e19799944a61bf7bc2f23e.VrActivity", 15000);
            var alt = RunAdb("shell monkey -p " + Pkg + " -c android.intent.category.LAUNCHER 1", 15000);
            txtHeadset.AppendText("\r\n" + alt);
        }

        void HeadsetStop()
        {
            txtHeadset.Text = RunAdb("shell am force-stop " + Pkg, 15000);
        }

        void HeadsetGrant()
        {
            var perms = new[]
            {
                "android.permission.RECORD_AUDIO",
                "android.permission.CAMERA",
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.ACCESS_WIFI_STATE",
                "android.permission.ACCESS_NETWORK_STATE",
                "android.permission.INTERNET",
                "android.permission.WAKE_LOCK",
                "android.permission.BLUETOOTH",
                "android.permission.BLUETOOTH_CONNECT"
            };
            var sb = new StringBuilder();
            foreach (var p in perms)
                sb.AppendLine(RunAdb("shell pm grant " + Pkg + " " + p, 10000));
            txtHeadset.Text = sb.ToString();
        }

        void HeadsetInstall()
        {
            var dir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            string apk = null;
            foreach (var n in new[]
            {
                Path.Combine(dir, "VirtualDesktop_1.34.22.0_patched.apk"),
                Path.Combine(dir, "signed_v22.apk"),
                Path.GetFullPath(Path.Combine(dir, @"..\analysis\apk_patch\output\signed_v22.apk")),
            })
                if (File.Exists(n)) { apk = n; break; }
            if (apk == null)
            {
                txtHeadset.Text = L.T("No patched APK next to VDH.", "VDH 旁边没有补丁 APK。");
                return;
            }
            if (MessageBox.Show(this,
                L.T("Install " + Path.GetFileName(apk) + " ? ~1 GB.", "安装 " + Path.GetFileName(apk) + " ？大约 1 GB。"),
                "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            txtHeadset.Text = L.T("Installing… keep USB plugged.", "正在安装…不要拔线。") + "\r\n" + apk + "\r\n";
            Application.DoEvents();
            txtHeadset.AppendText(RunAdb("install -r -g \"" + apk + "\"", 600000));
        }

        string DiagnosticDump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("VDH diagnostic");
            sb.AppendLine("time " + DateTime.Now.ToString("o"));
            sb.AppendLine("streamer " + DetectStreamerVer());
            sb.AppendLine("settings " + Paths.Settings);
            try
            {
                if (File.Exists(Paths.Settings))
                    sb.AppendLine(File.ReadAllText(Paths.Settings, Encoding.UTF8));
            }
            catch (Exception ex) { sb.AppendLine(ex.Message); }
            sb.AppendLine("--- adb ---");
            sb.AppendLine(RunAdb("devices -l", 15000));
            return sb.ToString();
        }

        void LoadWiki()
        {
            if (webWiki == null) return;
            webWiki.DocumentText = WikiHtml();
        }

        string WikiHtml()
        {
            var css = "body{font-family:'Segoe UI',sans-serif;margin:0;background:#f6f7fb;color:#1b1f2a;}"
                + "main{max-width:720px;margin:0 auto;padding:28px 22px 60px;}"
                + "h1{font-size:26px;margin:0 0 8px;} .muted{color:#667085;font-size:13px;margin-bottom:28px;}"
                + "h2{font-size:18px;border-bottom:1px solid #e6e8ee;padding-bottom:6px;margin:28px 0 10px;}"
                + "p,li{line-height:1.65;font-size:14.5px;} code{background:#eef1f8;padding:1px 6px;border-radius:4px;font-family:Consolas,monospace;font-size:13px;}"
                + "section{background:#fff;border:1px solid #e6e8ee;border-radius:12px;padding:16px 18px;margin:12px 0;}"
                + "table{width:100%;border-collapse:collapse;font-size:13.5px;} td,th{text-align:left;padding:8px;border-bottom:1px solid #eceff5;}"
                + "th{color:#3b6dff;} .note{background:#fff8e8;border:1px solid #f0e0a8;border-radius:10px;padding:12px 14px;}";
            if (L.Zh)
            {
                return "<!DOCTYPE html><html><head><meta charset=utf-8><style>" + css + "</style></head><body><main>"
                    + "<h1>Virtual Desktop 指南</h1><p class=muted>VDH " + AppCfg.Version + " · 作者 dwgx · 反馈 csgowiki@qq.com</p>"
                    + "<section><h2>这套东西分三份</h2><p><b>EN APK</b> / <b>ZH APK</b> 只装头显，zip 里没有 VDH。<b>VDH.exe</b> 是电脑端工具，单独更新。</p>"
                    + "<p><code>install_zh.bat</code> 只是中文安装提示，不会把菜单翻译进 APK。CJK 汉化要单独的中文包。</p></section>"
                    + "<section><h2>怎么连上电脑</h2><ol><li>Streamer 1.34.22.0，账户 → 添加 → Meta → <code>dwgx</code>（必须和 APK 烤名一致）。</li>"
                    + "<li>电脑和头显同一路由，关 AP 隔离。</li><li>先开 Streamer，再开头显 VD。</li>"
                    + "<li>HorizonOS「恢复应用」只点「打开应用」，不要点「恢复」。</li></ol></section>"
                    + "<section><h2>编码</h2><p>主页下拉同时写 <code>PreferredCodec</code> 和 <code>CodecName</code>，是同一项。</p>"
                    + "<table><tr><th>值</th><th>名称</th><th>说明</th></tr>"
                    + "<tr><td>0</td><td>H.264</td><td>兼容最好，同样画质更吃码率。</td></tr>"
                    + "<tr><td>5</td><td>H.264+</td><td>可用码率更高。可能卡顿、黑屏、多延迟。只建议独立路由。</td></tr>"
                    + "<tr><td>1</td><td>HEVC 10-bit</td><td>同码率更清晰。显卡要能编 HEVC 10-bit。AMD 24.1–24.2 桌面串流会冻。</td></tr>"
                    + "<tr><td>2</td><td>AV1 10-bit</td><td>压缩最好，显卡要能编 AV1。</td></tr></table>"
                    + "<p>头显滑条仍决定码率。Streamer JSON <b>没有</b> MaxBitrate 字段。上限在 Quest APK 的 IL（原版 500，本冻结包 960）。保存后请重启串流端。</p></section>"
                    + "<section><h2>主页上那些键</h2><table>"
                    + "<tr><td>显示配对请求</td><td>头显要配对时 Streamer 是否弹窗。局域网按账户名发现不依赖弹窗。</td></tr>"
                    + "<tr><td>已显示 H.264+ 警告</td><td>是否已经看过风险提示。关掉会再弹。</td></tr>"
                    + "<tr><td>设备名称</td><td>头显电脑列表上的显示名。</td></tr>"
                    + "<tr><td>视频目录</td><td>录像保存路径。</td></tr>"
                    + "<tr><td>上次连接日期</td><td>Streamer 自己写，只读。</td></tr>"
                    + "<tr><td>显示器数量</td><td>Streamer 向外提供几个桌面。</td></tr>"
                    + "<tr><td>不再警告的应用</td><td>逗号分隔的进程名。</td></tr>"
                    + "<tr><td>服务器轮换</td><td>云端计数。离线 LAN 不用动。</td></tr></table></section>"
                    + "<div class=note>配置目录：%AppData%\\VirtualDesktopHelper 。本软件页可以打开。检查更新只请求 GitHub，下载后核对 SHA-256，不会跟你填的网址走。</div>"
                    + "</main></body></html>";
            }
            return "<!DOCTYPE html><html><head><meta charset=utf-8><style>" + css + "</style></head><body><main>"
                + "<h1>Virtual Desktop guide</h1><p class=muted>VDH " + AppCfg.Version + " · dwgx · csgowiki@qq.com</p>"
                + "<section><h2>Three artefacts</h2><p><b>EN APK</b> / <b>ZH APK</b> are headset-only zips — no VDH inside. <b>VDH.exe</b> is the Windows tool and updates on its own.</p>"
                + "<p><code>install_zh.bat</code> is a Chinese installer script. It does not inject CJK into the APK.</p></section>"
                + "<section><h2>Pairing</h2><ol><li>Streamer 1.34.22.0 → Accounts → Add → Meta → <code>dwgx</code>.</li>"
                + "<li>Same router, AP isolation off.</li><li>Start Streamer first.</li>"
                + "<li>HorizonOS: Open app, never Restore.</li></ol></section>"
                + "<section><h2>Codecs</h2><p>The Home dropdown writes both <code>PreferredCodec</code> and <code>CodecName</code>.</p>"
                + "<table><tr><th>Id</th><th>Name</th><th>Notes</th></tr>"
                + "<tr><td>0</td><td>H.264</td><td>Widest compatibility.</td></tr>"
                + "<tr><td>5</td><td>H.264+</td><td>Higher bitrate ceiling. Lag / black frames possible.</td></tr>"
                + "<tr><td>1</td><td>HEVC 10-bit</td><td>Better quality per Mbit. AMD 24.1–24.2 desktop freeze.</td></tr>"
                + "<tr><td>2</td><td>AV1 10-bit</td><td>Best compression if the GPU encodes AV1.</td></tr></table>"
                + "<p>The headset slider still picks bitrate. Streamer JSON has no MaxBitrate. The cap is IL in the Quest APK (stock 500, this freeze 960).</p></section>"
                + "<section><h2>Home keys</h2><table>"
                + "<tr><td>Show pairing requests</td><td>Streamer popup. LAN match still works without it.</td></tr>"
                + "<tr><td>H.264+ warning shown</td><td>Whether the caution was dismissed.</td></tr>"
                + "<tr><td>Device name</td><td>Label in the headset PC list.</td></tr>"
                + "<tr><td>Videos folder</td><td>Capture path.</td></tr>"
                + "<tr><td>Last connect date</td><td>Written by Streamer, read-only.</td></tr>"
                + "<tr><td>Monitor count</td><td>Displays Streamer exposes.</td></tr>"
                + "<tr><td>Apps not to warn</td><td>Comma list.</td></tr>"
                + "<tr><td>Server rotation</td><td>Cloud counter. Leave it for LAN.</td></tr></table></section>"
                + "<div class=note>Config: %AppData%\\VirtualDesktopHelper. Updates hit GitHub only and verify SHA-256.</div>"
                + "</main></body></html>";
        }

        void RefreshBitrateLabel()
        {
            int cap = cfg.LastBitrate;
            if (cap <= 0) cap = ReadRepoBitrate();
            if (tbBitrate != null && (tbBitrate.Text == "" || tbBitrate.Text == "500" || tbBitrate.Text == "960"))
                tbBitrate.Text = cap > 0 ? cap.ToString() : "500";
            if (lbBitrate != null)
            {
                var sha = cfg.LastApkSha;
                string extraSha = string.IsNullOrEmpty(sha) ? "" : "  SHA " + sha.Substring(0, 8) + "…";
                lbBitrate.Text = L.T("Headset bitrate cap (APK IL)", "头显码率上限（APK IL）") + extraSha;
            }
        }

        int ReadRepoBitrate()
        {
            var dll = Paths.RepoMobile;
            if (!File.Exists(dll)) return 0;
            var raw = File.ReadAllBytes(dll);
            var hit = Catalog.ByMobileSize(raw.Length);
            if (hit == null || hit.BitrateImm == null || hit.BitrateImm.Length == 0) return 0;
            int v = BitConverter.ToInt32(raw, hit.BitrateImm[0]);
            cfg.LastBitrate = v;
            return v;
        }

        static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        void InstallFromFolder()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = L.T("Folder with our APK", "选择含有我们 APK 的文件夹");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var files = Directory.GetFiles(d.SelectedPath, "*.apk");
                if (files.Length == 0)
                {
                    MessageBox.Show(this, L.T("No .apk in that folder.", "这个文件夹里没有 apk。"));
                    return;
                }
                var sb = new StringBuilder();
                string pick = null;
                bool warn = false;
                foreach (var f in files)
                {
                    string hash;
                    try { hash = Sha256File(f); }
                    catch (Exception ex) { sb.AppendLine(Path.GetFileName(f) + " " + ex.Message); continue; }
                    string name;
                    bool known = Catalog.KnownApk.TryGetValue(hash, out name);
                    sb.AppendLine((known ? "[ok] " : "[?] ") + Path.GetFileName(f) + "  " + hash);
                    sb.AppendLine("    " + (known ? name : L.T("not in our SHA list", "不是我们登记的 SHA")));
                    if (known && pick == null) pick = f;
                    if (known && name.IndexOf("superseded", StringComparison.OrdinalIgnoreCase) >= 0) warn = true;
                    if (known)
                    {
                        cfg.LastApkSha = hash;
                        if (hash.StartsWith("8DEEF4FF", StringComparison.OrdinalIgnoreCase)
                            || hash.StartsWith("A9BE37D9", StringComparison.OrdinalIgnoreCase))
                            cfg.LastBitrate = 960;
                        cfg.Save();
                    }
                }
                txtHeadset.Text = sb.ToString();
                RefreshBitrateLabel();
                if (pick == null)
                {
                    MessageBox.Show(this,
                        L.T("No recognized SHA. Refusing to install an unknown APK.",
                            "没有认出我们的 SHA。不会安装来路不明的 APK。") + "\r\n\r\n" + sb);
                    return;
                }
                if (warn && MessageBox.Show(this,
                    L.T("This SHA is an older build we superseded. Install anyway?",
                        "这是已经淘汰的旧包。仍要装吗？"),
                    "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                if (MessageBox.Show(this,
                    L.T("Install recognized APK " + Path.GetFileName(pick) + " ?",
                        "安装已识别的 APK " + Path.GetFileName(pick) + " ？"),
                    "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                txtHeadset.AppendText("\r\n" + RunAdb("install -r -g \"" + pick + "\"", 600000));
            }
        }

        const string OtaApi = "https://api.github.com/repos/dwgx/VirtualDesktopHelper/releases/latest";

        static bool HostAllowed(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;
            var h = u.Host.ToLowerInvariant();
            return h == "api.github.com"
                || h == "github.com"
                || h == "objects.githubusercontent.com"
                || h == "release-assets.githubusercontent.com"
                || h.EndsWith(".githubusercontent.com");
        }

        string HttpGet(string url, int timeoutMs)
        {
            if (!HostAllowed(url)) throw new InvalidOperationException("blocked host " + url);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "VirtualDesktopHelper/" + AppCfg.Version;
            req.AllowAutoRedirect = false;
            req.Timeout = timeoutMs;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400)
                {
                    var loc = resp.Headers["Location"];
                    if (!HostAllowed(loc)) throw new InvalidOperationException("blocked redirect " + loc);
                    return HttpGet(loc, timeoutMs);
                }
                using (var s = resp.GetResponseStream())
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }

        void HttpDownload(string url, string dest, int timeoutMs)
        {
            if (!HostAllowed(url)) throw new InvalidOperationException("blocked host " + url);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "VirtualDesktopHelper/" + AppCfg.Version;
            req.AllowAutoRedirect = false;
            req.Timeout = timeoutMs;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400)
                {
                    var loc = resp.Headers["Location"];
                    if (!HostAllowed(loc)) throw new InvalidOperationException("blocked redirect " + loc);
                    HttpDownload(loc, dest, timeoutMs);
                    return;
                }
                using (var s = resp.GetResponseStream())
                using (var f = File.Create(dest))
                    s.CopyTo(f);
            }
        }

        void CheckOta(bool interactive)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var json = HttpGet(OtaApi, 20000);
                var ser = new JavaScriptSerializer();
                var map = ser.Deserialize<Dictionary<string, object>>(json);
                var tag = Convert.ToString(map.ContainsKey("tag_name") ? map["tag_name"] : "");
                if (string.IsNullOrEmpty(tag))
                {
                    if (interactive) MessageBox.Show(this, L.T("No release tag.", "没有 Release。"));
                    return;
                }
                var local = AppCfg.Version;
                if (string.Compare(tag.TrimStart('v'), local, StringComparison.OrdinalIgnoreCase) <= 0)
                {
                    if (interactive)
                        MessageBox.Show(this, L.T("Already current: " + local, "已是当前版本：" + local));
                    return;
                }
                string exeUrl = null, sumUrl = null;
                var assets = map["assets"] as System.Collections.ArrayList;
                if (assets != null)
                {
                    foreach (Dictionary<string, object> a in assets)
                    {
                        var n = Convert.ToString(a["name"]);
                        var u = Convert.ToString(a["browser_download_url"]);
                        if (!HostAllowed(u)) continue;
                        if (n.Equals("VDH.exe", StringComparison.OrdinalIgnoreCase)) exeUrl = u;
                        if (n.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumUrl = u;
                    }
                }
                if (exeUrl == null || sumUrl == null)
                {
                    if (interactive) MessageBox.Show(this, L.T("Release missing VDH.exe or SHA256SUMS.txt.", "Release 缺少 VDH.exe 或 SHA256SUMS.txt。"));
                    return;
                }
                if (MessageBox.Show(this,
                    L.T("Update " + local + " → " + tag + " from GitHub?\nSHA-256 will be checked.",
                        "从 GitHub 更新 " + local + " → " + tag + " ？\n会核对 SHA-256。"),
                    "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var tmp = Path.Combine(Path.GetTempPath(), "VDH-" + tag + ".exe");
                var sums = HttpGet(sumUrl, 20000);
                HttpDownload(exeUrl, tmp, 120000);
                var got = Sha256File(tmp);
                if (sums.IndexOf(got, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    File.Delete(tmp);
                    MessageBox.Show(this, L.T("SHA-256 mismatch. Update aborted.", "SHA-256 对不上。已中止。") + "\r\n" + got);
                    return;
                }
                var bat = Path.Combine(Path.GetTempPath(), "vdh-swap.bat");
                var self = Application.ExecutablePath;
                File.WriteAllText(bat,
                    "@echo off\r\nping 127.0.0.1 -n 2 >nul\r\ncopy /y \"" + tmp + "\" \"" + self + "\"\r\nstart \"\" \"" + self + "\"\r\n",
                    Encoding.ASCII);
                Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                if (interactive)
                    MessageBox.Show(this, L.T("Update check failed: ", "检查更新失败：") + ex.Message);
            }
        }

        void SendFeedback()
        {
            var dump = DiagnosticDump();
            var tmp = Path.Combine(Path.GetTempPath(), "vdh-feedback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(tmp, dump, Encoding.UTF8);
            try { Clipboard.SetText(dump); } catch { }
            var sub = Uri.EscapeDataString("VDH feedback " + DateTime.Now.ToString("yyyy-MM-dd"));
            var body = Uri.EscapeDataString(
                "Attach " + tmp + "\r\n\r\n" +
                (dump.Length > 1600 ? dump.Substring(0, 1600) + "\r\n…" : dump));
            OpenUrl("mailto:" + FeedbackMail + "?subject=" + sub + "&body=" + body);
            MessageBox.Show(this,
                L.T("Report copied. Mail to " + FeedbackMail + "\r\nSaved: " + tmp,
                    "诊断已复制。请发到 " + FeedbackMail + "\r\n已保存：" + tmp));
        }
    }
}
