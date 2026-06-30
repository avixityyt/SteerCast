namespace SteerCast.Core.Models;

public sealed record DeviceDescriptor(
    string Id,
    string Name,
    ushort VendorId,
    ushort ProductId,
    int AxisCount,
    int ButtonCount,
    int SwitchCount,
    bool IsRacingWheel,
    bool Connected);

