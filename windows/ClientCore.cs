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
            if (m == null) return false;
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
            Process p;
            try { p = Process.Start(psi); }
            catch { return false; }
            if (p == null) return false;
            CaptureOutput(p);
            return true;
        }

        /// <summary>把 Moonlight 的 stdout/stderr 追加写入 %LOCALAPPDATA%\LanStream\moonlight.log，
        /// 便于排查"配对成功却进不去桌面"这类问题。</summary>
        static void CaptureOutput(Process p)
        {
            string log = Path.Combine(Runtime.AppDataDir, "moonlight.log");
            try { Directory.CreateDirectory(Runtime.AppDataDir); } catch { }
            new Thread(delegate ()
            {
                try
                {
                    string out1 = p.StandardOutput.ReadToEnd();
                    string err1 = p.StandardError.ReadToEnd();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                    if (!string.IsNullOrEmpty(out1)) sb.AppendLine(out1);
                    if (!string.IsNullOrEmpty(err1)) sb.AppendLine(err1);
                    File.AppendAllText(log, sb.ToString(), System.Text.Encoding.UTF8);
                }
                catch { }
            }) { IsBackground = true }.Start();
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
