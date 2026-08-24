using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class DownloadForm : Form
    {
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _state = new Label();
        private readonly Label _bytes = new Label();
        private readonly Label _speed = new Label();
        private readonly Label _elapsed = new Label();
        private readonly Button _pause = new Button();
        private readonly Button _cancel = new Button();
        private readonly System.Windows.Forms.Timer _elapsedTimer = new System.Windows.Forms.Timer();
        private readonly ManagedDownloader _downloader = new ManagedDownloader();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Func<ManagedDownloader, IProgress<ProgressSnapshot>, CancellationToken, Task> _operation;
        private readonly string _successTitle;
        private readonly string _successMessage;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private bool _paused;

        private DownloadForm(string title, string successMessage, Func<ManagedDownloader, IProgress<ProgressSnapshot>, CancellationToken, Task> operation)
        {
            _operation = operation;
            _successTitle = title;
            _successMessage = successMessage;
            Text = title;
            Width = 590;
            Height = 185;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _progress.Left = 12;
            _progress.Top = 12;
            _progress.Width = 550;
            _progress.Height = 24;
            Controls.Add(_progress);

            _state.Left = 12;
            _state.Top = 46;
            _state.Width = 550;
            Controls.Add(_state);

            _bytes.Left = 12;
            _bytes.Top = 70;
            _bytes.Width = 550;
            Controls.Add(_bytes);

            _speed.Left = 12;
            _speed.Top = 94;
            _speed.Width = 270;
            Controls.Add(_speed);

            _elapsed.Left = 292;
            _elapsed.Top = 94;
            _elapsed.Width = 270;
            Controls.Add(_elapsed);
            UpdateElapsed();

            _elapsedTimer.Interval = 1000;
            _elapsedTimer.Tick += (s, e) => UpdateElapsed();
            _elapsedTimer.Start();

            _pause.Text = "暂停";
            _pause.Left = 350;
            _pause.Top = 124;
            _pause.Width = 100;
            _pause.Click += (s, e) => TogglePause();
            Controls.Add(_pause);

            _cancel.Text = "取消";
            _cancel.Left = 462;
            _cancel.Top = 124;
            _cancel.Width = 100;
            _cancel.Click += (s, e) => CancelOperation();
            Controls.Add(_cancel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Hide();
                }
            };

            Load += async (s, e) => await RunAsync();
        }

        public static DownloadForm ForInstall(ApiCatalogService catalog, SoftwareItem item)
        {
            return ForInstall(catalog, item, false);
        }

        public static DownloadForm ForInstall(ApiCatalogService catalog, SoftwareItem item, bool isUpdate)
        {
            var title = (isUpdate ? "更新 " : "下载 ") + item.Name;
            var success = isUpdate ? "更新完成。" : "安装完成。";
            return new DownloadForm(title, success, async (downloader, progress, token) =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "AI软件商店", item.Id, item.Version);
                Directory.CreateDirectory(dir);
                var zip = Path.Combine(dir, item.Id + "-" + item.Version + ".zip");
                var url = catalog.BuildDownloadUrl(item.Id, item.Version);
                await downloader.DownloadAsync(url, zip, item.PackageSha256, AsIntermediateProgress(progress), token).ConfigureAwait(false);
                await ElevatedInstallWorker.InstallAsync(zip, progress, token).ConfigureAwait(false);
            });
        }

        public static DownloadForm ForUninstall(SoftwareItem item)
        {
            return new DownloadForm("卸载 " + item.Name, "卸载完成。", async (downloader, progress, token) =>
            {
                var installer = new PackageInstaller();
                await installer.UninstallAsync(item, progress, token).ConfigureAwait(false);
            });
        }

        public static DownloadForm ForClientUpdate(ClientUpdateInfo update)
        {
            return new DownloadForm("更新 AI 软件商店", null, async (downloader, progress, token) =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "AI软件商店", "client-update");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "AIShop.Client.Update.zip");
                await downloader.DownloadAsync(update.DownloadUrl, file, update.Sha256, progress, token).ConfigureAwait(false);
                AppLog.Update("客户端下载包下载完成：" + file);
                StartUpdater(file, update.Sha256);
                progress.Report(new ProgressSnapshot { Percent = 100, Message = "正在启动更新程序", IsCompleted = true });
                Application.Exit();
            });
        }

        private async Task RunAsync()
        {
            var progress = new Progress<ProgressSnapshot>(UpdateProgress);

            try
            {
                await _operation(_downloader, progress, _cts.Token);
                if (!_cts.IsCancellationRequested && !string.IsNullOrWhiteSpace(_successMessage))
                {
                    _pause.Enabled = false;
                    _cancel.Enabled = false;
                    MessageBox.Show(_successMessage, _successTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                if (_cts.IsCancellationRequested)
                {
                    Close();
                    return;
                }

                AppLog.Error("操作失败", ex);
                _pause.Enabled = false;
                _cancel.Enabled = false;
                MessageBox.Show(ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _elapsedTimer.Stop();
            if (!_cts.IsCancellationRequested && _cancel.Enabled)
            {
                _cts.Cancel();
            }

            base.OnFormClosing(e);
        }

        private void UpdateProgress(ProgressSnapshot snapshot)
        {
            _progress.Value = Math.Max(0, Math.Min(100, snapshot.Percent));
            _state.Text = "状态：" + (string.IsNullOrWhiteSpace(snapshot.Message) ? "正在处理" : snapshot.Message);
            var hasTransfer = snapshot.TotalBytes > 0 || snapshot.BytesTransferred > 0 || snapshot.BytesPerSecond > 0;
            _bytes.Visible = hasTransfer;
            _speed.Visible = hasTransfer;
            if (hasTransfer)
            {
                _bytes.Text = "大小：" + FormatProgress(snapshot.BytesTransferred, snapshot.TotalBytes);
                _speed.Text = "速度：" + FormatRate(snapshot.BytesPerSecond);
            }
            UpdateElapsed();

            if (snapshot.IsCompleted || snapshot.IsFailed)
            {
                _pause.Enabled = false;
                _cancel.Enabled = false;
            }
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _downloader.Pause();
                _pause.Text = "继续";
                SetStatus("下载已暂停");
            }
            else
            {
                _downloader.Resume();
                _pause.Text = "暂停";
                SetStatus("继续下载");
            }
        }

        private void CancelOperation()
        {
            _cancel.Enabled = false;
            _pause.Enabled = false;
            SetStatus("正在取消...");
            _cts.Cancel();
        }

        private void SetStatus(string message)
        {
            _state.Text = "状态：" + message;
        }

        private void UpdateElapsed()
        {
            _elapsed.Text = "耗时：" + _watch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        private static IProgress<ProgressSnapshot> AsIntermediateProgress(IProgress<ProgressSnapshot> progress)
        {
            return new Progress<ProgressSnapshot>(snapshot =>
            {
                snapshot.IsCompleted = false;
                progress.Report(snapshot);
            });
        }

        private static string FormatProgress(long transferred, long total)
        {
            if (total <= 0)
            {
                return FormatBytes(transferred);
            }

            return FormatBytes(transferred) + " / " + FormatBytes(total);
        }

        private static string FormatRate(double value)
        {
            if (value <= 0)
            {
                return "0 B/s";
            }

            var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
            var size = value;
            var index = 0;
            while (size >= 1024 && index < units.Length - 1)
            {
                size /= 1024;
                index++;
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} {1}", size, units[index]);
        }

        private static string FormatBytes(long value)
        {
            if (value <= 0)
            {
                return "0 B";
            }

            var units = new[] { "B", "KB", "MB", "GB" };
            double size = value;
            var index = 0;
            while (size >= 1024 && index < units.Length - 1)
            {
                size /= 1024;
                index++;
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} {1}", size, units[index]);
        }

        private static void StartUpdater(string packagePath, string sha256)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var updater = Path.Combine(baseDir, "AI软件商店.Updater.exe");
            if (!File.Exists(updater))
            {
                throw new FileNotFoundException("找不到更新器。", updater);
            }

            var tempUpdaterDir = Path.Combine(Path.GetTempPath(), "AI软件商店", "updater-runner");
            Directory.CreateDirectory(tempUpdaterDir);
            var tempUpdater = Path.Combine(tempUpdaterDir, "AI软件商店.Updater.exe");
            File.Copy(updater, tempUpdater, true);

            var args =
                "-file \"" + packagePath + "\" " +
                "-sha256 \"" + (sha256 ?? "") + "\" " +
                "-target \"" + baseDir.TrimEnd(Path.DirectorySeparatorChar) + "\" " +
                "-restart \"" + Application.ExecutablePath + "\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = tempUpdater,
                Arguments = args,
                UseShellExecute = true
            });
        }
    }
}
