using System.Text.Json;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

public sealed class GameIntegrationManager : IDisposable
{
    private static readonly GameIntegrationDescriptor[] SupportedGames =
    [
        new(
            DirtRally2TelemetryAdapter.GameId,
            "DiRT Rally 2.0",
            "derived-telemetry",
            "Derived vehicle load",
            DirtRally2TelemetryAdapter.DefaultPort,
            "Uses the game's local UDP output. It estimates vehicle load; it does not read wheel force feedback.")
    ];

    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _settingsPath;
    private GameIntegrationSettings _settings;
    private IGameTelemetrySource? _activeSource;
    private bool _disposed;

    public GameIntegrationManager(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteerCast",
            "game-integration.json");
        _settings = LoadSettings(_settingsPath);
        ApplyAdapterState();
    }

    public GameIntegrationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new GameIntegrationSnapshot(_settings, SupportedGames, CurrentReading());
            }
        }
    }

    public InputFrame Apply(InputFrame frame)
    {
        lock (_sync)
        {
            if (!_settings.Enabled || !_settings.ShowOnOverlay) return frame;

            var reading = CurrentReading();
            return !reading.Available
                ? frame
                : frame with
                {
                    GameTelemetryStrength = reading.Strength,
                    GameTelemetryDirection = reading.Direction,
                    GameTelemetryKind = reading.SignalKind,
                    GameTelemetrySource = reading.Source
                };
        }
    }

    public async Task<GameIntegrationSnapshot> UpdateAsync(
        GameIntegrationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _settings = normalized;
                ApplyAdapterState();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var temporaryPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    AppJsonContext.Default.GameIntegrationSettings,
                    cancellationToken);
            }
            File.Move(temporaryPath, _settingsPath, true);
            return Snapshot;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void ApplyAdapterState()
    {
        var shouldRun = _settings.Enabled
            && string.Equals(_settings.GameId, DirtRally2TelemetryAdapter.GameId, StringComparison.Ordinal);
        if (shouldRun && _activeSource is null)
        {
            _activeSource = new DirtRally2TelemetryAdapter();
        }
        else if (!shouldRun && _activeSource is not null)
        {
            _activeSource.Dispose();
            _activeSource = null;
        }
    }

    private GameTelemetryReading CurrentReading() => _activeSource?.Reading ?? new(
        "none",
        SupportedGames[0].Name,
        SupportedGames[0].SignalKind,
        SupportedGames[0].SignalLabel,
        false,
        false,
        0,
        0,
        0,
        SupportedGames[0].Port,
        _settings.Enabled ? "Selected game adapter is unavailable." : "Game telemetry is off.");

    private static GameIntegrationSettings LoadSettings(string path)
    {
        if (!File.Exists(path)) return new GameIntegrationSettings();

        try
        {
            var settings = JsonSerializer.Deserialize(
                File.ReadAllBytes(path),
                AppJsonContext.Default.GameIntegrationSettings);
            return settings is null ? new GameIntegrationSettings() : Normalize(settings);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new GameIntegrationSettings();
        }
    }

    private static GameIntegrationSettings Normalize(GameIntegrationSettings settings)
    {
        var gameId = settings.GameId?.Trim();
        if (string.IsNullOrEmpty(gameId))
        {
            throw new ArgumentException("Choose a supported game integration.", nameof(settings));
        }
        if (!SupportedGames.Any(game => string.Equals(game.Id, gameId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Unsupported game integration.", nameof(settings));
        }

        return settings with { GameId = gameId };
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _activeSource?.Dispose();
            _activeSource = null;
        }
        _writeGate.Dispose();
    }
}
