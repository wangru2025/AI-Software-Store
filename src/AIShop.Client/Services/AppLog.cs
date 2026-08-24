using System;
using System.IO;

namespace AIShop.Client.Services
{
    public static class AppLog
    {
        private static readonly object SyncRoot = new object();
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AI软件商店",
            "Logs");

        public static void Client(string message)
        {
            Write("client.log", message, null);
        }

        public static void Download(string message)
        {
            Write("download.log", message, null);
        }

        public static void Install(string message)
        {
            Write("install.log", message, null);
        }

        public static void Update(string message)
        {
            Write("update.log", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("error.log", message, exception);
        }

        private static void Write(string fileName, string message, Exception exception)
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(BaseDir);
                var path = Path.Combine(BaseDir, fileName);
                using (var writer = File.AppendText(path))
                {
                    writer.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, message);
                    if (exception != null)
                    {
                        writer.WriteLine(exception);
                    }
                }
            }
        }
    }
}
