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

        using var gameIntegrations = new GameIntegrationManager();
        using var inputSource = new WindowsWheelInputSource();
        await using var broadcaster = new InputBroadcaster(inputSource, profileStore, gameIntegrations);
        await using var telemetryCapture = new TelemetryCaptureService(broadcaster, gameIntegrations);
        await using var server = new LocalServer(Port, profileStore, inputSource, broadcaster, gameIntegrations, telemetryCapture);
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
[JsonSerializable(typeof(GameIntegrationSettings))]
[JsonSerializable(typeof(GameIntegrationDescriptor[]))]
[JsonSerializable(typeof(GameTelemetryReading))]
[JsonSerializable(typeof(GameSetupState))]
[JsonSerializable(typeof(GameIntegrationSnapshot))]
[JsonSerializable(typeof(TelemetryCaptureRequest))]
[JsonSerializable(typeof(TelemetryCaptureStatus))]
[JsonSerializable(typeof(TelemetryCaptureSample[]))]
[JsonSerializable(typeof(TelemetryCaptureDocument))]
[JsonSerializable(typeof(CalibrationRequest))]
[JsonSerializable(typeof(AxisCalibration))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
