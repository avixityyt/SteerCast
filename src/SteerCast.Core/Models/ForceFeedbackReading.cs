namespace SteerCast.Core.Models;

/// <summary>
/// Force/torque reported by an optional vendor adapter. Values are device units
/// unless the adapter also provides a scale. They are intentionally nullable:
/// normal Windows input capture must continue to work without an adapter.
/// </summary>
public sealed record ForceFeedbackReading(
    double? Force,
    double? Torque,
    string Source,
    bool Available,
    string? Status = null);
