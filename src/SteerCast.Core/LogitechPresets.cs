using SteerCast.Core.Models;

namespace SteerCast.Core;

public static class LogitechPresets
{
    public const ushort LogitechVendorId = 0x046D;

    private static readonly IReadOnlyDictionary<ushort, string> Names = new Dictionary<ushort, string>
    {
        [0xC24F] = "Logitech G29",
        [0xC260] = "Logitech G29",
        [0xC262] = "Logitech G920",
        [0xC266] = "Logitech G923",
        [0xC267] = "Logitech G923",
        [0xC26D] = "Logitech G923",
        [0xC26E] = "Logitech G923"
    };

    public static string? GetName(ushort vendorId, ushort productId) =>
        vendorId == LogitechVendorId && Names.TryGetValue(productId, out var name) ? name : null;

    public static InputMapping CreateDefaultMapping(ushort vendorId, ushort productId) =>
        GetName(vendorId, productId) is null
            ? new InputMapping()
            : new InputMapping
            {
                SteeringAxis = 0,
                ThrottleAxis = 1,
                BrakeAxis = 2,
                ClutchAxis = 3,
                Steering = new AxisCalibration { Centered = true, Minimum = 0, Center = 0.5, Maximum = 1, DeadZone = 0.01 },
                Throttle = new AxisCalibration { Inverted = true },
                Brake = new AxisCalibration { Inverted = true },
                Clutch = new AxisCalibration { Inverted = true }
            };
}
