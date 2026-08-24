using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class RatingForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly SoftwareItem _software;
        private readonly ListBox _list;
        private readonly Button _publish;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private IReadOnlyList<RatingItem> _ratings = new List<RatingItem>();
        private int _lastIndex;

        public RatingForm(ApiCatalogService catalog, SoftwareItem software)
        {
            _catalog = catalog;
            _software = software;
            Text = "评分";
            Width = 760;
            Height = 500;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _publish = FormTools.Button("发布评分", 12, 10, 110);
            _publish.Click += async (s, e) => await PublishAsync();
            Controls.Add(_publish);

            _list = new ListBox
            {
                Left = 12,
                Top = 50,
                Width = 720,
                Height = 390,
                IntegralHeight = false,
                ContextMenuStrip = _menu
            };
            Controls.Add(_list);

            _menu.Items.Add("回复", null, async (s, e) => await ReplyAsync());

            _list.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OpenReplies();
                }
                else if (e.KeyCode == Keys.F5)
                {
                    _lastIndex = Math.Max(0, _list.SelectedIndex);
                    await RefreshAsync();
                }
            };
            _list.DoubleClick += (s, e) => OpenReplies();
            Load += async (s, e) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                _ratings = await _catalog.GetRatingsAsync(_software.Id);
                _list.Items.Clear();
                foreach (var rating in _ratings)
                {
                    _list.Items.Add(new DisplayItem<RatingItem>(rating, rating.ToListText()));
                }
                if (_list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
                }
                UpdatePublishButton();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }

        private async Task PublishAsync()
        {
            if (!_catalog.IsLoggedIn)
            {
                MessageBox.Show("请先登录后再评分。", "评分");
                return;
            }

            if (_catalog.IsDeveloper(_software))
            {
                MessageBox.Show("开发者不能给自己的软件评分。", "评分");
                return;
            }

            if (HasCurrentUserRated())
            {
                MessageBox.Show("你已经给这个软件评过分。", "评分");
                return;
            }

            using (var form = new PublishRatingForm(_catalog, _software))
            {
                form.ShowDialog(this);
            }
            await RefreshAsync();
        }

        private async Task ReplyAsync()
        {
            var rating = Selected();
            if (rating == null)
            {
                return;
            }

            if (!_catalog.IsDeveloper(_software))
            {
                MessageBox.Show("只有开发者可以回复评分。", "评分");
                return;
            }

            using (var form = new ReplyEditorForm())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    await _catalog.AddReplyAsync(rating.Id, null, form.Body);
                    await RefreshAsync();
                }
            }
        }

        private void OpenReplies()
        {
            var rating = Selected();
            if (rating == null || rating.ReplyCount <= 0)
            {
                return;
            }

            _lastIndex = _list.SelectedIndex;
            using (var form = new RatingRepliesForm(_catalog, _software, rating))
            {
                form.ShowDialog(this);
            }
            _list.SelectedIndex = Math.Min(_lastIndex, Math.Max(0, _list.Items.Count - 1));
            _list.Focus();
        }

        private RatingItem Selected()
        {
            var item = _list.SelectedItem as DisplayItem<RatingItem>;
            return item == null ? null : item.Value;
        }

        private void UpdatePublishButton()
        {
            _publish.Visible = !_catalog.IsLoggedIn || (!_catalog.IsDeveloper(_software) && !HasCurrentUserRated());
        }

        private bool HasCurrentUserRated()
        {
            var user = _catalog.CurrentUser;
            if (user == null)
            {
                return false;
            }

            foreach (var rating in _ratings)
            {
                if (string.Equals(rating.Username, user.Username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
