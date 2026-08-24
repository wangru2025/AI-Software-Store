using System;
using System.Windows.Forms;

namespace AIShop.Updater
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateForm(UpdateArguments.Parse(args)));
        }
    }
}
