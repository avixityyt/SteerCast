using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// A local, game-provided telemetry feed. Implementations must be passive:
/// they may receive an exported data stream but never modify the game, wheel,
/// or controller routing.
/// </summary>
public interface IGameTelemetrySource : IDisposable
{
    GameTelemetryReading Reading { get; }
}
