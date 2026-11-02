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
            return Runtime.LoadAppIcon(32, 32);      // 内嵌资源，托盘不再依赖外部文件
        }

        /// <summary>渲染托盘菜单图标（size 任意，矢量缩放）。
        /// public static 便于用反射离屏渲染后检查各图标形状。</summary>
        public static Bitmap Render(string kind, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                using (var b = new SolidBrush(ColorTranslator.FromHtml("#33404e")))
                using (var pen = new Pen(ColorTranslator.FromHtml("#33404e"), Math.Max(1.2f, s / 9f)))
                {
                    if (kind == "play")
                    {
                        g.FillPolygon(b, new[] {
                            new PointF(s * 0.08f, s * 0.08f),
                            new PointF(s * 0.92f, s * 0.50f),
                            new PointF(s * 0.08f, s * 0.92f) });
                    }
                    else if (kind == "stop")
                    {
                        g.FillRectangle(b, s * 0.12f, s * 0.12f, s * 0.76f, s * 0.76f);
                    }
                    else if (kind == "watch")
                    {
                        // 显示器：屏幕 + 支柱 + 底座
                        g.DrawRectangle(pen, s * 0.04f, s * 0.14f, s * 0.92f, s * 0.60f);
                        g.DrawLine(pen, s * 0.50f, s * 0.74f, s * 0.50f, s * 0.88f);
                        g.DrawLine(pen, s * 0.28f, s * 0.88f, s * 0.72f, s * 0.88f);
                    }
                    else // quit：电源符号（开口圆 + 顶部竖线），与"收看"一眼可分
                    {
                        g.DrawLine(pen, s * 0.50f, s * 0.06f, s * 0.50f, s * 0.46f);
                        g.DrawArc(pen, s * 0.12f, s * 0.22f, s * 0.76f, s * 0.76f, -45f, 270f);
                    }
                }
            }
            return bmp;
        }

        Image MenuIcon(string kind)
        {
            try { return Render(kind, 12); }
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
