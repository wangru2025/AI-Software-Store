using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AIShop.Client.Services
{
    public sealed class SingleInstanceManager : IDisposable
    {
        private const string MutexName = "AIShop.Client.SingleInstance";
        private const string PipeName = "AIShop.Client.Activation";
        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Action _activate;
        private readonly Form _form;

        private SingleInstanceManager(Mutex mutex, Form form, Action activate)
        {
            _mutex = mutex;
            _form = form;
            _activate = activate;
        }

        public static bool TryCreate(Form form, Action activate, out SingleInstanceManager manager)
        {
            bool createdNew;
            var mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                NotifyExistingInstance();
                manager = null;
                return false;
            }

            manager = new SingleInstanceManager(mutex, form, activate);
            manager.StartServer();
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _cts.Dispose();
        }

        private void StartServer()
        {
            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                            using (var reader = new StreamReader(pipe, Encoding.UTF8))
                            {
                                var message = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (message == "activate")
                                {
                                    _form.BeginInvoke((Action)(() =>
                                    {
                                        _activate();
                                        BringToForeground(Form.ActiveForm ?? _form);
                                    }));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch
                    {
                    }
                }
            });
        }

        private static void NotifyExistingInstance()
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipe.Connect(1000);
                    using (var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true })
                    {
                        writer.WriteLine("activate");
                    }
                }
            }
            catch
            {
            }
        }

        private static void BringToForeground(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Show();
            form.Activate();
            SetForegroundWindow(form.Handle);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
