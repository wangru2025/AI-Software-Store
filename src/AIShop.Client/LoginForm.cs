using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class LoginForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _username;
        private readonly TextBox _password;

        public LoginForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "登录";
            Width = 420;
            Height = 190;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("用户名", 20, 22));
            _username = FormTools.TextBox("请输入用户名，最多20个字符：", 135, 20, 240);
            Controls.Add(_username);

            Controls.Add(FormTools.Label("密码", 20, 58));
            _password = FormTools.TextBox("", 135, 56, 240, true);
            Controls.Add(_password);

            var submit = FormTools.Button("登录", 175, 100);
            var cancel = FormTools.Button("取消", 285, 100);
            submit.Click += async (s, e) => await SubmitAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(submit);
            Controls.Add(cancel);
        }

        private async Task SubmitAsync()
        {
            try
            {
                await _catalog.LoginAsync(_username.Text, _password.Text);
                MessageBox.Show("登录成功。", "登录");
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("登录失败", ex);
                MessageBox.Show(ex.Message, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
