using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// Optional force-feedback telemetry boundary. Implementations must never own
/// the wheel or stop another application's effects.
/// </summary>
public interface IForceFeedbackAdapter : IDisposable
{
    ForceFeedbackReading Status { get; }
    ForceFeedbackReading? Read(string deviceId);
}

public sealed class NullForceFeedbackAdapter : IForceFeedbackAdapter
{
    public ForceFeedbackReading Status { get; }

    public NullForceFeedbackAdapter()
    {
        var gHub = LogitechGHubDetector.Detect();
        Status = new ForceFeedbackReading(
            null,
            null,
            "none",
            false,
            gHub.Installed
                ? "G HUB detected. The separate Logitech telemetry adapter is not installed."
                : "G HUB and the optional Logitech telemetry adapter are not installed.",
            gHub.Installed,
            gHub.Running);
    }

    public ForceFeedbackReading? Read(string deviceId) => null;

    public void Dispose()
    {
    }
}
