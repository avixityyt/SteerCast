using SteerCast.Core;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class FrameChangeDetectorTests
{
    [Fact]
    public void FiltersAnalogNoiseButKeepsDigitalChanges()
    {
        var detector = new FrameChangeDetector(0.01);
        var initial = Frame(0, 0);

        Assert.True(detector.HasChanged(initial));
        Assert.False(detector.HasChanged(Frame(0.005, 0)));
        Assert.True(detector.HasChanged(Frame(0.005, 1)));
    }

    [Fact]
    public void PublishesGameTelemetryChanges()
    {
        var detector = new FrameChangeDetector(0.01);
        var initial = Frame(0, 0) with
        {
            GameTelemetryStrength = 0.2,
            GameTelemetryKind = "derived-telemetry",
            GameTelemetrySource = "test-game"
        };

        Assert.True(detector.HasChanged(initial));
        Assert.False(detector.HasChanged(initial with { GameTelemetryStrength = 0.205 }));
        Assert.True(detector.HasChanged(initial with { GameTelemetryStrength = 0.3 }));
        Assert.True(detector.HasChanged(initial with { GameTelemetryStrength = null }));
    }

    private static InputFrame Frame(double steering, ulong buttons) => new()
    {
        Sequence = 1,
        Timestamp = 1,
        DeviceId = "test",
        Connected = true,
        Steering = steering,
        Buttons = buttons
    };
}
