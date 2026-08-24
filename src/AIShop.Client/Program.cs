using System;
using System.Configuration;
using System.Windows.Forms;
using AIShop.Client.Services;

namespace AIShop.Client
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var baseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "http://localhost:8080";
            var pinnedCertificateSha256 = ConfigurationManager.AppSettings["ApiPinnedCertificateSha256"];
            CertificatePinning.Configure(baseUrl, pinnedCertificateSha256);
            var catalog = new ApiCatalogService(baseUrl);
            Application.Run(new MainForm(catalog));
        }
    }
}
