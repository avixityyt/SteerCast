using System.Collections.Concurrent;
using SteerCast.Core;
using SteerCast.Core.Models;
using Windows.Gaming.Input;

namespace SteerCast.App.Services;

public sealed class WindowsWheelInputSource : IWheelInputSource, IForceFeedbackStatusSource, IDisposable
{
    private readonly IForceFeedbackAdapter _forceFeedback;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, RawState> _rawStates = new(StringComparer.Ordinal);
    private DeviceEntry[] _devices = [];

    public WindowsWheelInputSource(IForceFeedbackAdapter? forceFeedback = null)
    {
        _forceFeedback = forceFeedback ?? new NullForceFeedbackAdapter();
        Refresh();
        RawGameController.RawGameControllerAdded += (_, _) => Refresh();
        RawGameController.RawGameControllerRemoved += (_, _) => Refresh();
        RacingWheel.RacingWheelAdded += (_, _) => Refresh();
        RacingWheel.RacingWheelRemoved += (_, _) => Refresh();
    }

    public IReadOnlyList<DeviceDescriptor> GetDevices()
    {
        lock (_sync)
        {
            return _devices.Select(entry => entry.Descriptor).ToArray();
        }
    }

    public void Refresh()
    {
        var wheels = RacingWheel.RacingWheels.ToArray();
        var occurrences = new Dictionary<(ushort VendorId, ushort ProductId, string Name), int>();
        var entries = RawGameController.RawGameControllers.Select(raw =>
        {
            RacingWheel? wheel = null;
            try
            {
                wheel = RacingWheel.FromGameController(raw);
            }
            catch (ArgumentException)
            {
                // The raw controller is not exposed as a racing wheel.
            }

            var knownName = LogitechPresets.GetName(raw.HardwareVendorId, raw.HardwareProductId);
            var name = knownName ?? raw.DisplayName ?? "Game controller";
            var key = (raw.HardwareVendorId, raw.HardwareProductId, name);
            occurrences.TryGetValue(key, out var ordinal);
            occurrences[key] = ordinal + 1;
            var id = CreateDeviceId(raw.HardwareVendorId, raw.HardwareProductId, name, ordinal);
            var descriptor = new DeviceDescriptor(
                id,
                name,
                raw.HardwareVendorId,
                raw.HardwareProductId,
                raw.AxisCount,
                raw.ButtonCount,
                raw.SwitchCount,
                wheel is not null || wheels.Any(candidate => ReferenceEquals(candidate, wheel)),
                true);

            _rawStates.GetOrAdd(id, _ => new RawState(raw));
            return new DeviceEntry(descriptor, raw, wheel);
        }).ToArray();

        lock (_sync)
        {
            _devices = entries;
        }
    }

    public RawDeviceReading? GetRawReading(string deviceId)
    {
        var normalizedDeviceId = CleanDeviceId(deviceId);
        DeviceEntry? entry;
        lock (_sync)
        {
            entry = _devices.FirstOrDefault(candidate => candidate.Descriptor.Id == normalizedDeviceId);
        }

        if (entry is null)
        {
            return null;
        }

        var state = _rawStates.GetOrAdd(entry.Descriptor.Id, _ => new RawState(entry.Raw));
        state.Read();
        return new RawDeviceReading(
            entry.Descriptor.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            [.. state.Axes],
            [.. state.Buttons],
            state.Switches.Select(position => (int)position).ToArray());
    }

    public InputFrame Read(OverlayProfile profile, long sequence)
    {
        DeviceEntry? entry;
        DeviceEntry? handbrakeEntry;
        var profileDeviceId = CleanDeviceId(profile.DeviceId);
        var handbrakeDeviceId = CleanDeviceId(profile.HandbrakeDeviceId);
        lock (_sync)
        {
            entry = _devices.FirstOrDefault(candidate => candidate.Descriptor.Id == profileDeviceId)
                ?? _devices.FirstOrDefault(candidate => candidate.Descriptor.IsRacingWheel)
                ?? _devices.FirstOrDefault();
            handbrakeEntry = !string.IsNullOrWhiteSpace(handbrakeDeviceId)
                ? _devices.FirstOrDefault(candidate => candidate.Descriptor.Id == handbrakeDeviceId)
                : null;
        }

        if (entry is null)
        {
            return Disconnected(profileDeviceId, sequence);
        }

        try
        {
            var frame = entry.Wheel is not null
                ? ReadRacingWheel(entry, profile, sequence)
                : ReadRawController(entry, profile, sequence);
            frame = ApplyHandbrakeOverride(frame, profile, handbrakeEntry, entry.Descriptor.Id);
            return ApplyForceFeedback(frame, entry.Descriptor.Id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            Refresh();
            return Disconnected(entry.Descriptor.Id, sequence);
        }
    }

    private InputFrame ApplyForceFeedback(InputFrame frame, string deviceId)
    {
        var telemetry = _forceFeedback.Read(deviceId);
        return telemetry is null
            ? frame
            : frame with
            {
                Force = telemetry.Force,
                Torque = telemetry.Torque,
                ForceFeedbackSource = telemetry.Source
            };
    }

    public ForceFeedbackReading ForceFeedbackStatus => _forceFeedback.Status;

    public void Dispose() => _forceFeedback.Dispose();

    private static InputFrame ReadRacingWheel(DeviceEntry entry, OverlayProfile profile, long sequence)
    {
        var reading = entry.Wheel!.GetCurrentReading();
        return new InputFrame
        {
            Sequence = sequence,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DeviceId = entry.Descriptor.Id,
            Connected = true,
            Steering = Clamp(reading.Wheel, -1, 1),
            Throttle = Clamp(reading.Throttle),
            Brake = Clamp(reading.Brake),
            Clutch = Clamp(reading.Clutch),
            Handbrake = profile.HandbrakeEnabled ? Clamp(reading.Handbrake) : 0,
            Gear = reading.PatternShifterGear,
            Buttons = Convert.ToUInt64(reading.Buttons)
        };
    }

    private InputFrame ReadRawController(DeviceEntry entry, OverlayProfile profile, long sequence)
    {
        var state = _rawStates.GetOrAdd(entry.Descriptor.Id, _ => new RawState(entry.Raw));
        state.Read();
        var mapping = profile.Mapping;

        return new InputFrame
        {
            Sequence = sequence,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DeviceId = entry.Descriptor.Id,
            Connected = true,
            Steering = Axis(state.Axes, mapping.SteeringAxis, mapping.Steering),
            Throttle = Axis(state.Axes, mapping.ThrottleAxis, mapping.Throttle),
            Brake = Axis(state.Axes, mapping.BrakeAxis, mapping.Brake),
            Clutch = Axis(state.Axes, mapping.ClutchAxis, mapping.Clutch),
            Handbrake = profile.HandbrakeEnabled ? Axis(state.Axes, mapping.HandbrakeAxis, mapping.Handbrake) : 0,
            Gear = InputNormalizer.ResolveGear(state.Buttons, mapping.GearButtons),
            Buttons = PackButtons(state.Buttons)
        };
    }

    private InputFrame ApplyHandbrakeOverride(
        InputFrame frame,
        OverlayProfile profile,
        DeviceEntry? handbrakeEntry,
        string primaryDeviceId)
    {
        if (!profile.HandbrakeEnabled)
        {
            return frame with { Handbrake = 0 };
        }

        if (handbrakeEntry is null || string.Equals(handbrakeEntry.Descriptor.Id, primaryDeviceId, StringComparison.Ordinal))
        {
            return frame;
        }

        try
        {
            return frame with { Handbrake = ReadHandbrake(handbrakeEntry, profile) };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            Refresh();
            return frame with { Handbrake = 0 };
        }
    }

    private double ReadHandbrake(DeviceEntry entry, OverlayProfile profile)
    {
        if (entry.Wheel is not null)
        {
            return Clamp(entry.Wheel.GetCurrentReading().Handbrake);
        }

        var state = _rawStates.GetOrAdd(entry.Descriptor.Id, _ => new RawState(entry.Raw));
        state.Read();
        return Axis(state.Axes, profile.Mapping.HandbrakeAxis, profile.Mapping.Handbrake);
    }

    private static double Axis(IReadOnlyList<double> axes, int index, AxisCalibration calibration) =>
        index >= 0 && index < axes.Count ? InputNormalizer.Normalize(axes[index], calibration) : 0;

    private static ulong PackButtons(IReadOnlyList<bool> buttons)
    {
        ulong result = 0;
        for (var index = 0; index < Math.Min(buttons.Count, 64); index++)
        {
            if (buttons[index])
            {
                result |= 1UL << index;
            }
        }

        return result;
    }

    private static InputFrame Disconnected(string deviceId, long sequence) => new()
    {
        Sequence = sequence,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        DeviceId = deviceId,
        Connected = false
    };

    private static double Clamp(double value, double minimum = 0, double maximum = 1) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0;

    private static string CleanDeviceId(string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId.Trim().Trim('\0');

    private static string CreateDeviceId(ushort vendorId, ushort productId, string name, int ordinal) =>
        $"wgi:{vendorId:x4}:{productId:x4}:{Slug(name)}:{ordinal}";

    private static string Slug(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 48)];
        var index = 0;
        var previousSeparator = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (index >= buffer.Length)
            {
                break;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                buffer[index++] = character;
                previousSeparator = false;
            }
            else if (!previousSeparator && index > 0)
            {
                buffer[index++] = '-';
                previousSeparator = true;
            }
        }

        if (index > 0 && buffer[index - 1] == '-')
        {
            index--;
        }

        return index == 0 ? "controller" : new string(buffer[..index]);
    }

    private sealed record DeviceEntry(DeviceDescriptor Descriptor, RawGameController Raw, RacingWheel? Wheel);

    private sealed class RawState(RawGameController controller)
    {
        public bool[] Buttons { get; } = new bool[controller.ButtonCount];
        public GameControllerSwitchPosition[] Switches { get; } = new GameControllerSwitchPosition[controller.SwitchCount];
        public double[] Axes { get; } = new double[controller.AxisCount];

        public void Read() => controller.GetCurrentReading(Buttons, Switches, Axes);
    }
}
