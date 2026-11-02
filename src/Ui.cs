using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MoonlightLanStreamer
{
    /// <summary>扁平化主题色（与 Python 版一致）。</summary>
    public static class Theme
    {
        public static readonly Color Bg = ColorTranslator.FromHtml("#f3f4f6");
        public static readonly Color Panel2 = ColorTranslator.FromHtml("#ffffff");
        public static readonly Color CardBorder = ColorTranslator.FromHtml("#e7edf5");
        public static readonly Color Fg = ColorTranslator.FromHtml("#1f2937");
        public static readonly Color Muted = ColorTranslator.FromHtml("#9ca3af");
        public static readonly Color Accent = ColorTranslator.FromHtml("#3b82f6");
        public static readonly Color Green = ColorTranslator.FromHtml("#10b981");
        public static readonly Color Red = ColorTranslator.FromHtml("#f43f5e");
        public static readonly Color Cyan = ColorTranslator.FromHtml("#06b6d4");
        public static readonly Color LogBg = ColorTranslator.FromHtml("#111827");
        public static readonly Color LogFg = ColorTranslator.FromHtml("#34d399");
        public static readonly Color LogDim = ColorTranslator.FromHtml("#6b7280");

        public static string UiFamily { get { return I18n.IsZh ? "Microsoft YaHei UI" : "Segoe UI"; } }
        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(UiFamily, size, style, GraphicsUnit.Point);
        }
        public static Font Mono(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Consolas", size, style, GraphicsUnit.Point);
        }
    }

    /// <summary>纯白底 + 1px 浅描边直角卡片。</summary>
    public class Card : Panel
    {
        public Card()
        {
            DoubleBuffered = true;
            BackColor = Theme.Panel2;
            Padding = new Padding(18, 16, 18, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.CardBorder))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>扁平按钮：直角填充 + 图标 + 文字，hover/pressed 微深色，disabled 置灰。</summary>
    public class FlatButton : Control
    {
        public Color Fill = Theme.Green;
        public Color TextColor = Color.White;
        public string Kind = "play";          // play / stop / watch / scan
        public int IconSize = 13;
        bool _hover, _down;
        bool _clickable = true;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.Ui(11f, FontStyle.Bold);
            Height = 42;
        }

        public bool Clickable
        {
            get { return _clickable; }
            set { _clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (_clickable) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_clickable) { _down = false; Invalidate(); }
            base.OnMouseUp(e);
        }
        protected override void OnClick(EventArgs e)
        {
            if (_clickable) base.OnClick(e);
        }

        static Color Shade(Color c, double k)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, Math.Min(255, c.R * k)),
                (int)Math.Max(0, Math.Min(255, c.G * k)),
                (int)Math.Max(0, Math.Min(255, c.B * k)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color fill = _clickable ? Fill : ColorTranslator.FromHtml("#e5e7eb");
            if (_clickable && _down) fill = Shade(fill, 0.86);
            else if (_clickable && _hover) fill = Shade(fill, 0.94);

            using (var b = new SolidBrush(fill)) g.FillRectangle(b, 0, 0, Width, Height);

            Color fg = _clickable ? TextColor : Theme.Muted;
            string text = Text ?? "";
            Size ts = TextRenderer.MeasureText(g, text, Font, new Size(int.MaxValue, int.MaxValue),
                                               TextFormatFlags.NoPadding);
            int gap = 7;
            int total = IconSize + gap + ts.Width;
            int x = Math.Max(6, (Width - total) / 2);
            int cy = Height / 2;

            DrawGlyph(g, x, cy - IconSize / 2, IconSize, fg);
            TextRenderer.DrawText(g, text, Font,
                new Rectangle(x + IconSize + gap, 0, Math.Max(10, Width - x - IconSize - gap), Height),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        void DrawGlyph(Graphics g, int x, int y, int s, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var p = new Pen(c, 1.6f))
            {
                if (Kind == "play")
                {
                    Point[] pts = { new Point(x, y), new Point(x + s, y + s / 2), new Point(x, y + s) };
                    g.FillPolygon(b, pts);
                }
                else if (Kind == "stop")
                {
                    g.FillRectangle(b, x, y, s, s);
                }
                else if (Kind == "watch")
                {
                    g.DrawRectangle(p, x, y + s / 6, s, s * 2 / 3);
                    g.FillRectangle(b, x + s / 3, y + s / 2 - 1, s / 3, Math.Max(2, s / 6));
                }
                else // scan：放大镜
                {
                    g.DrawEllipse(p, x, y, s - 3, s - 3);
                    g.DrawLine(p, x + s - 4, y + s - 4, x + s, y + s);
                }
            }
        }
    }

    /// <summary>胶囊状态徽章：圆点 + 文字（直角卡片风格）。</summary>
    public class PillTag : Control
    {
        public Color DotColor = Theme.Muted;
        Color _fill = ColorTranslator.FromHtml("#f3f4f6");
        Color _fg = ColorTranslator.FromHtml("#6b7280");

        public PillTag()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9f, FontStyle.Bold);
            Height = 24;
            Width = 76;
        }

        public void SetState(string text, Color fill, Color fg, Color dot)
        {
            Text = text;
            _fill = fill;
            _fg = fg;
            DotColor = dot;
            Width = MeasureWidth();
            Invalidate();
        }

        int MeasureWidth()
        {
            using (var g = CreateGraphics())
            {
                Size ts = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue),
                                                   TextFormatFlags.NoPadding);
                return ts.Width + 30;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(_fill)) g.FillRectangle(b, 0, 0, Width, Height);
            using (var b = new SolidBrush(DotColor))
                g.FillEllipse(b, 9, Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(g, Text ?? "", Font,
                new Rectangle(21, 0, Width - 25, Height), _fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>开关：圆角轨道 + 圆形滑块。</summary>
    public class ToggleSwitch : Control
    {
        public Color OnColor = Theme.Green;
        bool _on;
        public event EventHandler Changed;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(40, 21);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); var h = Changed; if (h != null) h(this, EventArgs.Empty); } }
        }

        public void SetSilent(bool v) { _on = v; Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            Checked = !_on;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Height, w = Width;
            int r = h;
            using (var path = Round(new Rectangle(0, 0, w - 1, h - 1), r / 2))
            using (var b = new SolidBrush(_on ? OnColor : ColorTranslator.FromHtml("#d1d5db")))
                g.FillPath(b, path);
            int d = h - 4;
            int x = _on ? w - d - 2 : 2;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, x, 2, d, d);
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>横向音频电平指示器：30 段（绿 → 黄 → 红），只看"有没有输入"。</summary>
    public class LevelMeter : Control
    {
        const int Segs = 30;
        float _lv;

        public LevelMeter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 22;
        }

        int _lastLit = -1;

        public void SetLevel(float v)
        {
            v = v < 0 ? 0 : (v > 1 ? 1 : v);
            _lv = v;
            // 按“点亮的格数”判断是否需要重绘：格数变了才画，既省 CPU 又不会因为
            // 微小抖动被阈值吃掉（原来用 0.004 的固定阈值，底噪附近几乎永远不重绘）。
            int lit = (int)Math.Round(v * Segs);
            if (lit == _lastLit) return;
            _lastLit = lit;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = Width, h = Height;
            if (w < 24 || h < 6) return;
            int gap = 3;
            float seg = Math.Max(1f, (w - gap * (Segs - 1)) / (float)Segs);
            int lit = (int)Math.Round(_lv * Segs);
            using (var off = new SolidBrush(ColorTranslator.FromHtml("#dfe5ec")))
            {
                for (int i = 0; i < Segs; i++)
                {
                    Color c;
                    if (i < Segs * 0.66) c = Theme.Green;
                    else if (i < Segs * 0.86) c = ColorTranslator.FromHtml("#f59e0b");
                    else c = Theme.Red;
                    float x0 = i * (seg + gap);
                    var rect = new RectangleF(x0, 3, seg, h - 6);
                    if (i < lit)
                    {
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, rect);
                    }
                    else
                    {
                        g.FillRectangle(off, rect);
                    }
                }
            }
        }
    }
}
