using System.Windows.Forms;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class ChangelogDetailForm : Form
    {
        public ChangelogDetailForm(ChangelogEntry entry)
        {
            Text = "更新日志";
            Width = 700;
            Height = 460;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Text = entry.Body
            };
            Controls.Add(text);
        }
    }
}
