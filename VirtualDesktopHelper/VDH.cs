using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VirtualDesktopHelper
{
    static class L
    {
        public static bool Zh;
        public static string T(string en, string zh) { return Zh ? zh : en; }
    }

    static class Catalog
    {
        public class Line
        {
            public string Version;
            public int MobileSize;
            public int[] BitrateImm;
        }

        public static readonly Line[] Lines = {
            new Line { Version="1.34.18.0", MobileSize=529408, BitrateImm=new int[0] },
            new Line { Version="1.34.19.0", MobileSize=540672, BitrateImm=new[]{0x1396C,0x14CA5,0x2BC97} },
            new Line { Version="1.34.22.0", MobileSize=544256, BitrateImm=new[]{0x13B0C,0x14E45,0x2BFE3} },
        };

        // Same four Streamer UI items, plus 6/11 if JSON already has them.
        public static readonly Codec[] Codecs = {
            new Codec(0, "H.264", "H.264", "H.264"),
            new Codec(5, "H.264+", "H.264+", "H.264+"),
            new Codec(1, "HEVC 10-bit", "HEVC 10-bit", "HEVC 10-bit"),
            new Codec(2, "AV1 10-bit", "AV1 10-bit", "AV1 10-bit"),
        };

        public static readonly Dictionary<string, string[]> Platforms = new Dictionary<string, string[]>
        {
            { "OculusQuest", new[] { "Meta Quest", "Meta Quest" } },
            { "Oculus", new[] { "Meta (Rift / PC)", "Meta（Rift / PC）" } },
            { "Vive", new[] { "Vive", "Vive" } },
            { "Pico", new[] { "Pico", "Pico" } },
            { "Google", new[] { "Google", "Google" } },
            { "PlayForDream", new[] { "Play For Dream", "Play For Dream" } },
            { "Apple", new[] { "Apple Vision", "Apple Vision" } },
            { "Steam", new[] { "Steam", "Steam" } },
        };

        public static readonly HashSet<string> SecretKeys = new HashSet<string> { "Accounts", "ProtectedComputerID" };

        // Frozen / superseded Quest APKs we built. Install only these without warning.
        public static readonly Dictionary<string, string> KnownApk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "B0604A84F9651D90C6D0685DDA8FC2EBA99677946BE0346854078C472D36712A", "1.34.22.0 EN LAN · IL 500 Mbps" },
            { "8DEEF4FF24839FA4244C61C9241485836D8BE0E576CD079512229525295E42DA", "1.34.22.0 EN LAN (960-cap, superseded)" },
            { "39C52DF500E7FAC06AAA00B69D51488E9093CAB863D4BDBC734C6724BC73F09A", "1.34.22.0 ZH LAN · IL 500 Mbps" },
            { "A9BE37D90287C259C8B71EC5A6BA937C69FEAF27F7DB9134CE1313CECD933AF4", "1.34.22.0 EN LAN (pre-watermark, superseded)" },
        };

        public static int CapForSha(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return 0;
            if (sha.StartsWith("B0604A84", StringComparison.OrdinalIgnoreCase)) return 500;
            if (sha.StartsWith("8DEEF4FF", StringComparison.OrdinalIgnoreCase)) return 960;
            if (sha.StartsWith("39C52DF5", StringComparison.OrdinalIgnoreCase)) return 500;
            if (sha.StartsWith("A9BE37D9", StringComparison.OrdinalIgnoreCase)) return 960;
            return 0;
        }

        public static readonly string[] SettingOrder = {
            "ShowPairingRequests","ShownH264PlusWarning","DeviceName",
            "VideosRootPath","LastConnectDate","MonitorCount","DontWarnApps","ServerRotation"
        };

        public static Line ByVersion(string v)
        {
            foreach (var x in Lines) if (x.Version == v) return x;
            return Lines[Lines.Length - 1];
        }

        public static Line ByMobileSize(int n)
        {
            foreach (var x in Lines) if (x.MobileSize == n) return x;
            return null;
        }

        public static string PlatformName(string key)
        {
            string[] p;
            if (!Platforms.TryGetValue(key, out p)) return key;
            return L.T(p[0], p[1]);
        }
    }

    sealed class Codec
    {
        public readonly int Id;
        public readonly string JsonName;
        public readonly string En, Zh;
        public Codec(int id, string json, string en, string zh) { Id = id; JsonName = json; En = en; Zh = zh; }
        public override string ToString() { return L.Zh ? Zh : En; }
    }

    static class Paths
    {
        public static readonly string StreamerExe = @"C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe";
        public static readonly string Settings = @"C:\ProgramData\Virtual Desktop\StreamerSettings.json";
        public static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualDesktopHelper");
        public static string Config { get { return Path.Combine(AppDir, "config.json"); } }
        public static string Factory { get { return Path.Combine(AppDir, "factory.json"); } }
        public static string HistoryDir { get { return Path.Combine(AppDir, "history"); } }
        public static string Changelog
        {
            get
            {
                var d = Path.GetDirectoryName(Application.ExecutablePath);
                return Path.Combine(d ?? ".", "CHANGELOG.md");
            }
        }
        public static string RepoMobile
        {
            get
            {
                var d = Path.GetDirectoryName(Application.ExecutablePath);
                return Path.GetFullPath(Path.Combine(d ?? ".", @"..\analysis\apk_patch\patched_assemblies\VirtualDesktop.Mobile.dll"));
            }
        }
    }

    class AppCfg
    {
        public bool AllowSecrets;
        public string Language;
        public bool CheckUpdates = true;
        public int LastBitrate;
        public string LastApkSha;
        public string AdbPath;
        public const string Version = "0.4.6";
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();
        public static AppCfg Load()
        {
            Directory.CreateDirectory(Paths.AppDir);
            if (!File.Exists(Paths.Config)) return new AppCfg { Language = "auto", CheckUpdates = true };
            try { return Ser.Deserialize<AppCfg>(File.ReadAllText(Paths.Config, Encoding.UTF8)) ?? new AppCfg(); }
            catch { return new AppCfg { Language = "auto", CheckUpdates = true }; }
        }
        public void Save()
        {
            Directory.CreateDirectory(Paths.AppDir);
            File.WriteAllText(Paths.Config, Ser.Serialize(this), Encoding.UTF8);
        }
    }

    partial class MainForm : Form
    {
        readonly AppCfg cfg;
        readonly JavaScriptSerializer ser = new JavaScriptSerializer();
        Dictionary<string, object> data = new Dictionary<string, object>();
        ComboBox cmbLang, cmbCodec, cmbHistory, cmbAddPlat;
        CheckBox chkSecrets, chkPair, chkH264Warn;
        TextBox tbDetect, tbDevice, tbVideos, tbLast, tbWarn, tbBitrate, tbAddName, txtHeadset, txtIlStatus;
        NumericUpDown numMon, numRot;
        ListView lvAcc;
        Label lbCodec, lbDevice, lbVideos, lbLast, lbMon, lbWarn, lbRot, lbAccHint, lbBitrate, lbHeadset;
        Button btnDetect, btnSave, btnFact, btnHist, btnRst, btnBrowse, btnRemove, btnOpenSt, btnCap, btnClearHist, btnFeedback;
        Button btnHsRefresh, btnHsLog, btnHsStart, btnHsStop, btnHsGrant, btnHsInstall, btnHsFolder, btnHsAdb, btnOpenCfg, btnOta, btnPickAdb;
        CheckBox chkOta;
        WebBrowser webWiki;
        LinkLabel lnkGh, lnkMail;
        TabControl tabs;
        ToolTip tips;
        Catalog.Line line;
        readonly Dictionary<string, Control> extra = new Dictionary<string, Control>();

        public MainForm(AppCfg c)
        {
            cfg = c;
            ApplyLang();
            Font = new Font("Segoe UI", 9.5f);
            MinimumSize = new Size(860, 580);
            Size = new Size(940, 640);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            tbDetect = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control };
            header.Controls.Add(tbDetect, 0, 0);
            header.SetRowSpan(tbDetect, 2);
            cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(8, 0, 8, 4) };
            cmbLang.Items.AddRange(new object[] { "Auto", "English", "中文" });
            cmbLang.SelectedIndex = cfg.Language == "en" ? 1 : cfg.Language == "zh" ? 2 : 0;
            cmbLang.SelectedIndexChanged += (s, e) =>
            {
                cfg.Language = new[] { "auto", "en", "zh" }[cmbLang.SelectedIndex];
                cfg.Save();
                ApplyLang();
                RebuildTexts();
            };
            header.Controls.Add(cmbLang, 1, 0);
            btnDetect = new Button { Dock = DockStyle.Fill, Margin = new Padding(0) };
            btnDetect.Click += (s, e) => RefreshDetect();
            header.Controls.Add(btnDetect, 2, 0);
            header.SetRowSpan(btnDetect, 2);
            root.Controls.Add(header, 0, 0);

            tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 200 };
            tabs = new TabControl { Dock = DockStyle.Fill };
            DoubleBuffered = true;
            tabs.TabPages.Add(BuildSettingsTab());
            tabs.TabPages.Add(BuildAccountsTab());
            tabs.TabPages.Add(BuildGuideTab());
            tabs.TabPages.Add(BuildHeadsetTab());
            tabs.TabPages.Add(BuildAppTab());
            tabs.TabPages.Add(BuildAboutTab());
            root.Controls.Add(tabs, 0, 1);

            var south = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            cmbHistory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 8, 8, 0) };
            btnHist = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnSave = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnFact = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnRst = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnClearHist = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnFeedback = new Button { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            btnHist.Click += (s, e) => LoadHistory();
            btnClearHist.Click += (s, e) => ClearHistory();
            btnSave.Click += (s, e) => SaveNow();
            btnFact.Click += (s, e) => RestoreFactory();
            btnRst.Click += (s, e) => RestartStreamer();
            btnFeedback.Click += (s, e) => SendFeedback();
            south.Controls.Add(cmbHistory);
            south.Controls.Add(btnHist);
            south.Controls.Add(btnClearHist);
            south.Controls.Add(btnSave);
            south.Controls.Add(btnFact);
            south.Controls.Add(btnRst);
            south.Controls.Add(btnFeedback);
            root.Controls.Add(south, 0, 2);

            LoadSettingsFile();
            EnsureFactory();
            RefreshDetect();
            FillFromData();
            LoadHistoryList();
            RebuildTexts();
            Shown += (s, e) =>
            {
                if (cfg.CheckUpdates)
                {
                    try { CheckOta(false); } catch { }
                }
            };
        }

        TabPage BuildSettingsTab()
        {
            var page = new TabPage();
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            int r = 0;
            Action<string, Control, Control> row = (key, ctl, extraCtl) =>
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                var lb = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Tag = key };
                extra[key + ".label"] = lb;
                grid.Controls.Add(lb, 0, r);
                ctl.Dock = DockStyle.Fill;
                grid.Controls.Add(ctl, 1, r);
                if (extraCtl != null) grid.Controls.Add(extraCtl, 2, r);
                r++;
            };

            lbCodec = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            cmbCodec = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var c in Catalog.Codecs) cmbCodec.Items.Add(c);
            row("codec", cmbCodec, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 0));
            grid.Controls.Add(lbCodec, 0, 0);

            chkPair = new CheckBox { AutoSize = true, Anchor = AnchorStyles.Left };
            row("ShowPairingRequests", chkPair, null);
            chkH264Warn = new CheckBox { AutoSize = true, Anchor = AnchorStyles.Left };
            row("ShownH264PlusWarning", chkH264Warn, null);

            lbDevice = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            tbDevice = new TextBox { ReadOnly = true, BackColor = Color.FromArgb(245, 245, 245) };
            row("DeviceName", tbDevice, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 3));
            grid.Controls.Add(lbDevice, 0, 3);

            lbVideos = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            tbVideos = new TextBox();
            btnBrowse = new Button { Dock = DockStyle.Fill };
            btnBrowse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.SelectedPath = tbVideos.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) tbVideos.Text = d.SelectedPath;
                }
            };
            row("VideosRootPath", tbVideos, btnBrowse);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 4));
            grid.Controls.Add(lbVideos, 0, 4);

            lbLast = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            tbLast = new TextBox { ReadOnly = true, BackColor = Color.FromArgb(245, 245, 245) };
            row("LastConnectDate", tbLast, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 5));
            grid.Controls.Add(lbLast, 0, 5);

            lbMon = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            numMon = new NumericUpDown { Minimum = 1, Maximum = 8, DecimalPlaces = 0 };
            row("MonitorCount", numMon, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 6));
            grid.Controls.Add(lbMon, 0, 6);

            lbWarn = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            tbWarn = new TextBox();
            row("DontWarnApps", tbWarn, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 7));
            grid.Controls.Add(lbWarn, 0, 7);

            lbRot = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            numRot = new NumericUpDown { Minimum = 0, Maximum = 99, DecimalPlaces = 0 };
            row("ServerRotation", numRot, null);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 8));
            grid.Controls.Add(lbRot, 0, 8);

            lbBitrate = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            tbBitrate = new TextBox { Text = "500" };
            btnCap = new Button { Dock = DockStyle.Fill };
            btnCap.Click += (s, e) => WriteBitrate();
            row("bitrate", tbBitrate, btnCap);
            grid.Controls.Remove(grid.GetControlFromPosition(0, 9));
            grid.Controls.Add(lbBitrate, 0, 9);

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            txtIlStatus = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 245, 245),
                BorderStyle = BorderStyle.FixedSingle
            };
            grid.Controls.Add(txtIlStatus, 0, 10);
            grid.SetColumnSpan(txtIlStatus, 3);

            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(grid);
            return page;
        }

        TabPage BuildAccountsTab()
        {
            var page = new TabPage();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            chkSecrets = new CheckBox { AutoSize = true, Checked = cfg.AllowSecrets };
            chkSecrets.CheckedChanged += (s, e) => { cfg.AllowSecrets = chkSecrets.Checked; cfg.Save(); FillAccounts(); };
            root.Controls.Add(chkSecrets, 0, 0);

            lvAcc = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            lvAcc.Columns.Add("p", 220);
            lvAcc.Columns.Add("v", 480);
            root.Controls.Add(lvAcc, 0, 1);

            lbAccHint = new Label { Dock = DockStyle.Fill };
            root.Controls.Add(lbAccHint, 0, 2);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            cmbAddPlat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Visible = false };
            tbAddName = new TextBox { Width = 160, Visible = false };
            btnRemove = new Button { AutoSize = true };
            btnOpenSt = new Button { AutoSize = true };
            btnRemove.Click += (s, e) => RemoveSelectedAccount();
            btnOpenSt.Click += (s, e) => RestartStreamer();
            bar.Controls.Add(btnRemove);
            bar.Controls.Add(btnOpenSt);
            root.Controls.Add(bar, 0, 3);
            page.Controls.Add(root);
            return page;
        }

        TabPage BuildAboutTab()
        {
            var page = new TabPage();
            var box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var t1 = new Label { AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), Text = "VirtualDesktopHelper  ·  VDH" };
            var t2 = new Label { AutoSize = true, Tag = "author" };
            extra["author"] = t2;
            lnkGh = new LinkLabel { AutoSize = true, Text = "https://github.com/dwgx" };
            lnkGh.LinkClicked += (s, e) => OpenUrl("https://github.com/dwgx");
            lnkMail = new LinkLabel { AutoSize = true, Text = "csgowiki@qq.com" };
            lnkMail.LinkClicked += (s, e) => SendFeedback();
            var log = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, Tag = "log" };
            extra["log"] = log;
            box.Controls.Add(t1, 0, 0);
            box.Controls.Add(t2, 0, 1);
            box.Controls.Add(lnkGh, 0, 2);
            box.Controls.Add(lnkMail, 0, 3);
            box.Controls.Add(log, 0, 4);
            page.Controls.Add(box);
            return page;
        }

        void ApplyLang()
        {
            if (cfg.Language == "en") L.Zh = false;
            else if (cfg.Language == "zh") L.Zh = true;
            else L.Zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.StartsWith("zh");
        }

        void RebuildTexts()
        {
            Text = "VirtualDesktopHelper  ·  VDH";
            btnDetect.Text = L.T("Detect", "识别");
            tabs.TabPages[0].Text = L.T("Home", "主页");
            tabs.TabPages[1].Text = L.T("Accounts", "账户");
            tabs.TabPages[2].Text = L.T("Wiki", "指南");
            tabs.TabPages[3].Text = L.T("Headset", "头显");
            tabs.TabPages[4].Text = L.T("VDH", "本软件");
            tabs.TabPages[5].Text = L.T("About", "关于");
            LoadWiki();
            ApplyTooltips();
            if (lbHeadset != null) RefreshHeadset(false);
            RefreshBitrateLabel();
            if (chkOta != null) chkOta.Checked = cfg.CheckUpdates;
            lbCodec.Text = L.T("Codec", "编码");
            extra["ShowPairingRequests.label"].Text = L.T("Show pairing requests", "显示配对请求");
            extra["ShownH264PlusWarning.label"].Text = L.T("H.264+ warning shown", "已显示 H.264+ 警告");
            lbDevice.Text = L.T("Device name", "设备名称");
            lbVideos.Text = L.T("Videos folder", "视频目录");
            btnBrowse.Text = L.T("Browse", "浏览");
            lbLast.Text = L.T("Last connect date", "上次连接日期");
            lbMon.Text = L.T("Monitor count", "显示器数量");
            lbWarn.Text = L.T("Apps not to warn", "不再警告的应用");
            lbRot.Text = L.T("Server rotation", "服务器轮换");
            lbBitrate.Text = L.T("Headset bitrate (optional)", "头显码率（可选）");
            btnCap.Text = L.T("Write IL", "写入 IL");
            chkPair.Text = L.T("On", "开");
            chkH264Warn.Text = L.T("On", "开");
            chkSecrets.Text = L.T("Allow removing saved accounts / computer ID", "允许删除已保存账户 / 电脑 ID");
            lvAcc.Columns[0].Text = L.T("Item", "项目");
            lvAcc.Columns[1].Text = L.T("Value", "内容");
            lbAccHint.Text = L.T(
                "Names are encrypted on this PC. VDH shows platform and count. Add accounts in Streamer.",
                "账户名是本机加密的，这里显示平台和数量。添加请用 Streamer。");
            tbAddName.Text = tbAddName.Text;
            btnRemove.Text = L.T("Remove selected", "删除选中");
            btnOpenSt.Text = L.T("Open Streamer", "打开串流端");
            btnHist.Text = L.T("Load history", "载入历史");
            btnClearHist.Text = L.T("Clear history", "清空历史");
            btnSave.Text = L.T("Save", "保存");
            btnFact.Text = L.T("Factory", "出场设置");
            btnRst.Text = L.T("Restart Streamer", "重启串流端");
            btnFeedback.Text = L.T("Feedback", "反馈");
            if (btnHsRefresh != null)
            {
                btnHsRefresh.Text = L.T("Detect headset", "检测头显");
                btnHsLog.Text = L.T("Logcat", "日志");
                btnHsStart.Text = L.T("Start app", "启动应用");
                btnHsStop.Text = L.T("Stop app", "停止应用");
                btnHsGrant.Text = L.T("Grant perms", "授权");
                btnHsInstall.Text = L.T("Install APK", "安装 APK");
                btnHsFolder.Text = L.T("Install from folder…", "从文件夹安装…");
                if (btnHsAdb != null) btnHsAdb.Text = L.T("Choose adb…", "选择 adb…");
            }
            if (btnOpenCfg != null)
            {
                extra["app.verlab"].Text = L.T("Version", "版本");
                extra["app.ver"].Text = "VDH " + AppCfg.Version;
                extra["app.pathlab"].Text = L.T("Config folder", "配置目录");
                btnOpenCfg.Text = L.T("Open folder", "打开目录");
                btnOta.Text = L.T("Check for updates", "检查更新");
                chkOta.Text = L.T("Check GitHub Release on start (HTTPS + SHA-256, no API token)", "启动时检查 GitHub Release（HTTPS + SHA-256，不用 API）");
                if (btnPickAdb != null) btnPickAdb.Text = L.T("Choose / download adb…", "选择或下载 adb…");
                if (extra.ContainsKey("app.adblab")) extra["app.adblab"].Text = L.T("adb", "adb");
                ShowAdbPath();
            }
            extra["author"].Text = L.T("Author  dwgx", "作者  dwgx");
            extra["log"].Text = ChangelogText();
            int keep = cmbCodec.SelectedIndex;
            cmbCodec.Items.Clear();
            foreach (var c in Catalog.Codecs) cmbCodec.Items.Add(c);
            if (keep >= 0 && keep < cmbCodec.Items.Count) cmbCodec.SelectedIndex = keep;
            else SyncCodecCombo();
            FillAccounts();
            RefreshDetect();
        }

        string ChangelogText()
        {
            var p = Paths.Changelog;
            if (File.Exists(p))
            {
                try { return File.ReadAllText(p, Encoding.UTF8); }
                catch { }
            }
            return L.T(
                "2026-08-28  VDH 0.2\r\n" +
                "- Human account list (no DPAPI dump)\r\n" +
                "- Codec dropdown writes PreferredCodec + CodecName together\r\n" +
                "- About: dwgx / github.com/dwgx / changelog\r\n" +
                "- Quest 1.34.22.0 LAN patch frozen (C2acked by dwgx)\r\n",
                "2026-08-28  VDH 0.2\r\n" +
                "- 账户改成可读：平台 + 数量，不再摊开密文\r\n" +
                "- 编码下拉同时写 PreferredCodec 和 CodecName\r\n" +
                "- 关于：dwgx / github.com/dwgx / 更新日志\r\n" +
                "- Quest 1.34.22.0 离线 LAN 冻结包（C2acked by dwgx）\r\n");
        }

        string DetectStreamerVer()
        {
            try
            {
                if (!File.Exists(Paths.StreamerExe)) return "";
                return FileVersionInfo.GetVersionInfo(Paths.StreamerExe).FileVersion ?? "";
            }
            catch { return ""; }
        }

        void RefreshDetect()
        {
            var ver = DetectStreamerVer();
            string lineId = "";
            foreach (var x in Catalog.Lines)
                if (!string.IsNullOrEmpty(ver) && ver.StartsWith(x.Version)) lineId = x.Version;
            if (lineId == "") lineId = "1.34.22.0";
            line = Catalog.ByVersion(lineId);
            tbDetect.Text = L.T(
                "Streamer " + (ver == "" ? "(not installed)" : ver) + "  →  " + line.Version,
                "串流端 " + (ver == "" ? "（未安装）" : ver) + "  →  " + line.Version);
        }

        void LoadSettingsFile()
        {
            if (!File.Exists(Paths.Settings)) { data = new Dictionary<string, object>(); return; }
            data = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(Paths.Settings, Encoding.UTF8))
                   ?? new Dictionary<string, object>();
        }

        void FillFromData()
        {
            chkPair.Checked = GetBool("ShowPairingRequests");
            chkH264Warn.Checked = GetBool("ShownH264PlusWarning");
            tbDevice.Text = GetStr("DeviceName");
            tbVideos.Text = GetStr("VideosRootPath");
            tbLast.Text = GetStr("LastConnectDate");
            try { numMon.Value = Math.Max(1, Math.Min(8, GetInt("MonitorCount", 1))); } catch { }
            tbWarn.Text = FormatList(GetObj("DontWarnApps"));
            try { numRot.Value = Math.Max(0, Math.Min(99, GetInt("ServerRotation", 0))); } catch { }
            SyncCodecCombo();
            FillAccounts();
        }

        bool GetBool(string k)
        {
            object v; if (!data.TryGetValue(k, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s == "True" || s == "true" || s == "1";
        }
        int GetInt(string k, int d)
        {
            object v; if (!data.TryGetValue(k, out v) || v == null) return d;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return d; }
        }
        string GetStr(string k)
        {
            object v; if (!data.TryGetValue(k, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }
        object GetObj(string k)
        {
            object v; data.TryGetValue(k, out v); return v;
        }

        void SyncCodecCombo()
        {
            object cn, id;
            string name = data.TryGetValue("CodecName", out cn) ? Convert.ToString(cn) : "";
            int pref = 5;
            if (data.TryGetValue("PreferredCodec", out id))
            {
                try { pref = Convert.ToInt32(id); } catch { }
            }
            int idx = 1;
            for (int i = 0; i < Catalog.Codecs.Length; i++)
            {
                if (Catalog.Codecs[i].JsonName == name || Catalog.Codecs[i].Id == pref) { idx = i; break; }
            }
            if (cmbCodec.Items.Count > idx) cmbCodec.SelectedIndex = idx;
        }

        static string FormatList(object v)
        {
            var list = v as IList;
            if (list == null || v is string) return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
            var parts = new List<string>();
            foreach (var x in list)
            {
                var s = Convert.ToString(x, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s)) parts.Add(s);
            }
            return string.Join(", ", parts.ToArray());
        }

        void FillAccounts()
        {
            lvAcc.Items.Clear();
            object accObj;
            var acc = data.TryGetValue("Accounts", out accObj) ? accObj as Dictionary<string, object> : null;
            if (acc != null)
            {
                foreach (var kv in acc)
                {
                    var list = kv.Value as IList;
                    int n = list == null ? 0 : list.Count;
                    string summary;
                    if (n == 0) summary = L.T("(none)", "（无）");
                    else if (cfg.AllowSecrets)
                        summary = L.T(n + " encrypted token(s) — Streamer shows the names", n + " 个加密条目 — 明文昵称在 Streamer 账户页");
                    else
                        summary = L.T(n == 1 ? "1 saved account" : n + " saved accounts", n == 1 ? "已保存 1 个账户" : "已保存 " + n + " 个账户");
                    var it = new ListViewItem(Catalog.PlatformName(kv.Key));
                    it.SubItems.Add(summary);
                    it.Tag = "acc:" + kv.Key;
                    lvAcc.Items.Add(it);
                    if (cfg.AllowSecrets && list != null)
                    {
                        int i = 1;
                        foreach (var blob in list)
                        {
                            var raw = Convert.ToString(blob) ?? "";
                            var child = new ListViewItem("    " + L.T("slot ", "槽 ") + i);
                            child.SubItems.Add(L.T("encrypted, " + raw.Length + " chars", "已加密，" + raw.Length + " 字符"));
                            child.ForeColor = Color.Gray;
                            child.Tag = "slot:" + kv.Key + ":" + (i - 1);
                            lvAcc.Items.Add(child);
                            i++;
                        }
                    }
                }
            }
            else
            {
                var it = new ListViewItem(L.T("Accounts", "账户"));
                it.SubItems.Add(L.T("(none)", "（无）"));
                lvAcc.Items.Add(it);
            }

            object pc;
            var pcItem = new ListViewItem(L.T("Protected computer ID", "受保护电脑 ID"));
            if (data.TryGetValue("ProtectedComputerID", out pc) && !string.IsNullOrEmpty(Convert.ToString(pc)))
                pcItem.SubItems.Add(L.T("Bound to this Windows user (encrypted)", "已绑定本机 Windows 用户（加密）"));
            else
                pcItem.SubItems.Add(L.T("(not set)", "（未设置）"));
            pcItem.Tag = "pcid";
            if (!cfg.AllowSecrets) { pcItem.ForeColor = Color.Gray; }
            lvAcc.Items.Add(pcItem);

            btnRemove.Enabled = cfg.AllowSecrets;
        }

        void RemoveSelectedAccount()
        {
            if (!cfg.AllowSecrets)
            {
                MessageBox.Show(this, L.T("Turn on the checkbox first.", "先勾选允许删除。"));
                return;
            }
            if (lvAcc.SelectedItems.Count == 0) return;
            var tag = Convert.ToString(lvAcc.SelectedItems[0].Tag) ?? "";
            if (tag.StartsWith("slot:"))
            {
                var parts = tag.Substring(5).Split(':');
                if (parts.Length != 2) return;
                object accObj;
                var acc = data.TryGetValue("Accounts", out accObj) ? accObj as Dictionary<string, object> : null;
                if (acc == null || !acc.ContainsKey(parts[0])) return;
                var list = acc[parts[0]] as IList;
                int idx;
                if (list == null || !int.TryParse(parts[1], out idx) || idx < 0 || idx >= list.Count) return;
                list.RemoveAt(idx);
                if (list.Count == 0) acc.Remove(parts[0]);
            }
            else if (tag.StartsWith("acc:"))
            {
                object accObj;
                var acc = data.TryGetValue("Accounts", out accObj) ? accObj as Dictionary<string, object> : null;
                if (acc != null) acc.Remove(tag.Substring(4));
            }
            else if (tag == "pcid")
            {
                if (MessageBox.Show(this,
                    L.T("Clear the protected computer ID?", "清除受保护电脑 ID？"),
                    "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                data["ProtectedComputerID"] = "";
            }
            FillAccounts();
        }

        void PushIntoData()
        {
            data["ShowPairingRequests"] = chkPair.Checked;
            data["ShownH264PlusWarning"] = chkH264Warn.Checked;
            data["DeviceName"] = tbDevice.Text ?? "";
            data["VideosRootPath"] = tbVideos.Text ?? "";
            data["MonitorCount"] = (int)numMon.Value;
            data["ServerRotation"] = (int)numRot.Value;
            var apps = new ArrayList();
            foreach (var p in (tbWarn.Text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = p.Trim();
                if (s.Length > 0) apps.Add(s);
            }
            data["DontWarnApps"] = apps;
            if (cmbCodec.SelectedItem is Codec)
            {
                var c = (Codec)cmbCodec.SelectedItem;
                data["PreferredCodec"] = c.Id;
                data["CodecName"] = c.JsonName;
            }
        }

        void SaveNow()
        {
            PushIntoData();
            Snapshot();
            File.WriteAllText(Paths.Settings, Pretty(data), Encoding.UTF8);
            LoadHistoryList();
            MessageBox.Show(this, L.T("Saved. Restart Streamer.", "已保存。请重启串流端。"));
        }

        string Pretty(Dictionary<string, object> d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = new List<string>(d.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append("  ");
                sb.Append(ser.Serialize(keys[i]));
                sb.Append(": ");
                sb.Append(FormatJsonValue(d[keys[i]], 1));
                if (i < keys.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append('}');
            sb.AppendLine();
            return sb.ToString();
        }

        string FormatJsonValue(object v, int depth)
        {
            if (v == null) return "null";
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is int || v is long || v is byte || v is short) return Convert.ToString(v, CultureInfo.InvariantCulture);
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                var keys = new List<string>(dict.Keys);
                var pad = new string(' ', (depth + 1) * 2);
                for (int i = 0; i < keys.Count; i++)
                {
                    sb.Append(pad);
                    sb.Append(ser.Serialize(keys[i]));
                    sb.Append(": ");
                    sb.Append(FormatJsonValue(dict[keys[i]], depth + 1));
                    if (i < keys.Count - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.Append(new string(' ', depth * 2));
                sb.Append('}');
                return sb.ToString();
            }
            var list = v as IList;
            if (list != null && !(v is string))
            {
                if (list.Count == 0) return "[]";
                var sb = new StringBuilder();
                sb.AppendLine("[");
                var pad = new string(' ', (depth + 1) * 2);
                for (int i = 0; i < list.Count; i++)
                {
                    sb.Append(pad);
                    sb.Append(FormatJsonValue(list[i], depth + 1));
                    if (i < list.Count - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.Append(new string(' ', depth * 2));
                sb.Append(']');
                return sb.ToString();
            }
            return ser.Serialize(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        void EnsureFactory()
        {
            if (File.Exists(Paths.Factory) || !File.Exists(Paths.Settings)) return;
            File.Copy(Paths.Settings, Paths.Factory);
        }

        void Snapshot()
        {
            Directory.CreateDirectory(Paths.HistoryDir);
            var name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
            if (File.Exists(Paths.Settings))
                File.Copy(Paths.Settings, Path.Combine(Paths.HistoryDir, name), true);
        }

        void LoadHistoryList()
        {
            cmbHistory.Items.Clear();
            if (!Directory.Exists(Paths.HistoryDir)) return;
            var files = Directory.GetFiles(Paths.HistoryDir, "*.json");
            Array.Sort(files);
            Array.Reverse(files);
            foreach (var f in files) cmbHistory.Items.Add(Path.GetFileName(f));
            if (cmbHistory.Items.Count > 0) cmbHistory.SelectedIndex = 0;
        }

        void ClearHistory()
        {
            if (!Directory.Exists(Paths.HistoryDir)) return;
            if (MessageBox.Show(this,
                L.T("Delete all saved setting snapshots?", "删除全部设置历史？"),
                "VDH", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var f in Directory.GetFiles(Paths.HistoryDir, "*.json"))
            {
                try { File.Delete(f); } catch { }
            }
            LoadHistoryList();
        }

        void LoadHistory()
        {
            if (cmbHistory.SelectedItem == null) return;
            var p = Path.Combine(Paths.HistoryDir, cmbHistory.SelectedItem.ToString());
            File.Copy(p, Paths.Settings, true);
            LoadSettingsFile();
            FillFromData();
        }

        void RestoreFactory()
        {
            if (!File.Exists(Paths.Factory))
            {
                MessageBox.Show(this, L.T("No factory snapshot yet.", "还没有出场备份。"));
                return;
            }
            Snapshot();
            File.Copy(Paths.Factory, Paths.Settings, true);
            LoadSettingsFile();
            FillFromData();
        }

        void RestartStreamer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("VirtualDesktop.Streamer"))
                    p.Kill();
            }
            catch { }
            if (File.Exists(Paths.StreamerExe))
                Process.Start(new ProcessStartInfo(Paths.StreamerExe) { WorkingDirectory = Path.GetDirectoryName(Paths.StreamerExe) });
        }

        void WriteBitrate()
        {
            int cap;
            if (!int.TryParse(tbBitrate.Text.Trim(), out cap) || cap < 50 || cap > 4000)
            {
                MessageBox.Show(this, L.T("Bitrate 50–4000.", "码率 50–4000。"));
                return;
            }
            using (var d = new OpenFileDialog())
            {
                d.Filter = "APK|*.apk";
                d.Title = L.T("Quest APK to patch", "选择要改上限的 Quest APK");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var dest = Path.Combine(Path.GetDirectoryName(d.FileName) ?? ".",
                    Path.GetFileNameWithoutExtension(d.FileName) + "_cap" + cap + ".apk");
                var log = new StringBuilder();
                try
                {
                    Cursor = Cursors.WaitCursor;
                    Application.DoEvents();
                    CapPatch.Apply(d.FileName, cap, dest, log);
                    cfg.LastBitrate = cap;
                    if (File.Exists(dest))
                    {
                        cfg.LastApkSha = BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(dest))).Replace("-", "");
                        cfg.Save();
                    }
                    RefreshBitrateLabel();
                    MessageBox.Show(this,
                        L.T("Patched APK written:\n", "已写出补丁 APK：\n") + dest + "\n\n" + log
                        + L.T("\nInstall it on the headset (Headset tab).", "\n用头显页安装到 Quest。"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.T("Patch failed: ", "打补丁失败：") + ex.Message + "\n" + log);
                }
                finally { Cursor = Cursors.Default; }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(Paths.AppDir);
            Directory.CreateDirectory(Paths.HistoryDir);
            Application.Run(new MainForm(AppCfg.Load()));
        }
    }
}
