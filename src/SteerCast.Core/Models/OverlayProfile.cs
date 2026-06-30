namespace SteerCast.Core.Models;

public sealed record OverlayElement
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Scale { get; init; } = 1;
    public bool Visible { get; init; } = true;
}

public sealed record OverlayLayout
{
    public OverlayElement Wheel { get; init; } = new() { X = 210, Y = 56 };
    public OverlayElement Pedals { get; init; } = new() { X = 35, Y = 360 };
    public OverlayElement Gear { get; init; } = new() { X = 560, Y = 92 };
    public OverlayElement Handbrake { get; init; } = new() { X = 545, Y = 330, Visible = true };
    public OverlayElement Buttons { get; init; } = new() { X = 35, Y = 535, Visible = false };
}

public sealed record OverlayTheme
{
    public string Name { get; init; } = "Palette";
    public string Accent { get; init; } = "#CEDFD9";
    public string Brake { get; init; } = "#9B6A6C";
    public string Surface { get; init; } = "#1B1815";
    public string Foreground { get; init; } = "#EBFCFB";
    public double Opacity { get; init; } = 0.88;
}

public sealed record OverlayProfile
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DeviceId { get; init; }
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 600;
    public int FramesPerSecond { get; init; } = 60;
    public double WheelRotationDegrees { get; init; } = 900;
    public bool HandbrakeEnabled { get; init; } = true;
    public string? HandbrakeDeviceId { get; init; }
    public InputMapping Mapping { get; init; } = new();
    public OverlayLayout Layout { get; init; } = new();
    public OverlayTheme Theme { get; init; } = new();
}
