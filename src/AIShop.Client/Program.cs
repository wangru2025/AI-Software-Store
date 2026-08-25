using System;
using System.Configuration;
using System.Windows.Forms;
using AIShop.Client.Services;

namespace AIShop.Client
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (ElevatedInstallWorker.TryRunFromCommandLine(args))
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var baseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "http://localhost:8080";
            var pinnedCertificateSha256 = ConfigurationManager.AppSettings["ApiPinnedCertificateSha256"];
            CertificatePinning.Configure(baseUrl, pinnedCertificateSha256);
            var authStore = new AuthStore();
            var catalog = new ApiCatalogService(baseUrl, authStore);
            var settings = ClientSettingsStore.Load();
            ErrorReportService.Configure(catalog);
            var persisted = authStore.Load();
            if (persisted != null)
            {
                catalog.RestoreSession(persisted.Token, persisted.User);
            }
            var mainForm = new MainForm(catalog, settings.StartHiddenToTray);
            SingleInstanceManager instance;
            if (!SingleInstanceManager.TryCreate(mainForm, mainForm.RestoreFromTray, out instance))
            {
                return;
            }

            using (instance)
            {
                Application.Run(mainForm);
            }
        }
    }
}
