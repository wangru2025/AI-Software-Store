using System;
using System.IO;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public static class ClientSettingsStore
    {
        private static readonly object SyncRoot = new object();
        private static ClientSettings _current;

        public static ClientSettings Current
        {
            get
            {
                lock (SyncRoot)
                {
                    return _current ?? (_current = LoadInternal());
                }
            }
        }

        public static ClientSettings Load()
        {
            lock (SyncRoot)
            {
                _current = LoadInternal();
                return _current;
            }
        }

        public static void Save(ClientSettings settings)
        {
            lock (SyncRoot)
            {
                _current = Normalize(settings);
                Directory.CreateDirectory(AppPaths.DataDir);
                AppDataSecurity.EnsureUsersCanModifyFile(AppPaths.SettingsPath);
                File.WriteAllText(AppPaths.SettingsPath, JsonConvert.SerializeObject(_current, Formatting.Indented));
                AppDataSecurity.EnsureUsersCanModifyFile(AppPaths.SettingsPath);
            }
        }

        private static ClientSettings LoadInternal()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsPath))
                {
                    var settings = JsonConvert.DeserializeObject<ClientSettings>(File.ReadAllText(AppPaths.SettingsPath));
                    return Normalize(settings);
                }
            }
            catch (Exception ex)
            {
                AppLog.Client("读取设置失败：" + ex.Message);
            }

            return Defaults();
        }

        private static ClientSettings Normalize(ClientSettings settings)
        {
            settings = settings ?? Defaults();
            if (string.IsNullOrWhiteSpace(settings.TempPackageDirectory))
            {
                settings.TempPackageDirectory = AppPaths.DefaultTempRoot;
            }
            settings.TempPackageDirectory = Environment.ExpandEnvironmentVariables(settings.TempPackageDirectory.Trim());
            return settings;
        }

        private static ClientSettings Defaults()
        {
            return new ClientSettings
            {
                AutoStart = false,
                StartHiddenToTray = false,
                TempPackageDirectory = AppPaths.DefaultTempRoot,
                AutoReportLogsOnError = true
            };
        }
    }
}
