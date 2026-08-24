using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class ChangePasswordForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _oldPassword;
        private readonly TextBox _newPassword;
        private readonly TextBox _repeatPassword;

        public ChangePasswordForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "修改密码";
            Width = 430;
            Height = 230;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("旧密码", 20, 24));
            _oldPassword = FormTools.TextBox("", 140, 22, 250, true);
            Controls.Add(_oldPassword);

            Controls.Add(FormTools.Label("新密码", 20, 60));
            _newPassword = FormTools.TextBox("", 140, 58, 250, true);
            Controls.Add(_newPassword);

            Controls.Add(FormTools.Label("重复新密码", 20, 96));
            _repeatPassword = FormTools.TextBox("", 140, 94, 250, true);
            Controls.Add(_repeatPassword);

            var save = FormTools.Button("修改", 180, 145);
            var cancel = FormTools.Button("取消", 290, 145);
            save.Click += async (s, e) => await SaveAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private async Task SaveAsync()
        {
            try
            {
                await _catalog.ChangePasswordAsync(_oldPassword.Text, _newPassword.Text, _repeatPassword.Text);
                MessageBox.Show("密码已修改。", "修改密码");
                Close();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }
    }
}
