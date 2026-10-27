using System;
using System.Drawing;
using System.Windows.Forms;
using ImimiCC;

internal static class ToastProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        string message = args.Length > 0 ? args[0] : "模式已切换";
        Theme.Initialize();
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
        {
            Theme.S = g.DpiX / 96f;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ToastForm toast = ToastForm.Create(message);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        toast.StartPosition = FormStartPosition.Manual;
        toast.Location = new Point(wa.Right - toast.Width - Theme.P(22), wa.Bottom - toast.Height - Theme.P(22));
        Application.Run(toast);
    }
}