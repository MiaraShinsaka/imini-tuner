using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _draft;
        private string _hover = "";
        private string _pressed = "";
        private bool _capturing;
        private string _captureError = "";

        public SettingsForm(AppSettings current)
        {
            _draft = current == null ? new AppSettings() : current.Clone();
            Text = "设置 - imini 控制中心";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size(Theme.P(600), Theme.P(500));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
        }

        public AppSettings Settings
        {
            get { return _draft; }
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

            Rectangle logo = R(24, 16, 30, 30);
            using (GraphicsPath p = Theme.Round(logo, Theme.P(9)))
            using (LinearGradientBrush b = new LinearGradientBrush(logo, Theme.Blue, Theme.AccentSilent, 45f))
            {
                g.FillPath(b, p);
            }
            DrawBolt(g, new RectangleF(Theme.P(30), Theme.P(22), Theme.P(18), Theme.P(18)), Color.White);

            Theme.DrawText(g, "设置", Theme.Ui(13.5f, true), Theme.Text, R(64, 12, 300, 28), Theme.Left);
            Theme.DrawText(g, "imini 控制中心", Theme.Ui(7.5f, false), Theme.Muted, R(64, 38, 300, 16), Theme.Left);
            DrawHeaderClose(g, R(548, 16, 32, 30));

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(24), Theme.P(66), Theme.P(576), Theme.P(66));
                g.DrawLine(pen, Theme.P(24), Theme.P(226), Theme.P(576), Theme.P(226));
                g.DrawLine(pen, Theme.P(24), Theme.P(424), Theme.P(576), Theme.P(424));
            }

            Theme.DrawText(g, "快捷键", Theme.Ui(9.5f, true), Theme.Text, R(28, 80, 300, 22), Theme.Left);
            Theme.DrawText(g, "循环切换模式", Theme.Ui(9.25f, false), Theme.Text, R(28, 106, 300, 22), Theme.Left);
            Theme.DrawText(g, "在静音 → 均衡 → 狂暴之间轮换。", Theme.Ui(8.25f, false), Theme.Muted,
                R(28, 128, 500, 20), Theme.Left);

            Rectangle field = R(28, 154, 250, 40);
            bool fieldHover = _hover == "hotkey";
            Color fieldBorder = _capturing ? Theme.Blue : (fieldHover ? Theme.Blend(Theme.Border, Theme.Blue, 0.55f) : Theme.Border);
            using (GraphicsPath p = Theme.Round(field, Theme.P(9)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillPath(b, p);
                using (Pen pen = new Pen(fieldBorder, Theme.SW(_capturing ? 1.6f : 1f))) g.DrawPath(pen, p);
            }
            string hotkeyText = _capturing ? "请按下组合键…" : _draft.HotkeyText;
            Theme.DrawText(g, hotkeyText, Theme.Ui(9.25f, true), _capturing ? Theme.Blue : Theme.Text,
                new Rectangle(field.X + Theme.P(12), field.Y, field.Width - Theme.P(20), field.Height), Theme.Left);

            DrawButton(g, R(288, 154, 92, 40), "defaults", "恢复默认", false);

            string hint = !string.IsNullOrEmpty(_captureError)
                ? _captureError
                : (_capturing ? "按下组合键，Esc 取消；需包含 Ctrl 或 Alt。" : "点击输入框后按下组合键；需包含 Ctrl 或 Alt。");
            Theme.DrawText(g, hint, Theme.Ui(7.75f, false), string.IsNullOrEmpty(_captureError) ? Theme.Faint : Theme.Red,
                R(28, 200, 540, 18), Theme.Left);

            Theme.DrawText(g, "窗口与托盘", Theme.Ui(9.5f, true), Theme.Text, R(28, 240, 300, 22), Theme.Left);

            DrawCheckRow(g, R(28, 264, 544, 52), "closeToTray", _draft.CloseToTray, _draft.ShowTray,
                "关闭窗口时最小化到托盘", _draft.ShowTray ? "关闭按钮只隐藏窗口，程序继续在托盘运行。" : "需要先启用托盘图标。");
            DrawCheckRow(g, R(28, 324, 544, 52), "showTray", _draft.ShowTray, true,
                "显示托盘图标", "关闭后不在通知区域显示；关闭窗口将直接退出。");

            Theme.DrawText(g, "切换模式后会在屏幕右下角显示通知。", Theme.Ui(7.75f, false), Theme.Faint,
                R(28, 392, 540, 18), Theme.Left);

            DrawButton(g, R(358, 444, 96, 38), "cancel", "取消", false);
            DrawButton(g, R(464, 444, 112, 38), "save", "保存设置", true);
        }

        private void DrawCheckRow(Graphics g, Rectangle row, string id, bool value, bool enabled, string title, string desc)
        {
            bool hover = enabled && _hover == id;
            if (hover)
            {
                using (GraphicsPath p = Theme.Round(row, Theme.P(8)))
                using (SolidBrush b = new SolidBrush(Theme.PanelHover)) g.FillPath(b, p);
            }

            Rectangle box = new Rectangle(row.X, row.Y + Theme.P(5), Theme.P(20), Theme.P(20));
            using (GraphicsPath p = Theme.Round(box, Theme.P(5)))
            {
                if (value && enabled)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Blue)) g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillPath(b, p);
                    using (Pen pen = new Pen(enabled ? Theme.Border : Theme.BorderSoft, Theme.SW(1))) g.DrawPath(pen, p);
                }
            }
            if (value && enabled)
            {
                using (Pen pen = new Pen(Color.White, Theme.SW(1.7f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    float cx = box.X + box.Width / 2f;
                    float cy = box.Y + box.Height / 2f;
                    g.DrawLines(pen, new PointF[] {
                        new PointF(cx - Theme.P(4), cy),
                        new PointF(cx - Theme.P(1), cy + Theme.P(3)),
                        new PointF(cx + Theme.P(5), cy - Theme.P(3))
                    });
                }
            }

            Color titleColor = enabled ? Theme.Text : Theme.Faint;
            Color descColor = enabled ? Theme.Muted : Theme.Faint;
            Theme.DrawText(g, title, Theme.Ui(9f, false), titleColor,
                new Rectangle(row.X + Theme.P(32), row.Y, row.Width - Theme.P(38), Theme.P(22)), Theme.Left);
            Theme.DrawText(g, desc, Theme.Ui(7.75f, false), descColor,
                new Rectangle(row.X + Theme.P(32), row.Y + Theme.P(24), row.Width - Theme.P(38), Theme.P(18)), Theme.Left);
        }

        private void DrawButton(Graphics g, Rectangle r, string id, string text, bool primary)
        {
            bool hover = _hover == id;
            bool down = _pressed == id;
            Color fill = primary ? Theme.Blue : Theme.Panel;
            if (primary)
            {
                if (hover) fill = Theme.Blend(Theme.Blue, Color.White, 0.08f);
                if (down) fill = Theme.Blend(Theme.Blue, Color.Black, 0.10f);
            }
            else
            {
                if (hover) fill = Theme.PanelHover;
                if (down) fill = Theme.Blend(fill, Color.Black, 0.08f);
            }

            using (GraphicsPath p = Theme.Round(r, Theme.P(9)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!primary)
                {
                    using (Pen pen = new Pen(Theme.Border, Theme.SW(1))) g.DrawPath(pen, p);
                }
            }
            Theme.DrawText(g, text, Theme.Ui(8.75f, primary), primary ? Color.White : (hover ? Theme.Text : Theme.Muted),
                r, Theme.Center);
        }

        private void DrawHeaderClose(Graphics g, Rectangle r)
        {
            bool hover = _hover == "x";
            if (hover)
            {
                using (GraphicsPath p = Theme.Round(r, Theme.P(8)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, Theme.Red))) g.FillPath(b, p);
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
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }

        private static Rectangle R(float x, float y, float w, float h)
        {
            return new Rectangle(Theme.P(x), Theme.P(y), Theme.P(w), Theme.P(h));
        }

        private string HitTest(Point p)
        {
            if (R(548, 16, 32, 30).Contains(p)) return "x";
            if (R(28, 154, 250, 40).Contains(p)) return "hotkey";
            if (R(288, 154, 92, 40).Contains(p)) return "defaults";
            if (R(28, 264, 544, 52).Contains(p) && _draft.ShowTray) return "closeToTray";
            if (R(28, 324, 544, 52).Contains(p)) return "showTray";
            if (R(358, 444, 96, 38).Contains(p)) return "cancel";
            if (R(464, 444, 112, 38).Contains(p)) return "save";
            return "";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
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
            base.OnMouseLeave(e);
            _hover = "";
            _pressed = "";
            Invalidate();
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

        private void Activate(string id)
        {
            switch (id)
            {
                case "x":
                case "cancel":
                    DialogResult = DialogResult.Cancel;
                    Close();
                    break;
                case "save":
                    _draft.Validate();
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
                case "hotkey":
                    _capturing = true;
                    _captureError = "";
                    Invalidate();
                    break;
                case "defaults":
                    _draft.HotkeyModifiers = 0x0003;
                    _draft.HotkeyKey = (int)Keys.M;
                    _draft.HotkeyEnabled = true;
                    _capturing = false;
                    _captureError = "";
                    Invalidate();
                    break;
                case "showTray":
                    _draft.ShowTray = !_draft.ShowTray;
                    if (!_draft.ShowTray) _draft.CloseToTray = false;
                    Invalidate();
                    break;
                case "closeToTray":
                    if (_draft.ShowTray)
                    {
                        _draft.CloseToTray = !_draft.CloseToTray;
                        Invalidate();
                    }
                    break;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == Keys.Escape)
            {
                _capturing = false;
                _captureError = "";
                Invalidate();
                return true;
            }

            Keys key = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            if (key == Keys.None || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin)
            {
                return true;
            }
            if ((mods & (Keys.Control | Keys.Alt)) == 0)
            {
                _captureError = "组合键需要包含 Ctrl 或 Alt。";
                Invalidate();
                return true;
            }
            if (key == Keys.F4 && (mods & Keys.Alt) != 0)
            {
                _captureError = "Alt + F4 是系统关闭快捷键，请换一个。";
                Invalidate();
                return true;
            }

            int m = 0;
            if ((mods & Keys.Control) != 0) m |= 0x0002;
            if ((mods & Keys.Alt) != 0) m |= 0x0001;
            if ((mods & Keys.Shift) != 0) m |= 0x0004;
            _draft.HotkeyModifiers = m;
            _draft.HotkeyKey = (int)key;
            _draft.HotkeyEnabled = true;
            _capturing = false;
            _captureError = "";
            Invalidate();
            return true;
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
    }
}
