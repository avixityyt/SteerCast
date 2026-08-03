using System.Xml.Linq;
using SteerCast.App.Services;

namespace SteerCast.Tests;

public sealed class DirtRally2SetupServiceTests : IDisposable
{
    private readonly string _documents = Path.Combine(Path.GetTempPath(), $"steercast-dirt-{Guid.NewGuid():N}");

    [Fact]
    public void ReportsMissingConfigurationBeforeGameHasRun()
    {
        var state = new DirtRally2SetupService(_documents).Inspect();

        Assert.Equal("not-found", state.Status);
        Assert.False(state.ConfigFound);
        Assert.False(state.CanConfigure);
    }

    [Fact]
    public void ConfiguresUdpAndCreatesOneRecoverableBackup()
    {
        var path = CreateConfig("<hardware_settings><motion_platform><udp enabled=\"false\" extradata=\"0\" ip=\"10.0.0.2\" port=\"1234\" delay=\"9\" /><keep value=\"unchanged\" /></motion_platform></hardware_settings>");
        var service = new DirtRally2SetupService(_documents);

        var state = service.Configure();
        service.Configure();

        Assert.True(state.Configured);
        Assert.Equal("configured", state.Status);
        Assert.Single(state.BackupPaths);
        Assert.True(File.Exists(path + ".steercast-backup"));
        var document = XDocument.Load(path);
        var udp = document.Descendants("udp").Single();
        Assert.Equal("true", (string?)udp.Attribute("enabled"));
        Assert.Equal("3", (string?)udp.Attribute("extradata"));
        Assert.Equal("127.0.0.1", (string?)udp.Attribute("ip"));
        Assert.Equal("20777", (string?)udp.Attribute("port"));
        Assert.Equal("1", (string?)udp.Attribute("delay"));
        Assert.Equal("unchanged", (string?)document.Descendants("keep").Single().Attribute("value"));
    }

    private string CreateConfig(string content)
    {
        var directory = Path.Combine(_documents, "My Games", "DiRT Rally 2.0", "hardwaresettings");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "hardware_settings_config.xml");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_documents)) Directory.Delete(_documents, true);
    }
}
