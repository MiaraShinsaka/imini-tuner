using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class ModernMenu : ContextMenuStrip
    {
        public ModernMenu()
        {
            Renderer = new ModernMenuRenderer();
            ShowImageMargin = false;
            ShowCheckMargin = false;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.25f, false);
            Padding = new Padding(Theme.P(6), Theme.P(6), Theme.P(6), Theme.P(12));
            MinimumSize = new Size(Theme.P(196), Theme.P(130));
            DropShadowEnabled = true;
            AllowItemReorder = false;
        }

        public ToolStripMenuItem Add(string text, EventHandler click)
        {
            return Add(text, click, false, false);
        }

        public ToolStripMenuItem AddBold(string text, EventHandler click)
        {
            return Add(text, click, true, false);
        }

        public ToolStripMenuItem AddCheck(string text, EventHandler click)
        {
            return Add(text, click, false, true);
        }

        private ToolStripMenuItem Add(string text, EventHandler click, bool bold, bool check)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Font = Theme.Ui(9.25f, bold);
            item.Padding = new Padding(Theme.P(1), Theme.P(3), Theme.P(8), Theme.P(3));
            item.CheckOnClick = false;
            item.AutoSize = true;
            if (click != null) item.Click += click;
            Items.Add(item);
            return item;
        }

        public void AddSeparator()
        {
            ToolStripSeparator sep = new ToolStripSeparator();
            sep.Margin = new Padding(Theme.P(10), Theme.P(4), Theme.P(10), Theme.P(4));
            Items.Add(sep);
        }

        public void RefreshTheme()
        {
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Invalidate();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            ApplyRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated) ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width, Height), Theme.P(12)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private sealed class ModernMenuRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Theme.Panel))
                {
                    e.Graphics.FillRectangle(b, e.ToolStrip.ClientRectangle);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (GraphicsPath p = Theme.Round(r, Theme.P(12)))
                using (Pen pen = new Pen(Theme.Border, Theme.SW(1)))
                {
                    e.Graphics.DrawPath(pen, p);
                }
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                ToolStripMenuItem item = e.Item as ToolStripMenuItem;
                Rectangle r = new Rectangle(Theme.P(4), Theme.P(1),
                    Math.Max(0, e.Item.Width - Theme.P(8)), Math.Max(0, e.Item.Height - Theme.P(2)));

                if (e.Item.Selected || e.Item.Pressed)
                {
                    Color fill = e.Item.Pressed ? Theme.Blend(Theme.PanelHover, Color.Black, 0.06f) : Theme.PanelHover;
                    using (GraphicsPath p = Theme.Round(r, Theme.P(8)))
                    using (SolidBrush b = new SolidBrush(fill))
                    {
                        e.Graphics.FillPath(b, p);
                    }
                }

                if (item != null && item.Checked)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Blue))
                    {
                        int d = Theme.P(6);
                        e.Graphics.FillEllipse(b, Theme.P(12), (e.Item.Height - d) / 2, d, d);
                    }
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color = e.Item.Enabled ? Theme.Text : Theme.Faint;
                int x = Theme.P(28);
                Rectangle r = new Rectangle(x, 0, Math.Max(0, e.Item.Width - x - Theme.P(10)), e.Item.Height);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (Pen pen = new Pen(Theme.BorderSoft, Theme.SW(1)))
                {
                    int y = e.Item.Height / 2;
                    e.Graphics.DrawLine(pen, Theme.P(14), y, e.Item.Width - Theme.P(14), y);
                }
            }
        }
    }
}
