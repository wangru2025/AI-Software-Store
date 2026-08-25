using System;
using System.IO;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using Microsoft.Win32;

namespace AIShop.Client
{
    public sealed class SettingsForm : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "AI软件商店";
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly CheckBox _startHidden = new CheckBox();
        private readonly TextBox _tempDir = new TextBox();
        private readonly Button _browse = new Button();
        private readonly CheckBox _autoReport = new CheckBox();
        private readonly Button _save;
        private readonly Button _cancel;

        public SettingsForm()
        {
            Text = "设置";
            Width = 620;
            Height = 280;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _autoStart.Text = "开机自动启动AI软件商店";
            _autoStart.Left = 16;
            _autoStart.Top = 18;
            _autoStart.Width = 360;
            _autoStart.AccessibleName = "开机自动启动AI软件商店";
            Controls.Add(_autoStart);

            _startHidden.Text = "启动软件时自动隐藏到托盘";
            _startHidden.Left = 16;
            _startHidden.Top = 48;
            _startHidden.Width = 360;
            _startHidden.AccessibleName = "启动软件时自动隐藏到托盘";
            Controls.Add(_startHidden);

            Controls.Add(FormTools.Label("临时软件包目录", 16, 86, 130));
            _tempDir.Left = 150;
            _tempDir.Top = 86;
            _tempDir.Width = 340;
            _tempDir.Height = 24;
            _tempDir.AccessibleName = "临时软件包目录";
            _tempDir.AccessibleDescription = "下载和解压投稿包、安装包、更新包时使用的临时目录。配置和日志目录不可自定义。";
            Controls.Add(_tempDir);

            _browse.Text = "浏览";
            _browse.Left = 500;
            _browse.Top = 84;
            _browse.Width = 80;
            _browse.Height = 28;
            _browse.Click += (s, e) => BrowseTempDir();
            Controls.Add(_browse);

            _autoReport.Text = "出错时自动上报所有日志";
            _autoReport.Left = 16;
            _autoReport.Top = 124;
            _autoReport.Width = 360;
            _autoReport.AccessibleName = "出错时自动上报所有日志";
            Controls.Add(_autoReport);

            Controls.Add(FormTools.Label("配置和日志目录固定为：" + AppPaths.DataDir, 16, 158, 560));

            _save = FormTools.Button("确定", 374, 198, 100);
            _save.Click += (s, e) => SaveSettings();
            Controls.Add(_save);

            _cancel = FormTools.Button("取消", 486, 198, 80);
            _cancel.Click += (s, e) => Close();
            Controls.Add(_cancel);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };

            Load += (s, e) => LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = ClientSettingsStore.Load();
            _autoStart.Checked = IsAutoStartEnabled();
            _startHidden.Checked = settings.StartHiddenToTray;
            _tempDir.Text = string.IsNullOrWhiteSpace(settings.TempPackageDirectory) ? AppPaths.DefaultTempRoot : settings.TempPackageDirectory;
            _autoReport.Checked = settings.AutoReportLogsOnError;
        }

        private void SaveSettings()
        {
            try
            {
                var tempDir = Environment.ExpandEnvironmentVariables((_tempDir.Text ?? "").Trim());
                if (string.IsNullOrWhiteSpace(tempDir))
                {
                    tempDir = AppPaths.DefaultTempRoot;
                }

                Directory.CreateDirectory(tempDir);
                SetAutoStart(_autoStart.Checked);
                ClientSettingsStore.Save(new ClientSettings
                {
                    AutoStart = _autoStart.Checked,
                    StartHiddenToTray = _startHidden.Checked,
                    TempPackageDirectory = tempDir,
                    AutoReportLogsOnError = _autoReport.Checked
                });
                MessageBox.Show("设置已保存。", "设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("保存设置失败", ex);
                FormTools.ShowError(ex);
            }
        }

        private void BrowseTempDir()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择临时软件包存储目录";
                dialog.SelectedPath = Directory.Exists(_tempDir.Text) ? _tempDir.Text : AppPaths.DefaultTempRoot;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _tempDir.Text = dialog.SelectedPath;
                }
            }
        }

        private static bool IsAutoStartEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                var value = key?.GetValue(RunValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        private static void SetAutoStart(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }
    }
}
