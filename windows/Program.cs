using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace MoonClass
{
    internal static class Program
    {
        const string MutexName = @"Local\MoonClassEduApp";
        const string EventName = @"Local\MoonClassEduActivate";

        static Mutex _mutex;
        static EventWaitHandle _event;

        [STAThread]
        static void Main()
        {
            I18n.Init();
            Runtime.Init();

            // 单实例：命名互斥 + 唤醒事件（NULL DACL，普通权限进程也能 SetEvent）
            bool created;
            _mutex = new Mutex(true, MutexName, out created, WorldSidMutex());
            if (!created)
            {
                try
                {
                    EventWaitHandle.OpenExisting(EventName).Set();
                }
                catch { }
                return;
            }
            // 第二个实例已经唤醒了，这里继续

            if (!Runtime.IsAdmin())
            {
                Runtime.RelaunchAsAdmin();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                WriteCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                WriteCrash(e.ExceptionObject as Exception);
            };

            // 把 94MB 运行时释放放到后台，首屏不必等它
            new Thread(delegate ()
            {
                try { Runtime.EnsureRuntimes(); } catch { }
            }) { IsBackground = true, Name = "RuntimeExtract" }.Start();

            var form = new MainForm();
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out created, WorldSidEvent());

            // 唤醒监听：再次双击图标时把已有窗口拉到最前
            new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        if (!_event.WaitOne()) continue;
                        try { form.BeginInvoke(new Action(form.ActivateFromOther)); } catch { }
                    }
                    catch { Thread.Sleep(500); }
                }
            }) { IsBackground = true, Name = "ActivateWatch" }.Start();

            Application.Run(form);
        }

        /// <summary>允许任意完整性级别的进程打开这些命名对象（否则 UAC 提升后普通进程 SetEvent 会失败）。</summary>
        static EventWaitHandleSecurity WorldSidEvent()
        {
            var sec = new EventWaitHandleSecurity();
            try
            {
                var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(sid,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow));
            }
            catch { }
            return sec;
        }

        static MutexSecurity WorldSidMutex()
        {
            var sec = new MutexSecurity();
            try
            {
                var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                sec.AddAccessRule(new MutexAccessRule(sid,
                    MutexRights.Modify | MutexRights.Synchronize, AccessControlType.Allow));
            }
            catch { }
            return sec;
        }

        static void WriteCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(Path.Combine(Runtime.AppDataDir, "crash.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + ex + "\n\n",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
