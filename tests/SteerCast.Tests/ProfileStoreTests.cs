using SteerCast.App.Services;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"steercast-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task PersistsProfilesWithStableIds()
    {
        var store = new ProfileStore(_directory);
        var saved = await store.SaveAsync(new OverlayProfile
        {
            Id = "race-night",
            Name = "Race Night",
            HandbrakeEnabled = false,
            HandbrakeDeviceId = " usb-handbrake "
        });
        var reloaded = await new ProfileStore(_directory).GetAsync("race-night");

        Assert.NotNull(reloaded);
        Assert.Equal(saved.Id, reloaded.Id);
        Assert.Equal(saved.Name, reloaded.Name);
        Assert.Equal(saved.Width, reloaded.Width);
        Assert.Equal(saved.Theme, reloaded.Theme);
        Assert.False(reloaded.HandbrakeEnabled);
        Assert.Equal("usb-handbrake", reloaded.HandbrakeDeviceId);
    }

    [Fact]
    public async Task RecoversFromACorruptDefaultProfile()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "default.json"), "{broken");

        var profiles = await new ProfileStore(_directory).GetAllAsync();

        Assert.Contains(profiles, profile => profile.Id == "default");
        Assert.Single(Directory.EnumerateFiles(_directory, "default.json.corrupt-*"));
    }

    [Fact]
    public async Task MigratesProfilesWithMissingHandbrakeLayout()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "default.json"),
            """
            {
              "schemaVersion": 1,
              "id": "default",
              "name": "Default Overlay",
              "layout": {
                "wheel": { "x": 1, "y": 2, "scale": 1, "visible": true },
                "pedals": { "x": 3, "y": 4, "scale": 1, "visible": true },
                "gear": { "x": 5, "y": 6, "scale": 1, "visible": true },
                "handbrake": null,
                "buttons": { "x": 7, "y": 8, "scale": 1, "visible": true }
              }
            }
            """);

        var profile = await new ProfileStore(_directory).GetAsync("default");

        Assert.NotNull(profile);
        Assert.NotNull(profile.Layout.Handbrake);
        Assert.Equal(545, profile.Layout.Handbrake.X);
        Assert.Equal(330, profile.Layout.Handbrake.Y);
    }

    [Fact]
    public async Task MigratesOldG920ThemeToPalette()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "default.json"),
            """
            {
              "schemaVersion": 1,
              "id": "default",
              "name": "Default Overlay",
              "theme": {
                "name": "G920 Inspired",
                "accent": "#28a9ff",
                "brake": "#ff4655",
                "surface": "#10151b",
                "foreground": "#f4f7fa",
                "opacity": 0.94
              }
            }
            """);

        var profile = await new ProfileStore(_directory).GetAsync("default");

        Assert.NotNull(profile);
        Assert.Equal("Palette", profile.Theme.Name);
        Assert.Equal("#CEDFD9", profile.Theme.Accent);
        Assert.Equal("#9B6A6C", profile.Theme.Brake);
    }

    [Fact]
    public async Task TrimsWindowsGamingInputNullTerminatedDeviceIds()
    {
        var store = new ProfileStore(_directory);
        var saved = await store.SaveAsync(new OverlayProfile
        {
            Id = "default",
            Name = "Default",
            DeviceId = "wheel-id\0",
            HandbrakeDeviceId = "handbrake-id\0"
        });

        Assert.Equal("wheel-id", saved.DeviceId);
        Assert.Equal("handbrake-id", saved.HandbrakeDeviceId);
    }

    [Fact]
    public async Task ClearsLegacyWindowsGamingInputNonRoamableDeviceIds()
    {
        var store = new ProfileStore(_directory);
        var saved = await store.SaveAsync(new OverlayProfile
        {
            Id = "default",
            Name = "Default",
            DeviceId = "{wgi/nrid/example}\0"
        });

        Assert.Null(saved.DeviceId);
    }

    [Fact]
    public async Task ResetsOffCanvasLayoutElements()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "default.json"),
            """
            {
              "schemaVersion": 1,
              "id": "default",
              "name": "Default Overlay",
              "width": 1200,
              "height": 600,
              "layout": {
                "wheel": { "x": 380, "y": 254, "scale": 1, "visible": true },
                "pedals": { "x": 57, "y": 191, "scale": 1, "visible": true },
                "gear": { "x": 466, "y": -2, "scale": 1, "visible": true },
                "buttons": { "x": 40, "y": 535, "scale": 1, "visible": true }
              }
            }
            """);

        var profile = await new ProfileStore(_directory).GetAsync("default");

        Assert.NotNull(profile);
        Assert.Equal(840, profile.Layout.Gear.X);
        Assert.Equal(92, profile.Layout.Gear.Y);
    }

    [Fact]
    public async Task PersistsLegacyProfileMigration()
    {
        var path = Path.Combine(_directory, "default.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "id": "default",
              "name": "Default Overlay",
              "deviceId": "{wgi/nrid/example}\u0000",
              "width": 1200,
              "height": 600,
              "layout": {
                "wheel": { "x": 380, "y": 254, "scale": 1, "visible": true },
                "pedals": { "x": 57, "y": 191, "scale": 1, "visible": true },
                "gear": { "x": 466, "y": -2, "scale": 1, "visible": true },
                "buttons": { "x": 40, "y": 535, "scale": 1, "visible": true }
              }
            }
            """);

        var profile = await new ProfileStore(_directory).GetAsync("default");
        var saved = await File.ReadAllTextAsync(path);

        Assert.NotNull(profile);
        Assert.Equal(OverlayProfile.CurrentSchemaVersion, profile.SchemaVersion);
        Assert.Null(profile.DeviceId);
        Assert.Equal(390, profile.Layout.Wheel.X);
        Assert.Equal(840, profile.Layout.Gear.X);
        Assert.Contains("\"schemaVersion\":5", saved.Replace(" ", string.Empty));
        Assert.DoesNotContain("wgi/nrid", saved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
