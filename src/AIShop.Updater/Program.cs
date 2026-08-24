using System;
using System.Windows.Forms;

namespace AIShop.Updater
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateForm(UpdateArguments.Parse(args)));
        }
    }
}
