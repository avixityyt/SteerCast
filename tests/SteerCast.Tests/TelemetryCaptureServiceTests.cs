using System.Text.Json;
using SteerCast.App.Services;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class TelemetryCaptureServiceTests
{
    [Fact]
    public async Task RecordsExistingBroadcastFramesAndSavesBoundedJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"steercast-capture-{Guid.NewGuid():N}");
        try
        {
            var profileStore = new ProfileStore(Path.Combine(directory, "profiles"));
            await profileStore.SaveAsync(new OverlayProfile { Id = "default", Name = "Default", FramesPerSecond = 60 });
            using var gameIntegrations = new GameIntegrationManager(
                Path.Combine(directory, "game-integration.json"),
                directory);
            await using var broadcaster = new InputBroadcaster(new TelemetryInputSource(), profileStore);
            await using var capture = new TelemetryCaptureService(
                broadcaster,
                gameIntegrations,
                Path.Combine(directory, "captures"));
            broadcaster.Start();

            var started = capture.Start(new TelemetryCaptureRequest("default", 30));
            Assert.True(started.Recording);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (capture.Status.SampleCount < 2)
            {
                await Task.Delay(25, timeout.Token);
            }

            var stopped = await capture.StopAsync();
            Assert.False(stopped.Recording);
            Assert.True(stopped.SampleCount >= 2);
            Assert.NotNull(stopped.LatestFile);
            Assert.True(File.Exists(stopped.LatestFile));

            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(stopped.LatestFile));
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(20, document.RootElement.GetProperty("sampleRateLimitHz").GetInt32());
            Assert.True(document.RootElement.GetProperty("samples").GetArrayLength() >= 2);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class TelemetryInputSource : IWheelInputSource
    {
        private long _reads;

        public IReadOnlyList<DeviceDescriptor> GetDevices() =>
            [new("fake", "Fake wheel", 0, 0, 4, 8, 0, true, true)];

        public RawDeviceReading? GetRawReading(string deviceId) => null;

        public InputFrame Read(OverlayProfile profile, long sequence)
        {
            var reads = Interlocked.Increment(ref _reads);
            return new InputFrame
            {
                Sequence = sequence,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DeviceId = "fake",
                Connected = true,
                Steering = Math.Sin(reads / 8d) * 0.5,
                Throttle = 0.7,
                Gear = 3,
                GameTelemetryStrength = 0.6,
                GameTelemetrySpeed = 22,
                GameTelemetrySlipAngle = 7,
                GameTelemetryYawRate = 0.45,
                GameTelemetryKind = "derived-telemetry",
                GameTelemetrySource = "test"
            };
        }

        public void Refresh()
        {
        }
    }
}
