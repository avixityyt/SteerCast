using SteerCast.Core;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class CalibrationFactoryTests
{
    [Fact]
    public void BuildsCalibrationFromFiniteSamples()
    {
        var calibration = CalibrationFactory.FromSamples(new CalibrationRequest
        {
            Samples = [0.9, 0.1, 0.5, double.NaN],
            Center = 0.48,
            Centered = true,
            DeadZone = 0.02
        });

        Assert.Equal(0.1, calibration.Minimum);
        Assert.Equal(0.9, calibration.Maximum);
        Assert.Equal(0.48, calibration.Center);
        Assert.True(calibration.Centered);
    }

    [Fact]
    public void RejectsAStationaryAxis()
    {
        Assert.Throws<ArgumentException>(() => CalibrationFactory.FromSamples(new CalibrationRequest
        {
            Samples = [0.5, 0.5]
        }));
    }
}
