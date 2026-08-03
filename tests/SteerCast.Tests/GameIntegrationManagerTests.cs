using SteerCast.App.Services;
using SteerCast.Core.Models;

namespace SteerCast.Tests;

public sealed class GameIntegrationManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"steercast-game-{Guid.NewGuid():N}");

    [Fact]
    public async Task PersistsDisabledIntegrationSettings()
    {
        var path = Path.Combine(_directory, "game-integration.json");
        using (var manager = new GameIntegrationManager(path))
        {
            var snapshot = await manager.UpdateAsync(new GameIntegrationSettings(false, "dirt-rally-2", false));
            Assert.False(snapshot.Settings.Enabled);
            Assert.False(snapshot.Settings.ShowOnOverlay);
            Assert.Equal("derived-telemetry", snapshot.Games.Single().SignalKind);
        }

        using var reloaded = new GameIntegrationManager(path);
        Assert.False(reloaded.Snapshot.Settings.Enabled);
        Assert.False(reloaded.Snapshot.Settings.ShowOnOverlay);
    }

    [Fact]
    public async Task RejectsUnknownGameAdapters()
    {
        using var manager = new GameIntegrationManager(Path.Combine(_directory, "game-integration.json"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.UpdateAsync(new GameIntegrationSettings(true, "unknown-game", true)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
