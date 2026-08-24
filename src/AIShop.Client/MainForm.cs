using System;
using System.Collections.Generic;
using System.Configuration;
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
        private readonly InstalledPackageStore _installed = new InstalledPackageStore();
        private readonly PackageInstaller _launcher = new PackageInstaller();
        private readonly ComboBox _category;
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly Button _searchButton;
        private readonly ContextMenuStrip _softwareMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _primarySoftwareAction = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _uninstallAction = new ToolStripMenuItem();
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private IReadOnlyList<SoftwareItem> _items = new List<SoftwareItem>();
        private string _loadedCategory = SoftwareCategories.All;
        private string _searchKeyword = "";
        private int _lastIndex;

        public MainForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "AI 软件商店";
            Width = 980;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("文件");
            file.DropDownItems.Add("投稿软件", null, async (s, e) => await OpenSubmissionAsync());
            file.DropDownItems.Add("个人中心", null, async (s, e) => await OpenPersonalCenterAsync());
            file.DropDownItems.Add("任务管理", null, (s, e) => OpenTaskManager());
            file.DropDownItems.Add("隐藏到托盘", null, (s, e) => HideToTray());
            file.DropDownItems.Add("退出", null, (s, e) => Close());
            var help = new ToolStripMenuItem("帮助");
            help.DropDownItems.Add("关于", null, (s, e) => MessageBox.Show("AI 软件商店", "关于"));
            help.DropDownItems.Add("检查更新", null, async (s, e) => await CheckUpdateAsync());
            menu.Items.Add(file);
            menu.Items.Add(help);
            Controls.Add(menu);
            MainMenuStrip = menu;

            _category = new ComboBox
            {
                Left = 12,
                Top = menu.Height + 8,
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = 0,
                AccessibleName = "软件分类",
                AccessibleDescription = "选择分类后按回车加载该分类的软件。"
            };
            _category.Items.Add(SoftwareCategories.All);
            foreach (var category in SoftwareCategories.Values)
            {
                _category.Items.Add(category);
            }
            _category.SelectedIndex = 0;
            _category.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    LoadSelectedCategory();
                    e.Handled = true;
                }
            };
            Controls.Add(_category);

            _list = FormTools.ListBox();
            _list.Dock = DockStyle.None;
            _list.TabIndex = 1;
            _list.ContextMenuStrip = _softwareMenu;
            _list.DoubleClick += async (s, e) => await RunPrimaryActionAsync();
            _list.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    await RunPrimaryActionAsync();
                }
                else if (e.KeyCode == Keys.F5)
                {
                    RememberFocus();
                    await RefreshSoftwareAsync();
                    RestoreFocus();
                }
            };
            Controls.Add(_list);
            _list.BringToFront();

            _search = new TextBox
            {
                Top = menu.Height + 8,
                Width = 360,
                Height = 24,
                TabIndex = 2,
                AccessibleName = "搜索关键词",
                AccessibleDescription = "输入关键词后按回车，在当前分类中搜索软件。"
            };
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SearchCurrentCategory();
                    e.Handled = true;
                }
            };
            Controls.Add(_search);

            _searchButton = new Button
            {
                Text = "搜索",
                Top = menu.Height + 6,
                Width = 80,
                Height = 28,
                TabIndex = 3
            };
            _searchButton.Click += (s, e) => SearchCurrentCategory();
            Controls.Add(_searchButton);

            BuildContextMenu();
            BuildTray();
            LayoutMainControls();
            Resize += (s, e) => LayoutMainControls();
            Load += async (s, e) =>
            {
                await _catalog.RefreshCurrentUserAsync();
                await RefreshSoftwareAsync();
            };
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    HideToTray();
                }
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    HideToTray();
                    e.Handled = true;
                }
            };
            FormClosing += OnMainFormClosing;
            BackgroundTaskManager.Changed += OnBackgroundTasksChanged;
            FormClosed += (s, e) =>
            {
                BackgroundTaskManager.Changed -= OnBackgroundTasksChanged;
                _trayIcon.Dispose();
            };
        }

        private void BuildContextMenu()
        {
            _primarySoftwareAction.Click += async (s, e) => await RunPrimaryActionAsync();
            _softwareMenu.Items.Add(_primarySoftwareAction);
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
            _uninstallAction.Text = "卸载";
            _uninstallAction.Click += (s, e) =>
            {
                var item = SelectedSoftware();
                if (item != null)
                {
                    var form = DownloadForm.ForUninstall(item);
                    form.Show(this);
                }
            };
            _softwareMenu.Items.Add(_uninstallAction);
            _softwareMenu.Opening += (s, e) => UpdateSoftwareMenu();
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开", null, (s, e) => RestoreFromTray());
            menu.Items.Add("退出", null, (s, e) => Close());
            UpdateTrayText();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private async Task RefreshSoftwareAsync()
        {
            try
            {
                _items = await _catalog.GetPublishedSoftwareAsync();
                PopulateSoftwareList();
            }
            catch (Exception ex)
            {
                AppLog.Error("刷新软件列表失败", ex);
                FormTools.ShowError(ex);
            }
        }

        private void PopulateSoftwareList()
        {
            _list.Items.Clear();
            foreach (var item in FilterSoftware(_items))
            {
                _list.Items.Add(new DisplayItem<SoftwareItem>(item, item.ToMainListText()));
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
            }
        }

        private IEnumerable<SoftwareItem> FilterSoftware(IEnumerable<SoftwareItem> items)
        {
            foreach (var item in items)
            {
                if (_loadedCategory != SoftwareCategories.All &&
                    !string.Equals(NormalizeCategory(item.Category), _loadedCategory, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesKeyword(item, _searchKeyword))
                {
                    continue;
                }

                yield return item;
            }
        }

        private static bool MatchesKeyword(SoftwareItem item, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            return Contains(item.Name, keyword) ||
                   Contains(item.Summary, keyword) ||
                   Contains(item.Author, keyword) ||
                   Contains(item.Id, keyword) ||
                   Contains(item.Category, keyword);
        }

        private static bool Contains(string value, string keyword)
        {
            return (value ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeCategory(string category)
        {
            return SoftwareCategories.IsValid(category) ? category : SoftwareCategories.Default;
        }

        private void LoadSelectedCategory()
        {
            _loadedCategory = _category.SelectedItem as string ?? SoftwareCategories.All;
            _lastIndex = 0;
            PopulateSoftwareList();
            _list.Focus();
        }

        private void SearchCurrentCategory()
        {
            _searchKeyword = (_search.Text ?? "").Trim();
            _lastIndex = 0;
            PopulateSoftwareList();
            _list.Focus();
        }

        private Task RunPrimaryActionAsync()
        {
            var item = SelectedSoftware();
            if (item == null)
            {
                return Task.CompletedTask;
            }

            var action = PrimaryActionFor(item);
            if (action == SoftwarePrimaryAction.Launch)
            {
                try
                {
                    _launcher.Launch(item);
                }
                catch (Exception ex)
                {
                    AppLog.Error("启动软件失败", ex);
                    FormTools.ShowError(ex);
                }
                return Task.CompletedTask;
            }

            var form = DownloadForm.ForInstall(_catalog, item, action == SoftwarePrimaryAction.Update);
            form.Show(this);
            return Task.CompletedTask;
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

        private void OpenTaskManager()
        {
            using (var form = new TaskManagerForm())
            {
                form.ShowDialog(this);
            }
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
                var currentVersion = ConfigurationManager.AppSettings["ClientVersion"] ?? "0.0.0";
                var update = await _catalog.CheckClientUpdateAsync(currentVersion);
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

        private void UpdateSoftwareMenu()
        {
            var item = SelectedSoftware();
            if (item == null)
            {
                _primarySoftwareAction.Text = "下载";
                _primarySoftwareAction.Enabled = false;
                _uninstallAction.Visible = false;
                return;
            }

            var action = PrimaryActionFor(item);
            _primarySoftwareAction.Enabled = true;
            _primarySoftwareAction.Text = action == SoftwarePrimaryAction.Launch
                ? "启动"
                : action == SoftwarePrimaryAction.Update ? "更新" : "下载";
            _uninstallAction.Visible = _installed.Find(item.Id) != null;
        }

        private SoftwarePrimaryAction PrimaryActionFor(SoftwareItem item)
        {
            var installed = _installed.Find(item.Id);
            if (installed == null)
            {
                return SoftwarePrimaryAction.Download;
            }

            return CompareVersion(installed.Version, item.Version) < 0
                ? SoftwarePrimaryAction.Update
                : SoftwarePrimaryAction.Launch;
        }

        private static int CompareVersion(string left, string right)
        {
            var leftParts = (left ?? "").Split('.');
            var rightParts = (right ?? "").Split('.');
            var length = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < length; i++)
            {
                var l = i < leftParts.Length ? VersionPart(leftParts[i]) : 0;
                var r = i < rightParts.Length ? VersionPart(rightParts[i]) : 0;
                if (l != r)
                {
                    return l.CompareTo(r);
                }
            }

            return string.Compare(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static int VersionPart(string value)
        {
            int number;
            return int.TryParse(value, out number) ? number : 0;
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

        private void LayoutMainControls()
        {
            var menuHeight = MainMenuStrip != null ? MainMenuStrip.Height : 24;
            var top = menuHeight + 8;
            var padding = 12;
            var buttonWidth = 80;
            var categoryWidth = 180;
            var searchWidth = Math.Max(200, ClientSize.Width - padding * 2 - categoryWidth - 12 - buttonWidth - 12);

            _category.SetBounds(padding, top, categoryWidth, 24);
            _search.SetBounds(padding + categoryWidth + 12, top, searchWidth, 24);
            _searchButton.SetBounds(padding + categoryWidth + 12 + searchWidth + 12, top - 1, buttonWidth, 28);
            _list.SetBounds(padding, top + 38, ClientSize.Width - padding * 2, ClientSize.Height - (top + 50) - padding);
        }

        private void OnMainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing || !BackgroundTaskManager.HasRunningTasks())
            {
                return;
            }

            var result = MessageBox.Show("有任务正在运行，是否退出？", "AI 软件商店", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void OnBackgroundTasksChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)UpdateTrayText);
                }
                return;
            }

            UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            _trayIcon.Text = BackgroundTaskManager.HasRunningTasks() ? "AI 软件商店（工作中）" : "AI 软件商店";
        }

        private void HideToTray()
        {
            _trayIcon.Visible = true;
            Hide();
        }

        public void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _list.Focus();
        }

        private enum SoftwarePrimaryAction
        {
            Download,
            Update,
            Launch
        }
    }
}
