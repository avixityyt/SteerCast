using System.Text.Json.Serialization;
using System.Windows.Forms;
using SteerCast.App;
using SteerCast.App.Services;
using SteerCast.Core.Models;

internal static class Program
{
    private const int Port = 38271;

    [STAThread]
    private static void Main(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    private static async Task RunAsync(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var profileStore = new ProfileStore();

        using var inputSource = new WindowsWheelInputSource();
        await using var broadcaster = new InputBroadcaster(inputSource, profileStore);
        await using var server = new LocalServer(Port, profileStore, inputSource, broadcaster);
        server.Start();
        var alwaysOnTop = args.Contains("--always-on-top", StringComparer.OrdinalIgnoreCase);

        using var tray = new NativeTrayApplication(
            server,
            inputSource,
            server.BaseUrl,
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "brand", "app-icon.ico"),
            alwaysOnTop);

        if (!args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            tray.OpenSetup(showLaunchSplash: true);
        }

        tray.Run();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(InputFrame))]
[JsonSerializable(typeof(OverlayProfile))]
[JsonSerializable(typeof(OverlayProfile[]))]
[JsonSerializable(typeof(DeviceDescriptor[]))]
[JsonSerializable(typeof(RawDeviceReading))]
[JsonSerializable(typeof(CalibrationRequest))]
[JsonSerializable(typeof(AxisCalibration))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
