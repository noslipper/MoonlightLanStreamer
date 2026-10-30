using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MoonClass
{
    /// <summary>音频：枚举录音设备 + 采集电平 + 麦克风局域网广播。
    ///
    /// 【为什么不再本机回放】旧方案把麦克风实时回放到默认输出，靠 Sunshine 的
    /// WASAPI loopback 把声音带进串流——副作用是教师从自己音响里听到自己（回声）。
    /// 现改为：采集到的 PCM 直接以 UDP 广播（端口 48050）发到局域网，
    /// 学生端客户端（本程序的学生模式）接收后本地播放，教师端不再出声。
    /// Sunshine 只负责系统声音（媒体/视频），麦克风走独立通道，互不干扰。</summary>
    public class MicAudio
    {
        public const int UDP_PORT = 48050;
        public const string MAGIC = "LMS1";

        public static long SentBlocks;

        const int WHDR_DONE = 0x00000001;
        const int WHDR_INQUEUE = 0x00000010;
        const int CALLBACK_NULL = 0;
        const int SAMPLE_RATE = 22050;
        const int CHANNELS = 1;
        const int BITS = 16;
        const int BLOCK_SEC = 80;                       // 80ms 一块
        static readonly int BUF_BYTES = SAMPLE_RATE * BLOCK_SEC / 1000 * CHANNELS * BITS / 8;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WAVEINCAPS
        {
            public short wMid;
            public short wPid;
            public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public int dwFormats;
            public short wChannels;
            public short wReserved1;
        }

        [DllImport("winmm.dll")] internal static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        internal static extern int waveInGetDevCapsW(IntPtr uDeviceID, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] internal static extern int waveInOpen(out IntPtr hwi, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] internal static extern int waveInPrepareHeader(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveInAddBuffer(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")] internal static extern int waveInStop(IntPtr hwi);
        [DllImport("winmm.dll")] internal static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")] internal static extern int waveInClose(IntPtr hwi);
        [DllImport("winmm.dll")] internal static extern int waveOutOpen(out IntPtr hwo, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] internal static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] internal static extern int waveOutClose(IntPtr hwo);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        internal static extern void CopyMemory(IntPtr dest, IntPtr src, int count);

        static readonly object _lock = new object();
        static float _level;

        /// <summary>只有直播中才对外广播麦克风（MainForm 每 1.5s 刷新一次）。</summary>
        public static volatile bool Live;

        IntPtr _hwi;
        IntPtr[] _inHdr, _inBuf;
        Thread _thread;
        volatile bool _run;
        bool _send;
        string _device = "";
        UdpClient _udp;                 // 发送：广播 + 单播（订阅者表由 TeacherBeacon 维护）
        List<IPEndPoint> _eps;
        uint _seq;
        public string LastError = "";

        // ---------------- 设备枚举 ----------------
        public static List<string> Inputs()
        {
            var list = new List<string>();
            try
            {
                int n = waveInGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var caps = new WAVEINCAPS();
                    if (waveInGetDevCapsW((IntPtr)i, ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0)
                    {
                        string name = (caps.szPname ?? "").Trim();
                        if (name.Length > 0) list.Add(name);
                    }
                }
            }
            catch { }
            return list;
        }

        public static string DefaultInput()
        {
            var l = Inputs();
            return l.Count > 0 ? l[0] : "";
        }

        static int IndexOf(string name)
        {
            var l = Inputs();
            for (int i = 0; i < l.Count; i++) if (l[i] == name) return i;
            return 0;
        }

        // ---------------- 状态 ----------------
        public bool IsRunning { get { return _run; } }
        public bool Send { get { return _send; } }
        public string Device { get { return _device; } }

        public float Level()
        {
            lock (_lock) { return _level > 1f ? 1f : _level; }
        }

        static WAVEFORMATEX Format()
        {
            var f = new WAVEFORMATEX();
            f.wFormatTag = 1;
            f.nChannels = CHANNELS;
            f.nSamplesPerSec = SAMPLE_RATE;
            f.nAvgBytesPerSec = SAMPLE_RATE * CHANNELS * BITS / 8;
            f.nBlockAlign = CHANNELS * BITS / 8;
            f.wBitsPerSample = BITS;
            f.cbSize = 0;
            return f;
        }

        // ---------------- 启停 ----------------
        public bool Start(string deviceName, bool sendMic)
        {
            Stop();
            LastError = "";
            _send = sendMic;
            _device = deviceName ?? "";
            var fmt = Format();
            int idx = IndexOf(_device);

            int rc = waveInOpen(out _hwi, idx, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
            if (rc != 0)
            {
                LastError = "waveInOpen rc=" + rc;
                _hwi = IntPtr.Zero;
                return false;
            }

            // 麦克风 UDP 广播：受限广播 + 子网定向广播双保险（收到端按来源去重）
            if (_send)
            {
                try
                {
                    _udp = new UdpClient();
                    _udp.EnableBroadcast = true;
                    _eps = new List<IPEndPoint>();
                    _eps.Add(new IPEndPoint(IPAddress.Broadcast, UDP_PORT));
                    try
                    {
                        foreach (string s in Runtime.LanIps())
                        {
                            IPAddress ip;
                            if (!IPAddress.TryParse(s, out ip)) continue;
                            byte[] b = ip.GetAddressBytes();
                            if (b.Length != 4) continue;
                            var sb = new IPAddress(new byte[] { b[0], b[1], b[2], 255 });
                            var ep = new IPEndPoint(sb, UDP_PORT);
                            if (!_eps.Contains(ep)) _eps.Add(ep);
                        }
                    }
                    catch { }
                }
                catch (Exception ex) { LastError = "udp " + ex.Message; }
            }
            else { _udp = null; }

            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
            _inHdr = new IntPtr[2];
            _inBuf = new IntPtr[2];
            for (int i = 0; i < 2; i++)
            {
                _inBuf[i] = Marshal.AllocHGlobal(BUF_BYTES);
                IntPtr h = Marshal.AllocHGlobal(hdrSize);
                var wh = new WAVEHDR();
                wh.lpData = _inBuf[i];
                wh.dwBufferLength = BUF_BYTES;
                Marshal.StructureToPtr(wh, h, false);
                waveInPrepareHeader(_hwi, h, hdrSize);
                waveInAddBuffer(_hwi, h, hdrSize);
                _inHdr[i] = h;
            }

            waveInStart(_hwi);
            _run = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "MicAudio";
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            _run = false;
            if (_thread != null && _thread.IsAlive) { try { _thread.Join(600); } catch { } }
            _thread = null;

            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
            try
            {
                if (_inHdr != null && _hwi != IntPtr.Zero)
                    foreach (IntPtr h in _inHdr) waveInUnprepareHeader(_hwi, h, hdrSize);
            }
            catch { }
            try
            {
                if (_hwi != IntPtr.Zero) { waveInStop(_hwi); waveInReset(_hwi); waveInClose(_hwi); }
            }
            catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
            _udp = null;
            _eps = null;

            if (_inBuf != null) foreach (IntPtr p in _inBuf) { try { Marshal.FreeHGlobal(p); } catch { } }
            if (_inHdr != null) foreach (IntPtr p in _inHdr) { try { Marshal.FreeHGlobal(p); } catch { } }
            _inHdr = _inBuf = null;
            _hwi = IntPtr.Zero;
            lock (_lock) _level = 0f;
        }

        void Loop()
        {
            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
            while (_run)
            {
                bool did = false;
                for (int i = 0; i < 2; i++)
                {
                    var wh = (WAVEHDR)Marshal.PtrToStructure(_inHdr[i], typeof(WAVEHDR));
                    if ((wh.dwFlags & WHDR_DONE) == 0) continue;
                    did = true;

                    int n = wh.dwBytesRecorded > 0 ? wh.dwBytesRecorded : BUF_BYTES;
                    UpdateLevel(_inBuf[i], n);
                    if (_send && Live && _udp != null) SendBlock(_inBuf[i], n);

                    // 保留 WHDR_PREPARED：清掉后 waveInAddBuffer 会返回 UNPREPARED，
                    // 采集在前两个缓冲（约 160ms）后永久停止——电平表不动、麦克风无输出的根因
                    wh.dwBufferLength = BUF_BYTES;
                    wh.dwBytesRecorded = 0;
                    Marshal.StructureToPtr(wh, _inHdr[i], false);
                    try { waveInAddBuffer(_hwi, _inHdr[i], hdrSize); } catch { }
                }
                Thread.Sleep(did ? 20 : 10);
            }
        }

        void SendBlock(IntPtr pcm, int bytes)
        {
            try
            {
                byte[] payload = new byte[bytes];
                Marshal.Copy(pcm, payload, 0, bytes);
                byte[] pkt = new byte[8 + bytes];
                Encoding.ASCII.GetBytes(MAGIC, 0, 4, pkt, 0);
                pkt[4] = (byte)_seq; pkt[5] = (byte)(_seq >> 8);
                pkt[6] = (byte)(_seq >> 16); pkt[7] = (byte)(_seq >> 24);
                _seq++;
                Buffer.BlockCopy(payload, 0, pkt, 8, bytes);

                if (_eps != null)                       // 广播兜底（同网段直连最省事）
                    foreach (IPEndPoint ep in _eps)
                    {
                        try { _udp.Send(pkt, pkt.Length, ep); } catch { }
                    }

                // 订阅者单播（跨网段/广播被拦时靠它；订阅表由 TeacherBeacon 维护）
                foreach (IPEndPoint ep in TeacherBeacon.Subs)
                {
                    try { _udp.Send(pkt, pkt.Length, ep); } catch { }
                }
                SentBlocks++;
            }
            catch { }
        }

        /// <summary>RMS → dB → 0~1：-55dB 视为静音，0dB 满量程（与 Python 版刻度一致）。</summary>
        void UpdateLevel(IntPtr buf, int bytes)
        {
            try
            {
                int n = bytes / 2;
                if (n <= 0) return;
                double total = 0;
                int cnt = 0;
                for (int i = 0; i < n; i += 8)
                {
                    short v = Marshal.ReadInt16(buf, i * 2);
                    total += (double)v * v;
                    cnt++;
                }
                if (cnt == 0) return;
                double rms = Math.Sqrt(total / cnt);
                double db = 20.0 * Math.Log10(Math.Max(rms, 1.0) / 32768.0);
                float lv = (float)Math.Max(0.0, Math.Min(1.0, (db + 55.0) / 55.0));
                lock (_lock)
                {
                    // 上升快、回落慢（每 80ms 采样一次），观感接近真实音频表
                    _level = lv >= _level ? lv : _level * 0.72f;
                }
            }
            catch { }
        }
    }

    /// <summary>教室端 ↔ 学生端信令通道（复用 UDP 48050，与麦克风数据同源）。
    ///
    /// 教室端：开播后监听学生端订阅包（LMSJ）并登记订阅者；停播时主动向
    ///   订阅者单播 + 全网广播停播信号（LMSQ，重复 3 遍），学生端收到后立即退出 Moonlight，
    ///   不再依赖"探活端口是否关闭"这种延迟数秒的被动判断。
    /// 学生端：收看期间每秒发一次订阅心跳，让教室端知道自己在场（订阅通道与
    ///   麦克风数据通道复用，跨网段/广播被禁时也能收到停播信号）。
    ///
    /// 注意：信令独立于麦克风开关——教师即使关闭了麦克风，停播信号照样能发出去。</summary>
    public static class TeacherBeacon
    {
        public const string SUB_MAGIC = "LMSJ";      // 学生端 → 教室端：订阅 / 心跳
        public const string STOP_MAGIC = "LMSQ";     // 教室端 → 学生端：已停播，请退出收看
        const int PORT = MicAudio.UDP_PORT;
        const int SUB_TTL_SEC = 6;                   // 超过 6 秒没心跳视为已离线

        class Sub { public IPEndPoint Ep; public DateTime Last; }
        static readonly object _lock = new object();
        static readonly Dictionary<string, Sub> _subs = new Dictionary<string, Sub>();
        static UdpClient _srv;
        static Thread _srvThread;
        static volatile bool _srvRun;
        static int _srvRaw;                          // 收到的原始包数（诊断）
        static string _srvErr = "";                  // 最近一次接收异常（诊断）

        /// <summary>学生端收到教室端停播信号（参数 = 教室端 IP）。在接收线程上触发，
        /// 订阅者需自行切回 UI 线程。</summary>
        public static event Action<string> StopReceived;

        /// <summary>教室端订阅监听是否已就绪（诊断用）。</summary>
        public static bool ServerUp { get { return _srv != null && _srvRun; } }
        /// <summary>订阅监听收到的原始包数（诊断用）。</summary>
        public static int ServerRawPackets { get { return _srvRaw; } }
        /// <summary>订阅监听诊断信息：本地端点 + 最近异常。</summary>
        public static string ServerDiag
        {
            get
            {
                string ep = "none";
                try { if (_srv != null && _srv.Client != null && _srv.Client.LocalEndPoint != null) ep = _srv.Client.LocalEndPoint.ToString(); }
                catch { }
                return ep + " err=" + (_srvErr.Length > 0 ? _srvErr : "-");
            }
        }

        public static int SubCount { get { lock (_lock) { return _subs.Count; } } }

        /// <summary>当前订阅者快照（麦克风单播与停播信号共用）。</summary>
        public static List<IPEndPoint> Subs
        {
            get
            {
                var list = new List<IPEndPoint>();
                lock (_lock) { foreach (Sub s in _subs.Values) list.Add(s.Ep); }
                return list;
            }
        }

        // ---------------- 教室端 ----------------
        /// <summary>开播后启动订阅监听（best effort：端口被占也不影响麦克风广播）。</summary>
        public static void StartServer()
        {
            StopServer();
            try
            {
                _srv = new UdpClient();
                _srv.ExclusiveAddressUse = false;
                _srv.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _srv.Client.Bind(new IPEndPoint(IPAddress.Any, PORT));
                _srv.Client.ReceiveTimeout = 500;
            }
            catch { try { if (_srv != null) _srv.Close(); } catch { } _srv = null; return; }

            _srvRun = true;
            _srvThread = new Thread(ServerLoop);
            _srvThread.IsBackground = true;
            _srvThread.Name = "BeaconSrv";
            _srvThread.Start();
        }

        public static void StopServer()
        {
            _srvRun = false;
            if (_srvThread != null && _srvThread.IsAlive) { try { _srvThread.Join(600); } catch { } }
            _srvThread = null;
            try { if (_srv != null) _srv.Close(); } catch { }
            _srv = null;
            lock (_lock) _subs.Clear();
        }

        static void ServerLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_srvRun && _srv != null)
            {
                try
                {
                    byte[] d = _srv.Receive(ref ep);
                    _srvRaw++;
                    if (d != null && d.Length >= 4 &&
                        d[0] == (byte)'L' && d[1] == (byte)'M' && d[2] == (byte)'S' && d[3] == (byte)'J')
                    {
                        lock (_lock) { _subs[ep.ToString()] = new Sub { Ep = ep, Last = DateTime.Now }; }
                    }
                }
                catch (SocketException se) { _srvErr = "sock:" + se.SocketErrorCode; }   // 接收超时，继续循环
                catch (Exception e) { _srvErr = e.GetType().Name + ":" + e.Message; break; }

                // 清理离线订阅者
                lock (_lock)
                {
                    var dead = new List<string>();
                    foreach (var kv in _subs)
                        if ((DateTime.Now - kv.Value.Last).TotalSeconds > SUB_TTL_SEC) dead.Add(kv.Key);
                    foreach (string k in dead) _subs.Remove(k);
                }
            }
        }

        /// <summary>停播时广播"已停播"信号。返回本轮发送次数（0 = 没有订阅者也没发成广播）。</summary>
        public static int AnnounceStop()
        {
            List<IPEndPoint> subs = Subs;
            int sent = 0;
            try
            {
                using (var u = new UdpClient())
                {
                    u.EnableBroadcast = true;
                    byte[] pkt = new byte[12];
                    Encoding.ASCII.GetBytes(STOP_MAGIC, 0, 4, pkt, 0);
                    // UDP 不可靠：连发 3 遍，间隔 120ms，丢一遍也不影响
                    for (int i = 0; i < 3; i++)
                    {
                        foreach (IPEndPoint ep in subs)
                        {
                            try { u.Send(pkt, pkt.Length, ep); sent++; } catch { }
                        }
                        try { u.Send(pkt, pkt.Length, new IPEndPoint(IPAddress.Broadcast, PORT)); sent++; } catch { }
                        foreach (string s in Runtime.LanIps())
                        {
                            IPAddress ip;
                            if (!IPAddress.TryParse(s, out ip)) continue;
                            byte[] b = ip.GetAddressBytes();
                            if (b.Length != 4) continue;
                            try
                            {
                                u.Send(pkt, pkt.Length,
                                       new IPEndPoint(new IPAddress(new byte[] { b[0], b[1], b[2], 255 }), PORT));
                                sent++;
                            }
                            catch { }
                        }
                        if (i < 2) Thread.Sleep(120);
                    }
                }
            }
            catch { }
            return sent;
        }

        // ---------------- 学生端 ----------------
        /// <summary>学生端供接收侧判断一个包是否为停播信号，是则触发 StopReceived（只触发一次）。</summary>
        internal static bool HandleStop(byte[] data, IPEndPoint remote, string lockIp, string teacherIp, ref bool fired)
        {
            if (data == null || data.Length < 4) return false;
            if (data[0] != (byte)'L' || data[1] != (byte)'M' ||
                data[2] != (byte)'S' || data[3] != (byte)'Q') return false;
            if (fired) return true;                       // 已处理过，吞掉重复包
            string ip = remote != null ? remote.Address.ToString() : "";
            // 目标是本机（教师机自收看）时，广播包的源地址是本机 LAN IP 而非 127.0.0.1，不能按 IP 拒绝
            bool localTarget = lockIp == "127.0.0.1" || lockIp == "localhost" ||
                               teacherIp == "127.0.0.1" || teacherIp == "localhost";
            if (!localTarget && lockIp.Length > 0 && lockIp != ip &&
                !(teacherIp.Length > 0 && teacherIp == ip)) return true;   // 别人的教室端，忽略
            fired = true;
            var h = StopReceived;
            if (h != null) { try { h(ip); } catch { } }
            return true;
        }
    }

    /// <summary>学生端：接收教室端的麦克风 UDP 广播（48050 端口）并本地播放。
    /// 仅在收看（Moonlight 运行）期间启用；锁定第一个有效来源，防止多教室串音。</summary>
    public class MicUdpReceiver : IDisposable
    {
        const int WHDR_DONE = 0x00000001;
        const int WHDR_INQUEUE = 0x00000010;
        const int WAVE_MAPPER = -1;
        const int SAMPLE_RATE = 22050;
        const int CHANNELS = 1;
        const int BITS = 16;
        const int BLOCK_SEC = 80;
        static readonly int BUF_BYTES = SAMPLE_RATE * BLOCK_SEC / 1000 * CHANNELS * BITS / 8;
        const int NBUF = 4;                             // 320ms 抖动余量

        MicAudio.WAVEFORMATEX Fmt()
        {
            var f = new MicAudio.WAVEFORMATEX();
            f.wFormatTag = 1;
            f.nChannels = CHANNELS;
            f.nSamplesPerSec = SAMPLE_RATE;
            f.nAvgBytesPerSec = SAMPLE_RATE * CHANNELS * BITS / 8;
            f.nBlockAlign = CHANNELS * BITS / 8;
            f.wBitsPerSample = BITS;
            f.cbSize = 0;
            return f;
        }

        Thread _thread;
        volatile bool _run;
        UdpClient _udp;
        IntPtr _hwo = IntPtr.Zero;
        IntPtr[] _hdr, _buf;
        string _lockIp = "";
        string _teacherIp = "";
        IPEndPoint _teacherEp;
        DateTime _lastSub;
        DateTime _startedAt = DateTime.MinValue;
        uint _lastSeq;
        bool _haveSeq;
        int _pkts;
        int _raw;
        int _dropLen, _dropMagic, _dropSeq, _dropIp, _dropBusy;
        string _lastSeenIp = "";
        bool _stopFired;                            // 停播信号只处理一次
        readonly int _hdrSize = Marshal.SizeOf(typeof(MicAudio.WAVEHDR));

        public string SourceIp { get { return _lockIp; } }
        public bool IsRunning { get { return _run; } }
        /// <summary>已收到的音频包数（>0 即证明麦克风链路通了）。</summary>
        public int Packets { get { return _pkts; } }
        /// <summary>收到的原始 UDP 包数（不区分内容，用于诊断链路是否通）。</summary>
        public int RawPackets { get { return _raw; } }
        /// <summary>诊断：各类丢弃计数。</summary>
        public string DropInfo
        {
            get
            {
                return string.Format("len={0} magic={1} dup={2} ip={3}({4}) busy={5}",
                    _dropLen, _dropMagic, _dropSeq, _dropIp, _lastSeenIp, _dropBusy);
            }
        }

        const int WAVE_MAPPER_RX = -1;
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, int device, ref MicAudio.WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

        public void Start(string expectIp)
        {
            if (_run && (string.IsNullOrEmpty(expectIp) || expectIp == _lockIp)) return;
            Stop();
            // 优先绑 48050（能同时收到广播和单播）；被占用则退化为临时端口，靠订阅单播
            try
            {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, MicAudio.UDP_PORT));
            }
            catch
            {
                try { if (_udp != null) _udp.Close(); } catch { }
                try { _udp = new UdpClient(0); }
                catch { _udp = null; return; }
            }
            try { _udp.Client.ReceiveTimeout = 200; } catch { }

            _teacherIp = expectIp ?? "";
            _teacherEp = null;
            if (_teacherIp.Length > 0)
            {
                IPAddress ta;
                if (IPAddress.TryParse(_teacherIp, out ta)) _teacherEp = new IPEndPoint(ta, MicAudio.UDP_PORT);
            }
            _lastSub = DateTime.MinValue;
            _startedAt = DateTime.Now;
            _haveSeq = false;
            _stopFired = false;
            _lockIp = expectIp ?? "";
            _run = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "MicUdpRx";
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            if (_thread != null && _thread.IsAlive) { try { _thread.Join(700); } catch { } }
            _thread = null;
            try { if (_udp != null) _udp.Close(); } catch { }
            _udp = null;
            try { if (_hwo != IntPtr.Zero) waveOutReset(_hwo); } catch { }
            if (_hdr != null) foreach (IntPtr h in _hdr) { try { waveOutUnprepareHeader(_hwo, h, _hdrSize); } catch { } }
            try { if (_hwo != IntPtr.Zero) waveOutClose(_hwo); } catch { }
            _hwo = IntPtr.Zero;
            if (_hdr != null) foreach (IntPtr p in _hdr) { try { Marshal.FreeHGlobal(p); } catch { } }
            if (_buf != null) foreach (IntPtr p in _buf) { try { Marshal.FreeHGlobal(p); } catch { } }
            _hdr = _buf = null;
            _lockIp = "";
        }

        public string LastError = "";

        void Loop()
        {
            // 播放缓冲：先准备好 NBUF 个头，有包就填一个空位
            _hdr = new IntPtr[NBUF];
            _buf = new IntPtr[NBUF];
            var fmt = Fmt();
            int rc = waveOutOpen(out _hwo, WAVE_MAPPER_RX, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (rc != 0) { _hwo = IntPtr.Zero; LastError = "waveOutOpen rc=" + rc; return; }
            for (int i = 0; i < NBUF; i++)
            {
                _buf[i] = Marshal.AllocHGlobal(BUF_BYTES);
                IntPtr h = Marshal.AllocHGlobal(_hdrSize);
                var wh = new MicAudio.WAVEHDR();
                wh.lpData = _buf[i];
                wh.dwBufferLength = BUF_BYTES;
                Marshal.StructureToPtr(wh, h, false);
                waveOutPrepareHeader(_hwo, h, _hdrSize);
                _hdr[i] = h;
            }

            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_run)
            {
                // 订阅心跳：每秒向教室端 48050 发一个 4 字节订阅包，
                // 教室端据此单播回麦克风数据（广播被拦时靠这条链路）
                if (_teacherEp != null && (DateTime.Now - _lastSub).TotalMilliseconds >= 1000)
                {
                    _lastSub = DateTime.Now;
                    try
                    {
                        _udp.Send(Encoding.ASCII.GetBytes(TeacherBeacon.SUB_MAGIC), 4, _teacherEp);
                    }
                    catch { }
                }

                byte[] data;
                try
                {
                    data = _udp.Receive(ref remote);
                }
                catch (SocketException)
                {
                    continue;                            // 超时，回去检查 _run
                }
                catch { break; }
                _raw++;
                // 教室端主动发来的"已停播"信号（优先于音频包处理）
                if (TeacherBeacon.HandleStop(data, remote, _lockIp, _teacherIp, ref _stopFired)) continue;
                if (data == null || data.Length < 12 || data.Length > 8 + BUF_BYTES) { _dropLen++; continue; }
                if (data[0] != (byte)'L' || data[1] != (byte)'M' ||
                    data[2] != (byte)'S' || data[3] != (byte)'1') { _dropMagic++; continue; }   // 魔数校验

                // 先按来源过滤，再去重：否则广播包（来源可能是教室端另一个 IP）会抢先推进序号，
                // 把真正要播的单播包判成重复包——麦克风"收得到却没声音"的根因
                string ip = ((IPEndPoint)remote).Address.ToString();
                if (_lockIp.Length == 0) _lockIp = ip;
                else if (_lockIp != ip)
                {
                    // 配置的 IP 长时间收不到包（换网段 / 多网卡 / 走的是广播源地址）时改锁定当前来源
                    if (_pkts == 0 && _startedAt != DateTime.MinValue &&
                        (DateTime.Now - _startedAt).TotalSeconds > 5)
                    {
                        _lockIp = ip;
                        _haveSeq = false;
                    }
                    else { _dropIp++; _lastSeenIp = ip; continue; }
                }

                // 广播 + 单播会收到同一包两次，按序号去重（避免重音/回声感）
                uint seq = BitConverter.ToUInt32(data, 4);
                if (_haveSeq)
                {
                    long diff = (long)seq - (long)_lastSeq;
                    if (diff <= 0 && diff > -100000L) { _dropSeq++; continue; }   // 旧包/重复包（序号回绕除外）
                }
                _lastSeq = seq;
                _haveSeq = true;

                // 找一个空闲播放位
                int k = -1;
                for (int i = 0; i < NBUF; i++)
                {
                    var oh = (MicAudio.WAVEHDR)Marshal.PtrToStructure(_hdr[i], typeof(MicAudio.WAVEHDR));
                    if ((oh.dwFlags & WHDR_INQUEUE) == 0) { k = i; break; }
                }
                if (k < 0) { _dropBusy++; continue; }   // 全忙：丢包（比堆延迟好）

                int payload = data.Length - 8;
                _pkts++;
                Marshal.Copy(data, 8, _buf[k], payload);
                var wh2 = (MicAudio.WAVEHDR)Marshal.PtrToStructure(_hdr[k], typeof(MicAudio.WAVEHDR));
                wh2.dwBufferLength = payload;        // 保留 PREPARED，驱动 write 时自行清标志
                Marshal.StructureToPtr(wh2, _hdr[k], false);
                try { waveOutWrite(_hwo, _hdr[k], _hdrSize); } catch { }
            }
        }

        public void Dispose() { Stop(); }
    }
}
