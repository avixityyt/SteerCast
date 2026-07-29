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
    public ForceFeedbackReading Status { get; } = new(
        null,
        null,
        "none",
        false,
        "Optional Logitech telemetry adapter is not installed.");

    public ForceFeedbackReading? Read(string deviceId) => null;

    public void Dispose()
    {
    }
}
