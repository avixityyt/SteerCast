using SteerCast.Core.Models;

namespace SteerCast.Core;

public static class InputNormalizer
{
    public static double Normalize(double value, AxisCalibration calibration)
    {
        var range = calibration.Maximum - calibration.Minimum;
        if (!double.IsFinite(value) || !double.IsFinite(range) || Math.Abs(range) < double.Epsilon)
        {
            return 0;
        }

        var normalized = Math.Clamp((value - calibration.Minimum) / range, 0, 1);
        if (calibration.Inverted)
        {
            normalized = 1 - normalized;
        }

        if (!calibration.Centered)
        {
            return normalized <= calibration.DeadZone
                ? 0
                : Math.Clamp((normalized - calibration.DeadZone) / (1 - calibration.DeadZone), 0, 1);
        }

        var center = Math.Clamp((calibration.Center - calibration.Minimum) / range, 0.01, 0.99);
        var centered = normalized < center
            ? (normalized - center) / center
            : (normalized - center) / (1 - center);

        var deadZone = Math.Clamp(calibration.DeadZone, 0, 0.95);
        if (Math.Abs(centered) <= deadZone)
        {
            return 0;
        }

        return Math.Clamp(
            Math.Sign(centered) * ((Math.Abs(centered) - deadZone) / (1 - deadZone)),
            -1,
            1);
    }

    public static int ResolveGear(IReadOnlyList<bool> buttons, IReadOnlyList<int> gearButtons)
    {
        for (var index = 0; index < gearButtons.Count; index++)
        {
            var button = gearButtons[index];
            if (button >= 0 && button < buttons.Count && buttons[button])
            {
                return index == 0 ? -1 : index;
            }
        }

        return 0;
    }
}

