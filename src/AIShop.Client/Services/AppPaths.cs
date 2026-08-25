using System;
using System.IO;

namespace AIShop.Client.Services
{
    public static class AppPaths
    {
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AI软件商店");

        public static readonly string LogsDir = Path.Combine(DataDir, "Logs");

        public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

        public static string DefaultTempRoot => Path.Combine(Path.GetTempPath(), "AI软件商店");

        public static string TempRoot()
        {
            var configured = ClientSettingsStore.Current.TempPackageDirectory;
            return string.IsNullOrWhiteSpace(configured) ? DefaultTempRoot : configured;
        }
    }
}
