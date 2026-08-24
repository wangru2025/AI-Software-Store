using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class UploadForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly string _zipPath;
        private readonly TextBox _status = new TextBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _state = new Label();
        private readonly Label _bytes = new Label();
        private readonly Label _speed = new Label();
        private readonly Label _elapsed = new Label();
        private readonly Button _cancel = new Button();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private string _lastLoggedMessage;
        private bool _finished;

        public UploadForm(ApiCatalogService catalog, string zipPath)
        {
            _catalog = catalog;
            _zipPath = zipPath;

            Text = "正在上传";
            Width = 590;
            Height = 330;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _status.Multiline = true;
            _status.ReadOnly = true;
            _status.ScrollBars = ScrollBars.Vertical;
            _status.Left = 12;
            _status.Top = 12;
            _status.Width = 550;
            _status.Height = 145;
            Controls.Add(_status);

            _progress.Left = 12;
            _progress.Top = 168;
            _progress.Width = 550;
            _progress.Height = 24;
            Controls.Add(_progress);

            _state.Left = 12;
            _state.Top = 202;
            _state.Width = 550;
            Controls.Add(_state);

            _bytes.Left = 12;
            _bytes.Top = 226;
            _bytes.Width = 550;
            Controls.Add(_bytes);

            _speed.Left = 12;
            _speed.Top = 250;
            _speed.Width = 270;
            Controls.Add(_speed);

            _elapsed.Left = 292;
            _elapsed.Top = 250;
            _elapsed.Width = 270;
            Controls.Add(_elapsed);

            _cancel.Text = "取消";
            _cancel.Left = 462;
            _cancel.Top = 280;
            _cancel.Width = 100;
            _cancel.Click += (s, e) => CancelUpload();
            Controls.Add(_cancel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            Load += async (s, e) => await RunAsync();
        }

        public bool Succeeded { get; private set; }

        private async Task RunAsync()
        {
            try
            {
                await _catalog.UploadSubmissionAsync(_zipPath, new Progress<ProgressSnapshot>(UpdateProgress), _cts.Token);
                Succeeded = true;
                _finished = true;
                MessageBox.Show("上传成功，已保存为草稿。", "投稿软件");
                Close();
            }
            catch (OperationCanceledException)
            {
                AppendStatus("已取消。");
                _cancel.Enabled = false;
            }
            catch (Exception ex)
            {
                AppLog.Error("投稿失败", ex);
                MessageBox.Show(FriendlyMessage(ex), "投稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _cancel.Enabled = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_finished && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            base.OnFormClosing(e);
        }

        private void UpdateProgress(ProgressSnapshot snapshot)
        {
            _progress.Value = Math.Max(0, Math.Min(100, snapshot.Percent));
            _state.Text = "状态：" + (string.IsNullOrWhiteSpace(snapshot.Message) ? "正在上传" : snapshot.Message);
            _bytes.Text = "大小：" + FormatProgress(snapshot.BytesTransferred, snapshot.TotalBytes);
            _speed.Text = "速度：" + FormatRate(snapshot.BytesPerSecond);
            _elapsed.Text = "耗时：" + _watch.Elapsed.ToString(@"hh\:mm\:ss");

            if (!string.IsNullOrWhiteSpace(snapshot.Message) && snapshot.Message != _lastLoggedMessage)
            {
                AppendStatus(snapshot.Message);
                _lastLoggedMessage = snapshot.Message;
            }

            if (snapshot.IsCompleted || snapshot.IsFailed)
            {
                _cancel.Enabled = false;
            }
        }

        private void CancelUpload()
        {
            _cancel.Enabled = false;
            AppendStatus("正在取消...");
            _cts.Cancel();
        }

        private void AppendStatus(string message)
        {
            _status.AppendText(message + Environment.NewLine);
        }

        private static string FriendlyMessage(Exception ex)
        {
            var message = ex.Message ?? "";
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
