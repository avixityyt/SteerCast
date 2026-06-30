using SteerCast.Core;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class InputNormalizerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void NormalizesUncenteredAxis(double input, double expected)
    {
        var result = InputNormalizer.Normalize(input, new AxisCalibration());
        Assert.Equal(expected, result, 6);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0.5, 0)]
    [InlineData(1, 1)]
    public void NormalizesCenteredAxis(double input, double expected)
    {
        var result = InputNormalizer.Normalize(input, new AxisCalibration { Centered = true });
        Assert.Equal(expected, result, 6);
    }

    [Fact]
    public void AppliesDeadZoneAndInversion()
    {
        var calibration = new AxisCalibration { DeadZone = 0.1, Inverted = true };
        Assert.Equal(0, InputNormalizer.Normalize(0.95, calibration), 6);
        Assert.Equal(1, InputNormalizer.Normalize(0, calibration), 6);
    }

    [Fact]
    public void ResolvesReverseNeutralAndForwardGears()
    {
        var mapping = new[] { 5, 6, 7 };
        Assert.Equal(-1, InputNormalizer.ResolveGear(new[] { false, false, false, false, false, true, false, false }, mapping));
        Assert.Equal(0, InputNormalizer.ResolveGear(new bool[8], mapping));
        Assert.Equal(2, InputNormalizer.ResolveGear(new[] { false, false, false, false, false, false, false, true }, mapping));
    }
}

