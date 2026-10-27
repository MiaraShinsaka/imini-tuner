using System;
using System.IO;
using System.Text;

namespace ImimiCC
{
    internal static class Log
    {
        private static readonly object _lock = new object();
        private static string _path;

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iminiControlCenter");
            }
        }

        public static string FilePath
        {
            get
            {
                if (_path == null)
                {
                    _path = Path.Combine(DirectoryPath, "app.log");
                }
                return _path;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string old = Path.Combine(DirectoryPath, "app.old.log");
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try { File.Move(FilePath, old); } catch { }
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\r\n";
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
