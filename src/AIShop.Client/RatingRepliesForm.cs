using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class RatingRepliesForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly SoftwareItem _software;
        private readonly RatingItem _rating;
        private readonly ListBox _list;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private IReadOnlyList<RatingReply> _replies = new List<RatingReply>();
        private int _lastIndex;

        public RatingRepliesForm(ApiCatalogService catalog, SoftwareItem software, RatingItem rating)
        {
            _catalog = catalog;
            _software = software;
            _rating = rating;
            Text = "回复列表";
            Width = 760;
            Height = 500;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _list = FormTools.ListBox();
            _list.ContextMenuStrip = _menu;
            Controls.Add(_list);
            _menu.Items.Add("回复", null, async (s, e) => await ReplyAsync());
            _list.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.F5)
                {
                    _lastIndex = Math.Max(0, _list.SelectedIndex);
                    await RefreshAsync();
                }
            };
            Load += async (s, e) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                _replies = await _catalog.GetRepliesAsync(_rating.Id);
                _list.Items.Clear();
                _list.Items.Add("原评分：" + _rating.ToListText());
                foreach (var reply in _replies)
                {
                    _list.Items.Add(new DisplayItem<RatingReply>(reply, reply.ToListText()));
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

        private async Task ReplyAsync()
        {
            if (!_catalog.IsDeveloper(_software))
            {
                MessageBox.Show("只有开发者可以回复评分。", "回复");
                return;
            }

            var selectedReply = _list.SelectedItem as DisplayItem<RatingReply>;
            using (var form = new ReplyEditorForm())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    await _catalog.AddReplyAsync(_rating.Id, selectedReply == null ? null : selectedReply.Value.Id, form.Body);
                    await RefreshAsync();
                }
            }
        }
    }
}
