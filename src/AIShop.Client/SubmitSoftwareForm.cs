using System;
using System.Windows.Forms;
using AIShop.Client.Services;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class SubmitSoftwareForm : Form
    {
        private readonly ApiCatalogService _catalog;
        private readonly TextBox _path;
        private readonly Button _upload;
        private bool _uploading;

        public SubmitSoftwareForm(ApiCatalogService catalog)
        {
            _catalog = catalog;
            Text = "投稿软件";
            Width = 620;
            Height = 175;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            Controls.Add(FormTools.Label("zip 文件", 18, 24));
            _path = FormTools.TextBox("zip 文件", "请选择要上传的 zip 投稿包：", 100, 22, 390);
            Controls.Add(_path);

            var browse = FormTools.Button("浏览", 500, 20, 75);
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            _upload = FormTools.Button("上传", 255, 75);
            _upload.Click += (s, e) => Upload();
            Controls.Add(_upload);

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

        private void Upload()
        {
            if (_uploading)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_path.Text))
            {
                MessageBox.Show("请选择要上传的 zip 投稿包。", "投稿软件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _uploading = true;
                _upload.Enabled = false;
                var form = new UploadForm(_catalog, _path.Text);
                if (Owner != null)
                {
                    form.Show(Owner);
                }
                else
                {
                    form.Show();
                }
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("投稿失败", ex);
                FormTools.ShowError(ex);
            }
            finally
            {
                _uploading = false;
                _upload.Enabled = true;
            }
        }
    }
}
