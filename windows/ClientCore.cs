using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace MoonClass
{
    /// <summary>Moonlight 客户端：身份注册表、配对、串流。
    /// 注册表位置与 Python 版一致：HKCU\Software\Moonlight Game Streaming Project\Moonlight</summary>
    public static class ClientCore
    {
        const string RegRoot = @"Software\Moonlight Game Streaming Project\Moonlight";

        static RegistryKey OpenRoot(bool writable)
        {
            try { return Registry.CurrentUser.OpenSubKey(RegRoot, writable); }
            catch { return null; }
        }

        /// <summary>is_paired 的等价实现：hosts 下存在带真实 srvcert 的主机项。</summary>
        public static bool IsPaired(string host)
        {
            try
            {
                using (RegistryKey root = OpenRoot(false))
                {
                    if (root == null) return false;
                    using (RegistryKey hosts = root.OpenSubKey("hosts"))
                    {
                        if (hosts == null) return false;
                        foreach (string name in hosts.GetSubKeyNames())
                        {
                            using (RegistryKey h = hosts.OpenSubKey(name))
                            {
                                if (h == null) continue;
                                string hn = Convert.ToString(h.GetValue("hostname", ""));
                                if (hn != host && hn != "127.0.0.1" && hn != "localhost") continue;
                                string cert = Convert.ToString(h.GetValue("srvcert", ""));
                                if (cert != null && cert.Length > 200) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>客户端是否已写入真实服务端证书（配对成功的标志）。</summary>
        public static bool ClientHasSrvcert()
        {
            try
            {
                using (RegistryKey root = OpenRoot(false))
                {
                    if (root == null) return false;
                    using (RegistryKey hosts = root.OpenSubKey("hosts"))
                    {
                        if (hosts == null) return false;
                        foreach (string name in hosts.GetSubKeyNames())
                        {
                            using (RegistryKey h = hosts.OpenSubKey(name))
                            {
                                if (h == null) continue;
                                string cert = Convert.ToString(h.GetValue("srvcert", ""));
                                if (cert != null && cert.Length > 200) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>清除 hosts 下的主机残留（含 apps 子键，必须递归删，否则会 "unexpected PC" 配对失败）。</summary>
        public static void ClearHosts()
        {
            try
            {
                using (RegistryKey root = OpenRoot(true))
                {
                    if (root == null) return;
                    root.DeleteSubKeyTree("hosts", false);
                }
            }
            catch { }
        }

        /// <summary>配对：(自动 PIN 由教师端守护线程提交) 轮询 srvcert 即返回，不等 pair 进程退出。</summary>
        public static bool Pair(string host, string pin)
        {
            string m = Runtime.FindMoonlight();
            if (m == null) return false;
            try { ClearHosts(); } catch { }

            Process p = null;
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = m;
                psi.Arguments = "pair " + host + " --pin " + pin;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p = Process.Start(psi);
                if (p != null)
                {
                    // 必须把输出读掉，否则管道缓冲写满会让 pair 进程阻塞
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
            }
            catch { return false; }

            bool ok = false;
            int deadline = Environment.TickCount + 30000;
            while (Environment.TickCount < deadline)
            {
                if (ClientHasSrvcert()) { ok = true; break; }
                if (p != null && p.HasExited)
                {
                    ok = (p.ExitCode == 0);
                    break;
                }
                Thread.Sleep(300);
            }

            if (p != null && !p.HasExited)
            {
                // pair 进程常常卡在 pairchallenge，不影响已完成的配对
                Runtime.KillByName("Moonlight.exe");
                try { p.Kill(); } catch { }
            }
            if (!ok) ok = ClientHasSrvcert() || IsPaired(host);
            return ok;
        }

        /// <summary>启动串流（只有配对成功后才调用，此时才让 Moonlight 主窗口出现）。</summary>
        public static bool Stream(string host, string app, int bitrateKbps, int fps, bool hevc, bool fullscreen)
        {
            string m = Runtime.FindMoonlight();
            if (m == null)
            {
                AppendLine("✗ 找不到 Moonlight.exe（" + Runtime.MoonlightExe +
                           " 不存在，且系统目录也没有安装）——运行时可能没释放完整");
                return false;
            }
            AppendLine("→ 启动: " + m + "  (exists=" + File.Exists(m) + ")");
            try { KillAll(); } catch { }              // 先清残留，避免多个实例抢同一个串流

            var psi = new ProcessStartInfo();
            psi.FileName = m;
            psi.Arguments =
                "stream " + host + " \"" + app + "\"" +
                " --bitrate " + bitrateKbps +
                " --fps " + fps +
                " --video-codec " + (hevc ? "HEVC" : "H.264") +
                " --display-mode " + (fullscreen ? "fullscreen" : "windowed") +
                " --audio-config stereo --no-audio-on-host" +
                " --no-vsync --no-hdr --video-decoder hardware";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;           // 由 Moonlight 自己开串流窗口
            psi.RedirectStandardOutput = true;   // 抓控制台输出，失败时可查 moonlight.log
            psi.RedirectStandardError = true;
            AppendLine("  参数: " + psi.Arguments);
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception ex)
            {
                AppendLine("✗ Process.Start 失败: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            if (p == null) { AppendLine("✗ Process.Start 返回 null"); return false; }
            CaptureOutput(p);
            return true;
        }

        /// <summary>把 Moonlight 的 stdout/stderr 追加写入 %LOCALAPPDATA%\LanStream\moonlight.log。
        /// 两条铁律：
        /// 1) stdout 与 stderr 必须各自单独 drain（顺序 ReadToEnd 在其中一路写满管道时会让子进程阻塞）；
        /// 2) 日志必须**实时落盘**——进程若卡在 loader 弹框（缺 VCRUNTIME 等）永不退出，
        ///    只在退出后写文件就等于"没有日志"，这正是学生端"启动不起来也没日志"的原因。</summary>
        static void CaptureOutput(Process p)
        {
            object wlock = new object();
            Action<string> write = delegate(string line)
            {
                lock (wlock) { AppendLine(line); }
            };

            write("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                  "  启动 Moonlight（PID " + SafePid(p) + "） ====");

            int pending = 2;
            var drained = new ManualResetEvent(false);
            Action<System.IO.StreamReader, string> drain = delegate(System.IO.StreamReader r, string tag)
            {
                try
                {
                    string line;
                    while ((line = r.ReadLine()) != null) write("[" + tag + "] " + line);
                }
                catch { }
                if (Interlocked.Decrement(ref pending) == 0) drained.Set();
            };
            new Thread(delegate() { drain(p.StandardOutput, "out"); }) { IsBackground = true }.Start();
            new Thread(delegate() { drain(p.StandardError, "err"); }) { IsBackground = true }.Start();

            new Thread(delegate()
            {
                try { while (!p.HasExited) Thread.Sleep(400); } catch { }
                drained.WaitOne(8000);                 // 输出最多再等 8 秒
                int code = -1;
                try { code = p.ExitCode; } catch { }
                write("exit code = " + code +
                      (code == -1073741515 || code == -1073741511
                          ? "（缺少 VC++ 运行库 / 依赖 DLL，静态链接问题）" : ""));
            }) { IsBackground = true }.Start();
        }

        static int SafePid(Process p)
        {
            try { return p.Id; } catch { return -1; }
        }

        static readonly object _mlLogLock = new object();

        /// <summary>往 moonlight.log 追加一行（实时，供排查"启动不起来"）。</summary>
        public static void AppendLine(string line)
        {
            try
            {
                lock (_mlLogLock)
                {
                    Directory.CreateDirectory(Runtime.AppDataDir);
                    File.AppendAllText(Path.Combine(Runtime.AppDataDir, "moonlight.log"),
                                       line + Environment.NewLine, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }

        /// <summary>一键收看：未配对则自动配对（固定 PIN），然后串流。</summary>
        public static Dictionary<string, object> Watch(string host, int bitrateKbps, int fps)
        {
            var res = new Dictionary<string, object>();
            if (Runtime.FindMoonlight() == null)
            {
                res["ok"] = false; res["msg"] = I18n.T_("no_moonlight");
                return res;
            }
            if (!IsPaired(host))
            {
                string last = "";
                bool ok = false;
                Thread.Sleep(2000);                  // 给教室端 Sunshine 一点时间完全就绪
                for (int i = 0; i < 3; i++)
                {
                    ok = Pair(host, Runtime.FixedPin) && IsPaired(host);
                    if (ok) break;
                    Thread.Sleep(8000);
                }
                if (!ok)
                {
                    res["ok"] = false;
                    res["msg"] = I18n.T_("pair_fail_prefix") + (last.Length > 0 ? last : I18n.T_("pair_fail"));
                    return res;
                }
            }
            bool started = Stream(host, "Desktop", bitrateKbps, fps, true, true);
            res["ok"] = started;
            res["msg"] = started ? I18n.T_("watching_host", host) : I18n.T_("no_moonlight");
            return res;
        }

        public static void KillAll()
        {
            Runtime.KillByName("Moonlight.exe");
        }
    }
}
