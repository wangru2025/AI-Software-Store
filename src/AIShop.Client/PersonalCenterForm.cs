using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class PersonalCenterForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly ListBox _list;
        private int _lastIndex;

        public PersonalCenterForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "个人中心";
            Width = 480;
            Height = 360;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _list = FormTools.ListBox();
            Controls.Add(_list);
            Load += (s, e) => RefreshItems();
            _list.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    await OpenSelectedAsync();
                }
            };
        }

        private void RefreshItems()
        {
            _list.Items.Clear();
            _list.Items.Add(_catalog.CurrentUser == null ? "用户昵称" : _catalog.CurrentUser.Nickname);
            _list.Items.Add("编辑个人资料");
            _list.Items.Add("修改密码");
            _list.Items.Add("我的投稿");
            _list.Items.Add("退出登录");
            _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
        }

        private async Task OpenSelectedAsync()
        {
            switch (_list.SelectedIndex)
            {
                case 1:
                    RememberFocus();
                    using (var form = new ProfileForm(_catalog))
                    {
                        form.ShowDialog(this);
                    }
                    RefreshItems();
                    RestoreFocus();
                    break;
                case 2:
                    RememberFocus();
                    using (var form = new ChangePasswordForm(_catalog))
                    {
                        form.ShowDialog(this);
                    }
                    RestoreFocus();
                    break;
                case 3:
                    RememberFocus();
                    using (var form = new MySubmissionsForm(_catalog))
                    {
                        form.ShowDialog(this);
                    }
                    RestoreFocus();
                    break;
                case 4:
                    _catalog.Logout();
                    MessageBox.Show("已退出登录。", "个人中心");
                    Close();
                    break;
            }
        }

        private void RememberFocus()
        {
            _lastIndex = _list.SelectedIndex;
        }

        private void RestoreFocus()
        {
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(Math.Max(0, _lastIndex), _list.Items.Count - 1);
            }
            _list.Focus();
        }
    }
}
