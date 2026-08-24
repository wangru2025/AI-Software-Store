using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIShop.Shared;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public sealed class PowerShellPackageExecutor
    {
        public async Task<ScriptExecutionResult> ExecuteAsync(string packageDir, string scriptName, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            var modulePath = Path.Combine(packageDir, "AIShop.Package.psm1");
            File.WriteAllText(modulePath, BuildModule(), Encoding.UTF8);

            var scriptPath = Path.Combine(packageDir, scriptName);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("找不到部署脚本。", scriptPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Import-Module '" + modulePath.Replace("'", "''") + "'; & '" + scriptPath.Replace("'", "''") + "'\"",
                WorkingDirectory = packageDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                var result = new ScriptExecutionResult();
                var stdout = ReadOutputAsync(process.StandardOutput, progress, result);
                var stderr = ReadErrorAsync(process.StandardError);

                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        File.WriteAllText(Path.Combine(packageDir, ".aishop-cancel"), "1");
                    }

                    await Task.Delay(200).ConfigureAwait(false);
                }

                await stdout.ConfigureAwait(false);
                await stderr.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("部署脚本没有完成。");
                }

                return result;
            }
        }

        private async Task ReadOutputAsync(StreamReader reader, IProgress<ProgressSnapshot> progress, ScriptExecutionResult result)
        {
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                AppLog.Install(line);
                if (!line.StartsWith("AISHOP_EVENT ", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line.Substring("AISHOP_EVENT ".Length);
                var evt = JsonConvert.DeserializeObject<ScriptEvent>(payload);
                if (evt.type == "uninstall")
                {
                    result.UninstallCommand = evt.command;
                    result.UninstallArguments = evt.arguments;
                }
                if (evt.type == "installLocation")
                {
                    result.InstallLocation = evt.path;
                }
                if (evt.type == "launch")
                {
                    result.LaunchPath = evt.path;
                    result.LaunchArguments = evt.arguments;
                }
                progress.Report(new ProgressSnapshot
                {
                    Percent = evt.percent,
                    Message = evt.message,
                    IsCompleted = evt.type == "complete",
                    IsFailed = evt.type == "error"
                });
            }
        }

        private async Task ReadErrorAsync(StreamReader reader)
        {
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                AppLog.Install("ERR " + line);
            }
        }

        private static string BuildModule()
        {
            return @"
function Write-AIShopEvent([hashtable]$obj) {
  Write-Output ('AISHOP_EVENT ' + ($obj | ConvertTo-Json -Compress))
}
function Set-AIShopStatus([string]$Message) { Write-AIShopEvent @{ type='status'; percent=0; message=$Message } }
function Set-AIShopProgress([int]$Percent, [string]$Message) { Write-AIShopEvent @{ type='progress'; percent=$Percent; message=$Message } }
function Register-AIShopUninstall([string]$Command, [string]$Arguments) { Write-AIShopEvent @{ type='uninstall'; percent=0; message='已记录卸载方式'; command=$Command; arguments=$Arguments } }
function Register-AIShopInstallLocation([string]$Path) { Write-AIShopEvent @{ type='installLocation'; percent=0; message='已记录安装位置'; path=$Path } }
function Register-AIShopLaunchPath([string]$Path, [string]$Arguments) { Write-AIShopEvent @{ type='launch'; percent=0; message='已记录启动方式'; path=$Path; arguments=$Arguments } }
function Complete-AIShopInstall() { Write-AIShopEvent @{ type='complete'; percent=100; message='安装完成' } }
function Fail-AIShopInstall([string]$Message) { Write-AIShopEvent @{ type='error'; percent=0; message=$Message }; exit 1 }
function Test-AIShopCancel() { if (Test-Path '.aishop-cancel') { throw '用户已取消操作' } }
";
        }

        private sealed class ScriptEvent
        {
            public string type { get; set; }
            public int percent { get; set; }
            public string message { get; set; }
            public string command { get; set; }
            public string arguments { get; set; }
            public string path { get; set; }
        }
    }

    public sealed class ScriptExecutionResult
    {
        public string InstallLocation { get; set; }
        public string UninstallCommand { get; set; }
        public string UninstallArguments { get; set; }
        public string LaunchPath { get; set; }
        public string LaunchArguments { get; set; }
    }
}
