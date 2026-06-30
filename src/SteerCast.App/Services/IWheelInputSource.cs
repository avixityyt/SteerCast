using SteerCast.Core.Models;

namespace SteerCast.App.Services;

public interface IWheelInputSource
{
    IReadOnlyList<DeviceDescriptor> GetDevices();
    RawDeviceReading? GetRawReading(string deviceId);
    InputFrame Read(OverlayProfile profile, long sequence);
    void Refresh();
}
