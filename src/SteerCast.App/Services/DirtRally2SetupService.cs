using System.Xml.Linq;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>Safely detects and configures DiRT Rally 2.0's documented UDP output.</summary>
public sealed class DirtRally2SetupService(string? documentsPath = null)
{
    private static readonly string[] ConfigNames =
    [
        "hardware_settings_config.xml",
        "hardware_settings_config_vr.xml"
    ];

    private readonly string _settingsDirectory = Path.Combine(
        documentsPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "DiRT Rally 2.0",
        "hardwaresettings");

    public GameSetupState Inspect()
    {
        var paths = ExistingConfigPaths();
        if (paths.Length == 0)
        {
            return new GameSetupState(
                "not-found", false, false, false, [], [],
                "No DiRT Rally 2.0 configuration was found. Launch the game once, close it, then check again.");
        }

        try
        {
            var configured = paths.All(IsConfigured);
            var backups = paths.Select(BackupPath).Where(File.Exists).ToArray();
            return new GameSetupState(
                configured ? "configured" : "needs-configuration",
                true,
                configured,
                true,
                paths,
                backups,
                configured
                    ? "DiRT Rally 2.0 is configured to send local telemetry to SteerCast."
                    : "SteerCast found the game configuration and can enable local telemetry automatically.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new GameSetupState(
                "unreadable", true, false, false, paths, [],
                "The DiRT Rally 2.0 configuration could not be read. Close the game and check file permissions.");
        }
    }

    public GameSetupState Configure()
    {
        var paths = ExistingConfigPaths();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("Launch DiRT Rally 2.0 once, close it, then try again.");
        }

        foreach (var path in paths)
        {
            ConfigureFile(path);
        }

        return Inspect();
    }

    private string[] ExistingConfigPaths() => ConfigNames
        .Select(name => Path.Combine(_settingsDirectory, name))
        .Where(File.Exists)
        .ToArray();

    private static bool IsConfigured(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var udp = FindUdp(document);
        return udp is not null
            && AttributeEquals(udp, "enabled", "true")
            && AttributeEquals(udp, "extradata", "3")
            && AttributeEquals(udp, "ip", "127.0.0.1")
            && AttributeEquals(udp, "port", DirtRally2TelemetryAdapter.DefaultPort.ToString())
            && AttributeEquals(udp, "delay", "1");
    }

    private static void ConfigureFile(string path)
    {
        var backupPath = BackupPath(path);
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var udp = FindUdp(document);
        if (udp is null)
        {
            var parent = document.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "motion_platform", StringComparison.OrdinalIgnoreCase))
                ?? document.Root
                ?? throw new InvalidOperationException("The game configuration has no XML root element.");
            udp = new XElement(parent.Name.Namespace + "udp");
            parent.Add(udp);
        }

        udp.SetAttributeValue("enabled", "true");
        udp.SetAttributeValue("extradata", "3");
        udp.SetAttributeValue("ip", "127.0.0.1");
        udp.SetAttributeValue("port", DirtRally2TelemetryAdapter.DefaultPort);
        udp.SetAttributeValue("delay", "1");

        var temporaryPath = path + ".steercast.tmp";
        try
        {
            document.Save(temporaryPath, SaveOptions.DisableFormatting);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static XElement? FindUdp(XDocument document) => document.Descendants().FirstOrDefault(element =>
        string.Equals(element.Name.LocalName, "udp", StringComparison.OrdinalIgnoreCase));

    private static bool AttributeEquals(XElement element, string name, string expected) =>
        string.Equals((string?)element.Attribute(name), expected, StringComparison.OrdinalIgnoreCase);

    private static string BackupPath(string path) => path + ".steercast-backup";
}
