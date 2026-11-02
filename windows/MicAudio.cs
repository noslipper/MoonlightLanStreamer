using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MoonClass
{
    /// <summary>音频：枚举录音设备 + 采集电平 + 麦克风实时回放。
    ///
    /// Sunshine 只捕获**系统播放设备**（WASAPI loopback），其协议的 Mic 通道在 Windows 上未实现。
    /// 所以"教师讲话让学生听到"用最朴素的办法：采集麦克风并实时回放到默认输出设备
    /// （等价于系统"侦听此设备"，但完全在程序内、可即时开关、不改注册表、不重启音频服务），
    /// Sunshine 的 loopback 就会把这段声音一起串流出去。</summary>
    public class MicAudio
    {
        const int WHDR_DONE = 0x00000001;
        const int WHDR_INQUEUE = 0x00000010;
        const int WAVE_MAPPER = -1;
        const int CALLBACK_NULL = 0;
        const int SAMPLE_RATE = 22050;
        const int CHANNELS = 1;
        const int BITS = 16;
        const int BLOCK_SEC = 80;                       // 80ms 一块
        static readonly int BUF_BYTES = SAMPLE_RATE * BLOCK_SEC / 1000 * CHANNELS * BITS / 8;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
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
        struct WAVEHDR
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
        struct WAVEINCAPS
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

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int waveInGetDevCapsW(IntPtr uDeviceID, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr hwi, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr hwi, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr hwi);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr hwi);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

        static readonly object _lock = new object();
        static float _level;

        IntPtr _hwi, _hwo;
        IntPtr[] _inHdr, _inBuf, _outHdr, _outBuf;
        Thread _thread;
        volatile bool _run;
        bool _playback;
        string _device = "";
        int _oi;
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
        public bool Playback { get { return _playback; } }
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
        public bool Start(string deviceName, bool playback)
        {
            Stop();
            LastError = "";
            _playback = playback;
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

            if (_playback)
            {
                rc = waveOutOpen(out _hwo, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (rc != 0)
                {
                    LastError = "waveOutOpen rc=" + rc;
                    _hwo = IntPtr.Zero;
                    _playback = false;              // 回放不可用时仍保留电平显示
                }
            }

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

            if (_hwo != IntPtr.Zero)
            {
                _outHdr = new IntPtr[2];
                _outBuf = new IntPtr[2];
                for (int i = 0; i < 2; i++)
                {
                    _outBuf[i] = Marshal.AllocHGlobal(BUF_BYTES);
                    IntPtr h = Marshal.AllocHGlobal(hdrSize);
                    var wh = new WAVEHDR();
                    wh.lpData = _outBuf[i];
                    wh.dwBufferLength = BUF_BYTES;
                    Marshal.StructureToPtr(wh, h, false);
                    waveOutPrepareHeader(_hwo, h, hdrSize);
                    _outHdr[i] = h;
                }
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
                if (_outHdr != null && _hwo != IntPtr.Zero)
                    foreach (IntPtr h in _outHdr) waveOutUnprepareHeader(_hwo, h, hdrSize);
            }
            catch { }
            try
            {
                if (_hwi != IntPtr.Zero) { waveInStop(_hwi); waveInReset(_hwi); waveInClose(_hwi); }
            }
            catch { }
            try
            {
                if (_hwo != IntPtr.Zero) { waveOutReset(_hwo); waveOutClose(_hwo); }
            }
            catch { }

            if (_inBuf != null) foreach (IntPtr p in _inBuf) { try { Marshal.FreeHGlobal(p); } catch { } }
            if (_outBuf != null) foreach (IntPtr p in _outBuf) { try { Marshal.FreeHGlobal(p); } catch { } }
            if (_inHdr != null) foreach (IntPtr p in _inHdr) { try { Marshal.FreeHGlobal(p); } catch { } }
            if (_outHdr != null) foreach (IntPtr p in _outHdr) { try { Marshal.FreeHGlobal(p); } catch { } }

            _inHdr = _inBuf = _outHdr = _outBuf = null;
            _hwi = _hwo = IntPtr.Zero;
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

                    if (_hwo != IntPtr.Zero && _outHdr != null)
                    {
                        int k = _oi % 2;
                        var oh = (WAVEHDR)Marshal.PtrToStructure(_outHdr[k], typeof(WAVEHDR));
                        if ((oh.dwFlags & WHDR_INQUEUE) == 0)
                        {
                            _oi++;
                            CopyMemory(_outBuf[k], _inBuf[i], n);
                            oh.dwBufferLength = n;
                            oh.dwFlags = 0;
                            Marshal.StructureToPtr(oh, _outHdr[k], false);
                            try { waveOutWrite(_hwo, _outHdr[k], hdrSize); } catch { }
                        }
                    }

                    wh.dwFlags = 0;
                    wh.dwBytesRecorded = 0;
                    Marshal.StructureToPtr(wh, _inHdr[i], false);
                    try { waveInAddBuffer(_hwi, _inHdr[i], hdrSize); } catch { }
                }
                Thread.Sleep(did ? 20 : 10);
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        static extern void CopyMemory(IntPtr dest, IntPtr src, int count);

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
}
