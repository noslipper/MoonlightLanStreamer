using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MoonClass
{
    /// <summary>系统托盘：单击呼出/最小化，右键菜单（开播/停止/收看/退出）。</summary>
    public class TrayIcon : IDisposable
    {
        readonly NotifyIcon _ni;
        readonly ContextMenuStrip _menu;
        readonly MainForm _form;
        ToolStripMenuItem _miStop;

        public TrayIcon(MainForm form)
        {
            _form = form;
            _menu = new ContextMenuStrip();
            _menu.Font = Theme.Ui(9f);
            _menu.ShowImageMargin = true;
            _menu.ImageScalingSize = new Size(12, 12);      // 菜单图标 12px

            var miStart = new ToolStripMenuItem(I18n.T_("start"), MenuIcon("play"));
            miStart.Click += delegate { _form.TrayStart(); };
            _miStop = new ToolStripMenuItem(I18n.T_("stop"), MenuIcon("stop"));
            _miStop.Click += delegate { _form.TrayStop(); };
            var miWatch = new ToolStripMenuItem(I18n.T_("watch"), MenuIcon("watch"));
            miWatch.Click += delegate { _form.TrayWatch(); };
            var miQuit = new ToolStripMenuItem(I18n.T_("quit"), MenuIcon("quit"));
            miQuit.Click += delegate { _form.TrayQuit(); };

            _menu.Items.Add(miStart);
            _menu.Items.Add(_miStop);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miWatch);
            _menu.Items.Add(miQuit);

            Icon ico = LoadIcon();
            _ni = new NotifyIcon
            {
                Icon = ico,
                Text = I18n.T_("app_title"),
                Visible = true,
                ContextMenuStrip = _menu,
            };
            _ni.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _form.TrayToggle();
            };
        }

        static Icon LoadIcon()
        {
            try
            {
                string p = Path.Combine(Runtime.ExeDir, "icon.ico");
                if (File.Exists(p)) return new Icon(p, 32, 32);
            }
            catch { }
            return SystemIcons.Application;
        }

        Image MenuIcon(string kind)
        {
            try
            {
                var bmp = new Bitmap(12, 12);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(ColorTranslator.FromHtml("#33404e")))
                    using (var pen = new Pen(ColorTranslator.FromHtml("#33404e"), 1.4f))
                    {
                        if (kind == "play") g.FillPolygon(b, new[] { new Point(1, 1), new Point(11, 6), new Point(1, 11) });
                        else if (kind == "stop") g.FillRectangle(b, 1, 1, 10, 10);
                        else if (kind == "watch") g.DrawRectangle(pen, 1, 3, 10, 6);
                        else { g.DrawRectangle(pen, 1, 1, 10, 10); g.DrawLine(pen, 4, 6, 9, 6); }
                    }
                }
                return bmp;
            }
            catch { return null; }
        }

        public void SetStopEnabled(bool on)
        {
            try { _miStop.Enabled = on; } catch { }
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                _ni.BalloonTipTitle = title;
                _ni.BalloonTipText = text;
                _ni.ShowBalloonTip(2000);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _ni.Visible = false; _ni.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
        }
    }
}
