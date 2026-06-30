namespace SteerCast.Core.Models;

public sealed record AxisCalibration
{
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 1;
    public double Center { get; init; } = 0.5;
    public double DeadZone { get; init; }
    public bool Inverted { get; init; }
    public bool Centered { get; init; }
}

public sealed record InputMapping
{
    public int SteeringAxis { get; init; } = 0;
    public int ThrottleAxis { get; init; } = 1;
    public int BrakeAxis { get; init; } = 2;
    public int ClutchAxis { get; init; } = 3;
    public int HandbrakeAxis { get; init; } = -1;
    public int LeftPaddleButton { get; init; } = -1;
    public int RightPaddleButton { get; init; } = -1;
    public int[] GearButtons { get; init; } = [];
    public AxisCalibration Steering { get; init; } = new() { Centered = true };
    public AxisCalibration Throttle { get; init; } = new();
    public AxisCalibration Brake { get; init; } = new();
    public AxisCalibration Clutch { get; init; } = new();
    public AxisCalibration Handbrake { get; init; } = new();
}

