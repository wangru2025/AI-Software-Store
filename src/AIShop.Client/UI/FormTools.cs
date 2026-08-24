using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIShop.Client.UI
{
    internal static class FormTools
    {
        public static void EnableEscClose(Form form)
        {
            form.KeyPreview = true;
            form.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Escape)
                {
                    form.Close();
                }
            };
        }

        public static Button Button(string text, int left, int top, int width = 100)
        {
            return new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 28
            };
        }

        public static TextBox TextBox(string prompt, int left, int top, int width, bool password = false, bool multiline = false)
        {
            return TextBox(null, prompt, left, top, width, password, multiline);
        }

        public static TextBox TextBox(string accessibleName, string prompt, int left, int top, int width, bool password = false, bool multiline = false)
        {
            var box = new TextBox
            {
                Left = left,
                Top = top,
                Width = width,
                Height = multiline ? 120 : 24,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Text = "",
                AccessibleName = accessibleName
            };

            box.AccessibleDescription = prompt;

            if (password)
            {
                box.UseSystemPasswordChar = true;
            }

            return box;
        }

        public static Label Label(string text, int left, int top, int width = 120)
        {
            return new Label
            {
                Text = text,
                Left = left,
                Top = top + 4,
                Width = width,
                Height = 20
            };
        }

        public static ListBox ListBox()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
        }

        public static void ShowError(Exception exception)
        {
            MessageBox.Show(exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
