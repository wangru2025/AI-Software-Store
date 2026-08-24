using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIShop.Shared;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public static class ElevatedInstallWorker
    {
        private const string WorkerSwitch = "--install-worker";
        private const string RecordWorkerSwitch = "--record-worker";

        public static async Task InstallAsync(string zipPath, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            var manifest = PackageInstaller.ReadManifest(zipPath);
            if (manifest == null)
            {
                throw new InvalidOperationException("软件包信息不完整。");
            }

            if (!manifest.requiresAdmin || PackageInstaller.IsAdministrator())
            {
                var installer = new PackageInstaller();
                await installer.InstallAsync(zipPath, progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            await RunElevatedAsync(zipPath, progress, cancellationToken).ConfigureAwait(false);
        }

        public static bool TryRunFromCommandLine(string[] args)
        {
            var parsed = ParseArgs(args);
            if (parsed.ContainsKey(RecordWorkerSwitch))
            {
                var id = Value(parsed, "--remove-installed");
                new InstalledPackageStore().RemoveLocal(id);
                return true;
            }

            if (!parsed.ContainsKey(WorkerSwitch))
            {
                return false;
            }

            var zipPath = Value(parsed, "--zip");
            var pipeName = Value(parsed, "--pipe");
            var nonce = Value(parsed, "--nonce");
            RunWorkerAsync(zipPath, pipeName, nonce).GetAwaiter().GetResult();
            return true;
        }

        public static void RemoveInstalledRecord(string id)
        {
            try
            {
                using (var process = StartElevatedProcess(RecordWorkerSwitch + " --remove-installed " + Quote(id)))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("卸载已完成，但清理本地安装记录失败。");
                    }
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    throw new OperationCanceledException("用户取消了管理员权限请求。", ex);
                }

                throw;
            }
        }

        private static async Task RunElevatedAsync(string zipPath, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            progress.Report(new ProgressSnapshot { Percent = 5, Message = "正在请求管理员权限" });

            var pipeName = "AIShop.Install." + Guid.NewGuid().ToString("N");
            var nonce = Guid.NewGuid().ToString("N");

            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            using (var process = StartElevatedWorker(zipPath, pipeName, nonce))
            {
                var connectTask = pipe.WaitForConnectionAsync(cancellationToken);
                var exitTask = Task.Run(() =>
                {
                    process.WaitForExit();
                    return process.ExitCode;
                });

                var first = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);
                if (first == exitTask)
                {
                    throw new InvalidOperationException("管理员安装进程没有正常启动。");
                }

                await connectTask.ConfigureAwait(false);
                progress.Report(new ProgressSnapshot { Percent = 8, Message = "已获得管理员权限，正在安装" });

                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                using (cancellationToken.Register(() => SendCancel(writer, nonce)))
                {
                    while (true)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }

                        var message = JsonConvert.DeserializeObject<WorkerMessage>(line);
                        if (message == null || !string.Equals(message.Nonce, nonce, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (message.Type == "progress" && message.Progress != null)
                        {
                            progress.Report(message.Progress);
                        }
                        else if (message.Type == "complete")
                        {
                            return;
                        }
                        else if (message.Type == "canceled")
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                        else if (message.Type == "error")
                        {
                            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message.Error) ? "管理员安装失败。" : message.Error);
                        }
                    }
                }
            }

            throw new InvalidOperationException("管理员安装进程已退出，但没有返回安装结果。");
        }

        private static Process StartElevatedWorker(string zipPath, string pipeName, string nonce)
        {
            try
            {
                return StartElevatedProcess(BuildWorkerArguments(zipPath, pipeName, nonce));
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    throw new OperationCanceledException("用户取消了管理员权限请求。", ex);
                }

                throw;
            }
        }

        private static Process StartElevatedProcess(string arguments)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null)
            {
                throw new InvalidOperationException("管理员进程没有正常启动。");
            }

            return process;
        }

        private static async Task RunWorkerAsync(string zipPath, string pipeName, string nonce)
        {
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(30000).ConfigureAwait(false);

                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                using (var cts = new CancellationTokenSource())
                {
                    var commandReader = ReadCommandsAsync(reader, nonce, cts);
                    var progress = new DirectProgress(snapshot => Send(writer, new WorkerMessage
                    {
                        Nonce = nonce,
                        Type = "progress",
                        Progress = snapshot
                    }));

                    try
                    {
                        var installer = new PackageInstaller();
                        await installer.InstallAsync(zipPath, progress, cts.Token).ConfigureAwait(false);
                        Send(writer, new WorkerMessage { Nonce = nonce, Type = "complete" });
                    }
                    catch (OperationCanceledException)
                    {
                        Send(writer, new WorkerMessage { Nonce = nonce, Type = "canceled" });
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("管理员安装失败", ex);
                        Send(writer, new WorkerMessage { Nonce = nonce, Type = "error", Error = ex.Message });
                    }

                    cts.Cancel();
                    _ = IgnoreErrors(commandReader);
                }
            }
        }

        private static async Task ReadCommandsAsync(StreamReader reader, string nonce, CancellationTokenSource cts)
        {
            while (!cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    return;
                }

                var message = JsonConvert.DeserializeObject<WorkerMessage>(line);
                if (message != null && message.Nonce == nonce && message.Type == "cancel")
                {
                    cts.Cancel();
                    return;
                }
            }
        }

        private static Task IgnoreErrors(Task task)
        {
            if (task.IsCompleted)
            {
                return Task.CompletedTask;
            }

            return Task.Run(async () =>
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }

        private static void SendCancel(StreamWriter writer, string nonce)
        {
            try
            {
                Send(writer, new WorkerMessage { Nonce = nonce, Type = "cancel" });
            }
            catch
            {
            }
        }

        private static void Send(StreamWriter writer, WorkerMessage message)
        {
            writer.WriteLine(JsonConvert.SerializeObject(message));
        }

        private static string BuildWorkerArguments(string zipPath, string pipeName, string nonce)
        {
            return WorkerSwitch + " --zip " + Quote(zipPath) + " --pipe " + Quote(pipeName) + " --nonce " + Quote(nonce);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args == null)
            {
                return parsed;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                if (!key.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = "";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                parsed[key] = value;
            }

            return parsed;
        }

        private static string Value(Dictionary<string, string> args, string key)
        {
            string value;
            if (!args.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("缺少安装 worker 参数：" + key);
            }
            return value;
        }

        private sealed class WorkerMessage
        {
            public string Nonce { get; set; }
            public string Type { get; set; }
            public string Error { get; set; }
            public ProgressSnapshot Progress { get; set; }
        }

        private sealed class DirectProgress : IProgress<ProgressSnapshot>
        {
            private readonly Action<ProgressSnapshot> _handler;

            public DirectProgress(Action<ProgressSnapshot> handler)
            {
                _handler = handler;
            }

            public void Report(ProgressSnapshot value)
            {
                _handler(value);
            }
        }
    }
}
