using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Threading;

namespace MoonClass
{
    /// <summary>路径、运行时释放、管理员判定等基础设施。
    /// 目录结构与 Python 版完全一致（%LOCALAPPDATA%\LanStream），两个版本可以共用
    /// 已释放的 Sunshine/Moonlight 运行时和配置文件。</summary>
    public static class Runtime
    {
        public const string AppName = "MoonClass LanStream";
        public const string Version = "v1.3.3";
        public const int SunshinePort = 47989;
        public const int StreamPortLo = 47984;
        public const int StreamPortHi = 48010;
        public const string FixedPin = "0000";
        public const string HostName = "MoonClass-Room";   // mDNS 主机名必须 ASCII

        public static string ExeDir
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        // ---------------- 应用图标（内嵌，单文件分发不依赖外部文件） ----------------
        static Icon _appIcon;

        /// <summary>从 exe 内嵌资源加载应用图标（含全部尺寸，任务栏/标题栏共用）。
        /// 优先内嵌资源，找不到再试 exe 同目录的 icon.ico，最后用系统默认。</summary>
        public static Icon LoadAppIcon(int width, int height)
        {
            try
            {
                if (_appIcon == null)
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream s = asm.GetManifestResourceStream("MoonClass.Resources.icon.ico"))
                    {
                        if (s != null) _appIcon = new Icon(s);
                    }
                    if (_appIcon == null)
                    {
                        string p = Path.Combine(ExeDir, "icon.ico");
                        if (File.Exists(p)) _appIcon = new Icon(p);
                    }
                    if (_appIcon == null) return SystemIcons.Application;
                }
                return new Icon(_appIcon, width, height);
            }
            catch { return SystemIcons.Application; }
        }

        /// <summary>取最合适的大图标（用于窗体 Icon，系统自动选任务栏/标题栏尺寸）。</summary>
        public static Icon LoadAppIcon()
        {
            return LoadAppIcon(64, 64);
        }

        public static string AppDataDir
        {
            get
            {
                string b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(b, "LanStream");
            }
        }

        public static string ConfigDir { get { return Path.Combine(AppDataDir, "config"); } }
        public static string LogFile { get { return Path.Combine(AppDataDir, "sunshine.log"); } }

        public static string SunshineExe
        {
            get { return Path.Combine(AppDataDir, Path.Combine("sunshine", "sunshine.exe")); }
        }

        public static string MoonlightExe
        {
            get { return Path.Combine(AppDataDir, Path.Combine("client", "Moonlight.exe")); }
        }

        public static void Init()
        {
            try { Directory.CreateDirectory(AppDataDir); } catch { }
            try { Directory.CreateDirectory(ConfigDir); } catch { }
        }

        /// <summary>运行时是否已完整释放。除了两个 exe，还要检查 VC++ 运行库：
        /// Moonlight（Qt/MSVC 编译）依赖 VCRUNTIME140/MSVCP140，这些不是 Windows 自带组件，
        /// 干净机器上缺了会直接在 loader 阶段失败（弹框卡住、连日志都不产生）。</summary>
        static bool RuntimesReady()
        {
            try
            {
                if (!File.Exists(SunshineExe) || !File.Exists(MoonlightExe)) return false;
                return File.Exists(Path.Combine(AppDataDir, Path.Combine("client", "vcruntime140.dll")))
                    && File.Exists(Path.Combine(AppDataDir, Path.Combine("client", "msvcp140.dll")));
            }
            catch { return false; }
        }

        /// <summary>首次运行释放内置的 Sunshine + Moonlight 运行时（约 94MB）。
        /// 优先用 exe 同目录的 runtimes.zip，其次用嵌入资源。</summary>
        public static void EnsureRuntimes()
        {
            try
            {
                if (RuntimesReady()) return;
                var sw = Stopwatch.StartNew();
                LogStartup("释放运行时开始（已就绪=false）");

                string zip = Path.Combine(ExeDir, "runtimes.zip");
                if (File.Exists(zip))
                {
                    ExtractZip(zip, AppDataDir);
                }
                else
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using (Stream s = asm.GetManifestResourceStream("MoonClass.Resources.runtimes.zip"))
                    {
                        if (s == null) { LogStartup("嵌入运行时资源缺失"); return; }
                        string tmp = Path.Combine(Path.GetTempPath(), "mcls_runtimes.zip");
                        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                            s.CopyTo(fs);
                        ExtractZip(tmp, AppDataDir);
                        try { File.Delete(tmp); } catch { }
                    }
                }
                LogStartup("释放运行时完成，" + sw.ElapsedMilliseconds + " ms，就绪=" + RuntimesReady());
            }
            catch (Exception ex) { LogStartup("释放运行时异常: " + ex.Message); }
        }

        /// <summary>启动诊断日志（%LOCALAPPDATA%\LanStream\startup.log）。</summary>
        public static void LogStartup(string msg)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                File.AppendAllText(Path.Combine(AppDataDir, "startup.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static void ExtractZip(string zip, string destDir)
        {
            Directory.CreateDirectory(destDir);
            using (var z = ZipFile.OpenRead(zip))
            {
                foreach (var e in z.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;      // 目录项
                    string target = Path.Combine(destDir, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    try { e.ExtractToFile(target, true); } catch { }
                }
            }
        }

        /// <summary>释放内置配置模板（apps.json + Sunshine Web 凭据），仅在缺失时进行。</summary>
        public static void EnsureConfig()
        {
            try
            {
                string statePath = Path.Combine(ConfigDir, "sunshine_state.json");
                string appsPath = Path.Combine(ConfigDir, "apps.json");
                string src = Path.Combine(ExeDir, "runtime_config");
                if (!Directory.Exists(src))
                {
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (string name in new[] { "sunshine_state.json", "apps.json" })
                    {
                        string target = Path.Combine(ConfigDir, name);
                        if (File.Exists(target)) continue;
                        using (Stream s = asm.GetManifestResourceStream("MoonClass.Resources.runtime_config." + name))
                        {
                            if (s == null) continue;
                            using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write))
                                s.CopyTo(fs);
                        }
                    }
                    return;
                }
                if (!File.Exists(statePath))
                    File.Copy(Path.Combine(src, "sunshine_state.json"), statePath, true);
                if (!File.Exists(appsPath))
                    File.Copy(Path.Combine(src, "apps.json"), appsPath, true);
            }
            catch { }
        }

        public static string FindSunshine()
        {
            if (File.Exists(SunshineExe)) return SunshineExe;
            string p = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine"), "sunshine.exe");
            if (File.Exists(p)) return p;
            return null;
        }

        public static string FindMoonlight()
        {
            if (File.Exists(MoonlightExe)) return MoonlightExe;
            string p = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Moonlight Game Streaming"), "Moonlight.exe");
            if (File.Exists(p)) return p;
            return null;
        }

        public static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>以管理员身份重新启动自己（UAC 确认后旧进程退出）。</summary>
        public static void RelaunchAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = Assembly.GetExecutingAssembly().Location;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>隐藏窗口启动进程（控制台程序绝不弹黑框）。</summary>
        public static Process StartHidden(string exe, string args, string workDir)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args ?? "";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            try { return Process.Start(psi); } catch { return null; }
        }

        /// <summary>启动并等待结束，返回 (stdout, 退出码)。</summary>
        public static int Run(string exe, string args, out string stdout, int timeoutMs)
        {
            stdout = "";
            var psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args ?? "";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    var std = p.StandardOutput.ReadToEndAsync();
                    var err = p.StandardError.ReadToEndAsync();     // 必须同时读，否则管道写满会挂住
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    try { stdout = std.Result; } catch { }
                    try { err.Wait(1000); } catch { }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        public static void KillByName(string name)
        {
            Run("taskkill", "/IM \"" + name + "\" /F /T", out _, 8000);
        }

        public static bool PortOpen(int port, int timeoutMs)
        {
            return PortOpen("127.0.0.1", port, timeoutMs);
        }

        /// <summary>探测任意主机的 TCP 端口（学生端用来判断教室端是否在开播）。</summary>
        public static bool PortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>本机所有 IPv4 局域网地址。</summary>
        public static System.Collections.Generic.List<string> LanIps()
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }

        public static string SelfIp()
        {
            var ips = LanIps();
            return ips.Count > 0 ? ips[0] : "127.0.0.1";
        }
    }
}
