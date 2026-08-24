using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class ProfileForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _username;
        private readonly TextBox _nickname;

        public ProfileForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "编辑个人资料";
            Width = 430;
            Height = 190;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("用户名", 20, 24));
            _username = FormTools.TextBox("请输入用户名，最多20个字符：", 140, 22, 250);
            _username.Text = catalog.CurrentUser.Username;
            Controls.Add(_username);

            Controls.Add(FormTools.Label("昵称", 20, 60));
            _nickname = FormTools.TextBox("请输入昵称，最多10个字符：", 140, 58, 250);
            _nickname.Text = catalog.CurrentUser.Nickname;
            Controls.Add(_nickname);

            var save = FormTools.Button("保存设置", 180, 105);
            var cancel = FormTools.Button("取消", 290, 105);
            save.Click += async (s, e) => await SaveAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private async Task SaveAsync()
        {
            try
            {
                await _catalog.UpdateProfileAsync(_username.Text, _nickname.Text);
                MessageBox.Show("保存成功。", "个人资料");
                Close();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }
    }
}
