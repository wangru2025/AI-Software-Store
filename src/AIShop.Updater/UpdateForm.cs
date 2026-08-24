using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AIShop.Updater
{
    public sealed class UpdateForm : Form
    {
        private readonly UpdateArguments _args;
        private readonly TextBox _status = new TextBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _cancel = new Button();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public UpdateForm(UpdateArguments args)
        {
            _args = args;
            Text = "AI 软件商店更新";
            Width = 560;
            Height = 300;
            StartPosition = FormStartPosition.CenterScreen;

            _status.Multiline = true;
            _status.ReadOnly = true;
            _status.ScrollBars = ScrollBars.Vertical;
            _status.Left = 12;
            _status.Top = 12;
            _status.Width = 520;
            _status.Height = 180;
            Controls.Add(_status);

            _progress.Left = 12;
            _progress.Top = 205;
            _progress.Width = 520;
            Controls.Add(_progress);

            _cancel.Text = "取消";
            _cancel.Left = 432;
            _cancel.Top = 235;
            _cancel.Width = 100;
            _cancel.Click += (s, e) => _cts.Cancel();
            Controls.Add(_cancel);

            Load += async (s, e) => await RunAsync();
        }

        private async Task RunAsync()
        {
            try
            {
                if ((string.IsNullOrWhiteSpace(_args.Url) && string.IsNullOrWhiteSpace(_args.File)) || string.IsNullOrWhiteSpace(_args.TargetDir))
                {
                    throw new InvalidOperationException("更新参数不完整。");
                }

                var workDir = Path.Combine(Path.GetTempPath(), "AI软件商店", "updater");
                Directory.CreateDirectory(workDir);
                var zip = _args.File;
                if (string.IsNullOrWhiteSpace(zip))
                {
                    zip = Path.Combine(workDir, "update.zip");
                    await DownloadAsync(_args.Url, zip, _cts.Token);
                }
                else
                {
                    Append("正在检查更新包");
                    _progress.Value = 100;
                }
                if (!string.IsNullOrWhiteSpace(_args.Sha256) && !string.Equals(Hash(zip), _args.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("更新包校验失败。");
                }
                Append("正在安装更新");
                ExtractOverwrite(zip, _args.TargetDir);
                Append("更新完成");
                if (!string.IsNullOrWhiteSpace(_args.RestartExe) && File.Exists(_args.RestartExe))
                {
                    Process.Start(_args.RestartExe);
                    Close();
                    return;
                }
                _cancel.Enabled = false;
            }
            catch (OperationCanceledException)
            {
                Append("已取消");
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                Append("更新失败：" + ex.Message);
            }
        }

        private async Task DownloadAsync(string url, string target, CancellationToken token)
        {
            Append("正在下载更新包");
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                long read = 0;
                using (var source = await response.Content.ReadAsStreamAsync())
                using (var file = File.Create(target))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var count = await source.ReadAsync(buffer, 0, buffer.Length, token);
                        if (count == 0) break;
                        await file.WriteAsync(buffer, 0, count, token);
                        read += count;
                        if (total > 0)
                        {
                            _progress.Value = Math.Max(0, Math.Min(100, (int)(read * 100 / total)));
                        }
                    }
                }
            }
            _progress.Value = 100;
        }

        private void Append(string message)
        {
            _status.AppendText(message + Environment.NewLine);
            Log(message);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void ExtractOverwrite(string zipPath, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            var root = Path.GetFullPath(targetDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                        continue;
                    }

                    var targetPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("更新包包含不安全路径。");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    entry.ExtractToFile(targetPath, true);
                }
            }
        }

        private static void Log(string message)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AI软件商店", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
        }
    }
}
