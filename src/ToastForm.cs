using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class ToastForm : Form
    {
        private const int WsExLayered = 0x00080000;
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
            ClientSize = new Size(Theme.P(330), Theme.P(84));
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
                // Layered: the card is rendered with per-pixel alpha, so the
                // rounded corners are genuinely transparent.  A plain opaque
                // borderless window makes DWM draw its own frame/shadow around
                // the window rectangle, which shows up as a hard black outline
                // on light backgrounds.
                cp.ExStyle |= WsExLayered;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_capture)
            {
                Location = new Point(-4000, -4000);
                Render();
                return;
            }
            Rectangle wa = (_screen ?? Screen.FromControl(this)).WorkingArea;
            Location = new Point(wa.Right - Width - Theme.P(22), wa.Bottom - Height - Theme.P(22));
            Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0040 | 0x0010);
            Render();
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
        }

        private void Render()
        {
            if (Handle == IntPtr.Zero) return;
            int w = Width;
            int h = Height;
            if (w <= 0 || h <= 0) return;

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.CompositingMode = CompositingMode.SourceOver;
                    PaintCard(g);
                }

                Premultiply(bmp);
                PushLayered(bmp);
            }
        }

        private void PaintCard(Graphics g)
        {
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

            DrawText(g, "iMini Tuner", Theme.Ui(7.5f, false), Theme.Faint,
                new Rectangle(Theme.P(54), Theme.P(16), Theme.P(250), Theme.P(16)), false);
            DrawText(g, Message, Theme.Ui(10f, true), Theme.Text,
                new Rectangle(Theme.P(54), Theme.P(34), Theme.P(252), Theme.P(34)), true);

            using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
            {
                g.DrawLine(pen, Theme.P(54), Theme.P(70), Theme.P(306), Theme.P(70));
            }
        }

        private static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, bool wrap)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericDefault))
            {
                sf.FormatFlags = StringFormatFlags.NoClip;
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = wrap ? StringAlignment.Near : StringAlignment.Center;
                if (!wrap)
                {
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                }
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.DrawString(s, f, b, r, sf);
                }
            }
        }

        /// <summary>
        /// UpdateLayeredWindow expects premultiplied BGRA pixels.  GDI+ draws
        /// straight alpha, so premultiply before handing the bitmap over.
        /// </summary>
        private static void Premultiply(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int i = 0; i < bytes; i += 4)
                {
                    int a = buffer[i + 3];
                    if (a == 255) continue;
                    if (a == 0)
                    {
                        buffer[i] = 0;
                        buffer[i + 1] = 0;
                        buffer[i + 2] = 0;
                        continue;
                    }
                    buffer[i] = (byte)(buffer[i] * a / 255);
                    buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                    buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
                }
                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void PushLayered(Bitmap bmp)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = Native.SelectObject(memDc, hBitmap);
            try
            {
                Native.POINT src = new Native.POINT();
                Native.POINT dst = new Native.POINT { X = Left, Y = Top };
                Native.SIZE size = new Native.SIZE { CX = bmp.Width, CY = bmp.Height };
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.SelectObject(memDc, oldBitmap);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
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
