using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class TaskManagerForm : Form
    {
        private readonly ListBox _list;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Timer _refreshTimer = new Timer();
        private IReadOnlyList<BackgroundTask> _tasks = new List<BackgroundTask>();
        private int _lastIndex;
        private bool _refreshPending;

        public TaskManagerForm()
        {
            Text = "任务管理";
            Width = 760;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _list = FormTools.ListBox();
            _list.ContextMenuStrip = _menu;
            _list.DoubleClick += (s, e) => OpenSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OpenSelected();
                }
                else if (e.KeyCode == Keys.F5)
                {
                    RefreshTasks();
                }
            };
            Controls.Add(_list);

            BuildMenu();
            _refreshTimer.Interval = 250;
            _refreshTimer.Tick += (s, e) =>
            {
                if (_refreshPending)
                {
                    _refreshPending = false;
                    RefreshTasks();
                }
            };
            _refreshTimer.Start();
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
            Load += (s, e) => RefreshTasks();
            BackgroundTaskManager.Changed += OnTasksChanged;
            FormClosed += (s, e) =>
            {
                _refreshTimer.Stop();
                BackgroundTaskManager.Changed -= OnTasksChanged;
            };
        }

        private void BuildMenu()
        {
            _menu.Items.Add("打开", null, (s, e) => OpenSelected());
            _menu.Items.Add("暂停", null, (s, e) =>
            {
                var task = SelectedTask();
                task?.Pause();
            });
            _menu.Items.Add("继续", null, (s, e) =>
            {
                var task = SelectedTask();
                task?.Resume();
            });
            _menu.Items.Add("取消", null, (s, e) =>
            {
                var task = SelectedTask();
                task?.Cancel();
            });
            _menu.Opening += (s, e) =>
            {
                var task = SelectedTask();
                _menu.Items[1].Enabled = task != null && !task.IsFinished && !task.IsPaused;
                _menu.Items[2].Enabled = task != null && !task.IsFinished && task.IsPaused;
                _menu.Items[3].Enabled = task != null && !task.IsFinished;
            };
        }

        private void RefreshTasks()
        {
            _lastIndex = Math.Max(0, _list.SelectedIndex);
            _tasks = BackgroundTaskManager.All();
            _list.Items.Clear();
            foreach (var task in _tasks)
            {
                _list.Items.Add(new DisplayItem<BackgroundTask>(task, task.ToListText()));
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
            }
        }

        private void OpenSelected()
        {
            var task = SelectedTask();
            if (task == null)
            {
                return;
            }

            var form = DownloadForm.ForTask(task);
            form.Show(this);
        }

        private BackgroundTask SelectedTask()
        {
            var item = _list.SelectedItem as DisplayItem<BackgroundTask>;
            return item == null ? null : item.Value;
        }

        private void OnTasksChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (!IsHandleCreated)
                {
                    return;
                }

                BeginInvoke((Action)(() => _refreshPending = true));
                return;
            }

            _refreshPending = true;
        }
    }
}
