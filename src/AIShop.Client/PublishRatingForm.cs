using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class PublishRatingForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly SoftwareItem _software;
        private readonly ComboBox _stars = new ComboBox();
        private readonly TextBox _comment;

        public PublishRatingForm(ApiCatalogService catalog, SoftwareItem software)
        {
            _catalog = catalog;
            _software = software;
            Text = "发布评分";
            Width = 520;
            Height = 280;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("星级", 20, 24));
            _stars.Left = 120;
            _stars.Top = 22;
            _stars.Width = 160;
            _stars.DropDownStyle = ComboBoxStyle.DropDownList;
            _stars.Items.AddRange(new object[] { "1星", "2星", "3星", "4星", "5星" });
            _stars.SelectedIndex = 4;
            Controls.Add(_stars);

            Controls.Add(FormTools.Label("评论", 20, 60));
            _comment = FormTools.TextBox("", 120, 58, 350, false, true);
            Controls.Add(_comment);

            var save = FormTools.Button("发布", 260, 195);
            var cancel = FormTools.Button("取消", 370, 195);
            save.Click += async (s, e) => await SaveAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private async Task SaveAsync()
        {
            try
            {
                await _catalog.SaveRatingAsync(_software.Id, _stars.SelectedIndex + 1, _comment.Text);
                MessageBox.Show("评分已发布。", "评分");
                Close();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }
    }
}
