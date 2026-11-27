using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace StockDesk.Desktop;

public partial class App : Application
{
    private Process? server;
    internal static Uri ApiAddress { get; private set; } = new("http://127.0.0.1:5187");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Uri.TryCreate(Environment.GetEnvironmentVariable("STOCKDESK_URL"), UriKind.Absolute, out var configured))
        {
            ApiAddress = configured;
            new MainWindow().Show();
            return;
        }
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { ApiAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}"); }
            finally { listener.Stop(); }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var exe = Path.Combine(AppContext.BaseDirectory, "server", "StockDesk.Api.exe");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add(ApiAddress.AbsoluteUri);
            start.ArgumentList.Add("--parent-pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            server = Process.Start(start) ?? throw new InvalidOperationException("Сервер не запустился.");
            for (int i = 0; i < 50; i++)
            {
                if (await Ready(http))
                {
                    new MainWindow().Show();
                    return;
                }
                if (server.HasExited) break;
                await Task.Delay(200);
            }
            throw new InvalidOperationException("Локальный сервер не ответил при запуске.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка запуска StockDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task<bool> Ready(HttpClient http)
    {
        try { return (await http.GetStringAsync(new Uri(ApiAddress, "/health"))).Contains("StockDesk"); }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (server is { } process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        base.OnExit(e);
    }
}
