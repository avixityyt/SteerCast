using SteerCast.Core.Models;

namespace SteerCast.Core;

public static class CalibrationFactory
{
    public static AxisCalibration FromSamples(CalibrationRequest request)
    {
        var samples = request.Samples.Where(double.IsFinite).ToArray();
        if (samples.Length < 2)
        {
            throw new ArgumentException("At least two finite samples are required.", nameof(request));
        }

        var minimum = samples.Min();
        var maximum = samples.Max();
        if (maximum - minimum < 0.001)
        {
            throw new ArgumentException("The captured input range is too small.", nameof(request));
        }

        return new AxisCalibration
        {
            Minimum = minimum,
            Maximum = maximum,
            Center = request.Center is { } center && double.IsFinite(center)
                ? Math.Clamp(center, minimum, maximum)
                : (minimum + maximum) / 2,
            DeadZone = Math.Clamp(request.DeadZone, 0, 0.25),
            Centered = request.Centered,
            Inverted = request.Inverted
        };
    }
}

