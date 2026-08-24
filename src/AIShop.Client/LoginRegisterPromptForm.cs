using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class LoginRegisterPromptForm : Form
    {
        private readonly ApiCatalogService _catalog;

        public LoginRegisterPromptForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "账号";
            Width = 280;
            Height = 140;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            FormTools.EnableEscClose(this);

            var login = FormTools.Button("登录", 35, 35, 85);
            var register = FormTools.Button("注册", 145, 35, 85);
            login.Click += (s, e) =>
            {
                using (var form = new LoginForm(_catalog))
                {
                    form.ShowDialog(this);
                }
                if (_catalog.IsLoggedIn)
                {
                    Close();
                }
            };
            register.Click += (s, e) =>
            {
                using (var form = new RegisterForm(_catalog))
                {
                    form.ShowDialog(this);
                }
                if (_catalog.IsLoggedIn)
                {
                    Close();
                }
            };
            Controls.Add(login);
            Controls.Add(register);
        }
    }
}
