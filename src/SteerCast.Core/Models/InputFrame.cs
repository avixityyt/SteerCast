namespace SteerCast.Core.Models;

public sealed record InputFrame
{
    public required long Sequence { get; init; }
    public required long Timestamp { get; init; }
    public required string DeviceId { get; init; }
    public required bool Connected { get; init; }
    public double Steering { get; init; }
    public double Throttle { get; init; }
    public double Brake { get; init; }
    public double Clutch { get; init; }
    public double Handbrake { get; init; }
    public int Gear { get; init; }
    public ulong Buttons { get; init; }
    public double? GameTelemetryStrength { get; init; }
    public int GameTelemetryDirection { get; init; }
    public string? GameTelemetryKind { get; init; }
    public string? GameTelemetrySource { get; init; }
}
