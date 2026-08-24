using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class MySubmissionsForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly ListBox _list;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private IReadOnlyList<SubmissionItem> _items = new List<SubmissionItem>();
        private int _lastIndex;

        public MySubmissionsForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "我的投稿";
            Width = 880;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _list = FormTools.ListBox();
            _list.ContextMenuStrip = _menu;
            Controls.Add(_list);

            _menu.Items.Add("删除", null, async (s, e) => await DeleteAsync());
            _menu.Items.Add("上架 / 转草稿", null, async (s, e) => await ToggleAsync());
            _menu.Items.Add("编辑", null, async (s, e) => await EditAsync());

            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && _list.SelectedItem != null)
                {
                    _menu.Show(_list, _list.PointToClient(Cursor.Position));
                }
            };

            Load += async (s, e) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                _items = await _catalog.GetMySubmissionsAsync();
                _list.Items.Clear();
                foreach (var item in _items)
                {
                    _list.Items.Add(new DisplayItem<SubmissionItem>(item, item.ToListText()));
                }
                if (_list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
                }
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }

        private async Task ToggleAsync()
        {
            var item = Selected();
            if (item == null)
            {
                return;
            }

            try
            {
                _lastIndex = _list.SelectedIndex;
                await _catalog.ToggleSubmissionStatusAsync(item.SoftwareId);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }

        private async Task EditAsync()
        {
            var item = Selected();
            if (item == null)
            {
                return;
            }

            using (var form = new EditSoftwareInfoForm(_catalog, item))
            {
                form.ShowDialog(this);
            }
            await RefreshAsync();
        }

        private async Task DeleteAsync()
        {
            var item = Selected();
            if (item == null)
            {
                return;
            }

            if (MessageBox.Show("确定删除这个投稿吗？", "我的投稿", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                _lastIndex = _list.SelectedIndex;
                await _catalog.DeleteSubmissionAsync(item.SoftwareId);
                MessageBox.Show("投稿已删除。", "我的投稿");
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }

        private SubmissionItem Selected()
        {
            var item = _list.SelectedItem as DisplayItem<SubmissionItem>;
            return item == null ? null : item.Value;
        }
    }
}
