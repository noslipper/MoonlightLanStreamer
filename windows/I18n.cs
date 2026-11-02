using System;
using System.Collections.Generic;
using System.Globalization;

namespace MoonClass
{
    /// <summary>界面多语言：跟随系统语言（中文系统 → 中文，其余 → 英文）。
    /// 环境变量 LANSTREAM_LANG=zh|en 可强制指定（便于测试）。</summary>
    public static class I18n
    {
        public static bool IsZh { get; private set; }
        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            // 通用
            { "app_title",              new[] { "月光极速电子教室", "MoonLight Classroom LanStream" } },
            // 开播
            { "start",                  new[] { "一键开播", "Start" } },
            { "start_hint",             new[] { "一键开播", "Start Streaming" } },
            { "stop",                   new[] { "停止", "Stop" } },
            { "bitrate",                new[] { "码率 (Mb/s)", "Bitrate (Mb/s)" } },
            { "fps",                    new[] { "帧率 (FPS)", "Frame Rate (FPS)" } },
            { "audio_input",            new[] { "音频输入设备", "Audio Input" } },
            { "audio_system",           new[] { "系统声音（默认输出）", "System Sound (default output)" } },
            { "mic",                    new[] { "麦克风", "Microphone" } },
            { "mic_on_log",             new[] { "麦克风已混入串流（建议使用耳麦，避免啸叫）", "Microphone mixed into the stream (use a headset to avoid feedback)" } },
            { "mic_off_log",            new[] { "麦克风已关闭，仅串流系统声音", "Microphone off - only system sound is streamed" } },
            { "audio_none",             new[] { "未检测到音频输入设备（仅串流系统声音）", "No audio input device found (system sound only)" } },
            { "audio_err",              new[] { "⚠ 音频启动失败：", "⚠ Audio start failed: " } },
            { "mic_silent",             new[] { "ℹ 麦克风持续无输入（约 10 秒）：请确认系统未将其静音、选择了正确的输入设备，或直接对着麦克风说话验证电平条", "ℹ No mic input for ~10 s: check the system input device / mute state, or speak into the mic to see the meter move" } },
            // 收看
            { "watch",                  new[] { "一键收看", "Watch" } },
            { "watch_hint",             new[] { "一键收看", "Watch Stream" } },
            { "target_host",            new[] { "目标主机 IP", "Target Host IP" } },
            { "scan",                   new[] { "检测", "Scan" } },
            { "autowatch",              new[] { "开机自动收看", "Auto-watch on launch" } },
            { "autowatch_tip",          new[] { "开启后启动软件即自动连接并收看", "Connect and watch automatically at startup" } },
            // 状态
            { "st_off",                 new[] { "未开播", "Idle" } },
            { "st_ready",               new[] { "已开播", "Ready" } },
            { "st_live",                new[] { "正在直播", "Live" } },
            { "st_starting",            new[] { "正在开播…", "Starting…" } },
            { "st_ready_wait",          new[] { "● 已开播，等待学生端连接", "● Ready, waiting for clients" } },
            { "st_live_n",              new[] { "● 正在直播 · 已连接 {0} 台客户端", "● Live · {0} client(s) connected" } },
            { "st_live_dot",            new[] { "● 正在直播", "● Live" } },
            { "st_starting_dot",        new[] { "● 正在开播…", "● Starting…" } },
            { "st_idle_dot",            new[] { "● 未开播", "● Idle" } },
            { "btn_live_n",             new[] { "直播中 · {0} 台", "Live · {0}" } },
            { "status_ip",              new[] { "本机 IP {0}  ·  {1}", "Local IP {0}  ·  {1}" } },
            { "clients_changed",        new[] { "客户端连接数：{0}", "Connected clients: {0}" } },
            // 日志
            { "log_title",              new[] { "系统日志", "System Log" } },
            { "ready_log",              new[] { "就绪 · 系统托盘常驻，单击呼出/最小化，右键更多操作", "Ready · tray icon active; click to show/hide, right-click for more" } },
            // 开播进度
            { "p_check_env",            new[] { "检查运行环境", "Checking environment" } },
            { "p_stop_svc",             new[] { "停止系统服务", "Stopping system service" } },
            { "p_write_cfg",            new[] { "写入串流配置", "Writing stream config" } },
            { "p_sync_cert",            new[] { "同步证书", "Syncing certificates" } },
            { "p_firewall",             new[] { "放行端口", "Opening ports" } },
            { "p_start_srv",            new[] { "启动服务端", "Starting server" } },
            { "p_wait_port",            new[] { "等待端口就绪", "Waiting for ports" } },
            { "p_pin_guard",            new[] { "启动配对守护", "Starting pairing guard" } },
            { "p_done",                 new[] { "开播完成", "Started" } },
            // 消息
            { "starting_with",          new[] { "正在开播…（码率 {0} Mb/s · {1} FPS）", "Starting… ({0} Mb/s · {1} FPS)" } },
            { "start_err",              new[] { "⚠ 开播异常：", "⚠ Start failed: " } },
            { "started",                new[] { "✔ 已开播", "✔ Streaming started" } },
            { "start_fail",             new[] { "开播失败", "Start failed" } },
            { "auto_on",                new[] { "开机自动收看已开启（已保存到配置文件）", "Auto-watch enabled (saved to config)" } },
            { "auto_off",               new[] { "开机自动收看已关闭（已保存到配置文件）", "Auto-watch disabled (saved to config)" } },
            { "auto_connecting",        new[] { "开机自动收看：正在连接 {0} …", "Auto-watch: connecting to {0} …" } },
            { "auto_join",              new[] { "检测到教室端已开播，自动进入收看…", "Teacher started streaming - joining automatically…" } },
            { "auto_exit",              new[] { "教室端已停播，自动退出收看", "Teacher stopped - leaving the stream" } },
            { "stopping",               new[] { "正在停止…", "Stopping…" } },
            { "stopped",                new[] { "已停止", "Stopped" } },
            { "stop_err",               new[] { "⚠ 停止异常：", "⚠ Stop failed: " } },
            { "scanning",               new[] { "正在检测局域网已开播主机…", "Scanning LAN for streaming hosts…" } },
            { "scan_err",               new[] { "⚠ 检测异常：", "⚠ Scan failed: " } },
            { "scan_done",              new[] { "检测完成：", "Scan complete: " } },
            { "scan_none",              new[] { "未发现其他开播主机", "no other streaming host found" } },
            { "connecting",             new[] { "正在连接 {0} …（码率 {1} Mb/s · {2} FPS）", "Connecting to {0} … ({1} Mb/s · {2} FPS)" } },
            { "local_not_started",      new[] { "本机未开播，先启动…", "Local host is idle, starting server…" } },
            { "watch_ok",               new[] { "✔ 已发起收看 {0}", "✔ Watching {0}" } },
            { "no_sunshine",            new[] { "未找到 Sunshine，请先安装", "Sunshine not found, please install it first" } },
            { "no_moonlight",           new[] { "未找到 Moonlight", "Moonlight not found" } },
            { "pair_ok",                new[] { "配对完成", "Paired" } },
            { "pair_fail",              new[] { "配对失败/超时", "Pairing failed / timed out" } },
            { "watching_host",          new[] { "已连接 {0}", "Connected to {0}" } },
            { "not_paired",             new[] { "未配对，请先开播", "Not paired, please start streaming first" } },
            { "pair_fail_prefix",       new[] { "配对失败：", "Pairing failed: " } },
            { "already_running",        new[] { "程序已在运行，已切换到当前窗口", "Already running - switched to the existing window" } },
            { "quit",                   new[] { "退出", "Quit" } },
        };

        public static void Init()
        {
            string forced = (Environment.GetEnvironmentVariable("LANSTREAM_LANG") ?? "").Trim().ToLowerInvariant();
            if (forced == "zh" || forced == "zh-cn" || forced == "cn") { IsZh = true; return; }
            if (forced == "en" || forced == "en-us") { IsZh = false; return; }
            try
            {
                IsZh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch { IsZh = false; }
        }

        public static string T_(string key)
        {
            string[] v;
            if (T.TryGetValue(key, out v)) return IsZh ? v[0] : v[1];
            return key;
        }

        public static string T_(string key, params object[] args)
        {
            string s = T_(key);
            try { return string.Format(s, args); } catch { return s; }
        }
    }
}
