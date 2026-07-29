using Microsoft.Win32;

namespace SteerCast.App.Services;

public static class LogitechGHubDetector
{
    private static readonly string[] ExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LGHUB", "lghub.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGHUB", "lghub.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LGHUB", "lghub.exe")
    ];

    public static GHubStatus Detect()
    {
        var installed = ExecutablePaths.Any(File.Exists) || IsRegisteredInstall();
        var running = new[] { "lghub", "lghub_agent", "lghub_updater" }
            .Any(name => System.Diagnostics.Process.GetProcessesByName(name).Length > 0);
        return new GHubStatus(installed, running);
    }

    private static bool IsRegisteredInstall()
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var root = hive.OpenSubKey(uninstallPath);
            if (root is null)
            {
                continue;
            }

            foreach (var keyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(keyName);
                if (key?.GetValue("DisplayName") is string displayName && displayName.Contains("Logitech G HUB", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public sealed record GHubStatus(bool Installed, bool Running);
