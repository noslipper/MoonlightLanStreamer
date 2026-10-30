using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace MoonClass
{
    /// <summary>界面设置持久化：与 Python 版共用 %LOCALAPPDATA%\LanStream\lanstream_state.json，
    /// 两个版本可以互相读到对方的设置。</summary>
    public class Settings
    {
        public string Host = "192.168.110.20";
        public List<string> Hosts = new List<string>();
        public double BitrateMbps = 10.0;
        public int Fps = 60;
        public bool Autowatch = false;
        public bool Mic = true;                 // 麦克风默认开启
        public string AudioDevice = "";

        public static string FilePath { get { return Path.Combine(Runtime.AppDataDir, "lanstream_state.json"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                string json = File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) return s;

                object v;
                if (d.TryGetValue("host", out v) && v is string && ((string)v).Length > 0) s.Host = (string)v;
                if (d.TryGetValue("bitrate_mbps", out v) && v != null)
                {
                    double mb;
                    if (double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out mb)) s.BitrateMbps = mb;
                }
                if (d.TryGetValue("fps", out v) && v != null)
                {
                    int f;
                    if (int.TryParse(Convert.ToString(v), out f) && f >= 15 && f <= 240) s.Fps = f;
                }
                if (d.TryGetValue("autowatch", out v) && v != null) s.Autowatch = Convert.ToBoolean(v);
                if (d.TryGetValue("mic", out v) && v != null) s.Mic = Convert.ToBoolean(v);
                if (d.TryGetValue("audio_device", out v) && v is string) s.AudioDevice = (string)v;
                if (d.TryGetValue("hosts", out v) && v is object[])
                {
                    s.Hosts.Clear();
                    foreach (object o in (object[])v) if (o is string) s.Hosts.Add((string)o);
                }
            }
            catch { }
            if (s.BitrateMbps < 1 || s.BitrateMbps > 200) s.BitrateMbps = 10.0;
            if (s.Fps < 15 || s.Fps > 240) s.Fps = 60;
            return s;
        }

        public void Save()
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["host"] = Host;
                d["hosts"] = Hosts.ToArray();
                d["bitrate_mbps"] = BitrateMbps;
                d["bitrate"] = (int)Math.Round(BitrateMbps * 1000);
                d["fps"] = Fps;
                d["autowatch"] = Autowatch;
                d["mic"] = Mic;
                d["audio_device"] = AudioDevice;
                d["pin"] = "0000";
                d["name"] = I18n.T_("app_title");
                var ser = new JavaScriptSerializer();
                Directory.CreateDirectory(Runtime.AppDataDir);
                File.WriteAllText(FilePath, ser.Serialize(d), new System.Text.UTF8Encoding(false));
            }
            catch { }
        }
    }
}
