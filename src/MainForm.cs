using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 0x4D43;
        private static readonly int[] DisplayModes = new int[] { 2, 0, 1 };
#if LITE
        private static readonly string[] FocusOrder = new string[] { "mode0", "mode1", "mode2" };
#else
        private static readonly string[] FocusOrder = new string[] { "mode0", "mode1", "mode2", "autostart" };
#endif

        private readonly bool _startMinimized;
        private readonly Timer _timer;
        private readonly Timer _toastTimer;
        private readonly AppSettings _settings;
        private bool _hotkeyRegistered;

        private PowerStatus _status;
        private string _hover = "";
        private string _pressed = "";
        private string _focus = "";
        private string _toast = "";
        private bool _toastError;
        private DateTime _toastUntil = DateTime.MinValue;
        private bool _busy;
        private bool _preview;
        private bool _exit;
        private bool _balloonShown;
        private bool _autoEnabled;

        private NotifyIcon _tray;
        private ModernMenu _trayMenu;
        private ModernMenu _formMenu;
        private ToolStripMenuItem _miSilent;
        private ToolStripMenuItem _miBalanced;
        private ToolStripMenuItem _miPerformance;
        private ToolStripMenuItem _miAutoTray;
        private ToolStripMenuItem _miAutoForm;

        public MainForm(bool startMinimized)
        {
            _startMinimized = startMinimized;
#if LITE
            _settings = new AppSettings();
#else
            _settings = AppSettings.Load();
#endif
            Text = "imini 控制中心";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size(Theme.P(860), Theme.P(400));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            _timer = new Timer();
            _timer.Interval = 3000;
            _timer.Tick += delegate { RefreshStatus(false); };

            _toastTimer = new Timer();
            _toastTimer.Interval = 1000;
            _toastTimer.Tick += delegate
            {
                if (_toastUntil <= DateTime.Now)
                {
                    _toast = "";
                    _toastTimer.Stop();
                }
                Invalidate();
            };

            BuildFormMenu();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi >= 72 && dpi <= 480)
                {
                    float scale = dpi / 96f;
                    if (Math.Abs(scale - Theme.S) > 0.01f)
                    {
                        Theme.S = scale;
                        ClientSize = new Size(Theme.P(860), Theme.P(400));
                    }
                }
            }
            catch { }
            Theme.ApplyWindow(Handle);
            RegisterHotkey();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
#if !LITE
            if (!_preview)
            {
                ApplyTray();
                _autoEnabled = Autostart.IsEnabled();
            }
#endif
            if (!_preview) _timer.Start();
            if (_preview) Invalidate();
            else RefreshStatus(false);
#if !LITE
            if (_startMinimized && _settings.ShowTray)
            {
                ShowInTaskbar = false;
                Hide();
            }
#endif
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
#if !LITE
            Log.Write("closing reason=" + e.CloseReason + " exit=" + _exit + " closeToTray=" + _settings.CloseToTray + " tray=" + (_tray != null));
            if (!_exit && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray && _tray != null)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
#endif
            UnregisterHotkey();
            _timer.Stop();
            _toastTimer.Stop();
            DisposeTray();
            if (_formMenu != null)
            {
                _formMenu.Dispose();
                _formMenu = null;
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId)
            {
                CycleMode();
                return;
            }
            if (m.Msg == 0x001A)
            {
                bool dark = Theme.ReadSystemDark();
                if (dark != Theme.IsDark)
                {
                    Theme.Apply(dark);
                    ApplyTheme();
                }
            }
            if (Program.ShowMessage != 0 && m.Msg == Program.ShowMessage)
            {
                RestoreFromTray();
                return;
            }
            if (m.Msg == 0x0084)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    int sx = (short)((long)m.LParam & 0xFFFF);
                    int sy = (short)(((long)m.LParam >> 16) & 0xFFFF);
                    Point client = PointToClient(new Point(sx, sy));
                    if (client.Y < Theme.P(66) && string.IsNullOrEmpty(HitTest(client)))
                    {
                        m.Result = (IntPtr)2;
                    }
                }
                return;
            }
            if (m.Msg == 0x00A3)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void ApplyTheme()
        {
            BackColor = Theme.Bg;
            if (IsHandleCreated) Theme.ApplyWindow(Handle);
            if (_formMenu != null) _formMenu.RefreshTheme();
            if (_trayMenu != null) _trayMenu.RefreshTheme();
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawBackground(g);
            DrawHeader(g);
            DrawCurrent(g);
            DrawCards(g);
            DrawFooter(g);
            DrawFocus(g);
        }

        private void DrawBackground(Graphics g)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, Theme.Bg, Theme.Bg2, 90f))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            Color accent = AccentFor(_status == null ? -1 : _status.CurrentMode);
            DrawGlow(g, R(520, -220, 520, 420), Color.FromArgb(Theme.IsDark ? 26 : 9, accent));
            DrawGlow(g, R(-180, 280, 420, 320), Color.FromArgb(Theme.IsDark ? 15 : 6, Theme.Blue));
        }

        private static void DrawGlow(Graphics g, Rectangle rect, Color color)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush b = new PathGradientBrush(path))
                {
                    b.CenterColor = color;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(b, path);
                }
            }
        }

        private void DrawHeader(Graphics g)
        {
            Rectangle logo = R(24, 18, 34, 34);
            using (GraphicsPath p = Theme.Round(logo, Theme.P(10)))
            using (LinearGradientBrush b = new LinearGradientBrush(logo, Theme.Blue, Theme.AccentSilent, 45f))
            {
                g.FillPath(b, p);
            }
            DrawBolt(g, new RectangleF(Theme.P(31), Theme.P(25), Theme.P(20), Theme.P(20)), Color.White);

            Theme.DrawText(g, "imini 控制中心", Theme.Ui(13.5f, true), Theme.Text, R(68, 13, 420, 28), Theme.Left);
            Theme.DrawText(g, "MECHREVO IMINI PRO 830", Theme.Ui(7.5f, false), Theme.Muted,
                R(68, 40, 420, 16), Theme.Left);

#if LITE
            DrawHeaderButton(g, R(764, 16, 32, 30), "min", false);
#else
            DrawHeaderButton(g, R(726, 16, 32, 30), "min", false);
            DrawHeaderButton(g, R(764, 16, 32, 30), "settings", false);
#endif
            DrawHeaderButton(g, R(802, 16, 32, 30), "close", true);

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(24), Theme.P(66), Theme.P(836), Theme.P(66));
            }
        }

        private void DrawHeaderButton(Graphics g, Rectangle r, string id, bool isClose)
        {
            bool hover = _hover == id;
            bool down = _pressed == id;
            if (hover)
            {
                Color fill = isClose ? Color.FromArgb(210, Theme.Red) : Theme.PanelHover;
                if (down) fill = Theme.Blend(fill, Color.Black, 0.12f);
                using (GraphicsPath p = Theme.Round(r, Theme.P(8)))
                using (SolidBrush b = new SolidBrush(fill))
                {
                    g.FillPath(b, p);
                }
            }
            Color glyph = isClose && hover ? Color.White : (_hover == id ? Theme.Text : Theme.Muted);
            using (Pen pen = new Pen(glyph, Theme.SW(1.4f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                if (id == "settings")
                {
                    float left = cx - Theme.P(7);
                    float right = cx + Theme.P(7);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = cy + Theme.P(-4 + i * 4);
                        g.DrawLine(pen, left, y, right, y);
                        float knob = cx + Theme.P(i == 0 ? -3 : (i == 1 ? 2 : -1));
                        g.DrawLine(pen, knob, y - Theme.P(2), knob, y + Theme.P(2));
                    }
                }
                else if (id == "min")
                {
                    g.DrawLine(pen, cx - Theme.P(5), cy + Theme.P(3), cx + Theme.P(5), cy + Theme.P(3));
                }
                else
                {
                    g.DrawLine(pen, cx - Theme.P(4), cy - Theme.P(4), cx + Theme.P(4), cy + Theme.P(4));
                    g.DrawLine(pen, cx + Theme.P(4), cy - Theme.P(4), cx - Theme.P(4), cy + Theme.P(4));
                }
            }
        }

        private void DrawCurrent(Graphics g)
        {
            bool ok = _status != null && _status.Connected;
            Color accent = AccentFor(_status == null ? -1 : _status.CurrentMode);
            string name = ok ? _status.ModeName : "未连接";
            Color nameColor = ok ? accent : Theme.Red;

            Theme.DrawText(g, "当前模式", Theme.Ui(7.5f, false), Theme.Faint, R(24, 84, 220, 16), Theme.Left);
            Theme.DrawText(g, name, Theme.Ui(22.5f, true), nameColor, R(24, 98, 520, 40), Theme.Left);

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(584), Theme.P(88), Theme.P(584), Theme.P(136));
            }

            int temp = ok ? _status.Temperature : -1;
            Color tempColor = temp < 0 ? Theme.Muted : (temp <= 60 ? Theme.Green : (temp <= 75 ? Theme.Yellow : Theme.Red));
            DrawMetric(g, 608, "CPU 温度", temp >= 0 ? temp.ToString() : "—", temp >= 0 ? "°C" : "", tempColor);
            DrawMetric(g, 720, "风扇转速", ok && _status.FanRpm >= 0 ? _status.FanRpm.ToString() : "—",
                ok && _status.FanRpm >= 0 ? "RPM" : "", Theme.Blue);
        }

        private void DrawMetric(Graphics g, float x, string label, string value, string unit, Color color)
        {
            Theme.DrawText(g, label, Theme.Ui(7.5f, false), Theme.Faint, R(x, 86, 120, 16), Theme.Left);
            Font vf = Theme.Num(15.5f, true);
            Size vs = TextRenderer.MeasureText(g, value, vf, Size.Empty, TextFormatFlags.NoPadding);
            Theme.DrawText(g, value, vf, value == "—" ? Theme.Muted : color, R(x, 101, 120, 28), Theme.Left);
            if (!string.IsNullOrEmpty(unit))
            {
                Theme.DrawText(g, unit, Theme.Ui(7.5f, false), Theme.Faint,
                    new Rectangle(Theme.P(x) + vs.Width + Theme.P(4), Theme.P(110), Theme.P(54), Theme.P(16)), Theme.Left);
            }
        }

        private void DrawCards(Graphics g)
        {
            for (int i = 0; i < 3; i++)
            {
                int mode = DisplayModes[i];
                Rectangle r = CardRect(i);
                bool selected = _status != null && _status.CurrentMode == mode;
                bool hover = _hover == "mode" + i;
                bool down = _pressed == "mode" + i;
                Color accent = AccentFor(mode);

                Color fill = selected ? Theme.Blend(Theme.Panel, accent, 0.10f) : (hover ? Theme.PanelHover : Theme.Panel);
                if (down) fill = Theme.Blend(fill, Color.Black, 0.10f);

                using (GraphicsPath p = Theme.Round(r, Theme.P(16)))
                {
                    if (selected)
                    {
                        using (Pen glow = new Pen(Color.FromArgb(Theme.IsDark ? 38 : 26, accent), Theme.SW(4)))
                        {
                            g.DrawPath(glow, p);
                        }
                    }
                    using (SolidBrush b = new SolidBrush(fill))
                    {
                        g.FillPath(b, p);
                    }
                    Color border = selected ? accent : (hover ? Theme.Blend(Theme.Border, accent, 0.55f) : Theme.Border);
                    using (Pen pen = new Pen(border, Theme.SW(selected ? 1.6f : 1f)))
                    {
                        g.DrawPath(pen, p);
                    }
                }

                DrawModeGlyph(g, mode, new Rectangle(r.X + Theme.P(18), r.Y + Theme.P(18), Theme.P(38), Theme.P(38)), accent);

                if (selected)
                {
                    Rectangle check = new Rectangle(r.Right - Theme.P(38), r.Y + Theme.P(18), Theme.P(20), Theme.P(20));
                    using (SolidBrush b = new SolidBrush(accent))
                    {
                        g.FillEllipse(b, check);
                    }
                    using (Pen pen = new Pen(Color.White, Theme.SW(1.8f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        float cx = check.X + check.Width / 2f;
                        float cy = check.Y + check.Height / 2f;
                        g.DrawLines(pen, new PointF[] {
                            new PointF(cx - Theme.P(4), cy),
                            new PointF(cx - Theme.P(1), cy + Theme.P(3)),
                            new PointF(cx + Theme.P(5), cy - Theme.P(3))
                        });
                    }
                }

                Theme.DrawText(g, ModeTitle(mode), Theme.Ui(13.5f, true), Theme.Text,
                    new Rectangle(r.X + Theme.P(18), r.Y + Theme.P(64), Theme.P(210), Theme.P(26)), Theme.Left);
                Theme.DrawText(g, ModeDesc(mode), Theme.Ui(8.5f, false), Theme.Muted,
                    new Rectangle(r.X + Theme.P(18), r.Y + Theme.P(96), Theme.P(224), Theme.P(20)), Theme.Left);
            }
        }

        private void DrawModeGlyph(Graphics g, int mode, Rectangle circle, Color accent)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Theme.IsDark ? 26 : 18, accent)))
            {
                g.FillEllipse(b, circle);
            }
            using (Pen border = new Pen(Color.FromArgb(Theme.IsDark ? 72 : 54, accent), Theme.SW(1)))
            {
                g.DrawEllipse(border, circle);
            }

            float cx = circle.X + circle.Width / 2f;
            float cy = circle.Y + circle.Height / 2f;
            if (mode == 2)
            {
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddBezier(cx - Theme.P(7), cy + Theme.P(6), cx - Theme.P(8), cy - Theme.P(7),
                        cx + Theme.P(2), cy - Theme.P(10), cx + Theme.P(8), cy - Theme.P(2));
                    p.AddBezier(cx + Theme.P(8), cy - Theme.P(2), cx + Theme.P(8), cy + Theme.P(8),
                        cx - Theme.P(2), cy + Theme.P(10), cx - Theme.P(7), cy + Theme.P(6));
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Theme.IsDark ? 45 : 30, accent)))
                    {
                        g.FillPath(b, p);
                    }
                    using (Pen pen = new Pen(accent, Theme.SW(1.7f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawPath(pen, p);
                    }
                }
            }
            else if (mode == 0)
            {
                using (Pen pen = new Pen(accent, Theme.SW(3f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, cx - Theme.P(7), cy - Theme.P(3), cx - Theme.P(7), cy + Theme.P(4));
                    g.DrawLine(pen, cx, cy - Theme.P(8), cx, cy + Theme.P(8));
                    g.DrawLine(pen, cx + Theme.P(7), cy - Theme.P(1), cx + Theme.P(7), cy + Theme.P(5));
                }
            }
            else
            {
                DrawBolt(g, new RectangleF(cx - Theme.P(9), cy - Theme.P(11), Theme.P(18), Theme.P(22)), accent);
            }
        }

        private static void DrawBolt(Graphics g, RectangleF r, Color color)
        {
            PointF[] pts = new PointF[]
            {
                new PointF(r.X + r.Width * 0.58f, r.Y),
                new PointF(r.X + r.Width * 0.20f, r.Y + r.Height * 0.55f),
                new PointF(r.X + r.Width * 0.47f, r.Y + r.Height * 0.55f),
                new PointF(r.X + r.Width * 0.40f, r.Y + r.Height),
                new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.42f),
                new PointF(r.X + r.Width * 0.54f, r.Y + r.Height * 0.42f)
            };
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
        }

        private void DrawFooter(Graphics g)
        {
            bool ok = _status != null && _status.Connected;
            Color statusColor = ok ? Theme.Green : Theme.Red;

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(24), Theme.P(330), Theme.P(836), Theme.P(330));
            }

            using (SolidBrush b = new SolidBrush(statusColor))
            {
                g.FillEllipse(b, R(24, 350, 7, 7));
            }
            string status = ok
                ? "已连接  ·  固件接口正常"
                : (_status == null ? "正在连接固件接口…"
                    : (string.IsNullOrEmpty(_status.Error) ? "固件接口不可用" : _status.Error));
            Theme.DrawText(g, status, Theme.Ui(8.25f, false), ok ? Theme.Text : Theme.Red,
                R(38, 340, 520, 22), Theme.Left);

            if (!string.IsNullOrEmpty(_toast) && _toastUntil > DateTime.Now)
            {
                Theme.DrawText(g, _toast, Theme.Ui(7.5f, false), _toastError ? Theme.Red : Theme.Muted,
                    R(38, 366, 520, 16), Theme.Left);
            }
            else
            {
#if LITE
                string hint = "右键查看更多";
#else
                string hint = _settings.HotkeyEnabled
                    ? "热键 " + _settings.HotkeyText + "  ·  右键查看更多"
                    : "快捷键 1 / 2 / 3  ·  右键查看更多";
#endif
                Theme.DrawText(g, hint, Theme.Ui(7.5f, false), Theme.Faint,
                    R(38, 366, 560, 16), Theme.Left);
            }

#if !LITE
            DrawAutostart(g);
#endif
        }

        private void DrawAutostart(Graphics g)
        {
            bool hover = _hover == "autostart";
            Color labelColor = _autoEnabled ? Theme.Text : (hover ? Theme.Text : Theme.Muted);
            Theme.DrawText(g, "开机自启", Theme.Ui(9f, false), labelColor, R(696, 340, 86, 36), Theme.Left);

            Rectangle track = R(790, 350, 32, 16);
            Rectangle knob = R(792 + (_autoEnabled ? 14 : 2), 352, 12, 12);
            using (GraphicsPath tp = Theme.Round(track, Theme.P(8)))
            using (SolidBrush tb = new SolidBrush(_autoEnabled ? Theme.Green : Theme.Border))
            {
                g.FillPath(tb, tp);
            }
            using (SolidBrush kb = new SolidBrush(_autoEnabled ? Color.White : Theme.Muted))
            {
                g.FillEllipse(kb, knob);
            }
        }

        private void DrawFocus(Graphics g)
        {
            if (string.IsNullOrEmpty(_focus)) return;
            Rectangle r;
            if (!TryGetRect(_focus, out r)) return;
            r.Inflate(-Theme.P(3), -Theme.P(3));
            using (Pen pen = new Pen(Color.FromArgb(Theme.IsDark ? 150 : 110, Theme.Text), Theme.SW(1)))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 3f, 3f };
                using (GraphicsPath p = Theme.Round(r, Theme.P(8)))
                {
                    g.DrawPath(pen, p);
                }
            }
        }

        private bool TryGetRect(string id, out Rectangle r)
        {
            r = Rectangle.Empty;
            if (id.StartsWith("mode"))
            {
                int idx;
                if (int.TryParse(id.Substring(4), out idx) && idx >= 0 && idx < 3)
                {
                    r = CardRect(idx);
                    return true;
                }
                return false;
            }
#if !LITE
            if (id == "autostart")
            {
                r = R(684, 336, 152, 42);
                return true;
            }
#endif
            return false;
        }

        private string HitTest(Point p)
        {
#if LITE
            if (R(764, 16, 32, 30).Contains(p)) return "min";
#else
            if (R(726, 16, 32, 30).Contains(p)) return "min";
            if (R(764, 16, 32, 30).Contains(p)) return "settings";
#endif
            if (R(802, 16, 32, 30).Contains(p)) return "close";
            for (int i = 0; i < 3; i++)
            {
                if (CardRect(i).Contains(p)) return "mode" + i;
            }
#if !LITE
            if (R(684, 336, 152, 42).Contains(p)) return "autostart";
#endif
            return "";
        }

        private static Rectangle CardRect(int index)
        {
            return R(24 + index * 276, 160, 260, 150);
        }

        private static Rectangle R(float x, float y, float w, float h)
        {
            return new Rectangle(Theme.P(x), Theme.P(y), Theme.P(w), Theme.P(h));
        }

        private static Color AccentFor(int mode)
        {
            switch (mode)
            {
                case 1: return Theme.AccentPerformance;
                case 2: return Theme.AccentSilent;
                case 0: return Theme.AccentBalanced;
                default: return Theme.Blue;
            }
        }

        private static string ModeTitle(int mode)
        {
            switch (mode)
            {
                case 1: return "狂暴模式";
                case 2: return "静音模式";
                default: return "均衡模式";
            }
        }

        private static string ModeDesc(int mode)
        {
            switch (mode)
            {
                case 1: return "满血性能，风扇全开";
                case 2: return "安静省电，适合夜间";
                default: return "性能与噪音平衡";
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(HitTest(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover("");
            if (_pressed != "")
            {
                _pressed = "";
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = HitTest(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            string id = HitTest(e.Location);
            if (id == _pressed && !string.IsNullOrEmpty(id))
            {
                _pressed = "";
                Activate(id);
            }
            else
            {
                _pressed = "";
                Invalidate();
            }
        }

        private void SetHover(string id)
        {
            if (id == _hover) return;
            _hover = id;
            Cursor = string.IsNullOrEmpty(id) ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        private void Activate(string id)
        {
            if (_busy && id != "close" && id != "min") return;
            if (id.StartsWith("mode"))
            {
                int idx;
                if (int.TryParse(id.Substring(4), out idx) && idx >= 0 && idx < 3)
                {
                    SwitchMode(DisplayModes[idx]);
                }
                return;
            }
            switch (id)
            {
                case "min":
                    WindowState = FormWindowState.Minimized;
                    break;
                case "close":
                    Close();
                    break;
#if !LITE
                case "settings":
                    OpenSettings();
                    break;
                case "autostart":
                    ToggleAutostart();
                    break;
#endif
            }
        }

        private void SwitchMode(int mode)
        {
            if (_busy) return;
            if (_status != null && _status.Connected && _status.CurrentMode == mode)
            {
                ShowToast("当前已是" + ModeTitle(mode), false);
                return;
            }
            _busy = true;
            Cursor = Cursors.WaitCursor;
            Invalidate();
            try
            {
                int result = PowerSwitch.SetMode(mode);
                System.Threading.Thread.Sleep(120);
                _status = PowerSwitch.GetStatus();
                if (!_status.Connected)
                {
                    ShowToast("切换后无法读取状态：" + _status.Error, true);
                }
                else if (_status.CurrentMode == mode)
                {
                    if (result == 0) NotifyModeSwitched(mode);
                    else ShowToast("已切换到“" + ModeTitle(mode) + "”（固件返回 " + result + "）", false);
                }
                else
                {
                    ShowToast("固件未确认切换，当前仍为" + _status.ModeName, true);
                }
            }
            catch (Exception ex)
            {
                ShowToast("切换失败：" + ex.Message, true);
                Log.Write("SwitchMode failed: " + ex);
            }
            finally
            {
                _busy = false;
                Cursor = string.IsNullOrEmpty(_hover) ? Cursors.Default : Cursors.Hand;
                UpdateTray();
                Invalidate();
            }
        }

        internal void SetPreviewStatus(PowerStatus st)
        {
            _preview = true;
            _status = st;
            _timer.Stop();
        }

        internal void CaptureMenu(string path)
        {
            BuildFormMenu();
            _formMenu.Show(this, new Point(Theme.P(560), Theme.P(72)));
            Application.DoEvents();
            System.Threading.Thread.Sleep(160);
            Application.DoEvents();
            Rectangle b = _formMenu.Bounds;
            Rectangle cap = new Rectangle(b.X - Theme.P(10), b.Y - Theme.P(10),
                b.Width + Theme.P(20), b.Height + Theme.P(20));
            using (Bitmap bmp = new Bitmap(cap.Width, cap.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(cap.Location, Point.Empty, cap.Size);
                bmp.Save(path, ImageFormat.Png);
            }
            _formMenu.Close();
        }

        internal void ExitNow()
        {
            _exit = true;
            Close();
        }

        private void RefreshStatus(bool manual)
        {
            if (_busy) return;
            _status = PowerSwitch.GetStatus();
            if (_status != null && !_status.Connected) Log.Write("Status error: " + _status.Error);
            if (manual)
            {
                ShowToast(_status != null && _status.Connected ? "状态已刷新" : (_status == null ? "读取失败" : _status.Error),
                    _status == null || !_status.Connected);
            }
            UpdateTray();
            Invalidate();
        }

        private void ToggleAutostart()
        {
            string error;
            bool ok;
            if (_autoEnabled) ok = Autostart.Disable(out error);
            else ok = Autostart.Enable(out error);
            if (ok)
            {
                _autoEnabled = !_autoEnabled;
                ShowToast(_autoEnabled ? "已开启开机自启（计划任务）" : "已关闭开机自启", false);
            }
            else
            {
                ShowToast("开机自启设置失败：" + error, true);
            }
            UpdateTray();
            Invalidate();
        }

        private void ShowToast(string message, bool error)
        {
            _toast = message;
            _toastError = error;
            _toastUntil = DateTime.Now.AddSeconds(4);
            _toastTimer.Start();
            Invalidate();
        }

        private void ShowAbout()
        {
            using (AboutForm f = new AboutForm(_status))
            {
                f.ShowDialog(this);
            }
        }

        private void NotifyModeSwitched(int mode)
        {
            string message = "已切换到“" + ModeTitle(mode) + "”";
            ShowToast(message, false);
#if !LITE
            if (!_preview) ToastForm.Show(this, message);
#endif
        }

#if !LITE
        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _settings.CloseToTray = f.Settings.CloseToTray;
                _settings.ShowTray = f.Settings.ShowTray;
                _settings.HotkeyEnabled = f.Settings.HotkeyEnabled;
                _settings.HotkeyModifiers = f.Settings.HotkeyModifiers;
                _settings.HotkeyKey = f.Settings.HotkeyKey;
                _settings.Validate();
                _settings.Save();
                ApplyTray();
                bool hotkeyOk = RegisterHotkey();
                _autoEnabled = Autostart.IsEnabled();
                UpdateMenuChecks();
                Invalidate();
                if (_settings.HotkeyEnabled && !hotkeyOk)
                {
                    ShowToast("设置已保存，但热键注册失败，可能已被其他程序占用。", true);
                }
                else
                {
                    ShowToast("设置已保存", false);
                }
            }
        }
#endif

        private bool RegisterHotkey()
        {
#if LITE
            return false;
#else
            UnregisterHotkey();
            if (!_settings.HotkeyEnabled || !IsHandleCreated) return false;
            _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId, _settings.HotkeyModifiers, _settings.HotkeyKey);
            if (!_hotkeyRegistered)
            {
                Log.Write("Hotkey register failed: " + _settings.HotkeyText);
            }
            return _hotkeyRegistered;
#endif
        }

        private void UnregisterHotkey()
        {
#if !LITE
            if (_hotkeyRegistered && IsHandleCreated)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
            }
            _hotkeyRegistered = false;
#endif
        }

        private void CycleMode()
        {
            int index = _status == null ? -1 : Array.IndexOf(DisplayModes, _status.CurrentMode);
            int next = DisplayModes[index < 0 ? 0 : (index + 1) % DisplayModes.Length];
            SwitchMode(next);
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            if (!_balloonShown && _tray != null)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(2500, "imini 控制中心", "程序仍在托盘运行，双击图标可重新打开。", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(RestoreFromTray));
                return;
            }
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            RefreshStatus(false);
        }

        private void BuildFormMenu()
        {
            _formMenu = new ModernMenu();
            _formMenu.Add("刷新状态", delegate { RefreshStatus(true); });
#if !LITE
            _miAutoForm = _formMenu.AddCheck("开机自启", delegate { ToggleAutostart(); });
#endif
            _formMenu.AddSeparator();
#if !LITE
            _formMenu.Add("设置…", delegate { OpenSettings(); });
#endif
            _formMenu.Add("关于", delegate { ShowAbout(); });
            _formMenu.Add("退出", delegate { ExitApplication(); });
            _formMenu.Opening += delegate { UpdateMenuChecks(); };
            ContextMenuStrip = _formMenu;
        }

        private void ApplyTray()
        {
#if LITE
            DisposeTray();
#else
            if (!_settings.ShowTray)
            {
                DisposeTray();
                return;
            }
            if (_tray == null) BuildTray();
            else UpdateTray();
#endif
        }

        private void DisposeTray()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
        }

        private void BuildTray()
        {
            if (_tray != null) return;
            _tray = new NotifyIcon();
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                _tray.Icon = ico != null ? ico : SystemIcons.Application;
            }
            catch
            {
                _tray.Icon = SystemIcons.Application;
            }
            _tray.Text = "imini 控制中心";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { RestoreFromTray(); };

            _trayMenu = new ModernMenu();
            _trayMenu.AddBold("打开控制中心", delegate { RestoreFromTray(); });
            _trayMenu.AddSeparator();

            _miSilent = _trayMenu.AddCheck("静音模式", delegate { SwitchMode(2); });
            _miBalanced = _trayMenu.AddCheck("均衡模式", delegate { SwitchMode(0); });
            _miPerformance = _trayMenu.AddCheck("狂暴模式", delegate { SwitchMode(1); });
            _trayMenu.AddSeparator();

#if !LITE
            _miAutoTray = _trayMenu.AddCheck("开机自启", delegate { ToggleAutostart(); });
#endif
            _trayMenu.Add("刷新状态", delegate { RefreshStatus(true); });
#if !LITE
            _trayMenu.Add("设置…", delegate { OpenSettings(); });
#endif
            _trayMenu.Add("关于", delegate { ShowAbout(); });
            _trayMenu.AddSeparator();
            _trayMenu.Add("退出", delegate { ExitApplication(); });
            _trayMenu.Opening += delegate { UpdateMenuChecks(); };

            _tray.ContextMenuStrip = _trayMenu;
        }

        private void ExitApplication()
        {
            _exit = true;
            Close();
        }

        private void UpdateMenuChecks()
        {
            bool ok = _status != null && _status.Connected;
            if (_miSilent != null)
            {
                _miSilent.Checked = ok && _status.CurrentMode == 2;
                _miBalanced.Checked = ok && _status.CurrentMode == 0;
                _miPerformance.Checked = ok && _status.CurrentMode == 1;
            }
            if (_miAutoTray != null) _miAutoTray.Checked = _autoEnabled;
            if (_miAutoForm != null) _miAutoForm.Checked = _autoEnabled;
        }

        private void UpdateTray()
        {
            UpdateMenuChecks();
            if (_tray == null) return;
            bool ok = _status != null && _status.Connected;
            string text = "imini 控制中心 · " + (ok ? _status.ModeName : "未连接");
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Tab)
            {
                CycleFocus(1);
                return true;
            }
            if (keyData == (Keys.Tab | Keys.Shift))
            {
                CycleFocus(-1);
                return true;
            }
            if (keyData == Keys.Escape)
            {
                _focus = "";
                Invalidate();
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                if (!string.IsNullOrEmpty(_focus))
                {
                    Activate(_focus);
                    return true;
                }
            }
#if !LITE
            if (keyData == Keys.D1 || keyData == Keys.NumPad1) { SwitchMode(2); return true; }
            if (keyData == Keys.D2 || keyData == Keys.NumPad2) { SwitchMode(0); return true; }
            if (keyData == Keys.D3 || keyData == Keys.NumPad3) { SwitchMode(1); return true; }
#endif
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CycleFocus(int direction)
        {
            int idx = Array.IndexOf(FocusOrder, _focus);
            if (idx < 0) idx = direction > 0 ? 0 : FocusOrder.Length - 1;
            else idx = (idx + direction + FocusOrder.Length) % FocusOrder.Length;
            _focus = FocusOrder[idx];
            Invalidate();
        }
    }
}
