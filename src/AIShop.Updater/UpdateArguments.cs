using System;

namespace AIShop.Updater
{
    public sealed class UpdateArguments
    {
        public string Url { get; set; }
        public string File { get; set; }
        public string Sha256 { get; set; }
        public string TargetDir { get; set; }
        public string RestartExe { get; set; }

        public static UpdateArguments Parse(string[] args)
        {
            var result = new UpdateArguments();
            for (var i = 0; i < args.Length - 1; i += 2)
            {
                var key = args[i].TrimStart('-', '/').ToLowerInvariant();
                var value = args[i + 1];
                if (key == "url") result.Url = value;
                if (key == "file") result.File = value;
                if (key == "sha256") result.Sha256 = value;
                if (key == "target") result.TargetDir = value;
                if (key == "restart") result.RestartExe = value;
            }
            return result;
        }
    }
}
