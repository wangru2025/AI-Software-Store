using System;
using System.Collections.Generic;
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
        private readonly BackgroundTask _task;
        private static readonly object WindowsSyncRoot = new object();
        private static readonly Dictionary<string, DownloadForm> WindowsByTaskId = new Dictionary<string, DownloadForm>();

        private DownloadForm(BackgroundTask task)
        {
            _task = task;
            Text = task.Title;
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
                    e.Handled = true;
                }
            };

            _task.Changed += OnTaskChanged;
            FormClosed += (s, e) =>
            {
                _task.Changed -= OnTaskChanged;
                lock (WindowsSyncRoot)
                {
                    DownloadForm current;
                    if (WindowsByTaskId.TryGetValue(_task.Id, out current) && ReferenceEquals(current, this))
                    {
                        WindowsByTaskId.Remove(_task.Id);
                    }
                }
            };
            Load += (s, e) =>
            {
                UpdateProgress(_task.Snapshot);
                _task.Start();
            };
        }

        public static DownloadForm ForInstall(ApiCatalogService catalog, SoftwareItem item)
        {
            return ForInstall(catalog, item, false);
        }

        public static DownloadForm ForInstall(ApiCatalogService catalog, SoftwareItem item, bool isUpdate)
        {
            var title = (isUpdate ? "更新 " : "下载 ") + item.Name;
            var success = isUpdate ? "更新完成。" : "安装完成。";
            var task = new BackgroundTask(title, success, async (backgroundTask, progress, token) =>
            {
                var downloader = new ManagedDownloader();
                backgroundTask.Downloader = downloader;
                var dir = Path.Combine(AppPaths.TempRoot(), item.Id, item.Version);
                Directory.CreateDirectory(dir);
                var zip = Path.Combine(dir, item.Id + "-" + item.Version + ".zip");
                var url = catalog.BuildDownloadUrl(item.Id, item.Version);
                await downloader.DownloadAsync(url, zip, item.PackageSha256, AsIntermediateProgress(progress), token).ConfigureAwait(false);
                backgroundTask.WaitIfPaused(token);
                await ElevatedInstallWorker.InstallAsync(zip, progress, token).ConfigureAwait(false);
            });
            return ForTask(task);
        }

        public static DownloadForm ForUninstall(SoftwareItem item)
        {
            var task = new BackgroundTask("卸载 " + item.Name, "卸载完成。", async (backgroundTask, progress, token) =>
            {
                var installer = new PackageInstaller();
                await installer.UninstallAsync(item, progress, token).ConfigureAwait(false);
            });
            return ForTask(task);
        }

        public static DownloadForm ForClientUpdate(ClientUpdateInfo update)
        {
            var task = new BackgroundTask("更新 AI 软件商店", null, async (backgroundTask, progress, token) =>
            {
                var downloader = new ManagedDownloader();
                backgroundTask.Downloader = downloader;
                var dir = Path.Combine(AppPaths.TempRoot(), "client-update");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "AIShop.Client.Update.zip");
                await downloader.DownloadAsync(update.DownloadUrl, file, update.Sha256, progress, token).ConfigureAwait(false);
                AppLog.Update("客户端下载包下载完成：" + file);
                StartUpdater(file, update.Sha256);
                progress.Report(new ProgressSnapshot { Percent = 100, Message = "正在启动更新程序", IsCompleted = true });
                Application.Exit();
            });
            return ForTask(task);
        }

        public static DownloadForm ForTask(BackgroundTask task)
        {
            lock (WindowsSyncRoot)
            {
                DownloadForm existing;
                if (WindowsByTaskId.TryGetValue(task.Id, out existing) && !existing.IsDisposed)
                {
                    return existing;
                }

                var form = new DownloadForm(task);
                WindowsByTaskId[task.Id] = form;
                return form;
            }
        }

        public static void ShowTask(BackgroundTask task, IWin32Window owner)
        {
            var form = ForTask(task);
            if (!form.Visible)
            {
                form.Show(owner);
            }
            else
            {
                form.Show();
            }

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }
            form.Activate();
        }

        private void OnTaskChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() => HandleTaskChanged()));
                }
                return;
            }

            HandleTaskChanged();
        }

        private void HandleTaskChanged()
        {
            UpdateProgress(_task.Snapshot);
            if (!_task.IsFinished || !_task.TryMarkNotificationShown())
            {
                return;
            }

            _pause.Enabled = false;
            _cancel.Enabled = false;

            if (_task.IsCompleted && !string.IsNullOrWhiteSpace(_task.SuccessMessage))
            {
                MessageBox.Show(_task.SuccessMessage, _task.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (_task.IsFailed)
            {
                MessageBox.Show(_task.ErrorMessage, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_task.IsFinished)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _elapsedTimer.Stop();
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
            _pause.Text = _task.IsPaused ? "继续" : "暂停";

            if (snapshot.IsCompleted || snapshot.IsFailed || _task.IsFinished)
            {
                _pause.Enabled = false;
                _cancel.Enabled = false;
            }
        }

        private void TogglePause()
        {
            if (_task.IsPaused)
            {
                _task.Resume();
                _pause.Text = "暂停";
                SetStatus("正在继续");
            }
            else
            {
                _task.Pause();
                _pause.Text = "继续";
                SetStatus("已暂停");
            }
        }

        private void CancelOperation()
        {
            _cancel.Enabled = false;
            _pause.Enabled = false;
            SetStatus("正在取消...");
            _task.Cancel();
        }

        private void SetStatus(string message)
        {
            _state.Text = "状态：" + message;
        }

        private void UpdateElapsed()
        {
            _elapsed.Text = "耗时：" + _task.Elapsed.ToString(@"hh\:mm\:ss");
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

            var tempUpdaterDir = Path.Combine(AppPaths.TempRoot(), "updater-runner");
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
