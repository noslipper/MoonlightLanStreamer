using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MoonClass
{
    public class MainForm : Form
    {
        // ---------- 组件 ----------
        Card _cardServer, _cardClient;
        PillTag _pill;
        FlatButton _btnStart, _btnStop, _btnWatch, _btnScan;
        ComboBox _cbBit, _cbFps, _cbAudio, _cbHost;
        ToggleSwitch _swMic, _swAuto;
        LevelMeter _meter;
        RichTextBox _log;
        Label _sbState, _sbInfo;
        ProgressBar _prog;
        // System.Threading.Timer 与 System.Windows.Forms.Timer 同名，两处 using 都在时会歧义，必须写全名
        System.Windows.Forms.Timer _pollTimer, _meterTimer;

        Settings _st = new Settings();
        TrayIcon _tray;
        readonly MicAudio _mic = new MicAudio();
        readonly MicUdpReceiver _udpMic = new MicUdpReceiver();
        int _lastClients = -1;
        readonly bool _meterTest = Environment.GetEnvironmentVariable("LANSTREAM_METER_TEST") == "1";
        readonly bool _dbg = Environment.GetEnvironmentVariable("LANSTREAM_DEBUG") == "1";
        bool _audioFailLogged;
        bool _silentWarned;
        int _silentTicks;
        int _dbgLastLog;
        int _sweepI;
        bool _started;
        string _startText = "";

        static readonly string[] BitChoices = { "5", "8", "10", "15", "20", "30", "50" };
        static readonly string[] FpsChoices = { "30", "60", "75", "90", "120", "144" };
        const string SystemSoundKey = "system";

        public MainForm()
        {
            I18n.Init();
            Runtime.Init();
            _st = Settings.Load();

            Text = I18n.T_("app_title");
            Font = Theme.Ui(9f);
            BackColor = Theme.Bg;
            ClientSize = new Size(I18n.IsZh ? 800 : 880, 620);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = new Icon(Path.Combine(Runtime.ExeDir, "icon.ico")); } catch { }

            BuildUi();
            LoadStateToUi();
            StartAudio();

            _pollTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _pollTimer.Tick += delegate { RefreshStatus(); SyncUdpMic(); FollowTeacher(); };
            _pollTimer.Start();

            _meterTimer = new System.Windows.Forms.Timer { Interval = 90 };
            _meterTimer.Tick += delegate
            {
                try
                {
                    if (_meterTest) { _meter.SetLevel(MeterSweep()); return; }   // 诊断：合成扫幅
                    if (!_mic.IsRunning) { ApplyAudio(); return; }               // 设备热插拔后自愈
                    float lv = _mic.Level();
                    _meter.SetLevel(lv);

                    // 长时间无输入时提示一次，帮用户区分「条坏了」和「麦克风没声音」
                    if (lv < 0.005f) _silentTicks++; else { _silentTicks = 0; _silentWarned = false; }
                    if (_silentTicks > 111 && !_silentWarned)     // 约 10 秒
                    {
                        _silentWarned = true;
                        Log(I18n.T_("mic_silent"));
                    }
                    if (_dbg && Environment.TickCount - _dbgLastLog > 1000)
                    {
                        _dbgLastLog = Environment.TickCount;
                        Log("[dbg] mic level=" + lv.ToString("0.000") +
                            " dev=" + _mic.Device + " send=" + _mic.Send +
                            " rx=" + _udpMic.IsRunning);
                    }
                }
                catch { }
            };
            _meterTimer.Start();

            RefreshStatus();
            Log(I18n.T_("ready_log"));

            if (_st.Autowatch)
                new Thread(delegate ()
                {
                    Thread.Sleep(3500);
                    try { BeginInvoke(new Action(AutoWatch)); } catch { }
                }) { IsBackground = true }.Start();
        }

        // ================= 布局 =================
        void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Bg,
                Padding = new Padding(16, 12, 16, 0),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));    // 顶栏
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 348));   // 卡片
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 日志
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));    // 状态栏

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildCards(), 0, 1);
            root.Controls.Add(BuildLog(), 0, 2);
            root.Controls.Add(BuildStatusBar(), 0, 3);
            Controls.Add(root);
        }

        Control BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            try
            {
                var ico = new Icon(Path.Combine(Runtime.ExeDir, "icon.ico"), 28, 28);
                var pb = new PictureBox
                {
                    Image = ico.ToBitmap(),
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Size = new Size(28, 28),
                    Location = new Point(0, 4),
                    BackColor = Theme.Bg,
                };
                p.Controls.Add(pb);
            }
            catch { }
            var title = new Label
            {
                Text = I18n.T_("app_title"),
                Font = Theme.Ui(15f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                Location = new Point(38, 4),
                BackColor = Theme.Bg,
            };
            var ver = new Label
            {
                Text = Runtime.Version,
                Font = Theme.Ui(8f),
                ForeColor = Theme.Muted,
                AutoSize = true,
                BackColor = Theme.Bg,
            };
            p.Controls.Add(title);
            p.Controls.Add(ver);
            p.Layout += delegate { ver.Location = new Point(title.Right + 8, title.Bottom - 15); };
            return p;
        }

        Control BuildCards()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Bg,
                Margin = new Padding(0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _cardServer = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            _cardClient = new Card { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
            BuildServerCard();
            BuildClientCard();
            grid.Controls.Add(_cardServer, 0, 0);
            grid.Controls.Add(_cardClient, 1, 0);
            return grid;
        }

        /// <summary>左右卡片共用的行高（保证"开播/停止"与"一键收看"严格水平对齐）。</summary>
        static TableLayoutPanel Rows()
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Theme.Panel2,
                Margin = new Padding(0),
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 行高说明（left/right 完全一致，总和必须 <= 卡片可用高度 348-16*2=316，
            // 超出时底部会被裁掉）：
            //  0 标题行 50（上边距14+徽章24+下边距10）
            //  1 标签行 20
            //  2 输入行 58（上边距8+控件30+下边距14）
            //  3 音频标签/麦克风行 32（上边距6 + 开关21 + 余量5）——小于 28 会把 21px 的开关底部裁掉
            //  4 下拉/说明行 56（上边距8+控件30+下边距12）
            //  5 电平条行 40（上边距2+电平条24+下边距14）
            //  6 按钮行 60（上边距2+按钮42+下边距16）
            int[] h = { 50, 20, 58, 32, 56, 40, 60 };
            for (int i = 0; i < h.Length; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, h[i]));
            return t;
        }

        static Label Caption(string text)
        {
            return new Label
            {
                Text = text,
                Font = Theme.Ui(10f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                BackColor = Theme.Panel2,
                Margin = new Padding(0, 0, 0, 0),
            };
        }

        void BuildServerCard()
        {
            var t = Rows();
            _cardServer.Controls.Add(t);

            // row0 标题 + 状态徽章
            var head = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 14, 0, 10) };
            var lab = new Label
            {
                Text = I18n.T_("start_hint"),
                Font = Theme.Ui(9f, FontStyle.Bold),
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(0, 4),
                BackColor = Theme.Panel2,
            };
            _pill = new PillTag { Location = new Point(0, 0) };
            _pill.SetState(I18n.T_("st_off"), ColorTranslator.FromHtml("#f3f4f6"),
                           ColorTranslator.FromHtml("#6b7280"), Theme.Muted);
            head.Controls.Add(lab);
            head.Controls.Add(_pill);
            head.Layout += delegate { _pill.Location = new Point(head.Width - _pill.Width, 0); };
            t.Controls.Add(head, 0, 0);

            // row1 视频标签
            t.Controls.Add(TwoLabels(I18n.T_("bitrate"), I18n.T_("fps")), 0, 1);

            // row2 视频下拉
            var row2 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 8, 0, 14) };
            _cbBit = MakeCombo(BitChoices, false);
            _cbFps = MakeCombo(FpsChoices, false);
            _cbBit.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _cbFps.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            row2.Controls.Add(_cbBit);
            row2.Controls.Add(_cbFps);
            row2.Layout += delegate
            {
                int w = (row2.Width - 6) / 2;
                _cbBit.SetBounds(0, 0, w, 30);
                _cbFps.SetBounds(w + 6, 0, w, 30);
            };
            t.Controls.Add(row2, 0, 2);

            // row3 音频标签 + 麦克风开关
            var row3 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 6, 0, 0) };
            var alab = Caption(I18n.T_("audio_input"));
            alab.Location = new Point(0, 0);
            _swMic = new ToggleSwitch { Size = new Size(40, 21) };
            _swMic.Changed += delegate { OnMicChanged(); };
            var micLab = new Label
            {
                Text = I18n.T_("mic"),
                Font = Theme.Ui(9f),
                ForeColor = Theme.Muted,
                AutoSize = true,
                BackColor = Theme.Panel2,
            };
            row3.Controls.Add(alab);
            row3.Controls.Add(_swMic);
            row3.Controls.Add(micLab);
            row3.Layout += delegate
            {
                // 开关在前、文字在后（整体仍靠右对齐）；y 微调让两者视觉中心对齐
                micLab.Location = new Point(row3.Width - micLab.Width, 2);
                _swMic.Location = new Point(row3.Width - micLab.Width - 8 - _swMic.Width, 1);
            };
            t.Controls.Add(row3, 0, 3);

            // row4 音频设备
            var row4 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 8, 0, 12) };
            _cbAudio = MakeCombo(null, true);
            _cbAudio.SelectedIndexChanged += delegate { OnAudioChanged(); };
            row4.Controls.Add(_cbAudio);
            row4.Layout += delegate { _cbAudio.SetBounds(0, 0, row4.Width, 30); };
            t.Controls.Add(row4, 0, 4);

            // row5 电平条
            var row5 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 2, 0, 14) };
            _meter = new LevelMeter { Dock = DockStyle.Fill };
            row5.Controls.Add(_meter);
            t.Controls.Add(row5, 0, 5);

            // row6 按钮
            var row6 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 2, 0, 16) };
            _btnStart = new FlatButton { Text = I18n.T_("start"), Fill = Theme.Green, Kind = "play" };
            _btnStop = new FlatButton { Text = I18n.T_("stop"), Fill = Theme.Red, Kind = "stop", Clickable = false };
            _btnStart.Click += delegate { DoStart(); };
            _btnStop.Click += delegate { DoStop(); };
            row6.Controls.Add(_btnStart);
            row6.Controls.Add(_btnStop);
            row6.Layout += delegate
            {
                int w = (row6.Width - 6) / 2;
                _btnStart.SetBounds(0, 0, w, 42);
                _btnStop.SetBounds(w + 6, 0, w, 42);
            };
            t.Controls.Add(row6, 0, 6);
        }

        Control TwoLabels(string a, string b)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0) };
            var la = Caption(a);
            var lb = Caption(b);
            p.Controls.Add(la);
            p.Controls.Add(lb);
            p.Layout += delegate
            {
                int w = p.Width / 2;
                la.Location = new Point(0, 0);
                lb.Location = new Point(w + 6, 0);
            };
            return p;
        }

        ComboBox MakeCombo(string[] values, bool readOnly)
        {
            var cb = new ComboBox
            {
                DropDownStyle = readOnly ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                Font = readOnly ? Theme.Ui(9f) : Theme.Mono(11f),
                BackColor = Color.White,
                ForeColor = Theme.Fg,
            };
            if (values != null) cb.Items.AddRange(values);
            return cb;
        }

        /// <summary>与左侧卡片同构的整行容器：Dock=Fill + 指定 Margin，内容一律顶对齐 (0,0)。
        /// 左右两张卡片每行都用它包一层，行内控件起点才会完全一致。</summary>
        static Panel Filled(Control inner, Padding margin)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = margin };
            if (inner != null)
            {
                inner.Location = new Point(0, 0);
                p.Controls.Add(inner);
            }
            return p;
        }

        void BuildClientCard()
        {
            var t = Rows();
            _cardClient.Controls.Add(t);

            var head = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 14, 0, 10) };
            var lab = new Label
            {
                Text = I18n.T_("watch_hint"),
                Font = Theme.Ui(9f, FontStyle.Bold),
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(0, 4),
                BackColor = Theme.Panel2,
            };
            head.Controls.Add(lab);
            t.Controls.Add(head, 0, 0);

            t.Controls.Add(Filled(Caption(I18n.T_("target_host")), new Padding(0)), 0, 1);

            var row2 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 8, 0, 14) };
            _cbHost = MakeCombo(null, false);
            _cbHost.Font = Theme.Mono(11f);
            _btnScan = new FlatButton { Text = I18n.T_("scan"), Fill = Theme.Cyan, Kind = "scan", Width = 86 };
            _btnScan.Click += delegate { DoScan(); };
            row2.Controls.Add(_cbHost);
            row2.Controls.Add(_btnScan);
            row2.Layout += delegate
            {
                _btnScan.SetBounds(row2.Width - 86, 0, 86, 30);
                _cbHost.SetBounds(0, 0, row2.Width - 92, 30);
            };
            t.Controls.Add(row2, 0, 2);

            t.Controls.Add(Filled(Caption(I18n.T_("autowatch")), new Padding(0, 6, 0, 0)), 0, 3);

            var row4 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 8, 0, 12) };
            var tip = new Label
            {
                Text = I18n.T_("autowatch_tip"),
                Font = Theme.Ui(9f),
                ForeColor = Theme.Muted,
                AutoSize = false,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Theme.Panel2,
            };
            _swAuto = new ToggleSwitch { Size = new Size(40, 21), OnColor = Theme.Accent };
            _swAuto.Changed += delegate
            {
                _st.Autowatch = _swAuto.Checked;
                _st.Save();
                Log(_st.Autowatch ? I18n.T_("auto_on") : I18n.T_("auto_off"));
            };
            row4.Controls.Add(tip);
            row4.Controls.Add(_swAuto);
            row4.Layout += delegate
            {
                _swAuto.Location = new Point(row4.Width - _swAuto.Width, 4);
                tip.SetBounds(0, 0, row4.Width - _swAuto.Width - 8, 30);
            };
            t.Controls.Add(row4, 0, 4);

            t.Controls.Add(Filled(null, new Padding(0, 2, 0, 14)), 0, 5);

            var row6 = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Margin = new Padding(0, 2, 0, 16) };
            _btnWatch = new FlatButton { Text = I18n.T_("watch"), Fill = Theme.Accent, Kind = "watch" };
            _btnWatch.Click += delegate { DoWatch(); };
            row6.Controls.Add(_btnWatch);
            row6.Layout += delegate { _btnWatch.SetBounds(0, 0, row6.Width, 42); };
            t.Controls.Add(row6, 0, 6);
        }

        Control BuildLog()
        {
            var wrap = new Panel { Dock = DockStyle.Fill, BackColor = Theme.LogBg, Margin = new Padding(0, 10, 0, 10) };
            var bar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = ColorTranslator.FromHtml("#1f2937") };
            bar.Controls.Add(new Label
            {
                Text = I18n.T_("log_title"),
                Font = Theme.Ui(9f, FontStyle.Bold),
                ForeColor = Theme.LogDim,
                AutoSize = true,
                Location = new Point(12, 5),
                BackColor = ColorTranslator.FromHtml("#1f2937"),
            });
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.LogBg,
                ForeColor = Theme.LogFg,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Font = Theme.Mono(9.5f),
                WordWrap = true,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,   // 不显示滚动条；滚轮/拖选仍可滚动
                Padding = new Padding(10, 4, 10, 6),
            };
            wrap.Controls.Add(_log);
            wrap.Controls.Add(bar);
            return wrap;
        }

        Control BuildStatusBar()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
            _sbState = new Label
            {
                Text = I18n.T_("st_idle_dot"),
                Font = Theme.Ui(9f, FontStyle.Bold),
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(2, 8),
                BackColor = Theme.Bg,
            };
            _sbInfo = new Label
            {
                Text = I18n.T_("status_ip", "-", Runtime.Version),
                Font = Theme.Ui(9f),
                ForeColor = Theme.Muted,
                AutoSize = true,
                BackColor = Theme.Bg,
            };
            _prog = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Visible = false, Size = new Size(170, 12) };
            p.Controls.Add(_sbState);
            p.Controls.Add(_prog);
            p.Controls.Add(_sbInfo);
            p.Layout += delegate
            {
                _sbInfo.Location = new Point(p.Width - _sbInfo.Width - 2, 8);
                _prog.Location = new Point(Math.Max(0, p.Width - _sbInfo.Width - 20 - _prog.Width), 11);
            };
            return p;
        }

        /// <summary>诊断用：模拟一个来回摆动的输入电平，用来确认电平条本身能刷新。</summary>
        float MeterSweep()
        {
            _sweepI = (_sweepI + 1) % 120;
            return (float)(0.5 + 0.45 * Math.Sin(_sweepI * Math.PI / 30.0));
        }

        // ================= 状态与设置 =================
        void LoadStateToUi()
        {
            _cbBit.Text = _st.BitrateMbps.ToString("0.##", CultureInfo.InvariantCulture);
            _cbFps.Text = _st.Fps.ToString();

            var devs = MicAudio.Inputs();
            _cbAudio.Items.Clear();
            _cbAudio.Items.Add(I18n.T_("audio_system"));
            foreach (string d in devs) _cbAudio.Items.Add(d);
            int idx = 0;
            if (!string.IsNullOrEmpty(_st.AudioDevice))
            {
                int k = _cbAudio.Items.IndexOf(_st.AudioDevice);
                if (k >= 0) idx = k;
            }
            _cbAudio.SelectedIndex = idx;

            _swMic.SetSilent(_st.Mic);
            _swAuto.SetSilent(_st.Autowatch);

            var vals = new List<string>();
            foreach (string h in _st.Hosts) if (!string.IsNullOrEmpty(h) && !vals.Contains(h)) vals.Add(h);
            foreach (string ip in Runtime.LanIps()) if (!vals.Contains(ip)) vals.Add(ip);
            if (!string.IsNullOrEmpty(_st.Host) && !vals.Contains(_st.Host)) vals.Insert(0, _st.Host);
            _cbHost.Items.Clear();
            foreach (string v in vals) _cbHost.Items.Add(v);
            _cbHost.Text = string.IsNullOrEmpty(_st.Host) ? "192.168.110.20" : _st.Host;
        }

        void SaveState()
        {
            try
            {
                double mb;
                if (!double.TryParse(_cbBit.Text.Trim().Replace("mb/s", "").Replace("Mbps", ""),
                                     NumberStyles.Float, CultureInfo.InvariantCulture, out mb)) mb = 10;
                if (mb < 1) mb = 1;
                if (mb > 200) mb = 200;
                _st.BitrateMbps = mb;
                _cbBit.Text = mb.ToString("0.##", CultureInfo.InvariantCulture);

                int fps;
                if (!int.TryParse(_cbFps.Text.Trim(), out fps)) fps = 60;
                if (fps < 15) fps = 15;
                if (fps > 240) fps = 240;
                _st.Fps = fps;
                _cbFps.Text = fps.ToString();

                string host = (_cbHost.Text ?? "").Trim();
                if (host.Length > 0) _st.Host = host;

                int sel = _cbAudio.SelectedIndex;
                _st.AudioDevice = sel > 0 ? Convert.ToString(_cbAudio.SelectedItem) : "";

                _st.Mic = _swMic.Checked;
                _st.Autowatch = _swAuto.Checked;

                var list = new List<string>();
                if (host.Length > 0) list.Add(host);
                foreach (object o in _cbHost.Items) { string s = Convert.ToString(o); if (s.Length > 0 && !list.Contains(s)) list.Add(s); }
                while (list.Count > 20) list.RemoveAt(list.Count - 1);
                _st.Hosts = list;
                _st.Save();
            }
            catch { }
        }

        // ================= 音频 =================
        string SelectedDevice()
        {
            int sel = _cbAudio.SelectedIndex;
            if (sel <= 0) return MicAudio.DefaultInput();       // 系统声音 → 用默认麦克风（只驱动电平）
            return Convert.ToString(_cbAudio.SelectedItem);
        }

        void StartAudio()
        {
            try
            {
                string dev = SelectedDevice();
                if (string.IsNullOrEmpty(dev))
                {
                    Log(I18n.T_("audio_none"));
                    return;
                }
                if (!_mic.Start(dev, _swMic.Checked) && _mic.LastError.Length > 0)
                    Log(I18n.T_("audio_err") + _mic.LastError);
            }
            catch (Exception ex) { Log(I18n.T_("audio_err") + ex.Message); }
        }

        void ApplyAudio()
        {
            try
            {
                string dev = SelectedDevice();
                if (string.IsNullOrEmpty(dev)) { _mic.Stop(); return; }
                if (_mic.IsRunning && _mic.Device == dev && _mic.Send == _swMic.Checked) return;
                // 失败只记一次，恢复成功才清标志——否则 90ms 的自愈定时器会把错误刷满日志
                bool ok = _mic.Start(dev, _swMic.Checked);
                if (!ok)
                {
                    if (!_audioFailLogged)
                    {
                        _audioFailLogged = true;
                        Log(I18n.T_("audio_err") + _mic.LastError);
                    }
                }
                else _audioFailLogged = false;
            }
            catch (Exception ex) { Log(I18n.T_("audio_err") + ex.Message); }
        }

        void OnMicChanged()
        {
            _st.Mic = _swMic.Checked;
            _st.Save();
            ApplyAudio();
            Log(_swMic.Checked ? I18n.T_("mic_on_log") : I18n.T_("mic_off_log"));
        }

        void OnAudioChanged()
        {
            _st.AudioDevice = _cbAudio.SelectedIndex > 0 ? Convert.ToString(_cbAudio.SelectedItem) : "";
            _st.Save();
            ApplyAudio();
        }

        // ================= 状态刷新 =================
        void RefreshStatus()
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(RefreshStatus)); } catch { } return; }   // 后台线程调用时切回 UI 线程
            SunCore.Status st;
            try { st = SunCore.ServerStatus(); }
            catch { return; }
            MicAudio.Live = st.Streaming;               // 只有直播中才对外广播麦克风

            if (st.Running && st.Clients != _lastClients)
            {
                if (_lastClients >= 0 || st.Clients > 0) Log(I18n.T_("clients_changed", st.Clients));
                _lastClients = st.Clients;
            }

            string sb;
            Color fg;
            if (st.Streaming)
            {
                _pill.SetState(I18n.T_("st_live"), ColorTranslator.FromHtml("#ffe4e6"),
                               ColorTranslator.FromHtml("#e11d48"), Theme.Red);
                sb = st.Clients > 0 ? I18n.T_("st_live_n", st.Clients) : I18n.T_("st_live_dot");
                fg = Theme.Red;
            }
            else if (st.Running)
            {
                _pill.SetState(I18n.T_("st_ready"), ColorTranslator.FromHtml("#ecfdf5"),
                               ColorTranslator.FromHtml("#047857"), Theme.Green);
                sb = I18n.T_("st_ready_wait");
                fg = Theme.Green;
            }
            else
            {
                _pill.SetState(I18n.T_("st_off"), ColorTranslator.FromHtml("#f3f4f6"),
                               ColorTranslator.FromHtml("#6b7280"), Theme.Muted);
                sb = I18n.T_("st_idle_dot");
                fg = Theme.Muted;
                _lastClients = -1;
            }
            if (_pill.Parent != null) _pill.Parent.PerformLayout();

            if (_tray != null) _tray.SetStopEnabled(st.Running);
            _btnStart.Clickable = !st.Running;
            _btnStop.Clickable = st.Running;
            _btnStart.Text = st.Running && st.Clients > 0 ? I18n.T_("btn_live_n", st.Clients)
                            : (st.Running ? I18n.T_("st_live") : I18n.T_("start"));
            if (!(_started && _prog.Visible)) _sbState.Text = sb;   // 进度条显示时保留阶段文案
            _sbState.ForeColor = fg;

            var ips = Runtime.LanIps();
            string ipText = ips.Count > 0 ? string.Join(I18n.IsZh ? "、" : " / ", ips.ToArray()) : "-";
            _sbInfo.Text = I18n.T_("status_ip", ipText, Runtime.Version);
            _sbInfo.Location = new Point(Math.Max(0, _sbInfo.Parent.Width - _sbInfo.Width - 2), 8);
        }

        // ================= 日志 =================
        delegate void LogDel(string s);
        public void Log(string s)
        {
            if (InvokeRequired) { try { BeginInvoke(new LogDel(Log), s); } catch { } return; }
            try
            {
                _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\n");
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
            catch { }
        }

        void ShowProgress(bool show, int pct, string text)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action<bool, int, string>(ShowProgress), show, pct, text); } catch { } return; }
            _prog.Visible = show;
            _prog.Value = Math.Max(0, Math.Min(100, pct));
            if (!string.IsNullOrEmpty(text))
            {
                _startText = text;
                _sbState.Text = "● " + text + "  " + pct + "%";
                _sbState.ForeColor = Theme.Green;
            }
        }

        // ================= 动作 =================
        public void DoStart()
        {
            SaveState();
            ApplyAudio();
            double mb = _st.BitrateMbps;
            int fps = _st.Fps;
            int kbps = (int)Math.Round(mb * 1000);
            Log(I18n.T_("starting_with", mb.ToString("0.##", CultureInfo.InvariantCulture), fps));
            _started = true;
            ShowProgress(true, 0, I18n.T_("st_starting"));
            _btnStart.Clickable = false;

            new Thread(delegate ()
            {
                try
                {
                    var prog = new SunCore.Progress();
                    prog.Report = delegate (int pct, string text) { ShowProgress(true, pct, text); };
                    var r = SunCore.StartServer(Runtime.SunshinePort, kbps, fps, true, prog);
                    bool ok = Convert.ToBoolean(r["ok"]);
                    if (ok) Log(I18n.T_("started"));
                    else Log("⚠ " + Convert.ToString(r["msg"]));
                    ShowProgress(true, ok ? 100 : 0, ok ? I18n.T_("p_done") : I18n.T_("start_fail"));
                }
                catch (Exception ex)
                {
                    Log(I18n.T_("start_err") + ex.Message);
                }
                finally
                {
                    Thread.Sleep(900);
                    _started = false;
                    ShowProgress(false, 0, null);
                    RefreshStatus();
                }
            }) { IsBackground = true }.Start();
        }

        public void DoStop()
        {
            Log(I18n.T_("stopping"));
            new Thread(delegate ()
            {
                try { SunCore.StopServer(); Log(I18n.T_("stopped")); }
                catch (Exception ex) { Log(I18n.T_("stop_err") + ex.Message); }
                RefreshStatus();
            }) { IsBackground = true }.Start();
        }

        void DoScan()
        {
            Log(I18n.T_("scanning"));
            new Thread(delegate ()
            {
                try
                {
                    var found = SunCore.ScanLan(320, 255);
                    if (found.Count > 0)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            foreach (string ip in found)
                                if (!_cbHost.Items.Contains(ip)) _cbHost.Items.Add(ip);
                        }));
                        Log(I18n.T_("scan_done") + string.Join(", ", found.ToArray()));
                    }
                    else Log(I18n.T_("scan_done") + I18n.T_("scan_none"));
                }
                catch (Exception ex) { Log(I18n.T_("scan_err") + ex.Message); }
            }) { IsBackground = true }.Start();
        }

        public void DoWatch()
        {
            SaveState();
            string host = (_cbHost.Text ?? "").Trim();
            if (host.Length == 0) return;
            double mb = _st.BitrateMbps;
            int fps = _st.Fps;
            Log(I18n.T_("connecting", host, mb.ToString("0.##", CultureInfo.InvariantCulture), fps));
            _btnWatch.Clickable = false;
            new Thread(delegate ()
            {
                try
                {
                    var r = ClientCore.Watch(host, (int)Math.Round(mb * 1000), fps);
                    bool ok = Convert.ToBoolean(r["ok"]);
                    Log(ok ? I18n.T_("watch_ok", host) : "⚠ " + Convert.ToString(r["msg"]));
                }
                catch (Exception ex) { Log("⚠ " + ex.Message); }
                finally
                {
                    BeginInvoke(new Action(delegate { _btnWatch.Clickable = true; }));
                }
            }) { IsBackground = true }.Start();
        }

        void AutoWatch()
        {
            if (!_swAuto.Checked) return;
            string host = (_st.Host ?? "").Trim();
            if (host.Length == 0) return;
            Log(I18n.T_("auto_connecting", host));

            // 仅当目标是自己（教师机开机自启场景）才顺带自动开播；
            // 学生机指向教师 IP 时绝不能替教师开播，交给 FollowTeacher 持续跟随
            bool isLocal = host == "127.0.0.1" || host == "localhost" ||
                           Runtime.LanIps().Contains(host);
            if (isLocal && !Runtime.PortOpen(Runtime.SunshinePort, 250))
            {
                DoStart();
            }
            FollowTeacher();
        }

        // ================= 自动跟随教室端 =================
        bool _followBusy;

        /// <summary>自动收看开关打开时，随教室端开播/停播自动进出收看：
        /// 教室端 Sunshine 端口开着且本机没在收看 → 自动发起收看；
        /// 教室端停播（端口关闭）且本机在收看 → 自动结束 Moonlight。
        /// 每 1.5 秒随状态轮询执行一次。</summary>
        void FollowTeacher()
        {
            if (!_swAuto.Checked || _followBusy) return;
            string host = (_st.Host ?? "").Trim();
            if (host.Length == 0) return;

            bool moonRunning;
            try { moonRunning = Process.GetProcessesByName("Moonlight").Length > 0; }
            catch { return; }
            bool teacherUp = Runtime.PortOpen(host, Runtime.SunshinePort, 400);

            if (teacherUp && !moonRunning)
            {
                _followBusy = true;                     // DoWatch 是异步的，防止下个 tick 重复触发
                Log(I18n.T_("auto_join"));
                DoWatch();
                new Thread(delegate() { Thread.Sleep(6000); _followBusy = false; })
                { IsBackground = true }.Start();
            }
            else if (!teacherUp && moonRunning)
            {
                try { ClientCore.KillAll(); } catch { }
                Log(I18n.T_("auto_exit"));
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _tray = new TrayIcon(this);
        }

        // ---------- 托盘入口 ----------
        public void TrayToggle()
        {
            if (Visible && WindowState != FormWindowState.Minimized) Hide();
            else ShowFromTray();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        public void ActivateFromOther() { ShowFromTray(); }

        public void TrayStart() { ShowFromTray(); DoStart(); }
        public void TrayStop() { DoStop(); }
        public void TrayWatch() { ShowFromTray(); DoWatch(); }

        public void TrayQuit()
        {
            SaveOnExit();
            try { if (_tray != null) _tray.Dispose(); } catch { }
            try { SunCore.StopServer(); } catch { }
            try { ClientCore.KillAll(); } catch { }
            try { Application.Exit(); } catch { }
            Environment.Exit(0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;        // 常驻托盘：关闭即最小化
                SaveState();
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        public void SaveOnExit()
        {
            try { SaveState(); } catch { }
            try { _mic.Stop(); } catch { }
            try { _udpMic.Stop(); } catch { }
        }

        // ================= 学生端麦克风接收 =================
        /// <summary>收看中（Moonlight 运行）才接收教室端的麦克风广播并本地播放；
        /// 退出收看立即停，避免把教室声音一直放出来。每 1.5s 随状态轮询执行。</summary>
        void SyncUdpMic()
        {
            bool moonRunning;
            try { moonRunning = Process.GetProcessesByName("Moonlight").Length > 0; }
            catch { return; }
            string host = (_cbHost.Text ?? "").Trim();
            if (moonRunning) _udpMic.Start(host);
            else if (_udpMic.IsRunning) _udpMic.Stop();
        }
    }
}
