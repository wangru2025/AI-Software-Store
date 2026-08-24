using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class RegisterForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _username;
        private readonly TextBox _nickname;
        private readonly TextBox _password;

        public RegisterForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "注册";
            Width = 440;
            Height = 230;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("用户名", 20, 22));
            _username = FormTools.TextBox("请输入用户名，最多20个字符：", 145, 20, 250);
            Controls.Add(_username);

            Controls.Add(FormTools.Label("昵称", 20, 58));
            _nickname = FormTools.TextBox("请输入昵称，最多10个字符：", 145, 56, 250);
            Controls.Add(_nickname);

            Controls.Add(FormTools.Label("密码", 20, 94));
            _password = FormTools.TextBox("", 145, 92, 250, true);
            Controls.Add(_password);

            var submit = FormTools.Button("注册", 185, 140);
            var cancel = FormTools.Button("取消", 295, 140);
            submit.Click += async (s, e) => await SubmitAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(submit);
            Controls.Add(cancel);
        }

        private async Task SubmitAsync()
        {
            try
            {
                await _catalog.RegisterAsync(_username.Text, _nickname.Text, _password.Text);
                MessageBox.Show("注册成功，已自动登录。", "注册");
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("注册失败", ex);
                MessageBox.Show(ex.Message, "注册失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
