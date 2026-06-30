using SteerCast.Core;

namespace SteerCast.Tests;

public sealed class LogitechPresetTests
{
    [Theory]
    [InlineData(0xC24F, "Logitech G29")]
    [InlineData(0xC260, "Logitech G29")]
    [InlineData(0xC262, "Logitech G920")]
    [InlineData(0xC266, "Logitech G923")]
    [InlineData(0xC267, "Logitech G923")]
    [InlineData(0xC26D, "Logitech G923")]
    [InlineData(0xC26E, "Logitech G923")]
    public void RecognizesSupportedLogitechWheelModes(ushort productId, string expected)
    {
        Assert.Equal(expected, LogitechPresets.GetName(LogitechPresets.LogitechVendorId, productId));
    }
}
