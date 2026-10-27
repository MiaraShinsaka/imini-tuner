using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class AboutForm : Form
    {
        private readonly PowerStatus _status;
        private string _hover = "";
        private string _pressed = "";
        private string _toast = "";
        private DateTime _toastUntil = DateTime.MinValue;
        private readonly Timer _toastTimer;

        public AboutForm(PowerStatus status)
        {
            _status = status;
            Text = "关于 imini 控制中心";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            ClientSize = new Size(Theme.P(520), Theme.P(345));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            _toastTimer = new Timer();
            _toastTimer.Interval = 1000;
            _toastTimer.Tick += delegate
            {
                if (_toastUntil <= DateTime.Now) _toast = "";
                Invalidate();
            };
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
            Theme.ApplyWindow(Handle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, Theme.Bg, Theme.Bg2, 90f))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            Rectangle logo = R(28, 16, 32, 32);
            using (GraphicsPath p = Theme.Round(logo, Theme.P(9)))
            using (LinearGradientBrush b = new LinearGradientBrush(logo, Theme.Blue, Theme.AccentSilent, 45f))
            {
                g.FillPath(b, p);
            }
            PointF[] bolt = new PointF[]
            {
                new PointF(Theme.P(43), Theme.P(23)),
                new PointF(Theme.P(36), Theme.P(33)),
                new PointF(Theme.P(42), Theme.P(33)),
                new PointF(Theme.P(40), Theme.P(42)),
                new PointF(Theme.P(48), Theme.P(31)),
                new PointF(Theme.P(42), Theme.P(31))
            };
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                g.FillPolygon(b, bolt);
            }

            Theme.DrawText(g, "imini 控制中心", Theme.Ui(13.5f, true), Theme.Text, R(70, 12, 300, 28), Theme.Left);
            Theme.DrawText(g, "v1.1.0  ·  非官方工具", Theme.Ui(7.5f, false), Theme.Muted,
                R(70, 38, 300, 16), Theme.Left);
            DrawHeaderClose(g, R(468, 16, 32, 30));

            Theme.DrawText(g, "关于", Theme.Ui(11.25f, true), Theme.Text, R(28, 72, 260, 24), Theme.Left);

            DrawRow(g, 104, "适用机型", "机械革命 imini Pro 830（IMINI-ARB32H-HPT）", Theme.Text);
            DrawRow(g, 130, "控制接口", "ACPI-WMI  ·  PowerSwitchInterface", Theme.Text);
            string state = _status != null && _status.Connected
                ? "已连接  ·  " + _status.ModeName
                : (_status == null ? "未知" : _status.Error);
            DrawRow(g, 156, "当前状态", state, _status != null && _status.Connected ? Theme.Green : Theme.Red);

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(28), Theme.P(184), Theme.P(492), Theme.P(184));
            }

            Theme.DrawText(g,
                "调用固件 ACPI-WMI 接口，与官方控制中心同一通道。切换立即生效，无需重启。",
                Theme.Ui(8.75f, false), Theme.Muted, R(28, 198, 464, 40), Theme.Wrap);

            Theme.DrawText(g,
                "非官方工具，按“现状”提供。因使用本程序造成的风险由使用者自行承担。",
                Theme.Ui(8.25f, false), Theme.Faint, R(28, 246, 464, 24), Theme.Wrap);

            if (!string.IsNullOrEmpty(_toast) && _toastUntil > DateTime.Now)
            {
                Theme.DrawText(g, _toast, Theme.Ui(8.25f, false), Theme.Muted, R(28, 268, 250, 20), Theme.Left);
            }

            DrawButton(g, R(300, 296, 100, 36), "copy", "复制诊断");
            DrawButton(g, R(410, 296, 82, 36), "close", "关闭");
        }

        private void DrawRow(Graphics g, float y, string label, string value, Color valueColor)
        {
            Theme.DrawText(g, label, Theme.Ui(8.25f, false), Theme.Faint, R(28, y, 72, 20), Theme.Left);
            Theme.DrawText(g, value, Theme.Ui(8.25f, false), valueColor, R(104, y, 388, 20), Theme.Left);
        }

        private void DrawButton(Graphics g, Rectangle r, string id, string text)
        {
            bool hover = _hover == id;
            bool down = _pressed == id;
            Color fill = Theme.Panel;
            if (hover) fill = Theme.PanelHover;
            if (down) fill = Theme.Blend(fill, Color.Black, 0.10f);
            using (GraphicsPath p = Theme.Round(r, Theme.P(10)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border, Theme.SW(1))) g.DrawPath(pen, p);
            }
            Theme.DrawText(g, text, Theme.Ui(9f, false), hover ? Theme.Text : Theme.Muted, r, Theme.Center);
        }

        private void DrawHeaderClose(Graphics g, Rectangle r)
        {
            bool hover = _hover == "x";
            if (hover)
            {
                using (GraphicsPath p = Theme.Round(r, Theme.P(8)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, Theme.Red)))
                {
                    g.FillPath(b, p);
                }
            }
            using (Pen pen = new Pen(hover ? Color.White : Theme.Muted, Theme.SW(1.4f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                g.DrawLine(pen, cx - Theme.P(4), cy - Theme.P(4), cx + Theme.P(4), cy + Theme.P(4));
                g.DrawLine(pen, cx + Theme.P(4), cy - Theme.P(4), cx - Theme.P(4), cy + Theme.P(4));
            }
        }

        private static Rectangle R(float x, float y, float w, float h)
        {
            return new Rectangle(Theme.P(x), Theme.P(y), Theme.P(w), Theme.P(h));
        }

        private string HitTest(Point p)
        {
            if (R(468, 16, 32, 30).Contains(p)) return "x";
            if (R(300, 296, 100, 36).Contains(p)) return "copy";
            if (R(410, 296, 82, 36).Contains(p)) return "close";
            return "";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = string.IsNullOrEmpty(h) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = "";
            _pressed = "";
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = HitTest(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string id = HitTest(e.Location);
            if (id == _pressed)
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

        private void Activate(string id)
        {
            switch (id)
            {
                case "x":
                case "close":
                    Close();
                    break;
                case "copy":
                    try
                    {
                        Clipboard.SetText(PowerSwitch.Diagnostics());
                        Toast("诊断信息已复制到剪贴板");
                    }
                    catch (Exception ex)
                    {
                        Toast("复制失败：" + ex.Message);
                    }
                    break;
            }
        }

        private void Toast(string message)
        {
            _toast = message;
            _toastUntil = DateTime.Now.AddSeconds(4);
            _toastTimer.Start();
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x001A)
            {
                bool dark = Theme.ReadSystemDark();
                if (dark != Theme.IsDark)
                {
                    Theme.Apply(dark);
                    BackColor = Theme.Bg;
                    Theme.ApplyWindow(Handle);
                    Invalidate();
                }
            }
            if (m.Msg == 0x0084)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    int sx = (short)((long)m.LParam & 0xFFFF);
                    int sy = (short)(((long)m.LParam >> 16) & 0xFFFF);
                    Point client = PointToClient(new Point(sx, sy));
                    if (client.Y < Theme.P(60) && string.IsNullOrEmpty(HitTest(client)))
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
    }
}
