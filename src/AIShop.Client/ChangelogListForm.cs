using System.Windows.Forms;
using AIShop.Client.UI;
using AIShop.Shared;

namespace AIShop.Client
{
    public sealed class ChangelogListForm : Form
    {
        private readonly ListBox _list;
        private int _lastIndex;

        public ChangelogListForm(SoftwareItem software)
        {
            Text = "更新日志";
            Width = 520;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            FormTools.EnableEscClose(this);

            _list = FormTools.ListBox();
            Controls.Add(_list);
            foreach (var entry in software.Changelogs)
            {
                _list.Items.Add(new DisplayItem<ChangelogEntry>(entry, entry.ToListText()));
            }

            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OpenSelected();
                }
            };
            _list.DoubleClick += (s, e) => OpenSelected();
        }

        private void OpenSelected()
        {
            var selected = _list.SelectedItem as DisplayItem<ChangelogEntry>;
            if (selected == null)
            {
                return;
            }

            _lastIndex = _list.SelectedIndex;
            using (var form = new ChangelogDetailForm(selected.Value))
            {
                form.ShowDialog(this);
            }
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = _lastIndex;
            }
            _list.Focus();
        }
    }
}
