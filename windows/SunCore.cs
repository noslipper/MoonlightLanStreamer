using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MoonClass
{
    /// <summary>Sunshine 服务端：生成配置、启停、状态与在线客户端统计、防火墙、PIN 守护、局域网扫描。
    /// 与 Python 版行为一致（同样的 sunshine.conf 键、同样的日志事件计数）。</summary>
    public static class SunCore
    {
        static Process _proc;
        static readonly object _lock = new object();
        static readonly object _logLock = new object();
        static long _logOffset;
        static int _clientCount;
        static Thread _pinThread;
        static volatile bool _pinRun;

        public static bool ServerAlive
        {
            get { lock (_lock) { return _proc != null && !_proc.HasExited; } }
        }

        // ---------------- 配置 ----------------
        /// <summary>生成 sunshine.conf 与 apps.json，返回 conf 路径。</summary>
        public static string WriteConfig(string sunshineExe, int port, int bitrateKbps, int fps,
                                         bool hevc, bool inputOn)
        {
            string cfgDir = Runtime.ConfigDir;
            Directory.CreateDirectory(cfgDir);
            string credDir = Path.Combine(cfgDir, "credentials");
            Directory.CreateDirectory(credDir);

            string confPath = Path.Combine(cfgDir, "sunshine.conf");
            string appsPath = Path.Combine(cfgDir, "apps.json");
            string statePath = Path.Combine(cfgDir, "sunshine_state.json");

            var sb = new StringBuilder();
            sb.AppendLine("# ==== " + I18n.T_("app_title") + " 核心配置 ====");
            sb.AppendLine("locale = " + (I18n.IsZh ? "zh" : "en"));
            sb.AppendLine("sunshine_name = " + Runtime.HostName);
            sb.AppendLine("min_log_level = info");
            sb.AppendLine("system_tray = disabled");
            sb.AppendLine();
            sb.AppendLine("# 输入：默认禁用键鼠控制（学生机只收看）");
            sb.AppendLine("keyboard = " + (inputOn ? "enabled" : "disabled"));
            sb.AppendLine("mouse = " + (inputOn ? "enabled" : "disabled"));
            sb.AppendLine("controller = " + (inputOn ? "enabled" : "disabled"));
            sb.AppendLine();
            sb.AppendLine("# 网络：bind_address=0.0.0.0 是关键——不绑定所有网卡会让 47984 只绑首个网卡 IP，");
            sb.AppendLine("# 导致 502 upstream connect failed，学生端配对与收看全部失败。");
            sb.AppendLine("port = " + port);
            sb.AppendLine("address_family = ipv4");
            sb.AppendLine("bind_address = 0.0.0.0");
            sb.AppendLine("upnp = disabled");
            sb.AppendLine("lan_encryption_mode = 0");
            sb.AppendLine("wan_encryption_mode = 0");
            sb.AppendLine("fec_percentage = 10");
            sb.AppendLine();
            sb.AppendLine("# 固定 PIN：多台学生机自动配对，无需人工输入");
            sb.AppendLine("pin = " + Runtime.FixedPin);
            sb.AppendLine();
            sb.AppendLine("# 音频：捕获默认播放设备 loopback，课件/视频声音随串流发给学生端");
            sb.AppendLine("stream_audio = enabled");
            sb.AppendLine();
            sb.AppendLine("# 编码：NVENC P1 最低延迟");
            sb.AppendLine("encoder = nvenc");
            sb.AppendLine("nvenc_preset = 1");
            sb.AppendLine("nvenc_twopass = disabled");
            sb.AppendLine("hevc_mode = " + (hevc ? "2" : "1"));
            sb.AppendLine("av1_mode = 1");
            sb.AppendLine("# Sunshine 2026.x 不再识别 fps / min_bitrate：码率用 max_bitrate(Kbps)，");
            sb.AppendLine("# 帧率由客户端串流请求决定，minimum_fps_target 保证静止画面也维持下限。");
            sb.AppendLine("max_bitrate = " + bitrateKbps);
            sb.AppendLine("minimum_fps_target = " + fps);
            sb.AppendLine();
            sb.AppendLine("# 全部指向可写运行时目录");
            sb.AppendLine("file_state = " + statePath);
            sb.AppendLine("credentials_file = " + statePath);
            // 与在线客户端统计读取同一个文件（ClientCount 增量解析此日志的 CLIENT 事件）
            sb.AppendLine("log_path = " + Runtime.LogFile);
            sb.AppendLine("pkey = " + Path.Combine(credDir, "cakey.pem"));
            sb.AppendLine("cert = " + Path.Combine(credDir, "cacert.pem"));
            sb.AppendLine("file_apps = " + appsPath);
            File.WriteAllText(confPath, sb.ToString(), new UTF8Encoding(false));

            // 应用列表：仅"桌面"（Sunshine 6.x 新格式：env + image-path）
            string appsJson = "{\n  \"env\": {},\n  \"apps\": [\n    {\n      \"name\": \"Desktop\",\n      \"image-path\": \"desktop.png\"\n    }\n  ]\n}\n";
            File.WriteAllText(appsPath, appsJson, new UTF8Encoding(false));
            return confPath;
        }

        // ---------------- 服务 ----------------
        public static string ServiceStatus()
        {
            try
            {
                string outp;
                int rc = Runtime.Run("sc", "query Sunshine", out outp, 5000);
                if (outp != null && outp.Contains("RUNNING")) return "running";
                if (outp != null && outp.Contains("STOPPED")) return "stopped";
                return "absent";
            }
            catch { return "absent"; }
        }

        public static void StopService()
        {
            try { Runtime.Run("sc", "stop Sunshineservice", out _, 8000); } catch { }
            try { Runtime.Run("sc", "stop Sunshine", out _, 8000); } catch { }
            Thread.Sleep(800);
        }

        public static void EnsureFirewall()
        {
            string[] argv =
            {
                "advfirewall firewall add rule name=\"LanStream_TCP\" dir=in action=allow protocol=TCP localport=47984-48010 profile=private,domain",
                "advfirewall firewall add rule name=\"LanStream_UDP\" dir=in action=allow protocol=UDP localport=47998-48010 profile=private,domain",
            };
            foreach (string a in argv)
            {
                try { Runtime.Run("netsh", a, out _, 15000); } catch { }
            }
        }

        // ---------------- 启动 ----------------
        public class Progress
        {
            public Action<int, string> Report;
            public void Call(int pct, string text)
            {
                var h = Report;
                if (h != null) { try { h(pct, text); } catch { } }
            }
        }

        public static Dictionary<string, object> StartServer(int port, int bitrateKbps, int fps,
                                                            bool hevc, Progress progress)
        {
            var res = new Dictionary<string, object>();
            Action<int, string> prog = delegate(int p, string s)
            {
                if (progress != null) progress.Call(p, s);
            };

            prog(3, I18n.T_("p_check_env"));
            string sunshine = Runtime.FindSunshine();
            if (sunshine == null)
            {
                res["ok"] = false; res["msg"] = I18n.T_("no_sunshine");
                return res;
            }
            Runtime.EnsureConfig();

            if (ServiceStatus() == "running")
            {
                prog(8, I18n.T_("p_stop_svc"));
                StopService();
            }
            Thread.Sleep(700);

            prog(18, I18n.T_("p_write_cfg"));
            string conf = WriteConfig(sunshine, port, bitrateKbps, fps, hevc, false);

            // Sunshine 需要自身 config 目录，且凭据要与运行时配置目录一致
            try
            {
                string sunCfg = Path.Combine(Path.GetDirectoryName(sunshine), "config");
                string sunCred = Path.Combine(sunCfg, "credentials");
                Directory.CreateDirectory(sunCfg);
                Directory.CreateDirectory(sunCred);
                string rtCred = Path.Combine(Runtime.ConfigDir, "credentials");
                foreach (string f in new[] { "cacert.pem", "cakey.pem" })
                {
                    string src = Path.Combine(rtCred, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(sunCred, f), true);
                }
            }
            catch { }

            prog(38, I18n.T_("p_sync_cert"));
            prog(52, I18n.T_("p_firewall"));
            try { EnsureFirewall(); } catch { }

            prog(66, I18n.T_("p_start_srv"));
            lock (_lock)
            {
                if (_proc != null && !_proc.HasExited)
                {
                    try { _proc.Kill(); } catch { }
                }
                ResetClientCount();
                var psi = new ProcessStartInfo();
                psi.FileName = sunshine;
                psi.Arguments = "\"" + conf + "\"";
                psi.WorkingDirectory = Path.GetDirectoryName(sunshine);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                try
                {
                    _proc = new Process();
                    _proc.StartInfo = psi;
                    _proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { AppendLog(e.Data); };
                    _proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { AppendLog(e.Data); };
                    _proc.Start();
                    _proc.BeginOutputReadLine();
                    _proc.BeginErrorReadLine();
                }
                catch { }
            }

            bool ready = false;
            for (int i = 0; i < 50; i++)
            {
                lock (_lock) { if (_proc == null || _proc.HasExited) break; }
                if (Runtime.PortOpen(port, 300)) { ready = true; break; }
                if (i % 3 == 0) prog(70 + Math.Min(20, i / 2), I18n.T_("p_wait_port"));
                Thread.Sleep(400);
            }

            prog(94, I18n.T_("p_pin_guard"));
            try { StartPinGuard(); } catch { }
            prog(100, I18n.T_("p_done"));

            res["ok"] = ready;
            res["pin"] = Runtime.FixedPin;
            res["port"] = port;
            res["msg"] = ready ? I18n.T_("st_ready") : I18n.T_("st_starting");
            res["ips"] = Runtime.LanIps();
            return res;
        }

        /// <summary>Sunshine 的 stdout/stderr 落到单独文件（仅用于排障）。
        /// 绝不能写进 log_path 指向的 sunshine.log：那份日志是客户端计数的依据，
        /// 混入一遍输出会让每个 CLIENT 事件被统计两次。</summary>
        static void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try
            {
                using (var fs = new FileStream(Path.Combine(Runtime.AppDataDir, "sunshine_stdout.log"),
                                               FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.WriteLine(line);
                }
            }
            catch { }
        }

        public static void StopServer()
        {
            lock (_lock)
            {
                try
                {
                    if (_proc != null && !_proc.HasExited) _proc.Kill();
                }
                catch { }
                _proc = null;
            }
            Runtime.KillByName("sunshine.exe");
            _pinRun = false;
            ResetClientCount();
        }

        // ---------------- PIN 守护 ----------------
        public static void StartPinGuard()
        {
            _pinRun = true;
            if (_pinThread != null && _pinThread.IsAlive) return;
            _pinThread = new Thread(PinGuardLoop);
            _pinThread.IsBackground = true;
            _pinThread.Name = "PinGuard";
            _pinThread.Start();
        }

        static void PinGuardLoop()
        {
            string url = "https://localhost:47990/api/pin";
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin1234"));
            while (_pinRun)
            {
                try
                {
                    string body = HttpGet(url, auth);
                    int i = 0;
                    while (true)
                    {
                        int k = body.IndexOf("\"id\"", i);
                        if (k < 0) break;
                        int a = body.IndexOf('"', k + 4);
                        int b = body.IndexOf('"', a + 1);
                        if (a < 0 || b < 0) break;
                        string pid = body.Substring(a + 1, b - a - 1);
                        i = b + 1;
                        if (pid.Length == 0) continue;
                        string payload = "{\"pairing_id\":\"" + pid + "\",\"pin\":\"" + Runtime.FixedPin +
                                         "\",\"name\":\"ClassroomClient\"}";
                        HttpPost(url, auth, payload);
                    }
                }
                catch { }
                Thread.Sleep(500);
            }
        }

        static string HttpGet(string url, string auth)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.ServerCertificateValidationCallback = delegate { return true; };
                req.Headers["Authorization"] = "Basic " + auth;
                req.Timeout = 4000;
                req.Proxy = null;
                using (var rsp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(rsp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        static void HttpPost(string url, string auth, string json)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "POST";
                req.ServerCertificateValidationCallback = delegate { return true; };
                req.Headers["Authorization"] = "Basic " + auth;
                req.ContentType = "application/json";
                req.Timeout = 8000;
                req.Proxy = null;
                byte[] data = Encoding.UTF8.GetBytes(json);
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var rsp = (System.Net.HttpWebResponse)req.GetResponse()) { rsp.Close(); }
            }
            catch { }
        }

        // ---------------- 状态 / 客户端计数 ----------------
        public static void ResetClientCount()
        {
            lock (_logLock) { _logOffset = -1; _clientCount = 0; }
        }

        static int _sendFails;

        /// <summary>取走自上次调用以来 sunshine.log 里累计的 UDP 发送失败次数（WSASendMsg 10055）。
        /// 健康会话为 0；持续刷 10055 说明客户端已死但 Sunshine 会话没释放（僵尸会话），
        /// 学生端后续重连都会被拒——教室端据此自动重启服务自愈。</summary>
        public static int TakeSendFails()
        {
            lock (_logLock)
            {
                int v = _sendFails;
                _sendFails = 0;
                return v;
            }
        }

        /// <summary>在线客户端数：增量读 sunshine.log 的 CLIENT CONNECTED / DISCONNECTED 事件。
        /// （Sunshine 串流视频走 UDP、控制连接是短连接，按 TCP ESTABLISHED 统计会恒为 0）</summary>
        public static int ClientCount()
        {
            lock (_logLock)
            {
                try
                {
                    var fi = new FileInfo(Runtime.LogFile);
                    if (!fi.Exists) return _clientCount;
                    long len = fi.Length;
                    if (_logOffset < 0) { _logOffset = len; return _clientCount; }   // 刚开播：跳过旧内容
                    if (len < _logOffset) { _logOffset = 0; _clientCount = 0; }
                    if (len == _logOffset) return _clientCount;

                    using (var fs = new FileStream(Runtime.LogFile, FileMode.Open, FileAccess.Read,
                                                   FileShare.ReadWrite))
                    {
                        fs.Seek(_logOffset, SeekOrigin.Begin);
                        int n = (int)Math.Min(len - _logOffset, 1 << 20);
                        var buf = new byte[n];
                        n = fs.Read(buf, 0, n);
                        _logOffset += n;
                        string text = Encoding.UTF8.GetString(buf, 0, n);
                        foreach (string line in text.Split('\n'))
                        {
                            if (line.Contains("CLIENT CONNECTED")) _clientCount++;
                            else if (line.Contains("CLIENT DISCONNECTED")) _clientCount = Math.Max(0, _clientCount - 1);
                            else if (line.Contains("WSASendMsg() failed")) _sendFails++;
                        }
                    }
                }
                catch { }
                return _clientCount;
            }
        }

        public class Status
        {
            public bool Running;
            public bool Streaming;
            public int Clients;
        }

        public static Status ServerStatus()
        {
            var st = new Status();
            st.Running = Runtime.PortOpen(Runtime.SunshinePort, 250);
            st.Clients = st.Running ? ClientCount() : 0;
            st.Streaming = st.Clients > 0;
            return st;
        }

        // ---------------- 局域网扫描 ----------------
        public static List<string> ScanLan(int timeoutMs, int maxHosts)
        {
            var found = new List<string>();
            string self = Runtime.SelfIp();
            int dot = self.LastIndexOf('.');
            if (dot <= 0) return found;
            string prefix = self.Substring(0, dot + 1);

            var results = new System.Collections.Concurrent.ConcurrentBag<string>();
            var opts = new ParallelOptions { MaxDegreeOfParallelism = 48 };
            try
            {
                Parallel.For(1, Math.Min(255, maxHosts), opts, delegate(int i)
                {
                    string ip = prefix + i;
                    if (ip == self) return;
                    try
                    {
                        using (var c = new TcpClient())
                        {
                            var ar = c.BeginConnect(ip, Runtime.SunshinePort, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(timeoutMs))
                            {
                                c.EndConnect(ar);
                                results.Add(ip);
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
            foreach (string s in results) found.Add(s);
            found.Sort(delegate(string a, string b)
            {
                string[] x = a.Split('.'), y = b.Split('.');
                for (int i = 0; i < 4; i++)
                {
                    int r = int.Parse(x[i]).CompareTo(int.Parse(y[i]));
                    if (r != 0) return r;
                }
                return 0;
            });
            return found;
        }
    }
}
