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
            || Changed(previous.Handbrake, next.Handbrake);
    }

    private bool Changed(double previous, double next) => Math.Abs(previous - next) >= analogThreshold;

}
