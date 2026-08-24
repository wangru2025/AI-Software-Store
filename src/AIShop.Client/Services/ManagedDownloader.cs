using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

            var partialPath = targetPath + ".part";
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            long read = 0;
            long total = -1;
            var watch = Stopwatch.StartNew();
            var buffer = new byte[81920];

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (_paused)
                    {
                        progress.Report(Snapshot(read, total, watch, "下载已暂停"));
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }

                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (read > 0)
                        {
                            request.Headers.Range = new RangeHeaderValue(read, null);
                        }

                        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();

                            if (read > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                            {
                                read = 0;
                                if (File.Exists(partialPath))
                                {
                                    File.Delete(partialPath);
                                }
                            }

                            total = ResolveTotalLength(response, read);

                            using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var target = new FileStream(partialPath, read > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                while (true)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    if (_paused)
                                    {
                                        progress.Report(Snapshot(read, total, watch, "下载已暂停"));
                                        break;
                                    }

                                    var count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                                    if (count == 0)
                                    {
                                        break;
                                    }

                                    await target.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                                    read += count;
                                    progress.Report(Snapshot(read, total, watch, "正在下载软件包"));
                                }
                            }
                        }
                    }

                    if (_paused)
                    {
                        continue;
                    }

                    if (total <= 0 || read >= total)
                    {
                        break;
                    }
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(partialPath, targetPath);

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    progress.Report(new ProgressSnapshot { Percent = 98, Message = "正在检查软件包", BytesTransferred = read, TotalBytes = total, BytesPerSecond = Speed(read, watch) });
                    var actual = ComputeSha256(targetPath);
                    if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("软件包校验失败。");
                    }
                }

                progress.Report(new ProgressSnapshot { Percent = 100, Message = "下载完成", BytesTransferred = read, TotalBytes = total, BytesPerSecond = Speed(read, watch), IsCompleted = true });
                AppLog.Download("下载完成：" + targetPath);
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
                throw;
            }
        }

        private static ProgressSnapshot Snapshot(long read, long total, Stopwatch watch, string message)
        {
            return new ProgressSnapshot
            {
                Percent = Percent(read, total),
                Message = message,
                BytesTransferred = read,
                TotalBytes = total,
                BytesPerSecond = Speed(read, watch)
            };
        }

        private static long ResolveTotalLength(HttpResponseMessage response, long read)
        {
            if (response.Content.Headers.ContentRange != null && response.Content.Headers.ContentRange.Length.HasValue)
            {
                return response.Content.Headers.ContentRange.Length.Value;
            }

            if (response.Content.Headers.ContentLength.HasValue)
            {
                return read + response.Content.Headers.ContentLength.Value;
            }

            return -1;
        }

        private static int Percent(long read, long total)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, (int)(read * 100 / total)));
        }

        private static double Speed(long read, Stopwatch watch)
        {
            return read / Math.Max(0.001, watch.Elapsed.TotalSeconds);
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
