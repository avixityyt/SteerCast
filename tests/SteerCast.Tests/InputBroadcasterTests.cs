using SteerCast.App.Services;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class InputBroadcasterTests
{
    [Fact]
    public async Task PublishesToMultipleClientsAndStopsCaptureWhenTheyLeave()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"steercast-broadcast-{Guid.NewGuid():N}");
        try
        {
            var store = new ProfileStore(directory);
            await store.SaveAsync(new OverlayProfile { Id = "default", Name = "Default" });
            var source = new FakeInputSource();
            await using var broadcaster = new InputBroadcaster(source, store);
            using var first = broadcaster.Subscribe("default");
            using var second = broadcaster.Subscribe("default");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            broadcaster.Start();
            var firstFrame = await first.Reader.ReadAsync(timeout.Token);
            var secondFrame = await second.Reader.ReadAsync(timeout.Token);
            await broadcaster.StopAsync();

            Assert.True(firstFrame.Connected);
            Assert.Equal(firstFrame.DeviceId, secondFrame.DeviceId);
            Assert.True(source.ReadCount > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task PublishesHeartbeatWhenValuesDoNotChange()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"steercast-heartbeat-{Guid.NewGuid():N}");
        try
        {
            var store = new ProfileStore(directory);
            await store.SaveAsync(new OverlayProfile { Id = "default", Name = "Default" });
            await using var broadcaster = new InputBroadcaster(new FakeInputSource(), store);
            using var subscription = broadcaster.Subscribe("default");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            broadcaster.Start();
            var first = await subscription.Reader.ReadAsync(timeout.Token);
            var second = await subscription.Reader.ReadAsync(timeout.Token);
            await broadcaster.StopAsync();

            Assert.True(first.Connected);
            Assert.True(second.Connected);
            Assert.True(second.Sequence > first.Sequence);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class FakeInputSource : IWheelInputSource
    {
        public int ReadCount { get; private set; }

        public IReadOnlyList<DeviceDescriptor> GetDevices() =>
            [new("fake", "Fake wheel", 0, 0, 4, 8, 0, true, true)];

        public RawDeviceReading? GetRawReading(string deviceId) => null;

        public InputFrame Read(OverlayProfile profile, long sequence)
        {
            ReadCount++;
            return new InputFrame
            {
                Sequence = sequence,
                Timestamp = sequence,
                DeviceId = "fake",
                Connected = true,
                Steering = 0.25
            };
        }

        public void Refresh()
        {
        }
    }
}
