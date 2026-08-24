using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class EditSoftwareInfoForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly SubmissionItem _item;
        private readonly TextBox _name;
        private readonly TextBox _summary;

        public EditSoftwareInfoForm(ApiCatalogService catalog, SubmissionItem item)
        {
            _catalog = catalog;
            _item = item;
            Text = "编辑投稿";
            Width = 560;
            Height = 300;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("软件名称", 20, 24));
            _name = FormTools.TextBox("", 120, 22, 380);
            _name.Text = item.Name;
            Controls.Add(_name);

            Controls.Add(FormTools.Label("简介", 20, 60));
            _summary = FormTools.TextBox("", 120, 58, 380, false, true);
            _summary.Text = item.Summary;
            Controls.Add(_summary);

            var save = FormTools.Button("保存", 290, 205);
            var cancel = FormTools.Button("取消", 400, 205);
            save.Click += async (s, e) => await SaveAsync();
            cancel.Click += (s, e) => Close();
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private async Task SaveAsync()
        {
            try
            {
                await _catalog.UpdateSoftwareInfoAsync(_item.SoftwareId, _name.Text, _summary.Text);
                MessageBox.Show("保存成功。", "编辑投稿");
                Close();
            }
            catch (Exception ex)
            {
                FormTools.ShowError(ex);
            }
        }
    }
}
