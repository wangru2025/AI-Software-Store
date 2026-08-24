using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class SubmitSoftwareForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _path;

        public SubmitSoftwareForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "投稿软件";
            Width = 620;
            Height = 175;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("zip 文件", 18, 24));
            _path = FormTools.TextBox("", 100, 22, 390);
            Controls.Add(_path);

            var browse = FormTools.Button("浏览", 500, 20, 75);
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            var upload = FormTools.Button("上传", 255, 75);
            upload.Click += async (s, e) => await UploadAsync();
            Controls.Add(upload);

            var help = FormTools.Button("投稿说明", 365, 75);
            help.Click += (s, e) =>
            {
                using (var form = new SubmissionGuideForm())
                {
                    form.ShowDialog(this);
                }
            };
            Controls.Add(help);

            var cancel = FormTools.Button("取消", 475, 75);
            cancel.Click += (s, e) => Close();
            Controls.Add(cancel);
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "zip 文件|*.zip";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _path.Text = dialog.FileName;
                }
            }
        }

        private async Task UploadAsync()
        {
            try
            {
                await _catalog.UploadSubmissionAsync(_path.Text);
                MessageBox.Show("上传成功，已保存为草稿。", "投稿软件");
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("投稿失败", ex);
                FormTools.ShowError(ex);
            }
        }
    }
}
