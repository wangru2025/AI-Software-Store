using System;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class UploadForm : Form
    {
        private readonly BackgroundTask _task;
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _state = new Label();
        private readonly Label _bytes = new Label();
        private readonly Label _speed = new Label();
        private readonly Label _elapsed = new Label();
        private readonly Button _cancel = new Button();
        private readonly Timer _elapsedTimer = new Timer();

        public UploadForm(ApiCatalogService catalog, string zipPath)
        {
            _task = new BackgroundTask("上传 " + System.IO.Path.GetFileName(zipPath), "上传成功，已保存投稿。", async (task, progress, token) =>
            {
                await catalog.UploadSubmissionAsync(zipPath, progress, token, () => task.WaitIfPaused(token)).ConfigureAwait(false);
            });

            Text = "正在上传";
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

            _cancel.Text = "取消";
            _cancel.Left = 462;
            _cancel.Top = 124;
            _cancel.Width = 100;
            _cancel.Click += (s, e) => CancelUpload();
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
            FormClosed += (s, e) => _task.Changed -= OnTaskChanged;
            Load += (s, e) =>
            {
                UpdateProgress(_task.Snapshot);
                _task.Start();
            };
        }

        public bool Succeeded { get; private set; }

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

            _cancel.Enabled = false;
            if (_task.IsCompleted)
            {
                Succeeded = true;
                MessageBox.Show(_task.SuccessMessage, "投稿软件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (_task.IsFailed)
            {
                MessageBox.Show(FriendlyMessage(_task.ErrorMessage), "投稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _state.Text = "状态：" + (string.IsNullOrWhiteSpace(snapshot.Message) ? "正在上传" : snapshot.Message);
            _bytes.Text = "大小：" + FormatProgress(snapshot.BytesTransferred, snapshot.TotalBytes);
            _speed.Text = "速度：" + FormatRate(snapshot.BytesPerSecond);
            UpdateElapsed();

            if (snapshot.IsCompleted || snapshot.IsFailed)
            {
                _cancel.Enabled = false;
            }
        }

        private void CancelUpload()
        {
            _cancel.Enabled = false;
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

        private static string FriendlyMessage(Exception ex)
        {
            return FriendlyMessage(ex.Message);
        }

        private static string FriendlyMessage(string rawMessage)
        {
            var message = rawMessage ?? "";
            if (message.IndexOf("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("software_versions_software_id_version_key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("SQLSTATE 23505", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "这个软件的当前版本已经投稿过，请修改版本号后再上传。";
            }

            return message;
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
    }
}
