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
        private readonly TextBox _status = new TextBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _pause = new Button();
        private readonly Button _cancel = new Button();
        private readonly ManagedDownloader _downloader = new ManagedDownloader();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Func<ManagedDownloader, IProgress<ProgressSnapshot>, CancellationToken, Task> _operation;
        private bool _paused;

        private DownloadForm(string title, Func<ManagedDownloader, IProgress<ProgressSnapshot>, CancellationToken, Task> operation)
        {
            _operation = operation;
            Text = title;
            Width = 560;
            Height = 320;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _status.Multiline = true;
            _status.ReadOnly = true;
            _status.ScrollBars = ScrollBars.Vertical;
            _status.Left = 12;
            _status.Top = 12;
            _status.Width = 520;
            _status.Height = 190;
            Controls.Add(_status);

            _progress.Left = 12;
            _progress.Top = 212;
            _progress.Width = 520;
            _progress.Height = 24;
            Controls.Add(_progress);

            _pause.Text = "暂停";
            _pause.Left = 320;
            _pause.Top = 245;
            _pause.Width = 100;
            _pause.Click += (s, e) => TogglePause();
            Controls.Add(_pause);

            _cancel.Text = "取消";
            _cancel.Left = 432;
            _cancel.Top = 245;
            _cancel.Width = 100;
            _cancel.Click += (s, e) => _cts.Cancel();
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
            return new DownloadForm("下载 " + item.Name, async (downloader, progress, token) =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "AI软件商店", item.Id, item.Version);
                Directory.CreateDirectory(dir);
                var zip = Path.Combine(dir, item.Id + "-" + item.Version + ".zip");
                var url = catalog.BuildDownloadUrl(item.Id, item.Version);
                await downloader.DownloadAsync(url, zip, item.PackageSha256, progress, token).ConfigureAwait(false);
                var installer = new PackageInstaller();
                await installer.InstallAsync(zip, progress, token).ConfigureAwait(false);
            });
        }

        public static DownloadForm ForUninstall(SoftwareItem item)
        {
            return new DownloadForm("卸载 " + item.Name, async (downloader, progress, token) =>
            {
                var installer = new PackageInstaller();
                await installer.UninstallAsync(item, progress, token).ConfigureAwait(false);
            });
        }

        public static DownloadForm ForClientUpdate(ClientUpdateInfo update)
        {
            return new DownloadForm("更新 AI 软件商店", async (downloader, progress, token) =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "AI软件商店", "client-update");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "AIShop.Client.Update.zip");
                await downloader.DownloadAsync(update.DownloadUrl, file, update.Sha256, progress, token).ConfigureAwait(false);
                AppLog.Update("客户端更新包下载完成：" + file);
                StartUpdater(file, update.Sha256);
                progress.Report(new ProgressSnapshot { Percent = 100, Message = "正在启动更新程序", IsCompleted = true });
                Application.Exit();
            });
        }

        private async Task RunAsync()
        {
            var progress = new Progress<ProgressSnapshot>(snapshot =>
            {
                _progress.Value = Math.Max(0, Math.Min(100, snapshot.Percent));
                if (!string.IsNullOrWhiteSpace(snapshot.Message))
                {
                    _status.AppendText(snapshot.Message + Environment.NewLine);
                }
                if (snapshot.IsCompleted || snapshot.IsFailed)
                {
                    _pause.Enabled = false;
                    _cancel.Enabled = false;
                }
            });

            try
            {
                await _operation(_downloader, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                _status.AppendText("已取消" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                AppLog.Error("操作失败", ex);
                _status.AppendText("操作失败：" + ex.Message + Environment.NewLine);
            }
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _downloader.Pause();
                _pause.Text = "继续";
                _status.AppendText("下载已暂停" + Environment.NewLine);
            }
            else
            {
                _downloader.Resume();
                _pause.Text = "暂停";
                _status.AppendText("继续下载" + Environment.NewLine);
            }
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
