using System.Windows.Forms;
using AIShop.Client.UI;

namespace AIShop.Client
{
    public sealed class ReplyEditorForm : Form
    {
        private readonly TextBox _body;

        public ReplyEditorForm()
        {
            Text = "回复";
            Width = 480;
            Height = 260;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _body = FormTools.TextBox("", 20, 20, 425, false, true);
            Controls.Add(_body);

            var save = FormTools.Button("发布", 235, 165);
            var cancel = FormTools.Button("取消", 345, 165);
            save.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => Close();
            Controls.Add(save);
            Controls.Add(cancel);
        }

        public string Body => _body.Text;
    }
}
