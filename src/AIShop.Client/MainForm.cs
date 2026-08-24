using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class MainForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly ListBox _list;
        private readonly ContextMenuStrip _softwareMenu = new ContextMenuStrip();
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private IReadOnlyList<SoftwareItem> _items = new List<SoftwareItem>();
        private int _lastIndex;

        public MainForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "AI 软件商店";
            Width = 980;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("文件");
            file.DropDownItems.Add("投稿软件", null, async (s, e) => await OpenSubmissionAsync());
            file.DropDownItems.Add("个人中心", null, async (s, e) => await OpenPersonalCenterAsync());
            file.DropDownItems.Add("隐藏到托盘", null, (s, e) => HideToTray());
            file.DropDownItems.Add("退出", null, (s, e) => Close());
            var help = new ToolStripMenuItem("帮助");
            help.DropDownItems.Add("关于", null, (s, e) => MessageBox.Show("AI 软件商店", "关于"));
            help.DropDownItems.Add("检查更新", null, async (s, e) => await CheckUpdateAsync());
            menu.Items.Add(file);
            menu.Items.Add(help);
            Controls.Add(menu);
            MainMenuStrip = menu;

            _list = FormTools.ListBox();
            _list.Top = menu.Height;
            _list.ContextMenuStrip = _softwareMenu;
            _list.DoubleClick += async (s, e) => await DownloadSelectedAsync();
            _list.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    await DownloadSelectedAsync();
                }
            };
            Controls.Add(_list);
            _list.BringToFront();

            BuildContextMenu();
            BuildTray();
            Load += async (s, e) => await RefreshSoftwareAsync();
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    HideToTray();
                }
            };
            FormClosed += (s, e) => _trayIcon.Dispose();
        }

        private void BuildContextMenu()
        {
            _softwareMenu.Items.Add("下载", null, async (s, e) => await DownloadSelectedAsync());
            _softwareMenu.Items.Add("查看简介", null, (s, e) =>
            {
                var item = SelectedSoftware();
                if (item != null)
                {
                    MessageBox.Show(item.Summary, item.Name);
                }
            });
            _softwareMenu.Items.Add("更新日志", null, (s, e) =>
            {
                var item = SelectedSoftware();
                if (item != null)
                {
                    RememberFocus();
                    using (var form = new ChangelogListForm(item))
                    {
                        form.ShowDialog(this);
                    }
                    RestoreFocus();
                }
            });
            _softwareMenu.Items.Add("评分", null, (s, e) =>
            {
                var item = SelectedSoftware();
                if (item != null)
                {
                    RememberFocus();
                    using (var form = new RatingForm(_catalog, item))
                    {
                        form.ShowDialog(this);
                    }
                    RestoreFocus();
                    _ = RefreshSoftwareAsync();
                }
            });
            _softwareMenu.Items.Add("卸载", null, (s, e) =>
            {
                var item = SelectedSoftware();
                if (item != null)
                {
                    var form = DownloadForm.ForUninstall(item);
                    form.Show(this);
                }
            });
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开", null, (s, e) => RestoreFromTray());
            menu.Items.Add("退出", null, (s, e) => Close());
            _trayIcon.Text = "AI 软件商店";
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private async Task RefreshSoftwareAsync()
        {
            try
            {
                _items = await _catalog.GetPublishedSoftwareAsync();
                _list.Items.Clear();
                foreach (var item in _items)
                {
                    _list.Items.Add(new DisplayItem<SoftwareItem>(item, item.ToMainListText()));
                }

                if (_list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("刷新软件列表失败", ex);
                FormTools.ShowError(ex);
            }
        }

        private async Task DownloadSelectedAsync()
        {
            var item = SelectedSoftware();
            if (item == null)
            {
                return;
            }

            var form = DownloadForm.ForInstall(_catalog, item);
            form.Show(this);
        }

        private async Task OpenSubmissionAsync()
        {
            if (!await EnsureLoggedInAsync())
            {
                return;
            }

            using (var form = new SubmitSoftwareForm(_catalog))
            {
                form.ShowDialog(this);
            }
        }

        private async Task OpenPersonalCenterAsync()
        {
            if (!await EnsureLoggedInAsync())
            {
                return;
            }

            RememberFocus();
            using (var form = new PersonalCenterForm(_catalog))
            {
                form.ShowDialog(this);
            }
            RestoreFocus();
            await RefreshSoftwareAsync();
        }

        private async Task<bool> EnsureLoggedInAsync()
        {
            if (_catalog.IsLoggedIn)
            {
                return true;
            }

            using (var prompt = new LoginRegisterPromptForm(_catalog))
            {
                prompt.ShowDialog(this);
            }

            return _catalog.IsLoggedIn;
        }

        private async Task CheckUpdateAsync()
        {
            try
            {
                var update = await _catalog.CheckClientUpdateAsync();
                if (update == null || !update.HasUpdate)
                {
                    MessageBox.Show("当前已是最新版本。", "检查更新");
                    return;
                }

                if (MessageBox.Show("发现新版本 " + update.Version + "，是否下载更新？", "检查更新", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    var form = DownloadForm.ForClientUpdate(update);
                    form.Show(this);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("检查客户端更新失败", ex);
                FormTools.ShowError(ex);
            }
        }

        private SoftwareItem SelectedSoftware()
        {
            var item = _list.SelectedItem as DisplayItem<SoftwareItem>;
            return item == null ? null : item.Value;
        }

        private void RememberFocus()
        {
            _lastIndex = Math.Max(0, _list.SelectedIndex);
        }

        private void RestoreFocus()
        {
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
            }
            _list.Focus();
        }

        private void HideToTray()
        {
            _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _list.Focus();
        }
    }
}
