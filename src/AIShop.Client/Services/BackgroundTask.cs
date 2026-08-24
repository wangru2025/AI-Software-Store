using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIShop.Shared;

namespace AIShop.Client.Services
{
    public sealed class BackgroundTask
    {
        private readonly Func<BackgroundTask, IProgress<ProgressSnapshot>, CancellationToken, Task> _operation;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ManualResetEventSlim _pauseGate = new ManualResetEventSlim(true);
        private readonly Stopwatch _watch = new Stopwatch();
        private bool _started;
        private bool _notificationShown;

        public BackgroundTask(string title, string successMessage, Func<BackgroundTask, IProgress<ProgressSnapshot>, CancellationToken, Task> operation)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = title;
            SuccessMessage = successMessage;
            _operation = operation;
            Snapshot = new ProgressSnapshot { Percent = 0, Message = "等待开始" };
        }

        public string Id { get; }
        public string Title { get; }
        public string SuccessMessage { get; }
        public ProgressSnapshot Snapshot { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool IsFailed { get; private set; }
        public bool IsCanceled { get; private set; }
        public bool IsFinished => IsCompleted || IsFailed || IsCanceled;
        public TimeSpan Elapsed => _watch.Elapsed;
        public ManagedDownloader Downloader { get; set; }

        public event EventHandler Changed;

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            Task.Run(RunAsync);
        }

        public void Pause()
        {
            if (IsFinished || IsPaused)
            {
                return;
            }

            IsPaused = true;
            _pauseGate.Reset();
            Downloader?.Pause();
            Report(new ProgressSnapshot
            {
                Percent = Snapshot.Percent,
                Message = "已暂停",
                BytesTransferred = Snapshot.BytesTransferred,
                TotalBytes = Snapshot.TotalBytes,
                BytesPerSecond = 0
            });
        }

        public void Resume()
        {
            if (IsFinished || !IsPaused)
            {
                return;
            }

            IsPaused = false;
            _pauseGate.Set();
            Downloader?.Resume();
            Report(new ProgressSnapshot
            {
                Percent = Snapshot.Percent,
                Message = "正在继续",
                BytesTransferred = Snapshot.BytesTransferred,
                TotalBytes = Snapshot.TotalBytes
            });
        }

        public void Cancel()
        {
            if (IsFinished)
            {
                return;
            }

            _pauseGate.Set();
            Downloader?.Resume();
            _cts.Cancel();
        }

        public bool TryMarkNotificationShown()
        {
            if (_notificationShown)
            {
                return false;
            }

            _notificationShown = true;
            return true;
        }

        public void WaitIfPaused(CancellationToken cancellationToken)
        {
            _pauseGate.Wait(cancellationToken);
        }

        public string ToListText()
        {
            if (IsFailed)
            {
                return Title + "，状态：失败，原因：" + ErrorMessage;
            }
            if (IsCanceled)
            {
                return Title + "，状态：已取消";
            }
            if (IsCompleted)
            {
                return Title + "，状态：已完成";
            }
            if (IsPaused)
            {
                return Title + "，状态：已暂停";
            }

            var message = string.IsNullOrWhiteSpace(Snapshot.Message) ? "正在处理" : Snapshot.Message;
            return Title + "，状态：" + message + "，百分比：" + Snapshot.Percent + "%";
        }

        private async Task RunAsync()
        {
            BackgroundTaskManager.Add(this);
            _watch.Start();
            try
            {
                await _operation(this, new DirectProgress(Report), _cts.Token).ConfigureAwait(false);
                if (!_cts.IsCancellationRequested)
                {
                    IsCompleted = true;
                    Report(new ProgressSnapshot { Percent = 100, Message = "已完成", IsCompleted = true });
                }
            }
            catch (OperationCanceledException)
            {
                IsCanceled = true;
                Report(new ProgressSnapshot { Percent = Snapshot.Percent, Message = "已取消" });
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                IsFailed = true;
                AppLog.Error("后台任务失败：" + Title, ex);
                Report(new ProgressSnapshot { Percent = Snapshot.Percent, Message = "失败", IsFailed = true });
            }
            finally
            {
                _watch.Stop();
                BackgroundTaskManager.NotifyChanged();
            }
        }

        private void Report(ProgressSnapshot snapshot)
        {
            Snapshot = snapshot ?? new ProgressSnapshot();
            Changed?.Invoke(this, EventArgs.Empty);
            BackgroundTaskManager.NotifyChanged();
        }

        private sealed class DirectProgress : IProgress<ProgressSnapshot>
        {
            private readonly Action<ProgressSnapshot> _handler;

            public DirectProgress(Action<ProgressSnapshot> handler)
            {
                _handler = handler;
            }

            public void Report(ProgressSnapshot value)
            {
                _handler(value);
            }
        }
    }
}
