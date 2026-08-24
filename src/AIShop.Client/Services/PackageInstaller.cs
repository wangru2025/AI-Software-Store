using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AIShop.Shared;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public sealed class PackageInstaller
    {
        private readonly PowerShellPackageExecutor _executor = new PowerShellPackageExecutor();
        private readonly InstalledPackageStore _installed = new InstalledPackageStore();

        public async Task InstallAsync(string zipPath, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            var workDir = Path.Combine(Path.GetTempPath(), "AI软件商店", "packages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            progress.Report(new ProgressSnapshot { Percent = 0, Message = "正在准备安装" });
            ExtractSafe(zipPath, workDir);

            var manifestPath = Path.Combine(workDir, "aishop.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("软件包缺少 aishop.json。");
            }

            var manifest = JsonConvert.DeserializeObject<PackageManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.id) || string.IsNullOrWhiteSpace(manifest.version))
            {
                throw new InvalidOperationException("软件包信息不完整。");
            }

            if (manifest.requiresAdmin && !IsAdministrator())
            {
                throw new InvalidOperationException("这个软件需要管理员权限，请以管理员身份运行 AI 软件商店后再安装。");
            }

            var installScript = string.IsNullOrWhiteSpace(manifest.install) ? "install.ps1" : manifest.install;
            progress.Report(new ProgressSnapshot { Percent = 5, Message = "正在安装" });
            var result = await _executor.ExecuteAsync(workDir, installScript, progress, cancellationToken).ConfigureAwait(false);

            var cacheDir = CachePackageFiles(workDir, manifest.id, manifest.version);
            var uninstallCommand = result.UninstallCommand;
            var uninstallArguments = result.UninstallArguments;
            if (string.IsNullOrWhiteSpace(uninstallCommand) && !string.IsNullOrWhiteSpace(manifest.uninstall))
            {
                var uninstallScript = Path.Combine(cacheDir, manifest.uninstall);
                if (File.Exists(uninstallScript))
                {
                    uninstallCommand = "powershell.exe";
                    uninstallArguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\"";
                }
            }

            _installed.Save(new InstalledPackage
            {
                Id = manifest.id,
                Name = manifest.name,
                Version = manifest.version,
                InstallLocation = result.InstallLocation,
                PackageCacheDir = cacheDir,
                UninstallCommand = uninstallCommand,
                UninstallArguments = uninstallArguments,
                LaunchPath = result.LaunchPath,
                LaunchArguments = result.LaunchArguments,
                InstalledAt = DateTime.Now
            });
            progress.Report(new ProgressSnapshot { Percent = 100, Message = "安装完成", IsCompleted = true });
        }

        public void Launch(SoftwareItem item)
        {
            var installed = _installed.Find(item.Id);
            if (installed == null)
            {
                throw new InvalidOperationException("本机没有找到该软件的安装记录。");
            }

            if (string.IsNullOrWhiteSpace(installed.LaunchPath))
            {
                throw new InvalidOperationException("该软件没有记录启动路径，请重新安装或更新后再试。");
            }

            if (!File.Exists(installed.LaunchPath) && !Directory.Exists(installed.LaunchPath))
            {
                throw new FileNotFoundException("找不到该软件的启动文件。", installed.LaunchPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installed.LaunchPath,
                Arguments = installed.LaunchArguments ?? "",
                UseShellExecute = true
            });
        }

        public async Task UninstallAsync(SoftwareItem item, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            var installed = _installed.Find(item.Id);
            if (installed == null)
            {
                throw new InvalidOperationException("本机没有找到该软件的安装记录。");
            }

            if (!string.IsNullOrWhiteSpace(installed.UninstallCommand))
            {
                progress.Report(new ProgressSnapshot { Percent = 10, Message = "正在启动卸载程序" });
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installed.UninstallCommand,
                    Arguments = installed.UninstallArguments ?? "",
                    UseShellExecute = true
                };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    while (!process.HasExited)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }
                }
                _installed.Remove(item.Id);
                progress.Report(new ProgressSnapshot { Percent = 100, Message = "卸载完成", IsCompleted = true });
                return;
            }

            throw new InvalidOperationException("该软件没有提供卸载方式。");
        }

        private static void ExtractSafe(string zipPath, string targetDir)
        {
            var root = Path.GetFullPath(targetDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var targetPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("软件包包含不安全路径。");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    entry.ExtractToFile(targetPath, true);
                }
            }
        }

        private static string CachePackageFiles(string sourceDir, string id, string version)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AI软件商店",
                "Packages",
                SafePathPart(id),
                SafePathPart(version));

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            CopyDirectory(sourceDir, root);
            return root;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(sourceDir, targetDir), true);
            }
        }

        private static string SafePathPart(string value)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(ch, '_');
            }
            return value.Replace("..", "_");
        }

        private static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
