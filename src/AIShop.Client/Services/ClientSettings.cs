namespace AIShop.Client.Services
{
    public sealed class ClientSettings
    {
        public bool AutoStart { get; set; }
        public bool StartHiddenToTray { get; set; }
        public string TempPackageDirectory { get; set; }
        public bool AutoReportLogsOnError { get; set; } = true;
    }
}
