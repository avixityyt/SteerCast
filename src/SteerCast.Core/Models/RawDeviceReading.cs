namespace SteerCast.Core.Models;

public sealed record RawDeviceReading(
    string DeviceId,
    long Timestamp,
    double[] Axes,
    bool[] Buttons,
    int[] Switches);

public sealed record CalibrationRequest
{
    public required double[] Samples { get; init; }
    public double? Center { get; init; }
    public double DeadZone { get; init; } = 0.01;
    public bool Centered { get; init; }
    public bool Inverted { get; init; }
}

