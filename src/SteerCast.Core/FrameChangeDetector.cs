using SteerCast.Core.Models;

namespace SteerCast.Core;

public sealed class FrameChangeDetector(double analogThreshold = 0.001)
{
    private InputFrame? _last;

    public bool HasChanged(InputFrame next)
    {
        var previous = _last;
        _last = next;

        return previous is null
            || previous.DeviceId != next.DeviceId
            || previous.Connected != next.Connected
            || previous.Gear != next.Gear
            || previous.Buttons != next.Buttons
            || Changed(previous.Steering, next.Steering)
            || Changed(previous.Throttle, next.Throttle)
            || Changed(previous.Brake, next.Brake)
            || Changed(previous.Clutch, next.Clutch)
            || Changed(previous.Handbrake, next.Handbrake)
            || Changed(previous.DerivedLoad, next.DerivedLoad)
            || previous.DerivedLoadDirection != next.DerivedLoadDirection
            || previous.TelemetrySource != next.TelemetrySource;
    }

    private bool Changed(double previous, double next) => Math.Abs(previous - next) >= analogThreshold;

    private bool Changed(double? previous, double? next) =>
        previous.HasValue != next.HasValue
        || (previous.HasValue && next.HasValue && Changed(previous.Value, next.Value));
}
