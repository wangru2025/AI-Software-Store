using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AIShop.Shared;

namespace AIShop.Client.Services
{
    public sealed class ManagedDownloader
    {
        private readonly HttpClient _http = new HttpClient();
        private volatile bool _paused;

        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }

        public async Task DownloadAsync(string url, string targetPath, string expectedSha256, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            AppLog.Download("开始下载：" + url);

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                long read = 0;

                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = File.Create(targetPath))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        while (_paused)
                        {
                            progress.Report(new ProgressSnapshot { Percent = Percent(read, total), Message = "下载已暂停" });
                            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                        }

                        var count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (count == 0)
                        {
                            break;
                        }

                        await target.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                        read += count;
                        progress.Report(new ProgressSnapshot
                        {
                            Percent = Percent(read, total),
                            Message = "正在下载软件包"
                        });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                progress.Report(new ProgressSnapshot { Percent = 98, Message = "正在检查软件包" });
                var actual = ComputeSha256(targetPath);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("软件包校验失败。");
                }
            }

            progress.Report(new ProgressSnapshot { Percent = 100, Message = "下载完成", IsCompleted = true });
            AppLog.Download("下载完成：" + targetPath);
        }

        private static int Percent(long read, long total)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, (int)(read * 100 / total)));
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
