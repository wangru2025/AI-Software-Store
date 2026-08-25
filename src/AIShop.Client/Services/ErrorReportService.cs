using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace AIShop.Client.Services
{
    public static class ErrorReportService
    {
        private static readonly object SyncRoot = new object();
        private static ApiCatalogService _catalog;
        private static int _uploading;

        public static void Configure(ApiCatalogService catalog)
        {
            lock (SyncRoot)
            {
                if (_catalog == null)
                {
                    AppLog.ErrorWritten += OnErrorWritten;
                }
                _catalog = catalog;
            }
        }

        private static void OnErrorWritten(string message, Exception exception)
        {
            if (!ClientSettingsStore.Current.AutoReportLogsOnError || _catalog == null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _uploading, 1) == 1)
            {
                return;
            }

            Task.Run(async () =>
            {
                string zipPath = null;
                try
                {
                    zipPath = CreateLogsZip();
                    if (zipPath != null)
                    {
                        await _catalog.UploadClientLogsAsync(zipPath, message ?? "").ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Client("自动上报日志失败：" + ex.Message);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(zipPath))
                    {
                        try { File.Delete(zipPath); } catch { }
                    }
                    Interlocked.Exchange(ref _uploading, 0);
                }
            });
        }

        private static string CreateLogsZip()
        {
            if (!Directory.Exists(AppPaths.LogsDir))
            {
                return null;
            }

            var tempDir = Path.Combine(AppPaths.TempRoot(), "log-reports");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, "logs-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(AppPaths.LogsDir, "*", SearchOption.AllDirectories))
                {
                    AddFile(zip, file);
                }
            }
            return zipPath;
        }

        private static void AddFile(ZipArchive zip, string file)
        {
            var relative = file.Substring(AppPaths.LogsDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var entry = zip.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
            using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var target = entry.Open())
            {
                source.CopyTo(target);
            }
        }
    }
}
