using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class ToastForm : Form
    {
        private static ToastForm _current;
        private readonly Timer _timer;
        private readonly Timer _topTimer;
        private Screen _screen;
        private bool _capture;

        private ToastForm(string message)
        {
            Text = "iMini Tuner";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Panel;
            DoubleBuffered = true;
            ClientSize = new Size(Theme.P(330), Theme.P(84));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Message = message;

            _timer = new Timer();
            _timer.Interval = 4500;
            _timer.Tick += delegate { Close(); };

            _topTimer = new Timer();
            _topTimer.Interval = 400;
            _topTimer.Tick += delegate
            {
                Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0,
                    0x0001 | 0x0002 | 0x0010 | 0x0040);
            };
        }

        public string Message { get; private set; }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindow(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_capture)
            {
                Location = new Point(-4000, -4000);
                return;
            }
            Rectangle wa = (_screen ?? Screen.FromControl(this)).WorkingArea;
            Location = new Point(wa.Right - Width - Theme.P(22), wa.Bottom - Height - Theme.P(22));
            Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0040 | 0x0010);
            Native.BringWindowToTop(Handle);
            _timer.Start();
            _topTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _topTimer.Stop();
            _topTimer.Dispose();
            if (_current == this) _current = null;
            base.OnFormClosed(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Theme.P(12)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Panel))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(Theme.Border, Theme.SW(1)))
                {
                    g.DrawPath(pen, p);
                }
            }

            using (SolidBrush b = new SolidBrush(Theme.Green))
            {
                g.FillEllipse(b, Theme.P(18), Theme.P(20), Theme.P(24), Theme.P(24));
            }
            using (Pen pen = new Pen(Color.White, Theme.SW(1.8f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cx = Theme.P(30);
                float cy = Theme.P(32);
                g.DrawLines(pen, new PointF[] {
                    new PointF(cx - Theme.P(5), cy),
                    new PointF(cx - Theme.P(1), cy + Theme.P(4)),
                    new PointF(cx + Theme.P(6), cy - Theme.P(5))
                });
            }

            Theme.DrawText(g, "iMini Tuner", Theme.Ui(7.5f, false), Theme.Faint,
                new Rectangle(Theme.P(54), Theme.P(16), Theme.P(250), Theme.P(16)), Theme.Left);
            Theme.DrawText(g, Message, Theme.Ui(10f, true), Theme.Text,
                new Rectangle(Theme.P(54), Theme.P(34), Theme.P(252), Theme.P(34)), Theme.Wrap);

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(54), Theme.P(70), Theme.P(306), Theme.P(70));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Close();
        }

        internal static ToastForm CreateForCapture(string message)
        {
            ToastForm f = new ToastForm(message);
            f._capture = true;
            return f;
        }

        internal static ToastForm Create(string message)
        {
            return new ToastForm(message);
        }

        public static void Show(Form owner, string message)
        {
            if (_current != null)
            {
                try { _current.Close(); } catch { }
                _current = null;
            }
            ToastForm f = new ToastForm(message);
            _current = f;
            if (owner != null) f._screen = Screen.FromControl(owner);
            f.Show();
        }
    }
}
